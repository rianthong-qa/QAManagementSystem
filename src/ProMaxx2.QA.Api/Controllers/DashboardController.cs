using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using ProMaxx2.QA.Application.Dashboard;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[RequireProjectAccess]
public sealed class DashboardController(DashboardService dashboard, IDataProtectionProvider dataProtection) : ControllerBase
{
    private readonly IDataProtector _shareProtector = dataProtection.CreateProtector("ProMaxx2.QA.DashboardShare.v1");

    [HttpGet("summary")]
    [Authorize(Policy = "ProjectView")]
    [ProducesResponseType<DashboardSummary>(StatusCodes.Status200OK)]
    public Task<DashboardSummary> GetSummary([FromQuery]Guid?projectId,[FromQuery]Guid?releaseId,[FromQuery]Guid?buildId,CancellationToken ct) => dashboard.GetAsync(projectId,releaseId,buildId,ct);

    [HttpPost("share")]
    [Authorize(Policy = "ProjectView")]
    public async Task<ActionResult<object>> CreateShareLink([FromBody] DashboardShareRequest request,CancellationToken ct)
    {
        // allow share links up to 30 days (720 hours)
        var expiresAt = DateTimeOffset.UtcNow.AddHours(Math.Clamp(request.ValidHours, 1, 720));
        var share=await dashboard.CreateShareAsync(request.ProjectId,request.ReleaseId,request.BuildId,expiresAt.UtcDateTime,ct);
        return Ok(new { code = share.Code, expiresAt });
    }

    [HttpGet("shared/{code}")]
    [AllowAnonymous]
    public async Task<ActionResult<DashboardSummary>> GetShortShared(string code,CancellationToken ct)
    {
        var share=await dashboard.FindShareAsync(code,ct);
        if(share is null)return Unauthorized(new ProblemDetails{Title="Dashboard share link is invalid or expired."});
        return Ok(await dashboard.GetAsync(share.ProjectId,share.ReleaseId,share.BuildId,ct));
    }

    [HttpGet("shared/{code}/timeline")]
    [AllowAnonymous]
    public async Task<ActionResult<DashboardTimeline>> GetShortSharedTimeline(string code,CancellationToken ct)
    {
        var share=await dashboard.FindShareAsync(code,ct);
        if(share is null)return Unauthorized(new ProblemDetails{Title="Dashboard share link is invalid or expired."});
        return Ok(await dashboard.GetTimelineAsync(share.ProjectId,share.ReleaseId,share.BuildId,ct));
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
            return Ok(await dashboard.GetAsync(Parse(parts[0]), Parse(parts[1]), Parse(parts[2]), ct));
        }
        catch
        {
            return Unauthorized(new ProblemDetails { Title = "Dashboard share link is invalid." });
        }
    }
}

public sealed record DashboardShareRequest(Guid? ProjectId, Guid? ReleaseId, Guid? BuildId, int ValidHours = 720);
