using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Execution;

namespace ProMaxx2.QA.Infrastructure.Persistence;

/// <summary>
/// Test Plan ที่แต่ละ Cycle อยู่ — หน้า Test Cycle (Badge/ลิงก์กลับ) และ Execution Workspace (Context + Banner Frozen) ใช้ร่วมกัน;
/// Query เดียวต่อหน้า (ไม่ใช่ต่อแถว) และ Cycle อยู่ได้ไม่เกิน 1 Run (unique index <c>TestPlanRuns.TestCycleId</c>)
/// </summary>
public static class TestCyclePlanLinks
{
    public static async Task<Dictionary<Guid, TestCyclePlanDto>> ForCyclesAsync(QaDbContext db, IReadOnlyCollection<Guid> cycleIds, CancellationToken ct)
    {
        if (cycleIds.Count == 0) return [];
        return await Query(db, cycleIds).ToDictionaryAsync(x => x.TestCycleId, x => x.Plan, ct);
    }

    public static IQueryable<TestCyclePlanLink> Query(QaDbContext db, IReadOnlyCollection<Guid> cycleIds) =>
        db.TestPlanRuns.AsNoTracking()
            .Where(r => cycleIds.Contains(r.TestCycleId))
            .Select(r => new TestCyclePlanLink(r.TestCycleId, new TestCyclePlanDto(r.TestPlanId, r.Plan.PlanCode, r.Plan.PlanName, r.TestPlanRunId, r.RunCode, r.RunName, r.ConfigurationName, r.Plan.LifecycleStatus, r.Plan.IsFrozen, r.Plan.IsDeleted)));
}

public sealed record TestCyclePlanLink(Guid TestCycleId, TestCyclePlanDto Plan);
