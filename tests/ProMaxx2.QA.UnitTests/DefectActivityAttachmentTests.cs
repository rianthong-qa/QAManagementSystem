using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;
namespace ProMaxx2.QA.UnitTests;

/// <summary>Activity "แนบรูปภาพ n ไฟล์" ต้องส่งรูปที่อัปโหลดในครั้งนั้นกลับไปให้หน้า Defect detail แสดงรูปย่อ</summary>
public sealed class DefectActivityAttachmentTests
{
    private static QaDbContext NewDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static DefectAttachment Image(Guid defectId, string name, Guid? uploadedBy, DateTime uploadedAt, Guid? commentId = null) =>
        new(Guid.NewGuid(), defectId, name, $"{Guid.NewGuid():N}_{name}", 8, "image/png", uploadedBy, uploadedAt, commentId);

    [Fact]
    public async Task Attachment_activity_returns_the_images_uploaded_with_it_but_not_comment_images()
    {
        using var db = NewDb();
        var defectId = Guid.NewGuid(); var user = Guid.NewGuid();
        var upload = new DefectActivity(defectId, "AttachmentAdded", "แนบรูปภาพ 2 ไฟล์", user);
        var comment = new DefectActivity(defectId, "Comment", "ดูรูปนี้", user);
        db.DefectActivities.AddRange(upload, comment, new DefectActivity(defectId, "Created", "Defect created", user));
        db.DefectAttachments.AddRange(Image(defectId, "a.png", user, upload.CreatedAt), Image(defectId, "b.png", user, upload.CreatedAt.AddSeconds(-1)),
            Image(defectId, "in-comment.png", user, upload.CreatedAt, comment.DefectActivityId));
        await db.SaveChangesAsync();

        var rows = await new DefectActivityService(db).GetActivitiesAsync(defectId, CancellationToken.None);

        var uploaded = rows.Single(x => x.ActivityType == "AttachmentAdded");
        Assert.Equal(["a.png", "b.png"], uploaded.Attachments!.Select(x => x.FileName).Order());
        Assert.All(rows.Where(x => x.ActivityType != "AttachmentAdded"), x => Assert.Null(x.Attachments));
    }

    [Fact]
    public async Task Images_from_another_uploader_or_far_from_the_activity_time_are_not_matched()
    {
        using var db = NewDb();
        var defectId = Guid.NewGuid(); var user = Guid.NewGuid();
        var upload = new DefectActivity(defectId, "AttachmentAdded", "แนบรูปภาพ 1 ไฟล์", user);
        db.DefectActivities.Add(upload);
        db.DefectAttachments.AddRange(Image(defectId, "other-user.png", Guid.NewGuid(), upload.CreatedAt), Image(defectId, "legacy.png", user, upload.CreatedAt.AddHours(-1)));
        await db.SaveChangesAsync();

        var rows = await new DefectActivityService(db).GetActivitiesAsync(defectId, CancellationToken.None);

        Assert.Null(rows.Single().Attachments);
    }
}
