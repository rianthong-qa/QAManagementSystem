using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController,Route("api/v1/defects"),Authorize(Policy="DefectEdit"),RequireProjectAccess]
public sealed class DefectsController(QaDbContext db,ProjectAccessContext projectCtx):ControllerBase
{
 [HttpGet]public async Task<IReadOnlyList<DefectDto>>List([FromQuery]Guid?projectId,[FromQuery]Guid?releaseId,[FromQuery]Guid?buildId,[FromQuery]string?search,CancellationToken ct){var q=db.Defects.AsNoTracking().Where(x=>!x.IsDeleted);if(projectId.HasValue)q=q.Where(x=>x.ProjectId==projectId);else if(projectCtx.AllowedProjectIds.Length>0)q=q.Where(x=>projectCtx.AllowedProjectIds.Contains(x.ProjectId));else q=q.Where(_=>false);if(releaseId.HasValue)q=q.Where(x=>x.ReleaseId==releaseId);if(buildId.HasValue)q=q.Where(x=>x.BuildId==buildId);if(!string.IsNullOrWhiteSpace(search))q=q.Where(x=>x.DefectCode.Contains(search)||x.Title.Contains(search));return await q.OrderByDescending(x=>x.CreatedAt).Select(x=>new DefectDto(x.DefectId,x.ProjectId,x.ReleaseId,x.BuildId,x.ModuleId,x.DefectCode,x.Title,x.Severity,x.Status,x.CreatedAt)).ToListAsync(ct);}
 [HttpPost]public async Task<ActionResult<DefectDto>>Create(SaveDefectRequest r,CancellationToken ct){if(!await db.Projects.AnyAsync(x=>x.ProjectId==r.ProjectId&&x.IsActive,ct))return BadRequest("Project is not active.");var prefix=await db.Projects.Where(x=>x.ProjectId==r.ProjectId).Select(x=>x.ProjectCode).SingleAsync(ct)+"-DEF-";var codes=await db.Defects.Where(x=>x.ProjectId==r.ProjectId&&x.DefectCode.StartsWith(prefix)).Select(x=>x.DefectCode).ToListAsync(ct);var next=codes.Select(x=>int.TryParse(x[prefix.Length..],out var n)?n:0).DefaultIfEmpty().Max()+1;var entity=new Defect(r.ProjectId,r.ReleaseId,r.BuildId,r.ModuleId,$"{prefix}{next:000}",r.Title,r.Severity,r.Status,UserId());await db.Defects.AddAsync(entity,ct);await db.SaveChangesAsync(ct);return Ok(Map(entity));}
 [HttpPut("{id:guid}")]public async Task<ActionResult<DefectDto>>Update(Guid id,SaveDefectRequest r,CancellationToken ct){var entity=await db.Defects.SingleOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null)return NotFound();entity.Update(r.Title,r.Severity,r.Status,UserId());await db.SaveChangesAsync(ct);return Ok(Map(entity));}
 [HttpDelete("{id:guid}")]public async Task<IActionResult>Delete(Guid id,CancellationToken ct){var entity=await db.Defects.SingleOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null)return NotFound();entity.SoftDelete(UserId());await db.SaveChangesAsync(ct);return NoContent();}
 private Guid?UserId()=>Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("sub"),out var id)?id:null;private static DefectDto Map(Defect x)=>new(x.DefectId,x.ProjectId,x.ReleaseId,x.BuildId,x.ModuleId,x.DefectCode,x.Title,x.Severity,x.Status,x.CreatedAt);
}
public sealed record SaveDefectRequest(Guid ProjectId,Guid?ReleaseId,Guid?BuildId,Guid?ModuleId,string Title,string Severity="Medium",string Status="Open");
public sealed record DefectDto(Guid DefectId,Guid ProjectId,Guid?ReleaseId,Guid?BuildId,Guid?ModuleId,string DefectCode,string Title,string Severity,string Status,DateTime CreatedAt);
