using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Settings;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class AiConfigurationEntityConfiguration : IEntityTypeConfiguration<AiConfiguration>
{
    public void Configure(EntityTypeBuilder<AiConfiguration> builder)
    {
        builder.ToTable("AiConfigurations");
        builder.HasKey(x => x.AiConfigurationId);
        builder.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Model).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BaseUrl).HasMaxLength(500);
        builder.Property(x => x.EncryptedApiKey).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.ApiKeyHint).HasMaxLength(20);
    }
}
