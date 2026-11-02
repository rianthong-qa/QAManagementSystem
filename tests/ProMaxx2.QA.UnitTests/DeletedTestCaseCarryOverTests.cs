using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Test Case ที่ถูกลบต้องไม่ถูกพาเข้า Cycle ใหม่ (Populate จาก Suite / Clone ทั้งสองโหมด) และ Cycle ที่ปิดแล้วต้องไม่รับผลใหม่</summary>
public sealed class DeletedTestCaseCarryOverTests
{
    [Theory]
    [InlineData("populate")]
    [InlineData(TestCycleCloneModes.SuiteLatest)]
    [InlineData(TestCycleCloneModes.SourceSnapshot)]
    public async Task New_cycle_skips_deleted_test_cases(string mode)
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var b = await AutomationTestFixtures.SeedBaselineAsync(db);
        var deleted = new TestCase(b.Project.ProjectId, b.Module.ModuleId, "TC-DEL", "Deleted", null, null, "P1", null, false, null, [new TestStepInput(1, "Action", null, "Expected")], null);
        var suite = new TestSuite(b.Project.ProjectId, "TS-1", "Suite", null, null, null);
        var source = new TestCycle(b.Project.ProjectId, b.Release.ReleaseId, b.Build.BuildId, b.Environment.TestEnvironmentId, suite.TestSuiteId, "CYC-SRC", "Source", "Regression", null, null, null, null, null);
        source.Cases.Add(new TestCycleCase(source.TestCycleId, b.TestCase.TestCaseId, b.TestCase.RevisionNo, "P1", 1));
        source.Cases.Add(new TestCycleCase(source.TestCycleId, deleted.TestCaseId, 1, "P1", 2));
        db.AddRange(deleted, suite, new TestSuiteCase(suite.TestSuiteId, b.TestCase.TestCaseId, 1, true), new TestSuiteCase(suite.TestSuiteId, deleted.TestCaseId, 2, true), source);
        await db.SaveChangesAsync();
        deleted.SoftDelete(null);
        await db.SaveChangesAsync();
        var repository = new TestCycleRepository(db, new ProjectAccessContext { AllowedProjectIds = [b.Project.ProjectId] });

        Guid targetId;
        if (mode == "populate")
        {
            var target = new TestCycle(b.Project.ProjectId, b.Release.ReleaseId, b.Build.BuildId, b.Environment.TestEnvironmentId, suite.TestSuiteId, "CYC-NEW", "New", "Regression", null, null, null, null, null);
            db.Add(target);
            await repository.PopulateAsync(target.TestCycleId, suite.TestSuiteId, false, CancellationToken.None);
            await db.SaveChangesAsync();
            targetId = target.TestCycleId;
        }
        else
        {
            var clone = await repository.CloneAsync(source.TestCycleId, new CloneTestCycleRequest(b.Release.ReleaseId, b.Build.BuildId, b.Environment.TestEnvironmentId, "CYC-NEW", "New", "Regression", null, null, null, null, mode), null, CancellationToken.None);
            targetId = clone.TestCycleId;
        }

        Assert.Equal([b.TestCase.TestCaseId], await db.TestCycleCases.Where(x => x.TestCycleId == targetId).Select(x => x.TestCaseId).ToListAsync());
    }

    [Theory]
    [InlineData("Closed")]
    [InlineData("Cancelled")]
    public async Task Closed_cycle_rejects_new_results(string status)
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var b = await AutomationTestFixtures.SeedBaselineAsync(db);
        var cycle = new TestCycle(b.Project.ProjectId, b.Release.ReleaseId, b.Build.BuildId, b.Environment.TestEnvironmentId, null, "CYC-1", "Cycle", "Smoke", null, null, null, null, null);
        var cycleCase = new TestCycleCase(cycle.TestCycleId, b.TestCase.TestCaseId, b.TestCase.RevisionNo, "P1", 1);
        cycle.ChangeStatus("InProgress");
        cycle.ChangeStatus(status);
        db.AddRange(cycle, cycleCase);
        await db.SaveChangesAsync();
        var service = new ExecutionService(new ExecutionRepository(db, new ProjectAccessContext()));

        var request = new CreateExecutionRequest("Pass", null, null, [new SaveStepResultRequest(1, "Pass", null, null)]);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(cycleCase.TestCycleCaseId, request, Guid.NewGuid(), CancellationToken.None));
        Assert.False(await db.TestExecutions.AnyAsync());
    }
}
