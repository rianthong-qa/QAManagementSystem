using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.Api.Controllers;

/// <summary>AUT-P1-001/AUT-P1-002: persistent Automation Suite — create/edit/close/reopen, plus case membership (add/remove/reorder, Required/Optional).</summary>
[ApiController, Route("api/v1/automation/suites"), Authorize(Policy = "AutomationView"), RequireProjectAccess]
public sealed class AutomationSuitesController(AutomationSuiteService service) : ControllerBase
{
    private Guid? UserId() => Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value, out var id) ? id : null;
    private static ProblemDetails Problem(string title, string detail, int status) => new() { Title = title, Detail = detail, Status = status };

    [HttpGet]
    public Task<IReadOnlyList<AutomationSuiteListDto>> List([FromQuery] Guid projectId, [FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken ct)
        => service.ListAsync(projectId, search, isActive, ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AutomationSuiteDto>> Get(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await service.GetAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost, Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationSuiteDto>> Create([FromQuery] Guid projectId, CreateAutomationSuiteRequest request, CancellationToken ct)
    {
        try
        {
            var result = await service.CreateAsync(projectId, request, UserId(), ct);
            return CreatedAtAction(nameof(Get), new { id = result.AutomationSuiteId, projectId }, result);
        }
        catch (EntityNotFoundException) { return NotFound(Problem("ไม่พบ Project", "Project ที่เลือกไม่มีอยู่จริง", 404)); }
        catch (DuplicateCodeException ex) { return Conflict(Problem("รหัส Suite ซ้ำ", ex.Message, 409)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationSuiteDto>> Update(Guid id, [FromQuery] Guid projectId, UpdateAutomationSuiteRequest request, CancellationToken ct)
    {
        try { return Ok(await service.UpdateAsync(id, projectId, request, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
        catch (InvalidOperationException ex) { return Conflict(Problem("แก้ไขไม่สำเร็จ", ex.Message, 409)); }
    }

    [HttpPost("{id:guid}/close"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationSuiteDto>> Close(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await service.CloseAsync(id, projectId, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("ปิด Suite ไม่สำเร็จ", ex.Message, 409)); }
    }

    [HttpPost("{id:guid}/reopen"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationSuiteDto>> Reopen(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await service.ReopenAsync(id, projectId, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("เปิด Suite ไม่สำเร็จ", ex.Message, 409)); }
    }

    [HttpPost("{id:guid}/cases"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationSuiteDto>> AddCases(Guid id, [FromQuery] Guid projectId, AddSuiteCasesRequest request, CancellationToken ct)
    {
        try { return Ok(await service.AddCasesAsync(id, projectId, request, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
        catch (InvalidOperationException ex) { return Conflict(Problem("เพิ่ม Case ไม่สำเร็จ", ex.Message, 409)); }
    }

    [HttpPut("{id:guid}/cases/{caseId:guid}"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationSuiteDto>> UpdateCase(Guid id, Guid caseId, [FromQuery] Guid projectId, UpdateSuiteCaseRequest request, CancellationToken ct)
    {
        try { return Ok(await service.UpdateCaseAsync(id, projectId, caseId, request, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(Problem("ลำดับไม่ถูกต้อง", ex.Message, 400)); }
        catch (InvalidOperationException ex) { return Conflict(Problem("แก้ไข Case ไม่สำเร็จ", ex.Message, 409)); }
    }

    [HttpDelete("{id:guid}/cases/{caseId:guid}"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationSuiteDto>> RemoveCase(Guid id, Guid caseId, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await service.RemoveCaseAsync(id, projectId, caseId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("ลบ Case ไม่สำเร็จ", ex.Message, 409)); }
    }
}
