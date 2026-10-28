namespace ProMaxx2.QA.Domain.Performance;

/// <summary>
/// ข้อความที่หัวหน้าเขียนประกอบ "สรุปประจำสัปดาห์" ที่ส่งผู้บริหาร — 1 แถวต่อ 1 สัปดาห์ (เริ่มวันจันทร์ตามเวลาไทย)
/// ตัวเลขผลงานไม่ถูกเก็บที่นี่ (คำนวณสดทุกครั้ง) เก็บเฉพาะส่วนที่คนเขียน: สถานะภาพรวม, สรุป, ความเสี่ยง, เรื่องที่ขอ, แผนสัปดาห์หน้า และความเห็นรายคน
/// </summary>
public sealed class WeeklyReportNote
{
    public const int MaxShortText = 500;
    public const int MaxLongText = 2000;
    public const int MaxMemberNote = 300;
    public static readonly string[] Healths = ["Green", "Yellow", "Red"];

    private WeeklyReportNote() { }

    public WeeklyReportNote(DateOnly weekStart, Guid createdBy, DateTime createdAt)
    {
        if (weekStart.DayOfWeek != DayOfWeek.Monday) throw new ArgumentException("สัปดาห์ต้องเริ่มวันจันทร์");
        if (createdBy == Guid.Empty) throw new ArgumentException("Creator is required.");
        WeeklyReportNoteId = Guid.NewGuid();
        WeekStart = weekStart;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedBy = createdBy;
        UpdatedAt = createdAt;
    }

    public Guid WeeklyReportNoteId { get; private set; }
    public DateOnly WeekStart { get; private set; }
    /// <summary>Green = ปกติ, Yellow = ต้องจับตา, Red = มีปัญหา — null = ยังไม่ระบุ</summary>
    public string? Health { get; private set; }
    public string? Headline { get; private set; }
    public string? KeyRisk { get; private set; }
    /// <summary>เรื่องที่ต้องให้ผู้บริหารตัดสินใจ/ช่วยเหลือ</summary>
    public string? Ask { get; private set; }
    public string? NextWeekPlan { get; private set; }
    /// <summary>ความเห็นหัวหน้ารายคน — JSON object { "userId": "ข้อความ" }</summary>
    public string? MemberNotesJson { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void Update(string? health, string? headline, string? keyRisk, string? ask, string? nextWeekPlan, IReadOnlyDictionary<Guid, string?>? memberNotes, Guid updatedBy, DateTime updatedAt)
    {
        var h = string.IsNullOrWhiteSpace(health) ? null : Healths.FirstOrDefault(x => x.Equals(health.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("สถานะภาพรวมต้องเป็น Green, Yellow หรือ Red");
        var notes = (memberNotes ?? new Dictionary<Guid, string?>())
            .Where(x => x.Key != Guid.Empty && !string.IsNullOrWhiteSpace(x.Value))
            .ToDictionary(x => x.Key, x => Clean(x.Value, MaxMemberNote, "ความเห็นรายคน")!);
        Health = h;
        Headline = Clean(headline, MaxShortText, "สรุปภาพรวม");
        KeyRisk = Clean(keyRisk, MaxLongText, "ความเสี่ยงหลัก");
        Ask = Clean(ask, MaxLongText, "เรื่องที่ต้องการให้ผู้บริหารช่วย");
        NextWeekPlan = Clean(nextWeekPlan, MaxLongText, "แผนสัปดาห์หน้า");
        MemberNotesJson = notes.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(notes);
        UpdatedBy = updatedBy;
        UpdatedAt = updatedAt;
    }

    public IReadOnlyDictionary<Guid, string> MemberNotes()
    {
        if (string.IsNullOrWhiteSpace(MemberNotesJson)) return new Dictionary<Guid, string>();
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<Guid, string>>(MemberNotesJson) ?? new Dictionary<Guid, string>(); }
        catch (System.Text.Json.JsonException) { return new Dictionary<Guid, string>(); }
    }

    private static string? Clean(string? value, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.Length > max) throw new ArgumentException($"{field}ยาวเกิน {max} ตัวอักษร");
        return text;
    }
}
