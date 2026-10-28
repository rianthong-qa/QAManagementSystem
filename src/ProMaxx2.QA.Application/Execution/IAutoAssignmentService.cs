namespace ProMaxx2.QA.Application.Execution;

/// <summary>Shared orchestration boundary for Auto Assign and Regression callers.</summary>
public interface IAutoAssignmentService
{
    Task<AutoAssignPreviewDto> PreviewAsync(Guid cycleId, AutoAssignPreviewRequest request, Guid actorUserId, CancellationToken ct);
    Task ConfirmAsync(Guid cycleId, AutoAssignConfirmRequest request, Guid actorUserId, CancellationToken ct);
    Task RebalanceAsync(Guid cycleId, AutoRebalanceRequest request, Guid actorUserId, CancellationToken ct);
}
