using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.Api.Controllers;

/// <summary>AUT-DATA-001: request/track a real DB backup ("snapshot") of an Environment's physical database before a
/// test run. This controller is the human/UI-facing side (list/request); the agent-facing claim/complete endpoints
/// live on <see cref="AutomationAgentController"/> alongside job claim/complete, under the same auth model.</summary>
[ApiController, Route("api/v1/automation/data"), Authorize(Policy = "AutomationView"), RequireProjectAccess]
public sealed class AutomationDataController(AutomationDataSnapshotService service) : ControllerBase
{
    private Guid? UserId() => Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value, out var id) ? id : null;
    private static ProblemDetails Problem(string title, string detail, int status) => new() { Title = title, Detail = detail, Status = status };

    [HttpGet("snapshots")]
    public Task<IReadOnlyList<AutomationDbSnapshotDto>> ListSnapshots([FromQuery] Guid projectId, [FromQuery] Guid? environmentId, [FromQuery] Guid? buildId, [FromQuery] int take = 100, CancellationToken ct = default)
        => service.ListAsync(projectId, environmentId, buildId, take, ct);

    [HttpGet("snapshots/{id:guid}")]
    public async Task<ActionResult<AutomationDbSnapshotDto>> GetSnapshot(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await service.GetAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("snapshots"), Authorize(Policy = "AutomationExecute")]
    public async Task<ActionResult<AutomationDbSnapshotDto>> RequestSnapshot([FromQuery] Guid projectId, RequestSnapshotRequest request, CancellationToken ct)
    {
        try
        {
            var result = await service.RequestAsync(projectId, request, UserId(), ct);
            return CreatedAtAction(nameof(GetSnapshot), new { id = result.AutomationDbSnapshotId, projectId }, result);
        }
        catch (EntityNotFoundException ex) { return NotFound(Problem("ไม่พบ Environment หรือ Build", ex.Message, 404)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }
}
