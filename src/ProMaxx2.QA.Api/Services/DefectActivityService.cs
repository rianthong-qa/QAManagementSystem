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

    /// <summary>ลบคอมเมนต์ได้เฉพาะเจ้าของคอมเมนต์หรือ SYS_ADMIN และบันทึก activity "CommentDeleted" แทนเพื่อไม่ให้
    /// audit trail หายไปเงียบๆ. คืน <c>false</c> เมื่อผู้เรียกไม่มีสิทธิ์ลบคอมเมนต์นี้</summary>
    public async Task<bool> DeleteCommentAsync(Guid defectId, Guid commentId, Guid? userId, bool isAdmin, CancellationToken ct)
    {
        var comment = await db.DefectActivities.SingleOrDefaultAsync(x => x.DefectActivityId == commentId && x.DefectId == defectId && x.ActionType == "Comment", ct);
        if (comment is null) return true;
        if (!isAdmin && (userId is null || comment.ActorUserId != userId)) return false;
        db.DefectActivities.Remove(comment);
        await db.DefectActivities.AddAsync(new DefectActivity(defectId, "CommentDeleted", "ลบคอมเมนต์", userId), ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>บันทึก activity เดียวกันให้หลาย Defect ด้วย SaveChanges ครั้งเดียว (ใช้กับ bulk action)</summary>
    public async Task LogManyAsync(IEnumerable<Guid> defectIds, string actionType, string message, Guid? userId, CancellationToken ct)
    {
        await db.DefectActivities.AddRangeAsync(defectIds.Select(id => new DefectActivity(id, actionType, message, userId)), ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task LogAsync(Guid defectId, string actionType, string message, Guid? userId, CancellationToken ct)
    {
        await db.DefectActivities.AddAsync(new DefectActivity(defectId, actionType, message, userId), ct);
        await db.SaveChangesAsync(ct);
    }
}
