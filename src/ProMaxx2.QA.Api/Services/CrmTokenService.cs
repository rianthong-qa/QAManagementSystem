using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace ProMaxx2.QA.Api.Services;

public sealed class CrmIntegrationException(string message) : Exception(message);
public sealed record CrmToken(string AccessToken, string? BranchId);

// Caches the Bearer token used for the CRM (BlueSea Helpdesk) integration — PER QA HUB USER now (each user logs
// in to CRM with their own BlueID Employee account; see CrmConfiguration.cs). Registered as a singleton (see
// Program.cs) — it must outlive per-request scoped DI to actually cache tokens across requests. Credentials live
// in the DB (via CrmConfigurationService, scoped) — this service stays credential-agnostic and is handed
// merchantId/username/password on every call, caching per userId.
//
// IMPORTANT: BlueID does NOT expose a clean server-to-server OAuth2 grant for this login (confirmed by testing —
// grant_type=password against client_id "BlueSea" returns invalid_client: "Client authentication is required
// for this application", and no client_secret was ever provisioned for it). The only proven-working approach —
// taken directly from this org's own existing tool that already does this in production (H:\APP\ExportReport\exporter.py,
// using Python + Playwright) — is to drive a real headless browser through BlueID's actual Employee login form
// (MerchantID + Username + Password, same 3 fields as the login page itself) and capture the Bearer token BlueSea's
// own SPA ends up with after a normal Authorization Code + PKCE login, then reuse that token for plain HTTP calls
// (see CrmApiClient). This mirrors exporter.py's auto_login_if_needed()/capture_bearer_token() functions.
public sealed class CrmTokenService
{
    // Any page under bluesea.seniorsoft.com/bluesea/ redirects to BlueID login when unauthenticated — the
    // Support list page is a convenient one since it also immediately fires an authenticated API request on
    // load (GET .../booklicenceapi/Support), which is how the Bearer token gets captured.
    private const string LoginTriggerUrl = "https://bluesea.seniorsoft.com/bluesea/BookLicence/MA/Support";
    private const string EmployeeTabSelector = "#profile-tab";
    // สำคัญ: หน้า login มีฟอร์ม "Owner" กับ "Employee" อยู่ใน DOM เดียวกันพร้อมกัน (สลับด้วย CSS tab, ไม่ได้ลบออก
    // จาก DOM) และทั้งสองฟอร์มมี id ซ้ำกันหมด (#MerchantID/#Username/#Password/button[value='login']) — ฝั่ง
    // Owner เป็น hidden input ที่ Playwright จะ resolve เจอก่อนถ้าไม่เจาะจง ต้อง scope ทุก selector ไว้ใต้
    // #EmpID (container ของแท็บ Employee) เท่านั้น
    private const string EmployeeFormScope = "#EmpID";
    private const string MerchantIdSelector = $"{EmployeeFormScope} #MerchantID";
    private const string UsernameSelector = $"{EmployeeFormScope} #Username";
    private const string PasswordSelector = $"{EmployeeFormScope} #Password";
    private const string LoginButtonSelector = $"{EmployeeFormScope} button[value='login']";
    private const string ApiRequestUrlMarker = "booklicenceapi/Support";

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt, string? BranchId);

    // Keyed by QA Hub UserId — each user's own login/token, isolated from everyone else's. Locks are per-user too
    // so one user's (slow, ~seconds-long) Playwright login never blocks a concurrent request from another user.
    private readonly ConcurrentDictionary<Guid, CachedToken> cache = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> locks = new();

    public async Task<CrmToken> GetTokenAsync(Guid userId, string merchantId, string username, string password, CancellationToken ct)
    {
        if (cache.TryGetValue(userId, out var hit) && DateTimeOffset.UtcNow < hit.ExpiresAt.AddMinutes(-5))
            return new(hit.AccessToken, hit.BranchId);
        var gate = locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(userId, out hit) && DateTimeOffset.UtcNow < hit.ExpiresAt.AddMinutes(-5))
                return new(hit.AccessToken, hit.BranchId); // re-check: another caller may have refreshed while we waited for the lock
            var token = await LoginViaBrowserAsync(merchantId, username, password, ct);
            var claims = DecodeJwtClaims(token);
            var expiresAt = claims.Expiry ?? DateTimeOffset.UtcNow.AddHours(24); // fall back to the ~24h lifetime confirmed earlier if the exp claim can't be read
            cache[userId] = new CachedToken(token, expiresAt, claims.BranchId);
            return new(token, claims.BranchId);
        }
        finally { gate.Release(); }
    }

    // Called after a credential change (CrmConfigurationService.SaveAsync) or a downstream 401 for that specific
    // user, so their next call re-authenticates immediately instead of retrying with a stale token.
    public void Invalidate(Guid userId) => cache.TryRemove(userId, out _);

    private static async Task<string> LoginViaBrowserAsync(string merchantId, string username, string password, CancellationToken ct)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
        var page = await context.NewPageAsync();

        string? capturedToken = null;
        page.Request += (_, req) =>
        {
            if (capturedToken is not null || !req.Url.Contains(ApiRequestUrlMarker, StringComparison.OrdinalIgnoreCase)) return;
            if (req.Headers.TryGetValue("authorization", out var auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                capturedToken = auth["Bearer ".Length..].Trim();
        };

        try
        {
            await page.GotoAsync(LoginTriggerUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            await TryDismissCookieBannerAsync(page);

            // ต้องคลิกแท็บ Employee ก่อนถึงจะเห็นฟอร์ม MerchantID/Username/Password (เหมือนหน้า login จริงของ BlueID)
            var employeeTab = page.Locator(EmployeeTabSelector);
            if (await employeeTab.CountAsync() > 0) await employeeTab.First.ClickAsync(new LocatorClickOptions { Timeout = 15_000 });

            await page.FillAsync(MerchantIdSelector, merchantId, new PageFillOptions { Timeout = 15_000 });
            await page.FillAsync(UsernameSelector, username, new PageFillOptions { Timeout = 15_000 });
            await page.FillAsync(PasswordSelector, password, new PageFillOptions { Timeout = 15_000 });
            await page.ClickAsync(LoginButtonSelector, new PageClickOptions { Timeout = 15_000 });

            try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 60_000 }); }
            catch (TimeoutException) { /* the Support list page keeps polling in the background — network never goes fully idle; not fatal */ }

            // ถ้ายังไม่จับ token ได้ (เช่น request ที่ต้องการยิงไปก่อนที่เราจะติด listener ทัน) ลอง reload อีกครั้ง
            // ด้วย session ที่ login แล้ว — รอบนี้ควรมี request ที่มี Authorization header แน่นอน
            string? firstAttemptScreenshot = null;
            if (capturedToken is null)
            {
                // เก็บ screenshot ของสถานะหลัง submit ครั้งแรกไว้ก่อน reload จะทับ (reload รีเซ็ตหน้ากลับไปที่แท็บ
                // Owner เริ่มต้นเสมอ ไม่ว่า submit ครั้งแรกจะเกิดอะไรขึ้นก็ตาม) — ไม่งั้น screenshot สุดท้ายจะไม่มี
                // ประโยชน์เลย เพราะเห็นแค่หน้า login สดใหม่ ไม่เห็นว่า submit ครั้งแรกจริงๆ ไปติดตรงไหน
                firstAttemptScreenshot = await TrySaveDiagnosticScreenshotAsync(page, "after-first-submit");
                await page.GotoAsync(LoginTriggerUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
                await TryDismissCookieBannerAsync(page);
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (capturedToken is null && DateTime.UtcNow < deadline) await Task.Delay(500, ct);
            }

            if (capturedToken is null)
            {
                // ไม่รู้ว่าหลัง submit ฟอร์มแล้วจริงๆ เบราว์เซอร์ไปโผล่หน้าไหน (login fail แบบเงียบๆ,
                // ติด MFA/challenge page ที่ selector ไม่รู้จัก, หรืออื่นๆ) — แนบ URL ปัจจุบัน + title ของหน้า
                // และ screenshot ไปกับ error เพื่อวินิจฉัยได้โดยไม่ต้อง reproduce ซ้ำ
                var currentUrl = page.Url;
                string pageTitle;
                try { pageTitle = await page.TitleAsync(); } catch { pageTitle = "(อ่าน title ไม่ได้)"; }
                var screenshotPath = await TrySaveDiagnosticScreenshotAsync(page, "after-reload");
                var screenshotNote = string.Join(" | ", new[]
                {
                    firstAttemptScreenshot is null ? null : $"screenshot (หลัง submit ครั้งแรก): {firstAttemptScreenshot}",
                    screenshotPath is null ? null : $"screenshot (หลัง reload): {screenshotPath}",
                }.Where(x => x is not null));
                throw new CrmIntegrationException($"Login เข้า BlueID สำเร็จ แต่จับ Bearer token จาก network request ไม่ได้ — หน้าเว็บ CRM อาจมีการเปลี่ยนแปลง (selector/URL ไม่ตรงแล้ว) [หน้าปัจจุบัน: {currentUrl} | title: {pageTitle}{(screenshotNote.Length > 0 ? " | " + screenshotNote : "")}]");
            }
            return capturedToken;
        }
        catch (PlaywrightException ex)
        {
            throw new CrmIntegrationException($"Login เข้า BlueID ไม่สำเร็จ (browser automation ล้มเหลว) — ตรวจสอบ MerchantID/Username/Password ของบัญชี CRM ของคุณ: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            throw new CrmIntegrationException($"Login เข้า BlueID ไม่สำเร็จ (หมดเวลารอ — ตรวจสอบ MerchantID/Username/Password ของบัญชี CRM ของคุณ หรือหน้าเว็บ CRM อาจเปลี่ยนไป): {ex.Message}");
        }
    }

    // เก็บไว้ที่ App_Data (เหมือน AutomationEvidence ของฟีเจอร์ Automation) ให้เปิดดูได้โดยไม่ต้องรอ reproduce
    // ปัญหาซ้ำ — best-effort ล้วนๆ ถ้า save ไม่ได้ (สิทธิ์ไฟล์/พื้นที่ดิสก์) แค่ไม่แนบ path ไปใน error ไม่ throw ต่อ
    private static async Task<string?> TrySaveDiagnosticScreenshotAsync(IPage page, string tag)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "App_Data", "CrmDiagnostics");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"login-failed-{tag}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path });
            return path;
        }
        catch { return null; }
    }

    // เจอ cookie-consent banner ใหม่ที่ไม่เคยมีมาก่อนบนหน้า login (2026-08-31) — เผื่อไว้ว่าอาจบัง/ขวางการคลิก
    // แท็บ Employee หรือปุ่ม Login ได้ ลองกด "Accept" ทิ้งก่อนเสมอ ถ้าไม่มี/กดไม่ได้ก็แค่ข้ามไป ไม่ fatal
    private static async Task TryDismissCookieBannerAsync(IPage page)
    {
        try
        {
            var acceptButton = page.GetByText("Accept", new PageGetByTextOptions { Exact = false });
            if (await acceptButton.CountAsync() > 0)
                await acceptButton.First.ClickAsync(new LocatorClickOptions { Timeout = 3_000 });
        }
        catch { /* banner ไม่มี/หาไม่เจอ/กดไม่ได้ (เช่นปุ่มถูก disable ไว้) — ปล่อยผ่าน ไม่ให้ล้มทั้ง flow เพราะจุดนี้ */ }
    }

    private readonly record struct JwtClaims(DateTimeOffset? Expiry, string? BranchId);

    // Bearer token from a captured login is a normal JWT — decode its middle (payload) segment to read the
    // "exp"/"branchid" claims so we cache them for real instead of guessing, no OAuth token response to read them from.
    private static JwtClaims DecodeJwtClaims(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return default;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            DateTimeOffset? expiry = doc.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var unixSeconds)
                ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds) : null;
            string? branchId = doc.RootElement.TryGetProperty("branchid", out var b) ? b.GetString() : null;
            return new JwtClaims(expiry, branchId);
        }
        catch { return default; } // malformed/unexpected token shape — callers fall back to their own defaults
    }
}
