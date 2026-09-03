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
        var rows = new List<AuditLogRow>();
        var defects = await db.DefectActivities.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(5000).Select(x => new { x.DefectActivityId, x.DefectId, x.ActionType, x.Message, x.ActorUserId, x.CreatedAt }).ToListAsync(ct);
        var regressions = await db.RegressionActivities.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(5000).Select(x => new { x.RegressionActivityId, x.ReleaseId, x.Action, x.Details, x.ActorUserId, x.CreatedAt }).ToListAsync(ct);
        rows.AddRange(defects.Select(x => new AuditLogRow(x.CreatedAt, x.ActorUserId, x.ActionType, "Defect", x.DefectId.ToString(), x.Message)));
        rows.AddRange(regressions.Select(x => new AuditLogRow(x.CreatedAt, x.ActorUserId, x.Action, "Regression", x.ReleaseId.ToString(), x.Details)));
        var query = rows.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => $"{x.Action} {x.Entity} {x.EntityId} {x.Summary}".Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(entity)) query = query.Where(x => x.Entity.Equals(entity.Trim(), StringComparison.OrdinalIgnoreCase));
        var ordered = query.OrderByDescending(x => x.Timestamp).ToList(); var total = ordered.Count;
        var items = ordered.Skip((page - 1) * size).Take(size).ToList();
        var userIds = items.Where(x => x.ActorUserId.HasValue).Select(x => x.ActorUserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, x => x.DisplayName, ct);
        return Ok(new AuditLogPage(items.Select(x => new AuditLogDto(x.Timestamp, x.ActorUserId, x.ActorUserId.HasValue && names.TryGetValue(x.ActorUserId.Value, out var name) ? name : null, x.Action, x.Entity, x.EntityId, x.Summary)).ToList(), total, page, size));
    }
    private sealed record AuditLogRow(DateTime Timestamp, Guid? ActorUserId, string Action, string Entity, string EntityId, string? Summary);
}
public sealed record AuditLogDto(DateTime Timestamp, Guid? ActorUserId, string? ActorName, string Action, string Entity, string EntityId, string? Summary);
public sealed record AuditLogPage(IReadOnlyList<AuditLogDto> Items, int Total, int Page, int Size);
