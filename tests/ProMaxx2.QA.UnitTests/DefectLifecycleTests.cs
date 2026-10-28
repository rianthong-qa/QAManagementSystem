using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>วงจร Defect ข้าม Build (DEFECT_BUILD_LIFECYCLE.md): กฎเปลี่ยนสถานะ, Fix/Verified Build, Reopen, ตรวจซ้ำ และ auto-create ตอนเคส Fail ซ้ำ</summary>
public sealed class DefectLifecycleTests
{
    private static readonly Guid FixBuild = Guid.NewGuid();
    private static readonly Guid RetestBuild = Guid.NewGuid();

    // ---------- กฎเปลี่ยนสถานะ ----------

    [Theory]
    [InlineData("Open", "In Progress", null, true)]
    [InlineData("In Progress", "Open", null, true)]
    [InlineData("Open", "Resolved", "Fixed", true)]
    [InlineData("Open", "Resolved", null, true)]           // Bulk/CRM ส่ง Resolved ได้โดยไม่มีผลการปิด
    [InlineData("Resolved", "Closed", null, true)]          // QA ตรวจซ้ำผ่าน
    [InlineData("Open", "Closed", "WontFix", true)]         // ปิดตรงด้วยผลที่ไม่ใช่ Fixed
    [InlineData("Open", "Closed", "Duplicate", true)]
    [InlineData("Open", "Closed", "Fixed", false)]          // แก้แล้วต้องผ่าน Resolved + ตรวจซ้ำ
    [InlineData("In Progress", "Closed", null, false)]      // ปิดตรงต้องระบุผล
    [InlineData("Closed", "Resolved", null, false)]         // ต้อง Reopen ก่อน
    [InlineData("Closed", "Rejected", null, false)]
    [InlineData("Rejected", "Resolved", null, false)]
    [InlineData("Rejected", "Open", null, true)]
    [InlineData("Resolved", "Open", null, true)]            // Reopen
    [InlineData("Closed", "In Progress", null, true)]       // Reopen
    [InlineData("Open", "Rejected", null, true)]
    public void Transition_rules(string from, string to, string? resolutionType, bool allowed) =>
        Assert.Equal(allowed, Defect.TransitionError(from, to, resolutionType) is null);

    [Fact]
    public void Resolve_then_close_records_fix_and_verified_build()
    {
        var defect = NewDefect();
        defect.TransitionTo("Resolved", FixBuild, null, null);
        Assert.Equal(("Resolved", FixBuild), (defect.Status, defect.FixedInBuildId));
        Assert.NotNull(defect.ResolvedAt);

        defect.TransitionTo("Closed", null, null, null);
        Assert.Equal(FixBuild, defect.VerifiedInBuildId); // ไม่ระบุ = ตรวจบน Fix Build
        Assert.NotNull(defect.ClosedAt);
    }

    [Fact]
    public void Reopen_counts_and_clears_verification_but_keeps_fix_build_history()
    {
        var defect = NewDefect();
        defect.SetResolution("Fixed", "Logic", null, null, null, null);
        defect.TransitionTo("Resolved", FixBuild, null, null);
        defect.TransitionTo("Closed", null, RetestBuild, null);

        defect.TransitionTo("Open", null, null, null);

        Assert.Equal(1, defect.ReopenCount);
        Assert.Null(defect.VerifiedInBuildId);
        Assert.Null(defect.ClosedAt);
        Assert.Null(defect.ResolutionType);
        Assert.Equal(FixBuild, defect.FixedInBuildId);
        defect.TransitionTo("In Progress", null, null, null);
        Assert.Equal(1, defect.ReopenCount); // Open → In Progress ไม่ใช่ Reopen
    }

    [Fact]
    public void Resolving_again_after_reopen_replaces_fix_build_and_records_retest_results()
    {
        var defect = NewDefect();
        defect.TransitionTo("Resolved", FixBuild, null, null);
        defect.TransitionTo("Open", null, null, null);
        Assert.Equal(("Fail", FixBuild), (defect.LastRetestResult, defect.FixedInBuildId));

        defect.TransitionTo("Resolved", RetestBuild, null, null);
        Assert.Equal(RetestBuild, defect.FixedInBuildId);
        defect.TransitionTo("Closed", null, null, null);
        Assert.Equal(("Pass", RetestBuild), (defect.LastRetestResult, defect.VerifiedInBuildId));

        var crmResolved = NewDefect();
        crmResolved.TransitionTo("Resolved", FixBuild, null, null);
        crmResolved.TransitionTo("Open", null, null, null);
        crmResolved.TransitionTo("Resolved", null, null, null); // ไม่มี Build ใหม่ = ไม่ระบุ ไม่ค้าง Fix Build รอบก่อน
        Assert.Null(crmResolved.FixedInBuildId);
    }

