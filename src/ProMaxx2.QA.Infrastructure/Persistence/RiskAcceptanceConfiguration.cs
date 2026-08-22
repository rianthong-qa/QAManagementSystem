using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Releases;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class RiskAcceptanceConfiguration : IEntityTypeConfiguration<RiskAcceptance>
{
    public void Configure(EntityTypeBuilder<RiskAcceptance> b)
    {
        b.ToTable("RiskAcceptances");
        b.HasKey(x => x.RiskAcceptanceId);
        b.Property(x => x.RiskAcceptanceId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.RiskCode).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.ProjectId, x.RiskCode }).IsUnique();
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.Issue).HasMaxLength(2000);
        b.Property(x => x.Impact).HasMaxLength(20).IsRequired();
        b.Property(x => x.Probability).HasMaxLength(20).IsRequired();
        b.Property(x => x.RiskLevel).HasMaxLength(20).IsRequired();
        b.Property(x => x.Status).HasMaxLength(30).IsRequired();
        b.Property(x => x.Workaround).HasMaxLength(2000);
        b.Property(x => x.TargetFix).HasMaxLength(2000);
        b.Property(x => x.QaRecommendation).HasMaxLength(4000);
        b.Property(x => x.ReviewComment).HasMaxLength(2000);
        b.Property(x => x.CreatedAt).HasPrecision(0);
        b.Property(x => x.UpdatedAt).HasPrecision(0);
        b.Property(x => x.ReviewDate).HasPrecision(0);
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Release>().WithMany().HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.ReleaseId);
        b.HasIndex(x => x.Status);
    }
}
