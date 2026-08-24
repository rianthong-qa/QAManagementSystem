using System.Text.Json;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.TestManagement;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.Application.Automation;

public sealed class AutomationCaseService(IAutomationRepository repository, ITestCaseRepository testCases)
{
    public Task<IReadOnlyList<AutomationCaseDto>> ListCasesAsync(Guid projectId, string? search, int take, CancellationToken ct)
        => repository.ListCasesAsync(projectId, search, Math.Clamp(take, 1, 200), ct);

    public async Task<AutomationCaseDto> GetCaseAsync(Guid id, Guid projectId, CancellationToken ct)
        => await repository.GetCaseAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");

    public async Task<AutomationCaseDto> CreateAsync(Guid projectId, CreateAutomationCaseRequest r, Guid? userId, CancellationToken ct)
    {
        var testCase = await testCases.GetAsync(r.TestCaseId, ct) ?? throw new EntityNotFoundException("Test case not found.");
        if (testCase.ProjectId != projectId) throw new ArgumentException("Test case does not belong to the selected project.");
        if (!testCase.AutomationCandidate) throw new ArgumentException("Only automation candidate test cases can have an automation case.");
        var existing = await repository.ListCasesAsync(projectId, testCase.TestCaseCode, 1, ct);
        if (existing.Any(x => x.TestCaseId == r.TestCaseId)) throw new ArgumentException("This test case already has an automation case.");
        var code = "AUT-" + (testCase.TestCaseCode.StartsWith("TC-", StringComparison.OrdinalIgnoreCase) ? testCase.TestCaseCode[3..] : testCase.TestCaseCode);
        var target = testCase.AutomationTarget switch { "pos" => "Pos", "app" => "App", _ => "WindowsUI" };
        var automationType = string.IsNullOrWhiteSpace(r.AutomationType) || r.AutomationType == "WindowsUI" ? target : r.AutomationType;
        var entity = new AutomationCase(r.TestCaseId, code, automationType, r.OwnerUserId ?? userId, userId);
        await repository.AddCaseAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        return await repository.GetCaseAsync(entity.AutomationCaseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
    }

    public async Task<AutomationVersionDto> CreateVersionAsync(Guid caseId, Guid projectId, CreateAutomationVersionRequest r, Guid? userId, CancellationToken ct)
    {
        var caseEntity = await repository.FindCaseAsync(caseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
        var testCase = await testCases.GetAsync(caseEntity.TestCaseId, ct) ?? throw new EntityNotFoundException("Test case not found.");
        var dsl = DeserializeDsl(r.DslJson);
        var nextNo = caseEntity.CurrentVersionNo + 1;
        var version = new AutomationVersion(caseId, nextNo, testCase.RevisionNo, JsonSerializer.Serialize(dsl), false, null, null, null, userId);
        version.RecordChangeReason(r.ChangeReason);
        await repository.AddVersionAsync(version, ct);
        caseEntity.SetVersion(nextNo, userId);
        await repository.SaveChangesAsync(ct);
        return (await repository.ListVersionsAsync(caseId, ct)).Single(x => x.AutomationVersionId == version.AutomationVersionId);
    }

    public async Task<AutomationVersionDto> UpdateVersionDslAsync(Guid versionId, Guid projectId, string dslJson, Guid? userId, CancellationToken ct)
    {
        var version = await repository.FindVersionAsync(versionId, ct) ?? throw new EntityNotFoundException("Automation version not found.");
        var caseEntity = await repository.FindCaseAsync(version.AutomationCaseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
        version.UpdateDsl(JsonSerializer.Serialize(DeserializeDsl(dslJson)));
        await repository.SaveChangesAsync(ct);
        return (await repository.ListVersionsAsync(caseEntity.AutomationCaseId, ct)).Single(x => x.AutomationVersionId == versionId);
    }

    public async Task<AutomationVersionDto> CreateAiVersionAsync(Guid caseId, Guid projectId, string dslJson, string aiProvider, string aiModel, double confidence, Guid? userId, CancellationToken ct)
    {
        var caseEntity = await repository.FindCaseAsync(caseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
        var testCase = await testCases.GetAsync(caseEntity.TestCaseId, ct) ?? throw new EntityNotFoundException("Test case not found.");
        var nextNo = caseEntity.CurrentVersionNo + 1;
        var version = new AutomationVersion(caseId, nextNo, testCase.RevisionNo, JsonSerializer.Serialize(DeserializeDsl(dslJson)), true, aiProvider, aiModel, confidence, userId);
        version.RecordChangeReason($"AI Generated ({aiProvider} / {aiModel}) — รอตรวจสอบ");
        await repository.AddVersionAsync(version, ct);
        caseEntity.SetVersion(nextNo, userId);
        caseEntity.MarkGeneratedByAi(true);
        caseEntity.ChangeStatus("NeedsReview");
        await repository.SaveChangesAsync(ct);
        return (await repository.ListVersionsAsync(caseId, ct)).Single(x => x.AutomationVersionId == version.AutomationVersionId);
    }

    public async Task<AutomationVersionDto> ValidateVersionAsync(Guid versionId, Guid projectId, CancellationToken ct)
    {
        var version = await repository.FindVersionAsync(versionId, ct) ?? throw new EntityNotFoundException("Automation version not found.");
        var caseEntity = await repository.FindCaseAsync(version.AutomationCaseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
        var testCase = await testCases.GetAsync(caseEntity.TestCaseId, ct);
        var dsl = version.ToDsl() ?? new DslDocument();
        var actions = await repository.ListActionCodesAsync(ct);
        var objects = await repository.ListObjectKeysAsync(projectId, ct);
        var result = AutomationValidator.Validate(dsl, actions, objects, null);
        version.SetValidation(result.IsValid ? "Valid" : "Invalid", result.IsValid ? null : string.Join(Environment.NewLine, result.Errors));
        if (!result.IsValid) caseEntity.ChangeStatus("NeedsReview");
        await repository.SaveChangesAsync(ct);
        return (await repository.ListVersionsAsync(caseEntity.AutomationCaseId, ct)).Single(x => x.AutomationVersionId == versionId);
    }

    public async Task<AutomationCaseDto> ApproveVersionAsync(Guid versionId, Guid projectId, Guid? userId, CancellationToken ct)
    {
        var version = await repository.FindVersionAsync(versionId, ct) ?? throw new EntityNotFoundException("Automation version not found.");
        var caseEntity = await repository.FindCaseAsync(version.AutomationCaseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
        version.Approve(userId);
        caseEntity.ChangeStatus("Ready");
        caseEntity.SetVersion(version.VersionNo, userId);
        await repository.SaveChangesAsync(ct);
        return await repository.GetCaseAsync(caseEntity.AutomationCaseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
    }

    public async Task<AutomationCaseDto> ChangeStatusAsync(Guid caseId, Guid projectId, string status, CancellationToken ct)
    {
        var caseEntity = await repository.FindCaseAsync(caseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
        caseEntity.ChangeStatus(status);
        await repository.SaveChangesAsync(ct);
        return await repository.GetCaseAsync(caseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
    }

    public async Task<AutomationCaseDto> ChangeTargetAsync(Guid caseId, Guid projectId, string targetApp, CancellationToken ct)
    {
        var caseEntity = await repository.FindCaseAsync(caseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
        caseEntity.SetTargetApp(targetApp);
        await repository.SaveChangesAsync(ct);
        return await repository.GetCaseAsync(caseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
    }

    public Task<IReadOnlyList<AutomationVersionDto>> ListVersionsAsync(Guid caseId, Guid projectId, CancellationToken ct)
        => repository.ListVersionsAsync(caseId, ct);

    public Task<IReadOnlyList<AutomationActionDto>> ListActionsAsync(CancellationToken ct) => repository.ListActionsAsync(ct);
    public async Task<AutomationActionDto> CreateActionAsync(CreateAutomationActionRequest r, CancellationToken ct)
    {
        if (await repository.FindActionByCodeAsync(r.ActionCode, ct) is not null) throw new ArgumentException("Action code already exists.");
        var entity = new AutomationAction(r.ActionCode, r.ActionName, r.Category, r.Description, r.ParameterSchemaJson, r.ActionCode, r.MinimumAgentVersion);
        await repository.AddActionAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        return (await repository.ListActionsAsync(ct)).Single(x => x.AutomationActionId == entity.AutomationActionId);
    }
    public async Task<AutomationActionDto> UpdateActionAsync(Guid id, UpdateAutomationActionRequest r, CancellationToken ct)
    {
        var entity = await repository.FindActionAsync(id, ct) ?? throw new EntityNotFoundException("Action not found.");
        entity.Update(r.ActionName, r.Category, r.Description, r.ParameterSchemaJson, r.MinimumAgentVersion, r.IsActive);
        await repository.SaveChangesAsync(ct);
        return (await repository.ListActionsAsync(ct)).Single(x => x.AutomationActionId == id);
    }

    public Task<IReadOnlyList<AutomationObjectDto>> ListObjectsAsync(Guid projectId, string? search, CancellationToken ct) => repository.ListObjectsAsync(projectId, search, ct);
    public async Task<AutomationObjectDto> CreateObjectAsync(CreateAutomationObjectRequest r, CancellationToken ct)
    {
        if (r.ProjectId == Guid.Empty) throw new ArgumentException("Project is required.");
        var entity = new AutomationObject(r.ProjectId, r.ModuleId, r.ApplicationCode, r.ScreenCode, r.ObjectCode, r.ObjectName, r.ControlType, r.AutomationId, r.SelectorJson);
        await repository.AddObjectAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        return (await repository.ListObjectsAsync(r.ProjectId, entity.ObjectCode, ct)).Single(x => x.AutomationObjectId == entity.AutomationObjectId);
    }
    public async Task<AutomationObjectDto> UpdateObjectAsync(Guid id, Guid projectId, UpdateAutomationObjectRequest r, CancellationToken ct)
    {
        var entity = await repository.FindObjectAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Object not found.");
        entity.Update(r.ObjectName, r.ControlType, r.AutomationId, r.SelectorJson);
        await repository.SaveChangesAsync(ct);
        return (await repository.ListObjectsAsync(projectId, entity.ObjectCode, ct)).Single(x => x.AutomationObjectId == id);
    }
    public async Task<AutomationObjectDto> SetObjectActiveAsync(Guid id, Guid projectId, bool active, CancellationToken ct)
    {
        var entity = await repository.FindObjectAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Object not found.");
        entity.SetActive(active);
        await repository.SaveChangesAsync(ct);
        return (await repository.ListObjectsAsync(projectId, entity.ObjectCode, ct)).Single(x => x.AutomationObjectId == id);
    }

    private static DslDocument DeserializeDsl(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("DSL is required.");
        DslDocument dsl;
        try
        {
            dsl = JsonSerializer.Deserialize<DslDocument>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new ArgumentException("DSL JSON cannot be empty.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"DSL JSON is invalid: {ex.Message}");
        }
        return dsl;
    }
}

public sealed class AutomationAgentService(IAutomationRepository repository)
{
    public async Task<AutomationAgentDto> RegisterAsync(RegisterAgentRequest r, Guid? userId, CancellationToken ct)
    {
        var agent = await repository.FindAgentByCodeAsync(r.AgentCode, ct);
        if (agent is null)
        {
            agent = new AutomationAgent(r.AgentCode, r.MachineName, r.AgentVersion, r.OperatingSystem, r.Architecture, userId);
            agent.ReplaceCapabilities(r.Capabilities.Select(c => new AutomationAgentCapability(agent.AgentId, c, "1.0")).ToList());
            await repository.AddAgentAsync(agent, ct);
        }
        else
        {
            agent.ReplaceCapabilities(r.Capabilities.Select(c => new AutomationAgentCapability(agent.AgentId, c, "1.0")).ToList());
            if (agent.IsDeleted) agent.Reactivate();
            agent.Heartbeat(DateTime.UtcNow, agent.CurrentExecutionId);
        }
        await repository.SaveChangesAsync(ct);
        return (await repository.ListAgentsAsync(ct)).Single(x => x.AgentId == agent.AgentId);
    }

    public async Task<AutomationAgentDto> HeartbeatAsync(AgentHeartbeatRequest r, CancellationToken ct)
    {
        var agent = await repository.FindAgentByCodeAsync(r.AgentCode, ct) ?? throw new EntityNotFoundException("Agent not registered. Register the agent first.");
        agent.Heartbeat(DateTime.UtcNow, r.CurrentExecutionId);
        if (string.Equals(r.Status, "Busy", StringComparison.OrdinalIgnoreCase)) agent.SetStatus("Busy");
        else if (string.Equals(r.Status, "Idle", StringComparison.OrdinalIgnoreCase)) agent.SetStatus("Idle");
        await repository.SaveChangesAsync(ct);
        return (await repository.ListAgentsAsync(ct)).Single(x => x.AgentId == agent.AgentId);
    }

    public async Task<AutomationAgentDto> SetAgentEnabledAsync(Guid agentId, bool enabled, CancellationToken ct)
    {
        var agent = await repository.FindAgentByCodeAsync("", ct);
        var dto = (await repository.ListAgentsAsync(ct)).SingleOrDefault(x => x.AgentId == agentId) ?? throw new EntityNotFoundException("Agent not found.");
        var entity = await repository.FindAgentByCodeAsync(dto.AgentCode, ct) ?? throw new EntityNotFoundException("Agent not found.");
        entity.SetEnabled(enabled);
        await repository.SaveChangesAsync(ct);
        return (await repository.ListAgentsAsync(ct)).Single(x => x.AgentId == agentId);
    }

    public async Task DeleteAgentAsync(Guid agentId, CancellationToken ct)
    {
        var dto = (await repository.ListAgentsAsync(ct)).SingleOrDefault(x => x.AgentId == agentId) ?? throw new EntityNotFoundException("Agent not found.");
        var entity = await repository.FindAgentByCodeAsync(dto.AgentCode, ct) ?? throw new EntityNotFoundException("Agent not found.");
        entity.SoftDelete();
        await repository.SaveChangesAsync(ct);
    }

    public Task<IReadOnlyList<AutomationAgentDto>> ListAgentsAsync(CancellationToken ct) => repository.ListAgentsAsync(ct);

    public async Task<AutomationExecutionDto> RequestExecutionAsync(Guid projectId, RequestExecutionRequest r, Guid? userId, CancellationToken ct)
    {
        var caseEntity = await repository.GetCaseAsync(r.CaseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
        if (caseEntity.Status != "Ready") throw new ArgumentException("Only Ready automation cases can be executed.");
        var versionId = r.VersionId;
        if (versionId == Guid.Empty)
        {
            var versions = await repository.ListVersionsAsync(r.CaseId, ct);
            var approved = versions.OrderByDescending(x => x.VersionNo).FirstOrDefault(x => x.ApprovedAt.HasValue && x.ValidationStatus == "Valid");
            if (approved is null) throw new ArgumentException("No approved automation version exists for this case.");
            versionId = approved.AutomationVersionId;
        }
        var execution = new AutomationExecution(r.CaseId, versionId, null, r.BuildId, r.EnvironmentId, userId?.ToString(), caseEntity.AutomationType);
        if (r.AgentId.HasValue) execution.AssignAgent(r.AgentId.Value);
        await repository.AddExecutionAsync(execution, ct);
        await repository.SaveChangesAsync(ct);
        var job = new AutomationJob(execution.AutomationExecutionId, r.AgentId, r.Priority, DateTime.UtcNow);
        await repository.AddJobAsync(job, ct);
        execution.LinkJob(job.JobId);
        await repository.SaveChangesAsync(ct);
        return await repository.GetExecutionAsync(execution.AutomationExecutionId, projectId, ct) ?? throw new EntityNotFoundException("Execution not found.");
    }

    public async Task<BatchRunResultDto> BatchRunAsync(Guid projectId, BatchRunRequest r, Guid? userId, CancellationToken ct)
    {
        if (r.CaseIds is null || r.CaseIds.Count == 0) throw new ArgumentException("กรุณาเลือก Automation Case อย่างน้อย 1 รายการ");
        var created = new List<AutomationExecutionDto>();
        var skipped = new List<string>();
        foreach (var caseId in r.CaseIds.Distinct())
        {
            var caseEntity = await repository.GetCaseAsync(caseId, projectId, ct);
            if (caseEntity is null || caseEntity.Status != "Ready")
            {
                skipped.Add(caseEntity?.AutomationCode ?? caseId.ToString());
                continue;
            }
            var versions = await repository.ListVersionsAsync(caseId, ct);
            var approved = versions.OrderByDescending(x => x.VersionNo).FirstOrDefault(x => x.ApprovedAt.HasValue && x.ValidationStatus == "Valid");
            if (approved is null)
            {
                skipped.Add(caseEntity.AutomationCode);
                continue;
            }
            var execution = new AutomationExecution(caseId, approved.AutomationVersionId, null, r.BuildId, r.EnvironmentId, userId?.ToString(), caseEntity.AutomationType);
            if (r.AgentId.HasValue) execution.AssignAgent(r.AgentId.Value);
            await repository.AddExecutionAsync(execution, ct);
            await repository.SaveChangesAsync(ct);
            var job = new AutomationJob(execution.AutomationExecutionId, r.AgentId, r.Priority, DateTime.UtcNow);
            await repository.AddJobAsync(job, ct);
            execution.LinkJob(job.JobId);
            await repository.SaveChangesAsync(ct);
            var dto = await repository.GetExecutionAsync(execution.AutomationExecutionId, projectId, ct);
            if (dto is not null) created.Add(dto);
        }
        return new BatchRunResultDto(created, skipped, r.CaseIds.Count);
    }

    public Task<AutomationDashboardDto> GetDashboardAsync(Guid projectId, CancellationToken ct) => repository.GetDashboardAsync(projectId, ct);

    public Task<IReadOnlyList<AutomationJobDto>> ListJobsAsync(Guid? projectId, Guid? buildId, int take, CancellationToken ct) => repository.ListJobsAsync(projectId, buildId, Math.Clamp(take, 1, 200), ct);
    public Task<IReadOnlyList<AutomationExecutionDto>> ListExecutionsAsync(Guid projectId, Guid? buildId, int take, CancellationToken ct) => repository.ListExecutionsAsync(projectId, buildId, Math.Clamp(take, 1, 200), ct);

    public async Task<AutomationJobPackageDto?> ClaimNextJobAsync(ClaimJobRequest r, CancellationToken ct)
    {
        var caps = r.Capabilities.Select(x => x.Trim().ToUpperInvariant()).Distinct().ToList();
        return await repository.ClaimNextJobAsync(r.AgentCode, r.AgentVersion, caps, r.TargetApp ?? "WindowsUI", ct);
    }

    public async Task ReportStepResultAsync(Guid executionId, ReportStepResultRequest r, CancellationToken ct)
    {
        var execution = await repository.FindExecutionAsync(executionId, ct) ?? throw new EntityNotFoundException("Execution not found.");
        await repository.AddStepResultAsync(new AutomationStepResult(executionId, r.StepNo, r.ActionCode, r.Status, r.ActualResult, r.ErrorCode, r.ErrorMessage, r.EvidencePath, r.StartedAt, r.CompletedAt), ct);
        await repository.SaveChangesAsync(ct);
    }

    public async Task<AutomationExecutionDto> CompleteExecutionAsync(Guid executionId, CompleteExecutionRequest r, CancellationToken ct)
    {
        var execution = await repository.FindExecutionAsync(executionId, ct) ?? throw new EntityNotFoundException("Execution not found.");
        execution.Complete(r.Status, r.FailureType, r.ErrorCode, r.ErrorMessage, DateTime.UtcNow);
        var job = await repository.FindJobByExecutionAsync(executionId, ct);
        if (job is not null) job.Complete(r.Status, r.ErrorMessage);
        var caseEntity = await repository.FindCaseByIdAsync(execution.AutomationCaseId, ct);
        if (caseEntity is not null)
        {
            if (r.Status == "Failed" && r.ErrorCode is "AUT-UI-001" or "AUT-UI-002" or "AUT-UI-003")
                caseEntity.RequireMaintenance(null);
            else
                caseEntity.ChangeStatus("Ready");
        }
        await repository.SaveChangesAsync(ct);
        return await repository.GetExecutionAsync(executionId, execution.AutomationCase.TestCase.ProjectId, ct) ?? throw new EntityNotFoundException("Execution not found.");
    }

    public async Task<AutomationExecutionDto> GetExecutionAsync(Guid executionId, Guid projectId, CancellationToken ct)
        => await repository.GetExecutionAsync(executionId, projectId, ct) ?? throw new EntityNotFoundException("Execution not found.");

    public async Task<AutomationExecutionDto> CancelExecutionAsync(Guid executionId, Guid projectId, CancellationToken ct)
    {
        var execution = await repository.FindExecutionAsync(executionId, ct) ?? throw new EntityNotFoundException("Execution not found.");
        if (execution.Status is not ("Queued" or "Running")) throw new InvalidOperationException("Only queued or running executions can be cancelled.");
        execution.Complete("Cancelled", "AutomationFailure", "AUT-JOB-003", "Execution cancelled by user.", DateTime.UtcNow);
        var job = await repository.FindJobByExecutionAsync(executionId, ct);
        if (job is not null) job.Complete("Cancelled", "Execution cancelled by user.");
        var caseEntity = await repository.FindCaseAsync(execution.AutomationCaseId, projectId, ct);
        caseEntity?.ChangeStatus("Ready");
        await repository.SaveChangesAsync(ct);
        return await repository.GetExecutionAsync(executionId, projectId, ct) ?? throw new EntityNotFoundException("Execution not found.");
    }

    public async Task UploadStepEvidenceAsync(Guid executionId, int stepNo, string relativePath, CancellationToken ct)
        => await repository.AttachStepEvidenceAsync(executionId, stepNo, relativePath, ct);

    public async Task UploadEvidenceAsync(Guid executionId, int? stepNo, string evidenceType, string relativePath, string? capturedBy, CancellationToken ct)
    {
        var execution = await repository.FindExecutionAsync(executionId, ct) ?? throw new EntityNotFoundException("Execution not found.");
        await repository.AddEvidenceAsync(new AutomationEvidence(executionId, stepNo, evidenceType, relativePath, capturedBy), ct);
        await repository.SaveChangesAsync(ct);
    }

    public Task<IReadOnlyList<AutomationEvidenceDto>> ListEvidenceAsync(Guid executionId, CancellationToken ct)
        => repository.ListEvidenceAsync(executionId, ct);

    public async Task<AutomationEvidenceDto> GetEvidenceAsync(Guid evidenceId, Guid executionId, CancellationToken ct)
    {
        var evidence = await repository.FindEvidenceAsync(evidenceId, executionId, ct) ?? throw new EntityNotFoundException("Evidence not found.");
        return new AutomationEvidenceDto(evidence.AutomationEvidenceId, evidence.StepNo, evidence.EvidenceType, evidence.FilePath, evidence.CapturedBy, evidence.CapturedAt);
    }

    public async Task<string?> GetEvidencePathAsync(Guid executionId, Guid stepResultId, Guid projectId, CancellationToken ct)
    {
        var execution = await repository.GetExecutionAsync(executionId, projectId, ct) ?? throw new EntityNotFoundException("Execution not found.");
        var step = await repository.FindStepResultAsync(stepResultId, executionId, ct) ?? throw new EntityNotFoundException("Step result not found.");
        return step.EvidencePath;
    }
}