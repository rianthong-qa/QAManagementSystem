using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Domain.Requirements;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class QaDbContext(DbContextOptions<QaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProductModule> Modules => Set<ProductModule>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<Build> Builds => Set<Build>();
    public DbSet<Requirement> Requirements => Set<Requirement>();
    public DbSet<RequirementRevision> RequirementRevisions => Set<RequirementRevision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyConfigurationsFromAssembly(typeof(QaDbContext).Assembly);
}
