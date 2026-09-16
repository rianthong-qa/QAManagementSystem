using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

public sealed class TestCycleCloneTests
{
    [Fact]
    public async Task Clone_source_snapshot_creates_fresh_draft_cases_without_execution_or_assignment()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var source = new TestCycle(baseline.Project.ProjectId, baseline.Release.ReleaseId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, "AUT-CYC-001", "Source cycle", "Regression", DateTime.UtcNow.Date, null, null, "Source notes", null);
        var sourceCase = new TestCycleCase(source.TestCycleId, baseline.TestCase.TestCaseId, 7, "P0", 1);
        source.Cases.Add(sourceCase);
        sourceCase.AssignTester(Guid.NewGuid());
        sourceCase.SetStatus("Completed");
        db.Add(source);
        db.Add(new TestExecution(sourceCase.TestCycleCaseId, 1, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, Guid.NewGuid(), "Pass", "done", null, [new StepResultInput(1, "Pass", "done", null)]));
        await db.SaveChangesAsync();

        var repository = new TestCycleRepository(db, new ProjectAccessContext { AllowedProjectIds = [baseline.Project.ProjectId] });
        var clone = await repository.CloneAsync(source.TestCycleId, new CloneTestCycleRequest(baseline.Release.ReleaseId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, "AUT-CYC-002", "Target cycle", "Regression", null, null, null, null), null, CancellationToken.None);

        Assert.NotEqual(source.TestCycleId, clone.TestCycleId);
        Assert.Equal(source.TestCycleId, clone.CopiedFromTestCycleId);
        Assert.Equal("Draft", clone.Status);
        var targetCase = Assert.Single(clone.Cases);
        Assert.NotEqual(sourceCase.TestCycleCaseId, targetCase.TestCycleCaseId);
        Assert.Equal(sourceCase.TestCaseRevisionNo, targetCase.TestCaseRevisionNo);
        Assert.Equal("NotRun", targetCase.CurrentStatus);
        Assert.Null(targetCase.AssignedTesterUserId);
        Assert.Empty(await db.TestExecutions.Where(x => x.CycleCase.TestCycleId == clone.TestCycleId).ToListAsync());
        Assert.Single(await db.AuditLogs.Where(x => x.EntityId == clone.TestCycleId.ToString() && x.Action == "Clone").ToListAsync());
    }

    [Fact]
    public async Task Clone_rejects_target_build_from_another_release()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var otherRelease = new Release(baseline.Project.ProjectId, "REL-002", "2.0", "Major", null, null, null, null);
        var otherBuild = new Build(otherRelease.ReleaseId, "2", "2.0", null, null, DateTime.UtcNow, null, null, null);
        var source = new TestCycle(baseline.Project.ProjectId, baseline.Release.ReleaseId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, "AUT-CYC-001", "Source cycle", null, null, null, null, null, null);
        db.AddRange(otherRelease, otherBuild, source);
        await db.SaveChangesAsync();
        var repository = new TestCycleRepository(db, new ProjectAccessContext { AllowedProjectIds = [baseline.Project.ProjectId] });

        await Assert.ThrowsAsync<ArgumentException>(() => repository.CloneAsync(source.TestCycleId, new CloneTestCycleRequest(baseline.Release.ReleaseId, otherBuild.BuildId, baseline.Environment.TestEnvironmentId, "AUT-CYC-002", "Target cycle", null, null, null, null, null), null, CancellationToken.None));
    }
}
