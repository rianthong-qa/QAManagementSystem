using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.TestManagement;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Controllers;

public sealed record GenerateTestCycleAiRequest(Guid ProjectId, Guid ReleaseId, Guid BuildId, Guid EnvironmentId, Guid? TestSuiteId);

[ApiController, Route("api/v1"), Authorize(Policy = "ExecutionRun"), RequireProjectAccess]
public sealed class TestCyclesController(TestCycleService service, TestCycleAiService ai, ProjectService projects, TestSuiteService suites, QaDbContext db) : ControllerBase
{
    [HttpGet("test-environments")]
    public Task<IReadOnlyList<EnvironmentDto>> Environments([FromQuery] Guid? projectId, CancellationToken ct) => service.EnvironmentsAsync(projectId, ct);

    [HttpPost("test-environments")]
    public async Task<ActionResult<EnvironmentDto>> CreateEnvironment(SaveEnvironmentRequest request, CancellationToken ct) => Ok(await service.CreateEnvironmentAsync(request, ct));

    [HttpGet("test-cycles")]
    public Task<TestCycleListResultDto> List([FromQuery] Guid? projectId, [FromQuery] Guid? releaseId, [FromQuery] Guid? buildId, [FromQuery] Guid? moduleId, [FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? cycleType, [FromQuery] Guid? createdBy, [FromQuery] int page = 1, [FromQuery] int size = 20, CancellationToken ct = default) => service.ListAsync(projectId, releaseId, buildId, moduleId, search, status, cycleType, createdBy, page, size, ct);

    [HttpGet("test-cycles/options")]
    public Task<IReadOnlyList<TestCycleOptionDto>> Options([FromQuery] Guid? projectId, CancellationToken ct = default) => service.ListOptionsAsync(projectId, ct);

    [HttpGet("test-cycles/{id:guid}")]
    public async Task<ActionResult<TestCycleDto>> Get(Guid id, CancellationToken ct)
    {
        var cycle = await service.GetAsync(id, ct);
        return cycle is null ? NotFound() : Ok(cycle);
    }

    [HttpPost("test-cycles")]
    public async Task<ActionResult<TestCycleDto>> Create(SaveTestCycleRequest request, CancellationToken ct)
    {
        try { return Ok(await service.CreateAsync(request, UserId(), ct)); }
        catch (DuplicateCodeException exception) { return Conflict(new ProblemDetails { Title = "รหัส Test Cycle ซ้ำ", Detail = exception.Message, Status = 409 }); }
        catch (Exception exception) when (exception is ArgumentException or EntityNotFoundException) { return BadRequest(new ProblemDetails { Title = "ข้อมูล Test Cycle ไม่ถูกต้อง", Detail = exception.Message, Status = 400 }); }
        catch (Exception exception) { return StatusCode(500, new ProblemDetails { Title = "เกิดข้อผิดพลาด", Detail = exception.Message, Status = 500 }); }
    }

    [HttpPut("test-cycles/{id:guid}")]
    public async Task<ActionResult<TestCycleDto>> Update(Guid id, SaveTestCycleRequest request, CancellationToken ct)
    {
        try { return Ok(await service.UpdateAsync(id, request, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (DuplicateCodeException exception) { return Conflict(new ProblemDetails { Title = "รหัส Test Cycle ซ้ำ", Detail = exception.Message, Status = 409 }); }
    }

    [HttpPost("test-cycles/{id:guid}/status")]
    public async Task<ActionResult<TestCycleDto>> Status(Guid id, ChangeCycleStatusRequest request, CancellationToken ct)
    {
        try { return Ok(await service.StatusAsync(id, request.Status, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException exception) { return BadRequest(new ProblemDetails { Title = "สถานะไม่ถูกต้อง", Detail = exception.Message, Status = 400 }); }
    }

    [HttpDelete("test-cycles/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await service.DeleteAsync(id, UserId(), ct); return NoContent(); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("test-cycles/generate-ai"), Authorize(Policy = "ExecutionRun"), RequestSizeLimit(21_000_000)]
    public async Task<ActionResult<GeneratedTestCycle>> GenerateAi(GenerateTestCycleAiRequest request, CancellationToken ct)
    {
        try
        {
            var project = (await projects.ListAsync(ct)).SingleOrDefault(item => item.ProjectId == request.ProjectId) ?? throw new ArgumentException("ไม่พบ Project ที่เลือก");
            var release = await db.Releases.FirstOrDefaultAsync(item => item.ReleaseId == request.ReleaseId && item.ProjectId == request.ProjectId, ct) ?? throw new ArgumentException("ไม่พบ Release");
            var build = await db.Builds.FirstOrDefaultAsync(item => item.BuildId == request.BuildId && item.ReleaseId == request.ReleaseId, ct) ?? throw new ArgumentException("ไม่พบ Build");
            var environment = await db.TestEnvironments.FirstOrDefaultAsync(item => item.TestEnvironmentId == request.EnvironmentId, ct) ?? throw new ArgumentException("ไม่พบ Environment");
            var testSuite = request.TestSuiteId.HasValue ? await suites.GetAsync(request.TestSuiteId.Value, ct) : null;
            var totalSuiteCases = testSuite?.Cases.Count ?? 0;
            var cycleTypes = await db.MasterOptions.AsNoTracking().Where(item => item.Category == "TestCycleType" && item.IsActive).OrderBy(item => item.SortOrder).Select(item => item.Value).ToListAsync(ct);
            var existingNames = await db.TestCycles.AsNoTracking().Where(item => item.ProjectId == request.ProjectId && !item.IsDeleted).Select(item => item.CycleName).ToListAsync(ct);
            return Ok(await ai.GenerateAsync(project.ProjectName, release.ReleaseCode, release.PlannedReleaseDate.HasValue ? release.PlannedReleaseDate.Value.ToString("yyyy-MM-dd") : null, build.BuildNumber, environment.EnvironmentName, testSuite, totalSuiteCases, cycleTypes, existingNames, ct));
        }
        catch (ArgumentException exception) { return BadRequest(new ProblemDetails { Title = "ข้อมูลไม่ครบ", Detail = exception.Message, Status = 400 }); }
        catch (InvalidOperationException exception) { var status = ai.IsConfigured ? 502 : 503; return StatusCode(status, new ProblemDetails { Title = "AI Generate Test Cycle ไม่พร้อมใช้งาน", Detail = exception.Message, Status = status }); }
        catch (OperationCanceledException) { return StatusCode(504, new ProblemDetails { Title = "AI ใช้เวลาประมวลผลนานเกินไป", Detail = "กรุณาลองใหม่อีกครั้ง", Status = 504 }); }
        catch (Exception exception) { return StatusCode(500, new ProblemDetails { Title = "AI Generate Test Cycle ไม่สำเร็จ", Detail = exception.InnerException?.Message ?? exception.Message, Status = 500 }); }
    }

    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
}
