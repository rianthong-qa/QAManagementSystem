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
public sealed class DefectsController(QaDbContext db,ProjectAccessContext projectCtx,DefectActivityService activityService,CrmSendToCrmService crmSendService,DefectImageStorage imageStorage,ILogger<DefectsController> logger,KnowledgeSearchService? knowledgeSearch=null):ControllerBase
{
 [HttpGet,Authorize(Policy="DefectView")]public async Task<ActionResult<DefectListResultDto>>List([FromQuery]Guid?projectId,[FromQuery]Guid?releaseId,[FromQuery]Guid?buildId,[FromQuery]Guid?moduleId,[FromQuery]bool unassignedModule,[FromQuery]Guid?assigneeUserId,[FromQuery]string?severity,[FromQuery]string?priority,[FromQuery]string?status,[FromQuery]string?search,[FromQuery]string?sortBy,[FromQuery]int page=1,[FromQuery]int size=20,[FromQuery]string?buildMode=null,CancellationToken ct=default){var q=db.Defects.AsNoTracking().Where(x=>!x.IsDeleted);if(projectId.HasValue)q=q.Where(x=>x.ProjectId==projectId);else if(projectCtx.AllowedProjectIds.Length>0)q=q.Where(x=>projectCtx.AllowedProjectIds.Contains(x.ProjectId));else q=q.Where(_=>false);if(releaseId.HasValue)q=q.Where(x=>x.ReleaseId==releaseId);if(buildId.HasValue&&buildMode=="openAsOf"){var order=await DefectBuilds.ReleaseOrderAsync(db,buildId.Value,ct);var upTo=order?.UpTo??[];q=q.Where(x=>x.BuildId.HasValue&&upTo.Contains(x.BuildId.Value)&&x.Status!="Closed"&&x.Status!="Rejected");}else if(buildId.HasValue)q=q.Where(x=>x.BuildId==buildId);var scope=q;if(unassignedModule)q=q.Where(x=>x.ModuleId==null);else if(moduleId.HasValue)q=q.Where(x=>x.ModuleId==moduleId);if(assigneeUserId.HasValue)q=q.Where(x=>x.AssigneeUserId==assigneeUserId);if(!string.IsNullOrWhiteSpace(severity))q=q.Where(x=>x.Severity==severity);if(!string.IsNullOrWhiteSpace(priority))q=q.Where(x=>db.DefectTestCaseLinks.Any(link=>link.DefectId==x.DefectId&&db.TestCases.Any(testCase=>testCase.TestCaseId==link.TestCaseId&&!testCase.IsDeleted&&testCase.Priority==priority)));if(!string.IsNullOrWhiteSpace(status))q=q.Where(x=>x.Status==status);if(!string.IsNullOrWhiteSpace(search))q=q.Where(x=>x.DefectCode.Contains(search)||x.Title.Contains(search));var total=await q.CountAsync(ct);page=Math.Max(1,page);size=Math.Clamp(size,1,500);/* หน้า Defect ให้ผู้ใช้กรอกจำนวนต่อหน้าเอง 1–500 */var rows=await DefectListSort.Apply(q,sortBy).Skip((page-1)*size).Take(size).Select(x=>new DefectDto(x.DefectId,x.ProjectId,x.ReleaseId,x.BuildId,x.ModuleId,x.DefectCode,x.Title,x.Description,x.StepsToReproduce,x.ExpectedResult,x.ActualResult,x.Severity,x.Status,x.AssigneeUserId,x.CreatedBy,x.CreatedAt,x.UpdatedAt,x.CrmTicketId,x.CrmSyncStatus,x.CrmLastSyncedAt,x.ResolutionType,x.RootCauseCategory,x.RootCauseNote,x.ResolutionNote,x.DuplicateOfDefectId,null,x.FixedInBuildId,null,x.VerifiedInBuildId,null,x.ResolvedAt,x.ClosedAt,x.ReopenCount,x.LastRetestAt,x.LastRetestResult,x.RegressionOfDefectId,null)).ToListAsync(ct);rows=await WithBuildNumbersAsync(rows,ct);var open=await scope.CountAsync(x=>x.Status=="Open",ct);var inProgress=await scope.CountAsync(x=>x.Status=="In Progress",ct);var closed=await scope.CountAsync(x=>x.Status=="Closed",ct);return Ok(new DefectListResultDto(rows,total,open,inProgress,closed));}
 // สรุปข้อมูล Defect สำหรับการ์ดด้านบนหน้า Defect — ขอบเขตตาม Project/Release/Build ที่เลือกด้านบน (ไม่ผูกกับ
 // ตัวกรองของตารางด้านล่าง เช่น module/severity/status/assignee/search) เพื่อให้เห็นภาพรวมทั้งหมดเสมอ
 [HttpGet("stats"),Authorize(Policy="DefectView")]public async Task<ActionResult<DefectStatsDto>>Stats([FromQuery]Guid?projectId,[FromQuery]Guid?releaseId,[FromQuery]Guid?buildId,CancellationToken ct){var q=db.Defects.AsNoTracking().Where(x=>!x.IsDeleted);if(projectId.HasValue)q=q.Where(x=>x.ProjectId==projectId);else if(projectCtx.AllowedProjectIds.Length>0)q=q.Where(x=>projectCtx.AllowedProjectIds.Contains(x.ProjectId));else q=q.Where(_=>false);if(releaseId.HasValue)q=q.Where(x=>x.ReleaseId==releaseId);if(buildId.HasValue)q=q.Where(x=>x.BuildId==buildId);var statusCounts=await q.GroupBy(x=>x.Status).Select(g=>new{Status=g.Key,Count=g.Count()}).ToListAsync(ct);var severityCounts=await q.GroupBy(x=>x.Severity).Select(g=>new{Severity=g.Key,Count=g.Count()}).ToListAsync(ct);var moduleCounts=await q.GroupBy(x=>x.ModuleId).Select(g=>new{ModuleId=g.Key,Count=g.Count()}).ToListAsync(ct);var moduleIds=moduleCounts.Where(x=>x.ModuleId.HasValue).Select(x=>x.ModuleId!.Value).ToArray();var moduleNames=await db.Modules.AsNoTracking().Where(x=>moduleIds.Contains(x.ModuleId)).Select(x=>new{x.ModuleId,x.ModuleCode,x.ModuleName}).ToDictionaryAsync(x=>x.ModuleId,ct);var moduleBreakdown=moduleCounts.Select(x=>{var module=x.ModuleId.HasValue?moduleNames.GetValueOrDefault(x.ModuleId.Value):null;return new DefectModuleCountDto(x.ModuleId,module?.ModuleCode,module?.ModuleName??(x.ModuleId.HasValue?"โมดูลที่ถูกลบ":"ไม่ระบุโมดูล"),x.Count);}).OrderByDescending(x=>x.Count).ThenBy(x=>x.ModuleCode??x.ModuleName,StringComparer.OrdinalIgnoreCase).ToArray();int ByStatus(string s)=>statusCounts.FirstOrDefault(x=>x.Status==s)?.Count??0;int BySeverity(string s)=>severityCounts.FirstOrDefault(x=>x.Severity==s)?.Count??0;var total=statusCounts.Sum(x=>x.Count);var resolved=ByStatus("Resolved");var closed=ByStatus("Closed");var oldestOpenCreatedAt=await q.Where(x=>x.Status=="Open"||x.Status=="In Progress").OrderBy(x=>x.CreatedAt).Select(x=>(DateTime?)x.CreatedAt).FirstOrDefaultAsync(ct);var oldestOpenAgeDays=oldestOpenCreatedAt.HasValue?(int)(DateTime.UtcNow-oldestOpenCreatedAt.Value).TotalDays:0;var unassignedActive=await q.Where(x=>(x.Status=="Open"||x.Status=="In Progress")&&x.AssigneeUserId==null).CountAsync(ct);var closureRate=total>0?Math.Round((resolved+closed)*100.0/total,1):0;return Ok(new DefectStatsDto(total,ByStatus("Open"),ByStatus("In Progress"),resolved,closed,ByStatus("Rejected"),BySeverity("Critical"),BySeverity("High"),BySeverity("Medium"),BySeverity("Low"),oldestOpenAgeDays,unassignedActive,closureRate,moduleBreakdown));}
 [HttpGet("{id:guid}"),Authorize(Policy="DefectView")]public async Task<ActionResult<DefectDto>>Get(Guid id,CancellationToken ct){var entity=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null||!CanAccess(entity.ProjectId))return NotFound();return Ok(await MapAsync(entity,ct));}
 [HttpGet("{id:guid}/activities"),Authorize(Policy="DefectView")]public async Task<ActionResult<IReadOnlyList<DefectActivityDto>>>Activities(Guid id,CancellationToken ct){var entity=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null||!CanAccess(entity.ProjectId))return NotFound();return Ok(await activityService.GetActivitiesAsync(id,ct));}
 [HttpGet("{id:guid}/comments"),Authorize(Policy="DefectView")]
 public async Task<ActionResult<IReadOnlyList<DefectCommentDto>>>Comments(Guid id,CancellationToken ct)
 {
  var entity=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);
  if(entity is null||!CanAccess(entity.ProjectId))return NotFound();
  return Ok(await activityService.GetCommentsAsync(id,ct));
 }
 // รับทั้ง multipart/form-data (body + files สูงสุด 5 รูป) และ JSON {body} แบบเดิม — ต้องมีข้อความหรือรูปอย่างน้อยหนึ่งอย่าง
 [HttpPost("{id:guid}/comments"),Authorize(Policy="DefectEdit"),RequestSizeLimit(26_000_000)]
 public async Task<IActionResult>AddComment(Guid id,CancellationToken ct)
 {
  var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);
  if(entity is null||!CanAccess(entity.ProjectId))return NotFound();
  string? body;IReadOnlyList<IFormFile> files=[];
  if(Request.HasFormContentType){var form=await Request.ReadFormAsync(ct);body=form["body"].FirstOrDefault();files=form.Files.GetFiles("files");}
  else body=(await Request.ReadFromJsonAsync<AddDefectCommentRequest>(ct))?.Body;
  if(string.IsNullOrWhiteSpace(body)&&files.Count==0)return BadRequest(new{detail="กรุณาพิมพ์ข้อความหรือแนบรูปอย่างน้อย 1 รูป"});
  if(body is {Length:>4000})return BadRequest(new{detail="ข้อความคอมเมนต์ต้องไม่เกิน 4,000 ตัวอักษร"});
  List<DefectImageStorage.PendingImage> images=[];
  if(files.Count>0){var (read,error)=await imageStorage.ReadAsync(files,[],ct);if(error is not null)return BadRequest(new{detail=error});images=read;}
  await activityService.AddCommentWithImagesAsync(id,body,images,imageStorage,UserId(),ct);
  var commentBody=string.IsNullOrWhiteSpace(body)?"":body.Trim();

  // Defect ที่ผูกกับ CRM ticket แล้ว ให้คอมเมนต์เดียวกันต่อท้าย Description ของ ticket นั้นด้วย — best-effort
  // เท่านั้น: ความล้มเหลวฝั่ง CRM ต้อง log ไว้เฉยๆ ไม่ทำให้ request นี้ล้มเหลวตาม (คอมเมนต์ใน QA Hub บันทึกไปแล้วข้างบน)
  if(entity.CrmSyncStatus=="Linked"&&!string.IsNullOrWhiteSpace(entity.CrmTicketId))
  {
   var me=await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==UserId(),ct);
   if(me is not null)
   {
    try{await crmSendService.AppendCommentAsync(entity,me.UserId,commentBody,me.DisplayName,ct,images.Count);}
    catch(CrmIntegrationException ex){logger.LogError(ex,"Sync คอมเมนต์ไป CRM ticket {TicketId} ไม่สำเร็จ สำหรับ Defect {DefectId}",entity.CrmTicketId,id);}
    catch(CrmNotConfiguredException ex){logger.LogError(ex,"Sync คอมเมนต์ไป CRM ไม่สำเร็จ (ยังไม่ได้ตั้งค่า) สำหรับ Defect {DefectId}",id);}
   }
  }
  return NoContent();
 }
 [HttpDelete("{defectId:guid}/comments/{commentId:guid}"),Authorize(Policy="DefectEdit")]public async Task<IActionResult>DeleteComment(Guid defectId,Guid commentId,CancellationToken ct){var defect=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==defectId&&!x.IsDeleted,ct);if(defect is null||!CanAccess(defect.ProjectId))return NotFound();if(!await activityService.DeleteCommentAsync(defectId,commentId,UserId(),User.IsInRole("SYS_ADMIN"),ct))return Forbid();
  // รูปของคอมเมนต์ไม่มี FK ไป DefectActivities (ดู DefectAttachment.CommentId) — ลบเองหลังลบคอมเมนต์สำเร็จ
  var images=await db.DefectAttachments.Where(x=>x.DefectId==defectId&&x.CommentId==commentId).ToListAsync(ct);
  if(images.Count>0){db.DefectAttachments.RemoveRange(images);await db.SaveChangesAsync(ct);imageStorage.DeleteFiles(defectId,images.Select(x=>x.StoredFileName));}
  return NoContent();}
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
  // นับเฉพาะรูปประกอบของ Defect — รูปในคอมเมนต์ (CommentId != null) มีโควตา 5 รูปต่อคอมเมนต์แยกต่างหาก
  var existing=await db.DefectAttachments.AsNoTracking().Where(x=>x.DefectId==id&&x.CommentId==null).Select(x=>x.SizeBytes).ToListAsync(ct);
  var (pending,error)=await imageStorage.ReadAsync(files,existing,ct);
  if(error is not null)return BadRequest(new{detail=error});
  var rows=await imageStorage.WriteAsync(id,pending,UserId(),null,ct);
  try
  {
   db.DefectAttachments.AddRange(rows);
   db.DefectActivities.Add(new DefectActivity(id,"AttachmentAdded",$"แนบรูปภาพ {pending.Count} ไฟล์",UserId()));
   await db.SaveChangesAsync(ct);
  }
  catch{imageStorage.DeleteFiles(id,rows.Select(x=>x.StoredFileName));throw;}
  return Ok(await ListAttachmentsAsync(id,ct));
 }
 [HttpDelete("{id:guid}/attachments/{attachmentId:guid}"),Authorize(Policy="DefectEdit")]
 public async Task<IActionResult>DeleteAttachment(Guid id,Guid attachmentId,CancellationToken ct)
 {
  var defect=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);
  if(defect is null||!CanAccess(defect.ProjectId))return NotFound();
  await ImportLegacyAttachmentsAsync(id,ct);
  // รูปของคอมเมนต์ลบพร้อมคอมเมนต์เท่านั้น (สิทธิ์เจ้าของคอมเมนต์/SYS_ADMIN) ไม่ใช่ผ่าน endpoint นี้
  var row=await db.DefectAttachments.FirstOrDefaultAsync(x=>x.DefectId==id&&x.DefectAttachmentId==attachmentId&&x.CommentId==null,ct);
  if(row is null)return NotFound();
  db.DefectAttachments.Remove(row);
  db.DefectActivities.Add(new DefectActivity(id,"AttachmentDeleted",$"ลบรูปภาพแนบ {row.FileName}",UserId()));
  await db.SaveChangesAsync(ct);
  // ลบไฟล์หลังบันทึกฐานข้อมูลสำเร็จ — ถ้าลบไฟล์ไม่ได้ (ถูกล็อก) รายการก็หายจากระบบแล้ว ไม่ตอบ 500 ให้ผู้ใช้
  if(!DefectImageStorage.TryDelete(imageStorage.PathOf(id,row.StoredFileName)))logger.LogWarning("ลบไฟล์แนบ {StoredFileName} ของ Defect {DefectId} ไม่สำเร็จ",row.StoredFileName,id);
  return NoContent();
 }
 private string AttachmentDirectory(Guid defectId)=>imageStorage.Directory(defectId);
 private async Task<IReadOnlyList<DefectAttachmentDto>>ListAttachmentsAsync(Guid defectId,CancellationToken ct)
 {
  await ImportLegacyAttachmentsAsync(defectId,ct);
  return await db.DefectAttachments.AsNoTracking().Where(x=>x.DefectId==defectId&&x.CommentId==null).OrderBy(x=>x.UploadedAt).ThenBy(x=>x.FileName).Select(x=>new DefectAttachmentDto(x.DefectAttachmentId,x.FileName,x.SizeBytes)).ToListAsync(ct);
 }
 // รูปที่อัปโหลดก่อนมีตาราง DefectAttachments อยู่บนดิสก์อย่างเดียว — นำเข้าเป็น metadata ครั้งแรกที่เปิด Defect นั้น
 private async Task ImportLegacyAttachmentsAsync(Guid defectId,CancellationToken ct)
 {
  var directory=AttachmentDirectory(defectId);
  if(!Directory.Exists(directory))return;
  var legacy=Directory.EnumerateFiles(directory).Select(path=>new FileInfo(path)).Where(file=>file.Name.Length>33&&Guid.TryParseExact(file.Name[..32],"N",out _)).ToList();
  if(legacy.Count==0)return;
  // เทียบรายไฟล์ด้วย DefectAttachmentId (32 ตัวแรกของชื่อไฟล์) — เดิมข้ามทั้งหมดถ้ามีแถวใดแถวหนึ่งแล้ว ทำให้ไฟล์เก่าที่ยังไม่ถูกนำเข้า
  // หายจากหน้าจอทันทีที่มีรูปในคอมเมนต์ (ใช้ตาราง DefectAttachments เดียวกัน) ทั้งที่ไฟล์ยังอยู่บนดิสก์
  var knownIds=(await db.DefectAttachments.Where(x=>x.DefectId==defectId).Select(x=>x.DefectAttachmentId).ToListAsync(ct)).ToHashSet();
  legacy=legacy.Where(file=>!knownIds.Contains(Guid.ParseExact(file.Name[..32],"N"))).ToList();
  if(legacy.Count==0)return;
  db.DefectAttachments.AddRange(legacy.Select(file=>new DefectAttachment(Guid.ParseExact(file.Name[..32],"N"),defectId,file.Name[33..],file.Name,Math.Max(1,file.Length),DefectImageStorage.ContentTypeFor(file.Extension.ToLowerInvariant()),null,file.CreationTimeUtc)));
  await db.SaveChangesAsync(ct);
 }
 [HttpPost("{id:guid}/test-cases"),Authorize(Policy="DefectEdit")]public async Task<IActionResult>LinkTestCase(Guid id,LinkDefectTestCaseRequest r,CancellationToken ct){var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null||!CanAccess(entity.ProjectId))return NotFound();if(!await db.TestCases.AnyAsync(x=>x.TestCaseId==r.TestCaseId&&!x.IsDeleted,ct))return BadRequest("Test Case not found.");if(await db.DefectTestCaseLinks.AnyAsync(x=>x.DefectId==id&&x.TestCaseId==r.TestCaseId,ct))return Conflict("Already linked.");db.DefectTestCaseLinks.Add(new DefectTestCaseLink(id,r.TestCaseId,UserId()));await db.SaveChangesAsync(ct);await activityService.LogAsync(id,"LinkTestCase",$"Linked test case {r.TestCaseId}",UserId(),ct);return NoContent();}
 [HttpDelete("{id:guid}/test-cases/{testCaseId:guid}"),Authorize(Policy="DefectEdit")]public async Task<IActionResult>UnlinkTestCase(Guid id,Guid testCaseId,CancellationToken ct){var defect=await db.Defects.AsNoTracking().FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(defect is null||!CanAccess(defect.ProjectId))return NotFound();var link=await db.DefectTestCaseLinks.FirstOrDefaultAsync(x=>x.DefectId==id&&x.TestCaseId==testCaseId,ct);if(link is null)return NotFound();db.DefectTestCaseLinks.Remove(link);await db.SaveChangesAsync(ct);await activityService.LogAsync(id,"UnlinkTestCase",$"Unlinked test case {testCaseId}",UserId(),ct);return NoContent();}
 // Bulk ผ่านกฎวงจร Defect เดียวกับหน้าเว็บ (Defect.TransitionTo) — รายการที่ผิดกฎข้ามไปและแจ้งกลับใน failed (รายการอื่นยังบันทึก)
 [HttpPost("bulk"),Authorize(Policy="DefectEdit")]
 public async Task<IActionResult>Bulk(BulkDefectRequest r,CancellationToken ct)
 {
  if(r.Ids is null||r.Ids.Count==0)return BadRequest("No defect IDs provided.");
  var entities=(await db.Defects.Where(x=>r.Ids.Contains(x.DefectId)&&!x.IsDeleted).ToListAsync(ct)).Where(e=>CanAccess(e.ProjectId)).ToList();
  var userId=UserId();
  if(r.Delete==true)
  {
   foreach(var e in entities)e.SoftDelete(userId);
   await db.SaveChangesAsync(ct);
   if(entities.Count>0)await activityService.LogManyAsync(entities.Select(e=>e.DefectId),"Deleted","Defect deleted by bulk action",userId,ct);
   return Ok(new{updated=entities.Count,failed=Array.Empty<BulkDefectFailure>()});
  }
  if(string.IsNullOrWhiteSpace(r.Status)&&(r.Resolution is not null||r.FixedInBuildId.HasValue))return BadRequest("ผลการปิดและ Fix Build ต้องส่งพร้อมสถานะที่จะเปลี่ยน");
  string? target=null;
  if(!string.IsNullOrWhiteSpace(r.Status)&&(target=Defect.Statuses.FirstOrDefault(x=>x.Equals(r.Status.Trim(),StringComparison.OrdinalIgnoreCase))) is null)return BadRequest("สถานะ Defect ไม่ถูกต้อง");
  if(r.FixedInBuildId is {} fixBuild)foreach(var projectId in entities.Select(e=>e.ProjectId).Distinct())if(!await DefectBuilds.BelongsToProjectAsync(db,fixBuild,projectId,ct))return BadRequest(BuildNotInProject);
  var failed=new List<BulkDefectFailure>();var updated=new List<Defect>();var statusChanges=new List<DefectActivity>();
  foreach(var e in entities)
  {
   var from=e.Status;
   if(target is not null)
   {
    // ตรวจกฎก่อนแตะข้อมูล — ผลการปิดที่จะใส่ใช้ตัดสินด้วย (ปิดตรงจาก Open ต้องไม่ใช่ Fixed)
    if(Defect.TransitionError(from,target,r.Resolution?.ResolutionType??e.ResolutionType) is {} error){failed.Add(new(e.DefectId,e.DefectCode,error));continue;}
    if(r.Resolution is not null&&await ApplyResolutionAsync(e,r.Resolution,ct) is {} resolutionError){failed.Add(new(e.DefectId,e.DefectCode,resolutionError));continue;}
    var fix=target=="Resolved"&&from!="Resolved"?r.FixedInBuildId??await DefectBuilds.LatestActiveBuildIdAsync(db,e.ReleaseId,ct):r.FixedInBuildId;
    e.TransitionTo(target,fix,null,userId);
    if(from!=e.Status)statusChanges.Add(StatusActivity(e,from,"bulk action"));
   }
   if(!string.IsNullOrWhiteSpace(r.Severity))e.UpdateSeverity(r.Severity,userId);
   if(r.AssigneeUserId.HasValue)e.Assign(r.AssigneeUserId.Value,userId);
   updated.Add(e);
  }
  db.DefectActivities.AddRange(statusChanges);
  if(r.Resolution is not null)db.DefectActivities.AddRange(updated.Where(e=>e.ResolutionType is not null).Select(e=>new DefectActivity(e.DefectId,"Resolution",ResolutionMessage(e),userId)));
  await db.SaveChangesAsync(ct);
  if(updated.Count>0&&(r.Severity is not null||r.AssigneeUserId.HasValue))
   await activityService.LogManyAsync(updated.Select(e=>e.DefectId),"Updated",$"Defect updated by bulk action{(r.Severity is null?string.Empty:$"; Severity={r.Severity}")}{(r.AssigneeUserId.HasValue?"; Assignee changed":string.Empty)}",userId,ct);
  return Ok(new{updated=updated.Count,failed});
 }
 [HttpPost,Authorize(Policy="DefectEdit")]
 public async Task<ActionResult<DefectDto>>Create(SaveDefectRequest r,CancellationToken ct)
 {
  if(!await db.Projects.AnyAsync(x=>x.ProjectId==r.ProjectId&&x.IsActive,ct))return BadRequest("Project is not active.");
  var projectCode=await db.Projects.Where(x=>x.ProjectId==r.ProjectId).Select(x=>x.ProjectCode).SingleAsync(ct);var prefix=$"{projectCode}-DEF";
  var existing=await db.Defects.Where(x=>x.ProjectId==r.ProjectId&&x.DefectCode.StartsWith(prefix)).Select(x=>x.DefectCode).ToListAsync(ct);var code=BusinessCodeGenerator.NextAvailable(prefix,existing);
  // สร้างเป็น Open ก่อน แล้วเปลี่ยนสถานะผ่านกฎเดียวกับการแก้ไข (สร้างเป็น Closed + Fixed ตรง ๆ ไม่ได้)
  var entity=new Defect(r.ProjectId,r.ReleaseId,r.BuildId,r.ModuleId,code,r.Title,r.Severity,"Open",UserId(),r.Description,r.StepsToReproduce,r.ExpectedResult,r.ActualResult,r.AssigneeUserId);
  if(r.Resolution is not null&&await ApplyResolutionAsync(entity,r.Resolution,ct) is {} createError)return BadRequest(createError);
  if(await TransitionAsync(entity,r.Status,r.FixedInBuildId,r.VerifiedInBuildId,ct) is {} statusError)return BadRequest(statusError);
  await db.Defects.AddAsync(entity,ct);await db.SaveChangesAsync(ct);
  await activityService.LogAsync(entity.DefectId,"Created","Defect created",UserId(),ct);
  return Ok(await MapAsync(entity,ct));
 }
 [HttpPut("{id:guid}"),Authorize(Policy="DefectEdit")]
 public async Task<ActionResult<DefectDto>>Update(Guid id,SaveDefectRequest r,CancellationToken ct)
 {
  var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);
  if(entity is null||!CanAccess(entity.ProjectId))return NotFound();
  var from=entity.Status;
  entity.Update(r.Title,r.Severity,r.Description,r.StepsToReproduce,r.ExpectedResult,r.ActualResult,r.AssigneeUserId,UserId());
  if(r.Resolution is not null&&await ApplyResolutionAsync(entity,r.Resolution,ct) is {} updateError)return BadRequest(updateError);
  if(await TransitionAsync(entity,r.Status,r.FixedInBuildId,r.VerifiedInBuildId,ct) is {} statusError)return BadRequest(statusError);
  if(from!=entity.Status)db.DefectActivities.Add(StatusActivity(entity,from,null));
  await db.SaveChangesAsync(ct);
  await activityService.LogAsync(id,"Updated","Defect details updated",UserId(),ct);
  if(r.Resolution is not null)await LogResolutionAsync(entity,ct);
  return Ok(await MapAsync(entity,ct));
 }
 [HttpPatch("{id:guid}/status"),Authorize(Policy="DefectEdit")]
 public async Task<ActionResult<DefectDto>>ChangeStatus(Guid id,ChangeDefectStatusRequest r,CancellationToken ct)
 {
  var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);
  if(entity is null||!CanAccess(entity.ProjectId))return NotFound();
  var from=entity.Status;
  // ผลการปิดก่อน แล้วค่อยเปลี่ยนสถานะ — กฎ "ปิดตรงจาก Open ต้องไม่ใช่ Fixed" ใช้ผลการปิดที่เพิ่งเลือก
  if(r.Resolution is not null&&await ApplyResolutionAsync(entity,r.Resolution,ct) is {} statusError)return BadRequest(statusError);
  if(await TransitionAsync(entity,r.Status,r.FixedInBuildId,r.VerifiedInBuildId,ct) is {} transitionError)return BadRequest(transitionError);
  if(from!=entity.Status)db.DefectActivities.Add(StatusActivity(entity,from,null));
  await db.SaveChangesAsync(ct);
  if(r.Resolution is not null)await LogResolutionAsync(entity,ct);
  return Ok(await MapAsync(entity,ct));
 }
 /// <summary>เลข Fix/Verified Build ของทั้งหน้าใน query เดียว</summary>
 private async Task<List<DefectDto>>WithBuildNumbersAsync(List<DefectDto> rows,CancellationToken ct)
 {
  var ids=rows.SelectMany(x=>new[]{x.FixedInBuildId,x.VerifiedInBuildId}).Where(x=>x.HasValue).Select(x=>x!.Value).Distinct().ToList();
  if(ids.Count==0)return rows;
  var numbers=await db.Builds.AsNoTracking().Where(b=>ids.Contains(b.BuildId)).ToDictionaryAsync(b=>b.BuildId,b=>b.BuildNumber,ct);
  return rows.Select(x=>x with{FixedInBuildNumber=x.FixedInBuildId is {} f?numbers.GetValueOrDefault(f):null,VerifiedInBuildNumber=x.VerifiedInBuildId is {} v?numbers.GetValueOrDefault(v):null}).ToList();
 }
 /// <summary>ผลตรวจซ้ำ Defect ที่ Resolved บน Build — ผ่าน = Closed (Verified Build = Build นี้), ไม่ผ่าน = Reopen; หมายเหตุเก็บใน Activity</summary>
 [HttpPost("{id:guid}/retest"),Authorize(Policy="DefectEdit")]
 public async Task<ActionResult<DefectDto>>Retest(Guid id,RetestDefectRequest r,CancellationToken ct)
 {
  var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);
  if(entity is null||!CanAccess(entity.ProjectId))return NotFound();
  if(!await DefectBuilds.BelongsToProjectAsync(db,r.BuildId,entity.ProjectId,ct))return BadRequest(BuildNotInProject);
  var from=entity.Status;
  try{entity.Retest(r.BuildId,r.Passed,UserId());}catch(DefectTransitionException ex){return BadRequest(ex.Message);}
  var buildNumber=await db.Builds.Where(b=>b.BuildId==r.BuildId).Select(b=>b.BuildNumber).SingleAsync(ct);
  var note=string.IsNullOrWhiteSpace(r.Note)?"":$" — {(r.Note.Trim().Length>1000?r.Note.Trim()[..1000]:r.Note.Trim())}";
  db.DefectActivities.Add(new DefectActivity(entity.DefectId,r.Passed?"Retested":"Reopened",
   r.Passed?$"ตรวจซ้ำผ่านบน Build {buildNumber} — Status changed from {from} to {entity.Status}{note}":$"ตรวจซ้ำไม่ผ่านบน Build {buildNumber} — Status changed from {from} to {entity.Status} (Reopen ครั้งที่ {entity.ReopenCount}){note}",
   UserId(),from,entity.Status,r.BuildId));
  await db.SaveChangesAsync(ct);
  return Ok(await MapAsync(entity,ct));
 }
 private const string BuildNotInProject="ไม่พบ Build ที่เลือกใน Project ของ Defect นี้";
 /// <summary>เปลี่ยนสถานะตามกฎวงจร Defect — Resolved ที่ไม่ระบุ Fix Build ใช้ Build ล่าสุดของ Release; คืนข้อความ error (ไทย) หรือ null</summary>
 private async Task<string?>TransitionAsync(Defect entity,string status,Guid? fixedInBuildId,Guid? verifiedInBuildId,CancellationToken ct)
 {
  foreach(var build in new[]{fixedInBuildId,verifiedInBuildId})if(build is {} buildId&&!await DefectBuilds.BelongsToProjectAsync(db,buildId,entity.ProjectId,ct))return BuildNotInProject;
  var target=Defect.Statuses.FirstOrDefault(x=>x.Equals(status?.Trim(),StringComparison.OrdinalIgnoreCase));
  if(target is null)return "สถานะ Defect ไม่ถูกต้อง";
  // Resolve รอบใหม่ (รวมหลัง Reopen) ไม่ระบุ Fix Build = Build ล่าสุดของ Release — Fix Build เดิมเป็นประวัติของรอบที่แก้ไม่ผ่าน
  if(target=="Resolved"&&entity.Status!="Resolved"&&fixedInBuildId is null)fixedInBuildId=await DefectBuilds.LatestActiveBuildIdAsync(db,entity.ReleaseId,ct);
  try{entity.TransitionTo(target,fixedInBuildId,verifiedInBuildId,UserId());return null;}
  catch(DefectTransitionException ex){return ex.Message;}
 }
 /// <summary>activity การเปลี่ยนสถานะแบบมีโครงสร้าง (from/to + Build ที่เกี่ยวข้อง) — ใช้นับ Reopen และเวลาแก้ไข</summary>
 private DefectActivity StatusActivity(Defect entity,string from,string? via)
 {
  var build=entity.Status switch{"Resolved"=>entity.FixedInBuildId,"Closed"=>entity.VerifiedInBuildId,_=>null};
  var reopen=Defect.UnresolvedStatuses.Contains(entity.Status)&&from is "Resolved" or "Closed";
  var message=$"Status changed from {from} to {entity.Status}{(reopen?$" (Reopen ครั้งที่ {entity.ReopenCount})":"")}{(via is null?"":$" — {via}")}";
  return new DefectActivity(entity.DefectId,reopen?"Reopened":"StatusChanged",message,UserId(),from,entity.Status,build);
 }
 [HttpPatch("{id:guid}/severity"),Authorize(Policy="DefectEdit")]public async Task<ActionResult<DefectDto>>ChangeSeverity(Guid id,ChangeDefectSeverityRequest r,CancellationToken ct){var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);if(entity is null||!CanAccess(entity.ProjectId))return NotFound();var old=entity.Severity;entity.UpdateSeverity(r.Severity,UserId());await db.SaveChangesAsync(ct);await activityService.LogAsync(id,"SeverityChanged",$"Severity changed from {old} to {r.Severity}",UserId(),ct);return Ok(await MapAsync(entity,ct));}
 // ผลการปิด (Root Cause / Resolution) แก้ภายหลังได้โดยไม่เปลี่ยนสถานะ — Phase 6 คลังความรู้
 [HttpPut("{id:guid}/resolution"),Authorize(Policy="DefectEdit")]
 public async Task<ActionResult<DefectDto>>SaveResolution(Guid id,DefectResolutionRequest r,CancellationToken ct)
 {
  var entity=await db.Defects.FirstOrDefaultAsync(x=>x.DefectId==id&&!x.IsDeleted,ct);
  if(entity is null||!CanAccess(entity.ProjectId))return NotFound();
  if(await ApplyResolutionAsync(entity,r,ct) is {} error)return BadRequest(error);
  await db.SaveChangesAsync(ct);
  await LogResolutionAsync(entity,ct);
  return Ok(await MapAsync(entity,ct));
 }
 // เตือน Defect ที่อาจซ้ำตอนแจ้ง Defect ใหม่ — ค้นคลังความรู้ (Full-Text ไทย + ความหมายถ้าตั้งค่า Embedding) เฉพาะ Defect ใน Project นี้
 [HttpGet("similar"),Authorize(Policy="DefectView")]
 public async Task<ActionResult<IReadOnlyList<SimilarDefectDto>>>Similar([FromQuery]Guid projectId,[FromQuery]string?text,[FromQuery]Guid?excludeId,CancellationToken ct)
 {
  if(!CanAccess(projectId))return Forbid();
  var query=Application.Knowledge.KnowledgeQuery.Normalize(text);
  if(query is null||knowledgeSearch is null)return Ok(Array.Empty<SimilarDefectDto>());
  var results=await knowledgeSearch.SearchAsync(query,new Application.Knowledge.KnowledgeSearchScope([projectId],false,projectId,["Defect"]),8,ct);
  var ids=results.Select(x=>Guid.TryParse(x.SourceKey,out var defectId)?defectId:Guid.Empty).Where(x=>x!=Guid.Empty&&x!=excludeId).ToList();
  var defects=await db.Defects.AsNoTracking().Where(x=>ids.Contains(x.DefectId)&&!x.IsDeleted&&x.ProjectId==projectId)
   .Select(x=>new{x.DefectId,x.DefectCode,x.Title,x.Status,x.Severity,x.ResolutionType}).ToDictionaryAsync(x=>x.DefectId,ct);
  return Ok(results.Where(x=>Guid.TryParse(x.SourceKey,out var defectId)&&defects.ContainsKey(defectId)).Take(5).Select(x=>{var d=defects[Guid.Parse(x.SourceKey)];return new SimilarDefectDto(d.DefectId,d.DefectCode,d.Title,d.Status,d.Severity,d.ResolutionType,x.Snippet);}).ToList());
 }
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
 private async Task<DefectDto>MapAsync(Defect x,CancellationToken ct){var duplicateCode=x.DuplicateOfDefectId is {} dupId?await db.Defects.AsNoTracking().Where(d=>d.DefectId==dupId).Select(d=>d.DefectCode).FirstOrDefaultAsync(ct):null;var regressionCode=x.RegressionOfDefectId is {} regId?await db.Defects.AsNoTracking().Where(d=>d.DefectId==regId).Select(d=>d.DefectCode).FirstOrDefaultAsync(ct):null;var buildIds=new[]{x.FixedInBuildId,x.VerifiedInBuildId}.Where(b=>b.HasValue).Select(b=>b!.Value).ToList();var buildNumbers=buildIds.Count==0?new Dictionary<Guid,string>():await db.Builds.AsNoTracking().Where(b=>buildIds.Contains(b.BuildId)).ToDictionaryAsync(b=>b.BuildId,b=>b.BuildNumber,ct);return new DefectDto(x.DefectId,x.ProjectId,x.ReleaseId,x.BuildId,x.ModuleId,x.DefectCode,x.Title,x.Description,x.StepsToReproduce,x.ExpectedResult,x.ActualResult,x.Severity,x.Status,x.AssigneeUserId,x.CreatedBy,x.CreatedAt,x.UpdatedAt,x.CrmTicketId,x.CrmSyncStatus,x.CrmLastSyncedAt,x.ResolutionType,x.RootCauseCategory,x.RootCauseNote,x.ResolutionNote,x.DuplicateOfDefectId,duplicateCode,x.FixedInBuildId,x.FixedInBuildId is {} f?buildNumbers.GetValueOrDefault(f):null,x.VerifiedInBuildId,x.VerifiedInBuildId is {} v?buildNumbers.GetValueOrDefault(v):null,x.ResolvedAt,x.ClosedAt,x.ReopenCount,x.LastRetestAt,x.LastRetestResult,x.RegressionOfDefectId,regressionCode);}
 public const string RootCauseCategory="DefectRootCause";
 /// <summary>ตรวจหมวดสาเหตุกับ Master Option (หมวด DefectRootCause ที่เปิดใช้) และ Defect ต้นฉบับ (Project เดียวกัน) แล้วบันทึก — คืนข้อความผิดพลาด หรือ null</summary>
 private async Task<string?>ApplyResolutionAsync(Defect entity,DefectResolutionRequest r,CancellationToken ct)
 {
  var category=string.IsNullOrWhiteSpace(r.RootCauseCategory)?null:r.RootCauseCategory.Trim();
  if(category is not null&&!await db.MasterOptions.AnyAsync(x=>x.Category==RootCauseCategory&&x.Value==category&&x.IsActive,ct))return "หมวดสาเหตุไม่ถูกต้อง หรือถูกปิดใช้งาน";
  if(r.DuplicateOfDefectId is {} dup&&!await db.Defects.AnyAsync(x=>x.DefectId==dup&&!x.IsDeleted&&x.ProjectId==entity.ProjectId,ct))return "ไม่พบ Defect ต้นฉบับใน Project เดียวกัน";
  try{entity.SetResolution(r.ResolutionType,category,r.RootCauseNote,r.ResolutionNote,r.DuplicateOfDefectId,UserId());return null;}
  catch(ArgumentException ex){return ex.Message switch{"Invalid resolution type."=>"ผลการปิดไม่ถูกต้อง","Root cause category is required when the defect is fixed."=>"แก้ไขแล้ว (Fixed) ต้องเลือกหมวดสาเหตุ","Duplicate requires the original defect."=>"ซ้ำ (Duplicate) ต้องระบุ Defect ต้นฉบับ",_=>ex.Message};}
 }
 private static string ResolutionMessage(Defect entity)=>$"Resolution: {entity.ResolutionType}{(entity.RootCauseCategory is null?"":$" · Root cause: {entity.RootCauseCategory}")}";
 private Task LogResolutionAsync(Defect entity,CancellationToken ct)=>activityService.LogAsync(entity.DefectId,"Resolution",ResolutionMessage(entity),UserId(),ct);
 private bool CanAccess(Guid projectId)=>projectCtx.AllowedProjectIds.Length==0||projectCtx.AllowedProjectIds.Contains(projectId);
 private Guid?UserId()=>Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("sub"),out var id)?id:null;
}
public sealed record SaveDefectRequest(Guid ProjectId,Guid?ReleaseId,Guid?BuildId,Guid?ModuleId,string Title,string Severity="Medium",string Status="Open",string?Description=null,string?StepsToReproduce=null,string?ExpectedResult=null,string?ActualResult=null,Guid?AssigneeUserId=null,DefectResolutionRequest?Resolution=null,Guid?FixedInBuildId=null,Guid?VerifiedInBuildId=null);
public sealed record DefectResolutionRequest(string ResolutionType,string?RootCauseCategory=null,string?RootCauseNote=null,string?ResolutionNote=null,Guid?DuplicateOfDefectId=null);
public sealed record SimilarDefectDto(Guid DefectId,string DefectCode,string Title,string Status,string Severity,string?ResolutionType,string Snippet);
/// <summary>FixedInBuildId = Build ที่แก้ (ตอนเป็น Resolved, ไม่ระบุ = Build ล่าสุดของ Release); VerifiedInBuildId = Build ที่ QA ตรวจซ้ำผ่าน (ตอน Resolved → Closed, ไม่ระบุ = Fix Build)</summary>
public sealed record ChangeDefectStatusRequest(string Status,DefectResolutionRequest?Resolution=null,Guid?FixedInBuildId=null,Guid?VerifiedInBuildId=null);
public sealed record ChangeDefectSeverityRequest(string Severity);
public sealed record AddDefectCommentRequest(string Body);
public sealed record LinkDefectTestCaseRequest(Guid TestCaseId);
public sealed record BulkDefectRequest(IReadOnlyList<Guid>?Ids,string?Status=null,string?Severity=null,Guid?AssigneeUserId=null,bool?Delete=null,DefectResolutionRequest?Resolution=null,Guid?FixedInBuildId=null);
public sealed record BulkDefectFailure(Guid DefectId,string DefectCode,string Error);
public sealed record RetestDefectRequest(Guid BuildId,bool Passed,string?Note=null);
public sealed record DefectDto(Guid DefectId,Guid ProjectId,Guid?ReleaseId,Guid?BuildId,Guid?ModuleId,string DefectCode,string Title,string?Description,string?StepsToReproduce,string?ExpectedResult,string?ActualResult,string Severity,string Status,Guid?AssigneeUserId,Guid?CreatedByUserId,DateTime CreatedAt,DateTime?UpdatedAt,string?CrmTicketId,string CrmSyncStatus,DateTime?CrmLastSyncedAt,string?ResolutionType=null,string?RootCauseCategory=null,string?RootCauseNote=null,string?ResolutionNote=null,Guid?DuplicateOfDefectId=null,string?DuplicateOfDefectCode=null,Guid?FixedInBuildId=null,string?FixedInBuildNumber=null,Guid?VerifiedInBuildId=null,string?VerifiedInBuildNumber=null,DateTime?ResolvedAt=null,DateTime?ClosedAt=null,int ReopenCount=0,DateTime?LastRetestAt=null,string?LastRetestResult=null,Guid?RegressionOfDefectId=null,string?RegressionOfDefectCode=null);
public sealed record SendToCrmRequest(string AssignToStaffCode,bool Relink=false);
public sealed record SendToCrmResponse(string CrmTicketId,string CrmSyncStatus,DateTime?CrmLastSyncedAt);
public sealed record CrmReassignRequest(string AssignToStaffCode);
public sealed record DefectActivityDto(Guid ActivityId,Guid DefectId,string ActivityType,string? Description,Guid? PerformedByUserId,DateTime PerformedAt,IReadOnlyList<DefectAttachmentDto>? Attachments);
public sealed record DefectTestCaseDto(Guid TestCaseId,string TestCaseCode,string Title);
public sealed record DefectSummaryDto(int Open,int InProgress,int Closed);
public sealed record DefectListResultDto(IReadOnlyList<DefectDto> Rows,int Total,int Open,int InProgress,int Closed);
public sealed record DefectStatsDto(int Total,int Open,int InProgress,int Resolved,int Closed,int Rejected,int Critical,int High,int Medium,int Low,int OldestOpenAgeDays,int UnassignedActive,double ClosureRate,IReadOnlyList<DefectModuleCountDto> Modules);
public sealed record DefectModuleCountDto(Guid? ModuleId,string? ModuleCode,string ModuleName,int Count);
public sealed record DefectAttachmentDto(Guid AttachmentId,string FileName,long Size);
/// <summary>Source: "QaHub" = คอมเมนต์ใน QA Hub (ลบได้โดยเจ้าของ/SYS_ADMIN), "Crm" = ข้อความที่ sync มาจาก CRM ticket</summary>
public sealed record DefectCommentDto(Guid CommentId,string Source,string Body,Guid? AuthorUserId,string AuthorName,DateTime CreatedAt,IReadOnlyList<DefectAttachmentDto> Attachments);
