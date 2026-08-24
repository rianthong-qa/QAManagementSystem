using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using System.Drawing;
using System.Drawing.Imaging;

namespace ProMaxx2.Automation.Core;

public interface IUiAutomationDriver : IDisposable
{
    Task LaunchAsync(string exePath, string? arguments, TimeSpan timeout);
    Task<bool> WaitForMainWindowAsync(string processName, TimeSpan timeout);
    Task<bool> ClickAsync(string automationId, string? controlType, TimeSpan timeout);
    Task<bool> SetTextAsync(string automationId, string? controlType, string value, TimeSpan timeout);
    Task<string?> GetTextAsync(string automationId, string? controlType, TimeSpan timeout);
    Task<bool> ExistsAsync(string automationId, string? controlType, TimeSpan timeout);
    Task<bool> SelectComboAsync(string automationId, string value, TimeSpan timeout);
    Task<bool> ToggleAsync(string automationId, bool check, TimeSpan timeout);
    Task<bool> PressKeyAsync(string key);
    Task<bool> ExpectMessageAsync(string messageKey, TimeSpan timeout);
    Task<byte[]?> CaptureScreenshotAsync();
    Task CloseAsync();
}

public sealed class FlaUiDriver : IUiAutomationDriver
{
    private readonly AutomationBase _automation = new UIA3Automation();
    private Application? _application;
    private Window? _mainWindow;

    public FlaUiDriver(string processName) { }

    public Task LaunchAsync(string exePath, string? arguments, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(exePath)) throw new InvalidOperationException("AUT_EXE is not configured.");
        _application = Application.Launch(exePath, arguments ?? "");
        return Task.CompletedTask;
    }

    public Task<bool> WaitForMainWindowAsync(string processName, TimeSpan timeout)
    {
        if (_application is null) return Task.FromResult(false);
        try
        {
            var window = _application.GetMainWindow(_automation, timeout);
            _mainWindow = window;
            return Task.FromResult(window is not null);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private AutomationElement? Find(string automationId, string? controlType, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            var window = CurrentWindow();
            if (window is not null)
            {
                try
                {
                    var found = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
                    if (found is not null) return found;
                }
                catch { }
            }
            Thread.Sleep(500);
        } while (DateTime.UtcNow < deadline);
        return null;
    }

    private Window? CurrentWindow()
    {
        if (_application is null) return _mainWindow;
        try
        {
            var main = _application.GetMainWindow(_automation, TimeSpan.FromMilliseconds(500));
            if (main is not null) _mainWindow = main;
        }
        catch { }
        return _mainWindow;
    }

    public Task<bool> ClickAsync(string automationId, string? controlType, TimeSpan timeout)
    {
        var element = Find(automationId, controlType, timeout);
        if (element is null) return Task.FromResult(false);
        element.Click();
        return Task.FromResult(true);
    }

    public Task<bool> SetTextAsync(string automationId, string? controlType, string value, TimeSpan timeout)
    {
        var element = Find(automationId, controlType, timeout);
        if (element is null) return Task.FromResult(false);
        try
        {
            element.Focus();
            if (element.Patterns.Value.PatternOrDefault is { } valuePattern)
            {
                valuePattern.SetValue(value);
            }
            else
            {
                Keyboard.Type(value);
            }
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<string?> GetTextAsync(string automationId, string? controlType, TimeSpan timeout)
    {
        var element = Find(automationId, controlType, timeout);
        return Task.FromResult(element is null ? null : element.Properties.Name.ValueOrDefault ?? element.Properties.HelpText.ValueOrDefault);
    }

    public Task<bool> ExistsAsync(string automationId, string? controlType, TimeSpan timeout)
        => Task.FromResult(Find(automationId, controlType, timeout) is not null);

    public Task<bool> SelectComboAsync(string automationId, string value, TimeSpan timeout)
    {
        var element = Find(automationId, controlType: null, timeout);
        if (element is null) return Task.FromResult(false);
        try
        {
            element.Focus();
            Keyboard.Type(value + "{Enter}");
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> ToggleAsync(string automationId, bool check, TimeSpan timeout)
    {
        var element = Find(automationId, controlType: null, timeout);
        if (element is null) return Task.FromResult(false);
        try
        {
            var current = element.Patterns.Toggle.PatternOrDefault;
            var isChecked = current?.ToggleState == ToggleState.On;
            if (isChecked != check) element.Click();
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> PressKeyAsync(string key)
    {
        try
        {
            if (_mainWindow is not null) _mainWindow.Focus();
            Keyboard.Type(key);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> ExpectMessageAsync(string messageKey, TimeSpan timeout)
    {
        var window = _mainWindow;
        if (window is null) return Task.FromResult(false);
        try
        {
            var elements = window.FindAllDescendants();
            foreach (var element in elements)
            {
                var text = element.Properties.Name.ValueOrDefault ?? "";
                if (text.Contains(messageKey, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<byte[]?> CaptureScreenshotAsync()
    {
        try
        {
            var bounds = _mainWindow?.BoundingRectangle ?? Rectangle.Empty;
            if (bounds.IsEmpty && _application is not null)
            {
                var window = _application.GetMainWindow(_automation, TimeSpan.FromSeconds(2));
                bounds = window?.BoundingRectangle ?? Rectangle.Empty;
            }
            if (bounds.IsEmpty) return Task.FromResult<byte[]?>(null);
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new Size(bounds.Width, bounds.Height));
            }
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return Task.FromResult<byte[]?>(stream.ToArray());
        }
        catch
        {
            return Task.FromResult<byte[]?>(null);
        }
    }

    public Task CloseAsync()
    {
        try { _application?.Close(); } catch { }
        _mainWindow = null;
        return Task.CompletedTask;
    }

    public void Dispose() => _automation.Dispose();
}