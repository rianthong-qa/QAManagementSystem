using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-TEST-005 (Retry). Exercises the auto-retry branch of <see cref="AutomationAgentService.CompleteExecutionAsync"/>.</summary>
public sealed class AutomationRetryTests
{
    private static async Task<(AutomationTestFixtures.Baseline Baseline, AutomationCaseDto ReadyCase, AutomationExecutionDto Execution)> SeedRunningExecutionAsync(QaDbContext db, AutomationAgentService agents, string agentCode = "AGENT-A")
    {
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        await agents.RegisterAsync(new RegisterAgentRequest(agentCode, "MACHINE-" + agentCode, "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest(agentCode, "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException("Expected a job to be claimable.");
        var execution = await agents.GetExecutionAsync(claim.AutomationExecutionId, baseline.Project.ProjectId, CancellationToken.None);
        return (baseline, readyCase, execution);
    }

    [Fact]
    public async Task Retryable_failure_creates_one_backed_off_retry_linked_to_the_original()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);
        var (baseline, readyCase, execution) = await SeedRunningExecutionAsync(db, agents);

        await agents.CompleteExecutionAsync(execution.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AgentFailure", "AUT-JOB-002", "Agent session dropped."), CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        var caseExecutions = executions.Where(x => x.AutomationCaseId == readyCase.AutomationCaseId).ToList();
        Assert.Equal(2, caseExecutions.Count); // original + exactly one retry, no duplicates
        var retry = caseExecutions.Single(x => x.AutomationExecutionId != execution.AutomationExecutionId);
        Assert.Equal(execution.AutomationExecutionId, retry.RetryOfExecutionId);
        Assert.Equal(1, retry.RetryCount);
        Assert.Equal("Queued", retry.Status);

        // Backoff: the retry job is scheduled policy.BackoffSeconds into the future, so it must not be claimable yet.
        var claimTooSoon = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);
        Assert.Null(claimTooSoon);
    }

    [Fact]
    public async Task Retry_stops_once_retry_count_reaches_max_attempts()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.UpdateRetryPolicyAsync(new UpdateRetryPolicyRequest(2, 0, true), null, CancellationToken.None); // no backoff, easy to chain-claim in a test
        var (baseline, readyCase, execution) = await SeedRunningExecutionAsync(db, agents);

        // Round 1: original fails -> retry #1 (RetryCount=1)
        await agents.CompleteExecutionAsync(execution.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AgentFailure", "AUT-JOB-002", "drop"), CancellationToken.None);
        var claim1 = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);
        Assert.NotNull(claim1);
        // Round 2: retry #1 fails -> retry #2 (RetryCount=2, == MaxAttempts)
        await agents.CompleteExecutionAsync(claim1!.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AgentFailure", "AUT-JOB-002", "drop"), CancellationToken.None);
        var claim2 = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);
        Assert.NotNull(claim2);
        // Round 3: retry #2 (RetryCount=2) fails -> 2 < MaxAttempts(2) is false, no further retry
        var finalResult = await agents.CompleteExecutionAsync(claim2!.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AgentFailure", "AUT-JOB-002", "drop"), CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        var caseExecutions = executions.Where(x => x.AutomationCaseId == readyCase.AutomationCaseId).ToList();
        Assert.Equal(3, caseExecutions.Count); // original + retry#1 + retry#2, never a 4th
        Assert.Equal("Failed", finalResult.Status);
        var caseAfter = await AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId).GetCaseAsync(readyCase.AutomationCaseId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal("Ready", caseAfter.Status); // AUT-JOB-002 is not a UI error code, so it falls through to Ready, not MaintenanceRequired
    }

    [Fact]
    public async Task Non_retryable_ui_failure_does_not_retry_and_requires_maintenance()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);
        var (baseline, readyCase, execution) = await SeedRunningExecutionAsync(db, agents);

        await agents.CompleteExecutionAsync(execution.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AutomationFailure", "AUT-UI-001", "Object not found: Sales.Save"), CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Single(executions, x => x.AutomationCaseId == readyCase.AutomationCaseId); // no retry created
        var caseAfter = await AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId).GetCaseAsync(readyCase.AutomationCaseId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal("MaintenanceRequired", caseAfter.Status);
    }

    [Fact]
    public async Task Unclassifiable_failure_does_not_retry_and_case_returns_to_ready()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);
        var (baseline, readyCase, execution) = await SeedRunningExecutionAsync(db, agents);

        await agents.CompleteExecutionAsync(execution.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AutomationFailure", "AUT-CUSTOM-999", "Unrecognized error code."), CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Single(executions, x => x.AutomationCaseId == readyCase.AutomationCaseId);
        var caseAfter = await AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId).GetCaseAsync(readyCase.AutomationCaseId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal("Ready", caseAfter.Status);
    }

    [Fact]
    public async Task Disabled_retry_policy_never_creates_a_retry_execution()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.UpdateRetryPolicyAsync(new UpdateRetryPolicyRequest(2, 30, false), null, CancellationToken.None);
        var (baseline, readyCase, execution) = await SeedRunningExecutionAsync(db, agents);

        await agents.CompleteExecutionAsync(execution.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AgentFailure", "AUT-JOB-002", "drop"), CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Single(executions, x => x.AutomationCaseId == readyCase.AutomationCaseId);
    }

    [Fact]
    public async Task Retry_is_blocked_once_an_unsafe_action_step_already_passed()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);
        var (baseline, readyCase, execution) = await SeedRunningExecutionAsync(db, agents);
        var repository = new AutomationRepository(db);
        await repository.AddActionAsync(new AutomationAction("SAVE_DOCUMENT", "Save Document", "Sales", null, "{}", "SAVE_DOCUMENT", null), CancellationToken.None); // RetrySafety defaults to "Unsafe"
        await db.SaveChangesAsync();
        await agents.ReportStepResultAsync(execution.AutomationExecutionId, new ReportStepResultRequest(2, "SAVE_DOCUMENT", "Pass", "Saved", null, null, null, DateTime.UtcNow.AddSeconds(-2), DateTime.UtcNow), CancellationToken.None);

        await agents.CompleteExecutionAsync(execution.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AgentFailure", "AUT-JOB-002", "drop after unsafe step ran"), CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Single(executions, x => x.AutomationCaseId == readyCase.AutomationCaseId); // classification was retryable, but the already-executed unsafe step blocks it
    }

    [Fact]
    public async Task Duplicate_completion_after_a_retry_was_already_created_does_not_create_a_second_retry()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);
        var (baseline, readyCase, execution) = await SeedRunningExecutionAsync(db, agents);

        await agents.CompleteExecutionAsync(execution.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AgentFailure", "AUT-JOB-002", "drop"), CancellationToken.None);
        // Simulates a retried/duplicated report for the same (now-terminal) execution.
        await agents.CompleteExecutionAsync(execution.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AgentFailure", "AUT-JOB-002", "drop"), CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Equal(2, executions.Where(x => x.AutomationCaseId == readyCase.AutomationCaseId).Count()); // original + exactly one retry
    }
}
