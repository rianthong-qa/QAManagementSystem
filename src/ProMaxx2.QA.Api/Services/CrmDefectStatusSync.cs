using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// ปรับสถานะ Defect ที่ผูกกับ CRM Ticket เมื่อ Dev จบงานใน CRM — Finish → Resolved (Fix Build = Build ล่าสุดของ Release)
/// ใช้ทั้งตอนอัปเดต Ticket จาก QA Hub (CrmController) และตอน CrmSyncWorker เห็นสถานะใหม่ (ปิดใน CRM โดยตรง)
/// Close ใน CRM ไม่ปิด Defect เอง — Closed หมายถึง QA ตรวจซ้ำบน Build ผ่านแล้ว (DEFECT_BUILD_LIFECYCLE.md) จึงบันทึกเป็น activity ให้ QA ตรวจซ้ำ
/// เปลี่ยนไปข้างหน้าเท่านั้น: เฉพาะ Defect ที่ยังเป็น Open/In Progress
/// </summary>
public static class CrmDefectStatusSync
{
    public static string? DefectStatusFor(string? crmStatus) => crmStatus?.Trim().ToUpperInvariant() switch
    {
        "FINISH" => "Resolved",
        _ => null,
    };

    public static bool IsCrmClose(string? crmStatus) => crmStatus?.Trim().ToUpperInvariant() is "CLOSE" or "CLOSED";

    /// <summary>คืนสถานะใหม่ของ Defect เมื่อมีการเปลี่ยน (null = ไม่เปลี่ยน) — ผู้เรียกต้อง SaveChanges เอง</summary>
    public static string? Apply(Defect defect, string? crmStatus, Guid? latestBuildId, Guid? actorUserId)
    {
        var target = DefectStatusFor(crmStatus);
        if (target is null || !Defect.UnresolvedStatuses.Contains(defect.Status)) return null;
        defect.TransitionTo(target, latestBuildId, null, actorUserId);
        return target;
    }

    /// <summary>ปรับสถานะ + บันทึก activity แบบเดียวกับการเปลี่ยนสถานะเองในหน้า Defect</summary>
    public static async Task<string?> ApplyAndLogAsync(QaDbContext db, Defect defect, string? crmStatus, Guid? actorUserId, DefectActivityService activity, CancellationToken ct)
    {
        var old = defect.Status;
        var latestBuildId = DefectStatusFor(crmStatus) is null ? null : await DefectBuilds.LatestActiveBuildIdAsync(db, defect.ReleaseId, ct);
        var changed = Apply(defect, crmStatus, latestBuildId, actorUserId);
        if (changed is not null)
            await activity.LogAsync(defect.DefectId, "StatusChanged",
                $"Status changed from {old} to {changed} (ตาม CRM Ticket #{defect.CrmTicketId} สถานะ {crmStatus?.Trim()} — Fix Build ตั้งอัตโนมัติเป็น Build ล่าสุดของ Release, QA แก้ได้ตอนตรวจซ้ำ)",
                actorUserId, ct, old, changed, defect.FixedInBuildId);
        else if (IsCrmClose(crmStatus) && defect.Status is not ("Closed" or "Rejected"))
            await activity.LogAsync(defect.DefectId, "CrmClosed",
                $"CRM Ticket #{defect.CrmTicketId} ปิดแล้ว — Defect ยังเป็น {defect.Status} รอ QA ตรวจซ้ำบน Build ก่อนปิด", actorUserId, ct);
        return changed;
    }
}
