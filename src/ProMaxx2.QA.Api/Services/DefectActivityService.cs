using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed partial class DefectActivityService(QaDbContext db)
{
    public async Task<IReadOnlyList<DefectActivityDto>> GetActivitiesAsync(Guid defectId, CancellationToken ct) =>
        await db.DefectActivities.AsNoTracking().Where(x => x.DefectId == defectId).OrderByDescending(x => x.CreatedAt)
            .Select(x => new DefectActivityDto(x.DefectActivityId, x.DefectId, x.ActionType, x.Message, x.ActorUserId, x.CreatedAt)).ToListAsync(ct);

    public async Task AddCommentAsync(Guid defectId, string body, Guid? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("Comment is required.");
        await LogAsync(defectId, "Comment", body.Trim(), userId, ct);
    }

    /// <summary>คอมเมนต์ทั้งหมดของ Defect (จาก QA Hub และที่ sync มาจาก CRM) เรียงเก่า → ใหม่ พร้อมชื่อผู้เขียนและรูปแนบของแต่ละคอมเมนต์</summary>
    public async Task<IReadOnlyList<DefectCommentDto>> GetCommentsAsync(Guid defectId, CancellationToken ct)
    {
        var rows = await db.DefectActivities.AsNoTracking().Where(x => x.DefectId == defectId && (x.ActionType == "Comment" || x.ActionType == "CrmComment"))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.DefectActivityId).ToListAsync(ct);
        var authorIds = rows.Where(x => x.ActorUserId != null).Select(x => x.ActorUserId!.Value).Distinct().ToList();
        var authors = await db.Users.AsNoTracking().Where(x => authorIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, x => x.DisplayName, ct);
        var images = (await db.DefectAttachments.AsNoTracking().Where(x => x.DefectId == defectId && x.CommentId != null).OrderBy(x => x.UploadedAt).ThenBy(x => x.FileName)
            .Select(x => new { CommentId = x.CommentId!.Value, Dto = new DefectAttachmentDto(x.DefectAttachmentId, x.FileName, x.SizeBytes) }).ToListAsync(ct))
            .GroupBy(x => x.CommentId).ToDictionary(g => g.Key, g => (IReadOnlyList<DefectAttachmentDto>)g.Select(x => x.Dto).ToList());
        return rows.Select(r =>
        {
            var attachments = images.GetValueOrDefault(r.DefectActivityId) ?? [];
            if (r.ActionType == "Comment") return new DefectCommentDto(r.DefectActivityId, "QaHub", r.Message, r.ActorUserId, (r.ActorUserId is { } a ? authors.GetValueOrDefault(a) : null) ?? "ผู้ใช้ QA Hub", r.CreatedAt, attachments);
            // CrmSyncService บันทึกเป็น "CRM Ticket #{id} — {posted}: {text}" — แยกผู้เขียนออกมาแสดงเหมือนคอมเมนต์ปกติ
            var m = CrmCommentPattern().Match(r.Message);
            return new DefectCommentDto(r.DefectActivityId, "Crm", m.Success ? m.Groups[2].Value : r.Message, null, m.Success ? $"CRM · {m.Groups[1].Value}" : "CRM", r.CreatedAt, attachments);
        }).ToList();
    }

    /// <summary>บันทึกคอมเมนต์ (ข้อความ และ/หรือ รูป) — รูปถูกเขียนลงดิสก์ก่อนแล้วบันทึก DB ครั้งเดียว ถ้า DB ล้มเหลวจะลบไฟล์ทิ้ง</summary>
    public async Task<Guid> AddCommentWithImagesAsync(Guid defectId, string? body, IReadOnlyList<DefectImageStorage.PendingImage> images, DefectImageStorage storage, Guid? userId, CancellationToken ct)
    {
        var text = body?.Trim() ?? "";
        if (text.Length == 0 && images.Count == 0) throw new ArgumentException("Comment is required.");
        var comment = new DefectActivity(defectId, "Comment", text, userId);
        db.DefectActivities.Add(comment);
        var attachments = images.Count == 0 ? [] : await storage.WriteAsync(defectId, images, userId, comment.DefectActivityId, ct);
        db.DefectAttachments.AddRange(attachments);
        try { await db.SaveChangesAsync(ct); }
        catch { storage.DeleteFiles(defectId, attachments.Select(x => x.StoredFileName)); throw; }
        return comment.DefectActivityId;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^CRM Ticket #\S+ — (.*?): (.*)$", System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex CrmCommentPattern();

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
