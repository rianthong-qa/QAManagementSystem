using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.TestPlanning;
using ProMaxx2.QA.Domain.TestPlanning;

namespace ProMaxx2.QA.Api.Controllers;

/// <summary>
/// Test Plan / Configuration Matrix (TEST_PLAN_CONFIGURATION_MATRIX_PLAN.md §4) — route ใช้ชื่อ planId/runId
/// เพื่อให้ <see cref="ProjectAccessFilter"/> ตรวจ Project ของ record ได้; ข้อผิดพลาดตอบ ProblemDetails พร้อม code TEST_PLAN_*
/// </summary>
[ApiController, Route("api/v1/test-plans"), Authorize(Policy = "TestPlanView"), RequireProjectAccess]
public sealed class TestPlansController(TestPlanService service) : ControllerBase
{
    [HttpGet]
    public Task<TestPlanListResultDto> List([FromQuery] Guid? projectId, [FromQuery] Guid? releaseId, [FromQuery] string? status, [FromQuery] string? planType, [FromQuery] Guid? ownerUserId, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int size = 20, CancellationToken ct = default)
        => service.ListAsync(projectId, releaseId, status, planType, ownerUserId, search, page, size, ct);

    [HttpGet("{planId:guid}")]
    public Task<ActionResult<TestPlanDto>> Get(Guid planId, CancellationToken ct) => Run(() => service.GetAsync(planId, ct));

    [HttpGet("{planId:guid}/matrix")]
    public Task<ActionResult<PagedResult<TestPlanRunDto>>> Matrix(Guid planId, [FromQuery] string? status, [FromQuery] string? configurationKey, [FromQuery] Guid? ownerUserId, [FromQuery] int page = 1, [FromQuery] int size = 50, CancellationToken ct = default)
        => Run(() => service.MatrixAsync(planId, status, configurationKey, ownerUserId, page, size, ct));

    [HttpPost, Authorize(Policy = "TestPlanEdit")]
    public Task<ActionResult<TestPlanDto>> Create(SaveTestPlanRequest request, CancellationToken ct) => Run(() => service.CreateAsync(request, UserId(), ct));

    [HttpPut("{planId:guid}"), Authorize(Policy = "TestPlanEdit")]
    public Task<ActionResult<TestPlanDto>> Update(Guid planId, SaveTestPlanRequest request, CancellationToken ct) => Run(() => service.UpdateAsync(planId, request, UserId(), ct));

    [HttpPost("{planId:guid}/close"), Authorize(Policy = "TestPlanFreeze")]
    public Task<ActionResult<TestPlanDto>> Close(Guid planId, TestPlanActionRequest request, CancellationToken ct) => Run(() => service.CloseAsync(planId, request.Reason, UserId(), ct));

    [HttpPost("{planId:guid}/cancel"), Authorize(Policy = "TestPlanFreeze")]
    public Task<ActionResult<TestPlanDto>> Cancel(Guid planId, TestPlanActionRequest request, CancellationToken ct) => Run(() => service.CancelAsync(planId, request.Reason, UserId(), ct));

    [HttpPost("{planId:guid}/reopen"), Authorize(Policy = "TestPlanFreeze")]
    public Task<ActionResult<TestPlanDto>> Reopen(Guid planId, TestPlanActionRequest request, CancellationToken ct) => Run(() => service.ReopenAsync(planId, request.Reason, UserId(), ct));

    [HttpPost("{planId:guid}/freeze"), Authorize(Policy = "TestPlanFreeze")]
    public Task<ActionResult<TestPlanDto>> Freeze(Guid planId, TestPlanActionRequest request, CancellationToken ct) => Run(() => service.FreezeAsync(planId, request.Reason, UserId(), ct));

    [HttpPost("{planId:guid}/unfreeze"), Authorize(Policy = "TestPlanFreeze")]
    public Task<ActionResult<TestPlanDto>> Unfreeze(Guid planId, TestPlanActionRequest request, CancellationToken ct) => Run(() => service.UnfreezeAsync(planId, request.Reason, UserId(), ct));

