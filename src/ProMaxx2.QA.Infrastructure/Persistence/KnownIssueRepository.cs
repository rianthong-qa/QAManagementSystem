using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Releases;
using ProMaxx2.QA.Domain.Defects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

/// <summary>Known Issues ของ Test Summary (DEFECT_BUILD_LIFECYCLE.md เฟส 4) — Defect ที่ปิดด้วย Deferred / Won't Fix, Defect ที่ยังไม่จบแต่มี Risk Acceptance ที่อนุมัติแล้ว แล้วต่อท้ายด้วย Known Issues ที่บันทึกใน Build</summary>
public sealed class KnownIssueRepository(QaDbContext db) : IKnownIssueRepository
{
    public const int MaxDefects = 200;

    public async Task<IReadOnlyList<KnownIssueDto>> ListAsync(Guid releaseId, CancellationToken ct)
    {
        var risks = await db.RiskAcceptances.AsNoTracking()
            .Where(r => r.ReleaseId == releaseId && !r.IsDeleted && r.Status == "Approved" && r.DefectId != null)
            .OrderBy(r => r.CreatedAt).Select(r => new { DefectId = r.DefectId!.Value, r.RiskCode }).ToListAsync(ct);
        var riskByDefect = risks.GroupBy(r => r.DefectId).ToDictionary(g => g.Key, g => g.First().RiskCode);
        var riskDefectIds = riskByDefect.Keys.ToList();

        var defects = await db.Defects.AsNoTracking()
            .Where(d => d.ReleaseId == releaseId && !d.IsDeleted
                && ((d.Status == "Closed" && d.ResolutionType != null && DefectStatusGroups.KnownIssueResolutions.Contains(d.ResolutionType))
                    || (DefectStatusGroups.NotDone.Contains(d.Status) && riskDefectIds.Contains(d.DefectId))))
            .OrderBy(d => d.Severity == "Critical" ? 0 : d.Severity == "High" ? 1 : d.Severity == "Medium" ? 2 : 3).ThenBy(d => d.DefectCode)
            .Take(MaxDefects)
            .Select(d => new { d.DefectId, d.DefectCode, d.Title, d.Severity, d.Status, d.ResolutionType })
            .ToListAsync(ct);

        var builds = await db.Builds.AsNoTracking()
            .Where(b => b.ReleaseId == releaseId && b.IsActive && b.KnownIssues != null && b.KnownIssues != "")
            .OrderBy(b => b.BuildDate ?? b.CreatedAt).ThenBy(b => b.CreatedAt)
            .Select(b => new { b.BuildNumber, b.KnownIssues }).ToListAsync(ct);

        return
        [
            .. defects.Select(d => new KnownIssueDto("Defect", d.DefectCode, d.Title, d.Severity, Reason(d.Status, d.ResolutionType, riskByDefect.GetValueOrDefault(d.DefectId)), d.DefectId)),
            .. builds.Select(b => new KnownIssueDto("Build", b.BuildNumber, b.KnownIssues!.Trim(), null, "บันทึกใน Build", null)),
        ];
    }

    private static string Reason(string status, string? resolutionType, string? riskCode) => status == "Closed"
        ? resolutionType == "Deferred" ? "เลื่อนไปแก้ภายหลัง (Deferred)" : "ไม่แก้ไข (Won't Fix)"
        : $"ยอมรับความเสี่ยง {riskCode} — สถานะ {status}";
}
