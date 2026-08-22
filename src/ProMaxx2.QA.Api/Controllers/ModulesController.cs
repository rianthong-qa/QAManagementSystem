using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController,Route("api/v1/modules"),Authorize(Policy="ProjectEdit"),RequireProjectAccess]
public sealed class ModulesController(ProjectService service):ControllerBase
{
    [HttpPut("{id:guid}")] public async Task<ActionResult<ModuleDto>> Update(Guid id,UpdateModuleRequest request,CancellationToken ct){try{return Ok(await service.UpdateModuleAsync(id,request,UserId(),ct));}catch(EntityNotFoundException){return NotFound();}catch(Exception ex)when(ex is ArgumentException or InvalidOperationException){return BadRequest(new ProblemDetails{Title="ข้อมูลไม่ถูกต้อง",Detail=ex.Message,Status=400});}}
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id,CancellationToken ct){try{await service.DeactivateModuleAsync(id,UserId(),ct);return NoContent();}catch(EntityNotFoundException){return NotFound();}}
    [HttpPost("{id:guid}/move")] public async Task<ActionResult<ModuleDto>> Move(Guid id,MoveModuleRequest request,CancellationToken ct){try{return Ok(await service.MoveModuleAsync(id,request,UserId(),ct));}catch(EntityNotFoundException){return NotFound();}catch(InvalidOperationException ex){return BadRequest(new ProblemDetails{Title="ไม่สามารถย้าย Module ได้",Detail=ex.Message,Status=400});}}
    private Guid? UserId()=>Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("sub"),out var id)?id:null;
}
