using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1/audit-logs"), Authorize(Policy = "AuditView")]
public sealed class AuditLogsController(QaDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AuditLogPage>> List([FromQuery] string? search, [FromQuery] string? entity, [FromQuery] int page = 1, [FromQuery] int size = 25, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, 10000); size = Math.Clamp(size, 1, 100);
        // Keep all three sources queryable without materializing their history in the API process.
        // Project into an anonymous type here, not the AuditLogRow record below — EF Core cannot
        // translate a call into a named type's constructor when the query is combined with Concat,
        // and silently falls back to evaluating that projection client-side; once one side of a
        // Concat needs client evaluation, the whole set operation fails at runtime with
        // "Unable to translate set operation after client projection has been applied." Anonymous
        // types don't have this limitation, so materialize into AuditLogRow only after the DB round-trip.
        var query = db.DefectActivities.AsNoTracking()
            .Select(x => new { Timestamp = x.CreatedAt, x.ActorUserId, Action = x.ActionType, Entity = "Defect", EntityId = x.DefectId.ToString(), Summary = (string?)x.Message, Source = 0, SourceId = x.DefectActivityId })
            .Concat(db.RegressionActivities.AsNoTracking()
                .Select(x => new { Timestamp = x.CreatedAt, x.ActorUserId, x.Action, Entity = "Regression", EntityId = x.ReleaseId.ToString(), Summary = x.Details, Source = 1, SourceId = x.RegressionActivityId }))
            .Concat(db.AuditLogs.AsNoTracking()
                .Select(x => new { Timestamp = x.CreatedAt, ActorUserId = x.UserId, x.Action, Entity = x.EntityType, x.EntityId, Summary = x.ChangeSummary, Source = 2, SourceId = x.AuditLogId }));

        if (!string.IsNullOrWhiteSpace(entity))
        {
            var entityFilter = entity.Trim();
            query = query.Where(x => x.Entity == entityFilter);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(x => EF.Functions.Like(x.Action, pattern, "\\")
                || EF.Functions.Like(x.Entity, pattern, "\\")
                || EF.Functions.Like(x.EntityId, pattern, "\\")
                || (x.Summary != null && EF.Functions.Like(x.Summary, pattern, "\\")));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Timestamp)
            .ThenBy(x => x.Source).ThenByDescending(x => x.SourceId)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var userIds = items.Where(x => x.ActorUserId.HasValue).Select(x => x.ActorUserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, x => x.DisplayName, ct);
        return Ok(new AuditLogPage(items.Select(x => new AuditLogDto(x.Timestamp, x.ActorUserId, x.ActorUserId.HasValue && names.TryGetValue(x.ActorUserId.Value, out var name) ? name : null, x.Action, x.Entity, x.EntityId, x.Summary)).ToList(), total, page, size));
    }
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "[[]");
}
public sealed record AuditLogDto(DateTime Timestamp, Guid? ActorUserId, string? ActorName, string Action, string Entity, string EntityId, string? Summary);
public sealed record AuditLogPage(IReadOnlyList<AuditLogDto> Items, int Total, int Page, int Size);
