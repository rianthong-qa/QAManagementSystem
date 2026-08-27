using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.Api.Controllers;

/// <summary>AUT-P1-008: manage CI/CD webhook tokens and view their delivery (audit) history. Normal
/// JWT-authenticated Automation-module admin work — the public webhook endpoint itself
/// (<see cref="AutomationBuildWebhookController"/>) is a separate, unauthenticated-at-this-layer controller that
/// authenticates callers by validating the token these endpoints issue.</summary>
[ApiController, Route("api/v1/automation/webhook-tokens"), Authorize(Policy = "AutomationView"), RequireProjectAccess]
public sealed class AutomationWebhookTokensController(AutomationWebhookService service) : ControllerBase
{
    private Guid? UserId() => Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value, out var id) ? id : null;
    private static ProblemDetails Problem(string title, string detail, int status) => new() { Title = title, Detail = detail, Status = status };

    [HttpGet]
    public Task<IReadOnlyList<AutomationWebhookTokenDto>> List([FromQuery] Guid projectId, CancellationToken ct) => service.ListTokensAsync(projectId, ct);

    [HttpGet("deliveries")]
    public Task<IReadOnlyList<AutomationWebhookDeliveryDto>> Deliveries([FromQuery] Guid projectId, CancellationToken ct) => service.ListDeliveriesAsync(projectId, ct);

    /// <summary>Returns the plaintext secret exactly once — it is never retrievable again after this response.</summary>
    [HttpPost, Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<CreateAutomationWebhookTokenResult>> Create([FromQuery] Guid projectId, CreateAutomationWebhookTokenRequest request, CancellationToken ct)
    {
        try { return Ok(await service.CreateTokenAsync(projectId, request, UserId(), ct)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }

    [HttpPost("{id:guid}/revoke"), Authorize(Policy = "AutomationEdit")]
    public async Task<IActionResult> Revoke(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { await service.RevokeTokenAsync(id, projectId, UserId(), ct); return NoContent(); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("เพิกถอน Token ไม่สำเร็จ", ex.Message, 409)); }
    }
}
