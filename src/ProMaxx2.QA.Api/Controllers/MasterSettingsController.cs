using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.Integrations;
using ProMaxx2.QA.Domain.Settings;
using ProMaxx2.QA.Infrastructure.Persistence;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1/master-settings"), Authorize]
public sealed class MasterSettingsController(QaDbContext db, SharedAiConfigurationService aiConfiguration, CrmSyncSettingsService crmSyncSettings, EmailConfigurationService emailConfiguration, EmailSenderService emailSender) : ControllerBase
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

    // CRM login เป็น self-service ต่อ user แล้ว (ดู AuthController: GET/PUT /auth/me/crm) — เหลือแค่รอบ Poll
    // (Phase 2 CrmSyncWorker) ที่ยังเป็นค่ากลางของทั้งระบบ ให้ admin ตั้งค่าตรงนี้
    [HttpGet("crm-sync"), Authorize(Policy = "AdminUser")]
    public Task<CrmSyncSettingsView> GetCrmSyncSettings(CancellationToken ct) => crmSyncSettings.GetViewAsync(ct);

    [HttpPut("crm-sync"), Authorize(Policy = "AdminUser")]
    public async Task<ActionResult<CrmSyncSettingsView>> SaveCrmSyncSettings(SaveCrmSyncSettingsRequest request, CancellationToken ct)
    {
        try { return Ok(await crmSyncSettings.SaveAsync(request.PollIntervalMinutes, ct)); }
        catch (ArgumentException ex) { return BadRequest(Problem(ex.Message)); }
    }

    [HttpGet("crm-mappings")]
    public async Task<IReadOnlyList<CrmProjectMappingDto>> CrmMappings(CancellationToken ct) =>
        await db.CrmProjectMappings.AsNoTracking().OrderBy(x => x.ProjectId)
            .Select(x => new CrmProjectMappingDto(x.CrmProjectMappingId, x.ProjectId, x.CrmProductId, x.CrmVersionId))
            .ToListAsync(ct);

    [HttpPost("crm-mappings"), Authorize(Policy = "AdminUser")]
    public async Task<ActionResult<CrmProjectMappingDto>> CreateCrmMapping(SaveCrmProjectMappingRequest request, CancellationToken ct)
    {
        if (await db.CrmProjectMappings.AnyAsync(x => x.ProjectId == request.ProjectId, ct))
            return Conflict(Problem("โปรเจกต์นี้มีการตั้งค่า CRM Mapping อยู่แล้ว"));
        var item = new CrmProjectMapping(request.ProjectId, request.CrmProductId, request.CrmVersionId);
        db.CrmProjectMappings.Add(item);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(item));
    }

    [HttpPut("crm-mappings/{id:guid}"), Authorize(Policy = "AdminUser")]
    public async Task<ActionResult<CrmProjectMappingDto>> UpdateCrmMapping(Guid id, SaveCrmProjectMappingRequest request, CancellationToken ct)
    {
        var item = await db.CrmProjectMappings.SingleOrDefaultAsync(x => x.CrmProjectMappingId == id, ct);
        if (item is null) return NotFound();
        item.Update(request.CrmProductId, request.CrmVersionId);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(item));
    }

    [HttpDelete("crm-mappings/{id:guid}"), Authorize(Policy = "AdminUser")]
    public async Task<IActionResult> DeleteCrmMapping(Guid id, CancellationToken ct)
    {
        var item = await db.CrmProjectMappings.SingleOrDefaultAsync(x => x.CrmProjectMappingId == id, ct);
        if (item is null) return NotFound();
        db.CrmProjectMappings.Remove(item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("email"), Authorize(Policy = "AdminUser")]
    public Task<EmailConfigurationView> GetEmailConfiguration(CancellationToken ct) => emailConfiguration.GetViewAsync(ct);

    [HttpPut("email"), Authorize(Policy = "AdminUser")]
    public async Task<ActionResult<EmailConfigurationView>> SaveEmailConfiguration(SaveEmailConfigurationRequest request, CancellationToken ct)
    {
        try { return Ok(await emailConfiguration.SaveAsync(request.SmtpHost, request.SmtpPort, request.SenderEmail, request.SenderDisplayName, request.Password, request.IsEnabled, request.ClearPassword, ct)); }
        catch (ArgumentException ex) { return BadRequest(Problem(ex.Message)); }
    }

    // ทดสอบส่งอีเมลจริงด้วยค่าที่บันทึกไว้แล้ว — ให้ admin เห็น error จริงจาก SMTP (host/port/App Password ผิด ฯลฯ)
    // ก่อนที่การแจ้งเตือนจริงจุดใดจุดหนึ่งจะไปเจอปัญหานี้แบบเงียบๆ (อีเมลแจ้งเตือนจริงเป็น best-effort กลืน error ทิ้ง)
    [HttpPost("email/test"), Authorize(Policy = "AdminUser")]
    public async Task<IActionResult> TestEmail(TestEmailRequest request, CancellationToken ct)
    {
        try { await emailSender.SendAsync(request.ToEmail, "[QA Hub] ทดสอบการส่งอีเมล", "นี่คืออีเมลทดสอบการตั้งค่า Email/SMTP จาก QA Hub Setting Center", ct); return NoContent(); }
        catch (EmailNotConfiguredException ex) { return BadRequest(Problem(ex.Message)); }
        catch (Exception ex) { return BadRequest(Problem($"ส่งอีเมลทดสอบไม่สำเร็จ: {ex.Message}")); }
    }

    private static MasterOptionDto ToDto(MasterOption x) => new(x.MasterOptionId, x.Category, x.Value, x.DisplayName, x.SortOrder, x.IsActive);
    private static EnvironmentSettingDto ToDto(TestEnvironment x) => new(x.TestEnvironmentId, x.ProjectId, x.EnvironmentName, x.BaseUrl, x.IsActive);
    private static CrmProjectMappingDto ToDto(CrmProjectMapping x) => new(x.CrmProjectMappingId, x.ProjectId, x.CrmProductId, x.CrmVersionId);
    private static ProblemDetails Problem(string detail) => new() { Detail = detail };
}

public sealed record MasterOptionDto(Guid MasterOptionId, string Category, string Value, string DisplayName, int SortOrder, bool IsActive);
public sealed record SaveMasterOptionRequest(string Category, string Value, string DisplayName, int SortOrder, bool IsActive = true);
public sealed record EnvironmentSettingDto(Guid TestEnvironmentId, Guid ProjectId, string EnvironmentName, string? BaseUrl, bool IsActive);
public sealed record SaveEnvironmentSettingRequest(Guid ProjectId, string EnvironmentName, string? BaseUrl, bool IsActive = true);
public sealed record SaveAiConfigurationRequest(string Provider, string Model, string? BaseUrl, string? ApiKey, bool IsEnabled = true, bool ClearApiKey = false);
public sealed record ListAiModelsRequest(string Provider, string? BaseUrl, string? ApiKey);
public sealed record SaveCrmSyncSettingsRequest(int PollIntervalMinutes);
public sealed record CrmProjectMappingDto(Guid CrmProjectMappingId, Guid ProjectId, string CrmProductId, string? CrmVersionId);
public sealed record SaveCrmProjectMappingRequest(Guid ProjectId, string CrmProductId, string? CrmVersionId);
public sealed record SaveEmailConfigurationRequest(string SmtpHost, int SmtpPort, string SenderEmail, string? SenderDisplayName, string? Password, bool IsEnabled = true, bool ClearPassword = false);
public sealed record TestEmailRequest(string ToEmail);
