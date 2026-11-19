using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Identity;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> b)
    {
        b.ToTable("UserPreferences");
        b.HasKey(x => new { x.UserId, x.PreferenceKey });
        b.Property(x => x.PreferenceKey).HasMaxLength(UserPreference.MaxKeyLength).IsRequired();
        b.Property(x => x.Value).HasMaxLength(UserPreference.MaxValueLength).IsRequired();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
