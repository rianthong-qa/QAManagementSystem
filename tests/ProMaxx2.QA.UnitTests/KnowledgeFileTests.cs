using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Phase 3: เอกสาร PDF/DOCX/MD/TXT ในคลังความรู้ — สกัดข้อความ, อัปโหลด/นำเข้า/ลบ, index และ MCP get_document</summary>
public sealed class KnowledgeFileTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "qa-knowledge-files-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    // ---------- DocumentTextExtractor ----------

    [Fact]
    public void Extract_TextEncodings()
    {
        Assert.Equal("ปิดวันแล้วยอดไม่ตรง ต้องตรวจบิลยกเลิก", DocumentTextExtractor.Extract(Encoding.UTF8.GetBytes("ปิดวันแล้วยอดไม่ตรง ต้องตรวจบิลยกเลิก"), ".md").Text);
        Assert.Equal("หัวข้อ\n\nเนื้อหาที่ยาวพอให้นับเป็นเอกสาร", DocumentTextExtractor.Extract([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("หัวข้อ\r\n\r\n\r\nเนื้อหาที่ยาวพอให้นับเป็นเอกสาร  ")], ".txt").Text);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var tis620 = Encoding.GetEncoding(874).GetBytes("เอกสารภาษาไทยแบบเก่าที่บันทึกเป็น TIS-620");
        Assert.Equal("เอกสารภาษาไทยแบบเก่าที่บันทึกเป็น TIS-620", DocumentTextExtractor.Extract(tis620, ".txt").Text);
        Assert.NotNull(DocumentTextExtractor.Extract(Encoding.UTF8.GetBytes("สั้น"), ".md").Error);
    }

    [Fact]
    public void Extract_DocxParagraphsAndTables()
    {
        var docx = Docx("""
            <w:p><w:r><w:t>ขั้นตอนการปิดวัน</w:t></w:r></w:p>
            <w:p><w:r><w:t xml:space="preserve">1. ตรวจ</w:t></w:r><w:r><w:tab/><w:t>บิลค้าง</w:t></w:r></w:p>
            <w:tbl><w:tr><w:tc><w:p><w:r><w:t>เมนู</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>ผลที่คาดหวัง</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
            """);
        Assert.True(DocumentTextExtractor.LooksValid(docx, ".docx"));
        var text = DocumentTextExtractor.Extract(docx, ".docx").Text!;
        Assert.Contains("ขั้นตอนการปิดวัน\n1. ตรวจ\tบิลค้าง", text);
        Assert.Contains("เมนู | ผลที่คาดหวัง |", text);
        Assert.NotNull(DocumentTextExtractor.Extract([(byte)'P', (byte)'K', 3, 4, 0, 0], ".docx").Error);
    }

    [Fact]
    public void Extract_PdfWithTextAndBlankPdf()
    {
        var pdf = Pdf("Closing day report mismatch: check cancelled bills before closing the shift.");
        Assert.True(DocumentTextExtractor.LooksValid(pdf, ".pdf"));
        var extracted = DocumentTextExtractor.Extract(pdf, ".pdf");
        Assert.Null(extracted.Error);
        Assert.Contains("check cancelled bills", extracted.Text);
        Assert.Equal(1, extracted.Pages);

        var blank = Pdf(null);
        Assert.Contains("สแกน", DocumentTextExtractor.Extract(blank, ".pdf").Error);
        Assert.False(DocumentTextExtractor.LooksValid(Encoding.ASCII.GetBytes("not a pdf"), ".pdf"));
        Assert.False(DocumentTextExtractor.LooksValid([0x41, 0x00, 0x42], ".txt"));
    }

    [Fact]
    public void RepairThaiPdf_RestoresSaraAmAndPresentationForms()
    {
        Assert.Equal("นำเสนอผลการทำงาน", DocumentTextExtractor.RepairThaiPdfText("น าเสนอผลการท างาน"));
        Assert.Equal("ค่ำ กำไร", DocumentTextExtractor.RepairThaiPdfText("ค่ า ก" + (char)0x0E4D + "า" + "ไร"));
        Assert.Equal("ปั้น", DocumentTextExtractor.RepairThaiPdfText("ป" + (char)0xF710 + (char)0xF70B + "น"));
        // ไม่แตะข้อความปกติ (ช่องว่างตามด้วยคำที่ขึ้นต้นด้วยพยัญชนะ / ภาษาอังกฤษ)
        Assert.Equal("ความ ถูกต้อง a า", DocumentTextExtractor.RepairThaiPdfText("ความ ถูกต้อง a า"));
    }

    [Theory]
    [InlineData("คู่มือ ปิดวัน.pdf", "คู่มือ ปิดวัน.pdf")]
    [InlineData("..\\..\\evil/../ระบบ.md", "ระบบ.md")]
    [InlineData("<script>.txt", "script.txt")]
    [InlineData("...", "document")]
    public void DisplayName_KeepsThaiMarksStripsPaths(string input, string expected) => Assert.Equal(expected, KnowledgeFileService.DisplayName(input));

    // ---------- KnowledgeFileService ----------

    [Fact]
    public async Task Upload_SavesExtractsRejectsDuplicatesAndFakes()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var service = Files(db);
        var md = Encoding.UTF8.GetBytes("# วิธีปิดวัน\nตรวจบิลยกเลิกก่อนปิดกะทุกครั้ง โทร 081-234-5678");

        var results = await service.UploadAsync([new("ปิดวัน.md", md), new("ซ้ำ.md", md), new("fake.pdf", Encoding.ASCII.GetBytes("hello")), new("virus.exe", [1, 2, 3])], null, "คู่มือ", userId, null, default);

        Assert.Equal([true, false, false, false], results.Select(x => x.Success));
        Assert.Contains("ซ้ำกับไฟล์", results[1].Message);
        Assert.Contains("ไม่ใช่ PDF", results[2].Message);
        var file = await db.KnowledgeFiles.SingleAsync();
        Assert.Equal(("ปิดวัน.md", "Upload", "คู่มือ"), (file.FileName, file.Origin, file.Description));
        Assert.Contains("ตรวจบิลยกเลิก", file.ExtractedText);
        Assert.True(File.Exists(Path.Combine(root, "App_Data", "KnowledgeFiles", file.StoragePath)));
        Assert.Equal(results[0].Item!.KnowledgeFileId, file.KnowledgeFileId);
        Assert.Single(await db.AuditLogs.Where(x => x.EntityType == "KnowledgeFile").ToListAsync());

        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadAsync(Enumerable.Range(0, 11).Select(i => new IncomingKnowledgeFile($"{i}.md", md)).ToList(), null, null, userId, null, default));
    }

    [Fact]
    public async Task Upload_ScannedPdfStoredWithErrorAndNotIndexed()
    {
        await using var db = CreateDb();
        var results = await Files(db).UploadAsync([new("scan.pdf", Pdf(null))], null, null, Guid.NewGuid(), null, default);
        Assert.False(results[0].Success);
        Assert.Contains("สแกน", results[0].Message);
        Assert.NotNull(results[0].Item);
        var indexer = Indexer(db);
        await indexer.SyncSourcesAsync(default);
        Assert.Empty(await db.KnowledgeDocuments.ToListAsync());
    }

    [Fact]
    public async Task Import_IsIdempotentAndUpdatesChangedFiles()
    {
        var source = Path.Combine(root, "SystemDocs");
        Directory.CreateDirectory(Path.Combine(source, "1.Module"));
        await File.WriteAllTextAsync(Path.Combine(source, "1.Module", "01-Transaction.md"), "# โมดูลขาย\nบันทึกการขายและออกใบกำกับภาษีเต็มรูป");
        await File.WriteAllTextAsync(Path.Combine(source, "README.txt"), "เอกสารประกอบระบบ ProMaxx2 สำหรับทีม QA");
        await File.WriteAllTextAsync(Path.Combine(source, "image.png"), "ignored");
        await using var db = CreateDb();
        var service = Files(db, source);
        var userId = Guid.NewGuid();

        var first = await service.ImportSystemDocumentsAsync(userId, null, default);
        Assert.Equal((2, 0, 0, 0), (first.Added, first.Updated, first.Unchanged, first.Failed));
        var imported = await db.KnowledgeFiles.SingleAsync(x => x.FileName == "01-Transaction.md");
        Assert.Equal(("Import", "SystemDocs/1.Module/01-Transaction.md", (Guid?)null), (imported.Origin, imported.ImportPath, imported.ProjectId));

        var second = await service.ImportSystemDocumentsAsync(userId, null, default);
        Assert.Equal((0, 0, 2), (second.Added, second.Updated, second.Unchanged));

        await File.WriteAllTextAsync(Path.Combine(source, "1.Module", "01-Transaction.md"), "# โมดูลขาย\nเพิ่มการพักบิลและเรียกบิลคืน");
        var third = await service.ImportSystemDocumentsAsync(userId, null, default);
        Assert.Equal((0, 1, 1), (third.Added, third.Updated, third.Unchanged));
        Assert.Contains("พักบิล", (await db.KnowledgeFiles.SingleAsync(x => x.KnowledgeFileId == imported.KnowledgeFileId)).ExtractedText);
        Assert.Equal(2, await db.KnowledgeFiles.CountAsync());
    }

    [Fact]
    public async Task DeleteAndOpen_RespectProjectAccess()
    {
        await using var db = CreateDb();
        var project = Guid.NewGuid();
        var service = Files(db);
        var result = await service.UploadAsync([new("เฉพาะโปรเจกต์.txt", Encoding.UTF8.GetBytes("ข้อมูลเฉพาะโปรเจกต์ที่คนอื่นห้ามเห็นเด็ดขาด"))], project, null, Guid.NewGuid(), null, default);
        var id = result[0].Item!.KnowledgeFileId;

        Assert.Null(await service.OpenAsync(id, [], default));
        Assert.Empty(await service.ListAsync([], null, null, default));
        Assert.NotNull(await service.OpenAsync(id, [project], default));
        Assert.False(await service.DeleteAsync(id, [], Guid.NewGuid(), null, default));
        Assert.True(await service.DeleteAsync(id, [project], Guid.NewGuid(), null, default));
        Assert.Null(await service.OpenAsync(id, [project], default));
        // ลบแบบ soft delete — เก็บไฟล์บนดิสก์ไว้ให้ SYS_ADMIN กู้คืนได้ (RecycleBinService)
        Assert.True(Directory.Exists(Path.Combine(root, "App_Data", "KnowledgeFiles", id.ToString("N"))));
    }

    // ---------- index + MCP ----------

    [Fact]
    public async Task Index_FileBecomesSearchableAndDeletedIsExcluded()
    {
        await using var db = CreateDb();
        var service = Files(db);
        var upload = await service.UploadAsync([new("คู่มือปิดวัน.md", Encoding.UTF8.GetBytes("# วิธีปิดวัน\n" + string.Join("\n\n", Enumerable.Range(1, 30).Select(i => $"ขั้นตอนที่ {i}: ตรวจรายการขายและบิลยกเลิกให้ครบก่อนปิดกะ"))))], null, null, Guid.NewGuid(), null, default);
        var indexer = Indexer(db);
        await indexer.SyncSourcesAsync(default);
        await indexer.IndexPendingAsync(10, default);

        var doc = await db.KnowledgeDocuments.SingleAsync();
        Assert.Equal(("File", "Global", "Indexed", $"#/knowledge?fileId={upload[0].Item!.KnowledgeFileId}"), (doc.SourceType, doc.Scope, doc.State, doc.SourceUrl));
        var chunks = await db.KnowledgeChunks.Select(x => x.Text).ToListAsync();
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.StartsWith("[เอกสาร] คู่มือปิดวัน.md\n", c));

        await service.DeleteAsync(upload[0].Item!.KnowledgeFileId, [], Guid.NewGuid(), null, default);
        await indexer.SyncSourcesAsync(default);
        await indexer.IndexPendingAsync(10, default);
        Assert.Equal("Excluded", (await db.KnowledgeDocuments.SingleAsync()).State);
        Assert.Empty(await db.KnowledgeChunks.ToListAsync());
    }

    [Fact]
    public async Task Mcp_GetDocumentPartsMaskedAndScoped()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var project = new Project("PMX", "ProMaxx", null, null, null);
        db.Add(project);
        db.Add(new ProjectUser(project.ProjectId, userId, null));
        await db.SaveChangesAsync();
        var body = "ติดต่อ admin@shop.co.th\n" + string.Join("\n", Enumerable.Range(1, 900).Select(i => $"บรรทัด {i} รายละเอียดการทดสอบโมดูลขาย"));
        var upload = await Files(db).UploadAsync([new("spec.md", Encoding.UTF8.GetBytes(body))], project.ProjectId, null, userId, null, default);
        var id = upload[0].Item!.KnowledgeFileId.ToString();
        var tools = Tools(db, userId);

        var part1 = await tools.GetDocument(id);
        Assert.StartsWith("[เอกสาร] spec.md — ส่วนที่ 1/", part1);
        Assert.Contains("[EMAIL]", part1);
        Assert.Contains($"get_document(\"{id}\", part: 2)", part1);
        Assert.True(part1.Length <= KnowledgeMcpTools.MaxOutputChars);
        Assert.Contains("ส่วนที่ 2/", await tools.GetDocument(id, 2));
        await Assert.ThrowsAsync<McpException>(() => tools.GetDocument(id, 99));
        await Assert.ThrowsAsync<McpException>(() => Tools(db, Guid.NewGuid()).GetDocument(id));
        await Assert.ThrowsAsync<McpException>(() => tools.GetDocument("x"));
    }

    // ---------- helpers ----------

    private static byte[] Docx(string bodyXml)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
            writer.Write($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>{bodyXml}</w:body></w:document>");
        }
        return memory.ToArray();
    }

    private static byte[] Pdf(string? text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        if (text is not null)
        {
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            page.AddText(text, 10, new UglyToad.PdfPig.Core.PdfPoint(25, 700), font);
        }
        return builder.Build();
    }

    private KnowledgeFileService Files(QaDbContext db, string? importFolder = null) =>
        new(db, new TestEnvironment(root), new ConfigurationBuilder().AddInMemoryCollection(importFolder is null ? [] : new Dictionary<string, string?> { ["Knowledge:SystemDocumentFolders:0"] = importFolder }).Build(), new KnowledgeIndexTrigger());

    private static KnowledgeIndexService Indexer(QaDbContext db) => new(db, NullLogger<KnowledgeIndexService>.Instance);

    private static KnowledgeMcpTools Tools(QaDbContext db, Guid userId) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "pat")) } },
            db, new KnowledgeSearchService(db, new NoSearch(), new ProjectAccessService(db), new Deny()), Indexer(db), new ConfigurationBuilder().Build());

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
    }

    private sealed class NoSearch : IKnowledgeTextSearch
    {
        public Task<IReadOnlyList<KnowledgeChunkHit>> SearchAsync(string query, KnowledgeSearchScope scope, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<KnowledgeChunkHit>>([]);
    }

    private sealed class Deny : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) => Task.FromResult(AuthorizationResult.Failed());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) => Task.FromResult(AuthorizationResult.Failed());
    }
}
