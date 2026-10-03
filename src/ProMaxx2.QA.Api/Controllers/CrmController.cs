using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController]
[Route("api/v1/crm")]
[Authorize(Policy = "CrmView")]
public sealed class CrmController(
    CrmApiClient crm,
    CrmConfigurationService configuration,
    CrmTicketDetailService detail,
    QaDbContext db,
    ProjectAccessContext projectContext,
    DefectActivityService activityService,
    CrmSendToCrmService crmSendService,
    CrmStaffDirectoryCache staffDirectory) : ControllerBase
{
    // ชุดเดียวกับตัวกรองสถานะในหน้า CRM (ค่าที่ CRM ใช้จริง)
    public static readonly string[] EditableStatuses = ["Open", "Continue", "Approve", "Develop", "Planning", "Test", "EditErr", "Finish", "Close"];
    private const int CrmReplyMaxLength = 1000;

    [HttpGet("connection")]
    public async Task<ActionResult<CrmConnectionStatus>> Connection(CancellationToken ct)
    {
        var userId = UserId();
        if (!userId.HasValue) return Unauthorized();
        var view = await configuration.GetViewAsync(userId.Value, ct);
        return Ok(new CrmConnectionStatus(view.HasPassword && view.IsEnabled, view.IsEnabled, string.IsNullOrWhiteSpace(view.Username) ? null : view.Username, view.UpdatedAt));
    }

    [HttpPost("connection/test")]
    public async Task<ActionResult<CrmConnectionProbeResult>> ProbeConnection(CancellationToken ct)
    {
        var userId = UserId();
        if (!userId.HasValue) return Unauthorized();
        try
        {
            await crm.ProbeConnectionAsync(userId.Value, ct);
            return Ok(new CrmConnectionProbeResult(true, DateTimeOffset.UtcNow));
        }
        catch (CrmNotConfiguredException ex)
        {
            return Unauthorized(Failure("CRM_NOT_CONFIGURED", "ยังไม่ได้ตั้งค่าบัญชี CRM", ex.Message, StatusCodes.Status401Unauthorized));
        }
        catch (CrmTimeoutException ex)
        {
            return StatusCode(StatusCodes.Status408RequestTimeout, Failure("CRM_TIMEOUT", "CRM ตอบกลับไม่ทันเวลา", ex.Message, StatusCodes.Status408RequestTimeout));
        }
        catch (CrmBadResponseException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, Failure("CRM_BAD_RESPONSE", "ข้อมูลจาก CRM ไม่ถูกต้อง", ex.Message, StatusCodes.Status502BadGateway));
        }
        catch (CrmIntegrationException ex)
        {
            return MapIntegrationError(ex);
        }
    }

    [HttpGet("tickets")]
    public async Task<ActionResult<CrmTicketListResult>> Tickets(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] bool refresh = false,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return BadRequest(Failure("CRM_INVALID_QUERY", "ข้อมูลค้นหาไม่ถูกต้อง", "page ต้องเริ่มที่ 1 และ pageSize ต้องอยู่ระหว่าง 1 ถึง 100", StatusCodes.Status400BadRequest));
        if (from.HasValue && to.HasValue && to.Value < from.Value)
            return BadRequest(Failure("CRM_INVALID_QUERY", "ช่วงวันที่ไม่ถูกต้อง", "วันที่สิ้นสุดต้องไม่น้อยกว่าวันที่เริ่มต้น", StatusCodes.Status400BadRequest));

        var userId = UserId();
        if (!userId.HasValue) return Unauthorized();

        try
        {
            var query = new CrmTicketListQuery(page, pageSize, search, status, from, to);
            return Ok(await crm.ListJobsAsync(userId.Value, query, ct, refresh));
        }
        catch (CrmNotConfiguredException ex)
        {
            return Unauthorized(Failure("CRM_NOT_CONFIGURED", "ยังไม่ได้เชื่อมต่อ CRM", ex.Message, StatusCodes.Status401Unauthorized));
        }
        catch (CrmResultTooLargeException ex)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, Failure("CRM_RESULT_TOO_LARGE", "ผลลัพธ์จาก CRM มีขนาดใหญ่เกินไป", ex.Message, StatusCodes.Status413PayloadTooLarge));
        }
        catch (CrmTimeoutException ex)
        {
            return StatusCode(StatusCodes.Status408RequestTimeout, Failure("CRM_TIMEOUT", "CRM ตอบกลับไม่ทันเวลา", ex.Message, StatusCodes.Status408RequestTimeout));
        }
        catch (CrmBadResponseException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, Failure("CRM_BAD_RESPONSE", "ข้อมูลจาก CRM ไม่ถูกต้อง", ex.Message, StatusCodes.Status502BadGateway));
        }
        catch (CrmIntegrationException ex)
        {
            return MapIntegrationError(ex);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Failure("CRM_INVALID_QUERY", "ข้อมูลค้นหาไม่ถูกต้อง", ex.Message, StatusCodes.Status400BadRequest));
        }
    }

    [HttpGet("tickets/{jobNo}")]
    public async Task<ActionResult<CrmTicketDetailResult>> TicketDetail(string jobNo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jobNo))
            return BadRequest(Failure("CRM_INVALID_QUERY", "ข้อมูลค้นหาไม่ถูกต้อง", "ต้องระบุ Job No.", StatusCodes.Status400BadRequest));

        var userId = UserId();
        if (!userId.HasValue) return Unauthorized();

        try
        {
            return Ok(await detail.GetAsync(userId.Value, jobNo, ct));
        }
        catch (CrmTicketNotFoundException ex)
        {
            return NotFound(Failure("CRM_TICKET_NOT_FOUND", "ไม่พบ Ticket", ex.Message, StatusCodes.Status404NotFound));
        }
        catch (CrmNotConfiguredException ex)
        {
            return Unauthorized(Failure("CRM_NOT_CONFIGURED", "ยังไม่ได้เชื่อมต่อ CRM", ex.Message, StatusCodes.Status401Unauthorized));
        }
        catch (CrmTimeoutException ex)
        {
            return StatusCode(StatusCodes.Status408RequestTimeout, Failure("CRM_TIMEOUT", "CRM ตอบกลับไม่ทันเวลา", ex.Message, StatusCodes.Status408RequestTimeout));
        }
        catch (CrmBadResponseException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, Failure("CRM_BAD_RESPONSE", "ข้อมูลจาก CRM ไม่ถูกต้อง", ex.Message, StatusCodes.Status502BadGateway));
        }
        catch (CrmIntegrationException ex)
        {
            return MapIntegrationError(ex);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Failure("CRM_INVALID_QUERY", "ข้อมูลค้นหาไม่ถูกต้อง", ex.Message, StatusCodes.Status400BadRequest));
        }
    }

    // รหัส → ชื่อพนักงานจาก BlueID directory (cache 1 ชม.) สำหรับแสดง "6101 เหรียญทอง" ในหน้า CRM — ไม่มี email
    [HttpGet("staff")]
    public async Task<ActionResult<IReadOnlyList<CrmStaffDto>>> Staff(CancellationToken ct)
    {
        var userId = UserId();
        if (!userId.HasValue) return Unauthorized();
        try { return Ok(await staffDirectory.GetAsync(token => crm.GetSeniorUserDirectoryAsync(userId.Value, token), ct)); }
        catch (CrmNotConfiguredException ex) { return Unauthorized(Failure("CRM_NOT_CONFIGURED", "ยังไม่ได้ตั้งค่าบัญชี CRM", ex.Message, StatusCodes.Status401Unauthorized)); }
        catch (CrmIntegrationException ex) { return MapIntegrationError(ex); }
    }

    [HttpGet("assignees"), Authorize(Policy = "CrmEdit")]
    public async Task<ActionResult<IReadOnlyList<BlueIdUserDto>>> Assignees(CancellationToken ct)
    {
        var userId = UserId();
        if (!userId.HasValue) return Unauthorized();
        try { return Ok(await crmSendService.GetAssignableUsersAsync(userId.Value, ct)); }
        catch (CrmNotConfiguredException ex) { return Unauthorized(Failure("CRM_NOT_CONFIGURED", "CRM is not configured", ex.Message, StatusCodes.Status401Unauthorized)); }
        catch (CrmIntegrationException ex) { return MapIntegrationError(ex); }
    }

    // แก้ Ticket ในงานของผู้ใช้เอง (Assignto = CRM Username — ตรวจซ้ำผ่าน detail.GetAsync) ไม่ต้องผูก Defect:
    // เพิ่มประวัติการติดต่อ (Message), เปลี่ยนสถานะ, ส่งกลับเจ้าของเรื่อง หรือเลือกผู้รับผิดชอบจาก directory ที่อนุญาต
    [HttpPatch("tickets/{jobNo}"), Authorize(Policy = "CrmEdit"), RequireProjectAccess]
    public async Task<ActionResult<CrmTicketMutationDto>> UpdateTicket(string jobNo, UpdateCrmTicketRequest request, CancellationToken ct)
    {
        var userId = UserId();
        if (!userId.HasValue) return Unauthorized();
        var message = request.Message?.Trim();
        if (string.IsNullOrWhiteSpace(jobNo) || (string.IsNullOrWhiteSpace(request.Status) && string.IsNullOrWhiteSpace(request.AssignToStaffCode) && !request.AssignToOwner && string.IsNullOrEmpty(message)))
            return BadRequest(Failure("CRM_INVALID_UPDATE", "ข้อมูลอัปเดตไม่ครบ", "กรุณากรอกข้อความ เลือกสถานะ หรือเลือกส่งกลับเจ้าของเรื่อง", StatusCodes.Status400BadRequest));
        if (message is { Length: > CrmReplyMaxLength })
            return BadRequest(Failure("CRM_INVALID_UPDATE", "ข้อความยาวเกินไป", $"ข้อความต้องไม่เกิน {CrmReplyMaxLength} ตัวอักษร", StatusCodes.Status400BadRequest));
        if (!string.IsNullOrWhiteSpace(request.Status) && !EditableStatuses.Contains(request.Status.Trim(), StringComparer.OrdinalIgnoreCase))
            return BadRequest(Failure("CRM_INVALID_STATUS", "สถานะไม่รองรับ", $"เลือกได้เฉพาะ {string.Join(", ", EditableStatuses)}", StatusCodes.Status400BadRequest));
        if (request.AssignToOwner && !string.IsNullOrWhiteSpace(request.AssignToStaffCode))
            return BadRequest(Failure("CRM_INVALID_UPDATE", "เลือกผู้รับผิดชอบซ้ำซ้อน", "เลือกได้อย่างใดอย่างหนึ่ง: ส่งกลับเจ้าของเรื่อง หรือระบุผู้รับผิดชอบ", StatusCodes.Status400BadRequest));

        CrmTicketDetailResult current;
        try
        {
            current = await detail.GetAsync(userId.Value, jobNo, ct);
        }
        catch (CrmTicketNotFoundException ex) { return NotFound(Failure("CRM_TICKET_NOT_FOUND", "ไม่พบ Ticket", ex.Message, StatusCodes.Status404NotFound)); }
        catch (CrmNotConfiguredException ex) { return Unauthorized(Failure("CRM_NOT_CONFIGURED", "ยังไม่ได้ตั้งค่าบัญชี CRM", ex.Message, StatusCodes.Status401Unauthorized)); }
        catch (CrmTimeoutException ex) { return StatusCode(StatusCodes.Status408RequestTimeout, Failure("CRM_TIMEOUT", "CRM ตอบกลับไม่ทันเวลา", ex.Message, StatusCodes.Status408RequestTimeout)); }
        catch (CrmBadResponseException ex) { return StatusCode(StatusCodes.Status502BadGateway, Failure("CRM_BAD_RESPONSE", "ข้อมูลจาก CRM ไม่ถูกต้อง", ex.Message, StatusCodes.Status502BadGateway)); }
        catch (CrmIntegrationException ex) { return MapIntegrationError(ex); }
        catch (ArgumentException ex) { return BadRequest(Failure("CRM_INVALID_QUERY", "Invalid CRM query", ex.Message, StatusCodes.Status400BadRequest)); }

        if (!MatchesExpected(request.ExpectedStatus, current.Ticket.Status) || !MatchesExpected(request.ExpectedAssignee, current.Ticket.Assignee))
            return Conflict(Failure("CRM_CONFLICT", "Ticket ถูกแก้ไขจากที่อื่น", "กรุณาโหลด Ticket ใหม่ก่อนบันทึก", StatusCodes.Status409Conflict));

        if (!string.IsNullOrWhiteSpace(request.AssignToStaffCode))
        {
            IReadOnlyList<BlueIdUserDto> assignees;
            try { assignees = await crmSendService.GetAssignableUsersAsync(userId.Value, ct); }
            catch (CrmNotConfiguredException ex) { return Unauthorized(Failure("CRM_NOT_CONFIGURED", "ยังไม่ได้ตั้งค่าบัญชี CRM", ex.Message, StatusCodes.Status401Unauthorized)); }
            catch (CrmIntegrationException ex) { return MapIntegrationError(ex); }
            if (!assignees.Any(x => string.Equals(x.StaffCode, request.AssignToStaffCode.Trim(), StringComparison.OrdinalIgnoreCase)))
                return BadRequest(Failure("CRM_INVALID_ASSIGNEE", "ผู้รับผิดชอบไม่ถูกต้อง", "ผู้รับผิดชอบที่เลือกไม่อยู่ในรายชื่อที่อนุญาต", StatusCodes.Status400BadRequest));
        }

        CrmTicketMutationResult result;
        try
        {
            result = await crmSendService.UpdateTicketAsync(userId.Value, jobNo.Trim(), message, request.Status, request.AssignToStaffCode, request.AssignToOwner, UserDisplayName(), ct);
        }
        catch (CrmNotConfiguredException ex) { return Unauthorized(Failure("CRM_NOT_CONFIGURED", "ยังไม่ได้ตั้งค่าบัญชี CRM", ex.Message, StatusCodes.Status401Unauthorized)); }
        catch (CrmTimeoutException ex) { return StatusCode(StatusCodes.Status408RequestTimeout, Failure("CRM_TIMEOUT", "CRM ตอบกลับไม่ทันเวลา", ex.Message, StatusCodes.Status408RequestTimeout)); }
        catch (CrmBadResponseException ex) { return StatusCode(StatusCodes.Status502BadGateway, Failure("CRM_BAD_RESPONSE", "ข้อมูลจาก CRM ไม่ถูกต้อง", ex.Message, StatusCodes.Status502BadGateway)); }
        // ข้อผิดพลาดที่เกิดก่อนเรียก CRM (เช่น Ticket ไม่มีเจ้าของเรื่อง, ไม่มีอะไรเปลี่ยน) เป็นข้อความสำหรับผู้ใช้ — ส่งต่อแบบ 400
        catch (CrmIntegrationException ex) when (ex.RemoteStatusCode is null)
        {
            return BadRequest(Failure("CRM_INVALID_UPDATE", "อัปเดต CRM ไม่ได้", ex.Message, StatusCodes.Status400BadRequest));
        }
        catch (CrmIntegrationException ex) { return MapIntegrationError(ex); }

        // ถ้า Ticket ผูกกับ Defect ที่ผู้ใช้เข้าถึงได้ อัปเดต snapshot + บันทึก activity ใน Defect ด้วย (ไม่บังคับ)
        var linkedQuery = db.Defects.Where(x => !x.IsDeleted && x.CrmTicketId == jobNo.Trim());
        if (projectContext.AllowedProjectIds.Length > 0)
            linkedQuery = linkedQuery.Where(x => projectContext.AllowedProjectIds.Contains(x.ProjectId));
        var defect = await linkedQuery.FirstOrDefaultAsync(ct);
        if (defect is not null)
        {
            defect.UpdateCrmSnapshot(result.Status, result.Assignee, DateTime.UtcNow);
            await db.SaveChangesAsync(ct);
            var replyNote = string.IsNullOrEmpty(message) ? "" : "; เพิ่มประวัติการติดต่อ";
            await activityService.LogAsync(defect.DefectId, "CrmTicketUpdated", $"CRM Ticket #{jobNo.Trim()} updated: Status={result.Status}; Assignee={result.Assignee}{replyNote}", userId, ct);
        }
        return Ok(new CrmTicketMutationDto(jobNo.Trim(), result.Status, result.Assignee, defect?.CrmLastSyncedAt));
    }

    [HttpGet("tickets/{jobNo}/defect"), RequireProjectAccess]
    public async Task<ActionResult<CrmLinkedDefectDto>> LinkedDefect(string jobNo, CancellationToken ct)
    {
        var userId = UserId();
        if (!userId.HasValue) return Unauthorized();

        try
        {
            await detail.GetAsync(userId.Value, jobNo, ct);
        }
        catch (CrmTicketNotFoundException ex)
        {
            return NotFound(Failure("CRM_TICKET_NOT_FOUND", "ไม่พบ Ticket", ex.Message, StatusCodes.Status404NotFound));
        }
        catch (CrmNotConfiguredException ex)
        {
            return Unauthorized(Failure("CRM_NOT_CONFIGURED", "ยังไม่ได้เชื่อมต่อ CRM", ex.Message, StatusCodes.Status401Unauthorized));
        }
        catch (CrmIntegrationException ex)
        {
            return MapIntegrationError(ex);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Failure("CRM_INVALID_QUERY", "ข้อมูลค้นหาไม่ถูกต้อง", ex.Message, StatusCodes.Status400BadRequest));
        }

        var linkedQuery = db.Defects.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CrmTicketId == jobNo.Trim());
        if (projectContext.AllowedProjectIds.Length > 0)
            linkedQuery = linkedQuery.Where(x => projectContext.AllowedProjectIds.Contains(x.ProjectId));

        var linked = await linkedQuery
            .Select(x => new CrmLinkedDefectDto(x.DefectId, x.DefectCode, x.Title, x.Severity, x.Status, x.ProjectId, x.CrmTicketId!, x.CrmSyncStatus))
            .FirstOrDefaultAsync(ct);

        return linked is null ? NotFound() : Ok(linked);
    }

    [HttpPost("tickets/{jobNo}/link-defect"), Authorize(Policy = "DefectEdit"), RequireProjectAccess]
    public async Task<ActionResult<CrmLinkedDefectDto>> LinkDefect(string jobNo, LinkCrmDefectRequest request, CancellationToken ct)
    {
        var userId = UserId();
        if (!userId.HasValue) return Unauthorized();
        if (request.DefectId == Guid.Empty || string.IsNullOrWhiteSpace(jobNo))
            return BadRequest(Failure("CRM_INVALID_QUERY", "ข้อมูลเชื่อมโยงไม่ถูกต้อง", "ต้องระบุ Job No. และ Defect", StatusCodes.Status400BadRequest));

        CrmTicketDetailResult ticket;
        try
        {
            ticket = await detail.GetAsync(userId.Value, jobNo, ct);
        }
        catch (CrmTicketNotFoundException ex)
        {
            return NotFound(Failure("CRM_TICKET_NOT_FOUND", "ไม่พบ Ticket", ex.Message, StatusCodes.Status404NotFound));
        }
        catch (CrmNotConfiguredException ex)
        {
            return Unauthorized(Failure("CRM_NOT_CONFIGURED", "ยังไม่ได้เชื่อมต่อ CRM", ex.Message, StatusCodes.Status401Unauthorized));
        }
        catch (CrmIntegrationException ex)
        {
            return MapIntegrationError(ex);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Failure("CRM_INVALID_QUERY", "ข้อมูลค้นหาไม่ถูกต้อง", ex.Message, StatusCodes.Status400BadRequest));
        }

        var defect = await db.Defects.FirstOrDefaultAsync(x => x.DefectId == request.DefectId && !x.IsDeleted, ct);
        if (defect is null || !CanAccess(defect.ProjectId)) return NotFound();

        var duplicate = await db.Defects.AsNoTracking()
            .AnyAsync(x => !x.IsDeleted && x.DefectId != defect.DefectId && x.CrmTicketId == ticket.Ticket.JobNo, ct);
        if (duplicate) return Conflict(new { detail = $"CRM Ticket {ticket.Ticket.JobNo} เชื่อมกับ Defect อื่นแล้ว" });

        if (string.Equals(defect.CrmTicketId, ticket.Ticket.JobNo, StringComparison.OrdinalIgnoreCase) && defect.CrmSyncStatus == "Linked")
            return Ok(ToLinkedDefect(defect));
        if (!string.IsNullOrWhiteSpace(defect.CrmTicketId) && !string.Equals(defect.CrmTicketId, ticket.Ticket.JobNo, StringComparison.OrdinalIgnoreCase))
            return Conflict(new { detail = $"Defect นี้เชื่อมกับ CRM Ticket {defect.CrmTicketId} อยู่แล้ว" });

        defect.SetCrmTicket(ticket.Ticket.JobNo, DateTime.UtcNow);
        defect.UpdateCrmSnapshot(ticket.Ticket.Status, ticket.Ticket.Assignee);
        await db.SaveChangesAsync(ct);
        await activityService.LogAsync(defect.DefectId, "CrmTicketLinked", $"เชื่อม CRM Ticket {ticket.Ticket.JobNo}", userId, ct);

        return Ok(ToLinkedDefect(defect));
    }

    [HttpPost("tickets/{jobNo}/create-defect"), Authorize(Policy = "DefectEdit"), RequireProjectAccess]
    public async Task<ActionResult<CrmCreatedDefectDto>> CreateDefect(string jobNo, CreateCrmDefectRequest request, CancellationToken ct)
    {
        var userId = UserId();
        if (!userId.HasValue) return Unauthorized();
        if (string.IsNullOrWhiteSpace(jobNo) || request.ProjectId == Guid.Empty)
            return BadRequest(Failure("CRM_INVALID_QUERY", "ข้อมูลสร้าง Defect ไม่ถูกต้อง", "ต้องระบุ Job No. และ Project", StatusCodes.Status400BadRequest));
        if (!CanAccess(request.ProjectId)) return NotFound();

        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == request.ProjectId && x.IsActive, ct);
        if (project is null) return BadRequest(Failure("CRM_INVALID_PROJECT", "Project ไม่พร้อมใช้งาน", "ต้องเลือก Project ที่ยังเปิดใช้งานและผู้ใช้เข้าถึงได้", StatusCodes.Status400BadRequest));

        CrmTicketDetailResult ticket;
        try
        {
            ticket = await detail.GetAsync(userId.Value, jobNo, ct);
        }
        catch (CrmTicketNotFoundException ex) { return NotFound(Failure("CRM_TICKET_NOT_FOUND", "ไม่พบ Ticket", ex.Message, StatusCodes.Status404NotFound)); }
        catch (CrmNotConfiguredException ex) { return Unauthorized(Failure("CRM_NOT_CONFIGURED", "CRM ยังไม่ได้ตั้งค่า", ex.Message, StatusCodes.Status401Unauthorized)); }
        catch (CrmTimeoutException ex) { return StatusCode(StatusCodes.Status408RequestTimeout, Failure("CRM_TIMEOUT", "CRM ตอบกลับไม่ทันเวลา", ex.Message, StatusCodes.Status408RequestTimeout)); }
        catch (CrmBadResponseException ex) { return StatusCode(StatusCodes.Status502BadGateway, Failure("CRM_BAD_RESPONSE", "ข้อมูลจาก CRM ไม่ถูกต้อง", ex.Message, StatusCodes.Status502BadGateway)); }
        catch (CrmIntegrationException ex) { return MapIntegrationError(ex); }
        catch (ArgumentException ex) { return BadRequest(Failure("CRM_INVALID_QUERY", "ข้อมูลค้นหาไม่ถูกต้อง", ex.Message, StatusCodes.Status400BadRequest)); }

        var existing = await db.Defects.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CrmTicketId == ticket.Ticket.JobNo)
            .Select(x => new { x.DefectId, x.DefectCode, x.ProjectId, x.Title, x.Severity, x.Status, x.CrmTicketId, x.CrmSyncStatus })
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return Conflict(new { code = "CRM_TICKET_ALREADY_LINKED", detail = $"CRM Ticket {ticket.Ticket.JobNo} เชื่อมกับ Defect {existing.DefectCode} อยู่แล้ว" });

        var title = string.IsNullOrWhiteSpace(request.Title) ? ticket.Ticket.Subject : request.Title.Trim();
        if (string.IsNullOrWhiteSpace(title)) title = $"CRM Ticket {ticket.Ticket.JobNo}";
        if (title.Length > 300) return BadRequest(Failure("CRM_INVALID_DEFECT", "ข้อมูล Defect ไม่ถูกต้อง", "Title ต้องไม่เกิน 300 ตัวอักษร", StatusCodes.Status400BadRequest));
        var severity = string.IsNullOrWhiteSpace(request.Severity) ? "Medium" : request.Severity.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? ticket.Description : request.Description.Trim();
        var prefix = $"{project.ProjectCode}-DEF";
        var existingCodes = await db.Defects.Where(x => x.ProjectId == project.ProjectId && x.DefectCode.StartsWith(prefix)).Select(x => x.DefectCode).ToListAsync(ct);
        var code = BusinessCodeGenerator.NextAvailable(prefix, existingCodes);
        var defect = new Defect(project.ProjectId, null, null, null, code, title, severity, "Open", userId, description, null, null, null, null);
        var now = DateTime.UtcNow;
        defect.SetCrmTicket(ticket.Ticket.JobNo, now);
        defect.UpdateCrmSnapshot(ticket.Ticket.Status, ticket.Ticket.Assignee, now);
        await db.Defects.AddAsync(defect, ct);
        await db.SaveChangesAsync(ct);
        await activityService.LogAsync(defect.DefectId, "CreatedFromCrm", $"สร้าง Defect จาก CRM Ticket {ticket.Ticket.JobNo}", userId, ct);

        return Ok(new CrmCreatedDefectDto(defect.DefectId, defect.DefectCode, defect.ProjectId, defect.Title, defect.Severity, defect.Status, defect.CrmTicketId!, defect.CrmSyncStatus));
    }

    private ActionResult MapIntegrationError(CrmIntegrationException exception)
    {
        if (exception.RemoteStatusCode == HttpStatusCode.GatewayTimeout)
            return StatusCode(StatusCodes.Status408RequestTimeout, Failure("CRM_TIMEOUT", "CRM ตอบกลับไม่ทันเวลา", exception.Message, StatusCodes.Status408RequestTimeout));
        // BlueID/เครือข่ายล่ม: ข้อความจาก CrmTokenService บอกสาเหตุและเวลาที่จะลองใหม่ (ไม่มี credential/ข้อมูลภายใน) จึงส่งต่อได้
        if (exception.IsServiceUnavailable)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Failure("CRM_UNAVAILABLE", "CRM ไม่พร้อมใช้งาน", exception.Message, StatusCodes.Status503ServiceUnavailable));
        if (exception.RemoteStatusCode is { } remoteStatus && (int)remoteStatus >= 500)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Failure("CRM_UNAVAILABLE", "CRM ไม่พร้อมใช้งาน", "กรุณาลองใหม่อีกครั้งภายหลัง", StatusCodes.Status503ServiceUnavailable));

        return exception.RemoteStatusCode switch
        {
            HttpStatusCode.Unauthorized => Unauthorized(Failure("CRM_UNAUTHORIZED", "CRM ไม่อนุญาตการเชื่อมต่อ", "กรุณาตรวจสอบบัญชี CRM ของคุณแล้วลองใหม่", StatusCodes.Status401Unauthorized)),
            HttpStatusCode.TooManyRequests => StatusCode(StatusCodes.Status429TooManyRequests, Failure("CRM_RATE_LIMITED", "CRM จำกัดจำนวนคำขอชั่วคราว", "กรุณารอสักครู่แล้วลองใหม่", StatusCodes.Status429TooManyRequests)),
            _ => StatusCode(StatusCodes.Status502BadGateway, Failure("CRM_BAD_RESPONSE", "เรียก CRM ไม่สำเร็จ", "กรุณาลองใหม่อีกครั้ง หรือแจ้งผู้ดูแลระบบพร้อม Trace ID", StatusCodes.Status502BadGateway))
        };
    }

    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;

    private string UserDisplayName() => User.FindFirstValue("display_name") ?? User.FindFirstValue("displayName") ?? User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "QA Hub User";

    private static bool MatchesExpected(string? expected, string? actual) => string.IsNullOrWhiteSpace(expected) || string.Equals(expected.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool CanAccess(Guid projectId) => projectContext.AllowedProjectIds.Length == 0 || projectContext.AllowedProjectIds.Contains(projectId);

    private static CrmLinkedDefectDto ToLinkedDefect(ProMaxx2.QA.Domain.Defects.Defect defect) =>
        new(defect.DefectId, defect.DefectCode, defect.Title, defect.Severity, defect.Status, defect.ProjectId, defect.CrmTicketId!, defect.CrmSyncStatus);

    private static ProblemDetails Failure(string code, string title, string detail, int status)
    {
        var problem = new ProblemDetails { Title = title, Detail = detail, Status = status };
        problem.Extensions["code"] = code;
        return problem;
    }
}

public sealed record LinkCrmDefectRequest(Guid DefectId);
public sealed record CrmLinkedDefectDto(Guid DefectId, string DefectCode, string Title, string Severity, string Status, Guid ProjectId, string CrmTicketId, string CrmSyncStatus);
public sealed record CreateCrmDefectRequest(Guid ProjectId, string? Title, string? Severity, string? Description);
public sealed record CrmCreatedDefectDto(Guid DefectId, string DefectCode, Guid ProjectId, string Title, string Severity, string Status, string CrmTicketId, string CrmSyncStatus);
public sealed record UpdateCrmTicketRequest(string? Status, string? AssignToStaffCode, string? ExpectedStatus, string? ExpectedAssignee, string? Message = null, bool AssignToOwner = false);
public sealed record CrmTicketMutationDto(string JobNo, string Status, string Assignee, DateTime? LastSyncedAt);
