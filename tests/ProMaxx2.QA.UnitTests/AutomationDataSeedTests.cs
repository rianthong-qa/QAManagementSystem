using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-DATA-003 (Seed Test Data): reusable seed-script CRUD, and the request/claim/complete
/// lifecycle for running one against an Environment. The actual SQL execution (split-and-run for Firebird, native
/// batch for SQL Server) lives entirely on the Windows Agent and is not exercised here — same split as
/// AutomationDataSnapshotTests/AutomationDataRestoreTests.</summary>
public sealed class AutomationDataSeedTests
{
    private const string SampleSql = "INSERT INTO Products (Code, Name) VALUES ('P001', 'Test Product');";

    [Fact]
    public async Task Creating_a_seed_script_defaults_it_to_active()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);

        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "Firebird", SampleSql), null, CancellationToken.None);

        Assert.True(script.IsActive);
        Assert.Equal("Firebird", script.DbKind);
        Assert.Equal(SampleSql, script.SqlScript);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Oracle")]
    public async Task Creating_a_seed_script_with_an_invalid_db_kind_throws(string dbKind)
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Bad", null, dbKind, SampleSql), null, CancellationToken.None));
    }

    [Fact]
    public async Task Updating_a_seed_script_changes_its_content()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "Firebird", SampleSql), null, CancellationToken.None);

        var updated = await service.UpdateScriptAsync(script.AutomationDataSeedScriptId, baseline.Project.ProjectId,
            new UpdateSeedScriptRequest("Baseline products v2", "updated", "Firebird", "UPDATE Products SET Name='X' WHERE Code='P001';"), null, CancellationToken.None);

        Assert.Equal("Baseline products v2", updated.Name);
        Assert.Equal("UPDATE Products SET Name='X' WHERE Code='P001';", updated.SqlScript);
    }

    [Fact]
    public async Task Deactivating_a_script_prevents_new_runs_from_being_requested()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "Firebird", SampleSql), null, CancellationToken.None);
        await service.SetScriptActiveAsync(script.AutomationDataSeedScriptId, baseline.Project.ProjectId, false, null, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None));
    }

    [Fact]
    public async Task Requesting_a_run_for_a_script_that_does_not_exist_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(Guid.NewGuid(), baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None));
    }

    [Fact]
    public async Task Requesting_a_run_creates_a_Requested_row_with_environment_and_build_metadata()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "Firebird", SampleSql), null, CancellationToken.None);

        var run = await service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);

        Assert.Equal("Requested", run.Status);
        Assert.Equal(script.Name, run.ScriptName);
        Assert.Equal(baseline.Environment.EnvironmentName, run.EnvironmentName);
        Assert.Equal(baseline.Build.BuildNumber, run.BuildNumber);
    }

    [Fact]
    public async Task Running_the_same_script_against_the_same_environment_twice_is_allowed()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "Firebird", SampleSql), null, CancellationToken.None);

        await service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);
        await service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);

        var runs = await service.ListRunsAsync(baseline.Project.ProjectId, script.AutomationDataSeedScriptId, CancellationToken.None);
        Assert.Equal(2, runs.Count); // idempotent scripts are meant to be re-run — no uniqueness constraint blocks it
    }

    [Fact]
    public async Task Claiming_the_next_seed_run_marks_it_Running_and_returns_the_script_content()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "Firebird", SampleSql), null, CancellationToken.None);
        var requested = await service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);

        var claimed = await service.ClaimNextAsync("AGENT-A", CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(requested.AutomationDataSeedRunId, claimed!.AutomationDataSeedRunId);
        Assert.Equal(SampleSql, claimed.SqlScript);
        Assert.Equal("Firebird", claimed.DbKind);
        var refreshed = await service.GetRunAsync(requested.AutomationDataSeedRunId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal("Running", refreshed.Status);
        Assert.NotNull(refreshed.AgentId);
    }

    [Fact]
    public async Task Claiming_with_no_pending_runs_returns_null()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);

        Assert.Null(await service.ClaimNextAsync("AGENT-A", CancellationToken.None));
    }

    [Fact]
    public async Task Completing_a_running_seed_run_as_Succeeded_records_rows_affected()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "Firebird", SampleSql), null, CancellationToken.None);
        var requested = await service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);
        await service.ClaimNextAsync("AGENT-A", CancellationToken.None);

        var completed = await service.CompleteRunAsync(requested.AutomationDataSeedRunId, new CompleteSeedRunRequest("Succeeded", 3, null), CancellationToken.None);

        Assert.Equal("Succeeded", completed.Status);
        Assert.Equal(3, completed.RowsAffected);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task Completing_a_running_seed_run_as_Failed_records_the_error_message()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "Firebird", SampleSql), null, CancellationToken.None);
        var requested = await service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);
        await service.ClaimNextAsync("AGENT-A", CancellationToken.None);

        var completed = await service.CompleteRunAsync(requested.AutomationDataSeedRunId, new CompleteSeedRunRequest("Failed", null, "Script is written for Firebird but this agent's DB profile is SqlServer"), CancellationToken.None);

        Assert.Equal("Failed", completed.Status);
        Assert.Contains("SqlServer", completed.ErrorMessage);
    }

    [Fact]
    public async Task A_late_duplicate_completion_report_is_ignored_idempotently()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "Firebird", SampleSql), null, CancellationToken.None);
        var requested = await service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);
        await service.ClaimNextAsync("AGENT-A", CancellationToken.None);
        await service.CompleteRunAsync(requested.AutomationDataSeedRunId, new CompleteSeedRunRequest("Succeeded", 3, null), CancellationToken.None);

        var second = await service.CompleteRunAsync(requested.AutomationDataSeedRunId, new CompleteSeedRunRequest("Failed", null, "late duplicate"), CancellationToken.None);

        Assert.Equal("Succeeded", second.Status);
        Assert.Equal(3, second.RowsAffected);
    }

    [Fact]
    public async Task Listing_scripts_filters_by_active_status()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var active = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Active one", null, "Firebird", SampleSql), null, CancellationToken.None);
        var toDeactivate = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Inactive one", null, "Firebird", SampleSql), null, CancellationToken.None);
        await service.SetScriptActiveAsync(toDeactivate.AutomationDataSeedScriptId, baseline.Project.ProjectId, false, null, CancellationToken.None);

        var activeOnly = await service.ListScriptsAsync(baseline.Project.ProjectId, true, CancellationToken.None);

        var only = Assert.Single(activeOnly);
        Assert.Equal(active.AutomationDataSeedScriptId, only.AutomationDataSeedScriptId);
    }

    [Fact]
    public async Task Getting_a_script_that_does_not_exist_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.GetScriptAsync(Guid.NewGuid(), baseline.Project.ProjectId, CancellationToken.None));
    }
}
