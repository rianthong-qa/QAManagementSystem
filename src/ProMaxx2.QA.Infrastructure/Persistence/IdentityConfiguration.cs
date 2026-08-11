using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Identity;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users"); b.HasKey(x => x.UserId); b.Property(x => x.UserId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.Username).HasMaxLength(100).IsRequired(); b.HasIndex(x => x.Username).IsUnique();
        b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired(); b.Property(x => x.Email).HasMaxLength(255);
        b.Property(x => x.PasswordHash).IsRequired(); b.Property(x => x.CreatedAt).HasPrecision(0); b.Property(x => x.LastLoginAt).HasPrecision(0); b.Property(x => x.UpdatedAt).HasPrecision(0);
    }
}
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b) { b.ToTable("Roles"); b.HasKey(x=>x.RoleId); b.Property(x=>x.RoleId).HasDefaultValueSql("NEWSEQUENTIALID()"); b.Property(x=>x.RoleCode).HasMaxLength(50).IsRequired(); b.HasIndex(x=>x.RoleCode).IsUnique(); b.Property(x=>x.RoleName).HasMaxLength(100).IsRequired(); b.Property(x=>x.Description).HasMaxLength(500); }
}
public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b) { b.ToTable("Permissions"); b.HasKey(x=>x.PermissionId); b.Property(x=>x.PermissionId).HasDefaultValueSql("NEWSEQUENTIALID()"); b.Property(x=>x.PermissionCode).HasMaxLength(100).IsRequired(); b.HasIndex(x=>x.PermissionCode).IsUnique(); b.Property(x=>x.PermissionName).HasMaxLength(200).IsRequired(); b.Property(x=>x.ModuleArea).HasMaxLength(100); }
}
public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b) { b.ToTable("UserRoles"); b.HasKey(x=>new{x.UserId,x.RoleId}); b.Property(x=>x.CreatedAt).HasPrecision(0); b.HasOne(x=>x.User).WithMany(x=>x.UserRoles).HasForeignKey(x=>x.UserId); b.HasOne(x=>x.Role).WithMany(x=>x.UserRoles).HasForeignKey(x=>x.RoleId); }
}
public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b) { b.ToTable("RolePermissions"); b.HasKey(x=>new{x.RoleId,x.PermissionId}); b.HasOne(x=>x.Role).WithMany(x=>x.RolePermissions).HasForeignKey(x=>x.RoleId); b.HasOne(x=>x.Permission).WithMany(x=>x.RolePermissions).HasForeignKey(x=>x.PermissionId); }
}
