using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Performance;
using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class EmployeeProfileConfiguration : IEntityTypeConfiguration<EmployeeProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeProfile> b)
    {
        b.ToTable("EmployeeProfiles");
        b.HasKey(x => x.UserId);
        b.Property(x => x.EmployeeCode).HasMaxLength(50);
        b.Property(x => x.Position).HasMaxLength(100);
        b.Property(x => x.Level).HasMaxLength(20);
        b.Property(x => x.EmploymentStatus).HasMaxLength(20).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.CreatedAt).HasPrecision(0);
        b.Property(x => x.UpdatedAt).HasPrecision(0);
        b.Ignore(x => x.IsOnProbation);
        b.HasOne<User>().WithOne().HasForeignKey<EmployeeProfile>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.SupervisorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.EmploymentStatus);
    }
}

public sealed class WorkLogEntryConfiguration : IEntityTypeConfiguration<WorkLogEntry>
{
    public void Configure(EntityTypeBuilder<WorkLogEntry> b)
    {
        b.ToTable("WorkLogEntries");
        b.HasKey(x => x.WorkLogEntryId);
        b.Property(x => x.WorkLogEntryId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.Category).HasMaxLength(30).IsRequired();
        b.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.EffortHours).HasPrecision(5, 2);
        b.Property(x => x.ReferenceUrl).HasMaxLength(500);
        b.Property(x => x.CreatedAt).HasPrecision(0);
        b.Property(x => x.UpdatedAt).HasPrecision(0);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.UserId, x.WorkDate });
    }
}

public sealed class EvaluationTemplateConfiguration : IEntityTypeConfiguration<EvaluationTemplate>
{
    public void Configure(EntityTypeBuilder<EvaluationTemplate> b)
    {
        b.ToTable("EvaluationTemplates");
        b.HasKey(x => x.EvaluationTemplateId);
        b.Property(x => x.EvaluationTemplateId).ValueGeneratedNever();
        b.Property(x => x.Type).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.CreatedAt).HasPrecision(0);
        b.Property(x => x.UpdatedAt).HasPrecision(0);
        b.HasMany(x => x.Criteria).WithOne().HasForeignKey(x => x.EvaluationTemplateId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.Type, x.IsActive });
    }
}

public sealed class EvaluationTemplateCriterionConfiguration : IEntityTypeConfiguration<EvaluationTemplateCriterion>
{
    public void Configure(EntityTypeBuilder<EvaluationTemplateCriterion> b)
    {
        b.ToTable("EvaluationTemplateCriteria");
        b.HasKey(x => x.EvaluationTemplateCriterionId);
        b.Property(x => x.EvaluationTemplateCriterionId).ValueGeneratedNever();
        b.Property(x => x.Group).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
    }
}

public sealed class EvaluationConfiguration : IEntityTypeConfiguration<Evaluation>
{
    public void Configure(EntityTypeBuilder<Evaluation> b)
    {
        b.ToTable("Evaluations");
        b.HasKey(x => x.EvaluationId);
        b.Property(x => x.EvaluationId).ValueGeneratedNever();
        b.Property(x => x.Type).HasMaxLength(20).IsRequired();
        b.Property(x => x.Round).HasMaxLength(10).IsRequired();
        b.Property(x => x.TemplateName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.SelfComment).HasMaxLength(4000);
        b.Property(x => x.Strengths).HasMaxLength(4000);
        b.Property(x => x.Improvements).HasMaxLength(4000);
        b.Property(x => x.NextGoals).HasMaxLength(4000);
        b.Property(x => x.EmployeeComment).HasMaxLength(4000);
        b.Property(x => x.ReopenReason).HasMaxLength(1000);
        b.Property(x => x.Recommendation).HasMaxLength(20);
        b.Property(x => x.Grade).HasMaxLength(2);
        b.Property(x => x.OverallScore).HasPrecision(4, 2);
        b.Property(x => x.SelfSubmittedAt).HasPrecision(0);
        b.Property(x => x.CompletedAt).HasPrecision(0);
        b.Property(x => x.AcknowledgedAt).HasPrecision(0);
        b.Property(x => x.CreatedAt).HasPrecision(0);
        b.Property(x => x.UpdatedAt).HasPrecision(0);
        b.Ignore(x => x.IsFinalProbation);
        b.Ignore(x => x.IsClosed);
        b.HasMany(x => x.Scores).WithOne().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.EvaluatorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.UserId, x.Round, x.Year });
        b.HasIndex(x => new { x.Status, x.DueDate });
    }
}

