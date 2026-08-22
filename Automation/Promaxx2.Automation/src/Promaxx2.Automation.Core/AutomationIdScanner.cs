using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace Promaxx2.Automation.Core;

public sealed record ScannerManifest(string TargetApp,string Build,IReadOnlyList<ScannerScreen> Screens,bool Login=true);
public sealed record ScannerScreen(string Name,IReadOnlyList<ScannerNavigationStep> Navigation);
public sealed record ScannerNavigationStep(string AutomationId,string Action="click",string? WaitForId=null,int TimeoutSeconds=15);
public sealed record ScannedElement(string AutomationId,string Name,string ClassName,string ControlType,string Path);
public sealed record ScannedScreen(string Name,IReadOnlyList<ScannedElement> Elements,IReadOnlyList<string> MissingAutomationIds,IReadOnlyList<string> DuplicateAutomationIds);
public sealed record ScannerDiff(IReadOnlyList<string> Added,IReadOnlyList<string> Removed,IReadOnlyList<string> Changed);
public sealed record ScannerReport(string SchemaVersion,string TargetApp,string Build,string Executable,string ScannedAtUtc,IReadOnlyList<ScannedScreen> Screens,ScannerDiff? Diff);

public sealed class AutomationIdScanner(AppConfig config)
{
    private static readonly HashSet<string> ActionableTypes = new(StringComparer.OrdinalIgnoreCase)
    { "Button", "Edit", "ComboBox", "DataGrid", "TabItem", "MenuItem", "CheckBox", "RadioButton", "ListItem", "TreeItem" };

    public static async Task<ScannerManifest> ReadManifestAsync(string path,CancellationToken ct=default)
        => JsonSerializer.Deserialize<ScannerManifest>(await File.ReadAllTextAsync(path,ct),JsonOptions)
           ?? throw new InvalidDataException("Scanner manifest is empty or invalid.");

    public static ScannerDiff CompareReports(IReadOnlyList<ScannedScreen> baseline,IReadOnlyList<ScannedScreen> current)
        => Compare(baseline,current);

    public async Task<ScannerReport> ScanAsync(ScannerManifest manifest,string? baselinePath,CancellationToken ct=default)
    {
        if(manifest.TargetApp is not ("pos" or "app"))throw new ArgumentException("targetApp must be pos or app.");
        var executable=manifest.TargetApp=="pos"?config.PosExePath:config.AppExePath;
        if(string.IsNullOrWhiteSpace(executable)||!File.Exists(executable))throw new FileNotFoundException($"Executable for {manifest.TargetApp} was not found.",executable);
        var process=new AppLauncher(config).Launch(manifest.TargetApp);
        try
        {
            using var automation=new UIA3Automation();using var app=Application.Attach(process.Id);
            var screens=new List<ScannedScreen>();
            var loginPanel=manifest.TargetApp=="pos"?"LoginOverlay":"LoginPanel";
            var window=WaitForWindowContaining(app,automation,loginPanel,30);
            var sensitive=new[]{config.PosUsername,config.PosPassword,config.AppUsername,config.AppPassword}.Where(x=>!string.IsNullOrWhiteSpace(x)).Cast<string>().ToHashSet(StringComparer.Ordinal);
            screens.Add(Snapshot("Login",window,sensitive));
            if(manifest.Login){Login(app,automation,manifest.TargetApp);window=WaitForWindow(app,automation,30);}
            if(manifest.Screens.Count==0)screens.Add(Snapshot("Home",window,sensitive));
            foreach(var screen in manifest.Screens)
            {
                foreach(var step in screen.Navigation)
                {
                    if(!step.Action.Equals("click",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException($"Unsupported safe navigation action: {step.Action}.");
                    Find(window,step.AutomationId,step.TimeoutSeconds).Click();
                    if(step.WaitForId is not null)Find(window,step.WaitForId,step.TimeoutSeconds);
                    await Task.Delay(300,ct);window=WaitForWindow(app,automation,step.TimeoutSeconds);
                }
                screens.Add(Snapshot(screen.Name,window,sensitive));
            }
            ScannerReport? baseline=null;
            if(!string.IsNullOrWhiteSpace(baselinePath)&&File.Exists(baselinePath))baseline=JsonSerializer.Deserialize<ScannerReport>(await File.ReadAllTextAsync(baselinePath,ct),JsonOptions);
            return new("1.0",manifest.TargetApp,manifest.Build,Path.GetFullPath(executable),DateTime.UtcNow.ToString("O"),screens,baseline is null?null:Compare(baseline.Screens,screens));
        }
        finally{if(!process.HasExited)new AppLauncher(config).Close(process);}
    }

    public static async Task WriteReportAsync(ScannerReport report,string path,CancellationToken ct=default)
    {var full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);await File.WriteAllTextAsync(full,JsonSerializer.Serialize(report,JsonOptions),ct);}

    public static async Task WriteRegistryAsync(ScannerReport report,string path,CancellationToken ct=default)
    {
        var lines=new List<string>{"# AutomationId Runtime Registry","",$"> Target: `{report.TargetApp}` | Build: `{report.Build}` | Scanned: `{report.ScannedAtUtc}`","","| Screen | Control | AutomationId | Type | Build |","|---|---|---|---|---|"};
        foreach(var screen in report.Screens)foreach(var element in screen.Elements.Where(x=>!string.IsNullOrWhiteSpace(x.AutomationId)).OrderBy(x=>x.AutomationId,StringComparer.OrdinalIgnoreCase))lines.Add($"| {Escape(screen.Name)} | {Escape(element.Name)} | `{Escape(element.AutomationId)}` | {Escape(element.ControlType)} | {Escape(report.Build)} |");
        lines.AddRange(["","## Contract Findings","",$"- Missing actionable AutomationId: {report.Screens.Sum(x=>x.MissingAutomationIds.Count)}",$"- Duplicate AutomationId: {report.Screens.Sum(x=>x.DuplicateAutomationIds.Count)}",$"- Removed from baseline: {report.Diff?.Removed.Count??0}"]);
        var full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);await File.WriteAllLinesAsync(full,lines,ct);
        static string Escape(string value)=>value.Replace("|","\\|").Replace("`","'");
    }

