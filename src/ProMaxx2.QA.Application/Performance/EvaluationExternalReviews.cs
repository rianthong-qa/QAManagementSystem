using System.Security.Cryptography;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Performance;

namespace ProMaxx2.QA.Application.Performance;

/// <summary>
/// ผู้ประเมินจากแผนกอื่น — หัวหน้าตรงสร้างลิงก์ `/?er=&lt;code&gt;` ให้หัวหน้าแผนกอื่น 1 ลิงก์ต่อ 1 คน ประเมินได้โดยไม่ต้อง login
/// code สุ่ม 12 ตัว, มีวันหมดอายุ (≤ 60 วัน), ส่งได้ครั้งเดียว, ยกเลิกได้, ผู้ประเมินไม่เห็นคะแนนตนเอง/หัวหน้าตรง และบันทึก AuditLog ทุกขั้น
/// คะแนนที่ส่งแล้วถูกนำไปถ่วงน้ำหนักกับคะแนนหัวหน้าตรงตอนส่งผล (<see cref="Evaluation.CombineScores"/>)
/// </summary>
public sealed partial class EvaluationService
{
    public const string ExternalReviewAuditEntity = "EvaluationExternalReview";
    private const string CodeAlphabet = "23456789abcdefghjkmnpqrstuvwxyz";
    private const int CodeLength = 12;
    private static readonly int[] ExternalReviewLifetimes = [7, 14, 30, 60];

