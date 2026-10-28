using ProMaxx2.QA.Domain.Governance;

namespace ProMaxx2.QA.Application.Governance;

public sealed record ReleaseSignoffDto(Guid ReleaseSignoffId, Guid ReleaseId, Guid BuildId, string BuildNumber, string SignoffType, string Decision, string? Comment, string? SignoffBy, DateTime CreatedAt);
/// <summary>Defect* = นับ Defect ของ Release ตาม DefectGateCounts เสมอ (ให้เห็นก่อนเปิดใช้) แต่มีผลต่อ RecommendedDecision เฉพาะเมื่อ DefectGateEnabled</summary>
public sealed record ReleaseGateDto(int OpenP0, int P1Blockers, decimal RequirementCoverage, decimal RegressionPassRate, bool UpdateTestPassed, int ApprovedRisks, string RecommendedDecision, string SmokeStatus = "NOT_RUN",
    bool DefectGateEnabled = false, int OpenCriticalDefects = 0, int OpenHighDefects = 0, int UnverifiedResolvedDefects = 0, int DefectBlockersWithoutRisk = 0, int DefectBlockersWithRisk = 0);

/// <summary>config <c>Defects:GateEnabled</c> — เปิดเมื่อเคลียร์ backlog แล้ว (DEFECT_BUILD_LIFECYCLE.md เฟส 4–5)</summary>
public sealed class DefectGateOptions { public const string Section = "Defects"; public bool GateEnabled { get; init; } }
public sealed record CreateReleaseSignoffRequest(Guid BuildId, string SignoffType, string Decision, string? Comment);

public interface IReleaseSignoffRepository
{
    Task<ReleaseGateDto> GetGateAsync(Guid releaseId, Guid? buildId, CancellationToken ct);
    Task<IReadOnlyList<ReleaseSignoffDto>> ListAsync(Guid releaseId, CancellationToken ct);
    Task<bool> BuildBelongsToReleaseAsync(Guid releaseId, Guid buildId, CancellationToken ct);
    Task<bool> ExistsForBuildAndTypeAsync(Guid releaseId, Guid buildId, string signoffType, CancellationToken ct);
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
        if (await repository.ExistsForBuildAndTypeAsync(releaseId, r.BuildId, r.SignoffType, ct)) throw new InvalidOperationException("A sign-off for this role already exists for the selected build.");
        var decision = ReleaseSignoff.NormalizeDecision(r.Decision);
        var e = new ReleaseSignoff(releaseId, r.BuildId, r.SignoffType, decision, r.Comment, userId);
        await repository.AddAsync(e, ct);
        await repository.SaveChangesAsync(ct);
        var buildNumber = await repository.GetBuildNumberAsync(r.BuildId, ct) ?? "-";
        return new ReleaseSignoffDto(e.ReleaseSignoffId, e.ReleaseId, e.BuildId, buildNumber, e.SignoffType, e.Decision, e.Comment, null, e.CreatedAt);
    }
}
