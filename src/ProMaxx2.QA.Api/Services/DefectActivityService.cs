using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed class DefectActivityService(QaDbContext db)
{
    public async Task<IReadOnlyList<DefectActivityDto>> GetActivitiesAsync(Guid defectId, CancellationToken ct) =>
        await db.DefectActivities.AsNoTracking().Where(x => x.DefectId == defectId).OrderByDescending(x => x.CreatedAt)
            .Select(x => new DefectActivityDto(x.DefectActivityId, x.DefectId, x.ActionType, x.Message, x.ActorUserId, x.CreatedAt)).ToListAsync(ct);

    public async Task AddCommentAsync(Guid defectId, string body, Guid? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("Comment is required.");
        await LogAsync(defectId, "Comment", body.Trim(), userId, ct);
    }

    public async Task DeleteCommentAsync(Guid defectId, Guid commentId, Guid? userId, CancellationToken ct)
    {
        var comment = await db.DefectActivities.SingleOrDefaultAsync(x => x.DefectActivityId == commentId && x.DefectId == defectId && x.ActionType == "Comment", ct);
        if (comment is null) return;
        db.DefectActivities.Remove(comment);
        await db.SaveChangesAsync(ct);
    }

    public async Task LogAsync(Guid defectId, string actionType, string message, Guid? userId, CancellationToken ct)
    {
        await db.DefectActivities.AddAsync(new DefectActivity(defectId, actionType, message, userId), ct);
        await db.SaveChangesAsync(ct);
    }
}
