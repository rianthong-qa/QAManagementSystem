using System.Net;
using Microsoft.AspNetCore.Http;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.UnitTests;

/// <summary>IP ของผู้เรียก (rate limit / Audit Log) — เชื่อ CF-Connecting-IP เฉพาะคำขอที่ cloudflared ส่งต่อมาทาง loopback</summary>
public sealed class ClientIpTests
{
    private static HttpContext Request(string remote, string? cfHeader)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (cfHeader is not null) http.Request.Headers[ClientIp.CloudflareHeader] = cfHeader;
        return http;
    }

    [Theory]
    [InlineData("127.0.0.1", "203.0.113.7", "203.0.113.7")]   // ผ่าน Cloudflare Tunnel
    [InlineData("::1", "2001:db8::5", "2001:db8::5")]
    [InlineData("127.0.0.1", null, "127.0.0.1")]
    [InlineData("127.0.0.1", "not-an-ip", "127.0.0.1")]      // header เพี้ยน ไม่ใช้
    [InlineData("192.168.200.50", "203.0.113.7", "192.168.200.50")] // ต่อตรงจาก LAN ปลอม header ไม่ได้
    [InlineData("192.168.200.50", null, "192.168.200.50")]
    public void Resolve_trusts_cloudflare_header_only_from_loopback(string remote, string? header, string expected) =>
        Assert.Equal(expected, ClientIp.Resolve(Request(remote, header)));

    [Fact]
    public void Resolve_without_context_is_null() => Assert.Null(ClientIp.Resolve(null));
}
