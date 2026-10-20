namespace ProMaxx2.QA.Domain.Performance;

/// <summary>ข้อมูลพนักงานที่ User ไม่มี (วันเริ่มงาน, สถานะทดลองงาน, หัวหน้า) — 1:1 กับ Users</summary>
public sealed class EmployeeProfile
{
    /// <summary>ช่วงทดลองงานมาตรฐาน 119 วัน (วันที่ 1 = วันเริ่มงาน)</summary>
    public const int ProbationDays = 119;
    public static readonly string[] Statuses = ["Probation", "Extended", "Permanent", "Resigned"];
    public static readonly string[] Levels = ["Junior", "Mid", "Senior", "Lead"];

    private EmployeeProfile() { }

    public EmployeeProfile(Guid userId, Guid? createdBy)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User is required.");
        UserId = userId;
        EmploymentStatus = "Permanent";
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }
    public string? EmployeeCode { get; private set; }
    public string? Position { get; private set; }
    public string? Level { get; private set; }
    public Guid? SupervisorUserId { get; private set; }
    public DateOnly? HireDate { get; private set; }
    public string EmploymentStatus { get; private set; } = "Permanent";
    public DateOnly? ProbationEndDate { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public bool IsOnProbation => EmploymentStatus is "Probation" or "Extended";

    public void Update(string? employeeCode, string? position, string? level, Guid? supervisorUserId, DateOnly? hireDate, string employmentStatus, DateOnly? probationEndDate, string? notes, Guid? updatedBy)
    {
        var status = Statuses.SingleOrDefault(x => x.Equals(employmentStatus?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("สถานะพนักงานไม่ถูกต้อง");
        var normalizedLevel = string.IsNullOrWhiteSpace(level) ? null : Levels.SingleOrDefault(x => x.Equals(level.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("ระดับพนักงานไม่ถูกต้อง");
        if (supervisorUserId == UserId) throw new ArgumentException("หัวหน้าต้องไม่ใช่ตัวพนักงานเอง");
        if (status is "Probation" or "Extended" && hireDate is null) throw new ArgumentException("พนักงานทดลองงานต้องระบุวันเริ่มงาน");
        if (probationEndDate is { } end && hireDate is { } start && end < start) throw new ArgumentException("วันสิ้นสุดทดลองงานต้องไม่ก่อนวันเริ่มงาน");

        EmployeeCode = Clean(employeeCode, 50);
        Position = Clean(position, 100);
        Level = normalizedLevel;
        SupervisorUserId = supervisorUserId == Guid.Empty ? null : supervisorUserId;
        HireDate = hireDate;
        EmploymentStatus = status;
        ProbationEndDate = hireDate is { } h && status is "Probation" or "Extended" ? probationEndDate ?? DefaultProbationEnd(h) : probationEndDate;
        Notes = Clean(notes, 2000);
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>ผลรอบสรุปทดลองงาน = ผ่าน</summary>
    public void ConfirmPermanent(Guid? updatedBy)
    {
        EmploymentStatus = "Permanent";
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>ผลรอบสรุปทดลองงาน = ขยาย — วันสิ้นสุดใหม่ต้องหลังวันสิ้นสุดเดิม</summary>
    public void ExtendProbation(DateOnly newEndDate, Guid? updatedBy)
    {
        if (HireDate is null) throw new InvalidOperationException("ต้องมีวันเริ่มงานก่อนขยายทดลองงาน");
        if (ProbationEndDate is { } end && newEndDate <= end) throw new ArgumentException("วันสิ้นสุดใหม่ต้องหลังวันสิ้นสุดเดิม");
        EmploymentStatus = "Extended";
        ProbationEndDate = newEndDate;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public static DateOnly DefaultProbationEnd(DateOnly hireDate) => hireDate.AddDays(ProbationDays - 1);

    /// <summary>วันที่เท่าไรของการทดลองงาน (วันเริ่มงาน = 1) — null เมื่อไม่ได้อยู่ในช่วงทดลองงาน</summary>
    public int? ProbationDay(DateOnly today) => IsOnProbation && HireDate is { } h ? Math.Max(1, today.DayNumber - h.DayNumber + 1) : null;

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > max) throw new ArgumentException($"ข้อความยาวเกิน {max} ตัวอักษร");
        return trimmed;
    }
}
