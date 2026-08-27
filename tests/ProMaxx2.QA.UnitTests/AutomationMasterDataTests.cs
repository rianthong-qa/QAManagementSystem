using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-DATA-005 (Master Data Setup Flow): "MasterData" is a third <c>ScriptType</c> on the same
/// Seed/Cleanup pipeline (see <see cref="AutomationDataCleanupTests"/> for the shared CRUD/claim/complete coverage
/// that applies identically to every ScriptType). This file covers what's specific to AUT-DATA-005's AC ("เตรียม
/// สินค้า/ราคา/โปรโมชั่นก่อน POS scenario ผ่าน UI หรือ approved DB seed"), read as: a MasterData script must be
/// reviewed and approved before it can be run —
/// - Creating/approving/rejecting a MasterData script and the resulting ApprovalStatus/ReviewedBy/ReviewedAt/RejectionReason.
/// - RequestRunAsync blocks an unapproved (Pending/Rejected) MasterData script and allows an Approved one.
/// - Seed/Cleanup scripts are never gated on ApprovalStatus, regardless of value.
/// - Editing an Approved script's SQL resets it back to Pending (an approval is a sign-off on specific content).</summary>
public sealed class AutomationMasterDataTests
{
    private const string MasterDataSql = "MERGE INTO Products USING (SELECT 'P001' AS Code FROM RDB$DATABASE) src ON Products.Code = src.Code WHEN NOT MATCHED THEN INSERT (Code, Name) VALUES ('P001', 'Test Product');";

    [Fact]
    public async Task Creating_a_MasterData_script_defaults_to_ApprovalStatus_Pending()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);

        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "MasterData", "Firebird", MasterDataSql), null, CancellationToken.None);

        Assert.Equal("MasterData", script.ScriptType);
        Assert.Equal("Pending", script.ApprovalStatus);
        Assert.Null(script.ReviewedBy);
        Assert.Null(script.ReviewedAt);
    }

    [Fact]
    public async Task Requesting_a_run_of_a_Pending_MasterData_script_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "MasterData", "Firebird", MasterDataSql), null, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None));
    }

    [Fact]
    public async Task Approving_a_MasterData_script_then_allows_a_run_to_be_requested()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "MasterData", "Firebird", MasterDataSql), null, CancellationToken.None);
        var approverId = Guid.NewGuid();

        var approved = await service.ApproveScriptAsync(script.AutomationDataSeedScriptId, baseline.Project.ProjectId, approverId, CancellationToken.None);
        var run = await service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);

        Assert.Equal("Approved", approved.ApprovalStatus);
        Assert.Equal(approverId, approved.ReviewedBy);
        Assert.NotNull(approved.ReviewedAt);
        Assert.Equal("Requested", run.Status);
    }

    [Fact]
    public async Task Rejecting_a_MasterData_script_records_the_reason_and_still_blocks_a_run()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "MasterData", "Firebird", MasterDataSql), null, CancellationToken.None);
        var reviewerId = Guid.NewGuid();

        var rejected = await service.RejectScriptAsync(script.AutomationDataSeedScriptId, baseline.Project.ProjectId, new RejectSeedScriptRequest("ราคาผิด"), reviewerId, CancellationToken.None);

        Assert.Equal("Rejected", rejected.ApprovalStatus);
        Assert.Equal(reviewerId, rejected.ReviewedBy);
        Assert.Equal("ราคาผิด", rejected.RejectionReason);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None));
    }

    [Theory]
    [InlineData("Seed")]
    [InlineData("Cleanup")]
    public async Task Seed_and_Cleanup_scripts_are_never_gated_on_ApprovalStatus(string scriptType)
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, scriptType, "Firebird", MasterDataSql), null, CancellationToken.None);

        Assert.Equal("Pending", script.ApprovalStatus);
        var run = await service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None);
        Assert.Equal("Requested", run.Status);
    }

    [Fact]
    public async Task Editing_an_Approved_MasterData_script_resets_it_back_to_Pending()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var script = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "MasterData", "Firebird", MasterDataSql), null, CancellationToken.None);
        await service.ApproveScriptAsync(script.AutomationDataSeedScriptId, baseline.Project.ProjectId, Guid.NewGuid(), CancellationToken.None);

        var updated = await service.UpdateScriptAsync(script.AutomationDataSeedScriptId, baseline.Project.ProjectId,
            new UpdateSeedScriptRequest("Baseline products v2", null, "MasterData", "Firebird", MasterDataSql + " -- v2"), null, CancellationToken.None);

        Assert.Equal("Pending", updated.ApprovalStatus);
        Assert.Null(updated.ReviewedBy);
        Assert.Null(updated.ReviewedAt);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RequestRunAsync(baseline.Project.ProjectId, new RequestSeedRunRequest(script.AutomationDataSeedScriptId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId), null, CancellationToken.None));
    }

    [Fact]
    public void Domain_Approve_and_Reject_are_mutually_overriding_and_clear_the_other_states_fields()
    {
        var entity = new AutomationDataSeedScript(Guid.NewGuid(), "MD", null, "MasterData", "Firebird", MasterDataSql, null);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        entity.Reject(userA, "not ready");
        Assert.Equal("Rejected", entity.ApprovalStatus);
        Assert.Equal("not ready", entity.RejectionReason);

        entity.Approve(userB);
        Assert.Equal("Approved", entity.ApprovalStatus);
        Assert.Equal(userB, entity.ReviewedBy);
        Assert.Null(entity.RejectionReason);
    }

    [Fact]
    public async Task Listing_scripts_filters_MasterData_separately_from_Seed_and_Cleanup()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.SeedService(db);
        var masterData = await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Baseline products", null, "MasterData", "Firebird", MasterDataSql), null, CancellationToken.None);
        await service.CreateScriptAsync(baseline.Project.ProjectId, new CreateSeedScriptRequest("Cleanup products", null, "Cleanup", "Firebird", "DELETE FROM Products WHERE Code='P001';"), null, CancellationToken.None);

        var masterDataOnly = await service.ListScriptsAsync(baseline.Project.ProjectId, "MasterData", null, CancellationToken.None);

        var only = Assert.Single(masterDataOnly);
        Assert.Equal(masterData.AutomationDataSeedScriptId, only.AutomationDataSeedScriptId);
        Assert.Equal("Pending", only.ApprovalStatus);
    }
}
