using System.Net;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// IP จริงของผู้เรียก — ใช้เป็น key ของ rate limit และบันทึกใน Audit Log / token
/// เชื่อ header CF-Connecting-IP เฉพาะคำขอที่มาจาก loopback (cloudflared บนเครื่องนี้ส่งต่อมาทาง localhost เสมอ)
/// คำขอที่ต่อตรงเข้า :5038 (เช่นจาก LAN) ปลอม header นี้ได้ จึงใช้ IP ที่เชื่อมต่อจริงแทน — กันการสุ่ม header เพื่อเลี่ยง rate limit login/share/oauth
/// </summary>
public static class ClientIp
{
    public const string CloudflareHeader = "CF-Connecting-IP";

    public static string? Resolve(HttpContext? http)
    {
        if (http is null) return null;
        var remote = http.Connection.RemoteIpAddress;
        if (remote is not null && IPAddress.IsLoopback(remote)
            && IPAddress.TryParse(http.Request.Headers[CloudflareHeader].FirstOrDefault()?.Trim(), out var forwarded))
            return forwarded.ToString();
        return remote?.ToString();
    }
}
