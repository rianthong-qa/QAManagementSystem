using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.Application.Automation;

public sealed record AutomationEnvironmentDataProfileDto(Guid AutomationEnvironmentDataProfileId, Guid ProjectId, Guid EnvironmentId, string EnvironmentName, string DbKind, string? Notes,
    Guid? CreatedBy, DateTime CreatedAt, DateTime? UpdatedAt);

public sealed record CreateEnvironmentDataProfileRequest(Guid EnvironmentId, string DbKind, string? Notes);
public sealed record UpdateEnvironmentDataProfileRequest(string DbKind, string? Notes);

public interface IAutomationEnvironmentDataProfileRepository
{
    Task<IReadOnlyList<AutomationEnvironmentDataProfileDto>> ListEnvironmentDataProfilesAsync(Guid projectId, CancellationToken ct);
    Task<AutomationEnvironmentDataProfileDto?> GetEnvironmentDataProfileAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<AutomationEnvironmentDataProfile?> FindEnvironmentDataProfileAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<bool> EnvironmentDataProfileExistsForEnvironmentAsync(Guid environmentId, CancellationToken ct);
    Task<bool> EnvironmentExistsAsync(Guid environmentId, Guid projectId, CancellationToken ct);
    Task AddEnvironmentDataProfileAsync(AutomationEnvironmentDataProfile entity, CancellationToken ct);

    /// <summary>AUT-DATA-006: used by <c>AutomationDataSeedService.RequestRunAsync</c>/
    /// <c>AutomationDataRestoreService.RequestAsync</c> to cross-check a script's/snapshot's DbKind against the
    /// Environment it targets before creating a run/restore request. Null when the Environment has no profile yet —
    /// the check is then skipped (opt-in, not retroactively enforced on Environments nobody has profiled).</summary>
    Task<string?> GetDataProfileDbKindForEnvironmentAsync(Guid environmentId, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>AUT-DATA-006: per-Environment, non-secret "what kind of database" metadata — see class summary on
/// <see cref="AutomationEnvironmentDataProfile"/> for the full rationale, especially why secrets are never involved
/// here at all.</summary>
public sealed class AutomationEnvironmentDataProfileService(IAutomationEnvironmentDataProfileRepository repository)
{
    public Task<IReadOnlyList<AutomationEnvironmentDataProfileDto>> ListAsync(Guid projectId, CancellationToken ct)
        => repository.ListEnvironmentDataProfilesAsync(projectId, ct);

    public async Task<AutomationEnvironmentDataProfileDto> GetAsync(Guid id, Guid projectId, CancellationToken ct)
        => await repository.GetEnvironmentDataProfileAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Environment data profile not found.");

    public async Task<AutomationEnvironmentDataProfileDto> CreateAsync(Guid projectId, CreateEnvironmentDataProfileRequest r, Guid? userId, CancellationToken ct)
    {
        if (!await repository.EnvironmentExistsAsync(r.EnvironmentId, projectId, ct)) throw new EntityNotFoundException("Environment not found.");
        if (await repository.EnvironmentDataProfileExistsForEnvironmentAsync(r.EnvironmentId, ct))
            throw new ArgumentException("This Environment already has a data profile. Edit the existing one instead of creating a second one.");
        var entity = new AutomationEnvironmentDataProfile(projectId, r.EnvironmentId, r.DbKind, r.Notes, userId);
        await repository.AddEnvironmentDataProfileAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        return await repository.GetEnvironmentDataProfileAsync(entity.AutomationEnvironmentDataProfileId, projectId, ct) ?? throw new EntityNotFoundException("Environment data profile not found.");
    }

    public async Task<AutomationEnvironmentDataProfileDto> UpdateAsync(Guid id, Guid projectId, UpdateEnvironmentDataProfileRequest r, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindEnvironmentDataProfileAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Environment data profile not found.");
        entity.Update(r.DbKind, r.Notes, userId);
        await repository.SaveChangesAsync(ct);
        return await repository.GetEnvironmentDataProfileAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Environment data profile not found.");
    }
}
