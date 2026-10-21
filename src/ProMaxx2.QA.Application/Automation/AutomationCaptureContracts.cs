namespace ProMaxx2.QA.Application.Automation;

public sealed record CaptureItemRequest(int StepNo, string EventType, string Action, string? TestData, string ExpectedResult, string ScreenCode, string ObjectCode, string ObjectName, string ControlType, string? AutomationId, string SelectorJson, bool Sensitive = false);
public sealed record CreateCaptureSessionRequest(Guid ProjectId, Guid ModuleId, Guid TestCaseId, string ApplicationCode, string? ApplicationVersion, string SourceMachine, IReadOnlyList<CaptureItemRequest> Items);
public sealed record CaptureObjectPreviewDto(int StepNo, string ObjectCode, string? AutomationId, string Status, string Message);
public sealed record CaptureSessionDto(Guid SessionId, string Status, IReadOnlyList<CaptureObjectPreviewDto> Items);
public sealed record CaptureCommitResultDto(Guid SessionId, Guid TestCaseId, int RevisionNo, int CreatedObjects, int MatchedObjects, IReadOnlyList<CaptureObjectPreviewDto> Items);
