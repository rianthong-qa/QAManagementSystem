using System.Text;
using System.Text.Json;
using ProMaxx2.Automation.Core;
using ProMaxx2.Automation.Hub;

if (args is { Length: > 0 } && args[0].Equals("inspect", StringComparison.OrdinalIgnoreCase))
{
    return await RunInspectAsync(args);
}

if (args is { Length: > 0 } && args[0].Equals("trylogin", StringComparison.OrdinalIgnoreCase))
{
    return await RunTryLoginAsync(args);
}

if (args is { Length: > 0 } && args[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
{
    return await RunVerifyAsync(args);
}

var config = AgentConfig.FromEnvironment();

if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
{
    Console.Error.WriteLine("Missing QAHUB_USERNAME / QAHUB_PASSWORD. ตั้งค่าก่อนรัน (ดู set-agent-env.ps1)");
    return 2;
}

using var client = new QaHubClient(config);

if (!await client.LoginAsync(CancellationToken.None))
{
    Console.Error.WriteLine($"Login ไป QA Hub ล้มเหลว ({config.HubBaseUrl}). ตรวจ Username/Password และสิทธิ์ AUTOMATION.EXECUTE");
    return 2;
}
Console.WriteLine($"[agent] Logged in to {config.HubBaseUrl} as {config.Username}");

await client.RegisterAsync(CancellationToken.None);
Console.WriteLine($"[agent] Registered agent '{config.AgentCode}' v{config.AgentVersion}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

while (!cts.IsCancellationRequested)
{
    try
    {
        await client.HeartbeatAsync("Idle", null, cts.Token);
        var package = await client.ClaimJobAsync(cts.Token);
        if (package is null)
        {
            await Task.Delay(TimeSpan.FromSeconds(config.HeartbeatSeconds), cts.Token);
            continue;
        }
        Console.WriteLine($"[job] Claimed {package.AutomationCode} (build {package.BuildNumber}) exec {package.AutomationExecutionId}");
        await ExecutePackageAsync(client, package, config, cts.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[agent] error: {ex.Message}");
        await Task.Delay(TimeSpan.FromSeconds(config.HeartbeatSeconds), cts.Token);
    }
}

Console.WriteLine("[agent] stopped.");
return 0;

static async Task<int> RunTryLoginAsync(string[] args)
{
    string? exe = null;
    var timeout = 30;
    var waitAfter = 8;
    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--exe": exe = args[++i]; break;
            case "--timeout": timeout = int.TryParse(args[++i], out var t) ? t : 30; break;
            case "--after": waitAfter = int.TryParse(args[++i], out var a) ? a : 8; break;
        }
    }
    if (string.IsNullOrWhiteSpace(exe)) { Console.Error.WriteLine("Usage: Runner trylogin --exe <path>"); return 2; }
    var candidates = new (string Emp, string Pwd)[]
    {
        ("admin", ""), ("admin", "admin"), ("admin", "1234"), ("1", ""), ("1", "1"),
        ("sa", ""), ("sa", "sa"), ("1000", ""), ("001", ""), ("a001", ""), ("admin", "123456"),
        ("admin", "password"), ("supervisor", ""), ("manager", ""), ("test", ""), ("qa", ""),
    };
    foreach (var (emp, pwd) in candidates)
    {
        Console.WriteLine($"\n== trying {emp} / '{pwd}' ==");
        var login = new LoginCredentials(emp, pwd, waitAfter);
        var result = await UiInspector.InspectAsync(exe!, null, timeout, 8, login, CancellationToken.None);
        if (result is null) { Console.WriteLine("no window; abort."); return 2; }
        var hasLogin = result.Nodes.Any(n => n.AutomationId == "TxtEmpId");
        Console.WriteLine($"   after login: TxtEmpId still present = {hasLogin}");
        if (!hasLogin)
        {
            Console.WriteLine($"SUCCESS: {emp} / '{pwd}'");
            await File.WriteAllTextAsync("tools/last-valid-login.txt", $"{emp}|{pwd}");
            return 0;
        }
    }
    Console.WriteLine("No candidate worked.");
    return 1;
}

static async Task<int> RunInspectAsync(string[] args)
{
    string? exe = null, outPath = null, processName = null;
    var timeout = 10;
    var settle = 8;
    string? emp = null, pwd = null;
    var afterLogin = 10;
    string? nav = null;
    var navWait = 8;
    string? scan = null;
    var scanWait = 8;
    string? qtyValue = null, pressKey = null;
    var postPress = 6;
    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--exe": exe = args[++i]; break;
            case "--process": processName = args[++i]; break;
            case "--out": outPath = args[++i]; break;
            case "--timeout": timeout = int.TryParse(args[++i], out var t) ? t : 10; break;
            case "--wait": settle = int.TryParse(args[++i], out var s) ? s : 8; break;
            case "--emp": emp = args[++i]; break;
            case "--pwd": pwd = args[++i]; break;
            case "--after": afterLogin = int.TryParse(args[++i], out var a) ? a : 10; break;
            case "--nav": nav = args[++i]; break;
            case "--navwait": navWait = int.TryParse(args[++i], out var nw) ? nw : 8; break;
            case "--scan": scan = args[++i]; break;
            case "--scanwait": scanWait = int.TryParse(args[++i], out var sw) ? sw : 8; break;
            case "--qty": qtyValue = args[++i]; break;
            case "--press": pressKey = args[++i]; break;
            case "--post": postPress = int.TryParse(args[++i], out var pp) ? pp : 6; break;
        }
    }
    if (string.IsNullOrWhiteSpace(exe) && string.IsNullOrWhiteSpace(processName))
    {
        Console.Error.WriteLine("Usage: Runner inspect --exe <path> [--process <name>] [--out <path>] [--timeout <sec>] [--wait <sec>] [--emp <id> --pwd <pwd>] [--nav <id>]");
        return 2;
    }
    Console.WriteLine($"[inspect] dumping UIA tree of {(exe ?? processName)} (timeout {timeout}s, settle {settle}s)...");
    var login = string.IsNullOrWhiteSpace(emp) ? null : new LoginCredentials(emp, pwd ?? "", afterLogin, nav, navWait, scan, scanWait, qtyValue, pressKey, postPress);
    var result = await UiInspector.InspectAsync(exe ?? "", processName, timeout, settle, login, CancellationToken.None);
    if (result is null)
    {
        Console.Error.WriteLine("[inspect] no main window found.");
        return 2;
    }
    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    var target = string.IsNullOrWhiteSpace(outPath) ? "uia-tree.json" : outPath;
    await File.WriteAllTextAsync(target, json);
    Console.WriteLine($"[inspect] captured {result.Nodes.Count} nodes -> {target}");
    var byType = result.Nodes.GroupBy(n => n.ControlType).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}={g.Count()}");
    Console.WriteLine("[inspect] control types: " + string.Join(", ", byType));
    var withId = result.Nodes.Count(n => n.AutomationId.Length > 0);
    Console.WriteLine($"[inspect] nodes with AutomationId: {withId}");
    return 0;
}

