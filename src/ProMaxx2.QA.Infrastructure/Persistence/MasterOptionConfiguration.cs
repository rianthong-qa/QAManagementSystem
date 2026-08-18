using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Settings;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class MasterOptionConfiguration : IEntityTypeConfiguration<MasterOption>
{
    public void Configure(EntityTypeBuilder<MasterOption> b)
    {
        b.ToTable("MasterOptions");
        b.HasKey(x => x.MasterOptionId);
        b.Property(x => x.MasterOptionId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.Category).HasMaxLength(50).IsRequired();
        b.Property(x => x.Value).HasMaxLength(100).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
        b.HasIndex(x => new { x.Category, x.Value }).IsUnique();
    }
}
