using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;
using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

/// <summary>AUT-P1-007 persistence for <see cref="AutomationBuildTriggerPolicy"/>/<see cref="AutomationBuildTriggerRun"/>
/// — split into its own file as a partial of <see cref="AutomationRepository"/>, same pattern as
/// AutomationScheduleRepository.cs.</summary>
public sealed partial class AutomationRepository
{
    public async Task<IReadOnlyList<AutomationBuildTriggerPolicyDto>> ListPoliciesAsync(Guid projectId, CancellationToken ct)
        => await db.AutomationBuildTriggerPolicies.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.Pack).ThenBy(x => x.CreatedAt)
            .Select(x => new AutomationBuildTriggerPolicyDto(x.AutomationBuildTriggerPolicyId, x.ProjectId, x.AutomationSuiteId, x.Suite.SuiteCode, x.Suite.SuiteName, x.Pack,
                x.EnvironmentId, db.TestEnvironments.Where(e => e.TestEnvironmentId == x.EnvironmentId).Select(e => e.EnvironmentName).FirstOrDefault() ?? "-",
                x.AgentId, x.AgentId != null ? db.AutomationAgents.Where(a => a.AgentId == x.AgentId).Select(a => a.AgentCode).FirstOrDefault() : null,
                x.Priority, x.IsActive, x.CreatedBy, x.CreatedAt, x.UpdatedAt))
            .ToListAsync(ct);

    public Task<AutomationBuildTriggerPolicyDto?> GetPolicyAsync(Guid id, Guid projectId, CancellationToken ct)
        => db.AutomationBuildTriggerPolicies.AsNoTracking().Where(x => x.AutomationBuildTriggerPolicyId == id && x.ProjectId == projectId)
            .Select(x => new AutomationBuildTriggerPolicyDto(x.AutomationBuildTriggerPolicyId, x.ProjectId, x.AutomationSuiteId, x.Suite.SuiteCode, x.Suite.SuiteName, x.Pack,
                x.EnvironmentId, db.TestEnvironments.Where(e => e.TestEnvironmentId == x.EnvironmentId).Select(e => e.EnvironmentName).FirstOrDefault() ?? "-",
                x.AgentId, x.AgentId != null ? db.AutomationAgents.Where(a => a.AgentId == x.AgentId).Select(a => a.AgentCode).FirstOrDefault() : null,
                x.Priority, x.IsActive, x.CreatedBy, x.CreatedAt, x.UpdatedAt))
            .SingleOrDefaultAsync(ct);

    public Task<AutomationBuildTriggerPolicy?> FindPolicyAsync(Guid id, Guid projectId, CancellationToken ct)
        => db.AutomationBuildTriggerPolicies.SingleOrDefaultAsync(x => x.AutomationBuildTriggerPolicyId == id && x.ProjectId == projectId, ct);

    public Task AddPolicyAsync(AutomationBuildTriggerPolicy entity, CancellationToken ct) => db.AutomationBuildTriggerPolicies.AddAsync(entity, ct).AsTask();

    public async Task<IReadOnlyList<AutomationBuildTriggerPolicy>> ListActivePoliciesForPackAsync(Guid projectId, string pack, CancellationToken ct)
        => await db.AutomationBuildTriggerPolicies.Where(x => x.ProjectId == projectId && x.Pack == pack && x.IsActive).ToListAsync(ct);

    public Task AddTriggerRunAsync(AutomationBuildTriggerRun entity, CancellationToken ct) => db.AutomationBuildTriggerRuns.AddAsync(entity, ct).AsTask();

    public async Task<IReadOnlyList<AutomationBuildTriggerRunDto>> ListTriggerRunsAsync(Guid policyId, CancellationToken ct)
        => await db.AutomationBuildTriggerRuns.AsNoTracking().Where(x => x.AutomationBuildTriggerPolicyId == policyId).OrderByDescending(x => x.FiredAtUtc)
            .Select(x => new AutomationBuildTriggerRunDto(x.AutomationBuildTriggerRunId, x.AutomationBuildTriggerPolicyId, x.BuildId,
                db.Builds.Where(b => b.BuildId == x.BuildId).Select(b => b.BuildNumber).FirstOrDefault() ?? "-",
                x.FiredAtUtc, x.Status, x.ExecutionsCreated, x.SkippedCount, x.ErrorMessage))
            .ToListAsync(ct);
}

public sealed class AutomationBuildTriggerPolicyConfiguration : IEntityTypeConfiguration<AutomationBuildTriggerPolicy>
{
    public void Configure(EntityTypeBuilder<AutomationBuildTriggerPolicy> b)
    {
        b.ToTable("AutomationBuildTriggerPolicies");
        b.HasKey(x => x.AutomationBuildTriggerPolicyId);
        b.Property(x => x.Pack).HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.ProjectId, x.Pack, x.IsActive });
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Suite).WithMany().HasForeignKey(x => x.AutomationSuiteId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AutomationBuildTriggerRunConfiguration : IEntityTypeConfiguration<AutomationBuildTriggerRun>
{
    public void Configure(EntityTypeBuilder<AutomationBuildTriggerRun> b)
    {
        b.ToTable("AutomationBuildTriggerRuns");
        b.HasKey(x => x.AutomationBuildTriggerRunId);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.HasIndex(x => new { x.AutomationBuildTriggerPolicyId, x.FiredAtUtc });
        b.HasOne(x => x.Policy).WithMany().HasForeignKey(x => x.AutomationBuildTriggerPolicyId).OnDelete(DeleteBehavior.Cascade);
    }
}
