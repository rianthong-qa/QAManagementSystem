using System.Security.Cryptography;
using System.Text;

namespace ProMaxx2.QA.Domain.Identity;

/// <summary>
/// Token ส่วนตัวสำหรับเชื่อมต่อเครื่องมือภายนอก (Claude Code / Claude Desktop ผ่าน MCP) — เก็บเฉพาะ SHA-256 ของ token
/// token จริงแสดงครั้งเดียวตอนสร้าง; Scope จำกัดว่าใช้ได้กับ endpoint ใด (ตอนนี้มีแค่ knowledge:read = /mcp)
/// </summary>
public sealed class PersonalAccessToken
{
    public const string Prefix = "qak_";
    /// <summary>access token ของ OAuth (claude.ai Team / Claude Code แบบ Login) — อายุสั้น ต่ออายุด้วย refresh token</summary>
    public const string OAuthAccessPrefix = "qao_";
    public const string OAuthRefreshPrefix = "qrt_";
    public static readonly TimeSpan OAuthAccessLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan OAuthRefreshLifetime = TimeSpan.FromDays(30);
    public static readonly string[] Kinds = ["Personal", "OAuth"];
    public const string KnowledgeReadScope = "knowledge:read";
    public const int DefaultLifetimeDays = 90;
    public const int MaxLifetimeDays = 365;
    public const int MaxActivePerUser = 10;
    public static readonly TimeSpan UsageWriteInterval = TimeSpan.FromMinutes(1);

    private PersonalAccessToken() { }

    public Guid PersonalAccessTokenId { get; private set; }
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string TokenPrefix { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public string Scope { get; private set; } = KnowledgeReadScope;
    /// <summary>Personal = สร้างเองในหน้าเว็บ, OAuth = ได้จากการ Login ผ่าน Claude (1 แถวต่อ 1 การเชื่อมต่อ — refresh แล้วเปลี่ยน hash ในแถวเดิม)</summary>
    public string Kind { get; private set; } = "Personal";
    public string? ClientId { get; private set; }
    public string? RefreshTokenHash { get; private set; }
    public DateTime? RefreshExpiresAt { get; private set; }
    /// <summary>audience ของ token (RFC 8707) — URL ของ MCP server</summary>
    public string? Resource { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }
    public string? LastUsedIp { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public Guid? RevokedBy { get; private set; }

    /// <summary>สร้าง token ใหม่ — คืนค่า token จริง (แสดงให้ผู้ใช้ครั้งเดียว)</summary>
    public static (PersonalAccessToken Token, string PlainText) Create(Guid userId, string name, int lifetimeDays, DateTime now)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User is required.");
        var trimmed = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("กรุณาตั้งชื่อ Token เช่น Claude Code — โน้ตบุ๊ก") : name.Trim();
        if (trimmed.Length > 100) throw new ArgumentException("ชื่อ Token ยาวได้ไม่เกิน 100 ตัวอักษร");
        if (lifetimeDays < 1 || lifetimeDays > MaxLifetimeDays) throw new ArgumentException($"อายุ Token ต้องอยู่ระหว่าง 1–{MaxLifetimeDays} วัน");
        var plain = Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = new PersonalAccessToken
        {
            PersonalAccessTokenId = Guid.NewGuid(), UserId = userId, Name = trimmed,
            TokenPrefix = plain[..12], TokenHash = Hash(plain),
            CreatedAt = now, ExpiresAt = now.AddDays(lifetimeDays),
        };
        return (token, plain);
    }

    /// <summary>สร้างการเชื่อมต่อ OAuth ใหม่ — คืน access + refresh token (ส่งให้ client ครั้งเดียว)</summary>
    public static (PersonalAccessToken Token, string AccessToken, string RefreshToken) CreateOAuth(Guid userId, string clientId, string clientName, string resource, DateTime now)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User is required.");
        var name = $"{(string.IsNullOrWhiteSpace(clientName) ? "OAuth client" : clientName.Trim())} (OAuth)";
        var token = new PersonalAccessToken
        {
            PersonalAccessTokenId = Guid.NewGuid(), UserId = userId, Name = name.Length > 100 ? name[..100] : name,
            Kind = "OAuth", ClientId = clientId, Resource = resource, CreatedAt = now,
        };
        var (access, refresh) = token.IssueOAuthPair(now);
        return (token, access, refresh);
    }

    /// <summary>ต่ออายุด้วย refresh token — refresh token เดิมใช้ไม่ได้อีก (rotation สำหรับ public client)</summary>
    public (string AccessToken, string RefreshToken) Rotate(DateTime now)
    {
        if (Kind != "OAuth" || !IsConnected(now)) throw new InvalidOperationException("การเชื่อมต่อนี้หมดอายุหรือถูกยกเลิกแล้ว");
        return IssueOAuthPair(now);
    }

    private (string AccessToken, string RefreshToken) IssueOAuthPair(DateTime now)
    {
        var access = OAuthAccessPrefix + OAuthSecrets.Random(32);
        var refresh = OAuthRefreshPrefix + OAuthSecrets.Random(32);
        TokenPrefix = access[..12]; TokenHash = Hash(access); ExpiresAt = now + OAuthAccessLifetime;
        RefreshTokenHash = Hash(refresh); RefreshExpiresAt = now + OAuthRefreshLifetime;
        return (access, refresh);
    }

    /// <summary>ยังเชื่อมต่ออยู่ — Personal = token ยังไม่หมดอายุ, OAuth = refresh token ยังใช้ได้ (access หมดอายุทุกชั่วโมงเป็นปกติ)</summary>
    public bool IsConnected(DateTime now) => RevokedAt is null && (Kind == "OAuth" ? RefreshExpiresAt > now : ExpiresAt > now);

    /// <summary>SHA-256 (hex ตัวพิมพ์เล็ก) — token สุ่ม 256 บิต จึงไม่ต้องใช้ salt/KDF แบบรหัสผ่าน</summary>
    public static string Hash(string plainText) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plainText ?? string.Empty)));

    public static bool LooksLikeToken(string? value) => value is { Length: > 20 } && (value.StartsWith(Prefix, StringComparison.Ordinal) || value.StartsWith(OAuthAccessPrefix, StringComparison.Ordinal));

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(Guid? revokedBy, DateTime now)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now; RevokedBy = revokedBy;
    }

    /// <summary>บันทึกการใช้งานไม่เกินนาทีละครั้ง — คืน true เมื่อมีการเปลี่ยนค่า (ต้อง SaveChanges)</summary>
    public bool MarkUsed(DateTime now, string? ip)
    {
        if (LastUsedAt is { } last && now - last < UsageWriteInterval && LastUsedIp == ip) return false;
        LastUsedAt = now;
        LastUsedIp = ip is { Length: > 64 } ? ip[..64] : ip;
        return true;
    }
}
