using System.Net;
using System.Net.Http.Headers;

namespace ProMaxx2.Automation.Hub;

/// <summary>AUT-AGT-001: ทำให้ Agent ทำงานต่อได้เองเมื่อ token หมดอายุ (JWT 24 ชม.) หรือ Hub เปลี่ยน signing key และเมื่อเครือข่าย
/// สะดุดชั่วคราว — เดิม Runner login ครั้งเดียวตอนเริ่ม พอ token หมดอายุ heartbeat จะล้มวนไปเรื่อย ๆ ไม่รับงานอีกเลย
/// และ execution ที่กำลังรันจะค้าง Running บน Hub.
/// <list type="bullet">
/// <item>401 จาก endpoint อื่นที่ไม่ใช่ /auth/login → login ใหม่ (ครั้งเดียวต่อ request) แล้วส่งซ้ำด้วย token ใหม่</item>
/// <item>network error / timeout / 408 / 429 / 502 / 503 / 504 → ส่งซ้ำสูงสุด <see cref="MaxTransientRetries"/> ครั้ง
/// แบบ backoff 1, 2, 4 วินาที (complete execution ฝั่ง Hub เป็น idempotent จึงส่งซ้ำได้ปลอดภัย)</item>
/// </list></summary>
public sealed class HubResilienceHandler(Func<CancellationToken, Task<string?>> relogin, Func<string?> currentToken, HttpMessageHandler? inner = null)
    : DelegatingHandler(inner ?? new HttpClientHandler())
{
    public const int MaxTransientRetries = 3;
    private readonly SemaphoreSlim _loginLock = new(1, 1);

    /// <summary>ใช้ใน test เพื่อไม่ต้องรอ backoff จริง</summary>
    public Func<int, TimeSpan> Backoff { get; init; } = attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var isLogin = request.RequestUri?.AbsolutePath.EndsWith("/auth/login", StringComparison.OrdinalIgnoreCase) == true;
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct);
        var reloggedIn = false;
        for (var attempt = 1; ; attempt++)
        {
            using var attemptRequest = Clone(request, body, isLogin ? null : currentToken());
            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(attemptRequest, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                if (attempt > MaxTransientRetries) throw;
                await Task.Delay(Backoff(attempt), ct);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && !isLogin && !reloggedIn)
            {
                response.Dispose();
                reloggedIn = true;
                await ReloginAsync(ct);
                attempt--; // การ login ใหม่ไม่นับเป็น transient retry
                continue;
            }
            if (IsTransient(response.StatusCode) && attempt <= MaxTransientRetries)
            {
                response.Dispose();
                await Task.Delay(Backoff(attempt), ct);
                continue;
            }
            return response;
        }
    }

    private async Task ReloginAsync(CancellationToken ct)
    {
        var before = currentToken();
        await _loginLock.WaitAsync(ct);
        try
        {
            // request อื่นอาจ login ใหม่ไปแล้วระหว่างรอ lock — ใช้ token นั้นเลยไม่ต้อง login ซ้ำ
            if (currentToken() == before) await relogin(ct);
        }
        finally { _loginLock.Release(); }
    }

    private static bool IsTransient(HttpStatusCode code) => code is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static HttpRequestMessage Clone(HttpRequestMessage source, byte[]? body, string? token)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri) { Version = source.Version };
        foreach (var header in source.Headers)
            if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (!string.IsNullOrWhiteSpace(token)) clone.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null && source.Content is not null)
        {
            clone.Content = new ByteArrayContent(body);
            foreach (var header in source.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _loginLock.Dispose();
        base.Dispose(disposing);
    }
}
