using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;
using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

/// <summary>AUT-DATA-002 persistence for <see cref="AutomationDbRestore"/> — split into its own file as a partial of
/// <see cref="AutomationRepository"/>, same pattern as AutomationDataSnapshotRepository.cs.</summary>
public sealed partial class AutomationRepository
{
    private static IQueryable<AutomationDbRestoreDto> ProjectRestoreDto(QaDbContext db) =>
        db.AutomationDbRestores.AsNoTracking()
            .Select(x => new AutomationDbRestoreDto(x.AutomationDbRestoreId, x.ProjectId, x.AutomationDbSnapshotId,
                x.Snapshot.EnvironmentId, db.TestEnvironments.Where(e => e.TestEnvironmentId == x.Snapshot.EnvironmentId).Select(e => e.EnvironmentName).FirstOrDefault() ?? "-",
                x.Snapshot.BuildId, db.Builds.Where(b => b.BuildId == x.Snapshot.BuildId).Select(b => b.BuildNumber).FirstOrDefault() ?? "-",
                x.Status, x.AgentId, x.AgentId != null ? db.AutomationAgents.Where(a => a.AgentId == x.AgentId).Select(a => a.AgentCode).FirstOrDefault() : null,
                x.ChecksumVerified, x.AvailabilityVerified, x.ErrorMessage, x.RequestedBy, x.RequestedAt, x.StartedAt, x.CompletedAt));

    public async Task<IReadOnlyList<AutomationDbRestoreDto>> ListRestoresAsync(Guid projectId, Guid? automationDbSnapshotId, CancellationToken ct)
    {
        var q = ProjectRestoreDto(db).Where(x => x.ProjectId == projectId);
        if (automationDbSnapshotId.HasValue) q = q.Where(x => x.AutomationDbSnapshotId == automationDbSnapshotId.Value);
        return await q.OrderByDescending(x => x.RequestedAt).ToListAsync(ct);
    }

    public Task<AutomationDbRestoreDto?> GetRestoreAsync(Guid id, Guid projectId, CancellationToken ct)
        => ProjectRestoreDto(db).Where(x => x.AutomationDbRestoreId == id && x.ProjectId == projectId).SingleOrDefaultAsync(ct);

    public Task<AutomationDbRestoreDto?> GetRestoreByIdAsync(Guid id, CancellationToken ct)
        => ProjectRestoreDto(db).Where(x => x.AutomationDbRestoreId == id).SingleOrDefaultAsync(ct);

    public Task AddRestoreAsync(AutomationDbRestore entity, CancellationToken ct) => db.AutomationDbRestores.AddAsync(entity, ct).AsTask();

    public Task<AutomationDbRestore?> FindRestoreAsync(Guid id, CancellationToken ct)
        => db.AutomationDbRestores.SingleOrDefaultAsync(x => x.AutomationDbRestoreId == id, ct);

    public async Task<ClaimRestorePackageDto?> ClaimNextRestoreRequestAsync(string agentCode, CancellationToken ct)
    {
        var agent = await db.AutomationAgents.SingleOrDefaultAsync(x => x.AgentCode == agentCode.Trim().ToUpperInvariant(), ct);
        if (agent is null || !agent.IsEnabled || agent.IsDeleted) return null;
        // Serializable, same pattern as ClaimNextSnapshotRequestAsync. Restricted to restores whose snapshot was
        // produced by THIS agent — see the interface doc comment for why (the backup file only exists locally on
        // the agent that made it).
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var next = await db.AutomationDbRestores.Include(x => x.Snapshot)
            .Where(x => x.Status == "Requested" && x.Snapshot.AgentId == agent.AgentId)
            .OrderBy(x => x.RequestedAt).FirstOrDefaultAsync(ct);
        if (next is null) { if (transaction is not null) await transaction.CommitAsync(ct); return null; }
        next.Claim(agent.AgentId);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new ClaimRestorePackageDto(next.AutomationDbRestoreId, next.AutomationDbSnapshotId, next.Snapshot.SnapshotPath!, next.Snapshot.Checksum!);
    }
}

public sealed class AutomationDbRestoreConfiguration : IEntityTypeConfiguration<AutomationDbRestore>
{
    public void Configure(EntityTypeBuilder<AutomationDbRestore> b)
    {
        b.ToTable("AutomationDbRestores");
        b.HasKey(x => x.AutomationDbRestoreId);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.HasIndex(x => new { x.ProjectId, x.RequestedAt });
        b.HasIndex(x => x.Status);
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Snapshot).WithMany().HasForeignKey(x => x.AutomationDbSnapshotId).OnDelete(DeleteBehavior.Restrict);
    }
}
