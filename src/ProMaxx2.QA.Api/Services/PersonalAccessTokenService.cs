using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ProMaxx2.QA.Application.Identity;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>ExpiresAt = วันที่การเชื่อมต่อหมดอายุ (OAuth = refresh token; access token ต่ออายุเองทุกชั่วโมง)</summary>
public sealed record PersonalAccessTokenDto(Guid PersonalAccessTokenId, Guid UserId, string? UserDisplayName, string Name, string TokenPrefix, string Scope,
    DateTime CreatedAt, DateTime ExpiresAt, DateTime? LastUsedAt, string? LastUsedIp, DateTime? RevokedAt, bool IsActive, string Kind);
public sealed record CreatedPersonalAccessTokenDto(string Token, PersonalAccessTokenDto Item);

/// <summary>
/// Personal Access Token สำหรับเชื่อม Claude (MCP) — สร้าง/ยกเลิกเอง, ตรวจด้วย SHA-256, สิทธิ์ใช้ค่าล่าสุดในฐานข้อมูล (cache 1 นาที)
/// ใช้ได้เมื่อผู้ดูแลเปิด "เชื่อมต่อจาก Claude (MCP)" ใน Setting Center เท่านั้น
/// </summary>
public sealed class PersonalAccessTokenService(QaDbContext db, IIdentityRepository identity, IMemoryCache cache, IConfiguration configuration)
{
    public const string AuditEntityType = "PersonalAccessToken";
    private static readonly TimeSpan PrincipalCacheDuration = TimeSpan.FromMinutes(1);

    private const string McpEnabledCacheKey = "pat:mcp-enabled";
    private static readonly TimeSpan McpEnabledCacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>สวิตช์ "เชื่อมต่อจาก Claude (MCP)" — ถูกถามทุกคำขอ /mcp และทุกขั้น OAuth จึง cache 30 วินาที (บันทึกใน Setting Center ล้าง cache ทันทีผ่าน <see cref="ForgetMcpEnabled"/>)</summary>
    public async Task<bool> IsMcpEnabledAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(McpEnabledCacheKey, out bool enabled)) return enabled;
        enabled = await db.AiConfigurations.AsNoTracking().OrderByDescending(x => x.UpdatedAt).Select(x => x.McpEnabled).FirstOrDefaultAsync(ct);
        cache.Set(McpEnabledCacheKey, enabled, McpEnabledCacheDuration);
        return enabled;
    }

    public void ForgetMcpEnabled() => cache.Remove(McpEnabledCacheKey);

    public async Task<IReadOnlyList<PersonalAccessTokenDto>> ListAsync(Guid? userId, CancellationToken ct)
    {
        var q = db.PersonalAccessTokens.AsNoTracking();
        if (userId.HasValue) q = q.Where(x => x.UserId == userId);
        var rows = await q.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
        var userIds = rows.Select(x => x.UserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, x => x.DisplayName, ct);
        var now = DateTime.UtcNow;
        return rows.Select(x => ToDto(x, names.GetValueOrDefault(x.UserId), now)).ToList();
    }

    public async Task<CreatedPersonalAccessTokenDto> CreateAsync(Guid userId, string name, int lifetimeDays, string? clientIp, CancellationToken ct)
    {
        if (!await IsMcpEnabledAsync(ct)) throw new InvalidOperationException("ผู้ดูแลระบบยังไม่เปิดการเชื่อมต่อจาก Claude (Setting Center → AI Configuration → คลังความรู้)");
        var now = DateTime.UtcNow;
        var active = await db.PersonalAccessTokens.CountAsync(x => x.UserId == userId && x.Kind == "Personal" && x.RevokedAt == null && x.ExpiresAt > now, ct);
        if (active >= PersonalAccessToken.MaxActivePerUser) throw new ArgumentException($"มี Token ที่ใช้งานอยู่ครบ {PersonalAccessToken.MaxActivePerUser} ใบแล้ว — ยกเลิกใบที่ไม่ใช้ก่อน");
        var (token, plain) = PersonalAccessToken.Create(userId, name, lifetimeDays, now);
        db.PersonalAccessTokens.Add(token);
        db.AuditLogs.Add(new AuditLog(userId, "Created", AuditEntityType, token.PersonalAccessTokenId.ToString(),
            $"สร้าง Token เชื่อมต่อ Claude \"{token.Name}\" ({token.TokenPrefix}…) หมดอายุ {token.ExpiresAt:yyyy-MM-dd}", null, null, clientIp));
        await db.SaveChangesAsync(ct);
        var displayName = await db.Users.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.DisplayName).FirstOrDefaultAsync(ct);
        return new(plain, ToDto(token, displayName, now));
    }

    /// <summary>ยกเลิก token — เจ้าของ หรือ SYS_ADMIN (asAdmin) เท่านั้น; คืน false เมื่อไม่พบ</summary>
    public async Task<bool> RevokeAsync(Guid actorUserId, Guid tokenId, bool asAdmin, string? clientIp, CancellationToken ct)
    {
        var token = await db.PersonalAccessTokens.FirstOrDefaultAsync(x => x.PersonalAccessTokenId == tokenId && (asAdmin || x.UserId == actorUserId), ct);
        if (token is null) return false;
        if (token.RevokedAt is null)
        {
            token.Revoke(actorUserId, DateTime.UtcNow);
            db.AuditLogs.Add(new AuditLog(actorUserId, "Revoked", AuditEntityType, tokenId.ToString(),
                $"ยกเลิก Token เชื่อมต่อ Claude \"{token.Name}\" ({token.TokenPrefix}…){(token.UserId == actorUserId ? "" : " ของผู้ใช้อื่น")}", null, null, clientIp));
            await db.SaveChangesAsync(ct);
            cache.Remove(CacheKey(token.TokenHash));
        }
        return true;
    }

    /// <summary>ตรวจ token จาก header — คืน principal (สิทธิ์ล่าสุดของผู้ใช้) หรือ null เมื่อไม่ถูกต้อง/หมดอายุ/ยกเลิก/ผู้ใช้ถูกปิด</summary>
    public async Task<ClaimsPrincipal?> AuthenticateAsync(string plainText, string? clientIp, string scheme, CancellationToken ct)
    {
        if (!PersonalAccessToken.LooksLikeToken(plainText)) return null;
        var hash = PersonalAccessToken.Hash(plainText);
        var now = DateTime.UtcNow;
        if (cache.TryGetValue(CacheKey(hash), out CachedPrincipal? cached) && cached is not null && cached.ExpiresAt > now)
            return cached.Principal;

        var token = await db.PersonalAccessTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (token is null || !token.IsActive(now)) return null;
        // OAuth token ต้องออกให้ MCP server นี้ (audience, RFC 8707)
        if (token.Resource is not null && !string.Equals(token.Resource, McpResource, StringComparison.OrdinalIgnoreCase)) return null;
        var isActiveUser = await db.Users.AsNoTracking().Where(x => x.UserId == token.UserId).Select(x => x.IsActive).FirstOrDefaultAsync(ct);
        if (!isActiveUser) return null;
        var profile = await identity.GetProfileAsync(token.UserId, ct);
        if (profile is null) return null;

        if (token.MarkUsed(now, clientIp)) await db.SaveChangesAsync(ct);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, profile.UserId.ToString()), new("sub", profile.UserId.ToString()),
            new(ClaimTypes.Name, profile.Username), new("display_name", profile.DisplayName),
            new("token_id", token.PersonalAccessTokenId.ToString()), new("token_prefix", token.TokenPrefix), new("scope", token.Scope),
        };
        claims.AddRange(profile.Roles.Select(x => new Claim(ClaimTypes.Role, x)));
        claims.AddRange(profile.Permissions.Select(x => new Claim("permission", x)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
        cache.Set(CacheKey(hash), new CachedPrincipal(principal, token.ExpiresAt < now + PrincipalCacheDuration ? token.ExpiresAt : now + PrincipalCacheDuration), PrincipalCacheDuration);
        return principal;
    }

    /// <summary>ล้าง cache ของ token ที่เพิ่งถูกแทนที่/ยกเลิก (refresh rotation, เชื่อมต่อใหม่)</summary>
    public void Forget(string tokenHash) => cache.Remove(CacheKey(tokenHash));

    private string McpResource => $"{(configuration["Mcp:PublicBaseUrl"] ?? "https://api-promaxx2.qahub.store").TrimEnd('/')}/mcp";
    private static string CacheKey(string hash) => $"pat:{hash}";
    private sealed record CachedPrincipal(ClaimsPrincipal Principal, DateTime ExpiresAt);

    private static PersonalAccessTokenDto ToDto(PersonalAccessToken x, string? displayName, DateTime now) =>
        new(x.PersonalAccessTokenId, x.UserId, displayName, x.Name, x.TokenPrefix, x.Scope, x.CreatedAt, x.Kind == "OAuth" ? x.RefreshExpiresAt ?? x.ExpiresAt : x.ExpiresAt,
            x.LastUsedAt, x.LastUsedIp, x.RevokedAt, x.IsConnected(now), x.Kind);
}

