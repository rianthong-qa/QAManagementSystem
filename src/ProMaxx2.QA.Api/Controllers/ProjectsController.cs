using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController,Route("api/v1/projects"),Authorize(Policy="ProjectView"),RequireProjectAccess]
public sealed class ProjectsController(ProjectService service):ControllerBase
{
    [HttpGet] public async Task<IReadOnlyList<ProjectDto>> List(CancellationToken ct)
    {
        var userId = UserId();
        if (userId.HasValue)
            return await service.ListForUserAsync(userId.Value, ct);
        return await service.ListAsync(ct);
    }
    [HttpGet("{id:guid}")] public async Task<ActionResult<ProjectDto>> Get(Guid id,CancellationToken ct){try{return Ok(await service.GetAsync(id,ct));}catch(EntityNotFoundException){return NotFound();}}
    [HttpPost,Authorize(Policy="ProjectEdit")] public async Task<ActionResult<ProjectDto>> Create(CreateProjectRequest request,CancellationToken ct){try{var result=await service.CreateAsync(request,UserId(),ct);return CreatedAtAction(nameof(Get),new{id=result.ProjectId},result);}catch(DuplicateCodeException ex){return Conflict(new ProblemDetails{Title="รหัสโครงการซ้ำ",Detail=ex.Message,Status=409});}catch(ArgumentException ex){return BadRequest(new ProblemDetails{Title="ข้อมูลไม่ถูกต้อง",Detail=ex.Message,Status=400});}}
    [HttpPut("{id:guid}"),Authorize(Policy="ProjectEdit")] public async Task<ActionResult<ProjectDto>> Update(Guid id,UpdateProjectRequest request,CancellationToken ct){try{return Ok(await service.UpdateAsync(id,request,UserId(),ct));}catch(EntityNotFoundException){return NotFound();}catch(ArgumentException ex){return BadRequest(new ProblemDetails{Title="ข้อมูลไม่ถูกต้อง",Detail=ex.Message,Status=400});}}
    [HttpDelete("{id:guid}"),Authorize(Policy="ProjectEdit")] public async Task<IActionResult> Delete(Guid id,CancellationToken ct){try{await service.DeactivateAsync(id,UserId(),ct);return NoContent();}catch(EntityNotFoundException){return NotFound();}}
    [HttpGet("{projectId:guid}/modules")] public Task<IReadOnlyList<ModuleDto>> Modules(Guid projectId,CancellationToken ct)=>service.ListModulesAsync(projectId,ct);
    [HttpPost("{projectId:guid}/modules"),Authorize(Policy="ProjectEdit")] public async Task<ActionResult<ModuleDto>> CreateModule(Guid projectId,CreateModuleRequest request,CancellationToken ct){try{return Ok(await service.CreateModuleAsync(projectId,request,UserId(),ct));}catch(DuplicateCodeException ex){return Conflict(new ProblemDetails{Title="รหัสโมดูลซ้ำ",Detail=ex.Message,Status=409});}catch(EntityNotFoundException){return NotFound();}catch(ArgumentException ex){return BadRequest(new ProblemDetails{Title="ข้อมูลไม่ถูกต้อง",Detail=ex.Message,Status=400});}}
    private Guid? UserId()=>Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("sub"),out var id)?id:null;
}
