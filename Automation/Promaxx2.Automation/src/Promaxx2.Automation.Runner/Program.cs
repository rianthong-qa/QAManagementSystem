using System.Diagnostics;
using Promaxx2.Automation.Core;
using Promaxx2.Automation.Hub;
using Promaxx2.Automation.Tests;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

using System.Reflection;
using System.Text;
using System.Text.Json;

// ---------------------------------------------------------------------------
// Promaxx2.Automation.Runner — CLI entry point
//
//   whoami                      ทดสอบ login QA Hub
//   export [options]              export Test Cases → testplan.json
//     --project <id|code>       Project GUID หรือ ProjectCode (บังคับ)
//     --status <Ready>          filter status (default: Ready)
//     --priority <P0>           filter priority เดียว เช่น P0 (optional)
//     --automation              เฉพาะ AutomationCandidate = true
//     --app <pos|app>           target app (required): pos=POS/sales, app=master data
//     --out <path>              ไฟล์ผลลัพธ์ (default: testplan.json)
//   run [options]               run smoke cases from testplan.json
//     --plan <path>             testplan.json (default: ./testplan.json)
//     --cases <codes>           run specific case codes (comma-separated, optional)
//     --target-app <pos|app>    target app default pos (optional)
//     --out <path>              ผลลัพธ์ JSON (default: run-results.json)
//   inspect [options]           dump UIA Automation tree ของ AUT สำหรับสร้าง AutomationId contract
//     --exe <path>              path ของ .exe ที่จะ dump (default: PromaxxsPos.exe หรือ Promaxxs.App.exe)
//     --out <path>              ผลลัพธ์ JSON tree (default: uia-tree.json)
//     --timeout <sec>           timeout รอ window เปิด (default: 10)
//
// Config ผ่าน env: QAHUB_BASE_URL, QAHUB_USERNAME, QAHUB_PASSWORD
// ---------------------------------------------------------------------------

var config = new AppConfig();

try
{
    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
        PrintUsage();
        return 0;
    }

    return args[0].ToLowerInvariant() switch
    {
        "whoami" => await RunWhoamiAsync(config),
        "export" => await RunExportAsync(config, args.Skip(1).ToArray()),
        "run" => await RunSmokeAsync(config, args.Skip(1).ToArray()),
        "inspect" => RunInspect(args.Skip(1).ToArray()),
        "scan" => await RunScannerAsync(config,args.Skip(1).ToArray()),
        "gate" => await RunQualityGateAsync(args.Skip(1).ToArray()),
        "worker" => await RunWorkerAsync(config,args.Skip(1).ToArray()),
        _ => Fail($"Unknown command \"{args[0]}\".")
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ERROR] {ex.Message}");
    return 1;
}

static async Task LoginAsync(QaHubClient client, AppConfig config)
{
    if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
        throw new InvalidOperationException("Set QAHUB_USERNAME and QAHUB_PASSWORD environment variables.");
    await client.LoginAsync(config.Username, config.Password);
}

static async Task<int> RunWhoamiAsync(AppConfig config)
{
    using var client = new QaHubClient(config);
    await LoginAsync(client, config);
    var u = client.User!;
    Console.WriteLine($"{u.DisplayName} ({u.Username}) · roles: {string.Join(", ", u.Roles)}");
    Console.WriteLine($"projects assigned: {u.AssignedProjectIds.Count}");
    return 0;
}

static async Task<int> RunExportAsync(AppConfig config, string[] args)
{
    using var client = new QaHubClient(config);
    await LoginAsync(client, config);
    string? project = null, status = "Ready", priority = null, app = null, cycle = null;
    string output = "testplan.json";
    bool automationOnly = false;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--project": project = Value(args, ref i); break;
            case "--status": status = Value(args, ref i); break;
            case "--priority": priority = Value(args, ref i); break;
            case "--automation": automationOnly = true; break;
            case "--app": app = Value(args, ref i); break;
            case "--cycle": cycle = Value(args, ref i); break;
            case "--out": output = Value(args, ref i); break;
            default: return Fail($"Unknown option \"{args[i]}\".");
        }
    }

    if (string.IsNullOrWhiteSpace(project)) return Fail("--project is required (GUID or ProjectCode).");
    if (string.IsNullOrWhiteSpace(app))
        return Fail("--app is required: use 'pos' for POS/sales or 'app' for master data.");
    TestPlanMapper.ValidateTargetApp(app);

    var proj = await client.ResolveProjectAsync(project);
    Console.WriteLine($"Project: {proj.ProjectCode} · {proj.ProjectName}");

    var source = await client.ExportCasesAsync(proj.ProjectId, status, priority, automationOnly);
    Console.WriteLine($"Exported {source.Cases.Count} test cases (status={status ?? "-"}, priority={priority ?? "-"}, automationOnly={automationOnly})");

    var plan = TestPlanMapper.ToTestPlan(source, targetApp: app,
        releaseCode: proj.ProjectCode, buildNumber: null);
    if(cycle is not null)
    {
        if(!Guid.TryParse(cycle,out var cycleId))return Fail("--cycle must be a Test Cycle GUID.");
        plan=plan with{TestCycleId=cycleId};
    }
    await TestPlanWriter.WriteAsync(plan, output);
    Console.WriteLine($"Written: {Path.GetFullPath(output)} ({plan.Cases.Count} cases)");
    return 0;

    static string Value(string[] a, ref int i) =>
        ++i < a.Length ? a[i] : throw new ArgumentException($"Missing value after {a[i - 1]}.");
}

