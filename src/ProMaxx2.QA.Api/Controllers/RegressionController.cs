using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Regression;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1"), Authorize(Policy = "RegressionView"), RequireProjectAccess]
public sealed class RegressionController(QaDbContext db, ProjectAccessContext projectCtx) : ControllerBase
{
    [HttpPost("releases/{releaseId:guid}/regression-impact"),Authorize(Policy="RegressionManage")]
    public async Task<ActionResult<RegressionImpactDto>> Impact(Guid releaseId, RegressionImpactRequest request, CancellationToken ct)
    {
        var release = await db.Releases.AsNoTracking().SingleOrDefaultAsync(x => x.ReleaseId == releaseId, ct);
        if (release is null) return NotFound();
        try { return Ok(await RegressionImpactAnalyzer.RunAsync(db, release, request, UserId(), ct)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Title = "Build ไม่ถูกต้อง", Detail = ex.Message, Status = 400 }); }
    }

    [HttpPost("regression/automation-run-preview")]
    public async Task<ActionResult<RegressionAutomationPreviewDto>> AutomationRunPreview(RegressionAutomationPreviewRequest request, CancellationToken ct)
        => Ok(await RegressionAutomationRunPlanner.ResolveEligibleAsync(db, request.TestCaseIds, ct, projectCtx.AllowedProjectIds)); // AUT-SEC-002

    [HttpGet("releases/{releaseId:guid}/regression-history")]
    public async Task<ActionResult<IReadOnlyList<RegressionHistoryDto>>> History(Guid releaseId,[FromQuery]int size=20,CancellationToken ct=default)
    {
        var rows=await db.RegressionAnalyses.AsNoTracking().Where(x=>x.ReleaseId==releaseId&&db.Builds.Any(b=>b.BuildId==x.BuildId&&b.IsActive)).OrderByDescending(x=>x.AnalyzedAt).Take(Math.Clamp(size,1,100))
            .Select(x=>new RegressionHistoryDto(x.RegressionAnalysisId,x.ReleaseId,x.BuildId,db.Builds.Where(b=>b.BuildId==x.BuildId).Select(b=>b.BuildNumber).FirstOrDefault()??"-",x.ImpactedModules,x.RecommendedCases,x.MinimumPriority,x.ChangeNotes,x.AnalyzedBy,x.AnalyzedBy.HasValue?db.Users.Where(u=>u.UserId==x.AnalyzedBy).Select(u=>u.DisplayName).FirstOrDefault():null,x.AnalyzedAt)).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("releases/{releaseId:guid}/regression-activities")]
    public async Task<ActionResult<IReadOnlyList<RegressionActivityDto>>> Activities(Guid releaseId,[FromQuery]int size=50,CancellationToken ct=default)=>Ok(await db.RegressionActivities.AsNoTracking().Where(x=>x.ReleaseId==releaseId).OrderByDescending(x=>x.CreatedAt).Take(Math.Clamp(size,1,200)).Select(x=>new RegressionActivityDto(x.RegressionActivityId,x.ReleaseId,x.BuildId,x.Action,x.Details,x.ActorUserId,x.ActorUserId.HasValue?db.Users.Where(u=>u.UserId==x.ActorUserId).Select(u=>u.DisplayName).FirstOrDefault():null,x.CreatedAt)).ToListAsync(ct));

    [HttpGet("projects/{projectId:guid}/regression-profiles")]
    public async Task<ActionResult<IReadOnlyList<RegressionProfileDto>>> Profiles(Guid projectId,CancellationToken ct)
    {var userId=UserId();return Ok(await db.RegressionProfiles.AsNoTracking().Where(x=>x.ProjectId==projectId&&x.IsActive&&(x.Visibility=="Shared"||x.OwnerUserId==userId)).OrderByDescending(x=>x.UpdatedAt??x.CreatedAt).Select(x=>new RegressionProfileDto(x.RegressionProfileId,x.ProjectId,x.Name,x.Visibility,x.OwnerUserId,x.OwnerUserId.HasValue?db.Users.Where(u=>u.UserId==x.OwnerUserId).Select(u=>u.DisplayName).FirstOrDefault():null,x.SettingsJson,x.OwnerUserId==userId,x.CreatedAt)).ToListAsync(ct));}

    [HttpPost("regression-profiles"),Authorize(Policy="RegressionManage")]
    public async Task<ActionResult<RegressionProfileDto>> SaveProfile(SaveRegressionProfileRequest request,CancellationToken ct)
    {if(string.IsNullOrWhiteSpace(request.Name)||request.Visibility is not ("Private" or "Shared" or "Owner"))return BadRequest();var visibility=request.Visibility=="Owner"?"Private":request.Visibility;var entity=new RegressionProfile(request.ProjectId,request.Name,visibility,UserId(),request.SettingsJson);db.RegressionProfiles.Add(entity);await db.SaveChangesAsync(ct);return Ok(new RegressionProfileDto(entity.RegressionProfileId,entity.ProjectId,entity.Name,entity.Visibility,entity.OwnerUserId,null,entity.SettingsJson,true,entity.CreatedAt));}

    [HttpPut("regression-profiles/{id:guid}"),Authorize(Policy="RegressionManage")]
    public async Task<ActionResult<RegressionProfileDto>> UpdateProfile(Guid id,UpdateRegressionProfileRequest request,CancellationToken ct)
    {var entity=await db.RegressionProfiles.SingleOrDefaultAsync(x=>x.RegressionProfileId==id&&x.IsActive,ct);if(entity is null)return NotFound();if(entity.OwnerUserId.HasValue&&entity.OwnerUserId!=UserId()&&!User.IsInRole("SYS_ADMIN"))return Forbid();if(string.IsNullOrWhiteSpace(request.Name)||request.Visibility is not ("Private" or "Shared"))return BadRequest(new ProblemDetails{Title="ข้อมูลไม่ถูกต้อง",Detail="กรุณาระบุชื่อ Profile และ Visibility เป็น Private หรือ Shared",Status=400});entity.Update(request.Name,request.Visibility,request.SettingsJson);await db.SaveChangesAsync(ct);return Ok(new RegressionProfileDto(entity.RegressionProfileId,entity.ProjectId,entity.Name,entity.Visibility,entity.OwnerUserId,entity.OwnerUserId.HasValue?db.Users.Where(u=>u.UserId==entity.OwnerUserId).Select(u=>u.DisplayName).FirstOrDefault():null,entity.SettingsJson,entity.OwnerUserId==UserId(),entity.CreatedAt));}

    [HttpDelete("regression-profiles/{id:guid}"),Authorize(Policy="RegressionManage")]
    public async Task<IActionResult> DeleteProfile(Guid id,CancellationToken ct)
    {var entity=await db.RegressionProfiles.SingleOrDefaultAsync(x=>x.RegressionProfileId==id&&x.IsActive,ct);if(entity is null)return NotFound();if(entity.OwnerUserId.HasValue&&entity.OwnerUserId!=UserId()&&!User.IsInRole("SYS_ADMIN"))return Forbid();entity.Deactivate();await db.SaveChangesAsync(ct);return NoContent();}

    [HttpGet("projects/{projectId:guid}/regression-schedules")]
    public async Task<ActionResult<IReadOnlyList<RegressionScheduleDto>>> Schedules(Guid projectId,CancellationToken ct)=>Ok(await db.RegressionSchedules.AsNoTracking().Where(x=>x.ProjectId==projectId&&x.IsActive).OrderBy(x=>x.Name).Select(x=>new RegressionScheduleDto(x.RegressionScheduleId,x.ProjectId,x.ReleaseId,x.RegressionProfileId,x.Name,x.IsActive,x.CreatedAt,x.EnvironmentId,x.EnvironmentId.HasValue?db.TestEnvironments.Where(e=>e.TestEnvironmentId==x.EnvironmentId).Select(e=>e.EnvironmentName).FirstOrDefault():null,x.Priority)).ToListAsync(ct));

    [HttpPost("regression-schedules"),Authorize(Policy="RegressionManage")]
    public async Task<ActionResult<RegressionScheduleDto>> SaveSchedule(SaveRegressionScheduleRequest request,CancellationToken ct)
    {var release=await db.Releases.AsNoTracking().SingleOrDefaultAsync(x=>x.ReleaseId==request.ReleaseId&&x.Status!="Cancelled",ct);if(release is null)return BadRequest();var entity=new RegressionSchedule(release.ProjectId,release.ReleaseId,request.RegressionProfileId,string.IsNullOrWhiteSpace(request.Name)?"Regression on new build":request.Name,UserId());entity.ConfigureAutomation(request.EnvironmentId,request.Priority);db.RegressionSchedules.Add(entity);await db.SaveChangesAsync(ct);var environmentName=entity.EnvironmentId.HasValue?await db.TestEnvironments.Where(e=>e.TestEnvironmentId==entity.EnvironmentId).Select(e=>e.EnvironmentName).FirstOrDefaultAsync(ct):null;return Ok(new RegressionScheduleDto(entity.RegressionScheduleId,entity.ProjectId,entity.ReleaseId,entity.RegressionProfileId,entity.Name,entity.IsActive,entity.CreatedAt,entity.EnvironmentId,environmentName,entity.Priority));}

    [HttpDelete("regression-schedules/{id:guid}"),Authorize(Policy="RegressionManage")]
    public async Task<IActionResult> DeleteSchedule(Guid id,CancellationToken ct)
    {var entity=await db.RegressionSchedules.SingleOrDefaultAsync(x=>x.RegressionScheduleId==id&&x.IsActive,ct);if(entity is null)return NotFound();if(entity.OwnerUserId.HasValue&&entity.OwnerUserId!=UserId()&&!User.IsInRole("SYS_ADMIN"))return Forbid();entity.Deactivate();await db.SaveChangesAsync(ct);return NoContent();}

    [HttpGet("projects/{projectId:guid}/regression-notifications")]
    public async Task<ActionResult<IReadOnlyList<RegressionNotificationDto>>> Notifications(Guid projectId,CancellationToken ct)
    {var schedules=await db.RegressionSchedules.AsNoTracking().Where(x=>x.ProjectId==projectId&&x.IsActive).ToListAsync(ct);var result=new List<RegressionNotificationDto>();foreach(var schedule in schedules){var build=await db.Builds.AsNoTracking().Where(x=>x.ReleaseId==schedule.ReleaseId&&x.IsActive&&x.CreatedAt>=schedule.CreatedAt&&x.BuildId!=schedule.LastNotifiedBuildId).OrderByDescending(x=>x.CreatedAt).FirstOrDefaultAsync(ct);if(build is not null)result.Add(new RegressionNotificationDto(schedule.RegressionScheduleId,build.BuildId,build.BuildNumber,schedule.Name,$"Build {build.BuildNumber} พร้อมสำหรับ Regression",build.CreatedAt));}return Ok(result.OrderByDescending(x=>x.CreatedAt).ToList());}

    [HttpPost("regression-schedules/{scheduleId:guid}/acknowledge/{buildId:guid}"),Authorize(Policy="RegressionManage")]
    public async Task<IActionResult> Acknowledge(Guid scheduleId,Guid buildId,CancellationToken ct){var entity=await db.RegressionSchedules.SingleOrDefaultAsync(x=>x.RegressionScheduleId==scheduleId&&x.IsActive,ct);if(entity is null)return NotFound();entity.Acknowledge(buildId);await db.SaveChangesAsync(ct);return NoContent();}

    [HttpGet("releases/{releaseId:guid}/regression-baseline")]
    public async Task<ActionResult<RegressionBaselineDto>> Baseline(Guid releaseId,[FromQuery]Guid baselineBuildId,[FromQuery]Guid targetBuildId,CancellationToken ct=default)
    {
        if(!await db.Builds.AnyAsync(x=>x.ReleaseId==releaseId&&x.BuildId==baselineBuildId,ct)||!await db.Builds.AnyAsync(x=>x.ReleaseId==releaseId&&x.BuildId==targetBuildId,ct))return BadRequest(new ProblemDetails{Title="Build ไม่ถูกต้อง",Detail="Baseline และ Target Build ต้องอยู่ใน Release เดียวกัน",Status=400});
        var baseline=await BuildMetrics(baselineBuildId,ct);var target=await BuildMetrics(targetBuildId,ct);
        return Ok(new RegressionBaselineDto(baseline,target,target.ExecutedCases-baseline.ExecutedCases,target.PassedCases-baseline.PassedCases,target.FailedCases-baseline.FailedCases,target.PassRate-baseline.PassRate));
    }

    [HttpPost("regression-suites/generate"), Authorize(Policy = "RegressionManage")]
    public async Task<ActionResult<RegressionSuiteResultDto>> GenerateSuite(GenerateRegressionSuiteRequest request, CancellationToken ct)
    {
        var release = await db.Releases.AsNoTracking().SingleOrDefaultAsync(x => x.ReleaseId == request.ReleaseId, ct);
        if (release is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.SuiteName) || request.TestCaseIds.Count == 0) return BadRequest(new ProblemDetails { Title = "ข้อมูลไม่ครบ", Detail = "กรุณาระบุชื่อ Suite และเลือก Test Case อย่างน้อย 1 รายการ", Status = 400 });
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.ProjectId == release.ProjectId, ct);
        var prefix = $"{project.ProjectCode}-TS";
        var codes = await db.TestSuites.AsNoTracking().Where(x => x.ProjectId == release.ProjectId && x.SuiteCode.StartsWith(prefix)).Select(x => x.SuiteCode).ToListAsync(ct);
        var code = BusinessCodeGenerator.NextAvailable(prefix, codes);
        var validCases = await db.TestCases.AsNoTracking().Where(x => request.TestCaseIds.Contains(x.TestCaseId) && x.ProjectId == release.ProjectId && !x.IsDeleted).Select(x => x.TestCaseId).ToListAsync(ct);
        var suite = new TestSuite(release.ProjectId, code, request.SuiteName, "Regression", request.Description, request.RiskTier ?? "High");
        await db.TestSuites.AddAsync(suite, ct);
        db.TestSuiteCases.AddRange(validCases.Distinct().Select((id, index) => new TestSuiteCase(suite.TestSuiteId, id, index + 1, true)));
        db.RegressionActivities.Add(new RegressionActivity(release.ProjectId,request.ReleaseId,null,"SuiteGenerated",$"{code}: {validCases.Count} cases",UserId()));
        await db.SaveChangesAsync(ct);
        return Ok(new RegressionSuiteResultDto(suite.TestSuiteId, suite.SuiteCode, suite.SuiteName, validCases.Count));
    }

    [HttpPost("test-cycles/{cycleId:guid}/add-impact-cases"), Authorize(Policy = "RegressionManage")]
    public async Task<IActionResult> AddImpactCases(Guid cycleId, AddImpactCasesRequest request, CancellationToken ct)
    {
        var cycle = await db.TestCycles.SingleOrDefaultAsync(x => x.TestCycleId == cycleId && !x.IsDeleted, ct);
        if (cycle is null) return NotFound();
        var existing = await db.TestCycleCases.Where(x => x.TestCycleId == cycleId).Select(x => x.TestCaseId).ToListAsync(ct);
        var cases = await db.TestCases.AsNoTracking().Where(x => request.TestCaseIds.Contains(x.TestCaseId) && x.ProjectId == cycle.ProjectId && !x.IsDeleted).Select(x => new { x.TestCaseId, x.RevisionNo, x.Priority }).ToListAsync(ct);
        var nextOrder = await db.TestCycleCases.Where(x => x.TestCycleId == cycleId).Select(x => (int?)x.ExecutionOrder).MaxAsync(ct) ?? 0;
        db.TestCycleCases.AddRange(cases.Where(x => !existing.Contains(x.TestCaseId)).Select((x, index) => new TestCycleCase(cycleId, x.TestCaseId, x.RevisionNo, x.Priority, nextOrder + index + 1)));
        db.RegressionActivities.Add(new RegressionActivity(cycle.ProjectId,cycle.ReleaseId,cycle.BuildId,"CasesAddedToCycle",$"{cases.Count} requested cases added to {cycle.CycleCode}",UserId()));
        await db.SaveChangesAsync(ct);
        return request.AutoAssignPreview ? Accepted($"/api/v1/test-cycles/{cycleId}/auto-assign/regression-auto-preview", new { previewRequested = true, cycleId }) : NoContent();
    }

    private Guid? UserId(){var principal=ControllerContext.HttpContext?.User;return principal is not null&&Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier)??principal.FindFirstValue("sub"),out var id)?id:null;}
    private async Task<RegressionBuildMetricsDto> BuildMetrics(Guid buildId,CancellationToken ct){var number=await db.Builds.Where(x=>x.BuildId==buildId).Select(x=>x.BuildNumber).SingleAsync(ct);var statuses=await db.TestCycleCases.AsNoTracking().Where(x=>x.Cycle.BuildId==buildId&&x.Cycle.CycleType=="Regression"&&!x.Cycle.IsDeleted).Select(x=>x.CurrentStatus).ToListAsync(ct);var executed=statuses.Count(x=>x!="NotRun");var passed=statuses.Count(x=>x=="Pass");var failed=statuses.Count(x=>x=="Fail");var blocked=statuses.Count(x=>x=="Blocked");return new RegressionBuildMetricsDto(buildId,number,statuses.Count,executed,passed,failed,blocked,statuses.Count-executed,executed==0?0:Math.Round(passed*100m/executed,1));}
}
