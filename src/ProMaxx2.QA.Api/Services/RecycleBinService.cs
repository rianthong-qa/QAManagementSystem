using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>รายการที่ลบแล้ว (soft delete) ของหน้าต่าง ๆ — SYS_ADMIN ดูและกู้คืนได้</summary>
public sealed record DeletedItemDto(Guid Id, string? Code, string Title, string? Detail, Guid? ProjectId, string? ProjectName, DateTime? DeletedAt, string? DeletedByName);

/// <summary>กู้คืนไม่ได้ (เช่น ไฟล์ไม่อยู่บนดิสก์แล้ว หรือมีรายการเดียวกันที่ใช้งานอยู่)</summary>
public sealed class RestoreConflictException(string message) : Exception(message);

/// <summary>
/// ถังรายการที่ลบแล้วของ Test Case / Test Cycle / Requirement / Defect / Risk Acceptance / บทความ / เอกสารความรู้ / บันทึกงาน —
/// การลบในหน้าเหล่านี้เป็น soft delete ที่เก็บประวัติไว้ (รายงานไม่นับ) และกู้คืนจากที่นี่ได้
/// รายการที่ลบก่อนมีคอลัมน์ DeletedAt ใช้ UpdatedAt แทน (Test Cycle ไม่มี UpdatedAt จึงอาจไม่มีวันที่)
/// </summary>
public sealed class RecycleBinService(QaDbContext db, DefectActivityService defectActivity, KnowledgeFileService knowledgeFiles, KnowledgeIndexTrigger knowledgeTrigger)
{
    public static readonly string[] Types = ["test-cases", "test-cycles", "requirements", "defects", "risk-acceptances", "knowledge-articles", "knowledge-files", "work-logs"];
    private const int MaxRows = 500;

    private sealed record Row(Guid Id, string? Code, string Title, string? Detail, Guid? ProjectId, DateTime? DeletedAt, Guid? DeletedBy);

