using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.Application.Automation;

public sealed record CreateAutomationCaseRequest(Guid TestCaseId, string AutomationType, Guid? OwnerUserId);
public sealed record AutomationCaseDto(Guid AutomationCaseId, Guid TestCaseId, string TestCaseCode, string TestCaseTitle, string AutomationCode, string AutomationType, string Status, int CurrentVersionNo, int VersionCount, Guid? OwnerUserId, string? OwnerName, bool IsAiGenerated, DateTime CreatedAt,
    string? MaintenanceReason = null, Guid? MaintenanceOwnerUserId = null, DateTime? MaintenanceOpenedAt = null,
    bool IsQuarantined = false, string? QuarantineReason = null, Guid? QuarantineOwnerUserId = null, DateTime? QuarantineExpiresAt = null);
public sealed record AutomationVersionDto(Guid AutomationVersionId, Guid AutomationCaseId, int VersionNo, int TestCaseRevisionNo, string DslVersion, string DslJson, bool GeneratedByAi, string? AiProvider, string? AiModel, double? AiConfidence, string ValidationStatus, string? ValidationErrors, Guid? ApprovedBy, DateTime? ApprovedAt, string? ChangeReason, DateTime CreatedAt);
public sealed record CreateAutomationVersionRequest(string DslJson, string? ChangeReason);

public sealed record CreateAutomationActionRequest(string ActionCode, string ActionName, string Category, string? Description, string ParameterSchemaJson, string? MinimumAgentVersion);
public sealed record UpdateAutomationActionRequest(string ActionName, string Category, string? Description, string ParameterSchemaJson, string HandlerKey, string? MinimumAgentVersion, bool IsActive, string? RetrySafety = null);
public sealed record AutomationActionDto(Guid AutomationActionId, string ActionCode, string ActionName, string Category, string? Description, string ParameterSchemaJson, string HandlerKey, string? MinimumAgentVersion, bool IsActive, string RetrySafety = "Unsafe");

public sealed record CreateAutomationObjectRequest(Guid ProjectId, Guid? ModuleId, string ApplicationCode, string ScreenCode, string ObjectCode, string ObjectName, string ControlType, string? AutomationId, string SelectorJson);
public sealed record UpdateAutomationObjectRequest(Guid? ModuleId, string ApplicationCode, string ScreenCode, string ObjectCode, string ObjectName, string ControlType, string? AutomationId, string SelectorJson);
public sealed record AutomationObjectDto(Guid AutomationObjectId, Guid ProjectId, Guid? ModuleId, string? ModuleCode, string? ModuleName, string ApplicationCode, string ScreenCode, string ObjectCode, string ObjectName, string ControlType, string? AutomationId, string SelectorJson, int ObjectVersion, bool IsActive);
public sealed record ImportAutomationObjectsRequest(Guid ProjectId, IReadOnlyList<AutomationObjectImportItem> Items);
public sealed record AutomationObjectImportItem(Guid? ModuleId, string ApplicationCode, string ScreenCode, string ObjectCode, string ObjectName, string ControlType, string? AutomationId, string SelectorJson);
public sealed record AutomationObjectImportRowDto(string BusinessKey, string? AutomationId, string Status, string Message, AutomationObjectDto? Object);
public sealed record AutomationObjectImportResultDto(int Imported, int Skipped, IReadOnlyList<AutomationObjectImportRowDto> Rows);

public sealed record RequestObjectVerificationRequest(IReadOnlyList<Guid> ObjectIds, Guid? AgentId);
public sealed record AutomationObjectVerificationDto(Guid AutomationObjectVerificationId, Guid AutomationObjectId, string ObjectCode, string ScreenCode, string? ExpectedAutomationId, string ExpectedControlType, string? ActualAutomationId, string? ActualControlType, string Status, Guid? AssignedAgentId, string? AssignedAgentCode, DateTime RequestedAt, DateTime? CompletedAt, string? Message);
public sealed record VerificationObjectItemDto(Guid VerificationId, string ObjectCode, string ApplicationCode, string ScreenCode, string? ExpectedAutomationId, string ExpectedControlType);
public sealed record VerificationBatchPackageDto(IReadOnlyList<VerificationObjectItemDto> Items);
public sealed record ClaimVerificationBatchRequest(string AgentCode);
public sealed record ReportVerificationResultRequest(Guid VerificationId, string Status, string? ActualAutomationId, string? ActualControlType, string? Message);

public sealed record AssignMaintenanceOwnerRequest(Guid OwnerUserId);
public sealed record ResolveMaintenanceRequest(string? ResolutionNote);

