using Microsoft.EntityFrameworkCore;using ProMaxx2.QA.Application.Projects;using ProMaxx2.QA.Application.TestManagement;using ProMaxx2.QA.Domain.TestManagement;using ProMaxx2.QA.Application.Common;
namespace ProMaxx2.QA.Infrastructure.Persistence;
public sealed class TestSuiteRepository(QaDbContext db,ProjectAccessContext projectCtx):ITestSuiteRepository
{
 public async Task<PagedResult<TestSuiteListDto>>ListAsync(Guid?projectId,string?search,string?suiteType,string?riskTier,bool?isActive,Guid?moduleId,int page,int size,CancellationToken ct){
    var q=db.TestSuites.AsNoTracking();if(projectId.HasValue)q=q.Where(x=>x.ProjectId==projectId);else if(projectCtx.AllowedProjectIds.Length>0)q=q.Where(x=>projectCtx.AllowedProjectIds.Contains(x.ProjectId));else q=q.Where(_=>false);
    if(!string.IsNullOrWhiteSpace(search))q=q.Where(x=>x.SuiteCode.Contains(search)||x.SuiteName.Contains(search));
    if(!string.IsNullOrWhiteSpace(suiteType))q=q.Where(x=>x.SuiteType==suiteType);
    if(!string.IsNullOrWhiteSpace(riskTier))q=q.Where(x=>x.RiskTier==riskTier);
    if(isActive.HasValue)q=q.Where(x=>x.IsActive==isActive);
    if(moduleId.HasValue)q=q.Where(x=>x.Cases.Any(c=>c.TestCase.ModuleId==moduleId));
    var total=await q.CountAsync(ct);var p=Math.Max(1,page);var s=Math.Clamp(size,20,100);
    var items=await q.OrderByDescending(x=>x.CreatedAt).Skip((p-1)*s).Take(s).Select(x=>new TestSuiteListDto(x.TestSuiteId,x.ProjectId,x.SuiteCode,x.SuiteName,x.SuiteType,x.Description,x.RiskTier,x.IsActive,db.TestCycles.Count(c=>c.TestSuiteId==x.TestSuiteId&&!c.IsDeleted),x.Cases.Count,new List<SuiteModuleDto>(),x.CreatedBy,db.Users.Where(u=>u.UserId==x.CreatedBy).Select(u=>u.DisplayName).FirstOrDefault(),x.CreatedAt)).ToListAsync(ct);
    // Suites aren't scoped to a single module — a suite can span many. Look up every distinct module
    // touched by each suite's cases (for this page only) so the list can show/filter by them.
    var suiteIds=items.Select(x=>x.TestSuiteId).ToList();
    var moduleLinks=await db.TestSuiteCases.AsNoTracking().Where(x=>suiteIds.Contains(x.TestSuiteId)).Select(x=>new{x.TestSuiteId,x.TestCase.ModuleId}).Distinct().ToListAsync(ct);
    var moduleIds=moduleLinks.Select(x=>x.ModuleId).Distinct().ToList();
    var moduleInfo=await db.Modules.AsNoTracking().Where(m=>moduleIds.Contains(m.ModuleId)).ToDictionaryAsync(m=>m.ModuleId,m=>new SuiteModuleDto(m.ModuleId,m.ModuleCode,m.ModuleName),ct);
    var bySuite=moduleLinks.GroupBy(x=>x.TestSuiteId).ToDictionary(g=>g.Key,g=>(IReadOnlyList<SuiteModuleDto>)g.Select(x=>moduleInfo.GetValueOrDefault(x.ModuleId)).Where(x=>x is not null).Select(x=>x!).OrderBy(x=>x.ModuleCode).ToList());
    var result=items.Select(x=>x with{Modules=bySuite.GetValueOrDefault(x.TestSuiteId,Array.Empty<SuiteModuleDto>())}).ToList();
    return new(total,result);
 }
 private IQueryable<TestSuiteDto>Project(IQueryable<TestSuite>q)=>q.Select(x=>new TestSuiteDto(x.TestSuiteId,x.ProjectId,x.SuiteCode,x.SuiteName,x.SuiteType,x.Description,x.RiskTier,x.IsActive,db.TestCycles.Count(c=>c.TestSuiteId==x.TestSuiteId&&!c.IsDeleted),x.Cases.OrderBy(c=>c.SortOrder).Select(c=>new SuiteCaseDto(c.TestCaseId,c.TestCase.TestCaseCode,c.TestCase.Title,c.TestCase.Priority,c.SortOrder,c.IsRequired)).ToList(),db.TestCycles.Where(c=>c.TestSuiteId==x.TestSuiteId).OrderBy(c=>c.IsDeleted).ThenByDescending(c=>c.CreatedAt).Select(c=>new SuiteCycleRefDto(c.TestCycleId,c.CycleCode,c.CycleName,c.Status,c.IsDeleted,db.Builds.Where(b=>b.BuildId==c.BuildId).Select(b=>b.BuildNumber).FirstOrDefault(),c.StartDate,c.EndDate,db.Users.Where(u=>u.UserId==c.OwnerUserId).Select(u=>u.DisplayName).FirstOrDefault(),c.Cases.Count,c.Cases.Count(cc=>cc.CurrentStatus!="NotRun"),c.Cases.Count==0?0:Math.Round(c.Cases.Count(cc=>cc.CurrentStatus!="NotRun")*100m/c.Cases.Count,2))).ToList(),x.CreatedBy,db.Users.Where(u=>u.UserId==x.CreatedBy).Select(u=>u.DisplayName).FirstOrDefault(),x.CreatedAt));
 public Task<TestSuiteDto?>GetAsync(Guid id,CancellationToken ct)=>Project(db.TestSuites.AsNoTracking().Where(x=>x.TestSuiteId==id)).SingleOrDefaultAsync(ct);
 public async Task<TestSuiteDto?>GetForAiAsync(Guid id,int maxCases,CancellationToken ct)=>await db.TestSuites.AsNoTracking().Where(x=>x.TestSuiteId==id).Select(x=>new TestSuiteDto(x.TestSuiteId,x.ProjectId,x.SuiteCode,x.SuiteName,x.SuiteType,x.Description,x.RiskTier,x.IsActive,db.TestCycles.Count(c=>c.TestSuiteId==x.TestSuiteId&&!c.IsDeleted),x.Cases.OrderBy(c=>c.SortOrder).Take(maxCases).Select(c=>new SuiteCaseDto(c.TestCaseId,c.TestCase.TestCaseCode,c.TestCase.Title,c.TestCase.Priority,c.SortOrder,c.IsRequired)).ToList(),new List<SuiteCycleRefDto>(),x.CreatedBy,null,x.CreatedAt)).SingleOrDefaultAsync(ct);
 public async Task<TestSuite?>FindAsync(Guid id,bool activeOnly,CancellationToken ct){var allowed=projectCtx.AllowedProjectIds;var x=activeOnly?await db.TestSuites.SingleOrDefaultAsync(x=>x.TestSuiteId==id&&x.IsActive,ct):await db.TestSuites.SingleOrDefaultAsync(x=>x.TestSuiteId==id,ct);if(x is not null&&allowed.Length>0&&!allowed.Contains(x.ProjectId))return null;return x;}
 public Task<bool>CodeExistsAsync(Guid projectId,string code,Guid?excludeId,CancellationToken ct)=>db.TestSuites.AnyAsync(x=>x.ProjectId==projectId&&x.SuiteCode==code&&(!excludeId.HasValue||x.TestSuiteId!=excludeId),ct);
 public Task<bool>NameExistsAsync(Guid projectId,string name,Guid?excludeId,CancellationToken ct)=>db.TestSuites.AnyAsync(x=>x.ProjectId==projectId&&x.SuiteName==name&&(!excludeId.HasValue||x.TestSuiteId!=excludeId),ct);
 public async Task<IReadOnlyList<string>>ListCodesAsync(Guid projectId,string prefix,CancellationToken ct)=>await db.TestSuites.AsNoTracking().Where(x=>x.ProjectId==projectId&&x.SuiteCode.StartsWith(prefix)).Select(x=>x.SuiteCode).ToListAsync(ct);
 // Deliberately NOT filtering by !IsDeleted here: a soft-deleted TestCycle row still physically exists
 // and still trips the FK_TestCycles_TestSuites_TestSuiteId constraint on a real DELETE of the suite —
 // so a permanent suite delete cascades into permanently deleting every linked cycle (and, via each
 // cycle's own Cascade-configured FKs, its TestCycleCases/TestExecutions/TestStepResults/Assignments too).
 public async Task DeleteLinkedCyclesAsync(Guid id,CancellationToken ct){var cycles=await db.TestCycles.Where(c=>c.TestSuiteId==id).ToListAsync(ct);if(cycles.Count>0)db.TestCycles.RemoveRange(cycles);}
 public Task AddAsync(TestSuite suite,CancellationToken ct)=>db.TestSuites.AddAsync(suite,ct).AsTask();
 public async Task AddCasesAsync(Guid id,IReadOnlyList<Guid>caseIds,bool required,CancellationToken ct){var existing=await db.TestSuiteCases.Where(x=>x.TestSuiteId==id).Select(x=>x.TestCaseId).ToListAsync(ct);var valid=await db.TestCases.Where(x=>caseIds.Contains(x.TestCaseId)&&!x.IsDeleted).Select(x=>x.TestCaseId).ToListAsync(ct);var next=await db.TestSuiteCases.Where(x=>x.TestSuiteId==id).Select(x=>(int?)x.SortOrder).MaxAsync(ct)??0;db.TestSuiteCases.AddRange(valid.Except(existing).Select((caseId,index)=>new TestSuiteCase(id,caseId,next+index+1,required)));}
 public async Task UpdateCaseAsync(Guid id,Guid caseId,int sortOrder,bool required,CancellationToken ct){var link=await db.TestSuiteCases.SingleOrDefaultAsync(x=>x.TestSuiteId==id&&x.TestCaseId==caseId,ct)??throw new EntityNotFoundException("Suite case not found.");var previousOrder=link.SortOrder;var occupied=await db.TestSuiteCases.SingleOrDefaultAsync(x=>x.TestSuiteId==id&&x.TestCaseId!=caseId&&x.SortOrder==sortOrder,ct);occupied?.Update(previousOrder,occupied.IsRequired);link.Update(sortOrder,required);}
 public async Task RemoveCaseAsync(Guid id,Guid caseId,CancellationToken ct){var link=await db.TestSuiteCases.FindAsync([id,caseId],ct);if(link is not null)db.TestSuiteCases.Remove(link);}
 public Task DeleteAsync(TestSuite suite,CancellationToken ct){db.TestSuites.Remove(suite);return Task.CompletedTask;}
 public Task SaveAsync(CancellationToken ct)=>db.SaveChangesAsync(ct);
}
