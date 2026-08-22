using System.Text.Json;

namespace Promaxx2.Automation.Hub;

// DTOs mirror QA Hub API contracts (camelCase JSON)

public sealed record LoginRequest(string Username, string Password);

public sealed record AuthenticatedUser(
    Guid UserId,
    string Username,
    string DisplayName,
    string? Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<Guid> AssignedProjectIds);

public sealed record LoginResponse(string AccessToken, int ExpiresIn, AuthenticatedUser User);

public sealed record ProjectDto(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string? Description,
    string Status,
    Guid? OwnerUserId,
    bool IsActive,
    DateTime CreatedAt);

public sealed record ModuleDto(
    Guid ModuleId,
    Guid ProjectId,
    Guid? ParentModuleId,
    string ModuleCode,
    string ModuleName,
    string? Description,
    Guid? OwnerUserId,
    bool IsActive,
    int SortOrder);

public sealed record StepDto(int StepNo, string Action, string? TestData, string ExpectedResult);

public sealed record TestCaseListDto(
    Guid TestCaseId,
    Guid ProjectId,
    Guid ModuleId,
    string TestCaseCode,
    string Title,
    string Priority,
    string? TestType,
    bool AutomationCandidate,
    string Status,
    int RevisionNo,
    Guid? OwnerUserId,
    int StepCount);

public sealed record TestCaseDto(
    Guid TestCaseId,
    Guid ProjectId,
    Guid ModuleId,
    string TestCaseCode,
    string Title,
    string? Objective,
    string? Preconditions,
    string Priority,
    string? TestType,
    bool AutomationCandidate,
    string Status,
    int RevisionNo,
    Guid? OwnerUserId,
    IReadOnlyList<StepDto> Steps,
    string? AutomationTarget=null);

public sealed record PagedResult<T>(int Total, IReadOnlyList<T> Rows);

public sealed record PublishAutomationCaseResult(
    Guid? TestCaseId,
    string TestCaseCode,
    string Status,
    long DurationMs,
    string? ErrorMessage,
    string? EvidencePath);

public sealed record PublishAutomationRunRequest(
    Guid ProjectId,
    Guid? ReleaseId,
    Guid? BuildId,
    Guid? TestCycleId,
    string TargetApp,
    string? RunnerName,
    DateTime StartedAt,
    IReadOnlyList<PublishAutomationCaseResult> Results);

public sealed record PublishedAutomationRun(
    Guid AutomationRunId,
    string TargetApp,
    string Status,
    int TotalCount,
    int PassedCount,
    int FailedCount,
    int SkippedCount,
    IReadOnlyList<PublishedAutomationCase> Results);

public sealed record PublishedAutomationCase(Guid AutomationRunCaseId,string TestCaseCode,string Status,string?EvidencePath);
public sealed record AutomationQueueJob(Guid AutomationQueueJobId,Guid ProjectId,Guid ReleaseId,Guid BuildId,Guid?TestCycleId,string TargetApp,string Pack,int MaxAttempts,string Status,string?RunnerName,string?LeaseToken);
public sealed record ClaimAutomationQueueJobRequest(string RunnerName,IReadOnlyList<string>TargetApps);
public sealed record UpdateAutomationQueueJobRequest(string LeaseToken,string Status,string?ErrorMessage,Guid?AutomationRunId,string?ErrorType);
public sealed record AutomationRunnerHeartbeatRequest(string RunnerName,string MachineName,string Version,IReadOnlyList<string>Capabilities,string State,Guid?CurrentJobId,string?LeaseToken);

public static class Json
{
    public static readonly JsonSerializerOptions ApiOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}
