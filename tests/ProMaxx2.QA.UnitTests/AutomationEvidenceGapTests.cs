using ProMaxx2.QA.Application.Automation;

namespace ProMaxx2.QA.UnitTests;

/// <summary>AUT-TEST-011: test ของฟีเจอร์ที่เดิมมีหลักฐานแค่ build — Import Object (AUT-P0-005), Failure Dashboard
/// (AUT-P0-008) และ Flaky Candidates (AUT-P0-010)</summary>
public sealed class AutomationEvidenceGapTests
{
    private static AutomationObjectImportItem Item(string code, string? automationId, string name = "Object", string screen = "SALE")
        => new(null, "Promaxx2", screen, code, name, "Button", automationId, "{}");

    [Fact]
    public async Task Import_objects_imports_valid_rows_and_skips_duplicates_existing_and_invalid_rows_with_reasons()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);
        await service.ImportObjectsAsync(new ImportAutomationObjectsRequest(baseline.Project.ProjectId, [Item("EXISTING", "BtnExisting")]), CancellationToken.None);

        var result = await service.ImportObjectsAsync(new ImportAutomationObjectsRequest(baseline.Project.ProjectId,
        [
            Item("SAVE", "BtnSave"),
            Item("save", "BtnSave2"),         // same business key (code is upper-cased) as SAVE
            Item("CANCEL", "BtnSave"),        // same AutomationId in this batch
            Item("EXISTING", "BtnOther"),     // business key already in the repository
            Item("OTHER", "BtnExisting"),     // AutomationId already in the repository
            Item("NONAME", "BtnNoName", name: ""),
        ]), CancellationToken.None);

        Assert.Equal(1, result.Imported);
        Assert.Equal(5, result.Skipped);
        Assert.Equal("Imported", result.Rows[0].Status);
        Assert.Contains("Duplicate business key", result.Rows[1].Message);
        Assert.Contains("Duplicate AutomationId", result.Rows[2].Message);
        Assert.Contains("Business key already exists", result.Rows[3].Message);
        Assert.Contains("AutomationId already exists", result.Rows[4].Message);
        Assert.Contains("required", result.Rows[5].Message);
        var objects = await service.ListObjectsAsync(baseline.Project.ProjectId, null, CancellationToken.None);
        Assert.Equal(2, objects.Count); // EXISTING + SAVE
    }

    [Fact]
    public async Task Import_objects_rejects_empty_and_oversized_batches()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportObjectsAsync(new ImportAutomationObjectsRequest(baseline.Project.ProjectId, []), CancellationToken.None));
        var tooMany = Enumerable.Range(0, 501).Select(i => Item($"O{i}", null)).ToList();
        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportObjectsAsync(new ImportAutomationObjectsRequest(baseline.Project.ProjectId, tooMany), CancellationToken.None));
    }

    private static async Task RunAsync(AutomationAgentService agents, AutomationTestFixtures.Baseline baseline, Guid caseId, Guid versionId, string status, string? errorCode)
    {
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(caseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException("Expected a job.");
        await agents.CompleteExecutionAsync(claim.AutomationExecutionId, new CompleteExecutionRequest(status, status == "Passed" ? null : "AutomationFailure", errorCode, errorCode is null ? null : "failed", "AGENT-A"), CancellationToken.None);
    }

    private static async Task<(AutomationTestFixtures.Baseline Baseline, AutomationAgentService Agents, Guid CaseId, Guid VersionId)> SeedAsync(ProMaxx2.QA.Infrastructure.Persistence.QaDbContext db)
    {
        var agents = AutomationTestFixtures.AgentService(db);
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.UpdateRetryPolicyAsync(new UpdateRetryPolicyRequest(2, 0, false), null, CancellationToken.None); // no auto-retry noise
        return (baseline, agents, readyCase.AutomationCaseId, versionId);
    }

    [Fact]
    public async Task Failure_dashboard_groups_only_failed_executions_and_honours_filters()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, agents, caseId, versionId) = await SeedAsync(db);
        // order matters: AUT-UI-001 moves the case to MaintenanceRequired, so it runs last
        await RunAsync(agents, baseline, caseId, versionId, "Failed", "AUT-DB-001"); // EnvironmentFailure
        await RunAsync(agents, baseline, caseId, versionId, "Passed", null);
        await RunAsync(agents, baseline, caseId, versionId, "Failed", "AUT-UI-001"); // AutomationFailure
        var projectId = baseline.Project.ProjectId;

        var all = await agents.GetFailureBreakdownAsync(projectId, null, null, null, null, null, CancellationToken.None);
        Assert.Equal(2, all.TotalFailed);
        Assert.Equal(new[] { "AutomationFailure", "EnvironmentFailure" }, all.ByFailureType.Select(x => x.Key).OrderBy(x => x));
        Assert.Equal(2, all.ByAgent.Single(x => x.Key == "AGENT-A").Count);
        Assert.Equal(2, all.ByAutomationCase.Single().Count);

        var environmentOnly = await agents.GetFailureBreakdownAsync(projectId, null, null, null, null, "EnvironmentFailure", CancellationToken.None);
        Assert.Equal(1, environmentOnly.TotalFailed);
        var rows = await agents.ListFailedExecutionsAsync(projectId, null, null, null, null, "AutomationFailure", 50, CancellationToken.None);
        Assert.Equal("AUT-UI-001", Assert.Single(rows).ErrorCode);

        var future = await agents.GetFailureBreakdownAsync(projectId, DateTime.UtcNow.AddDays(1), null, null, null, null, CancellationToken.None);
        Assert.Equal(0, future.TotalFailed);
        var otherProject = await agents.GetFailureBreakdownAsync(Guid.NewGuid(), null, null, null, null, null, CancellationToken.None);
        Assert.Equal(0, otherProject.TotalFailed);
    }

    [Fact]
    public async Task Flaky_candidates_need_at_least_three_runs_with_two_pass_fail_transitions()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, agents, caseId, versionId) = await SeedAsync(db);
        var cases = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);
        var projectId = baseline.Project.ProjectId;

        await RunAsync(agents, baseline, caseId, versionId, "Passed", null);
        await RunAsync(agents, baseline, caseId, versionId, "Failed", "AUT-DB-001"); // environment failure keeps the case Ready
        Assert.Empty(await cases.GetFlakyCandidatesAsync(projectId, CancellationToken.None)); // only 2 runs

        await RunAsync(agents, baseline, caseId, versionId, "Passed", null);
        var candidate = Assert.Single(await cases.GetFlakyCandidatesAsync(projectId, CancellationToken.None));
        Assert.Equal(caseId, candidate.AutomationCaseId);
        Assert.Equal(3, candidate.RecentRuns);
        Assert.Equal(2, candidate.Transitions);

        await cases.QuarantineCaseAsync(caseId, projectId, new QuarantineCaseRequest("flaky", null, null), CancellationToken.None);
        Assert.Empty(await cases.GetFlakyCandidatesAsync(projectId, CancellationToken.None)); // quarantined cases are excluded
    }
}
