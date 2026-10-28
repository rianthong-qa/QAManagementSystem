using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Governance;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>เฟส 4 ของ DEFECT_BUILD_LIFECYCLE.md: นิยามกลุ่มสถานะเดียว, Release Gate นับ Defect (เปิดด้วย Defects:GateEnabled) และ Known Issues จาก Defect</summary>
public sealed class DefectReleaseGateTests
{
    [Fact]
    public void Status_groups_partition_all_statuses()
    {
        Assert.Equal(Defect.Statuses.Order(), DefectStatusGroups.Unresolved.Concat(DefectStatusGroups.AwaitingVerification).Concat(DefectStatusGroups.Done).Order());
        Assert.Equal(DefectStatusGroups.Unresolved.Concat(DefectStatusGroups.AwaitingVerification), DefectStatusGroups.NotDone);
        Assert.All(DefectStatusGroups.KnownIssueResolutions, x => Assert.Contains(x, Defect.ResolutionTypes));
    }

    [Fact]
    public void Defect_counts_classify_blockers()
    {
        var counts = DefectGateCounts.From([
            ("Critical", "Open", true),        // Critical ค้าง — Risk ปลดไม่ได้
            ("High", "In Progress", false),    // Blocker ไม่มี Risk
            ("High", "Open", true),            // Blocker มี Risk
            ("Medium", "Resolved", false),     // ยังไม่ตรวจซ้ำ — Blocker ทุก Severity
            ("Critical", "Resolved", true),    // ยังไม่ตรวจซ้ำ มี Risk
            ("Medium", "Open", false),         // ไม่ใช่ Blocker
            ("High", "Closed", false),
        ]);
        Assert.Equal(new DefectGateCounts(OpenCritical: 1, OpenHigh: 2, UnverifiedResolved: 2, BlockersWithoutRisk: 2, BlockersWithRisk: 2), counts);
    }

    [Theory]
    [InlineData(0, 0, 0, ReleaseDecision.Go)]
    [InlineData(1, 0, 0, ReleaseDecision.NoGo)]          // Critical ค้าง
    [InlineData(0, 1, 0, ReleaseDecision.NoGo)]          // High / Resolved ที่ไม่มี Risk
    [InlineData(0, 0, 1, ReleaseDecision.ConditionalGo)] // ทุก Blocker มี Risk
    [InlineData(0, 1, 1, ReleaseDecision.NoGo)]
    public void Gate_uses_defects_only_when_enabled(int critical, int withoutRisk, int withRisk, ReleaseDecision expected)
    {
        var defects = new DefectGateCounts(critical, 0, 0, withoutRisk, withRisk);
        Assert.Equal(expected, ReleaseGate.Evaluate(new(0, 0, false, true, defects)));
        Assert.Equal(ReleaseDecision.Go, ReleaseGate.Evaluate(new(0, 0, false, true, null))); // ปิด config = ไม่นับ Defect
    }

    [Fact]
    public void Gate_keeps_existing_rules_before_defects()
    {
        var clean = new DefectGateCounts(0, 0, 0, 0, 0);
        Assert.Equal(ReleaseDecision.NoGo, ReleaseGate.Evaluate(new(0, 0, true, false, clean)));  // Smoke ไม่ผ่าน
        Assert.Equal(ReleaseDecision.NoGo, ReleaseGate.Evaluate(new(1, 0, true, true, clean)));   // P0
        Assert.Equal(ReleaseDecision.ConditionalGo, ReleaseGate.Evaluate(new(0, 1, true, true, clean)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Release_gate_always_reports_defect_counts(bool enabled)
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var s = await SeedAsync(db);

        var gate = await Repository(db, s, enabled).GetGateAsync(s.Baseline.Release.ReleaseId, null, default);

        // CRIT = Critical ค้าง, HIGH-RISK = High ค้างที่มี Risk, RES = Resolved ยังไม่ตรวจซ้ำ (ไม่มี Risk); Draft Risk / Defect ที่ลบ / ปิดแล้ว ไม่นับ
        Assert.Equal((enabled, 1, 1, 1, 1, 1), (gate.DefectGateEnabled, gate.OpenCriticalDefects, gate.OpenHighDefects, gate.UnverifiedResolvedDefects, gate.DefectBlockersWithoutRisk, gate.DefectBlockersWithRisk));
    }

    [Fact]
    public async Task Known_issues_come_from_deferred_wont_fix_risk_accepted_defects_and_builds()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var s = await SeedAsync(db);

        var issues = await new KnownIssueRepository(db).ListAsync(s.Baseline.Release.ReleaseId, default);

        Assert.Equal(["DEFERRED", "HIGH-RISK", "WONTFIX", s.Baseline.Build.BuildNumber], issues.Select(x => x.Reference)); // Severity แล้วรหัส, Build ต่อท้าย
        Assert.Contains("RISK-1", issues[1].Reason);
        Assert.Equal(("Build", "ทดสอบไม่ได้บน iOS 15"), (issues[^1].Source, issues[^1].Title));
    }

    [Fact]
    public void Gate_and_known_issue_queries_translate_to_sql()
    {
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer(@"Server=(localdb)\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True").Options);
        var releaseId = Guid.NewGuid();
        List<Guid> riskIds = [Guid.NewGuid()];
        var gate = db.Defects.Where(d => d.ReleaseId == releaseId && !d.IsDeleted && DefectStatusGroups.NotDone.Contains(d.Status)).Select(d => new { d.DefectId, d.Severity, d.Status });
        var known = db.Defects.Where(d => d.ReleaseId == releaseId && !d.IsDeleted
            && ((d.Status == "Closed" && d.ResolutionType != null && DefectStatusGroups.KnownIssueResolutions.Contains(d.ResolutionType))
                || (DefectStatusGroups.NotDone.Contains(d.Status) && riskIds.Contains(d.DefectId))))
            .OrderBy(d => d.Severity == "Critical" ? 0 : d.Severity == "High" ? 1 : 2).ThenBy(d => d.DefectCode).Take(KnownIssueRepository.MaxDefects);

        Assert.Contains("[Status]", gate.ToQueryString());
        Assert.Contains("[ResolutionType]", known.ToQueryString());
    }