public sealed class EvaluationScoreConfiguration : IEntityTypeConfiguration<EvaluationScore>
{
    public void Configure(EntityTypeBuilder<EvaluationScore> b)
    {
        b.ToTable("EvaluationScores");
        b.HasKey(x => x.EvaluationScoreId);
        b.Property(x => x.EvaluationScoreId).ValueGeneratedNever();
        b.Property(x => x.Group).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.SelfComment).HasMaxLength(1000);
        b.Property(x => x.ManagerComment).HasMaxLength(1000);
    }
}

public sealed class WorkLogAttachmentConfiguration : IEntityTypeConfiguration<WorkLogAttachment>
{
    public void Configure(EntityTypeBuilder<WorkLogAttachment> b)
    {
        b.ToTable("WorkLogAttachments");
        b.HasKey(x => x.WorkLogAttachmentId);
        b.Property(x => x.WorkLogAttachmentId).ValueGeneratedNever();
        b.Property(x => x.FileName).HasMaxLength(100).IsRequired();
        b.Property(x => x.StoredFileName).HasMaxLength(150).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(50).IsRequired();
        b.Property(x => x.UploadedAt).HasPrecision(0);
        b.HasOne<WorkLogEntry>().WithMany().HasForeignKey(x => x.WorkLogEntryId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.WorkLogEntryId);
    }
}

public sealed class EvaluationNotificationConfiguration : IEntityTypeConfiguration<EvaluationNotification>
{
    public void Configure(EntityTypeBuilder<EvaluationNotification> b)
    {
        b.ToTable("EvaluationNotifications");
        b.HasKey(x => x.EvaluationNotificationId);
        b.Property(x => x.EvaluationNotificationId).ValueGeneratedNever();
        b.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        b.Property(x => x.SentAt).HasPrecision(0);
        b.HasOne<Evaluation>().WithMany().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.EvaluationId, x.Kind, x.DueDate, x.RecipientUserId }).IsUnique();
    }
}

public sealed class PerformanceShareLinkConfiguration : IEntityTypeConfiguration<PerformanceShareLink>
{
    public void Configure(EntityTypeBuilder<PerformanceShareLink> b)
    {
        b.ToTable("PerformanceShareLinks");
        b.HasKey(x => x.PerformanceShareLinkId);
        b.Property(x => x.PerformanceShareLinkId).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(12).IsRequired();
        b.Property(x => x.Note).HasMaxLength(200);
        b.Property(x => x.ExpiresAt).HasPrecision(0);
        b.Property(x => x.CreatedAt).HasPrecision(0);
        b.Property(x => x.RevokedAt).HasPrecision(0);
        b.Property(x => x.LastViewedAt).HasPrecision(0);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.RevokedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}

public sealed class WeeklyReportNoteConfiguration : IEntityTypeConfiguration<WeeklyReportNote>
{
    public void Configure(EntityTypeBuilder<WeeklyReportNote> b)
    {
        b.ToTable("WeeklyReportNotes");
        b.HasKey(x => x.WeeklyReportNoteId);
        b.Property(x => x.WeeklyReportNoteId).ValueGeneratedNever();
        b.Property(x => x.Health).HasMaxLength(10);
        b.Property(x => x.Headline).HasMaxLength(WeeklyReportNote.MaxShortText);
        b.Property(x => x.KeyRisk).HasMaxLength(WeeklyReportNote.MaxLongText);
        b.Property(x => x.Ask).HasMaxLength(WeeklyReportNote.MaxLongText);
        b.Property(x => x.NextWeekPlan).HasMaxLength(WeeklyReportNote.MaxLongText);
        b.Property(x => x.CreatedAt).HasPrecision(0);
        b.Property(x => x.UpdatedAt).HasPrecision(0);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.WeekStart).IsUnique();
    }
}
