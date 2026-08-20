using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Infrastructure.Persistence;
using Xunit;

namespace ProMaxx2.QA.UnitTests;

public sealed class DashboardQueryTranslationTests
{
    private static QaDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new QaDbContext(options);
    }

    [Fact]
    public void Dashboard_group_by_queries_translate_to_sql()
    {
        var db = CreateDb();
        var byProject = true;
        var pf = Guid.NewGuid();
        var noFilter = false;
        var allowed = new Guid[0];
        var releaseId = (Guid?)null;
        var buildId = (Guid?)null;

        var cycleBase = db.TestCycleCases.AsNoTracking().Where(x =>
            !x.Cycle.IsDeleted &&
            (byProject ? x.Cycle.ProjectId == pf : noFilter || allowed.Contains(x.Cycle.ProjectId)) &&
            (!releaseId.HasValue || x.Cycle.ReleaseId == releaseId) &&
            (!buildId.HasValue || x.Cycle.BuildId == buildId));

        var statusSql = cycleBase
            .GroupBy(x => x.CurrentStatus)
            .Select(g => new { g.Key, C = g.Count() })
            .ToQueryString();

        var moduleSql = cycleBase
            .GroupBy(x => x.TestCase.ModuleId)
            .Select(g => new
            {
                g.Key,
                Total = g.Count(),
                Executed = g.Count(x => x.CurrentStatus != "NotRun"),
                Pass = g.Count(x => x.CurrentStatus == "Pass"),
                Fail = g.Count(x => x.CurrentStatus == "Fail"),
                Blocked = g.Count(x => x.CurrentStatus == "Blocked"),
                P0 = g.Count(x => (x.CurrentStatus == "Fail" || x.CurrentStatus == "Blocked") && x.Priority == "P0"),
                P1 = g.Count(x => (x.CurrentStatus == "Fail" || x.CurrentStatus == "Blocked") && x.Priority == "P1"),
            })
            .ToQueryString();

        var userSql = db.TestExecutions.AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                !x.CycleCase.Cycle.IsDeleted &&
                (byProject ? x.CycleCase.Cycle.ProjectId == pf : noFilter || allowed.Contains(x.CycleCase.Cycle.ProjectId)) &&
                (!releaseId.HasValue || x.CycleCase.Cycle.ReleaseId == releaseId) &&
                (!buildId.HasValue || x.BuildId == buildId))
            .GroupBy(x => x.TesterUserId)
            .Select(g => new
            {
                g.Key,
                C = g.Count(),
                Pass = g.Count(x => x.Status == "Pass"),
                Fail = g.Count(x => x.Status == "Fail"),
                Blocked = g.Count(x => x.Status == "Blocked"),
                Last = g.Max(x => (DateTime?)x.CompletedAt),
            })
            .ToQueryString();

        var caseSql = db.TestCases.AsNoTracking()
            .Where(x => !x.IsDeleted && (byProject ? x.ProjectId == pf : noFilter || allowed.Contains(x.ProjectId)))
            .GroupBy(x => x.ModuleId)
            .Select(g => new { g.Key, C = g.Count() })
            .ToQueryString();

        var defectSql = db.Defects.AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                (byProject ? x.ProjectId == pf : noFilter || allowed.Contains(x.ProjectId)) &&
                (!releaseId.HasValue || x.ReleaseId == releaseId) &&
                (!buildId.HasValue || x.BuildId == buildId))
            .GroupBy(x => new { x.Severity, x.Status })
            .Select(g => new { g.Key.Severity, g.Key.Status, C = g.Count() })
            .ToQueryString();

        Assert.Contains("GROUP BY", statusSql);
        Assert.Contains("GROUP BY", moduleSql);
        Assert.Contains("GROUP BY", userSql);
        Assert.Contains("GROUP BY", caseSql);
        Assert.Contains("GROUP BY", defectSql);
    }

    [Fact]
    public void TestCycle_summary_aggregates_translate_to_sql()
    {
        var db = CreateDb();
        var projectId = Guid.NewGuid();
        var allowed = new Guid[0];

        var q = db.TestCycles.AsNoTracking().Where(x =>
            !x.IsDeleted && x.ProjectId == projectId);

        var sql = q
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                InProgress = g.Count(x => x.Status == "InProgress"),
                CompletedClosed = g.Count(x => x.Status == "Completed" || x.Status == "Closed"),
                Cancelled = g.Count(x => x.Status == "Cancelled"),
                Executed = g.Sum(x => x.Cases.Count(c => c.CurrentStatus != "NotRun")),
                CaseCount = g.Sum(x => x.Cases.Count),
            })
            .ToQueryString();

        Assert.Contains("GROUP BY", sql);
        Assert.Contains("SUM", sql);
    }
}