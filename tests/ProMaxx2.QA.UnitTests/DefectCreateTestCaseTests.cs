using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>ฟอร์มสร้าง Defect ของหน้า Defect สร้าง Test Case (Draft) จากขั้นตอนการทำซ้ำและเชื่อมโยงให้ในคำขอเดียว</summary>
public sealed class DefectCreateTestCaseTests
{
    [Fact]
    public async Task Create_with_test_case_adds_linked_draft_test_case()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);
        var request = Request(seed.Project.ProjectId, seed.Module.ModuleId, "High", new NewDefectTestCaseRequest("Login as cashier", [
            new("Open sales", null, "Sales screen shown"),
            new("", null, ""),   // แถวว่างไม่นับ
            new("Sell 5 units", "SKU-1", "Total is 500"),
        ]));

        var result = await Controller(db).Create(request, CancellationToken.None);

        var defect = Assert.IsType<OkObjectResult>(result.Result).Value as DefectDto;
        var link = await db.DefectTestCaseLinks.SingleAsync(x => x.DefectId == defect!.DefectId);
        var testCase = await db.TestCases.Include(x => x.Steps).SingleAsync(x => x.TestCaseId == link.TestCaseId);
        Assert.Equal("AUT-SALES-TC-001", testCase.TestCaseCode);
        Assert.Equal("Wrong total", testCase.Title);
        Assert.Equal("P1", testCase.Priority);
        Assert.Equal("Draft", testCase.Status);
        Assert.Equal(seed.Module.ModuleId, testCase.ModuleId);
        Assert.Equal("Login as cashier", testCase.Preconditions);
        Assert.Equal(["Open sales", "Sell 5 units"], testCase.Steps.OrderBy(x => x.StepNo).Select(x => x.Action));
        Assert.Equal([1, 2], testCase.Steps.OrderBy(x => x.StepNo).Select(x => x.StepNo));
    }

    [Fact]
    public async Task Create_with_test_case_requires_module_and_saves_nothing()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);
        var request = Request(seed.Project.ProjectId, null, "Medium", new NewDefectTestCaseRequest(null, [new("Open sales", null, "Shown")]));

        var result = await Controller(db).Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(await db.Defects.AnyAsync());
        Assert.Equal(1, await db.TestCases.CountAsync());
    }

    [Fact]
    public async Task Create_with_test_case_rejects_step_without_expected_result()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);
        var request = Request(seed.Project.ProjectId, seed.Module.ModuleId, "Low", new NewDefectTestCaseRequest(null, [new("Open sales", null, " ")]));

        var result = await Controller(db).Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(await db.Defects.AnyAsync());
    }

    [Fact]
    public async Task Create_without_test_case_keeps_previous_behaviour()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);

        var result = await Controller(db).Create(Request(seed.Project.ProjectId, null, "Medium", null), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.False(await db.DefectTestCaseLinks.AnyAsync());
        Assert.Equal(1, await db.TestCases.CountAsync());
    }

    [Fact]
    public async Task List_filters_by_creator()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);
        var owner = Guid.NewGuid();
        db.Defects.AddRange(
            new Defect(seed.Project.ProjectId, null, null, null, "AUT-DEF-001", "Mine", "High", "Open", owner, null, null, null, null, null),
            new Defect(seed.Project.ProjectId, null, null, null, "AUT-DEF-002", "Other", "High", "Open", Guid.NewGuid(), null, null, null, null, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var result = await Controller(db).List(seed.Project.ProjectId, null, null, null, false, null, null, null, null, null, null, createdByUserId: owner);

        var list = Assert.IsType<OkObjectResult>(result.Result).Value as DefectListResultDto;
        Assert.Equal(["AUT-DEF-001"], list!.Rows.Select(x => x.DefectCode));
    }

    [Fact]
    public async Task Get_and_list_return_release_code_and_build_number()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);
        var created = await Controller(db).Create(new SaveDefectRequest(seed.Project.ProjectId, seed.Release.ReleaseId, seed.Build.BuildId, null, "Wrong total"), CancellationToken.None);
        var id = (Assert.IsType<OkObjectResult>(created.Result).Value as DefectDto)!.DefectId;

        var one = Assert.IsType<OkObjectResult>((await Controller(db).Get(id, CancellationToken.None)).Result).Value as DefectDto;
        var list = Assert.IsType<OkObjectResult>((await Controller(db).List(seed.Project.ProjectId, null, null, null, false, null, null, null, null, null, null)).Result).Value as DefectListResultDto;

        Assert.Equal(("REL-001", "1.0", "1"), (one!.ReleaseCode, one.ReleaseVersion, one.BuildNumber));
        Assert.Equal(("REL-001", "1"), (list!.Rows[0].ReleaseCode, list.Rows[0].BuildNumber));
    }

    private static SaveDefectRequest Request(Guid projectId, Guid? moduleId, string severity, NewDefectTestCaseRequest? testCase) =>
        new(projectId, null, null, moduleId, "Wrong total", severity, TestCase: testCase);

    private static DefectsController Controller(QaDbContext db) =>
        new(db, new ProjectAccessContext(), new DefectActivityService(db), null!, null!, NullLogger<DefectsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test")) } },
        };
}
