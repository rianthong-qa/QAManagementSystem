using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-DATA-002 (Database Restore): request/claim/complete lifecycle for restoring an Environment's
/// DB from a previously completed AUT-DATA-001 snapshot. The actual restore (gbak -rep / RESTORE DATABASE) plus its
/// checksum/availability verification live entirely on the Windows Agent and are not exercised here — same split as
/// AutomationDataSnapshotTests.</summary>
public sealed class AutomationDataRestoreTests
{
    private static async Task<(AutomationTestFixtures.Baseline Baseline, AutomationDbSnapshotDto Snapshot)> SeedSucceededSnapshotAsync(ProMaxx2.QA.Infrastructure.Persistence.QaDbContext db)
    {
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var snapshotService = AutomationTestFixtures.SnapshotService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var requested = await snapshotService.RequestAsync(baseline.Project.ProjectId, new RequestSnapshotRequest(baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);
        await snapshotService.ClaimNextAsync("AGENT-A", CancellationToken.None);
        var completed = await snapshotService.CompleteAsync(requested.AutomationDbSnapshotId, new CompleteSnapshotRequest("Succeeded", "Firebird", @"C:\snapshots\qa.fbk", "deadbeef", 12345, null), CancellationToken.None);
        return (baseline, completed);
    }

    [Fact]
    public async Task Requesting_a_restore_from_a_succeeded_snapshot_creates_a_Requested_row()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, snapshot) = await SeedSucceededSnapshotAsync(db);
        var service = AutomationTestFixtures.RestoreService(db);

        var restore = await service.RequestAsync(baseline.Project.ProjectId, new RequestRestoreRequest(snapshot.AutomationDbSnapshotId), null, CancellationToken.None);

