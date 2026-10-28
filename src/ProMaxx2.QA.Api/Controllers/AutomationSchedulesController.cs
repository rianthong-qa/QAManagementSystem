using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.Api.Controllers;

/// <summary>AUT-P1-005: persistent Automation Schedule — create/edit/activate/deactivate a recurring
/// (Once/Daily/Weekly, timezone-aware) timetable that re-runs an Automation Suite against a fixed Build/Environment.
/// Actually firing a run when <c>nextRunAtUtc</c> arrives is AUT-P1-006 (Schedule Execution Worker), not this controller.</summary>
[ApiController, Route("api/v1/automation/schedules"), Authorize(Policy = "AutomationView"), RequireProjectAccess]
public sealed class AutomationSchedulesController(AutomationScheduleService service) : ControllerBase
{
    // worker-status ถูกเรียกแบบไม่มี JWT จาก tools/ProMaxx2.ServiceManager บนเครื่องเดียวกัน (127.0.0.1) จึงยังเป็น
    // AllowAnonymous แต่จำกัดเฉพาะ request ที่มาจาก loopback จริงและไม่ผ่าน proxy — traffic จาก Cloudflare Tunnel
    // ก็มาจาก loopback เหมือนกัน แต่จะมี header CF-Connecting-IP/X-Forwarded-For ติดมาเสมอ จึงถูกปฏิเสธ
    // ผู้ใช้ที่ login แล้วและเป็น SYS_ADMIN หรือมีสิทธิ์ AUTOMATION.MANAGE ยังเรียกผ่าน public domain ได้
    [HttpGet("worker-status"), AllowAnonymous]
    public IActionResult WorkerStatus()
        => CanControlWorker() ? Ok(new { enabled = AutomationScheduleWorker.IsEnabled, running = true }) : StatusCode(StatusCodes.Status403Forbidden);

    [HttpPost("worker-status"), AllowAnonymous]
    public IActionResult SetWorkerStatus([FromQuery] bool enabled)
    {
        if (!CanControlWorker()) return StatusCode(StatusCodes.Status403Forbidden);
        AutomationScheduleWorker.SetEnabled(enabled);
        return Ok(new { enabled, running = true });
    }

    private bool CanControlWorker()
    {
        if (User.Identity?.IsAuthenticated == true && (User.IsInRole("SYS_ADMIN") || User.HasClaim("permission", "AUTOMATION.MANAGE"))) return true;
        var remote = HttpContext.Connection.RemoteIpAddress;
        var proxied = Request.Headers.ContainsKey("CF-Connecting-IP") || Request.Headers.ContainsKey("X-Forwarded-For") || Request.Headers.ContainsKey("Forwarded");
        return remote is not null && System.Net.IPAddress.IsLoopback(remote) && !proxied;
    }

    private Guid? UserId() => Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value, out var id) ? id : null;
    private static ProblemDetails Problem(string title, string detail, int status) => new() { Title = title, Detail = detail, Status = status };

    [HttpGet]
    public Task<IReadOnlyList<AutomationScheduleListDto>> List([FromQuery] Guid projectId, [FromQuery] bool? isActive, CancellationToken ct)
        => service.ListAsync(projectId, isActive, ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AutomationScheduleDto>> Get(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await service.GetAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    /// <summary>AUT-P1-006: audit history of every time <c>AutomationScheduleWorker</c> fired this schedule.</summary>
    [HttpGet("{id:guid}/runs")]
    public async Task<ActionResult<IReadOnlyList<AutomationScheduleRunDto>>> Runs(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await service.ListRunsAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost, Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationScheduleDto>> Create([FromQuery] Guid projectId, CreateAutomationScheduleRequest request, CancellationToken ct)
    {
        try
        {
            var result = await service.CreateAsync(projectId, request, UserId(), ct);
            return CreatedAtAction(nameof(Get), new { id = result.AutomationScheduleId, projectId }, result);
        }
        catch (EntityNotFoundException) { return NotFound(Problem("ไม่พบ Automation Suite", "Suite ที่เลือกไม่มีอยู่จริง", 404)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationScheduleDto>> Update(Guid id, [FromQuery] Guid projectId, UpdateAutomationScheduleRequest request, CancellationToken ct)
    {
        try { return Ok(await service.UpdateAsync(id, projectId, request, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }

    [HttpPost("{id:guid}/activate"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationScheduleDto>> Activate(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await service.ActivateAsync(id, projectId, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("เปิดใช้งาน Schedule ไม่สำเร็จ", ex.Message, 409)); }
    }

    [HttpPost("{id:guid}/deactivate"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationScheduleDto>> Deactivate(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await service.DeactivateAsync(id, projectId, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("ปิดใช้งาน Schedule ไม่สำเร็จ", ex.Message, 409)); }
    }

    /// <summary>AUT-P1-009: Started/Completed/Failed/NoAgent notifications for executions that a schedule fired,
    /// newest first — each row carries the ExecutionId to link to (see <c>AutomationScheduleNotificationDto</c>).</summary>
    [HttpGet("notifications")]
    public Task<IReadOnlyList<AutomationScheduleNotificationDto>> ListNotifications([FromQuery] Guid projectId, [FromQuery] bool? unreadOnly, [FromQuery] int take = 50, CancellationToken ct = default)
        => service.ListNotificationsAsync(projectId, unreadOnly, take, ct);

    [HttpGet("notifications/unread-count")]
    public Task<int> UnreadNotificationCount([FromQuery] Guid projectId, CancellationToken ct)
        => service.CountUnreadNotificationsAsync(projectId, ct);

    [HttpPost("notifications/{id:guid}/read")]
    public async Task<IActionResult> MarkNotificationRead(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { await service.MarkNotificationReadAsync(id, projectId, ct); return NoContent(); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("notifications/mark-all-read")]
    public async Task<IActionResult> MarkAllNotificationsRead([FromQuery] Guid projectId, CancellationToken ct)
    {
        await service.MarkAllNotificationsReadAsync(projectId, ct);
        return NoContent();
    }
}
