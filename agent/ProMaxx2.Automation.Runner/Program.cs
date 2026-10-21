using System.Text;
using System.Text.Json;
using ProMaxx2.Automation.Core;
using ProMaxx2.Automation.Hub;

if (args is { Length: > 0 } && args[0].Equals("inspect", StringComparison.OrdinalIgnoreCase))
{
    return await RunInspectAsync(args);
}

if (args is { Length: > 0 } && args[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
{
    return await RunVerifyAsync(args);
}

if (args is { Length: > 0 } && args[0].Equals("snapshot", StringComparison.OrdinalIgnoreCase))
{
    return await RunSnapshotAsync();
}

if (args is { Length: > 0 } && args[0].Equals("restore", StringComparison.OrdinalIgnoreCase))
{
    return await RunRestoreAsync();
}

if (args is { Length: > 0 } && args[0].Equals("seed", StringComparison.OrdinalIgnoreCase))
{
    return await RunSeedAsync();
}

AgentConfig config;
try { config = AgentConfig.FromEnvironment(); }
catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return 2; }

if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
{
    Console.Error.WriteLine("Missing QAHUB_USERNAME / QAHUB_PASSWORD. ตั้งค่าก่อนรัน (ดู set-agent-env.ps1)");
    return 2;
}
if (HubUrlPolicy.Validate(config.HubBaseUrl, config.AllowInsecureHttp) is { } urlError)
{
    Console.Error.WriteLine(urlError);
    return 2;
}

using var client = new QaHubClient(config);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// AUT-AGT-001: Hub อาจยังไม่พร้อมตอน Agent เริ่ม (เช่น เปิดเครื่องพร้อมกัน/ Hub restart) — รอแล้วลองใหม่แทนการปิดตัว;
// ปิดตัวเฉพาะเมื่อ Hub ปฏิเสธ credential จริง ๆ
var retryDelay = TimeSpan.FromSeconds(5);
while (true)
{
    try
    {
        if (!await client.LoginAsync(cts.Token))
        {
            Console.Error.WriteLine($"Login ไป QA Hub ล้มเหลว ({config.HubBaseUrl}). ตรวจ Username/Password และสิทธิ์ AUTOMATION.EXECUTE");
            return 2;
        }
        await client.RegisterAsync(cts.Token);
        break;
    }
    catch (OperationCanceledException) { Console.WriteLine("[agent] stopped."); return 0; }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[agent] QA Hub ยังติดต่อไม่ได้ ({ex.Message}) — ลองใหม่ใน {retryDelay.TotalSeconds:0} วินาที");
        try { await Task.Delay(retryDelay, cts.Token); } catch (OperationCanceledException) { Console.WriteLine("[agent] stopped."); return 0; }
        retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 60));
    }
}
Console.WriteLine($"[agent] Logged in to {config.HubBaseUrl} as {config.Username}");
Console.WriteLine($"[agent] Registered agent '{config.AgentCode}' v{config.AgentVersion}");

