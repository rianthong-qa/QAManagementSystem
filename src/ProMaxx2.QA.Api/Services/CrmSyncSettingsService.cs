using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Integrations;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed record CrmSyncSettingsView(int PollIntervalMinutes, DateTimeOffset? UpdatedAt);

// System-wide poll interval for CrmSyncWorker (Phase 2) — admin-managed, separate from per-user CrmConfiguration
// (see CrmSyncSettings.cs for why). Single row, same shape as EmailConfigurationService minus the encrypted-secret bits.
public sealed class CrmSyncSettingsService(QaDbContext db)
{
    public async Task<CrmSyncSettingsView> GetViewAsync(CancellationToken ct)
    {
        var s = await db.CrmSyncSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        return s is null ? new(CrmSyncSettings.DefaultPollIntervalMinutes, null) : new(s.PollIntervalMinutes, s.UpdatedAt);
    }

    // เรียกทุก tick ของ CrmSyncWorker — อ่านค่าสดๆ จาก DB (ไม่ cache) เพื่อให้ admin เปลี่ยนค่าที่ Setting Center
    // แล้วมีผลตั้งแต่ tick ถัดไปเลย ไม่ต้อง restart API
    public async Task<TimeSpan> GetPollIntervalAsync(CancellationToken ct)
    {
        var minutes = await db.CrmSyncSettings.AsNoTracking().Select(x => (int?)x.PollIntervalMinutes).SingleOrDefaultAsync(ct) ?? CrmSyncSettings.DefaultPollIntervalMinutes;
        var clamped = Math.Clamp(minutes, CrmSyncSettings.MinPollIntervalMinutes, CrmSyncSettings.MaxPollIntervalMinutes);
        return TimeSpan.FromMinutes(clamped);
    }

    public async Task<CrmSyncSettingsView> SaveAsync(int pollIntervalMinutes, CancellationToken ct)
    {
        var s = await db.CrmSyncSettings.SingleOrDefaultAsync(ct);
        if (s is null) { s = new(pollIntervalMinutes); db.CrmSyncSettings.Add(s); }
        else s.Update(pollIntervalMinutes);
        await db.SaveChangesAsync(ct);
        return await GetViewAsync(ct);
    }
}
