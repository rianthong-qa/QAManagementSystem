using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-DATA-001 (Database Snapshot): request/claim/complete lifecycle for a real DB backup of an
/// Environment before a test run. The actual backup tool invocation (gbak/BACKUP DATABASE) lives entirely on the
/// Windows Agent (agent/ProMaxx2.Automation.Core/DatabaseSnapshotService.cs) and is not exercised here — this covers
/// the Hub-side orchestration only, same split as AUT-TEST-003 (job claim) vs the agent's own action execution.</summary>
public sealed class AutomationDataSnapshotTests
{
    [Fact]
    public async Task Requesting_a_snapshot_creates_a_Requested_row_with_environment_and_build_metadata()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SnapshotService(db);

        var snapshot = await service.RequestAsync(baseline.Project.ProjectId, new RequestSnapshotRequest(baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);

        Assert.Equal("Requested", snapshot.Status);
        Assert.Equal(baseline.Environment.EnvironmentName, snapshot.EnvironmentName);
        Assert.Equal(baseline.Build.BuildNumber, snapshot.BuildNumber);
        Assert.Null(snapshot.DbKind);
        Assert.Null(snapshot.AgentId);
    }

    [Fact]
    public async Task Requesting_a_snapshot_for_an_environment_that_does_not_belong_to_the_project_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SnapshotService(db);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            service.RequestAsync(Guid.NewGuid(), new RequestSnapshotRequest(baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None));
    }

    [Fact]
    public async Task Requesting_a_snapshot_for_a_build_that_does_not_exist_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SnapshotService(db);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            service.RequestAsync(baseline.Project.ProjectId, new RequestSnapshotRequest(baseline.Environment.TestEnvironmentId, Guid.NewGuid()), null, CancellationToken.None));
    }

    [Fact]
    public async Task Claiming_the_next_snapshot_request_marks_it_Running_and_assigns_the_claiming_agent()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SnapshotService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var requested = await service.RequestAsync(baseline.Project.ProjectId, new RequestSnapshotRequest(baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);

        var claimed = await service.ClaimNextAsync("AGENT-A", CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(requested.AutomationDbSnapshotId, claimed!.AutomationDbSnapshotId);
        var refreshed = await service.GetAsync(requested.AutomationDbSnapshotId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal("Running", refreshed.Status);
        Assert.NotNull(refreshed.AgentId);
        Assert.NotNull(refreshed.StartedAt);
    }

    [Fact]
    public async Task Claiming_with_no_pending_requests_returns_null()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SnapshotService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);

        Assert.Null(await service.ClaimNextAsync("AGENT-A", CancellationToken.None));
    }

    [Fact]
    public async Task A_second_claim_after_the_first_does_not_reclaim_the_same_request()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SnapshotService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-B", "MACHINE-B", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await service.RequestAsync(baseline.Project.ProjectId, new RequestSnapshotRequest(baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);

        var first = await service.ClaimNextAsync("AGENT-A", CancellationToken.None);
        var second = await service.ClaimNextAsync("AGENT-B", CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task Completing_a_running_snapshot_as_Succeeded_records_the_result_metadata()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SnapshotService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var requested = await service.RequestAsync(baseline.Project.ProjectId, new RequestSnapshotRequest(baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);
        await service.ClaimNextAsync("AGENT-A", CancellationToken.None);

        var completed = await service.CompleteAsync(requested.AutomationDbSnapshotId, new CompleteSnapshotRequest("Succeeded", "Firebird", @"C:\snapshots\qa.fbk", "deadbeef", 12345, null), CancellationToken.None);

        Assert.Equal("Succeeded", completed.Status);
        Assert.Equal("Firebird", completed.DbKind);
        Assert.Equal(@"C:\snapshots\qa.fbk", completed.SnapshotPath);
        Assert.Equal("deadbeef", completed.Checksum);
        Assert.Equal(12345, completed.SizeBytes);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task Completing_a_running_snapshot_as_Failed_records_the_error_message()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SnapshotService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var requested = await service.RequestAsync(baseline.Project.ProjectId, new RequestSnapshotRequest(baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);
        await service.ClaimNextAsync("AGENT-A", CancellationToken.None);

        var completed = await service.CompleteAsync(requested.AutomationDbSnapshotId, new CompleteSnapshotRequest("Failed", "SqlServer", null, null, null, "gbak exited with code 1: database not found"), CancellationToken.None);

        Assert.Equal("Failed", completed.Status);
        Assert.Equal("gbak exited with code 1: database not found", completed.ErrorMessage);
    }

    [Fact]
    public async Task A_late_duplicate_completion_report_is_ignored_idempotently_instead_of_overwriting_the_result()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SnapshotService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var requested = await service.RequestAsync(baseline.Project.ProjectId, new RequestSnapshotRequest(baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);
        await service.ClaimNextAsync("AGENT-A", CancellationToken.None);
        await service.CompleteAsync(requested.AutomationDbSnapshotId, new CompleteSnapshotRequest("Succeeded", "Firebird", @"C:\snapshots\qa.fbk", "deadbeef", 12345, null), CancellationToken.None);

        var second = await service.CompleteAsync(requested.AutomationDbSnapshotId, new CompleteSnapshotRequest("Failed", "Firebird", null, null, null, "late duplicate report"), CancellationToken.None);

        Assert.Equal("Succeeded", second.Status); // first report wins — the late duplicate did not overwrite it
        Assert.Equal("deadbeef", second.Checksum);
    }

    [Fact]
    public async Task Listing_snapshots_filters_by_environment_and_build()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var otherEnvironment = new ProMaxx2.QA.Domain.Execution.TestEnvironment(baseline.Project.ProjectId, "Staging", null);
        db.Add(otherEnvironment);
        await db.SaveChangesAsync();
        var service = AutomationTestFixtures.SnapshotService(db);
        await service.RequestAsync(baseline.Project.ProjectId, new RequestSnapshotRequest(baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);
        await service.RequestAsync(baseline.Project.ProjectId, new RequestSnapshotRequest(otherEnvironment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);

        var filtered = await service.ListAsync(baseline.Project.ProjectId, otherEnvironment.TestEnvironmentId, null, 50, CancellationToken.None);

        var only = Assert.Single(filtered);
        Assert.Equal(otherEnvironment.TestEnvironmentId, only.EnvironmentId);
    }

    [Fact]
    public async Task Getting_a_snapshot_that_does_not_exist_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SnapshotService(db);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.GetAsync(Guid.NewGuid(), baseline.Project.ProjectId, CancellationToken.None));
    }
}
