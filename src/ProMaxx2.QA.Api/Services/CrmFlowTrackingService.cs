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
        $"Support {state.Support ?? "-"} → QA {state.Qa ?? "-"} → Dev {state.Developer ?? "รอส่งต่อ"} · สถานะ {state.Status ?? "ไม่ระบุ"}";

    private static string NormalizeJobNo(string value) => value.Trim().ToUpperInvariant();
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeDeveloper(string? value) => string.Equals(value?.Trim(), "0", StringComparison.OrdinalIgnoreCase) ? null : Normalize(value);
}
