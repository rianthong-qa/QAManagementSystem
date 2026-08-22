using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.Releases;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1"), Authorize(Policy="ProjectView"), RequireProjectAccess]
public sealed class TestSummaryController(TestSummaryService service) : ControllerBase
{
    [HttpGet("releases/{releaseId:guid}/test-summary")]
    [ProducesResponseType<TestSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TestSummaryDto>> Get(Guid releaseId, CancellationToken ct)
    {
        try { return Ok(await service.GetAsync(releaseId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }
}
