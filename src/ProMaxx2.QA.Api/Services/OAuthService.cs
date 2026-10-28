using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>ข้อผิดพลาดตาม RFC 6749 §5.2 / RFC 7591 §3.2.2 (error + error_description)</summary>
public sealed class OAuthException(string error, string description, int statusCode = 400) : Exception(description)
{
    public string Error { get; } = error;
    public int StatusCode { get; } = statusCode;
}

public sealed record OAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("scope")] string Scope);

public sealed record OAuthConsentDto(string RequestId, string ClientName, string RedirectHost, bool IsLoopback, string Scope, DateTime ExpiresAt, bool McpEnabled, bool CanApprove);
public sealed record OAuthDecisionDto(string RedirectUrl);

/// <summary>ผลของ GET /oauth/authorize — ไปหน้ายืนยันในเว็บ, ส่ง error กลับ client ทาง redirect หรือแสดงหน้า error (เมื่อ client/redirect_uri เชื่อถือไม่ได้)</summary>
public sealed record OAuthAuthorizeResult(string? RedirectUrl, string? ErrorPage);

/// <summary>
/// OAuth 2.1 Authorization Server สำหรับ MCP (AI_KNOWLEDGE_CHAT_PLAN.md — OAuth สำหรับ claude.ai Team)
/// - Dynamic Client Registration (RFC 7591) แบบ public client; redirect_uri ต้องเป็น https://claude.ai/api/mcp/auth_callback (config) หรือ loopback (Claude Code)
/// - Authorization Code + PKCE S256 (บังคับ), resource indicator (RFC 8707) ต้องเป็น URL ของ MCP server
/// - ผู้ใช้ Login + ยืนยันในหน้าเว็บ QA Hub (?oauth=) — API ไม่มี session/cookie ของตัวเอง
/// - access token 1 ชม. + refresh token 30 วันแบบ rotation เก็บในตาราง PersonalAccessTokens (Kind = OAuth) จึงยกเลิกได้จากหน้า "เชื่อมต่อ Claude"
/// </summary>
public sealed class OAuthService(QaDbContext db, IConfiguration configuration, PersonalAccessTokenService tokens)
{
    public const string AuditEntityType = "OAuth";
    public static readonly string[] SupportedScopes = [PersonalAccessToken.KnowledgeReadScope];

    public string Issuer => (configuration["Mcp:PublicBaseUrl"] ?? "https://api-promaxx2.qahub.store").TrimEnd('/');
    public string Resource => $"{Issuer}/mcp";
    public string ResourceMetadataUrl => $"{Issuer}/.well-known/oauth-protected-resource/mcp";
    private string WebBaseUrl => (configuration["PublicWebBaseUrl"] ?? "https://promaxx2.qahub.store").TrimEnd('/');
    private IReadOnlyList<string> AllowedRedirectUris => configuration.GetSection("Mcp:OAuth:AllowedRedirectUris").Get<string[]>() is { Length: > 0 } configured
        ? configured : ["https://claude.ai/api/mcp/auth_callback"];
    private bool AllowLoopback => configuration.GetValue("Mcp:OAuth:AllowLoopbackRedirects", true);

    // ---------- metadata ----------

    /// <summary>RFC 9728 Protected Resource Metadata — resource ต้องตรงกับ URL ที่ผู้ใช้ใส่ใน Claude</summary>
    public object ProtectedResourceMetadata() => new Dictionary<string, object>
    {
        ["resource"] = Resource,
        ["authorization_servers"] = new[] { Issuer },
        ["scopes_supported"] = SupportedScopes,
        ["bearer_methods_supported"] = new[] { "header" },
        ["resource_name"] = "ProMaxx2 QA Hub — คลังความรู้",
    };

    /// <summary>RFC 8414 Authorization Server Metadata — ไม่ประกาศ CIMD (Claude จะใช้ DCR); public client เท่านั้น</summary>
    public object AuthorizationServerMetadata() => new Dictionary<string, object>
    {
        ["issuer"] = Issuer,
        ["authorization_endpoint"] = $"{Issuer}/oauth/authorize",
        ["token_endpoint"] = $"{Issuer}/oauth/token",
        ["registration_endpoint"] = $"{Issuer}/oauth/register",
        ["response_types_supported"] = new[] { "code" },
        ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
        ["code_challenge_methods_supported"] = new[] { "S256" },
        ["token_endpoint_auth_methods_supported"] = new[] { "none" },
        ["scopes_supported"] = SupportedScopes,
        ["authorization_response_iss_parameter_supported"] = true,
    };

