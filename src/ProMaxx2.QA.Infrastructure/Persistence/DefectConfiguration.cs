using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Domain.TestManagement;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class DefectConfiguration : IEntityTypeConfiguration<Defect>
{
    public void Configure(EntityTypeBuilder<Defect> b)
    {
        b.ToTable("Defects");
        b.HasKey(x => x.DefectId);
        b.Property(x => x.DefectId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.DefectCode).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.ProjectId, x.DefectCode }).IsUnique();
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.Severity).HasMaxLength(20).IsRequired();
        b.Property(x => x.Status).HasMaxLength(30).IsRequired();
        b.Property(x => x.CreatedAt).HasPrecision(0);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.StepsToReproduce).HasMaxLength(4000);
        b.Property(x => x.ExpectedResult).HasMaxLength(2000);
        b.Property(x => x.ActualResult).HasMaxLength(2000);
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Release>().WithMany().HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Build>().WithMany().HasForeignKey(x => x.BuildId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductModule>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DefectActivityConfiguration : IEntityTypeConfiguration<DefectActivity>
{
    public void Configure(EntityTypeBuilder<DefectActivity> b)
    {
        b.ToTable("DefectActivities");
        b.HasKey(x => x.DefectActivityId);
        b.Property(x => x.DefectActivityId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.ActionType).HasMaxLength(30).IsRequired();
        b.Property(x => x.Message).HasMaxLength(4000).IsRequired();
        b.Property(x => x.CreatedAt).HasPrecision(0);
        b.HasOne<Defect>().WithMany().HasForeignKey(x => x.DefectId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.DefectId);
        b.HasIndex(x => x.ActorUserId);
    }
}

public sealed class DefectTestCaseLinkConfiguration : IEntityTypeConfiguration<DefectTestCaseLink>
{
    public void Configure(EntityTypeBuilder<DefectTestCaseLink> b)
    {
        b.ToTable("DefectTestCaseLinks");
        b.HasKey(x => new { x.DefectId, x.TestCaseId });
        b.HasOne<Defect>().WithMany().HasForeignKey(x => x.DefectId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<TestCase>().WithMany().HasForeignKey(x => x.TestCaseId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.TestCaseId);
        b.HasIndex(x => x.LinkedByUserId);
    }
}
