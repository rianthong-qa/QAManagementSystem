using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System.Text.Json;

namespace ProMaxx2.Automation.Core;

public sealed record UiNode(string Name, string AutomationId, string ControlType, bool IsEnabled, string ClassName, int Depth);
public sealed record UiInspectResult(string Application, string MainWindow, DateTime CapturedAt, List<UiNode> Nodes);
public sealed record LoginCredentials(string EmpId, string Password, int AfterLoginWaitSeconds = 10, string? NavAutomationId = null, int NavWaitSeconds = 8, string? ScanCode = null, int ScanWaitSeconds = 8, string? SetQtyValue = null, string? PressKey = null, int PostPressWaitSeconds = 6);

public sealed class UiInspector
{
    public static async Task<UiInspectResult?> InspectAsync(string exePath, string? processName, int timeoutSeconds, int settleSeconds, LoginCredentials? login, CancellationToken ct)
    {
        using var automation = new UIA3Automation();
        Application? app = null;
        Window? window = null;
        var process = processName ?? Path.GetFileNameWithoutExtension(exePath);

        if (string.IsNullOrWhiteSpace(exePath))
        {
            var existing = System.Diagnostics.Process.GetProcessesByName(process).FirstOrDefault();
            if (existing is null) return null;
            app = Application.Attach(existing.Id);
        }
        else
        {
            app = Application.Launch(exePath);
        }

        try
        {
            window = app.GetMainWindow(automation, TimeSpan.FromSeconds(timeoutSeconds));
        }
        catch
        {
            var all = app.GetAllTopLevelWindows(automation);
            window = all.FirstOrDefault();
        }
        if (window is null) { try { app.Close(); } catch { } return null; }

        if (settleSeconds > 0)
        {
            Console.WriteLine($"[inspect] waiting {settleSeconds}s for UI to settle...");
            await Task.Delay(TimeSpan.FromSeconds(settleSeconds), ct);
            try { window = app.GetMainWindow(automation, TimeSpan.FromSeconds(5)); } catch { }
        }

        if (login is not null && window is not null)
        {
            Console.WriteLine($"[inspect] attempting login as '{login.EmpId}'...");
            var emp = window.FindFirstDescendant(cf => cf.ByAutomationId("TxtEmpId")) ?? window.FindFirstDescendant(cf => cf.ByAutomationId("TxtUsername"));
            var pwd = window.FindFirstDescendant(cf => cf.ByAutomationId("PwdBox"));
            var signIn = window.FindFirstDescendant(cf => cf.ByAutomationId("BtnSignIn"));
            if (emp is not null && pwd is not null && signIn is not null)
            {
                emp.Focus();
                emp.Patterns.Value.PatternOrDefault?.SetValue(login.EmpId);
                pwd.Focus();
                pwd.Patterns.Value.PatternOrDefault?.SetValue(login.Password);
                signIn.Click();
                Console.WriteLine($"[inspect] login submitted, waiting {login.AfterLoginWaitSeconds}s...");
                await Task.Delay(TimeSpan.FromSeconds(login.AfterLoginWaitSeconds), ct);
                try { window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10)); } catch { }
var stillLoggedOut = window?.FindFirstDescendant(cf => cf.ByAutomationId("TxtEmpId")) is not null
                    || window?.FindFirstDescendant(cf => cf.ByAutomationId("TxtUsername")) is not null;
            Console.WriteLine(stillLoggedOut ? "[inspect] login FAILED (login overlay still present)." : "[inspect] login OK (overlay closed).");
            if (!string.IsNullOrWhiteSpace(login.NavAutomationId))
            {
                var nav = window?.FindFirstDescendant(cf => cf.ByAutomationId(login.NavAutomationId));
                if (nav is not null)
                {
                    nav.Click();
                    Console.WriteLine($"[inspect] clicked nav '{login.NavAutomationId}', waiting {login.NavWaitSeconds}s...");
                    await Task.Delay(TimeSpan.FromSeconds(login.NavWaitSeconds), ct);
                }
                else Console.WriteLine($"[inspect] nav '{login.NavAutomationId}' not found.");
            }
            if (!string.IsNullOrWhiteSpace(login.ScanCode))
            {
                var scanBox = window?.FindFirstDescendant(cf => cf.ByAutomationId("ScanCodeBox"));
                if (scanBox is not null)
                {
                    scanBox.Focus();
                    scanBox.Patterns.Value.PatternOrDefault?.SetValue(login.ScanCode);
                    FlaUI.Core.Input.Keyboard.Type("{Enter}");
                    Console.WriteLine($"[inspect] scanned '{login.ScanCode}', waiting {login.ScanWaitSeconds}s...");
                    await Task.Delay(TimeSpan.FromSeconds(login.ScanWaitSeconds), ct);
                }
                else Console.WriteLine("[inspect] ScanCodeBox not found.");
            }
            if (!string.IsNullOrWhiteSpace(login.SetQtyValue))
            {
                var qty = window?.FindFirstDescendant(cf => cf.ByAutomationId("TxtQty"));
                if (qty is not null)
                {
                    qty.Focus();
                    qty.Patterns.Value.PatternOrDefault?.SetValue(login.SetQtyValue);
                    FlaUI.Core.Input.Keyboard.Type("{Enter}");
                    Console.WriteLine($"[inspect] set qty '{login.SetQtyValue}'.");
                }
            }
            if (!string.IsNullOrWhiteSpace(login.PressKey))
            {
                FlaUI.Core.Input.Keyboard.Type(login.PressKey);
                Console.WriteLine($"[inspect] pressed '{login.PressKey}', waiting {login.PostPressWaitSeconds}s...");
                await Task.Delay(TimeSpan.FromSeconds(login.PostPressWaitSeconds), ct);
            }
        }
            else
            {
                Console.WriteLine("[inspect] login controls not found (already logged in?).");
            }
        }

        var nodes = new List<UiNode>();
        if (window is not null)
        {
            await Task.Run(() => Dump(window, 0, nodes, ct), ct);
            try
            {
                var others = app.GetAllTopLevelWindows(automation);
                foreach (var other in others)
                {
                    if (other.Properties.Name.ValueOrDefault is { Length: > 0 } n && !n.Equals(window.Properties.Name.ValueOrDefault, StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Run(() => Dump(other, 0, nodes, ct), ct);
                    }
                }
            }
            catch { }
        }
        var result = new UiInspectResult(exePath, window?.Properties.Name.ValueOrDefault ?? process, DateTime.UtcNow, nodes);
        try { app.Close(); } catch { }
        return result;
    }

    private static void Dump(AutomationElement element, int depth, List<UiNode> nodes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > 30) return;
        var name = element.Properties.Name.ValueOrDefault ?? "";
        var id = element.Properties.AutomationId.ValueOrDefault ?? "";
        var type = element.Properties.ControlType.ValueOrDefault.ToString() ?? "";
        var className = element.Properties.ClassName.ValueOrDefault ?? "";
        if (id.Length > 0 || name.Length > 0)
        {
            nodes.Add(new UiNode(name, id, type, element.IsEnabled, className, depth));
        }
        AutomationElement[] children;
        try { children = element.FindAllChildren(); }
        catch { return; }
        foreach (var child in children) Dump(child, depth + 1, nodes, ct);
    }
}