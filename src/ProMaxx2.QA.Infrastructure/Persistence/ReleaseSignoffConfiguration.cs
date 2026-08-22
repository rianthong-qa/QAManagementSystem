using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Releases;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class ReleaseSignoffConfiguration : IEntityTypeConfiguration<ReleaseSignoff>
{
    public void Configure(EntityTypeBuilder<ReleaseSignoff> b)
    {
        b.ToTable("ReleaseSignoffs");
        b.HasKey(x => x.ReleaseSignoffId);
        b.Property(x => x.ReleaseSignoffId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.SignoffType).HasMaxLength(30).IsRequired();
        b.Property(x => x.Decision).HasMaxLength(30).IsRequired();
        b.Property(x => x.Comment).HasMaxLength(2000);
        b.Property(x => x.CreatedAt).HasPrecision(0);
        b.HasOne<Release>().WithMany().HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Build>().WithMany().HasForeignKey(x => x.BuildId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.ReleaseId);
        b.HasIndex(x => x.BuildId);
    }
}
