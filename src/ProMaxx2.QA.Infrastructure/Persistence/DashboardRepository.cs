using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Dashboard;
using ProMaxx2.QA.Domain.Dashboard;
using ProMaxx2.QA.Application.Common;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class DashboardRepository(QaDbContext db,ProjectAccessContext projectCtx):IDashboardRepository
{
 private const string ShareAlphabet="23456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
 public async Task<DashboardShareScope>CreateShareAsync(Guid?projectId,Guid?releaseId,Guid?buildId,DateTime expiresAt,CancellationToken ct){string code;do{code=string.Concat(Enumerable.Range(0,8).Select(_=>ShareAlphabet[Random.Shared.Next(ShareAlphabet.Length)]));}while(await db.DashboardShares.AnyAsync(x=>x.Code==code,ct));var entity=new DashboardShare(code,projectId,releaseId,buildId,expiresAt);await db.DashboardShares.AddAsync(entity,ct);await db.SaveChangesAsync(ct);return new(code,projectId,releaseId,buildId,expiresAt);}
 public async Task<DashboardShareScope?>FindShareAsync(string code,CancellationToken ct)=>await db.DashboardShares.AsNoTracking().Where(x=>x.Code==code&&x.ExpiresAt>DateTime.UtcNow).Select(x=>new DashboardShareScope(x.Code,x.ProjectId,x.ReleaseId,x.BuildId,x.ExpiresAt)).SingleOrDefaultAsync(ct);
 public async Task<DashboardSummary>GetAsync(Guid?projectId,Guid?releaseId,Guid?buildId,CancellationToken ct)
 {
  bool byProject=projectId.HasValue;Guid pf=projectId??Guid.Empty;bool noFilter=projectCtx.AllowedProjectIds.Length==0;Guid[] allowed=projectCtx.AllowedProjectIds;
  var modules=await db.Modules.AsNoTracking().Where(x=>x.IsActive&&(byProject?x.ProjectId==pf:noFilter||allowed.Contains(x.ProjectId))).Select(x=>new{x.ModuleId,x.ParentModuleId,x.ModuleCode,x.ModuleName,x.SortOrder}).ToListAsync(ct);
  var requirements=await db.Requirements.AsNoTracking().Where(x=>!x.IsDeleted&&x.IsInScope&&(byProject?x.ProjectId==pf:noFilter||allowed.Contains(x.ProjectId))&&(!releaseId.HasValue||x.ReleaseId==releaseId)).Select(x=>new{x.RequirementId,x.ModuleId,x.Priority}).ToListAsync(ct);
  var requirementIds=requirements.Select(x=>x.RequirementId).ToList();
  var coveredIds=await db.RequirementTestCases.AsNoTracking().Where(x=>requirementIds.Contains(x.RequirementId)).Select(x=>x.RequirementId).Distinct().ToListAsync(ct);
  var cases=await db.TestCases.AsNoTracking().Where(x=>!x.IsDeleted&&(byProject?x.ProjectId==pf:noFilter||allowed.Contains(x.ProjectId))).Select(x=>new{x.TestCaseId,x.ModuleId,x.Priority}).ToListAsync(ct);
  var cycleCases=await db.TestCycleCases.AsNoTracking().Where(x=>!x.Cycle.IsDeleted&&(byProject?x.Cycle.ProjectId==pf:noFilter||allowed.Contains(x.Cycle.ProjectId))&&(!releaseId.HasValue||x.Cycle.ReleaseId==releaseId)&&(!buildId.HasValue||x.Cycle.BuildId==buildId)).Select(x=>new{x.TestCycleCaseId,x.TestCaseId,x.TestCase.ModuleId,x.Priority,x.CurrentStatus}).ToListAsync(ct);
  var executions=await db.TestExecutions.AsNoTracking().Where(x=>!x.IsDeleted&&!x.CycleCase.Cycle.IsDeleted&&(byProject?x.CycleCase.Cycle.ProjectId==pf:noFilter||allowed.Contains(x.CycleCase.Cycle.ProjectId))&&(!releaseId.HasValue||x.CycleCase.Cycle.ReleaseId==releaseId)&&(!buildId.HasValue||x.BuildId==buildId)).Select(x=>new{x.TesterUserId,x.Status,x.CompletedAt,DisplayName=db.Users.Where(u=>u.UserId==x.TesterUserId).Select(u=>u.DisplayName).First()}).ToListAsync(ct);
  var defects=await db.Defects.AsNoTracking().Where(x=>!x.IsDeleted&&(byProject?x.ProjectId==pf:noFilter||allowed.Contains(x.ProjectId))&&(!releaseId.HasValue||x.ReleaseId==releaseId)&&(!buildId.HasValue||x.BuildId==buildId)).Select(x=>new{x.Severity,x.Status}).ToListAsync(ct);
  decimal Pct(int value,int total)=>total==0?0:Math.Round(value*100m/total,1);
  var coveredSet=coveredIds.ToHashSet();var executed=cycleCases.Count(x=>x.CurrentStatus!="NotRun");var passed=cycleCases.Count(x=>x.CurrentStatus=="Pass");var failed=cycleCases.Count(x=>x.CurrentStatus=="Fail");var blocked=cycleCases.Count(x=>x.CurrentStatus=="Blocked");
  var moduleRows=modules.Select(m=>{var mr=requirements.Where(x=>x.ModuleId==m.ModuleId).ToList();var mc=cases.Where(x=>x.ModuleId==m.ModuleId).ToList();var mx=cycleCases.Where(x=>x.ModuleId==m.ModuleId).ToList();var cov=mr.Count(x=>coveredSet.Contains(x.RequirementId));var mex=mx.Count(x=>x.CurrentStatus!="NotRun");var mp=mx.Count(x=>x.CurrentStatus=="Pass");var mf=mx.Count(x=>x.CurrentStatus=="Fail");var mb=mx.Count(x=>x.CurrentStatus=="Blocked");var rate=Pct(mp,mex);var health=mr.Count==0&&mx.Count==0?"No Data":mb>0||rate<70?"Risk":mf>0||rate<90?"Watch":"Healthy";return new DashboardModuleHealth(m.ModuleId,m.ParentModuleId,m.ModuleCode,m.ModuleName,m.SortOrder,mr.Count,cov,mc.Count,mex,mp,mf,mb,Pct(cov,mr.Count),Pct(mex,mx.Count),rate,health);}).OrderBy(x=>x.SortOrder).ThenBy(x=>x.ModuleCode).ToList();
  var users=executions.GroupBy(x=>new{x.TesterUserId,x.DisplayName}).Select(g=>new DashboardUserPerformance(g.Key.TesterUserId,g.Key.DisplayName,g.Count(),g.Count(x=>x.Status=="Pass"),g.Count(x=>x.Status=="Fail"),g.Count(x=>x.Status=="Blocked"),Pct(g.Count(x=>x.Status=="Pass"),g.Count()),g.Max(x=>x.CompletedAt))).OrderByDescending(x=>x.Executions).ToList();
  var openDefects=defects.Where(x=>x.Status is not ("Resolved" or "Closed" or "Rejected")).ToList();var criticalDefects=openDefects.Count(x=>x.Severity=="Critical");var highDefects=openDefects.Count(x=>x.Severity=="High");var defectQuality=Math.Max(0,100-criticalDefects*40-highDefects*20-openDefects.Count(x=>x.Severity=="Medium")*8-openDefects.Count(x=>x.Severity=="Low")*3);
  var severityDist=openDefects.GroupBy(x=>x.Severity).Select(g=>new DashboardSeveritySlice(g.Key,g.Count(),g.Key switch{"Critical"=>"#dc2626","High"=>"#f59e0b","Medium"=>"#2563eb","Low"=>"#94a3b8",_=>"#94a3b8"})).OrderBy(x=>x.Severity switch{"Critical"=>0,"High"=>1,"Medium"=>2,"Low"=>3,_=>4}).ToList();
  var openP0=cycleCases.Count(x=>(x.CurrentStatus=="Fail"||x.CurrentStatus=="Blocked")&&x.Priority=="P0");var openP1=cycleCases.Count(x=>(x.CurrentStatus=="Fail"||x.CurrentStatus=="Blocked")&&x.Priority=="P1");var coverage=Pct(coveredSet.Count,requirements.Count);var progress=Pct(executed,cycleCases.Count);var passRate=Pct(passed,executed);decimal? overall=requirements.Count>0&&cycleCases.Count>0&&executed>0?Math.Round(coverage*.25m+progress*.25m+passRate*.30m+defectQuality*.20m,1):null;var decision=requirements.Count==0&&cycleCases.Count==0?"NO DATA":openP0>0||criticalDefects>0?"NO-GO":openP1>0||highDefects>0||coverage<90||passRate<90?"CONDITIONAL GO":"GO";
   return new(requirements.Count,coveredSet.Count,coverage,cycleCases.Count,executed,progress,passed,passRate,openP0,openP1,overall,defects.Count,openDefects.Count,criticalDefects,highDefects,defectQuality,decision,moduleRows,users,[new("Pass",passed,"#16a36a"),new("Fail",failed,"#e5484d"),new("Blocked",blocked,"#f59e0b"),new("Not Run",cycleCases.Count-executed,"#94a3b8")],severityDist,DateTime.UtcNow);
  }
  public async Task<DashboardTimeline>GetTimelineAsync(Guid?projectId,Guid?releaseId,Guid?buildId,CancellationToken ct)
  {
   bool byProject=projectId.HasValue;Guid pf=projectId??Guid.Empty;bool noFilter=projectCtx.AllowedProjectIds.Length==0;Guid[] allowed=projectCtx.AllowedProjectIds;
   var releaseQuery=db.Releases.AsNoTracking().Where(x=>byProject?x.ProjectId==pf:noFilter||allowed.Contains(x.ProjectId));
   if(releaseId.HasValue)releaseQuery=releaseQuery.Where(x=>x.ReleaseId==releaseId);
   var releases=await releaseQuery.Where(x=>new[]{"Draft","Testing","Ready"}.Contains(x.Status)).OrderBy(x=>x.PlannedReleaseDate).ThenBy(x=>x.ReleaseCode).Select(x=>new DashboardTimelineRelease(x.ReleaseId,x.ReleaseCode,x.Version,x.Status,x.PlannedReleaseDate,x.ActualReleaseDate)).ToListAsync(ct);
   var cycleQuery=db.TestCycles.AsNoTracking().Where(x=>!x.IsDeleted&&(byProject?x.ProjectId==pf:noFilter||allowed.Contains(x.ProjectId)));
   if(releaseId.HasValue)cycleQuery=cycleQuery.Where(x=>x.ReleaseId==releaseId);
   if(buildId.HasValue)cycleQuery=cycleQuery.Where(x=>x.BuildId==buildId);
   var cycles=await cycleQuery.Where(x=>x.Status=="Draft"||x.Status=="InProgress").OrderBy(x=>x.StartDate).ThenBy(x=>x.CycleCode).Select(x=>new DashboardTimelineCycle(x.TestCycleId,x.CycleCode,x.CycleName,x.Status,x.StartDate,x.EndDate,x.Cases.Count==0?0m:Math.Round(x.Cases.Count(c=>c.CurrentStatus!="NotRun")*100m/x.Cases.Count,2))).ToListAsync(ct);
   return new(releases,cycles);
  }
 }
