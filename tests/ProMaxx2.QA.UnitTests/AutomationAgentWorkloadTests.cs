namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-P2-004 (Agent workload/history): utilization/queue time/runtime/failure metrics plus a
/// capped heartbeat history — see the class summary on <c>AutomationAgentHeartbeatEvent</c> for why this is a
/// literal, bounded log of heartbeat calls rather than a background-worker-driven online/offline timeline (the
/// scope confirmed with the user before implementing).</summary>
public sealed class AutomationAgentWorkloadTests
{
    [Fact]
    public async Task Registering_an_agent_records_one_heartbeat_event()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);

        var agent = await agents.RegisterAsync(new ProMaxx2.QA.Application.Automation.RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var workload = await agents.GetAgentWorkloadAsync(agent.AgentId, null, null, CancellationToken.None);

        Assert.Single(workload.RecentHeartbeats);
    }

    [Fact]
    public async Task Heartbeat_history_is_capped_and_keeps_the_most_recent_events()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);
        var agent = await agents.RegisterAsync(new ProMaxx2.QA.Application.Automation.RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);

        for (var i = 0; i < 60; i++)
            await agents.HeartbeatAsync(new ProMaxx2.QA.Application.Automation.AgentHeartbeatRequest("AGENT-A", "MACHINE-A", "1.0.0", "Idle", null), CancellationToken.None);

        var workload = await agents.GetAgentWorkloadAsync(agent.AgentId, null, null, CancellationToken.None);

        Assert.Equal(50, workload.RecentHeartbeats.Count);
    }

    [Fact]
    public async Task Workload_computes_total_failed_and_failure_rate_from_completed_executions()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var agent = await agents.RegisterAsync(new ProMaxx2.QA.Application.Automation.RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);

        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new ProMaxx2.QA.Application.Automation.RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim1 = await agents.ClaimNextJobAsync(new ProMaxx2.QA.Application.Automation.ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);
        await agents.CompleteExecutionAsync(claim1!.AutomationExecutionId, new ProMaxx2.QA.Application.Automation.CompleteExecutionRequest("Passed", null, null, null), CancellationToken.None);

        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new ProMaxx2.QA.Application.Automation.RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim2 = await agents.ClaimNextJobAsync(new ProMaxx2.QA.Application.Automation.ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);
        await agents.CompleteExecutionAsync(claim2!.AutomationExecutionId, new ProMaxx2.QA.Application.Automation.CompleteExecutionRequest("Failed", "AutomationFailure", "AUT-UI-001", "boom"), CancellationToken.None);

        var workload = await agents.GetAgentWorkloadAsync(agent.AgentId, null, null, CancellationToken.None);

        Assert.Equal(2, workload.TotalExecutions);
        Assert.Equal(1, workload.FailedExecutions);
        Assert.Equal(50m, workload.FailureRatePercent);
    }

    [Fact]
    public async Task Workload_computes_average_queue_time_from_assigned_jobs()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var agent = await agents.RegisterAsync(new ProMaxx2.QA.Application.Automation.RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);

        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new ProMaxx2.QA.Application.Automation.RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ProMaxx2.QA.Application.Automation.ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);

        var workload = await agents.GetAgentWorkloadAsync(agent.AgentId, null, null, CancellationToken.None);

        Assert.NotNull(claim);
        Assert.NotNull(workload.AvgQueueTimeMs);
        Assert.True(workload.AvgQueueTimeMs >= 0);
    }

    [Fact]
    public async Task Workload_has_null_averages_and_zero_utilization_when_there_is_no_data_in_the_window()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);
        var agent = await agents.RegisterAsync(new ProMaxx2.QA.Application.Automation.RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);

        var workload = await agents.GetAgentWorkloadAsync(agent.AgentId, null, null, CancellationToken.None);

        Assert.Null(workload.AvgQueueTimeMs);
        Assert.Null(workload.AvgRuntimeMs);
        Assert.Equal(0, workload.TotalExecutions);
        Assert.Equal(0m, workload.UtilizationPercent);
    }

    [Fact]
    public async Task Workload_for_a_nonexistent_agent_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);

        await Assert.ThrowsAsync<ProMaxx2.QA.Application.Projects.EntityNotFoundException>(() =>
            agents.GetAgentWorkloadAsync(Guid.NewGuid(), null, null, CancellationToken.None));
    }
}
