namespace Promaxx2.Automation.Core;

/// <summary>
/// Configuration อ่านจาก environment variables เท่านั้น — ห้าม hardcode credential ใน source
/// </summary>
public sealed class AppConfig
{
    /// <summary>Base URL ของ QA Hub API เช่น http://localhost:5038/api/v1/</summary>
    public string QaHubBaseUrl { get; init; } =
        NormalizeBaseUrl(Environment.GetEnvironmentVariable("QAHUB_BASE_URL")) ?? "http://localhost:5038/api/v1/";

    public string? Username { get; init; } = Environment.GetEnvironmentVariable("QAHUB_USERNAME");

    public string? Password { get; init; } = Environment.GetEnvironmentVariable("QAHUB_PASSWORD");

    /// <summary>รหัสพนักงานสำหรับ Positive Login Smoke ของ PromaxxsPos</summary>
    public string? PosUsername { get; init; } = Environment.GetEnvironmentVariable("AUT_POS_USERNAME");

    /// <summary>รหัสผ่านสำหรับ Positive Login Smoke ของ PromaxxsPos</summary>
    public string? PosPassword { get; init; } = Environment.GetEnvironmentVariable("AUT_POS_PASSWORD");

    /// <summary>ชื่อผู้ใช้สำหรับ Positive Login Smoke ของ Promaxxs.App</summary>
    public string? AppUsername { get; init; } = Environment.GetEnvironmentVariable("AUT_APP_USERNAME");

    /// <summary>รหัสผ่านสำหรับ Positive Login Smoke ของ Promaxxs.App</summary>
    public string? AppPassword { get; init; } = Environment.GetEnvironmentVariable("AUT_APP_PASSWORD");

    /// <summary>Full path ของ PromaxxsPos.exe (AUT #2 - บิลขาย)</summary>
    public string PosExePath { get; init; } = Environment.GetEnvironmentVariable("AUT_POS_EXE") ?? "";

    /// <summary>Full path ของ Promaxxs.App.exe (AUT #1 - Master Data)</summary>
    public string AppExePath { get; init; } = Environment.GetEnvironmentVariable("AUT_APP_EXE") ?? "";

    /// <summary>Full path ของ DB/FBMAXX2.FDB (Firebird)</summary>
    public string FdbPath { get; init; } = Environment.GetEnvironmentVariable("AUT_FDB_PATH") ?? "";

    private static string? NormalizeBaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return url.EndsWith('/') ? url : url + "/";
    }
}
