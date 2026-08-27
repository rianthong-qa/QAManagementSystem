using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>
/// Covers AUT-TEST-003 (Atomic Job Claim). Note: the current implementation has no separate "lease token" field —
/// atomicity comes from (a) the domain guard <see cref="AutomationJob.Assign"/> throwing once a job leaves "Queued",
/// and (b) a Serializable transaction wrapping select+assign in <see cref="AutomationRepository.ClaimNextJobAsync"/>
/// on relational providers. EF Core InMemory does not support transactions, so these tests verify the sequential
/// invariant (a claimed job is never handed out twice) and the domain guard directly; a genuine concurrent-race test
/// needs a relational provider and is out of scope here.
/// </summary>
public sealed class AutomationJobClaimTests
{
    private static async Task<Guid> RegisterAgentAsync(AutomationAgentService agents, string code)
    {
        var dto = await agents.RegisterAsync(new RegisterAgentRequest(code, "MACHINE-" + code, "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        return dto.AgentId;
    }

    [Fact]
    public async Task Second_agent_gets_nothing_once_the_only_job_is_claimed()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await RegisterAgentAsync(agents, "AGENT-A");
        await RegisterAgentAsync(agents, "AGENT-B");
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        var firstClaim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);
        var secondClaim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-B", "1.0.0", [], "WindowsUI"), CancellationToken.None);

        Assert.NotNull(firstClaim);
        Assert.Null(secondClaim);
    }

    [Fact]
    public async Task Claiming_assigns_the_job_to_the_claiming_agent_and_starts_the_execution()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var agentId = await RegisterAgentAsync(agents, "AGENT-A");
        var execution = await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);

        Assert.NotNull(claim);
        Assert.Equal(execution.AutomationExecutionId, claim!.AutomationExecutionId);
        var job = await new AutomationRepository(db).FindJobAsync(claim.JobId, CancellationToken.None);
        Assert.Equal("Assigned", job!.Status);
        Assert.Equal(agentId, job.AssignedAgentId);
    }

    [Fact]
    public async Task Disabled_agent_cannot_claim_a_job()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var agentId = await RegisterAgentAsync(agents, "AGENT-A");
        await agents.SetAgentEnabledAsync(agentId, false, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);

        Assert.Null(claim);
    }

    [Fact]
    public async Task No_queued_job_returns_null()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);
        await RegisterAgentAsync(agents, "AGENT-A");

        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);

        Assert.Null(claim);
    }

    [Fact]
    public async Task Job_scheduled_in_the_future_is_not_claimable_yet()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await RegisterAgentAsync(agents, "AGENT-A");
        var execution = await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        // Simulate a backoff-delayed retry job (QueuedAt in the future), same shape as CompleteExecutionAsync's auto-retry.
        var job = await db.AutomationJobs.SingleAsync(j => j.AutomationExecutionId == execution.AutomationExecutionId);
        db.AutomationJobs.Remove(job);
        var future = new AutomationJob(execution.AutomationExecutionId, null, 5, DateTime.UtcNow.AddMinutes(5));
        db.AutomationJobs.Add(future);
        await db.SaveChangesAsync();

        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);

        Assert.Null(claim);
    }

    [Fact]
    public async Task Higher_priority_job_is_claimed_before_lower_priority_job()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await RegisterAgentAsync(agents, "AGENT-A");
        var low = await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 8), null, CancellationToken.None);
        var high = await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 1), null, CancellationToken.None);

        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);

        Assert.NotNull(claim);
        Assert.Equal(high.AutomationExecutionId, claim!.AutomationExecutionId);
        Assert.NotEqual(low.AutomationExecutionId, claim.AutomationExecutionId);
    }

    [Fact]
    public void Assign_throws_once_job_already_left_queued()
    {
        var job = new AutomationJob(Guid.NewGuid(), null, 5, DateTime.UtcNow);
        job.Assign(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => job.Assign(Guid.NewGuid()));
    }
}
