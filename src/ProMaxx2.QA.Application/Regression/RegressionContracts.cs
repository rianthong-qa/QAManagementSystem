namespace ProMaxx2.QA.Application.Regression;

public sealed record RegressionImpactRequest(
    Guid BuildId,
    IReadOnlyList<Guid> ChangedModuleIds,
    bool IncludeSharedDependencies = true,
    string MinimumPriority = "P1",
    bool DatabaseChange = false,
    bool ApiChange = false,
    bool CalculationChange = false,
    bool PermissionChange = false,
    bool InstallerChange = false,
    bool DefectFix = false,
    string? SharedComponents = null,
    string? ChangeNotes = null,
    int Page = 1,
    int PageSize = 50,
    int DirectImpactWeight = 40,
    int HistoricalDefectWeight = 30,
    int CriticalPriorityWeight = 20,
    int SharedDependencyWeight = 10,
    bool RecordAnalysis = true,
    bool IncludeAllCaseIds = false);

public sealed record RegressionCaseDto(
    Guid TestCaseId,
    string TestCaseCode,
    string Title,
    Guid ModuleId,
    string ModuleName,
    string Priority,
    string? TestType,
    int RevisionNo,
    string Status,
    string? LastResult,
    string ImpactType,
    string Reason,
    bool IsRequired,
    int RiskScore);

public sealed record RegressionMetricsDto(
    int ImpactedModules,
    int RecommendedCases,
    int RegressionCycles,
    int TotalCycleCases,
    int ExecutedCases,
    int PassedCases,
    int FailedCases,
    decimal ProgressPercent,
    decimal PassRate,
    int OpenDefects,
    string OverallStatus);

public sealed record RegressionImpactDto(
    Guid ReleaseId,
    Guid BuildId,
    RegressionMetricsDto Metrics,
    IReadOnlyList<RegressionCaseDto> Cases,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<Guid> AllCaseIds);

public sealed record GenerateRegressionSuiteRequest(
    Guid ReleaseId,
    string SuiteName,
    string? Description,
    string? RiskTier,
    IReadOnlyList<Guid> TestCaseIds);

public sealed record RegressionSuiteResultDto(Guid TestSuiteId, string SuiteCode, string SuiteName, int CaseCount);
public sealed record AddImpactCasesRequest(IReadOnlyList<Guid> TestCaseIds, bool AutoAssignPreview = false);
public sealed record RegressionHistoryDto(Guid RegressionAnalysisId,Guid ReleaseId,Guid BuildId,string BuildNumber,int ImpactedModules,int RecommendedCases,string MinimumPriority,string?ChangeNotes,Guid?AnalyzedBy,string?AnalyzedByName,DateTime AnalyzedAt);
public sealed record RegressionBuildMetricsDto(Guid BuildId,string BuildNumber,int TotalCases,int ExecutedCases,int PassedCases,int FailedCases,int BlockedCases,int NotRunCases,decimal PassRate);
public sealed record RegressionBaselineDto(RegressionBuildMetricsDto Baseline,RegressionBuildMetricsDto Target,int ExecutedDelta,int PassedDelta,int FailedDelta,decimal PassRateDelta);
public sealed record RegressionActivityDto(Guid RegressionActivityId,Guid ReleaseId,Guid?BuildId,string Action,string?Details,Guid?ActorUserId,string?ActorName,DateTime CreatedAt);
public sealed record RegressionProfileDto(Guid RegressionProfileId,Guid ProjectId,string Name,string Visibility,Guid?OwnerUserId,string?OwnerName,string SettingsJson,bool IsOwner,DateTime CreatedAt);
public sealed record SaveRegressionProfileRequest(Guid ProjectId,string Name,string Visibility,string SettingsJson);
public sealed record UpdateRegressionProfileRequest(string Name,string Visibility,string SettingsJson);
public sealed record RegressionScheduleDto(Guid RegressionScheduleId,Guid ProjectId,Guid ReleaseId,Guid?RegressionProfileId,string Name,bool IsActive,DateTime CreatedAt);
public sealed record SaveRegressionScheduleRequest(Guid ReleaseId,Guid?RegressionProfileId,string Name);
public sealed record RegressionNotificationDto(Guid RegressionScheduleId,Guid BuildId,string BuildNumber,string ScheduleName,string Message,DateTime CreatedAt);
