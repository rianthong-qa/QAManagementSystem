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
    public void DetailParser_rejects_ticket_outside_current_assignee_scope()
    {
        using var document = JsonDocument.Parse("""
        { "jobNo": "BHD-9", "assignto": "6202", "subject": "Other user" }
        """);

        Assert.Throws<CrmTicketNotFoundException>(() => CrmTicketDetailParser.Parse(
            document.RootElement, "BHD-9", "6101", [], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MapTicket_accepts_detail_field_aliases_from_crm()
    {
        using var document = JsonDocument.Parse("""
        {
          "JobNo": "BHD-10", "Assignto": "6101", "sysServiceTypeName": "Question",
          "sysProductName": "SNS ProMaxx", "ownerSubjectName": "6511", "ContactName": "Customer",
          "lastReplyDate": "2026-10-03T10:00:00Z"
        }
        """);

        var result = CrmTicketListParser.MapTicket(document.RootElement);

        Assert.NotNull(result);
        Assert.Equal("Question", result.ServiceType);
        Assert.Equal("SNS ProMaxx", result.Product);
        Assert.Equal("6511", result.Owner);
        Assert.Equal("Customer", result.Member);
        Assert.Equal("2026-10-03T10:00:00.0000000Z", result.LastReplyAt);
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