public sealed record CountByKeyDto(string Key, int Count);
public sealed record FailureBreakdownDto(int TotalFailed, IReadOnlyList<CountByKeyDto> ByFailureType, IReadOnlyList<CountByKeyDto> ByBuild, IReadOnlyList<CountByKeyDto> ByAgent, IReadOnlyList<CountByKeyDto> ByAutomationCase);

public sealed record RetryPolicyDto(int MaxAttempts, int BackoffSeconds, bool Enabled, DateTime? UpdatedAt);
public sealed record UpdateRetryPolicyRequest(int MaxAttempts, int BackoffSeconds, bool Enabled);

public sealed record FlakyCandidateDto(Guid AutomationCaseId, string AutomationCode, int RecentRuns, int Transitions, DateTime LastExecutedAt);

/// <summary>AUT-P2-003.</summary>
public sealed record ExecutionTrendBucketDto(string BucketKey, string BucketLabel, int Passed, int Failed, int Flaky, int Total);
public sealed record ExecutionTrendDto(string GroupBy, IReadOnlyList<ExecutionTrendBucketDto> Buckets);
public sealed record QuarantineCaseRequest(string Reason, Guid? OwnerUserId, DateTime? ExpiresAt);

public sealed record RegisterAgentRequest(string AgentCode, string MachineName, string AgentVersion, string OperatingSystem, string Architecture, IReadOnlyList<string> Capabilities);
public sealed record AgentHeartbeatRequest(string AgentCode, string MachineName, string AgentVersion, string Status, Guid? CurrentExecutionId);
public sealed record AutomationAgentDto(Guid AgentId, string AgentCode, string MachineName, string AgentVersion, string OperatingSystem, string Architecture, string Status, DateTime LastHeartbeatAt, Guid? CurrentExecutionId, DateTime RegisteredAt, bool IsEnabled, string Connectivity, IReadOnlyList<string> Capabilities);

public sealed record CreateAutomationExecutionRequest(Guid BuildId, Guid EnvironmentId, Guid? AgentId, int Priority);
public sealed record AutomationExecutionDto(Guid AutomationExecutionId, Guid AutomationCaseId, string AutomationCode, string TestCaseCode, string TestCaseTitle, Guid AutomationVersionId, int VersionNo, Guid? TestExecutionId, Guid? DefectId, string TargetApp, Guid? AgentId, string? AgentCode, Guid BuildId, string BuildNumber, Guid EnvironmentId, string EnvironmentName, Guid? JobId, string Status, DateTime? StartedAt, DateTime? CompletedAt, long? DurationMs, string? FailureType, string? ErrorCode, string? ErrorMessage, IReadOnlyList<AutomationStepResultDto> StepResults, IReadOnlyList<AutomationEvidenceDto> Evidence = null!,
    string? ClassifiedFailureType = null, string? ClassifiedRecommendation = null, Guid? RetryOfExecutionId = null, int RetryCount = 0);
public sealed record AutomationFailureClassificationDto(string FailureType, bool IsProductDefectCandidate, string Recommendation, string? Detail);
public sealed record CreateAutomationDefectRequest(string? Classification, string? Severity, string? Title, string? Description);
public sealed record AutomationStepResultDto(Guid AutomationStepResultId, int StepNo, string ActionCode, string Status, DateTime StartedAt, DateTime CompletedAt, long DurationMs, string? ActualResult, string? ErrorCode, string? ErrorMessage);
public sealed record AutomationEvidenceDto(Guid AutomationEvidenceId, int? StepNo, string EvidenceType, string FilePath, string? CapturedBy, DateTime CapturedAt);
public sealed record ReportStepResultRequest(int StepNo, string ActionCode, string Status, string? ActualResult, string? ErrorCode, string? ErrorMessage, string? EvidencePath, DateTime StartedAt, DateTime CompletedAt);
public sealed record CompleteExecutionRequest(string Status, string? FailureType, string? ErrorCode, string? ErrorMessage);
public sealed record RequestExecutionRequest(Guid CaseId, Guid VersionId, Guid BuildId, Guid EnvironmentId, Guid? AgentId, int Priority);
public sealed record BatchRunRequest(IReadOnlyList<Guid> CaseIds, Guid BuildId, Guid EnvironmentId, Guid? AgentId, int Priority);
public sealed record RunSuiteRequest(Guid AutomationSuiteId, Guid BuildId, Guid EnvironmentId, Guid? AgentId, int Priority);
public sealed record BatchRunResultDto(IReadOnlyList<AutomationExecutionDto> Created, IReadOnlyList<string> SkippedCodes, int Total);
public sealed record AutomationDashboardDto(int TotalTestCases, int AutomationCandidates, int AutomationCases, int Ready, int MaintenanceRequired, int NeedsReview, int InProgress, int Running, int PassToday, int FailToday, long? AverageDurationMs, int AgentsOnline, int AgentsTotal, decimal ReadyCoverage, decimal CandidateCoverage);

