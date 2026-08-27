using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.UnitTests;

/// <summary>
/// Covers AUT-TEST-007 (Evidence security): type/size validation and the path-traversal write guard on
/// <see cref="AutomationAgentController"/>'s upload endpoints (instantiated directly against a temp evidence root —
/// no HTTP host needed, since these methods don't touch HttpContext), plus project-scoped read access at the
/// Application layer. Two things are explicitly out of scope here and left for AUT-TEST-008: the
/// `[Authorize(Policy = "AutomationEvidence")]` attribute and `AutomationController`'s `[RequireProjectAccess]`
/// filter are enforced by the ASP.NET authorization pipeline, which requires a hosted server (WebApplicationFactory)
/// to exercise — not reachable by calling a controller method directly.
/// </summary>
public sealed class AutomationEvidenceSecurityTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "aut-evidence-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private sealed class FakeWebHostEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Test";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = contentRoot;
    }

    private AutomationAgentController MakeController(ProMaxx2.QA.Infrastructure.Persistence.QaDbContext db) =>
        new(AutomationTestFixtures.AgentService(db), AutomationTestFixtures.SnapshotService(db), AutomationTestFixtures.RestoreService(db), AutomationTestFixtures.SeedService(db), new FakeWebHostEnvironment(_tempRoot));

    private static IFormFile MakeFile(string fileName, int sizeBytes = 10) =>
        new FormFile(new MemoryStream(new byte[sizeBytes]), 0, sizeBytes, "file", fileName);

    private static string EvidenceRoot(string tempRoot) => Path.Combine(tempRoot, "App_Data", "AutomationEvidence") + Path.DirectorySeparatorChar;

    [Fact]
    public async Task Step_evidence_upload_rejects_disallowed_file_extension()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var controller = MakeController(db);

        var result = await controller.UploadEvidence(Guid.NewGuid(), 1, MakeFile("payload.exe"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(Directory.Exists(EvidenceRoot(_tempRoot))); // rejected before any file/dir was created
    }

    [Fact]
    public async Task Step_evidence_upload_rejects_zero_byte_and_oversized_files()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var controller = MakeController(db);

        var empty = await controller.UploadEvidence(Guid.NewGuid(), 1, MakeFile("shot.png", 0), CancellationToken.None);
        var tooBig = await controller.UploadEvidence(Guid.NewGuid(), 1, MakeFile("shot.png", 10_000_001), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(empty);
        Assert.IsType<BadRequestObjectResult>(tooBig);
    }

    [Fact]
    public async Task Step_evidence_upload_for_a_step_that_was_never_reported_returns_not_found_and_deletes_the_orphan_file()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var controller = MakeController(db);
        var executionId = Guid.NewGuid(); // no execution/step seeded -> AttachStepEvidenceAsync will fail

        var result = await controller.UploadEvidence(executionId, 1, MakeFile("shot.png"), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        var expectedPath = Path.Combine(EvidenceRoot(_tempRoot), executionId.ToString("N"), "step1.png");
        Assert.False(System.IO.File.Exists(expectedPath)); // written speculatively, then cleaned up on failure
    }

    [Fact]
    public async Task Execution_evidence_upload_rejects_disallowed_extension_and_oversized_file()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var controller = MakeController(db);

        var badType = await controller.UploadExecutionEvidence(Guid.NewGuid(), null, "AutomationLog", MakeFile("payload.exe"), CancellationToken.None);
        var tooBig = await controller.UploadExecutionEvidence(Guid.NewGuid(), null, "AutomationLog", MakeFile("log.txt", 10_000_001), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(badType);
        Assert.IsType<BadRequestObjectResult>(tooBig);
    }

    [Fact]
    public async Task Execution_evidence_upload_blocks_a_path_traversal_evidence_type_from_escaping_the_evidence_root()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var controller = MakeController(db);
        var executionId = Guid.NewGuid();

        // evidenceType is free text from the agent/form and is embedded directly into the on-disk file name —
        // a malicious value must not be able to walk the resulting path outside the evidence root.
        var result = await controller.UploadExecutionEvidence(executionId, null, "..\\..\\..\\escaped", MakeFile("log.txt"), CancellationToken.None);

        Assert.IsType<BadRequestResult>(result); // the traversal guard returns a bare BadRequest(), not a Problem body
        Assert.False(Directory.Exists(_tempRoot) && Directory.EnumerateFiles(_tempRoot, "escaped*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Execution_evidence_upload_for_an_execution_that_does_not_exist_returns_not_found_and_deletes_the_orphan_file()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var controller = MakeController(db);
        var executionId = Guid.NewGuid();

        var result = await controller.UploadExecutionEvidence(executionId, null, "AutomationLog", MakeFile("log.txt"), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        Assert.False(Directory.Exists(EvidenceRoot(_tempRoot)) && Directory.EnumerateFiles(Path.Combine(EvidenceRoot(_tempRoot), executionId.ToString("N"))).Any());
    }

    [Fact]
    public async Task Execution_evidence_upload_succeeds_for_a_real_execution_and_lands_only_under_its_own_execution_folder()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException();
        var controller = MakeController(db);

        var result = await controller.UploadExecutionEvidence(claim.AutomationExecutionId, null, "Screenshot", MakeFile("shot.png"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var executionFolder = Path.Combine(EvidenceRoot(_tempRoot), claim.AutomationExecutionId.ToString("N"));
        Assert.True(Directory.Exists(executionFolder));
        Assert.Single(Directory.EnumerateFiles(executionFolder));
    }

    [Fact]
    public async Task Evidence_path_lookup_is_scoped_to_the_owning_project_not_visible_from_another_project()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException();
        await agents.ReportStepResultAsync(claim.AutomationExecutionId, new ReportStepResultRequest(1, "LOGIN", "Pass", "ok", null, null, "evidence/step1.png", DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow), CancellationToken.None);
        var execution = await agents.GetExecutionAsync(claim.AutomationExecutionId, baseline.Project.ProjectId, CancellationToken.None);
        var stepResultId = execution.StepResults.Single().AutomationStepResultId;
        var otherProjectId = Guid.NewGuid();

        // Same executionId, but requested under a project the execution does not belong to.
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            agents.GetEvidencePathAsync(claim.AutomationExecutionId, stepResultId, otherProjectId, CancellationToken.None));

        // The owning project can still read it.
        var path = await agents.GetEvidencePathAsync(claim.AutomationExecutionId, stepResultId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal("evidence/step1.png", path);
    }
}
