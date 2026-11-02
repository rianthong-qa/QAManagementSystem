using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed record DeletedCycleCaseSummaryDto(int Cycles, int RemovableCases);
public sealed record RemoveDeletedCycleCasesResultDto(int Cycles, int RemovedCases, int SkippedFrozenCycles);

/// <summary>
/// เอาเคสที่ Test Case ถูกลบ (soft delete) ออกจาก Test Cycle — เฉพาะเคสที่ยังไม่เคยมีผลการทดสอบเลย (NotRun และไม่มีแถว
/// TestExecutions แม้แต่แถวที่ลบแล้ว เพราะ FK เป็น CASCADE ผลเดิมจะหายตาม) เคสที่มีผลแล้วคงไว้เป็นหลักฐานของ Cycle
/// ข้าม Cycle ที่ปิดแล้ว/ลบแล้ว และ Cycle ใน Test Plan ที่ Frozen; บันทึก Audit Log ต่อ Cycle
/// </summary>
public sealed class DeletedCycleCaseCleanupService(QaDbContext db)
{
    private static readonly string[] ClosedStatuses = ["Completed", "Closed", "Cancelled"];

    private IQueryable<Domain.Execution.TestCycleCase> Removable(IQueryable<Domain.Execution.TestCycle> cycles) =>
        db.TestCycleCases.Where(cc => cycles.Any(c => c.TestCycleId == cc.TestCycleId)
            && cc.TestCase.IsDeleted && cc.CurrentStatus == "NotRun" && !cc.Executions.Any());

    private IQueryable<Domain.Execution.TestCycle> OpenCycles(Guid projectId) =>
        db.TestCycles.Where(c => c.ProjectId == projectId && !c.IsDeleted && !ClosedStatuses.Contains(c.Status)
            && !db.TestPlanRuns.Any(r => r.TestCycleId == c.TestCycleId && r.Plan.IsFrozen));

    public async Task<DeletedCycleCaseSummaryDto> SummaryAsync(Guid projectId, CancellationToken ct)
    {
        var rows = await Removable(OpenCycles(projectId)).AsNoTracking().GroupBy(cc => cc.TestCycleId).Select(g => g.Count()).ToListAsync(ct);
        return new(rows.Count, rows.Sum());
    }

    /// <summary>cycleId = null → ทุก Cycle ใน Project</summary>
    public async Task<RemoveDeletedCycleCasesResultDto> RemoveAsync(Guid projectId, Guid? cycleId, Guid? userId, string? clientIp, CancellationToken ct)
    {
        var scope = db.TestCycles.Where(c => c.ProjectId == projectId && !c.IsDeleted && !ClosedStatuses.Contains(c.Status) && (cycleId == null || c.TestCycleId == cycleId));
        var frozen = scope.Where(c => db.TestPlanRuns.Any(r => r.TestCycleId == c.TestCycleId && r.Plan.IsFrozen));
        var skippedFrozen = await Removable(frozen).Select(cc => cc.TestCycleId).Distinct().CountAsync(ct);
        var open = scope.Where(c => !db.TestPlanRuns.Any(r => r.TestCycleId == c.TestCycleId && r.Plan.IsFrozen));
        var targets = await Removable(open).Select(cc => new { Entity = cc, cc.TestCase.TestCaseCode, cc.Cycle.CycleCode }).ToListAsync(ct);
        if (targets.Count == 0) return new(0, 0, skippedFrozen);
        db.TestCycleCases.RemoveRange(targets.Select(x => x.Entity));
        foreach (var group in targets.GroupBy(x => (x.Entity.TestCycleId, x.CycleCode)))
        {
            var codes = group.Select(x => x.TestCaseCode).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var preview = string.Join(", ", codes.Take(10)) + (codes.Count > 10 ? $" และอีก {codes.Count - 10} เคส" : "");
            db.AuditLogs.Add(new AuditLog(userId, "Updated", "TestCycle", group.Key.TestCycleId.ToString(),
                $"เอาเคสที่ Test Case ถูกลบออกจาก {group.Key.CycleCode} {codes.Count} เคส (ยังไม่มีผลทดสอบ): {preview}",
                JsonSerializer.Serialize(new { removedTestCaseCodes = codes }), null, clientIp));
        }
        await db.SaveChangesAsync(ct);
        return new(targets.Select(x => x.Entity.TestCycleId).Distinct().Count(), targets.Count, skippedFrozen);
    }
}
