using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Infrastructure.Persistence;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1"), Authorize, RequireProjectAccess]
public sealed class WorkloadController(QaDbContext db) : ControllerBase
{
    [HttpGet("my-work"), Authorize(Policy = "QaMyWorkView")]
    public async Task<IActionResult> MyWork(string? status, bool? today, CancellationToken ct)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        var query = from x in db.TestCycleCases.AsNoTracking()
                    join a in db.TestCycleCaseAssignments.AsNoTracking() on x.TestCycleCaseId equals a.TestCycleCaseId
                    where !x.Cycle.IsDeleted && x.AssignedTesterUserId == userId
                    select new { x, a };
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.x.CurrentStatus == status || x.a.Status == status);
        if (today == true) { var todayUtc = DateTime.UtcNow.Date; query = query.Where(x => x.x.Cycle.StartDate >= todayUtc || x.x.Cycle.EndDate >= todayUtc); }
        var rows = await query.OrderBy(x => x.x.Cycle.EndDate).ThenBy(x => x.x.ExecutionOrder)
            .Select(x => new { x.x.TestCycleCaseId, x.x.TestCaseId, x.x.TestCase.TestCaseCode, x.x.TestCase.Title, x.x.TestCase.ModuleId, x.x.Priority, x.x.TestCase.TestType, TestCycleId = x.x.Cycle.TestCycleId, x.x.Cycle.CycleCode, x.x.Cycle.BuildId, x.x.CurrentStatus, AssignmentStatus = x.a.Status, x.a.DueDate, x.x.EstimatedMinutesSnapshot, x.a.AssignedAt, x.x.TestCase.IsCritical, x.x.TestCase.ReviewerRequired })
            .ToListAsync(ct);
        // Module/Build are looked up separately (no navigation property on TestCase/TestCycle) so the
        // My Work table can show human-readable names instead of raw ids.
        var moduleIds = rows.Select(x => x.ModuleId).Distinct().ToList();
        var buildIds = rows.Select(x => x.BuildId).Distinct().ToList();
        var moduleNames = await db.Modules.Where(m => moduleIds.Contains(m.ModuleId)).ToDictionaryAsync(m => m.ModuleId, m => m.ModuleName, ct);
        var buildNumbers = await db.Builds.Where(b => buildIds.Contains(b.BuildId)).ToDictionaryAsync(b => b.BuildId, b => b.BuildNumber, ct);
        var result = rows.Select(x => new
        {
            x.TestCycleCaseId, x.TestCaseId, x.TestCaseCode, x.Title,
            x.ModuleId, ModuleName = moduleNames.GetValueOrDefault(x.ModuleId),
            x.Priority, x.TestType, x.TestCycleId, x.CycleCode,
            x.BuildId, BuildNumber = buildNumbers.GetValueOrDefault(x.BuildId),
            x.CurrentStatus, x.AssignmentStatus, x.DueDate, x.EstimatedMinutesSnapshot, x.AssignedAt,
            x.IsCritical, x.ReviewerRequired,
        });
        return Ok(new { userId, total = rows.Count, rows = result });
    }

    [HttpGet("qa-workload"), Authorize(Policy = "QaWorkloadView")]
    public async Task<IActionResult> QaWorkload(Guid? projectId, Guid? testCycleId, CancellationToken ct)
    {
        var query = db.TestCycleCases.AsNoTracking().Where(x => !x.Cycle.IsDeleted);
        if (projectId.HasValue) query = query.Where(x => x.Cycle.ProjectId == projectId);
        if (testCycleId.HasValue) query = query.Where(x => x.TestCycleId == testCycleId);
        var rows = await (from x in query
                          join a in db.TestCycleCaseAssignments.AsNoTracking() on x.TestCycleCaseId equals a.TestCycleCaseId
                          where x.AssignedTesterUserId.HasValue
                          group new { x, a } by new { x.AssignedTesterUserId, a.Status } into g
                          select new { userId = g.Key.AssignedTesterUserId, assignmentStatus = g.Key.Status, count = g.Count(), estimatedMinutes = g.Sum(y => y.x.EstimatedMinutesSnapshot) })
            .ToListAsync(ct);
        var cases = await (from x in query
                           join a in db.TestCycleCaseAssignments.AsNoTracking() on x.TestCycleCaseId equals a.TestCycleCaseId
                           where x.AssignedTesterUserId.HasValue
                           select new { userId = x.AssignedTesterUserId, x.TestCycleCaseId, x.TestCase.TestCaseCode, x.TestCase.Title, assignmentStatus = a.Status }).ToListAsync(ct);
        return Ok(rows.GroupBy(x => x.userId).Select(g => new { userId = g.Key, assigned = g.Sum(x => x.count), estimatedMinutes = g.Sum(x => x.estimatedMinutes), statuses = g.ToDictionary(x => x.assignmentStatus, x => x.count), cases = cases.Where(x => x.userId == g.Key) }));
    }

    [HttpPost("test-cycles/{cycleId:guid}/assign"), Authorize(Policy = "QaAssignmentCreate")]
    public async Task<IActionResult> Assign(Guid cycleId, AssignRequest request, CancellationToken ct)
    {
        if (!TryUserId(out var actor)) return Unauthorized();
        if (request.TesterUserId == Guid.Empty || request.TestCycleCaseIds is null || request.TestCycleCaseIds.Count == 0) return BadRequest("Tester and cases are required.");
        var cases = await db.TestCycleCases.Where(x => x.TestCycleId == cycleId && request.TestCycleCaseIds.Contains(x.TestCycleCaseId)).ToListAsync(ct);
        if (cases.Count != request.TestCycleCaseIds.Distinct().Count()) return BadRequest("One or more cases do not belong to the cycle.");
        foreach (var item in cases)
        {
            var old = item.AssignedTesterUserId;
            item.AssignTester(request.TesterUserId);
            var metadata = await db.TestCycleCaseAssignments.SingleOrDefaultAsync(x => x.TestCycleCaseId == item.TestCycleCaseId, ct);
            if (metadata is null) db.TestCycleCaseAssignments.Add(metadata = new TestCycleCaseAssignment(item.TestCycleCaseId));
            metadata.Assign(actor, request.DueDate);
            db.AssignmentHistories.Add(new AssignmentHistory(item.TestCycleCaseId, old, request.TesterUserId, item.CaseWeight, 0, request.Reason ?? "Manual assignment", old.HasValue ? "Reassigned" : "Assigned", actor, "manual-v1"));
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { cycleId, assigned = cases.Count });
    }

    [HttpPost("test-cycle-cases/{cycleCaseId:guid}/accept")]
    public Task<IActionResult> Accept(Guid cycleCaseId, CancellationToken ct) => ChangeAssignment(cycleCaseId, false, ct);

    [HttpPost("test-cycle-cases/{cycleCaseId:guid}/start")]
    public Task<IActionResult> Start(Guid cycleCaseId, CancellationToken ct) => ChangeAssignment(cycleCaseId, true, ct);

    [HttpPost("test-cycle-cases/{cycleCaseId:guid}/reassign"), Authorize(Policy = "QaAssignmentReassign")]
    public async Task<IActionResult> Reassign(Guid cycleCaseId, ReassignRequest request, CancellationToken ct)
    {
        if (!TryUserId(out var actor)) return Unauthorized();
        if (request.TesterUserId == Guid.Empty) return BadRequest("Tester is required.");
        var item = await db.TestCycleCases.Include(x => x.Cycle).SingleOrDefaultAsync(x => x.TestCycleCaseId == cycleCaseId, ct);
        if (item is null) return NotFound();
        var old = item.AssignedTesterUserId;
        try { item.AssignTester(request.TesterUserId); }
        catch (InvalidOperationException ex) { return Conflict(new { code = "ASSIGNMENT_CONFLICT", detail = ex.Message }); }
        var metadata = await db.TestCycleCaseAssignments.SingleOrDefaultAsync(x => x.TestCycleCaseId == cycleCaseId, ct);
        if (metadata is null) db.TestCycleCaseAssignments.Add(metadata = new TestCycleCaseAssignment(cycleCaseId));
        metadata.Assign(actor, request.DueDate);
        db.AssignmentHistories.Add(new AssignmentHistory(cycleCaseId, old, request.TesterUserId, item.CaseWeight, 0, request.Reason ?? "Manual reassignment", "Reassigned", actor, "manual-v1"));
        await db.SaveChangesAsync(ct);
        return Ok(new { cycleCaseId, previousTesterUserId = old, testerUserId = request.TesterUserId, status = metadata.Status });
    }

    private async Task<IActionResult> ChangeAssignment(Guid cycleCaseId, bool start, CancellationToken ct)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        var item = await db.TestCycleCases.SingleOrDefaultAsync(x => x.TestCycleCaseId == cycleCaseId && x.AssignedTesterUserId == userId, ct);
        var metadata = await db.TestCycleCaseAssignments.SingleOrDefaultAsync(x => x.TestCycleCaseId == cycleCaseId, ct);
        if (item is null || metadata is null) return NotFound();
        try { if (start) metadata.Start(); else metadata.Accept(); }
        catch (InvalidOperationException ex) { return Conflict(new { code = "INVALID_ASSIGNMENT_TRANSITION", detail = ex.Message }); }
        await db.SaveChangesAsync(ct);
        return Ok(new { cycleCaseId, status = metadata.Status });
    }

    private bool TryUserId(out Guid id) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out id);
    public sealed record AssignRequest(IReadOnlyList<Guid> TestCycleCaseIds, Guid TesterUserId, string? Reason, DateTime? DueDate);
    public sealed record ReassignRequest(Guid TesterUserId, string? Reason, DateTime? DueDate);
}
