using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-P1-001 (Persistent Automation Suite): create/edit/close/reopen, persisted and project-scoped.</summary>
public sealed class AutomationSuiteTests
{
    [Fact]
    public async Task Create_with_explicit_code_persists_and_is_retrievable()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);

        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest("AUT-AS-SMOKE", "Smoke Suite", "Daily smoke run"), null, CancellationToken.None);

        Assert.Equal("AUT-AS-SMOKE", created.SuiteCode);
        Assert.True(created.IsActive);
        var fetched = await service.GetAsync(created.AutomationSuiteId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal(created.AutomationSuiteId, fetched.AutomationSuiteId);
    }

    [Fact]
    public async Task Create_without_a_code_auto_generates_one_from_the_project_code()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);

        var first = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite One", null), null, CancellationToken.None);
        var second = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Suite Two", null), null, CancellationToken.None);

        Assert.Equal("AUT-AS-001", first.SuiteCode);
        Assert.Equal("AUT-AS-002", second.SuiteCode);
    }

    [Fact]
    public async Task Create_with_a_duplicate_code_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest("AUT-AS-DUP", "First", null), null, CancellationToken.None);

        await Assert.ThrowsAsync<DuplicateCodeException>(() =>
            service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest("AUT-AS-DUP", "Second", null), null, CancellationToken.None));
    }

    [Fact]
    public async Task Update_changes_name_and_description()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Original", null), null, CancellationToken.None);

        var updated = await service.UpdateAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new UpdateAutomationSuiteRequest("Renamed", "New description"), null, CancellationToken.None);

        Assert.Equal("Renamed", updated.SuiteName);
        Assert.Equal("New description", updated.Description);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task Close_sets_inactive_and_closed_at()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "To Close", null), null, CancellationToken.None);

        var closed = await service.CloseAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new SuiteLifecycleRequest(), null, CancellationToken.None);

        Assert.False(closed.IsActive);
        Assert.NotNull(closed.ClosedAt);
    }

    [Fact]
    public async Task Closing_an_already_closed_suite_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "To Close", null), null, CancellationToken.None);
        await service.CloseAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new SuiteLifecycleRequest(), null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new SuiteLifecycleRequest(), null, CancellationToken.None));
    }

    [Fact]
    public async Task Editing_a_closed_suite_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "To Close", null), null, CancellationToken.None);
        await service.CloseAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new SuiteLifecycleRequest(), null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new UpdateAutomationSuiteRequest("New Name", null), null, CancellationToken.None));
    }

    [Fact]
    public async Task Reopen_restores_active_and_clears_closed_metadata()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "To Reopen", null), null, CancellationToken.None);
        await service.CloseAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new SuiteLifecycleRequest(), null, CancellationToken.None);

        var reopened = await service.ReopenAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new SuiteLifecycleRequest(), null, CancellationToken.None);

        Assert.True(reopened.IsActive);
        Assert.Null(reopened.ClosedAt);
    }

    [Fact]
    public async Task Reopening_an_already_open_suite_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Open", null), null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReopenAsync(created.AutomationSuiteId, baseline.Project.ProjectId, new SuiteLifecycleRequest(), null, CancellationToken.None));
    }

    [Fact]
    public async Task List_filters_by_active_status_and_search_text()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var open = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Nightly Regression", null), null, CancellationToken.None);
        var closed = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Old Smoke", null), null, CancellationToken.None);
        await service.CloseAsync(closed.AutomationSuiteId, baseline.Project.ProjectId, new SuiteLifecycleRequest(), null, CancellationToken.None);

        var activeOnly = await service.ListAsync(baseline.Project.ProjectId, null, true, CancellationToken.None);
        var bySearch = await service.ListAsync(baseline.Project.ProjectId, "Nightly", null, CancellationToken.None);

        Assert.Single(activeOnly, x => x.AutomationSuiteId == open.AutomationSuiteId);
        Assert.Single(bySearch, x => x.AutomationSuiteId == open.AutomationSuiteId);
    }

    [Fact]
    public async Task Suite_is_not_visible_from_a_different_project()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Scoped", null), null, CancellationToken.None);
        var otherProjectId = Guid.NewGuid();

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            service.GetAsync(created.AutomationSuiteId, otherProjectId, CancellationToken.None));
    }
}
