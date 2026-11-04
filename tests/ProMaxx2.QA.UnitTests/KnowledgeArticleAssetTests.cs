using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>รูป/ฟอนต์ที่แยกออกจากบทความ HTML (qa-asset:) — ตรวจชนิดไฟล์, ผูกกับบทความ, ลบไฟล์ที่ไม่มีเจ้าของ และ header ตอนเสิร์ฟ</summary>
public sealed class KnowledgeArticleAssetTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "qa-asset-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static QaDbContext NewDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private KnowledgeAssetService NewService(QaDbContext db) => new(db, new TestEnvironment(root), NullLogger<KnowledgeAssetService>.Instance);

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
    [InlineData(new byte[] { 0x77, 0x4F, 0x46, 0x32, 0, 0 }, "font/woff2")]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67, 0x3E }, "image/svg+xml")]
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E }, null)] // <html> — ไม่ใช่รูป
    public void DetectContentType_uses_file_content(byte[] data, string? expected) => Assert.Equal(expected, KnowledgeAssetService.DetectContentType(data));

    [Fact]
    public void AssetIds_reads_references_once()
    {
        var id = Guid.NewGuid();
        var html = $"<img src=\"qa-asset:{id:N}\"><div style=\"background:url(qa-asset:{id:N})\"></div><p>qa-asset:xyz</p>";
        Assert.Equal([id], HtmlArticleContent.AssetIds(html));
        Assert.Empty(HtmlArticleContent.AssetIds(null));
    }

    [Fact]
    public async Task Upload_rejects_non_images_attaches_on_save_and_serves_with_safe_headers()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var results = await service.UploadAsync([Png, "<script>x</script>"u8.ToArray()], Guid.NewGuid(), default);
        Assert.NotNull(results[0].AssetId);
        Assert.Null(results[1].AssetId);
        Assert.NotNull(results[1].Error);

        var id = Guid.ParseExact(results[0].AssetId!, "N");
        var articleId = Guid.NewGuid();
        await service.AttachAsync(articleId, $"<img src=\"qa-asset:{id:N}\">", default);
        await db.SaveChangesAsync();
        Assert.Equal(articleId, (await db.KnowledgeArticleAssets.SingleAsync()).KnowledgeArticleId);

        var controller = new KnowledgeAssetsController(service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var file = Assert.IsType<PhysicalFileResult>(await controller.Get(id, default));
        Assert.Equal("image/png", file.ContentType);
        var headers = controller.Response.Headers;
        Assert.Contains("sandbox", headers.ContentSecurityPolicy.ToString());
        Assert.Equal("*", headers.AccessControlAllowOrigin.ToString());
        Assert.IsType<NotFoundResult>(await controller.Get(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Upload_deletes_old_unattached_assets()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var old = Guid.ParseExact((await service.UploadAsync([Png], Guid.NewGuid(), default))[0].AssetId!, "N");
        var asset = await db.KnowledgeArticleAssets.SingleAsync();
        db.Entry(asset).Property(x => x.UploadedAt).CurrentValue = DateTime.UtcNow.AddDays(-3);
        await db.SaveChangesAsync();

        await service.UploadAsync([Png], Guid.NewGuid(), default);
        Assert.DoesNotContain(await db.KnowledgeArticleAssets.ToListAsync(), x => x.KnowledgeArticleAssetId == old);
        Assert.False(File.Exists(Path.Combine(root, "App_Data", "KnowledgeAssets", old.ToString("N"))));
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
    }
}
