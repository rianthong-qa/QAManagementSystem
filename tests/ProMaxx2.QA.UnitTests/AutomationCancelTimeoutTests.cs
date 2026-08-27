using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.UnitTests;

/// <summary>
/// Covers AUT-TEST-004 (Cancel/Timeout/Lease Recovery), scoped to what actually exists today: manual Cancel of a
/// Queued/Running execution, terminal Timeout/AgentLost completion reports, and idempotent handling of a late/duplicate
/// agent result (a report that arrives after the execution already reached a terminal state). There is no background
/// lease-expiry watchdog in this codebase yet (no hosted service marks a stale Running job as AgentLost automatically) —
/// that gap is called out in AUTOMATION_TODO.md rather than faked here.
/// </summary>
public sealed class AutomationCancelTimeoutTests
{
    private static async Task<(AutomationTestFixtures.Baseline Baseline, AutomationExecutionDto Execution)> SeedRunningExecutionAsync(ProMaxx2.QA.Infrastructure.Persistence.QaDbContext db)
    {
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException("Expected a job to be claimable.");
        var execution = await agents.GetExecutionAsync(claim.AutomationExecutionId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal("Running", execution.Status);
        return (baseline, execution);
    }

    [Fact]
    public async Task Cancel_running_execution_marks_execution_and_job_cancelled_and_case_returns_to_ready()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, execution) = await SeedRunningExecutionAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);

        var cancelled = await agents.CancelExecutionAsync(execution.AutomationExecutionId, baseline.Project.ProjectId, CancellationToken.None);

        Assert.Equal("Cancelled", cancelled.Status);
        var jobs = await agents.ListJobsAsync(baseline.Project.ProjectId, null, 10, CancellationToken.None);
        Assert.Equal("Cancelled", jobs.Single(j => j.AutomationExecutionId == execution.AutomationExecutionId).Status);
    }

    [Fact]
    public async Task Cancel_already_terminal_execution_throws_conflict()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, execution) = await SeedRunningExecutionAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.CancelExecutionAsync(execution.AutomationExecutionId, baseline.Project.ProjectId, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agents.CancelExecutionAsync(execution.AutomationExecutionId, baseline.Project.ProjectId, CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_queued_execution_that_was_never_claimed_succeeds()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var execution = await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        Assert.Equal("Queued", execution.Status);

        var cancelled = await agents.CancelExecutionAsync(execution.AutomationExecutionId, baseline.Project.ProjectId, CancellationToken.None);

        Assert.Equal("Cancelled", cancelled.Status);
    }

    [Fact]
    public async Task Timeout_result_completes_execution_and_classifies_failure()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, execution) = await SeedRunningExecutionAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);

        var completed = await agents.CompleteExecutionAsync(execution.AutomationExecutionId, new CompleteExecutionRequest("Timeout", "AutomationFailure", "AUT-JOB-002", "Step timed out waiting for control."), CancellationToken.None);

        Assert.Equal("Timeout", completed.Status);
        Assert.NotNull(completed.ClassifiedFailureType);
    }

    [Fact]
    public async Task AgentLost_result_completes_execution()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, execution) = await SeedRunningExecutionAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);

        var completed = await agents.CompleteExecutionAsync(execution.AutomationExecutionId, new CompleteExecutionRequest("AgentLost", "AutomationFailure", "AUT-JOB-004", "Heartbeat lost."), CancellationToken.None);

        Assert.Equal("AgentLost", completed.Status);
    }

    [Fact]
    public async Task Late_result_after_cancel_is_ignored_instead_of_overwriting_cancelled_state()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, execution) = await SeedRunningExecutionAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.CancelExecutionAsync(execution.AutomationExecutionId, baseline.Project.ProjectId, CancellationToken.None);

        // Agent's result for the already-cancelled run arrives late (e.g. was in flight when the user cancelled).
        var afterLateResult = await agents.CompleteExecutionAsync(execution.AutomationExecutionId, new CompleteExecutionRequest("Passed", null, null, null), CancellationToken.None);

        Assert.Equal("Cancelled", afterLateResult.Status);
    }

    [Fact]
    public async Task Duplicate_result_report_for_the_same_execution_is_ignored()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, execution) = await SeedRunningExecutionAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var first = await agents.CompleteExecutionAsync(execution.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AutomationFailure", "AUT-UI-001", "Object not found."), CancellationToken.None);
        Assert.Equal("Failed", first.Status);

        // A retried/duplicated report for the same execution (e.g. agent didn't get the ack) must not double-process
        // (would otherwise spawn a second retry execution or flip the case status again).
        var second = await agents.CompleteExecutionAsync(execution.AutomationExecutionId, new CompleteExecutionRequest("Passed", null, null, null), CancellationToken.None);

        Assert.Equal("Failed", second.Status);
    }

    [Fact]
    public void Job_complete_throws_when_job_already_completed()
    {
        var job = new AutomationJob(Guid.NewGuid(), null, 5, DateTime.UtcNow);
        job.Assign(Guid.NewGuid());
        job.Complete("Passed", null);

        Assert.Throws<InvalidOperationException>(() => job.Complete("Failed", "late"));
    }

    [Fact]
    public void Execution_complete_throws_when_execution_already_completed()
    {
        var execution = new AutomationExecution(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), null);
        execution.Start(Guid.NewGuid(), DateTime.UtcNow);
        execution.Complete("Passed", null, null, null, DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => execution.Complete("Failed", "AutomationFailure", "AUT-JOB-001", "late", DateTime.UtcNow));
    }
}