    public async Task<EvaluationExternalReviewDto> CreateExternalReviewAsync(PerformanceViewer viewer, Guid evaluationId, CreateExternalReviewRequest r, CancellationToken ct)
    {
        RequireManage(viewer);
        var e = await FindVisibleAsync(viewer, evaluationId, ct);
        await RequireSupervisorAsync(viewer, e, ct);
        if (e.IsClosed) throw new InvalidOperationException("การประเมินปิดแล้ว — เปิดใหม่ก่อนจึงจะขอให้แผนกอื่นประเมินได้");
        if (!ExternalReviewLifetimes.Contains(r.ExpiresInDays)) throw new ArgumentException($"อายุลิงก์ต้องเป็น {string.Join("/", ExternalReviewLifetimes)} วัน");
        var existing = await repository.ListExternalReviewsAsync(e.EvaluationId, ct);
        if (existing.Count(x => x.RevokedAt is null) >= EvaluationExternalReview.MaxPerEvaluation)
            throw new ArgumentException($"ขอให้แผนกอื่นประเมินได้สูงสุด {EvaluationExternalReview.MaxPerEvaluation} คนต่อรอบ");

        string code;
        do code = string.Concat(Enumerable.Range(0, CodeLength).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]));
        while (await repository.ExternalReviewCodeExistsAsync(code, ct));

        var now = DateTime.UtcNow;
        var review = new EvaluationExternalReview(e.EvaluationId, code, r.ReviewerName, r.ReviewerDepartment, r.Note, now.AddDays(r.ExpiresInDays), viewer.UserId, now);
        await repository.AddExternalReviewAsync(review, ct);
        performance.AddAudit(new AuditLog(viewer.UserId, "Created", ExternalReviewAuditEntity, review.EvaluationExternalReviewId.ToString(),
            $"สร้างลิงก์ให้ {review.ReviewerName}{(review.ReviewerDepartment is null ? "" : $" ({review.ReviewerDepartment})")} ประเมินรอบ {e.Round}/{e.Year} อายุ {r.ExpiresInDays} วัน", null, null, null));
        await performance.SaveChangesAsync(ct);
        return ExternalReviewDto(review, now, true);
    }

    public async Task<EvaluationExternalReviewDto> RevokeExternalReviewAsync(PerformanceViewer viewer, Guid evaluationId, Guid reviewId, CancellationToken ct)
    {
        RequireManage(viewer);
        var e = await FindVisibleAsync(viewer, evaluationId, ct);
        await RequireSupervisorAsync(viewer, e, ct);
        var review = await repository.FindExternalReviewAsync(reviewId, ct);
        if (review is null || review.EvaluationId != e.EvaluationId) throw new EntityNotFoundException("ไม่พบลิงก์ประเมิน");
        if (review.IsSubmitted) throw new InvalidOperationException("ผู้ประเมินส่งผลแล้ว ยกเลิกลิงก์ไม่ได้");
        var now = DateTime.UtcNow;
        if (review.RevokedAt is null)
        {
            review.Revoke(viewer.UserId, now);
            performance.AddAudit(new AuditLog(viewer.UserId, "Revoked", ExternalReviewAuditEntity, review.EvaluationExternalReviewId.ToString(), $"ยกเลิกลิงก์ประเมินของ {review.ReviewerName}", null, null, null));
            await performance.SaveChangesAsync(ct);
        }
        return ExternalReviewDto(review, now, true);
    }

    /// <summary>เปิดแบบฟอร์มจากลิงก์ (anonymous) — null = ไม่มี code นี้; ลิงก์ที่ส่งแล้วคืนสถานะ Submitted ให้หน้าเว็บแสดงคำขอบคุณ</summary>
    public async Task<ExternalReviewFormDto?> OpenExternalReviewAsync(string code, CancellationToken ct)
    {
        var (review, e) = await FindReviewForCodeAsync(code, ct);
        if (review is null || e is null) return null;
        var now = DateTime.UtcNow;
        if (!review.IsSubmitted)
        {
            EnsureUsable(review, e, now);
            review.RecordView(now);
            await performance.SaveChangesAsync(ct);
        }
        return await FormDtoAsync(review, e, now, ct);
    }

    public async Task<ExternalReviewFormDto?> SubmitExternalReviewAsync(string code, SubmitExternalReviewRequest r, CancellationToken ct)
    {
        var (review, e) = await FindReviewForCodeAsync(code, ct);
        if (review is null || e is null) return null;
        var now = DateTime.UtcNow;
        if (review.IsSubmitted) throw new InvalidOperationException("ส่งผลประเมินจากลิงก์นี้แล้ว");
        EnsureUsable(review, e, now);
        review.Submit(e, ToMap(r.Scores), r.Comment, now);
        // ไม่มีผู้ใช้ที่ login — บันทึกเป็นการกระทำของผู้ประเมินภายนอก (userId ว่าง) พร้อมชื่อที่หัวหน้าตรงระบุไว้
        performance.AddAudit(new AuditLog(null, "Submitted", ExternalReviewAuditEntity, review.EvaluationExternalReviewId.ToString(),
            $"{review.ReviewerName}{(review.ReviewerDepartment is null ? "" : $" ({review.ReviewerDepartment})")} ส่งผลประเมินรอบ {e.Round}/{e.Year} คะแนน {review.WeightedScore}", null, null, null));
        await performance.SaveChangesAsync(ct);
        return await FormDtoAsync(review, e, now, ct);
    }

    private async Task<(EvaluationExternalReview? Review, Evaluation? Evaluation)> FindReviewForCodeAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 16) return (null, null);
        var review = await repository.FindExternalReviewByCodeAsync(code.Trim(), ct);
        if (review is null) return (null, null);
        return (review, await repository.FindAsync(review.EvaluationId, ct));
    }

    private static void EnsureUsable(EvaluationExternalReview review, Evaluation e, DateTime now)
    {
        if (review.RevokedAt is not null) throw new ExternalReviewGoneException("ลิงก์นี้ถูกยกเลิกแล้ว กรุณาติดต่อผู้ส่งลิงก์หากต้องการประเมิน");
        if (e.IsClosed) throw new ExternalReviewGoneException("การประเมินรอบนี้ปิดแล้ว จึงส่งผลประเมินไม่ได้");
        if (review.ExpiresAt <= now) throw new ExternalReviewGoneException("ลิงก์นี้หมดอายุแล้ว กรุณาขอลิงก์ใหม่จากผู้ส่ง");
    }

    private async Task<ExternalReviewFormDto> FormDtoAsync(EvaluationExternalReview review, Evaluation e, DateTime now, CancellationToken ct)
    {
        var profile = await performance.FindProfileAsync(e.UserId, ct);
        var names = await repository.UserNamesAsync([e.UserId, review.CreatedBy], ct);
        var criteria = e.Scores.OrderBy(x => x.SortOrder).Select(x => new ExternalReviewCriterionDto(x.EvaluationScoreId, x.Group, x.Name, x.Description, x.Weight)).ToList();
        return new ExternalReviewFormDto(review.ReviewerName, review.ReviewerDepartment, review.Note, names.GetValueOrDefault(review.CreatedBy),
            names.GetValueOrDefault(e.UserId) ?? "-", profile?.Position, e.Type, e.Round, e.Year, e.PeriodStart, e.PeriodEnd, e.TemplateName,
            review.ExpiresAt, review.StatusAt(now), review.SubmittedAt, review.IsSubmitted ? [] : criteria);
    }

    private static EvaluationExternalReviewDto ExternalReviewDto(EvaluationExternalReview x, DateTime now, bool canManage)
    {
        var status = x.StatusAt(now);
        return new EvaluationExternalReviewDto(x.EvaluationExternalReviewId, x.ReviewerName, x.ReviewerDepartment, x.Note, status, x.ExpiresAt, x.CreatedAt, x.SubmittedAt,
            x.WeightedScore, x.Comment, x.ViewCount, canManage && status == "Pending" ? x.Code : null);
    }
}
