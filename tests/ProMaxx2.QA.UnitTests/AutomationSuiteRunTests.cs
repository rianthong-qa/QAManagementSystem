using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-P1-004 (Run Suite ซ้ำ): pick an existing Suite and run it against a Build/Environment
/// without re-selecting cases — reuses BatchRunAsync with the suite's current case membership.</summary>
public sealed class AutomationSuiteRunTests
{
    [Fact]
    public async Task Running_a_suite_creates_one_execution_per_ready_case_without_reselecting_them()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cases) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 3);
        var agents = AutomationTestFixtures.AgentService(db);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suiteService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Smoke", null), null, CancellationToken.None);
        await suiteService.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest(cases.Select(c => c.ReadyCase.AutomationCaseId).ToList(), true), CancellationToken.None);

        var result = await agents.RunSuiteAsync(baseline.Project.ProjectId, new RunSuiteRequest(suite.AutomationSuiteId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Created.Count);
        Assert.Empty(result.SkippedCodes);
    }

    [Fact]
    public async Task Running_a_suite_twice_against_different_builds_does_not_require_reselecting_cases()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suiteService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Regression", null), null, CancellationToken.None);
        await suiteService.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);

        var firstRun = await agents.RunSuiteAsync(baseline.Project.ProjectId, new RunSuiteRequest(suite.AutomationSuiteId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var secondRun = await agents.RunSuiteAsync(baseline.Project.ProjectId, new RunSuiteRequest(suite.AutomationSuiteId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        Assert.Single(firstRun.Created);
        Assert.Single(secondRun.Created);
        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Equal(2, executions.Count(x => x.AutomationCaseId == readyCase.AutomationCaseId)); // two separate runs, same case, no reselection needed
    }

    [Fact]
    public async Task Running_a_suite_skips_non_ready_cases_and_reports_them_as_skipped()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var caseService = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);
        var draftTestCase = new ProMaxx2.QA.Domain.TestManagement.TestCase(baseline.Project.ProjectId, baseline.Module.ModuleId, "TC-SALE-888", "Not ready", null, null, "P1", "Functional", true, null, [new ProMaxx2.QA.Domain.TestManagement.TestStepInput(1, "Add item", null, "Item added")], null);
        draftTestCase.SetAutomationTarget("app", null);
        db.Add(draftTestCase);
        await db.SaveChangesAsync();
        var draftCase = await caseService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationCaseRequest(draftTestCase.TestCaseId, "WindowsUI", null), null, CancellationToken.None);
        var agents = AutomationTestFixtures.AgentService(db);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suiteService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Mixed", null), null, CancellationToken.None);
        await suiteService.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId, draftCase.AutomationCaseId], true), CancellationToken.None);

        var result = await agents.RunSuiteAsync(baseline.Project.ProjectId, new RunSuiteRequest(suite.AutomationSuiteId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Single(result.Created);
        Assert.Contains(draftCase.AutomationCode, result.SkippedCodes);
    }

    [Fact]
    public async Task Running_a_closed_suite_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suiteService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);
        await suiteService.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);
        await suiteService.CloseAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new SuiteLifecycleRequest(), null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agents.RunSuiteAsync(baseline.Project.ProjectId, new RunSuiteRequest(suite.AutomationSuiteId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None));
    }

    [Fact]
    public async Task Running_a_suite_with_no_cases_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suiteService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Empty", null), null, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            agents.RunSuiteAsync(baseline.Project.ProjectId, new RunSuiteRequest(suite.AutomationSuiteId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None));
    }

    [Fact]
    public async Task Running_a_suite_from_a_different_project_throws_not_found()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suiteService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            agents.RunSuiteAsync(Guid.NewGuid(), new RunSuiteRequest(suite.AutomationSuiteId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None));
    }
}
