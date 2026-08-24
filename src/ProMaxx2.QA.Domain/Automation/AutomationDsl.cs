namespace ProMaxx2.QA.Domain.Automation;

public sealed class DslDocument
{
    public string DslVersion { get; set; } = "1.0";
    public string AutomationType { get; set; } = "WindowsUI";
    public List<DslStep> Steps { get; set; } = [];
}

public sealed class DslStep
{
    public int StepNo { get; set; }
    public string Action { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new();
}