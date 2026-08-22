using System.Diagnostics;
using FlaUI.Core;
using FlaUI.UIA3;
using Promaxx2.Automation.Core;

namespace Promaxx2.Automation.Tests;

/// <summary>
/// ผลการรันหนึ่ง Test Case (นอกเหนือจาก NUnit — ใช้โดย Runner run command)
/// </summary>
public sealed record CaseResult(
    string TestCaseCode,
    bool Passed,
    string? ErrorMessage,
    TimeSpan Duration,
    string? ScreenshotPath);

/// <summary>
/// Context สำหรับ run หนึ่ง case
/// </summary>
public sealed class CaseContext
{
    public CaseContext(AppConfig config, string targetApp, string testCaseCode)
    {
        Config = config;
        TargetApp = targetApp;
        TestCaseCode = testCaseCode;
        Launcher = new AppLauncher(config);
        Switcher = new EnvironmentSwitcher();
    }

    public AppConfig Config { get; }
    public string TargetApp { get; }
    public string TestCaseCode { get; }
    public AppLauncher Launcher { get; }
    public EnvironmentSwitcher Switcher { get; }
    public Process? Process { get; set; }
    public Application? App => Process is not null ? Application.Attach(Process.Id) : null;
}

/// <summary>
/// Interface ของ smoke case — Runner discover ผ่าน reflection
/// </summary>
public interface IAutomationCase
{
    string TestCaseCode { get; }
    string TargetApp { get; } // "pos" | "app"
    Task<CaseResult> RunAsync(CaseContext ctx);
}

/// <summary>
/// Base สำหรับ smoke case — จัดการ launch/close/screenshot อัตโนมัติ
/// </summary>
public abstract class SmokeCaseBase : IAutomationCase
{
    public abstract string TestCaseCode { get; }
    public virtual string TargetApp => "pos";

    public async Task<CaseResult> RunAsync(CaseContext ctx)
    {
        var sw = Stopwatch.StartNew();
        string? screenshot = null;
        try
        {
            ctx.Process = ctx.Launcher.Launch(ctx.TargetApp);
            await RunCoreAsync(ctx);
            sw.Stop();
            return new CaseResult(TestCaseCode, true, null, sw.Elapsed, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            screenshot = await CaptureScreenshotAsync(ctx, TestCaseCode);
            return new CaseResult(TestCaseCode, false, ex.Message, sw.Elapsed, screenshot);
        }
        finally
        {
            if (ctx.Process != null && !ctx.Process.HasExited)
            {
                ctx.Launcher.Close(ctx.Process);
            }
        }
    }

    protected abstract Task RunCoreAsync(CaseContext ctx);

    private async Task<string?> CaptureScreenshotAsync(CaseContext ctx, string code)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "artifacts", "screenshots");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{code}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            var app = ctx.App;
            if (app != null)
            {
                using var automation = new UIA3Automation();
                var window = app.GetMainWindow(automation)
                    ?? throw new InvalidOperationException("AUT main window was not found for screenshot capture.");
                var bmp = window.Capture();
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                return path;
            }
        }
        catch { }
        return null;
    }
}
