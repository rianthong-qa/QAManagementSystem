using System.Text.Json;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.UnitTests;

public sealed class CrmTicketListParserTests
{
    [Fact]
    public void Parse_ignores_crm_grouping_rows_and_enforces_current_assignee_scope()
    {
        const string json = """
        {
          "data": [
            { "owner": "สรุปตาม Owner", "count": 2 },
            { "jobNo": "BHD690902000002", "assignto": "6101", "status": "Continue", "subject": "Second", "contactDate": "02/09/2569" },
            { "jobNo": "BHD690901000001", "assignto": "6101", "status": "Close", "subject": "First", "contactDate": "01/09/2569" },
            { "jobNo": "BHD690903000003", "assignto": "6202", "status": "Open", "subject": "Other user" }
          ]
        }
        """;

        var result = CrmTicketListParser.Parse(
            json,
            new CrmTicketListQuery(1, 25, null, null, null, null),
            "6101",
            DateTimeOffset.Parse("2026-10-02T00:00:00Z"));

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Summary.InProgress);
        Assert.Equal(0, result.Summary.Open);
        Assert.Equal(1, result.Summary.Closed);
        Assert.Equal(new[] { "BHD690902000002", "BHD690901000001" }, result.Rows.Select(x => x.JobNo));
        // วันที่ไม่มีเวลาเป็นเที่ยงคืนตามเวลาไทย (00:00 +07:00) ไม่ใช่เที่ยงคืน UTC ซึ่งจะแสดงเป็น 07:00 น.
        Assert.Equal("2026-09-01T17:00:00.0000000Z", result.Rows[0].ContactDate);
    }

    [Fact]
    public void Parse_applies_search_status_and_server_side_pagination_after_scope_filter()
    {
        const string json = """
        [
          { "jobNo": "BHD-1", "assignto": "6101", "status": "Open", "subject": "Login issue", "contactDate": "2026-09-01" },
          { "jobNo": "BHD-2", "assignto": "6101", "status": "Open", "subject": "Login issue 2", "contactDate": "2026-09-02" },
          { "jobNo": "BHD-3", "assignto": "6101", "status": "Close", "subject": "Login issue 3", "contactDate": "2026-09-03" }
        ]
        """;

        var result = CrmTicketListParser.Parse(
            json,
            new CrmTicketListQuery(2, 1, "login", "Open", null, null),
            "6101",
            DateTimeOffset.UtcNow);

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Summary.Open);
        Assert.Single(result.Rows);
        Assert.Equal("BHD-1", result.Rows[0].JobNo);
    }

    [Fact]
    public void Parse_isolates_results_between_two_crm_user_scopes()
    {
        const string json = """
        [
          { "jobNo": "BHD-6101-1", "assignto": "6101", "status": "Open", "subject": "User one" },
          { "jobNo": "BHD-6101-2", "assignto": "เหรียญทอง เจือบุญ (6101)", "status": "Continue", "subject": "User one display" },
          { "jobNo": "BHD-6202-1", "assignto": "6202", "status": "Close", "subject": "User two" }
        ]
        """;

        var userOne = CrmTicketListParser.Parse(
            json,
            new CrmTicketListQuery(1, 25, null, null, null, null),
            "6101",
            DateTimeOffset.UtcNow);
        var userTwo = CrmTicketListParser.Parse(
            json,
            new CrmTicketListQuery(1, 25, null, null, null, null),
            "6202",
            DateTimeOffset.UtcNow);

        Assert.Equal(new[] { "BHD-6101-1", "BHD-6101-2" }, userOne.Rows.Select(x => x.JobNo).OrderBy(x => x));
        Assert.Equal(2, userOne.Total);
        Assert.Equal(new[] { "BHD-6202-1" }, userTwo.Rows.Select(x => x.JobNo).OrderBy(x => x));
        Assert.Equal(1, userTwo.Total);
    }

    [Fact]
    public void Parse_lists_tickets_handed_from_qa_to_developer_under_previous_scope()
    {
        const string json = """
        [
          { "jobNo": "BHD-QA-DEV", "assignto": "4208", "ownerSubjectId": "6101", "sysDevelop": "4208", "status": "Continue", "subject": "Handed to Dev" },
          { "jobNo": "BHD-OTHER", "assignto": "4208", "ownerSubjectId": "6303", "sysDevelop": "4208", "status": "Continue", "subject": "Other user's job" }
        ]
        """;

        var mine = CrmTicketListParser.Parse(
            json, new CrmTicketListQuery(1, 25, "BHD-QA-DEV", null, null, null), "6101", DateTimeOffset.UtcNow);
        var previous = CrmTicketListParser.Parse(
            json, new CrmTicketListQuery(1, 25, "BHD-QA-DEV", null, null, null, CrmTicketScope.Previous), "6101", DateTimeOffset.UtcNow);

        Assert.Empty(mine.Rows);
        Assert.Equal("BHD-QA-DEV", Assert.Single(previous.Rows).JobNo);
    }

    [Fact]
    public void Parse_moves_ticket_assigned_to_someone_else_out_of_mine_even_when_user_is_developer()
    {
        // BHD690928000002: Owner = Assignto = Support 5807, sysDevelop = 6101, status Test
        const string json = """
        [
          { "jobNo": "BHD690928000002", "assignto": "5807", "ownerSubjectId": "5807", "sysDevelop": "6101", "status": "Test" },
          { "jobNo": "BHD-DEV-CLOSED", "assignto": "5807", "ownerSubjectId": "5807", "sysDevelop": "6101", "status": "Finish" },
          { "jobNo": "BHD-MINE", "assignto": "6101", "ownerSubjectId": "5807", "sysDevelop": "6101", "status": "Close" }
        ]
        """;

        var mine = CrmTicketListParser.Parse(
            json, new CrmTicketListQuery(1, 25, null, null, null, null), "6101", DateTimeOffset.UtcNow);
        var previous = CrmTicketListParser.Parse(
            json, new CrmTicketListQuery(1, 25, null, null, null, null, CrmTicketScope.Previous), "6101", DateTimeOffset.UtcNow);

        Assert.Equal("BHD-MINE", Assert.Single(mine.Rows).JobNo);
        Assert.Equal("BHD690928000002", Assert.Single(previous.Rows).JobNo);
        Assert.Equal(new CrmTicketScopeCounts(1, 1), mine.ScopeCounts);
    }

    [Fact]
    public void Parse_keeps_active_ticket_handed_back_from_qa_to_support_but_drops_closed_ones()
    {
        // BHD691006000009: Support 6710 → QA 6101 → back to Support 6710
        const string json = """
        [
          { "fd": { "jobNo": "BHD-BACK", "assignto": "6710", "ownerSubjectId": "6710", "status": "Test" }, "answers": [] },
          { "fd": { "jobNo": "BHD-BACK-CLOSED", "assignto": "6710", "ownerSubjectId": "6710", "status": "Close" }, "answers": [] },
          { "fd": { "jobNo": "BHD-BACK-FINISH", "assignto": "6710", "ownerSubjectId": "6710", "status": "Finish" }, "answers": [] },
          { "fd": { "jobNo": "BHD-REPLIED", "assignto": "6710", "ownerSubjectId": "6710", "status": "Continue" },
            "answers": [ { "answerNo": "1", "posted": "เหรียญทอง เจือบุญ (6101)" } ] },
          { "fd": { "jobNo": "BHD-REPLIED-FINISH", "assignto": "6710", "ownerSubjectId": "6710", "status": "Finish" },
            "answers": [ { "answerNo": "2", "posted": "6101" } ] },
          { "fd": { "jobNo": "BHD-UNRELATED", "assignto": "6710", "ownerSubjectId": "6710", "status": "Continue" },
            "answers": [ { "answerNo": "3", "posted": "61010" } ] }
        ]
        """;

        var previouslyAssigned = new HashSet<string>(["bhd-back", "BHD-BACK-CLOSED", "BHD-BACK-FINISH"], StringComparer.OrdinalIgnoreCase);

        var mine = CrmTicketListParser.Parse(
            json, new CrmTicketListQuery(1, 25, null, null, null, null), "6101", DateTimeOffset.UtcNow, previouslyAssigned);
        var previous = CrmTicketListParser.Parse(
            json, new CrmTicketListQuery(1, 25, null, null, null, null, CrmTicketScope.Previous), "6101", DateTimeOffset.UtcNow, previouslyAssigned);

        Assert.Empty(mine.Rows);
        Assert.Equal(new CrmTicketScopeCounts(0, 2), mine.ScopeCounts);
        Assert.Equal(new[] { "BHD-BACK", "BHD-REPLIED" }, previous.Rows.Select(x => x.JobNo).OrderBy(x => x));
        Assert.Equal(2, previous.Total);
        Assert.Equal(new CrmTicketScopeCounts(0, 2), previous.ScopeCounts);
    }

    [Fact]
    public void Parse_separates_current_work_from_handed_back_tickets_and_counts_both_after_filters()
    {
        const string json = """
        [
          { "jobNo": "BHD-MINE-1", "assignto": "6101", "status": "Continue", "subject": "Login" },
          { "jobNo": "BHD-MINE-2", "assignto": "6101", "status": "Open", "subject": "Printer" },
          { "jobNo": "BHD-BACK", "assignto": "6710", "ownerSubjectId": "6710", "status": "Test", "subject": "Login back" }
        ]
        """;
        var previouslyAssigned = new HashSet<string>(["BHD-BACK", "BHD-MINE-1"], StringComparer.OrdinalIgnoreCase);

        var mine = CrmTicketListParser.Parse(
            json, new CrmTicketListQuery(1, 25, "login", null, null, null), "6101", DateTimeOffset.UtcNow, previouslyAssigned);
        var previous = CrmTicketListParser.Parse(
            json, new CrmTicketListQuery(1, 25, "login", null, null, null, CrmTicketScope.Previous), "6101", DateTimeOffset.UtcNow, previouslyAssigned);

        // A ticket currently assigned to the user stays in "mine" even if it was also held before.
        Assert.Equal(new[] { "BHD-MINE-1" }, mine.Rows.Select(x => x.JobNo));
        Assert.Equal(new[] { "BHD-BACK" }, previous.Rows.Select(x => x.JobNo));
        Assert.Equal(new CrmTicketScopeCounts(1, 1), mine.ScopeCounts);
        Assert.Equal(1, previous.Summary.InProgress);
    }

    [Fact]
    public void DetailParser_accepts_active_ticket_previously_assigned_to_current_user()
    {
        using var active = JsonDocument.Parse("""
        { "jobNo": "BHD-9", "assignto": "6710", "ownerSubjectId": "6710", "status": "Test" }
        """);
        using var closed = JsonDocument.Parse("""
        { "jobNo": "BHD-9", "assignto": "6710", "ownerSubjectId": "6710", "status": "Close" }
        """);

        Assert.Equal("BHD-9", CrmTicketDetailParser.Parse(active.RootElement, "BHD-9", "6101", [], DateTimeOffset.UtcNow, wasPreviouslyAssigned: true).Ticket.JobNo);
        Assert.Throws<CrmTicketNotFoundException>(() => CrmTicketDetailParser.Parse(
            active.RootElement, "BHD-9", "6101", [], DateTimeOffset.UtcNow));
        Assert.Throws<CrmTicketNotFoundException>(() => CrmTicketDetailParser.Parse(
            closed.RootElement, "BHD-9", "6101", [], DateTimeOffset.UtcNow, wasPreviouslyAssigned: true));
    }

    [Fact]
    public void Ticket_scope_matches_owner_or_developer_without_matching_unrelated_staff()
    {
        var handedToDeveloper = CrmTicketListParser.MapTicket(JsonDocument.Parse("""
        { "jobNo": "BHD-QA-DEV", "assignto": "4208", "ownerSubjectId": "6101", "sysDevelop": "4208" }
        """).RootElement)!;
        var unrelated = CrmTicketListParser.MapTicket(JsonDocument.Parse("""
        { "jobNo": "BHD-OTHER", "assignto": "4208", "ownerSubjectId": "6303", "sysDevelop": "4208" }
        """).RootElement)!;

        Assert.True(CrmTicketListParser.IsTicketInScope(handedToDeveloper, "6101"));
        Assert.True(CrmTicketListParser.IsTicketInScope(handedToDeveloper, "4208"));
        Assert.False(CrmTicketListParser.IsTicketInScope(unrelated, "6101"));
    }

    [Fact]
    public void Parse_accepts_crm_display_assignee_with_code_and_buddhist_contact_date()
    {
        const string json = """
        [
          { "jobNo": "BHD690917000005", "assignto": "เหรียญทอง เจือบุญ (6101)", "status": "Continue", "subject": "Sleep mode", "contactDate": "17/09/2569 17:09" },
          { "jobNo": "BHD690917000006", "assignto": "ธวัช ใจแสน (4208)", "status": "Open", "subject": "Other user", "contactDate": "17/09/2569 17:10" }
        ]
        """;

        var result = CrmTicketListParser.Parse(
            json,
            new CrmTicketListQuery(1, 25, null, null, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)),
            "6101",
            DateTimeOffset.UtcNow);

        Assert.Single(result.Rows);
        Assert.Equal("BHD690917000005", result.Rows[0].JobNo);
        Assert.Equal("2026-09-17T10:09:00.0000000Z", result.Rows[0].ContactDate);
    }

    [Fact]
    public void Assignee_scope_does_not_match_partial_code()
    {
        Assert.False(CrmTicketListParser.IsAssigneeInScope("เหรียญทอง เจือบุญ (61010)", "6101"));
        Assert.True(CrmTicketListParser.IsAssigneeInScope("เหรียญทอง เจือบุญ (6101)", "6101"));
    }

    [Fact]
    public void Parse_unwraps_real_helpdesk_export_fd_records()
    {
        const string json = """
        [
          {
            "fd": {
              "jobNo": "BHD690917000005",
              "assignto": "เหรียญทอง เจือบุญ (6101)",
              "status": "Continue",
              "subject": "Sleep mode",
              "contactDate": "17/09/2569 17:09"
            },
            "answers": []
          }
        ]
        """;

        var result = CrmTicketListParser.Parse(
            json,
            new CrmTicketListQuery(1, 25, null, null, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)),
            "6101",
            DateTimeOffset.UtcNow);

        Assert.Single(result.Rows);
        Assert.Equal("BHD690917000005", result.Rows[0].JobNo);
    }

    [Fact]
    public void DetailParser_returns_description_and_answers_for_current_assignee()
    {
        using var document = JsonDocument.Parse("""
        { "jobNo": "BHD-9", "assignto": "6101", "subject": "Printer", "status": "Open", "description": "Paper jam" }
        """);

        var result = CrmTicketDetailParser.Parse(
            document.RootElement,
            "BHD-9",
            "6101",
            [new CrmTicketAnswerItem("1", "รับเรื่องแล้ว", "6101", "2026-10-03T08:00:00Z", "A", null)],
            DateTimeOffset.Parse("2026-10-03T09:00:00Z"));

        Assert.Equal("Paper jam", result.Description);
        Assert.Single(result.Answers);
        Assert.Equal("รับเรื่องแล้ว", result.Answers[0].Description);
    }

    [Fact]
    public void DetailParser_rejects_ticket_outside_current_ticket_scope()
    {
        using var document = JsonDocument.Parse("""
        { "jobNo": "BHD-9", "assignto": "6202", "subject": "Other user" }
        """);

        Assert.Throws<CrmTicketNotFoundException>(() => CrmTicketDetailParser.Parse(
            document.RootElement, "BHD-9", "6101", [], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DetailParser_accepts_ticket_handed_to_developer_when_current_user_is_owner()
    {
        using var document = JsonDocument.Parse("""
        { "jobNo": "BHD-9", "assignto": "4208", "ownerSubjectId": "6101", "sysDevelop": "4208", "subject": "Handed over" }
        """);

        var result = CrmTicketDetailParser.Parse(
            document.RootElement, "BHD-9", "6101", [], DateTimeOffset.UtcNow);

        Assert.Equal("BHD-9", result.Ticket.JobNo);
    }

    [Fact]
    public void MapTicket_accepts_detail_field_aliases_from_crm()
    {
        using var document = JsonDocument.Parse("""
        {
          "JobNo": "BHD-10", "Assignto": "6101", "sysDevelop": "4208", "sysServiceTypeName": "Question",
          "sysProductName": "SNS ProMaxx", "ownerSubjectName": "6511", "ContactName": "Customer",
          "lastReplyDate": "2026-10-03T10:00:00Z"
        }
        """);

        var result = CrmTicketListParser.MapTicket(document.RootElement);

        Assert.NotNull(result);
        Assert.Equal("Question", result.ServiceType);
        Assert.Equal("SNS ProMaxx", result.Product);
        Assert.Equal("6511", result.Owner);
        Assert.Equal("4208", result.Developer);
        Assert.Equal("Customer", result.Member);
        Assert.Equal("2026-10-03T10:00:00.0000000Z", result.LastReplyAt);
    }

    [Fact]
    public void MapTicket_treats_crm_zero_developer_as_not_assigned()
    {
        using var document = JsonDocument.Parse("""
        { "jobNo": "BHD-12", "assignto": "6101", "sysDevelop": "0" }
        """);

        var result = CrmTicketListParser.MapTicket(document.RootElement);

        Assert.Null(result?.Developer);
    }

    [Fact]
    public void MapTicket_normalizes_crm_closed_status_to_close_for_the_ui()
    {
        using var document = JsonDocument.Parse("""
        { "jobNo": "BHD-11", "status": "Closed", "assignto": "6101" }
        """);

        var result = CrmTicketListParser.MapTicket(document.RootElement);

        Assert.Equal("Close", result?.Status);
    }

    [Fact]
    public void ParseHelpDeskAnswers_handles_null_and_non_string_optional_fields()
    {
        using var document = JsonDocument.Parse("""
        {
          "helpDeskAnswers": [
            { "answerNo": "1", "description": "รับเรื่องแล้ว", "posted": "6101", "ansDate": 20261003080000, "fanswerType": "A", "image": 12 },
            { "answerNo": "2", "description": "", "posted": "6101", "ansDate": null, "fanswerType": "A", "image": null },
            { "description": "ข้ามรายการนี้เพราะไม่มีเลขคำตอบ" }
          ]
        }
        """);

        var result = CrmApiClient.ParseHelpDeskAnswers(document.RootElement);

        Assert.Equal(2, result.Count);
        Assert.Equal("20261003080000", result[0].AnsDate);
        Assert.Equal("12", result[0].Image);
        Assert.Null(result[1].AnsDate);
        Assert.Null(result[1].Image);
    }

    [Fact]
    public void ParseJobDetail_accepts_array_and_common_wrapper_shapes()
    {
        using var document = JsonDocument.Parse("""
        {
          "result": {
            "data": [ { "jobNo": "BHD-11", "assignto": "6101", "subject": "Wrapped" } ]
          }
        }
        """);

        var result = CrmApiClient.ParseJobDetail(document.RootElement, "BHD-11");

        Assert.Equal("BHD-11", result.GetProperty("jobNo").GetString());
        Assert.Equal("Wrapped", result.GetProperty("subject").GetString());
    }

    [Fact]
    public void ParseJobDetail_rejects_unsupported_response_shape()
    {
        using var document = JsonDocument.Parse("""{ "message": "not a job" }""");

        Assert.Throws<CrmIntegrationException>(() => CrmApiClient.ParseJobDetail(document.RootElement, "BHD-12"));
    }

    [Fact]
    public void ParseLookup_reads_numeric_ids_wrapped_arrays_and_ignores_case()
    {
        var result = CrmApiClient.ParseLookup("""{ "data": [ { "SysProductID": 12, "ProductName": "iConnect2" }, { "sysProductId": "3", "productName": "ProMaxx" }, { "sysProductId": "3", "productName": "Dup" }, { "productName": "no id" } ] }""",
            "Products", ["sysProductId", "productId", "id"], ["productName", "name"]);

        Assert.Equal(new[] { "12:iConnect2", "3:ProMaxx" }, result.Select(x => $"{x.Id}:{x.Name}"));
    }

    [Fact]
    public void ParseLookup_reports_field_names_when_items_cannot_be_read()
    {
        var ex = Assert.Throws<CrmBadResponseException>(() => CrmApiClient.ParseLookup("""[ { "code": "A", "title": "x" } ]""", "Products", ["sysProductId"], ["productName"]));

        Assert.Contains("code", ex.Message);
        Assert.Contains("title", ex.Message);
        Assert.DoesNotContain("\"x\"", ex.Message);
    }

    [Fact]
    public void ParseLookup_returns_empty_for_empty_array()
    {
        Assert.Empty(CrmApiClient.ParseLookup("[]", "Products", ["id"], ["name"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "message": "not a job" }""")]
    [InlineData("<html><body>Not Found</body></html>")]
    public void TryParseJobDetail_returns_null_for_unknown_job_responses(string body)
    {
        Assert.Null(CrmApiClient.TryParseJobDetail(body));
    }

    [Fact]
    public void TryParseJobDetail_returns_job_from_wrapper()
    {
        var result = CrmApiClient.TryParseJobDetail("""{ "data": { "jobNo": "BHD-10", "subject": "Found" } }""");

        Assert.NotNull(result);
        Assert.Equal("Found", result.Value.GetProperty("subject").GetString());
    }

    [Fact]
    public void ParseHelpDeskAnswers_accepts_wrapper_and_pascal_case_fields()
    {
        using var document = JsonDocument.Parse("""
        {
          "result": {
            "answers": [ { "AnswerNo": "3", "Description": "ตอบกลับแล้ว", "Posted": "6101", "AnsDate": "2026-10-03T08:00:00Z", "FAnswerType": "A", "Image": null } ]
          }
        }
        """);

        var result = CrmApiClient.ParseHelpDeskAnswers(document.RootElement);

        Assert.Single(result);
        Assert.Equal("3", result[0].AnswerNo);
        Assert.Equal("ตอบกลับแล้ว", result[0].Description);
        Assert.Equal("2026-10-03T08:00:00Z", result[0].AnsDate);
    }

    [Theory]
    [InlineData("17/09/2569 17:09", "2026-09-17T10:09:00.0000000Z")]
    [InlineData("17/09/2026 17:09:30", "2026-09-17T10:09:30.0000000Z")]
    [InlineData("2026-10-03T08:00:00", "2026-10-03T01:00:00.0000000Z")]
    [InlineData("2026-10-03T08:00:00Z", "2026-10-03T08:00:00.0000000Z")]
    [InlineData("2026-10-03T08:00:00+07:00", "2026-10-03T01:00:00.0000000Z")]
    [InlineData("20261003080000", "2026-10-03T01:00:00.0000000Z")]
    [InlineData("25691003080000", "2026-10-03T01:00:00.0000000Z")]
    [InlineData("03/10/2569", "2026-10-02T17:00:00.0000000Z")]
    public void NormalizeDate_treats_crm_values_without_offset_as_bangkok_time(string input, string expected)
    {
        Assert.Equal(expected, CrmTicketListParser.NormalizeDate(input));
    }

    [Fact]
    public void Parse_filters_date_range_by_bangkok_calendar_day()
    {
        // 01/10/2569 03:00 น. เวลาไทย = 30/09 20:00 UTC — ต้องนับเป็นวันที่ 1 ต.ค. ตามปฏิทินไทย
        const string json = """
        [
          { "jobNo": "BHD-EARLY", "assignto": "6101", "status": "Open", "subject": "Early morning", "contactDate": "01/10/2569 03:00" },
          { "jobNo": "BHD-LATE", "assignto": "6101", "status": "Open", "subject": "Late night", "contactDate": "30/09/2569 23:30" }
        ]
        """;

        var result = CrmTicketListParser.Parse(
            json,
            new CrmTicketListQuery(1, 25, null, null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1)),
            "6101",
            DateTimeOffset.UtcNow);

        Assert.Equal(new[] { "BHD-EARLY" }, result.Rows.Select(x => x.JobNo));
    }
}