public sealed record ClaimJobRequest(string AgentCode, string AgentVersion, IReadOnlyList<string> Capabilities, string TargetApp = "WindowsUI");
public sealed record AutomationJobDto(Guid JobId, Guid AutomationExecutionId, int Priority, Guid? RequestedAgentId, Guid? AssignedAgentId, string? AssignedAgentCode, string Status, DateTime QueuedAt, DateTime? AssignedAt, DateTime? StartedAt, DateTime? CompletedAt, int RetryCount, string? LastError);
public sealed record AutomationJobPackageDto(Guid JobId, Guid AutomationExecutionId, Guid AutomationCaseId, string AutomationCode, Guid AutomationVersionId, int VersionNo, string DslVersion, string DslJson, Guid BuildId, string BuildNumber, Guid EnvironmentId, string EnvironmentName, IReadOnlyList<string> Actions, IReadOnlyList<AutomationObjectDto> Objects);

public interface IAutomationRepository
{
    Task<IReadOnlyList<AutomationCaseDto>> ListCasesAsync(Guid projectId, string? search, int take, CancellationToken ct);
    /// <summary>AUT-P2-001: real server-side page/size/filter/sort for the Automation Cases table — a sibling of
    /// <see cref="ListCasesAsync"/> rather than a replacement, since that one is also used internally (duplicate-code
    /// check in <c>AutomationCaseService.CreateAsync</c>) and by the shared cross-cutting page load (dashboard KPIs,
    /// batch-run/suite case pickers) that needs a flat "up to N" list, not a UI page.</summary>
    Task<PagedResult<AutomationCaseDto>> ListCasesPagedAsync(Guid projectId, string? search, string? status, string? automationTarget, string? sortBy, int page, int size, CancellationToken ct);
    Task<AutomationCaseDto?> GetCaseAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<AutomationCase?> FindCaseAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<AutomationCase?> FindCaseByIdAsync(Guid id, CancellationToken ct);
    Task<bool> CodeExistsAsync(Guid projectId, string code, CancellationToken ct);
    Task AddCaseAsync(AutomationCase entity, CancellationToken ct);
    Task<IReadOnlyList<AutomationVersionDto>> ListVersionsAsync(Guid caseId, CancellationToken ct);
    Task<AutomationVersion?> FindVersionAsync(Guid versionId, CancellationToken ct);
    Task AddVersionAsync(AutomationVersion entity, CancellationToken ct);

    Task<IReadOnlyList<AutomationActionDto>> ListActionsAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> ListActionCodesAsync(CancellationToken ct);
    Task<AutomationAction?> FindActionAsync(Guid id, CancellationToken ct);
    Task<AutomationAction?> FindActionByCodeAsync(string code, CancellationToken ct);
    Task AddActionAsync(AutomationAction entity, CancellationToken ct);

    Task<IReadOnlyList<AutomationObjectDto>> ListObjectsAsync(Guid projectId, string? search, CancellationToken ct);
    Task<IReadOnlyList<string>> ListObjectKeysAsync(Guid projectId, CancellationToken ct);
    Task<AutomationObject?> FindObjectAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<bool> ObjectKeyExistsAsync(Guid projectId, string applicationCode, string screenCode, string objectCode, Guid? excludeId, CancellationToken ct);
    Task<bool> ObjectAutomationIdExistsAsync(Guid projectId, string applicationCode, string automationId, Guid? excludeId, CancellationToken ct);
    Task AddObjectAsync(AutomationObject entity, CancellationToken ct);

    Task<AutomationAgent?> FindAgentByCodeAsync(string agentCode, CancellationToken ct);
    Task<IReadOnlyList<AutomationAgentDto>> ListAgentsAsync(CancellationToken ct);
    Task AddAgentAsync(AutomationAgent entity, CancellationToken ct);

