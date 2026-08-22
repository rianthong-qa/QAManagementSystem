using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace Promaxx2.Automation.Pages.App;

/// <summary>
/// Base ของ PageObjects สำหรับ Promaxxs.App.exe (Master Data)
/// Selector ต้องอ้าง AutomationId เท่านั้น (ตาม SELECTOR_CONTRACT.md)
/// </summary>
public abstract class AppPageBase : IDisposable
{
    protected readonly UIA3Automation Automation = new();
    protected Application App { get; }

    public AppPageBase(Application app) => App = app;

    public Window MainWindow => App.GetMainWindow(Automation)
        ?? throw new InvalidOperationException("Promaxxs.App main window was not found.");

    protected AutomationElement? FindById(string automationId, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (el != null) return el;
            Thread.Sleep(100);
        }
        return null;
    }

    protected T? FindById<T>(string automationId, int timeoutMs = 5000) where T : AutomationElement
        => FindById(automationId, timeoutMs) as T;

    protected void Click(string automationId, int timeoutMs = 5000)
    {
        var el = FindById(automationId, timeoutMs);
        if (el is null) throw new InvalidOperationException($"Element with AutomationId '{automationId}' not found.");
        el.Click();
    }

    protected void TypeText(string automationId, string text, int timeoutMs = 5000)
    {
        var el = FindById(automationId, timeoutMs);
        if (el is null) throw new InvalidOperationException($"Element with AutomationId '{automationId}' not found.");
        el.AsTextBox().Enter(text);
    }

    protected string? GetText(string automationId, int timeoutMs = 5000)
        => FindById(automationId, timeoutMs)?.Name;

    protected bool Exists(string automationId, int timeoutMs = 1000)
        => FindById(automationId, timeoutMs) != null;

    public void Dispose() => Automation.Dispose();
}
