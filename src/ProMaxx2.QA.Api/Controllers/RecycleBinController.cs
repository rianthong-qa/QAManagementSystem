using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

/// <summary>รายการที่ลบแล้ว (soft delete) ของแต่ละหน้า — ดูและกู้คืนได้เฉพาะ SYS_ADMIN; type ดู RecycleBinService.Types</summary>
[ApiController, Route("api/v1/recycle-bin"), Authorize(Policy = "SysAdminOnly")]
public sealed class RecycleBinController(RecycleBinService service) : ControllerBase
{
    [HttpGet("{type}")]
    public async Task<ActionResult<IReadOnlyList<DeletedItemDto>>> List(string type, [FromQuery] Guid? projectId, CancellationToken ct)
    {
        if (!RecycleBinService.Types.Contains(type)) return NotFound();
        return Ok(await service.ListAsync(type, projectId, ct));
    }

    [HttpPost("{type}/{id:guid}/restore")]
    public async Task<IActionResult> Restore(string type, Guid id, CancellationToken ct)
    {
        if (!RecycleBinService.Types.Contains(type)) return NotFound();
        try { return await service.RestoreAsync(type, id, UserId(), ClientIp.Resolve(HttpContext), ct) ? NoContent() : NotFound(); }
        catch (RestoreConflictException ex) { return Conflict(new ProblemDetails { Title = "กู้คืนไม่ได้", Detail = ex.Message, Status = 409 }); }
    }

    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
}
