using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Identity;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController,Route("api/v1/defects"),Authorize(Policy="DefectView"),RequireProjectAccess]
public sealed class DefectsController(QaDbContext db,ProjectAccessContext projectCtx,DefectActivityService activityService,CrmSendToCrmService crmSendService,ILogger<DefectsController> logger):ControllerBase
{
 [HttpGet,Authorize(Policy="DefectView")]public async Task<ActionResult<DefectListResultDto>>List([FromQuery]Guid?projectId,[FromQuery]Guid?releaseId,[FromQuery]Guid?buildId,[FromQuery]Guid?moduleId,[FromQuery]Guid?assigneeUserId,[FromQuery]string?severity,[FromQuery]string?status,[FromQuery]string?search,[FromQuery]int page=1,[FromQuery]int size=20,CancellationToken ct=default){var q=db.Defects.AsNoTracking().Where(x=>!x.IsDeleted);if(projectId.HasValue)q=q.Where(x=>x.ProjectId==projectId);else if(projectCtx.AllowedProjectIds.Length>0)q=q.Where(x=>projectCtx.AllowedProjectIds.Contains(x.ProjectId));else q=q.Where(_=>false);if(releaseId.HasValue)q=q.Where(x=>x.ReleaseId==releaseId);if(buildId.HasValue)q=q.Where(x=>x.BuildId==buildId);if(moduleId.HasValue)q=q.Where(x=>x.ModuleId==moduleId);if(assigneeUserId.HasValue)q=q.Where(x=>x.AssigneeUserId==assigneeUserId);if(!string.IsNullOrWhiteSpace(severity))q=q.Where(x=>x.Severity==severity);if(!string.IsNullOrWhiteSpace(status))q=q.Where(x=>x.Status==status);if(!string.IsNullOrWhiteSpace(search))q=q.Where(x=>x.DefectCode.Contains(search)||x.Title.Contains(search));var total=await q.CountAsync(ct);var rows=await q.OrderByDescending(x=>x.CreatedAt).Skip((page-1)*size).Take(size).Select(x=>new DefectDto(x.DefectId,x.ProjectId,x.ReleaseId,x.BuildId,x.ModuleId,x.DefectCode,x.Title,x.Description,x.StepsToReproduce,x.ExpectedResult,x.ActualResult,x.Severity,x.Status,x.AssigneeUserId,x.CreatedBy,x.CreatedAt,x.UpdatedAt,x.CrmTicketId,x.CrmSyncStatus,x.CrmLastSyncedAt)).ToListAsync(ct);var open=await db.Defects.AsNoTracking().Where(x=>!x.IsDeleted&&x.Status=="Open").CountAsync(ct);var inProgress=await db.Defects.AsNoTracking().Where(x=>!x.IsDeleted&&x.Status=="In Progress").CountAsync(ct);var closed=await db.Defects.AsNoTracking().Where(x=>!x.IsDeleted&&x.Status=="Closed").CountAsync(ct);return Ok(new DefectListResultDto(rows,total,open,inProgress,closed));}
 [HttpGet("{id:guid}"),Authorize(Policy="DefectView")]public async Task<ActionResult<DefectDto>>Get(Guid id,CancellationToken ct){var entity=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null||!CanAccess(entity.ProjectId))return NotFound();return Ok(await MapAsync(entity,ct));}
 [HttpGet("{id:guid}/activities"),Authorize(Policy="DefectView")]public async Task<ActionResult<IReadOnlyList<DefectActivityDto>>>Activities(Guid id,CancellationToken ct){var entity=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null||!CanAccess(entity.ProjectId))return NotFound();return Ok(await activityService.GetActivitiesAsync(id,ct));}
 [HttpPost("{id:guid}/comments"),Authorize(Policy="DefectEdit")]
 public async Task<IActionResult>AddComment(Guid id,AddDefectCommentRequest r,CancellationToken ct)
 {
  var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);
  if(entity is null||!CanAccess(entity.ProjectId))return NotFound();
  await activityService.AddCommentAsync(id,r.Body,UserId(),ct);

  // Defect ที่ผูกกับ CRM ticket แล้ว ให้คอมเมนต์เดียวกันต่อท้าย Description ของ ticket นั้นด้วย — best-effort
  // เท่านั้น: ความล้มเหลวฝั่ง CRM ต้อง log ไว้เฉยๆ ไม่ทำให้ request นี้ล้มเหลวตาม (คอมเมนต์ใน QA Hub บันทึกไปแล้วข้างบน)
  if(entity.CrmSyncStatus=="Linked"&&!string.IsNullOrWhiteSpace(entity.CrmTicketId))
  {
   var me=await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==UserId(),ct);
   if(me is not null)
   {
    try{await crmSendService.AppendCommentAsync(entity,me.UserId,r.Body,me.DisplayName,ct);}
    catch(CrmIntegrationException ex){logger.LogError(ex,"Sync คอมเมนต์ไป CRM ticket {TicketId} ไม่สำเร็จ สำหรับ Defect {DefectId}",entity.CrmTicketId,id);}
    catch(CrmNotConfiguredException ex){logger.LogError(ex,"Sync คอมเมนต์ไป CRM ไม่สำเร็จ (ยังไม่ได้ตั้งค่า) สำหรับ Defect {DefectId}",id);}
   }
  }
  return NoContent();
 }
 [HttpDelete("{defectId:guid}/comments/{commentId:guid}"),Authorize(Policy="DefectEdit")]public async Task<IActionResult>DeleteComment(Guid defectId,Guid commentId,CancellationToken ct){await activityService.DeleteCommentAsync(defectId,commentId,UserId(),ct);return NoContent();}
 [HttpGet("{id:guid}/test-cases"),Authorize(Policy="DefectView")]public async Task<ActionResult<IReadOnlyList<DefectTestCaseDto>>>TestCases(Guid id,CancellationToken ct){var entity=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null||!CanAccess(entity.ProjectId))return NotFound();return Ok(await db.DefectTestCaseLinks.AsNoTracking().Where(x=>x.DefectId==id).Join(db.TestCases,x=>x.TestCaseId,y=>y.TestCaseId,(x,y)=>new DefectTestCaseDto(y.TestCaseId,y.TestCaseCode,y.Title)).ToListAsync(ct));}
 [HttpPost("{id:guid}/test-cases"),Authorize(Policy="DefectEdit")]public async Task<IActionResult>LinkTestCase(Guid id,LinkDefectTestCaseRequest r,CancellationToken ct){var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null||!CanAccess(entity.ProjectId))return NotFound();if(!await db.TestCases.AnyAsync(x=>x.TestCaseId==r.TestCaseId&&!x.IsDeleted,ct))return BadRequest("Test Case not found.");if(await db.DefectTestCaseLinks.AnyAsync(x=>x.DefectId==id&&x.TestCaseId==r.TestCaseId,ct))return Conflict("Already linked.");db.DefectTestCaseLinks.Add(new DefectTestCaseLink(id,r.TestCaseId,UserId()));await db.SaveChangesAsync(ct);await activityService.LogAsync(id,"LinkTestCase",$"Linked test case {r.TestCaseId}",UserId(),ct);return NoContent();}
 [HttpDelete("{id:guid}/test-cases/{testCaseId:guid}"),Authorize(Policy="DefectEdit")]public async Task<IActionResult>UnlinkTestCase(Guid id,Guid testCaseId,CancellationToken ct){var link=await db.DefectTestCaseLinks.FirstOrDefaultAsync(x=>x.DefectId==id&&x.TestCaseId==testCaseId,ct);if(link is null)return NotFound();db.DefectTestCaseLinks.Remove(link);await db.SaveChangesAsync(ct);await activityService.LogAsync(id,"UnlinkTestCase",$"Unlinked test case {testCaseId}",UserId(),ct);return NoContent();}
 [HttpPost("bulk"),Authorize(Policy="DefectEdit")]public async Task<IActionResult>Bulk(BulkDefectRequest r,CancellationToken ct){if(r.Ids is null||r.Ids.Count==0)return BadRequest("No defect IDs provided.");var entities=(await db.Defects.Where(x=>r.Ids.Contains(x.DefectId)&&!x.IsDeleted).ToListAsync(ct)).Where(e=>CanAccess(e.ProjectId)).ToList();foreach(var e in entities){if(r.Delete.HasValue&&r.Delete.Value){e.SoftDelete(UserId());}else{if(!string.IsNullOrWhiteSpace(r.Status))e.UpdateStatus(r.Status,UserId());if(!string.IsNullOrWhiteSpace(r.Severity))e.UpdateSeverity(r.Severity,UserId());if(r.AssigneeUserId.HasValue)e.Assign(r.AssigneeUserId.Value,UserId());}}await db.SaveChangesAsync(ct);return Ok(new{updated=entities.Count});}
 [HttpPost,Authorize(Policy="DefectEdit")]public async Task<ActionResult<DefectDto>>Create(SaveDefectRequest r,CancellationToken ct){if(!await db.Projects.AnyAsync(x=>x.ProjectId==r.ProjectId&&x.IsActive,ct))return BadRequest("Project is not active.");var projectCode=await db.Projects.Where(x=>x.ProjectId==r.ProjectId).Select(x=>x.ProjectCode).SingleAsync(ct);var prefix=$"{projectCode}-DEF";var existing=await db.Defects.Where(x=>x.ProjectId==r.ProjectId&&x.DefectCode.StartsWith(prefix)).Select(x=>x.DefectCode).ToListAsync(ct);var code=BusinessCodeGenerator.NextAvailable(prefix,existing);var entity=new Defect(r.ProjectId,r.ReleaseId,r.BuildId,r.ModuleId,code,r.Title,r.Severity,r.Status,UserId(),r.Description,r.StepsToReproduce,r.ExpectedResult,r.ActualResult,r.AssigneeUserId);await db.Defects.AddAsync(entity,ct);await db.SaveChangesAsync(ct);await activityService.LogAsync(entity.DefectId,"Created","Defect created",UserId(),ct);return Ok(await MapAsync(entity,ct));}
 [HttpPut("{id:guid}"),Authorize(Policy="DefectEdit")]public async Task<ActionResult<DefectDto>>Update(Guid id,SaveDefectRequest r,CancellationToken ct){var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null||!CanAccess(entity.ProjectId))return NotFound();entity.Update(r.Title,r.Severity,r.Status,r.Description,r.StepsToReproduce,r.ExpectedResult,r.ActualResult,r.AssigneeUserId,UserId());await db.SaveChangesAsync(ct);await activityService.LogAsync(id,"Updated","Defect details updated",UserId(),ct);return Ok(await MapAsync(entity,ct));}
 [HttpPatch("{id:guid}/status"),Authorize(Policy="DefectEdit")]public async Task<ActionResult<DefectDto>>ChangeStatus(Guid id,ChangeDefectStatusRequest r,CancellationToken ct){var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null||!CanAccess(entity.ProjectId))return NotFound();var old=entity.Status;entity.UpdateStatus(r.Status,UserId());await db.SaveChangesAsync(ct);await activityService.LogAsync(id,"StatusChanged",$"Status changed from {old} to {r.Status}",UserId(),ct);return Ok(await MapAsync(entity,ct));}
 [HttpPatch("{id:guid}/severity"),Authorize(Policy="DefectEdit")]public async Task<ActionResult<DefectDto>>ChangeSeverity(Guid id,ChangeDefectSeverityRequest r,CancellationToken ct){var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null||!CanAccess(entity.ProjectId))return NotFound();var old=entity.Severity;entity.UpdateSeverity(r.Severity,UserId());await db.SaveChangesAsync(ct);await activityService.LogAsync(id,"SeverityChanged",$"Severity changed from {old} to {r.Severity}",UserId(),ct);return Ok(await MapAsync(entity,ct));}
 [HttpDelete("{id:guid}"),Authorize(Policy="DefectEdit")]public async Task<IActionResult>Delete(Guid id,CancellationToken ct){var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null||!CanAccess(entity.ProjectId))return NotFound();entity.SoftDelete(UserId());await db.SaveChangesAsync(ct);await activityService.LogAsync(id,"Deleted","Defect deleted",UserId(),ct);return NoContent();}
 [HttpPost("{id:guid}/send-to-crm"),Authorize(Policy="DefectEdit")]
 public async Task<ActionResult<SendToCrmResponse>>SendToCrm(Guid id,SendToCrmRequest r,CancellationToken ct)
 {
  var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);
  if(entity is null||!CanAccess(entity.ProjectId))return NotFound();
  if(entity.CrmSyncStatus=="Linked"&&!r.Relink)return Conflict(new{detail=$"Defect นี้เชื่อมโยงกับ CRM Ticket {entity.CrmTicketId} แล้ว"});
  if(string.IsNullOrWhiteSpace(r.AssignToStaffCode))return BadRequest(new{detail="กรุณาเลือกผู้รับผิดชอบฝั่ง CRM"});
  var me=await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==UserId(),ct);
  if(me is null)return Unauthorized();
  string jobNo;
  try{jobNo=await crmSendService.SendAsync(entity,me.UserId,me.DisplayName,r.AssignToStaffCode,ct);}
  catch(CrmIntegrationException ex)
  {
   // ใช้ 400 ไม่ใช้ 502 — Cloudflare (ที่ tunnel หน้าเว็บอยู่) แทนที่ response 502 ของ origin ด้วยหน้า error
   // ของตัวเองแบบไม่มี CORS header ทำให้ browser เห็นเป็น "Failed to fetch" มองไม่เห็นข้อความจริงเลย
   logger.LogError(ex,"ส่งไป CRM ไม่สำเร็จ สำหรับ Defect {DefectId}",id);
   entity.SetCrmSyncFailed(DateTime.UtcNow);
   await db.SaveChangesAsync(ct);
   await activityService.LogAsync(id,"CrmSyncFailed",$"ส่งไป CRM ไม่สำเร็จ: {ex.Message}",UserId(),ct);
   return BadRequest(new{detail=ex.Message});
  }
  catch(CrmNotConfiguredException ex)
  {
   // ยังไม่ได้ตั้งค่า/ปิดใช้งาน CRM Service Account หรือ Project นี้ยังไม่มี CRM Mapping — เป็นปัญหาระดับการตั้งค่า
   logger.LogError(ex,"ส่งไป CRM ไม่สำเร็จ (ยังไม่ได้ตั้งค่า) สำหรับ Defect {DefectId}",id);
   entity.SetCrmSyncFailed(DateTime.UtcNow);
   await db.SaveChangesAsync(ct);
   await activityService.LogAsync(id,"CrmSyncFailed",$"ส่งไป CRM ไม่สำเร็จ: {ex.Message}",UserId(),ct);
   return BadRequest(new{detail=ex.Message});
  }
  // บันทึก activity ที่มี ticket number ก่อนแตะ entity/SaveChanges — ถ้า SaveChanges ล้มเหลวหลัง CRM สร้าง ticket
  // สำเร็จแล้ว จะยังมีหลักฐาน ticket id ใน Activities feed ให้กู้คืนได้ ไม่ใช่ ticket ที่ CRM สร้างไว้กำพร้าแบบไม่มีร่องรอย
  await activityService.LogAsync(id,"CrmSent",$"ส่งไป CRM สำเร็จ Ticket #{jobNo} (มอบหมายให้ {r.AssignToStaffCode})",UserId(),ct);
  entity.SetCrmTicket(jobNo,DateTime.UtcNow);
  await db.SaveChangesAsync(ct);
  return Ok(new SendToCrmResponse(jobNo,entity.CrmSyncStatus,entity.CrmLastSyncedAt));
 }
 [HttpPost("{id:guid}/crm-reassign"),Authorize(Policy="DefectEdit")]
 public async Task<IActionResult>CrmReassign(Guid id,CrmReassignRequest r,CancellationToken ct)
 {
  var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);
  if(entity is null||!CanAccess(entity.ProjectId))return NotFound();
  if(entity.CrmSyncStatus!="Linked"||string.IsNullOrWhiteSpace(entity.CrmTicketId))return BadRequest(new{detail="Defect นี้ยังไม่ได้เชื่อมโยงกับ CRM Ticket กรุณา \"ส่งไป CRM\" ก่อน"});
  if(string.IsNullOrWhiteSpace(r.AssignToStaffCode))return BadRequest(new{detail="กรุณาเลือกผู้รับผิดชอบฝั่ง CRM"});
  var me=await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==UserId(),ct);
  if(me is null)return Unauthorized();
  try{await crmSendService.ChangeAssigneeAsync(entity,me.UserId,r.AssignToStaffCode,me.DisplayName,ct);}
  catch(CrmIntegrationException ex)
  {
   logger.LogError(ex,"เปลี่ยนผู้รับผิดชอบใน CRM Ticket {TicketId} ไม่สำเร็จ สำหรับ Defect {DefectId}",entity.CrmTicketId,id);
   await activityService.LogAsync(id,"CrmReassignFailed",$"เปลี่ยนผู้รับผิดชอบใน CRM ไม่สำเร็จ: {ex.Message}",UserId(),ct);
   return BadRequest(new{detail=ex.Message});
  }
  catch(CrmNotConfiguredException ex)
  {
   logger.LogError(ex,"เปลี่ยนผู้รับผิดชอบใน CRM ไม่สำเร็จ (ยังไม่ได้ตั้งค่า) สำหรับ Defect {DefectId}",id);
   await activityService.LogAsync(id,"CrmReassignFailed",$"เปลี่ยนผู้รับผิดชอบใน CRM ไม่สำเร็จ: {ex.Message}",UserId(),ct);
   return BadRequest(new{detail=ex.Message});
  }
  await activityService.LogAsync(id,"CrmReassigned",$"เปลี่ยนผู้รับผิดชอบใน CRM Ticket #{entity.CrmTicketId} เป็น {r.AssignToStaffCode}",UserId(),ct);
  return NoContent();
 }
 [HttpGet("crm/dev-users"),Authorize(Policy="DefectEdit")]
 public async Task<ActionResult<IReadOnlyList<BlueIdUserDto>>>CrmDevUsers(CancellationToken ct)
 {
  var me=await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==UserId(),ct);
  if(me is null)return Unauthorized();
  try{return Ok(await crmSendService.GetAssignableUsersAsync(me.UserId,ct));}
  catch(CrmIntegrationException ex){logger.LogError(ex,"โหลดรายชื่อผู้รับผิดชอบฝั่ง CRM ไม่สำเร็จ");return BadRequest(new{detail=ex.Message});}
  catch(CrmNotConfiguredException ex){logger.LogError(ex,"โหลดรายชื่อผู้รับผิดชอบฝั่ง CRM ไม่สำเร็จ (ยังไม่ได้ตั้งค่า)");return BadRequest(new{detail=ex.Message});}
 }
 private Task<DefectDto>MapAsync(Defect x,CancellationToken ct)=>Task.FromResult(new DefectDto(x.DefectId,x.ProjectId,x.ReleaseId,x.BuildId,x.ModuleId,x.DefectCode,x.Title,x.Description,x.StepsToReproduce,x.ExpectedResult,x.ActualResult,x.Severity,x.Status,x.AssigneeUserId,x.CreatedBy,x.CreatedAt,x.UpdatedAt,x.CrmTicketId,x.CrmSyncStatus,x.CrmLastSyncedAt));
 private bool CanAccess(Guid projectId)=>projectCtx.AllowedProjectIds.Length==0||projectCtx.AllowedProjectIds.Contains(projectId);
 private Guid?UserId()=>Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("sub"),out var id)?id:null;
}
public sealed record SaveDefectRequest(Guid ProjectId,Guid?ReleaseId,Guid?BuildId,Guid?ModuleId,string Title,string Severity="Medium",string Status="Open",string?Description=null,string?StepsToReproduce=null,string?ExpectedResult=null,string?ActualResult=null,Guid?AssigneeUserId=null);
public sealed record ChangeDefectStatusRequest(string Status);
public sealed record ChangeDefectSeverityRequest(string Severity);
public sealed record AddDefectCommentRequest(string Body);
public sealed record LinkDefectTestCaseRequest(Guid TestCaseId);
public sealed record BulkDefectRequest(IReadOnlyList<Guid>?Ids,string?Status=null,string?Severity=null,Guid?AssigneeUserId=null,bool?Delete=null);
public sealed record DefectDto(Guid DefectId,Guid ProjectId,Guid?ReleaseId,Guid?BuildId,Guid?ModuleId,string DefectCode,string Title,string?Description,string?StepsToReproduce,string?ExpectedResult,string?ActualResult,string Severity,string Status,Guid?AssigneeUserId,Guid?CreatedByUserId,DateTime CreatedAt,DateTime?UpdatedAt,string?CrmTicketId,string CrmSyncStatus,DateTime?CrmLastSyncedAt);
public sealed record SendToCrmRequest(string AssignToStaffCode,bool Relink=false);
public sealed record SendToCrmResponse(string CrmTicketId,string CrmSyncStatus,DateTime?CrmLastSyncedAt);
public sealed record CrmReassignRequest(string AssignToStaffCode);
public sealed record DefectActivityDto(Guid ActivityId,Guid DefectId,string ActivityType,string? Description,Guid? PerformedByUserId,DateTime PerformedAt);
public sealed record DefectTestCaseDto(Guid TestCaseId,string TestCaseCode,string Title);
public sealed record DefectSummaryDto(int Open,int InProgress,int Closed);
public sealed record DefectListResultDto(IReadOnlyList<DefectDto> Rows,int Total,int Open,int InProgress,int Closed);
