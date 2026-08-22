using System.IO;

namespace Promaxx2.Automation.Core;

/// <summary>
/// Swap config ini profiles ต่อ scenario — backup ไฟล์ต้นฉบับไว้ restore
/// </summary>
public sealed class EnvironmentSwitcher
{
    private readonly string _configDir;
    private readonly Dictionary<string, (string sourcePath, string backupPath)> _activeBackups = new();

    public EnvironmentSwitcher(string? configDir = null)
    {
        _configDir = configDir ?? Environment.GetEnvironmentVariable("AUT_CONFIG_DIR") 
            ?? Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("AUT_POS_EXE") ?? "") ?? "", "config");
    }

    /// <summary>ใช้ profile จากไฟล์ .ini แยก (เช่น system.ini.lab) แทนไฟล์จริง</summary>
    public void ApplyProfile(string profileName)
    {
        var targetFiles = new[] { "system.ini", "position.ini", "barcode.ini", "ui_amail.ini" };
        foreach (var file in targetFiles)
        {
            var targetPath = Path.Combine(_configDir, file);
            var profilePath = Path.Combine(_configDir, $"{file}.{profileName}");
            if (File.Exists(profilePath))
            {
                var backupPath = targetPath + $".bak_{DateTime.Now:yyyyMMddHHmmss}";
                if (File.Exists(targetPath))
                    File.Copy(targetPath, backupPath, true);
                File.Copy(profilePath, targetPath, true);
                _activeBackups[file] = (targetPath, backupPath);
            }
        }
    }

    /// <summary>Restore ไฟล์ที่ backup ไว้จาก ApplyProfile</summary>
    public void RestoreAll()
    {
        foreach (var kvp in _activeBackups)
        {
            var (targetPath, backupPath) = kvp.Value;
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, targetPath, true);
                try { File.Delete(backupPath); } catch { }
            }
        }
        _activeBackups.Clear();
    }
}