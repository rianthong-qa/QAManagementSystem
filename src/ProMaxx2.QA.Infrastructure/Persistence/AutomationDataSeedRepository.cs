using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;
using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

/// <summary>AUT-DATA-003 persistence for <see cref="AutomationDataSeedScript"/>/<see cref="AutomationDataSeedRun"/> —
/// split into its own file as a partial of <see cref="AutomationRepository"/>, same pattern as the rest of this
/// module.</summary>
public sealed partial class AutomationRepository
{
    public async Task<IReadOnlyList<AutomationDataSeedScriptListDto>> ListScriptsAsync(Guid projectId, bool? isActive, CancellationToken ct)
    {
        var q = db.AutomationDataSeedScripts.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (isActive.HasValue) q = q.Where(x => x.IsActive == isActive.Value);
        return await q.OrderBy(x => x.Name)
            .Select(x => new AutomationDataSeedScriptListDto(x.AutomationDataSeedScriptId, x.ProjectId, x.Name, x.Description, x.DbKind, x.IsActive, x.CreatedAt))
            .ToListAsync(ct);
    }

    public Task<AutomationDataSeedScriptDto?> GetScriptAsync(Guid id, Guid projectId, CancellationToken ct)
        => db.AutomationDataSeedScripts.AsNoTracking().Where(x => x.AutomationDataSeedScriptId == id && x.ProjectId == projectId)
            .Select(x => new AutomationDataSeedScriptDto(x.AutomationDataSeedScriptId, x.ProjectId, x.Name, x.Description, x.DbKind, x.SqlScript, x.IsActive, x.CreatedBy, x.CreatedAt, x.UpdatedAt))
            .SingleOrDefaultAsync(ct);

    public Task<AutomationDataSeedScript?> FindScriptAsync(Guid id, Guid projectId, CancellationToken ct)
        => db.AutomationDataSeedScripts.SingleOrDefaultAsync(x => x.AutomationDataSeedScriptId == id && x.ProjectId == projectId, ct);

    public Task AddScriptAsync(AutomationDataSeedScript entity, CancellationToken ct) => db.AutomationDataSeedScripts.AddAsync(entity, ct).AsTask();

    private static IQueryable<AutomationDataSeedRunDto> ProjectSeedRunDto(QaDbContext db) =>
        db.AutomationDataSeedRuns.AsNoTracking()
            .Select(x => new AutomationDataSeedRunDto(x.AutomationDataSeedRunId, x.ProjectId, x.AutomationDataSeedScriptId, x.Script.Name,
                x.EnvironmentId, db.TestEnvironments.Where(e => e.TestEnvironmentId == x.EnvironmentId).Select(e => e.EnvironmentName).FirstOrDefault() ?? "-",
                x.BuildId, db.Builds.Where(b => b.BuildId == x.BuildId).Select(b => b.BuildNumber).FirstOrDefault() ?? "-",
                x.Status, x.AgentId, x.AgentId != null ? db.AutomationAgents.Where(a => a.AgentId == x.AgentId).Select(a => a.AgentCode).FirstOrDefault() : null,
                x.RowsAffected, x.ErrorMessage, x.RequestedBy, x.RequestedAt, x.StartedAt, x.CompletedAt));

    public async Task<IReadOnlyList<AutomationDataSeedRunDto>> ListRunsAsync(Guid projectId, Guid? scriptId, CancellationToken ct)
    {
        var q = ProjectSeedRunDto(db).Where(x => x.ProjectId == projectId);
        if (scriptId.HasValue) q = q.Where(x => x.AutomationDataSeedScriptId == scriptId.Value);
        return await q.OrderByDescending(x => x.RequestedAt).ToListAsync(ct);
    }

    public Task<AutomationDataSeedRunDto?> GetRunAsync(Guid id, Guid projectId, CancellationToken ct)
        => ProjectSeedRunDto(db).Where(x => x.AutomationDataSeedRunId == id && x.ProjectId == projectId).SingleOrDefaultAsync(ct);

    public Task<AutomationDataSeedRunDto?> GetRunByIdAsync(Guid id, CancellationToken ct)
        => ProjectSeedRunDto(db).Where(x => x.AutomationDataSeedRunId == id).SingleOrDefaultAsync(ct);

    public Task AddRunAsync(AutomationDataSeedRun entity, CancellationToken ct) => db.AutomationDataSeedRuns.AddAsync(entity, ct).AsTask();

    public Task<AutomationDataSeedRun?> FindRunAsync(Guid id, CancellationToken ct)
        => db.AutomationDataSeedRuns.SingleOrDefaultAsync(x => x.AutomationDataSeedRunId == id, ct);

    public async Task<ClaimSeedRunPackageDto?> ClaimNextSeedRunRequestAsync(string agentCode, CancellationToken ct)
    {
        var agent = await db.AutomationAgents.SingleOrDefaultAsync(x => x.AgentCode == agentCode.Trim().ToUpperInvariant(), ct);
        if (agent is null || !agent.IsEnabled || agent.IsDeleted) return null;
        // Serializable, same pattern as ClaimNextSnapshotRequestAsync/ClaimNextRestoreRequestAsync.
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var next = await db.AutomationDataSeedRuns.Include(x => x.Script).Where(x => x.Status == "Requested").OrderBy(x => x.RequestedAt).FirstOrDefaultAsync(ct);
        if (next is null) { if (transaction is not null) await transaction.CommitAsync(ct); return null; }
        next.Claim(agent.AgentId);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new ClaimSeedRunPackageDto(next.AutomationDataSeedRunId, next.Script.Name, next.Script.DbKind, next.Script.SqlScript);
    }
}

public sealed class AutomationDataSeedScriptConfiguration : IEntityTypeConfiguration<AutomationDataSeedScript>
{
    public void Configure(EntityTypeBuilder<AutomationDataSeedScript> b)
    {
        b.ToTable("AutomationDataSeedScripts");
        b.HasKey(x => x.AutomationDataSeedScriptId);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.DbKind).HasMaxLength(20).IsRequired();
        b.Property(x => x.SqlScript).HasMaxLength(50_000).IsRequired();
        b.HasIndex(x => new { x.ProjectId, x.IsActive });
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AutomationDataSeedRunConfiguration : IEntityTypeConfiguration<AutomationDataSeedRun>
{
    public void Configure(EntityTypeBuilder<AutomationDataSeedRun> b)
    {
        b.ToTable("AutomationDataSeedRuns");
        b.HasKey(x => x.AutomationDataSeedRunId);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.HasIndex(x => new { x.ProjectId, x.RequestedAt });
        b.HasIndex(x => x.Status);
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Script).WithMany().HasForeignKey(x => x.AutomationDataSeedScriptId).OnDelete(DeleteBehavior.Restrict);
    }
}
