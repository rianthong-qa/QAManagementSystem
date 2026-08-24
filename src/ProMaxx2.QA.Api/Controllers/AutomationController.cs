using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using ProMaxx2.QA.Application.Automation;
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
    [HttpGet("cases")] public Task<IReadOnlyList<AutomationCaseDto>> ListCases([FromQuery] Guid projectId, [FromQuery] string? search, [FromQuery] int take = 100, CancellationToken ct = default)
        => cases.ListCasesAsync(projectId, search, take, ct);

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
    }

    [HttpGet("objects")] public Task<IReadOnlyList<AutomationObjectDto>> ListObjects([FromQuery] Guid projectId, [FromQuery] string? search, CancellationToken ct) => cases.ListObjectsAsync(projectId, search, ct);
    [HttpPost("objects"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<AutomationObjectDto>> CreateObject(CreateAutomationObjectRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.CreateObjectAsync(request, ct)); }
        catch (ArgumentException ex) { return BadRequest(Problem("สร้าง Object ไม่สำเร็จ", ex.Message, 400)); }
    }
    [HttpPut("objects/{id:guid}"), Authorize(Policy = "AutomationManage")] public async Task<ActionResult<AutomationObjectDto>> UpdateObject(Guid id, [FromQuery] Guid projectId, UpdateAutomationObjectRequest request, CancellationToken ct)
    {
        try { return Ok(await cases.UpdateObjectAsync(id, projectId, request, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
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

    [HttpGet("jobs")] public Task<IReadOnlyList<AutomationJobDto>> ListJobs([FromQuery] Guid? projectId, [FromQuery] Guid? buildId, [FromQuery] int take = 100, CancellationToken ct = default)
        => agentService.ListJobsAsync(projectId, buildId, take, ct);

    [HttpGet("executions")] public Task<IReadOnlyList<AutomationExecutionDto>> ListExecutions([FromQuery] Guid projectId, [FromQuery] Guid? buildId, [FromQuery] int take = 100, CancellationToken ct = default)
        => agentService.ListExecutionsAsync(projectId, buildId, take, ct);

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