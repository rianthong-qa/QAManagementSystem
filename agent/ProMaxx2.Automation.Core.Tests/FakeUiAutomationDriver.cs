namespace ProMaxx2.Automation.Core.Tests;

/// <summary>Scriptable stand-in for <see cref="IUiAutomationDriver"/> — no real FlaUI/Windows UI Automation involved.</summary>
internal sealed class FakeUiAutomationDriver : IUiAutomationDriver
{
    public bool ClickResult = true;
    public bool SetTextResult = true;
    public string? GetTextResult = "";
    public bool ExistsResult = true;
    public bool SelectComboResult = true;
    public bool ToggleResult = true;
    public bool PressKeyResult = true;
    public bool ExpectMessageResult = true;
    public byte[]? Screenshot = [9, 9, 9];
    public Exception? ThrowOnClick;
    public readonly List<string> Calls = [];

    public Task LaunchAsync(string exePath, string? arguments, TimeSpan timeout) => Task.CompletedTask;
    public Task<bool> WaitForMainWindowAsync(string processName, TimeSpan timeout) => Task.FromResult(true);

    public Task<bool> ClickAsync(string automationId, string? controlType, TimeSpan timeout)
    {
        Calls.Add($"Click:{automationId}");
        if (ThrowOnClick is not null) throw ThrowOnClick;
        return Task.FromResult(ClickResult);
    }

    public Task<bool> SetTextAsync(string automationId, string? controlType, string value, TimeSpan timeout)
    {
        Calls.Add($"SetText:{automationId}={value}");
        return Task.FromResult(SetTextResult);
    }

    public Task<string?> GetTextAsync(string automationId, string? controlType, TimeSpan timeout)
    {
        Calls.Add($"GetText:{automationId}");
        return Task.FromResult(GetTextResult);
    }

    public Task<bool> ExistsAsync(string automationId, string? controlType, TimeSpan timeout)
    {
        Calls.Add($"Exists:{automationId}");
        return Task.FromResult(ExistsResult);
    }

    public Task<bool> SelectComboAsync(string automationId, string value, TimeSpan timeout)
    {
        Calls.Add($"SelectCombo:{automationId}={value}");
        return Task.FromResult(SelectComboResult);
    }

    public Task<bool> ToggleAsync(string automationId, bool check, TimeSpan timeout)
    {
        Calls.Add($"Toggle:{automationId}={check}");
        return Task.FromResult(ToggleResult);
    }

    public Task<bool> PressKeyAsync(string key)
    {
        Calls.Add($"PressKey:{key}");
        return Task.FromResult(PressKeyResult);
    }

    public Task<bool> ExpectMessageAsync(string messageKey, TimeSpan timeout)
    {
        Calls.Add($"ExpectMessage:{messageKey}");
        return Task.FromResult(ExpectMessageResult);
    }

    public Task<byte[]?> CaptureScreenshotAsync() => Task.FromResult(Screenshot);
    public Task CloseAsync() => Task.CompletedTask;
    public void Dispose() { }
}
