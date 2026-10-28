using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Performance;
using ProMaxx2.QA.Domain.Performance;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class EvaluationRepository(QaDbContext db) : IEvaluationRepository
{
    private static readonly string[] OpenStatuses = ["Open", "SelfSubmitted"];

    public async Task<IReadOnlyList<EvaluationTemplate>> ListTemplatesAsync(CancellationToken ct)
        => await db.EvaluationTemplates.Include(x => x.Criteria).Where(x => x.IsActive).OrderBy(x => x.Type).ToListAsync(ct);

    // ดูของที่เพิ่มแล้วแต่ยังไม่ SaveChanges ก่อน — กันสร้างแบบประเมินตั้งต้นซ้ำใน request เดียว
    public async Task<EvaluationTemplate?> FindActiveTemplateAsync(string type, CancellationToken ct)
        => db.EvaluationTemplates.Local.FirstOrDefault(x => x.Type == type && x.IsActive)
            ?? await db.EvaluationTemplates.Include(x => x.Criteria).Where(x => x.Type == type && x.IsActive).OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);

    public Task<EvaluationTemplate?> FindTemplateAsync(Guid id, CancellationToken ct)
        => db.EvaluationTemplates.Include(x => x.Criteria).SingleOrDefaultAsync(x => x.EvaluationTemplateId == id, ct);

    public Task AddTemplateAsync(EvaluationTemplate template, CancellationToken ct) => db.EvaluationTemplates.AddAsync(template, ct).AsTask();

    public async Task<IReadOnlyList<Evaluation>> ListAsync(Guid? userId, bool openOnly, CancellationToken ct)
    {
        var q = db.Evaluations.AsNoTracking();
        if (userId is { } id) q = q.Where(x => x.UserId == id);
        if (openOnly) q = q.Where(x => OpenStatuses.Contains(x.Status));
        return await q.OrderBy(x => OpenStatuses.Contains(x.Status) ? 0 : 1).ThenBy(x => x.DueDate).ThenBy(x => x.CreatedAt).Take(500).ToListAsync(ct);
    }

    public Task<Evaluation?> FindAsync(Guid id, CancellationToken ct)
        => db.Evaluations.Include(x => x.Scores).SingleOrDefaultAsync(x => x.EvaluationId == id, ct);

    public async Task<IReadOnlyList<Evaluation>> FindByUserAsync(Guid userId, IReadOnlyCollection<string> rounds, CancellationToken ct)
        => await db.Evaluations.Where(x => x.UserId == userId && rounds.Contains(x.Round)).ToListAsync(ct);

    // รอบล่าสุดที่ปิดแล้ว (ตามช่วงประเมิน) — ผู้เรียกเรียงกลับเป็นเก่า → ใหม่เอง
    public async Task<IReadOnlyList<Evaluation>> ListClosedWithScoresAsync(Guid userId, int take, CancellationToken ct)
        => await db.Evaluations.AsNoTracking().Include(x => x.Scores)
            .Where(x => x.UserId == userId && (x.Status == "Completed" || x.Status == "Acknowledged") && x.OverallScore != null)
            .OrderByDescending(x => x.PeriodEnd).ThenByDescending(x => x.CompletedAt).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<Evaluation>> ListQuarterlyAsync(int year, CancellationToken ct)
        => await QuarterlyQuery(db, year).ToListAsync(ct);

    // แยกเป็น static เพื่อให้ unit test ตรวจการแปลงเป็น SQL ได้
    public static IQueryable<Evaluation> QuarterlyQuery(QaDbContext db, int year)
        => db.Evaluations.AsNoTracking().Where(x => x.Type == "Quarterly" && (x.Year == year || (x.Year == year - 1 && x.Round == "Q4")));

    public async Task<IReadOnlyCollection<Guid>> UsersWithRoundAsync(string round, int year, CancellationToken ct)
        => await db.Evaluations.AsNoTracking().Where(x => x.Round == round && x.Year == year).Select(x => x.UserId).Distinct().ToListAsync(ct);

    public Task AddAsync(Evaluation evaluation, CancellationToken ct) => db.Evaluations.AddAsync(evaluation, ct).AsTask();

    public async Task<IReadOnlyList<EvaluationExternalReview>> ListExternalReviewsAsync(Guid evaluationId, CancellationToken ct)
        => await db.EvaluationExternalReviews.Include(x => x.Scores).Where(x => x.EvaluationId == evaluationId).OrderBy(x => x.CreatedAt).ToListAsync(ct);

    public Task<EvaluationExternalReview?> FindExternalReviewAsync(Guid reviewId, CancellationToken ct)
        => db.EvaluationExternalReviews.Include(x => x.Scores).SingleOrDefaultAsync(x => x.EvaluationExternalReviewId == reviewId, ct);

    public Task<EvaluationExternalReview?> FindExternalReviewByCodeAsync(string code, CancellationToken ct)
        => db.EvaluationExternalReviews.Include(x => x.Scores).SingleOrDefaultAsync(x => x.Code == code, ct);

    public Task<bool> ExternalReviewCodeExistsAsync(string code, CancellationToken ct) => db.EvaluationExternalReviews.AnyAsync(x => x.Code == code, ct);

    public Task AddExternalReviewAsync(EvaluationExternalReview review, CancellationToken ct) => db.EvaluationExternalReviews.AddAsync(review, ct).AsTask();

    public async Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(x => x != Guid.Empty).Distinct().ToList();
        return await db.Users.AsNoTracking().Where(x => ids.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, x => x.DisplayName, ct);
    }
}
