using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using ProMaxx2.QA.Application.Dashboard;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[RequireProjectAccess]
public sealed class DashboardController(DashboardService dashboard, IDataProtectionProvider dataProtection, ProMaxx2.QA.Application.Common.ProjectAccessContext projectCtx) : ControllerBase
{
    private readonly IDataProtector _shareProtector = dataProtection.CreateProtector("ProMaxx2.QA.DashboardShare.v1");

    [HttpGet("summary")]
    [Authorize(Policy = "ProjectView")]
    [ProducesResponseType<DashboardSummary>(StatusCodes.Status200OK)]
    public async Task<DashboardSummary> GetSummary([FromQuery]Guid?projectId,[FromQuery]Guid?releaseId,[FromQuery]Guid?buildId,[FromQuery]bool includeInsights,CancellationToken ct)
    {
        // includeInsights: รวมข้อมูล Defect ของมุมมองผู้บริหาร/Dev มาในคำขอเดียว — ฝั่ง server เพิ่ม ~10 ms แต่ประหยัด round trip
        // ผ่าน Cloudflare (~0.5–1.5 วินาที/คำขอ + CORS preflight ของ URL ใหม่)
        var summary=await dashboard.GetAsync(projectId,releaseId,buildId,ct);
        return includeInsights?summary with{Insights=await dashboard.GetDefectInsightsAsync(projectId,releaseId,buildId,null,true,ct)}:summary;
    }

    /// <summary>ข้อมูล Defect ของมุมมองผู้บริหาร/Dev — mine=true = เฉพาะ Defect ที่ผู้ใช้เป็นผู้รับผิดชอบ</summary>
    [HttpGet("defect-insights")]
    [Authorize(Policy = "ProjectView")]
    public Task<DashboardDefectInsights> GetDefectInsights([FromQuery]Guid?projectId,[FromQuery]Guid?releaseId,[FromQuery]Guid?buildId,[FromQuery]bool mine,CancellationToken ct)
    {
        // ไม่มี user id ใน token (ไม่ควรเกิด) + mine = ไม่คืนของใคร แทนที่จะคืนทั้งทีม
        Guid? assignee = mine ? (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : Guid.Empty) : null;
        return dashboard.GetDefectInsightsAsync(projectId,releaseId,buildId,assignee,true,ct);
    }

    [HttpPost("share")]
    [Authorize(Policy = "ProjectView")]
    public async Task<ActionResult<object>> CreateShareLink([FromBody] DashboardShareRequest request,CancellationToken ct)
    {
        // ลิงก์แชร์เปิดอ่านได้แบบ anonymous — ต้องผูกกับ Project ที่ผู้สร้างมีสิทธิ์เท่านั้น ถ้าปล่อย ProjectId ว่าง
        // endpoint shared/* จะไม่มี project filter และคืนข้อมูลของทุก Project ในระบบให้คนนอก
        if (request.ProjectId is not { } projectId || projectId == Guid.Empty)
            return BadRequest(new ProblemDetails { Title = "Project is required.", Detail = "กรุณาเลือก Project ก่อนสร้างลิงก์แชร์ Dashboard" });
        if (!projectCtx.AllowedProjectIds.Contains(projectId))
            return Forbid();
        // allow share links up to 90 days (2160 hours)
        var expiresAt = DateTimeOffset.UtcNow.AddHours(Math.Clamp(request.ValidHours, 1, 2160));
        var share=await dashboard.CreateShareAsync(request.ProjectId,request.ReleaseId,request.BuildId,expiresAt.UtcDateTime,ct);
        return Ok(new { code = share.Code, expiresAt });
    }

    /// <summary>ยกเลิกลิงก์แชร์ก่อนหมดอายุ — ทำได้เฉพาะลิงก์ของ Project ที่ผู้ใช้เป็นสมาชิก</summary>
    [HttpDelete("share/{code}")]
    [Authorize(Policy = "ProjectView")]
    public async Task<IActionResult> RevokeShareLink(string code, CancellationToken ct)
        => await dashboard.RevokeShareAsync(code, projectCtx.AllowedProjectIds, ct) ? NoContent() : NotFound();

