using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Integrations;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class CrmSyncSettingsEntityConfiguration : IEntityTypeConfiguration<CrmSyncSettings>
{
    public void Configure(EntityTypeBuilder<CrmSyncSettings> b)
    {
        b.ToTable("CrmSyncSettings");
        b.HasKey(x => x.CrmSyncSettingsId);
        b.Property(x => x.CrmSyncSettingsId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.UpdatedAt).HasPrecision(0);
    }
}
