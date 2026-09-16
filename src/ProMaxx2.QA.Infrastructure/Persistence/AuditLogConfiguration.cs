using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Identity;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(x => x.AuditLogId);
        builder.Property(x => x.AuditLogId).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.Action).HasMaxLength(60).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(60).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ChangeSummary).HasMaxLength(4000);
        builder.Property(x => x.BeforeJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.AfterJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ClientIp).HasMaxLength(64);
        builder.Property(x => x.CreatedAt).HasPrecision(0);
        builder.HasIndex(x => new { x.EntityType, x.EntityId, x.CreatedAt });
        builder.HasIndex(x => new { x.CreatedAt, x.AuditLogId });
        builder.HasIndex(x => new { x.EntityType, x.CreatedAt, x.AuditLogId });
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
