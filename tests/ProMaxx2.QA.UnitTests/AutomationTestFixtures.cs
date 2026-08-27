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

    public static AutomationAgentService AgentService(QaDbContext db)
    {
        var repo = new AutomationRepository(db);
        return new AutomationAgentService(repo, repo, repo);
    }

    public static ProMaxx2.QA.Application.Projects.IProjectRepository ProjectRepository(QaDbContext db, Guid projectId) =>
        new ProjectRepository(db, new ProjectAccessContext { AllowedProjectIds = [projectId] });

    public static AutomationSuiteService SuiteService(QaDbContext db, Guid projectId) =>
        new(new AutomationRepository(db), ProjectRepository(db, projectId));

    public static AutomationScheduleService ScheduleService(QaDbContext db)
    {
        var repo = new AutomationRepository(db);
        return new AutomationScheduleService(repo, repo);
    }

    public static AutomationBuildTriggerService BuildTriggerService(QaDbContext db)
    {
        var repo = new AutomationRepository(db);
        return new AutomationBuildTriggerService(repo, repo, AgentService(db));
    }

    /// <summary>A real <c>ReleaseService</c> wired with a real <see cref="AutomationBuildTriggerService"/> (AUT-P1-007),
    /// so tests can exercise Build-creation/Mark-RC through the actual production wiring instead of seeding
    /// Build/Release directly via EF like <see cref="SeedBaselineAsync"/> does.</summary>
    public static ProMaxx2.QA.Application.Releases.ReleaseService ReleaseServiceWithBuildTrigger(QaDbContext db, Guid projectId)
    {
        var releaseRepo = new ReleaseRepository(db, new ProjectAccessContext { AllowedProjectIds = [projectId] });
        return new ProMaxx2.QA.Application.Releases.ReleaseService(releaseRepo, ProjectRepository(db, projectId), BuildTriggerService(db));
    }

    /// <summary>A real <c>AutomationWebhookService</c> wired with a real <see cref="ReleaseServiceWithBuildTrigger"/>
    /// (AUT-P1-008), so a webhook-created Build goes through the exact same production chain as one created via the
    /// Automation Suite/Schedule/Build Trigger features already covered elsewhere.</summary>
    public static AutomationWebhookService WebhookService(QaDbContext db, Guid projectId)
    {
        var repo = new AutomationRepository(db);
        return new AutomationWebhookService(repo, ReleaseServiceWithBuildTrigger(db, projectId));
    }

    public const string SampleDsl = """
        {"dslVersion":"1.0","automationType":"WindowsUI","steps":[{"stepNo":1,"action":"LOGIN","parameters":{"userRef":"QA_STANDARD_USER"}},{"stepNo":2,"action":"SAVE_DOCUMENT","parameters":{}}]}
        """;

    /// <summary>Seeds a Project/Build/Environment/TestCase plus one Automation Case with an approved version (case ends up Ready), ready to request executions/jobs against.</summary>
    public static async Task<(Baseline Baseline, AutomationCaseDto ReadyCase, Guid VersionId)> SeedReadyCaseAsync(QaDbContext db)
    {
        var baseline = await SeedBaselineAsync(db);
        var (readyCase, versionId) = await ApproveNewCaseAsync(db, baseline, baseline.TestCase.TestCaseId, "WindowsUI");
        return (baseline, readyCase, versionId);
    }

    /// <summary>Seeds one baseline project/build/environment plus <paramref name="count"/> additional Ready automation cases (each on its own TestCase), for tests that need several cases to batch-run against.</summary>
    public static async Task<(Baseline Baseline, IReadOnlyList<(AutomationCaseDto ReadyCase, Guid VersionId)> Cases)> SeedReadyCasesAsync(QaDbContext db, int count, string automationType = "WindowsUI")
    {
        var baseline = await SeedBaselineAsync(db);
        var cases = new List<(AutomationCaseDto, Guid)>();
        for (var i = 0; i < count; i++)
        {
            var testCase = new TestCase(baseline.Project.ProjectId, baseline.Module.ModuleId, $"TC-SALE-{i + 100}", $"Batch case {i}", null, null,
                "P1", "Functional", true, null, [new TestStepInput(1, "Add item", null, "Item added")], null);
            testCase.SetAutomationTarget("app", null);
            db.Add(testCase);
            await db.SaveChangesAsync();
            cases.Add(await ApproveNewCaseAsync(db, baseline, testCase.TestCaseId, automationType));
        }
        return (baseline, cases);
    }

    private static async Task<(AutomationCaseDto ReadyCase, Guid VersionId)> ApproveNewCaseAsync(QaDbContext db, Baseline baseline, Guid testCaseId, string automationType)
    {
        var caseService = CaseService(db, baseline.Project.ProjectId);
        var created = await caseService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationCaseRequest(testCaseId, automationType, null), null, CancellationToken.None);
        var version = await caseService.CreateVersionAsync(created.AutomationCaseId, baseline.Project.ProjectId, new CreateAutomationVersionRequest(SampleDsl, "initial"), null, CancellationToken.None);
        await caseService.ValidateVersionAsync(version.AutomationVersionId, baseline.Project.ProjectId, CancellationToken.None);
        var readyCase = await caseService.ApproveVersionAsync(version.AutomationVersionId, baseline.Project.ProjectId, null, CancellationToken.None);
        return (readyCase, version.AutomationVersionId);
    }
}
