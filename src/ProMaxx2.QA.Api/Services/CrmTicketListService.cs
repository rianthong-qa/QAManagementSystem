using System.Globalization;
using System.Text.Json;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// <c>Mine</c>: the user is the current Assignto.
/// <c>Previous</c>: active (not Close/Finish) tickets now with someone else that the user is still part of —
/// Owner, Developer, or a previous Assignto (e.g. QA → back to Support).
/// </summary>
public enum CrmTicketScope { Mine, Previous }

public sealed record CrmTicketListQuery(
    int Page,
    int PageSize,
    string? Search,
    string? Status,
    DateOnly? From,
    DateOnly? To,
    CrmTicketScope Scope = CrmTicketScope.Mine);

public sealed record CrmTicketScopeCounts(int Mine, int Previous);

public sealed record CrmTicketListItem(
    string JobNo,
    string? Subject,
    string? Status,
    string? JobType,
    string? ServiceType,
    string? Product,
    string? Assignee,
    string? Owner,
    string? Member,
    string? ContactDate,
    string? DueDate,
    string? LastReplyAt,
    string? Branch,
    string? Developer,
    string? Email);

public sealed record CrmTicketSummary(int Total, int Open, int InProgress, int Closed);

public sealed record CrmTicketListDiagnostics(
    string RootKind,
    int RecordCount,
    int RecordsWithJobNo,
    int RecordsWithAssignee,
    int RecordsWithContactDate,
    IReadOnlyList<string> SamplePropertyNames);

public sealed record CrmTicketListResult(
    IReadOnlyList<CrmTicketListItem> Rows,
    int Total,
    int Page,
    int PageSize,
    CrmTicketSummary Summary,
    DateTimeOffset LastFetchedAt,
    CrmTicketScopeCounts ScopeCounts,
    // jobNo → last QA from Flow Tracking history, for tickets that have been sent back to Support
    IReadOnlyDictionary<string, string>? PreviousQa = null);

public sealed record CrmConnectionStatus(
    bool IsConfigured,
    bool IsEnabled,
    string? Username,
    DateTimeOffset? UpdatedAt);

public sealed record CrmConnectionProbeResult(bool IsReachable, DateTimeOffset CheckedAt);

public sealed class CrmResultTooLargeException(string message) : Exception(message);
public sealed class CrmBadResponseException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class CrmTimeoutException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Maps the HelpDeskExport response into the QA Hub contract. CRM returns summary/grouping rows
/// together with ticket rows, so JobNo is deliberately required before a row is exposed to callers.
/// </summary>
public static class CrmTicketListParser
{
    private const int MaxTicketRows = 5000;

    public static CrmTicketListResult Parse(
        string body,
        CrmTicketListQuery query,
        string currentCrmUsername,
        DateTimeOffset lastFetchedAt,
        IReadOnlySet<string>? previouslyAssignedJobNos = null)
    {
        using var document = JsonDocument.Parse(body);
        var records = CoerceRecords(document.RootElement);
        var tickets = new List<CrmTicketListItem>();
        int mineCount = 0, previousCount = 0;

        foreach (var record in records)
        {
            var item = MapTicket(record);
            if (item is null) continue;
            // "ส่งมาหาฉัน" = Assignto is the user right now. Being Owner/Developer or a past holder only keeps
            // an active ticket under "ส่งต่อแล้ว" — the work is with someone else (user rule, 2026-10-06).
            var isMine = IsAssigneeInScope(item.Assignee, currentCrmUsername);
            var isPrevious = !isMine && !IsClosed(item.Status) &&
                             (IsTicketInScope(item, currentCrmUsername) ||
                              (previouslyAssignedJobNos?.Contains(item.JobNo) ?? false) ||
                              HasAnswerPostedBy(record, currentCrmUsername));
            if (!isMine && !isPrevious) continue;

            if (!MatchesQuery(item, query)) continue;
            if (isMine) mineCount++; else previousCount++;
            if (isMine != (query.Scope == CrmTicketScope.Mine)) continue;
            tickets.Add(item);
            if (tickets.Count > MaxTicketRows)
                throw new CrmResultTooLargeException($"CRM ส่งข้อมูลเกินขีดจำกัด {MaxTicketRows:N0} รายการ กรุณาระบุช่วงวันที่ให้แคบลง");
        }

        var ordered = tickets
            .OrderByDescending(x => x.ContactDate ?? string.Empty, StringComparer.Ordinal)
            .ThenByDescending(x => x.JobNo, StringComparer.Ordinal)
            .ToArray();
        var summary = new CrmTicketSummary(
            ordered.Length,
            ordered.Count(x => string.Equals(x.Status, "Open", StringComparison.OrdinalIgnoreCase)),
            ordered.Count(x => !IsClosed(x.Status) && !string.Equals(x.Status, "Open", StringComparison.OrdinalIgnoreCase)),
            ordered.Count(x => IsClosed(x.Status)));
        var rows = ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray();

        return new(rows, ordered.Length, query.Page, query.PageSize, summary, lastFetchedAt, new(mineCount, previousCount));
    }

