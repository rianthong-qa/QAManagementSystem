using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProMaxx2.Automation.AgentGui;

public sealed class AgentConfig
{
    public string HubBaseUrl { get; set; } = "http://localhost:5038/api/v1";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string AgentCode { get; set; } = Environment.MachineName;
    public int HeartbeatSeconds { get; set; } = 15;
    public int ActionTimeoutSeconds { get; set; } = 25;
    public string AutExe { get; set; } = "";
    public string AutExe2 { get; set; } = "";
    public string AutUser { get; set; } = "";
    public string AutPassword { get; set; } = "";
    public string DbType { get; set; } = "Firebird";
    public string DbHost { get; set; } = "127.0.0.1";
    public int DbPort { get; set; } = 3050;
    public string DbUser { get; set; } = "SYSDBA";
    public string DbPassword { get; set; } = "";
    public string DbDatabase { get; set; } = "";
    public string RunnerExe { get; set; } = "";
}

public sealed class AgentConfigStore
{
    private readonly string _path;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ProMaxx2.Automation.AgentGui.v1");

    /// <summary>AUT-AGT-004: ข้อความเตือนจากการ Load ครั้งล่าสุด (เช่นถอดรหัสรหัสผ่านไม่ได้) — null ถ้าไม่มีปัญหา</summary>
    public string? LastLoadWarning { get; private set; }

    public AgentConfigStore()
    {
        var dir = AppContext.BaseDirectory;
        _path = Path.Combine(dir, "agent-config.json");
    }

    public AgentConfig Load()
    {
        LastLoadWarning = null;
        if (!File.Exists(_path)) return new AgentConfig();
        AgentConfig config;
        try
        {
            config = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(_path)) ?? new AgentConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LastLoadWarning = $"อ่าน agent-config.json ไม่ได้ ({ex.Message}) — ใช้ค่าเริ่มต้นแทน กรอกตั้งค่าแล้วกดบันทึกใหม่";
            return new AgentConfig();
        }
        var unreadable = new List<string>();
        config.Password = Unprotect(config.Password, "Password (QA Hub)", unreadable);
        config.AutPassword = Unprotect(config.AutPassword, "AUT Password", unreadable);
        config.DbPassword = Unprotect(config.DbPassword, "DB Password", unreadable);
        if (unreadable.Count > 0)
            LastLoadWarning = $"ถอดรหัส {string.Join(", ", unreadable)} ไม่ได้ (ไฟล์ตั้งค่าถูกบันทึกโดย Windows user อื่น หรือบันทึกแบบไม่เข้ารหัสจากเวอร์ชันเก่า) — กรอกรหัสผ่านใหม่แล้วกดบันทึก";
        return config;
    }

    public void Save(AgentConfig config)
    {
        var toWrite = new AgentConfig
        {
            HubBaseUrl = config.HubBaseUrl,
            Username = config.Username,
            Password = Protect(config.Password),
            AgentCode = config.AgentCode,
            HeartbeatSeconds = config.HeartbeatSeconds,
            ActionTimeoutSeconds = config.ActionTimeoutSeconds,
            AutExe = config.AutExe,
            AutExe2 = config.AutExe2,
            AutUser = config.AutUser,
            AutPassword = Protect(config.AutPassword),
            DbType = config.DbType,
            DbHost = config.DbHost,
            DbPort = config.DbPort,
            DbUser = config.DbUser,
            DbPassword = Protect(config.DbPassword),
            DbDatabase = config.DbDatabase,
            RunnerExe = config.RunnerExe,
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(toWrite, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string? FindRunnerExe()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "ProMaxx2.Automation.Runner.exe"),
            Path.Combine(baseDir, "..", "ProMaxx2.Automation.Runner.exe"),
            Path.Combine(baseDir, "..", "..", "..", "ProMaxx2.Automation.Runner", "bin", "Debug", "net10.0-windows", "ProMaxx2.Automation.Runner.exe"),
            Path.Combine(baseDir, "..", "..", "..", "ProMaxx2.Automation.Runner", "bin", "Release", "net10.0-windows", "ProMaxx2.Automation.Runner.exe"),
        };
        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    /// <summary>AUT-AGT-004: เข้ารหัสไม่สำเร็จต้องไม่บันทึกเป็น plaintext แบบเงียบ ๆ (เดิม catch แล้วคืนค่าเดิม) — โยน error ให้ผู้ใช้เห็น</summary>
    private static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("เข้ารหัสรหัสผ่านด้วย Windows DPAPI ไม่สำเร็จ — ไม่บันทึกรหัสผ่านเป็นข้อความธรรมดา", ex);
        }
    }

    /// <summary>AUT-AGT-004: ถอดรหัสไม่ได้ → คืนค่าว่างและบันทึกชื่อช่องไว้เตือน (เดิมคืนข้อความที่เข้ารหัสแล้วมาใช้เป็นรหัสผ่านตรง ๆ)</summary>
    private static string Unprotect(string encrypted, string field, List<string> unreadable)
    {
        if (string.IsNullOrEmpty(encrypted)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            unreadable.Add(field);
            return "";
        }
    }
}