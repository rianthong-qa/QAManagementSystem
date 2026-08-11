using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Dashboard;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class DashboardRepository(QaDbContext db):IDashboardRepository
{
 public async Task<DashboardSummary>GetAsync(Guid?projectId,Guid?releaseId,Guid?buildId,CancellationToken ct)
 {
  var modules=await db.Modules.AsNoTracking().Where(x=>x.IsActive&&(!projectId.HasValue||x.ProjectId==projectId)).Select(x=>new{x.ModuleId,x.ModuleName}).ToListAsync(ct);
  var requirements=await db.Requirements.AsNoTracking().Where(x=>!x.IsDeleted&&x.IsInScope&&(!projectId.HasValue||x.ProjectId==projectId)&&(!releaseId.HasValue||x.ReleaseId==releaseId)).Select(x=>new{x.RequirementId,x.ModuleId,x.Priority}).ToListAsync(ct);
  var requirementIds=requirements.Select(x=>x.RequirementId).ToList();
  var coveredIds=await db.RequirementTestCases.AsNoTracking().Where(x=>requirementIds.Contains(x.RequirementId)).Select(x=>x.RequirementId).Distinct().ToListAsync(ct);
  var cases=await db.TestCases.AsNoTracking().Where(x=>!x.IsDeleted&&(!projectId.HasValue||x.ProjectId==projectId)).Select(x=>new{x.TestCaseId,x.ModuleId,x.Priority}).ToListAsync(ct);
  var cycleCases=await db.TestCycleCases.AsNoTracking().Where(x=>!x.Cycle.IsDeleted&&(!projectId.HasValue||x.Cycle.ProjectId==projectId)&&(!releaseId.HasValue||x.Cycle.ReleaseId==releaseId)&&(!buildId.HasValue||x.Cycle.BuildId==buildId)).Select(x=>new{x.TestCycleCaseId,x.TestCaseId,x.TestCase.ModuleId,x.Priority,x.CurrentStatus}).ToListAsync(ct);
  var executions=await db.TestExecutions.AsNoTracking().Where(x=>!x.IsDeleted&&!x.CycleCase.Cycle.IsDeleted&&(!projectId.HasValue||x.CycleCase.Cycle.ProjectId==projectId)&&(!releaseId.HasValue||x.CycleCase.Cycle.ReleaseId==releaseId)&&(!buildId.HasValue||x.BuildId==buildId)).Select(x=>new{x.TesterUserId,x.Status,x.CompletedAt,DisplayName=db.Users.Where(u=>u.UserId==x.TesterUserId).Select(u=>u.DisplayName).First()}).ToListAsync(ct);
  decimal Pct(int value,int total)=>total==0?0:Math.Round(value*100m/total,1);
  var coveredSet=coveredIds.ToHashSet();var executed=cycleCases.Count(x=>x.CurrentStatus!="NotRun");var passed=cycleCases.Count(x=>x.CurrentStatus=="Pass");var failed=cycleCases.Count(x=>x.CurrentStatus=="Fail");var blocked=cycleCases.Count(x=>x.CurrentStatus=="Blocked");
  var moduleRows=modules.Select(m=>{var mr=requirements.Where(x=>x.ModuleId==m.ModuleId).ToList();var mc=cases.Where(x=>x.ModuleId==m.ModuleId).ToList();var mx=cycleCases.Where(x=>x.ModuleId==m.ModuleId).ToList();var cov=mr.Count(x=>coveredSet.Contains(x.RequirementId));var mex=mx.Count(x=>x.CurrentStatus!="NotRun");var mp=mx.Count(x=>x.CurrentStatus=="Pass");var mf=mx.Count(x=>x.CurrentStatus=="Fail");var mb=mx.Count(x=>x.CurrentStatus=="Blocked");var rate=Pct(mp,mex);var health=mb>0||rate<70?"Risk":mf>0||rate<90?"Watch":"Healthy";return new DashboardModuleHealth(m.ModuleId,m.ModuleName,mr.Count,cov,mc.Count,mex,mp,mf,mb,Pct(cov,mr.Count),Pct(mex,mx.Count),rate,health);}).OrderBy(x=>x.ModuleName).ToList();
  var users=executions.GroupBy(x=>new{x.TesterUserId,x.DisplayName}).Select(g=>new DashboardUserPerformance(g.Key.TesterUserId,g.Key.DisplayName,g.Count(),g.Count(x=>x.Status=="Pass"),g.Count(x=>x.Status=="Fail"),g.Count(x=>x.Status=="Blocked"),Pct(g.Count(x=>x.Status=="Pass"),g.Count()),g.Max(x=>x.CompletedAt))).OrderByDescending(x=>x.Executions).ToList();
  var openP0=cycleCases.Count(x=>(x.CurrentStatus=="Fail"||x.CurrentStatus=="Blocked")&&x.Priority=="P0");var openP1=cycleCases.Count(x=>(x.CurrentStatus=="Fail"||x.CurrentStatus=="Blocked")&&x.Priority=="P1");var coverage=Pct(coveredSet.Count,requirements.Count);var progress=Pct(executed,cycleCases.Count);var passRate=Pct(passed,executed);var decision=openP0>0?"NO-GO":openP1>0||coverage<90||passRate<90?"CONDITIONAL GO":"GO";
  return new(requirements.Count,coveredSet.Count,coverage,cycleCases.Count,executed,progress,passed,passRate,openP0,openP1,decision,moduleRows,users,[new("Pass",passed,"#16a36a"),new("Fail",failed,"#e5484d"),new("Blocked",blocked,"#f59e0b"),new("Not Run",cycleCases.Count-executed,"#94a3b8")],DateTime.UtcNow);
 }
}
