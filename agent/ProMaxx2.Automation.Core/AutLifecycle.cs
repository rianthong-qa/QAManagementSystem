using System.Diagnostics;

namespace ProMaxx2.Automation.Core;

/// <summary>AUT-AGT-003: Agent หลายตัวบนเครื่องเดียว (GUI เปิด Runner ของ POS และ App พร้อมกัน) ใช้ mouse/keyboard/หน้าจอชุดเดียวกัน
/// — ถ้ารันพร้อมกันจะกดแย่งกันจนผลทดสอบเชื่อถือไม่ได้. ใช้ไฟล์ล็อกแบบ exclusive: ถือไว้ตั้งแต่ก่อน claim งานจนงานจบ
/// และ Windows คืนล็อกให้อัตโนมัติเมื่อ process ตาย (ต่างจาก named semaphore ที่ค้างถาวรถ้า Runner crash)</summary>
public sealed class UiSessionLock : IDisposable
{
    private readonly FileStream _stream;
    private UiSessionLock(FileStream stream) => _stream = stream;

    public static string DefaultPath => Path.Combine(Path.GetTempPath(), "ProMaxx2.Automation.ui-session.lock");

    /// <summary>คืน null ทันทีถ้า Runner ตัวอื่นถือล็อกอยู่</summary>
    public static UiSessionLock? TryAcquire(string? path = null)
    {
        try
        {
            return new UiSessionLock(new FileStream(path ?? DefaultPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}

/// <summary>AUT-AGT-003: จัดการ instance ของ AUT (ProMaxx2) ที่ค้างอยู่ก่อนเริ่มงาน</summary>
public static class AutProcess
{
    /// <summary>instance ของ exe เดียวกัน (เทียบ full path; ถ้าอ่าน path ไม่ได้เทียบชื่อ process)</summary>
    public static IReadOnlyList<Process> FindInstances(string exePath)
    {
        var full = Path.GetFullPath(exePath);
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(full)))
        {
            string? path = null;
            try { path = process.MainModule?.FileName; } catch { /* access denied/exited — fall back to name match */ }
            if (path is null || string.Equals(Path.GetFullPath(path), full, StringComparison.OrdinalIgnoreCase)) result.Add(process);
            else process.Dispose();
        }
        return result;
    }

    /// <summary>ปิด instance ที่ค้าง (เช่น Runner รอบก่อน crash ระหว่างรัน) — ขอปิดแบบปกติก่อน ถ้าไม่ปิดภายใน
    /// <paramref name="grace"/> (เช่นมี dialog ถามยืนยัน) จึง kill ทั้ง process tree. คืนจำนวนที่ปิดได้</summary>
    public static int CloseInstances(string exePath, TimeSpan grace)
    {
        var closed = 0;
        foreach (var process in FindInstances(exePath))
        {
            using (process)
            {
                try
                {
                    if (process.HasExited) continue;
                    if (process.CloseMainWindow() && process.WaitForExit(grace)) { closed++; continue; }
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(TimeSpan.FromSeconds(5));
                    closed++;
                }
                catch (InvalidOperationException) { /* exited between checks */ }
            }
        }
        return closed;
    }
}
