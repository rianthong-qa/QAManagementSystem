using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Common;

namespace ProMaxx2.QA.UnitTests;

public sealed class AutomationCaptureControllerValidationTests
{
    [Fact]
    public async Task Create_rejects_capture_item_without_action_or_object_code()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var access = new ProjectAccessContext { AllowedProjectIds = [baseline.Project.ProjectId] };
        var controller = new AutomationCaptureController(db, access);
        var request = Request(baseline, [Item(action: "", objectCode: "loginButton")]);

        var result = await controller.Create(null, request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(db.AutomationCaptureSessions);
    }

    [Fact]
    public async Task Create_rejects_duplicate_automation_ids_case_insensitively()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var access = new ProjectAccessContext { AllowedProjectIds = [baseline.Project.ProjectId] };
        var controller = new AutomationCaptureController(db, access);
        var request = Request(baseline, [Item(automationId: "BtnLogin"), Item(automationId: "btnlogin", objectCode: "loginButton2")]);

        var result = await controller.Create(null, request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Duplicate AutomationId", badRequest.Value?.ToString());
        Assert.Empty(db.AutomationCaptureSessions);
    }

    private static CreateCaptureSessionRequest Request(AutomationTestFixtures.Baseline baseline, IReadOnlyList<CaptureItemRequest> items) =>
        new(baseline.Project.ProjectId, baseline.Module.ModuleId, baseline.TestCase.TestCaseId, "app", "1.0", "QA-PC", items);

    private static CaptureItemRequest Item(string action = "Click Login", string objectCode = "loginButton", string? automationId = "BtnLogin") =>
        new(1, "Click", action, null, "", "Login", objectCode, "Login", "Button", automationId, "{}", false);
}
