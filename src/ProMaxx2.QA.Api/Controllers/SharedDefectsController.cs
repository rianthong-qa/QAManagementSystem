using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Controllers;

// ลิงก์อ่านอย่างเดียวของ Defect ที่แนบไปกับ CRM ticket (ดู DefectShareLinkService) — AllowAnonymous เพราะผู้เปิดคือทีม
// CRM/Dev ที่ไม่มีบัญชี QA Hub; สิทธิ์มาจาก short code สุ่ม (หรือ token รุ่นแรก) ที่ผูกกับ Defect เดียวเท่านั้น
// ไม่ใส่ RequireProjectAccess เพราะไม่มีผู้ใช้ให้ตรวจ Project; คืนรายละเอียด Defect, รูปแนบ และคอมเมนต์พร้อมรูป
// (ไม่มีประวัติกิจกรรมภายในอื่น ๆ และไม่เปิดให้เขียนคอมเมนต์จากหน้านี้)
[ApiController, Route("api/v1/shared/defects"), AllowAnonymous, EnableRateLimiting("share")]
public sealed class SharedDefectsController(QaDbContext db, DefectShareLinkService shareLinks, DefectActivityService activities, DefectImageStorage imageStorage) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<ActionResult<SharedDefectDto>> Get(string token, CancellationToken ct)
    {
        if (await shareLinks.ResolveAsync(token, ct) is not { } defectId) return NotFound();
        var d = await db.Defects.AsNoTracking().FirstOrDefaultAsync(x => x.DefectId == defectId && !x.IsDeleted, ct);
        if (d is null) return NotFound();
        var project = await db.Projects.AsNoTracking().Where(x => x.ProjectId == d.ProjectId).Select(x => x.ProjectName).FirstOrDefaultAsync(ct);
        var module = d.ModuleId is { } moduleId ? await db.Modules.AsNoTracking().Where(x => x.ModuleId == moduleId).Select(x => x.ModuleCode + " · " + x.ModuleName).FirstOrDefaultAsync(ct) : null;
        var release = d.ReleaseId is { } releaseId ? await db.Releases.AsNoTracking().Where(x => x.ReleaseId == releaseId).Select(x => x.ReleaseCode + " · " + x.Version).FirstOrDefaultAsync(ct) : null;
        var build = d.BuildId is { } buildId ? await db.Builds.AsNoTracking().Where(x => x.BuildId == buildId).Select(x => x.BuildNumber).FirstOrDefaultAsync(ct) : null;
        var reporter = d.CreatedBy is { } createdBy ? await db.Users.AsNoTracking().Where(x => x.UserId == createdBy).Select(x => x.DisplayName).FirstOrDefaultAsync(ct) : null;
        var assignee = d.AssigneeUserId is { } assigneeId ? await db.Users.AsNoTracking().Where(x => x.UserId == assigneeId).Select(x => x.DisplayName).FirstOrDefaultAsync(ct) : null;
        var attachments = await db.DefectAttachments.AsNoTracking().Where(x => x.DefectId == defectId && x.CommentId == null).OrderBy(x => x.UploadedAt).ThenBy(x => x.FileName)
            .Select(x => new DefectAttachmentDto(x.DefectAttachmentId, x.FileName, x.SizeBytes)).ToListAsync(ct);
        var testCases = await db.DefectTestCaseLinks.AsNoTracking().Where(x => x.DefectId == defectId)
            .Join(db.TestCases, x => x.TestCaseId, y => y.TestCaseId, (x, y) => new SharedDefectTestCaseDto(y.TestCaseCode, y.Title)).ToListAsync(ct);
        // ไม่ส่ง UserId ภายในออกไปให้คนนอก — ใช้แค่ชื่อผู้เขียน
        var comments = (await activities.GetCommentsAsync(defectId, ct)).Select(x => x with { AuthorUserId = null }).ToList();
        return Ok(new SharedDefectDto(d.DefectCode, d.Title, d.Severity, d.Status, project, module, release, build, reporter, assignee,
            d.Description, d.StepsToReproduce, d.ExpectedResult, d.ActualResult, d.CreatedAt, d.UpdatedAt, d.CrmTicketId, testCases, attachments, comments));
    }

    // ใช้ได้ทั้งรูปประกอบของ Defect และรูปในคอมเมนต์ — ตรวจว่ารูปเป็นของ Defect ที่ code ชี้เท่านั้น
    [HttpGet("{token}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> Attachment(string token, Guid attachmentId, CancellationToken ct)
    {
        if (await shareLinks.ResolveAsync(token, ct) is not { } defectId) return NotFound();
        if (!await db.Defects.AsNoTracking().AnyAsync(x => x.DefectId == defectId && !x.IsDeleted, ct)) return NotFound();
        var row = await db.DefectAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.DefectId == defectId && x.DefectAttachmentId == attachmentId, ct);
        var path = row is null ? null : imageStorage.PathOf(defectId, row.StoredFileName);
        if (row is null || !System.IO.File.Exists(path)) return NotFound();
        Response.Headers.ContentDisposition = $"inline; filename=\"{attachmentId:N}{Path.GetExtension(row.StoredFileName)}\"";
        return PhysicalFile(path, row.ContentType);
    }
}

public sealed record SharedDefectTestCaseDto(string TestCaseCode, string Title);
public sealed record SharedDefectDto(string DefectCode, string Title, string Severity, string Status, string? ProjectName, string? Module, string? Release, string? Build,
    string? ReportedBy, string? Assignee, string? Description, string? StepsToReproduce, string? ExpectedResult, string? ActualResult,
    DateTime CreatedAt, DateTime? UpdatedAt, string? CrmTicketId, IReadOnlyList<SharedDefectTestCaseDto> TestCases, IReadOnlyList<DefectAttachmentDto> Attachments,
    IReadOnlyList<DefectCommentDto> Comments);
