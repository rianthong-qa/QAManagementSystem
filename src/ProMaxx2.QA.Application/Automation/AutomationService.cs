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

    /// <summary>AUT-P2-001.</summary>
    public Task<PagedResult<AutomationCaseDto>> ListCasesPagedAsync(Guid projectId, string? search, string? status, string? automationTarget, string? sortBy, int page, int size, CancellationToken ct)
        => repository.ListCasesPagedAsync(projectId, search, status, automationTarget, sortBy, page, size, ct);

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

    /// <summary>ลบ Automation Case ถาวร — ตรวจว่าทุก id ที่ระบุอยู่ใน project นี้จริงก่อน (project scope กันลบข้าม
    /// project จากช่องโหว่ id เดา) แล้วส่งต่อให้ repository จัดการลำดับการลบตาราง Restrict/Cascade ที่เกี่ยวข้อง.</summary>
    public async Task HardDeleteCasesAsync(Guid projectId, HardDeleteAutomationCasesRequest r, CancellationToken ct)
    {
        if (r.AutomationCaseIds.Count == 0) return;
        var owned = new List<Guid>();
        foreach (var id in r.AutomationCaseIds.Distinct())
            if (await repository.GetCaseAsync(id, projectId, ct) is not null) owned.Add(id);
        if (owned.Count == 0) throw new EntityNotFoundException("Automation case not found.");
        await repository.HardDeleteCasesAsync(owned, ct);
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
        ValidateJson(r.ParameterSchemaJson, "Parameter schema");
        var entity = new AutomationAction(r.ActionCode, r.ActionName, r.Category, r.Description, r.ParameterSchemaJson, r.ActionCode, r.MinimumAgentVersion);
        await repository.AddActionAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        return (await repository.ListActionsAsync(ct)).Single(x => x.AutomationActionId == entity.AutomationActionId);
    }
    public async Task<AutomationActionDto> UpdateActionAsync(Guid id, UpdateAutomationActionRequest r, CancellationToken ct)
    {
        var entity = await repository.FindActionAsync(id, ct) ?? throw new EntityNotFoundException("Action not found.");
        ValidateJson(r.ParameterSchemaJson, "Parameter schema");
        entity.Update(r.ActionName, r.Category, r.Description, r.ParameterSchemaJson, r.HandlerKey, r.MinimumAgentVersion, r.IsActive, r.RetrySafety);
        await repository.SaveChangesAsync(ct);
        return (await repository.ListActionsAsync(ct)).Single(x => x.AutomationActionId == id);
    }

    public Task<IReadOnlyList<AutomationObjectDto>> ListObjectsAsync(Guid projectId, string? search, CancellationToken ct) => repository.ListObjectsAsync(projectId, search, ct);
    public async Task<AutomationObjectDto> CreateObjectAsync(CreateAutomationObjectRequest r, CancellationToken ct)
    {
        if (r.ProjectId == Guid.Empty) throw new ArgumentException("Project is required.");
        ValidateJson(r.SelectorJson, "Selector");
        if (await repository.ObjectKeyExistsAsync(r.ProjectId, r.ApplicationCode, r.ScreenCode, r.ObjectCode, null, ct)) throw new ArgumentException("Object business key already exists.");
        if (!string.IsNullOrWhiteSpace(r.AutomationId) && await repository.ObjectAutomationIdExistsAsync(r.ProjectId, r.ApplicationCode, r.AutomationId, null, ct)) throw new ArgumentException("AutomationId already exists.");
        var entity = new AutomationObject(r.ProjectId, r.ModuleId, r.ApplicationCode, r.ScreenCode, r.ObjectCode, r.ObjectName, r.ControlType, r.AutomationId, r.SelectorJson);
        await repository.AddObjectAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        return (await repository.ListObjectsAsync(r.ProjectId, entity.ObjectCode, ct)).Single(x => x.AutomationObjectId == entity.AutomationObjectId);
    }
    public async Task<AutomationObjectDto> UpdateObjectAsync(Guid id, Guid projectId, UpdateAutomationObjectRequest r, CancellationToken ct)
    {
        var entity = await repository.FindObjectAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Object not found.");
        ValidateJson(r.SelectorJson, "Selector");
        if (await repository.ObjectKeyExistsAsync(projectId, r.ApplicationCode, r.ScreenCode, r.ObjectCode, id, ct)) throw new ArgumentException("Object business key already exists.");
        if (!string.IsNullOrWhiteSpace(r.AutomationId) && await repository.ObjectAutomationIdExistsAsync(projectId, r.ApplicationCode, r.AutomationId, id, ct)) throw new ArgumentException("AutomationId already exists.");
        entity.Update(r.ModuleId, r.ApplicationCode, r.ScreenCode, r.ObjectCode, r.ObjectName, r.ControlType, r.AutomationId, r.SelectorJson);
        await repository.SaveChangesAsync(ct);
        return (await repository.ListObjectsAsync(projectId, entity.ObjectCode, ct)).Single(x => x.AutomationObjectId == id);
    }

    public async Task<AutomationObjectImportResultDto> ImportObjectsAsync(ImportAutomationObjectsRequest r, CancellationToken ct)
    {
        if (r.ProjectId == Guid.Empty) throw new ArgumentException("Project is required.");
        if (r.Items.Count == 0) throw new ArgumentException("Import items are required.");
        if (r.Items.Count > 500) throw new ArgumentException("Import supports up to 500 objects per batch.");

        var rows = new List<AutomationObjectImportRowDto>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenAutomationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in r.Items)
        {
            var app = string.IsNullOrWhiteSpace(item.ApplicationCode) ? "Promaxx2" : item.ApplicationCode.Trim();
            var screen = string.IsNullOrWhiteSpace(item.ScreenCode) ? "Default" : item.ScreenCode.Trim();
            var code = item.ObjectCode?.Trim().ToUpperInvariant() ?? "";
            var key = $"{app}.{screen}.{code}";
            var automationId = item.AutomationId?.Trim();

            try
            {
                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(item.ObjectName) || string.IsNullOrWhiteSpace(item.ControlType))
                    throw new ArgumentException("Object code, object name and control type are required.");
                ValidateJson(item.SelectorJson, "Selector");

                if (!seenKeys.Add(key))
                {
                    rows.Add(new AutomationObjectImportRowDto(key, automationId, "Skipped", "Duplicate business key in import batch.", null));
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(automationId) && !seenAutomationIds.Add($"{app}.{automationId}"))
                {
                    rows.Add(new AutomationObjectImportRowDto(key, automationId, "Skipped", "Duplicate AutomationId in import batch.", null));
                    continue;
                }
                if (await repository.ObjectKeyExistsAsync(r.ProjectId, app, screen, code, null, ct))
                {
                    rows.Add(new AutomationObjectImportRowDto(key, automationId, "Skipped", "Business key already exists.", null));
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(automationId) && await repository.ObjectAutomationIdExistsAsync(r.ProjectId, app, automationId, null, ct))
                {
                    rows.Add(new AutomationObjectImportRowDto(key, automationId, "Skipped", "AutomationId already exists.", null));
                    continue;
                }

                var entity = new AutomationObject(r.ProjectId, item.ModuleId, app, screen, code, item.ObjectName, item.ControlType, automationId, item.SelectorJson);
                await repository.AddObjectAsync(entity, ct);
                rows.Add(new AutomationObjectImportRowDto(key, automationId, "Imported", "Imported.", null));
            }
            catch (ArgumentException ex)
            {
                rows.Add(new AutomationObjectImportRowDto(key, automationId, "Skipped", ex.Message, null));
            }
        }

        await repository.SaveChangesAsync(ct);
        var imported = rows.Count(x => x.Status == "Imported");
        return new AutomationObjectImportResultDto(imported, rows.Count - imported, rows);
    }
    public async Task<AutomationObjectDto> SetObjectActiveAsync(Guid id, Guid projectId, bool active, CancellationToken ct)
    {
        var entity = await repository.FindObjectAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Object not found.");
        entity.SetActive(active);
        await repository.SaveChangesAsync(ct);
        return (await repository.ListObjectsAsync(projectId, entity.ObjectCode, ct)).Single(x => x.AutomationObjectId == id);
    }

    public async Task<IReadOnlyList<AutomationObjectVerificationDto>> RequestObjectVerificationAsync(Guid projectId, RequestObjectVerificationRequest r, Guid? userId, CancellationToken ct)
    {
        if (r.ObjectIds is null || r.ObjectIds.Count == 0) throw new ArgumentException("กรุณาเลือก Object อย่างน้อย 1 รายการ");
        var items = new List<AutomationObjectVerification>();
        foreach (var objectId in r.ObjectIds.Distinct())
        {
            var obj = await repository.FindObjectAsync(objectId, projectId, ct) ?? throw new EntityNotFoundException("Object not found.");
            var verification = new AutomationObjectVerification(obj.AutomationObjectId, r.AgentId, userId);
            items.Add(verification);
        }
        await repository.AddVerificationsAsync(items, ct);
        await repository.SaveChangesAsync(ct);
        return await repository.ListVerificationsAsync(projectId, null, ct);
    }

    public Task<IReadOnlyList<AutomationObjectVerificationDto>> ListVerificationsAsync(Guid projectId, Guid? objectId, CancellationToken ct)
        => repository.ListVerificationsAsync(projectId, objectId, ct);

    public async Task<AutomationCaseDto> AssignMaintenanceOwnerAsync(Guid caseId, Guid projectId, Guid ownerUserId, CancellationToken ct)
    {
        var caseEntity = await repository.FindCaseAsync(caseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
        caseEntity.AssignMaintenanceOwner(ownerUserId);
        await repository.SaveChangesAsync(ct);
        return await repository.GetCaseAsync(caseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
    }

    public async Task<AutomationCaseDto> ResolveMaintenanceAsync(Guid caseId, Guid projectId, string? resolutionNote, Guid? userId, CancellationToken ct)
    {
        var caseEntity = await repository.FindCaseAsync(caseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
        caseEntity.ResolveMaintenance(userId);
        if (!string.IsNullOrWhiteSpace(resolutionNote))
        {
            var latest = (await repository.ListVersionsAsync(caseId, ct)).OrderByDescending(x => x.VersionNo).FirstOrDefault();
            if (latest is not null)
            {
                var version = await repository.FindVersionAsync(latest.AutomationVersionId, ct);
                version?.RecordChangeReason($"Maintenance resolved: {resolutionNote.Trim()}");
            }
        }
        await repository.SaveChangesAsync(ct);
        return await repository.GetCaseAsync(caseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
    }

    public Task<IReadOnlyList<FlakyCandidateDto>> GetFlakyCandidatesAsync(Guid projectId, CancellationToken ct)
        => repository.GetFlakyCandidatesAsync(projectId, 5, ct);

    public async Task<AutomationCaseDto> QuarantineCaseAsync(Guid caseId, Guid projectId, QuarantineCaseRequest r, CancellationToken ct)
    {
        var caseEntity = await repository.FindCaseAsync(caseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
        caseEntity.Quarantine(r.Reason, r.OwnerUserId, r.ExpiresAt);
        await repository.SaveChangesAsync(ct);
        return await repository.GetCaseAsync(caseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
    }

    public async Task<AutomationCaseDto> UnquarantineCaseAsync(Guid caseId, Guid projectId, CancellationToken ct)
    {
        var caseEntity = await repository.FindCaseAsync(caseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
        caseEntity.Unquarantine();
        await repository.SaveChangesAsync(ct);
        return await repository.GetCaseAsync(caseId, projectId, ct) ?? throw new EntityNotFoundException("Automation case not found.");
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

    private static void ValidateJson(string? json, string fieldName)
    {
        try { using var _ = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); }
        catch (JsonException ex) { throw new ArgumentException($"{fieldName} must be valid JSON.", ex); }
    }
}

public sealed class AutomationAgentService(IAutomationRepository repository, IAutomationSuiteRepository suiteRepository, IAutomationScheduleRepository scheduleRepository)
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
        // AUT-P2-004: record this check-in in the (capped) heartbeat history — staged, not saved separately, so it
        // commits in the same SaveChangesAsync call below as the agent entity itself.
        await repository.RecordHeartbeatEventAsync(agent.AgentId, agent.Status, agent.CurrentExecutionId, ct);
        await repository.SaveChangesAsync(ct);
        return (await repository.ListAgentsAsync(ct)).Single(x => x.AgentId == agent.AgentId);
    }

    public async Task<AutomationAgentDto> HeartbeatAsync(AgentHeartbeatRequest r, CancellationToken ct)
    {
        var agent = await repository.FindAgentByCodeAsync(r.AgentCode, ct) ?? throw new EntityNotFoundException("Agent not registered. Register the agent first.");
        agent.Heartbeat(DateTime.UtcNow, r.CurrentExecutionId);
        if (string.Equals(r.Status, "Busy", StringComparison.OrdinalIgnoreCase)) agent.SetStatus("Busy");
        else if (string.Equals(r.Status, "Idle", StringComparison.OrdinalIgnoreCase)) agent.SetStatus("Idle");
        // AUT-P2-004: see RegisterAsync — same capped heartbeat history, same single SaveChangesAsync commit.
        await repository.RecordHeartbeatEventAsync(agent.AgentId, agent.Status, agent.CurrentExecutionId, ct);
        await repository.SaveChangesAsync(ct);
        return (await repository.ListAgentsAsync(ct)).Single(x => x.AgentId == agent.AgentId);
    }

    /// <summary>AUT-P2-004.</summary>
    public Task<AutomationAgentWorkloadDto> GetAgentWorkloadAsync(Guid agentId, DateTime? from, DateTime? to, CancellationToken ct)
        => repository.GetAgentWorkloadAsync(agentId, from, to, ct);

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
        if (caseEntity.IsQuarantined) throw new ArgumentException("This case is quarantined and cannot be executed until it is unquarantined.");
        var versionId = r.VersionId;
        if (versionId == Guid.Empty)
        {
            var versions = await repository.ListVersionsAsync(r.CaseId, ct);
            var approved = versions.OrderByDescending(x => x.VersionNo).FirstOrDefault(x => x.ApprovedAt.HasValue && x.ValidationStatus == "Valid");
            if (approved is null) throw new ArgumentException("No approved automation version exists for this case.");
            versionId = approved.AutomationVersionId;
        }
        var version = await repository.FindVersionAsync(versionId, ct) ?? throw new ArgumentException("Automation version not found.");
        var validation = AutomationValidator.Validate(version.ToDsl() ?? new DslDocument(), await repository.ListActionCodesAsync(ct), await repository.ListObjectKeysAsync(projectId, ct), null);
        if (!validation.IsValid) throw new ArgumentException($"Automation Case ต้อง Validate ใหม่ก่อนรัน: {string.Join("; ", validation.Errors)}");
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
            if (caseEntity is null || caseEntity.Status != "Ready" || caseEntity.IsQuarantined)
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

    /// <summary>AUT-P1-004: run an existing Suite's cases against a (possibly new) Build/Environment without
    /// re-selecting cases — just reuses BatchRunAsync with the suite's current case membership. Ready/quarantine
    /// filtering, version resolution and skip-reporting are therefore identical to a manual batch run.</summary>
    public async Task<BatchRunResultDto> RunSuiteAsync(Guid projectId, RunSuiteRequest r, Guid? userId, CancellationToken ct)
    {
        var suite = await suiteRepository.GetSuiteAsync(r.AutomationSuiteId, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
        if (!suite.IsActive) throw new InvalidOperationException("Cannot run a closed suite. Reopen it first.");
        if (suite.Cases.Count == 0) throw new ArgumentException("Suite นี้ยังไม่มี Automation Case — เพิ่ม Case ก่อนรัน");
        var caseIds = suite.Cases.Select(c => c.AutomationCaseId).ToList();
        return await BatchRunAsync(projectId, new BatchRunRequest(caseIds, r.BuildId, r.EnvironmentId, r.AgentId, r.Priority), userId, ct);
    }

    /// <summary>AUT-P1-006: polled by <c>AutomationScheduleWorker</c> (a BackgroundService in the Api process). Atomically
    /// claims every Automation Schedule whose NextRunAtUtc has arrived — see
    /// <see cref="IAutomationScheduleRepository.ClaimDueSchedulesAsync"/> for how the claim itself makes each fire
    /// exactly-once — then actually runs each claimed suite and records an audit row regardless of outcome. One
    /// schedule's suite being closed/deleted/empty must not stop the rest from firing, so failures are caught per
    /// schedule rather than aborting the batch.</summary>
    public async Task FireDueSchedulesAsync(DateTime nowUtc, CancellationToken ct)
    {
        var due = await scheduleRepository.ClaimDueSchedulesAsync(nowUtc, ct);
        foreach (var schedule in due)
        {
            AutomationScheduleRun run;
            try
            {
                var result = await RunSuiteAsync(schedule.ProjectId, new RunSuiteRequest(schedule.AutomationSuiteId, schedule.BuildId, schedule.EnvironmentId, schedule.AgentId, schedule.Priority), null, ct);
                run = new AutomationScheduleRun(schedule.AutomationScheduleId, nowUtc, result.Created.Count > 0 ? "Succeeded" : "NoReadyCases", result.Created.Count, result.SkippedCodes.Count, null);
                await NotifyScheduleFiredAsync(schedule, result.Created, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Suite closed/deleted or had zero cases entirely — the schedule itself is already claimed and
                // advanced (or deactivated, for Once) so it won't spin retrying every tick; just make the failure visible.
                run = new AutomationScheduleRun(schedule.AutomationScheduleId, nowUtc, "Failed", 0, 0, ex.Message);
            }
            await scheduleRepository.AddScheduleRunAsync(run, ct);
            await scheduleRepository.SaveChangesAsync(ct);
        }
    }

    /// <summary>AUT-P1-009: "Started" notification for every execution the fire actually created, plus a "NoAgent"
    /// notification (linked to the first created execution) when no enabled automation agent currently exists at
    /// all — the newly queued job(s) will sit unclaimed until one registers. Scoped to "no agent exists" rather than
    /// "no agent matches this case's capability" because job claiming does not actually filter by capability today
    /// (only by TargetApp, and only when it isn't the WindowsUI default — see AUT-TEST-006 notes), so a
    /// capability-based check here would claim a precision the runtime doesn't have.</summary>
    private async Task NotifyScheduleFiredAsync(DueScheduleDto schedule, IReadOnlyList<AutomationExecutionDto> created, CancellationToken ct)
    {
        if (created.Count == 0) return;
        foreach (var execution in created)
            await scheduleRepository.AddNotificationAsync(new AutomationScheduleNotification(schedule.ProjectId, schedule.AutomationScheduleId, execution.AutomationExecutionId,
                "Started", $"Schedule '{schedule.Name}' started execution {execution.AutomationCode}."), ct);

        var agents = await repository.ListAgentsAsync(ct);
        if (!agents.Any(a => a.IsEnabled))
            await scheduleRepository.AddNotificationAsync(new AutomationScheduleNotification(schedule.ProjectId, schedule.AutomationScheduleId, created[0].AutomationExecutionId,
                "NoAgent", $"Schedule '{schedule.Name}' fired but no active automation agent is available to run it."), ct);
    }

    public Task<AutomationDashboardDto> GetDashboardAsync(Guid projectId, CancellationToken ct) => repository.GetDashboardAsync(projectId, ct);

    public Task<IReadOnlyList<AutomationJobDto>> ListJobsAsync(Guid? projectId, Guid? buildId, int take, CancellationToken ct) => repository.ListJobsAsync(projectId, buildId, Math.Clamp(take, 1, 200), ct);
    public Task<IReadOnlyList<AutomationExecutionDto>> ListExecutionsAsync(Guid projectId, Guid? buildId, int take, CancellationToken ct) => repository.ListExecutionsAsync(projectId, buildId, Math.Clamp(take, 1, 200), ct);

    /// <summary>AUT-P2-001.</summary>
    public Task<PagedResult<AutomationJobDto>> ListJobsPagedAsync(Guid? projectId, Guid? buildId, string? status, string? sortBy, int page, int size, CancellationToken ct)
        => repository.ListJobsPagedAsync(projectId, buildId, status, sortBy, page, size, ct);
    /// <summary>AUT-P2-001/AUT-P2-002.</summary>
    public Task<PagedResult<AutomationExecutionDto>> ListExecutionsPagedAsync(Guid projectId, Guid? buildId, Guid? environmentId, Guid? agentId, string? targetApp, string? status, string? failureType,
        DateTime? from, DateTime? to, string? search, string? sortBy, int page, int size, CancellationToken ct)
        => repository.ListExecutionsPagedAsync(projectId, buildId, environmentId, agentId, targetApp, status, failureType, from, to, search, sortBy, page, size, ct);

    /// <summary>AUT-P2-003.</summary>
    public Task<ExecutionTrendDto> GetExecutionTrendAsync(Guid projectId, string? groupBy, DateTime? from, DateTime? to, Guid? releaseId, CancellationToken ct)
        => repository.GetExecutionTrendAsync(projectId, groupBy, from, to, releaseId, ct);

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
        var executionProjectId = execution.AutomationCase.TestCase.ProjectId;
        if (execution.Status is not ("Queued" or "Running"))
        {
            // Late/duplicate result: the agent reported after the execution was already cancelled or completed
            // (e.g. lease expired and the job was reassigned, or a duplicate report was retried in transit).
            // Ignore idempotently instead of corrupting a state that has already moved on.
            return await repository.GetExecutionAsync(executionId, executionProjectId, ct) ?? throw new EntityNotFoundException("Execution not found.");
        }
        execution.Complete(r.Status, r.FailureType, r.ErrorCode, r.ErrorMessage, DateTime.UtcNow);

        // AUT-P1-009: only executions the schedule worker itself created carry a "Started" notification — an
        // auto-retry child created below does not, so it deliberately gets no Completed/Failed notification of its own.
        var started = await scheduleRepository.FindStartedNotificationByExecutionAsync(executionId, ct);
        if (started is not null)
        {
            var eventType = r.Status == "Passed" ? "Completed" : "Failed";
            var message = eventType == "Completed"
                ? $"Execution {execution.AutomationCase.AutomationCode} completed successfully."
                : $"Execution {execution.AutomationCase.AutomationCode} failed ({r.Status}: {r.ErrorMessage ?? r.ErrorCode ?? "unknown error"}).";
            await scheduleRepository.AddNotificationAsync(new AutomationScheduleNotification(started.ProjectId, started.AutomationScheduleId, executionId, eventType, message), ct);
        }

        var job = await repository.FindJobByExecutionAsync(executionId, ct);
        try
        {
            if (job is not null) job.Complete(r.Status, r.ErrorMessage);
        }
        catch (InvalidOperationException)
        {
            // The job was already completed by a concurrent duplicate/racing report that landed between our
            // FindExecutionAsync and FindJobByExecutionAsync reads above — that request's write is authoritative.
            // Treat this one idempotently instead of surfacing a confusing error to the agent.
            return await repository.GetExecutionAsync(executionId, executionProjectId, ct) ?? throw new EntityNotFoundException("Execution not found.");
        }
        var projectId = executionProjectId;
        var caseEntity = await repository.FindCaseByIdAsync(execution.AutomationCaseId, ct);
        await repository.SaveChangesAsync(ct); // flush Status/ErrorCode before re-reading for classification

        var retried = false;
        AutomationFailureClassificationDto? classification = null;
        if (r.Status is "Failed" or "Timeout" or "AgentLost")
        {
            var dtoForClassification = await repository.GetExecutionAsync(executionId, projectId, ct) ?? throw new EntityNotFoundException("Execution not found.");
            classification = AutomationFailureClassifier.Classify(dtoForClassification);
            execution.SetClassification(classification.FailureType, classification.Recommendation);

            var policy = await repository.GetRetryPolicyAsync(ct);
            var retryable = classification.Recommendation is "Retry" or "RetryOrCheckEnvironment";
            var executedActionCodes = execution.StepResults.Where(s => s.Status == "Pass").Select(s => s.ActionCode).Distinct().ToList();
            var unsafeExecuted = executedActionCodes.Count > 0 && (await repository.GetUnsafeActionCodesAsync(executedActionCodes, ct)).Count > 0;

            if (policy.Enabled && retryable && !unsafeExecuted && execution.RetryCount < policy.MaxAttempts && caseEntity is not null && !caseEntity.IsQuarantined)
            {
                var retryExecution = new AutomationExecution(execution.AutomationCaseId, execution.AutomationVersionId, null, execution.BuildId, execution.EnvironmentId, "system:auto-retry", execution.TargetApp);
                retryExecution.MarkAsRetry(executionId, execution.RetryCount + 1);
                await repository.AddExecutionAsync(retryExecution, ct);
                await repository.SaveChangesAsync(ct);
                var retryJob = new AutomationJob(retryExecution.AutomationExecutionId, null, 5, DateTime.UtcNow.AddSeconds(policy.BackoffSeconds));
                await repository.AddJobAsync(retryJob, ct);
                retryExecution.LinkJob(retryJob.JobId);
                retried = true;
            }
        }

        if (caseEntity is not null && !retried)
        {
            // Use the same classifier the retry decision above relies on — it already knows AUT-DSL-001/AUT-AI-001
            // also need maintenance, not just the three AUT-UI-* codes, and it now runs for Timeout/AgentLost too.
            if (classification?.Recommendation == "MaintenanceRequired")
                caseEntity.RequireMaintenance(execution.ErrorMessage, null);
            else
                caseEntity.ChangeStatus("Ready");
        }
        await repository.SaveChangesAsync(ct);
        return await repository.GetExecutionAsync(executionId, projectId, ct) ?? throw new EntityNotFoundException("Execution not found.");
    }

    public async Task<VerificationBatchPackageDto?> ClaimVerificationBatchAsync(string agentCode, CancellationToken ct)
        => await repository.ClaimVerificationBatchAsync(agentCode, ct);

    public async Task ReportVerificationResultAsync(ReportVerificationResultRequest r, CancellationToken ct)
    {
        var verification = await repository.FindVerificationAsync(r.VerificationId, ct) ?? throw new EntityNotFoundException("Verification not found.");
        if (verification.Status is not ("Pending" or "Assigned")) return; // late/duplicate report — already completed, ignore idempotently
        verification.Complete(r.Status, r.ActualControlType, r.ActualAutomationId, r.Message);
        await repository.SaveChangesAsync(ct);
    }

    public Task<FailureBreakdownDto> GetFailureBreakdownAsync(Guid projectId, DateTime? from, DateTime? to, Guid? buildId, Guid? agentId, string? failureType, CancellationToken ct)
        => repository.GetFailureBreakdownAsync(projectId, from, to, buildId, agentId, failureType, ct);

    public Task<IReadOnlyList<AutomationExecutionDto>> ListFailedExecutionsAsync(Guid projectId, DateTime? from, DateTime? to, Guid? buildId, Guid? agentId, string? failureType, int take, CancellationToken ct)
        => repository.ListFailedExecutionsAsync(projectId, from, to, buildId, agentId, failureType, Math.Clamp(take, 1, 500), ct);

    public Task<RetryPolicyDto> GetRetryPolicyAsync(CancellationToken ct) => repository.GetRetryPolicyAsync(ct);

    public async Task<RetryPolicyDto> UpdateRetryPolicyAsync(UpdateRetryPolicyRequest r, Guid? userId, CancellationToken ct)
    {
        await repository.UpdateRetryPolicyAsync(r.MaxAttempts, r.BackoffSeconds, r.Enabled, userId, ct);
        return await repository.GetRetryPolicyAsync(ct);
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