    /// <summary>Maps a single CRM job record into the normalized list contract.</summary>
    public static CrmTicketListItem? MapTicket(JsonElement record)
    {
        record = TicketRecord(record);
        var jobNo = StringValue(record, "jobNo", "JobNo", "jobno");
        if (string.IsNullOrWhiteSpace(jobNo)) return null;
        var assignee = StringValue(
            record,
            "assignto", "assignTo", "Assignto", "SAssignTo",
            "assignee", "Assignee", "assigneeCode", "AssigneeCode",
            "assignToCode", "AssignToCode", "assigntoName", "AssigntoName",
            "assignToName", "AssignToName");
        return new CrmTicketListItem(
            jobNo.Trim(),
            StringValue(record, "subject", "Subject"),
            NormalizeStatus(StringValue(record, "status", "Status")),
            StringValue(record, "jobType", "JobType"),
            StringValue(record, "sysserViceTypeName", "sysServiceTypeName", "serviceType", "ServiceType", "service"),
            StringValue(record, "productName", "sysProductName", "product", "Product"),
            assignee,
            StringValue(record, "ownerSubject", "ownerSubjectId", "OwnerSubjectId", "ownerSubjectName", "owner"),
            StringValue(record, "member", "Member", "contactName", "ContactName", "fname", "FName"),
            NormalizeDate(StringValue(record, "contactDate", "ContactDate")),
            NormalizeDate(StringValue(record, "duedate", "dueDate", "Duedate", "DueDate")),
            NormalizeDate(StringValue(record, "ansDate", "lastReplyAt", "lastReplyDate", "lastAnswerDate", "lastReply", "LastReplyAt", "LastReplyDate")),
            StringValue(record, "branchName", "branch", "Branch"),
            NormalizeDeveloper(StringValue(record, "sysDevelop", "sysDevelopName", "develop", "developer", "Developer")),
            StringValue(record, "email", "Email"));
    }

    public static string? ReadString(JsonElement record, params string[] names) => StringValue(record, names);

    private static string? NormalizeDeveloper(string? value) =>
        string.Equals(value?.Trim(), "0", StringComparison.OrdinalIgnoreCase) ? null : value;

    // CRM's update endpoint accepts "Closed", while its list/detail responses and QA Hub UI use "Close".
    public static string? NormalizeStatus(string? status) =>
        string.Equals(status?.Trim(), "Closed", StringComparison.OrdinalIgnoreCase) ? "Close" : status;

