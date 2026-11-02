using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Governance;

namespace ProMaxx2.QA.Infrastructure.Persistence;

/// <summary>Smoke ของ Build: Status สำหรับแสดง + Passed สำหรับ ReleaseGate (ไม่ได้ตั้ง Smoke policy / ไม่ได้เลือก Build = ไม่ประเมิน)</summary>
public sealed record SmokeCheck(string Status, bool Passed);

/// <summary>
/// ข้อมูลตัดสินความพร้อมของ Release ที่ Dashboard / Test Summary และ Release Sign-off ต้องคำนวณเหมือนกัน (ตรวจ 2026-11-02 เดิมแต่ละหน้าใช้สูตรของตัวเอง
/// จน Release เดียวกันขึ้น CONDITIONAL GO หน้าหนึ่งแต่ NO_GO อีกหน้า และ Coverage 100% ที่ Dashboard แต่ 60% ที่ RTM)
/// </summary>
public static class ReleaseReadinessQueries
{
    public const string SmokeNotConfigured = "NOT_CONFIGURED";
    public const string SmokeNotRun = "NOT_RUN";

    /// <summary>Requirement ที่ Covered = ผูกกับ Test Case ที่ยังไม่ถูกลบและสถานะ Ready — กฎเดียวกับ "Covered" ของ RTM (TestCaseRepository.RtmAsync)</summary>
    public static IQueryable<Guid> CoveredRequirementIds(QaDbContext db, IReadOnlyCollection<Guid> requirementIds) =>
        db.RequirementTestCases.AsNoTracking()
            .Where(x => requirementIds.Contains(x.RequirementId) && db.TestCases.Any(t => t.TestCaseId == x.TestCaseId && !t.IsDeleted && t.Status == "Ready"))
            .Select(x => x.RequirementId).Distinct();

    /// <summary>Smoke นับเฉพาะเมื่อ Project ตั้ง Smoke policy ที่เปิดใช้ไว้และเลือก Build — เดิม Sign-off ถือว่า "ไม่มีผล Smoke" = NO_GO ทำให้ทุก Release ที่ไม่ใช้ Smoke อัตโนมัติขึ้น NO_GO เสมอ</summary>
    public static async Task<SmokeCheck> SmokeAsync(QaDbContext db, Guid projectId, Guid? buildId, CancellationToken ct)
    {
        if (!await db.AutomationBuildTriggerPolicies.AsNoTracking().AnyAsync(x => x.ProjectId == projectId && x.Pack == "Smoke" && x.IsActive, ct)) return new(SmokeNotConfigured, true);
        if (!buildId.HasValue) return new(SmokeNotRun, true);
        var status = await db.AutomationBuildTriggerRuns.AsNoTracking().Where(x => x.BuildId == buildId.Value && x.Policy.Pack == "Smoke").OrderByDescending(x => x.FiredAtUtc).Select(x => x.Status).FirstOrDefaultAsync(ct) ?? SmokeNotRun;
        return new(status, status == "Succeeded");
    }

    /// <summary>Defect ที่ยังไม่จบในขอบเขต + Risk Acceptance ที่อนุมัติแล้วและผูกกับ Defect นั้น</summary>
    public static async Task<DefectGateCounts> DefectCountsAsync(QaDbContext db, IQueryable<Defect> scope, CancellationToken ct)
    {
        var rows = await scope.AsNoTracking().Where(d => !d.IsDeleted && DefectStatusGroups.NotDone.Contains(d.Status)).Select(d => new { d.DefectId, d.Severity, d.Status }).ToListAsync(ct);
        var ids = rows.Select(d => d.DefectId).ToList();
        var accepted = (await db.RiskAcceptances.AsNoTracking().Where(r => !r.IsDeleted && r.Status == "Approved" && r.DefectId != null && ids.Contains(r.DefectId.Value)).Select(r => r.DefectId!.Value).ToListAsync(ct)).ToHashSet();
        return DefectGateCounts.From(rows.Select(d => (d.Severity, d.Status, accepted.Contains(d.DefectId))));
    }
}
