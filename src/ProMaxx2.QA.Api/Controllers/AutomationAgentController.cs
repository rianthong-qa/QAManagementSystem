using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1/automation"), Authorize(Policy = "AutomationExecute")]
public sealed class AutomationAgentController(AutomationAgentService service, AutomationDataSnapshotService snapshots, AutomationDataRestoreService restores, AutomationDataSeedService seeds, IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("agents/register")] public async Task<ActionResult<AutomationAgentDto>> Register(RegisterAgentRequest request, CancellationToken ct)
    {
        try { return Ok(await service.RegisterAsync(request, null, ct)); }
        catch (ArgumentException ex) { return BadRequest(Problem("Agent registration invalid", ex.Message, 400)); }
    }

    [HttpPost("agents/heartbeat")] public async Task<ActionResult<AutomationAgentDto>> Heartbeat(AgentHeartbeatRequest request, CancellationToken ct)
    {
        try { return Ok(await service.HeartbeatAsync(request, ct)); }
        catch (EntityNotFoundException) { return NotFound(Problem("Agent not registered", "Register the agent before heartbeat.", 404)); }
    }

    [HttpPost("jobs/claim")] public async Task<ActionResult<AutomationJobPackageDto?>> Claim(ClaimJobRequest request, CancellationToken ct)
    {
        var package = await service.ClaimNextJobAsync(request, ct);
        return package is null ? NoContent() : Ok(package);
    }

    [HttpPost("executions/{id:guid}/steps/{stepNo:int}/result")] public async Task<IActionResult> ReportStep(Guid id, int stepNo, ReportStepResultRequest request, CancellationToken ct)
    {
        if (request.StepNo != stepNo) return BadRequest();
        if (MissingAgent(request.AgentCode) is { } missing) return missing;
        try { await service.ReportStepResultAsync(id, request, ct); return NoContent(); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (AgentMismatchException ex) { return AgentForbidden(ex); }
    }

    [HttpPost("executions/{id:guid}/steps/{stepNo:int}/evidence"), RequestSizeLimit(10_500_000)] public async Task<IActionResult> UploadEvidence(Guid id, int stepNo, IFormFile file, CancellationToken ct, [FromForm] string? agentCode = null)
    {
        if (file.Length == 0 || file.Length > 10_000_000) return BadRequest(Problem("Evidence invalid", "File must be between 1 byte and 10 MB.", 400));
        string[] allowed = [".png", ".jpg", ".jpeg", ".webp", ".txt", ".log", ".json"];
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowed.Contains(ext)) return BadRequest(Problem("Evidence type not supported", "Supported: PNG, JPG, WEBP, TXT, LOG, JSON.", 400));
        if (await VerifyEvidenceAgentAsync(id, agentCode, ct) is { } rejected) return rejected;
        var relative = Path.Combine(id.ToString("N"), $"step{stepNo}{ext}");
        var root = EvidenceRoot();
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return BadRequest();
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using (var stream = System.IO.File.Create(full)) await file.CopyToAsync(stream, ct);
        try { await service.UploadStepEvidenceAsync(id, stepNo, relative.Replace('\\', '/'), ct); return Ok(new { evidencePath = relative.Replace('\\', '/') }); }
        catch (InvalidOperationException) { System.IO.File.Delete(full); return NotFound(); }
    }

    [HttpPost("executions/{id:guid}/evidence/upload"), RequestSizeLimit(10_500_000)] public async Task<IActionResult> UploadExecutionEvidence(Guid id, [FromForm] int? stepNo, [FromForm] string evidenceType, IFormFile file, CancellationToken ct, [FromForm] string? agentCode = null)
    {
        if (file.Length == 0 || file.Length > 10_000_000) return BadRequest(Problem("Evidence invalid", "File must be between 1 byte and 10 MB.", 400));
        string[] allowed = [".png", ".jpg", ".jpeg", ".webp", ".txt", ".log", ".json", ".csv"];
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowed.Contains(ext)) return BadRequest(Problem("Evidence type not supported", "Supported: PNG, JPG, WEBP, TXT, LOG, JSON, CSV.", 400));
        var type = string.IsNullOrWhiteSpace(evidenceType) ? "AutomationLog" : evidenceType.Trim();
        // evidenceType ถูกฝังลงชื่อไฟล์ตรงๆ — รับเฉพาะ A-Z a-z 0-9 _ - เพื่อไม่ให้ค่าอย่าง "../<executionอื่น>/x" เขียนข้าม
        // โฟลเดอร์ของ execution อื่นได้ (guard StartsWith(root) ด้านล่างกันได้แค่การหลุดออกนอก evidence root ทั้งหมด)
        if (type.Length > 50 || !type.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return BadRequest();
        if (await VerifyEvidenceAgentAsync(id, agentCode, ct) is { } rejected) return rejected;
        var relative = Path.Combine(id.ToString("N"), $"{type}_{stepNo?.ToString() ?? "exec"}_{DateTime.UtcNow:HHmmss}{ext}");
        var root = EvidenceRoot();
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return BadRequest();
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using (var stream = System.IO.File.Create(full)) await file.CopyToAsync(stream, ct);
        try { await service.UploadEvidenceAsync(id, stepNo, type, relative.Replace('\\', '/'), null, ct); return Ok(new { evidencePath = relative.Replace('\\', '/') }); }
        catch (EntityNotFoundException) { System.IO.File.Delete(full); return NotFound(); }
    }

    [HttpPost("executions/{id:guid}/complete")] public async Task<ActionResult<AutomationExecutionDto>> Complete(Guid id, CompleteExecutionRequest request, CancellationToken ct)
    {
        if (MissingAgent(request.AgentCode) is { } missing) return missing;
        try { return Ok(await service.CompleteExecutionAsync(id, request, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (AgentMismatchException ex) { return AgentForbidden(ex); }
        catch (ArgumentException ex) { return BadRequest(Problem("Completion invalid", ex.Message, 400)); }
    }

    [HttpPost("verifications/claim")] public async Task<ActionResult<VerificationBatchPackageDto?>> ClaimVerifications(ClaimVerificationBatchRequest request, CancellationToken ct)
    {
        var package = await service.ClaimVerificationBatchAsync(request.AgentCode, ct);
        return package is null ? NoContent() : Ok(package);
    }

    [HttpPost("verifications/result")] public async Task<IActionResult> ReportVerificationResult(ReportVerificationResultRequest request, CancellationToken ct)
    {
        if (MissingAgent(request.AgentCode) is { } missing) return missing;
        try { await service.ReportVerificationResultAsync(request, ct); return NoContent(); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (AgentMismatchException ex) { return AgentForbidden(ex); }
        catch (ArgumentException ex) { return BadRequest(Problem("Verification result invalid", ex.Message, 400)); }
    }

    /// <summary>AUT-DATA-001: one at a time, mirroring "jobs/claim" — a DB backup is heavy/serial work.</summary>
    [HttpPost("snapshots/claim")] public async Task<ActionResult<ClaimSnapshotPackageDto?>> ClaimSnapshot(ClaimSnapshotRequest request, CancellationToken ct)
    {
        var package = await snapshots.ClaimNextAsync(request.AgentCode, ct);
        return package is null ? NoContent() : Ok(package);
    }

    [HttpPost("snapshots/{id:guid}/complete")] public async Task<ActionResult<AutomationDbSnapshotDto>> CompleteSnapshot(Guid id, CompleteSnapshotRequest request, CancellationToken ct)
    {
        if (MissingAgent(request.AgentCode) is { } missing) return missing;
        try { return Ok(await snapshots.CompleteAsync(id, request, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (AgentMismatchException ex) { return AgentForbidden(ex); }
        catch (ArgumentException ex) { return BadRequest(Problem("Snapshot completion invalid", ex.Message, 400)); }
    }

    /// <summary>AUT-DATA-002: same "one at a time" claim shape as snapshots/claim, restricted server-side to
    /// requests whose snapshot was produced by this claiming agent.</summary>
    [HttpPost("restores/claim")] public async Task<ActionResult<ClaimRestorePackageDto?>> ClaimRestore(ClaimRestoreRequest request, CancellationToken ct)
    {
        var package = await restores.ClaimNextAsync(request.AgentCode, ct);
        return package is null ? NoContent() : Ok(package);
    }

    [HttpPost("restores/{id:guid}/complete")] public async Task<ActionResult<AutomationDbRestoreDto>> CompleteRestore(Guid id, CompleteRestoreRequest request, CancellationToken ct)
    {
        if (MissingAgent(request.AgentCode) is { } missing) return missing;
        try { return Ok(await restores.CompleteAsync(id, request, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (AgentMismatchException ex) { return AgentForbidden(ex); }
        catch (ArgumentException ex) { return BadRequest(Problem("Restore completion invalid", ex.Message, 400)); }
    }

    /// <summary>AUT-DATA-003.</summary>
    [HttpPost("seed-runs/claim")] public async Task<ActionResult<ClaimSeedRunPackageDto?>> ClaimSeedRun(ClaimSeedRunRequest request, CancellationToken ct)
    {
        var package = await seeds.ClaimNextAsync(request.AgentCode, ct);
        return package is null ? NoContent() : Ok(package);
    }

    [HttpPost("seed-runs/{id:guid}/complete")] public async Task<ActionResult<AutomationDataSeedRunDto>> CompleteSeedRun(Guid id, CompleteSeedRunRequest request, CancellationToken ct)
    {
        if (MissingAgent(request.AgentCode) is { } missing) return missing;
        try { return Ok(await seeds.CompleteRunAsync(id, request, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (AgentMismatchException ex) { return AgentForbidden(ex); }
        catch (ArgumentException ex) { return BadRequest(Problem("Seed run completion invalid", ex.Message, 400)); }
    }

    // AUT-SEC-005: ทุกการรายงานผล/อัปโหลดหลักฐานจาก agent ต้องระบุ agentCode และต้องตรงกับ agent ที่รับงานนั้น
    // (service ตรวจความตรงกัน ส่วน controller บังคับว่าต้องส่งมา)
    private ObjectResult? MissingAgent(string? agentCode)
        => string.IsNullOrWhiteSpace(agentCode) ? BadRequest(Problem("agentCode is required", "Agent ต้องส่ง agentCode ของตัวเองมากับการรายงานผล (อัปเดต Agent เป็นเวอร์ชันล่าสุด)", 400)) : null;

    private ObjectResult AgentForbidden(AgentMismatchException ex) => StatusCode(StatusCodes.Status403Forbidden, Problem("Agent mismatch", ex.Message, 403));

    private async Task<IActionResult?> VerifyEvidenceAgentAsync(Guid executionId, string? agentCode, CancellationToken ct)
    {
        if (MissingAgent(agentCode) is { } missing) return missing;
        try { await service.EnsureAgentOwnsExecutionAsync(executionId, agentCode, ct); return null; }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (AgentMismatchException ex) { return AgentForbidden(ex); }
    }

    private string EvidenceRoot()
    {
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "AutomationEvidence"));
        Directory.CreateDirectory(root);
        return root + Path.DirectorySeparatorChar;
    }
}
