using System.Diagnostics;
using Promaxx2.Automation.Core;

namespace Promaxx2.Automation.Core;

/// <summary>
/// Launch/close AUT (Promaxxs.App.exe / PromaxxsPos.exe) พร้อมจัดการ timeout/crash
/// </summary>
public sealed class AppLauncher
{
    private readonly AppConfig _config;

    public AppLauncher(AppConfig config) => _config = config;

    /// <summary>Launch แอป ตาม appKey ("pos" หรือ "app") คืน Process ที่รันอยู่</summary>
    public Process Launch(string appKey, TimeSpan? startupTimeout = null)
    {
        var (exePath, args) = ResolveExe(appKey);
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"{appKey} executable not found: {exePath} (set AUT_{appKey.ToUpper()}_EXE)");

        var psi = new ProcessStartInfo(exePath, args)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
            CreateNoWindow = false
        };

        var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {exePath}");
        
        // Wait for input idle (WPF window ready) with timeout
        var timeout = startupTimeout ?? TimeSpan.FromSeconds(30);
        if (!proc.WaitForInputIdle((int)timeout.TotalMilliseconds))
        {
            proc.Kill(true);
            throw new TimeoutException($"{appKey} did not become idle within {timeout}.");
        }

        return proc;
    }

    public void Close(Process proc, TimeSpan? gracefulTimeout = null)
    {
        if (proc.HasExited) return;
        
        var timeout = gracefulTimeout ?? TimeSpan.FromSeconds(10);
        proc.CloseMainWindow();
        if (!proc.WaitForExit((int)timeout.TotalMilliseconds))
        {
            proc.Kill(true);
            proc.WaitForExit(2000);
        }
    }

    public (string exePath, string args) ResolveExe(string appKey)
    {
        return appKey.ToLowerInvariant() switch
        {
            "pos" => (_config.PosExePath, ""),
            "app" => (_config.AppExePath, ""),
            _ => throw new ArgumentException($"Unknown appKey: {appKey} (expected 'pos' or 'app')")
        };
    }
}