using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Performance;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Infrastructure.Identity;

namespace ProMaxx2.QA.Api.Controllers;

/// <summary>
/// ผลงานและการประเมินทีม QA — ข้อมูลไม่ได้ผูกกับ Project เดียวจึงไม่ใช้ [RequireProjectAccess]
/// แต่ metric ทุกตัวกรองเฉพาะ Project ที่ผู้ดูเป็นสมาชิก และ service ตรวจว่าผู้ที่ไม่มี PERFORMANCE.EDIT ดูได้เฉพาะของตัวเอง
/// </summary>
[ApiController, Route("api/v1/performance"), Authorize(Policy = "PerformanceView")]
public sealed class PerformanceController(PerformanceService service, EvaluationService evaluations, PerformanceReportService reports, PerformanceShareLinkService shareLinks, WeeklyReportService weekly, ProjectAccessService projectAccess, PerformanceEvidenceStorage evidence, ILogger<PerformanceController> logger) : ControllerBase
{
    /// <summary>สรุปผลงานรายคนในหน้าเดียว (หน้า "สรุปผลงาน") — ข้อมูลชุดเดียวกับที่ลิงก์แชร์ให้ HR แสดง</summary>
    [HttpGet("users/{userId:guid}/report")]
    public Task<ActionResult<EmployeeReportDto>> Report(Guid userId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] bool evaluations = true, [FromQuery] bool workLogs = true, CancellationToken ct = default)
        => Run(async v => await reports.BuildAsync(v, userId, from, to, evaluations, workLogs, ct), ct);

    [HttpGet("users/{userId:guid}/share-links")]
    public Task<ActionResult<IReadOnlyList<PerformanceShareLinkDto>>> ShareLinks(Guid userId, CancellationToken ct)
        => Run(async v => await shareLinks.ListAsync(v, userId, ct), ct);

    [HttpPost("users/{userId:guid}/share-links")]
    public Task<ActionResult<PerformanceShareLinkDto>> CreateShareLink(Guid userId, CreatePerformanceShareLinkRequest request, CancellationToken ct)
        => Run(async v => await shareLinks.CreateAsync(v, userId, request, ct), ct);

    [HttpPost("share-links/{id:guid}/revoke")]
    public Task<ActionResult<PerformanceShareLinkDto>> RevokeShareLink(Guid id, CancellationToken ct)
        => Run(async v => await shareLinks.RevokeAsync(v, id, ct), ct);

    /// <summary>สรุปประจำสัปดาห์ของทีมสำหรับส่งผู้บริหาร — weekStart ไม่ระบุ = สัปดาห์นี้ (วันที่อื่นจะถูกปัดเป็นวันจันทร์ของสัปดาห์นั้น)</summary>
    [HttpGet("weekly")]
    public Task<ActionResult<WeeklyReportDto>> Weekly([FromQuery] DateOnly? weekStart, CancellationToken ct)
        => Run(async v => await weekly.BuildAsync(v, weekStart, ct), ct);

    [HttpPut("weekly/{weekStart}/note")]
    public Task<ActionResult<WeeklyNarrativeDto>> SaveWeeklyNote(DateOnly weekStart, SaveWeeklyReportNoteRequest request, CancellationToken ct)
        => Run(async v => await weekly.SaveNoteAsync(v, weekStart, request, ct), ct);

    [HttpGet("team")]
    public Task<ActionResult<TeamOverviewDto>> Team([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Run(async v => await service.GetTeamAsync(v, from, to, ct), ct);

    [HttpGet("users")]
    public Task<ActionResult<IReadOnlyList<UserOptionDto>>> Users(CancellationToken ct)
        => Run(async v => await service.ListUsersAsync(v, ct), ct);

    [HttpGet("users/{userId:guid}/summary")]
    public Task<ActionResult<UserPerformanceDto>> Summary(Guid userId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Run(async v => await service.GetUserAsync(v, userId, from, to, ct), ct);

    [HttpGet("users/{userId:guid}/timeline")]
    public Task<ActionResult<ActivityPageDto>> Timeline(Guid userId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int page = 1, [FromQuery] int size = 20, CancellationToken ct = default)
        => Run(async v => await service.GetTimelineAsync(v, userId, from, to, page, size, ct), ct);

    [HttpPut("users/{userId:guid}/profile")]
    public Task<ActionResult<EmployeeProfileDto>> SaveProfile(Guid userId, SaveEmployeeProfileRequest request, CancellationToken ct)
        => Run(async v =>
        {
            var saved = await service.SaveProfileAsync(v, userId, request, ct);
            // ตั้งเป็นทดลองงาน/ขยาย หรือเปลี่ยนวันเริ่มงาน → สร้าง/เลื่อนรอบประเมิน 30/60/90/สรุป ให้อัตโนมัติ
            await evaluations.SyncProbationRoundsAsync(v, userId, ct);
            return saved;
        }, ct);

    [HttpGet("users/{userId:guid}/work-logs")]
    public Task<ActionResult<IReadOnlyList<WorkLogDto>>> WorkLogs(Guid userId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Run(async v => await service.ListWorkLogsAsync(v, userId, from, to, ct), ct);

    [HttpPost("work-logs")]
    public Task<ActionResult<WorkLogDto>> CreateWorkLog(SaveWorkLogRequest request, CancellationToken ct)
        => Run(async v => await service.CreateWorkLogAsync(v, request, ct), ct);

    [HttpPut("work-logs/{id:guid}")]
    public Task<ActionResult<WorkLogDto>> UpdateWorkLog(Guid id, SaveWorkLogRequest request, CancellationToken ct)
        => Run(async v => await service.UpdateWorkLogAsync(v, id, request, ct), ct);

    [HttpDelete("work-logs/{id:guid}")]
    public async Task<IActionResult> DeleteWorkLog(Guid id, CancellationToken ct)
    {
        var result = await Run(async v => { await service.DeleteWorkLogAsync(v, id, ct); return true; }, ct);
        // soft delete — เก็บโฟลเดอร์หลักฐานไว้เพื่อกู้คืนได้
        return result.Result ?? NoContent();
    }

    /// <summary>แนบไฟล์หลักฐาน (PNG/JPG/WebP/PDF) — สิทธิ์เดียวกับการแก้บันทึกงาน</summary>
    [HttpPost("work-logs/{id:guid}/attachments"), RequestSizeLimit(26_000_000)]
    public Task<ActionResult<WorkLogDto>> UploadAttachments(Guid id, [FromForm] List<IFormFile> files, CancellationToken ct)
        => Run(async v =>
        {
            var existing = await service.PrepareAttachmentUploadAsync(v, id, ct);
            var (pending, error) = await evidence.ReadAsync(files, existing, ct);
            if (error is not null) throw new ArgumentException(error);
            var rows = await evidence.WriteAsync(id, pending, v.UserId, ct);
            try { return await service.AddAttachmentsAsync(v, id, rows, ct); }
            catch { evidence.DeleteFiles(id, rows.Select(x => x.StoredFileName)); throw; }
        }, ct);

    [HttpGet("work-logs/{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> Attachment(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var result = await Run(async v => await service.FindVisibleAttachmentAsync(v, id, attachmentId, ct), ct);
        if (result.Result is not null) return result.Result;
        var path = evidence.PathOf(id, result.Value!.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound(Problem("ไม่พบข้อมูล", "ไม่พบไฟล์แนบบนเซิร์ฟเวอร์", 404));
        Response.Headers.ContentDisposition = $"inline; filename=\"{attachmentId:N}{Path.GetExtension(result.Value.StoredFileName)}\"";
        return PhysicalFile(path, result.Value.ContentType);
    }

    [HttpDelete("work-logs/{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DeleteAttachment(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var result = await Run(async v => await service.DeleteAttachmentAsync(v, id, attachmentId, ct), ct);
        if (result.Result is not null) return result.Result;
        if (!DefectImageStorage.TryDelete(evidence.PathOf(id, result.Value!.StoredFileName))) logger.LogWarning("ลบไฟล์หลักฐาน {StoredFileName} ของบันทึกงาน {WorkLogEntryId} ไม่สำเร็จ", result.Value.StoredFileName, id);
        return NoContent();
    }

    [HttpGet("evaluations")]
    public Task<ActionResult<IReadOnlyList<EvaluationSummaryDto>>> Evaluations([FromQuery] Guid? userId, [FromQuery] bool openOnly = false, CancellationToken ct = default)
        => Run(async v => await evaluations.ListAsync(v, userId, openOnly, ct), ct);

    [HttpGet("evaluations/team-comparison")]
    public Task<ActionResult<TeamComparisonDto>> TeamComparison([FromQuery] int? year, CancellationToken ct)
        => Run(async v => await evaluations.CompareTeamAsync(v, year ?? DateTime.UtcNow.AddHours(7).Year, ct), ct);

    [HttpGet("users/{userId:guid}/evaluation-comparison")]
    public Task<ActionResult<EvaluationComparisonDto>> Comparison(Guid userId, CancellationToken ct)
        => Run(async v => await evaluations.CompareAsync(v, userId, ct), ct);

    [HttpGet("evaluations/{id:guid}")]
    public Task<ActionResult<EvaluationDetailDto>> Evaluation(Guid id, CancellationToken ct)
        => Run(async v => await evaluations.GetAsync(v, id, ct), ct);

    [HttpPost("evaluations/quarterly")]
    public Task<ActionResult<CreateQuarterlyEvaluationsResult>> CreateQuarterly(CreateQuarterlyEvaluationsRequest request, CancellationToken ct)
        => Run(async v => await evaluations.CreateQuarterlyAsync(v, request, ct), ct);

    [HttpPut("evaluations/{id:guid}/self")]
    public Task<ActionResult<EvaluationDetailDto>> SaveSelf(Guid id, SaveSelfEvaluationRequest request, CancellationToken ct)
        => Run(async v => await evaluations.SaveSelfAsync(v, id, request, ct), ct);

    [HttpPut("evaluations/{id:guid}/manager")]
    public Task<ActionResult<EvaluationDetailDto>> SaveManager(Guid id, SaveManagerEvaluationRequest request, CancellationToken ct)
        => Run(async v => await evaluations.SaveManagerAsync(v, id, request, ct), ct);

    [HttpPost("evaluations/{id:guid}/acknowledge")]
    public Task<ActionResult<EvaluationDetailDto>> Acknowledge(Guid id, AcknowledgeEvaluationRequest request, CancellationToken ct)
        => Run(async v => await evaluations.AcknowledgeAsync(v, id, request, ct), ct);

    [HttpPost("evaluations/{id:guid}/reopen")]
    public Task<ActionResult<EvaluationDetailDto>> Reopen(Guid id, ReopenEvaluationRequest request, CancellationToken ct)
        => Run(async v => await evaluations.ReopenAsync(v, id, request, ct), ct);

    /// <summary>หัวหน้าตรงขอให้หัวหน้าแผนกอื่นประเมิน — สร้างลิงก์ /?er=code 1 ลิงก์ต่อผู้ประเมิน 1 คน</summary>
    [HttpPost("evaluations/{id:guid}/external-reviews")]
    public Task<ActionResult<EvaluationExternalReviewDto>> CreateExternalReview(Guid id, CreateExternalReviewRequest request, CancellationToken ct)
        => Run(async v => await evaluations.CreateExternalReviewAsync(v, id, request, ct), ct);

    [HttpPost("evaluations/{id:guid}/external-reviews/{reviewId:guid}/revoke")]
    public Task<ActionResult<EvaluationExternalReviewDto>> RevokeExternalReview(Guid id, Guid reviewId, CancellationToken ct)
        => Run(async v => await evaluations.RevokeExternalReviewAsync(v, id, reviewId, ct), ct);

    [HttpGet("templates")]
    public Task<ActionResult<IReadOnlyList<EvaluationTemplateDto>>> Templates(CancellationToken ct)
        => Run(async v => await evaluations.ListTemplatesAsync(v, ct), ct);

    [HttpPut("templates/{id:guid}")]
    public Task<ActionResult<EvaluationTemplateDto>> SaveTemplate(Guid id, SaveEvaluationTemplateRequest request, CancellationToken ct)
        => Run(async v => await evaluations.SaveTemplateAsync(v, id, request, ct), ct);

    private async Task<ActionResult<T>> Run<T>(Func<PerformanceViewer, Task<T>> action, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var userId)) return Unauthorized();
        var canManage = User.IsInRole("SYS_ADMIN") || User.HasClaim("permission", "PERFORMANCE.EDIT");
        var viewer = new PerformanceViewer(userId, canManage, await projectAccess.GetAllowedProjectIdsAsync(userId, ct));
        try { return await action(viewer); }
        catch (PerformanceAccessDeniedException ex) { return StatusCode(StatusCodes.Status403Forbidden, Problem("ไม่มีสิทธิ์", ex.Message, 403)); }
        catch (EntityNotFoundException ex) { return NotFound(Problem("ไม่พบข้อมูล", ex.Message, 404)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
        // เฉพาะกฎธุรกิจที่โยนจาก Domain/Application — InvalidOperationException ของ EF (เช่น query แปลงเป็น SQL ไม่ได้) ต้องไม่หลุดข้อความภายในไปถึงผู้ใช้
        catch (InvalidOperationException ex) when (ex.Source?.StartsWith("ProMaxx2.", StringComparison.Ordinal) == true) { return Conflict(Problem("ดำเนินการไม่ได้", ex.Message, 409)); }
    }

    private static ProblemDetails Problem(string title, string detail, int status) => new() { Title = title, Detail = detail, Status = status };
}
