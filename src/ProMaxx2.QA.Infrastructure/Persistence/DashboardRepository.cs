using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Dashboard;
using ProMaxx2.QA.Domain.Dashboard;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Application.Common;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class DashboardRepository(QaDbContext db,ProjectAccessContext projectCtx,Microsoft.Extensions.Options.IOptionsMonitor<ProMaxx2.QA.Application.Governance.DefectGateOptions>? defectGate=null):IDashboardRepository
{
 private const string ShareAlphabet="23456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
 public async Task<DashboardShareScope>CreateShareAsync(Guid?projectId,Guid?releaseId,Guid?buildId,DateTime expiresAt,CancellationToken ct){string code;do{code=string.Concat(Enumerable.Range(0,8).Select(_=>ShareAlphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(ShareAlphabet.Length)]));}while(await db.DashboardShares.AnyAsync(x=>x.Code==code,ct));var entity=new DashboardShare(code,projectId,releaseId,buildId,expiresAt);await db.DashboardShares.AddAsync(entity,ct);await db.SaveChangesAsync(ct);return new(code,projectId,releaseId,buildId,expiresAt);}
 public async Task<bool>RevokeShareAsync(string code,IReadOnlyCollection<Guid>allowedProjectIds,CancellationToken ct){var entity=await db.DashboardShares.SingleOrDefaultAsync(x=>x.Code==code,ct);if(entity is null||entity.ProjectId is not {} projectId||!allowedProjectIds.Contains(projectId))return false;entity.Revoke(DateTime.UtcNow);await db.SaveChangesAsync(ct);return true;}
 public async Task<DashboardShareScope?>FindShareAsync(string code,CancellationToken ct)=>await db.DashboardShares.AsNoTracking().Where(x=>x.Code==code&&x.ExpiresAt>DateTime.UtcNow).Select(x=>new DashboardShareScope(x.Code,x.ProjectId,x.ReleaseId,x.BuildId,x.ExpiresAt)).SingleOrDefaultAsync(ct);
 public async Task<DashboardSummary>GetAsync(Guid?projectId,Guid?releaseId,Guid?buildId,CancellationToken ct)
 {
  bool byProject=projectId.HasValue;Guid pf=projectId??Guid.Empty;bool noFilter=projectCtx.AllowedProjectIds.Length==0;Guid[] allowed=projectCtx.AllowedProjectIds;
  // Module ที่ซ่อน (IsActive=false) ไม่นำมาคำนวณทั้งหน้า: ตัด Requirement/Test Case/ผลทดสอบ/Defect ที่อยู่ใน Module นั้นออก (ข้อมูลที่ไม่ระบุ Module ยังนับ)
  var hiddenModules=db.Modules.Where(m=>!m.IsActive);
  var modules=await db.Modules.AsNoTracking().Where(x=>x.IsActive&&(byProject?x.ProjectId==pf:noFilter||allowed.Contains(x.ProjectId))).Select(x=>new{x.ModuleId,x.ParentModuleId,x.ModuleCode,x.ModuleName,x.SortOrder}).ToListAsync(ct);
  var requirements=await db.Requirements.AsNoTracking().Where(x=>!x.IsDeleted&&x.IsInScope&&!hiddenModules.Any(m=>m.ModuleId==x.ModuleId)&&(byProject?x.ProjectId==pf:noFilter||allowed.Contains(x.ProjectId))&&(!releaseId.HasValue||x.ReleaseId==releaseId)).Select(x=>new{x.RequirementId,x.ModuleId,x.Priority}).ToListAsync(ct);
  var requirementIds=requirements.Select(x=>x.RequirementId).ToList();
  // Covered = ผูกกับ Test Case ที่ยังไม่ถูกลบและ Ready — กฎเดียวกับ RTM และ Release Sign-off (ReleaseReadinessQueries)
  var coveredIds=await ReleaseReadinessQueries.CoveredRequirementIds(db,requirementIds).ToListAsync(ct);
  var cases=await db.TestCases.AsNoTracking().Where(x=>!x.IsDeleted&&!hiddenModules.Any(m=>m.ModuleId==x.ModuleId)&&(byProject?x.ProjectId==pf:noFilter||allowed.Contains(x.ProjectId))).Select(x=>new{x.TestCaseId,x.ModuleId,x.Priority}).ToListAsync(ct);
  var cycleCases=await db.TestCycleCases.AsNoTracking().Where(x=>!x.Cycle.IsDeleted&&!x.TestCase.IsDeleted&&!hiddenModules.Any(m=>m.ModuleId==x.TestCase.ModuleId)&&(byProject?x.Cycle.ProjectId==pf:noFilter||allowed.Contains(x.Cycle.ProjectId))&&(!releaseId.HasValue||x.Cycle.ReleaseId==releaseId)&&(!buildId.HasValue||x.Cycle.BuildId==buildId)).Select(x=>new{x.TestCycleCaseId,x.TestCaseId,x.TestCase.ModuleId,x.Priority,x.CurrentStatus}).ToListAsync(ct);
  var executions=await db.TestExecutions.AsNoTracking().Where(x=>!x.IsDeleted&&!x.CycleCase.Cycle.IsDeleted&&!x.CycleCase.TestCase.IsDeleted&&!hiddenModules.Any(m=>m.ModuleId==x.CycleCase.TestCase.ModuleId)&&(byProject?x.CycleCase.Cycle.ProjectId==pf:noFilter||allowed.Contains(x.CycleCase.Cycle.ProjectId))&&(!releaseId.HasValue||x.CycleCase.Cycle.ReleaseId==releaseId)&&(!buildId.HasValue||x.BuildId==buildId)).Select(x=>new{x.TesterUserId,x.Status,x.CompletedAt,DisplayName=db.Users.Where(u=>u.UserId==x.TesterUserId).Select(u=>u.DisplayName).First()}).ToListAsync(ct);
  // Defect ที่สรุปในหน้า Dashboard กรองตามขอบเขตที่เลือก: Release = ทั้ง Release, เลือก Build = เฉพาะที่พบใน Build นั้น (Found in Build)
  // ตามที่ผู้ใช้กำหนด 2026-11-11 — คำตัดสิน GO/NO-GO ยังนับ Defect ทั้ง Release (gateDefects ด้านล่าง) เพื่อไม่ให้ของค้างจาก Build ก่อนหลุด
  var defects=await ScopedDefects(projectId,releaseId,buildId).Select(x=>new{x.Severity,x.Status,x.ModuleId,x.BuildId}).ToListAsync(ct);
  int? newDefectsInBuild=buildId.HasValue?defects.Count:null;
  decimal Pct(int value,int total)=>total==0?0:Math.Round(value*100m/total,1);
  var coveredSet=coveredIds.ToHashSet();var executed=cycleCases.Count(x=>x.CurrentStatus!="NotRun");var passed=cycleCases.Count(x=>x.CurrentStatus=="Pass");var failed=cycleCases.Count(x=>x.CurrentStatus=="Fail");var blocked=cycleCases.Count(x=>x.CurrentStatus=="Blocked");var skipped=cycleCases.Count(x=>x.CurrentStatus=="Skipped");var inProgress=cycleCases.Count(x=>x.CurrentStatus=="InProgress");
  // ความคืบหน้าการทดสอบ = จำนวน Test Case (จากหน้า Test Case) ที่ถูกทดสอบจริงอย่างน้อย 1 ครั้ง หารด้วยจำนวน Test Case ทั้งหมด
  // เมื่อเลือก Release/Build ไว้ ให้จำกัดขอบเขต "ทั้งหมด" เหลือเฉพาะ Test Case ที่ถูกดึงเข้า Cycle ในขอบเขตนั้นจริง (ไม่ใช่ทั้งโปรเจกต์) ให้สอดคล้องกับตัวเลขอื่นในหน้า Dashboard
  var testedCaseIds=cycleCases.Where(x=>x.CurrentStatus!="NotRun").Select(x=>x.TestCaseId).Distinct().ToHashSet();
  var totalTestCaseCount=(releaseId.HasValue||buildId.HasValue)?cycleCases.Select(x=>x.TestCaseId).Distinct().Count():cases.Count;
  var testedTestCaseCount=cases.Count(x=>testedCaseIds.Contains(x.TestCaseId));
  var testCaseProgress=totalTestCaseCount>0?Math.Round(testedTestCaseCount*100m/totalTestCaseCount,1):0m;
  // จำนวน Test Case ต่อโมดูล: เลือก Release/Build = นับเฉพาะ Test Case ที่อยู่ใน Cycle ของขอบเขตนั้น (ไม่ซ้ำ) แบบเดียวกับ totalTestCaseCount ด้านบน — ไม่เลือก = ทั้ง Project
  var moduleRows=modules.Select(m=>{var mr=requirements.Where(x=>x.ModuleId==m.ModuleId).ToList();var mc=cases.Where(x=>x.ModuleId==m.ModuleId).ToList();var mx=cycleCases.Where(x=>x.ModuleId==m.ModuleId).ToList();var cov=mr.Count(x=>coveredSet.Contains(x.RequirementId));var mex=mx.Count(x=>x.CurrentStatus!="NotRun");var mp=mx.Count(x=>x.CurrentStatus=="Pass");var mf=mx.Count(x=>x.CurrentStatus=="Fail");var mb=mx.Count(x=>x.CurrentStatus=="Blocked");var rate=Pct(mp,mex);var health=mr.Count==0&&mx.Count==0?"No Data":mb>0||rate<70?"Risk":mf>0||rate<90?"Watch":"Healthy";var openModuleDefects=defects.Count(x=>x.ModuleId==m.ModuleId&&DefectStatusGroups.NotDone.Contains(x.Status));return new DashboardModuleHealth(m.ModuleId,m.ParentModuleId,m.ModuleCode,m.ModuleName,m.SortOrder,mr.Count,cov,(releaseId.HasValue||buildId.HasValue)?mx.Select(x=>x.TestCaseId).Distinct().Count():mc.Count,mex,mp,mf,mb,Pct(cov,mr.Count),Pct(mex,mx.Count),rate,health,openModuleDefects,mx.Count);}).OrderBy(x=>x.SortOrder).ThenBy(x=>x.ModuleCode).ToList();
  var users=executions.GroupBy(x=>new{x.TesterUserId,x.DisplayName}).Select(g=>new DashboardUserPerformance(g.Key.TesterUserId,g.Key.DisplayName,g.Count(),g.Count(x=>x.Status=="Pass"),g.Count(x=>x.Status=="Fail"),g.Count(x=>x.Status=="Blocked"),Pct(g.Count(x=>x.Status=="Pass"),g.Count()),g.Max(x=>x.CompletedAt))).OrderByDescending(x=>x.Executions).ToList();
  // คำตัดสินใช้ ReleaseGate ชุดเดียวกับ Release Sign-off: Defect ทั้ง Release/Project (ไม่กรอง Build — ของค้างจาก Build ก่อนยังกันการปล่อย), Smoke เฉพาะเมื่อเลือก Build และตั้ง Smoke policy
  // ส่วนที่กันการปล่อย (P0/P1 ค้าง, Defect ค้าง) นับรวม Module ที่ซ่อน — ไม่ให้ซ่อน Module แล้วเปลี่ยน NO-GO เป็น GO ได้; ตัวเลขเกณฑ์ GateCritical/GateHigh/P0/P1 มาจากชุดเดียวกับคำตัดสิน (CriticalDefects/HighDefects = สรุป Defect ตามขอบเขตที่เลือก)
  var gateDefects=await ReleaseReadinessQueries.DefectCountsAsync(db,db.Defects.Where(x=>(byProject?x.ProjectId==pf:noFilter||allowed.Contains(x.ProjectId))&&(!releaseId.HasValue||x.ReleaseId==releaseId)),ct);
  var openBlockers=await db.TestCycleCases.AsNoTracking().Where(x=>!x.Cycle.IsDeleted&&!x.TestCase.IsDeleted&&(byProject?x.Cycle.ProjectId==pf:noFilter||allowed.Contains(x.Cycle.ProjectId))&&(!releaseId.HasValue||x.Cycle.ReleaseId==releaseId)&&(!buildId.HasValue||x.Cycle.BuildId==buildId)&&(x.CurrentStatus=="Fail"||x.CurrentStatus=="Blocked")&&(x.Priority=="P0"||x.Priority=="P1")).GroupBy(x=>x.Priority).Select(g=>new{Priority=g.Key,Count=g.Count()}).ToListAsync(ct);
  var openDefects=defects.Where(x=>DefectStatusGroups.NotDone.Contains(x.Status)).ToList();var criticalDefects=openDefects.Count(x=>x.Severity=="Critical");var highDefects=openDefects.Count(x=>x.Severity=="High");var defectQuality=Math.Max(0,100-criticalDefects*40-highDefects*20-openDefects.Count(x=>x.Severity=="Medium")*8-openDefects.Count(x=>x.Severity=="Low")*3);
  var severityDist=openDefects.GroupBy(x=>x.Severity).Select(g=>new DashboardSeveritySlice(g.Key,g.Count(),g.Key switch{"Critical"=>"#dc2626","High"=>"#f59e0b","Medium"=>"#2563eb","Low"=>"#94a3b8",_=>"#94a3b8"})).OrderBy(x=>x.Severity switch{"Critical"=>0,"High"=>1,"Medium"=>2,"Low"=>3,_=>4}).ToList();
  var openP0=openBlockers.Where(x=>x.Priority=="P0").Sum(x=>x.Count);var openP1=openBlockers.Where(x=>x.Priority=="P1").Sum(x=>x.Count);var coverage=Pct(coveredSet.Count,requirements.Count);var progress=Pct(executed,cycleCases.Count);var passRate=Pct(passed,executed);decimal? overall=requirements.Count>0&&cycleCases.Count>0&&executed>0?Math.Round(coverage*.25m+progress*.25m+passRate*.30m+defectQuality*.20m,1):null;
   var hasApprovedRisk=await db.RiskAcceptances.AsNoTracking().AnyAsync(r=>!r.IsDeleted&&r.Status=="Approved"&&(releaseId.HasValue?r.ReleaseId==releaseId:byProject?r.ProjectId==pf:noFilter||allowed.Contains(r.ProjectId)),ct);
   var smokeProjectId=buildId.HasValue?await db.Builds.AsNoTracking().Where(b=>b.BuildId==buildId).Select(b=>(Guid?)db.Releases.Where(r=>r.ReleaseId==b.ReleaseId).Select(r=>r.ProjectId).First()).FirstOrDefaultAsync(ct):null;
   var smoke=smokeProjectId is {} sp?await ReleaseReadinessQueries.SmokeAsync(db,sp,buildId,ct):new SmokeCheck(ReleaseReadinessQueries.SmokeNotConfigured,true);
   var gate=ProMaxx2.QA.Domain.Governance.ReleaseGate.Assess(new(openP0,openP1,hasApprovedRisk,smoke.Passed,gateDefects,requirements.Count==0?null:coverage,executed==0?null:passRate,defectGate?.CurrentValue.GateEnabled??false));
   var noData=requirements.Count==0&&cycleCases.Count==0;
   // ไม่มีผลทดสอบ = ยังประเมินไม่ได้ (ไม่ให้ GO โดยไม่มีหลักฐาน) แต่ Defect ทำให้ผลแย่ลงได้เสมอ: Critical ค้าง = NO-GO แม้ยังไม่มี Cycle
   var noDataCritical=noData&&gateDefects.OpenCritical>0;
   // เลือก Build แล้ว Critical/High ของทั้ง Release มากกว่าที่พบใน Build นี้ = คำตัดสินมาจาก Defect ของ Build อื่น — บอกไว้ในเหตุผล
   var gateWiderThanBuild=buildId.HasValue&&(gateDefects.OpenCritical!=criticalDefects||gateDefects.OpenHigh!=highDefects);
   var decision=noDataCritical?"NO-GO":noData?"NO DATA":gate.Decision switch{ProMaxx2.QA.Domain.Governance.ReleaseDecision.Go=>"GO",ProMaxx2.QA.Domain.Governance.ReleaseDecision.ConditionalGo=>"CONDITIONAL GO",_=>"NO-GO"};
   var projectName=projectId.HasValue?await db.Projects.AsNoTracking().Where(x=>x.ProjectId==projectId.Value).Select(x=>x.ProjectName).FirstOrDefaultAsync(ct):null;
   var releaseInfo=releaseId.HasValue?await db.Releases.AsNoTracking().Where(x=>x.ReleaseId==releaseId.Value).Select(x=>new{x.ReleaseCode,x.Version}).FirstOrDefaultAsync(ct):null;
   var buildNumber=buildId.HasValue?await db.Builds.AsNoTracking().Where(x=>x.BuildId==buildId.Value).Select(x=>x.BuildNumber).FirstOrDefaultAsync(ct):null;
   return new(requirements.Count,coveredSet.Count,coverage,cycleCases.Count,executed,progress,passed,passRate,openP0,openP1,overall,defects.Count,openDefects.Count,criticalDefects,highDefects,defectQuality,decision,moduleRows,users,[new("Pass",passed,"#16a36a"),new("Fail",failed,"#e5484d"),new("Blocked",blocked,"#f59e0b"),new("Skipped",skipped,"#8b5cf6"),new("In Progress",inProgress,"#2563eb"),new("Not Run",cycleCases.Count-executed,"#94a3b8")],severityDist,totalTestCaseCount,testedTestCaseCount,testCaseProgress,executions.Count,DateTime.UtcNow,projectName,releaseInfo?.ReleaseCode,releaseInfo?.Version,buildNumber,noDataCritical?$"พบ Critical Defect ที่ยังไม่ได้แก้{(buildId.HasValue?"ใน Release นี้":"")} {gateDefects.OpenCritical} รายการ · ยังไม่มีผลทดสอบ":noData?NoDataReason(openDefects.Count,criticalDefects,highDefects):gateWiderThanBuild?$"{gate.Reason} · เกณฑ์ Defect นับทั้ง Release":gate.Reason,newDefectsInBuild,gateDefects.BlockersWithoutRisk,gateDefects.BlockersWithRisk,gateDefects.OpenCritical,gateDefects.OpenHigh);
  }
  /// <summary>Defect ในขอบเขต Dashboard: ไม่ลบ, ไม่อยู่ใน Module ที่ซ่อน, Project ที่เข้าถึงได้, Release และ (เลือก Build) พบใน Build นี้</summary>
  private IQueryable<Defect>ScopedDefects(Guid?projectId,Guid?releaseId,Guid?buildId)
  {
   bool byProject=projectId.HasValue;Guid pf=projectId??Guid.Empty;bool noFilter=projectCtx.AllowedProjectIds.Length==0;Guid[] allowed=projectCtx.AllowedProjectIds;
   var hiddenModules=db.Modules.Where(m=>!m.IsActive);
   var q=db.Defects.AsNoTracking().Where(x=>!x.IsDeleted&&!hiddenModules.Any(m=>m.ModuleId==x.ModuleId)&&(byProject?x.ProjectId==pf:noFilter||allowed.Contains(x.ProjectId))&&(!releaseId.HasValue||x.ReleaseId==releaseId));
   return buildId.HasValue?q.Where(x=>x.BuildId==buildId):q;
  }
  /// <summary>มุมมองผู้บริหาร/Dev: สถานะ, อายุ, โมดูล × ความรุนแรง, แนวโน้ม 30 วัน และ (includeItems) รายการที่ต้องดูก่อน/รอตรวจซ้ำ — ลิงก์แชร์ไม่ส่งรายการ</summary>
  public async Task<DashboardDefectInsights>GetDefectInsightsAsync(Guid?projectId,Guid?releaseId,Guid?buildId,Guid?assigneeUserId,bool includeItems,CancellationToken ct)
  {
   var scope=ScopedDefects(projectId,releaseId,buildId);
   if(assigneeUserId.HasValue)scope=scope.Where(x=>x.AssigneeUserId==assigneeUserId);
   var rows=await scope.Select(x=>new{x.Severity,x.Status,x.ModuleId,x.BuildId,x.CreatedAt,x.ResolvedAt,x.ClosedAt,x.ReopenCount,Crm=x.CrmTicketId!=null}).ToListAsync(ct);
   var now=DateTime.UtcNow;
   var open=rows.Where(x=>DefectStatusGroups.NotDone.Contains(x.Status)).ToList();
   var unresolved=open.Where(x=>DefectStatusGroups.Unresolved.Contains(x.Status)).ToList();
   // อายุ = วันนับจากแจ้ง ของ Defect ที่ Dev ยังไม่ได้แก้ (Open/In Progress)
   var ages=unresolved.Select(x=>Math.Max(0,(int)(now-x.CreatedAt).TotalDays)).ToList();
   decimal? averageAge=ages.Count==0?null:Math.Round((decimal)ages.Average(),1);
   int[] ageBuckets=[ages.Count(a=>a<=3),ages.Count(a=>a is >=4 and <=7),ages.Count(a=>a is >=8 and <=14),ages.Count(a=>a>14)];
   var newRows=buildId.HasValue?rows.Where(x=>x.BuildId==buildId).ToList():null;
   var moduleIds=open.Where(x=>x.ModuleId.HasValue).Select(x=>x.ModuleId!.Value).Distinct().ToList();
   var moduleInfo=await db.Modules.AsNoTracking().Where(m=>moduleIds.Contains(m.ModuleId)).Select(m=>new{m.ModuleId,m.ModuleCode,m.ModuleName}).ToDictionaryAsync(m=>m.ModuleId,ct);
   var modules=open.GroupBy(x=>x.ModuleId).Select(g=>{var info=g.Key is {} id&&moduleInfo.TryGetValue(id,out var mi)?mi:null;return new DashboardModuleDefects(g.Key,info?.ModuleCode,info?.ModuleName??"ไม่ระบุโมดูล",g.Count(x=>x.Severity=="Critical"),g.Count(x=>x.Severity=="High"),g.Count(x=>x.Severity=="Medium"),g.Count(x=>x.Severity=="Low"),g.Count());})
    .OrderByDescending(x=>x.Total).ThenByDescending(x=>x.Critical).ThenByDescending(x=>x.High).ThenBy(x=>x.ModuleName,StringComparer.Ordinal).ToList();
   // แนวโน้ม 30 วัน (เวลาไทย): เข้าใหม่ = วันที่แจ้ง, แก้แล้ว/ปิด = ResolvedAt (Resolved/Closed) หรือ ClosedAt (Rejected) — Reopen ล้างค่าเหล่านี้จึงไม่นับซ้ำ
   static DateOnly Day(DateTime utc)=>DateOnly.FromDateTime(utc.AddHours(7));
   var today=Day(now);
   var created=rows.GroupBy(x=>Day(x.CreatedAt)).ToDictionary(g=>g.Key,g=>g.Count());
   var closed=rows.Where(x=>(x.ResolvedAt??x.ClosedAt).HasValue).GroupBy(x=>Day((x.ResolvedAt??x.ClosedAt)!.Value)).ToDictionary(g=>g.Key,g=>g.Count());
   var trend=Enumerable.Range(0,30).Select(i=>today.AddDays(i-29)).Select(d=>new DashboardDefectTrendPoint(d,created.GetValueOrDefault(d),closed.GetValueOrDefault(d))).ToList();
   List<DashboardDefectItem>attention=[],retest=[];
   if(includeItems)
   {
    var newBuild=buildId??Guid.Empty;
    IQueryable<ItemRow>Project(IQueryable<Defect>q)=>q.Select(x=>new ItemRow(x.DefectId,x.DefectCode,x.Title,x.Severity,x.Status,db.Modules.Where(m=>m.ModuleId==x.ModuleId).Select(m=>m.ModuleName).FirstOrDefault(),db.Builds.Where(b=>b.BuildId==x.BuildId).Select(b=>b.BuildNumber).FirstOrDefault(),x.CreatedAt,x.BuildId==newBuild,x.CrmTicketId!=null,x.ReopenCount,x.ResolvedAt));
    DashboardDefectItem Map(ItemRow x)=>new(x.DefectId,x.DefectCode,x.Title,x.Severity,x.Status,x.ModuleName,x.FoundIn,x.CreatedAt,Math.Max(0,(int)(now-x.CreatedAt).TotalDays),x.IsNew,x.Crm,x.ReopenCount,x.ResolvedAt);
    // ต้องดูก่อน: ความรุนแรง → ค้างนานสุด (เฉพาะที่ Dev ยังไม่ได้แก้)
    attention=(await Project(scope.Where(x=>DefectStatusGroups.Unresolved.Contains(x.Status)).OrderBy(x=>x.Severity=="Critical"?0:x.Severity=="High"?1:x.Severity=="Medium"?2:3).ThenBy(x=>x.CreatedAt).Take(8)).ToListAsync(ct)).Select(Map).ToList();
    retest=(await Project(scope.Where(x=>DefectStatusGroups.AwaitingVerification.Contains(x.Status)).OrderBy(x=>x.ResolvedAt).Take(10)).ToListAsync(ct)).Select(Map).ToList();
   }
   return new(open.Count,unresolved.Count,open.Count(x=>x.Status=="In Progress"),open.Count(x=>DefectStatusGroups.AwaitingVerification.Contains(x.Status)),
    open.Count(x=>x.Severity=="Critical"),open.Count(x=>x.Severity=="High"),open.Count(x=>x.Severity=="Medium"),open.Count(x=>x.Severity=="Low"),
    newRows?.Count,newRows?.Count(x=>x.Severity is "High" or "Critical"),rows.Count(x=>x.Status=="Closed"),rows.Count(x=>x.Status=="Rejected"),
    open.Count(x=>x.ReopenCount>0),open.Count(x=>x.Crm),averageAge,ageBuckets,modules,trend,attention,retest,now);
  }
  private sealed record ItemRow(Guid DefectId,string DefectCode,string Title,string Severity,string Status,string? ModuleName,string? FoundIn,DateTime CreatedAt,bool IsNew,bool Crm,int ReopenCount,DateTime? ResolvedAt);
  /// <summary>เหตุผลของ NO DATA — บอก Defect ที่ค้างในขอบเขตด้วย ผู้ดูจะได้ไม่เข้าใจว่าไม่มีปัญหา</summary>
  public static string NoDataReason(int open,int critical,int high)
  {
   const string baseReason="ยังไม่มี Requirement หรือ Test Cycle สำหรับประเมิน";
   if(open==0)return baseReason;
   var parts=string.Join(", ",new[]{critical>0?$"Critical {critical}":null,high>0?$"High {high}":null}.Where(x=>x is not null));
   return $"{baseReason} · มี Defect ค้าง {open} รายการ{(parts.Length>0?$" ({parts})":"")}";
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