        Assert.Equal("Requested", restore.Status);
        Assert.Equal(baseline.Environment.EnvironmentName, restore.EnvironmentName);
        Assert.Equal(baseline.Build.BuildNumber, restore.BuildNumber);
        Assert.False(restore.ChecksumVerified);
        Assert.False(restore.AvailabilityVerified);
    }

    [Fact]
    public async Task Requesting_a_restore_from_a_snapshot_that_has_not_succeeded_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var snapshotService = AutomationTestFixtures.SnapshotService(db);
        var pending = await snapshotService.RequestAsync(baseline.Project.ProjectId, new RequestSnapshotRequest(baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);
        var service = AutomationTestFixtures.RestoreService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.RequestAsync(baseline.Project.ProjectId, new RequestRestoreRequest(pending.AutomationDbSnapshotId), null, CancellationToken.None));
    }

    [Fact]
    public async Task Requesting_a_restore_for_a_snapshot_that_does_not_exist_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.RestoreService(db);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.RequestAsync(baseline.Project.ProjectId, new RequestRestoreRequest(Guid.NewGuid()), null, CancellationToken.None));
    }

    [Fact]
    public async Task Claiming_a_restore_request_only_succeeds_for_the_agent_that_produced_the_snapshot()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, snapshot) = await SeedSucceededSnapshotAsync(db); // snapshot was produced by AGENT-A
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-B", "MACHINE-B", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var service = AutomationTestFixtures.RestoreService(db);
        var requested = await service.RequestAsync(baseline.Project.ProjectId, new RequestRestoreRequest(snapshot.AutomationDbSnapshotId), null, CancellationToken.None);

        var wrongAgentClaim = await service.ClaimNextAsync("AGENT-B", CancellationToken.None);
        var rightAgentClaim = await service.ClaimNextAsync("AGENT-A", CancellationToken.None);

        Assert.Null(wrongAgentClaim); // the backup file only exists on AGENT-A's disk — AGENT-B must never claim it
        Assert.NotNull(rightAgentClaim);
        Assert.Equal(requested.AutomationDbRestoreId, rightAgentClaim!.AutomationDbRestoreId);
        Assert.Equal(@"C:\snapshots\qa.fbk", rightAgentClaim.SnapshotPath);
        Assert.Equal("deadbeef", rightAgentClaim.ExpectedChecksum);
    }

    [Fact]
    public async Task Claiming_marks_the_restore_Running_and_assigns_the_claiming_agent()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, snapshot) = await SeedSucceededSnapshotAsync(db);
        var service = AutomationTestFixtures.RestoreService(db);
        var requested = await service.RequestAsync(baseline.Project.ProjectId, new RequestRestoreRequest(snapshot.AutomationDbSnapshotId), null, CancellationToken.None);

        await service.ClaimNextAsync("AGENT-A", CancellationToken.None);

        var refreshed = await service.GetAsync(requested.AutomationDbRestoreId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal("Running", refreshed.Status);
        Assert.NotNull(refreshed.AgentId);
        Assert.NotNull(refreshed.StartedAt);
    }

    [Fact]
    public async Task Completing_a_running_restore_as_Succeeded_records_both_verifications_true()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, snapshot) = await SeedSucceededSnapshotAsync(db);
        var service = AutomationTestFixtures.RestoreService(db);
        var requested = await service.RequestAsync(baseline.Project.ProjectId, new RequestRestoreRequest(snapshot.AutomationDbSnapshotId), null, CancellationToken.None);
        await service.ClaimNextAsync("AGENT-A", CancellationToken.None);

        var completed = await service.CompleteAsync(requested.AutomationDbRestoreId, new CompleteRestoreRequest("Succeeded", true, true, null), CancellationToken.None);

        Assert.Equal("Succeeded", completed.Status);
        Assert.True(completed.ChecksumVerified);
        Assert.True(completed.AvailabilityVerified);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task Completing_a_running_restore_as_Failed_after_the_availability_check_records_checksum_true_availability_false()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, snapshot) = await SeedSucceededSnapshotAsync(db);
        var service = AutomationTestFixtures.RestoreService(db);
        var requested = await service.RequestAsync(baseline.Project.ProjectId, new RequestRestoreRequest(snapshot.AutomationDbSnapshotId), null, CancellationToken.None);
        await service.ClaimNextAsync("AGENT-A", CancellationToken.None);

        var completed = await service.CompleteAsync(requested.AutomationDbRestoreId,
            new CompleteRestoreRequest("Failed", true, false, "Restore command completed but the database did not respond to a basic availability check afterward."), CancellationToken.None);

        Assert.Equal("Failed", completed.Status);
        Assert.True(completed.ChecksumVerified);
        Assert.False(completed.AvailabilityVerified);
        Assert.Contains("availability check", completed.ErrorMessage);
    }

    [Fact]
    public async Task Completing_a_running_restore_as_Failed_on_checksum_mismatch_records_both_verifications_false()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, snapshot) = await SeedSucceededSnapshotAsync(db);
        var service = AutomationTestFixtures.RestoreService(db);
        var requested = await service.RequestAsync(baseline.Project.ProjectId, new RequestRestoreRequest(snapshot.AutomationDbSnapshotId), null, CancellationToken.None);
        await service.ClaimNextAsync("AGENT-A", CancellationToken.None);

        var completed = await service.CompleteAsync(requested.AutomationDbRestoreId, new CompleteRestoreRequest("Failed", false, false, "Checksum mismatch — the backup file may be corrupted or has been replaced since it was taken."), CancellationToken.None);

        Assert.False(completed.ChecksumVerified);
        Assert.False(completed.AvailabilityVerified);
    }

    [Fact]
    public async Task A_late_duplicate_completion_report_is_ignored_idempotently()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, snapshot) = await SeedSucceededSnapshotAsync(db);
        var service = AutomationTestFixtures.RestoreService(db);
        var requested = await service.RequestAsync(baseline.Project.ProjectId, new RequestRestoreRequest(snapshot.AutomationDbSnapshotId), null, CancellationToken.None);
        await service.ClaimNextAsync("AGENT-A", CancellationToken.None);
        await service.CompleteAsync(requested.AutomationDbRestoreId, new CompleteRestoreRequest("Succeeded", true, true, null), CancellationToken.None);

        var second = await service.CompleteAsync(requested.AutomationDbRestoreId, new CompleteRestoreRequest("Failed", false, false, "late duplicate"), CancellationToken.None);

        Assert.Equal("Succeeded", second.Status); // the first report wins
        Assert.True(second.ChecksumVerified);
    }

    [Fact]
    public async Task Listing_restores_filters_by_snapshot()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, snapshot) = await SeedSucceededSnapshotAsync(db);
        var service = AutomationTestFixtures.RestoreService(db);
        await service.RequestAsync(baseline.Project.ProjectId, new RequestRestoreRequest(snapshot.AutomationDbSnapshotId), null, CancellationToken.None);
        await service.RequestAsync(baseline.Project.ProjectId, new RequestRestoreRequest(snapshot.AutomationDbSnapshotId), null, CancellationToken.None);

        var restores = await service.ListAsync(baseline.Project.ProjectId, snapshot.AutomationDbSnapshotId, CancellationToken.None);

        Assert.Equal(2, restores.Count);
    }

    [Fact]
    public async Task Getting_a_restore_that_does_not_exist_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.RestoreService(db);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.GetAsync(Guid.NewGuid(), baseline.Project.ProjectId, CancellationToken.None));
    }
}
