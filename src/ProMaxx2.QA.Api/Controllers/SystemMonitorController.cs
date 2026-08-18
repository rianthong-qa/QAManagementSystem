using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1/system-monitor")]
[Authorize(Roles = "SYS_ADMIN")]
public sealed class SystemMonitorController(
    QaDbContext db,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<SystemMonitorController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SystemMonitorDto>> Get(CancellationToken ct)
    {
        var databaseStarted = Stopwatch.GetTimestamp();
        var databaseOnline = false;
        string? databaseError = null;
        try { databaseOnline = await db.Database.CanConnectAsync(ct); }
        catch (Exception ex) { databaseError = ex.Message; }

        var services = new List<ManagedServiceDto>();
        foreach (var item in Options()) services.Add(await ReadServiceAsync(item, ct));
        var process = Process.GetCurrentProcess();
        return Ok(new SystemMonitorDto(
            DateTime.UtcNow,
            Environment.MachineName,
            environment.EnvironmentName,
            new ApiMonitorDto("Online", process.Id, DateTime.UtcNow - process.StartTime.ToUniversalTime(), process.WorkingSet64, Environment.ProcessorCount),
            new DatabaseMonitorDto(databaseOnline ? "Online" : "Offline", Stopwatch.GetElapsedTime(databaseStarted).TotalMilliseconds, databaseError),
            services));
    }

    [HttpPost("services/{key}/start")]
    public Task<ActionResult<ManagedServiceDto>> Start(string key, CancellationToken ct) => Control(key, "start", ct);

    [HttpPost("services/{key}/restart")]
    public Task<ActionResult<ManagedServiceDto>> Restart(string key, CancellationToken ct) => Control(key, "restart", ct);

    private async Task<ActionResult<ManagedServiceDto>> Control(string key, string action, CancellationToken ct)
    {
        var item = Options().SingleOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (item is null) return NotFound(new ProblemDetails { Title = "ไม่พบ Service", Detail = "Service นี้ไม่ได้อยู่ในรายการที่ระบบอนุญาต", Status = 404 });
        if (!OperatingSystem.IsWindows()) return Problem("การควบคุม Service รองรับ Windows เท่านั้น", statusCode: 501);
        try
        {
            if (action == "restart")
            {
                await RunScAsync("stop", item.ServiceName, ct, allowAlreadyStopped: true);
                await WaitForAsync(item.ServiceName, running: false, ct);
            }
            await RunScAsync("start", item.ServiceName, ct, allowAlreadyStopped: false);
            await WaitForAsync(item.ServiceName, running: true, ct);
            logger.LogWarning("System service {ServiceName} was {Action}ed by user {UserId}", item.ServiceName, action, User.FindFirstValue(ClaimTypes.NameIdentifier));
            return Ok(await ReadServiceAsync(item, ct));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to {Action} system service {ServiceName}", action, item.ServiceName);
            var detail = ex.Message.Contains("FAILED 5", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
                ? "บัญชีที่ใช้รัน API ยังไม่มีสิทธิ์ Start/Stop Service นี้ กรุณาเปิด PowerShell แบบ Run as administrator แล้วรัน scripts/system-monitor/grant-cloudflared-control.ps1 หนึ่งครั้ง"
                : ex.Message;
            return Problem(detail, title: "ควบคุม Service ไม่สำเร็จ", statusCode: 409);
        }
    }

    private IReadOnlyList<ManagedServiceOptions> Options() => configuration.GetSection("SystemMonitor:Services").Get<List<ManagedServiceOptions>>() ?? [];

    private static async Task<ManagedServiceDto> ReadServiceAsync(ManagedServiceOptions item, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return new(item.Key, item.DisplayName, item.Description, "Unsupported", false, "รองรับ Windows เท่านั้น");
        var result = await ExecuteScAsync("query", item.ServiceName, ct);
        var status = result.ExitCode == 0 ? ParseStatus(result.Output) : "NotInstalled";
        return new(item.Key, item.DisplayName, item.Description, status, status == "Running", result.ExitCode == 0 ? null : result.Output.Trim());
    }

    private static string ParseStatus(string output)
    {
        if (output.Contains("STATE", StringComparison.OrdinalIgnoreCase))
        {
            if (output.Contains(" 4  RUNNING", StringComparison.OrdinalIgnoreCase)) return "Running";
            if (output.Contains(" 1  STOPPED", StringComparison.OrdinalIgnoreCase)) return "Stopped";
            if (output.Contains(" 2  START_PENDING", StringComparison.OrdinalIgnoreCase)) return "Starting";
            if (output.Contains(" 3  STOP_PENDING", StringComparison.OrdinalIgnoreCase)) return "Stopping";
            if (output.Contains(" 7  PAUSED", StringComparison.OrdinalIgnoreCase)) return "Paused";
        }
        return "Unknown";
    }

    private static async Task RunScAsync(string command, string serviceName, CancellationToken ct, bool allowAlreadyStopped)
    {
        var result = await ExecuteScAsync(command, serviceName, ct);
        if (result.ExitCode != 0 && !(allowAlreadyStopped && result.Output.Contains("1062", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(result.Output.Trim());
    }

    private static async Task WaitForAsync(string serviceName, bool running, CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            var result = await ExecuteScAsync("query", serviceName, ct);
            if ((ParseStatus(result.Output) == "Running") == running) return;
            await Task.Delay(500, ct);
        }
        throw new TimeoutException("Service ใช้เวลาเปลี่ยนสถานะนานเกินกำหนด");
    }

    private static async Task<(int ExitCode, string Output)> ExecuteScAsync(string command, string serviceName, CancellationToken ct)
    {
        var info = new ProcessStartInfo { FileName = "sc.exe", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(command);
        info.ArgumentList.Add(serviceName);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("ไม่สามารถเปิด Service Controller ได้");
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, (await outputTask) + (await errorTask));
    }
}

public sealed class ManagedServiceOptions { public string Key { get; set; } = ""; public string ServiceName { get; set; } = ""; public string DisplayName { get; set; } = ""; public string? Description { get; set; } }
public sealed record SystemMonitorDto(DateTime CheckedAt, string MachineName, string Environment, ApiMonitorDto Api, DatabaseMonitorDto Database, IReadOnlyList<ManagedServiceDto> Services);
public sealed record ApiMonitorDto(string Status, int ProcessId, TimeSpan Uptime, long MemoryBytes, int ProcessorCount);
public sealed record DatabaseMonitorDto(string Status, double ResponseMilliseconds, string? Error);
public sealed record ManagedServiceDto(string Key, string DisplayName, string? Description, string Status, bool IsRunning, string? Error);
