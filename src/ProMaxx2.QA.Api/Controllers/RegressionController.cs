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
public sealed class RegressionController(QaDbContext db) : ControllerBase
{
    [HttpPost("releases/{releaseId:guid}/regression-impact"),Authorize(Policy="RegressionManage")]
    public async Task<ActionResult<RegressionImpactDto>> Impact(Guid releaseId, RegressionImpactRequest request, CancellationToken ct)
    {
        var release = await db.Releases.AsNoTracking().SingleOrDefaultAsync(x => x.ReleaseId == releaseId, ct);
        if (release is null) return NotFound();
        var buildExists = await db.Builds.AnyAsync(x => x.BuildId == request.BuildId && x.ReleaseId == releaseId && x.IsActive, ct);
        if (!buildExists) return BadRequest(new ProblemDetails { Title = "Build ไม่ถูกต้อง", Detail = "Build ที่เลือกไม่ได้อยู่ใน Release นี้", Status = 400 });

        var changedModules = request.ChangedModuleIds.Distinct().ToArray();
        var priorityLimit = PriorityRank(request.MinimumPriority);
        var specialImpact = request.DatabaseChange || request.ApiChange || request.CalculationChange || request.PermissionChange || request.InstallerChange;
        var linkedDefectCases = request.DefectFix
            ? await db.DefectTestCaseLinks.AsNoTracking().Where(link => db.Defects.Any(defect => defect.DefectId == link.DefectId && defect.ProjectId == release.ProjectId && !defect.IsDeleted)).Select(x => x.TestCaseId).Distinct().ToListAsync(ct)
            : [];

        var rows = await db.TestCases.AsNoTracking()
            .Where(x => x.ProjectId == release.ProjectId && !x.IsDeleted && x.Status != "Deprecated")
            .Where(x => changedModules.Contains(x.ModuleId)
                || linkedDefectCases.Contains(x.TestCaseId)
                || (request.IncludeSharedDependencies && (x.Priority == "P0" || x.Priority == "P1" || x.TestType == "Regression"))
                || (specialImpact && x.TestType == "Regression"))
            .Where(x => (x.Priority == "P0" ? 0 : x.Priority == "P1" ? 1 : x.Priority == "P2" ? 2 : 3) <= priorityLimit || changedModules.Contains(x.ModuleId) || linkedDefectCases.Contains(x.TestCaseId))
            .Select(x => new { x.TestCaseId, x.TestCaseCode, x.Title, x.ModuleId, ModuleName = db.Modules.Where(m => m.ModuleId == x.ModuleId).Select(m => m.ModuleName).FirstOrDefault() ?? "-", x.Priority, x.TestType, x.RevisionNo, x.Status })
            .OrderBy(x => x.Priority).ThenBy(x => x.TestCaseCode).ToListAsync(ct);

        var caseIds = rows.Select(x => x.TestCaseId).ToArray();
        var executionRows = await db.TestExecutions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BuildId == request.BuildId && caseIds.Contains(x.CycleCase.TestCaseId))
            .Select(x => new { x.CycleCase.TestCaseId, x.Status, x.CompletedAt, x.ExecutionNo }).ToListAsync(ct);
        var lastResults = executionRows.GroupBy(x => x.TestCaseId).ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.CompletedAt).ThenByDescending(y => y.ExecutionNo).First().Status);

        var allCases = rows.Select(x =>
        {
            var historical = linkedDefectCases.Contains(x.TestCaseId);
            var direct = changedModules.Contains(x.ModuleId);
            var critical = x.Priority is "P0" or "P1";
            var impactType = direct ? "Direct Impact" : historical ? "Historical Defect" : critical ? "Critical P0/P1" : "Shared Dependency";
            var reason = direct ? $"อยู่ใน Module ที่มีการเปลี่ยนแปลง: {x.ModuleName}"
                : historical ? "เคยเชื่อมโยงกับ Defect ในโครงการ"
                : critical ? $"Test Case ระดับ {x.Priority} ควรอยู่ใน Critical Regression"
                : "เป็น Regression case หรือเกี่ยวข้องกับ shared impact";
            var score = Math.Clamp((direct ? request.DirectImpactWeight : 0) + (historical ? request.HistoricalDefectWeight : 0) + (critical ? request.CriticalPriorityWeight : 0) + (!direct && !historical ? request.SharedDependencyWeight : 0), 0, 100);
            return new RegressionCaseDto(x.TestCaseId, x.TestCaseCode, x.Title, x.ModuleId, x.ModuleName, x.Priority, x.TestType, x.RevisionNo, x.Status, lastResults.GetValueOrDefault(x.TestCaseId), impactType, reason, direct || historical || x.Priority == "P0", score);
        }).OrderByDescending(x=>x.RiskScore).ThenBy(x=>x.Priority).ThenBy(x=>x.TestCaseCode).ToList();
        var pageSize=Math.Clamp(request.PageSize,10,200);var totalPages=Math.Max(1,(int)Math.Ceiling(allCases.Count/(double)pageSize));var page=Math.Clamp(request.Page,1,totalPages);
        var cases=allCases.Skip((page-1)*pageSize).Take(pageSize).ToList();

        var cycleCases = await db.TestCycleCases.AsNoTracking().Where(x => x.Cycle.ReleaseId == releaseId && x.Cycle.BuildId == request.BuildId && x.Cycle.CycleType == "Regression" && !x.Cycle.IsDeleted).Select(x => x.CurrentStatus).ToListAsync(ct);
        var cycleCount = await db.TestCycles.CountAsync(x => x.ReleaseId == releaseId && x.BuildId == request.BuildId && x.CycleType == "Regression" && !x.IsDeleted, ct);
        var executed = cycleCases.Count(x => x != "NotRun");
        var passed = cycleCases.Count(x => x == "Pass");
        var failed = cycleCases.Count(x => x is "Fail" or "Blocked");
        var openDefects = await db.Defects.CountAsync(x => x.ReleaseId == releaseId && !x.IsDeleted && x.Status != "Closed" && x.Status != "Rejected", ct);
        var progress = cycleCases.Count == 0 ? 0 : Math.Round(executed * 100m / cycleCases.Count, 1);
        var passRate = executed == 0 ? 0 : Math.Round(passed * 100m / executed, 1);
        var overall = cycleCases.Count == 0 ? "Not Started" : failed > 0 || openDefects > 0 ? "At Risk" : progress < 100 ? "In Progress" : "Passed";
        var metrics = new RegressionMetricsDto(changedModules.Length, allCases.Count, cycleCount, cycleCases.Count, executed, passed, failed, progress, passRate, openDefects, overall);
        if(request.RecordAnalysis){db.RegressionAnalyses.Add(new RegressionAnalysis(release.ProjectId,releaseId,request.BuildId,changedModules.Length,allCases.Count,request.MinimumPriority,request.ChangeNotes,UserId()));db.RegressionActivities.Add(new RegressionActivity(release.ProjectId,releaseId,request.BuildId,"ImpactAnalyzed",$"{changedModules.Length} modules, {allCases.Count} recommended cases",UserId()));await db.SaveChangesAsync(ct);}
        return Ok(new RegressionImpactDto(releaseId, request.BuildId, metrics, cases,page,pageSize,allCases.Count,totalPages,request.IncludeAllCaseIds?allCases.Select(x=>x.TestCaseId).ToArray():[]));
    }

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
    public async Task<ActionResult<IReadOnlyList<RegressionScheduleDto>>> Schedules(Guid projectId,CancellationToken ct)=>Ok(await db.RegressionSchedules.AsNoTracking().Where(x=>x.ProjectId==projectId&&x.IsActive).OrderBy(x=>x.Name).Select(x=>new RegressionScheduleDto(x.RegressionScheduleId,x.ProjectId,x.ReleaseId,x.RegressionProfileId,x.Name,x.IsActive,x.CreatedAt)).ToListAsync(ct));

    [HttpPost("regression-schedules"),Authorize(Policy="RegressionManage")]
    public async Task<ActionResult<RegressionScheduleDto>> SaveSchedule(SaveRegressionScheduleRequest request,CancellationToken ct)
    {var release=await db.Releases.AsNoTracking().SingleOrDefaultAsync(x=>x.ReleaseId==request.ReleaseId&&x.Status!="Cancelled",ct);if(release is null)return BadRequest();var entity=new RegressionSchedule(release.ProjectId,release.ReleaseId,request.RegressionProfileId,string.IsNullOrWhiteSpace(request.Name)?"Regression on new build":request.Name,UserId());db.RegressionSchedules.Add(entity);await db.SaveChangesAsync(ct);return Ok(new RegressionScheduleDto(entity.RegressionScheduleId,entity.ProjectId,entity.ReleaseId,entity.RegressionProfileId,entity.Name,entity.IsActive,entity.CreatedAt));}

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
        return NoContent();
    }

    private static int PriorityRank(string value) => value.ToUpperInvariant() switch { "P0" => 0, "P1" => 1, "P2" => 2, "P3" => 3, _ => 1 };
    private Guid? UserId(){var principal=ControllerContext.HttpContext?.User;return principal is not null&&Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier)??principal.FindFirstValue("sub"),out var id)?id:null;}
    private async Task<RegressionBuildMetricsDto> BuildMetrics(Guid buildId,CancellationToken ct){var number=await db.Builds.Where(x=>x.BuildId==buildId).Select(x=>x.BuildNumber).SingleAsync(ct);var statuses=await db.TestCycleCases.AsNoTracking().Where(x=>x.Cycle.BuildId==buildId&&x.Cycle.CycleType=="Regression"&&!x.Cycle.IsDeleted).Select(x=>x.CurrentStatus).ToListAsync(ct);var executed=statuses.Count(x=>x!="NotRun");var passed=statuses.Count(x=>x=="Pass");var failed=statuses.Count(x=>x=="Fail");var blocked=statuses.Count(x=>x=="Blocked");return new RegressionBuildMetricsDto(buildId,number,statuses.Count,executed,passed,failed,blocked,statuses.Count-executed,executed==0?0:Math.Round(passed*100m/executed,1));}
}
