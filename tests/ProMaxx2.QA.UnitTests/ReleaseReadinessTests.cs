using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Requirements;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>กฎความพร้อมของ Release ชุดเดียวที่ Dashboard / Test Summary / Release Sign-off ใช้ร่วมกัน</summary>
public sealed class ReleaseReadinessTests
{
    private static readonly DefectGateCounts Clean = new(0, 0, 0, 0, 0);

    [Fact]
    public void Unified_gate_rules()
    {
        Assert.Equal(ReleaseDecision.Go, ReleaseGate.Evaluate(new(0, 0, false, true, Clean, 95, 95)));
        Assert.Equal(ReleaseDecision.ConditionalGo, ReleaseGate.Evaluate(new(0, 1, false, true, Clean)));          // P1 ค้างไม่มี Risk = มีเงื่อนไข (เดิม Sign-off = NO_GO)
        Assert.Equal(ReleaseDecision.ConditionalGo, ReleaseGate.Evaluate(new(0, 0, false, true, Clean, 89.9m, 95))); // Coverage ต่ำกว่าเกณฑ์ (เดิม Sign-off ไม่ดู)
        Assert.Equal(ReleaseDecision.ConditionalGo, ReleaseGate.Evaluate(new(0, 0, false, true, Clean, 95, 80)));    // Pass rate ต่ำกว่าเกณฑ์
        Assert.Equal(ReleaseDecision.Go, ReleaseGate.Evaluate(new(0, 0, false, true, Clean, null, null)));         // ไม่มี Requirement/ผล = ไม่ประเมินส่วนนั้น
        // Critical ค้าง = NO-GO แม้ปิด Defect Gate (เดิม Sign-off ไม่นับ Defect เลยเมื่อปิด config)
        Assert.Equal(ReleaseDecision.NoGo, ReleaseGate.Evaluate(new(0, 0, false, true, new(1, 0, 0, 0, 0), StrictDefectGate: false)));
        // High ค้างไม่มี Risk: ปิด Gate = มีเงื่อนไข (เหมือน Dashboard เดิม), เปิด Gate = NO-GO
        Assert.Equal(ReleaseDecision.ConditionalGo, ReleaseGate.Evaluate(new(0, 0, false, true, new(0, 1, 0, 1, 0), StrictDefectGate: false)));
        Assert.Equal(ReleaseDecision.NoGo, ReleaseGate.Evaluate(new(0, 0, false, true, new(0, 1, 0, 1, 0), StrictDefectGate: true)));
        var (decision, reason) = ReleaseGate.Assess(new(0, 0, false, false, Clean));
        Assert.Equal(ReleaseDecision.NoGo, decision);
        Assert.Contains("Smoke", reason);
    }

    [Fact]
    public async Task Coverage_counts_only_ready_non_deleted_test_cases_and_smoke_is_skipped_without_policy()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var b = await AutomationTestFixtures.SeedBaselineAsync(db);
        var ready = Case(b, "TC-READY", "Ready");
        var draft = Case(b, "TC-DRAFT", "Draft");
        var deleted = Case(b, "TC-DEL", "Ready");
        var reqReady = Requirement(b, "REQ-1");
        var reqDraftOnly = Requirement(b, "REQ-2");
        var reqDeletedOnly = Requirement(b, "REQ-3");
        db.AddRange(ready, draft, deleted, reqReady, reqDraftOnly, reqDeletedOnly);
        await db.SaveChangesAsync();
        db.RequirementTestCases.AddRange(new RequirementTestCase(reqReady.RequirementId, ready.TestCaseId, "Direct"), new RequirementTestCase(reqDraftOnly.RequirementId, draft.TestCaseId, "Direct"), new RequirementTestCase(reqDeletedOnly.RequirementId, deleted.TestCaseId, "Direct"));
        deleted.SoftDelete(null);
        await db.SaveChangesAsync();

        var covered = await ReleaseReadinessQueries.CoveredRequirementIds(db, [reqReady.RequirementId, reqDraftOnly.RequirementId, reqDeletedOnly.RequirementId]).ToListAsync();
        var smoke = await ReleaseReadinessQueries.SmokeAsync(db, b.Project.ProjectId, b.Build.BuildId, CancellationToken.None);

        Assert.Equal([reqReady.RequirementId], covered);
        Assert.Equal((ReleaseReadinessQueries.SmokeNotConfigured, true), (smoke.Status, smoke.Passed));
    }

    [Fact]
    public void Readiness_queries_translate_to_sql()
    {
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer(@"Server=(localdb)\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True").Options);
        var sql = ReleaseReadinessQueries.CoveredRequirementIds(db, [Guid.NewGuid()]).ToQueryString();
        Assert.Contains("[Status]", sql);
        Assert.Contains("[IsDeleted]", sql);
    }

    private static TestCase Case(AutomationTestFixtures.Baseline b, string code, string status)
    {
        var tc = new TestCase(b.Project.ProjectId, b.Module.ModuleId, code, code, null, null, "P1", null, false, null, [new TestStepInput(1, "Action", null, "Expected")], null);
        tc.ChangeStatus(status, null);
        return tc;
    }

    private static Requirement Requirement(AutomationTestFixtures.Baseline b, string code) => new(b.Project.ProjectId, b.Release.ReleaseId, b.Module.ModuleId, code, code, null, null, "P1", null, null, null, true, null);
}
