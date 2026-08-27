using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;
using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

/// <summary>AUT-DATA-001 persistence for <see cref="AutomationDbSnapshot"/> — split into its own file as a partial of
/// <see cref="AutomationRepository"/>, same pattern as AutomationScheduleRepository.cs.</summary>
public sealed partial class AutomationRepository
{
    private static IQueryable<AutomationDbSnapshotDto> ProjectSnapshotDto(QaDbContext db) =>
        db.AutomationDbSnapshots.AsNoTracking()
            .Select(x => new AutomationDbSnapshotDto(x.AutomationDbSnapshotId, x.ProjectId, x.EnvironmentId,
                db.TestEnvironments.Where(e => e.TestEnvironmentId == x.EnvironmentId).Select(e => e.EnvironmentName).FirstOrDefault() ?? "-",
                x.BuildId, db.Builds.Where(b => b.BuildId == x.BuildId).Select(b => b.BuildNumber).FirstOrDefault() ?? "-",
                x.Status, x.DbKind, x.AgentId, x.AgentId != null ? db.AutomationAgents.Where(a => a.AgentId == x.AgentId).Select(a => a.AgentCode).FirstOrDefault() : null,
                x.SnapshotPath, x.Checksum, x.SizeBytes, x.ErrorMessage, x.RequestedBy, x.RequestedAt, x.StartedAt, x.CompletedAt));

    public async Task<IReadOnlyList<AutomationDbSnapshotDto>> ListSnapshotsAsync(Guid projectId, Guid? environmentId, Guid? buildId, int take, CancellationToken ct)
    {
        var q = ProjectSnapshotDto(db).Where(x => x.ProjectId == projectId);
        if (environmentId.HasValue) q = q.Where(x => x.EnvironmentId == environmentId.Value);
        if (buildId.HasValue) q = q.Where(x => x.BuildId == buildId.Value);
        return await q.OrderByDescending(x => x.RequestedAt).Take(take).ToListAsync(ct);
    }

    public Task<AutomationDbSnapshotDto?> GetSnapshotAsync(Guid id, Guid projectId, CancellationToken ct)
        => ProjectSnapshotDto(db).Where(x => x.AutomationDbSnapshotId == id && x.ProjectId == projectId).SingleOrDefaultAsync(ct);

    public Task<AutomationDbSnapshotDto?> GetSnapshotByIdAsync(Guid id, CancellationToken ct)
        => ProjectSnapshotDto(db).Where(x => x.AutomationDbSnapshotId == id).SingleOrDefaultAsync(ct);

    public Task<bool> EnvironmentExistsAsync(Guid environmentId, Guid projectId, CancellationToken ct)
        => db.TestEnvironments.AsNoTracking().AnyAsync(x => x.TestEnvironmentId == environmentId && x.ProjectId == projectId, ct);

    public Task<bool> BuildExistsAsync(Guid buildId, CancellationToken ct)
        => db.Builds.AsNoTracking().AnyAsync(x => x.BuildId == buildId, ct);

    public Task AddSnapshotAsync(AutomationDbSnapshot entity, CancellationToken ct) => db.AutomationDbSnapshots.AddAsync(entity, ct).AsTask();

    public Task<AutomationDbSnapshot?> FindSnapshotAsync(Guid id, CancellationToken ct)
        => db.AutomationDbSnapshots.SingleOrDefaultAsync(x => x.AutomationDbSnapshotId == id, ct);

    public async Task<ClaimSnapshotPackageDto?> ClaimNextSnapshotRequestAsync(string agentCode, CancellationToken ct)
    {
        var agent = await db.AutomationAgents.SingleOrDefaultAsync(x => x.AgentCode == agentCode.Trim().ToUpperInvariant(), ct);
        if (agent is null || !agent.IsEnabled || agent.IsDeleted) return null;
        // Serializable, same pattern as ClaimNextJobAsync: without this, two agents polling concurrently could both
        // read the same "Requested" row before either commits and both end up backing up the same request.
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var next = await db.AutomationDbSnapshots.Where(x => x.Status == "Requested").OrderBy(x => x.RequestedAt).FirstOrDefaultAsync(ct);
        if (next is null) { if (transaction is not null) await transaction.CommitAsync(ct); return null; }
        next.Claim(agent.AgentId);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);

        var envName = await db.TestEnvironments.AsNoTracking().Where(e => e.TestEnvironmentId == next.EnvironmentId).Select(e => e.EnvironmentName).FirstOrDefaultAsync(ct) ?? "-";
        var buildNumber = await db.Builds.AsNoTracking().Where(b => b.BuildId == next.BuildId).Select(b => b.BuildNumber).FirstOrDefaultAsync(ct) ?? "-";
        return new ClaimSnapshotPackageDto(next.AutomationDbSnapshotId, next.EnvironmentId, envName, next.BuildId, buildNumber);
    }
}

public sealed class AutomationDbSnapshotConfiguration : IEntityTypeConfiguration<AutomationDbSnapshot>
{
    public void Configure(EntityTypeBuilder<AutomationDbSnapshot> b)
    {
        b.ToTable("AutomationDbSnapshots");
        b.HasKey(x => x.AutomationDbSnapshotId);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.DbKind).HasMaxLength(20);
        b.Property(x => x.SnapshotPath).HasMaxLength(1000);
        b.Property(x => x.Checksum).HasMaxLength(64);
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.HasIndex(x => new { x.ProjectId, x.RequestedAt });
        b.HasIndex(x => x.Status);
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
