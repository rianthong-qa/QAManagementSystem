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
    public async Task Record_execution_off_keeps_previous_behaviour()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);

        var defect = await Create(db, seed.Project.ProjectId, seed.Build.BuildId, seed.Module.ModuleId, "High", recordExecution: false);

        Assert.Equal((null, null), (defect.AutoExecutionCycleCode, defect.AutoExecutionSkipped));
        Assert.False(await db.TestCycles.AnyAsync());
        Assert.True(await db.DefectTestCaseLinks.AnyAsync());
    }

    private static async Task<DefectDto> Create(QaDbContext db, Guid projectId, Guid? buildId, Guid moduleId, string severity, bool recordExecution = true, Guid? tester = null)
    {
        var request = new SaveDefectRequest(projectId, null, buildId, moduleId, "Wrong total", severity, ActualResult: "Total is 400",
            TestCase: new NewDefectTestCaseRequest(null, [new("Open sales", null, "Sales screen shown"), new("Sell 5 units", "SKU-1", "Total is 500")], recordExecution));
        var controller = new DefectsController(db, new ProjectAccessContext(), new DefectActivityService(db), null!, null!, NullLogger<DefectsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, (tester ?? Tester).ToString())], "test")) } },
        };
        var result = await controller.Create(request, CancellationToken.None);
        return (DefectDto)Assert.IsType<OkObjectResult>(result.Result).Value!;
    }
}
