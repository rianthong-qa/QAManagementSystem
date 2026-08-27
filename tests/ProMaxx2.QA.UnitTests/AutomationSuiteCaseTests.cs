using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-P1-002 (Automation Suite case management): add/remove/reorder Case, Required/Optional, Target/Status visibility.</summary>
public sealed class AutomationSuiteCaseTests
{
    [Fact]
    public async Task Add_case_appears_with_sort_order_one_and_required_flag()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);

        var updated = await service.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);

        var link = Assert.Single(updated.Cases);
        Assert.Equal(readyCase.AutomationCaseId, link.AutomationCaseId);
        Assert.Equal(1, link.SortOrder);
        Assert.True(link.IsRequired);
        Assert.Equal(readyCase.AutomationCode, link.AutomationCode);
        Assert.Equal("Ready", link.Status);
    }

    [Fact]
    public async Task Adding_multiple_cases_assigns_sequential_sort_order()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cases) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 3);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);

        var updated = await service.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest(cases.Select(c => c.ReadyCase.AutomationCaseId).ToList(), false), CancellationToken.None);

        Assert.Equal([1, 2, 3], updated.Cases.OrderBy(x => x.SortOrder).Select(x => x.SortOrder));
    }

    [Fact]
    public async Task Adding_an_already_added_case_again_is_idempotent()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);
        await service.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);

        var updated = await service.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);

        Assert.Single(updated.Cases);
    }

    [Fact]
    public async Task Adding_a_case_from_a_different_project_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var otherBaseline = await AutomationTestFixtures.SeedBaselineAsync(db); // separate project
        var service = AutomationTestFixtures.SuiteService(db, otherBaseline.Project.ProjectId);
        var suite = await service.CreateAsync(otherBaseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddCasesAsync(suite.AutomationSuiteId, otherBaseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None));
    }

    [Fact]
    public async Task Adding_an_empty_case_list_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([], true), CancellationToken.None));
    }

    [Fact]
    public async Task Cannot_add_cases_to_a_closed_suite()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);
        await service.CloseAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new SuiteLifecycleRequest(), null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None));
    }

    [Fact]
    public async Task Update_case_changes_sort_order_and_required_flag()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);
        await service.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);

        var updated = await service.UpdateCaseAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, readyCase.AutomationCaseId, new UpdateSuiteCaseRequest(5, false), null, CancellationToken.None);

        var link = Assert.Single(updated.Cases);
        Assert.Equal(5, link.SortOrder);
        Assert.False(link.IsRequired);
    }

    [Fact]
    public async Task Update_case_not_in_the_suite_throws_not_found()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            service.UpdateCaseAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, Guid.NewGuid(), new UpdateSuiteCaseRequest(1, true), null, CancellationToken.None));
    }

    [Fact]
    public async Task Remove_case_deletes_the_membership()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);
        await service.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);

        var updated = await service.RemoveCaseAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, readyCase.AutomationCaseId, null, null, CancellationToken.None);

        Assert.Empty(updated.Cases);
    }

    [Fact]
    public async Task Cannot_modify_cases_on_a_closed_suite()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);
        await service.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);
        await service.CloseAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new SuiteLifecycleRequest(), null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RemoveCaseAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, readyCase.AutomationCaseId, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task List_reports_case_count_and_ready_case_count_separately()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var caseService = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);
        // A second TestCase whose AutomationCase stays Draft (never validated/approved) — should count toward
        // CaseCount but not ReadyCaseCount once added to the suite.
        var draftTestCase = new ProMaxx2.QA.Domain.TestManagement.TestCase(baseline.Project.ProjectId, baseline.Module.ModuleId, "TC-SALE-777", "Draft one", null, null, "P1", "Functional", true, null, [new ProMaxx2.QA.Domain.TestManagement.TestStepInput(1, "Add item", null, "Item added")], null);
        draftTestCase.SetAutomationTarget("app", null);
        db.Add(draftTestCase);
        await db.SaveChangesAsync();
        var draftCase = await caseService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationCaseRequest(draftTestCase.TestCaseId, "WindowsUI", null), null, CancellationToken.None);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);
        await service.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId, draftCase.AutomationCaseId], true), CancellationToken.None);

        var list = await service.ListAsync(baseline.Project.ProjectId, null, null, CancellationToken.None);

        var row = Assert.Single(list, x => x.AutomationSuiteId == suite.AutomationSuiteId);
        Assert.Equal(2, row.CaseCount);
        Assert.Equal(1, row.ReadyCaseCount);
    }
}
