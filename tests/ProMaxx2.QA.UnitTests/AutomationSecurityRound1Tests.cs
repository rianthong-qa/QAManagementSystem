using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Automation;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>AUT-SEC-003/004/005 (รอบที่ 1 หลังวิเคราะห์ระบบ Automation 2026-09-23): project isolation ของ service,
/// การจำกัด SQL ใน DB assertion และการตรวจตัวตน Agent ที่รายงานผล</summary>
public sealed class AutomationSecurityRound1Tests
{
    private static async Task<(Project Project, Release Release, Build Build, TestEnvironment Environment)> SeedOtherProjectAsync(QaDbContext db)
    {
        var project = new Project("OTH", "Other", null, null, null);
        var release = new Release(project.ProjectId, "REL-OTH", "9.0", "Major", null, null, null, null);
        var build = new Build(release.ReleaseId, "99", "9.0", null, null, DateTime.UtcNow, null, null, null);
        var environment = new TestEnvironment(project.ProjectId, "OTHER-QA", null);
        db.AddRange(project, release, build, environment);
        await db.SaveChangesAsync();
        return (project, release, build, environment);
    }

    // ---------- AUT-SEC-003: project isolation ----------

    [Fact]
    public async Task Listing_versions_of_a_case_through_another_project_is_not_found()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var other = await SeedOtherProjectAsync(db);
        var cases = AutomationTestFixtures.CaseService(db, other.Project.ProjectId);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => cases.ListVersionsAsync(readyCase.AutomationCaseId, other.Project.ProjectId, CancellationToken.None));
        Assert.NotEmpty(await AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId).ListVersionsAsync(readyCase.AutomationCaseId, baseline.Project.ProjectId, CancellationToken.None));
    }

    [Fact]
    public async Task Running_with_an_explicit_version_requires_it_to_belong_to_the_case_and_be_approved()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cases) = await AutomationTestFixtures.SeedReadyCasesAsync(db, 2);
        var agents = AutomationTestFixtures.AgentService(db);
        var (caseA, _) = cases[0];
        var (_, versionOfB) = cases[1];

        await Assert.ThrowsAsync<ArgumentException>(() => agents.RequestExecutionAsync(baseline.Project.ProjectId,
            new RequestExecutionRequest(caseA.AutomationCaseId, versionOfB, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None));

        var caseService = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);
        var draft = await caseService.CreateVersionAsync(caseA.AutomationCaseId, baseline.Project.ProjectId, new CreateAutomationVersionRequest(AutomationTestFixtures.SampleDsl, "draft"), null, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => agents.RequestExecutionAsync(baseline.Project.ProjectId,
            new RequestExecutionRequest(caseA.AutomationCaseId, draft.AutomationVersionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None));
    }

    [Fact]
    public async Task Build_and_environment_from_another_project_are_rejected_for_runs()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var other = await SeedOtherProjectAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => agents.RequestExecutionAsync(baseline.Project.ProjectId,
            new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, other.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => agents.BatchRunAsync(baseline.Project.ProjectId,
            new BatchRunRequest([readyCase.AutomationCaseId], baseline.Build.BuildId, other.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None));
    }

    [Fact]
    public async Task Cancelling_through_another_project_does_not_touch_the_execution()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var execution = await agents.RequestExecutionAsync(baseline.Project.ProjectId,
            new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => agents.CancelExecutionAsync(execution.AutomationExecutionId, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("Queued", (await db.AutomationExecutions.AsNoTracking().SingleAsync(x => x.AutomationExecutionId == execution.AutomationExecutionId)).Status);
    }

    [Fact]
    public async Task A_case_cannot_be_forced_to_Ready_without_an_approved_version()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var cases = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);
        var created = await cases.CreateAsync(baseline.Project.ProjectId, new CreateAutomationCaseRequest(baseline.TestCase.TestCaseId, "WindowsUI", null), null, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => cases.ChangeStatusAsync(created.AutomationCaseId, baseline.Project.ProjectId, "Ready", CancellationToken.None));
    }

    [Fact]
    public async Task A_webhook_token_cannot_create_a_build_in_another_projects_release()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var other = await SeedOtherProjectAsync(db);
        // production: the webhook request has no user, so the release repository sees an empty project list —
        // reproduce that instead of the fixture's project-scoped repository
        var repo = new AutomationRepository(db);
        var releases = new ProMaxx2.QA.Application.Releases.ReleaseService(new ReleaseRepository(db, new ProjectAccessContext()), AutomationTestFixtures.ProjectRepository(db, baseline.Project.ProjectId), AutomationTestFixtures.BuildTriggerService(db));
        var webhooks = new AutomationWebhookService(repo, releases);
        var token = await webhooks.CreateTokenAsync(baseline.Project.ProjectId, new CreateAutomationWebhookTokenRequest("CI"), null, CancellationToken.None);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => webhooks.ReceiveBuildAsync(token.PlainTextToken,
            new ReceiveBuildWebhookRequest(other.Release.ReleaseId, "100", null, null, null, null, null, null, "ci-cross"), CancellationToken.None));

        Assert.False(await db.Builds.AnyAsync(x => x.ReleaseId == other.Release.ReleaseId && x.BuildNumber == "100"));
    }

    // ---------- AUT-SEC-004: read-only DB assertion SQL ----------

    [Theory]
    [InlineData("DELETE FROM ITEMS")]
    [InlineData("SELECT 1; DROP TABLE ITEMS")]
    [InlineData("SELECT * INTO BACKUP_ITEMS FROM ITEMS")]
    [InlineData("SELECT 1 -- hidden")]
    [InlineData("EXEC sp_who")]
    [InlineData("WITH x AS (SELECT 1 AS n) UPDATE ITEMS SET QTY = 0")]
    public void Db_assertion_queries_that_can_change_data_are_rejected(string query)
    {
        var dsl = new DslDocument { DslVersion = "1.0", AutomationType = "WindowsUI", Steps = [new DslStep { StepNo = 1, Action = "EXPECT_DB_VALUE", Parameters = new() { ["query"] = query, ["expected"] = "1" } }] };

        var result = AutomationValidator.Validate(dsl, [], [], null);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("SELECT COUNT(*) FROM ITEMS WHERE STATUS = 'DELETE'")]
    [InlineData("select qty from items where code = @code;")]
    [InlineData("WITH s AS (SELECT 1 AS n) SELECT n FROM s")]
    public void Read_only_select_queries_are_accepted(string query)
    {
        Assert.True(DbAssertionSqlGuard.IsReadOnlySelect(query, out var reason), reason);
    }

    // ---------- AUT-SEC-005: reporting agent identity ----------

    private static async Task<(AutomationAgentService Agents, AutomationJobPackageDto Claim)> ClaimAsAgentAAsync(QaDbContext db)
    {
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "M-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-B", "M-B", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException("no job");
        return (agents, claim);
    }

    [Fact]
    public async Task Only_the_claiming_agent_can_report_steps_and_complete_an_execution()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (agents, claim) = await ClaimAsAgentAAsync(db);
        var now = DateTime.UtcNow;

        await Assert.ThrowsAsync<AgentMismatchException>(() => agents.ReportStepResultAsync(claim.AutomationExecutionId, new ReportStepResultRequest(1, "LOGIN", "Pass", null, null, null, null, now, now, "AGENT-B"), CancellationToken.None));
        await Assert.ThrowsAsync<AgentMismatchException>(() => agents.CompleteExecutionAsync(claim.AutomationExecutionId, new CompleteExecutionRequest("Passed", null, null, null, "AGENT-B"), CancellationToken.None));

        await agents.ReportStepResultAsync(claim.AutomationExecutionId, new ReportStepResultRequest(1, "LOGIN", "Pass", null, null, null, null, now, now, "AGENT-A"), CancellationToken.None);
        var completed = await agents.CompleteExecutionAsync(claim.AutomationExecutionId, new CompleteExecutionRequest("Passed", null, null, null, "AGENT-A"), CancellationToken.None);
        Assert.Equal("Passed", completed.Status);

        // a late step report after completion is ignored instead of appending to a finished execution
        await agents.ReportStepResultAsync(claim.AutomationExecutionId, new ReportStepResultRequest(2, "SAVE_DOCUMENT", "Fail", null, null, null, null, now, now, "AGENT-A"), CancellationToken.None);
        Assert.Equal(1, await db.AutomationStepResults.CountAsync(x => x.AutomationExecutionId == claim.AutomationExecutionId));
    }

    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "aut-sec-" + Guid.NewGuid().ToString("N"));
        public string EnvironmentName { get; set; } = "Test";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = "";
    }

    [Fact]
    public async Task Agent_endpoints_require_an_agent_code()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var controller = new AutomationAgentController(AutomationTestFixtures.AgentService(db), AutomationTestFixtures.SnapshotService(db), AutomationTestFixtures.RestoreService(db), AutomationTestFixtures.SeedService(db), new FakeEnvironment());

        Assert.IsType<BadRequestObjectResult>((await controller.Complete(Guid.NewGuid(), new CompleteExecutionRequest("Passed", null, null, null), CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>(await controller.ReportStep(Guid.NewGuid(), 1, new ReportStepResultRequest(1, "LOGIN", "Pass", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>((await controller.CompleteSeedRun(Guid.NewGuid(), new CompleteSeedRunRequest("Succeeded", 1, null), CancellationToken.None)).Result);
    }

    [Fact]
    public async Task A_master_data_run_is_not_delivered_when_the_script_changed_after_the_request()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var seeds = AutomationTestFixtures.SeedService(db);
        await AutomationTestFixtures.AgentService(db).RegisterAsync(new RegisterAgentRequest("AGENT-A", "M-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var script = await seeds.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Prices", null, "MasterData", "Firebird", "UPDATE PRICES SET P = 1;"), null, CancellationToken.None);
        await seeds.ApproveScriptAsync(script.AutomationDataSeedScriptId, baseline.Project.ProjectId, Guid.NewGuid(), CancellationToken.None);
        var run = await seeds.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);

        // edited after the run was requested → approval resets to Pending
        await seeds.UpdateScriptAsync(script.AutomationDataSeedScriptId, baseline.Project.ProjectId, new UpdateSeedScriptRequest("Prices", null, "MasterData", "Firebird", "DELETE FROM PRICES;"), null, CancellationToken.None);

        Assert.Null(await seeds.ClaimNextAsync("AGENT-A", CancellationToken.None));
        Assert.Equal("Failed", (await seeds.GetRunAsync(run.AutomationDataSeedRunId, baseline.Project.ProjectId, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task The_author_of_a_seed_script_cannot_approve_it()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var seeds = AutomationTestFixtures.SeedService(db);
        var author = Guid.NewGuid();
        var script = await seeds.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Prices", null, "MasterData", "Firebird", "UPDATE PRICES SET P = 1;"), author, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => seeds.ApproveScriptAsync(script.AutomationDataSeedScriptId, baseline.Project.ProjectId, author, CancellationToken.None));
        Assert.Equal("Approved", (await seeds.ApproveScriptAsync(script.AutomationDataSeedScriptId, baseline.Project.ProjectId, Guid.NewGuid(), CancellationToken.None)).ApprovalStatus);
    }
}
