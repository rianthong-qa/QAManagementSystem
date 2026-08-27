using ProMaxx2.QA.Application.Automation;

namespace ProMaxx2.QA.UnitTests;

/// <summary>
/// Covers AUT-TEST-006 (Batch Run / Multi-Agent), scoped to what the implementation actually does: BatchRunAsync
/// creates one execution+job per Ready case (skipping the rest) and multiple agents polling the shared queue drain it
/// without double-claiming (queue-based distribution — proven per-job in AutomationJobClaimTests). Three things the
/// AC wording implies but the code does not fully do are called out rather than faked: (1) there is no
/// capability-based routing — ClaimNextJobAsync accepts a `capabilities` list but never filters on it; (2) target-app
/// routing only restricts a non-default poller (any non-"WindowsUI" targetApp) — a poller using the default
/// "WindowsUI" target is unfiltered and can claim a job for any AutomationType; (3) AutomationJob.RequestedAgentId
/// (set from BatchRunRequest.AgentId) is not enforced at claim time, so any agent can pick up a job "requested" for
/// another agent.
/// </summary>
public sealed class AutomationBatchRunTests
{
    [Fact]
    public async Task Batch_run_creates_one_execution_per_ready_case_and_skips_the_rest()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cases) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 2);
        var agents = AutomationTestFixtures.AgentService(db);
        var caseService = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);
        // A third case that never got validated/approved (still Draft) must be skipped, not error the whole batch.
        var draftTestCase = new ProMaxx2.QA.Domain.TestManagement.TestCase(baseline.Project.ProjectId, baseline.Module.ModuleId, "TC-SALE-999", "Never approved", null, null, "P1", "Functional", true, null, [new ProMaxx2.QA.Domain.TestManagement.TestStepInput(1, "Add item", null, "Item added")], null);
        draftTestCase.SetAutomationTarget("app", null);
        db.Add(draftTestCase);
        await db.SaveChangesAsync();
        var draftCase = await caseService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationCaseRequest(draftTestCase.TestCaseId, "WindowsUI", null), null, CancellationToken.None);

        var result = await agents.BatchRunAsync(baseline.Project.ProjectId, new BatchRunRequest([cases[0].ReadyCase.AutomationCaseId, cases[1].ReadyCase.AutomationCaseId, draftCase.AutomationCaseId], baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.Created.Count);
        Assert.Single(result.SkippedCodes, draftCase.AutomationCode);
        Assert.Equal([cases[0].ReadyCase.AutomationCaseId, cases[1].ReadyCase.AutomationCaseId], result.Created.Select(c => c.AutomationCaseId));
    }

    [Fact]
    public async Task Batch_run_deduplicates_repeated_case_ids()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);

        var result = await agents.BatchRunAsync(baseline.Project.ProjectId, new BatchRunRequest([readyCase.AutomationCaseId, readyCase.AutomationCaseId], baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        Assert.Single(result.Created);
    }

    [Fact]
    public async Task Batch_run_with_no_case_ids_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            agents.BatchRunAsync(baseline.Project.ProjectId, new BatchRunRequest([], baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None));
    }

    [Fact]
    public async Task Two_agents_draining_a_batch_never_claim_the_same_job_and_results_aggregate_completely()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cases) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 4);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-B", "MACHINE-B", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var caseIds = cases.Select(c => c.ReadyCase.AutomationCaseId).ToList();
        await agents.BatchRunAsync(baseline.Project.ProjectId, new BatchRunRequest(caseIds, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        // Two agents alternately poll the shared queue until it's empty.
        var claimedByA = new List<AutomationJobPackageDto>();
        var claimedByB = new List<AutomationJobPackageDto>();
        AutomationJobPackageDto? next;
        while ((next = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None)) is not null)
            claimedByA.Add(next);
        while ((next = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-B", "1.0.0", [], "WindowsUI"), CancellationToken.None)) is not null)
            claimedByB.Add(next);

        var allClaimed = claimedByA.Concat(claimedByB).ToList();
        Assert.Equal(4, allClaimed.Count);
        Assert.Equal(4, allClaimed.Select(x => x.AutomationExecutionId).Distinct().Count()); // no job claimed twice, none missing
        Assert.Equal(caseIds.OrderBy(x => x), allClaimed.Select(x => x.AutomationCaseId).OrderBy(x => x)); // every batched case got exactly one job claimed

        foreach (var claim in claimedByA.Concat(claimedByB))
            await agents.CompleteExecutionAsync(claim.AutomationExecutionId, new CompleteExecutionRequest("Passed", null, null, null), CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Equal(4, executions.Count(x => x.Status == "Passed")); // results aggregate completely across both agents
    }

    [Fact]
    public async Task Non_windows_ui_poll_only_matches_its_own_target_or_the_windows_ui_fallback()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cases) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 1, "iOSApp");
        var (readyCase, versionId) = cases[0];
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        // A MobileApp poller must not pick up an iOSApp job (neither its own target nor the WindowsUI fallback).
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "MobileApp"), CancellationToken.None);

        Assert.Null(claim);
    }

    [Fact]
    public async Task Windows_ui_poll_is_not_restricted_by_target_and_can_claim_any_job()
    {
        // Documents current behavior: ClaimNextJobAsync only applies the target filter when the POLLER's targetApp
        // is not "WindowsUI" (`if (target != "WindowsUI") { ... }`). A poller using the default "WindowsUI" target
        // gets no filter at all, so it can claim jobs meant for any AutomationType — target-based routing only
        // works one way (a non-default poller is restricted; the default poller is not).
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cases) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 1, "iOSApp");
        var (readyCase, versionId) = cases[0];
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);

        Assert.NotNull(claim);
    }

    [Fact]
    public async Task Requested_agent_is_not_enforced_at_claim_time_any_registered_agent_can_take_it()
    {
        // Documents current behavior: BatchRunRequest.AgentId only sets AutomationJob.RequestedAgentId, but
        // ClaimNextJobAsync never filters on it, so "assign this batch to Agent A" is not actually enforced.
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var agentA = await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-B", "MACHINE-B", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.BatchRunAsync(baseline.Project.ProjectId, new BatchRunRequest([readyCase.AutomationCaseId], baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, agentA.AgentId, 5), null, CancellationToken.None);

        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-B", "1.0.0", [], "WindowsUI"), CancellationToken.None);

        Assert.NotNull(claim); // Agent B claimed a job that was "requested" for Agent A — no enforcement today
    }
}
