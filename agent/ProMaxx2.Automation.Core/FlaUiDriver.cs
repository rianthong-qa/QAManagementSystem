using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using System.Drawing;
using System.Drawing.Imaging;

namespace ProMaxx2.Automation.Core;

public interface IUiAutomationDriver : IDisposable
{
    Task LaunchAsync(string exePath, string? arguments, TimeSpan timeout);
    Task<bool> WaitForMainWindowAsync(string processName, TimeSpan timeout);
    /// <summary>AUT-AGT-002: รอ window/หน้าจอที่ title มีข้อความ หรือ AutomationId ตรงกับ <paramref name="titleOrAutomationId"/></summary>
    Task<bool> WaitForWindowAsync(string titleOrAutomationId, TimeSpan timeout);
    Task<bool> ClickAsync(string automationId, string? controlType, TimeSpan timeout);
    Task<bool> SetTextAsync(string automationId, string? controlType, string value, TimeSpan timeout);
    Task<string?> GetTextAsync(string automationId, string? controlType, TimeSpan timeout);
    Task<bool> ExistsAsync(string automationId, string? controlType, TimeSpan timeout);
    /// <summary>AUT-AGT-002: true/false ตาม IsEnabled ของ control, null เมื่อหา control ไม่เจอ</summary>
    Task<bool?> IsEnabledAsync(string automationId, string? controlType, TimeSpan timeout);
    Task<bool> SelectComboAsync(string automationId, string value, TimeSpan timeout);
    Task<bool> ToggleAsync(string automationId, bool check, TimeSpan timeout);
    /// <summary>รับ key string แบบ SendKeys เช่น <c>{ENTER}</c>, <c>{F8}</c>, <c>%F</c> (ดู <see cref="KeySequence"/>)</summary>
    Task<bool> PressKeyAsync(string key);
    Task<bool> ExpectMessageAsync(string messageKey, TimeSpan timeout);
    Task<byte[]?> CaptureScreenshotAsync();
    Task CloseAsync();
}

