using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.TestManagement;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Shared seeding helpers for Automation module tests. Mirrors the pattern used by RegressionControllerIntegrationTests (fresh InMemory QaDbContext per test, seed domain entities directly).</summary>
internal static class AutomationTestFixtures
{
    public static QaDbContext CreateInMemoryDatabase() =>
        new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public sealed record Baseline(Project Project, ProductModule Module, Release Release, Build Build, TestEnvironment Environment, TestCase TestCase);

    public static async Task<Baseline> SeedBaselineAsync(QaDbContext db, bool automationCandidate = true, string automationTarget = "app")
    {
        var project = new Project("AUT", "Automation", null, null, null);
        var module = new ProductModule(project.ProjectId, "SALES", "Sales", null, null, null, null);
        var release = new Release(project.ProjectId, "REL-001", "1.0", "Major", null, null, null, null);
        var build = new Build(release.ReleaseId, "1", "1.0", null, null, DateTime.UtcNow, null, null, null);
        var environment = new TestEnvironment(project.ProjectId, "QA", null);
        var testCase = new TestCase(project.ProjectId, module.ModuleId, "TC-SALE-001", "Sell over stock", "Verify stock guard", null,
            "P0", "Functional", automationCandidate, null, [new TestStepInput(1, "Add item", null, "Item added")], null);
        if (automationCandidate) testCase.SetAutomationTarget(automationTarget, null);
        db.AddRange(project, module, release, build, environment, testCase);
        await db.SaveChangesAsync();
        return new Baseline(project, module, release, build, environment, testCase);
    }

    public static ITestCaseRepository TestCaseRepository(QaDbContext db, Guid projectId) =>
        new TestCaseRepository(db, new ProjectAccessContext { AllowedProjectIds = [projectId] });

    public static AutomationCaseService CaseService(QaDbContext db, Guid projectId) =>
        new(new AutomationRepository(db), TestCaseRepository(db, projectId));

    public static AutomationAgentService AgentService(QaDbContext db) => new(new AutomationRepository(db));

    public const string SampleDsl = """
        {"dslVersion":"1.0","automationType":"WindowsUI","steps":[{"stepNo":1,"action":"LOGIN","parameters":{"userRef":"QA_STANDARD_USER"}},{"stepNo":2,"action":"SAVE_DOCUMENT","parameters":{}}]}
        """;

    /// <summary>Seeds a Project/Build/Environment/TestCase plus one Automation Case with an approved version (case ends up Ready), ready to request executions/jobs against.</summary>
    public static async Task<(Baseline Baseline, AutomationCaseDto ReadyCase, Guid VersionId)> SeedReadyCaseAsync(QaDbContext db)
    {
        var baseline = await SeedBaselineAsync(db);
        var caseService = CaseService(db, baseline.Project.ProjectId);
        var created = await caseService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationCaseRequest(baseline.TestCase.TestCaseId, "WindowsUI", null), null, CancellationToken.None);
        var version = await caseService.CreateVersionAsync(created.AutomationCaseId, baseline.Project.ProjectId, new CreateAutomationVersionRequest(SampleDsl, "initial"), null, CancellationToken.None);
        await caseService.ValidateVersionAsync(version.AutomationVersionId, baseline.Project.ProjectId, CancellationToken.None);
        var readyCase = await caseService.ApproveVersionAsync(version.AutomationVersionId, baseline.Project.ProjectId, null, CancellationToken.None);
        return (baseline, readyCase, version.AutomationVersionId);
    }
}