static async Task<int> RunVerifyAsync(string[] args)
{
    string? exe = null, processName = null;
    var timeout = 30;
    var settle = 8;
    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--exe": exe = args[++i]; break;
            case "--process": processName = args[++i]; break;
            case "--timeout": timeout = int.TryParse(args[++i], out var t) ? t : 30; break;
            case "--wait": settle = int.TryParse(args[++i], out var s) ? s : 8; break;
        }
    }
    if (string.IsNullOrWhiteSpace(exe) && string.IsNullOrWhiteSpace(processName))
    {
        Console.Error.WriteLine("Usage: Runner verify --exe <path> [--process <name>] [--timeout <sec>] [--wait <sec>]");
        return 2;
    }

    var config = AgentConfig.FromEnvironment();
    if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
    {
        Console.Error.WriteLine("Missing QAHUB_USERNAME / QAHUB_PASSWORD. ตั้งค่าก่อนรัน (ดู set-agent-env.ps1)");
        return 2;
    }

    using var client = new QaHubClient(config);
    if (!await client.LoginAsync(CancellationToken.None))
    {
        Console.Error.WriteLine($"Login ไป QA Hub ล้มเหลว ({config.HubBaseUrl}). ตรวจ Username/Password และสิทธิ์ AUTOMATION.EXECUTE");
        return 2;
    }
    await client.RegisterAsync(CancellationToken.None);

    var batch = await client.ClaimVerificationBatchAsync(CancellationToken.None);
    if (batch is null || batch.Items.Count == 0)
    {
        Console.WriteLine("[verify] ไม่มี Object รอตรวจสอบสำหรับ Agent นี้");
        return 0;
    }
    Console.WriteLine($"[verify] claimed {batch.Items.Count} object(s) to verify — scanning {(exe ?? processName)}...");

    var scan = await UiInspector.InspectAsync(exe ?? "", processName, timeout, settle, null, CancellationToken.None);
    var counts = new Dictionary<string, int>();
    foreach (var item in batch.Items)
    {
        var outcome = scan is null
            ? new VerifierOutcome("Error", null, null, "Could not find or launch the application's main window.")
            : ObjectVerifier.Verify(scan, item.ExpectedAutomationId, item.ExpectedControlType);
        counts[outcome.Status] = counts.GetValueOrDefault(outcome.Status) + 1;
        Console.WriteLine($"  {item.ScreenCode}.{item.ObjectCode} ({item.ExpectedAutomationId ?? "-"}) => {outcome.Status}{(outcome.Message is null ? "" : $" — {outcome.Message}")}");
        try { await client.ReportVerificationResultAsync(item.VerificationId, outcome.Status, outcome.ActualAutomationId, outcome.ActualControlType, outcome.Message, CancellationToken.None); }
        catch (Exception ex) { Console.Error.WriteLine($"  report FAILED for {item.ObjectCode}: {ex.Message}"); }
    }
    Console.WriteLine("[verify] summary: " + string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}")));
    return 0;
}

