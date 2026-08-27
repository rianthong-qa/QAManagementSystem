using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.Api.Controllers;

/// <summary>AUT-DATA-001: request/track a real DB backup ("snapshot") of an Environment's physical database before a
/// test run. This controller is the human/UI-facing side (list/request); the agent-facing claim/complete endpoints
/// live on <see cref="AutomationAgentController"/> alongside job claim/complete, under the same auth model.</summary>
[ApiController, Route("api/v1/automation/data"), Authorize(Policy = "AutomationView"), RequireProjectAccess]
public sealed class AutomationDataController(AutomationDataSnapshotService service, AutomationDataRestoreService restores, AutomationDataSeedService seeds, AutomationEnvironmentDataProfileService profiles) : ControllerBase
{
    private Guid? UserId() => Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value, out var id) ? id : null;
    private static ProblemDetails Problem(string title, string detail, int status) => new() { Title = title, Detail = detail, Status = status };

    [HttpGet("snapshots")]
    public Task<IReadOnlyList<AutomationDbSnapshotDto>> ListSnapshots([FromQuery] Guid projectId, [FromQuery] Guid? environmentId, [FromQuery] Guid? buildId, [FromQuery] int take = 100, CancellationToken ct = default)
        => service.ListAsync(projectId, environmentId, buildId, take, ct);

    [HttpGet("snapshots/{id:guid}")]
    public async Task<ActionResult<AutomationDbSnapshotDto>> GetSnapshot(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await service.GetAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("snapshots"), Authorize(Policy = "AutomationExecute")]
    public async Task<ActionResult<AutomationDbSnapshotDto>> RequestSnapshot([FromQuery] Guid projectId, RequestSnapshotRequest request, CancellationToken ct)
    {
        try
        {
            var result = await service.RequestAsync(projectId, request, UserId(), ct);
            return CreatedAtAction(nameof(GetSnapshot), new { id = result.AutomationDbSnapshotId, projectId }, result);
        }
        catch (EntityNotFoundException ex) { return NotFound(Problem("ไม่พบ Environment หรือ Build", ex.Message, 404)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }

    /// <summary>AUT-DATA-002.</summary>
    [HttpGet("restores")]
    public Task<IReadOnlyList<AutomationDbRestoreDto>> ListRestores([FromQuery] Guid projectId, [FromQuery] Guid? automationDbSnapshotId, CancellationToken ct)
        => restores.ListAsync(projectId, automationDbSnapshotId, ct);

    [HttpGet("restores/{id:guid}")]
    public async Task<ActionResult<AutomationDbRestoreDto>> GetRestore(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await restores.GetAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("restores"), Authorize(Policy = "AutomationExecute")]
    public async Task<ActionResult<AutomationDbRestoreDto>> RequestRestore([FromQuery] Guid projectId, RequestRestoreRequest request, CancellationToken ct)
    {
        try
        {
            var result = await restores.RequestAsync(projectId, request, UserId(), ct);
            return CreatedAtAction(nameof(GetRestore), new { id = result.AutomationDbRestoreId, projectId }, result);
        }
        catch (EntityNotFoundException ex) { return NotFound(Problem("ไม่พบ Snapshot", ex.Message, 404)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }

    // ---- AUT-DATA-003: Seed Scripts ----

    [HttpGet("seed-scripts")]
    public Task<IReadOnlyList<AutomationDataSeedScriptListDto>> ListSeedScripts([FromQuery] Guid projectId, [FromQuery] string? scriptType, [FromQuery] bool? isActive, CancellationToken ct)
        => seeds.ListScriptsAsync(projectId, scriptType, isActive, ct);

    [HttpGet("seed-scripts/{id:guid}")]
    public async Task<ActionResult<AutomationDataSeedScriptDto>> GetSeedScript(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await seeds.GetScriptAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("seed-scripts"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationDataSeedScriptDto>> CreateSeedScript([FromQuery] Guid projectId, CreateSeedScriptRequest request, CancellationToken ct)
    {
        try
        {
            var result = await seeds.CreateScriptAsync(projectId, request, UserId(), ct);
            return CreatedAtAction(nameof(GetSeedScript), new { id = result.AutomationDataSeedScriptId, projectId }, result);
        }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }

    [HttpPut("seed-scripts/{id:guid}"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationDataSeedScriptDto>> UpdateSeedScript(Guid id, [FromQuery] Guid projectId, UpdateSeedScriptRequest request, CancellationToken ct)
    {
        try { return Ok(await seeds.UpdateScriptAsync(id, projectId, request, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }

    [HttpPost("seed-scripts/{id:guid}/activate"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationDataSeedScriptDto>> ActivateSeedScript(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await seeds.SetScriptActiveAsync(id, projectId, true, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("seed-scripts/{id:guid}/deactivate"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationDataSeedScriptDto>> DeactivateSeedScript(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await seeds.SetScriptActiveAsync(id, projectId, false, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    /// <summary>AUT-DATA-005: required before a "MasterData" script can be run — see
    /// <c>AutomationDataSeedService.RequestRunAsync</c>. Uses the same "AutomationEdit" policy as script CRUD: there
    /// is no separate approver role in this system's policy set (View/Edit/Execute only), and introducing one is out
    /// of scope for this AC — documented the same way as other known routing/role gaps in this module.</summary>
    [HttpPost("seed-scripts/{id:guid}/approve"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationDataSeedScriptDto>> ApproveSeedScript(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await seeds.ApproveScriptAsync(id, projectId, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("seed-scripts/{id:guid}/reject"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationDataSeedScriptDto>> RejectSeedScript(Guid id, [FromQuery] Guid projectId, RejectSeedScriptRequest request, CancellationToken ct)
    {
        try { return Ok(await seeds.RejectScriptAsync(id, projectId, request, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    // ---- AUT-DATA-003: Seed Runs ----

    [HttpGet("seed-runs")]
    public Task<IReadOnlyList<AutomationDataSeedRunDto>> ListSeedRuns([FromQuery] Guid projectId, [FromQuery] Guid? automationDataSeedScriptId, CancellationToken ct)
        => seeds.ListRunsAsync(projectId, automationDataSeedScriptId, ct);

    [HttpGet("seed-runs/{id:guid}")]
    public async Task<ActionResult<AutomationDataSeedRunDto>> GetSeedRun(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await seeds.GetRunAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("seed-runs"), Authorize(Policy = "AutomationExecute")]
    public async Task<ActionResult<AutomationDataSeedRunDto>> RequestSeedRun([FromQuery] Guid projectId, RequestSeedRunRequest request, CancellationToken ct)
    {
        try
        {
            var result = await seeds.RequestRunAsync(projectId, request, UserId(), ct);
            return CreatedAtAction(nameof(GetSeedRun), new { id = result.AutomationDataSeedRunId, projectId }, result);
        }
        catch (EntityNotFoundException ex) { return NotFound(Problem("ไม่พบ Seed Script", ex.Message, 404)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }

    // ---- AUT-DATA-006: Environment Data Profiles ----

    [HttpGet("environment-data-profiles")]
    public Task<IReadOnlyList<AutomationEnvironmentDataProfileDto>> ListEnvironmentDataProfiles([FromQuery] Guid projectId, CancellationToken ct)
        => profiles.ListAsync(projectId, ct);

    [HttpGet("environment-data-profiles/{id:guid}")]
    public async Task<ActionResult<AutomationEnvironmentDataProfileDto>> GetEnvironmentDataProfile(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await profiles.GetAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("environment-data-profiles"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationEnvironmentDataProfileDto>> CreateEnvironmentDataProfile([FromQuery] Guid projectId, CreateEnvironmentDataProfileRequest request, CancellationToken ct)
    {
        try
        {
            var result = await profiles.CreateAsync(projectId, request, UserId(), ct);
            return CreatedAtAction(nameof(GetEnvironmentDataProfile), new { id = result.AutomationEnvironmentDataProfileId, projectId }, result);
        }
        catch (EntityNotFoundException ex) { return NotFound(Problem("ไม่พบ Environment", ex.Message, 404)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }

    [HttpPut("environment-data-profiles/{id:guid}"), Authorize(Policy = "AutomationEdit")]
    public async Task<ActionResult<AutomationEnvironmentDataProfileDto>> UpdateEnvironmentDataProfile(Guid id, [FromQuery] Guid projectId, UpdateEnvironmentDataProfileRequest request, CancellationToken ct)
    {
        try { return Ok(await profiles.UpdateAsync(id, projectId, request, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }
}
