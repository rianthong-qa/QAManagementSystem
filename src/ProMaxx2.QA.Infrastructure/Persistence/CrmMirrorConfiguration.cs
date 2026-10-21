using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Integrations;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class CrmTicketConfiguration : IEntityTypeConfiguration<CrmTicket>
{
    public void Configure(EntityTypeBuilder<CrmTicket> b)
    {
        b.ToTable("CrmTickets");
        b.HasKey(x => x.JobNo);
        b.Property(x => x.JobNo).HasMaxLength(40).ValueGeneratedNever();
        b.Property(x => x.Subject).HasMaxLength(300);
        b.Property(x => x.Status).HasMaxLength(30);
        b.Property(x => x.JobType).HasMaxLength(30);
        b.Property(x => x.ServiceType).HasMaxLength(200);
        b.Property(x => x.Product).HasMaxLength(200);
        b.Property(x => x.AssigneeCode).HasMaxLength(50);
        b.Property(x => x.OwnerCode).HasMaxLength(50);
        b.Property(x => x.DeveloperCode).HasMaxLength(50);
        b.Property(x => x.Branch).HasMaxLength(200);
        b.HasIndex(x => x.AssigneeCode);
        b.HasIndex(x => x.ContactDate);
    }
}

public sealed class CrmTicketEventConfiguration : IEntityTypeConfiguration<CrmTicketEvent>
{
    public void Configure(EntityTypeBuilder<CrmTicketEvent> b)
    {
        b.ToTable("CrmTicketEvents");
        b.HasKey(x => x.CrmTicketEventId);
        b.Property(x => x.JobNo).HasMaxLength(40).IsRequired();
        b.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        b.Property(x => x.Source).HasMaxLength(20).IsRequired();
        b.Property(x => x.FromStatus).HasMaxLength(30);
        b.Property(x => x.ToStatus).HasMaxLength(30);
        b.Property(x => x.FromAssigneeCode).HasMaxLength(50);
        b.Property(x => x.ToAssigneeCode).HasMaxLength(50);
        b.Property(x => x.FromDeveloperCode).HasMaxLength(50);
        b.Property(x => x.ToDeveloperCode).HasMaxLength(50);
        b.Property(x => x.OwnerCode).HasMaxLength(50);
        b.Property(x => x.AnswerNo).HasMaxLength(40);
        b.Property(x => x.ActorCode).HasMaxLength(50);
        b.HasIndex(x => new { x.JobNo, x.OccurredAt });
        b.HasIndex(x => x.OccurredAt);
        // ข้อความตอบหนึ่งรายการบันทึกครั้งเดียว (sync ซ้ำได้)
        b.HasIndex(x => new { x.JobNo, x.AnswerNo }).IsUnique().HasFilter("[AnswerNo] IS NOT NULL");
    }
}

public sealed class CrmMirrorRunConfiguration : IEntityTypeConfiguration<CrmMirrorRun>
{
    public void Configure(EntityTypeBuilder<CrmMirrorRun> b)
    {
        b.ToTable("CrmMirrorRuns");
        b.HasKey(x => x.CrmMirrorRunId);
        b.Property(x => x.CrmMirrorRunId).ValueGeneratedNever();
        b.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.AccountUsername).HasMaxLength(50);
        b.Property(x => x.Error).HasMaxLength(2000);
        b.HasIndex(x => x.StartedAt);
    }
}
