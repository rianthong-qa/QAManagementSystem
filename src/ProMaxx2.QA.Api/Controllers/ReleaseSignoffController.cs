using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Governance;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1"), Authorize(Policy="ProjectView"), RequireProjectAccess]
public sealed class ReleaseSignoffController(ReleaseSignoffService service) : ControllerBase
{
    [HttpGet("releases/{releaseId:guid}/release-gate")]
    public Task<ReleaseGateDto> Gate(Guid releaseId, [FromQuery] Guid? buildId, CancellationToken ct) => service.GetGateAsync(releaseId, buildId, ct);

    [HttpGet("releases/{releaseId:guid}/signoffs")]
    public Task<IReadOnlyList<ReleaseSignoffDto>> List(Guid releaseId, CancellationToken ct) => service.ListAsync(releaseId, ct);

    [HttpPost("releases/{releaseId:guid}/signoffs"), Authorize(Policy="ReleaseSignoff")]
    public async Task<ActionResult<ReleaseSignoffDto>> Create(Guid releaseId, CreateReleaseSignoffRequest request, CancellationToken ct)
    {
        try { return Ok(await service.CreateAsync(releaseId, request, UserId(), ct)); }
        catch (InvalidOperationException ex) { return Conflict(Problem("สร้าง Sign-off ไม่ได้", ex.Message, 409)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูล Sign-off ไม่ถูกต้อง", ex.Message, 400)); }
    }

    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
    private static ProblemDetails Problem(string title, string detail, int status) => new() { Title = title, Detail = detail, Status = status };
}