public sealed class FlaUiDriver : IUiAutomationDriver
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);
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

    public Task<bool> WaitForWindowAsync(string titleOrAutomationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            foreach (var window in Windows())
            {
                try
                {
                    var title = window.Title ?? "";
                    var id = window.Properties.AutomationId.ValueOrDefault ?? "";
                    if (title.Contains(titleOrAutomationId, StringComparison.OrdinalIgnoreCase) || id.Equals(titleOrAutomationId, StringComparison.OrdinalIgnoreCase)
                        || window.FindFirstDescendant(cf => cf.ByAutomationId(titleOrAutomationId)) is not null)
                        return Task.FromResult(true);
                }
                catch { }
            }
            Thread.Sleep(PollInterval);
        } while (DateTime.UtcNow < deadline);
        return Task.FromResult(false);
    }

    /// <summary>หน้าต่างทั้งหมดของ AUT — main window ก่อน ตามด้วย top-level อื่น (dialog/MessageBox/popup)
    /// เดิมค้นแค่ main window ทำให้มองไม่เห็น control ใน popup</summary>
    private IReadOnlyList<Window> Windows()
    {
        var result = new List<Window>();
        var main = CurrentWindow();
        if (main is not null) result.Add(main);
        if (_application is not null)
        {
            try
            {
                foreach (var w in _application.GetAllTopLevelWindows(_automation))
                    if (main is null || !w.Equals(main)) result.Add(w);
            }
            catch { }
        }
        return result;
    }

    private AutomationElement? Find(string automationId, string? controlType, TimeSpan timeout)
    {
        var hasType = Enum.TryParse<ControlType>(controlType, ignoreCase: true, out var type);
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            foreach (var window in Windows())
            {
                try
                {
                    // กรองตาม ControlType ด้วยเมื่อระบุมา — กัน AutomationId ซ้ำระหว่าง label กับ textbox
                    var found = hasType
                        ? window.FindFirstDescendant(cf => cf.ByAutomationId(automationId).And(cf.ByControlType(type)))
                        : window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
                    if (found is not null) return found;
                }
                catch { }
            }
            if (DateTime.UtcNow >= deadline) break;
            Thread.Sleep(PollInterval);
        } while (true);
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
        try
        {
            element.Click();
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
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
                // ล้างค่าเดิมก่อนพิมพ์ — เดิมพิมพ์ต่อท้ายข้อความที่มีอยู่
                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                Keyboard.Type(VirtualKeyShort.DELETE);
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
        if (element is null) return Task.FromResult<string?>(null);
        // ค่าที่ผู้ใช้เห็นใน textbox อยู่ใน Value pattern — Name มักเป็น label ของ control (EXPECT_VALUE เดิมอ่านผิดช่อง)
        var value = element.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault;
        return Task.FromResult(!string.IsNullOrEmpty(value) ? value : element.Properties.Name.ValueOrDefault ?? element.Properties.HelpText.ValueOrDefault);
    }

    public Task<bool> ExistsAsync(string automationId, string? controlType, TimeSpan timeout)
        => Task.FromResult(Find(automationId, controlType, timeout) is not null);

    public Task<bool?> IsEnabledAsync(string automationId, string? controlType, TimeSpan timeout)
    {
        var element = Find(automationId, controlType, timeout);
        if (element is null) return Task.FromResult<bool?>(null);
        try { return Task.FromResult<bool?>(element.IsEnabled); }
        catch { return Task.FromResult<bool?>(null); }
    }

    public Task<bool> SelectComboAsync(string automationId, string value, TimeSpan timeout)
    {
        var element = Find(automationId, controlType: null, timeout);
        if (element is null) return Task.FromResult(false);
        try
        {
            // ใช้ SelectionItem ของ ComboBox ก่อน แล้วค่อย fallback เป็นพิมพ์ค่า + Enter
            var combo = element.AsComboBox();
            if (combo.Select(value) is not null) return Task.FromResult(true);
        }
        catch { }
        try
        {
            element.Focus();
            Keyboard.Type(value);
            Keyboard.Type(VirtualKeyShort.RETURN);
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
        // key string ผิดรูปแบบโยน ArgumentException (AUT-DSL-002) ออกไปให้ ActionExecutor รายงานเป็นข้อผิดพลาดของ DSL
        var strokes = KeySequence.Parse(key);
        try
        {
            CurrentWindow()?.Focus();
            FlaUiKeyboard.Send(strokes);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> ExpectMessageAsync(string messageKey, TimeSpan timeout)
    {
        // MessageBox/popup เป็น top-level window แยก และอาจขึ้นช้ากว่าการกดปุ่ม — วนค้นทุก window จนหมดเวลา
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            foreach (var window in Windows())
            {
                try
                {
                    if ((window.Title ?? "").Contains(messageKey, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(true);
                    foreach (var element in window.FindAllDescendants())
                    {
                        var text = element.Properties.Name.ValueOrDefault ?? "";
                        if (text.Contains(messageKey, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(true);
                    }
                }
                catch { }
            }
            if (DateTime.UtcNow >= deadline) break;
            Thread.Sleep(PollInterval);
        } while (true);
        return Task.FromResult(false);
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

    /// <summary>AUT-AGT-003: ขอปิดแบบปกติก่อน ถ้า AUT ไม่ปิดภายใน 10 วินาที (เช่นมี dialog ยืนยันการออก หรือค้าง) จึง kill
    /// — เดิมเรียก Close() ครั้งเดียวแล้วกลืน error ทำให้ ProMaxx2 ค้างอยู่และงานถัดไปเจอหน้าจอเดิม</summary>
    public async Task CloseAsync()
    {
        var app = _application;
        _application = null;
        _mainWindow = null;
        if (app is null) return;
        try
        {
            try { if (!app.HasExited) app.Close(); } catch { /* window already gone */ }
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!app.HasExited && DateTime.UtcNow < deadline) await Task.Delay(250);
            if (!app.HasExited)
            {
                try { app.Kill(); } catch { /* exited meanwhile */ }
            }
        }
        finally
        {
            app.Dispose();
        }
    }

    public void Dispose() => _automation.Dispose();
}

/// <summary>AUT-AGT-002: ส่ง <see cref="KeyStroke"/> ผ่าน FlaUI Keyboard โดยแปลงปุ่มพิเศษเป็น virtual key จริง</summary>
public static class FlaUiKeyboard
{
    public static void SendKeys(string keys) => Send(KeySequence.Parse(keys));

    public static void Send(IEnumerable<KeyStroke> strokes)
    {
        foreach (var stroke in strokes) Send(stroke);
    }

    public static void Send(KeyStroke stroke)
    {
        var modifiers = new List<VirtualKeyShort>();
        if (stroke.Ctrl) modifiers.Add(VirtualKeyShort.CONTROL);
        if (stroke.Alt) modifiers.Add(VirtualKeyShort.ALT);
        if (stroke.Shift) modifiers.Add(VirtualKeyShort.SHIFT);
        if (stroke.Key is not null)
        {
            var key = MapKey(stroke.Key);
            if (modifiers.Count == 0) Keyboard.Type(key);
            else Keyboard.TypeSimultaneously([.. modifiers, key]);
            return;
        }
        if (string.IsNullOrEmpty(stroke.Text)) return;
        if (modifiers.Count == 0) { Keyboard.Type(stroke.Text); return; }
        var ch = char.ToUpperInvariant(stroke.Text[0]);
        if (ch is >= 'A' and <= 'Z') Keyboard.TypeSimultaneously([.. modifiers, (VirtualKeyShort)ch]);
        else if (ch is >= '0' and <= '9') Keyboard.TypeSimultaneously([.. modifiers, (VirtualKeyShort)ch]);
        else
        {
            var pressed = modifiers.Select(m => Keyboard.Pressing(m)).ToList();
            try { Keyboard.Type(stroke.Text); }
            finally { for (var i = pressed.Count - 1; i >= 0; i--) pressed[i].Dispose(); }
        }
    }

    private static VirtualKeyShort MapKey(string key) => key switch
    {
        "ENTER" => VirtualKeyShort.RETURN,
        "TAB" => VirtualKeyShort.TAB,
        "ESC" => VirtualKeyShort.ESCAPE,
        "BACKSPACE" => VirtualKeyShort.BACK,
        "DELETE" => VirtualKeyShort.DELETE,
        "INSERT" => VirtualKeyShort.INSERT,
        "HOME" => VirtualKeyShort.HOME,
        "END" => VirtualKeyShort.END,
        "PGUP" => VirtualKeyShort.PRIOR,
        "PGDN" => VirtualKeyShort.NEXT,
        "UP" => VirtualKeyShort.UP,
        "DOWN" => VirtualKeyShort.DOWN,
        "LEFT" => VirtualKeyShort.LEFT,
        "RIGHT" => VirtualKeyShort.RIGHT,
        "SPACE" => VirtualKeyShort.SPACE,
        "F1" => VirtualKeyShort.F1, "F2" => VirtualKeyShort.F2, "F3" => VirtualKeyShort.F3, "F4" => VirtualKeyShort.F4,
        "F5" => VirtualKeyShort.F5, "F6" => VirtualKeyShort.F6, "F7" => VirtualKeyShort.F7, "F8" => VirtualKeyShort.F8,
        "F9" => VirtualKeyShort.F9, "F10" => VirtualKeyShort.F10, "F11" => VirtualKeyShort.F11, "F12" => VirtualKeyShort.F12,
        _ => throw new ArgumentException($"Unsupported key '{{{key}}}' (AUT-DSL-002)."),
    };
}
