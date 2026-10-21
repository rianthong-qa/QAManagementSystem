using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Domain.Knowledge;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Phase 0 ของคลังความรู้ (AI_KNOWLEDGE_CHAT_PLAN.md): masking, chunking, hash, entity rules และการตั้งค่า Embedding</summary>
public sealed class KnowledgeFoundationTests
{
    // ---------- SensitiveDataMasker ----------

    [Theory]
    [InlineData("ติดต่อ somchai.k@shop-abc.co.th ได้เลย", "ติดต่อ [EMAIL] ได้เลย")]
    [InlineData("โทร 081-234-5678 ครับ", "โทร [PHONE] ครับ")]
    [InlineData("เบอร์ร้าน 02-123-4567", "เบอร์ร้าน [PHONE]")]
    [InlineData("มือถือ 0812345678", "มือถือ [PHONE]")]
    [InlineData("+66 81 234 5678 แจ้งปัญหา", "[PHONE] แจ้งปัญหา")]
    [InlineData("เลขบัตร 1103700123456 ของลูกค้า", "เลขบัตร [NATIONAL_ID] ของลูกค้า")]
    [InlineData("บัตร 1-1037-00123-45-6", "บัตร [NATIONAL_ID]")]
    [InlineData("ชำระด้วย 4111 1111 1111 1111", "ชำระด้วย [CARD]")]
    [InlineData("server 192.168.200.219 ล่ม", "server [IP] ล่ม")]
    [InlineData("ต่อ 10.1.2.3 ไม่ได้", "ต่อ [IP] ไม่ได้")]
    public void Masker_ReplacesPersonalData(string input, string expected) =>
        Assert.Equal(expected, SensitiveDataMasker.Mask(input));

    [Fact]
    public void Masker_HidesSecretsButKeepsKeyName()
    {
        var masked = SensitiveDataMasker.Mask("Server=db;User Id=sa;Password=P@ssw0rd!;Encrypt=false และ api_key: sk-abc123456789");
        Assert.Contains("Password=[SECRET]", masked);
        Assert.Contains("api_key: [SECRET]", masked);
        Assert.DoesNotContain("P@ssw0rd!", masked);
        Assert.DoesNotContain("sk-abc123456789", masked);
        Assert.Contains("Encrypt=false", masked);
    }

    [Fact]
    public void Masker_HidesBearerAndJwt()
    {
        var masked = SensitiveDataMasker.Mask("Authorization: Bearer abcdEFGH12345678 และ token eyJhbGciOi.eyJzdWIiOiIx.SflKxwRJSM");
        Assert.Contains("Bearer [SECRET]", masked);
        Assert.DoesNotContain("eyJhbGciOi", masked);
    }

    [Theory]
    [InlineData("Build 10.0.228 แก้แล้ว")]
    [InlineData("REL-2026.09 รอบ 3")]
    [InlineData("วันที่ 2026-10-21 เวลา 10:30")]
    [InlineData("DEF-1021 ยอดรวม 1,250.00 บาท")]
    public void Masker_LeavesBusinessDataUntouched(string input) =>
        Assert.Equal(input, SensitiveDataMasker.Mask(input));

    [Fact]
    public void Masker_ReplacesSourceTermsLongestFirst()
    {
        var masked = SensitiveDataMasker.Mask("ร้านสมใจพาณิชย์ สาขาบางนา แจ้งว่า สมใจ ใช้งานไม่ได้ ตอบโดย คุณวิภา",
            [new("สมใจ", "CUSTOMER"), new("ร้านสมใจพาณิชย์", "CUSTOMER"), new("บางนา", "BRANCH"), new("คุณวิภา", "[staff]"), new(" ", "X")]);
        Assert.Equal("[CUSTOMER] สาขา[BRANCH] แจ้งว่า [CUSTOMER] ใช้งานไม่ได้ ตอบโดย [STAFF]", masked);
    }

    [Fact]
    public void Masker_HandlesEmpty()
    {
        Assert.Equal("", SensitiveDataMasker.Mask(null));
        Assert.Equal("", SensitiveDataMasker.Mask(""));
    }

    // ---------- TextChunker ----------

    [Fact]
    public void Chunker_ShortTextIsSingleNormalizedChunk()
    {
        var chunks = TextChunker.Split("  ปัญหา:   พิมพ์ใบกำกับภาษี\r\n\r\n\r\nไม่ได้  ");
        Assert.Equal(["ปัญหา: พิมพ์ใบกำกับภาษี\n\nไม่ได้"], chunks);
    }

    [Fact]
    public void Chunker_EmptyTextHasNoChunks() => Assert.Empty(TextChunker.Split("   \n\n "));

