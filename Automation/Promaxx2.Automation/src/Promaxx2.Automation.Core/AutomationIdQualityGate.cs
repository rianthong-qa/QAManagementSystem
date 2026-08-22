using System.Text.Json;
using System.Xml.Linq;

namespace Promaxx2.Automation.Core;

public sealed record AutomationIdGatePolicy(
    int MaxNewMissingAutomationIds=0,
    int MaxNewDuplicateAutomationIds=0,
    int MaxRemovedAutomationIds=0,
    int MaxChangedAutomationIds=0,
    IReadOnlyList<string>? AllowedRemoved=null,
    IReadOnlyList<string>? AllowedChanged=null);

public sealed record AutomationIdGateResult(
    bool Passed,
    string TargetApp,
    string BaselineBuild,
    string CurrentBuild,
    IReadOnlyList<string> NewMissingAutomationIds,
    IReadOnlyList<string> NewDuplicateAutomationIds,
    IReadOnlyList<string> RemovedAutomationIds,
    IReadOnlyList<string> ChangedAutomationIds,
    IReadOnlyList<string> Messages);

public static class AutomationIdQualityGate
{
    public static async Task<AutomationIdGateResult> EvaluateAsync(string baselinePath,string currentPath,string policyPath,CancellationToken ct=default)
    {
        var baseline=await ReadAsync<ScannerReport>(baselinePath,ct);var current=await ReadAsync<ScannerReport>(currentPath,ct);var policy=await ReadAsync<AutomationIdGatePolicy>(policyPath,ct);
        return Evaluate(baseline,current,policy);
    }

    public static AutomationIdGateResult Evaluate(ScannerReport baseline,ScannerReport current,AutomationIdGatePolicy policy)
    {
        if(!baseline.TargetApp.Equals(current.TargetApp,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Baseline and current report targetApp do not match.");
        static HashSet<string> Findings(IReadOnlyList<ScannedScreen> screens,Func<ScannedScreen,IReadOnlyList<string>> selector)=>screens.SelectMany(s=>selector(s).Select(x=>$"{s.Name}/{x}")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newMissing=Findings(current.Screens,x=>x.MissingAutomationIds).Except(Findings(baseline.Screens,x=>x.MissingAutomationIds),StringComparer.OrdinalIgnoreCase).Order().ToList();
        var newDuplicates=Findings(current.Screens,x=>x.DuplicateAutomationIds).Except(Findings(baseline.Screens,x=>x.DuplicateAutomationIds),StringComparer.OrdinalIgnoreCase).Order().ToList();
        var diff=AutomationIdScanner.CompareReports(baseline.Screens,current.Screens);var allowedRemoved=(policy.AllowedRemoved??[]).ToHashSet(StringComparer.OrdinalIgnoreCase);var allowedChanged=(policy.AllowedChanged??[]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed=diff.Removed.Where(x=>!allowedRemoved.Contains(x)).ToList();var changed=diff.Changed.Where(x=>!allowedChanged.Contains(x)).ToList();var messages=new List<string>();
        Check(newMissing.Count,policy.MaxNewMissingAutomationIds,"new missing AutomationId",messages);Check(newDuplicates.Count,policy.MaxNewDuplicateAutomationIds,"new duplicate AutomationId",messages);Check(removed.Count,policy.MaxRemovedAutomationIds,"removed AutomationId",messages);Check(changed.Count,policy.MaxChangedAutomationIds,"changed AutomationId",messages);var passed=messages.Count==0;
        if(passed)messages.Add("AutomationId contract is compatible with the configured baseline.");
        return new(passed,current.TargetApp,baseline.Build,current.Build,newMissing,newDuplicates,removed,changed,messages);
    }

    public static async Task WriteJsonAsync(AutomationIdGateResult result,string path,CancellationToken ct=default)
    {var full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);await File.WriteAllTextAsync(full,JsonSerializer.Serialize(result,JsonOptions),ct);}

    public static async Task WriteJUnitAsync(AutomationIdGateResult result,string path,CancellationToken ct=default)
    {
        var testCase=new XElement("testcase",new XAttribute("name",$"AutomationId contract - {result.TargetApp}"),new XAttribute("classname","Promaxx2.AutomationIdQualityGate"));
        if(!result.Passed)testCase.Add(new XElement("failure",new XAttribute("message","AutomationId quality gate failed"),string.Join(Environment.NewLine,result.Messages)));
        var suite=new XElement("testsuite",new XAttribute("name","AutomationId Quality Gate"),new XAttribute("tests",1),new XAttribute("failures",result.Passed?0:1),testCase);var document=new XDocument(new XElement("testsuites",suite));var full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);await File.WriteAllTextAsync(full,document.ToString(),ct);
    }

    private static void Check(int actual,int maximum,string label,List<string> messages){if(actual>maximum)messages.Add($"{label}: {actual} exceeds policy maximum {maximum}.");}
    private static async Task<T>ReadAsync<T>(string path,CancellationToken ct)=>JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path,ct),JsonOptions)??throw new InvalidDataException($"Invalid JSON: {path}");
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web){WriteIndented=true,PropertyNameCaseInsensitive=true};
}
