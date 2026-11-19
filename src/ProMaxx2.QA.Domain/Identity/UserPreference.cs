using System.Text.RegularExpressions;

namespace ProMaxx2.QA.Domain.Identity;

/// <summary>
/// ค่าที่ผู้ใช้ตั้งเองและต้องตามไปทุกเครื่อง (เช่น เมนูที่ปักหมุด / หน้าล่าสุดของ Sidebar) — 1 แถวต่อ 1 คีย์ต่อผู้ใช้
/// Value เป็น JSON ที่ frontend กำหนดรูปแบบเอง; backend ตรวจแค่ชื่อคีย์และขนาด
/// </summary>
public sealed partial class UserPreference
{
    public const int MaxKeyLength = 100;
    public const int MaxValueLength = 4000;

    private UserPreference() { }

    public Guid UserId { get; private set; }
    public string PreferenceKey { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public DateTime UpdatedAt { get; private set; }

    public static UserPreference Create(Guid userId, string key, string value, DateTime now)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User is required.");
        var preference = new UserPreference { UserId = userId, PreferenceKey = NormalizeKey(key) };
        preference.Update(value, now);
        return preference;
    }

    public void Update(string value, DateTime now)
    {
        if (string.IsNullOrEmpty(value)) throw new ArgumentException("ไม่มีค่าที่จะบันทึก");
        if (value.Length > MaxValueLength) throw new ArgumentException($"ค่าที่บันทึกยาวเกิน {MaxValueLength} ตัวอักษร");
        Value = value;
        UpdatedAt = now;
    }

    /// <summary>ชื่อคีย์: a-z 0-9 . _ - ขึ้นต้นด้วยตัวอักษร/ตัวเลข ยาวไม่เกิน 100 (เช่น sidebar)</summary>
    public static string NormalizeKey(string? key)
    {
        var trimmed = (key ?? string.Empty).Trim().ToLowerInvariant();
        if (!KeyPattern().IsMatch(trimmed)) throw new ArgumentException("ชื่อคีย์ไม่ถูกต้อง (ใช้ a-z 0-9 . _ - ไม่เกิน 100 ตัวอักษร)");
        return trimmed;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,99}$")]
    private static partial Regex KeyPattern();
}