    [Fact]
    public void Direct_close_with_fixed_resolution_is_rejected_without_changing_status()
    {
        var defect = NewDefect();
        defect.SetResolution("Fixed", "Logic", null, null, null, null);
        var error = Assert.Throws<DefectTransitionException>(() => defect.TransitionTo("Closed", null, null, null));
        Assert.Contains("Resolved", error.Message);
        Assert.Equal("Open", defect.Status);
    }

    [Fact]
    public void Retest_pass_closes_on_that_build_and_fail_reopens()
    {
        var passed = NewDefect();
        passed.TransitionTo("Resolved", FixBuild, null, null);
        passed.Retest(RetestBuild, passed: true, null);
        Assert.Equal(("Closed", RetestBuild, "Pass"), (passed.Status, passed.VerifiedInBuildId, passed.LastRetestResult));

        var failed = NewDefect();
        failed.TransitionTo("Resolved", FixBuild, null, null);
        failed.Retest(RetestBuild, passed: false, null);
        Assert.Equal(("Open", 1, "Fail"), (failed.Status, failed.ReopenCount, failed.LastRetestResult));

        Assert.Throws<DefectTransitionException>(() => NewDefect().Retest(RetestBuild, true, null)); // ยังไม่ Resolved
    }

    // ---------- CRM ----------

    [Theory]
    [InlineData("Open", "Finish", "Resolved")]
    [InlineData("In Progress", "finish", "Resolved")]
    public void Crm_finish_resolves_with_latest_build(string status, string crmStatus, string expected)
    {
        var defect = NewDefect(status);
        Assert.Equal(expected, CrmDefectStatusSync.Apply(defect, crmStatus, FixBuild, Guid.NewGuid()));
        Assert.Equal(FixBuild, defect.FixedInBuildId);
    }

    [Theory]
    [InlineData("Open", "Close")]       // CRM ปิด Ticket ไม่ปิด Defect เอง — รอ QA ตรวจซ้ำ
    [InlineData("Resolved", "Close")]
    [InlineData("Resolved", "Finish")]  // ตรงอยู่แล้ว
    [InlineData("Closed", "Finish")]
    [InlineData("Rejected", "Finish")]
    [InlineData("Open", "Test")]
    public void Crm_other_cases_leave_status_unchanged(string status, string crmStatus)
    {
        var defect = NewDefect(status);
        Assert.Null(CrmDefectStatusSync.Apply(defect, crmStatus, FixBuild, Guid.NewGuid()));
        Assert.Equal(status, defect.Status);
    }

    // ---------- auto-create ตอนเคส Fail ----------

