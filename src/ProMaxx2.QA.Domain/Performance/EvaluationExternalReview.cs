namespace ProMaxx2.QA.Domain.Performance;

/// <summary>
/// ลิงก์ให้หัวหน้าแผนกอื่นประเมินพนักงานโดยไม่ต้อง login (`/?er=&lt;code&gt;`) — 1 ลิงก์ = ผู้ประเมิน 1 คนของการประเมิน 1 รอบ
/// ให้คะแนน 1–5 ทุกเกณฑ์ของรอบนั้น ส่งได้ครั้งเดียว มีวันหมดอายุเสมอ และยกเลิกได้ (ไม่ลบแถว เพื่อเก็บประวัติ)
/// คะแนนที่ส่งแล้วนำไปคำนวณผลรวมตอนหัวหน้าตรงส่งผล (ดู <see cref="Evaluation.CombineScores"/>)
/// </summary>
public sealed class EvaluationExternalReview
{
    public const int MaxPerEvaluation = 10;
    public const int MaxLifetimeDays = 60;

    private EvaluationExternalReview() { }

    public EvaluationExternalReview(Guid evaluationId, string code, string reviewerName, string? reviewerDepartment, string? note, DateTime expiresAt, Guid createdBy, DateTime createdAt)
    {
        if (evaluationId == Guid.Empty || string.IsNullOrWhiteSpace(code) || createdBy == Guid.Empty) throw new ArgumentException("Evaluation, code and creator are required.");
        if (expiresAt <= createdAt || expiresAt > createdAt.AddDays(MaxLifetimeDays)) throw new ArgumentException($"วันหมดอายุต้องอยู่ภายใน {MaxLifetimeDays} วัน");
        EvaluationExternalReviewId = Guid.NewGuid();
        EvaluationId = evaluationId;
        Code = code;
        ReviewerName = EvaluationTemplate.RequireText(reviewerName, 100, "ชื่อผู้ประเมิน");
        ReviewerDepartment = EvaluationTemplate.OptionalText(reviewerDepartment, 100, "แผนกของผู้ประเมิน");
        Note = EvaluationTemplate.OptionalText(note, 300, "ข้อความถึงผู้ประเมิน");
        ExpiresAt = expiresAt;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid EvaluationExternalReviewId { get; private set; }
    public Guid EvaluationId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string ReviewerName { get; private set; } = string.Empty;
    public string? ReviewerDepartment { get; private set; }
    /// <summary>ข้อความจากหัวหน้าตรงถึงผู้ประเมิน เช่น "ช่วยประเมินงานที่ทำร่วมกับทีม Dev ในไตรมาสนี้"</summary>
    public string? Note { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public Guid? RevokedBy { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    /// <summary>คะแนนรวมของผู้ประเมินคนนี้ (เฉลี่ยถ่วงน้ำหนักเกณฑ์) — คำนวณตอนส่ง</summary>
    public decimal? WeightedScore { get; private set; }
    public string? Comment { get; private set; }
    public int ViewCount { get; private set; }
    public DateTime? LastViewedAt { get; private set; }
    public ICollection<EvaluationExternalScore> Scores { get; private set; } = [];

    public bool IsSubmitted => SubmittedAt is not null;
    public bool IsUsable(DateTime utcNow) => RevokedAt is null && SubmittedAt is null && ExpiresAt > utcNow;

    public string StatusAt(DateTime utcNow) => SubmittedAt is not null ? "Submitted" : RevokedAt is not null ? "Revoked" : ExpiresAt <= utcNow ? "Expired" : "Pending";

    public void RecordView(DateTime utcNow)
    {
        ViewCount++;
        LastViewedAt = utcNow;
    }

    /// <summary>ส่งผลประเมิน — ต้องให้คะแนน 1–5 ครบทุกเกณฑ์ของรอบ; ส่งได้ครั้งเดียว</summary>
    public void Submit(Evaluation evaluation, IReadOnlyDictionary<Guid, (int? Score, string? Comment)> scores, string? comment, DateTime utcNow)
    {
        if (evaluation.EvaluationId != EvaluationId) throw new ArgumentException("ลิงก์นี้ไม่ใช่ของการประเมินนี้");
        if (SubmittedAt is not null) throw new InvalidOperationException("ส่งผลประเมินจากลิงก์นี้แล้ว");
        if (!IsUsable(utcNow) || evaluation.IsClosed) throw new InvalidOperationException("ลิงก์นี้ใช้ส่งผลประเมินไม่ได้แล้ว");
        // ตรวจครบทุกข้อก่อนแล้วค่อยเพิ่มคะแนน — ถ้าเพิ่มไประหว่างตรวจแล้วโยน error คะแนนข้อแรก ๆ จะค้างใน entity และซ้ำตอนส่งใหม่
        var given = new List<(Guid Id, int Score, string? Comment)>();
        foreach (var criterion in evaluation.Scores)
        {
            if (!scores.TryGetValue(criterion.EvaluationScoreId, out var v) || v.Score is null) throw new ArgumentException("ต้องให้คะแนนครบทุกเกณฑ์ก่อนส่ง");
            if (v.Score is < 1 or > 5) throw new ArgumentException("คะแนนต้องอยู่ระหว่าง 1–5");
            given.Add((criterion.EvaluationScoreId, v.Score.Value, EvaluationTemplate.OptionalText(v.Comment, 1000, "ความเห็น")));
        }
        var overall = EvaluationTemplate.OptionalText(comment, 2000, "ความเห็นภาพรวม");
        foreach (var (id, score, note) in given) Scores.Add(new EvaluationExternalScore(EvaluationExternalReviewId, id, score, note));
        Comment = overall;
        WeightedScore = evaluation.WeightedScore(given.ToDictionary(x => x.Id, x => x.Score));
        SubmittedAt = utcNow;
    }

    public void Revoke(Guid revokedBy, DateTime utcNow)
    {
        if (RevokedAt is not null || SubmittedAt is not null) return;
        RevokedAt = utcNow;
        RevokedBy = revokedBy;
    }
}

public sealed class EvaluationExternalScore
{
    private EvaluationExternalScore() { }

    public EvaluationExternalScore(Guid reviewId, Guid evaluationScoreId, int score, string? comment)
    {
        EvaluationExternalScoreId = Guid.NewGuid();
        EvaluationExternalReviewId = reviewId;
        EvaluationScoreId = evaluationScoreId;
        Score = score;
        Comment = comment;
    }

    public Guid EvaluationExternalScoreId { get; private set; }
    public Guid EvaluationExternalReviewId { get; private set; }
    /// <summary>เกณฑ์ของรอบ (แถว EvaluationScores)</summary>
    public Guid EvaluationScoreId { get; private set; }
    public int Score { get; private set; }
    public string? Comment { get; private set; }
}
