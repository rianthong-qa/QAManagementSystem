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

    public AgentConfigStore()
    {
        var dir = AppContext.BaseDirectory;
        _path = Path.Combine(dir, "agent-config.json");
    }

    public AgentConfig Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AgentConfig();
            var json = File.ReadAllText(_path);
            var config = JsonSerializer.Deserialize<AgentConfig>(json) ?? new AgentConfig();
            config.Password = Unprotect(config.Password);
            config.AutPassword = Unprotect(config.AutPassword);
            config.DbPassword = Unprotect(config.DbPassword);
            return config;
        }
        catch
        {
            return new AgentConfig();
        }
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

    private static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            return plain;
        }
    }

    private static string Unprotect(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return encrypted;
        }
    }
}