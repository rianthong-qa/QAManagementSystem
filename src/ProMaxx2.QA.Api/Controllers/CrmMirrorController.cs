using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Controllers;

public sealed record CrmMirrorRunDto(Guid CrmMirrorRunId, string Kind, DateOnly RangeFrom, DateOnly RangeTo, string Status, DateTime StartedAt, DateTime? CompletedAt, int TicketCount, int NewTickets, int ChangedTickets, int NewAnswers, string? Error);
public sealed record CrmMirrorStatusDto(bool Enabled, string AccountUsername, int Tickets, int Events, DateTime? LastSucceededAt, IReadOnlyList<CrmMirrorRunDto> Runs, int IntervalMinutes);

/// <summary>สถานะ/สั่งดึงสำเนา CRM Ticket ใน QA Hub (ใช้วัดผลงาน CRM) — เฉพาะ SYS_ADMIN</summary>
[ApiController, Route("api/v1/crm/mirror"), Authorize(Policy = "SysAdminOnly")]
public sealed class CrmMirrorController(CrmMirrorSyncService mirror, QaDbContext db) : ControllerBase
{
    [HttpGet("status")]
    public async Task<CrmMirrorStatusDto> Status(CancellationToken ct)
    {
        var runs = await db.CrmMirrorRuns.AsNoTracking().OrderByDescending(x => x.StartedAt).Take(10)
            .Select(x => new CrmMirrorRunDto(x.CrmMirrorRunId, x.Kind, x.RangeFrom, x.RangeTo, x.Status, x.StartedAt, x.CompletedAt, x.TicketCount, x.NewTickets, x.ChangedTickets, x.NewAnswers, x.Error))
            .ToListAsync(ct);
        var lastSucceeded = await db.CrmMirrorRuns.AsNoTracking().Where(x => x.Status == "Succeeded").MaxAsync(x => (DateTime?)x.StartedAt, ct);
        var interval = await db.CrmSyncSettings.AsNoTracking().Select(x => (int?)x.MirrorIntervalMinutes).SingleOrDefaultAsync(ct) ?? Domain.Integrations.CrmSyncSettings.DefaultMirrorIntervalMinutes;
        return new CrmMirrorStatusDto(mirror.Enabled, mirror.AccountUsername, await db.CrmTickets.CountAsync(ct), await db.CrmTicketEvents.CountAsync(ct), lastSucceeded, runs, interval);
    }

    [HttpPost("sync")]
    public async Task<ActionResult<CrmMirrorRunDto>> Sync([FromQuery] bool full = false, CancellationToken ct = default)
    {
        try
        {
            var r = await mirror.RunAsync(full, Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null, ct);
            return Ok(new CrmMirrorRunDto(r.CrmMirrorRunId, r.Kind, r.RangeFrom, r.RangeTo, r.Status, r.StartedAt, r.CompletedAt, r.TicketCount, r.NewTickets, r.ChangedTickets, r.NewAnswers, r.Error));
        }
        catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Title = "กำลังดึงข้อมูลอยู่", Detail = ex.Message, Status = 409 }); }
    }
}