    private void Login(Application app,UIA3Automation automation,string target)
    {
        var user=target=="pos"?config.PosUsername:config.AppUsername;var password=target=="pos"?config.PosPassword:config.AppPassword;
        if(string.IsNullOrWhiteSpace(user)||string.IsNullOrWhiteSpace(password))throw new InvalidOperationException($"Login scan requires AUT_{target.ToUpperInvariant()}_USERNAME and AUT_{target.ToUpperInvariant()}_PASSWORD.");
        var panel=target=="pos"?"LoginOverlay":"LoginPanel";var userId=target=="pos"?"TxtEmpId":"TxtUsername";
        var window=WaitForWindowContaining(app,automation,panel,30);Find(window,userId,10).AsTextBox().Enter(user);Find(window,"PwdBox",10).AsTextBox().Enter(password);Find(window,"BtnSignIn",10).Click();
        var deadline=DateTime.UtcNow.AddSeconds(30);while(DateTime.UtcNow<deadline){var stillVisible=app.GetAllTopLevelWindows(automation).Any(w=>w.AutomationId==panel||w.FindFirstDescendant(cf=>cf.ByAutomationId(panel)) is not null);if(!stillVisible)return;Thread.Sleep(250);}throw new TimeoutException("Login panel remained visible after scanner sign-in.");
    }

    private static Window WaitForWindow(Application app,UIA3Automation automation,int seconds)
    {var deadline=DateTime.UtcNow.AddSeconds(seconds);Window? best=null;while(DateTime.UtcNow<deadline){best=app.GetAllTopLevelWindows(automation).OrderByDescending(x=>x.FindAllDescendants().Length).FirstOrDefault()??best;if(best is not null)return best;Thread.Sleep(200);}return best??throw new TimeoutException("AUT window was not found.");}
    private static Window WaitForWindowContaining(Application app,UIA3Automation automation,string id,int seconds)
    {var deadline=DateTime.UtcNow.AddSeconds(seconds);while(DateTime.UtcNow<deadline){foreach(var window in app.GetAllTopLevelWindows(automation)){if(window.AutomationId==id||window.FindFirstDescendant(cf=>cf.ByAutomationId(id)) is not null)return window;}Thread.Sleep(200);}throw new TimeoutException($"Window containing AutomationId '{id}' was not found.");}
    private static AutomationElement Find(Window window,string id,int seconds)
    {var deadline=DateTime.UtcNow.AddSeconds(seconds);while(DateTime.UtcNow<deadline){var found=window.FindFirstDescendant(cf=>cf.ByAutomationId(id));if(found is not null)return found;Thread.Sleep(150);}throw new TimeoutException($"Navigation AutomationId '{id}' was not found.");}
    private static ScannedScreen Snapshot(string name,Window window,IReadOnlySet<string> sensitive)
    {
        var raw=window.FindAllDescendants().Prepend(window).ToList();var elements=raw.Select((e,i)=>{var type=e.ControlType.ToString();var rawName=e.Name??"";var name=type.Equals("Edit",StringComparison.OrdinalIgnoreCase)||sensitive.Contains(rawName)?"<redacted>":rawName;return new ScannedElement(e.AutomationId??"",name,e.ClassName??"",type,$"{i:D4}/{e.ControlType}");}).ToList();
        var missing=elements.Where(x=>string.IsNullOrWhiteSpace(x.AutomationId)&&ActionableTypes.Contains(x.ControlType)).Select(x=>$"{x.ControlType}: {x.Name} ({x.Path})").ToList();
        var duplicates=elements.Where(x=>!string.IsNullOrWhiteSpace(x.AutomationId)).GroupBy(x=>x.AutomationId,StringComparer.OrdinalIgnoreCase).Where(x=>x.Count()>1).Select(x=>$"{x.Key} ({x.Count()})").ToList();
        return new(name,elements,missing,duplicates);
    }
    private static ScannerDiff Compare(IReadOnlyList<ScannedScreen> before,IReadOnlyList<ScannedScreen> after)
    {
        static Dictionary<string,ScannedElement> Map(IReadOnlyList<ScannedScreen> screens)=>screens.SelectMany(s=>s.Elements.Where(e=>!string.IsNullOrWhiteSpace(e.AutomationId)).Select(e=>new{Key=$"{s.Name}/{e.AutomationId}",Element=e})).GroupBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>x.First().Element,StringComparer.OrdinalIgnoreCase);
        var oldMap=Map(before);var newMap=Map(after);var added=newMap.Keys.Except(oldMap.Keys,StringComparer.OrdinalIgnoreCase).Order().ToList();var removed=oldMap.Keys.Except(newMap.Keys,StringComparer.OrdinalIgnoreCase).Order().ToList();var changed=oldMap.Keys.Intersect(newMap.Keys,StringComparer.OrdinalIgnoreCase).Where(k=>oldMap[k].ControlType!=newMap[k].ControlType||oldMap[k].ClassName!=newMap[k].ClassName).Order().ToList();return new(added,removed,changed);
    }
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web){WriteIndented=true,PropertyNameCaseInsensitive=true};
}