    public async Task<IReadOnlyList<DeletedItemDto>> ListAsync(string type, Guid? projectId, CancellationToken ct)
    {
        var rows = type switch
        {
            "test-cases" => await db.TestCases.AsNoTracking().Where(x => x.IsDeleted && (projectId == null || x.ProjectId == projectId))
                .Select(x => new Row(x.TestCaseId, x.TestCaseCode, x.Title, x.Status, x.ProjectId, x.DeletedAt ?? x.UpdatedAt, x.DeletedBy ?? x.UpdatedBy)).ToListAsync(ct),
            "test-cycles" => await db.TestCycles.AsNoTracking().Where(x => x.IsDeleted && (projectId == null || x.ProjectId == projectId))
                .Select(x => new Row(x.TestCycleId, x.CycleCode, x.CycleName, x.Status, x.ProjectId, x.DeletedAt, x.DeletedBy)).ToListAsync(ct),
            "requirements" => await db.Requirements.AsNoTracking().Where(x => x.IsDeleted && (projectId == null || x.ProjectId == projectId))
                .Select(x => new Row(x.RequirementId, x.RequirementCode, x.Title, x.Status, x.ProjectId, x.DeletedAt ?? x.UpdatedAt, x.DeletedBy ?? x.UpdatedBy)).ToListAsync(ct),
            "defects" => await db.Defects.AsNoTracking().Where(x => x.IsDeleted && (projectId == null || x.ProjectId == projectId))
                .Select(x => new Row(x.DefectId, x.DefectCode, x.Title, x.Status, x.ProjectId, x.DeletedAt ?? x.UpdatedAt, x.DeletedBy ?? x.UpdatedBy)).ToListAsync(ct),
            "risk-acceptances" => await db.RiskAcceptances.AsNoTracking().Where(x => x.IsDeleted && (projectId == null || x.ProjectId == projectId))
                .Select(x => new Row(x.RiskAcceptanceId, x.RiskCode, x.Title, x.Status, x.ProjectId, x.DeletedAt ?? x.UpdatedAt, x.DeletedBy ?? x.UpdatedBy)).ToListAsync(ct),
            "knowledge-articles" => await db.KnowledgeArticles.AsNoTracking().Where(x => x.IsDeleted && (projectId == null || x.ProjectId == null || x.ProjectId == projectId))
                .Select(x => new Row(x.KnowledgeArticleId, null, x.Title, x.Status, x.ProjectId, x.DeletedAt ?? x.UpdatedAt, x.DeletedBy ?? x.UpdatedBy)).ToListAsync(ct),
            "knowledge-files" => await db.KnowledgeFiles.AsNoTracking().Where(x => x.IsDeleted && (projectId == null || x.ProjectId == null || x.ProjectId == projectId))
                .Select(x => new Row(x.KnowledgeFileId, null, x.FileName, x.ImportPath != null ? "นำเข้าจากโฟลเดอร์" : null, x.ProjectId, x.DeletedAt ?? x.UpdatedAt, x.DeletedBy)).ToListAsync(ct),
            // บันทึกงานเป็นของแต่ละคน — Detail = เจ้าของ · วันที่
            "work-logs" => (await db.WorkLogEntries.AsNoTracking().Where(x => x.IsDeleted)
                .Select(x => new { x.WorkLogEntryId, x.Title, x.WorkDate, x.ProjectId, x.DeletedAt, x.DeletedBy, Owner = db.Users.Where(u => u.UserId == x.UserId).Select(u => u.DisplayName).FirstOrDefault() }).ToListAsync(ct))
                .Select(x => new Row(x.WorkLogEntryId, null, x.Title, $"{x.Owner} · {x.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", x.ProjectId, x.DeletedAt, x.DeletedBy)).ToList(),
            _ => throw new ArgumentException("ประเภทรายการไม่ถูกต้อง"),
        };
        rows = rows.OrderByDescending(x => x.DeletedAt ?? DateTime.MinValue).Take(MaxRows).ToList();
        var projectIds = rows.Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId!.Value).Distinct().ToList();
        var userIds = rows.Where(x => x.DeletedBy.HasValue).Select(x => x.DeletedBy!.Value).Distinct().ToList();
        var projects = await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.ProjectId)).ToDictionaryAsync(p => p.ProjectId, p => p.ProjectName, ct);
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.DisplayName, ct);
        return rows.Select(x => new DeletedItemDto(x.Id, x.Code, x.Title, x.Detail, x.ProjectId,
            x.ProjectId is { } p ? projects.GetValueOrDefault(p) : null, x.DeletedAt, x.DeletedBy is { } u ? users.GetValueOrDefault(u) : null)).ToList();
    }

    /// <summary>คืน false เมื่อไม่พบรายการที่ลบแล้ว</summary>
    public async Task<bool> RestoreAsync(string type, Guid id, Guid? userId, string? clientIp, CancellationToken ct)
    {
        string entityType, label;
        switch (type)
        {
            case "test-cases":
            {
                var e = await db.TestCases.FirstOrDefaultAsync(x => x.TestCaseId == id && x.IsDeleted, ct); if (e is null) return false;
                e.Restore(userId); (entityType, label) = ("TestCase", e.TestCaseCode); break;
            }
            case "test-cycles":
            {
                var e = await db.TestCycles.FirstOrDefaultAsync(x => x.TestCycleId == id && x.IsDeleted, ct); if (e is null) return false;
                e.Restore(); (entityType, label) = ("TestCycle", e.CycleCode); break;
            }
            case "requirements":
            {
                var e = await db.Requirements.FirstOrDefaultAsync(x => x.RequirementId == id && x.IsDeleted, ct); if (e is null) return false;
                e.Restore(userId); (entityType, label) = ("Requirement", e.RequirementCode); break;
            }
            case "defects":
            {
                var e = await db.Defects.FirstOrDefaultAsync(x => x.DefectId == id && x.IsDeleted, ct); if (e is null) return false;
                e.Restore(userId); (entityType, label) = ("Defect", e.DefectCode); break;
            }
            case "risk-acceptances":
            {
                var e = await db.RiskAcceptances.FirstOrDefaultAsync(x => x.RiskAcceptanceId == id && x.IsDeleted, ct); if (e is null) return false;
                e.Restore(userId); (entityType, label) = ("RiskAcceptance", e.RiskCode); break;
            }
            case "knowledge-articles":
            {
                var e = await db.KnowledgeArticles.FirstOrDefaultAsync(x => x.KnowledgeArticleId == id && x.IsDeleted, ct); if (e is null) return false;
                e.Restore(userId); (entityType, label) = ("KnowledgeArticle", e.Title); break;
            }
            case "knowledge-files":
            {
                var e = await db.KnowledgeFiles.FirstOrDefaultAsync(x => x.KnowledgeFileId == id && x.IsDeleted, ct); if (e is null) return false;
                // ไฟล์ที่ลบก่อนมีการกู้คืนถูกลบออกจากดิสก์ไปแล้ว
                if (!knowledgeFiles.StoredFileExists(e)) throw new RestoreConflictException("ไฟล์นี้ถูกลบออกจากดิสก์แล้ว (ลบก่อนระบบรองรับการกู้คืน) — อัปโหลดใหม่แทน");
                if (await db.KnowledgeFiles.AnyAsync(x => !x.IsDeleted && x.KnowledgeFileId != id && ((e.ImportPath != null && x.ImportPath == e.ImportPath) || (x.ContentHash == e.ContentHash && x.ProjectId == e.ProjectId)), ct))
                    throw new RestoreConflictException($"มีเอกสาร \"{e.FileName}\" เนื้อหาเดียวกันอยู่ในคลังความรู้แล้ว (อาจนำเข้าใหม่จากโฟลเดอร์)");
                e.Restore(); (entityType, label) = (KnowledgeFileService.AuditEntityType, e.FileName); break;
            }
            case "work-logs":
            {
                var e = await db.WorkLogEntries.FirstOrDefaultAsync(x => x.WorkLogEntryId == id && x.IsDeleted, ct); if (e is null) return false;
                e.Restore(); (entityType, label) = ("WorkLogEntry", e.Title); break;
            }
            default: throw new ArgumentException("ประเภทรายการไม่ถูกต้อง");
        }
        db.AuditLogs.Add(new AuditLog(userId, "Restored", entityType, id.ToString(), $"กู้คืน {label} จากรายการที่ลบแล้ว", null, null, clientIp));
        await db.SaveChangesAsync(ct);
        if (type == "defects") await defectActivity.LogAsync(id, "Restored", "Defect restored", userId, ct);
        // Defect / บทความ / เอกสาร อยู่ในคลังความรู้ — UpdatedAt ใหม่ทำให้ index กลับเข้าไป
        if (type is "defects" or "knowledge-articles" or "knowledge-files") knowledgeTrigger.Signal();
        return true;
    }
}
