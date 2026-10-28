using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Domain.Integrations;
using ProMaxx2.QA.Domain.Knowledge;
using ProMaxx2.QA.Domain.Settings;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Phase 4: ข้อความ CRM Ticket (รายละเอียด + คำตอบ) เข้าคลังความรู้หลัง mask</summary>
public sealed class KnowledgeCrmTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 3, 0, 0, DateTimeKind.Utc);

    // ---------- parser ----------

    [Fact]
    public void Parser_CleansHtmlAndStaffNames()
    {
        Assert.Equal("บรรทัดแรก\nบรรทัด & สอง", CrmKnowledgeParser.CleanText("<p>บรรทัดแรก</p><div>บรรทัด&nbsp;&amp; <b>สอง</b></div>"));
        Assert.Equal("", CrmKnowledgeParser.CleanText("  <br/> "));
        Assert.Equal("เหรียญทอง เจือบุญ", CrmKnowledgeParser.StaffName("เหรียญทอง เจือบุญ (6101)"));
        Assert.Null(CrmKnowledgeParser.StaffName("6101"));
        Assert.Null(CrmKnowledgeParser.StaffName(null));
    }

    [Fact]
    public void Parser_TextAnswersSkipAttachmentsEditHistoryAndEmpty()
    {
        var answers = CrmKnowledgeParser.TextAnswers([
            Answer("3", "ปิดงานแล้ว", "2026-10-03T09:00:00"),
            Answer("1", "แจ้งปัญหา", "2026-10-01T09:00:00"),
            Answer("2", "", "2026-10-02T09:00:00"),
            Answer("4", "รูป.jpg", "2026-10-02T10:00:00", type: "P"),
            Answer("5", "[QA Hub] แก้ชื่อเรื่อง", "2026-10-02T11:00:00"),
        ]);

        Assert.Equal(["1", "3"], answers.Select(x => x.AnswerNo));
    }

    [Fact]
    public void Parser_MaskTermsFromDetailAndAnswers()
    {
        using var doc = JsonDocument.Parse("""
            { "fd": { "jobNo": "HD-1", "fname": "สมชาย", "lname": "ใจดี", "nickName": "ชาย", "branchName": "สาขาบางนา", "email": "a@b.co",
                      "member": "12345", "customerFrs": "บริษัท ร้านดีมาก จำกัด", "assignto": "เหรียญทอง เจือบุญ (6101)" } }
            """);
        var terms = CrmKnowledgeParser.MaskTerms(doc.RootElement, [Answer("1", "x", null, posted: "สุดา ทองดี (6200)")]);

        Assert.Contains(terms, x => x is { Term: "สมชาย ใจดี", Label: "CUSTOMER" });
        Assert.Contains(terms, x => x is { Term: "สมชาย", Label: "CUSTOMER" });
        Assert.Contains(terms, x => x is { Term: "บริษัท ร้านดีมาก จำกัด", Label: "CUSTOMER" });
        Assert.Contains(terms, x => x is { Term: "สาขาบางนา", Label: "BRANCH" });
        Assert.Contains(terms, x => x is { Term: "เหรียญทอง", Label: "STAFF" });
        Assert.Contains(terms, x => x is { Term: "สุดา ทองดี", Label: "STAFF" });
        Assert.DoesNotContain(terms, x => x.Term is "12345" or "ชาย" or "บริษัท" or "จำกัด");
    }

    [Fact]
    public void SourceText_CrmTicketFormat()
    {
        var text = KnowledgeSourceText.ForCrmTicket(new("HD-1", "พิมพ์ใบกำกับไม่ได้", "Close", "HD", "Support", "ProMaxx POS", new DateTime(2026, 10, 1),
            "กดพิมพ์แล้วค้าง", [new(new DateTime(2026, 10, 2), "ให้ลง driver ใหม่"), new(null, "  ")]));

        Assert.Equal("[CRM] HD-1: พิมพ์ใบกำกับไม่ได้", text.Header);
        Assert.Contains("สินค้า: ProMaxx POS", text.Body);
        Assert.Contains("สถานะ: Close | แจ้งเมื่อ: 2026-10-01", text.Body);
        Assert.Contains("รายละเอียดปัญหา:\nกดพิมพ์แล้วค้าง", text.Body);
        Assert.Contains("การตอบ / วิธีแก้:\n- 2026-10-02: ให้ลง driver ใหม่", text.Body);
    }

    // ---------- fetch + mask ----------

    [Fact]
    public async Task Apply_MasksUsesOldestAnswerAsDescriptionAndDetectsChange()
    {
        await using var db = CreateDb();
        await SeedTicketAsync(db, "HD-1", "Close", "ลูกค้า สมชาย พิมพ์ไม่ได้");
        var fetcher = Fetcher(db, new FakeSource());
        var raw = new CrmKnowledgeRaw(null, [new("สมชาย", "CUSTOMER"), new("เหรียญทอง", "STAFF")], [
            Answer("2", "เหรียญทอง: ให้ลง driver ใหม่ โทร 081-234-5678", "2026-10-02T09:00:00"),
            Answer("1", "<p>คุณสมชาย แจ้งว่ากดพิมพ์แล้วค้าง</p>", "2026-10-01T09:00:00"),
        ]);

        Assert.True(await fetcher.ApplyAsync("HD-1", raw, Now, default));
        var content = await db.CrmTicketContents.SingleAsync();
        Assert.Equal("ลูกค้า [CUSTOMER] พิมพ์ไม่ได้", content.SubjectMasked);
        Assert.Equal("คุณ[CUSTOMER] แจ้งว่ากดพิมพ์แล้วค้าง", content.DescriptionMasked);
        var answer = await db.CrmTicketAnswers.SingleAsync();
        Assert.Equal(("2", "[STAFF]: ให้ลง driver ใหม่ โทร [PHONE]"), (answer.AnswerNo, answer.TextMasked));
        Assert.Equal(Now, content.ChangedAt);

        // ดึงซ้ำเนื้อหาเดิม = ไม่เปลี่ยน; มีคำตอบใหม่ = แทนชุดคำตอบ
        Assert.False(await fetcher.ApplyAsync("HD-1", raw, Now.AddHours(2), default));
        Assert.Equal(Now, (await db.CrmTicketContents.SingleAsync()).ChangedAt);
        var more = raw with { Answers = [.. raw.Answers, Answer("3", "ปิดงาน", "2026-10-03T09:00:00")] };
        Assert.True(await fetcher.ApplyAsync("HD-1", more, Now.AddHours(3), default));
        Assert.Equal(2, await db.CrmTicketAnswers.CountAsync());
    }

    [Fact]
    public async Task Candidates_ClosedOrRepliedNewFirstAndRefetchOnlyAfterChange()
    {
        await using var db = CreateDb();
        await SeedTicketAsync(db, "HD-OPEN", "Open", "ยังไม่ตอบ");
        await SeedTicketAsync(db, "HD-CLOSED", "Close", "ปิดแล้ว");
        await SeedTicketAsync(db, "HD-REPLIED", "Continue", "มีการตอบ", lastReplyAt: Now.AddDays(-1));
        await SeedTicketAsync(db, "HD-DONE", "Finish", "ดึงแล้ว", lastReplyAt: Now.AddDays(-3));
        var done = new CrmTicketContent("HD-DONE");
        done.Apply("ดึงแล้ว", "x", "hash", Now.AddDays(-2));
        db.CrmTicketContents.Add(done);
        await db.SaveChangesAsync();

        var jobs = await CrmKnowledgeFetcher.Candidates(db, Now).Select(x => x.JobNo).ToListAsync();
        Assert.Equal(["HD-REPLIED", "HD-CLOSED"], jobs);

        // มีการตอบใหม่หลังดึง → ดึงใหม่ก่อนใบที่ยังไม่เคยดึง
        var ticket = await db.CrmTickets.SingleAsync(x => x.JobNo == "HD-DONE");
        ticket.Apply(ticket.Subject, ticket.Status, null, null, null, null, null, null, null, null, null, Now.AddMinutes(-90), Now);
        await db.SaveChangesAsync();
        Assert.Equal("HD-DONE", (await CrmKnowledgeFetcher.Candidates(db, Now).FirstAsync()).JobNo);

        // ดึงไม่สำเร็จ → ลองใหม่หลัง 24 ชม.
        done.Fail("ไม่พบ", Now.AddHours(-2));
        await db.SaveChangesAsync();
        Assert.DoesNotContain("HD-DONE", await CrmKnowledgeFetcher.Candidates(db, Now).Select(x => x.JobNo).ToListAsync());
        Assert.Contains("HD-DONE", await CrmKnowledgeFetcher.Candidates(db, Now.AddHours(23)).Select(x => x.JobNo).ToListAsync());
    }

    [Fact]
    public async Task FetchPending_UsesCentralAccountRecordsMissingAndStopsWhenCrmDown()
    {
        await using var db = CreateDb();
        var account = new CrmConfiguration(Guid.NewGuid(), "m", "6101", "enc", null, true);
        db.CrmConfigurations.Add(account);
        foreach (var n in new[] { "1", "2", "3", "4" }) await SeedTicketAsync(db, $"HD-{n}", "Close", $"เรื่อง {n}", lastReplyAt: Now.AddDays(-int.Parse(n)));
        var source = new FakeSource
        {
            ["HD-1"] = new CrmKnowledgeRaw("ปัญหาหนึ่ง", [], []),
            ["HD-2"] = null,
            ["HD-3"] = new CrmIntegrationException("CRM ล่ม", HttpStatusCode.ServiceUnavailable),
        };
        var trigger = new KnowledgeIndexTrigger();

        Assert.Equal(2, await Fetcher(db, source, trigger).FetchPendingAsync(default));
        Assert.All(source.Calls, x => Assert.Equal(account.UserId, x.AccountUserId));
        Assert.Equal(["HD-1", "HD-2", "HD-3"], source.Calls.Select(x => x.JobNo));
        Assert.Equal("ปัญหาหนึ่ง", (await db.CrmTicketContents.SingleAsync(x => x.JobNo == "HD-1")).DescriptionMasked);
        Assert.Equal("ไม่พบ Ticket ใน CRM", (await db.CrmTicketContents.SingleAsync(x => x.JobNo == "HD-2")).FetchError);
        Assert.False(await db.CrmTicketContents.AnyAsync(x => x.JobNo == "HD-3" || x.JobNo == "HD-4"));
        Assert.True(await (Task<bool>)trigger.WaitAsync(TimeSpan.Zero, default)); // ถูกปลุกให้ index ทันที

        // ไม่มีบัญชีกลาง = ไม่ดึง
        db.CrmConfigurations.Remove(account);
        await db.SaveChangesAsync();
        Assert.Equal(0, await Fetcher(db, source).FetchPendingAsync(default));
    }

    // ---------- index + access ----------

    [Fact]
    public async Task Index_CrmTicketInCrmScopeAndReindexOnStatusChange()
    {
        await using var db = CreateDb();
        await SeedTicketAsync(db, "HD-1", "Close", "พิมพ์ไม่ได้", contactDate: new DateTime(2026, 10, 1));
        await Fetcher(db, new FakeSource()).ApplyAsync("HD-1", new("ร้าน สมชาย กดพิมพ์ค้าง ติดต่อ a@b.co", [new("สมชาย", "CUSTOMER")], [Answer("9", "ลง driver ใหม่", "2026-10-02T09:00:00")]), Now, default);
        var indexer = new KnowledgeIndexService(db, NullLogger<KnowledgeIndexService>.Instance);

        await indexer.SyncSourcesAsync(default);
        Assert.Equal(1, await indexer.IndexPendingAsync(10, default));
        var doc = await db.KnowledgeDocuments.SingleAsync();
        Assert.Equal(("CrmTicket", "HD-1", "Crm", (Guid?)null, "Indexed", true), (doc.SourceType, doc.SourceKey, doc.Scope, doc.ProjectId, doc.State, doc.ContainsCustomerData));
        Assert.Equal("#/knowledge?jobNo=HD-1", doc.SourceUrl);
        var chunk = await db.KnowledgeChunks.SingleAsync();
        Assert.StartsWith("[CRM] HD-1: พิมพ์ไม่ได้", chunk.Text);
        Assert.Contains("ร้าน [CUSTOMER] กดพิมพ์ค้าง ติดต่อ [EMAIL]", chunk.Text);
        Assert.Contains("ลง driver ใหม่", chunk.Text);

        // สถานะ Ticket เปลี่ยน (Close → Continue) → index ใหม่
        var ticket = await db.CrmTickets.SingleAsync();
        ticket.Apply(ticket.Subject, "Continue", null, null, null, "6200", null, null, null, ticket.ContactDate, null, null, DateTime.UtcNow.AddMinutes(1));
        await db.SaveChangesAsync();
        await indexer.SyncSourcesAsync(default);
        Assert.Equal("Stale", (await db.KnowledgeDocuments.SingleAsync()).State);
        await indexer.IndexPendingAsync(10, default);
        Assert.Contains("สถานะ: Continue", (await db.KnowledgeChunks.SingleAsync()).Text);
    }

    [Fact]
    public async Task GetAsync_ReturnsMaskedContentOnly()
    {
        await using var db = CreateDb();
        await SeedTicketAsync(db, "HD-1", "Close", "สมชาย พิมพ์ไม่ได้");
        var fetcher = Fetcher(db, new FakeSource());
        await fetcher.ApplyAsync("HD-1", new("สมชาย แจ้ง", [new("สมชาย", "CUSTOMER")], [Answer("1", "ตอบ", "2026-10-02T09:00:00")]), Now, default);

        var dto = await fetcher.GetAsync(" HD-1 ", default);
        Assert.NotNull(dto);
        Assert.Equal(("[CUSTOMER] พิมพ์ไม่ได้", "[CUSTOMER] แจ้ง", "Close", 1), (dto.Subject, dto.Description, dto.Status, dto.Answers.Count));
        Assert.Null(await fetcher.GetAsync("HD-404", default));
    }

    [Fact]
    public async Task Mcp_CrmOnlyWhenAdminAllowsExternalAi()
    {
        await using var db = CreateDb();
        await SeedTicketAsync(db, "HD-1", "Close", "พิมพ์ไม่ได้");
        await Fetcher(db, new FakeSource()).ApplyAsync("HD-1", new("กดพิมพ์ค้าง โทร 081-234-5678", [], []), Now, default);
        var userId = Guid.NewGuid();
        var search = new CapturingSearch();
        var tools = new KnowledgeMcpTools(Accessor(userId), db, new KnowledgeSearchService(db, search, new ProjectAccessService(db), new Allow(true)),
            new KnowledgeIndexService(db, NullLogger<KnowledgeIndexService>.Instance), new ConfigurationBuilder().Build());

        await Assert.ThrowsAsync<McpException>(() => tools.GetCrmTicket("HD-1"));
        await Assert.ThrowsAsync<McpException>(() => tools.SearchKnowledge("พิมพ์", source: "crm"));
        await tools.SearchKnowledge("พิมพ์");
        Assert.False(search.LastScope!.IncludeCrm);

        var config = new AiConfiguration("Local", "qwen3", "http://localhost:11434/v1", "", null, true);
        config.UpdateKnowledge("None", null, null, "", null, true, true);
        db.AiConfigurations.Add(config);
        await db.SaveChangesAsync();

        var text = await tools.GetCrmTicket("HD-1");
        Assert.StartsWith("[CRM] HD-1: พิมพ์ไม่ได้", text);
        Assert.Contains("[PHONE]", text);
        Assert.Contains("#/knowledge?jobNo=HD-1", text);
        await tools.SearchKnowledge("พิมพ์", source: "crm");
        Assert.True(search.LastScope!.IncludeCrm);
        Assert.Equal(["CrmTicket"], search.LastScope.SourceTypes!);
        await Assert.ThrowsAsync<McpException>(() => tools.GetCrmTicket("HD-404"));
    }

    // ---------- helpers ----------

    private static CrmHelpDeskAnswer Answer(string no, string text, string? at, string type = "A", string posted = "เหรียญทอง เจือบุญ (6101)") => new(no, text, posted, at, type, null);

    private static async Task SeedTicketAsync(QaDbContext db, string jobNo, string status, string subject, DateTime? lastReplyAt = null, DateTime? contactDate = null)
    {
        var ticket = new CrmTicket(jobNo, Now.AddDays(-10));
        ticket.Apply(subject, status, "HD", "Support", "ProMaxx POS", "6101", null, null, "สาขาบางนา", contactDate, null, lastReplyAt, Now.AddDays(-10));
        db.CrmTickets.Add(ticket);
        await db.SaveChangesAsync();
    }

    private static CrmKnowledgeFetcher Fetcher(QaDbContext db, ICrmKnowledgeSource source, KnowledgeIndexTrigger? trigger = null) =>
        new(db, source, trigger ?? new KnowledgeIndexTrigger(), new ConfigurationBuilder().Build(), NullLogger<CrmKnowledgeFetcher>.Instance);

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IHttpContextAccessor Accessor(Guid userId) => new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "PersonalAccessToken")) },
    };

    /// <summary>ผลต่อ JobNo: CrmKnowledgeRaw = สำเร็จ, null = ไม่พบ, Exception = โยน; ไม่ได้กำหนด = สำเร็จแบบว่าง</summary>
    private sealed class FakeSource : Dictionary<string, object?>, ICrmKnowledgeSource
    {
        public List<(Guid AccountUserId, string JobNo)> Calls { get; } = [];

        public Task<CrmKnowledgeRaw?> FetchAsync(Guid accountUserId, string jobNo, string jobType, CancellationToken ct)
        {
            Calls.Add((accountUserId, jobNo));
            if (!TryGetValue(jobNo, out var result)) return Task.FromResult<CrmKnowledgeRaw?>(new("รายละเอียด", [], []));
            if (result is Exception ex) throw ex;
            return Task.FromResult(result as CrmKnowledgeRaw);
        }
    }

    private sealed class CapturingSearch : IKnowledgeTextSearch
    {
        public KnowledgeSearchScope? LastScope { get; private set; }
        public Task<IReadOnlyList<KnowledgeChunkHit>> SearchAsync(string query, KnowledgeSearchScope scope, int take, CancellationToken ct) { LastScope = scope; return Task.FromResult<IReadOnlyList<KnowledgeChunkHit>>([]); }
    }

    private sealed class Allow(bool succeed) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) => Task.FromResult(succeed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) => Task.FromResult(succeed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }
}
