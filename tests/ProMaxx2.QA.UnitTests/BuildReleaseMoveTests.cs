using ProMaxx2.QA.Application.Releases;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Releases;

namespace ProMaxx2.QA.UnitTests;

public sealed class BuildReleaseMoveTests
{
    [Fact]
    public async Task Unused_build_can_move_to_another_release_in_the_same_project()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var target = new Release(baseline.Project.ProjectId, "REL-002", "2.0", null, null, null, null, null);
        db.Releases.Add(target);
        await db.SaveChangesAsync();
        var service = AutomationTestFixtures.ReleaseServiceWithBuildTrigger(db, baseline.Project.ProjectId);

        var result = await service.UpdateBuildAsync(baseline.Build.BuildId,
            new UpdateBuildRequest("2.0", null, null, null, null, null, target.ReleaseId), CancellationToken.None);

        Assert.Equal(target.ReleaseId, result.ReleaseId);
        Assert.Equal("2.0", result.ApplicationVersion);
    }

    [Fact]
    public async Task Build_with_test_cycle_cannot_move()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var target = new Release(baseline.Project.ProjectId, "REL-002", "2.0", null, null, null, null, null);
        var cycle = new TestCycle(baseline.Project.ProjectId, baseline.Release.ReleaseId, baseline.Build.BuildId,
            baseline.Environment.TestEnvironmentId, null, "CYC-001", "Existing run", null, null, null, null, null, null);
        db.AddRange(target, cycle);
        await db.SaveChangesAsync();
        var service = AutomationTestFixtures.ReleaseServiceWithBuildTrigger(db, baseline.Project.ProjectId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateBuildAsync(baseline.Build.BuildId,
            new UpdateBuildRequest(null, null, null, null, null, null, target.ReleaseId), CancellationToken.None));
        Assert.Equal(baseline.Release.ReleaseId, baseline.Build.ReleaseId);
    }

    [Fact]
    public async Task Duplicate_build_number_in_target_release_is_rejected()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var target = new Release(baseline.Project.ProjectId, "REL-002", "2.0", null, null, null, null, null);
        var duplicate = new Build(target.ReleaseId, baseline.Build.BuildNumber, null, null, null, null, null, null, null);
        db.AddRange(target, duplicate);
        await db.SaveChangesAsync();
        var service = AutomationTestFixtures.ReleaseServiceWithBuildTrigger(db, baseline.Project.ProjectId);

        await Assert.ThrowsAsync<DuplicateCodeException>(() => service.UpdateBuildAsync(baseline.Build.BuildId,
            new UpdateBuildRequest(null, null, null, null, null, null, target.ReleaseId), CancellationToken.None));
    }

    [Fact]
    public async Task Build_cannot_move_to_another_project()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var otherProject = new Project("OTHER", "Other", null, null, null);
        var target = new Release(otherProject.ProjectId, "OTHER-REL-001", "1.0", null, null, null, null, null);
        db.AddRange(otherProject, target);
        await db.SaveChangesAsync();
        var service = AutomationTestFixtures.ReleaseServiceWithBuildTrigger(db, baseline.Project.ProjectId);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateBuildAsync(baseline.Build.BuildId,
            new UpdateBuildRequest(null, null, null, null, null, null, target.ReleaseId), CancellationToken.None));
        Assert.Equal(baseline.Release.ReleaseId, baseline.Build.ReleaseId);
    }
}
