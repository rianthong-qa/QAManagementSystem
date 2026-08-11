using System.Security.Claims;using Microsoft.AspNetCore.Authorization;using Microsoft.AspNetCore.Mvc;using ProMaxx2.QA.Application.Execution;using ProMaxx2.QA.Application.Projects;
namespace ProMaxx2.QA.Api.Controllers;
[ApiController,Route("api/v1"),Authorize(Policy="ExecutionRun")]
public sealed class ExecutionsController(ExecutionService service):ControllerBase
{
 [HttpGet("test-cycles/{cycleId:guid}/execution")]public async Task<ActionResult<ExecutionWorkspaceDto>>Workspace(Guid cycleId,CancellationToken ct){try{return Ok(await service.WorkspaceAsync(cycleId,ct));}catch(EntityNotFoundException){return NotFound();}}[HttpPost("test-cycle-cases/{cycleCaseId:guid}/executions")]public async Task<ActionResult<ExecutionHistoryDto>>Create(Guid cycleCaseId,CreateExecutionRequest r,CancellationToken ct){try{return Ok(await service.CreateAsync(cycleCaseId,r,UserId(),ct));}catch(EntityNotFoundException){return NotFound();}catch(ArgumentException ex){return BadRequest(new ProblemDetails{Title="ผลการทดสอบไม่ถูกต้อง",Detail=ex.Message,Status=400});}}private Guid?UserId()=>Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("sub"),out var id)?id:null;
}
