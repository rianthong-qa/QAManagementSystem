using ProMaxx2.QA.Application.Dashboard;

namespace ProMaxx2.QA.Application.Releases;

public sealed record TestSummaryDto(ReleaseDto Release, DashboardSummary Summary, DateTime GeneratedAt);

public sealed class TestSummaryService(ReleaseService releases, DashboardService dashboard)
{
    public async Task<TestSummaryDto> GetAsync(Guid releaseId, CancellationToken ct)
    {
        var release = await releases.GetAsync(releaseId, ct);
        var summary = await dashboard.GetAsync(release.ProjectId, releaseId, null, ct);
        return new TestSummaryDto(release, summary, DateTime.UtcNow);
    }
}
