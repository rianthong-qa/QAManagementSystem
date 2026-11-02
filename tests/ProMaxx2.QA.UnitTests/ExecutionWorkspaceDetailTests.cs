using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;
namespace ProMaxx2.QA.UnitTests;

/// <summary>Execution Workspace: แท็บประวัติต้องได้ผลราย Step ของแต่ละ Run และคิวต้องรู้ว่าเคสถูก assign ให้ใคร</summary>
public sealed class ExecutionWorkspaceDetailTests
{
    [Fact]
    public async Task Case_detail_history_carries_step_results_of_each_run_and_queue_carries_assignee()
    {
        using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db, automationCandidate: false);
        var tester = new User("tester", "Tester", null, "hash");
        var cycle = new TestCycle(baseline.Project.ProjectId, baseline.Release.ReleaseId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, "CYC-001", "Cycle", "Smoke", null, null, null, null, tester.UserId);
        var cycleCase = new TestCycleCase(cycle.TestCycleId, baseline.TestCase.TestCaseId, baseline.TestCase.RevisionNo, "P0", 1);
        cycleCase.AssignTester(tester.UserId);
        var run1 = new TestExecution(cycleCase.TestCycleCaseId, 1, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, tester.UserId, "Fail", "พัง", null, [new StepResultInput(1, "Fail", "stock ติดลบ", null)]);
        var run2 = new TestExecution(cycleCase.TestCycleCaseId, 2, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, tester.UserId, "Pass", null, null, [new StepResultInput(1, "Pass", null, null)]);
        db.AddRange(tester, cycle, cycleCase, run1, run2);
        await db.SaveChangesAsync();
        var repository = new ExecutionRepository(db, new ProjectAccessContext());

        var detail = await repository.CaseDetailAsync(cycle.TestCycleId, cycleCase.TestCycleCaseId, CancellationToken.None);
        var workspace = await repository.WorkspaceAsync(cycle.TestCycleId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal([2, 1], detail.History.Select(x => x.ExecutionNo));
        Assert.Equal("Pass", detail.History[0].StepResults!.Single().Status);
        var failedRun = detail.History[1].StepResults!.Single();
        Assert.Equal(("Fail", "stock ติดลบ"), (failedRun.Status, failedRun.ActualResult));
        Assert.Equal("Pass", detail.Steps.Single().LastStatus);
        Assert.Equal(tester.UserId, workspace!.Cases.Single().AssignedTesterUserId);
        Assert.False(workspace.Cases.Single().TestCaseDeleted);
    }

    [Fact]
    public async Task Deleted_test_case_stays_visible_in_cycle_but_rejects_new_results()
    {
        using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db, automationCandidate: false);
        var tester = new User("tester", "Tester", null, "hash");
        var cycle = new TestCycle(baseline.Project.ProjectId, baseline.Release.ReleaseId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, "CYC-001", "Cycle", "Smoke", null, null, null, null, tester.UserId);
        var cycleCase = new TestCycleCase(cycle.TestCycleId, baseline.TestCase.TestCaseId, baseline.TestCase.RevisionNo, "P0", 1);
        var run = new TestExecution(cycleCase.TestCycleCaseId, 1, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, tester.UserId, "Fail", "พัง", null, [new StepResultInput(1, "Fail", "พัง", null)]);
        db.AddRange(tester, cycle, cycleCase, run);
        baseline.TestCase.SoftDelete(tester.UserId);
        await db.SaveChangesAsync();
        var repository = new ExecutionRepository(db, new ProjectAccessContext());

        var workspace = await repository.WorkspaceAsync(cycle.TestCycleId, CancellationToken.None);
        var detail = await repository.CaseDetailAsync(cycle.TestCycleId, cycleCase.TestCycleCaseId, CancellationToken.None);

        Assert.True(workspace!.Cases.Single().TestCaseDeleted);
        Assert.True(detail!.TestCaseDeleted);
        Assert.Single(detail.History);
        var request = new CreateExecutionRequest("Pass", null, null, [new SaveStepResultRequest(1, "Pass", null, null)]);
        await Assert.ThrowsAsync<TestCaseDeletedException>(() => new ExecutionService(repository).CreateAsync(cycleCase.TestCycleCaseId, request, tester.UserId, CancellationToken.None));
        Assert.Equal(1, db.TestExecutions.Count(x => x.TestCycleCaseId == cycleCase.TestCycleCaseId));
    }
}
