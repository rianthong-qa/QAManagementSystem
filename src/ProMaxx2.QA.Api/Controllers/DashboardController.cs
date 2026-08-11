using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using ProMaxx2.QA.Application.Dashboard;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public sealed class DashboardController(DashboardService dashboard, IDataProtectionProvider dataProtection) : ControllerBase
{
    private readonly IDataProtector _shareProtector = dataProtection.CreateProtector("ProMaxx2.QA.DashboardShare.v1");

    [HttpGet("summary")]
    [Authorize(Policy = "ProjectView")]
    [ProducesResponseType<DashboardSummary>(StatusCodes.Status200OK)]
    public Task<DashboardSummary> GetSummary([FromQuery]Guid?projectId,[FromQuery]Guid?releaseId,[FromQuery]Guid?buildId,CancellationToken ct) => dashboard.GetAsync(projectId,releaseId,buildId,ct);

    [HttpPost("share")]
    [Authorize(Policy = "ProjectView")]
    public ActionResult<object> CreateShareLink([FromBody] DashboardShareRequest request)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(Math.Clamp(request.ValidHours, 1, 168));
        var payload = string.Join('|', request.ProjectId, request.ReleaseId, request.BuildId, expiresAt.ToUnixTimeSeconds());
        return Ok(new { token = _shareProtector.Protect(payload), expiresAt });
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

public sealed record DashboardShareRequest(Guid? ProjectId, Guid? ReleaseId, Guid? BuildId, int ValidHours = 24);
