namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-P2-001 (Server-side pagination) for the three list endpoints named in the AC — Automation
/// Cases, Jobs, Executions. Scoped per the confirmed design: the new <c>*PagedAsync</c> methods are siblings of the
/// original <c>List*Async</c> ones (kept unchanged for the shared "flat, up to N rows" cross-cutting consumers —
/// dashboard KPIs, batch-run/suite case pickers — and the ~25 existing test call sites that use them as a plain
/// unpaginated helper), not replacements — see the class summary on <c>ListCasesPagedAsync</c> in
/// AutomationContracts.cs. Each test seeds enough rows to actually exercise Skip/Take rather than asserting against
/// a single-page result.</summary>
public sealed class AutomationListPaginationTests
{
    [Fact]
    public async Task Cases_paging_returns_the_correct_total_and_slices_rows_by_page_and_size()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cases) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 5);
        var caseService = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);

        var page1 = await caseService.ListCasesPagedAsync(baseline.Project.ProjectId, null, null, null, null, 1, 2, CancellationToken.None);
        var page3 = await caseService.ListCasesPagedAsync(baseline.Project.ProjectId, null, null, null, null, 3, 2, CancellationToken.None);

        Assert.Equal(5, page1.Total);
        Assert.Equal(2, page1.Rows.Count);
        Assert.Equal(5, page3.Total);
        Assert.Single(page3.Rows);
        // No overlap between pages — the second page's first row is not present on the first page.
        Assert.DoesNotContain(page1.Rows[0].AutomationCaseId, page3.Rows.Select(r => r.AutomationCaseId));
    }

    [Fact]
    public async Task Cases_paging_filters_by_status()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCases) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 2);
        var caseService = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);
        var draftTestCase = new ProMaxx2.QA.Domain.TestManagement.TestCase(baseline.Project.ProjectId, baseline.Module.ModuleId, "TC-SALE-999", "Never approved", null, null, "P1", "Functional", true, null,
            [new ProMaxx2.QA.Domain.TestManagement.TestStepInput(1, "Add item", null, "Item added")], null);
        draftTestCase.SetAutomationTarget("app", null);
        db.Add(draftTestCase);
        await db.SaveChangesAsync();
        await caseService.CreateAsync(baseline.Project.ProjectId, new ProMaxx2.QA.Application.Automation.CreateAutomationCaseRequest(draftTestCase.TestCaseId, "WindowsUI", null), null, CancellationToken.None);

        var readyOnly = await caseService.ListCasesPagedAsync(baseline.Project.ProjectId, null, "Ready", null, null, 1, 20, CancellationToken.None);

        Assert.Equal(2, readyOnly.Total);
        Assert.All(readyOnly.Rows, r => Assert.Equal("Ready", r.Status));
    }

    [Fact]
    public async Task Cases_paging_sorts_by_code_ascending_when_requested()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 3);
        var caseService = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);

        var sorted = await caseService.ListCasesPagedAsync(baseline.Project.ProjectId, null, null, null, "code", 1, 20, CancellationToken.None);

        var codes = sorted.Rows.Select(r => r.AutomationCode).ToList();
        Assert.Equal(codes.OrderBy(x => x, StringComparer.Ordinal), codes);
    }

    [Fact]
    public async Task Jobs_and_Executions_paging_returns_the_correct_total_and_slices_rows()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cases) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 3);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.BatchRunAsync(baseline.Project.ProjectId,
            new ProMaxx2.QA.Application.Automation.BatchRunRequest(cases.Select(c => c.ReadyCase.AutomationCaseId).ToList(), baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        var jobsPage1 = await agents.ListJobsPagedAsync(baseline.Project.ProjectId, null, null, null, 1, 2, CancellationToken.None);
        var execPage1 = await agents.ListExecutionsPagedAsync(baseline.Project.ProjectId, null, null, null, null, 1, 2, CancellationToken.None);

        Assert.Equal(3, jobsPage1.Total);
        Assert.Equal(2, jobsPage1.Rows.Count);
        Assert.Equal(3, execPage1.Total);
        Assert.Equal(2, execPage1.Rows.Count);
    }

    [Fact]
    public async Task Executions_paging_filters_by_status()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cases) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 2);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new ProMaxx2.QA.Application.Automation.RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.BatchRunAsync(baseline.Project.ProjectId,
            new ProMaxx2.QA.Application.Automation.BatchRunRequest(cases.Select(c => c.ReadyCase.AutomationCaseId).ToList(), baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ProMaxx2.QA.Application.Automation.ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);
        await agents.CompleteExecutionAsync(claim!.AutomationExecutionId, new ProMaxx2.QA.Application.Automation.CompleteExecutionRequest("Failed", "AutomationFailure", "AUT-UI-001", "Object not found"), CancellationToken.None);

        var failedOnly = await agents.ListExecutionsPagedAsync(baseline.Project.ProjectId, null, "Failed", null, null, 1, 20, CancellationToken.None);

        Assert.Single(failedOnly.Rows);
        Assert.Equal("Failed", failedOnly.Rows[0].Status);
    }

    [Fact]
    public async Task Executions_paging_searches_by_automation_code()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cases) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 2);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.BatchRunAsync(baseline.Project.ProjectId,
            new ProMaxx2.QA.Application.Automation.BatchRunRequest(cases.Select(c => c.ReadyCase.AutomationCaseId).ToList(), baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var target = cases[0].ReadyCase.AutomationCode;

        var found = await agents.ListExecutionsPagedAsync(baseline.Project.ProjectId, null, null, target, null, 1, 20, CancellationToken.None);

        Assert.Single(found.Rows);
        Assert.Equal(target, found.Rows[0].AutomationCode);
    }

    [Fact]
    public async Task Jobs_paging_filters_by_buildId()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cases) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 2);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.BatchRunAsync(baseline.Project.ProjectId,
            new ProMaxx2.QA.Application.Automation.BatchRunRequest(cases.Select(c => c.ReadyCase.AutomationCaseId).ToList(), baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        var wrongBuild = await agents.ListJobsPagedAsync(baseline.Project.ProjectId, Guid.NewGuid(), null, null, 1, 20, CancellationToken.None);
        var rightBuild = await agents.ListJobsPagedAsync(baseline.Project.ProjectId, baseline.Build.BuildId, null, null, 1, 20, CancellationToken.None);

        Assert.Equal(0, wrongBuild.Total);
        Assert.Equal(2, rightBuild.Total);
    }

    [Fact]
    public async Task Page_below_one_is_treated_as_page_one()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 1);
        var caseService = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);

        var negativePage = await caseService.ListCasesPagedAsync(baseline.Project.ProjectId, null, null, null, null, 0, 20, CancellationToken.None);

        Assert.Single(negativePage.Rows);
    }
}
