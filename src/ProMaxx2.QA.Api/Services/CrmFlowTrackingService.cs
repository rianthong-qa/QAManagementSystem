using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed record CrmFlowState(
    string JobNo,
    string? Status,
    string? Support,
    string? Qa,
    string? Developer);

public sealed record CrmFlowHistoryItem(
    DateTime Timestamp,
    string Action,
    CrmFlowState? Before,
    CrmFlowState After,
    Guid? ActorUserId,
    string? ActorName);

/// <summary>
/// Stores a deduplicated CRM flow snapshot in the existing audit log table. CRM is the
/// source of truth for the current ticket, while these snapshots preserve the route that
/// the ticket took through Support, QA, and Development in QA Hub.
/// </summary>
public sealed class CrmFlowTrackingService(QaDbContext db)
{
    private const string EntityType = "CrmTicketFlow";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RecordSnapshotsAsync(Guid userId, IEnumerable<CrmTicketListItem> tickets, CancellationToken ct)
    {
        var rows = tickets
            .Where(x => !string.IsNullOrWhiteSpace(x.JobNo))
            .GroupBy(x => NormalizeJobNo(x.JobNo), StringComparer.Ordinal)
            .Select(x => x.Last())
            .ToArray();
        if (rows.Length == 0) return;

        var jobNos = rows.Select(x => NormalizeJobNo(x.JobNo)).ToArray();
        var latest = await db.AuditLogs.AsNoTracking()
            .Where(x => x.EntityType == EntityType && jobNos.Contains(x.EntityId))
            .GroupBy(x => x.EntityId)
            .Select(x => x.OrderByDescending(y => y.CreatedAt).ThenByDescending(y => y.AuditLogId).First())
            .ToDictionaryAsync(x => x.EntityId, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var row in rows)
        {
            var after = ToState(row);
            latest.TryGetValue(NormalizeJobNo(row.JobNo), out var previousLog);
            var before = previousLog is null ? null : Deserialize(previousLog.AfterJson);
            if (before is not null && StatesEqual(before, after)) continue;

            var action = before is null ? "FlowStarted" : "FlowChanged";
            db.AuditLogs.Add(new AuditLog(
                userId,
                action,
                EntityType,
                after.JobNo,
                Summary(after),
                before is null ? null : JsonSerializer.Serialize(before, JsonOptions),
                JsonSerializer.Serialize(after, JsonOptions),
                null));
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CrmFlowHistoryItem>> GetHistoryAsync(string jobNo, CancellationToken ct)
    {
        var normalizedJobNo = NormalizeJobNo(jobNo);
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(x => x.EntityType == EntityType && x.EntityId == normalizedJobNo)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.AuditLogId)
            .ToListAsync(ct);
        if (rows.Count == 0) return [];

        var userIds = rows.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).Distinct().ToArray();
        var names = await db.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .ToDictionaryAsync(x => x.UserId, x => x.DisplayName, ct);

        return rows.Select(row => new CrmFlowHistoryItem(
            row.CreatedAt,
            row.Action,
            Deserialize(row.BeforeJson),
            Deserialize(row.AfterJson) ?? new CrmFlowState(normalizedJobNo, null, null, null, null),
            row.UserId,
            row.UserId.HasValue && names.TryGetValue(row.UserId.Value, out var name) ? name : null)).ToArray();
    }

