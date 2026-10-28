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
        b.Property(x => x.CrmTicketId).HasMaxLength(50);
        b.Property(x => x.CrmSyncStatus).HasMaxLength(20).IsRequired().HasDefaultValue("None");
        b.Property(x => x.CrmLastSyncedAt).HasPrecision(0);
        b.Property(x => x.CrmLastKnownStatus).HasMaxLength(50);
        b.Property(x => x.CrmLastKnownAssignto).HasMaxLength(20);
        b.Property(x => x.CrmLastSeenAnswerNo).HasMaxLength(20);
        // ผลการปิด (Phase 6 คลังความรู้) — RootCauseCategory = MasterOption.Value หมวด DefectRootCause
        b.Property(x => x.ResolutionType).HasMaxLength(20);
        b.Property(x => x.RootCauseCategory).HasMaxLength(50);
        b.Property(x => x.RootCauseNote).HasMaxLength(2000);
        b.Property(x => x.ResolutionNote).HasMaxLength(2000);
        b.HasOne<Defect>().WithMany().HasForeignKey(x => x.DuplicateOfDefectId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => x.DuplicateOfDefectId);
        // วงจรข้าม Build: แก้ใน Build ไหน / ตรวจซ้ำผ่านใน Build ไหน / Defect เดิมที่ปัญหากลับมา (DEFECT_BUILD_LIFECYCLE.md)
        b.HasOne<Build>().WithMany().HasForeignKey(x => x.FixedInBuildId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Build>().WithMany().HasForeignKey(x => x.VerifiedInBuildId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Defect>().WithMany().HasForeignKey(x => x.RegressionOfDefectId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => x.FixedInBuildId);
        b.HasIndex(x => x.RegressionOfDefectId);
        b.Property(x => x.ResolvedAt).HasPrecision(0);
        b.Property(x => x.ClosedAt).HasPrecision(0);
        b.Property(x => x.LastRetestAt).HasPrecision(0);
        b.Property(x => x.LastRetestResult).HasMaxLength(10);
        // Defect ค้าง/คิวตรวจซ้ำของ Build ใหม่ กรองตาม Release + สถานะ
        b.HasIndex(x => new { x.ReleaseId, x.Status });
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Release>().WithMany().HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Build>().WithMany().HasForeignKey(x => x.BuildId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductModule>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
        // รายการ Defect กรองตาม Project + IsDeleted แล้วเรียงตาม CreatedAt เสมอ (List/Stats)
        b.HasIndex(x => new { x.ProjectId, x.IsDeleted, x.CreatedAt });
        // หน้าผลงานรายคนนับ Defect ที่แต่ละคนแจ้งในช่วงวันที่
        b.HasIndex(x => new { x.CreatedBy, x.CreatedAt });
    }
}

public sealed class DefectAttachmentConfiguration : IEntityTypeConfiguration<DefectAttachment>
{
    public void Configure(EntityTypeBuilder<DefectAttachment> b)
    {
        b.ToTable("DefectAttachments");
        b.HasKey(x => x.DefectAttachmentId);
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.StoredFileName).HasMaxLength(300).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.UploadedAt).HasPrecision(0);
        b.HasOne<Defect>().WithMany().HasForeignKey(x => x.DefectId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.DefectId);
        b.HasIndex(x => x.CommentId);
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
        b.Property(x => x.FromValue).HasMaxLength(50);
        b.Property(x => x.ToValue).HasMaxLength(50);
        b.HasOne<Defect>().WithMany().HasForeignKey(x => x.DefectId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Build>().WithMany().HasForeignKey(x => x.BuildId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => x.DefectId);
        b.HasIndex(x => x.ActorUserId);
        b.HasIndex(x => new { x.CreatedAt, x.DefectActivityId });
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
