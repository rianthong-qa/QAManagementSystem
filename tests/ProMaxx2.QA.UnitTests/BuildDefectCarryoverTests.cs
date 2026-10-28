using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>เฟส 3 ของ DEFECT_BUILD_LIFECYCLE.md: Defect ของ Build (เจอใน Build นี้ / ค้างจาก Build ก่อน / คิวตรวจซ้ำ), ตัวกรอง openAsOf, ตรวจซ้ำ และ Retest Cycle</summary>
public sealed class BuildDefectCarryoverTests
{
    [Fact]
    public async Task Carryover_groups_defects_relative_to_the_build()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var s = await SeedAsync(db);

        var dto = Ok(await BuildController(db, s).Carryover(s.B1.BuildId, default));

        Assert.Equal(["OPEN-B1"], Codes(dto.FoundHere));
        Assert.Equal(["OPEN-B0"], Codes(dto.CarriedOver));               // Open-B2 เจอทีหลัง ไม่ใช่ของค้าง
        Assert.Equal(["RES-FIX-B1", "RES-NOFIX"], Codes(dto.RetestQueue)); // Fix Build B2 ยังไม่ถึง Build นี้
        Assert.Equal(1, dto.CarriedOver.BySeverity["High"]);

        var earliest = Ok(await BuildController(db, s).Carryover(s.B0.BuildId, default));
        Assert.Empty(earliest.CarriedOver.Items);
        Assert.Equal(["RES-NOFIX"], Codes(earliest.RetestQueue));
    }

    [Fact]
    public async Task Carryover_is_hidden_for_other_projects()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var s = await SeedAsync(db);
        var controller = BuildController(db, s, allowedProject: Guid.NewGuid());
        Assert.IsType<NotFoundResult>((await controller.Carryover(s.B1.BuildId, default)).Result);
    }

    [Fact]
    public async Task List_open_as_of_build_includes_unclosed_defects_found_up_to_that_build()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var s = await SeedAsync(db);

        var result = (OkObjectResult)(await DefectController(db, s).List(s.ProjectId, null, s.B1.BuildId, null, false, null, null, null, null, null, null, 1, 100, "openAsOf", default)).Result!;
        var codes = ((DefectListResultDto)result.Value!).Rows.Select(x => x.DefectCode).OrderBy(x => x).ToList();

        Assert.Equal(["OPEN-B0", "OPEN-B1", "RES-FIX-B1", "RES-FIX-B2", "RES-NOFIX"], codes); // ไม่มี Closed และ Defect ที่เจอใน B2
    }

    [Fact]
    public async Task Retest_pass_closes_on_build_and_fail_reopens()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var s = await SeedAsync(db);
        var controller = DefectController(db, s);

        var passed = (DefectDto)((OkObjectResult)(await controller.Retest(s.Id("RES-FIX-B1"), new(s.B1.BuildId, true, "ผ่านทุกขั้น"), default)).Result!).Value!;
        Assert.Equal(("Closed", s.B1.BuildId, "Pass"), (passed.Status, passed.VerifiedInBuildId, passed.LastRetestResult));

        var failed = (DefectDto)((OkObjectResult)(await controller.Retest(s.Id("RES-NOFIX"), new(s.B1.BuildId, false), default)).Result!).Value!;
        Assert.Equal(("Open", 1), (failed.Status, failed.ReopenCount));
        Assert.Contains(await db.DefectActivities.ToListAsync(), a => a.ActionType == "Retested" && a.BuildId == s.B1.BuildId && a.Message.Contains("ผ่านทุกขั้น"));

        Assert.IsType<BadRequestObjectResult>((await controller.Retest(s.Id("OPEN-B0"), new(s.B1.BuildId, true), default)).Result); // ยังไม่ Resolved
        Assert.IsType<BadRequestObjectResult>((await controller.Retest(s.Id("RES-FIX-B2"), new(Guid.NewGuid(), true), default)).Result); // Build อื่น
    }

    [Fact]
    public async Task Retest_cycle_uses_test_cases_linked_to_the_retest_queue()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var s = await SeedAsync(db);
        db.DefectTestCaseLinks.Add(new DefectTestCaseLink(s.Id("RES-FIX-B1"), s.Baseline.TestCase.TestCaseId, null));
        db.DefectTestCaseLinks.Add(new DefectTestCaseLink(s.Id("RES-FIX-B2"), s.Baseline.TestCase.TestCaseId, null)); // นอกคิวของ B1 — ไม่ซ้ำเคส
        await db.SaveChangesAsync();

        var result = (RetestCycleResultDto)((OkObjectResult)(await BuildController(db, s).CreateRetestCycle(s.B1.BuildId, new(s.Baseline.Environment.TestEnvironmentId), default)).Result!).Value!;

        Assert.Equal(1, result.CaseCount);
        var cycle = await db.TestCycles.Include(c => c.Cases).SingleAsync(c => c.TestCycleId == result.TestCycleId);
        Assert.Equal((s.B1.BuildId, "Regression", $"Retest Build {s.B1.BuildNumber}"), (cycle.BuildId, cycle.CycleType, cycle.CycleName));
        Assert.Equal(s.Baseline.TestCase.TestCaseId, cycle.Cases.Single().TestCaseId);

        // คิวว่าง (ไม่มีเคสผูก) = แจ้งให้ตรวจซ้ำจากหน้า Defect
        Assert.IsType<BadRequestObjectResult>((await BuildController(db, s).CreateRetestCycle(s.B0.BuildId, new(s.Baseline.Environment.TestEnvironmentId), default)).Result);
    }

    [Fact]
    public void Carryover_queries_translate_to_sql()
    {
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer(@"Server=(localdb)\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True").Options);
        IReadOnlyList<Guid> upTo = [Guid.NewGuid(), Guid.NewGuid()];
        var carried = db.Defects.Where(d => !d.IsDeleted && Defect.UnresolvedStatuses.Contains(d.Status) && d.BuildId.HasValue && upTo.Contains(d.BuildId.Value));
        var queue = db.Defects.Where(d => d.Status == "Resolved" && (!d.FixedInBuildId.HasValue || upTo.Contains(d.FixedInBuildId.Value)));
        var ordered = queue.OrderBy(d => d.Severity == "Critical" ? 0 : d.Severity == "High" ? 1 : 2).ThenBy(d => d.CreatedAt).Take(BuildDefectsController.ItemsPerGroup);

        Assert.Contains("[FixedInBuildId]", ordered.ToQueryString());
        Assert.Contains("[BuildId]", carried.GroupBy(d => d.Severity).Select(g => new { g.Key, Count = g.Count() }).ToQueryString());
    }

    // ---------- seed ----------

    private sealed record Seed(AutomationTestFixtures.Baseline Baseline, Build B0, Build B1, Build B2, Guid UserId, Dictionary<string, Guid> Defects)
    {
        public Guid ProjectId => Baseline.Project.ProjectId;
        public Guid Id(string code) => Defects[code];
    }

    /// <summary>Release เดียว 3 Build (B0 → B1 → B2 ตาม BuildDate) + Defect ทุกแบบ</summary>
    private static async Task<Seed> SeedAsync(QaDbContext db)
    {
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db, automationCandidate: false);
        var b1 = baseline.Build;
        var b0 = new Build(baseline.Release.ReleaseId, "0", "1.0", null, null, b1.BuildDate!.Value.AddDays(-5), null, null, null);
        var b2 = new Build(baseline.Release.ReleaseId, "2", "1.0", null, null, b1.BuildDate!.Value.AddDays(5), null, null, null);
        db.AddRange(b0, b2);
        var defects = new Dictionary<string, Guid>();
        Defect Add(string code, Build found, Action<Defect>? arrange = null)
        {
            var d = new Defect(baseline.Project.ProjectId, baseline.Release.ReleaseId, found.BuildId, null, code, code, "High", "Open", null, null, null, null, null, null);
            arrange?.Invoke(d);
            db.Defects.Add(d);
            defects[code] = d.DefectId;
            return d;
        }
        Add("OPEN-B0", b0);
        Add("OPEN-B1", b1);
        Add("OPEN-B2", b2);
        Add("RES-FIX-B1", b0, d => d.TransitionTo("Resolved", b1.BuildId, null, null));
        Add("RES-FIX-B2", b0, d => d.TransitionTo("Resolved", b2.BuildId, null, null));
        Add("RES-NOFIX", b0, d => d.TransitionTo("Resolved", null, null, null));
        Add("CLOSED-B0", b0, d => { d.TransitionTo("Resolved", b0.BuildId, null, null); d.TransitionTo("Closed", null, null, null); });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new(baseline, b0, b1, b2, Guid.NewGuid(), defects);
    }

    private static List<string> Codes(BuildDefectGroupDto group) => group.Items.Select(x => x.DefectCode).OrderBy(x => x).ToList();
    private static BuildDefectCarryoverDto Ok(ActionResult<BuildDefectCarryoverDto> result) => (BuildDefectCarryoverDto)((OkObjectResult)result.Result!).Value!;

    private static ProjectAccessContext Access(Seed s, Guid? allowedProject = null) => new() { CurrentUserId = s.UserId, AllowedProjectIds = [allowedProject ?? s.ProjectId] };
    private static ControllerContext Context(Guid userId) => new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test")) } };

    private static BuildDefectsController BuildController(QaDbContext db, Seed s, Guid? allowedProject = null) =>
        new(db, Access(s, allowedProject)) { ControllerContext = Context(s.UserId) };

    private static DefectsController DefectController(QaDbContext db, Seed s) =>
        new(db, Access(s), new DefectActivityService(db), null!, null!, NullLogger<DefectsController>.Instance, null) { ControllerContext = Context(s.UserId) };
}
