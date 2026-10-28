using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Domain.Integrations;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>รอบดึงข้อมูล CRM (CrmMirrorWorker) ตั้งเองได้ — ค่าเริ่มต้น 15 นาที, ช่วง 5–1440 นาที, บันทึกแยกจากรอบ Poll ได้</summary>
public sealed class CrmMirrorIntervalTests
{
    [Fact]
    public async Task Defaults_to_15_minutes_when_no_settings_row()
    {
        await using var db = CreateDb();
        var service = new CrmSyncSettingsService(db);

        Assert.Equal(TimeSpan.FromMinutes(15), await service.GetMirrorIntervalAsync(default));
        Assert.Equal(15, (await service.GetViewAsync(default)).MirrorIntervalMinutes);
    }

    [Fact]
    public async Task Saving_mirror_interval_keeps_poll_interval()
    {
        await using var db = CreateDb();
        var service = new CrmSyncSettingsService(db);
        await service.SaveAsync(7, null, default);

        var view = await service.SaveAsync(null, 60, default);

        Assert.Equal(7, view.PollIntervalMinutes);
        Assert.Equal(60, view.MirrorIntervalMinutes);
        Assert.Equal(TimeSpan.FromMinutes(60), await service.GetMirrorIntervalAsync(default));
    }

    [Fact]
    public async Task Saving_poll_interval_only_keeps_mirror_default()
    {
        await using var db = CreateDb();
        var view = await new CrmSyncSettingsService(db).SaveAsync(3, null, default);

        Assert.Equal(3, view.PollIntervalMinutes);
        Assert.Equal(CrmSyncSettings.DefaultMirrorIntervalMinutes, view.MirrorIntervalMinutes);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1441)]
    public void Rejects_mirror_interval_out_of_range(int minutes)
    {
        var settings = new CrmSyncSettings(2);
        Assert.Throws<ArgumentException>(() => settings.UpdateMirrorInterval(minutes));
    }

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
