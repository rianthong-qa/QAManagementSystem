using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Identity;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class PersonalAccessTokenConfiguration : IEntityTypeConfiguration<PersonalAccessToken>
{
    public void Configure(EntityTypeBuilder<PersonalAccessToken> b)
    {
        b.ToTable("PersonalAccessTokens");
        b.HasKey(x => x.PersonalAccessTokenId);
        b.Property(x => x.PersonalAccessTokenId).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.TokenPrefix).HasMaxLength(12).IsRequired();
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Scope).HasMaxLength(50).IsRequired();
        b.Property(x => x.LastUsedIp).HasMaxLength(64);
        b.Property(x => x.Kind).HasMaxLength(20).IsRequired().HasDefaultValue("Personal");
        b.Property(x => x.ClientId).HasMaxLength(64);
        b.Property(x => x.RefreshTokenHash).HasMaxLength(64);
        b.Property(x => x.Resource).HasMaxLength(300);
        // ค้น refresh token ตอนต่ออายุ (OAuth)
        b.HasIndex(x => x.RefreshTokenHash).IsUnique().HasFilter("[RefreshTokenHash] IS NOT NULL");
        // ค้นจาก hash ทุกคำขอ
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.RevokedAt });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class OAuthClientConfiguration : IEntityTypeConfiguration<OAuthClient>
{
    public void Configure(EntityTypeBuilder<OAuthClient> b)
    {
        b.ToTable("OAuthClients");
        b.HasKey(x => x.ClientId);
        b.Property(x => x.ClientId).HasMaxLength(64).ValueGeneratedNever();
        b.Property(x => x.ClientName).HasMaxLength(100).IsRequired();
        b.Property(x => x.RedirectUris).HasMaxLength(4000).IsRequired();
        b.Property(x => x.CreatedIp).HasMaxLength(64);
        b.Ignore(x => x.RedirectUriList);
    }
}

public sealed class OAuthAuthorizationConfiguration : IEntityTypeConfiguration<OAuthAuthorization>
{
    public void Configure(EntityTypeBuilder<OAuthAuthorization> b)
    {
        b.ToTable("OAuthAuthorizations");
        b.HasKey(x => x.OAuthAuthorizationId);
        b.Property(x => x.OAuthAuthorizationId).HasMaxLength(64).ValueGeneratedNever();
        b.Property(x => x.ClientId).HasMaxLength(64).IsRequired();
        b.Property(x => x.RedirectUri).HasMaxLength(1000).IsRequired();
        b.Property(x => x.State).HasMaxLength(500);
        b.Property(x => x.CodeChallenge).HasMaxLength(128).IsRequired();
        b.Property(x => x.Scope).HasMaxLength(200).IsRequired();
        b.Property(x => x.Resource).HasMaxLength(300).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.CodeHash).HasMaxLength(64);
        // แลก code ด้วย hash
        b.HasIndex(x => x.CodeHash).IsUnique().HasFilter("[CodeHash] IS NOT NULL");
        b.HasIndex(x => x.CreatedAt);
        b.HasOne<OAuthClient>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
    }
}
