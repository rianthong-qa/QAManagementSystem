using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Governance;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1/risk-acceptances"), Authorize(Policy="ProjectView"), RequireProjectAccess]
public sealed class RiskAcceptanceController(RiskAcceptanceService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<RiskAcceptanceDto>> List([FromQuery] Guid? projectId, CancellationToken ct) => service.ListAsync(projectId, ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RiskAcceptanceDto>> Get(Guid id, CancellationToken ct)
    {
        try { return Ok(await service.GetAsync(id, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost]
    public async Task<ActionResult<RiskAcceptanceDto>> Create(CreateRiskAcceptanceRequest request, CancellationToken ct)
    {
        try { return Ok(await service.CreateAsync(request, UserId(), ct)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูล Risk Acceptance ไม่ถูกต้อง", ex.Message, 400)); }
        catch (DuplicateCodeException ex) { return Conflict(Problem("รหัส Risk ซ้ำ", ex.Message, 409)); }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RiskAcceptanceDto>> Update(Guid id, UpdateRiskAcceptanceRequest request, CancellationToken ct)
    {
        try { return Ok(await service.UpdateAsync(id, request, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("แก้ไข Risk ไม่ได้", ex.Message, 409)); }
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<ActionResult<RiskAcceptanceDto>> Submit(Guid id, CancellationToken ct)
    {
        try { return Ok(await service.SubmitAsync(id, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("ส่ง Risk ไม่ได้", ex.Message, 409)); }
    }

    [HttpPost("{id:guid}/approve"), Authorize(Policy="RiskApprove")]
    public async Task<ActionResult<RiskAcceptanceDto>> Approve(Guid id, RiskDecisionRequest request, CancellationToken ct)
    {
        try { return Ok(await service.ApproveAsync(id, request.Comment, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("อนุมัติ Risk ไม่ได้", ex.Message, 409)); }
    }

    [HttpPost("{id:guid}/reject"), Authorize(Policy="RiskApprove")]
    public async Task<ActionResult<RiskAcceptanceDto>> Reject(Guid id, RiskDecisionRequest request, CancellationToken ct)
    {
        try { return Ok(await service.RejectAsync(id, request.Comment, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("ปฏิเสธ Risk ไม่ได้", ex.Message, 409)); }
    }

    [HttpPost("{id:guid}/close"), Authorize(Policy="RiskApprove")]
    public async Task<ActionResult<RiskAcceptanceDto>> Close(Guid id, CancellationToken ct)
    {
        try { return Ok(await service.CloseAsync(id, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("ปิด Risk ไม่ได้", ex.Message, 409)); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await service.DeleteAsync(id, UserId(), ct); return NoContent(); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
    private static ProblemDetails Problem(string title, string detail, int status) => new() { Title = title, Detail = detail, Status = status };
}
