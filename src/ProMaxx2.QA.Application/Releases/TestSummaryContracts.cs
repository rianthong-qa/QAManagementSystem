using ProMaxx2.QA.Application.Dashboard;

namespace ProMaxx2.QA.Application.Releases;

public sealed record TestSummaryDto(ReleaseDto Release, DashboardSummary Summary, DateTime GeneratedAt, IReadOnlyList<KnownIssueDto> KnownIssues);

/// <summary>Known Issue ของ Release (DEFECT_BUILD_LIFECYCLE.md เฟส 4) — Source = Defect (ปิดด้วย Deferred / Won't Fix หรือยังไม่จบแต่มี Risk Acceptance ที่อนุมัติแล้ว) หรือ Build (ข้อความ Known Issues ของ Build)</summary>
public sealed record KnownIssueDto(string Source, string Reference, string Title, string? Severity, string Reason, Guid? DefectId);

public interface IKnownIssueRepository
{
    Task<IReadOnlyList<KnownIssueDto>> ListAsync(Guid releaseId, CancellationToken ct);
}

public sealed class TestSummaryService(ReleaseService releases, DashboardService dashboard, IKnownIssueRepository knownIssues)
{
    public async Task<TestSummaryDto> GetAsync(Guid releaseId, CancellationToken ct)
    {
        var release = await releases.GetAsync(releaseId, ct);
        var summary = await dashboard.GetAsync(release.ProjectId, releaseId, null, ct);
        return new TestSummaryDto(release, summary, DateTime.UtcNow, await knownIssues.ListAsync(releaseId, ct));
    }
}