    // ---------- redirect_uri ----------

    public bool IsAllowedRedirectUri(string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !string.IsNullOrEmpty(parsed.Fragment)) return false;
        if (AllowedRedirectUris.Any(x => string.Equals(x, uri, StringComparison.Ordinal))) return true;
        return AllowLoopback && IsLoopback(parsed);
    }

    /// <summary>ตรงกับที่ลงทะเบียนทุกตัวอักษร — ยกเว้น loopback ที่ไม่สนพอร์ต (RFC 8252 §7.3, Claude Code ใช้พอร์ตสุ่ม)</summary>
    public static bool RedirectMatches(string registered, string requested)
    {
        if (string.Equals(registered, requested, StringComparison.Ordinal)) return true;
        if (!Uri.TryCreate(registered, UriKind.Absolute, out var a) || !Uri.TryCreate(requested, UriKind.Absolute, out var b)) return false;
        return IsLoopback(a) && IsLoopback(b) && a.Scheme == b.Scheme && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
            && a.AbsolutePath == b.AbsolutePath && a.Query == b.Query;
    }

    private static bool IsLoopback(Uri uri) => uri.Scheme == Uri.UriSchemeHttp && (uri.Host is "localhost" or "127.0.0.1" or "[::1]" || uri.IsLoopback);

    // ---------- registration (RFC 7591) ----------

    public async Task<object> RegisterAsync(JsonElement body, string? clientIp, CancellationToken ct)
    {
        if (body.ValueKind != JsonValueKind.Object) throw new OAuthException("invalid_client_metadata", "Body must be a JSON object.");
        var redirects = body.TryGetProperty("redirect_uris", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : [];
        if (redirects.Count == 0 || redirects.Count > 10) throw new OAuthException("invalid_redirect_uri", "redirect_uris is required (1-10).");
        var rejected = redirects.FirstOrDefault(x => !IsAllowedRedirectUri(x));
        if (rejected is not null) throw new OAuthException("invalid_redirect_uri", $"redirect_uri is not allowed by this server: {rejected}");
        // ทุก client เป็น public client (PKCE บังคับ) — client ที่ขอวิธียืนยันตัวแบบอื่นจะได้ "none" กลับไป (RFC 7591 §3.2.1: server กำหนดค่าแทนได้และ client ต้องใช้ค่าที่ตอบกลับ)
        if (body.TryGetProperty("grant_types", out var g) && g.ValueKind == JsonValueKind.Array
            && g.EnumerateArray().Any(x => x.GetString() is not ("authorization_code" or "refresh_token")))
            throw new OAuthException("invalid_client_metadata", "Supported grant_types: authorization_code, refresh_token.");
        if (body.TryGetProperty("response_types", out var rt) && rt.ValueKind == JsonValueKind.Array && rt.EnumerateArray().Any(x => x.GetString() != "code"))
            throw new OAuthException("invalid_client_metadata", "Supported response_types: code.");

        var now = DateTime.UtcNow;
        var client = OAuthClient.Register(Str(body, "client_name"), redirects, clientIp, now);
        db.OAuthClients.Add(client);
        db.AuditLogs.Add(new AuditLog(null, "Created", AuditEntityType, client.ClientId, $"ลงทะเบียน OAuth client \"{client.ClientName}\" → {string.Join(", ", client.RedirectUriList)}", null, null, clientIp));
        await db.SaveChangesAsync(ct);
        return new Dictionary<string, object?>
        {
            ["client_id"] = client.ClientId,
            ["client_id_issued_at"] = new DateTimeOffset(now).ToUnixTimeSeconds(),
            ["client_name"] = client.ClientName,
            ["redirect_uris"] = client.RedirectUriList,
            ["grant_types"] = new[] { "authorization_code", "refresh_token" },
            ["response_types"] = new[] { "code" },
            ["token_endpoint_auth_method"] = "none",
            ["scope"] = string.Join(' ', SupportedScopes),
        };
    }

    // ---------- authorize ----------

    public async Task<OAuthAuthorizeResult> AuthorizeAsync(IQueryCollection query, CancellationToken ct)
    {
        string? Q(string name) => query.TryGetValue(name, out var v) ? v.ToString() : null;
        var clientId = Q("client_id");
        var redirectUri = Q("redirect_uri");
        var client = string.IsNullOrEmpty(clientId) ? null : await db.OAuthClients.FirstOrDefaultAsync(x => x.ClientId == clientId, ct);
        // client/redirect_uri ไม่ถูกต้อง = ห้าม redirect (RFC 6749 §4.1.2.1) — แสดงหน้า error แทน
        if (client is null) return new(null, "ไม่พบ OAuth client — ลบ connector แล้วเพิ่มใหม่ใน Claude");
        if (string.IsNullOrEmpty(redirectUri) || !client.RedirectUriList.Any(x => RedirectMatches(x, redirectUri)) || !IsAllowedRedirectUri(redirectUri))
            return new(null, "redirect_uri ไม่ตรงกับที่ลงทะเบียนไว้");

        var state = Q("state");
        string Back(string error, string description) => AppendQuery(redirectUri, ("error", error), ("error_description", description), ("state", state), ("iss", Issuer));
        if (Q("response_type") != "code") return new(Back("unsupported_response_type", "response_type must be code"), null);
        var challenge = Q("code_challenge");
        if (string.IsNullOrEmpty(challenge) || Q("code_challenge_method") != "S256" || challenge.Length is < 43 or > 128)
            return new(Back("invalid_request", "PKCE with code_challenge_method=S256 is required"), null);
        var resource = NormalizeResource(Q("resource"));
        if (resource is null) return new(Back("invalid_target", $"resource must be {Resource}"), null);
        var scope = NormalizeScope(Q("scope"));
        if (scope is null) return new(Back("invalid_scope", $"Supported scopes: {string.Join(' ', SupportedScopes)}"), null);
        if (!await tokens.IsMcpEnabledAsync(ct)) return new(Back("access_denied", "ผู้ดูแลระบบปิดการเชื่อมต่อจาก Claude (MCP) อยู่"), null);

        var now = DateTime.UtcNow;
        var request = OAuthAuthorization.Start(client.ClientId, redirectUri, state, challenge, scope, resource, now);
        db.OAuthAuthorizations.Add(request);
        client.MarkUsed(now);
        await db.SaveChangesAsync(ct);
        return new($"{WebBaseUrl}/?oauth={Uri.EscapeDataString(request.OAuthAuthorizationId)}", null);
    }

    public async Task<OAuthConsentDto?> GetConsentAsync(string requestId, ClaimsPrincipal user, CancellationToken ct)
    {
        var request = await db.OAuthAuthorizations.AsNoTracking().FirstOrDefaultAsync(x => x.OAuthAuthorizationId == requestId, ct);
        if (request is null || !request.IsPending(DateTime.UtcNow)) return null;
        var client = await db.OAuthClients.AsNoTracking().FirstAsync(x => x.ClientId == request.ClientId, ct);
        var redirect = new Uri(request.RedirectUri);
        return new(request.OAuthAuthorizationId, client.ClientName, redirect.Host, IsLoopback(redirect), request.Scope, request.ExpiresAt, await tokens.IsMcpEnabledAsync(ct), CanConnect(user));
    }

    public async Task<OAuthDecisionDto> DecideAsync(string requestId, ClaimsPrincipal user, Guid userId, bool approve, string? clientIp, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var request = await db.OAuthAuthorizations.FirstOrDefaultAsync(x => x.OAuthAuthorizationId == requestId, ct);
        if (request is null || !request.IsPending(now)) throw new OAuthException("invalid_request", "คำขอนี้หมดอายุหรือถูกใช้ไปแล้ว — กลับไปกด Connect ใน Claude อีกครั้ง", 404);
        var client = await db.OAuthClients.AsNoTracking().FirstAsync(x => x.ClientId == request.ClientId, ct);
        if (approve && !await tokens.IsMcpEnabledAsync(ct)) throw new OAuthException("access_denied", "ผู้ดูแลระบบปิดการเชื่อมต่อจาก Claude (MCP) อยู่", 403);
        if (approve && !CanConnect(user)) throw new OAuthException("access_denied", "บัญชีนี้ไม่มีสิทธิ์เชื่อมต่อ Claude (ต้องมีสิทธิ์ KNOWLEDGE.VIEW และ KNOWLEDGE.MCP)", 403);
        string url;
        if (approve)
        {
            var code = request.Approve(userId, now);
            url = AppendQuery(request.RedirectUri, ("code", code), ("state", request.State), ("iss", Issuer));
        }
        else
        {
            request.Deny(userId, now);
            url = AppendQuery(request.RedirectUri, ("error", "access_denied"), ("error_description", "ผู้ใช้ไม่อนุญาต"), ("state", request.State), ("iss", Issuer));
        }
        db.AuditLogs.Add(new AuditLog(userId, approve ? "Approved" : "Denied", AuditEntityType, request.OAuthAuthorizationId,
            $"{(approve ? "อนุญาต" : "ไม่อนุญาต")}ให้ \"{client.ClientName}\" เชื่อมต่อคลังความรู้ (OAuth)", null, null, clientIp));
        await db.SaveChangesAsync(ct);
        return new(url);
    }

    // ---------- token ----------

    public async Task<OAuthTokenResponse> TokenAsync(IFormCollection form, string? clientIp, CancellationToken ct)
    {
        string? F(string name) => form.TryGetValue(name, out var v) ? v.ToString() : null;
        var grantType = F("grant_type");
        var clientId = F("client_id");
        if (string.IsNullOrEmpty(clientId)) throw new OAuthException("invalid_client", "client_id is required.", 401);
        var client = await db.OAuthClients.FirstOrDefaultAsync(x => x.ClientId == clientId, ct) ?? throw new OAuthException("invalid_client", "Unknown client.", 401);
        var now = DateTime.UtcNow;
        if (!await tokens.IsMcpEnabledAsync(ct)) throw new OAuthException("invalid_grant", "MCP is disabled by the administrator.");

        if (grantType == "authorization_code")
        {
            var code = F("code") ?? throw new OAuthException("invalid_request", "code is required.");
            var redirectUri = F("redirect_uri") ?? throw new OAuthException("invalid_request", "redirect_uri is required.");
            var codeHash = OAuthSecrets.Hash(code);
            var request = await db.OAuthAuthorizations.FirstOrDefaultAsync(x => x.CodeHash == codeHash, ct);
            if (request is null || !request.TryExchange(code, client.ClientId, redirectUri, now))
                throw new OAuthException("invalid_grant", "Authorization code is invalid, expired or already used.");
            // code ถูกใช้ไปแล้วเมื่อผ่าน TryExchange — verifier/resource ผิดก็บันทึกสถานะ Exchanged ก่อนแจ้ง error (ห้ามลองซ้ำด้วย code เดิม)
            var resource = F("resource");
            var failure = !OAuthSecrets.VerifyPkce(F("code_verifier"), request.CodeChallenge) ? new OAuthException("invalid_grant", "PKCE verification failed.")
                : !string.IsNullOrEmpty(resource) && NormalizeResource(resource) != request.Resource ? new OAuthException("invalid_target", $"resource must be {Resource}")
                : null;
            if (failure is not null)
            {
                await SaveOnceAsync("Authorization code is invalid, expired or already used.", ct);
                throw failure;
            }
            var userId = request.UserId!.Value;
            // 1 การเชื่อมต่อต่อผู้ใช้ต่อ client — เชื่อมต่อใหม่ = ยกเลิกของเดิม
            var previous = await db.PersonalAccessTokens.Where(x => x.UserId == userId && x.Kind == "OAuth" && x.ClientId == client.ClientId && x.RevokedAt == null).ToListAsync(ct);
            foreach (var old in previous) old.Revoke(userId, now);
            var (grant, access, refresh) = PersonalAccessToken.CreateOAuth(userId, client.ClientId, client.ClientName, request.Resource, now);
            db.PersonalAccessTokens.Add(grant);
            db.AuditLogs.Add(new AuditLog(userId, "Created", PersonalAccessTokenService.AuditEntityType, grant.PersonalAccessTokenId.ToString(),
                $"เชื่อมต่อ \"{client.ClientName}\" ผ่าน OAuth ({grant.TokenPrefix}…){(previous.Count > 0 ? $" แทนการเชื่อมต่อเดิม {previous.Count} รายการ" : "")}", null, null, clientIp));
            client.MarkUsed(now);
            // Status เป็น concurrency token — คำขอแลก code เดียวกันที่บันทึกทีหลังจะล้มที่นี่ (ไม่ออก grant ซ้ำ)
            await SaveOnceAsync("Authorization code is invalid, expired or already used.", ct);
            foreach (var old in previous) tokens.Forget(old.TokenHash);
            return Response(access, refresh, request.Scope);
        }

        if (grantType == "refresh_token")
        {
            var refresh = F("refresh_token") ?? throw new OAuthException("invalid_request", "refresh_token is required.");
            var hash = OAuthSecrets.Hash(refresh);
            var grant = await db.PersonalAccessTokens.FirstOrDefaultAsync(x => x.RefreshTokenHash == hash && x.Kind == "OAuth", ct);
            if (grant is null)
            {
                // refresh token ที่ถูก rotate ไปแล้วถูกนำมาใช้อีก = อาจถูกขโมย → ยกเลิกการเชื่อมต่อนั้นทั้งหมด (ผู้ใช้ต้องเชื่อมต่อใหม่)
                var reused = await db.PersonalAccessTokens.FirstOrDefaultAsync(x => x.PreviousRefreshTokenHash == hash && x.Kind == "OAuth" && x.RevokedAt == null, ct);
                if (reused is not null && reused.ClientId == client.ClientId)
                {
                    reused.Revoke(null, now);
                    db.AuditLogs.Add(new AuditLog(reused.UserId, "Revoked", PersonalAccessTokenService.AuditEntityType, reused.PersonalAccessTokenId.ToString(),
                        $"ยกเลิกการเชื่อมต่อ \"{client.ClientName}\" อัตโนมัติ — refresh token เก่าถูกนำมาใช้ซ้ำ", null, null, clientIp));
                    try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { /* rotate พร้อมกัน — ครั้งหน้าจะจับได้อีก */ }
                    tokens.Forget(reused.TokenHash);
                }
                throw new OAuthException("invalid_grant", "Refresh token is invalid, expired or revoked.");
            }
            if (grant.ClientId != client.ClientId || !grant.IsConnected(now)) throw new OAuthException("invalid_grant", "Refresh token is invalid, expired or revoked.");
            var userActive = await db.Users.AsNoTracking().Where(x => x.UserId == grant.UserId).Select(x => x.IsActive).FirstOrDefaultAsync(ct);
            if (!userActive) throw new OAuthException("invalid_grant", "User is disabled.");
            var oldHash = grant.TokenHash;
            var (access, nextRefresh) = grant.Rotate(now);
            // RefreshTokenHash เป็น concurrency token — refresh พร้อมกันด้วย token เดียวกัน คำขอที่บันทึกทีหลังล้ม (ไม่เขียนทับคู่ token ของอีกคำขอ)
            await SaveOnceAsync("Refresh token is invalid, expired or revoked.", ct);
            tokens.Forget(oldHash);
            return Response(access, nextRefresh, grant.Scope);
        }

        throw new OAuthException("unsupported_grant_type", "Supported grant types: authorization_code, refresh_token.");
    }

    /// <summary>บันทึกโดยถือว่าการชนกันของ concurrency token = code/refresh token ถูกใช้ไปแล้วโดยคำขออื่น → invalid_grant</summary>
    private async Task SaveOnceAsync(string message, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new OAuthException("invalid_grant", message); }
    }

    private static OAuthTokenResponse Response(string access, string refresh, string scope) =>
        new(access, "Bearer", (int)PersonalAccessToken.OAuthAccessLifetime.TotalSeconds, refresh, scope);

    private static bool CanConnect(ClaimsPrincipal user) =>
        user.IsInRole("SYS_ADMIN") || (user.HasClaim("permission", "KNOWLEDGE.VIEW") && user.HasClaim("permission", "KNOWLEDGE.MCP"));

    /// <summary>resource ไม่ส่งมา = ใช้ MCP server นี้ (client รุ่นเก่า); ส่งมาต้องตรง (ไม่สน / ท้าย)</summary>
    private string? NormalizeResource(string? resource) =>
        string.IsNullOrWhiteSpace(resource) ? Resource : string.Equals(resource.Trim().TrimEnd('/'), Resource, StringComparison.OrdinalIgnoreCase) ? Resource : null;

    /// <summary>ไม่ระบุ = knowledge:read; offline_access ยอมรับแต่ไม่จำเป็น (ออก refresh token เสมอ)</summary>
    private static string? NormalizeScope(string? scope)
    {
        var parts = (scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(x => !SupportedScopes.Contains(x) && x != "offline_access")) return null;
        return string.Join(' ', SupportedScopes);
    }

    public static string AppendQuery(string url, params (string Key, string? Value)[] values)
    {
        var pairs = values.Where(x => x.Value is not null).Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}");
        return $"{url}{(url.Contains('?') ? "&" : "?")}{string.Join("&", pairs)}";
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
