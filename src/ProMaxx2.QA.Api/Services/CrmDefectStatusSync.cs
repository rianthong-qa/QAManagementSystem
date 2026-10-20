using ProMaxx2.QA.Domain.Defects;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// ปรับสถานะ Defect ที่ผูกกับ CRM Ticket ให้ตรงกับ CRM เมื่อ Ticket จบงาน — Finish → Resolved, Close → Closed
/// ใช้ทั้งตอนอัปเดต Ticket จาก QA Hub (CrmController) และตอน CrmSyncWorker เห็นสถานะใหม่ (ปิดใน CRM โดยตรง)
/// เปลี่ยนไปข้างหน้าเท่านั้น: ไม่ทับ Defect ที่ Closed แล้ว, ไม่ทับ Rejected (QA ตัดสินแล้วว่าไม่ใช่ Defect) และไม่ย้อน Closed → Resolved
/// </summary>
public static class CrmDefectStatusSync
{
    public static string? DefectStatusFor(string? crmStatus) => crmStatus?.Trim().ToUpperInvariant() switch
    {
        "FINISH" => "Resolved",
        "CLOSE" or "CLOSED" => "Closed",
        _ => null,
    };

    /// <summary>คืนสถานะใหม่ของ Defect เมื่อมีการเปลี่ยน (null = ไม่เปลี่ยน) — ผู้เรียกต้อง SaveChanges เอง</summary>
    public static string? Apply(Defect defect, string? crmStatus, Guid? actorUserId)
    {
        var target = DefectStatusFor(crmStatus);
        if (target is null || defect.Status == target || defect.Status is "Closed" or "Rejected") return null;
        defect.UpdateStatus(target, actorUserId);
        return target;
    }

    /// <summary>ปรับสถานะ + บันทึก activity แบบเดียวกับการเปลี่ยนสถานะเองในหน้า Defect</summary>
    public static async Task<string?> ApplyAndLogAsync(Defect defect, string? crmStatus, Guid? actorUserId, DefectActivityService activity, CancellationToken ct)
    {
        var old = defect.Status;
        var changed = Apply(defect, crmStatus, actorUserId);
        if (changed is not null)
            await activity.LogAsync(defect.DefectId, "StatusChanged", $"Status changed from {old} to {changed} (ตาม CRM Ticket #{defect.CrmTicketId} สถานะ {crmStatus?.Trim()})", actorUserId, ct);
        return changed;
    }
}
