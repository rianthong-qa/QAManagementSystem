using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.Settings;
using ProMaxx2.QA.Infrastructure.Persistence;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1/master-settings"), Authorize]
public sealed class MasterSettingsController(QaDbContext db, SharedAiConfigurationService aiConfiguration) : ControllerBase
{
    public static readonly string[] Categories =
    [
        "ReleaseType", "TestCasePriority", "TestCaseType",
        "TestSuiteType", "TestSuiteRiskTier", "TestCycleType"
    ];

    [HttpGet("ai"), Authorize(Policy = "AdminUser")]
    public Task<AiConfigurationView> GetAiConfiguration(CancellationToken ct) => aiConfiguration.GetViewAsync(ct);

    [HttpPut("ai"), Authorize(Policy = "AdminUser")]
    public async Task<ActionResult<AiConfigurationView>> SaveAiConfiguration(SaveAiConfigurationRequest request, CancellationToken ct)
    {
        try { return Ok(await aiConfiguration.SaveAsync(request.Provider, request.Model, request.BaseUrl, request.ApiKey, request.IsEnabled, request.ClearApiKey, ct)); }
        catch (ArgumentException ex) { return BadRequest(Problem(ex.Message)); }
    }

    [HttpPost("ai/models"), Authorize(Policy = "AdminUser")]
    public async Task<ActionResult<IReadOnlyList<AiModelView>>> ListAiModels(ListAiModelsRequest request, CancellationToken ct)
    {
        try { return Ok(await aiConfiguration.ListModelsAsync(request.Provider, request.BaseUrl, request.ApiKey, ct)); }
        catch (ArgumentException ex) { return BadRequest(Problem(ex.Message)); }
        catch (AiNotConfiguredException ex) { return BadRequest(Problem(ex.Message)); }
        catch (InvalidOperationException ex) { return StatusCode(502, Problem(ex.Message)); }
    }

    [HttpGet]
    public async Task<IReadOnlyList<MasterOptionDto>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        await db.MasterOptions.AsNoTracking()
            .Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.Category).ThenBy(x => x.SortOrder).ThenBy(x => x.DisplayName)
            .Select(x => new MasterOptionDto(x.MasterOptionId, x.Category, x.Value, x.DisplayName, x.SortOrder, x.IsActive))
            .ToListAsync(ct);

    [HttpPost, Authorize(Policy = "AdminUser")]
    public async Task<ActionResult<MasterOptionDto>> Create(SaveMasterOptionRequest request, CancellationToken ct)
    {
        if (!Categories.Contains(request.Category)) return BadRequest(Problem("ประเภทการตั้งค่าไม่ถูกต้อง"));
        if (await db.MasterOptions.AnyAsync(x => x.Category == request.Category && x.Value == request.Value.Trim(), ct))
            return Conflict(Problem("รหัสค่านี้มีอยู่แล้ว"));
        var item = new MasterOption(request.Category, request.Value, request.DisplayName, request.SortOrder);
        db.MasterOptions.Add(item);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(item));
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "AdminUser")]
    public async Task<ActionResult<MasterOptionDto>> Update(Guid id, SaveMasterOptionRequest request, CancellationToken ct)
    {
        var item = await db.MasterOptions.SingleOrDefaultAsync(x => x.MasterOptionId == id, ct);
        if (item is null) return NotFound();
        if (item.Category != request.Category) return BadRequest(Problem("ไม่สามารถเปลี่ยนกลุ่มของข้อมูลเดิมได้"));
        if (await db.MasterOptions.AnyAsync(x => x.MasterOptionId != id && x.Category == request.Category && x.Value == request.Value.Trim(), ct))
            return Conflict(Problem("รหัสค่านี้มีอยู่แล้ว"));
        item.Update(request.Value, request.DisplayName, request.SortOrder, request.IsActive);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(item));
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "AdminUser")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var item = await db.MasterOptions.SingleOrDefaultAsync(x => x.MasterOptionId == id, ct);
        if (item is null) return NotFound();
        db.MasterOptions.Remove(item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("environments")]
    public async Task<IReadOnlyList<EnvironmentSettingDto>> Environments(CancellationToken ct) =>
        await db.TestEnvironments.AsNoTracking().OrderBy(x => x.ProjectId).ThenBy(x => x.EnvironmentName)
            .Select(x => new EnvironmentSettingDto(x.TestEnvironmentId, x.ProjectId, x.EnvironmentName, x.BaseUrl, x.IsActive))
            .ToListAsync(ct);

    [HttpPost("environments"), Authorize(Policy = "AdminUser")]
    public async Task<ActionResult<EnvironmentSettingDto>> CreateEnvironment(SaveEnvironmentSettingRequest request, CancellationToken ct)
    {
        var item = new TestEnvironment(request.ProjectId, request.EnvironmentName, request.BaseUrl);
        db.TestEnvironments.Add(item);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(item));
    }

    [HttpPut("environments/{id:guid}"), Authorize(Policy = "AdminUser")]
    public async Task<ActionResult<EnvironmentSettingDto>> UpdateEnvironment(Guid id, SaveEnvironmentSettingRequest request, CancellationToken ct)
    {
        var item = await db.TestEnvironments.SingleOrDefaultAsync(x => x.TestEnvironmentId == id, ct);
        if (item is null) return NotFound();
        if (item.ProjectId != request.ProjectId) return BadRequest(Problem("ไม่สามารถเปลี่ยน Project ของ Environment ได้"));
        item.Update(request.EnvironmentName, request.BaseUrl, request.IsActive);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(item));
    }

    [HttpDelete("environments/{id:guid}"), Authorize(Policy = "AdminUser")]
    public async Task<IActionResult> DeleteEnvironment(Guid id, CancellationToken ct)
    {
        var item = await db.TestEnvironments.SingleOrDefaultAsync(x => x.TestEnvironmentId == id, ct);
        if (item is null) return NotFound();
        db.TestEnvironments.Remove(item);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Conflict(Problem("Environment นี้ถูกใช้งานใน Test Cycle หรือ Execution แล้ว จึงไม่สามารถลบได้ กรุณาปิดใช้งานแทน"));
        }
        return NoContent();
    }

    private static MasterOptionDto ToDto(MasterOption x) => new(x.MasterOptionId, x.Category, x.Value, x.DisplayName, x.SortOrder, x.IsActive);
    private static EnvironmentSettingDto ToDto(TestEnvironment x) => new(x.TestEnvironmentId, x.ProjectId, x.EnvironmentName, x.BaseUrl, x.IsActive);
    private static ProblemDetails Problem(string detail) => new() { Detail = detail };
}

public sealed record MasterOptionDto(Guid MasterOptionId, string Category, string Value, string DisplayName, int SortOrder, bool IsActive);
public sealed record SaveMasterOptionRequest(string Category, string Value, string DisplayName, int SortOrder, bool IsActive = true);
public sealed record EnvironmentSettingDto(Guid TestEnvironmentId, Guid ProjectId, string EnvironmentName, string? BaseUrl, bool IsActive);
public sealed record SaveEnvironmentSettingRequest(Guid ProjectId, string EnvironmentName, string? BaseUrl, bool IsActive = true);
public sealed record SaveAiConfigurationRequest(string Provider, string Model, string? BaseUrl, string? ApiKey, bool IsEnabled = true, bool ClearApiKey = false);
public sealed record ListAiModelsRequest(string Provider, string? BaseUrl, string? ApiKey);
