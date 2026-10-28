using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-P1-003 (Suite Version/History): every mutation records a revision with change type, an
/// optional change reason, who changed it, and when — and the suite's own RevisionNo counter tracks the latest.</summary>
public sealed class AutomationSuiteHistoryTests
{
    [Fact]
    public async Task Creating_a_suite_records_revision_one_as_created()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);

        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Smoke", null), null, CancellationToken.None);

        Assert.Equal(1, created.RevisionNo);
        var history = await service.ListRevisionsAsync(created.AutomationSuiteId, baseline.Project.ProjectId, CancellationToken.None);
        var entry = Assert.Single(history);
        Assert.Equal(1, entry.RevisionNo);
        Assert.Equal("Created", entry.ChangeType);
    }

    [Fact]
    public async Task Updating_a_suite_bumps_the_revision_and_records_the_change_reason()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Original", null), null, CancellationToken.None);

        var updated = await service.UpdateAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new UpdateAutomationSuiteRequest("Renamed", null, "Aligning with new naming convention"), null, CancellationToken.None);

        Assert.Equal(2, updated.RevisionNo);
        var history = await service.ListRevisionsAsync(created.AutomationSuiteId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal(2, history.Count);
        var latest = history[0]; // newest first
        Assert.Equal(2, latest.RevisionNo);
        Assert.Equal("Updated", latest.ChangeType);
        Assert.Equal("Aligning with new naming convention", latest.ChangeReason);
        Assert.Contains("Renamed", latest.Detail);
    }

    [Fact]
    public async Task Close_and_reopen_each_record_their_own_revision()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);

        await service.CloseAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new SuiteLifecycleRequest("End of sprint"), null, CancellationToken.None);
        await service.ReopenAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new SuiteLifecycleRequest("Needed for hotfix regression"), null, CancellationToken.None);

        var history = await service.ListRevisionsAsync(created.AutomationSuiteId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal(["Reopened", "Closed", "Created"], history.Select(x => x.ChangeType));
        Assert.Equal("End of sprint", history.Single(x => x.ChangeType == "Closed").ChangeReason);
        Assert.Equal("Needed for hotfix regression", history.Single(x => x.ChangeType == "Reopened").ChangeReason);
    }

    [Fact]
    public async Task Adding_cases_records_one_revision_listing_the_case_codes()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cases) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 2);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);

        await service.AddCasesAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest(cases.Select(c => c.ReadyCase.AutomationCaseId).ToList(), true, "Initial smoke set"), CancellationToken.None);

        var history = await service.ListRevisionsAsync(created.AutomationSuiteId, baseline.Project.ProjectId, CancellationToken.None);
        var entry = history.Single(x => x.ChangeType == "CasesAdded");
        Assert.Equal("Initial smoke set", entry.ChangeReason);
        Assert.Contains(cases[0].ReadyCase.AutomationCode, entry.Detail);
        Assert.Contains(cases[1].ReadyCase.AutomationCode, entry.Detail);
    }

    [Fact]
    public async Task Re_adding_only_already_present_cases_does_not_create_a_spurious_revision()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);
        await service.AddCasesAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);
        var revisionAfterFirstAdd = (await service.GetAsync(created.AutomationSuiteId, baseline.Project.ProjectId, CancellationToken.None)).RevisionNo;

        var afterIdempotentAdd = await service.AddCasesAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);

        Assert.Equal(revisionAfterFirstAdd, afterIdempotentAdd.RevisionNo); // no-op add didn't bump the revision
        var history = await service.ListRevisionsAsync(created.AutomationSuiteId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Single(history, x => x.ChangeType == "CasesAdded");
    }

    [Fact]
    public async Task Updating_and_removing_a_case_each_record_a_revision_naming_the_case()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);
        await service.AddCasesAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);

        await service.UpdateCaseAsync(created.AutomationSuiteId, baseline.Project.ProjectId, readyCase.AutomationCaseId, new UpdateSuiteCaseRequest(1, false), null, CancellationToken.None);
        await service.RemoveCaseAsync(created.AutomationSuiteId, baseline.Project.ProjectId, readyCase.AutomationCaseId, "No longer relevant", null, CancellationToken.None);

        var history = await service.ListRevisionsAsync(created.AutomationSuiteId, baseline.Project.ProjectId, CancellationToken.None);
        var updateEntry = history.Single(x => x.ChangeType == "CaseUpdated");
        var removeEntry = history.Single(x => x.ChangeType == "CaseRemoved");
        Assert.Contains(readyCase.AutomationCode, updateEntry.Detail);
        Assert.Contains(readyCase.AutomationCode, removeEntry.Detail);
        Assert.Equal("No longer relevant", removeEntry.ChangeReason);
    }

    [Fact]
    public async Task History_for_a_suite_in_a_different_project_throws_not_found()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite", null), null, CancellationToken.None);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            service.ListRevisionsAsync(created.AutomationSuiteId, Guid.NewGuid(), CancellationToken.None));
    }
}
