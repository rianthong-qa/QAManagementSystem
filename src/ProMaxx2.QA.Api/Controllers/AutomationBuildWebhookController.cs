using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.Api.Controllers;

/// <summary>AUT-P1-008: the public CI/CD → QA Hub build webhook. Deliberately routed outside <c>api/v1/automation/*</c>
/// and carries no <c>[Authorize]</c>/<c>RequireProjectAccess</c> — a CI system has no user JWT, so the caller is
/// authenticated manually here by hashing the <c>X-Webhook-Token</c> header and looking it up
/// (<c>AutomationWebhookService.ReceiveBuildAsync</c>), which is also what resolves which project the call is for.</summary>
[ApiController, Route("api/v1/webhooks/automation/builds")]
public sealed class AutomationBuildWebhookController(AutomationWebhookService service) : ControllerBase
{
    private static ProblemDetails Problem(string title, string detail, int status) => new() { Title = title, Detail = detail, Status = status };

    [HttpPost]
    public async Task<ActionResult<ReceiveBuildWebhookResult>> ReceiveBuild(ReceiveBuildWebhookRequest request, CancellationToken ct)
    {
        var token = Request.Headers["X-Webhook-Token"].ToString();
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized(Problem("ไม่พบ Webhook Token", "ต้องส่ง header X-Webhook-Token มาด้วย", 401));
        try { return Ok(await service.ReceiveBuildAsync(token, request, ct)); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(Problem("Webhook Token ไม่ถูกต้อง", ex.Message, 401)); }
        catch (EntityNotFoundException) { return NotFound(Problem("ไม่พบ Release", "Release ที่ระบุไม่มีอยู่จริง", 404)); }
        catch (DuplicateCodeException ex) { return Conflict(Problem("สร้าง Build ไม่สำเร็จ", ex.Message, 409)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }
}