    [HttpGet("shared/{code}")]
    [AllowAnonymous]
    public async Task<ActionResult<DashboardSummary>> GetShortShared(string code,CancellationToken ct,[FromQuery]bool includeInsights=false)
    {
        var share=await dashboard.FindShareAsync(code,ct);
        if(share?.ProjectId is null)return Unauthorized(new ProblemDetails{Title="Dashboard share link is invalid or expired."});
        var summary=await dashboard.GetAsync(share.ProjectId,share.ReleaseId,share.BuildId,ct);
        // ลิงก์แชร์ (anonymous) ได้เฉพาะตัวเลขรวม ไม่มีรายการ Defect
        return Ok(includeInsights?summary with{Insights=await dashboard.GetDefectInsightsAsync(share.ProjectId,share.ReleaseId,share.BuildId,null,false,ct)}:summary);
    }

    [HttpGet("shared/{code}/timeline")]
    [AllowAnonymous]
    public async Task<ActionResult<DashboardTimeline>> GetShortSharedTimeline(string code,CancellationToken ct)
    {
        var share=await dashboard.FindShareAsync(code,ct);
        if(share?.ProjectId is null)return Unauthorized(new ProblemDetails{Title="Dashboard share link is invalid or expired."});
        return Ok(await dashboard.GetTimelineAsync(share.ProjectId,share.ReleaseId,share.BuildId,ct));
    }

    /// <summary>ลิงก์แชร์ (anonymous) ได้เฉพาะตัวเลขรวม — ไม่ส่งรายการ Defect (ชื่อเรื่อง) ให้คนที่ถือลิงก์</summary>
    [HttpGet("shared/{code}/defect-insights")]
    [AllowAnonymous]
    public async Task<ActionResult<DashboardDefectInsights>> GetShortSharedDefectInsights(string code,CancellationToken ct)
    {
        var share=await dashboard.FindShareAsync(code,ct);
        if(share?.ProjectId is null)return Unauthorized(new ProblemDetails{Title="Dashboard share link is invalid or expired."});
        return Ok(await dashboard.GetDefectInsightsAsync(share.ProjectId,share.ReleaseId,share.BuildId,null,false,ct));
    }

    [HttpGet("shared/timeline")]
    [AllowAnonymous]
    public async Task<ActionResult<DashboardTimeline>> GetSharedTimeline([FromQuery] string token, CancellationToken ct)
    {
        try
        {
            var parts = _shareProtector.Unprotect(token).Split('|');
            if (parts.Length != 4 || !long.TryParse(parts[3], out var unix) || DateTimeOffset.FromUnixTimeSeconds(unix) <= DateTimeOffset.UtcNow)
                return Unauthorized(new ProblemDetails { Title = "Dashboard share link has expired." });
            Guid? Parse(string value) => Guid.TryParse(value, out var id) ? id : null;
            // token แบบเดิมที่ไม่มี Project จะเปิดข้อมูลทุก Project — ปฏิเสธเหมือน short code
            if (Parse(parts[0]) is null) return Unauthorized(new ProblemDetails { Title = "Dashboard share link is invalid." });
            return Ok(await dashboard.GetTimelineAsync(Parse(parts[0]), Parse(parts[1]), Parse(parts[2]), ct));
        }
        catch
        {
            return Unauthorized(new ProblemDetails { Title = "Dashboard share link is invalid." });
        }
    }

    [HttpGet("shared")]
    [AllowAnonymous]
    public async Task<ActionResult<DashboardSummary>> GetShared([FromQuery] string token, CancellationToken ct)
    {
        try
        {
            var parts = _shareProtector.Unprotect(token).Split('|');
            if (parts.Length != 4 || !long.TryParse(parts[3], out var unix) || DateTimeOffset.FromUnixTimeSeconds(unix) <= DateTimeOffset.UtcNow)
                return Unauthorized(new ProblemDetails { Title = "Dashboard share link has expired." });
            Guid? Parse(string value) => Guid.TryParse(value, out var id) ? id : null;
            // token แบบเดิมที่ไม่มี Project จะเปิดข้อมูลทุก Project — ปฏิเสธเหมือน short code
            if (Parse(parts[0]) is null) return Unauthorized(new ProblemDetails { Title = "Dashboard share link is invalid." });
            return Ok(await dashboard.GetAsync(Parse(parts[0]), Parse(parts[1]), Parse(parts[2]), ct));
        }
        catch
        {
            return Unauthorized(new ProblemDetails { Title = "Dashboard share link is invalid." });
        }
    }
}

public sealed record DashboardShareRequest(Guid? ProjectId, Guid? ReleaseId, Guid? BuildId, int ValidHours = 2160);