    Task<AutomationExecutionDto?> GetExecutionAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<AutomationExecution?> FindExecutionAsync(Guid id, CancellationToken ct);
    Task AddExecutionAsync(AutomationExecution entity, CancellationToken ct);
    Task<AutomationJob?> FindJobByExecutionAsync(Guid executionId, CancellationToken ct);
    Task AddJobAsync(AutomationJob entity, CancellationToken ct);
    Task<AutomationJob?> FindJobAsync(Guid jobId, CancellationToken ct);
    Task<AutomationJobPackageDto?> ClaimNextJobAsync(string agentCode, string agentVersion, IReadOnlyList<string> capabilities, string targetApp, CancellationToken ct);
    Task<IReadOnlyList<AutomationJobDto>> ListJobsAsync(Guid? projectId, Guid? buildId, int take, CancellationToken ct);
    Task<IReadOnlyList<AutomationExecutionDto>> ListExecutionsAsync(Guid projectId, Guid? buildId, int take, CancellationToken ct);
    /// <summary>AUT-P2-001: real server-side page/size/filter/sort for the Job Queue table — sibling of
    /// <see cref="ListJobsAsync"/>, same rationale as <see cref="ListCasesPagedAsync"/>.</summary>
    Task<PagedResult<AutomationJobDto>> ListJobsPagedAsync(Guid? projectId, Guid? buildId, string? status, string? sortBy, int page, int size, CancellationToken ct);
    /// <summary>AUT-P2-001/AUT-P2-002: real server-side page/size/filter/sort for the Execution table — sibling of
    /// <see cref="ListExecutionsAsync"/>, same rationale as <see cref="ListCasesPagedAsync"/>. <paramref name="search"/>
    /// preserves the existing Run History "search by code/agent" UX that used to be client-side only.
    /// <paramref name="environmentId"/>/<paramref name="agentId"/>/<paramref name="targetApp"/>/<paramref name="failureType"/>/
    /// <paramref name="from"/>/<paramref name="to"/> are AUT-P2-002's advanced filters — <paramref name="failureType"/>
    /// filters on <c>ClassifiedFailureType</c>, matching the meaning already established by
    /// <see cref="ListFailedExecutionsAsync"/>/the Failure Dashboard, not the raw agent-reported <c>FailureType</c>.</summary>
    Task<PagedResult<AutomationExecutionDto>> ListExecutionsPagedAsync(Guid projectId, Guid? buildId, Guid? environmentId, Guid? agentId, string? targetApp, string? status, string? failureType,
        DateTime? from, DateTime? to, string? search, string? sortBy, int page, int size, CancellationToken ct);
    /// <summary>AUT-P2-003: Pass/Fail/Flaky trend, bucketed by day, Build, or Release. <paramref name="groupBy"/> is
    /// "day" (default)/"build"/"release". Defaults to the last 90 days when <paramref name="from"/>/<paramref name="to"/>
    /// are omitted — an unbounded full-table scan isn't needed for a trend chart. "Flaky" reuses the same status-
    /// transition concept as <see cref="GetFlakyCandidatesAsync"/> (a case's status differing from its immediately
    /// preceding execution), attributed to the bucket of the later of the two executions — not a separate metric.</summary>
    Task<ExecutionTrendDto> GetExecutionTrendAsync(Guid projectId, string? groupBy, DateTime? from, DateTime? to, Guid? releaseId, CancellationToken ct);
    Task<AutomationDashboardDto> GetDashboardAsync(Guid projectId, CancellationToken ct);
    Task AddStepResultAsync(AutomationStepResult entity, CancellationToken ct);
    Task<AutomationStepResult?> FindStepResultAsync(Guid stepResultId, Guid executionId, CancellationToken ct);
    Task AttachStepEvidenceAsync(Guid executionId, int stepNo, string path, CancellationToken ct);
    Task AddEvidenceAsync(AutomationEvidence entity, CancellationToken ct);
    Task<IReadOnlyList<AutomationEvidenceDto>> ListEvidenceAsync(Guid executionId, CancellationToken ct);
    Task<AutomationEvidence?> FindEvidenceAsync(Guid evidenceId, Guid executionId, CancellationToken ct);

    Task<IReadOnlyList<AutomationObjectVerificationDto>> ListVerificationsAsync(Guid projectId, Guid? objectId, CancellationToken ct);
    Task AddVerificationsAsync(IReadOnlyList<AutomationObjectVerification> items, CancellationToken ct);
    Task<VerificationBatchPackageDto?> ClaimVerificationBatchAsync(string agentCode, CancellationToken ct);
    Task<AutomationObjectVerification?> FindVerificationAsync(Guid id, CancellationToken ct);

    Task<FailureBreakdownDto> GetFailureBreakdownAsync(Guid projectId, DateTime? from, DateTime? to, Guid? buildId, Guid? agentId, string? failureType, CancellationToken ct);
    Task<IReadOnlyList<AutomationExecutionDto>> ListFailedExecutionsAsync(Guid projectId, DateTime? from, DateTime? to, Guid? buildId, Guid? agentId, string? failureType, int take, CancellationToken ct);

    Task<RetryPolicyDto> GetRetryPolicyAsync(CancellationToken ct);
    Task UpdateRetryPolicyAsync(int maxAttempts, int backoffSeconds, bool enabled, Guid? userId, CancellationToken ct);
    Task<IReadOnlyList<string>> GetUnsafeActionCodesAsync(IEnumerable<string> actionCodes, CancellationToken ct);

    Task<IReadOnlyList<FlakyCandidateDto>> GetFlakyCandidatesAsync(Guid projectId, int lookback, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
