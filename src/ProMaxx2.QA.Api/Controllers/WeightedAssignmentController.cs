using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Infrastructure.Persistence;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1/test-cycles/{cycleId:guid}/auto-assign"), Authorize(Policy = "ExecutionRun"), RequireProjectAccess]
public sealed class WeightedAssignmentController(QaDbContext db) : ControllerBase
{
    [HttpPost("regression-auto-preview")]
    public Task<ActionResult<AutoAssignPreviewDto>> RegressionAutoPreview(Guid cycleId, AutoAssignPreviewRequest request, CancellationToken ct) => Preview(cycleId, request, ct);

    [HttpPost("preview")]
    public async Task<ActionResult<AutoAssignPreviewDto>> Preview(Guid cycleId, AutoAssignPreviewRequest request, CancellationToken ct)
    {
        var cycle = await db.TestCycles.AsNoTracking().SingleOrDefaultAsync(x => x.TestCycleId == cycleId && !x.IsDeleted, ct);
        if (cycle is null) return NotFound();
        var ids = request.TestCycleCaseIds.Count == 0 ? null : request.TestCycleCaseIds.ToHashSet();
        var cases = await db.TestCycleCases.AsNoTracking().Include(x => x.TestCase).Where(x => x.TestCycleId == cycleId && x.CurrentStatus != "InProgress" && x.CurrentStatus != "Completed" && (ids == null || ids.Contains(x.TestCycleCaseId))).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().Where(x => x.IsActive && (request.QaPoolUserIds.Count == 0 || request.QaPoolUserIds.Contains(x.UserId))).ToListAsync(ct);
        var skills = await db.QaSkillMatrixEntries.AsNoTracking().Where(x => users.Select(u => u.UserId).Contains(x.UserId) && x.IsActive).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var availability = await db.QaAvailabilities.AsNoTracking().Where(x => x.Date == today && users.Select(u => u.UserId).Contains(x.UserId)).ToListAsync(ct);
        var assignedWork = await db.TestCycleCases.AsNoTracking().Include(x => x.TestCase).Where(x => x.AssignedTesterUserId.HasValue && x.CurrentStatus != "Completed" && x.CurrentStatus != "Skipped").GroupBy(x => x.AssignedTesterUserId!.Value).Select(x => new { UserId = x.Key, Minutes = x.Sum(y => y.TestCase.EstimatedMinutes) }).ToDictionaryAsync(x => x.UserId, x => x.Minutes, ct);
        var input = cases.Select(x => new WeightedAssignmentCase(x.TestCycleCaseId, x.Priority ?? x.TestCase.Priority, x.TestCase.ComplexityWeight, x.TestCase.EstimatedMinutes, x.TestCase.RequiredSkillLevel, x.TestCase.IsCritical, x.TestCase.ReviewerRequired));
        var executionCounts = await db.TestExecutions.AsNoTracking().Where(x => x.TesterUserId != Guid.Empty).GroupBy(x => x.TesterUserId).Select(x => new { UserId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
        var candidates = users.Select(u => { var skill = skills.Where(x => x.UserId == u.UserId).Select(x => x.Level).DefaultIfEmpty(0).Max(); var a = availability.SingleOrDefault(x => x.UserId == u.UserId); var experience = Math.Min(100, executionCounts.GetValueOrDefault(u.UserId) * 5); var capacity = a?.CapacityMinutes ?? 0; var current = assignedWork.GetValueOrDefault(u.UserId); var load = capacity == 0 ? 0 : Math.Clamp((int)Math.Round(current * 100m / capacity), 0, 200); return new WeightedAssignmentCandidate(u.UserId, u.DisplayName, skill, load, Math.Max(0, capacity - current), a?.IsAssignable == true, skill >= 4, experience); });
        var suggestions = WeightedAssignmentEngine.Suggest(input, candidates);
        var actor = CurrentUserId(); var preview = new ProMaxx2.QA.Domain.Execution.AssignmentPreview(cycleId, actor, DateTime.UtcNow.AddMinutes(10)); db.AssignmentPreviews.Add(preview); await db.SaveChangesAsync(ct);
        var threshold = Math.Clamp(request.WorkloadThresholdPercent, 1, 200);
        var warnings = suggestions.Where(x => x.ErrorCode is not null).Select(x => $"{x.TestCycleCaseId}: {x.ErrorCode}").ToList();
        warnings.AddRange(suggestions.Where(x => x.ErrorCode is null && x.AfterLoadPercent > threshold).Select(x => $"{x.TestCycleCaseId}: AUTOASSIGN_CAPACITY_EXCEEDED ({x.AfterLoadPercent}% > {threshold}%)"));
        return Ok(new AutoAssignPreviewDto(preview.AssignmentPreviewId, preview.Version, preview.ExpiresAt, suggestions, warnings, users.Select(x => new AutoAssignTesterOption(x.UserId, x.DisplayName)).ToList()));
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(Guid cycleId, AutoAssignConfirmRequest request, CancellationToken ct)
    {
        var preview = await db.AssignmentPreviews.SingleOrDefaultAsync(x => x.AssignmentPreviewId == request.PreviewId && x.TestCycleId == cycleId, ct);
        if (preview is null) return NotFound();
        try { WeightedAssignmentWorkflow.ValidateConfirm(request, new AssignmentPreviewState(preview.AssignmentPreviewId, preview.Version, preview.ExpiresAt), DateTime.UtcNow); }
        catch (InvalidOperationException e) when (e.Message == "AUTOASSIGN_PREVIEW_EXPIRED") { return Conflict(new { code = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { code = e.Message }); }
        var ids = request.Assignments.Select(x => x.TestCycleCaseId).ToHashSet(); var cases = await db.TestCycleCases.Where(x => x.TestCycleId == cycleId && ids.Contains(x.TestCycleCaseId)).ToListAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var actor = CurrentUserId(); foreach (var item in request.Assignments) { var entity = cases.SingleOrDefault(x => x.TestCycleCaseId == item.TestCycleCaseId); if (entity is null) return BadRequest(new { code = "AUTOASSIGN_CASE_NOT_FOUND" }); var old = entity.AssignedTesterUserId; try { entity.AssignTester(item.TesterUserId); } catch (InvalidOperationException) { await tx.RollbackAsync(ct); return Conflict(new { code = "AUTOASSIGN_ASSIGNMENT_CONFLICT" }); } db.AssignmentHistories.Add(new ProMaxx2.QA.Domain.Execution.AssignmentHistory(entity.TestCycleCaseId, old, item.TesterUserId, entity.CaseWeight, 0, item.OverrideReason, old.HasValue ? "AssignmentOverridden" : "AssignmentConfirmed", actor, entity.AlgorithmVersion)); }
        preview.Confirm(); try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); } catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(ct); return Conflict(new { code = "AUTOASSIGN_ASSIGNMENT_CONFLICT" }); } return NoContent();
    }

    [HttpPost("rebalance")]
    public async Task<IActionResult> Rebalance(Guid cycleId, AutoRebalanceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return BadRequest(new { code = "AUTOASSIGN_REBALANCE_REASON_REQUIRED" });
        var ids = request.Assignments.Select(x => x.TestCycleCaseId).ToHashSet();
        var cases = await db.TestCycleCases.Where(x => x.TestCycleId == cycleId && ids.Contains(x.TestCycleCaseId)).ToListAsync(ct);
        if (cases.Count != ids.Count) return BadRequest(new { code = "AUTOASSIGN_CASE_NOT_FOUND" });
        await using var tx = await db.Database.BeginTransactionAsync(ct); var actor = CurrentUserId();
        foreach (var item in request.Assignments)
        {
            var entity = cases.Single(x => x.TestCycleCaseId == item.TestCycleCaseId); var old = entity.AssignedTesterUserId;
            try { entity.AssignTester(item.TesterUserId); } catch (InvalidOperationException) { await tx.RollbackAsync(ct); return Conflict(new { code = "AUTOASSIGN_ASSIGNMENT_CONFLICT" }); }
            db.AssignmentHistories.Add(new ProMaxx2.QA.Domain.Execution.AssignmentHistory(entity.TestCycleCaseId, old, item.TesterUserId, entity.CaseWeight, 0, request.Reason, "AssignmentRebalanced", actor, entity.AlgorithmVersion));
        }
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); } catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(ct); return Conflict(new { code = "AUTOASSIGN_ASSIGNMENT_CONFLICT" }); }
        return NoContent();
    }

    [HttpGet("history")]
    public async Task<ActionResult> History(Guid cycleId, CancellationToken ct)
    {
        var rows = await db.AssignmentHistories.AsNoTracking().Where(x => x.TestCycleCase.Cycle.TestCycleId == cycleId).OrderByDescending(x => x.CreatedAt).Select(x => new { x.AssignmentHistoryId, x.TestCycleCaseId, x.SuggestedTesterUserId, x.FinalTesterUserId, x.Weight, x.Score, x.Reason, x.Action, x.ActorUserId, x.AlgorithmVersion, x.CreatedAt }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("integration-decision")]
    public async Task<ActionResult<AssignmentIntegrationDecision>> IntegrationDecision(Guid cycleId, AssignmentIntegrationRequest request, CancellationToken ct)
    {
        if (request.TestCycleCaseId == Guid.Empty) return BadRequest(new { code = "AUTOASSIGN_CASE_REQUIRED" });
        var users = await db.Users.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var skills = await db.QaSkillMatrixEntries.AsNoTracking().Where(x => users.Select(u => u.UserId).Contains(x.UserId) && x.IsActive).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow); var availability = await db.QaAvailabilities.AsNoTracking().Where(x => x.Date == today && users.Select(u => u.UserId).Contains(x.UserId)).ToListAsync(ct);
        var candidates = users.Select(u => { var skill = skills.Where(x => x.UserId == u.UserId).Select(x => x.Level).DefaultIfEmpty(0).Max(); var a = availability.SingleOrDefault(x => x.UserId == u.UserId); return new WeightedAssignmentCandidate(u.UserId, u.DisplayName, skill, 0, a?.CapacityMinutes ?? 0, a?.IsAssignable == true, skill >= 4); });
        return Ok(AssignmentIntegrationRules.PreferOriginal(request, candidates));
    }

    [HttpGet("retest-decision/{cycleCaseId:guid}")]
    public async Task<ActionResult<AssignmentIntegrationDecision>> RetestDecision(Guid cycleId, Guid cycleCaseId, CancellationToken ct)
    {
        var cycleCase = await db.TestCycleCases.AsNoTracking().Include(x => x.TestCase).Include(x => x.Executions).SingleOrDefaultAsync(x => x.TestCycleId == cycleId && x.TestCycleCaseId == cycleCaseId, ct);
        if (cycleCase is null) return NotFound();
        var original = cycleCase.Executions.Where(x => !x.IsDeleted).OrderByDescending(x => x.ExecutionNo).Select(x => (Guid?)x.TesterUserId).FirstOrDefault();
        var request = new AssignmentIntegrationRequest(AssignmentWorkKind.DefectRetest, cycleCaseId, original, cycleCase.TestCase.RequiredSkillLevel, cycleCase.TestCase.EstimatedMinutes);
        var users = await db.Users.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct); var skills = await db.QaSkillMatrixEntries.AsNoTracking().Where(x => users.Select(u => u.UserId).Contains(x.UserId) && x.IsActive).ToListAsync(ct); var date = DateOnly.FromDateTime(DateTime.UtcNow); var availability = await db.QaAvailabilities.AsNoTracking().Where(x => x.Date == date && users.Select(u => u.UserId).Contains(x.UserId)).ToListAsync(ct);
        var candidates = users.Select(u => { var skill = skills.Where(x => x.UserId == u.UserId).Select(x => x.Level).DefaultIfEmpty(0).Max(); var a = availability.SingleOrDefault(x => x.UserId == u.UserId); return new WeightedAssignmentCandidate(u.UserId, u.DisplayName, skill, 0, a?.CapacityMinutes ?? 0, a?.IsAssignable == true, skill >= 4); });
        return Ok(AssignmentIntegrationRules.PreferOriginal(request, candidates));
    }

    private Guid CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : Guid.Empty;
}