    /// <summary>Returns safe shape/count diagnostics without exposing CRM ticket values.</summary>
    public static CrmTicketListDiagnostics Diagnose(string body)
    {
        using var document = JsonDocument.Parse(body);
        var records = CoerceRecords(document.RootElement);
        var jobNoNames = new[] { "jobNo", "JobNo", "jobno" };
        var assigneeNames = new[]
        {
            "assignto", "assignTo", "Assignto", "SAssignTo", "assignee", "Assignee",
            "assigneeCode", "AssigneeCode", "assignToCode", "AssignToCode", "assigntoName",
            "AssigntoName", "assignToName", "AssignToName"
        };
        var contactDateNames = new[] { "contactDate", "ContactDate" };
        var propertyNames = records
            .Where(x => x.ValueKind == JsonValueKind.Object)
            .SelectMany(x => x.EnumerateObject().Select(p => p.Name))
            .Concat(records
                .Select(TicketRecord)
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .SelectMany(x => x.EnumerateObject().Select(p => p.Name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToArray();

        return new(
            document.RootElement.ValueKind.ToString(),
            records.Count,
            records.Count(x => HasValue(TicketRecord(x), jobNoNames)),
            records.Count(x => HasValue(TicketRecord(x), assigneeNames)),
            records.Count(x => HasValue(TicketRecord(x), contactDateNames)),
            propertyNames);
    }

    /// <summary>
    /// CRM displays an assignee as a human-readable name followed by the CRM code,
    /// e.g. "เหรียญทอง เจือบุญ (6101)". The configured username is the code only,
    /// so ownership checks must recognize both representations without using a broad
    /// substring match that could cross user boundaries.
    /// </summary>
    public static bool IsAssigneeInScope(string? assignee, string? currentCrmUsername)
    {
        if (string.IsNullOrWhiteSpace(assignee) || string.IsNullOrWhiteSpace(currentCrmUsername)) return false;

        var expected = currentCrmUsername.Trim();
        var actual = assignee.Trim();
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;

        var tokens = actual.Split(
            new[] { ' ', '\t', '\r', '\n', '(', ')', '[', ']', '{', '}', ',', ';', '|', ':' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Any(token => string.Equals(token, expected, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A CRM ticket can move from QA to Development by changing Assignto while keeping
    /// the original owner and development staff on the ticket. Keep the ticket visible
    /// to any staff member who is part of that route, without exposing unrelated jobs.
    /// A ticket the user previously held (e.g. Support → QA → back to Support) stays visible
    /// only while it is still active; Close/Finish tickets drop out once ownership moves on.
    /// </summary>
    public static bool IsTicketInScope(CrmTicketListItem item, string? currentCrmUsername, bool wasPreviouslyAssigned = false) =>
        IsAssigneeInScope(item.Assignee, currentCrmUsername) ||
        IsAssigneeInScope(item.Owner, currentCrmUsername) ||
        IsAssigneeInScope(item.Developer, currentCrmUsername) ||
        (wasPreviouslyAssigned && !IsClosed(item.Status));

    /// <summary>
    /// HelpDeskExport records are shaped as <c>{ fd, answers }</c>. An answer posted by the user
    /// means they worked the ticket before it was handed back, even if QA Hub never saw that state.
    /// </summary>
    private static bool HasAnswerPostedBy(JsonElement record, string? currentCrmUsername)
    {
        if (record.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(currentCrmUsername)) return false;
        foreach (var name in new[] { "answers", "Answers" })
        {
            if (!record.TryGetProperty(name, out var answers) || answers.ValueKind != JsonValueKind.Array) continue;
            return answers.EnumerateArray().Any(answer =>
                answer.ValueKind == JsonValueKind.Object &&
                IsAssigneeInScope(StringValue(answer, "posted", "Posted"), currentCrmUsername));
        }
        return false;
    }

    private static bool MatchesQuery(CrmTicketListItem item, CrmTicketListQuery query)
    {
        if (!MatchesDateRange(item.ContactDate, query.From, query.To)) return false;
        if (!string.IsNullOrWhiteSpace(query.Status) &&
            !string.Equals(item.Status, query.Status.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(query.Search)) return true;

        var search = query.Search.Trim();
        return Contains(item.JobNo, search) || Contains(item.Subject, search) || Contains(item.Member, search) ||
               Contains(item.ServiceType, search) || Contains(item.Product, search);
    }

    private static bool MatchesDateRange(string? normalizedDate, DateOnly? from, DateOnly? to)
    {
        if (string.IsNullOrWhiteSpace(normalizedDate) || (!from.HasValue && !to.HasValue)) return true;
        // ช่วงวันที่ที่ผู้ใช้เลือกเป็นวันตามปฏิทินไทย — ต้องแปลงค่า UTC กลับเป็นวันที่กรุงเทพก่อนเทียบ
        // (ถ้าตัด 10 ตัวแรกของ ISO UTC ตรง ๆ Ticket ที่ติดต่อช่วง 00:00–06:59 น. จะตกไปเป็นวันก่อนหน้า)
        if (!DateTimeOffset.TryParse(normalizedDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) return true;
        var date = DateOnly.FromDateTime(parsed.ToOffset(BangkokOffset).DateTime);
        if (from.HasValue && date < from.Value) return false;
        if (to.HasValue && date > to.Value) return false;
        return true;
    }

    private static bool Contains(string? value, string search) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(search, StringComparison.OrdinalIgnoreCase);

    public static bool IsClosed(string? status) =>
        string.Equals(status, "Close", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Finish", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<JsonElement> CoerceRecords(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().Select(x => x.Clone()).ToArray();
        if (root.ValueKind != JsonValueKind.Object) return [];
        foreach (var key in new[] { "data", "Data", "result", "Result", "items", "Items", "rows", "Rows" })
        {
            if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array)
                return value.EnumerateArray().Select(x => x.Clone()).ToArray();
        }
        return [root.Clone()];
    }

    private static string? StringValue(JsonElement record, params string[] names)
    {
        foreach (var name in names)
        {
            if (!record.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        }
        return null;
    }

    private static bool HasValue(JsonElement record, IEnumerable<string> names) =>
        record.ValueKind == JsonValueKind.Object &&
        names.Any(name => record.TryGetProperty(name, out var value) &&
                          value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined);

    private static JsonElement TicketRecord(JsonElement record)
    {
        if (record.ValueKind == JsonValueKind.Object &&
            record.TryGetProperty("fd", out var nested) &&
            nested.ValueKind == JsonValueKind.Object)
            return nested;
        return record;
    }

    private static readonly TimeSpan BangkokOffset = TimeSpan.FromHours(7);

    /// <summary>
    /// แปลงวันที่จาก CRM เป็น ISO UTC (`...Z`) ให้ frontend แสดงเป็นเวลาไทยได้ถูกต้อง. CRM ส่งเวลาไทยโดยไม่มี offset
    /// หลายรูปแบบ: `dd/MM/yyyy[ HH:mm[:ss]]` (ปี พ.ศ. หรือ ค.ศ.), `yyyy-MM-dd[THH:mm:ss]` และตัวเลขล้วน
    /// `yyyyMMdd[HHmm[ss]]` — ค่าที่ไม่มี offset (รวมวันที่ที่ไม่มีเวลา) ถือเป็นเวลากรุงเทพ (+07:00) เสมอ
    /// ส่วนค่าที่มี `Z` หรือ `+hh:mm` ใช้ offset นั้นตามจริง
    /// </summary>
    public static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.Length is 8 or 12 or 14 && text.All(char.IsAsciiDigit))
        {
            var format = text.Length switch { 8 => "yyyyMMdd", 12 => "yyyyMMddHHmm", _ => "yyyyMMddHHmmss" };
            if (DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var compact))
            {
                if (compact.Year > 2400) compact = compact.AddYears(-543);
                return ToIsoUtc(new DateTimeOffset(compact, BangkokOffset));
            }
        }
        var dateTimeSeparator = text.IndexOfAny(new[] { ' ', 'T' });
        var dateText = dateTimeSeparator >= 0 ? text[..dateTimeSeparator] : text;
        var parts = dateText.Split(new[] { '/', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && parts.All(x => int.TryParse(x, out _)) &&
            int.TryParse(parts[0], out var first) && int.TryParse(parts[1], out var second) && int.TryParse(parts[2], out var third))
        {
            var year = first > 31 ? first : third;
            var month = second;
            var day = first > 31 ? third : first;
            if (year > 2400) year -= 543;
            if (DateTime.TryParseExact($"{day:00}/{month:00}/{year:0000}", "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                var time = TimeSpan.Zero;
                var offset = BangkokOffset;
                if (dateTimeSeparator >= 0)
                {
                    var timeText = text[(dateTimeSeparator + 1)..].Trim();
                    if (timeText.EndsWith('Z'))
                    {
                        offset = TimeSpan.Zero;
                        timeText = timeText[..^1];
                    }
                    var offsetIndex = timeText.IndexOfAny(new[] { '+', '-' });
                    if (offsetIndex > 0)
                    {
                        var offsetText = timeText[offsetIndex..];
                        var negative = offsetText[0] == '-';
                        if (TimeSpan.TryParse(offsetText[1..], CultureInfo.InvariantCulture, out var parsedOffset))
                            offset = negative ? -parsedOffset : parsedOffset;
                        timeText = timeText[..offsetIndex];
                    }
                    TimeSpan.TryParse(timeText, CultureInfo.InvariantCulture, out time);
                }

                return ToIsoUtc(new DateTimeOffset(date.Add(time), offset));
            }
        }
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            var hasOffset = text.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(text, @"[+-]\d{2}:?\d{2}$");
            return ToIsoUtc(hasOffset ? parsed : new DateTimeOffset(parsed.DateTime, BangkokOffset));
        }
        return text;
    }

    private static string ToIsoUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