    [HttpDelete("{planId:guid}"), Authorize(Policy = "TestPlanEdit")]
    public async Task<IActionResult> Delete(Guid planId, CancellationToken ct)
    {
        var result = await Run(async () => { await service.DeleteAsync(planId, UserId(), ct); return true; });
        return result.Result is OkObjectResult ? NoContent() : result.Result!;
    }

    [HttpPost("{planId:guid}/runs"), Authorize(Policy = "TestPlanEdit")]
    public Task<ActionResult<TestPlanRunDto>> CreateRun(Guid planId, CreateTestPlanRunRequest request, CancellationToken ct) => Run(() => service.CreateRunAsync(planId, request, UserId(), ct));

    [HttpPost("{planId:guid}/runs/attach-cycle"), Authorize(Policy = "TestPlanEdit")]
    public Task<ActionResult<TestPlanRunDto>> AttachCycle(Guid planId, AttachTestPlanCycleRequest request, CancellationToken ct) => Run(() => service.AttachCycleAsync(planId, request, UserId(), ct));

    [HttpPost("{planId:guid}/clone"), Authorize(Policy = "TestPlanEdit")]
    public Task<ActionResult<TestPlanDto>> Clone(Guid planId, CloneTestPlanRequest request, CancellationToken ct) => Run(() => service.CloneAsync(planId, request, UserId(), ct));

    [HttpGet("{planId:guid}/members"), Authorize(Policy = "TestPlanAssign")]
    public Task<ActionResult<IReadOnlyList<TestPlanMemberDto>>> Members(Guid planId, CancellationToken ct) => Run(() => service.MembersAsync(planId, ct));

    [HttpPost("{planId:guid}/runs/{runId:guid}/assign"), Authorize(Policy = "TestPlanAssign")]
    public Task<ActionResult<AssignTestPlanRunResultDto>> AssignRun(Guid planId, Guid runId, AssignTestPlanRunRequest request, CancellationToken ct) => Run(() => service.AssignRunAsync(planId, runId, request, UserId(), ct));

    [HttpGet("{planId:guid}/runs/{runId:guid}")]
    public Task<ActionResult<TestPlanRunDto>> GetRun(Guid planId, Guid runId, CancellationToken ct) => Run(() => service.GetRunAsync(planId, runId, ct));

    [HttpPut("{planId:guid}/runs/{runId:guid}"), Authorize(Policy = "TestPlanEdit")]
    public Task<ActionResult<TestPlanRunDto>> UpdateRun(Guid planId, Guid runId, UpdateTestPlanRunRequest request, CancellationToken ct) => Run(() => service.UpdateRunAsync(planId, runId, request, UserId(), ct));

    [HttpDelete("{planId:guid}/runs/{runId:guid}"), Authorize(Policy = "TestPlanEdit")]
    public async Task<IActionResult> DetachRun(Guid planId, Guid runId, CancellationToken ct)
    {
        var result = await Run(async () => { await service.DetachRunAsync(planId, runId, UserId(), ct); return true; });
        return result.Result is OkObjectResult ? NoContent() : result.Result!;
    }

    /// <summary>แปลงข้อผิดพลาดของ Service เป็น ProblemDetails + code เดียวกันทุก endpoint</summary>
    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (EntityNotFoundException ex) { return NotFound(Failure(TestPlanErrorCodes.NotFound, "ไม่พบข้อมูล", ex.Message, 404)); }
        catch (DuplicateCodeException ex) { return Conflict(Failure(TestPlanErrorCodes.DuplicateCode, "รหัสซ้ำ", ex.Message, 409)); }
        catch (TestPlanRuleException ex) { return Conflict(Failure(ex.Code, "ดำเนินการกับ Test Plan ไม่ได้", ex.Message, 409)); }
        catch (ArgumentException ex) { return BadRequest(Failure(TestPlanErrorCodes.Invalid, "ข้อมูล Test Plan ไม่ถูกต้อง", ex.Message, 400)); }
    }

    private static ProblemDetails Failure(string code, string title, string detail, int status)
    {
        var problem = new ProblemDetails { Title = title, Detail = detail, Status = status };
        problem.Extensions["code"] = code;
        return problem;
    }

    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
}
