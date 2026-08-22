using ProMaxx2.QA.Domain.Governance;

namespace ProMaxx2.QA.Application.Governance;

public sealed record ReleaseSignoffDto(Guid ReleaseSignoffId, Guid ReleaseId, Guid BuildId, string BuildNumber, string SignoffType, string Decision, string? Comment, string? SignoffBy, DateTime CreatedAt);
public sealed record ReleaseGateDto(bool SmokePassed, int OpenP0, int P1Blockers, decimal RequirementCoverage, decimal RegressionPassRate, bool UpdateTestPassed, int ApprovedRisks, string RecommendedDecision);
public sealed record CreateReleaseSignoffRequest(Guid BuildId, string SignoffType, string Decision, string? Comment);

public interface IReleaseSignoffRepository
{
    Task<ReleaseGateDto> GetGateAsync(Guid releaseId, Guid? buildId, CancellationToken ct);
    Task<IReadOnlyList<ReleaseSignoffDto>> ListAsync(Guid releaseId, CancellationToken ct);
    Task<bool> BuildBelongsToReleaseAsync(Guid releaseId, Guid buildId, CancellationToken ct);
    Task<string?> GetBuildNumberAsync(Guid buildId, CancellationToken ct);
    Task AddAsync(ReleaseSignoff entity, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public sealed class ReleaseSignoffService(IReleaseSignoffRepository repository)
{
    public Task<ReleaseGateDto> GetGateAsync(Guid releaseId, Guid? buildId, CancellationToken ct) => repository.GetGateAsync(releaseId, buildId, ct);
    public Task<IReadOnlyList<ReleaseSignoffDto>> ListAsync(Guid releaseId, CancellationToken ct) => repository.ListAsync(releaseId, ct);

    public async Task<ReleaseSignoffDto> CreateAsync(Guid releaseId, CreateReleaseSignoffRequest r, Guid? userId, CancellationToken ct)
    {
        if (!await repository.BuildBelongsToReleaseAsync(releaseId, r.BuildId, ct)) throw new InvalidOperationException("Build does not belong to the selected release.");
        var decision = ReleaseSignoff.NormalizeDecision(r.Decision);
        var e = new ReleaseSignoff(releaseId, r.BuildId, r.SignoffType, decision, r.Comment, userId);
        await repository.AddAsync(e, ct);
        await repository.SaveChangesAsync(ct);
        var buildNumber = await repository.GetBuildNumberAsync(r.BuildId, ct) ?? "-";
        return new ReleaseSignoffDto(e.ReleaseSignoffId, e.ReleaseId, e.BuildId, buildNumber, e.SignoffType, e.Decision, e.Comment, null, e.CreatedAt);
    }
}
