using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>
/// Regression coverage for bugs found by a code-review pass on PR #1 (2026-08-27), before merging
/// feat/automation-reliability-tests into main: quarantine bypass (Run/Batch Run/auto-retry ignored
/// IsQuarantined), the retry-classification whitelist drifting from AutomationFailureClassifier's own
/// MaintenanceRequired recommendation, and a race between two "complete execution" reports where the job's own
/// idempotency guard threw uncaught instead of being handled gracefully.
/// </summary>
public sealed class AutomationCodeReviewFixTests
{
    private static async Task<(AutomationTestFixtures.Baseline Baseline, AutomationCaseDto QuarantinedCase, Guid VersionId)> SeedQuarantinedReadyCaseAsync(QaDbContext db)
    {
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var caseService = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);
        var quarantined = await caseService.QuarantineCaseAsync(readyCase.AutomationCaseId, baseline.Project.ProjectId, new QuarantineCaseRequest("Flaky", null, null), CancellationToken.None);
        return (baseline, quarantined, versionId);
    }

    [Fact]
    public async Task Request_execution_rejects_a_quarantined_ready_case()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, quarantinedCase, versionId) = await SeedQuarantinedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(quarantinedCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None));
    }

    [Fact]
    public async Task Batch_run_skips_a_quarantined_ready_case_instead_of_running_it()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, quarantinedCase, _) = await SeedQuarantinedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);

        var result = await agents.BatchRunAsync(baseline.Project.ProjectId, new BatchRunRequest([quarantinedCase.AutomationCaseId], baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        Assert.Empty(result.Created);
        Assert.Contains(result.SkippedCodes, c => c == quarantinedCase.AutomationCode);
    }

    [Fact]
    public async Task Auto_retry_does_not_fire_for_a_quarantined_case_even_when_the_failure_is_otherwise_retryable()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException();
        // Quarantine the case while its execution is already in flight (the realistic race this fix guards against).
        await AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId).QuarantineCaseAsync(readyCase.AutomationCaseId, baseline.Project.ProjectId, new QuarantineCaseRequest("Flaky", null, null), CancellationToken.None);

        await agents.CompleteExecutionAsync(claim.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AgentFailure", "AUT-JOB-002", "drop"), CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Single(executions, x => x.AutomationCaseId == readyCase.AutomationCaseId); // no retry execution created
    }

    [Fact]
    public async Task Dsl_error_failure_is_flagged_for_maintenance_even_though_its_error_code_is_not_a_ui_code()
    {
        // AUT-DSL-001 is not in the old hardcoded "AUT-UI-00x" whitelist, but AutomationFailureClassifier has always
        // recommended MaintenanceRequired for it — the fix wires RequireMaintenance to the classifier's own
        // recommendation instead of a second, separately-maintained code list.
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException();

        await agents.CompleteExecutionAsync(claim.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AutomationFailure", "AUT-DSL-001", "DSL has no steps."), CancellationToken.None);

        var caseAfter = await AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId).GetCaseAsync(readyCase.AutomationCaseId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal("MaintenanceRequired", caseAfter.Status);
    }

    [Fact]
    public async Task Timeout_status_with_a_ui_error_code_is_also_flagged_for_maintenance_not_just_failed_status()
    {
        // The old check required Status == "Failed" specifically; the classifier itself has no such restriction
        // (only excludes non-terminal statuses), so a Timeout with a UI object-not-found code should also route
        // to maintenance now that RequireMaintenance follows the classifier's recommendation.
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException();

        await agents.CompleteExecutionAsync(claim.AutomationExecutionId, new CompleteExecutionRequest("Timeout", "AutomationFailure", "AUT-UI-001", "Object not found."), CancellationToken.None);

        var caseAfter = await AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId).GetCaseAsync(readyCase.AutomationCaseId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal("MaintenanceRequired", caseAfter.Status);
    }

    [Fact]
    public async Task Completing_an_execution_whose_job_was_already_completed_by_a_racing_request_is_handled_gracefully()
    {
        // Simulates the race: execution is still Running (so it passes the early idempotency check), but its job
        // was already completed by a concurrent duplicate report that landed between the two reads. Before the fix
        // this let AutomationJob's own idempotency guard throw uncaught out of CompleteExecutionAsync.
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException();
        var repo = new AutomationRepository(db);
        var job = await repo.FindJobByExecutionAsync(claim.AutomationExecutionId, CancellationToken.None) ?? throw new InvalidOperationException();
        job.Complete("Passed", null); // simulate the racing request's write already landing
        await repo.SaveChangesAsync(CancellationToken.None);

        var result = await agents.CompleteExecutionAsync(claim.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AgentFailure", "AUT-JOB-002", "drop"), CancellationToken.None);

        // No exception, and this request's own (conflicting) write was discarded rather than persisted —
        // the execution still shows Running (this request never got to save its "Failed" result).
        Assert.Equal("Running", result.Status);
    }
}
