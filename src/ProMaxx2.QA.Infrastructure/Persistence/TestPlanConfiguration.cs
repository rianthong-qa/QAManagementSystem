using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Domain.TestPlanning;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class TestPlanConfiguration : IEntityTypeConfiguration<TestPlan>
{
    public void Configure(EntityTypeBuilder<TestPlan> b)
    {
        b.ToTable("TestPlans");
        b.HasKey(x => x.TestPlanId);
        b.Property(x => x.TestPlanId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.PlanCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.PlanName).HasMaxLength(200).IsRequired();
        b.Property(x => x.PlanType).HasMaxLength(50).IsRequired();
        b.Property(x => x.LifecycleStatus).HasMaxLength(30).IsRequired().HasDefaultValue(TestPlanLifecycleStatuses.Active);
        b.Property(x => x.StartDate).HasPrecision(0);
        b.Property(x => x.EndDate).HasPrecision(0);
        b.Property(x => x.FrozenAt).HasPrecision(0);
        b.Property(x => x.CreatedAt).HasPrecision(0);
        b.Property(x => x.UpdatedAt).HasPrecision(0);
        b.Property(x => x.DeletedAt).HasPrecision(0);
        b.Ignore(x => x.IsActive);
        b.HasIndex(x => new { x.ProjectId, x.PlanCode }).IsUnique();
        b.HasIndex(x => new { x.ProjectId, x.ReleaseId, x.LifecycleStatus });
        b.HasIndex(x => new { x.ProjectId, x.IsDeleted, x.CreatedAt });
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Release>().WithMany().HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.FrozenByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TestPlan>().WithMany().HasForeignKey(x => x.CopiedFromTestPlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TestPlanRunConfiguration : IEntityTypeConfiguration<TestPlanRun>
{
    public void Configure(EntityTypeBuilder<TestPlanRun> b)
    {
        b.ToTable("TestPlanRuns");
        b.HasKey(x => x.TestPlanRunId);
        b.Property(x => x.TestPlanRunId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.RunCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.RunName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ConfigurationKey).HasMaxLength(100);
        b.Property(x => x.ConfigurationName).HasMaxLength(200);
        b.Property(x => x.CreatedAt).HasPrecision(0);
        b.Property(x => x.UpdatedAt).HasPrecision(0);
        b.HasIndex(x => new { x.TestPlanId, x.RunCode }).IsUnique();
        // MVP: Test Cycle 1 ชุดอยู่ได้ไม่เกิน 1 Plan ทั้งระบบ (รวม Plan ที่ถูก soft delete) — กันตัวเลขซ้ำใน Report
        b.HasIndex(x => x.TestCycleId).IsUnique();
        b.HasIndex(x => new { x.TestPlanId, x.ConfigurationKey });
        b.HasOne(x => x.Plan).WithMany(x => x.Runs).HasForeignKey(x => x.TestPlanId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Cycle).WithMany().HasForeignKey(x => x.TestCycleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TestPlanRunAssignmentConfiguration : IEntityTypeConfiguration<TestPlanRunAssignment>
{
    public void Configure(EntityTypeBuilder<TestPlanRunAssignment> b)
    {
        b.ToTable("TestPlanRunAssignments");
        b.HasKey(x => x.TestPlanRunAssignmentId);
        b.Property(x => x.TestPlanRunAssignmentId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.Role).HasMaxLength(30).IsRequired();
        b.Property(x => x.AssignedAt).HasPrecision(0);
        b.HasIndex(x => new { x.TestPlanRunId, x.TesterUserId, x.Role }).IsUnique();
        b.HasOne<TestPlanRun>().WithMany(x => x.Assignments).HasForeignKey(x => x.TestPlanRunId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.TesterUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
