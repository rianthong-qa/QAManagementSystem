namespace ProMaxx2.QA.Domain.Performance;

/// <summary>แบบประเมิน (เกณฑ์ + น้ำหนัก) — การประเมินแต่ละครั้ง copy เกณฑ์ไปเก็บเอง จึงแก้แบบประเมินได้โดยไม่กระทบผลเดิม</summary>
public sealed class EvaluationTemplate
{
    public static readonly string[] Types = ["Probation", "Quarterly"];
    public static readonly string[] Groups = ["ปริมาณงาน", "คุณภาพงาน", "ความรู้และทักษะ", "พฤติกรรม", "การสื่อสาร"];
    public const int MaxCriteria = 15;

    private EvaluationTemplate() { }

    public EvaluationTemplate(string type, string name, Guid? createdBy)
    {
        EvaluationTemplateId = Guid.NewGuid();
        Type = Types.SingleOrDefault(x => x.Equals(type, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("ประเภทแบบประเมินไม่ถูกต้อง");
        Name = RequireText(name, 200, "ชื่อแบบประเมิน");
        IsActive = true;
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid EvaluationTemplateId { get; private set; }
    public string Type { get; private set; } = "Quarterly";
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public ICollection<EvaluationTemplateCriterion> Criteria { get; private set; } = [];

    /// <summary>แทนที่เกณฑ์ทั้งชุด — ต้องมี 1–15 ข้อ น้ำหนักแต่ละข้อ 1–100 และรวมได้ 100 พอดี</summary>
    public void ReplaceCriteria(string name, IReadOnlyList<(string Group, string Name, string? Description, int Weight)> criteria, Guid? updatedBy)
    {
        if (criteria.Count is 0 or > MaxCriteria) throw new ArgumentException($"แบบประเมินต้องมีเกณฑ์ 1–{MaxCriteria} ข้อ");
        if (criteria.Any(x => x.Weight is < 1 or > 100)) throw new ArgumentException("น้ำหนักแต่ละเกณฑ์ต้องอยู่ระหว่าง 1–100");
        if (criteria.Sum(x => x.Weight) != 100) throw new ArgumentException("น้ำหนักรวมของเกณฑ์ต้องเท่ากับ 100");
        Name = RequireText(name, 200, "ชื่อแบบประเมิน");
        Criteria.Clear();
        var order = 1;
        foreach (var c in criteria) Criteria.Add(new EvaluationTemplateCriterion(EvaluationTemplateId, c.Group, c.Name, c.Description, c.Weight, order++));
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    internal static string RequireText(string? value, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"ต้องระบุ{field}");
        var trimmed = value.Trim();
        if (trimmed.Length > max) throw new ArgumentException($"{field}ยาวเกิน {max} ตัวอักษร");
        return trimmed;
    }

    internal static string? OptionalText(string? value, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > max) throw new ArgumentException($"{field}ยาวเกิน {max} ตัวอักษร");
        return trimmed;
    }

    /// <summary>เกณฑ์ตั้งต้น — สร้างให้อัตโนมัติเมื่อยังไม่มีแบบประเมินที่ใช้งานของประเภทนั้น</summary>
    public static IReadOnlyList<(string Group, string Name, string? Description, int Weight)> DefaultCriteria(string type) => type == "Probation"
        ? [
            ("ปริมาณงาน", "ปริมาณงานที่ทำได้เทียบกับที่ได้รับมอบหมาย", "รันเทส/เขียน Test Case/ตรวจ Defect ได้ตามที่มอบหมาย", 15),
            ("คุณภาพงาน", "ความถูกต้องของการทดสอบและการรายงาน Defect", "Defect ชัดเจน ทำซ้ำได้ ไม่ถูก Reject บ่อย", 20),
            ("ความรู้และทักษะ", "ความเข้าใจระบบงานและเครื่องมือ QA", null, 15),
            ("ความรู้และทักษะ", "การเรียนรู้และพัฒนาตนเอง", "ความเร็วในการเรียนรู้ระบบและนำ feedback ไปปรับปรุง", 15),
            ("พฤติกรรม", "ความรับผิดชอบและการตรงต่อเวลา", null, 15),
            ("พฤติกรรม", "การปฏิบัติตามกระบวนการทำงานของทีม", null, 10),
            ("การสื่อสาร", "การสื่อสารและการทำงานร่วมกับทีม", null, 10),
        ]
        : [
            ("ปริมาณงาน", "ปริมาณงานและการส่งงานตามกำหนด", null, 20),
            ("คุณภาพงาน", "คุณภาพการทดสอบ (Test Case และ Defect ที่มีคุณภาพ)", null, 25),
            ("ความรู้และทักษะ", "ทักษะเทคนิค เช่น Automation, SQL, เครื่องมือทดสอบ", null, 15),
            ("พฤติกรรม", "ความรับผิดชอบ ความคิดริเริ่ม และการแก้ปัญหา", null, 15),
            ("พฤติกรรม", "การปฏิบัติตามกระบวนการทำงานของทีม", null, 10),
            ("การสื่อสาร", "การสื่อสารและการทำงานร่วมกับ Dev และทีมอื่น", null, 15),
        ];
}

public sealed class EvaluationTemplateCriterion
{
    private EvaluationTemplateCriterion() { }

    public EvaluationTemplateCriterion(Guid templateId, string group, string name, string? description, int weight, int sortOrder)
    {
        EvaluationTemplateCriterionId = Guid.NewGuid();
        EvaluationTemplateId = templateId;
        Group = EvaluationTemplate.RequireText(group, 50, "กลุ่มเกณฑ์");
        Name = EvaluationTemplate.RequireText(name, 200, "ชื่อเกณฑ์");
        Description = EvaluationTemplate.OptionalText(description, 500, "คำอธิบายเกณฑ์");
        Weight = weight;
        SortOrder = sortOrder;
    }

    public Guid EvaluationTemplateCriterionId { get; private set; }
    public Guid EvaluationTemplateId { get; private set; }
    public string Group { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int Weight { get; private set; }
    public int SortOrder { get; private set; }
}

/// <summary>
/// การประเมินหนึ่งรอบ — Open (พนักงานประเมินตนเอง/หัวหน้าร่างได้) → SelfSubmitted → Completed (หัวหน้าส่งผล) → Acknowledged (พนักงานรับทราบ)
/// รอบทดลองงาน: P30/P60/P90/PFinal · รอบไตรมาส: Q1–Q4
/// </summary>
public sealed class Evaluation
{
    public static readonly string[] ProbationRounds = ["P30", "P60", "P90", "PFinal"];
    public static readonly string[] Recommendations = ["Pass", "Extend", "NotPass"];

    private Evaluation() { }

    public Evaluation(Guid userId, string type, string round, int year, DateOnly periodStart, DateOnly periodEnd, DateOnly dueDate, EvaluationTemplate template, Guid? createdBy)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User is required.");
        if (periodEnd < periodStart) throw new ArgumentException("ช่วงประเมินไม่ถูกต้อง");
        EvaluationId = Guid.NewGuid();
        UserId = userId;
        Type = type;
        Round = round;
        Year = year;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        DueDate = dueDate;
        TemplateName = template.Name;
        Status = "Open";
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
        foreach (var c in template.Criteria.OrderBy(x => x.SortOrder))
            Scores.Add(new EvaluationScore(EvaluationId, c.Group, c.Name, c.Description, c.Weight, c.SortOrder));
    }

    public Guid EvaluationId { get; private set; }
    public Guid UserId { get; private set; }
    public string Type { get; private set; } = "Quarterly";
    public string Round { get; private set; } = string.Empty;
    public int Year { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string TemplateName { get; private set; } = string.Empty;
    public string Status { get; private set; } = "Open";
    public string? SelfComment { get; private set; }
    public DateTime? SelfSubmittedAt { get; private set; }
    public Guid? EvaluatorUserId { get; private set; }
    public decimal? OverallScore { get; private set; }
    public string? Grade { get; private set; }
    public string? Strengths { get; private set; }
    public string? Improvements { get; private set; }
    public string? NextGoals { get; private set; }
    public string? Recommendation { get; private set; }
    public DateOnly? ExtendToDate { get; private set; }
    public string? MetricsSnapshotJson { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? EmployeeComment { get; private set; }
    public DateTime? AcknowledgedAt { get; private set; }
    public string? ReopenReason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public ICollection<EvaluationScore> Scores { get; private set; } = [];

    public bool IsFinalProbation => Round == "PFinal";
    public bool IsClosed => Status is "Completed" or "Acknowledged";

    /// <summary>เลื่อนช่วง/กำหนดส่งของรอบที่ยังไม่ปิด (เช่น แก้วันเริ่มงาน)</summary>
    public void Reschedule(DateOnly periodStart, DateOnly periodEnd, DateOnly dueDate)
    {
        if (IsClosed) return;
        if (periodEnd < periodStart) throw new ArgumentException("ช่วงประเมินไม่ถูกต้อง");
        PeriodStart = periodStart; PeriodEnd = periodEnd; DueDate = dueDate; Year = periodEnd.Year;
    }

    public void SaveSelf(IReadOnlyDictionary<Guid, (int? Score, string? Comment)> scores, string? comment, bool submit, Guid actor)
    {
        if (Status != "Open") throw new InvalidOperationException("ส่งแบบประเมินตนเองแล้ว หรือการประเมินปิดแล้ว");
        foreach (var s in Scores)
            if (scores.TryGetValue(s.EvaluationScoreId, out var v)) s.SetSelf(v.Score, v.Comment);
        SelfComment = EvaluationTemplate.OptionalText(comment, 4000, "ความเห็นของพนักงาน");
        if (submit)
        {
            if (Scores.Any(x => x.SelfScore is null)) throw new ArgumentException("ต้องให้คะแนนตนเองครบทุกเกณฑ์ก่อนส่ง");
            Status = "SelfSubmitted";
            SelfSubmittedAt = DateTime.UtcNow;
        }
        Touch(actor);
    }

    public void SaveManager(IReadOnlyDictionary<Guid, (int? Score, string? Comment)> scores, string? strengths, string? improvements, string? nextGoals, string? recommendation, DateOnly? extendToDate, Guid actor)
    {
        if (IsClosed) throw new InvalidOperationException("การประเมินปิดแล้ว — ต้องเปิดใหม่ก่อนแก้ไข");
        if (actor == UserId) throw new InvalidOperationException("ประเมินผลตัวเองในฐานะหัวหน้าไม่ได้");
        foreach (var s in Scores)
            if (scores.TryGetValue(s.EvaluationScoreId, out var v)) s.SetManager(v.Score, v.Comment);
        Strengths = EvaluationTemplate.OptionalText(strengths, 4000, "จุดแข็ง");
        Improvements = EvaluationTemplate.OptionalText(improvements, 4000, "สิ่งที่ควรพัฒนา");
        NextGoals = EvaluationTemplate.OptionalText(nextGoals, 4000, "เป้าหมายรอบถัดไป");
        if (string.IsNullOrWhiteSpace(recommendation)) { Recommendation = null; ExtendToDate = null; }
        else
        {
            if (!IsFinalProbation) throw new ArgumentException("ผลการทดลองงานระบุได้เฉพาะรอบสรุปผล");
            Recommendation = Recommendations.SingleOrDefault(x => x.Equals(recommendation.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("ผลการทดลองงานไม่ถูกต้อง");
            ExtendToDate = Recommendation == "Extend" ? extendToDate : null;
        }
        EvaluatorUserId = actor;
        Touch(actor);
    }

    /// <summary>หัวหน้าส่งผล — ต้องให้คะแนนครบ, รอบสรุปผลต้องระบุผลทดลองงาน (ขยายต้องมีวันสิ้นสุดใหม่หลังวันสิ้นสุดเดิม)</summary>
    public void Complete(string metricsSnapshotJson, DateOnly currentProbationEnd, Guid actor)
    {
        if (IsClosed) throw new InvalidOperationException("การประเมินปิดแล้ว");
        if (actor == UserId) throw new InvalidOperationException("ประเมินผลตัวเองในฐานะหัวหน้าไม่ได้");
        if (Scores.Any(x => x.ManagerScore is null)) throw new ArgumentException("ต้องให้คะแนนครบทุกเกณฑ์ก่อนส่งผล");
        if (IsFinalProbation)
        {
            if (Recommendation is null) throw new ArgumentException("รอบสรุปผลต้องระบุผลการทดลองงาน (ผ่าน / ขยาย / ไม่ผ่าน)");
            if (Recommendation == "Extend" && (ExtendToDate is null || ExtendToDate <= currentProbationEnd)) throw new ArgumentException("การขยายทดลองงานต้องระบุวันสิ้นสุดใหม่ที่หลังวันสิ้นสุดเดิม");
        }
        var totalWeight = Scores.Sum(x => x.Weight);
        OverallScore = totalWeight == 0 ? 0 : Math.Round(Scores.Sum(x => (decimal)x.ManagerScore!.Value * x.Weight) / totalWeight, 2);
        Grade = GradeFor(OverallScore.Value);
        MetricsSnapshotJson = metricsSnapshotJson;
        EvaluatorUserId = actor;
        Status = "Completed";
        CompletedAt = DateTime.UtcNow;
        ReopenReason = null;
        Touch(actor);
    }

    public void Acknowledge(string? comment, Guid actor)
    {
        if (Status != "Completed") throw new InvalidOperationException("รับทราบได้เฉพาะการประเมินที่หัวหน้าส่งผลแล้ว");
        if (actor != UserId) throw new InvalidOperationException("ผู้ถูกประเมินเท่านั้นที่รับทราบผลได้");
        EmployeeComment = EvaluationTemplate.OptionalText(comment, 4000, "ความเห็นของพนักงาน");
        Status = "Acknowledged";
        AcknowledgedAt = DateTime.UtcNow;
        Touch(actor);
    }

    /// <summary>เปิดการประเมินที่ปิดแล้วกลับมาแก้ — ต้องมีเหตุผล (บันทึกใน AuditLog โดย service)</summary>
    public void Reopen(string reason, Guid actor)
    {
        if (!IsClosed) throw new InvalidOperationException("การประเมินยังไม่ปิด");
        ReopenReason = EvaluationTemplate.RequireText(reason, 1000, "เหตุผลที่เปิดใหม่");
        Status = SelfSubmittedAt is null ? "Open" : "SelfSubmitted";
        CompletedAt = null;
        AcknowledgedAt = null;
        OverallScore = null;
        Grade = null;
        Touch(actor);
    }

    public static string GradeFor(decimal score) => score switch
    {
        >= 4.5m => "A",
        >= 3.5m => "B",
        >= 2.5m => "C",
        >= 1.5m => "D",
        _ => "E",
    };

    private void Touch(Guid actor) { UpdatedAt = DateTime.UtcNow; UpdatedBy = actor; }
}

public sealed class EvaluationScore
{
    private EvaluationScore() { }

    public EvaluationScore(Guid evaluationId, string group, string name, string? description, int weight, int sortOrder)
    {
        EvaluationScoreId = Guid.NewGuid();
        EvaluationId = evaluationId;
        Group = group;
        Name = name;
        Description = description;
        Weight = weight;
        SortOrder = sortOrder;
    }

    public Guid EvaluationScoreId { get; private set; }
    public Guid EvaluationId { get; private set; }
    public string Group { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int Weight { get; private set; }
    public int SortOrder { get; private set; }
    public int? SelfScore { get; private set; }
    public string? SelfComment { get; private set; }
    public int? ManagerScore { get; private set; }
    public string? ManagerComment { get; private set; }

    internal void SetSelf(int? score, string? comment) { SelfScore = Validate(score); SelfComment = EvaluationTemplate.OptionalText(comment, 1000, "ความเห็น"); }
    internal void SetManager(int? score, string? comment) { ManagerScore = Validate(score); ManagerComment = EvaluationTemplate.OptionalText(comment, 1000, "ความเห็น"); }

    private static int? Validate(int? score) => score is null or (>= 1 and <= 5) ? score : throw new ArgumentException("คะแนนต้องอยู่ระหว่าง 1–5");
}

/// <summary>
/// อีเมลเตือนการประเมินที่ส่งไปแล้ว — กันส่งซ้ำด้วย (EvaluationId, Kind, DueDate, RecipientUserId)
/// DueDate อยู่ใน key เพื่อให้รอบที่ถูกเลื่อนกำหนดส่ง (เช่น แก้วันเริ่มงาน) ได้รับการเตือนใหม่ตามวันใหม่
/// </summary>
public sealed class EvaluationNotification
{
    public static readonly string[] Kinds = ["DueSoon", "Overdue"];

    private EvaluationNotification() { }

    public EvaluationNotification(Guid evaluationId, string kind, DateOnly dueDate, Guid recipientUserId, DateTime sentAt)
    {
        EvaluationNotificationId = Guid.NewGuid();
        EvaluationId = evaluationId;
        Kind = Kinds.Contains(kind) ? kind : throw new ArgumentException("ประเภทการแจ้งเตือนไม่ถูกต้อง");
        DueDate = dueDate;
        RecipientUserId = recipientUserId;
        SentAt = sentAt;
    }

    public Guid EvaluationNotificationId { get; private set; }
    public Guid EvaluationId { get; private set; }
    public string Kind { get; private set; } = "DueSoon";
    public DateOnly DueDate { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public DateTime SentAt { get; private set; }
}
