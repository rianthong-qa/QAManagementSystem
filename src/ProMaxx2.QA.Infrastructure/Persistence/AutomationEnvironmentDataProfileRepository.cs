using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;
using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

/// <summary>AUT-DATA-006 persistence for <see cref="AutomationEnvironmentDataProfile"/> — split into its own file
/// as a partial of <see cref="AutomationRepository"/>, same pattern as the rest of this module. Note:
/// <c>EnvironmentExistsAsync</c> is not (re)implemented here — the one already implemented for
/// <see cref="IAutomationDataSnapshotRepository"/> in AutomationDataSnapshotRepository.cs has the identical
/// signature, so it satisfies <see cref="IAutomationEnvironmentDataProfileRepository"/> automatically.</summary>
public sealed partial class AutomationRepository
{
    private static IQueryable<AutomationEnvironmentDataProfileDto> ProjectEnvironmentDataProfileDto(QaDbContext db) =>
        db.AutomationEnvironmentDataProfiles.AsNoTracking()
            .Select(x => new AutomationEnvironmentDataProfileDto(x.AutomationEnvironmentDataProfileId, x.ProjectId, x.EnvironmentId,
                db.TestEnvironments.Where(e => e.TestEnvironmentId == x.EnvironmentId).Select(e => e.EnvironmentName).FirstOrDefault() ?? "-",
                x.DbKind, x.Notes, x.CreatedBy, x.CreatedAt, x.UpdatedAt));

    public async Task<IReadOnlyList<AutomationEnvironmentDataProfileDto>> ListEnvironmentDataProfilesAsync(Guid projectId, CancellationToken ct) =>
        await ProjectEnvironmentDataProfileDto(db).Where(x => x.ProjectId == projectId).OrderBy(x => x.EnvironmentName).ToListAsync(ct);

    public Task<AutomationEnvironmentDataProfileDto?> GetEnvironmentDataProfileAsync(Guid id, Guid projectId, CancellationToken ct) =>
        ProjectEnvironmentDataProfileDto(db).Where(x => x.AutomationEnvironmentDataProfileId == id && x.ProjectId == projectId).SingleOrDefaultAsync(ct);

    public Task<AutomationEnvironmentDataProfile?> FindEnvironmentDataProfileAsync(Guid id, Guid projectId, CancellationToken ct) =>
        db.AutomationEnvironmentDataProfiles.SingleOrDefaultAsync(x => x.AutomationEnvironmentDataProfileId == id && x.ProjectId == projectId, ct);

    public Task<bool> EnvironmentDataProfileExistsForEnvironmentAsync(Guid environmentId, CancellationToken ct) =>
        db.AutomationEnvironmentDataProfiles.AnyAsync(x => x.EnvironmentId == environmentId, ct);

    public Task AddEnvironmentDataProfileAsync(AutomationEnvironmentDataProfile entity, CancellationToken ct) =>
        db.AutomationEnvironmentDataProfiles.AddAsync(entity, ct).AsTask();

    public Task<string?> GetDataProfileDbKindForEnvironmentAsync(Guid environmentId, CancellationToken ct) =>
        db.AutomationEnvironmentDataProfiles.AsNoTracking().Where(x => x.EnvironmentId == environmentId).Select(x => (string?)x.DbKind).FirstOrDefaultAsync(ct);
}

public sealed class AutomationEnvironmentDataProfileConfiguration : IEntityTypeConfiguration<AutomationEnvironmentDataProfile>
{
    public void Configure(EntityTypeBuilder<AutomationEnvironmentDataProfile> b)
    {
        b.ToTable("AutomationEnvironmentDataProfiles");
        b.HasKey(x => x.AutomationEnvironmentDataProfileId);
        b.Property(x => x.DbKind).HasMaxLength(20).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(2000);
        // One profile per Environment — the service layer also enforces this with a check-then-create (same pattern
        // used elsewhere in this module), this index is the DB-level backstop against a race between two concurrent
        // creates for the same Environment.
        b.HasIndex(x => x.EnvironmentId).IsUnique();
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
