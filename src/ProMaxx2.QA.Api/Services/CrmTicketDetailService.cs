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
    public static CrmTicketDetailResult Parse(
        JsonElement raw,
        string requestedJobNo,
        string currentCrmUsername,
        IReadOnlyList<CrmTicketAnswerItem> answers,
        DateTimeOffset lastFetchedAt,
        bool wasPreviouslyAssigned = false)
    {
        var ticket = CrmTicketListParser.MapTicket(raw);
        if (ticket is null || !string.Equals(ticket.JobNo, requestedJobNo.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !CrmTicketListParser.IsTicketInScope(ticket, currentCrmUsername, wasPreviouslyAssigned))
            throw new CrmTicketNotFoundException($"ไม่พบ Ticket {requestedJobNo} ในขอบเขตงานของผู้ใช้ปัจจุบัน");
        return new(ticket, CrmTicketListParser.ReadString(raw, "description", "Description", "detail", "Detail"), answers, lastFetchedAt);
    }
}

/// <summary>
/// Reads a single CRM ticket and its read-only answer history. Scope is checked against the
/// current user's CRM username before any detail is returned to the frontend. An active ticket
/// the user previously held (Flow Tracking history or an answer they posted) is also in scope.
/// </summary>
public sealed class CrmTicketDetailService(CrmApiClient crm, CrmConfigurationService configuration, CrmFlowTrackingService flowTracking)
{
    public async Task<CrmTicketDetailResult> GetAsync(Guid userId, string jobNo, CancellationToken ct)
    {
        var requestedJobNo = jobNo.Trim();
        if (string.IsNullOrWhiteSpace(requestedJobNo)) throw new ArgumentException("ต้องระบุ Job No.");

        var (cfg, _) = await configuration.GetRuntimeAsync(userId, ct);
        JsonElement raw;
        try
        {
            raw = await crm.FindJobDetailAsync(userId, requestedJobNo, "HD", ct)
                ?? throw new CrmTicketNotFoundException($"ไม่พบ Ticket {requestedJobNo} ในขอบเขตงานของผู้ใช้ปัจจุบัน");
        }
        catch (CrmIntegrationException ex) when (ex.RemoteStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new CrmTicketNotFoundException($"ไม่พบ Ticket {requestedJobNo} ในขอบเขตงานของผู้ใช้ปัจจุบัน");
        }
        // Previous-assignment checks only run for active tickets outside the current route,
        // cheapest source first; answers fetched here are reused for the response.
        IReadOnlyList<CrmHelpDeskAnswer>? answers = null;
        var wasPreviouslyAssigned = false;
        var ticket = CrmTicketListParser.MapTicket(raw);
        if (ticket is not null && !CrmTicketListParser.IsClosed(ticket.Status) && !CrmTicketListParser.IsTicketInScope(ticket, cfg.Username))
        {
            wasPreviouslyAssigned = await flowTracking.WasPreviouslyAssignedToAsync(ticket.JobNo, cfg.Username, ct);
            if (!wasPreviouslyAssigned)
            {
                answers = await crm.GetHelpDeskAnswersAsync(userId, requestedJobNo, ct);
                wasPreviouslyAssigned = answers.Any(x => CrmTicketListParser.IsAssigneeInScope(x.Posted, cfg.Username));
            }
        }
        var detailWithoutAnswers = CrmTicketDetailParser.Parse(raw, requestedJobNo, cfg.Username, [], DateTimeOffset.UtcNow, wasPreviouslyAssigned);

        answers ??= await crm.GetHelpDeskAnswersAsync(userId, requestedJobNo, ct);
        var normalizedAnswers = answers
            // AnsDate จาก CRM เป็นเวลาไทยแบบไม่มี offset (หรือเลขล้วน yyyyMMddHHmmss) — normalize เป็น ISO UTC ชุดเดียวกับ Ticket
            .Select(x => new CrmTicketAnswerItem(x.AnswerNo, x.Description, x.Posted, CrmTicketListParser.NormalizeDate(x.AnsDate), x.FAnswerType, x.Image))
            .ToArray();

        return detailWithoutAnswers with { Answers = normalizedAnswers, LastFetchedAt = DateTimeOffset.UtcNow };
    }
}
