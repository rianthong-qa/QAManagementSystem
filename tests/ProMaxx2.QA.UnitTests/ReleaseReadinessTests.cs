using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Governance;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Identity;
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
    public async Task Hiding_a_module_does_not_lift_blockers_in_sign_off_or_dashboard()
    {
        // ซ่อน Module ได้แม้มี Defect/ผลทดสอบ — ห้ามใช้ซ่อน Module เพื่อเปลี่ยน NO-GO เป็น GO (ผล /code-review 2026-11-02)
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var b = await AutomationTestFixtures.SeedBaselineAsync(db, automationCandidate: false);
        var owner = new User("owner", "Owner", null, "hash");
        var cycle = new TestCycle(b.Project.ProjectId, b.Release.ReleaseId, b.Build.BuildId, b.Environment.TestEnvironmentId, null, "CYC-1", "CYC-1", "Smoke", null, null, null, null, owner.UserId);
        var failedP0 = new TestCycleCase(cycle.TestCycleId, b.TestCase.TestCaseId, b.TestCase.RevisionNo, "P0", 1);
        failedP0.SetStatus("Fail");
        var critical = new Defect(b.Project.ProjectId, b.Release.ReleaseId, b.Build.BuildId, b.Module.ModuleId, "DF-1", "Crash", "Critical", "Open", null, null, null, null, null, null);
        db.AddRange(owner, cycle, failedP0, critical);
        b.Module.Deactivate(null);
        await db.SaveChangesAsync();
        var access = new ProjectAccessContext { CurrentUserId = owner.UserId, AllowedProjectIds = [b.Project.ProjectId] };

        var gate = await new ReleaseSignoffRepository(db, access, new FixedOptions(new DefectGateOptions())).GetGateAsync(b.Release.ReleaseId, null, CancellationToken.None);
        var dashboard = await new DashboardRepository(db, access).GetAsync(b.Project.ProjectId, b.Release.ReleaseId, null, CancellationToken.None);

        Assert.Equal(("NO_GO", 1, 1), (gate.RecommendedDecision, gate.OpenP0, gate.OpenCriticalDefects));
        Assert.Equal((1, 1), (dashboard.OpenP0, dashboard.CriticalDefects));
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

    private sealed class FixedOptions(DefectGateOptions value) : IOptionsMonitor<DefectGateOptions>
    {
        public DefectGateOptions CurrentValue => value;
        public DefectGateOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<DefectGateOptions, string?> listener) => null;
    }

    private static Requirement Requirement(AutomationTestFixtures.Baseline b, string code) => new(b.Project.ProjectId, b.Release.ReleaseId, b.Module.ModuleId, code, code, null, null, "P1", null, null, null, true, null);
}
