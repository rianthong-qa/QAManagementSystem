using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Application.Regression;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Infrastructure.Persistence;
using System.Security.Claims;

namespace ProMaxx2.QA.UnitTests;

public sealed class RegressionControllerIntegrationTests
{
    [Fact]
    public async Task Impact_returns_direct_case_from_changed_module()
    {
        await using var db = CreateDatabase();
        var data = await SeedAsync(db);
        var controller = new RegressionController(db);

        var result = await controller.Impact(data.Release.ReleaseId,
            new RegressionImpactRequest(data.Build.BuildId, [data.Module.ModuleId], false), CancellationToken.None);

        var dto = Assert.IsType<RegressionImpactDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var testCase = Assert.Single(dto.Cases);
        Assert.Equal(data.TestCase.TestCaseId, testCase.TestCaseId);
        Assert.Equal("Direct Impact", testCase.ImpactType);
        Assert.True(testCase.IsRequired);
        Assert.Equal(60, testCase.RiskScore);
        Assert.Equal(1, dto.Page);
        Assert.Equal(1, dto.TotalItems);
        Assert.Equal(1, dto.TotalPages);
        var inactiveBuild = new Build(data.Release.ReleaseId, "inactive", "0.8", null, null, DateTime.UtcNow, null, null, null);
        inactiveBuild.Deactivate();
        db.AddRange(inactiveBuild, new RegressionAnalysis(data.Project.ProjectId, data.Release.ReleaseId, inactiveBuild.BuildId, 1, 99, "P1", "inactive build", null));
        await db.SaveChangesAsync();
        var historyResult = await controller.History(data.Release.ReleaseId, 20, CancellationToken.None);
        var history = Assert.IsType<List<RegressionHistoryDto>>(Assert.IsType<OkObjectResult>(historyResult.Result).Value);
        Assert.Equal(1, Assert.Single(history).ImpactedModules);
        Assert.Contains(await db.RegressionActivities.ToListAsync(), x => x.Action == "ImpactAnalyzed");
    }

    [Fact]
    public async Task Baseline_compares_regression_execution_metrics_between_builds()
    {
        await using var db = CreateDatabase();
        var data = await SeedAsync(db);
        var baselineBuild = new Build(data.Release.ReleaseId, "0", "0.9", null, null, DateTime.UtcNow, null, null, null);
        db.Builds.Add(baselineBuild); await db.SaveChangesAsync();
        var controller = new RegressionController(db);
        var result = await controller.Baseline(data.Release.ReleaseId, baselineBuild.BuildId, data.Build.BuildId, CancellationToken.None);
        var comparison = Assert.IsType<RegressionBaselineDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("0", comparison.Baseline.BuildNumber);
        Assert.Equal("1", comparison.Target.BuildNumber);
        Assert.Equal(0, comparison.PassRateDelta);
    }

