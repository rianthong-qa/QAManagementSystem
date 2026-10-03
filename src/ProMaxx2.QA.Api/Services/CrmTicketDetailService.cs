using System.Text.Json;

namespace ProMaxx2.QA.Api.Services;

public sealed record CrmTicketAnswerItem(
    string AnswerNo,
    string Description,
    string Posted,
    string? AnswerDate,
    string AnswerType,
    string? Image);

public sealed record CrmTicketDetailResult(
    CrmTicketListItem Ticket,
    string? Description,
    IReadOnlyList<CrmTicketAnswerItem> Answers,
    DateTimeOffset LastFetchedAt);

public sealed class CrmTicketNotFoundException(string message) : Exception(message);

public static class CrmTicketDetailParser
{
    public static CrmTicketDetailResult Parse(JsonElement raw, string requestedJobNo, string currentCrmUsername, IReadOnlyList<CrmTicketAnswerItem> answers, DateTimeOffset lastFetchedAt)
    {
        var ticket = CrmTicketListParser.MapTicket(raw);
        if (ticket is null || !string.Equals(ticket.JobNo, requestedJobNo.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !CrmTicketListParser.IsAssigneeInScope(ticket.Assignee, currentCrmUsername))
            throw new CrmTicketNotFoundException($"ไม่พบ Ticket {requestedJobNo} ในขอบเขตงานของผู้ใช้ปัจจุบัน");
        return new(ticket, CrmTicketListParser.ReadString(raw, "description", "Description", "detail", "Detail"), answers, lastFetchedAt);
    }
}

/// <summary>
/// Reads a single CRM ticket and its read-only answer history. Scope is checked against the
/// current user's CRM username before any detail is returned to the frontend.
/// </summary>
public sealed class CrmTicketDetailService(CrmApiClient crm, CrmConfigurationService configuration)
{
    public async Task<CrmTicketDetailResult> GetAsync(Guid userId, string jobNo, CancellationToken ct)
    {
        var requestedJobNo = jobNo.Trim();
        if (string.IsNullOrWhiteSpace(requestedJobNo)) throw new ArgumentException("ต้องระบุ Job No.");

        var (cfg, _) = await configuration.GetRuntimeAsync(userId, ct);
        var raw = await crm.GetJobDetailAsync(userId, requestedJobNo, "HD", ct);
        var detailWithoutAnswers = CrmTicketDetailParser.Parse(raw, requestedJobNo, cfg.Username, [], DateTimeOffset.UtcNow);

        var answers = await crm.GetHelpDeskAnswersAsync(userId, requestedJobNo, ct);
        var normalizedAnswers = answers
            // AnsDate จาก CRM เป็นเวลาไทยแบบไม่มี offset (หรือเลขล้วน yyyyMMddHHmmss) — normalize เป็น ISO UTC ชุดเดียวกับ Ticket
            .Select(x => new CrmTicketAnswerItem(x.AnswerNo, x.Description, x.Posted, CrmTicketListParser.NormalizeDate(x.AnsDate), x.FAnswerType, x.Image))
            .ToArray();

        return detailWithoutAnswers with { Answers = normalizedAnswers, LastFetchedAt = DateTimeOffset.UtcNow };
    }
}
