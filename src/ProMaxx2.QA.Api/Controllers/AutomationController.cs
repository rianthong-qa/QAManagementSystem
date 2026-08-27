using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.TestManagement;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1/automation"), Authorize(Policy = "AutomationView"), RequireProjectAccess]
public sealed class AutomationController(
    AutomationCaseService cases,
    AutomationAgentService agentService,
    AutomationAiService aiService,
    AutomationDefectService defectService,
    ITestCaseRepository testCases,
    IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>AUT-P2-001: real server-side page/size/filter/sort — <c>page</c>/<c>size</c> default to a single
    /// page of 200 so existing callers that just want "up to 200 flat" (dashboard KPIs, batch-run/suite case
    /// pickers) keep working unchanged by simply reading <c>.Rows</c> off the response.</summary>
    [HttpGet("cases")] public Task<PagedResult<AutomationCaseDto>> ListCases([FromQuery] Guid projectId, [FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? automationTarget,
        [FromQuery] string? sortBy, [FromQuery] int page = 1, [FromQuery] int size = 200, CancellationToken ct = default)
        => cases.ListCasesPagedAsync(projectId, search, status, automationTarget, sortBy, page, size, ct);

    [HttpGet("cases/{id:guid}")] public async Task<ActionResult<AutomationCaseDto>> GetCase(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await cases.GetCaseAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("cases"), Authorize(Policy = "AutomationEdit")] public async Task<ActionResult<AutomationCaseDto>> CreateCase([FromQuery] Guid projectId, CreateAutomationCaseRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.CreateAsync(projectId, request, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("สร้าง Automation Case ไม่สำเร็จ", ex.Message, 400)); }
    }

    [HttpGet("cases/{id:guid}/versions")] public Task<IReadOnlyList<AutomationVersionDto>> ListVersions(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
        => cases.ListVersionsAsync(id, projectId, ct);

    [HttpPost("cases/{id:guid}/status"), Authorize(Policy = "AutomationEdit")] public async Task<ActionResult<AutomationCaseDto>> ChangeStatus(Guid id, [FromQuery] Guid projectId, ChangeCaseStatusRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.ChangeStatusAsync(id, projectId, request.Status, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("สถานะไม่ถูกต้อง", ex.Message, 400)); }
    }

    [HttpPost("cases/{id:guid}/target"), Authorize(Policy = "AutomationEdit")] public async Task<ActionResult<AutomationCaseDto>> ChangeTarget(Guid id, [FromQuery] Guid projectId, ChangeCaseTargetRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.ChangeTargetAsync(id, projectId, request.TargetApp, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("Target App ไม่ถูกต้อง", ex.Message, 400)); }
    }

    [HttpPost("cases/{id:guid}/generate"), Authorize(Policy = "AutomationGenerateAi")] public async Task<ActionResult<AutomationVersionDto>> Generate(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try
        {
            var caseEntity = await cases.GetCaseAsync(id, projectId, ct);
            var testCase = await testCases.GetAsync(caseEntity.TestCaseId, ct) ?? throw new EntityNotFoundException("Test case not found.");
            var actions = await cases.ListActionsAsync(ct);
            var objects = await cases.ListObjectsAsync(projectId, null, ct);
            var context = new AutomationAiContext(
                testCase.TestCaseCode, testCase.Title, testCase.Objective, testCase.Preconditions,
                testCase.Steps.Select(s => new AiTestStep(s.StepNo, s.Action, s.TestData, s.ExpectedResult)).ToList(),
                actions.Select(a => a.ActionCode).ToList(),
                objects.Select(o => $"{o.ScreenCode}.{o.ObjectCode}").ToList(),
                []);
            var result = await aiService.GenerateAsync(context, ct);
            return Ok(await cases.CreateAiVersionAsync(id, projectId, result.DslJson, result.AiProvider, result.AiModel, result.Confidence, UserId(), ct));
        }
        catch (AiNotConfiguredException ex) { return BadRequest(Problem("AI ยังไม่พร้อมใช้งาน", ex.Message, 400)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("Generate ไม่สำเร็จ", ex.Message, 400)); }
    }

    [HttpPost("cases/{id:guid}/versions"), Authorize(Policy = "AutomationEdit")] public async Task<ActionResult<AutomationVersionDto>> CreateVersion(Guid id, [FromQuery] Guid projectId, CreateAutomationVersionRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.CreateVersionAsync(id, projectId, request, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("สร้าง Version ไม่สำเร็จ", ex.Message, 400)); }
    }

    [HttpPost("versions/{id:guid}/dsl"), Authorize(Policy = "AutomationEdit")] public async Task<ActionResult<AutomationVersionDto>> UpdateDsl(Guid id, [FromQuery] Guid projectId, UpdateDslRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.UpdateVersionDslAsync(id, projectId, request.DslJson, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("บันทึก DSL ไม่สำเร็จ", ex.Message, 400)); }
    }

    [HttpPost("versions/{id:guid}/validate"), Authorize(Policy = "AutomationValidate")] public async Task<ActionResult<AutomationVersionDto>> Validate(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await cases.ValidateVersionAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("versions/{id:guid}/approve"), Authorize(Policy = "AutomationApprove")] public async Task<ActionResult<AutomationCaseDto>> Approve(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await cases.ApproveVersionAsync(id, projectId, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("อนุมัติไม่สำเร็จ", ex.Message, 409)); }
    }

    [HttpPost("cases/{id:guid}/run"), Authorize(Policy = "AutomationExecute")] public async Task<ActionResult<AutomationExecutionDto>> Run(Guid id, [FromQuery] Guid projectId, RunAutomationRequest request, CancellationToken ct)
    {
        try { return Ok(await agentService.RequestExecutionAsync(projectId, new RequestExecutionRequest(id, request.VersionId, request.BuildId, request.EnvironmentId, request.AgentId, request.Priority), UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("สั่งรันไม่สำเร็จ", ex.Message, 400)); }
    }

    [HttpPost("batch-run"), Authorize(Policy = "AutomationExecute")] public async Task<ActionResult<BatchRunResultDto>> BatchRun([FromQuery] Guid projectId, BatchRunRequest request, CancellationToken ct)
    {
        try { return Ok(await agentService.BatchRunAsync(projectId, request, UserId(), ct)); }
        catch (ArgumentException ex) { return BadRequest(Problem("สั่งรัน Batch ไม่สำเร็จ", ex.Message, 400)); }
    }

    [HttpGet("dashboard")] public Task<AutomationDashboardDto> Dashboard([FromQuery] Guid projectId, CancellationToken ct) => agentService.GetDashboardAsync(projectId, ct);

    [HttpGet("actions")] public Task<IReadOnlyList<AutomationActionDto>> ListActions(CancellationToken ct) => cases.ListActionsAsync(ct);
    [HttpPost("actions"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<AutomationActionDto>> CreateAction(CreateAutomationActionRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.CreateActionAsync(request, ct)); }
        catch (ArgumentException ex) { return BadRequest(Problem("สร้าง Action ไม่สำเร็จ", ex.Message, 400)); }
    }
    [HttpPut("actions/{id:guid}"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<AutomationActionDto>> UpdateAction(Guid id, UpdateAutomationActionRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.UpdateActionAsync(id, request, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("แก้ไข Action ไม่สำเร็จ", ex.Message, 400)); }
    }

    [HttpGet("objects")] public Task<IReadOnlyList<AutomationObjectDto>> ListObjects([FromQuery] Guid projectId, [FromQuery] string? search, CancellationToken ct) => cases.ListObjectsAsync(projectId, search, ct);
    [HttpPost("objects"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<AutomationObjectDto>> CreateObject(CreateAutomationObjectRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.CreateObjectAsync(request, ct)); }
        catch (ArgumentException ex) { return BadRequest(Problem("สร้าง Object ไม่สำเร็จ", ex.Message, 400)); }
    }
    [HttpPost("objects/import"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<AutomationObjectImportResultDto>> ImportObjects(ImportAutomationObjectsRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.ImportObjectsAsync(request, ct)); }
        catch (ArgumentException ex) { return BadRequest(Problem("Import Object failed", ex.Message, 400)); }
    }
    [HttpPut("objects/{id:guid}"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<AutomationObjectDto>> UpdateObject(Guid id, [FromQuery] Guid projectId, UpdateAutomationObjectRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.UpdateObjectAsync(id, projectId, request, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("แก้ไข Object ไม่สำเร็จ", ex.Message, 400)); }
    }
    [HttpPost("objects/{id:guid}/activate"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<AutomationObjectDto>> ActivateObject(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await cases.SetObjectActiveAsync(id, projectId, true, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }
    [HttpPost("objects/{id:guid}/deactivate"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<AutomationObjectDto>> DeactivateObject(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await cases.SetObjectActiveAsync(id, projectId, false, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("objects/verify"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<IReadOnlyList<AutomationObjectVerificationDto>>> RequestObjectVerification([FromQuery] Guid projectId, RequestObjectVerificationRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.RequestObjectVerificationAsync(projectId, request, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("ขอตรวจสอบ Object ไม่สำเร็จ", ex.Message, 400)); }
    }

    [HttpGet("objects/verifications")] public Task<IReadOnlyList<AutomationObjectVerificationDto>> ListVerifications([FromQuery] Guid projectId, [FromQuery] Guid? objectId, CancellationToken ct)
        => cases.ListVerificationsAsync(projectId, objectId, ct);

    [HttpPost("cases/{id:guid}/maintenance/owner"), Authorize(Policy = "AutomationEdit")] public async Task<ActionResult<AutomationCaseDto>> AssignMaintenanceOwner(Guid id, [FromQuery] Guid projectId, AssignMaintenanceOwnerRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.AssignMaintenanceOwnerAsync(id, projectId, request.OwnerUserId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("มอบหมายไม่สำเร็จ", ex.Message, 409)); }
    }

    [HttpPost("cases/{id:guid}/maintenance/resolve"), Authorize(Policy = "AutomationEdit")] public async Task<ActionResult<AutomationCaseDto>> ResolveMaintenance(Guid id, [FromQuery] Guid projectId, ResolveMaintenanceRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.ResolveMaintenanceAsync(id, projectId, request.ResolutionNote, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("แก้ไขไม่สำเร็จ", ex.Message, 409)); }
    }

    [HttpGet("cases/flaky-candidates")] public Task<IReadOnlyList<FlakyCandidateDto>> FlakyCandidates([FromQuery] Guid projectId, CancellationToken ct) => cases.GetFlakyCandidatesAsync(projectId, ct);

    [HttpPost("cases/{id:guid}/quarantine"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<AutomationCaseDto>> Quarantine(Guid id, [FromQuery] Guid projectId, QuarantineCaseRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.QuarantineCaseAsync(id, projectId, request, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("Quarantine ไม่สำเร็จ", ex.Message, 400)); }
    }

    [HttpPost("cases/{id:guid}/unquarantine"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<AutomationCaseDto>> Unquarantine(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await cases.UnquarantineCaseAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpGet("failures/dashboard")] public Task<FailureBreakdownDto> FailureDashboard([FromQuery] Guid projectId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] Guid? buildId, [FromQuery] Guid? agentId, [FromQuery] string? failureType, CancellationToken ct)
        => agentService.GetFailureBreakdownAsync(projectId, from, to, buildId, agentId, failureType, ct);

    [HttpGet("failures/executions")] public Task<IReadOnlyList<AutomationExecutionDto>> FailureExecutions([FromQuery] Guid projectId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] Guid? buildId, [FromQuery] Guid? agentId, [FromQuery] string? failureType, [FromQuery] int take = 200, CancellationToken ct = default)
        => agentService.ListFailedExecutionsAsync(projectId, from, to, buildId, agentId, failureType, take, ct);

    [HttpGet("settings/retry-policy")] public Task<RetryPolicyDto> GetRetryPolicy(CancellationToken ct) => agentService.GetRetryPolicyAsync(ct);

    [HttpPut("settings/retry-policy"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<RetryPolicyDto>> UpdateRetryPolicy(UpdateRetryPolicyRequest request, CancellationToken ct)
        => Ok(await agentService.UpdateRetryPolicyAsync(request, UserId(), ct));

    [HttpGet("agents")] public Task<IReadOnlyList<AutomationAgentDto>> ListAgents(CancellationToken ct) => agentService.ListAgentsAsync(ct);
    [HttpPost("agents/{id:guid}/enable"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<AutomationAgentDto>> EnableAgent(Guid id, CancellationToken ct)
    {
        try { return Ok(await agentService.SetAgentEnabledAsync(id, true, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }
    [HttpPost("agents/{id:guid}/disable"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<AutomationAgentDto>> DisableAgent(Guid id, CancellationToken ct)
    {
        try { return Ok(await agentService.SetAgentEnabledAsync(id, false, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpDelete("agents/{id:guid}"), Authorize(Policy = "AutomationManage")] public async Task<IActionResult> DeleteAgent(Guid id, CancellationToken ct)
    {
        try { await agentService.DeleteAgentAsync(id, ct); return NoContent(); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    /// <summary>AUT-P2-001: see remark on <c>ListCases</c> — same "default page 1/size 200 keeps flat callers
    /// working via .Rows" rationale.</summary>
    [HttpGet("jobs")] public Task<PagedResult<AutomationJobDto>> ListJobs([FromQuery] Guid? projectId, [FromQuery] Guid? buildId, [FromQuery] string? status, [FromQuery] string? sortBy,
        [FromQuery] int page = 1, [FromQuery] int size = 200, CancellationToken ct = default)
        => agentService.ListJobsPagedAsync(projectId, buildId, status, sortBy, page, size, ct);

    /// <summary>AUT-P2-002: adds environmentId/agentId/targetApp/failureType/from/to on top of AUT-P2-001's
    /// buildId/status/search/sortBy/page/size.</summary>
    [HttpGet("executions")] public Task<PagedResult<AutomationExecutionDto>> ListExecutions([FromQuery] Guid projectId, [FromQuery] Guid? buildId, [FromQuery] Guid? environmentId, [FromQuery] Guid? agentId,
        [FromQuery] string? targetApp, [FromQuery] string? status, [FromQuery] string? failureType, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? search, [FromQuery] string? sortBy,
        [FromQuery] int page = 1, [FromQuery] int size = 200, CancellationToken ct = default)
        => agentService.ListExecutionsPagedAsync(projectId, buildId, environmentId, agentId, targetApp, status, failureType, from, to, search, sortBy, page, size, ct);

    /// <summary>AUT-P2-003: Pass/Fail/Flaky trend, bucketed by day (default)/build/release.</summary>
    [HttpGet("executions/trend")] public Task<ExecutionTrendDto> GetExecutionTrend([FromQuery] Guid projectId, [FromQuery] string? groupBy, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] Guid? releaseId, CancellationToken ct = default)
        => agentService.GetExecutionTrendAsync(projectId, groupBy, from, to, releaseId, ct);

    [HttpGet("executions/{id:guid}")] public async Task<ActionResult<AutomationExecutionDto>> GetExecution(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await agentService.GetExecutionAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("executions/{id:guid}/cancel"), Authorize(Policy = "AutomationExecute")] public async Task<ActionResult<AutomationExecutionDto>> Cancel(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await agentService.CancelExecutionAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("ยกเลิกไม่สำเร็จ", ex.Message, 409)); }
    }

    [HttpPost("executions/{id:guid}/classify"), Authorize(Policy = "AutomationView")] public async Task<ActionResult<AutomationFailureClassificationDto>> Classify(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await defectService.ClassifyAsync(id, projectId, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("executions/{id:guid}/analyze"), Authorize(Policy = "AutomationGenerateAi")] public async Task<ActionResult<AiFailureAnalysisDto>> Analyze(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { return Ok(await defectService.AnalyzeAsync(id, projectId, ct)); }
        catch (AiNotConfiguredException ex) { return BadRequest(Problem("AI ยังไม่พร้อมใช้งาน", ex.Message, 400)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(Problem("วิเคราะห์ Fail ไม่สำเร็จ", ex.Message, 400)); }
    }

    [HttpPost("executions/{id:guid}/defect"), Authorize(Policy = "DefectEdit")] public async Task<ActionResult<object>> CreateDefect(Guid id, [FromQuery] Guid projectId, CreateAutomationDefectRequest request, CancellationToken ct)
    {
        try { return Ok(await defectService.CreateDefectAsync(id, projectId, request, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(Problem("สร้าง Defect ไม่สำเร็จ", ex.Message, 409)); }
    }

    [HttpGet("executions/{id:guid}/evidence"), Authorize(Policy = "AutomationEvidence")] public async Task<ActionResult<IReadOnlyList<AutomationEvidenceDto>>> ListEvidence(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try { await agentService.GetExecutionAsync(id, projectId, ct); return Ok(await agentService.ListEvidenceAsync(id, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpGet("executions/{id:guid}/evidence/{evidenceId:guid}"), Authorize(Policy = "AutomationEvidence")] public async Task<IActionResult> DownloadEvidence(Guid id, Guid evidenceId, [FromQuery] Guid projectId, CancellationToken ct)
    {
        try
        {
            await agentService.GetExecutionAsync(id, projectId, ct);
            var evidence = await agentService.GetEvidenceAsync(evidenceId, id, ct);
            if (string.IsNullOrWhiteSpace(evidence.FilePath)) return NotFound();
            var root = EvidenceRoot();
            var full = Path.GetFullPath(Path.Combine(root, evidence.FilePath));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(full)) return NotFound();
            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(full, out var contentType)) contentType = "application/octet-stream";
            return PhysicalFile(full, contentType, Path.GetFileName(full));
        }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    private string EvidenceRoot()
    {
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "AutomationEvidence"));
        Directory.CreateDirectory(root);
        return root + Path.DirectorySeparatorChar;
    }

    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
}

public sealed record UpdateDslRequest(string DslJson);
public sealed record RunAutomationRequest(Guid VersionId, Guid BuildId, Guid EnvironmentId, Guid? AgentId, int Priority);
public sealed record ChangeCaseStatusRequest(string Status);
public sealed record ChangeCaseTargetRequest(string TargetApp);
