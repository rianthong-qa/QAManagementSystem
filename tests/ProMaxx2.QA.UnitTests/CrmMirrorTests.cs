using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Integrations;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

public sealed class CrmMirrorTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 2, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("เหรียญทอง เจือบุญ (6101)", "6101")]
    [InlineData(" 6101 ", "6101")]
    [InlineData("0", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Staff_code_is_extracted_from_display_value(string? raw, string? expected)
        => Assert.Equal(expected, CrmMirrorParser.StaffCode(raw));

    [Fact]
    public void Parser_reads_tickets_and_answer_metadata_without_content()
    {
        const string body = """
        { "data": [
          { "owner": "สรุป", "count": 1 },
          { "fd": { "jobNo": "BHD-1", "subject": "POS ค้าง", "status": "Test", "assignto": "เหรียญทอง (6101)", "ownerSubjectId": "1001", "sysDevelop": "0", "contactDate": "01/10/2569 09:00", "tel": "0812345678" },
            "answers": [ { "answerNo": "11", "posted": "1001", "ansDate": "01/10/2569 10:00", "description": "ข้อความลูกค้า" }, { "answerNo": "12", "posted": "6101" } ] }
        ] }
        """;

        var (records, diagnostics) = CrmMirrorParser.Parse(body);

        var record = Assert.Single(records);
        Assert.Equal("BHD-1", record.Ticket.JobNo);
        Assert.Equal(2, record.Answers.Count);
        Assert.Equal(("11", "1001"), (record.Answers[0].AnswerNo, record.Answers[0].PostedCode));
        Assert.Equal(new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc), record.Answers[0].AnsweredAt);
        Assert.Null(record.Answers[1].AnsweredAt);
        Assert.Equal((2, 1, 1, 1), (diagnostics.Records, diagnostics.WithJobNo, diagnostics.WithAnswers, diagnostics.AnswersWithDate));
        Assert.Contains("ansDate", diagnostics.AnswerFields);
    }

    [Fact]
    public void Parser_reads_help_desk_answers_inside_ticket_record_without_duplicates()
    {
        const string body = """
        [ { "fd": { "jobNo": "BHD-2", "status": "Continue", "assignto": "6101",
                    "helpDeskAnswers": [ { "answerNo": "21", "posted": "6101", "ansDate": "02/10/2569 09:00" }, { "answerNo": "22", "posted": "1001" } ] },
            "answers": [ { "answerNo": "21", "posted": "6101" } ] } ]
        """;

        var record = Assert.Single(CrmMirrorParser.Parse(body).Records);

        Assert.Equal(["21", "22"], record.Answers.Select(x => x.AnswerNo));
        Assert.Equal("6101", record.Answers[0].PostedCode);
    }

    [Fact]
    public async Task Apply_records_snapshot_then_route_changes_and_each_answer_once()
    {
        await using var db = CreateDb();
        var service = Service(db);

        await service.ApplyAsync([Record("BHD-1", "Test", "6101", "1001", null, ("11", "1001"))], T0, CancellationToken.None);
        var second = await service.ApplyAsync([Record("BHD-1", "Develop", "3003", "1001", "3003", ("11", "1001"), ("12", "6101"))], T0.AddHours(2), CancellationToken.None);
        var unchanged = await service.ApplyAsync([Record("BHD-1", "Develop", "3003", "1001", "3003", ("11", "1001"), ("12", "6101"))], T0.AddHours(3), CancellationToken.None);

        Assert.Equal((0, 1, 1), second);
        Assert.Equal((0, 0, 0), unchanged);
        var ticket = await db.CrmTickets.SingleAsync();
        Assert.Equal(("Develop", "3003", "3003", "1001"), (ticket.Status, ticket.AssigneeCode, ticket.DeveloperCode, ticket.OwnerCode));
        var events = await db.CrmTicketEvents.OrderBy(x => x.OccurredAt).ThenBy(x => x.Kind).ToListAsync();
        Assert.Equal((1, 1, 2), (events.Count(x => x.Kind == "Snapshot"), events.Count(x => x.Kind == "Changed"), events.Count(x => x.Kind == "Answer")));
        var change = events.Single(x => x.Kind == "Changed");
        Assert.Equal(("6101", "3003", "Test", "Develop"), (change.FromAssigneeCode, change.ToAssigneeCode, change.FromStatus, change.ToStatus));
    }

    [Fact]
    public void Flow_counts_handled_handoffs_closed_edit_err_and_time_with_qa()
    {
        var from = T0;
        var to = T0.AddDays(10);
        var events = new List<CrmTicketEvent>
        {
            // BHD-A: เห็นครั้งแรกตอนอยู่กับ 6101 แล้วส่ง Dev — นับดูแล/ส่ง Dev แต่ไม่นับเวลา (ไม่รู้เวลาเริ่มจริง)
            CrmTicketEvent.Route("BHD-A", "Snapshot", T0.AddHours(1), "Sync", null, "Test", null, "6101", null, null, "1001"),
            CrmTicketEvent.Route("BHD-A", "Changed", T0.AddHours(5), "Sync", "Test", "Develop", "6101", "3003", null, "3003", "1001"),
            // BHD-B: Support → 6101 (เริ่มจริง) → ส่งกลับแก้ EditErr ให้ Dev หลัง 10 ชม.
            CrmTicketEvent.Route("BHD-B", "Snapshot", T0.AddHours(1), "Sync", null, "Continue", null, "1001", null, null, "1001"),
            CrmTicketEvent.Route("BHD-B", "Changed", T0.AddHours(2), "Sync", "Continue", "Test", "1001", "6101", null, null, "1001"),
            CrmTicketEvent.Route("BHD-B", "Changed", T0.AddHours(12), "Sync", "Test", "EditErr", "6101", "3003", null, "3003", "1001"),
            // BHD-C: 6101 ปิดงานเอง
            CrmTicketEvent.Route("BHD-C", "Snapshot", T0.AddHours(1), "Sync", null, "Test", null, "6101", null, null, "1001"),
            CrmTicketEvent.Route("BHD-C", "Changed", T0.AddHours(3), "Sync", "Test", "Close", "6101", "6101", null, null, "1001"),
            // BHD-D: 6101 เป็นเจ้าของเรื่อง (Support) — ไม่ใช่ขั้น QA
            CrmTicketEvent.Route("BHD-D", "Snapshot", T0.AddHours(1), "Sync", null, "Continue", null, "6101", null, null, "6101"),
            // BHD-E: อยู่กับ 6101 ก่อนช่วงวันที่แล้วส่งกลับ Support ก่อนช่วง — ไม่นับ
            CrmTicketEvent.Route("BHD-E", "Snapshot", T0.AddDays(-5), "Sync", null, "Test", null, "6101", null, null, "1001"),
            CrmTicketEvent.Route("BHD-E", "Changed", T0.AddDays(-4), "Sync", "Test", "Continue", "6101", "1001", null, null, "1001"),
        };

        var stats = CrmPerformanceSource.ComputeFlow("6101", events, answers: 4, from, to);

        Assert.Equal(3, stats.Handled);           // A, B, C
        Assert.Equal(2, stats.ToDev);              // A, B
        Assert.Equal(0, stats.ToSupport);
        Assert.Equal(1, stats.Closed);             // C
        Assert.Equal(1, stats.EditErr);            // B
        Assert.Equal(4, stats.Answers);
        Assert.Equal(10.0m, stats.AvgQaHours);     // B เท่านั้น
    }

    [Fact]
    public async Task Legacy_flow_history_is_imported_once()
    {
        await using var db = CreateDb();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), "FlowStarted", "CrmTicketFlow", "BHD-9", "x", null, JsonSerializer.Serialize(new CrmFlowState("BHD-9", "Test", "1001", "เหรียญทอง (6101)", "0"), json), null));
        db.AuditLogs.Add(new AuditLog(Guid.NewGuid(), "FlowChanged", "CrmTicketFlow", "BHD-9", "x",
            JsonSerializer.Serialize(new CrmFlowState("BHD-9", "Test", "1001", "6101", "0"), json), JsonSerializer.Serialize(new CrmFlowState("BHD-9", "Develop", "1001", "3003", "3003"), json), null));
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.Equal(2, await service.ImportLegacyFlowOnceAsync(CancellationToken.None));
        Assert.Equal(0, await service.ImportLegacyFlowOnceAsync(CancellationToken.None));
        var events = await db.CrmTicketEvents.ToListAsync();
        Assert.All(events, e => Assert.Equal("Legacy", e.Source));
        Assert.Contains(events, e => e.Kind == "Changed" && e.FromAssigneeCode == "6101" && e.ToAssigneeCode == "3003" && e.ToDeveloperCode == "3003");
        Assert.Contains(events, e => e.Kind == "Snapshot" && e.ToAssigneeCode == "6101" && e.ToDeveloperCode == null);
    }

    [Fact]
    public void Mirror_queries_translate_to_sql()
    {
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
        Assert.Contains("DISTINCT", CrmPerformanceSource.InvolvedJobNos(db, "6101", T0).ToQueryString());
        Assert.Contains("WHERE", CrmPerformanceSource.RouteEvents(db, ["BHD-1"], T0).ToQueryString());
        Assert.Contains("WHERE", CrmPerformanceSource.AnswerCount(db, "6101", T0, T0.AddDays(1)).ToQueryString());
        Assert.Contains("EXISTS", CrmMirrorSyncService.PendingAnswerTickets(db, T0).Take(60).ToQueryString());
    }

    private static CrmMirrorRecord Record(string jobNo, string status, string assignee, string owner, string? developer, params (string No, string Posted)[] answers)
        => new(new CrmTicketListItem(jobNo, "เรื่อง", status, "HD", "Bug", "ProMaxx", assignee, owner, "member", null, null, null, "สาขา", developer, null),
            answers.Select(a => new CrmMirrorAnswer(a.No, a.Posted, T0.AddMinutes(-30))).ToList());

    private static CrmMirrorSyncService Service(QaDbContext db)
        => new(db, null!, new ConfigurationBuilder().Build(), NullLogger<CrmMirrorSyncService>.Instance);

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
