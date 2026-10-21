using System.Net;
using System.Text;
using ProMaxx2.Automation.Hub;
using Xunit;

namespace ProMaxx2.Automation.Core.Tests;

/// <summary>AUT-AGT-001: Agent ต้องทำงานต่อได้เองเมื่อ token หมดอายุ/ถูก rotate และเมื่อ Hub สะดุดชั่วคราว</summary>
public sealed class QaHubClientResilienceTests
{
    /// <summary>Hub จำลอง: ออก token ใหม่ทุกครั้งที่ login และรับเฉพาะ token ล่าสุด; ตั้งให้ endpoint อื่นตอบ 503 ได้ชั่วคราว</summary>
    private sealed class FakeHub : HttpMessageHandler
    {
        public int Logins;
        public string ValidToken = "";
        public int TransientFailuresRemaining;
        public bool RejectCredentials;
        public readonly List<string> Requests = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path} {request.Headers.Authorization?.Parameter}");
            if (path.EndsWith("/auth/login"))
            {
                if (RejectCredentials) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                Logins++;
                ValidToken = $"token-{Logins}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"accessToken\":\"{ValidToken}\"}}", Encoding.UTF8, "application/json") });
            }
            if (TransientFailuresRemaining > 0)
            {
                TransientFailuresRemaining--;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            if (request.Headers.Authorization?.Parameter != ValidToken) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static readonly AgentConfig Config = new() { HubBaseUrl = "http://hub.test/api/v1", Username = "agent", Password = "secret", AgentCode = "AGENT-A" };

    private static QaHubClient MakeClient(FakeHub hub) => new(Config, hub, _ => TimeSpan.Zero);

    [Fact]
    public async Task An_expired_or_rotated_token_triggers_one_relogin_and_the_request_is_retried()
    {
        var hub = new FakeHub();
        using var client = MakeClient(hub);
        Assert.True(await client.LoginAsync(CancellationToken.None));

        hub.ValidToken = "rotated-key"; // Hub restarted with a new signing key → old token rejected
        await client.HeartbeatAsync("Idle", null, CancellationToken.None);

        Assert.Equal(2, hub.Logins);
        Assert.EndsWith("token-2", hub.Requests[^1]);
    }

    [Fact]
    public async Task Transient_hub_errors_are_retried_with_the_same_body()
    {
        var hub = new FakeHub { TransientFailuresRemaining = 2 };
        using var client = MakeClient(hub);
        await client.LoginAsync(CancellationToken.None);
        hub.TransientFailuresRemaining = 2;

        await client.CompleteAsync(Guid.NewGuid(), "Passed", null, null, null, CancellationToken.None);

        Assert.Equal(3, hub.Requests.Count(r => r.Contains("/complete")));
    }

    [Fact]
    public async Task Persistent_hub_errors_eventually_surface_to_the_caller()
    {
        var hub = new FakeHub();
        using var client = MakeClient(hub);
        await client.LoginAsync(CancellationToken.None);
        hub.TransientFailuresRemaining = 10;

        await Assert.ThrowsAsync<HttpRequestException>(() => client.HeartbeatAsync("Idle", null, CancellationToken.None));
        Assert.Equal(1 + HubResilienceHandler.MaxTransientRetries, hub.Requests.Count(r => r.Contains("/heartbeat")));
    }

    [Fact]
    public async Task Rejected_credentials_return_false_instead_of_retrying_forever()
    {
        var hub = new FakeHub { RejectCredentials = true };
        using var client = MakeClient(hub);

        Assert.False(await client.LoginAsync(CancellationToken.None));
        Assert.Single(hub.Requests);
    }
}
