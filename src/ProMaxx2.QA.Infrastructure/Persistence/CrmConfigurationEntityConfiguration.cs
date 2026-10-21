using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Integrations;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class CrmConfigurationEntityConfiguration : IEntityTypeConfiguration<CrmConfiguration>
{
    public void Configure(EntityTypeBuilder<CrmConfiguration> b)
    {
        b.ToTable("CrmConfigurations");
        b.HasKey(x => x.CrmConfigurationId);
        b.Property(x => x.CrmConfigurationId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.HasIndex(x => x.UserId).IsUnique(); // หนึ่งแถวต่อหนึ่ง user เท่านั้น
        b.Property(x => x.MerchantId).HasMaxLength(20).IsRequired();
        b.Property(x => x.Username).HasMaxLength(50).IsRequired();
        b.Property(x => x.EncryptedPassword).HasMaxLength(4000).IsRequired();
        b.Property(x => x.PasswordHint).HasMaxLength(20);
        b.Property(x => x.UpdatedAt).HasPrecision(0);
    }
}
