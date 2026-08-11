using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Dashboard;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public sealed class DashboardController(IDashboardService dashboard) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummary>(StatusCodes.Status200OK)]
    public ActionResult<DashboardSummary> GetSummary() => Ok(dashboard.GetSummary());
}
