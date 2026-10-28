using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-DATA-004 (Cleanup Test Data): "Cleanup" is the same reusable-script pipeline as AUT-DATA-003
/// ("Seed"), disambiguated by <c>ScriptType</c> — see <see cref="AutomationDataSeedTests"/> for the shared
/// CRUD/claim/complete coverage that applies identically to both types. This file covers what's specific to
/// AUT-DATA-004's AC ("cleanup สำเร็จแม้ execution ถูก cancel หรือ Agent หาย"):
/// - ScriptType itself (create/filter as "Cleanup").
/// - <see cref="AutomationDataSeedRun.ReclaimIfStale"/> — tested directly against the domain entity (not through the
///   repository's claim call) because the repository sweep drives off the real wall-clock (<c>DateTime.UtcNow</c>),
///   which cannot be fast-forwarded in a test; the domain method is where all of the actual reclaim logic lives, so
///   testing it directly with a synthetic "now" gives full, deterministic coverage without waiting on a real clock.
/// - Independence from AutomationExecution — nothing on <see cref="AutomationDataSeedRun"/> references an execution
///   at all, so cancelling one is structurally incapable of affecting a cleanup run in progress; there is no wiring
///   to test here because there is no wiring at all, by design.</summary>
public sealed class AutomationDataCleanupTests
{
    private const string CleanupSql = "DELETE FROM Products WHERE Code = 'P001';";

    [Fact]
    public async Task Creating_a_script_with_ScriptType_Cleanup_is_stored_as_such()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);

        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Cleanup products", null, "Cleanup", "Firebird", CleanupSql), null, CancellationToken.None);

        Assert.Equal("Cleanup", script.ScriptType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Purge")]
    public async Task Creating_a_script_with_an_invalid_ScriptType_throws(string scriptType)
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Bad", null, scriptType, "Firebird", CleanupSql), null, CancellationToken.None));
    }

    [Fact]
    public async Task Listing_scripts_filters_by_ScriptType_so_cleanup_and_seed_scripts_do_not_mix()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var cleanup = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Cleanup products", null, "Cleanup", "Firebird", CleanupSql), null, CancellationToken.None);
        await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "Seed", "Firebird", "INSERT INTO Products VALUES ('P001');"), null, CancellationToken.None);

        var cleanupOnly = await service.ListScriptsAsync(baseline.Project.ProjectId, "Cleanup", null, CancellationToken.None);

        var only = Assert.Single(cleanupOnly);
        Assert.Equal(cleanup.AutomationDataSeedScriptId, only.AutomationDataSeedScriptId);
    }

    [Fact]
    public async Task A_cleanup_run_goes_through_the_same_claim_complete_lifecycle_as_a_seed_run()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Cleanup products", null, "Cleanup", "Firebird", CleanupSql), null, CancellationToken.None);
        var requested = await service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);

        var claimed = await service.ClaimNextAsync("AGENT-A", CancellationToken.None);
        var completed = await service.CompleteRunAsync(requested.AutomationDataSeedRunId, new CompleteSeedRunRequest("Succeeded", 1, null), CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(CleanupSql, claimed!.SqlScript);
        Assert.Equal("Cleanup", completed.ScriptType);
        Assert.Equal("Succeeded", completed.Status);
        Assert.Equal(1, completed.RowsAffected);
    }

    [Fact]
    public async Task ReclaimIfStale_reverts_a_running_run_to_Requested_after_the_agent_disappears()
    {
        var run = new AutomationDataSeedRun(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);
        var agentId = Guid.NewGuid();
        run.Claim(agentId);
        var startedAt = run.StartedAt!.Value;

        run.ReclaimIfStale(startedAt.AddMinutes(31), TimeSpan.FromMinutes(30));

        Assert.Equal("Requested", run.Status);
        Assert.Null(run.AgentId);
        Assert.Null(run.StartedAt);
    }

    [Fact]
    public async Task ReclaimIfStale_does_nothing_while_still_within_the_threshold()
    {
        var run = new AutomationDataSeedRun(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);
        var agentId = Guid.NewGuid();
        run.Claim(agentId);
        var startedAt = run.StartedAt!.Value;

        run.ReclaimIfStale(startedAt.AddMinutes(10), TimeSpan.FromMinutes(30));

        Assert.Equal("Running", run.Status);
        Assert.Equal(agentId, run.AgentId);
    }

    [Theory]
    [InlineData("Requested")]
    [InlineData("Succeeded")]
    [InlineData("Failed")]
    public void ReclaimIfStale_does_nothing_to_a_run_that_is_not_Running(string terminalStatus)
    {
        var run = new AutomationDataSeedRun(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);
        if (terminalStatus != "Requested")
        {
            run.Claim(Guid.NewGuid());
            if (terminalStatus == "Succeeded") run.Complete(1); else run.Fail("boom");
        }

        run.ReclaimIfStale(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(30));

        Assert.Equal(terminalStatus, run.Status);
    }

    [Fact]
    public void A_cleanup_run_has_no_reference_to_any_AutomationExecution_so_cancelling_one_cannot_affect_it()
    {
        // Structural assertion, not a behavioral one: AutomationDataSeedRun's public surface is checked for any
        // Execution-shaped member. There being none is exactly why "execution ถูก cancel" cannot touch a cleanup run
        // — there is no code path connecting the two at all, so nothing needed to be built (or tested) to prevent it.
        var members = typeof(AutomationDataSeedRun).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(members, name => name.Contains("Execution", StringComparison.OrdinalIgnoreCase));
    }
}
