using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Domain.Requirements;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Domain.Execution;

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
    public DbSet<TestCase> TestCases => Set<TestCase>();
    public DbSet<TestStep> TestSteps => Set<TestStep>();
    public DbSet<RequirementTestCase> RequirementTestCases => Set<RequirementTestCase>();
    public DbSet<TestSuite> TestSuites => Set<TestSuite>();
    public DbSet<TestSuiteCase> TestSuiteCases => Set<TestSuiteCase>();
    public DbSet<TestEnvironment> TestEnvironments => Set<TestEnvironment>();
    public DbSet<TestCycle> TestCycles => Set<TestCycle>();
    public DbSet<TestCycleCase> TestCycleCases => Set<TestCycleCase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyConfigurationsFromAssembly(typeof(QaDbContext).Assembly);
}
