using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1/automation"), Authorize(Policy = "AutomationExecute")]
public sealed class AutomationAgentController(AutomationAgentService service, IWebHostEnvironment environment) : ControllerBase
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
        try { await service.ReportStepResultAsync(id, request, ct); return NoContent(); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("executions/{id:guid}/steps/{stepNo:int}/evidence"), RequestSizeLimit(10_500_000)] public async Task<IActionResult> UploadEvidence(Guid id, int stepNo, IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0 || file.Length > 10_000_000) return BadRequest(Problem("Evidence invalid", "File must be between 1 byte and 10 MB.", 400));
        string[] allowed = [".png", ".jpg", ".jpeg", ".webp", ".txt", ".log", ".json"];
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowed.Contains(ext)) return BadRequest(Problem("Evidence type not supported", "Supported: PNG, JPG, WEBP, TXT, LOG, JSON.", 400));
        var relative = Path.Combine(id.ToString("N"), $"step{stepNo}{ext}");
        var root = EvidenceRoot();
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return BadRequest();
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using (var stream = System.IO.File.Create(full)) await file.CopyToAsync(stream, ct);
        try { await service.UploadStepEvidenceAsync(id, stepNo, relative.Replace('\\', '/'), ct); return Ok(new { evidencePath = relative.Replace('\\', '/') }); }
        catch (InvalidOperationException) { System.IO.File.Delete(full); return NotFound(); }
    }

    [HttpPost("executions/{id:guid}/evidence/upload"), RequestSizeLimit(10_500_000)] public async Task<IActionResult> UploadExecutionEvidence(Guid id, [FromForm] int? stepNo, [FromForm] string evidenceType, IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0 || file.Length > 10_000_000) return BadRequest(Problem("Evidence invalid", "File must be between 1 byte and 10 MB.", 400));
        string[] allowed = [".png", ".jpg", ".jpeg", ".webp", ".txt", ".log", ".json", ".csv"];
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowed.Contains(ext)) return BadRequest(Problem("Evidence type not supported", "Supported: PNG, JPG, WEBP, TXT, LOG, JSON, CSV.", 400));
        var type = string.IsNullOrWhiteSpace(evidenceType) ? "AutomationLog" : evidenceType.Trim();
        var safeName = Path.GetFileName(file.FileName);
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
        try { return Ok(await service.CompleteExecutionAsync(id, request, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("Completion invalid", ex.Message, 400)); }
    }

    [HttpPost("verifications/claim")] public async Task<ActionResult<VerificationBatchPackageDto?>> ClaimVerifications(ClaimVerificationBatchRequest request, CancellationToken ct)
    {
        var package = await service.ClaimVerificationBatchAsync(request.AgentCode, ct);
        return package is null ? NoContent() : Ok(package);
    }

    [HttpPost("verifications/result")] public async Task<IActionResult> ReportVerificationResult(ReportVerificationResultRequest request, CancellationToken ct)
    {
        try { await service.ReportVerificationResultAsync(request, ct); return NoContent(); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("Verification result invalid", ex.Message, 400)); }
    }

    private string EvidenceRoot()
    {
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "AutomationEvidence"));
        Directory.CreateDirectory(root);
        return root + Path.DirectorySeparatorChar;
    }
}