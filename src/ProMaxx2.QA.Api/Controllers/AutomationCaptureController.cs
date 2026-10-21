using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;
using ProMaxx2.QA.Infrastructure.Persistence;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1/automation/capture"), Authorize(Policy = "AutomationManage"), RequireProjectAccess]
public sealed class AutomationCaptureController(QaDbContext db, ProjectAccessContext projectAccess) : ControllerBase
{
    [HttpGet("test-cases")]
    public async Task<IActionResult> TestCases([FromQuery] Guid projectId, CancellationToken ct) => Ok(await db.TestCases.AsNoTracking().Where(x => x.ProjectId == projectId && !x.IsDeleted).OrderBy(x => x.TestCaseCode).Select(x => new { x.TestCaseId, x.ProjectId, x.ModuleId, x.TestCaseCode, x.Title }).ToListAsync(ct));

    [HttpPost("sessions")]
    public async Task<ActionResult<CaptureSessionDto>> Create([FromQuery] Guid? projectId, CreateCaptureSessionRequest request, CancellationToken ct)
    {
        if (projectId.HasValue && projectId.Value != request.ProjectId || !projectAccess.AllowedProjectIds.Contains(request.ProjectId)) return Forbid();
        if (request.Items is null || request.Items.Count == 0) return BadRequest("At least one capture item is required.");
        if (request.Items.Any(x => string.IsNullOrWhiteSpace(x.ObjectCode) || string.IsNullOrWhiteSpace(x.Action))) return BadRequest("ObjectCode and Action are required for every capture item.");
        var duplicateAutomationIds = request.Items.Where(x => !string.IsNullOrWhiteSpace(x.AutomationId)).GroupBy(x => x.AutomationId!, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
        if (duplicateAutomationIds.Count > 0) return BadRequest($"Duplicate AutomationId in capture items: {string.Join(", ", duplicateAutomationIds)}");
        if (request.ApplicationCode is not ("pos" or "app")) return BadRequest("ApplicationCode must be pos or app.");
        var tc = await db.TestCases.SingleOrDefaultAsync(x => x.TestCaseId == request.TestCaseId && x.ProjectId == request.ProjectId && x.ModuleId == request.ModuleId && !x.IsDeleted, ct);
        if (tc is null) return NotFound("Test case not found.");
        var items = request.Items.Select(x => x with { TestData = x.Sensitive ? null : x.TestData, ExpectedResult = string.IsNullOrWhiteSpace(x.ExpectedResult) ? "ผู้ใช้ตรวจสอบผลลัพธ์หลังดำเนินการ" : x.ExpectedResult }).ToList();
        var session = new AutomationCaptureSession(request.ProjectId, request.ModuleId, request.TestCaseId, UserId(), request.ApplicationCode, request.SourceMachine, request.ApplicationVersion, JsonSerializer.Serialize(items));
        db.AutomationCaptureSessions.Add(session); await db.SaveChangesAsync(ct);
        return Ok(await Preview(session, items, ct));
    }

    [HttpPost("sessions/{id:guid}/preview")]
    public async Task<ActionResult<CaptureSessionDto>> Preview(Guid id, CancellationToken ct)
    {
        var s = await db.AutomationCaptureSessions.SingleOrDefaultAsync(x => x.CaptureSessionId == id && x.UserId == UserId() && projectAccess.AllowedProjectIds.Contains(x.ProjectId), ct); if (s is null) return NotFound(); if (s.Status != "Draft" || s.ExpiresAt < DateTime.UtcNow) return Conflict("Capture session is no longer available.");
        return Ok(await Preview(s, JsonSerializer.Deserialize<List<CaptureItemRequest>>(s.ItemsJson) ?? [], ct));
    }

    [HttpPost("sessions/{id:guid}/commit")]
    public async Task<ActionResult<CaptureCommitResultDto>> Commit(Guid id, CancellationToken ct)
    {
        var s = await db.AutomationCaptureSessions.SingleOrDefaultAsync(x => x.CaptureSessionId == id && x.UserId == UserId() && projectAccess.AllowedProjectIds.Contains(x.ProjectId), ct);
        if (s is null) return NotFound(); if (s.Status != "Draft" || s.ExpiresAt < DateTime.UtcNow) return Conflict("Capture session is no longer available.");
        var tc = await db.TestCases.Include(x => x.Steps).SingleOrDefaultAsync(x => x.TestCaseId == s.TestCaseId && x.ProjectId == s.ProjectId && !x.IsDeleted, ct); if (tc is null) return NotFound();
        var items = JsonSerializer.Deserialize<List<CaptureItemRequest>>(s.ItemsJson) ?? [];
        var firstNewStep = tc.Steps.Where(x => x.RevisionNo == tc.RevisionNo).Select(x => x.StepNo).DefaultIfEmpty(0).Max() + 1;
        items = items.OrderBy(x => x.StepNo).Select((x, index) => x with { StepNo = firstNewStep + index }).ToList();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var results = new List<CaptureObjectPreviewDto>(); var created = 0; var matched = 0;
        foreach (var item in items.OrderBy(x => x.StepNo))
        {
            var existing = !string.IsNullOrWhiteSpace(item.AutomationId) ? await db.AutomationObjects.FirstOrDefaultAsync(x => x.ProjectId == s.ProjectId && x.ApplicationCode == s.ApplicationCode && x.AutomationId == item.AutomationId && x.IsActive, ct) : null;
            if (existing is null && !string.IsNullOrWhiteSpace(item.AutomationId)) { db.AutomationObjects.Add(new AutomationObject(s.ProjectId, s.ModuleId, s.ApplicationCode, item.ScreenCode, item.ObjectCode, item.ObjectName, item.ControlType, item.AutomationId, item.SelectorJson)); created++; results.Add(new(item.StepNo, item.ObjectCode, item.AutomationId, "New", "Object created.")); }
            else if (existing is not null) { matched++; results.Add(new(item.StepNo, item.ObjectCode, item.AutomationId, "Matched", "Existing object reused.")); }
            else results.Add(new(item.StepNo, item.ObjectCode, null, "Missing AutomationId", "Review selector before automation."));
        }
        var stepInputs = tc.Steps.Where(x => x.RevisionNo == tc.RevisionNo).OrderBy(x => x.StepNo).Select(x => new ProMaxx2.QA.Domain.TestManagement.TestStepInput(x.StepNo, x.Action, x.TestDataText, x.ExpectedResult)).Concat(items.OrderBy(x => x.StepNo).Select(x => new ProMaxx2.QA.Domain.TestManagement.TestStepInput(x.StepNo, x.Action, x.TestData, x.ExpectedResult))).ToList();
        tc.CreateRevision(tc.Title, tc.Objective, tc.Preconditions, stepInputs, "Imported from ProMaxx2 Capture Companion", UserId());
        s.Complete("Committed"); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Ok(new CaptureCommitResultDto(id, tc.TestCaseId, tc.RevisionNo, created, matched, results));
    }

    [HttpPost("sessions/{id:guid}/discard")]
    public async Task<IActionResult> Discard(Guid id, CancellationToken ct) { var s = await db.AutomationCaptureSessions.SingleOrDefaultAsync(x => x.CaptureSessionId == id && x.UserId == UserId() && projectAccess.AllowedProjectIds.Contains(x.ProjectId), ct); if (s is null) return NotFound(); s.Complete("Discarded"); await db.SaveChangesAsync(ct); return NoContent(); }
    private async Task<CaptureSessionDto> Preview(AutomationCaptureSession s, IReadOnlyList<CaptureItemRequest> items, CancellationToken ct) { var ids = items.Where(x => !string.IsNullOrWhiteSpace(x.AutomationId)).Select(x => x.AutomationId!).ToList(); var existing = await db.AutomationObjects.Where(x => x.ProjectId == s.ProjectId && x.ApplicationCode == s.ApplicationCode && ids.Contains(x.AutomationId!)).Select(x => x.AutomationId!).ToListAsync(ct); return new(s.CaptureSessionId, s.Status, items.OrderBy(x => x.StepNo).Select(x => new CaptureObjectPreviewDto(x.StepNo, x.ObjectCode, x.AutomationId, string.IsNullOrWhiteSpace(x.AutomationId) ? "Missing AutomationId" : existing.Contains(x.AutomationId) ? "Matched" : "New", string.IsNullOrWhiteSpace(x.AutomationId) ? "AutomationId is required for stable selector." : "Ready for review.")).ToList()); }
    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
}
