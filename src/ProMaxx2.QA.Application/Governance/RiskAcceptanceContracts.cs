using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Governance;

namespace ProMaxx2.QA.Application.Governance;

public sealed record RiskAcceptanceDto(Guid RiskAcceptanceId, Guid ProjectId, Guid ReleaseId, Guid? DefectId, string RiskCode, string Title, string Issue, string Impact, string Probability, string RiskLevel, string Status, string? Workaround, string? TargetFix, string? QaRecommendation, Guid? OwnerUserId, string? OwnerName, string? ReleaseCode, string? ReleaseVersion, string? DefectCode, DateTime CreatedAt, DateTime? ReviewDate, string? ReviewComment, string? ReviewedByName);
public sealed record CreateRiskAcceptanceRequest(Guid ProjectId, Guid ReleaseId, Guid? DefectId, string Title, string Issue, string Impact, string Probability, string? Workaround, string? TargetFix, string? QaRecommendation, Guid? OwnerUserId);
public sealed record UpdateRiskAcceptanceRequest(string Title, string Issue, string Impact, string Probability, string? Workaround, string? TargetFix, string? QaRecommendation, Guid? OwnerUserId);
public sealed record RiskDecisionRequest(string? Comment);

public interface IRiskAcceptanceRepository
{
    Task<IReadOnlyList<RiskAcceptanceDto>> ListAsync(Guid? projectId, CancellationToken ct);
    Task<RiskAcceptanceDto?> GetAsync(Guid id, CancellationToken ct);
    Task<RiskAcceptance?> FindAsync(Guid id, CancellationToken ct);
    Task<bool> CodeExistsAsync(Guid projectId, string code, CancellationToken ct);
    Task<IReadOnlyList<string>> ListCodesAsync(Guid projectId, string prefix, CancellationToken ct);
    Task AddAsync(RiskAcceptance entity, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public sealed class RiskAcceptanceService(IRiskAcceptanceRepository repository, IProjectRepository projects)
{
    public Task<IReadOnlyList<RiskAcceptanceDto>> ListAsync(Guid? projectId, CancellationToken ct) => repository.ListAsync(projectId, ct);

    public async Task<RiskAcceptanceDto> GetAsync(Guid id, CancellationToken ct) => await repository.GetAsync(id, ct) ?? throw new EntityNotFoundException("Risk acceptance not found.");

    public async Task<RiskAcceptanceDto> CreateAsync(CreateRiskAcceptanceRequest r, Guid? userId, CancellationToken ct)
    {
        var code = await ResolveCodeAsync(r.ProjectId, ct);
        if (await repository.CodeExistsAsync(r.ProjectId, code, ct)) throw new DuplicateCodeException("Risk code already exists.");
        var e = new RiskAcceptance(r.ProjectId, r.ReleaseId, r.DefectId, r.Title, r.Issue, r.Impact, r.Probability, r.Workaround, r.TargetFix, r.QaRecommendation, r.OwnerUserId, userId);
        e.AssignCode(code);
        await repository.AddAsync(e, ct);
        await repository.SaveChangesAsync(ct);
        return (await repository.GetAsync(e.RiskAcceptanceId, ct))!;
    }

    public async Task<RiskAcceptanceDto> UpdateAsync(Guid id, UpdateRiskAcceptanceRequest r, Guid? userId, CancellationToken ct)
    {
        var e = await repository.FindAsync(id, ct) ?? throw new EntityNotFoundException("Risk acceptance not found.");
        e.Update(r.Title, r.Issue, r.Impact, r.Probability, r.Workaround, r.TargetFix, r.QaRecommendation, r.OwnerUserId, userId);
        await repository.SaveChangesAsync(ct);
        return (await repository.GetAsync(id, ct))!;
    }

    public async Task<RiskAcceptanceDto> SubmitAsync(Guid id, Guid? userId, CancellationToken ct) { var e = await repository.FindAsync(id, ct) ?? throw new EntityNotFoundException("Risk acceptance not found."); e.Submit(userId); await repository.SaveChangesAsync(ct); return (await repository.GetAsync(id, ct))!; }
    public async Task<RiskAcceptanceDto> ApproveAsync(Guid id, string? comment, Guid? userId, CancellationToken ct) { var e = await repository.FindAsync(id, ct) ?? throw new EntityNotFoundException("Risk acceptance not found."); e.Approve(comment, userId); await repository.SaveChangesAsync(ct); return (await repository.GetAsync(id, ct))!; }
    public async Task<RiskAcceptanceDto> RejectAsync(Guid id, string? comment, Guid? userId, CancellationToken ct) { var e = await repository.FindAsync(id, ct) ?? throw new EntityNotFoundException("Risk acceptance not found."); e.Reject(comment, userId); await repository.SaveChangesAsync(ct); return (await repository.GetAsync(id, ct))!; }
    public async Task<RiskAcceptanceDto> CloseAsync(Guid id, Guid? userId, CancellationToken ct) { var e = await repository.FindAsync(id, ct) ?? throw new EntityNotFoundException("Risk acceptance not found."); e.Close(userId); await repository.SaveChangesAsync(ct); return (await repository.GetAsync(id, ct))!; }

    public async Task DeleteAsync(Guid id, Guid? userId, CancellationToken ct)
    {
        var e = await repository.FindAsync(id, ct) ?? throw new EntityNotFoundException("Risk acceptance not found.");
        e.SoftDelete(userId);
        await repository.SaveChangesAsync(ct);
    }

    private async Task<string> ResolveCodeAsync(Guid projectId, CancellationToken ct)
    {
        var project = await projects.GetAsync(projectId, ct) ?? throw new EntityNotFoundException("Project not found.");
        var prefix = $"{project.ProjectCode.Trim().ToUpperInvariant()}-RSK";
        var existing = await repository.ListCodesAsync(projectId, prefix, ct);
        return BusinessCodeGenerator.NextAvailable(prefix, existing);
    }
}