/// <summary>
/// Authentication scheme "PersonalAccessToken" (ใช้เฉพาะ endpoint /mcp) — header <c>Authorization: Bearer qak_…</c>
/// ตอบ 401 พร้อม <c>WWW-Authenticate: Bearer</c> ที่ไม่มี resource metadata เพื่อไม่ให้ Claude พยายามทำ OAuth discovery
/// </summary>
public sealed class PersonalAccessTokenAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PersonalAccessToken";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = header["Bearer ".Length..].Trim();
        if (!PersonalAccessToken.LooksLikeToken(token)) return AuthenticateResult.NoResult();
        var service = Context.RequestServices.GetRequiredService<PersonalAccessTokenService>();
        var ip = ClientIp.Resolve(Context);
        var principal = await service.AuthenticateAsync(token, ip, Scheme.Name, Context.RequestAborted);
        return principal is null ? AuthenticateResult.Fail("Token ไม่ถูกต้อง หมดอายุ หรือถูกยกเลิก") : AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        var oauth = Context.RequestServices.GetRequiredService<OAuthService>();
        Response.Headers.WWWAuthenticate = $"Bearer realm=\"ProMaxx2 QA Hub\", resource_metadata=\"{oauth.ResourceMetadataUrl}\", scope=\"{PersonalAccessToken.KnowledgeReadScope}\"";
        await Response.WriteAsJsonAsync(new { detail = "ต้อง Login ผ่าน OAuth (claude.ai) หรือใช้ Personal Access Token ของ QA Hub (ผู้ช่วย AI / คลังความรู้ → เชื่อมต่อ Claude) ใน header Authorization: Bearer qak_…" });
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Response.WriteAsJsonAsync(new { detail = "บัญชีนี้ไม่มีสิทธิ์เชื่อมต่อ Claude (ต้องมีสิทธิ์ KNOWLEDGE.VIEW และ KNOWLEDGE.MCP)" });
    }
}