    [Fact]
    public async Task Fail_on_fix_build_reopens_resolved_defect()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cycleCaseId) = await SeedCycleAsync(db, baselineBuildIsLater: true);
        var defect = await LinkDefectAsync(db, baseline, d => d.TransitionTo("Resolved", baseline.Build.BuildId, null, null));

        var result = await AutoCreate(db).CreateFromFailAsync(cycleCaseId, FailRequest(), null, default);

        Assert.Equal((defect.DefectCode, (string?)null), (result.ReopenedDefectCode, result.CreatedDefectCode));
        var reopened = await db.Defects.AsNoTracking().SingleAsync();
        Assert.Equal(("Open", 1), (reopened.Status, reopened.ReopenCount));
        Assert.Contains(await db.DefectActivities.ToListAsync(), a => a.ActionType == "Reopened" && a.FromValue == "Resolved" && a.BuildId == baseline.Build.BuildId);
    }

    [Fact]
    public async Task Fail_on_build_older_than_fix_keeps_resolved_defect()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cycleCaseId) = await SeedCycleAsync(db, baselineBuildIsLater: false);
        var newer = await db.Builds.SingleAsync(b => b.BuildId != baseline.Build.BuildId);
        var defect = await LinkDefectAsync(db, baseline, d => d.TransitionTo("Resolved", newer.BuildId, null, null));

        var result = await AutoCreate(db).CreateFromFailAsync(cycleCaseId, FailRequest(), null, default);

        Assert.Equal((defect.DefectCode, "Resolved"), (result.ExistingDefectCode, result.ExistingDefectStatus));
        Assert.Equal("Resolved", (await db.Defects.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Fail_after_closed_creates_regression_defect_linked_to_original()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cycleCaseId) = await SeedCycleAsync(db, baselineBuildIsLater: true);
        var original = await LinkDefectAsync(db, baseline, d => { d.TransitionTo("Resolved", baseline.Build.BuildId, null, null); d.TransitionTo("Closed", null, null, null); });

        var result = await AutoCreate(db).CreateFromFailAsync(cycleCaseId, FailRequest(), null, default);

        Assert.NotNull(result.CreatedDefectCode);
        Assert.Equal(original.DefectCode, result.RegressionOfDefectCode);
        var created = await db.Defects.AsNoTracking().SingleAsync(d => d.DefectCode == result.CreatedDefectCode);
        Assert.Equal(original.DefectId, created.RegressionOfDefectId);
        Assert.Equal("Closed", (await db.Defects.AsNoTracking().SingleAsync(d => d.DefectId == original.DefectId)).Status);
    }

    [Fact]
    public async Task Fail_after_closed_as_wontfix_creates_plain_defect_not_regression()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cycleCaseId) = await SeedCycleAsync(db, baselineBuildIsLater: true);
        await LinkDefectAsync(db, baseline, d => { d.SetResolution("WontFix", null, null, null, null, null); d.TransitionTo("Closed", null, null, null); });

        var result = await AutoCreate(db).CreateFromFailAsync(cycleCaseId, FailRequest(), null, default);

        Assert.NotNull(result.CreatedDefectCode);
        Assert.Null(result.RegressionOfDefectCode);
        Assert.Null((await db.Defects.AsNoTracking().SingleAsync(d => d.DefectCode == result.CreatedDefectCode)).RegressionOfDefectId);
    }

    [Fact]
    public async Task Fail_with_open_defect_reuses_it()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, cycleCaseId) = await SeedCycleAsync(db, baselineBuildIsLater: true);
        var defect = await LinkDefectAsync(db, baseline, _ => { });

        var result = await AutoCreate(db).CreateFromFailAsync(cycleCaseId, FailRequest(), null, default);

        Assert.Equal(defect.DefectCode, result.ExistingDefectCode);
        Assert.Equal(1, await db.Defects.CountAsync());
    }

    // ---------- helpers ----------

    private static Defect NewDefect(string status = "Open")
    {
        var defect = new Defect(Guid.NewGuid(), null, null, null, "D-1", "Bug", "High", "Open", Guid.NewGuid(), null, null, null, null, null);
        switch (status)
        {
            case "Resolved": defect.TransitionTo("Resolved", FixBuild, null, null); break;
            case "Closed": defect.TransitionTo("Resolved", FixBuild, null, null); defect.TransitionTo("Closed", null, null, null); break;
            case "Rejected" or "In Progress": defect.TransitionTo(status, null, null, null); break;
        }
        return defect;
    }

    /// <summary>Cycle บน baseline.Build + อีก Build หนึ่งใน Release เดียวกัน (เก่ากว่าหรือใหม่กว่า baseline.Build)</summary>
    private static async Task<(AutomationTestFixtures.Baseline Baseline, Guid CycleCaseId)> SeedCycleAsync(QaDbContext db, bool baselineBuildIsLater)
    {
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db, automationCandidate: false);
        var otherDate = baselineBuildIsLater ? baseline.Build.BuildDate!.Value.AddDays(-3) : baseline.Build.BuildDate!.Value.AddDays(3);
        var other = new Build(baseline.Release.ReleaseId, baselineBuildIsLater ? "0" : "2", "1.0", null, null, otherDate, null, null, null);
        var cycle = new TestCycle(baseline.Project.ProjectId, baseline.Release.ReleaseId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, "CYC-001", "Cycle", "Regression", null, null, null, null, null);
        var cycleCase = new TestCycleCase(cycle.TestCycleId, baseline.TestCase.TestCaseId, baseline.TestCase.RevisionNo, "P1", 1);
        db.AddRange(other, cycle, cycleCase);
        await db.SaveChangesAsync();
        return (baseline, cycleCase.TestCycleCaseId);
    }

    private static async Task<Defect> LinkDefectAsync(QaDbContext db, AutomationTestFixtures.Baseline baseline, Action<Defect> arrange)
    {
        var defect = new Defect(baseline.Project.ProjectId, baseline.Release.ReleaseId, baseline.Build.BuildId, null, $"{baseline.Project.ProjectCode}-DEF-001", "Bug", "High", "Open", null, null, null, null, null, null);
        arrange(defect);
        db.Defects.Add(defect);
        db.DefectTestCaseLinks.Add(new DefectTestCaseLink(defect.DefectId, baseline.TestCase.TestCaseId, null));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return defect;
    }

    private static DefectAutoCreateService AutoCreate(QaDbContext db) => new(db, NullLogger<DefectAutoCreateService>.Instance);
    private static CreateExecutionRequest FailRequest() => new("Fail", "ยังพัง", null, []);
}
