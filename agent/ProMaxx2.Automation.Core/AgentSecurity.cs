using System.Security.Cryptography;
using System.Text;

namespace ProMaxx2.Automation.Core;

/// <summary>AUT-AGT-004: ห้ามส่ง credential ของ QA Hub ผ่าน http ไปยังเครื่องอื่น (รหัสผ่านและ JWT จะวิ่งแบบไม่เข้ารหัส)</summary>
public static class HubUrlPolicy
{
    public const string AllowInsecureEnv = "QAHUB_ALLOW_INSECURE_HTTP";

    /// <summary>คืน null เมื่อใช้ได้ หรือข้อความอธิบายเมื่อไม่ควรใช้ — https ใช้ได้เสมอ, http ใช้ได้เฉพาะ localhost
    /// เว้นแต่ตั้ง <see cref="AllowInsecureEnv"/>=true (เครือข่ายภายในที่ยอมรับความเสี่ยงแล้ว)</summary>
    public static string? Validate(string? url, bool allowInsecureHttp)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return $"QA Hub URL ไม่ถูกต้อง: '{url}' (ต้องขึ้นต้นด้วย https:// หรือ http://)";
        if (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback || allowInsecureHttp) return null;
        return $"QA Hub URL '{url}' ใช้ http ไปยังเครื่องอื่น — รหัสผ่านและ token จะถูกส่งแบบไม่เข้ารหัส ให้ใช้ https "
            + $"(เช่น https://api-promaxx2.qahub.store/api/v1) หรือตั้ง {AllowInsecureEnv}=true ถ้าจำเป็นจริง ๆ ในเครือข่ายภายใน";
    }

    public static bool IsInsecureAllowed(Func<string, string?> getEnv)
        => getEnv(AllowInsecureEnv) is { } v && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1");
}

/// <summary>AUT-AGT-004: อ่านรหัสผ่านของ Agent จาก environment — ถ้ามี <c>NAME_DPAPI</c> (ค่าที่ <c>set-agent-env.ps1</c>
/// เข้ารหัสด้วย <c>ConvertFrom-SecureString</c> = DPAPI ของ Windows user ปัจจุบัน) จะถอดรหัสแล้วใช้แทน <c>NAME</c> แบบ plaintext</summary>
public static class AgentSecrets
{
    public const string ProtectedSuffix = "_DPAPI";

    public static string Read(string name, Func<string, string?> getEnv)
    {
        var protectedHex = getEnv(name + ProtectedSuffix);
        if (!string.IsNullOrWhiteSpace(protectedHex)) return Unprotect(name, protectedHex.Trim());
        return getEnv(name) ?? "";
    }

    /// <summary>รูปแบบเดียวกับ <c>ConvertFrom-SecureString</c> (ไม่ระบุ -Key): hex ของ DPAPI blob ที่เข้ารหัสข้อความ UTF-16</summary>
    public static string Protect(string plain)
        => Convert.ToHexString(ProtectedData.Protect(Encoding.Unicode.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    private static string Unprotect(string name, string hex)
    {
        try
        {
            var plain = ProtectedData.Unprotect(Convert.FromHexString(hex), null, DataProtectionScope.CurrentUser);
            return Encoding.Unicode.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            // ไม่ fallback เป็นค่าว่าง/ค่าเข้ารหัสแบบเงียบ ๆ (จะได้ login fail แบบหาสาเหตุไม่เจอ) — บอกวิธีแก้แทน
            throw new InvalidOperationException($"ถอดรหัส {name}{ProtectedSuffix} ไม่ได้ — ค่านี้ถูกเข้ารหัสด้วย Windows user อื่นหรือข้อมูลเสีย ให้รัน set-agent-env.ps1 ใหม่ด้วย Windows user ที่ใช้รัน Agent", ex);
        }
    }
}