    [Fact]
    public void Chunker_SplitsParagraphsWithinLimitAndOverlaps()
    {
        var paragraphs = Enumerable.Range(1, 12).Select(i => $"ย่อหน้าที่ {i} " + string.Join(" ", Enumerable.Repeat($"คำ{i}", 40))).ToList();
        var chunks = TextChunker.Split(string.Join("\n\n", paragraphs), maxChars: 600, overlapChars: 80);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 600, $"chunk ยาว {c.Length}"));
        // ทุกย่อหน้าต้องอยู่ในอย่างน้อยหนึ่ง chunk (ไม่มีเนื้อหาหาย)
        Assert.All(paragraphs, p => Assert.Contains(chunks, c => c.Contains(p[..20], StringComparison.Ordinal)));
        // chunk ถัดไปเริ่มด้วยท้ายของ chunk ก่อนหน้า
        var tail = chunks[0][^30..].Trim();
        Assert.Contains(tail.Split(' ').Last(), chunks[1]);
    }

    [Fact]
    public void Chunker_HardSplitsLongParagraphAtSpaces()
    {
        var longParagraph = string.Join(" ", Enumerable.Range(1, 400).Select(i => $"w{i}"));
        var chunks = TextChunker.Split(longParagraph, maxChars: 300, overlapChars: 50);

        Assert.All(chunks, c => Assert.True(c.Length <= 300));
        // ไม่ตัดกลางคำ
        Assert.All(chunks, c => Assert.Matches(@"^w\d+(\s+w\d+)*$", c));
        Assert.Contains(chunks, c => c.EndsWith("w400", StringComparison.Ordinal));
    }

    [Fact]
    public void Chunker_StartsNewPieceAtMarkdownHeading()
    {
        var body = "# วิธีปิดวัน\n" + new string('ก', 500) + "\n# วิธีเปิดวัน\n" + new string('ข', 500);
        var chunks = TextChunker.Split(body, maxChars: 600, overlapChars: 0);
        Assert.Equal(2, chunks.Count);
        Assert.StartsWith("# วิธีปิดวัน", chunks[0]);
        Assert.StartsWith("# วิธีเปิดวัน", chunks[1]);
    }

    [Fact]
    public void Chunker_RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Split("x", maxChars: 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Split("x", maxChars: 200, overlapChars: 150));
    }

    // ---------- KnowledgeHash ----------

    [Fact]
    public void Hash_IsStableLowercaseSha256()
    {
        var hash = KnowledgeHash.Compute("ปัญหา: ปิดวันไม่ได้");
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, KnowledgeHash.Compute("ปัญหา: ปิดวันไม่ได้"));
        Assert.NotEqual(hash, KnowledgeHash.Compute("ปัญหา: เปิดวันไม่ได้"));
        Assert.Equal(hash.ToLowerInvariant(), hash);
    }

    // ---------- Entity rules ----------

    [Fact]
    public void Document_ProjectScopeRequiresProject() =>
        Assert.Throws<ArgumentException>(() => new KnowledgeDocument("Defect", "d1", "Project", null, "DEF-1", null));

    [Fact]
    public void Document_LifecycleAndCustomerFlag()
    {
        var doc = new KnowledgeDocument("crmticket", "HD-1", "crm", null, "  ", "#/crm");
        Assert.Equal("CrmTicket", doc.SourceType);
        Assert.Equal("Crm", doc.Scope);
        Assert.Equal("HD-1", doc.Title);
        Assert.True(doc.ContainsCustomerData);
        Assert.Equal("Stale", doc.State);

        doc.MarkIndexed("abc", 3, DateTime.UtcNow);
        Assert.Equal(("Indexed", 3), (doc.State, doc.ChunkCount));

        doc.Exclude();
        doc.MarkStale();
        Assert.Equal("Excluded", doc.State);

        doc.Restore();
        Assert.Equal("Stale", doc.State);
        Assert.Null(doc.ContentHash);
    }

    [Fact]
    public void Chunk_EmbeddingRoundTrips()
    {
        var chunk = new KnowledgeChunk(Guid.NewGuid(), 0, "ข้อความ");
        chunk.SetEmbedding([0.25f, -1.5f, 3f], "bge-m3");
        Assert.Equal(3, chunk.EmbeddingDim);
        Assert.Equal(12, chunk.Embedding!.Length);
        Assert.Equal([0.25f, -1.5f, 3f], chunk.GetEmbedding()!);
    }

    [Fact]
    public void Article_ValidatesStatusAndTitle()
    {
        Assert.Throws<ArgumentException>(() => new KnowledgeArticle(" ", "body", null, null, "Draft", null));
        Assert.Throws<ArgumentException>(() => new KnowledgeArticle("t", "body", null, null, "Archived", null));
        Assert.Equal("Published", new KnowledgeArticle("t", "body", null, null, "published", null).Status);
    }

    [Fact]
    public void File_AllowsOnlySupportedTypes()
    {
        Assert.Throws<ArgumentException>(() => new KnowledgeFile("virus.exe", "application/octet-stream", 1, "p", null, null, null));
        Assert.Equal("คู่มือ.DOCX", new KnowledgeFile("คู่มือ.DOCX", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1, "p", null, null, null).FileName);
    }

    [Fact]
    public void ChatMessage_FeedbackOnlyForAssistant()
    {
        var question = new ChatMessage(Guid.NewGuid(), "user", "ถาม");
        Assert.Throws<InvalidOperationException>(() => question.SetFeedback(1, null));
        var answer = new ChatMessage(Guid.NewGuid(), "assistant", "ตอบ");
        Assert.Throws<ArgumentException>(() => answer.SetFeedback(2, null));
        answer.SetFeedback(-1, "  ไม่ตรงคำถาม ");
        Assert.Equal((short)-1, answer.Feedback);
        Assert.Equal("ไม่ตรงคำถาม", answer.FeedbackNote);
    }

    // ---------- Embedding settings (SharedAiConfigurationService.SaveKnowledgeAsync) ----------

    [Fact]
    public async Task SaveKnowledge_RequiresMainAiConfigurationFirst()
    {
        await using var db = CreateDb();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).SaveKnowledgeAsync("Local", "bge-m3", "http://localhost:11434/v1", null, false, false, default));
        Assert.Contains("AI หลัก", ex.Message);
    }

    [Fact]
    public async Task SaveKnowledge_LocalNeedsHttpBaseUrl_KeyOptional()
    {
        await using var db = await CreateDbWithAiAsync();
        var service = CreateService(db);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveKnowledgeAsync("Local", "bge-m3", "ftp://x", null, false, false, default));

        var view = await service.SaveKnowledgeAsync("local", "bge-m3", "http://localhost:11434/v1/", null, false, true, default);
        Assert.Equal(("Local", "bge-m3", "http://localhost:11434/v1", false, true), (view.EmbeddingProvider, view.EmbeddingModel, view.EmbeddingBaseUrl, view.HasEmbeddingApiKey, view.AllowCrmDataToExternalAi));
    }

    [Fact]
    public async Task SaveKnowledge_OpenAiRequiresKey_KeepsKeyUntilProviderChanges()
    {
        await using var db = await CreateDbWithAiAsync();
        var service = CreateService(db);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveKnowledgeAsync("OpenAI", "text-embedding-3-small", null, null, false, false, default));

        var saved = await service.SaveKnowledgeAsync("OpenAI", "text-embedding-3-small", "https://evil.example", "sk-embed-12345678", false, false, default);
        Assert.Equal(("https://api.openai.com/v1", true, "••••5678"), (saved.EmbeddingBaseUrl, saved.HasEmbeddingApiKey, saved.EmbeddingApiKeyHint));
        var row = await db.AiConfigurations.SingleAsync();
        Assert.DoesNotContain("sk-embed", row.EncryptedEmbeddingApiKey);
        Assert.NotEqual(row.EncryptedApiKey, row.EncryptedEmbeddingApiKey);

        // บันทึกซ้ำโดยไม่กรอกคีย์ = ใช้คีย์เดิม
        Assert.True((await service.SaveKnowledgeAsync("OpenAI", "text-embedding-3-large", null, null, false, false, default)).HasEmbeddingApiKey);

        // เปลี่ยนเป็น Local = ล้างคีย์ OpenAI
        Assert.False((await service.SaveKnowledgeAsync("Local", "bge-m3", "http://ai:11434/v1", null, false, false, default)).HasEmbeddingApiKey);
    }

    [Fact]
    public async Task SaveKnowledge_NoneClearsEmbeddingButKeepsChatSettings()
    {
        await using var db = await CreateDbWithAiAsync();
        var service = CreateService(db);
        await service.SaveKnowledgeAsync("OpenAI", "text-embedding-3-small", null, "sk-embed-12345678", false, false, default);

        var view = await service.SaveKnowledgeAsync("None", "ignored", "http://x", "ignored", false, false, default);
        Assert.Equal("None", view.EmbeddingProvider);
        Assert.Null(view.EmbeddingModel);
        Assert.Null(view.EmbeddingBaseUrl);
        Assert.False(view.HasEmbeddingApiKey);
        Assert.Equal(("OpenAI", "gpt-5-mini", true), (view.Provider, view.Model, view.HasApiKey));
    }

    [Fact]
    public async Task SaveKnowledge_RejectsUnknownProviderAndMissingModel()
    {
        await using var db = await CreateDbWithAiAsync();
        var service = CreateService(db);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveKnowledgeAsync("Anthropic", "x", null, "k", false, false, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveKnowledgeAsync("Local", " ", "http://localhost:11434/v1", null, false, false, default));
    }

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<QaDbContext> CreateDbWithAiAsync()
    {
        var db = CreateDb();
        await CreateService(db).SaveAsync("OpenAI", "gpt-5-mini", null, "sk-chat-abcdefgh", true, false, default);
        return db;
    }

    private static readonly IDataProtectionProvider Protection = new EphemeralDataProtectionProvider();

    private static SharedAiConfigurationService CreateService(QaDbContext db) =>
        new(db, Protection, new ConfigurationBuilder().Build(), null!);
}
