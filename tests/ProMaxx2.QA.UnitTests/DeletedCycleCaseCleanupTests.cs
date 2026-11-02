using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Infrastructure.Persistence;
namespace ProMaxx2.QA.UnitTests;

/// <summary>เอาเคสที่ Test Case ถูกลบออกจาก Cycle: เอาออกเฉพาะเคสที่ยังไม่เคยมีผล — เคสที่มีผล/Test Case ที่ยังใช้งาน/Cycle ที่ปิดแล้วต้องคงไว้</summary>
public sealed class DeletedCycleCaseCleanupTests
{
    [Fact]
    public async Task Removes_only_never_executed_cases_of_deleted_test_cases_and_writes_audit()
    {
        using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var b = await AutomationTestFixtures.SeedBaselineAsync(db, automationCandidate: false);
        var tester = new User("tester", "Tester", null, "hash");
        var deletedNotRun = Case(b, "TC-DEL-1");
        var deletedWithResult = Case(b, "TC-DEL-2");
        var deletedRunThenRemoved = Case(b, "TC-DEL-3");
        var open = Cycle(b, tester, "CYC-OPEN");
        var closed = Cycle(b, tester, "CYC-CLOSED");
        closed.ChangeStatus("Closed");
        var removable = new TestCycleCase(open.TestCycleId, deletedNotRun.TestCaseId, 1, "P1", 1);
        var keptWithResult = new TestCycleCase(open.TestCycleId, deletedWithResult.TestCaseId, 1, "P1", 2);
        keptWithResult.SetStatus("Pass");
        // ผลถูกลบ (soft delete) จนสถานะกลับเป็น NotRun — ยังมีแถว TestExecutions ที่ CASCADE จะลบตาม จึงต้องคงไว้
        var keptSoftDeletedRun = new TestCycleCase(open.TestCycleId, deletedRunThenRemoved.TestCaseId, 1, "P1", 3);
        var keptLiveCase = new TestCycleCase(open.TestCycleId, b.TestCase.TestCaseId, b.TestCase.RevisionNo, "P1", 4);
        var keptInClosedCycle = new TestCycleCase(closed.TestCycleId, deletedNotRun.TestCaseId, 1, "P1", 1);
        var pass = new TestExecution(keptWithResult.TestCycleCaseId, 1, b.Build.BuildId, b.Environment.TestEnvironmentId, tester.UserId, "Pass", null, null, [new StepResultInput(1, "Pass", null, null)]);
        var removedRun = new TestExecution(keptSoftDeletedRun.TestCycleCaseId, 1, b.Build.BuildId, b.Environment.TestEnvironmentId, tester.UserId, "Fail", "x", null, [new StepResultInput(1, "Fail", "x", null)]);
        removedRun.SoftDelete(tester.UserId);
        db.AddRange(tester, deletedNotRun, deletedWithResult, deletedRunThenRemoved, open, closed, removable, keptWithResult, keptSoftDeletedRun, keptLiveCase, keptInClosedCycle, pass, removedRun);
        await db.SaveChangesAsync();
        foreach (var tc in new[] { deletedNotRun, deletedWithResult, deletedRunThenRemoved }) tc.SoftDelete(tester.UserId);
        await db.SaveChangesAsync();
        var service = new DeletedCycleCaseCleanupService(db);

        var summary = await service.SummaryAsync(b.Project.ProjectId, CancellationToken.None);
        var result = await service.RemoveAsync(b.Project.ProjectId, null, tester.UserId, "127.0.0.1", CancellationToken.None);

        Assert.Equal((1, 1), (summary.Cycles, summary.RemovableCases));
        Assert.Equal((1, 1, 0), (result.Cycles, result.RemovedCases, result.SkippedFrozenCycles));
        var remaining = await db.TestCycleCases.Select(x => x.TestCycleCaseId).ToListAsync();
        Assert.DoesNotContain(removable.TestCycleCaseId, remaining);
        Assert.Equal(4, remaining.Count);
        Assert.Equal(2, await db.TestExecutions.CountAsync());
        var audit = Assert.Single(await db.AuditLogs.ToListAsync());
        Assert.Equal(open.TestCycleId.ToString(), audit.EntityId);
        Assert.Contains("TC-DEL-1", audit.ChangeSummary);
        Assert.Equal(0, (await service.RemoveAsync(b.Project.ProjectId, open.TestCycleId, tester.UserId, null, CancellationToken.None)).RemovedCases);
    }

    [Fact]
    public void Cleanup_queries_translate_to_sql()
    {
        // InMemory ไม่ตรวจการแปลงเป็น SQL — ตรวจ query หลักของ service กับ provider SQL Server จริง
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer(@"Server=(localdb)\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True").Options);
        var closedStatuses = new[] { "Completed", "Closed", "Cancelled" };
        var projectId = Guid.NewGuid();
        var cycles = db.TestCycles.Where(c => c.ProjectId == projectId && !c.IsDeleted && !closedStatuses.Contains(c.Status) && !db.TestPlanRuns.Any(r => r.TestCycleId == c.TestCycleId && r.Plan.IsFrozen));
        var sql = db.TestCycleCases.Where(cc => cycles.Any(c => c.TestCycleId == cc.TestCycleId) && cc.TestCase.IsDeleted && cc.CurrentStatus == "NotRun" && !cc.Executions.Any())
            .GroupBy(cc => cc.TestCycleId).Select(g => g.Count()).ToQueryString();
        Assert.Contains("[TestExecutions]", sql);
        Assert.Contains("[IsFrozen]", sql);
    }

    private static TestCase Case(AutomationTestFixtures.Baseline b, string code) => new(b.Project.ProjectId, b.Module.ModuleId, code, code, null, null, "P1", null, false, null, [new TestStepInput(1, "Action", null, "Expected")], null);
    private static TestCycle Cycle(AutomationTestFixtures.Baseline b, User owner, string code) => new(b.Project.ProjectId, b.Release.ReleaseId, b.Build.BuildId, b.Environment.TestEnvironmentId, null, code, code, "Smoke", null, null, null, null, owner.UserId);
}