    /// <summary>
    /// Job numbers whose recorded route ever had the given CRM user as Assignto. Used to keep
    /// tickets visible after the user hands them back (e.g. QA → Support).
    /// </summary>
    public async Task<IReadOnlySet<string>> GetJobsPreviouslyAssignedToAsync(string? crmUsername, CancellationToken ct)
    {
        var code = crmUsername?.Trim();
        if (string.IsNullOrWhiteSpace(code)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The LIKE pre-filter keeps the scan small; the exact code match below rejects partial codes (6101 vs 61010).
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(x => x.EntityType == EntityType && x.AfterJson != null && x.AfterJson.Contains(code))
            .Select(x => new { x.EntityId, x.AfterJson })
            .ToListAsync(ct);
        return rows
            .Where(x => CrmTicketListParser.IsAssigneeInScope(Deserialize(x.AfterJson)?.Qa, code))
            .Select(x => x.EntityId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> WasPreviouslyAssignedToAsync(string jobNo, string? crmUsername, CancellationToken ct)
    {
        var code = crmUsername?.Trim();
        if (string.IsNullOrWhiteSpace(code)) return false;
        var normalizedJobNo = NormalizeJobNo(jobNo);
        var snapshots = await db.AuditLogs.AsNoTracking()
            .Where(x => x.EntityType == EntityType && x.EntityId == normalizedJobNo && x.AfterJson != null && x.AfterJson.Contains(code))
            .Select(x => x.AfterJson)
            .ToListAsync(ct);
        return snapshots.Any(x => CrmTicketListParser.IsAssigneeInScope(Deserialize(x)?.Qa, code));
    }

    /// <summary>
    /// The most recent QA per job: an Assignto that was neither the Support owner nor the job's
    /// Developer. CRM keeps a single Assignto, so once QA hands the ticket to Dev or back to
    /// Support the QA stage can only come from history. The Developer is taken from the newest
    /// snapshot that has one, because Assignto often moves to Dev before sysDevelop is filled in.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetLastQaAsync(IEnumerable<string> jobNos, CancellationToken ct)
    {
        var keys = jobNos.Where(x => !string.IsNullOrWhiteSpace(x)).Select(NormalizeJobNo).Distinct().ToArray();
        if (keys.Length == 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(x => x.EntityType == EntityType && keys.Contains(x.EntityId))
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.AuditLogId)
            .Select(x => new { x.EntityId, x.AfterJson })
            .ToListAsync(ct);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in rows.GroupBy(x => x.EntityId, StringComparer.OrdinalIgnoreCase))
        {
            var states = job.Select(x => Deserialize(x.AfterJson)).OfType<CrmFlowState>().ToArray(); // newest first
            var developer = states.Select(x => x.Developer).FirstOrDefault(x => x is not null);
            var qa = states.FirstOrDefault(x => x.Qa is not null && !SameStaff(x.Qa, x.Support) && !SameStaff(x.Qa, developer))?.Qa;
            if (qa is not null) result[job.Key] = qa;
        }
        return result;
    }

    // CRM sends staff either as a bare code ("6101") or "ชื่อ นามสกุล (6101)".
    private static bool SameStaff(string? left, string? right)
    {
        static string? Code(string? value)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(text, @"\((\d+)\)$");
            return match.Success ? match.Groups[1].Value : text;
        }
        var a = Code(left);
        return a is not null && string.Equals(a, Code(right), StringComparison.OrdinalIgnoreCase);
    }

    private static CrmFlowState ToState(CrmTicketListItem item) => new(
        NormalizeJobNo(item.JobNo),
        Normalize(item.Status),
        Normalize(item.Owner),
        Normalize(item.Assignee),
        NormalizeDeveloper(item.Developer));

    private static CrmFlowState? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<CrmFlowState>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static bool StatesEqual(CrmFlowState left, CrmFlowState right) =>
        string.Equals(left.Status, right.Status, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Support, right.Support, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Qa, right.Qa, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Developer, right.Developer, StringComparison.OrdinalIgnoreCase);

    private static string Summary(CrmFlowState state) =>
        // CRM field names, not job roles: the person in Development may be QA, the owner may not be Support.
        $"เจ้าของเรื่อง {state.Support ?? "-"} → Assign To {state.Qa ?? "-"} → Development {state.Developer ?? "ไม่ระบุ"} · สถานะ {state.Status ?? "ไม่ระบุ"}";

    private static string NormalizeJobNo(string value) => value.Trim().ToUpperInvariant();
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeDeveloper(string? value) => string.Equals(value?.Trim(), "0", StringComparison.OrdinalIgnoreCase) ? null : Normalize(value);
}
