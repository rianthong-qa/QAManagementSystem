using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Knowledge;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> b)
    {
        b.ToTable("KnowledgeDocuments");
        b.HasKey(x => x.KnowledgeDocumentId);
        b.Property(x => x.KnowledgeDocumentId).ValueGeneratedNever();
        b.Property(x => x.SourceType).HasMaxLength(20).IsRequired();
        b.Property(x => x.SourceKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.Scope).HasMaxLength(20).IsRequired();
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.SourceUrl).HasMaxLength(500);
        b.Property(x => x.ContentHash).HasMaxLength(64);
        b.Property(x => x.State).HasMaxLength(20).IsRequired();
        b.Property(x => x.Error).HasMaxLength(2000);
        // 1 แหล่งข้อมูล = 1 เอกสาร
        b.HasIndex(x => new { x.SourceType, x.SourceKey }).IsUnique();
        // worker หาเอกสารที่ต้อง index
        b.HasIndex(x => x.State);
        // กรองสิทธิ์ตอนค้น
        b.HasIndex(x => new { x.Scope, x.ProjectId });
    }
}

public sealed class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunk> b)
    {
        b.ToTable("KnowledgeChunks");
        // Full-Text Index ต้องใช้ KEY INDEX ที่เป็น unique, single-column, non-nullable — ใช้ int identity (PK_KnowledgeChunks)
        b.HasKey(x => x.KnowledgeChunkId);
        b.Property(x => x.KnowledgeChunkId).UseIdentityColumn();
        b.Property(x => x.Text).IsRequired();
        b.Property(x => x.EmbeddingModel).HasMaxLength(100);
        b.HasIndex(x => new { x.KnowledgeDocumentId, x.ChunkNo }).IsUnique();
        b.HasOne<KnowledgeDocument>().WithMany().HasForeignKey(x => x.KnowledgeDocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class KnowledgeArticleConfiguration : IEntityTypeConfiguration<KnowledgeArticle>
{
    public void Configure(EntityTypeBuilder<KnowledgeArticle> b)
    {
        b.ToTable("KnowledgeArticles");
        b.HasKey(x => x.KnowledgeArticleId);
        b.Property(x => x.KnowledgeArticleId).ValueGeneratedNever();
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.BodyMarkdown).IsRequired();
        b.Property(x => x.Tags).HasMaxLength(500);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        // บทความเดิมทั้งหมดเป็น Markdown; BodyHtml (nvarchar(max)) มีค่าเฉพาะบทความที่นำเข้าจาก HTML
        b.Property(x => x.ContentFormat).HasMaxLength(10).IsRequired().HasDefaultValue("Markdown");
        b.Property(x => x.BodyHtml);
        b.HasIndex(x => new { x.IsDeleted, x.ProjectId });
    }
}

public sealed class KnowledgeArticleShareLinkConfiguration : IEntityTypeConfiguration<KnowledgeArticleShareLink>
{
    public void Configure(EntityTypeBuilder<KnowledgeArticleShareLink> b)
    {
        b.ToTable("KnowledgeArticleShareLinks");
        b.HasKey(x => x.KnowledgeArticleShareLinkId);
        b.Property(x => x.KnowledgeArticleShareLinkId).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(12).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        // บทความละ 1 ลิงก์ที่ยังใช้งานอยู่ — กันสร้างซ้อนเมื่อกดแชร์พร้อมกัน (filtered index: apply ด้วย sqlcmd -I)
        b.HasIndex(x => x.KnowledgeArticleId).IsUnique().HasFilter("[RevokedAt] IS NULL");
        b.Property(x => x.CreatedAt).HasPrecision(0);
        b.Property(x => x.RevokedAt).HasPrecision(0);
    }
}

public sealed class KnowledgeFileConfiguration : IEntityTypeConfiguration<KnowledgeFile>
{
    public void Configure(EntityTypeBuilder<KnowledgeFile> b)
    {
        b.ToTable("KnowledgeFiles");
        b.HasKey(x => x.KnowledgeFileId);
        b.Property(x => x.KnowledgeFileId).ValueGeneratedNever();
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        b.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.ExtractError).HasMaxLength(1000);
        b.Property(x => x.ContentHash).HasMaxLength(64).IsRequired().HasDefaultValue("");
        b.Property(x => x.Origin).HasMaxLength(20).IsRequired().HasDefaultValue("Upload");
        b.Property(x => x.ImportPath).HasMaxLength(300);
        b.HasIndex(x => new { x.IsDeleted, x.ProjectId });
        // กันไฟล์ซ้ำ / นำเข้าซ้ำ
        b.HasIndex(x => x.ContentHash);
        b.HasIndex(x => x.ImportPath).HasFilter("[ImportPath] IS NOT NULL");
    }
}

public sealed class CrmTicketContentConfiguration : IEntityTypeConfiguration<CrmTicketContent>
{
    public void Configure(EntityTypeBuilder<CrmTicketContent> b)
    {
        b.ToTable("CrmTicketContents");
        b.HasKey(x => x.JobNo);
        b.Property(x => x.JobNo).HasMaxLength(40).ValueGeneratedNever();
        b.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.SubjectMasked).HasMaxLength(300);
        b.Property(x => x.FetchError).HasMaxLength(500);
        b.HasOne<Domain.Integrations.CrmTicket>().WithOne().HasForeignKey<CrmTicketContent>(x => x.JobNo).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CrmTicketAnswerConfiguration : IEntityTypeConfiguration<CrmTicketAnswer>
{
    public void Configure(EntityTypeBuilder<CrmTicketAnswer> b)
    {
        b.ToTable("CrmTicketAnswers");
        b.HasKey(x => x.CrmTicketAnswerId);
        b.Property(x => x.JobNo).HasMaxLength(40).IsRequired();
        b.Property(x => x.AnswerNo).HasMaxLength(40).IsRequired();
        b.Property(x => x.AnswerType).HasMaxLength(10);
        b.HasIndex(x => new { x.JobNo, x.AnswerNo }).IsUnique();
        b.HasOne<Domain.Integrations.CrmTicket>().WithMany().HasForeignKey(x => x.JobNo).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    public void Configure(EntityTypeBuilder<ChatSession> b)
    {
        b.ToTable("ChatSessions");
        b.HasKey(x => x.ChatSessionId);
        b.Property(x => x.ChatSessionId).ValueGeneratedNever();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        // รายการบทสนทนาของผู้ใช้ เรียงล่าสุดก่อน
        b.HasIndex(x => new { x.UserId, x.IsDeleted, x.UpdatedAt });
    }
}

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> b)
    {
        b.ToTable("ChatMessages");
        b.HasKey(x => x.ChatMessageId);
        b.Property(x => x.Role).HasMaxLength(20).IsRequired();
        b.Property(x => x.Content).IsRequired();
        b.Property(x => x.Model).HasMaxLength(100);
        b.Property(x => x.FeedbackNote).HasMaxLength(1000);
        b.HasIndex(x => new { x.ChatSessionId, x.ChatMessageId });
        b.HasOne<ChatSession>().WithMany().HasForeignKey(x => x.ChatSessionId).OnDelete(DeleteBehavior.Cascade);
    }
}
