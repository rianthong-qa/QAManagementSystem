namespace ProMaxx2.QA.Application.Execution;

public sealed record AutoAssignPreviewRequest(IReadOnlyList<Guid> TestCycleCaseIds, IReadOnlyList<Guid> QaPoolUserIds, int WorkloadThresholdPercent = 100);
public sealed record AutoAssignOverride(Guid TestCycleCaseId, Guid TesterUserId, string OverrideReason);
public sealed record AutoAssignConfirmRequest(Guid PreviewId, string PreviewVersion, IReadOnlyList<AutoAssignOverride> Assignments);
public sealed record AutoRebalanceRequest(IReadOnlyList<AutoAssignOverride> Assignments, string Reason);
public sealed record AutoAssignPreviewDto(Guid PreviewId, string PreviewVersion, DateTime ExpiresAt, IReadOnlyList<WeightedAssignmentSuggestion> Assignments, IReadOnlyList<string> Warnings, IReadOnlyList<AutoAssignTesterOption>? Testers = null);
public sealed record AutoAssignTesterOption(Guid UserId, string DisplayName);

public static class WeightedAssignmentWorkflow
{
    public static void ValidateConfirm(AutoAssignConfirmRequest request, AssignmentPreviewState preview, DateTime utcNow)
    {
        if (preview.PreviewId != request.PreviewId || !string.Equals(preview.Version, request.PreviewVersion, StringComparison.Ordinal)) throw new InvalidOperationException("AUTOASSIGN_ASSIGNMENT_CONFLICT");
        if (preview.ExpiresAt <= utcNow) throw new InvalidOperationException("AUTOASSIGN_PREVIEW_EXPIRED");
        foreach (var item in request.Assignments) if (item.TesterUserId == Guid.Empty || string.IsNullOrWhiteSpace(item.OverrideReason)) throw new ArgumentException("OverrideReason is required for manual overrides.");
    }
}

public sealed record AssignmentPreviewState(Guid PreviewId, string Version, DateTime ExpiresAt);
