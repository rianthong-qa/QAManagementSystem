using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Settings;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class EmailConfigurationEntityConfiguration : IEntityTypeConfiguration<EmailConfiguration>
{
    public void Configure(EntityTypeBuilder<EmailConfiguration> b)
    {
        b.ToTable("EmailConfigurations");
        b.HasKey(x => x.EmailConfigurationId);
        b.Property(x => x.EmailConfigurationId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.SmtpHost).HasMaxLength(200).IsRequired();
        b.Property(x => x.SenderEmail).HasMaxLength(200).IsRequired();
        b.Property(x => x.SenderDisplayName).HasMaxLength(100);
        b.Property(x => x.EncryptedPassword).HasMaxLength(4000).IsRequired();
        b.Property(x => x.PasswordHint).HasMaxLength(20);
        b.Property(x => x.UpdatedAt).HasPrecision(0);
    }
}