    [Fact]
    public async Task Impact_to_suite_cycle_and_execution_workspace_completes_end_to_end()
    {
        await using var db = CreateDatabase();
        var data = await SeedAsync(db);
        var controller = new RegressionController(db);

        var impactResult = await controller.Impact(data.Release.ReleaseId,
            new RegressionImpactRequest(data.Build.BuildId, [data.Module.ModuleId], false), CancellationToken.None);
        var impact = Assert.IsType<RegressionImpactDto>(Assert.IsType<OkObjectResult>(impactResult.Result).Value);
        var selectedCase = Assert.Single(impact.Cases);

        var suiteResult = await controller.GenerateSuite(
            new GenerateRegressionSuiteRequest(data.Release.ReleaseId, "Automated Regression", null, "High", [selectedCase.TestCaseId]), CancellationToken.None);
        var suite = Assert.IsType<RegressionSuiteResultDto>(Assert.IsType<OkObjectResult>(suiteResult.Result).Value);
        Assert.Equal(1, suite.CaseCount);
        Assert.Single(await db.TestSuiteCases.Where(x => x.TestSuiteId == suite.TestSuiteId).ToListAsync());

        var environment = new TestEnvironment(data.Project.ProjectId, "Integration", null);
        var cycle = new TestCycle(data.Project.ProjectId, data.Release.ReleaseId, data.Build.BuildId, environment.TestEnvironmentId,
            suite.TestSuiteId, "REG-CYCLE-001", "Regression Cycle", "Regression", null, null, null, null, null);
        db.AddRange(environment, cycle);
        await db.SaveChangesAsync();

        var response = await controller.AddImpactCases(cycle.TestCycleId, new AddImpactCasesRequest([selectedCase.TestCaseId]), CancellationToken.None);
        Assert.IsType<NoContentResult>(response);
        Assert.Single(await db.TestCycleCases.Where(x => x.TestCycleId == cycle.TestCycleId).ToListAsync());
        var execution = new ExecutionService(new ExecutionRepository(db));
        var workspace = await execution.WorkspaceAsync(cycle.TestCycleId, CancellationToken.None);
        Assert.Equal("REG-CYCLE-001", workspace.CycleCode);
        Assert.Equal(data.TestCase.TestCaseId, Assert.Single(workspace.Cases).TestCaseId);
        var auditActions = await db.RegressionActivities.Select(x => x.Action).ToListAsync();
        Assert.Contains("ImpactAnalyzed", auditActions);
        Assert.Contains("SuiteGenerated", auditActions);
        Assert.Contains("CasesAddedToCycle", auditActions);
    }

    [Fact]
    public async Task Shared_profile_schedule_notification_and_server_selection_work_end_to_end()
    {
        await using var db = CreateDatabase();
        var data = await SeedAsync(db);
        var controller = new RegressionController(db);
        var profileResult = await controller.SaveProfile(new SaveRegressionProfileRequest(data.Project.ProjectId, "Team Critical", "Shared", "{\"minimumPriority\":\"P1\"}"), CancellationToken.None);
        var profile = Assert.IsType<RegressionProfileDto>(Assert.IsType<OkObjectResult>(profileResult.Result).Value);
        Assert.Equal("Shared", profile.Visibility);
        var profilesResult = await controller.Profiles(data.Project.ProjectId, CancellationToken.None);
        Assert.Contains(Assert.IsAssignableFrom<IReadOnlyList<RegressionProfileDto>>(Assert.IsType<OkObjectResult>(profilesResult.Result).Value), x => x.RegressionProfileId == profile.RegressionProfileId);

        var scheduleResult = await controller.SaveSchedule(new SaveRegressionScheduleRequest(data.Release.ReleaseId, profile.RegressionProfileId, "New build regression"), CancellationToken.None);
        var schedule = Assert.IsType<RegressionScheduleDto>(Assert.IsType<OkObjectResult>(scheduleResult.Result).Value);
        var newBuild = new Build(data.Release.ReleaseId, "2", "2.0", null, null, DateTime.UtcNow, null, null, null);
        db.Builds.Add(newBuild); await db.SaveChangesAsync();
        var notificationsResult = await controller.Notifications(data.Project.ProjectId, CancellationToken.None);
        var notification = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<RegressionNotificationDto>>(Assert.IsType<OkObjectResult>(notificationsResult.Result).Value));
        Assert.Equal(newBuild.BuildId, notification.BuildId);
        Assert.IsType<NoContentResult>(await controller.Acknowledge(schedule.RegressionScheduleId, newBuild.BuildId, CancellationToken.None));

