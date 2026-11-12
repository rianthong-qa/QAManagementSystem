using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Flow ย้อนกลับของ Defect ที่แจ้งเอง: Test Case → Suite กลาง → Cycle "พบนอกแผน" ของ Build → ผล Fail (ให้ Dashboard เห็นผลทดสอบ)</summary>
public sealed class DefectUnplannedExecutionTests
{
    private static readonly Guid Tester = Guid.NewGuid();

    [Fact]
    public async Task Create_records_fail_in_shared_unplanned_cycle_of_the_build()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);

        // Dashboard แสดงชื่อผู้ทดสอบ จึงต้องมี User จริง
        var user = new User("tester", "Tester", null, "hash");
        db.Add(user);
        await db.SaveChangesAsync();
        var tester = user.UserId;

        var first = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "Critical", tester: tester);
        var second = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "Low", tester: tester);

        // Suite และ Cycle ใช้ร่วมกันทั้ง 2 Defect — ไม่สร้าง 1 ต่อ 1
        var suite = await db.TestSuites.SingleAsync();
        Assert.Equal(("AUT-TS-DEFECT", "Defect Regression", true), (suite.SuiteCode, suite.SuiteName, suite.IsActive));
        Assert.Equal([1, 2], await db.TestSuiteCases.OrderBy(x => x.SortOrder).Select(x => x.SortOrder).ToListAsync());
        var cycle = await db.TestCycles.SingleAsync();
        Assert.Equal((seed.Release.ReleaseId, seed.Build.BuildId, seed.Environment.TestEnvironmentId, suite.TestSuiteId), (cycle.ReleaseId, cycle.BuildId, cycle.EnvironmentId, cycle.TestSuiteId!.Value));
        Assert.Equal(("AUT-CYC-001", "พบนอกแผน · 1", "Ad hoc", "InProgress"), (cycle.CycleCode, cycle.CycleName, cycle.CycleType, cycle.Status));
        Assert.Equal(("AUT-CYC-001", null), (first.AutoExecutionCycleCode, first.AutoExecutionSkipped));
        Assert.Equal("AUT-CYC-001", second.AutoExecutionCycleCode);

        var cycleCases = await db.TestCycleCases.OrderBy(x => x.ExecutionOrder).ToListAsync();
        Assert.Equal([("P0", "Fail", 1), ("P3", "Fail", 2)], cycleCases.Select(x => (x.Priority!, x.CurrentStatus, x.ExecutionOrder)));
        var execution = await db.TestExecutions.Include(x => x.StepResults).SingleAsync(x => x.TestCycleCaseId == cycleCases[0].TestCycleCaseId);
        Assert.Equal(("Fail", tester, seed.Build.BuildId, "Total is 400"), (execution.Status, execution.TesterUserId, execution.BuildId, execution.ActualResult));
        Assert.Contains(first.DefectCode, execution.Comment);
        // ขั้นสุดท้าย = จุดที่ผลไม่ตรง
        Assert.Equal([(1, "Pass"), (2, "Fail")], execution.StepResults.OrderBy(x => x.StepNo).Select(x => (x.StepNo, x.Status)));

        // Dashboard ของ Build เห็นผลทดสอบแล้ว (เดิมเป็น NO DATA เพราะไม่มี Cycle)
        var summary = await new DashboardRepository(db, new ProjectAccessContext()).GetAsync(seed.Project.ProjectId, seed.Release.ReleaseId, seed.Build.BuildId, CancellationToken.None);
        Assert.Equal((2, 2, 0, 1), (summary.TotalCases, summary.ExecutedCases, summary.PassedCases, summary.OpenP0));
        Assert.Equal("NO-GO", summary.RecommendedDecision);

        // Suite และ Cycle ที่ระบบสร้างมี Audit Log เหมือนการสร้างทางอื่น — สร้างครั้งเดียว จึงมีอย่างละ 1 รายการ
        Assert.Equal(["TestCycle", "TestSuite"], await db.AuditLogs.Where(x => x.Action == "Created").Select(x => x.EntityType).OrderBy(x => x).ToListAsync());
    }

    [Fact]
    public async Task Closed_unplanned_cycle_is_not_reused_and_other_build_gets_its_own()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);
        var later = new Build(seed.Release.ReleaseId, "2", "1.0", null, null, DateTime.UtcNow.AddDays(1), null, null, null);
        db.Builds.Add(later);
        await db.SaveChangesAsync();

        await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "High");
        (await db.TestCycles.SingleAsync()).ChangeStatus("Closed");
        await db.SaveChangesAsync();
        var afterClose = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "High");
        var otherBuild = await Create(db, seed.Project.ProjectId, later.BuildId, seed.Module.ModuleId, "High");

        Assert.Equal("AUT-CYC-002", afterClose.AutoExecutionCycleCode);
        Assert.Equal("AUT-CYC-003", otherBuild.AutoExecutionCycleCode);
        Assert.Equal("พบนอกแผน · 2", (await db.TestCycles.SingleAsync(x => x.BuildId == later.BuildId)).CycleName);
        Assert.Equal(1, await db.TestSuites.CountAsync());
        Assert.Equal(3, await db.TestExecutions.CountAsync());
    }

    [Fact]
    public async Task Without_build_or_environment_defect_is_created_and_reason_returned()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);

        var noBuild = await Create(db, seed.Project.ProjectId, null, seed.Module.ModuleId, "High");
        seed.Environment.Update("QA", null, false);
        await db.SaveChangesAsync();
        var noEnvironment = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "High");

        Assert.Null(noBuild.AutoExecutionCycleCode);
        Assert.Contains("ไม่ได้เลือก Build", noBuild.AutoExecutionSkipped);
        Assert.Contains("Environment", noEnvironment.AutoExecutionSkipped);
        Assert.Equal(2, await db.Defects.CountAsync());
        Assert.Equal(2, await db.DefectTestCaseLinks.CountAsync());
        Assert.False(await db.TestCycles.AnyAsync());
        Assert.False(await db.TestSuites.AnyAsync());
        Assert.False(await db.TestExecutions.AnyAsync());
    }

    [Fact]
    public async Task Closed_release_inactive_build_or_no_execution_permission_skips_with_reason()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);

        // Developer: แจ้ง Defect ได้ (DEFECT.EDIT) แต่ไม่มี EXECUTION.RUN — ไม่ถูกบันทึกเป็นผู้ทดสอบ
        var noPermission = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "High", canRunExecution: false);
        var inactive = new Build(seed.Release.ReleaseId, "2", "1.0", null, null, DateTime.UtcNow.AddDays(1), null, null, null);
        db.Builds.Add(inactive);
        await db.SaveChangesAsync();
        inactive.Deactivate();
        await db.SaveChangesAsync();
        var inactiveBuild = await Create(db, seed.Project.ProjectId, inactive.BuildId, seed.Module.ModuleId, "High");
        seed.Release.ChangeStatus("Released", null);
        await db.SaveChangesAsync();
        var closedRelease = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "High");

        Assert.Contains("EXECUTION.RUN", noPermission.AutoExecutionSkipped);
        Assert.Contains("ปิดใช้งาน", inactiveBuild.AutoExecutionSkipped);
        Assert.Contains("Released", closedRelease.AutoExecutionSkipped);
        Assert.Equal(3, await db.Defects.CountAsync());
        Assert.Equal(3, await db.DefectTestCaseLinks.CountAsync());
        Assert.False(await db.TestCycles.AnyAsync());
        Assert.False(await db.TestSuites.AnyAsync());
        Assert.False(await db.TestExecutions.AnyAsync());
    }

    [Fact]
    public async Task Deleting_defect_withdraws_its_automatic_fail()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);
        var onlyAuto = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "Critical");
        var rerun = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "High");
        // เคสที่ 2 มีผลที่ QA รันซ้ำเองใน Execution Workspace ด้วย — ผลนั้นต้องอยู่ต่อ
        var rerunCase = await db.TestCycleCases.OrderBy(x => x.ExecutionOrder).LastAsync();
        db.TestExecutions.Add(new ProMaxx2.QA.Domain.Execution.TestExecution(rerunCase.TestCycleCaseId, 2, seed.Build.BuildId, seed.Environment.TestEnvironmentId, Tester, "Blocked", null, "รันซ้ำเอง", []));
        rerunCase.SetStatus("Blocked");
        await db.SaveChangesAsync();

        Assert.IsType<NoContentResult>(await Controller(db).Delete(onlyAuto.DefectId, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await Controller(db).Bulk(new BulkDefectRequest([rerun.DefectId], Delete: true), CancellationToken.None));

        // เคสที่มีแต่ผลอัตโนมัติ = ออกจาก Cycle; เคสที่มีผลอื่น = ลบเฉพาะผลอัตโนมัติ สถานะตามผลที่เหลือ
        var remaining = await db.TestCycleCases.Include(x => x.Executions).SingleAsync();
        Assert.Equal((rerunCase.TestCycleCaseId, "Blocked"), (remaining.TestCycleCaseId, remaining.CurrentStatus));
        Assert.Equal([(1, true), (2, false)], remaining.Executions.OrderBy(x => x.ExecutionNo).Select(x => (x.ExecutionNo, x.IsDeleted)));
        Assert.Equal(2, await db.TestExecutions.CountAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.EntityType == "TestExecution" && x.Action == "Deleted"));
    }

    [Fact]
    public async Task Leaving_resolved_records_the_retest_result_in_the_cycle_of_the_tested_build()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);
        var fixBuild = await AddBuild(db, seed.Release.ReleaseId, "2");
        var sameBuild = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "Critical");
        var laterBuild = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "High");
        var wontFix = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "Low");

        // ตรวจซ้ำผ่านบน Build เดียวกับที่พบ = ผลรอบที่ 2 ในเคสเดิม เคสไม่ค้างเป็น Fail
        await Controller(db).ChangeStatus(sameBuild.DefectId, new ChangeDefectStatusRequest("Resolved", FixedInBuildId: seed.Build.BuildId), CancellationToken.None);
        var closed = Dto(await Controller(db).ChangeStatus(sameBuild.DefectId, new ChangeDefectStatusRequest("Closed", VerifiedInBuildId: seed.Build.BuildId), CancellationToken.None));
        Assert.Equal("AUT-CYC-001", closed.AutoExecutionCycleCode);
        var first = await db.TestCycleCases.Include(x => x.Executions).ThenInclude(x => x.StepResults).OrderBy(x => x.ExecutionOrder).FirstAsync();
        Assert.Equal("Pass", first.CurrentStatus);
        Assert.Equal([(1, "Fail"), (2, "Pass")], first.Executions.OrderBy(x => x.ExecutionNo).Select(x => (x.ExecutionNo, x.Status)));
        Assert.All(first.Executions.Single(x => x.ExecutionNo == 2).StepResults, x => Assert.Equal("Pass", x.Status));

        // ตรวจซ้ำบน Fix Build ที่ยังไม่มี Cycle: ไม่ผ่าน (Reopen) แล้วผ่าน = Cycle พบนอกแผนของ Build นั้น 1 รอบ เคสเดียว 2 ผล
        await Controller(db).ChangeStatus(laterBuild.DefectId, new ChangeDefectStatusRequest("Resolved", FixedInBuildId: fixBuild.BuildId), CancellationToken.None);
        var reopened = Dto(await Controller(db).ChangeStatus(laterBuild.DefectId, new ChangeDefectStatusRequest("Open"), CancellationToken.None));
        Assert.Equal("AUT-CYC-002", reopened.AutoExecutionCycleCode);
        await Controller(db).ChangeStatus(laterBuild.DefectId, new ChangeDefectStatusRequest("Resolved", FixedInBuildId: fixBuild.BuildId), CancellationToken.None);
        var retested = Dto(await Controller(db).Retest(laterBuild.DefectId, new RetestDefectRequest(fixBuild.BuildId, true), CancellationToken.None));
        Assert.Equal("AUT-CYC-002", retested.AutoExecutionCycleCode);
        var onFixBuild = await db.TestCycleCases.Include(x => x.Executions).SingleAsync(x => x.Cycle.BuildId == fixBuild.BuildId);
        Assert.Equal("Pass", onFixBuild.CurrentStatus);
        Assert.Equal(["Fail", "Pass"], onFixBuild.Executions.OrderBy(x => x.ExecutionNo).Select(x => x.Status));

        // ปิดด้วยผลอื่น (Won't Fix) ไม่ใช่การตรวจซ้ำ — ไม่บันทึกผล
        await Controller(db).ChangeStatus(wontFix.DefectId, new ChangeDefectStatusRequest("Resolved", new DefectResolutionRequest("WontFix"), seed.Build.BuildId), CancellationToken.None);
        var notRetest = Dto(await Controller(db).ChangeStatus(wontFix.DefectId, new ChangeDefectStatusRequest("Closed"), CancellationToken.None));
        Assert.Equal((null, null), (notRetest.AutoExecutionCycleCode, notRetest.AutoExecutionSkipped));
        Assert.Equal(6, await db.TestExecutions.CountAsync());
        // ผลตรวจซ้ำไม่เพิ่ม Test Case เข้า Suite ซ้ำ
        Assert.Equal(3, await db.TestSuiteCases.CountAsync());
    }

    [Fact]
    public async Task Bulk_close_of_resolved_defects_shares_one_new_unplanned_cycle()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);
        var a = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "High", recordExecution: false);
        var b = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "High", recordExecution: false);
        Assert.IsType<OkObjectResult>(await Controller(db).Bulk(new BulkDefectRequest([a.DefectId, b.DefectId], "Resolved", FixedInBuildId: seed.Build.BuildId), CancellationToken.None));

        Assert.IsType<OkObjectResult>(await Controller(db).Bulk(new BulkDefectRequest([a.DefectId, b.DefectId], "Closed"), CancellationToken.None));

        var cycle = await db.TestCycles.SingleAsync();
        Assert.Equal("AUT-CYC-001", cycle.CycleCode);
        Assert.Equal(["1:Pass", "2:Pass"], (await db.TestCycleCases.OrderBy(x => x.ExecutionOrder).ToListAsync()).Select(x => $"{x.ExecutionOrder}:{x.CurrentStatus}"));
        Assert.Equal(2, await db.TestExecutions.CountAsync());
        Assert.Equal(1, await db.TestSuites.CountAsync());
    }

    [Fact]
    public async Task Fail_can_be_recorded_later_for_a_defect_created_without_it()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);
        var defect = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "High", recordExecution: false);
        var noBuild = await Create(db, seed.Project.ProjectId, null, seed.Module.ModuleId, "High", recordExecution: false);
        async Task<bool> HasResult(Guid id) => ((IReadOnlyList<DefectTestCaseDto>)Assert.IsType<OkObjectResult>((await Controller(db).TestCases(id, CancellationToken.None)).Result).Value!).Single().HasResultInFoundBuild;
        Assert.False(await HasResult(defect.DefectId));

        // ผู้ไม่มีสิทธิ์บันทึกผล และ Defect ที่ไม่ได้ระบุ Build = ทำไม่ได้พร้อมเหตุผล
        Assert.IsType<BadRequestObjectResult>((await Controller(db, canRunExecution: false).RecordExecution(defect.DefectId, CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await Controller(db).RecordExecution(noBuild.DefectId, CancellationToken.None)).Result);
        Assert.False(await db.TestCycles.AnyAsync());

        var recorded = Dto(await Controller(db).RecordExecution(defect.DefectId, CancellationToken.None));

        Assert.Equal("AUT-CYC-001", recorded.AutoExecutionCycleCode);
        Assert.Equal("Fail", (await db.TestCycleCases.SingleAsync()).CurrentStatus);
        Assert.Equal(1, await db.TestSuiteCases.CountAsync());
        Assert.True(await HasResult(defect.DefectId));
        // มีผลใน Build ที่พบแล้ว = ไม่บันทึกซ้ำ
        Assert.IsType<BadRequestObjectResult>((await Controller(db).RecordExecution(defect.DefectId, CancellationToken.None)).Result);
        Assert.Equal(1, await db.TestExecutions.CountAsync());
    }

    [Fact]
    public async Task Record_execution_off_keeps_previous_behaviour()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);

        var defect = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "High", recordExecution: false);

        Assert.Equal((null, null), (defect.AutoExecutionCycleCode, defect.AutoExecutionSkipped));
        Assert.False(await db.TestCycles.AnyAsync());
        Assert.True(await db.DefectTestCaseLinks.AnyAsync());
    }

    private static async Task<DefectDto> Create(QaDbContext db, Guid projectId, Guid? buildId, Guid moduleId, string severity, bool recordExecution = true, Guid? tester = null, bool canRunExecution = true)
    {
        var request = new SaveDefectRequest(projectId, null, buildId, moduleId, "Wrong total", severity, ActualResult: "Total is 400",
            TestCase: new NewDefectTestCaseRequest(null, [new("Open sales", null, "Sales screen shown"), new("Sell 5 units", "SKU-1", "Total is 500")], recordExecution));
        var result = await Controller(db, tester, canRunExecution).Create(request, CancellationToken.None);
        return Dto(result);
    }

    private static DefectsController Controller(QaDbContext db, Guid? tester = null, bool canRunExecution = true) =>
        new(db, new ProjectAccessContext(), new DefectActivityService(db), null!, null!, NullLogger<DefectsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, (tester ?? Tester).ToString()), .. canRunExecution ? [new Claim("permission", "EXECUTION.RUN")] : Array.Empty<Claim>()], "test")) } },
        };

    private static DefectDto Dto(ActionResult<DefectDto> result) => (DefectDto)Assert.IsType<OkObjectResult>(result.Result).Value!;

    private static async Task<Build> AddBuild(QaDbContext db, Guid releaseId, string number)
    {
        var build = new Build(releaseId, number, "1.0", null, null, DateTime.UtcNow.AddDays(1), null, null, null);
        db.Builds.Add(build);
        await db.SaveChangesAsync();
        return build;
    }
}
