using System.Security.Cryptography;
using System.Text;

namespace ProMaxx2.QA.Domain.Identity;

/// <summary>
/// OAuth client ที่ลงทะเบียนผ่าน Dynamic Client Registration (RFC 7591) — Claude (claude.ai / Claude Desktop / Claude Code) เป็น public client
/// redirect_uri ต้องอยู่ใน allowlist (ตรวจตอนลงทะเบียนโดย OAuthService) จึงลงทะเบียนเองได้โดยไม่เสี่ยงส่ง code ไปเว็บอื่น
/// </summary>
public sealed class OAuthClient
{
    private OAuthClient() { }

    public static OAuthClient Register(string? clientName, IReadOnlyList<string> redirectUris, string? createdIp, DateTime now)
    {
        if (redirectUris.Count == 0) throw new ArgumentException("redirect_uris is required.");
        var name = string.IsNullOrWhiteSpace(clientName) ? "OAuth client" : clientName.Trim();
        return new OAuthClient
        {
            ClientId = "qac_" + OAuthSecrets.Random(24),
            ClientName = name.Length > 100 ? name[..100] : name,
            RedirectUris = string.Join('\n', redirectUris.Select(x => x.Trim()).Distinct()),
            CreatedAt = now,
            CreatedIp = createdIp is { Length: > 64 } ? createdIp[..64] : createdIp,
        };
    }

    public string ClientId { get; private set; } = string.Empty;
    public string ClientName { get; private set; } = string.Empty;
    /// <summary>คั่นด้วยขึ้นบรรทัด</summary>
    public string RedirectUris { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public string? CreatedIp { get; private set; }
    public DateTime? LastUsedAt { get; private set; }

    public IReadOnlyList<string> RedirectUriList => RedirectUris.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    public void MarkUsed(DateTime now) => LastUsedAt = now;
}

/// <summary>
/// คำขออนุญาต 1 ครั้ง (GET /oauth/authorize → หน้ายืนยันในเว็บ → approve/deny → code → /oauth/token)
/// ผูก client, redirect_uri, PKCE challenge, resource; code ใช้ได้ครั้งเดียวภายใน 2 นาที
/// </summary>
public sealed class OAuthAuthorization
{
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(2);
    public static readonly string[] Statuses = ["Pending", "Approved", "Denied", "Exchanged", "Expired"];

    private OAuthAuthorization() { }

    public static OAuthAuthorization Start(string clientId, string redirectUri, string? state, string codeChallenge, string scope, string resource, DateTime now) => new()
    {
        OAuthAuthorizationId = "req_" + OAuthSecrets.Random(24),
        ClientId = clientId, RedirectUri = redirectUri, State = state is { Length: > 500 } ? state[..500] : state,
        CodeChallenge = codeChallenge, Scope = scope, Resource = resource,
        Status = "Pending", CreatedAt = now, ExpiresAt = now + RequestLifetime,
    };

    /// <summary>id สุ่มที่ใช้ในลิงก์หน้ายืนยัน (?oauth=) — ไม่ใช่ความลับระดับ token แต่เดาไม่ได้</summary>
    public string OAuthAuthorizationId { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;
    public string RedirectUri { get; private set; } = string.Empty;
    public string? State { get; private set; }
    public string CodeChallenge { get; private set; } = string.Empty;
    public string Scope { get; private set; } = string.Empty;
    public string Resource { get; private set; } = string.Empty;
    public string Status { get; private set; } = "Pending";
    public Guid? UserId { get; private set; }
    public string? CodeHash { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public DateTime? CodeExpiresAt { get; private set; }

    public bool IsPending(DateTime now) => Status == "Pending" && ExpiresAt > now;

    /// <summary>ผู้ใช้กดอนุญาต — คืน authorization code (แสดงครั้งเดียว ส่งกลับให้ client ทาง redirect)</summary>
    public string Approve(Guid userId, DateTime now)
    {
        if (!IsPending(now)) throw new InvalidOperationException("คำขอนี้หมดอายุหรือถูกใช้ไปแล้ว — กลับไปเริ่มเชื่อมต่อใหม่จาก Claude");
        var code = OAuthSecrets.Random(32);
        UserId = userId; Status = "Approved"; DecidedAt = now; CodeHash = OAuthSecrets.Hash(code); CodeExpiresAt = now + CodeLifetime;
        return code;
    }

    public void Deny(Guid userId, DateTime now)
    {
        if (!IsPending(now)) throw new InvalidOperationException("คำขอนี้หมดอายุหรือถูกใช้ไปแล้ว");
        UserId = userId; Status = "Denied"; DecidedAt = now;
    }

    /// <summary>แลก code — ใช้ได้ครั้งเดียว; คืน false เมื่อ code/ client/ redirect ไม่ตรง หรือหมดอายุ</summary>
    public bool TryExchange(string code, string clientId, string redirectUri, DateTime now)
    {
        if (Status != "Approved" || CodeHash is null || CodeExpiresAt is null || CodeExpiresAt <= now) return false;
        if (clientId != ClientId || redirectUri != RedirectUri) return false;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(OAuthSecrets.Hash(code)), Encoding.ASCII.GetBytes(CodeHash))) return false;
        Status = "Exchanged";
        return true;
    }
}

public static class OAuthSecrets
{
    /// <summary>ค่าสุ่ม base64url (ไม่มี padding)</summary>
    public static string Random(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    public static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>PKCE S256 (RFC 7636): BASE64URL(SHA256(ASCII(code_verifier))) == code_challenge; verifier ยาว 43–128 ตัวอักษร unreserved</summary>
    public static bool VerifyPkce(string? codeVerifier, string codeChallenge)
    {
        if (string.IsNullOrEmpty(codeVerifier) || codeVerifier.Length is < 43 or > 128) return false;
        if (!codeVerifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~')) return false;
        var computed = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(codeChallenge));
    }
}
