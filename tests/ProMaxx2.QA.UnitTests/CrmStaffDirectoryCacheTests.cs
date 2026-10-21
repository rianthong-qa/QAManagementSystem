using Microsoft.Extensions.DependencyInjection;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.UnitTests;

public sealed class CrmStaffDirectoryCacheTests
{
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Loads_once_per_hour_dedupes_codes_and_drops_email()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var cache = new CrmStaffDirectoryCache(clock);
        var loads = 0;
        Task<IReadOnlyList<BlueIdUserDto>> Load(CancellationToken _)
        {
            loads++;
            return Task.FromResult<IReadOnlyList<BlueIdUserDto>>([
                new(" 6101 ", " เหรียญทอง เจือบุญ ", "a@x.com"), new("6101", "ซ้ำ", null), new("", "ไม่มีรหัส", null), new("4926", "สมชาย ใจดี", null)]);
        }

        var first = await cache.GetAsync(Load, CancellationToken.None);
        await cache.GetAsync(Load, CancellationToken.None);
        Assert.Equal(1, loads);
        Assert.Equal(new[] { new CrmStaffDto("4926", "สมชาย ใจดี"), new CrmStaffDto("6101", "เหรียญทอง เจือบุญ") }, first);

        clock.Now += CrmStaffDirectoryCache.Lifetime + TimeSpan.FromSeconds(1);
        await cache.GetAsync(Load, CancellationToken.None);
        Assert.Equal(2, loads);
    }

    [Fact]
    public void Di_container_can_create_singleton()
    {
        using var provider = new ServiceCollection().AddSingleton<CrmStaffDirectoryCache>().BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<CrmStaffDirectoryCache>());
    }
}
