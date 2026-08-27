using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;
using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

/// <summary>AUT-P1-001/AUT-P1-002 persistence for <see cref="AutomationSuite"/>/<see cref="AutomationSuiteCase"/> — split into its own file as a partial of <see cref="AutomationRepository"/> to keep the (already large) main file from growing further; SaveChangesAsync is shared with <see cref="IAutomationRepository"/>'s implementation.</summary>
public sealed partial class AutomationRepository
{
    public async Task<IReadOnlyList<AutomationSuiteListDto>> ListSuitesAsync(Guid projectId, string? search, bool? isActive, CancellationToken ct)
    {
        var q = db.AutomationSuites.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.SuiteCode.Contains(search) || x.SuiteName.Contains(search));
        if (isActive.HasValue) q = q.Where(x => x.IsActive == isActive.Value);
        return await q.OrderByDescending(x => x.CreatedAt)
            .Select(x => new AutomationSuiteListDto(x.AutomationSuiteId, x.ProjectId, x.SuiteCode, x.SuiteName, x.Description, x.IsActive, x.CreatedAt, x.ClosedAt,
                x.Cases.Count, x.Cases.Count(c => c.AutomationCase.Status == "Ready")))
            .ToListAsync(ct);
    }

    public async Task<AutomationSuiteDto?> GetSuiteAsync(Guid id, Guid projectId, CancellationToken ct)
    {
        var suite = await db.AutomationSuites.AsNoTracking().Where(x => x.AutomationSuiteId == id && x.ProjectId == projectId)
            .Select(x => new { x.AutomationSuiteId, x.ProjectId, x.SuiteCode, x.SuiteName, x.Description, x.IsActive, x.CreatedBy, x.CreatedAt, x.UpdatedAt, x.ClosedAt })
            .SingleOrDefaultAsync(ct);
        if (suite is null) return null;
        var cases = await db.AutomationSuiteCases.AsNoTracking().Where(x => x.AutomationSuiteId == id).OrderBy(x => x.SortOrder)
            .Select(x => new SuiteCaseDto(x.AutomationCaseId, x.AutomationCase.AutomationCode, x.AutomationCase.TestCase.TestCaseCode, x.AutomationCase.TestCase.Title, x.AutomationCase.AutomationType, x.AutomationCase.Status, x.SortOrder, x.IsRequired))
            .ToListAsync(ct);
        return new AutomationSuiteDto(suite.AutomationSuiteId, suite.ProjectId, suite.SuiteCode, suite.SuiteName, suite.Description, suite.IsActive, suite.CreatedBy, suite.CreatedAt, suite.UpdatedAt, suite.ClosedAt, cases);
    }

    public Task<AutomationSuite?> FindSuiteAsync(Guid id, Guid projectId, CancellationToken ct)
        => db.AutomationSuites.SingleOrDefaultAsync(x => x.AutomationSuiteId == id && x.ProjectId == projectId, ct);

    public Task<bool> SuiteCodeExistsAsync(Guid projectId, string code, Guid? excludeId, CancellationToken ct)
        => db.AutomationSuites.AnyAsync(x => x.ProjectId == projectId && x.SuiteCode == code && (!excludeId.HasValue || x.AutomationSuiteId != excludeId.Value), ct);

    public async Task<IReadOnlyList<string>> ListSuiteCodesAsync(Guid projectId, string prefix, CancellationToken ct)
        => await db.AutomationSuites.AsNoTracking().Where(x => x.ProjectId == projectId && x.SuiteCode.StartsWith(prefix)).Select(x => x.SuiteCode).ToListAsync(ct);

    public Task AddSuiteAsync(AutomationSuite entity, CancellationToken ct) => db.AutomationSuites.AddAsync(entity, ct).AsTask();

    public Task<bool> CaseExistsInProjectAsync(Guid projectId, Guid caseId, CancellationToken ct)
        => db.AutomationCases.AnyAsync(x => x.AutomationCaseId == caseId && !x.IsDeleted && x.TestCase.ProjectId == projectId, ct);

    public Task<bool> SuiteCaseExistsAsync(Guid suiteId, Guid caseId, CancellationToken ct)
        => db.AutomationSuiteCases.AnyAsync(x => x.AutomationSuiteId == suiteId && x.AutomationCaseId == caseId, ct);

    public async Task<int> GetNextSuiteCaseSortOrderAsync(Guid suiteId, CancellationToken ct)
        => (await db.AutomationSuiteCases.Where(x => x.AutomationSuiteId == suiteId).Select(x => (int?)x.SortOrder).MaxAsync(ct) ?? 0) + 1;

    public Task AddSuiteCaseAsync(AutomationSuiteCase entity, CancellationToken ct) => db.AutomationSuiteCases.AddAsync(entity, ct).AsTask();

    public Task<AutomationSuiteCase?> FindSuiteCaseAsync(Guid suiteId, Guid caseId, CancellationToken ct)
        => db.AutomationSuiteCases.SingleOrDefaultAsync(x => x.AutomationSuiteId == suiteId && x.AutomationCaseId == caseId, ct);

    public Task RemoveSuiteCaseAsync(AutomationSuiteCase entity, CancellationToken ct)
    {
        db.AutomationSuiteCases.Remove(entity);
        return Task.CompletedTask;
    }
}

public sealed class AutomationSuiteConfiguration : IEntityTypeConfiguration<AutomationSuite>
{
    public void Configure(EntityTypeBuilder<AutomationSuite> b)
    {
        b.ToTable("AutomationSuites");
        b.HasKey(x => x.AutomationSuiteId);
        b.Property(x => x.SuiteCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.SuiteName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.HasIndex(x => new { x.ProjectId, x.SuiteCode }).IsUnique();
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AutomationSuiteCaseConfiguration : IEntityTypeConfiguration<AutomationSuiteCase>
{
    public void Configure(EntityTypeBuilder<AutomationSuiteCase> b)
    {
        b.ToTable("AutomationSuiteCases");
        b.HasKey(x => new { x.AutomationSuiteId, x.AutomationCaseId });
        b.HasOne(x => x.Suite).WithMany(x => x.Cases).HasForeignKey(x => x.AutomationSuiteId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.AutomationCase).WithMany().HasForeignKey(x => x.AutomationCaseId).OnDelete(DeleteBehavior.Restrict);
    }
}
