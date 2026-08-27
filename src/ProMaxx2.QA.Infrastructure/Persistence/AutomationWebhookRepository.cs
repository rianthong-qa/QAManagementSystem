using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;
using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

/// <summary>AUT-P1-008 persistence for <see cref="AutomationWebhookToken"/>/<see cref="AutomationWebhookDelivery"/>
/// — split into its own file as a partial of <see cref="AutomationRepository"/>, same pattern as
/// AutomationScheduleRepository.cs/AutomationBuildTriggerRepository.cs.</summary>
public sealed partial class AutomationRepository
{
    public async Task<IReadOnlyList<AutomationWebhookTokenDto>> ListTokensAsync(Guid projectId, CancellationToken ct)
        => await db.AutomationWebhookTokens.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.CreatedAt)
            .Select(x => new AutomationWebhookTokenDto(x.AutomationWebhookTokenId, x.ProjectId, x.Name, x.TokenPrefix, x.IsActive, x.LastUsedAtUtc, x.CreatedBy, x.CreatedAt, x.RevokedAt))
            .ToListAsync(ct);

    public Task<AutomationWebhookToken?> FindTokenAsync(Guid id, Guid projectId, CancellationToken ct)
        => db.AutomationWebhookTokens.SingleOrDefaultAsync(x => x.AutomationWebhookTokenId == id && x.ProjectId == projectId, ct);

    public Task<AutomationWebhookToken?> FindActiveTokenByHashAsync(string tokenHash, CancellationToken ct)
        => db.AutomationWebhookTokens.SingleOrDefaultAsync(x => x.TokenHash == tokenHash && x.IsActive, ct);

    public Task AddTokenAsync(AutomationWebhookToken entity, CancellationToken ct) => db.AutomationWebhookTokens.AddAsync(entity, ct).AsTask();

    public async Task<IReadOnlyList<AutomationWebhookDeliveryDto>> ListDeliveriesAsync(Guid projectId, CancellationToken ct)
        => await db.AutomationWebhookDeliveries.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.ReceivedAtUtc)
            .Select(x => new AutomationWebhookDeliveryDto(x.AutomationWebhookDeliveryId, x.ProjectId, x.AutomationWebhookTokenId, x.Token.Name, x.RequestId, x.ReceivedAtUtc,
                x.BuildId, x.BuildId != null ? db.Builds.Where(b => b.BuildId == x.BuildId).Select(b => b.BuildNumber).FirstOrDefault() : null, x.Status, x.ErrorMessage))
            .ToListAsync(ct);

    public Task<AutomationWebhookDelivery?> FindSuccessfulDeliveryAsync(Guid projectId, string requestId, CancellationToken ct)
        => db.AutomationWebhookDeliveries.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.RequestId == requestId && x.Status == "Created", ct);

    public Task AddDeliveryAsync(AutomationWebhookDelivery entity, CancellationToken ct) => db.AutomationWebhookDeliveries.AddAsync(entity, ct).AsTask();
}

public sealed class AutomationWebhookTokenConfiguration : IEntityTypeConfiguration<AutomationWebhookToken>
{
    public void Configure(EntityTypeBuilder<AutomationWebhookToken> b)
    {
        b.ToTable("AutomationWebhookTokens");
        b.HasKey(x => x.AutomationWebhookTokenId);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.TokenPrefix).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.ProjectId, x.IsActive });
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AutomationWebhookDeliveryConfiguration : IEntityTypeConfiguration<AutomationWebhookDelivery>
{
    public void Configure(EntityTypeBuilder<AutomationWebhookDelivery> b)
    {
        b.ToTable("AutomationWebhookDeliveries");
        b.HasKey(x => x.AutomationWebhookDeliveryId);
        b.Property(x => x.RequestId).HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        // Not unique on (ProjectId, RequestId): a "Failed" delivery must not permanently block a retry with the
        // same RequestId once the underlying problem (e.g. release not found yet) is fixed — see
        // IAutomationWebhookRepository.FindSuccessfulDeliveryAsync for how idempotency is actually enforced.
        b.HasIndex(x => new { x.ProjectId, x.RequestId });
        b.HasOne(x => x.Token).WithMany().HasForeignKey(x => x.AutomationWebhookTokenId).OnDelete(DeleteBehavior.Restrict);
    }
}
