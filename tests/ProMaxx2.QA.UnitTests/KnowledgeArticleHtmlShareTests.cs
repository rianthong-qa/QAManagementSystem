using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Domain.Knowledge;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>บทความนำเข้าจาก HTML (ทำความสะอาด/สกัดข้อความ) และลิงก์แชร์สาธารณะของบทความ</summary>
public sealed class KnowledgeArticleHtmlShareTests
{
    private static QaDbContext NewDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static KnowledgeArticleShareService NewShares(QaDbContext db) =>
        new(db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PublicWebBaseUrl"] = "https://example.test/" }).Build());

    [Fact]
    public void Sanitize_removes_scripts_handlers_and_script_urls_but_keeps_layout()
    {
        const string html = """
            <html><head><title>คู่มือ</title><meta http-equiv="refresh" content="0;url=https://evil"><link rel="stylesheet" href="https://x/y.css">
            <style>.box{color:red;width:expression(alert(1))}</style><script>alert(1)</script></head>
            <body onload="steal()"><div class="box" style="color:blue" onclick='x()'>สวัสดี</div>
            <a href="javascript:alert(1)">a</a><a href=" JaVaScRiPt:alert(2)">b</a><img src="data:text/html;base64,AAA" onerror=hack()>
            <img src="data:image/png;base64,iVBOR"><iframe src="https://evil"></iframe><object data="x.swf"></object><iframe srcdoc="<b>x</b>"></body></html>
            """;
        var clean = HtmlArticleContent.Sanitize(html);
        foreach (var bad in new[] { "<script", "alert(1)</script>", "onload", "onclick", "onerror", "javascript:", "JaVaScRiPt", "data:text/html", "<iframe", "<object", "<meta", "<link", "expression(", "srcdoc" })
            Assert.DoesNotContain(bad, clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<style>", clean);
        Assert.Contains("class=\"box\" style=\"color:blue\"", clean);
        Assert.Contains("data:image/png;base64,iVBOR", clean);
        Assert.Contains("<title>คู่มือ</title>", clean);
    }

    [Fact]
    public void ToText_keeps_headings_lists_and_tables_and_drops_head_style_script()
    {
        const string html = "<html><head><title>T</title><style>p{}</style></head><body><h1>หัวข้อ &amp; สรุป</h1><p>ย่อหน้า&nbsp;แรก</p>"
            + "<ul><li>หนึ่ง</li><li>สอง</li></ul><table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table><script>var x=1</script></body></html>";
        var text = HtmlArticleContent.ToText(html);
        Assert.Contains("## หัวข้อ & สรุป", text);
        Assert.Contains("ย่อหน้า แรก", text);
        Assert.Contains("- หนึ่ง", text);
        Assert.Contains("- สอง", text);
        Assert.Contains("A | B", text);
        Assert.DoesNotContain("p{}", text);
        Assert.DoesNotContain("var x", text);
        Assert.DoesNotContain("<", text);
    }

    [Fact]
    public void ExtractTitle_prefers_title_then_first_h1()
    {
        Assert.Equal("คู่มือ & การใช้งาน", HtmlArticleContent.ExtractTitle("<title> คู่มือ &amp; การใช้งาน </title><h1>อื่น</h1>"));
        Assert.Equal("หัวข้อ ตัวหนา", HtmlArticleContent.ExtractTitle("<body><h1 class='x'>หัวข้อ <b>ตัวหนา</b></h1></body>"));
        Assert.Null(HtmlArticleContent.ExtractTitle("<p>ไม่มีหัวข้อ</p>"));
        Assert.Null(HtmlArticleContent.ExtractTitle("<title>   </title>"));
    }

    [Fact]
    public void Article_with_html_is_html_format_and_switching_back_clears_html()
    {
        var a = new KnowledgeArticle("t", "ข้อความ", null, null, "Draft", null, "<p>ข้อความ</p>");
        Assert.Equal("Html", a.ContentFormat);
        Assert.Equal("<p>ข้อความ</p>", a.BodyHtml);
        a.Update("t", "markdown", null, null, "Draft", null);
        Assert.Equal("Markdown", a.ContentFormat);
        Assert.Null(a.BodyHtml);
        var ex = Assert.Throws<ArgumentException>(() => new KnowledgeArticle("t", " ", null, null, "Draft", null, "<img src='x.png'>"));
        Assert.Contains("HTML", ex.Message);
    }

    [Fact]
    public async Task Share_link_is_reused_revocable_and_only_resolves_published_articles()
    {
        using var db = NewDb();
        var article = new KnowledgeArticle("คู่มือ", "ข้อความ", null, "tag", "Published", null, "<p>ข้อความ</p>");
        db.KnowledgeArticles.Add(article);
        await db.SaveChangesAsync();
        var shares = NewShares(db);

        var (code, created) = await shares.GetOrCreateAsync(article.KnowledgeArticleId, null, default);
        Assert.True(created);
        Assert.Equal(8, code.Length);
        Assert.Equal($"https://example.test/?ka={code}", shares.UrlFor(code));
        Assert.Equal((code, false), await shares.GetOrCreateAsync(article.KnowledgeArticleId, null, default));

        var controller = new SharedArticlesController(db, shares) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var dto = Assert.IsType<SharedArticleDto>(Assert.IsType<OkObjectResult>((await controller.Get(code, default)).Result).Value);
        Assert.Equal(("คู่มือ", "Html", "<p>ข้อความ</p>"), (dto.Title, dto.ContentFormat, dto.BodyHtml));
        Assert.Null(dto.BodyMarkdown);
        Assert.Equal("noindex, nofollow", controller.Response.Headers["X-Robots-Tag"].ToString());

        // ฉบับร่างเปิดไม่ได้ แม้ลิงก์ยังไม่ถูกยกเลิก
        article.Update("คู่มือ", "ข้อความ", null, null, "Draft", null, "<p>ข้อความ</p>");
        await db.SaveChangesAsync();
        Assert.Null(await shares.ResolveAsync(code, default));
        article.Update("คู่มือ", "ข้อความ", null, null, "Published", null, "<p>ข้อความ</p>");
        await db.SaveChangesAsync();
        Assert.NotNull(await shares.ResolveAsync(code, default));

        Assert.True(await shares.RevokeAsync(article.KnowledgeArticleId, null, default));
        await db.SaveChangesAsync();
        Assert.Null(await shares.ResolveAsync(code, default));
        Assert.IsType<NotFoundResult>((await controller.Get(code, default)).Result);
        var (next, _) = await shares.GetOrCreateAsync(article.KnowledgeArticleId, null, default);
        Assert.NotEqual(code, next);
        Assert.Null(await shares.ResolveAsync("", default));
        Assert.Null(await shares.ResolveAsync("zzzzzzzz", default));
    }
}
