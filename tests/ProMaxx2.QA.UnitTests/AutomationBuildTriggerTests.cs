using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.Releases;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-P1-007 (Build Trigger): a configurable policy that automatically runs an Automation Suite
/// when a Build event happens — "Smoke" fires on every new Build, "Regression" fires only when a Build is marked a
/// Release Candidate. Exercises through the real <c>ReleaseService</c> (not by calling
/// AutomationBuildTriggerService.FireForBuildAsync directly) so the actual production wiring — the hook inside
/// CreateBuildAsync/MarkRcAsync — is what's under test, not just the trigger logic in isolation.</summary>
public sealed class AutomationBuildTriggerTests
{
    private static async Task<(AutomationTestFixtures.Baseline Baseline, Guid SuiteId)> SeedSuiteWithReadyCaseAsync(ProMaxx2.QA.Infrastructure.Persistence.QaDbContext db)
    {
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suiteService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Nightly", null), null, CancellationToken.None);
        await suiteService.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);
        return (baseline, suite.AutomationSuiteId);
    }

    [Fact]
    public async Task Creating_a_build_fires_active_Smoke_policies()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteWithReadyCaseAsync(db);
        var triggerService = AutomationTestFixtures.BuildTriggerService(db);
        await triggerService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationBuildTriggerPolicyRequest(suiteId, "Smoke", baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var releases = AutomationTestFixtures.ReleaseServiceWithBuildTrigger(db, baseline.Project.ProjectId);
        var agents = AutomationTestFixtures.AgentService(db);

        var build = await releases.CreateBuildAsync(baseline.Release.ReleaseId, new CreateBuildRequest("2", null, null, null, null, null, null), null, CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, build.BuildId, 50, CancellationToken.None);
        Assert.Single(executions);
    }

    [Fact]
    public async Task Creating_a_build_does_not_fire_Regression_policies()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteWithReadyCaseAsync(db);
        var triggerService = AutomationTestFixtures.BuildTriggerService(db);
        await triggerService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationBuildTriggerPolicyRequest(suiteId, "Regression", baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var releases = AutomationTestFixtures.ReleaseServiceWithBuildTrigger(db, baseline.Project.ProjectId);
        var agents = AutomationTestFixtures.AgentService(db);

        var build = await releases.CreateBuildAsync(baseline.Release.ReleaseId, new CreateBuildRequest("2", null, null, null, null, null, null), null, CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, build.BuildId, 50, CancellationToken.None);
        Assert.Empty(executions);
    }

    [Fact]
    public async Task Marking_a_build_as_release_candidate_fires_Regression_policies_but_not_Smoke()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteWithReadyCaseAsync(db);
        var triggerService = AutomationTestFixtures.BuildTriggerService(db);
        await triggerService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationBuildTriggerPolicyRequest(suiteId, "Regression", baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        await triggerService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationBuildTriggerPolicyRequest(suiteId, "Smoke", baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var releases = AutomationTestFixtures.ReleaseServiceWithBuildTrigger(db, baseline.Project.ProjectId);
        var agents = AutomationTestFixtures.AgentService(db);
        var build = await releases.CreateBuildAsync(baseline.Release.ReleaseId, new CreateBuildRequest("2", null, null, null, null, null, null), null, CancellationToken.None);
        var afterCreate = await agents.ListExecutionsAsync(baseline.Project.ProjectId, build.BuildId, 50, CancellationToken.None);
        Assert.Single(afterCreate); // just the Smoke policy from Create

        await releases.MarkRcAsync(build.BuildId, CancellationToken.None);

        var afterRc = await agents.ListExecutionsAsync(baseline.Project.ProjectId, build.BuildId, 50, CancellationToken.None);
        Assert.Equal(2, afterRc.Count); // Smoke (from create) + Regression (from RC) — not doubled, not skipped
    }

    [Fact]
    public async Task An_inactive_policy_never_fires()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteWithReadyCaseAsync(db);
        var triggerService = AutomationTestFixtures.BuildTriggerService(db);
        var policy = await triggerService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationBuildTriggerPolicyRequest(suiteId, "Smoke", baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        await triggerService.DeactivateAsync(policy.AutomationBuildTriggerPolicyId, baseline.Project.ProjectId, null, CancellationToken.None);
        var releases = AutomationTestFixtures.ReleaseServiceWithBuildTrigger(db, baseline.Project.ProjectId);
        var agents = AutomationTestFixtures.AgentService(db);

        var build = await releases.CreateBuildAsync(baseline.Release.ReleaseId, new CreateBuildRequest("2", null, null, null, null, null, null), null, CancellationToken.None);

        Assert.Empty(await agents.ListExecutionsAsync(baseline.Project.ProjectId, build.BuildId, 50, CancellationToken.None));
    }

    [Fact]
    public async Task A_closed_suite_records_a_failed_run_but_does_not_fail_build_creation()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteWithReadyCaseAsync(db);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        await suiteService.CloseAsync(suiteId, baseline.Project.ProjectId, new SuiteLifecycleRequest(), null, CancellationToken.None);
        var triggerService = AutomationTestFixtures.BuildTriggerService(db);
        var policy = await triggerService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationBuildTriggerPolicyRequest(suiteId, "Smoke", baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var releases = AutomationTestFixtures.ReleaseServiceWithBuildTrigger(db, baseline.Project.ProjectId);

        var build = await releases.CreateBuildAsync(baseline.Release.ReleaseId, new CreateBuildRequest("2", null, null, null, null, null, null), null, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, build.BuildId); // build creation itself succeeded despite the closed suite
        var runs = await triggerService.ListRunsAsync(policy.AutomationBuildTriggerPolicyId, baseline.Project.ProjectId, CancellationToken.None);
        var run = Assert.Single(runs);
        Assert.Equal("Failed", run.Status);
        Assert.NotNull(run.ErrorMessage);
    }

    [Fact]
    public async Task Creating_a_policy_for_a_suite_that_does_not_exist_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var triggerService = AutomationTestFixtures.BuildTriggerService(db);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => triggerService.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationBuildTriggerPolicyRequest(Guid.NewGuid(), "Smoke", baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None));
    }

    [Fact]
    public async Task Creating_a_policy_with_an_invalid_pack_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteWithReadyCaseAsync(db);
        var triggerService = AutomationTestFixtures.BuildTriggerService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => triggerService.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationBuildTriggerPolicyRequest(suiteId, "Nightly", baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None));
    }

    [Fact]
    public async Task List_returns_policies_for_the_project()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteWithReadyCaseAsync(db);
        var triggerService = AutomationTestFixtures.BuildTriggerService(db);
        await triggerService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationBuildTriggerPolicyRequest(suiteId, "Smoke", baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        await triggerService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationBuildTriggerPolicyRequest(suiteId, "Regression", baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        var policies = await triggerService.ListAsync(baseline.Project.ProjectId, CancellationToken.None);

        Assert.Equal(2, policies.Count);
    }

    [Fact]
    public async Task Activating_an_already_active_policy_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteWithReadyCaseAsync(db);
        var triggerService = AutomationTestFixtures.BuildTriggerService(db);
        var policy = await triggerService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationBuildTriggerPolicyRequest(suiteId, "Smoke", baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => triggerService.ActivateAsync(policy.AutomationBuildTriggerPolicyId, baseline.Project.ProjectId, null, CancellationToken.None));
    }
}
