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
public sealed class DefectsController(QaDbContext db,ProjectAccessContext projectCtx,DefectActivityService activityService,CrmSendToCrmService crmSendService,IWebHostEnvironment environment,ILogger<DefectsController> logger):ControllerBase
{
 [HttpGet,Authorize(Policy="DefectView")]public async Task<ActionResult<DefectListResultDto>>List([FromQuery]Guid?projectId,[FromQuery]Guid?releaseId,[FromQuery]Guid?buildId,[FromQuery]Guid?moduleId,[FromQuery]bool unassignedModule,[FromQuery]Guid?assigneeUserId,[FromQuery]string?severity,[FromQuery]string?priority,[FromQuery]string?status,[FromQuery]string?search,[FromQuery]int page=1,[FromQuery]int size=20,CancellationToken ct=default){var q=db.Defects.AsNoTracking().Where(x=>!x.IsDeleted);if(projectId.HasValue)q=q.Where(x=>x.ProjectId==projectId);else if(projectCtx.AllowedProjectIds.Length>0)q=q.Where(x=>projectCtx.AllowedProjectIds.Contains(x.ProjectId));else q=q.Where(_=>false);if(releaseId.HasValue)q=q.Where(x=>x.ReleaseId==releaseId);if(buildId.HasValue)q=q.Where(x=>x.BuildId==buildId);var scope=q;if(unassignedModule)q=q.Where(x=>x.ModuleId==null);else if(moduleId.HasValue)q=q.Where(x=>x.ModuleId==moduleId);if(assigneeUserId.HasValue)q=q.Where(x=>x.AssigneeUserId==assigneeUserId);if(!string.IsNullOrWhiteSpace(severity))q=q.Where(x=>x.Severity==severity);if(!string.IsNullOrWhiteSpace(priority))q=q.Where(x=>db.DefectTestCaseLinks.Any(link=>link.DefectId==x.DefectId&&db.TestCases.Any(testCase=>testCase.TestCaseId==link.TestCaseId&&!testCase.IsDeleted&&testCase.Priority==priority)));if(!string.IsNullOrWhiteSpace(status))q=q.Where(x=>x.Status==status);if(!string.IsNullOrWhiteSpace(search))q=q.Where(x=>x.DefectCode.Contains(search)||x.Title.Contains(search));var total=await q.CountAsync(ct);var rows=await q.OrderByDescending(x=>x.CreatedAt).Skip((page-1)*size).Take(size).Select(x=>new DefectDto(x.DefectId,x.ProjectId,x.ReleaseId,x.BuildId,x.ModuleId,x.DefectCode,x.Title,x.Description,x.StepsToReproduce,x.ExpectedResult,x.ActualResult,x.Severity,x.Status,x.AssigneeUserId,x.CreatedBy,x.CreatedAt,x.UpdatedAt,x.CrmTicketId,x.CrmSyncStatus,x.CrmLastSyncedAt)).ToListAsync(ct);var open=await scope.CountAsync(x=>x.Status=="Open",ct);var inProgress=await scope.CountAsync(x=>x.Status=="In Progress",ct);var closed=await scope.CountAsync(x=>x.Status=="Closed",ct);return Ok(new DefectListResultDto(rows,total,open,inProgress,closed));}
 // สรุปข้อมูล Defect สำหรับการ์ดด้านบนหน้า Defect — ขอบเขตตาม Project/Release/Build ที่เลือกด้านบน (ไม่ผูกกับ
 // ตัวกรองของตารางด้านล่าง เช่น module/severity/status/assignee/search) เพื่อให้เห็นภาพรวมทั้งหมดเสมอ
 [HttpGet("stats"),Authorize(Policy="DefectView")]public async Task<ActionResult<DefectStatsDto>>Stats([FromQuery]Guid?projectId,[FromQuery]Guid?releaseId,[FromQuery]Guid?buildId,CancellationToken ct){var q=db.Defects.AsNoTracking().Where(x=>!x.IsDeleted);if(projectId.HasValue)q=q.Where(x=>x.ProjectId==projectId);else if(projectCtx.AllowedProjectIds.Length>0)q=q.Where(x=>projectCtx.AllowedProjectIds.Contains(x.ProjectId));else q=q.Where(_=>false);if(releaseId.HasValue)q=q.Where(x=>x.ReleaseId==releaseId);if(buildId.HasValue)q=q.Where(x=>x.BuildId==buildId);var statusCounts=await q.GroupBy(x=>x.Status).Select(g=>new{Status=g.Key,Count=g.Count()}).ToListAsync(ct);var severityCounts=await q.GroupBy(x=>x.Severity).Select(g=>new{Severity=g.Key,Count=g.Count()}).ToListAsync(ct);var moduleCounts=await q.GroupBy(x=>x.ModuleId).Select(g=>new{ModuleId=g.Key,Count=g.Count()}).ToListAsync(ct);var moduleIds=moduleCounts.Where(x=>x.ModuleId.HasValue).Select(x=>x.ModuleId!.Value).ToArray();var moduleNames=await db.Modules.AsNoTracking().Where(x=>moduleIds.Contains(x.ModuleId)).Select(x=>new{x.ModuleId,x.ModuleCode,x.ModuleName}).ToDictionaryAsync(x=>x.ModuleId,ct);var moduleBreakdown=moduleCounts.Select(x=>{var module=x.ModuleId.HasValue?moduleNames.GetValueOrDefault(x.ModuleId.Value):null;return new DefectModuleCountDto(x.ModuleId,module?.ModuleCode,module?.ModuleName??(x.ModuleId.HasValue?"โมดูลที่ถูกลบ":"ไม่ระบุโมดูล"),x.Count);}).OrderByDescending(x=>x.Count).ThenBy(x=>x.ModuleCode??x.ModuleName,StringComparer.OrdinalIgnoreCase).ToArray();int ByStatus(string s)=>statusCounts.FirstOrDefault(x=>x.Status==s)?.Count??0;int BySeverity(string s)=>severityCounts.FirstOrDefault(x=>x.Severity==s)?.Count??0;var total=statusCounts.Sum(x=>x.Count);var resolved=ByStatus("Resolved");var closed=ByStatus("Closed");var oldestOpenCreatedAt=await q.Where(x=>x.Status=="Open"||x.Status=="In Progress").OrderBy(x=>x.CreatedAt).Select(x=>(DateTime?)x.CreatedAt).FirstOrDefaultAsync(ct);var oldestOpenAgeDays=oldestOpenCreatedAt.HasValue?(int)(DateTime.UtcNow-oldestOpenCreatedAt.Value).TotalDays:0;var unassignedActive=await q.Where(x=>(x.Status=="Open"||x.Status=="In Progress")&&x.AssigneeUserId==null).CountAsync(ct);var closureRate=total>0?Math.Round((resolved+closed)*100.0/total,1):0;return Ok(new DefectStatsDto(total,ByStatus("Open"),ByStatus("In Progress"),resolved,closed,ByStatus("Rejected"),BySeverity("Critical"),BySeverity("High"),BySeverity("Medium"),BySeverity("Low"),oldestOpenAgeDays,unassignedActive,closureRate,moduleBreakdown));}
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
 [HttpDelete("{defectId:guid}/comments/{commentId:guid}"),Authorize(Policy="DefectEdit")]public async Task<IActionResult>DeleteComment(Guid defectId,Guid commentId,CancellationToken ct){var defect=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==defectId&&!x.IsDeleted,ct);if(defect is null||!CanAccess(defect.ProjectId))return NotFound();return await activityService.DeleteCommentAsync(defectId,commentId,UserId(),User.IsInRole("SYS_ADMIN"),ct)?NoContent():Forbid();}
 [HttpGet("{id:guid}/test-cases"),Authorize(Policy="DefectView")]public async Task<ActionResult<IReadOnlyList<DefectTestCaseDto>>>TestCases(Guid id,CancellationToken ct){var entity=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null||!CanAccess(entity.ProjectId))return NotFound();return Ok(await db.DefectTestCaseLinks.AsNoTracking().Where(x=>x.DefectId==id).Join(db.TestCases,x=>x.TestCaseId,y=>y.TestCaseId,(x,y)=>new DefectTestCaseDto(y.TestCaseId,y.TestCaseCode,y.Title)).ToListAsync(ct));}
 [HttpGet("by-test-case/{testCaseId:guid}"),Authorize(Policy="DefectView")]
 public async Task<ActionResult<IReadOnlyList<DefectDto>>>ByTestCase(Guid testCaseId,CancellationToken ct)
 {
  var allowed=projectCtx.AllowedProjectIds;
  var rows=await db.DefectTestCaseLinks.AsNoTracking().Where(x=>x.TestCaseId==testCaseId)
   .Join(db.Defects.AsNoTracking().Where(x=>!x.IsDeleted&&allowed.Contains(x.ProjectId)),link=>link.DefectId,defect=>defect.DefectId,(link,defect)=>defect)
   .OrderByDescending(x=>x.CreatedAt).Take(200)
   .Select(x=>new DefectDto(x.DefectId,x.ProjectId,x.ReleaseId,x.BuildId,x.ModuleId,x.DefectCode,x.Title,x.Description,x.StepsToReproduce,x.ExpectedResult,x.ActualResult,x.Severity,x.Status,x.AssigneeUserId,x.CreatedBy,x.CreatedAt,x.UpdatedAt,x.CrmTicketId,x.CrmSyncStatus,x.CrmLastSyncedAt)).ToListAsync(ct);
  return Ok(rows);
 }
 [HttpGet("{id:guid}/attachments"),Authorize(Policy="DefectView")]
 public async Task<ActionResult<IReadOnlyList<DefectAttachmentDto>>>Attachments(Guid id,CancellationToken ct)
 {
  var defect=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);
  if(defect is null||!CanAccess(defect.ProjectId))return NotFound();
  return Ok(await ListAttachmentsAsync(id,ct));
 }
 [HttpGet("{id:guid}/attachments/{attachmentId:guid}"),Authorize(Policy="DefectView")]
 public async Task<IActionResult>Attachment(Guid id,Guid attachmentId,CancellationToken ct)
 {
  var defect=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);
  if(defect is null||!CanAccess(defect.ProjectId))return NotFound();
  await ImportLegacyAttachmentsAsync(id,ct);
  var row=await db.DefectAttachments.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&x.DefectAttachmentId==attachmentId,ct);
  var path=row is null?null:Path.Combine(AttachmentDirectory(id),row.StoredFileName);
  if(row is null||!System.IO.File.Exists(path))return NotFound();
  Response.Headers.ContentDisposition=$"inline; filename=\"{attachmentId:N}{Path.GetExtension(row.StoredFileName)}\"";
  return PhysicalFile(path,row.ContentType);
 }
 [HttpPost("{id:guid}/attachments"),Authorize(Policy="DefectEdit"),RequestSizeLimit(26_000_000)]
 public async Task<ActionResult<IReadOnlyList<DefectAttachmentDto>>>UploadAttachments(Guid id,[FromForm] List<IFormFile> files,CancellationToken ct)
 {
  var defect=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);
  if(defect is null||!CanAccess(defect.ProjectId))return NotFound();
  await ImportLegacyAttachmentsAsync(id,ct);
  var existing=await db.DefectAttachments.AsNoTracking().Where(x=>x.DefectId==id).Select(x=>x.SizeBytes).ToListAsync(ct);
  if(files.Count==0||files.Count>5||existing.Count+files.Count>5)return BadRequest(new{detail="แนบรูปได้สูงสุด 5 รูปต่อ Defect"});
  if(files.Any(x=>x.Length==0||x.Length>5_000_000)||files.Sum(x=>x.Length)>20_000_000)return BadRequest(new{detail="รูปแต่ละไฟล์ต้องไม่เกิน 5 MB และรวมไม่เกิน 20 MB"});
  var pending=new List<(Guid Id,string DisplayName,string StoredName,string ContentType,byte[] Data)>();
  foreach(var file in files)
  {
   var extension=Path.GetExtension(file.FileName).ToLowerInvariant();
   if(extension is not(".png" or ".jpg" or ".jpeg" or ".webp"))return BadRequest(new{detail="รองรับเฉพาะ PNG, JPG และ WebP"});
   await using var memory=new MemoryStream();await file.CopyToAsync(memory,ct);var bytes=memory.ToArray();
   if(!ValidImage(bytes,extension))return BadRequest(new{detail=$"ไฟล์ {Path.GetFileName(file.FileName)} ไม่ใช่รูปภาพที่ถูกต้อง"});
   var safeName=new string(Path.GetFileNameWithoutExtension(file.FileName).Where(c=>char.IsLetterOrDigit(c)||c is '-' or '_' or ' ').Take(70).ToArray()).Trim();
   var display=$"{(safeName.Length>0?safeName:"image")}{extension}";
   var attachmentId=Guid.NewGuid();
   pending.Add((attachmentId,display,$"{attachmentId:N}_{display}",ContentTypeFor(extension),bytes));
  }
  var directory=AttachmentDirectory(id);Directory.CreateDirectory(directory);
  var written=new List<string>();
  try
  {
   foreach(var item in pending){var path=Path.Combine(directory,item.StoredName);await System.IO.File.WriteAllBytesAsync(path,item.Data,ct);written.Add(path);}
   var now=DateTime.UtcNow;
   db.DefectAttachments.AddRange(pending.Select(x=>new DefectAttachment(x.Id,id,x.DisplayName,x.StoredName,x.Data.LongLength,x.ContentType,UserId(),now)));
   db.DefectActivities.Add(new DefectActivity(id,"AttachmentAdded",$"แนบรูปภาพ {pending.Count} ไฟล์",UserId()));
   await db.SaveChangesAsync(ct);
  }
  catch{foreach(var path in written)TryDeleteFile(path);throw;}
  return Ok(await ListAttachmentsAsync(id,ct));
 }
 [HttpDelete("{id:guid}/attachments/{attachmentId:guid}"),Authorize(Policy="DefectEdit")]
 public async Task<IActionResult>DeleteAttachment(Guid id,Guid attachmentId,CancellationToken ct)
 {
  var defect=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);
  if(defect is null||!CanAccess(defect.ProjectId))return NotFound();
  await ImportLegacyAttachmentsAsync(id,ct);
  var row=await db.DefectAttachments.FirstOrDefaultAsync(x=>x.DefectId==id&&x.DefectAttachmentId==attachmentId,ct);
  if(row is null)return NotFound();
  db.DefectAttachments.Remove(row);
  db.DefectActivities.Add(new DefectActivity(id,"AttachmentDeleted",$"ลบรูปภาพแนบ {row.FileName}",UserId()));
  await db.SaveChangesAsync(ct);
  // ลบไฟล์หลังบันทึกฐานข้อมูลสำเร็จ — ถ้าลบไฟล์ไม่ได้ (ถูกล็อก) รายการก็หายจากระบบแล้ว ไม่ตอบ 500 ให้ผู้ใช้
  if(!TryDeleteFile(Path.Combine(AttachmentDirectory(id),row.StoredFileName)))logger.LogWarning("ลบไฟล์แนบ {StoredFileName} ของ Defect {DefectId} ไม่สำเร็จ",row.StoredFileName,id);
  return NoContent();
 }
 private string AttachmentDirectory(Guid defectId)=>Path.Combine(environment.ContentRootPath,"App_Data","DefectAttachments",defectId.ToString("N"));
 private async Task<IReadOnlyList<DefectAttachmentDto>>ListAttachmentsAsync(Guid defectId,CancellationToken ct)
 {
  await ImportLegacyAttachmentsAsync(defectId,ct);
  return await db.DefectAttachments.AsNoTracking().Where(x=>x.DefectId==defectId).OrderBy(x=>x.UploadedAt).ThenBy(x=>x.FileName).Select(x=>new DefectAttachmentDto(x.DefectAttachmentId,x.FileName,x.SizeBytes)).ToListAsync(ct);
 }
 // รูปที่อัปโหลดก่อนมีตาราง DefectAttachments อยู่บนดิสก์อย่างเดียว — นำเข้าเป็น metadata ครั้งแรกที่เปิด Defect นั้น
 private async Task ImportLegacyAttachmentsAsync(Guid defectId,CancellationToken ct)
 {
  var directory=AttachmentDirectory(defectId);
  if(!Directory.Exists(directory)||await db.DefectAttachments.AnyAsync(x=>x.DefectId==defectId,ct))return;
  var legacy=Directory.EnumerateFiles(directory).Select(path=>new FileInfo(path)).Where(file=>file.Name.Length>33&&Guid.TryParseExact(file.Name[..32],"N",out _)).ToList();
  if(legacy.Count==0)return;
  db.DefectAttachments.AddRange(legacy.Select(file=>new DefectAttachment(Guid.ParseExact(file.Name[..32],"N"),defectId,file.Name[33..],file.Name,Math.Max(1,file.Length),ContentTypeFor(file.Extension.ToLowerInvariant()),null,file.CreationTimeUtc)));
  await db.SaveChangesAsync(ct);
 }
 private static string ContentTypeFor(string extension)=>extension switch{".png"=>"image/png",".jpg" or ".jpeg"=>"image/jpeg",".webp"=>"image/webp",_=>"application/octet-stream"};
 private static bool TryDeleteFile(string path){try{if(System.IO.File.Exists(path))System.IO.File.Delete(path);return true;}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}}
 private static bool ValidImage(byte[] bytes,string extension)=>extension switch
 {
  ".png"=>bytes.Length>=8&&bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),
  ".jpg" or ".jpeg"=>bytes.Length>=3&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255,
  ".webp"=>bytes.Length>=12&&bytes.AsSpan(0,4).SequenceEqual("RIFF"u8)&&bytes.AsSpan(8,4).SequenceEqual("WEBP"u8),
  _=>false
 };
 [HttpPost("{id:guid}/test-cases"),Authorize(Policy="DefectEdit")]public async Task<IActionResult>LinkTestCase(Guid id,LinkDefectTestCaseRequest r,CancellationToken ct){var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null||!CanAccess(entity.ProjectId))return NotFound();if(!await db.TestCases.AnyAsync(x=>x.TestCaseId==r.TestCaseId&&!x.IsDeleted,ct))return BadRequest("Test Case not found.");if(await db.DefectTestCaseLinks.AnyAsync(x=>x.DefectId==id&&x.TestCaseId==r.TestCaseId,ct))return Conflict("Already linked.");db.DefectTestCaseLinks.Add(new DefectTestCaseLink(id,r.TestCaseId,UserId()));await db.SaveChangesAsync(ct);await activityService.LogAsync(id,"LinkTestCase",$"Linked test case {r.TestCaseId}",UserId(),ct);return NoContent();}
 [HttpDelete("{id:guid}/test-cases/{testCaseId:guid}"),Authorize(Policy="DefectEdit")]public async Task<IActionResult>UnlinkTestCase(Guid id,Guid testCaseId,CancellationToken ct){var defect=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(defect is null||!CanAccess(defect.ProjectId))return NotFound();var link=await db.DefectTestCaseLinks.FirstOrDefaultAsync(x=>x.DefectId==id&&x.TestCaseId==testCaseId,ct);if(link is null)return NotFound();db.DefectTestCaseLinks.Remove(link);await db.SaveChangesAsync(ct);await activityService.LogAsync(id,"UnlinkTestCase",$"Unlinked test case {testCaseId}",UserId(),ct);return NoContent();}
 [HttpPost("bulk"),Authorize(Policy="DefectEdit")]public async Task<IActionResult>Bulk(BulkDefectRequest r,CancellationToken ct){if(r.Ids is null||r.Ids.Count==0)return BadRequest("No defect IDs provided.");var entities=(await db.Defects.Where(x=>r.Ids.Contains(x.DefectId)&&!x.IsDeleted).ToListAsync(ct)).Where(e=>CanAccess(e.ProjectId)).ToList();var userId=UserId();var isDelete=r.Delete==true;foreach(var e in entities){if(isDelete){e.SoftDelete(userId);}else{if(!string.IsNullOrWhiteSpace(r.Status))e.UpdateStatus(r.Status,userId);if(!string.IsNullOrWhiteSpace(r.Severity))e.UpdateSeverity(r.Severity,userId);if(r.AssigneeUserId.HasValue)e.Assign(r.AssigneeUserId.Value,userId);}}await db.SaveChangesAsync(ct);var action=isDelete?"Deleted":"Updated";var details=isDelete?"Defect deleted by bulk action":$"Defect updated by bulk action{(r.Status is null?string.Empty:$"; Status={r.Status}")}{(r.Severity is null?string.Empty:$"; Severity={r.Severity}")}{(r.AssigneeUserId.HasValue?"; Assignee changed":string.Empty)}";if(entities.Count>0)await activityService.LogManyAsync(entities.Select(e=>e.DefectId),action,details,userId,ct);return Ok(new{updated=entities.Count});}
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
public sealed record DefectStatsDto(int Total,int Open,int InProgress,int Resolved,int Closed,int Rejected,int Critical,int High,int Medium,int Low,int OldestOpenAgeDays,int UnassignedActive,double ClosureRate,IReadOnlyList<DefectModuleCountDto> Modules);
public sealed record DefectModuleCountDto(Guid? ModuleId,string? ModuleCode,string ModuleName,int Count);
public sealed record DefectAttachmentDto(Guid AttachmentId,string FileName,long Size);
