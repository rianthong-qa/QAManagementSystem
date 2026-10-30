using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.TestPlanning;
using ProMaxx2.QA.Domain.TestPlanning;

namespace ProMaxx2.QA.Infrastructure.Persistence;

/// <summary>
/// ตรวจว่า Test Cycle อยู่ใน Test Plan ที่ Frozen หรือไม่ — นับรวม Plan ที่ถูก soft delete เพราะ Plan ที่ลบได้ต้องปิด/ยกเลิก (Frozen) แล้ว
/// และผลย้อนหลังของแผนเหล่านั้นต้องไม่ถูกแก้ (Run ยังล็อก Cycle ไว้ §7.2.1)
/// </summary>
public sealed class TestPlanFreezeGuard(QaDbContext db) : ITestPlanFreezeGuard
{
    public async Task EnsureNotFrozenAsync(Guid testCycleId, CancellationToken ct)
    {
        var frozen = await db.TestPlanRuns.AsNoTracking()
            .Where(r => r.TestCycleId == testCycleId && r.Plan.IsFrozen)
            .Select(r => new { r.Plan.PlanCode, r.RunCode, r.Plan.LifecycleStatus })
            .FirstOrDefaultAsync(ct);
        if (frozen is null) return;
        var reason = frozen.LifecycleStatus == TestPlanLifecycleStatuses.Active ? "ถูก Freeze" : frozen.LifecycleStatus == TestPlanLifecycleStatuses.Closed ? "ปิดแล้ว" : "ถูกยกเลิก";
        throw new TestPlanRuleException(TestPlanErrorCodes.Frozen, $"Test Cycle นี้อยู่ใน Test Plan {frozen.PlanCode} (Run {frozen.RunCode}) ที่{reason} — แก้ผลการทดสอบ, Cycle หรือผู้รับผิดชอบไม่ได้ ให้ผู้มีสิทธิ์ Unfreeze/Reopen แผนก่อน");
    }
}