var waitingForDesktop = false;
while (!cts.IsCancellationRequested)
{
    UiSessionLock? desktop = null;
    try
    {
        await client.HeartbeatAsync("Idle", null, cts.Token);
        // AUT-AGT-003: Runner ตัวอื่นบนเครื่องนี้ (เช่น POS กับ App ที่ GUI เปิดคู่กัน) กำลังใช้หน้าจอ — ยังไม่ claim งาน
        // เพื่อไม่ให้งานที่รับมาแล้วต้องนั่งรอ และไม่ให้สองตัวกด mouse/keyboard แย่งกัน
        desktop = UiSessionLock.TryAcquire();
        if (desktop is null)
        {
            if (!waitingForDesktop) Console.WriteLine("[agent] Runner อีกตัวบนเครื่องนี้กำลังใช้หน้าจอ — รอจนเสร็จก่อนรับงาน");
            waitingForDesktop = true;
            await Task.Delay(TimeSpan.FromSeconds(config.HeartbeatSeconds), cts.Token);
            continue;
        }
        waitingForDesktop = false;
        var package = await client.ClaimJobAsync(cts.Token);
        if (package is null)
        {
            desktop.Dispose();
            desktop = null;
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
        // Ctrl+C ระหว่างรอต้องจบโปรแกรมอย่างปกติ — เดิม OperationCanceledException จาก Task.Delay ใน catch หลุดออกไปทำให้ crash
        try { await Task.Delay(TimeSpan.FromSeconds(config.HeartbeatSeconds), cts.Token); }
        catch (OperationCanceledException) { break; }
    }
    finally
    {
        desktop?.Dispose();
    }
}

Console.WriteLine("[agent] stopped.");
return 0;

static async Task<int> RunInspectAsync(string[] args)
{
    // AUT-AGT-003: ใช้หน้าจอเหมือน job — ห้ามชนกับ Runner ที่กำลังรันงานอยู่บนเครื่องเดียวกัน
    using var desktop = UiSessionLock.TryAcquire();
    if (desktop is null) { Console.Error.WriteLine("Runner อีกตัวบนเครื่องนี้กำลังใช้หน้าจออยู่ — รอให้เสร็จหรือหยุด Agent ก่อน"); return 2; }
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
        Console.Error.WriteLine("Usage: Runner inspect --exe <path> [--process <name>] [--out <path>] [--timeout <sec>] [--wait <sec>] [--emp <id>] [--nav <id>]  (รหัสผ่านอ่านจาก AUT_PASSWORD)");
        return 2;
    }
    // AUT-AGT-004: รหัสผ่านบน command line (--pwd) อ่านได้จาก Task Manager/ประวัติ shell — ใช้ AUT_PASSWORD(_DPAPI) แทนเมื่อไม่ระบุ
    if (pwd is not null) Console.Error.WriteLine("[inspect] คำเตือน: --pwd ทำให้รหัสผ่านอยู่บน command line — แนะนำตั้ง AUT_PASSWORD ด้วย set-agent-env.ps1 แล้วไม่ต้องใส่ --pwd");
    else if (!string.IsNullOrWhiteSpace(emp)) pwd = AgentSecrets.Read("AUT_PASSWORD", Environment.GetEnvironmentVariable);
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
    // AUT-AGT-003: ใช้หน้าจอเหมือน job — ห้ามชนกับ Runner ที่กำลังรันงานอยู่บนเครื่องเดียวกัน
    using var desktop = UiSessionLock.TryAcquire();
    if (desktop is null) { Console.Error.WriteLine("Runner อีกตัวบนเครื่องนี้กำลังใช้หน้าจออยู่ — รอให้เสร็จหรือหยุด Agent ก่อน"); return 2; }
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

/// <summary>AUT-DATA-001: a standalone, single-invocation command (same shape as `runner verify`, not part of the
/// main job-polling loop) — a DB backup is heavy/disruptive enough that it should run when explicitly asked for
/// (manually, or from a CI/scheduling step right before a test run), not silently in the background between jobs.
/// Drains the queue: claims and runs snapshot requests one at a time until none are left, since more than one can
/// legitimately pile up between invocations.</summary>
static async Task<int> RunSnapshotAsync()
{
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

    var profile = DbProfile.FromEnvironment(config);
    IDbSnapshotService snapshotService = new DatabaseSnapshotService(config.GbakPath);
    var processed = 0;
    var failed = 0;
    while (true)
    {
        var package = await client.ClaimSnapshotAsync(CancellationToken.None);
        if (package is null) break;
        processed++;
        Console.WriteLine($"[snapshot] claimed {package.AutomationDbSnapshotId} — {package.EnvironmentName} / build {package.BuildNumber} ({profile.Kind})");
        var fileNameHint = $"{package.EnvironmentName}_{package.BuildNumber}";
        var result = await snapshotService.CreateSnapshotAsync(profile, config.SnapshotDirectory, fileNameHint, CancellationToken.None);
        try
        {
            if (result.Success)
            {
                Console.WriteLine($"[snapshot] {package.AutomationDbSnapshotId} => Succeeded ({result.FilePath}, {result.SizeBytes} bytes, {result.ElapsedMs}ms)");
                await client.CompleteSnapshotAsync(package.AutomationDbSnapshotId, "Succeeded", profile.Kind.ToString(), result.FilePath, result.Checksum, result.SizeBytes, null, CancellationToken.None);
            }
            else
            {
                failed++;
                Console.Error.WriteLine($"[snapshot] {package.AutomationDbSnapshotId} => Failed: {result.Error}");
                await client.CompleteSnapshotAsync(package.AutomationDbSnapshotId, "Failed", profile.Kind.ToString(), null, null, null, result.Error, CancellationToken.None);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[snapshot] report FAILED for {package.AutomationDbSnapshotId}: {ex.Message}"); }
    }

    if (processed == 0) { Console.WriteLine("[snapshot] ไม่มี Snapshot request รอดำเนินการสำหรับ Agent นี้"); return 0; }
    Console.WriteLine($"[snapshot] summary: processed={processed}, failed={failed}");
    return failed > 0 ? 1 : 0;
}

/// <summary>AUT-DATA-002: standalone command, same shape/rationale as `runner snapshot` — a restore overwrites the
/// database, so it should only ever run when explicitly asked for (e.g. a pipeline step right before a retry), never
/// silently in the background. Drains the queue the same way `runner snapshot` does.</summary>
static async Task<int> RunRestoreAsync()
{
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

    var profile = DbProfile.FromEnvironment(config);
    IDbSnapshotService snapshotService = new DatabaseSnapshotService(config.GbakPath);
    var processed = 0;
    var failed = 0;
    while (true)
    {
        var package = await client.ClaimRestoreAsync(CancellationToken.None);
        if (package is null) break;
        processed++;
        Console.WriteLine($"[restore] claimed {package.AutomationDbRestoreId} <- snapshot {package.AutomationDbSnapshotId} ({package.SnapshotPath})");
        var result = await snapshotService.RestoreSnapshotAsync(profile, package.SnapshotPath, package.ExpectedChecksum, CancellationToken.None);
        try
        {
            if (result.Success)
            {
                Console.WriteLine($"[restore] {package.AutomationDbRestoreId} => Succeeded (checksum ok, DB available, {result.ElapsedMs}ms)");
                await client.CompleteRestoreAsync(package.AutomationDbRestoreId, "Succeeded", result.ChecksumVerified, result.AvailabilityVerified, null, CancellationToken.None);
            }
            else
            {
                failed++;
                Console.Error.WriteLine($"[restore] {package.AutomationDbRestoreId} => Failed: {result.Error}");
                await client.CompleteRestoreAsync(package.AutomationDbRestoreId, "Failed", result.ChecksumVerified, result.AvailabilityVerified, result.Error, CancellationToken.None);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[restore] report FAILED for {package.AutomationDbRestoreId}: {ex.Message}"); }
    }

    if (processed == 0) { Console.WriteLine("[restore] ไม่มี Restore request รอดำเนินการสำหรับ Agent นี้"); return 0; }
    Console.WriteLine($"[restore] summary: processed={processed}, failed={failed}");
    return failed > 0 ? 1 : 0;
}

/// <summary>AUT-DATA-003/AUT-DATA-004: standalone command, same shape as `runner snapshot`/`runner restore` — run
/// right before a suite that depends on known baseline data (Seed scripts), or after one to tear it back down
/// (Cleanup scripts). One command drains both queues since the Hub's "seed-runs" claim endpoint hands out either
/// kind identically — the only difference between a Seed and a Cleanup script is what its SQL does, not how it's
/// run. Fails fast on a DB-kind mismatch between the script and this agent's own DbProfile, rather than attempting
/// to run the wrong dialect's SQL.</summary>
static async Task<int> RunSeedAsync()
{
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

    var profile = DbProfile.FromEnvironment(config);
    IDbSeedService seedService = new DatabaseSeedService();
    var processed = 0;
    var failed = 0;
    while (true)
    {
        var package = await client.ClaimSeedRunAsync(CancellationToken.None);
        if (package is null) break;
        processed++;
        Console.WriteLine($"[seed] claimed {package.AutomationDataSeedRunId} — script '{package.ScriptName}' ({package.DbKind})");

        if (!string.Equals(package.DbKind, profile.Kind.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            failed++;
            var mismatch = $"Script is written for {package.DbKind} but this agent's DB profile is {profile.Kind} — refusing to run the wrong SQL dialect.";
            Console.Error.WriteLine($"[seed] {package.AutomationDataSeedRunId} => Failed: {mismatch}");
            try { await client.CompleteSeedRunAsync(package.AutomationDataSeedRunId, "Failed", null, mismatch, CancellationToken.None); }
            catch (Exception ex) { Console.Error.WriteLine($"[seed] report FAILED for {package.AutomationDataSeedRunId}: {ex.Message}"); }
            continue;
        }

        var result = await seedService.RunSeedScriptAsync(profile, package.SqlScript, CancellationToken.None);
        try
        {
            if (result.Success)
            {
                Console.WriteLine($"[seed] {package.AutomationDataSeedRunId} => Succeeded ({result.RowsAffected} rows affected, {result.ElapsedMs}ms)");
                await client.CompleteSeedRunAsync(package.AutomationDataSeedRunId, "Succeeded", result.RowsAffected, null, CancellationToken.None);
            }
            else
            {
                failed++;
                Console.Error.WriteLine($"[seed] {package.AutomationDataSeedRunId} => Failed: {result.Error}");
                await client.CompleteSeedRunAsync(package.AutomationDataSeedRunId, "Failed", null, result.Error, CancellationToken.None);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[seed] report FAILED for {package.AutomationDataSeedRunId}: {ex.Message}"); }
    }

    if (processed == 0) { Console.WriteLine("[seed] ไม่มี Seed/Cleanup run รอดำเนินการสำหรับ Agent นี้"); return 0; }
    Console.WriteLine($"[seed] summary: processed={processed}, failed={failed}");
    return failed > 0 ? 1 : 0;
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
    var completionSent = false;

    // AUT-AGT-001: ส่ง heartbeat "Busy" พร้อม execution ที่กำลังรันเป็นระยะระหว่างทำงาน — Hub มองว่า Agent Offline เมื่อ
    // ไม่ได้ heartbeat เกิน 60 วินาที เดิมส่งแค่ตอนว่างก่อน claim ทำให้งานยาวเห็น Agent เป็น Offline ทั้งที่ยังรันอยู่
    using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var heartbeatTask = Task.Run(async () =>
    {
        while (!heartbeatCts.IsCancellationRequested)
        {
            try { await client.HeartbeatAsync("Busy", package.AutomationExecutionId, heartbeatCts.Token); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.Error.WriteLine($"[job] heartbeat failed: {ex.Message}"); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, config.HeartbeatSeconds)), heartbeatCts.Token); }
            catch (OperationCanceledException) { break; }
        }
    });

    // AUT-AGT-003: เปิด AUT ไม่ได้/ไม่มีหน้าต่างหลัก ต้องรายงานเป็น EnvironmentFailure (AUT-APP-*) ทันที — เดิมกลืน error
    // แล้วรัน step ต่อจนได้ ObjectNotFound (AUT-UI-001) ซึ่งทำให้ Case ถูกส่งเข้า MaintenanceRequired ผิดสาเหตุ
    async Task FailEnvironmentAsync(string code, string message)
    {
        Log($"{code}: {message}");
        Console.Error.WriteLine($"[job] {package.AutomationCode} => {code}: {message}");
        completionSent = true;
        await client.CompleteAsync(package.AutomationExecutionId, "Failed", "EnvironmentFailure", code, message, ct);
        await UploadLogAsync(client, package, log, ct);
    }

    try
    {
        var started = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(config.AutExe))
        {
            if (!File.Exists(config.AutExe)) { await FailEnvironmentAsync("AUT-APP-001", $"ไม่พบไฟล์ AUT: {config.AutExe}"); return; }
            var leftovers = AutProcess.FindInstances(config.AutExe);
            var leftoverCount = leftovers.Count;
            foreach (var p in leftovers) p.Dispose();
            if (leftoverCount > 0)
            {
                if (!config.CloseExistingAut) { await FailEnvironmentAsync("AUT-APP-001", $"มี {Path.GetFileName(config.AutExe)} เปิดอยู่แล้ว {leftoverCount} หน้าต่าง และตั้ง AUT_CLOSE_EXISTING=false ไว้ — ปิดโปรแกรมก่อนแล้วรันใหม่"); return; }
                var closed = AutProcess.CloseInstances(config.AutExe, TimeSpan.FromSeconds(10));
                Log($"ปิด {Path.GetFileName(config.AutExe)} ที่ค้างอยู่ {closed}/{leftoverCount} instance ก่อนเริ่มงาน");
            }
            try { await driver.LaunchAsync(config.AutExe, null, TimeSpan.FromSeconds(30)); }
            catch (Exception ex) { await FailEnvironmentAsync("AUT-APP-001", $"เปิด AUT ไม่สำเร็จ: {ex.Message}"); return; }
            if (!await driver.WaitForMainWindowAsync(processName, TimeSpan.FromSeconds(30)))
            {
                await FailEnvironmentAsync("AUT-APP-002", "เปิด AUT แล้วแต่ไม่พบหน้าต่างหลักภายใน 30 วินาที");
                return;
            }
        }

        var dsl = JsonSerializer.Deserialize<DslDocument>(package.DslJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (dsl?.Steps is null || dsl.Steps.Count == 0)
        {
            Log("DSL has no steps.");
            completionSent = true;
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

        await driver.CloseAsync(); // ปิดก่อนรายงานผล — finally จะปิดซ้ำแบบ no-op
        var status = overall ? "Passed" : "Failed";
        // Forward the failed step's actual ErrorCode/ErrorMessage instead of a hardcoded "AUT-UI-003" — the server's
        // AutomationFailureClassifier branches on ErrorCode (AUT-DB-*/AUT-APP-*/AUT-AGENT-* etc. drive Retry vs
        // MaintenanceRequired vs QAReview), so a hardcoded generic code made every real failure look the same to it.
        var failureType = overall ? null : "AutomationFailure";
        var errorCode = overall ? null : (failedStep?.ErrorCode ?? "AUT-UI-003");
        var errorMessage = overall ? null : (failedStep?.ErrorMessage ?? "One or more automation steps failed.");
        log.AppendLine($"Result: {status} in {(DateTime.UtcNow - started).TotalSeconds:0.0}s");
        completionSent = true;
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
            // ถ้าส่งผลสรุปไปแล้วแต่ขั้นหลังจากนั้นล้ม (เช่นอัปโหลด log) ห้ามส่ง AgentFailure ทับผลจริงอีกรอบ
            if (!completionSent) await client.CompleteAsync(package.AutomationExecutionId, "Failed", "AgentFailure", "AUT-AGENT-001", ex.Message, CancellationToken.None);
            await UploadLogAsync(client, package, log, CancellationToken.None);
        }
        catch { }
    }
    finally
    {
        // AUT-AGT-003: ปิด AUT ทุกเส้นทาง (Fail/exception/ยกเลิก) — เดิมปิดเฉพาะเส้นทางปกติ ทำให้หน้าจอค้างไปถึงงานถัดไป
        try { await driver.CloseAsync(); } catch (Exception ex) { Console.Error.WriteLine($"[job] close AUT failed: {ex.Message}"); }
        heartbeatCts.Cancel();
        try { await heartbeatTask; } catch { }
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