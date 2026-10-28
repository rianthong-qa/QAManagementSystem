using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.UnitTests;

public sealed class CrmTokenServiceCooldownTests
{
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData("net::ERR_CONNECTION_TIMED_OUT at https://bluesea.seniorsoft.com/bluesea/BookLicence/MA/Support", "ERR_CONNECTION_TIMED_OUT")]
    [InlineData("net::ERR_NAME_NOT_RESOLVED at https://blueid.seniorsoft.com/", "ERR_NAME_NOT_RESOLVED")]
    [InlineData("Timeout 15000ms exceeded waiting for locator('#EmpID #MerchantID')", null)]
    [InlineData(null, null)]
    public void NetworkErrorCode_detects_only_browser_network_failures(string? message, string? expected)
    {
        Assert.Equal(expected, CrmTokenService.NetworkErrorCode(message));
    }

    [Fact]
    public void Cooldown_blocks_new_logins_for_one_minute_with_unavailable_error()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 3, 2, 0, 0, TimeSpan.Zero)); // 09:00 น. เวลาไทย
        var service = new CrmTokenService(clock);
        service.ThrowIfInCooldown(); // ยังไม่เคยล้มเหลว — ไม่ throw

        service.MarkUnavailable();
        var ex = Assert.Throws<CrmIntegrationException>(service.ThrowIfInCooldown);
        Assert.True(ex.IsServiceUnavailable);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.RemoteStatusCode);
        Assert.Contains("09:01", ex.Message);

        clock.Now = clock.Now + CrmTokenService.UnavailableCooldown + TimeSpan.FromSeconds(1);
        Assert.Null(service.UnavailableUntil);
        service.ThrowIfInCooldown();
    }

    [Fact]
    public async Task GetTokenAsync_fails_fast_during_cooldown_without_opening_a_browser()
    {
        var service = new CrmTokenService(new ManualClock(DateTimeOffset.UtcNow));
        service.MarkUnavailable();
        var started = DateTime.UtcNow;
        var ex = await Assert.ThrowsAsync<CrmIntegrationException>(() => service.GetTokenAsync(Guid.NewGuid(), "1", "u", "p", CancellationToken.None));
        Assert.True(ex.IsServiceUnavailable);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Di_container_can_create_singleton_with_two_constructors()
    {
        // Program.cs ลงทะเบียน AddSingleton<CrmTokenService>() โดยไม่มี TimeProvider — ต้องเลือก constructor แบบไม่มีพารามิเตอร์ได้
        using var provider = new ServiceCollection().AddSingleton<CrmTokenService>().BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<CrmTokenService>());
    }

    [Fact]
    public async Task Login_failure_pauses_only_that_user_until_cooldown_ends_or_account_is_saved()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 3, 2, 0, 0, TimeSpan.Zero)); // 09:00 น.
        var service = new CrmTokenService(clock);
        var failedUser = Guid.NewGuid();
        service.MarkLoginFailed(failedUser);

        var ex = Assert.Throws<CrmIntegrationException>(() => service.ThrowIfLoginCoolingDown(failedUser));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.RemoteStatusCode); // map เป็น CRM_UNAUTHORIZED ให้ผู้ใช้ตรวจบัญชี
        Assert.Contains("09:05", ex.Message);
        service.ThrowIfLoginCoolingDown(Guid.NewGuid()); // ผู้ใช้อื่นไม่ได้รับผลกระทบ

        // GetTokenAsync ตอบทันทีโดยไม่เปิดเบราว์เซอร์
        await Assert.ThrowsAsync<CrmIntegrationException>(() => service.GetTokenAsync(failedUser, "1", "u", "p", CancellationToken.None));

        service.ResetLoginFailure(failedUser); // บันทึกบัญชี CRM ใหม่
        service.ThrowIfLoginCoolingDown(failedUser);

        service.MarkLoginFailed(failedUser);
        clock.Now += CrmTokenService.LoginFailureCooldown + TimeSpan.FromSeconds(1);
        service.ThrowIfLoginCoolingDown(failedUser);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public void Transient_errors_are_unavailable_or_timeout_only(HttpStatusCode status, bool transient)
    {
        Assert.Equal(transient, new CrmIntegrationException("x", status).IsTransient);
        Assert.False(new CrmIntegrationException("x").IsTransient);
    }

    [Fact]
    public void Resource_limits_stay_conservative()
    {
        Assert.Equal(2, CrmTokenService.MaxConcurrentBrowserLogins);
        Assert.Equal(TimeSpan.FromSeconds(30), CrmApiClient.RequestTimeout);
        Assert.Equal(366, CrmApiClient.MaxTicketListRangeDays);
    }
}
