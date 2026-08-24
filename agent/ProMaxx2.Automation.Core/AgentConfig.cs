namespace ProMaxx2.Automation.Core;

public sealed class AgentConfig
{
    public string HubBaseUrl { get; init; } = "http://localhost:5038/api/v1";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string AgentCode { get; init; } = Environment.MachineName;
    public string AgentVersion { get; init; } = "1.0.0";
    public string MachineName { get; init; } = Environment.MachineName;
    public string AutExe { get; init; } = "";
    public string AutUser { get; init; } = "";
    public string AutPassword { get; init; } = "";
    public string? FdbPath { get; init; }
    public string TargetApp { get; init; } = "WindowsUI";
    public string DbType { get; init; } = "Firebird";
    public string DbHost { get; init; } = "127.0.0.1";
    public int DbPort { get; init; } = 3050;
    public string DbUser { get; init; } = "SYSDBA";
    public string DbPassword { get; init; } = "";
    public string DbDatabase { get; init; } = "";
    public int HeartbeatSeconds { get; init; } = 15;
    public int ActionTimeoutSeconds { get; init; } = 20;
    public TimeSpan ActionTimeout => TimeSpan.FromSeconds(ActionTimeoutSeconds);

    public static AgentConfig FromEnvironment()
    {
        static string Get(string key) => Environment.GetEnvironmentVariable(key) ?? "";
        static int GetInt(string key, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(key), out var value) ? value : fallback;
        return new AgentConfig
        {
            HubBaseUrl = (Get("QAHUB_BASE_URL") is { Length: > 0 } url ? url : "http://localhost:5038/api/v1").TrimEnd('/'),
            Username = Get("QAHUB_USERNAME"),
            Password = Get("QAHUB_PASSWORD"),
            AgentCode = (Get("AGENT_CODE") is { Length: > 0 } code ? code : Environment.MachineName),
            AutExe = Get("AUT_EXE"),
            AutUser = Get("AUT_USER"),
            AutPassword = Get("AUT_PASSWORD"),
            FdbPath = Get("AUT_FDB_PATH") is { Length: > 0 } fdb ? fdb : null,
            TargetApp = ResolveTargetApp(Get("AUT_TARGET"), Get("AUT_EXE")),
            DbType = Get("AUT_DB_TYPE") is { Length: > 0 } dt ? dt : "Firebird",
            DbHost = Get("AUT_DB_HOST") is { Length: > 0 } dh ? dh : "127.0.0.1",
            DbPort = GetInt("AUT_DB_PORT", 3050),
            DbUser = Get("AUT_DB_USER") is { Length: > 0 } du ? du : "SYSDBA",
            DbPassword = Get("AUT_DB_PASSWORD"),
            DbDatabase = Get("AUT_DB_DATABASE") is { Length: > 0 } dd ? dd : "",
            HeartbeatSeconds = GetInt("HEARTBEAT_SECONDS", 15),
            ActionTimeoutSeconds = GetInt("ACTION_TIMEOUT_SECONDS", 20),
        };
    }

    private static string ResolveTargetApp(string targetEnv, string autExe)
    {
        if (!string.IsNullOrWhiteSpace(targetEnv))
        {
            var t = targetEnv.Trim();
            if (t.Equals("Pos", StringComparison.OrdinalIgnoreCase) || t.Equals("App", StringComparison.OrdinalIgnoreCase) || t.Equals("WindowsUI", StringComparison.OrdinalIgnoreCase)) return t == "WindowsUI" ? "WindowsUI" : (t == "Pos" ? "Pos" : "App");
        }
        if (!string.IsNullOrWhiteSpace(autExe))
        {
            var name = Path.GetFileNameWithoutExtension(autExe);
            if (name.Contains("Pos", StringComparison.OrdinalIgnoreCase)) return "Pos";
            if (name.Contains("App", StringComparison.OrdinalIgnoreCase)) return "App";
        }
        return "WindowsUI";
    }
}

public sealed record DslDocument { public string DslVersion { get; set; } = "1.0"; public string AutomationType { get; set; } = "WindowsUI"; public List<DslStep> Steps { get; set; } = []; }
public sealed record DslStep { public int StepNo { get; set; } public string Action { get; set; } = ""; public Dictionary<string, string> Parameters { get; set; } = new(); }

public sealed record ObjectDescriptor
{
    public Guid AutomationObjectId { get; init; }
    public string ApplicationCode { get; init; } = "Promaxx2";
    public string ScreenCode { get; init; } = "Default";
    public string ObjectCode { get; init; } = "";
    public string ObjectName { get; init; } = "";
    public string ControlType { get; init; } = "Control";
    public string? AutomationId { get; init; }
    public string Key => $"{ScreenCode}.{ObjectCode}";
}

public sealed record EvidenceAttachment(byte[] Data, string FileName, string EvidenceType);
public sealed record StepOutcome(bool Passed, string? ActualResult, string? ErrorCode, string? ErrorMessage, byte[]? Screenshot, DateTime StartedAt, DateTime CompletedAt, IReadOnlyList<EvidenceAttachment>? Evidence = null);