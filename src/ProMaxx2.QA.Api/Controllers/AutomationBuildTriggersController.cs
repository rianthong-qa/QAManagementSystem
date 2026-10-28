using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.Api.Controllers;

/// <summary>AUT-P1-007: Build Trigger policies — automatically run an Automation Suite when a Build event happens.
/// "Smoke" policies fire on every new Build (see <c>ReleaseService.CreateBuildAsync</c>); "Regression" policies fire
/// only when a Build is marked a Release Candidate (see <c>ReleaseService.MarkRcAsync</c>). Firing itself lives in
/// <c>AutomationBuildTriggerService.FireForBuildAsync</c>; this controller only manages the policies and their audit history.</summary>
[ApiController, Route("api/v1/automation/build-triggers"), Authorize(Policy = "AutomationView"), RequireProjectAccess]
public sealed class AutomationBuildTriggersController(AutomationBuildTriggerService service) : ControllerBase
{
    private Guid? UserId() => Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value, out var id) ? id : null;
    private static ProblemDetails Problem(string title, string detail, int status) => new() { Title = title, Detail = detail, Status = status };

    [HttpGet]
    public Task<IReadOnlyList<AutomationBuildTriggerPolicyDto>> List([FromQuery] Guid projectId, CancellationToken ct)
        => service.ListAsync(projectId, ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AutomationBuildTriggerPolicyDto>> Get(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await service.GetAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpGet("{id:guid}/runs")]
    public async Task<ActionResult<IReadOnlyList<AutomationBuildTriggerRunDto>>> Runs(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await service.ListRunsAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost, Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationBuildTriggerPolicyDto>> Create([FromQuery] Guid projectId, CreateAutomationBuildTriggerPolicyRequest request, CancellationToken ct)
    {
        try
        {
            var result = await service.CreateAsync(projectId, request, UserId(), ct);
            return CreatedAtAction(nameof(Get), new { id = result.AutomationBuildTriggerPolicyId, projectId }, result);
        }
        catch (EntityNotFoundException) { return NotFound(Problem("ไม่พบ Automation Suite", "Suite ที่เลือกไม่มีอยู่จริง", 404)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationBuildTriggerPolicyDto>> Update(Guid id, [FromQuery] Guid projectId, UpdateAutomationBuildTriggerPolicyRequest request, CancellationToken ct)
    {
        try { return Ok(await service.UpdateAsync(id, projectId, request, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(Problem("ไม่พบ Automation Suite", "Suite ที่เลือกไม่มีอยู่จริง", 404)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }

    [HttpPost("{id:guid}/activate"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationBuildTriggerPolicyDto>> Activate(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await service.ActivateAsync(id, projectId, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("เปิดใช้งาน Policy ไม่สำเร็จ", ex.Message, 409)); }
    }

    [HttpPost("{id:guid}/deactivate"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationBuildTriggerPolicyDto>> Deactivate(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await service.DeactivateAsync(id, projectId, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("ปิดใช้งาน Policy ไม่สำเร็จ", ex.Message, 409)); }
    }
}