    // ---------- seed ----------

    private sealed record Seed(AutomationTestFixtures.Baseline Baseline, Guid UserId);

    /// <summary>Release เดียว: Critical ค้าง, High ค้าง 1 (มี Risk), Resolved ไม่มี Risk, ปิดด้วย Deferred / Won't Fix / Fixed, Build มี Known Issues</summary>
    private static async Task<Seed> SeedAsync(QaDbContext db)
    {
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db, automationCandidate: false);
        var build = baseline.Build;
        Defect Add(string code, string severity, Action<Defect>? arrange = null)
        {
            var d = new Defect(baseline.Project.ProjectId, baseline.Release.ReleaseId, build.BuildId, null, code, code, severity, "Open", null, null, null, null, null, null);
            arrange?.Invoke(d);
            db.Defects.Add(d);
            return d;
        }
        void CloseAs(Defect d, string resolution) { d.SetResolution(resolution, null, null, null, null, null); d.TransitionTo("Closed", null, null, null); }
        Add("CRIT", "Critical");
        var risky = Add("HIGH-RISK", "High");
        Add("RES", "Medium", d => d.TransitionTo("Resolved", build.BuildId, null, null));
        Add("DEFERRED", "High", d => CloseAs(d, "Deferred"));
        Add("WONTFIX", "Low", d => CloseAs(d, "WontFix"));
        Add("FIXED", "High", d => { d.TransitionTo("Resolved", build.BuildId, null, null); d.TransitionTo("Closed", null, null, null); });
        Add("GONE", "Critical", d => d.SoftDelete(null));

        var risk = new RiskAcceptance(baseline.Project.ProjectId, baseline.Release.ReleaseId, risky.DefectId, "รับความเสี่ยง", "issue", "Medium", "Low", null, null, null, null, null);
        risk.AssignCode("RISK-1"); risk.Submit(null); risk.Approve(null, null);
        var draft = new RiskAcceptance(baseline.Project.ProjectId, baseline.Release.ReleaseId, null, "ยังไม่อนุมัติ", "issue", "Medium", "Low", null, null, null, null, null);
        db.RiskAcceptances.AddRange(risk, draft);
        build.Update(build.ApplicationVersion, build.PackageVersion, build.CommitReference, build.BuildDate, build.ChangeNotes, " ทดสอบไม่ได้บน iOS 15 ");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new(baseline, Guid.NewGuid());
    }

    private static ReleaseSignoffRepository Repository(QaDbContext db, Seed s, bool enabled) =>
        new(db, new ProjectAccessContext { CurrentUserId = s.UserId, AllowedProjectIds = [s.Baseline.Project.ProjectId] }, new FixedOptions(new DefectGateOptions { GateEnabled = enabled }));

    private sealed class FixedOptions(DefectGateOptions value) : IOptionsMonitor<DefectGateOptions>
    {
        public DefectGateOptions CurrentValue => value;
        public DefectGateOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<DefectGateOptions, string?> listener) => null;
    }
}
