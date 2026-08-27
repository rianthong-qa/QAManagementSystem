using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-DATA-006 (Environment Data Profile): a Hub-side, non-secret registry of "what kind of
/// database does this Environment run" (see class summary on <c>AutomationEnvironmentDataProfile</c> for why
/// secrets are never involved) plus the resulting DbKind cross-check that Seed (AUT-DATA-003/004/005) and Restore
/// (AUT-DATA-002) requests now perform against it before creating a request.</summary>
public sealed class AutomationEnvironmentDataProfileTests
{
    [Fact]
    public async Task Creating_a_profile_for_an_Environment_that_does_not_exist_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.EnvironmentDataProfileService(db);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            service.CreateAsync(baseline.Project.ProjectId, new CreateEnvironmentDataProfileRequest(Guid.NewGuid(), "Firebird", null), null, CancellationToken.None));
    }

    [Fact]
    public async Task Creating_a_second_profile_for_the_same_Environment_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.EnvironmentDataProfileService(db);
        await service.CreateAsync(baseline.Project.ProjectId, new CreateEnvironmentDataProfileRequest(baseline.Environment.TestEnvironmentId, "Firebird", null), null, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(baseline.Project.ProjectId, new CreateEnvironmentDataProfileRequest(baseline.Environment.TestEnvironmentId, "SqlServer", null), null, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Oracle")]
    public async Task Creating_a_profile_with_an_invalid_DbKind_throws(string dbKind)
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.EnvironmentDataProfileService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(baseline.Project.ProjectId, new CreateEnvironmentDataProfileRequest(baseline.Environment.TestEnvironmentId, dbKind, null), null, CancellationToken.None));
    }

    [Fact]
    public async Task Updating_a_profile_changes_DbKind_and_Notes()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.EnvironmentDataProfileService(db);
        var profile = await service.CreateAsync(baseline.Project.ProjectId, new CreateEnvironmentDataProfileRequest(baseline.Environment.TestEnvironmentId, "Firebird", "old notes"), null, CancellationToken.None);

        var updated = await service.UpdateAsync(profile.AutomationEnvironmentDataProfileId, baseline.Project.ProjectId, new UpdateEnvironmentDataProfileRequest("SqlServer", "new notes"), null, CancellationToken.None);

        Assert.Equal("SqlServer", updated.DbKind);
        Assert.Equal("new notes", updated.Notes);
    }

    [Fact]
    public async Task Requesting_a_Seed_run_against_an_unprofiled_Environment_skips_the_DbKind_check()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var seeds = AutomationTestFixtures.SeedService(db);
        var script = await seeds.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "Seed", "SqlServer", "SELECT 1;"), null, CancellationToken.None);

        // No AutomationEnvironmentDataProfile ever created for this Environment — the check must not block anything.
        var run = await seeds.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);

        Assert.Equal("Requested", run.Status);
    }

    [Fact]
    public async Task Requesting_a_Seed_run_whose_DbKind_mismatches_the_Environments_profile_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var profiles = AutomationTestFixtures.EnvironmentDataProfileService(db);
        await profiles.CreateAsync(baseline.Project.ProjectId, new CreateEnvironmentDataProfileRequest(baseline.Environment.TestEnvironmentId, "SqlServer", null), null, CancellationToken.None);
        var seeds = AutomationTestFixtures.SeedService(db);
        var script = await seeds.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "Seed", "Firebird", "SELECT 1 FROM RDB$DATABASE;"), null, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            seeds.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None));
    }

    [Fact]
    public async Task Requesting_a_Seed_run_whose_DbKind_matches_the_Environments_profile_succeeds()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var profiles = AutomationTestFixtures.EnvironmentDataProfileService(db);
        await profiles.CreateAsync(baseline.Project.ProjectId, new CreateEnvironmentDataProfileRequest(baseline.Environment.TestEnvironmentId, "Firebird", null), null, CancellationToken.None);
        var seeds = AutomationTestFixtures.SeedService(db);
        var script = await seeds.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "Seed", "Firebird", "SELECT 1 FROM RDB$DATABASE;"), null, CancellationToken.None);

        var run = await seeds.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);

        Assert.Equal("Requested", run.Status);
    }

    [Fact]
    public async Task Requesting_a_restore_whose_snapshot_DbKind_mismatches_the_Environments_profile_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var profiles = AutomationTestFixtures.EnvironmentDataProfileService(db);
        await profiles.CreateAsync(baseline.Project.ProjectId, new CreateEnvironmentDataProfileRequest(baseline.Environment.TestEnvironmentId, "SqlServer", null), null, CancellationToken.None);
        var snapshots = AutomationTestFixtures.SnapshotService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var snapshot = await snapshots.RequestAsync(baseline.Project.ProjectId, new RequestSnapshotRequest(baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);
        var claimed = await snapshots.ClaimNextAsync("AGENT-A", CancellationToken.None);
        await snapshots.CompleteAsync(claimed!.AutomationDbSnapshotId, new CompleteSnapshotRequest("Succeeded", "Firebird", @"C:\snap.fbk", "abc123", 1024, null), CancellationToken.None);
        var restores = AutomationTestFixtures.RestoreService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            restores.RequestAsync(baseline.Project.ProjectId, new RequestRestoreRequest(snapshot.AutomationDbSnapshotId), null, CancellationToken.None));
    }

    [Fact]
    public async Task Requesting_a_restore_whose_snapshot_DbKind_matches_the_Environments_profile_succeeds()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var profiles = AutomationTestFixtures.EnvironmentDataProfileService(db);
        await profiles.CreateAsync(baseline.Project.ProjectId, new CreateEnvironmentDataProfileRequest(baseline.Environment.TestEnvironmentId, "Firebird", null), null, CancellationToken.None);
        var snapshots = AutomationTestFixtures.SnapshotService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var snapshot = await snapshots.RequestAsync(baseline.Project.ProjectId, new RequestSnapshotRequest(baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);
        var claimed = await snapshots.ClaimNextAsync("AGENT-A", CancellationToken.None);
        await snapshots.CompleteAsync(claimed!.AutomationDbSnapshotId, new CompleteSnapshotRequest("Succeeded", "Firebird", @"C:\snap.fbk", "abc123", 1024, null), CancellationToken.None);
        var restores = AutomationTestFixtures.RestoreService(db);

        var restore = await restores.RequestAsync(baseline.Project.ProjectId, new RequestRestoreRequest(snapshot.AutomationDbSnapshotId), null, CancellationToken.None);

        Assert.Equal("Requested", restore.Status);
    }

    [Fact]
    public async Task Listing_profiles_is_scoped_to_the_project()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.EnvironmentDataProfileService(db);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateEnvironmentDataProfileRequest(baseline.Environment.TestEnvironmentId, "Firebird", "primary QA DB"), null, CancellationToken.None);

        var list = await service.ListAsync(baseline.Project.ProjectId, CancellationToken.None);

        var only = Assert.Single(list);
        Assert.Equal(created.AutomationEnvironmentDataProfileId, only.AutomationEnvironmentDataProfileId);
        Assert.Equal(baseline.Environment.EnvironmentName, only.EnvironmentName);
    }
}