        var impactResult = await controller.Impact(data.Release.ReleaseId,
            new RegressionImpactRequest(data.Build.BuildId, [data.Module.ModuleId], false, IncludeAllCaseIds: true, RecordAnalysis: false), CancellationToken.None);
        var impact = Assert.IsType<RegressionImpactDto>(Assert.IsType<OkObjectResult>(impactResult.Result).Value);
        Assert.Equal(data.TestCase.TestCaseId, Assert.Single(impact.AllCaseIds));
    }

    [Fact]
    public async Task Profile_update_and_schedule_deactivate_enforce_ownership()
    {
        await using var db = CreateDatabase();
        var data = await SeedAsync(db);
        var controller = new RegressionController(db);
        var ownerId = Guid.NewGuid();
        SetUser(controller, ownerId);

        var createdResult = await controller.SaveProfile(new SaveRegressionProfileRequest(data.Project.ProjectId, "Draft", "Private", "{\"minimumPriority\":\"P1\"}"), CancellationToken.None);
        var created = Assert.IsType<RegressionProfileDto>(Assert.IsType<OkObjectResult>(createdResult.Result).Value);

        var otherId = Guid.NewGuid();
        SetUser(controller, otherId);
        Assert.IsType<ForbidResult>((await controller.UpdateProfile(created.RegressionProfileId,
            new UpdateRegressionProfileRequest("Hijacked", "Shared", "{}"), CancellationToken.None)).Result);

        SetUser(controller, ownerId);
        var updatedResult = await controller.UpdateProfile(created.RegressionProfileId,
            new UpdateRegressionProfileRequest("Renamed", "Shared", "{\"minimumPriority\":\"P0\"}"), CancellationToken.None);
        var updated = Assert.IsType<RegressionProfileDto>(Assert.IsType<OkObjectResult>(updatedResult.Result).Value);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("Shared", updated.Visibility);
        Assert.True(updated.IsOwner);
        var stored = await db.RegressionProfiles.SingleAsync(x => x.RegressionProfileId == created.RegressionProfileId);
        Assert.Equal("{\"minimumPriority\":\"P0\"}", stored.SettingsJson);
        Assert.NotNull(stored.UpdatedAt);

        Assert.IsType<BadRequestObjectResult>((await controller.UpdateProfile(created.RegressionProfileId,
            new UpdateRegressionProfileRequest("Bad", "Team", "{}"), CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await controller.UpdateProfile(Guid.NewGuid(),
            new UpdateRegressionProfileRequest("Missing", "Shared", "{}"), CancellationToken.None)).Result);

        var scheduleResult = await controller.SaveSchedule(new SaveRegressionScheduleRequest(data.Release.ReleaseId, created.RegressionProfileId, "Nightly"), CancellationToken.None);
        var schedule = Assert.IsType<RegressionScheduleDto>(Assert.IsType<OkObjectResult>(scheduleResult.Result).Value);
        SetUser(controller, otherId);
        Assert.IsType<ForbidResult>(await controller.DeleteSchedule(schedule.RegressionScheduleId, CancellationToken.None));
        SetUser(controller, Guid.NewGuid(), "SYS_ADMIN");
        Assert.IsType<NoContentResult>(await controller.DeleteSchedule(schedule.RegressionScheduleId, CancellationToken.None));
        Assert.DoesNotContain(await db.RegressionSchedules.ToListAsync(), x => x.RegressionScheduleId == schedule.RegressionScheduleId && x.IsActive);
        var schedulesResult = await controller.Schedules(data.Project.ProjectId, CancellationToken.None);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<RegressionScheduleDto>>(Assert.IsType<OkObjectResult>(schedulesResult.Result).Value));
    }

    private static void SetUser(RegressionController controller, Guid userId, string? role = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
    }

    private static QaDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<QaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new QaDbContext(options);
    }

    private static async Task<(Project Project, ProductModule Module, Release Release, Build Build, TestCase TestCase)> SeedAsync(QaDbContext db)
    {
        var project = new Project("REG", "Regression", null, null, null);
        var module = new ProductModule(project.ProjectId, "CORE", "Core", null, null, null, null);
        var release = new Release(project.ProjectId, "REL-001", "1.0", "Major", null, null, null, null);
        var build = new Build(release.ReleaseId, "1", "1.0", null, null, DateTime.UtcNow, null, null, null);
        var testCase = new TestCase(project.ProjectId, module.ModuleId, "TC-001", "Critical path", "Verify path", null,
            "P0", "Regression", false, null, [new TestStepInput(1, "Run", null, "Pass")], null);
        db.AddRange(project, module, release, build, testCase);
        await db.SaveChangesAsync();
        return (project, module, release, build, testCase);
    }
}