static async Task ExecutePackageAsync(QaHubClient client, JobPackage package, AgentConfig config, CancellationToken ct)
{
    var processName = Path.GetFileNameWithoutExtension(string.IsNullOrWhiteSpace(config.AutExe) ? "PromaxxsPos" : config.AutExe);
    using var driver = new FlaUiDriver(processName);
    var executor = new ActionExecutor(config, package.Objects);
    var log = new StringBuilder();
    log.AppendLine($"AUTOMATION LOG - {package.AutomationCode} (exec {package.AutomationExecutionId})");
    log.AppendLine($"Build: {package.BuildNumber} | DSL v{package.DslVersion} | Started: {DateTime.UtcNow:O}");
    void Log(string line) => log.AppendLine($"{DateTime.UtcNow:HH:mm:ss.fff}  {line}");

    try
    {
        var started = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(config.AutExe))
        {
            try { await driver.LaunchAsync(config.AutExe, null, TimeSpan.FromSeconds(30)); }
            catch { /* already running or launch failed */ }
            await driver.WaitForMainWindowAsync(processName, TimeSpan.FromSeconds(30));
        }

        var dsl = JsonSerializer.Deserialize<DslDocument>(package.DslJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (dsl?.Steps is null || dsl.Steps.Count == 0)
        {
            Log("DSL has no steps.");
            await client.CompleteAsync(package.AutomationExecutionId, "Failed", "AutomationFailure", "AUT-DSL-001", "DSL has no steps.", ct);
            await UploadLogAsync(client, package, log, ct);
            return;
        }

        var overall = true;
        StepOutcome? failedStep = null;
        foreach (var step in dsl.Steps.OrderBy(s => s.StepNo))
        {
            var outcome = await executor.ExecuteAsync(step, driver, ct);
            Log($"Step {step.StepNo} {step.Action} => {(outcome.Passed ? "PASS" : "FAIL")} ({(outcome.ErrorCode ?? "-")}) {(outcome.ErrorMessage is null ? "" : outcome.ErrorMessage)}");
            await client.ReportStepAsync(package.AutomationExecutionId, step.StepNo, step.Action, outcome.Passed ? "Pass" : "Fail", outcome.ActualResult, outcome.ErrorCode, outcome.ErrorMessage, outcome.StartedAt, outcome.CompletedAt, ct);
            foreach (var ev in outcome.Evidence ?? [])
            {
                try { await client.UploadGenericEvidenceAsync(package.AutomationExecutionId, step.StepNo, ev.EvidenceType, ev.FileName, ev.Data, ct); Log($"   evidence: {ev.EvidenceType} {ev.FileName}"); }
                catch (Exception ex) { Log($"   evidence FAILED: {ex.Message}"); }
            }
            if (!outcome.Passed)
            {
                overall = false;
                failedStep = outcome;
                foreach (var remaining in dsl.Steps.Where(s => s.StepNo > step.StepNo))
                {
                    var now = DateTime.UtcNow;
                    await client.ReportStepAsync(package.AutomationExecutionId, remaining.StepNo, remaining.Action, "Skipped", "Stopped after failure.", null, null, now, now, ct);
                }
                break;
            }
        }

        await driver.CloseAsync();
        var status = overall ? "Passed" : "Failed";
        // Forward the failed step's actual ErrorCode/ErrorMessage instead of a hardcoded "AUT-UI-003" — the server's
        // AutomationFailureClassifier branches on ErrorCode (AUT-DB-*/AUT-APP-*/AUT-AGENT-* etc. drive Retry vs
        // MaintenanceRequired vs QAReview), so a hardcoded generic code made every real failure look the same to it.
        var failureType = overall ? null : "AutomationFailure";
        var errorCode = overall ? null : (failedStep?.ErrorCode ?? "AUT-UI-003");
        var errorMessage = overall ? null : (failedStep?.ErrorMessage ?? "One or more automation steps failed.");
        log.AppendLine($"Result: {status} in {(DateTime.UtcNow - started).TotalSeconds:0.0}s");
        await client.CompleteAsync(package.AutomationExecutionId, status, failureType, errorCode, errorMessage, ct);
        await UploadLogAsync(client, package, log, ct);
        Console.WriteLine($"[job] {package.AutomationCode} => {status} ({(DateTime.UtcNow - started).TotalSeconds:0.0}s)");
    }
    catch (Exception ex)
    {
        Log($"FATAL: {ex.Message}");
        Console.Error.WriteLine($"[job] fatal: {ex.Message}");
        try
        {
            await client.CompleteAsync(package.AutomationExecutionId, "Failed", "AgentFailure", "AUT-AGENT-001", ex.Message, CancellationToken.None);
            await UploadLogAsync(client, package, log, CancellationToken.None);
        }
        catch { }
    }
}

static async Task UploadLogAsync(QaHubClient client, JobPackage package, StringBuilder log, CancellationToken ct)
{
    try
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(log.ToString());
        await client.UploadGenericEvidenceAsync(package.AutomationExecutionId, null, "AutomationLog", "automation.log", bytes, ct);
    }
    catch (Exception ex) { Console.Error.WriteLine($"[job] log upload failed: {ex.Message}"); }
}