static async Task<int> RunSmokeAsync(AppConfig config, string[] args,Action<Guid>? onPublished=null)
{
    // Parse: run [--plan <path>] [--cases codes] [--target-app pos|app] [--out path]
    string planPath = "./testplan.json";
    string? specificCodes = null;
    string? targetAppOverride = null;
    Guid? cycleOverride = null;
    Guid? releaseOverride = null,buildOverride = null;
    string output = "run-results.json";
    bool publish = true;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--plan": planPath = Value(args, ref i); break;
            case "--cases": specificCodes = Value(args, ref i); break;
            case "--target-app": targetAppOverride = Value(args, ref i); break;
            case "--cycle": cycleOverride = Guid.TryParse(Value(args, ref i),out var cycleId)?cycleId:throw new ArgumentException("--cycle must be a Test Cycle GUID."); break;
            case "--release": releaseOverride = Guid.TryParse(Value(args, ref i),out var releaseId)?releaseId:throw new ArgumentException("--release must be a Release GUID."); break;
            case "--build": buildOverride = Guid.TryParse(Value(args, ref i),out var buildId)?buildId:throw new ArgumentException("--build must be a Build GUID."); break;
            case "--out": output = Value(args, ref i); break;
            case "--no-publish": publish = false; break;
            default: return Fail($"Unknown option \"{args[i]}\".");
        }
    }

    if (targetAppOverride is not null)
        TestPlanMapper.ValidateTargetApp(targetAppOverride);

    // Load testplan
    var planJson = File.ReadAllText(planPath);
    var plan = JsonSerializer.Deserialize<TestPlan>(planJson, Json.ApiOptions)
        ?? throw new InvalidDataException("Invalid or empty test plan.");
    var cases = plan.Cases;

    // Build list of case codes to run
    var codesToRun = specificCodes != null
        ? specificCodes.Split(',').Select(c => c.Trim()).ToList()
        : null;

    var results = new List<CaseResult>();
    var implementations = DiscoverCases();

    var startedAt = DateTime.UtcNow;
    var executedCases = new List<TestPlanCase>();
    string? runTargetApp = null;
    foreach (var element in cases)
    {
        var code = element.TestCaseCode;
        
        // Filter: if specificCodes provided, only run those
        if (specificCodes != null && !codesToRun!.Contains(code))
            continue;

        // Resolve targetApp from plan if not specified
        var targetApp = targetAppOverride ?? element.TargetApp
            ?? throw new InvalidDataException($"{code} is missing required targetApp (pos|app).");
        TestPlanMapper.ValidateTargetApp(targetApp);
        runTargetApp ??= targetApp;
        if (!string.Equals(runTargetApp, targetApp, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("One run can contain only one targetApp. Export/run pos and app separately.");
        executedCases.Add(element);

        Console.WriteLine($"▶ Running {code} on {targetApp}...");

        try
        {
            var ctx = new CaseContext(config, targetApp, code);
            if (!implementations.TryGetValue(code, out var caseInstance))
            {
                Console.WriteLine($"  ⇅ {code}: SKIPPED (case class not implemented yet)");
                results.Add(new CaseResult(code, false, "Not implemented", TimeSpan.Zero, null));
                continue;
            }

            if (!string.Equals(caseInstance.TargetApp, targetApp, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Target app mismatch: plan={targetApp}, implementation={caseInstance.TargetApp}.");

            var result = await caseInstance.RunAsync(ctx);
            results.Add(result);
            Console.WriteLine($"  {(result.Passed ? "✓" : "✗")} {code} ({result.Duration:mm\\:ss}) {(string.IsNullOrWhiteSpace(result.ErrorMessage) ? "" : $"● {result.ErrorMessage}")}");

            if (result.ScreenshotPath != null)
                Console.WriteLine($"    Screenshot: {result.ScreenshotPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ {code} ไม่สามารถรันได้: {ex.Message}");
            results.Add(new CaseResult(code, false, ex.Message, TimeSpan.Zero, null));
        }
    }

    if (results.Count == 0)
        return Fail("No test cases matched the requested plan/filter.");

    // Write results
    var resultJson = JsonSerializer.Serialize(new
    {
        Plan = planJson,
        TargetApp = targetAppOverride,
        Cases = results.Select(r => new
        {
            r.TestCaseCode,
            r.Passed,
            r.ErrorMessage,
            r.Duration,
            r.ScreenshotPath
        })
    }, new JsonSerializerOptions { WriteIndented = true });

    var outDir = Path.GetDirectoryName(output);
    if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
    File.WriteAllText(output, resultJson);
    Console.WriteLine($"\nRun complete: {results.Count} cases (Passed: {results.Count(r => r.Passed)}/{results.Count})");
    Console.WriteLine($"Output: {Path.GetFullPath(output)}");

    if (publish)
    {
        if (plan.ProjectId is null || plan.ProjectId == Guid.Empty)
            throw new InvalidDataException("Publishing requires projectId in testplan schema 1.1. Re-export the plan or use --no-publish.");

        using var hub = new QaHubClient(config);
        await LoginAsync(hub, config);
        var request = new PublishAutomationRunRequest(
            plan.ProjectId.Value, releaseOverride, buildOverride, cycleOverride??plan.TestCycleId, runTargetApp!, Environment.MachineName, startedAt,
            results.Select((result, index) => new PublishAutomationCaseResult(
                executedCases[index].TestCaseId,
                result.TestCaseCode,
                result.Passed ? "Passed" : result.ErrorMessage == "Not implemented" ? "Skipped" : "Failed",
                Math.Max(0, (long)result.Duration.TotalMilliseconds),
                result.Passed ? null : result.ErrorMessage,
                result.ScreenshotPath)).ToList());
        var published = await hub.PublishAutomationRunAsync(request);
        Console.WriteLine($"Published to QA Hub: {published.AutomationRunId} ({published.Status})");
        onPublished?.Invoke(published.AutomationRunId);
        foreach(var result in results.Where(x=>!string.IsNullOrWhiteSpace(x.ScreenshotPath)&&File.Exists(x.ScreenshotPath)))
        {
            var remote=published.Results.Single(x=>x.TestCaseCode.Equals(result.TestCaseCode,StringComparison.OrdinalIgnoreCase));
            await hub.UploadAutomationEvidenceAsync(plan.ProjectId.Value,published.AutomationRunId,remote.AutomationRunCaseId,result.ScreenshotPath!);
            Console.WriteLine($"Evidence uploaded: {result.TestCaseCode}");
        }
    }

    return results.All(r => r.Passed) ? 0 : 1;
}

static async Task<int> RunWorkerAsync(AppConfig config,string[] args)
{
    Guid projectId=Guid.Empty;var once=false;var pollSeconds=10;var targets=new List<string>{"pos","app"};var workDir="queue-work";
    for(var i=0;i<args.Length;i++)switch(args[i]){case "--project":projectId=Guid.TryParse(Value(args,ref i),out var id)?id:throw new ArgumentException("--project must be a GUID.");break;case "--once":once=true;break;case "--poll":pollSeconds=Math.Clamp(int.Parse(Value(args,ref i)),2,300);break;case "--targets":targets=Value(args,ref i).Split(',').Select(x=>x.Trim().ToLowerInvariant()).ToList();break;case "--work-dir":workDir=Value(args,ref i);break;default:return Fail($"Unknown option \"{args[i]}\".");}
    if(projectId==Guid.Empty)return Fail("--project is required for worker.");foreach(var target in targets)TestPlanMapper.ValidateTargetApp(target);
    using var hub=new QaHubClient(config);await LoginAsync(hub,config);Directory.CreateDirectory(workDir);var runnerName=Environment.MachineName;var runnerVersion=typeof(Program).Assembly.GetName().Version?.ToString()??"dev";
    do
    {
        await hub.HeartbeatRunnerAsync(projectId,runnerName,runnerVersion,targets);var job=await hub.ClaimQueueJobAsync(projectId,runnerName,targets);if(job is null){if(once){Console.WriteLine("No queued automation job.");return 0;}await Task.Delay(TimeSpan.FromSeconds(pollSeconds));continue;}
        Console.WriteLine($"Claimed {job.AutomationQueueJobId} ({job.TargetApp})");
        using var heartbeatStop=new CancellationTokenSource();var heartbeat=Task.Run(async()=>{while(!heartbeatStop.IsCancellationRequested){try{await Task.Delay(TimeSpan.FromSeconds(20),heartbeatStop.Token);await hub.HeartbeatRunnerAsync(projectId,runnerName,runnerVersion,targets,job,heartbeatStop.Token);}catch(OperationCanceledException)when(heartbeatStop.IsCancellationRequested){break;}catch(Exception ex){Console.Error.WriteLine($"[HEARTBEAT WARNING] {ex.Message}");}}});
        try
        {
            await hub.UpdateQueueJobAsync(job,"Running");var source=await hub.ExportCasesAsync(job.ProjectId,"Ready",null,true);source=new TestPlanSource(source.Cases.Where(x=>x.AutomationTarget==job.TargetApp).ToList(),source.Modules);if(source.Cases.Count==0)throw new InvalidOperationException($"No Ready automation cases routed to {job.TargetApp}.");
            var plan=TestPlanMapper.ToTestPlan(source,job.TargetApp) with{TestCycleId=job.TestCycleId};var jobDir=Path.Combine(workDir,job.AutomationQueueJobId.ToString("N"));Directory.CreateDirectory(jobDir);var planPath=Path.Combine(jobDir,"testplan.json");var resultPath=Path.Combine(jobDir,"run-results.json");await TestPlanWriter.WriteAsync(plan,planPath);Guid? runId=null;
            var runArgs=new List<string>{"--plan",planPath,"--target-app",job.TargetApp,"--release",job.ReleaseId.ToString(),"--build",job.BuildId.ToString(),"--out",resultPath};if(job.TestCycleId.HasValue){runArgs.Add("--cycle");runArgs.Add(job.TestCycleId.Value.ToString());}var exitCode=await RunSmokeAsync(config,runArgs.ToArray(),id=>runId=id);await hub.UpdateQueueJobAsync(job,exitCode==0?"Completed":"Failed",exitCode==0?null:$"Automation exited with code {exitCode}.",runId,exitCode==0?null:"Assertion");heartbeatStop.Cancel();await heartbeat;if(once)return exitCode;
        }
        catch(Exception ex){heartbeatStop.Cancel();await heartbeat;var errorType=ex is TimeoutException?"Timeout":ex is HttpRequestException?"Infrastructure":ex.Message.Contains("start",StringComparison.OrdinalIgnoreCase)||ex.Message.Contains("launch",StringComparison.OrdinalIgnoreCase)?"ApplicationStart":"Configuration";try{await hub.UpdateQueueJobAsync(job,"Failed",ex.Message,errorType:errorType);}catch(Exception updateEx){Console.Error.WriteLine($"[QUEUE STATUS WARNING] {updateEx.Message}");}Console.Error.WriteLine($"[QUEUE ERROR] {ex.Message}");if(once)return 1;}
    }while(true);
}

static IReadOnlyDictionary<string, IAutomationCase> DiscoverCases()
{
    var cases = typeof(IAutomationCase).Assembly
        .GetTypes()
        .Where(t => !t.IsAbstract && typeof(IAutomationCase).IsAssignableFrom(t))
        .Select(t => (IAutomationCase?)Activator.CreateInstance(t))
        .Where(x => x is not null)
        .Cast<IAutomationCase>()
        .ToList();

    var duplicate = cases
        .GroupBy(x => x.TestCaseCode, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault(g => g.Count() > 1);
    if (duplicate is not null)
        throw new InvalidOperationException($"Duplicate automation implementation: {duplicate.Key}.");

    return cases.ToDictionary(x => x.TestCaseCode, StringComparer.OrdinalIgnoreCase);
}

static async Task<int> RunScannerAsync(AppConfig config,string[] args)
{
    string? manifestPath=null,baselinePath=null,buildOverride=null;string output="automation-id-report.json",registry="automation-id-registry.md";
    for(int i=0;i<args.Length;i++)
    {
        switch(args[i])
        {
            case "--manifest":manifestPath=Value(args,ref i);break;
            case "--out":output=Value(args,ref i);break;
            case "--baseline":baselinePath=Value(args,ref i);break;
            case "--registry":registry=Value(args,ref i);break;
            case "--build":buildOverride=Value(args,ref i);break;
            default:return Fail($"Unknown option \"{args[i]}\".");
        }
    }
    if(string.IsNullOrWhiteSpace(manifestPath))return Fail("--manifest is required.");
    var scanner=new AutomationIdScanner(config);var manifest=await AutomationIdScanner.ReadManifestAsync(manifestPath);if(!string.IsNullOrWhiteSpace(buildOverride))manifest=manifest with{Build=buildOverride};var report=await scanner.ScanAsync(manifest,baselinePath);await AutomationIdScanner.WriteReportAsync(report,output);await AutomationIdScanner.WriteRegistryAsync(report,registry);
    var missing=report.Screens.Sum(x=>x.MissingAutomationIds.Count);var duplicates=report.Screens.Sum(x=>x.DuplicateAutomationIds.Count);
    Console.WriteLine($"Scanned {report.Screens.Count} screens / {report.Screens.Sum(x=>x.Elements.Count)} elements.");
    Console.WriteLine($"Missing actionable AutomationId: {missing}; duplicate IDs: {duplicates}");
    if(report.Diff is not null)Console.WriteLine($"Baseline diff: +{report.Diff.Added.Count} -{report.Diff.Removed.Count} ~{report.Diff.Changed.Count}");
    Console.WriteLine($"Report: {Path.GetFullPath(output)}");
    Console.WriteLine($"Registry: {Path.GetFullPath(registry)}");
    return missing==0&&duplicates==0&&(report.Diff?.Removed.Count??0)==0?0:2;
}

static async Task<int> RunQualityGateAsync(string[] args)
{
    string? baseline=null,current=null,policy=null;string output="automation-id-gate.json",junit="automation-id-gate.junit.xml";
    for(int i=0;i<args.Length;i++)
    {
        switch(args[i])
        {
            case "--baseline":baseline=Value(args,ref i);break;
            case "--current":current=Value(args,ref i);break;
            case "--policy":policy=Value(args,ref i);break;
            case "--out":output=Value(args,ref i);break;
            case "--junit":junit=Value(args,ref i);break;
            default:return Fail($"Unknown option \"{args[i]}\".");
        }
    }
    if(string.IsNullOrWhiteSpace(baseline)||string.IsNullOrWhiteSpace(current)||string.IsNullOrWhiteSpace(policy))return Fail("--baseline, --current and --policy are required.");
    var result=await AutomationIdQualityGate.EvaluateAsync(baseline,current,policy);await AutomationIdQualityGate.WriteJsonAsync(result,output);await AutomationIdQualityGate.WriteJUnitAsync(result,junit);
    Console.WriteLine($"AutomationId gate: {(result.Passed?"PASSED":"FAILED")} ({result.TargetApp} {result.BaselineBuild} -> {result.CurrentBuild})");foreach(var message in result.Messages)Console.WriteLine($"- {message}");Console.WriteLine($"JSON: {Path.GetFullPath(output)}");Console.WriteLine($"JUnit: {Path.GetFullPath(junit)}");return result.Passed?0:3;
}

static int RunInspect(string[] args)
{
    // inspect [--exe path] [--out path] [--timeout sec]
    string exePath = null!;
    string output = "uia-tree.json";
    int timeoutSec = 10;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--exe": exePath = Value(args, ref i); break;
            case "--out": output = Value(args, ref i); break;
            case "--timeout": timeoutSec = int.Parse(Value(args, ref i)); break;
            default: return Fail($"Unknown option \"{args[i]}\".");
        }
    }

    // Resolve exe path: default to PromaxxsPos.exe หรือ Promaxxs.App.exe ในเดียวกับ env
    if (string.IsNullOrWhiteSpace(exePath))
    {
        var pos = Environment.GetEnvironmentVariable("AUT_POS_EXE");
        var app = Environment.GetEnvironmentVariable("AUT_APP_EXE");
        exePath = pos ?? app ?? "PromaxxsPos.exe";
    }

    if (!File.Exists(exePath))
        throw new FileNotFoundException($"AUT executable not found: {exePath}");

    Console.WriteLine($"▶ Dumping UIA tree from {exePath} (timeout {timeoutSec}s)...");

    var psi = new ProcessStartInfo(exePath)
    {
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var proc = Process.Start(psi)!;

    using var automation = new UIA3Automation();
    using var attachedApp = Application.Attach(proc.Id);
    List<Dictionary<string, string>>? elements = null;
    var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);

    // Splash screen becomes idle before the actual login window is ready. Poll all
    // top-level windows and retain the richest tree until LoadingText disappears.
    while (DateTime.UtcNow < deadline && !proc.HasExited)
    {
        try
        {
            var candidates = attachedApp.GetAllTopLevelWindows(automation)
                .Select(Snapshot)
                .OrderByDescending(x => x.Count)
                .ToList();
            var best = candidates.FirstOrDefault();
            if (best is not null && (elements is null || best.Count >= elements.Count))
                elements = best;

            if (best is not null && best.Count > 1 && best.All(x => x["Id"] != "LoadingText"))
                break;
        }
        catch
        {
            // The window can be recreated while transitioning away from the splash.
        }

        Thread.Sleep(250);
    }

    if (elements is null)
    {
        Console.WriteLine("⚠ ไม่พบ window ของ AUT ภายใน timeout");
        if (!proc.HasExited) proc.Kill(true);
        return 1;
    }

    static List<Dictionary<string, string>> Snapshot(Window window) =>
        window.FindAllDescendants()
            .Prepend(window)
            .Select(el => new Dictionary<string, string>
            {
                ["Id"] = el.AutomationId ?? "",
                ["Name"] = el.Name ?? "",
                ["Class"] = el.ClassName ?? "",
                ["ControlType"] = el.ControlType.ToString()
            })
            .ToList();

    var json = new
    {
        Executable = exePath,
        TimeoutSec = timeoutSec,
        ElementCount = elements.Count,
        Elements = elements
    };

    var dir = Path.GetDirectoryName(output);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    File.WriteAllText(output, JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"✓ Dumped {elements.Count} elements to {Path.GetFullPath(output)}");
    if (!proc.HasExited) proc.Kill(true);
    return 0;
}

static string Value(string[] a, ref int i) =>
    ++i < a.Length ? a[i] : throw new ArgumentException($"Missing value after {a[i - 1]}.");

static int Fail(string message)
{
    Console.Error.WriteLine($"[ERROR] {message}");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        Promaxx2.Automation.Runner
          whoami                          verify QA Hub login
          export [options]                export Test Cases → testplan.json
            --app <pos|app>               required: pos=POS/sales, app=master data
            --cycle <guid>                ผูก Test Plan กับ Test Cycle (optional)
          run [options]                   run smoke cases from testplan.json
            --plan <path>                 testplan.json (default: ./testplan.json)
            --cases <codes>               run specific case codes (comma-separated)
            --target-app <pos|app>        explicitly override target app (optional)
            --cycle <guid>                ผูกผลรันเข้ากับ Test Cycle (optional)
            --out <path>                  ผลลัพธ์ JSON (default: run-results.json)
            --no-publish                  เก็บผลไว้ในเครื่องโดยไม่ส่งเข้า QA Hub
          inspect [options]               dump UIA Automation tree ของ AUT
            --exe <path>                  path ของ .exe (default: env AUT_POS_EXE)
            --out <path>                  ผลลัพธ์ JSON tree (default: uia-tree.json)
            --timeout <sec>               timeout รอ window เปิด (default: 10)
          scan [options]                  login, navigate และตรวจ AutomationId ทุก screen ตาม manifest
            --manifest <path>             navigation manifest JSON (required)
            --out <path>                  scanner report JSON
            --baseline <path>             report จาก build ก่อนหน้าสำหรับ diff
            --registry <path>             Markdown registry ที่สร้างจาก runtime
            --build <version>             override build version จาก manifest
          gate [options]                  เปรียบเทียบ scanner report กับ approved baseline
            --baseline <path>             approved baseline report (required)
            --current <path>              report ของ build ที่ตรวจ (required)
            --policy <path>               quality-gate policy JSON (required)
            --out <path>                  gate result JSON
            --junit <path>                JUnit XML สำหรับ CI test report
          worker [options]                poll และรันงานจาก QA Hub Automation Queue
            --project <guid>              Project ที่ Runner รับงาน (required)
            --targets <pos,app>           target ที่ Runner รองรับ (default: pos,app)
            --poll <seconds>              ช่วงเวลารอเมื่อไม่มีงาน (default: 10)
            --once                        รับงานครั้งเดียวแล้วจบ
            --work-dir <path>             ที่เก็บ plan/result ของแต่ละ job
        env: QAHUB_BASE_URL, QAHUB_USERNAME, QAHUB_PASSWORD
        """);
}
