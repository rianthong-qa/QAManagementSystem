using ProMaxx2.QA.Application.Automation;

namespace ProMaxx2.QA.UnitTests;

public sealed class AutomationCaseWorkflowTests
{
    [Fact]
    public async Task Create_case_starts_in_draft_and_rejects_non_candidate_test_case()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db, automationCandidate: false);
        var service = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationCaseRequest(baseline.TestCase.TestCaseId, "WindowsUI", null), null, CancellationToken.None));
    }

    [Fact]
    public async Task Full_workflow_draft_to_ready_to_maintenance_to_needs_review()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);

        // Draft
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationCaseRequest(baseline.TestCase.TestCaseId, "WindowsUI", null), null, CancellationToken.None);
        Assert.Equal("Draft", created.Status);
        Assert.Equal(0, created.CurrentVersionNo);

        // Only one automation case per test case
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationCaseRequest(baseline.TestCase.TestCaseId, "WindowsUI", null), null, CancellationToken.None));

        // Create version -> stays Draft until validated/approved
        var version = await service.CreateVersionAsync(created.AutomationCaseId, baseline.Project.ProjectId, new CreateAutomationVersionRequest(AutomationTestFixtures.SampleDsl, "initial"), null, CancellationToken.None);
        Assert.Equal("Pending", version.ValidationStatus);

        // Validate -> Valid (no action/object library seeded so those checks are bypassed)
        var validated = await service.ValidateVersionAsync(version.AutomationVersionId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal("Valid", validated.ValidationStatus);

        // Approve -> case becomes Ready
        var approvedCase = await service.ApproveVersionAsync(version.AutomationVersionId, baseline.Project.ProjectId, null, CancellationToken.None);
        Assert.Equal("Ready", approvedCase.Status);
        Assert.Equal(1, approvedCase.CurrentVersionNo);

        // Simulate a failed run that requires maintenance
        var maintenanceCase = await service.ChangeStatusAsync(created.AutomationCaseId, baseline.Project.ProjectId, "MaintenanceRequired", CancellationToken.None);
        Assert.Equal("MaintenanceRequired", maintenanceCase.Status);
        Assert.Null(maintenanceCase.MaintenanceReason); // ChangeStatusAsync doesn't set a reason (only CompleteExecutionAsync's RequireMaintenance path does)

        // Assign owner then resolve -> back to NeedsReview
        var ownerId = Guid.NewGuid();
        var withOwner = await service.AssignMaintenanceOwnerAsync(created.AutomationCaseId, baseline.Project.ProjectId, ownerId, CancellationToken.None);
        Assert.Equal(ownerId, withOwner.MaintenanceOwnerUserId);

        var resolved = await service.ResolveMaintenanceAsync(created.AutomationCaseId, baseline.Project.ProjectId, "Fixed the AutomationId mapping", null, CancellationToken.None);
        Assert.Equal("NeedsReview", resolved.Status);
        Assert.Null(resolved.MaintenanceOwnerUserId);
        Assert.Null(resolved.MaintenanceReason);
    }

    [Fact]
    public async Task Invalid_dsl_marks_version_invalid_and_case_needs_review()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationCaseRequest(baseline.TestCase.TestCaseId, "WindowsUI", null), null, CancellationToken.None);
        var badDsl = """{"dslVersion":"2.0","automationType":"WindowsUI","steps":[]}""";
        var version = await service.CreateVersionAsync(created.AutomationCaseId, baseline.Project.ProjectId, new CreateAutomationVersionRequest(badDsl, null), null, CancellationToken.None);

        var validated = await service.ValidateVersionAsync(version.AutomationVersionId, baseline.Project.ProjectId, CancellationToken.None);

        Assert.Equal("Invalid", validated.ValidationStatus);
        Assert.NotNull(validated.ValidationErrors);
        var caseAfter = await service.GetCaseAsync(created.AutomationCaseId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal("NeedsReview", caseAfter.Status);
    }

    [Fact]
    public async Task Resolve_maintenance_throws_when_case_is_not_in_maintenance()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);
        var created = await service.CreateAsync(baseline.Project.ProjectId, new CreateAutomationCaseRequest(baseline.TestCase.TestCaseId, "WindowsUI", null), null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResolveMaintenanceAsync(created.AutomationCaseId, baseline.Project.ProjectId, null, null, CancellationToken.None));
    }
}
