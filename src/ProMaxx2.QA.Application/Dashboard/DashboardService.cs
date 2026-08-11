namespace ProMaxx2.QA.Application.Dashboard;

public sealed record DashboardSummary(
    decimal RequirementCoverage,
    decimal ExecutionProgress,
    decimal PassRate,
    int OpenP0,
    int OpenP1,
    string RecommendedDecision);

public interface IDashboardService
{
    DashboardSummary GetSummary();
}

public sealed class DashboardService : IDashboardService
{
    public DashboardSummary GetSummary() => new(94m, 82m, 91.7m, 0, 2, "CONDITIONAL GO");
}
