using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

// Phase 2 poller logic — called by CrmSyncWorker's timer loop every 2 minutes. Kept as a separate scoped service
// (not inline in the BackgroundService) so the timer/retry-on-failure plumbing and the actual poll logic stay
// independently testable/readable, same split as AutomationScheduleWorker/AutomationAgentService.
//
// Polls each linked ticket using the Defect's own Assignee's CRM login (not whoever originally sent it to CRM) —
// per-user credentials mean there's no single shared identity to poll with anymore, and the Assignee is the
// person who owns following up on this Defect right now.
public sealed class CrmSyncService(QaDbContext db, CrmApiClient crmApi, DefectActivityService activityService, EmailSenderService emailSender, ILogger<CrmSyncService> logger)
{
    public async Task PollLinkedDefectsAsync(CancellationToken ct)
    {
        var linked = await db.Defects.Where(x => x.CrmSyncStatus == "Linked" && x.CrmTicketId != null && !x.IsDeleted).ToListAsync(ct);
        foreach (var defect in linked)
        {
            if (defect.AssigneeUserId is not { } assigneeUserId) continue; // ไม่มี Assignee ใน QA Hub — ไม่รู้จะ poll ด้วย identity ไหน ข้ามไปก่อน
            try { await PollOneAsync(defect, assigneeUserId, ct); }
            catch (CrmIntegrationException ex) { logger.LogWarning(ex, "CRM poll failed for defect {DefectId} ticket {TicketId}", defect.DefectId, defect.CrmTicketId); }
            // Assignee คนนี้ยังไม่ได้ตั้งค่า/ปิดใช้งานบัญชี CRM ของตัวเอง — ข้าม Defect นี้ไปเฉยๆ ไม่ใช่หยุดทั้ง
            // tick เหมือนตอนใช้ Service Account กลาง เพราะ Defect อื่นอาจมี Assignee คนละคนที่ตั้งค่าไว้แล้วก็ได้
            catch (CrmNotConfiguredException) { continue; }
        }
    }

    private async Task PollOneAsync(Defect defect, Guid assigneeUserId, CancellationToken ct)
    {
        var job = await crmApi.GetJobDetailAsync(assigneeUserId, defect.CrmTicketId!, "HD", ct);
        var status = CrmApiClient.GetFieldAsString(job, "status");
        var assignto = CrmApiClient.GetFieldAsString(job, "assignto");
        var ownerSubjectId = CrmApiClient.GetFieldAsString(job, "ownerSubjectId");
        var statusChanged = status != (defect.CrmLastKnownStatus ?? "");
        var assigntoChanged = assignto != (defect.CrmLastKnownAssignto ?? "");

        if (statusChanged || assigntoChanged)
        {
            // ครั้งแรกหลังผูก ticket (ยังไม่เคย poll มาก่อน เลยไม่มี baseline ให้เทียบ) แค่ตั้งค่าเริ่มต้นเงียบๆ
            // ไม่นับเป็น "เปลี่ยน"/ไม่ log Activity/ไม่ส่งอีเมล — ไม่งั้นทุก Defect ที่เพิ่งผูกจะโดน log ทันทีที่ poll รอบแรก
            var isFirstPoll = defect.CrmLastKnownStatus is null && defect.CrmLastKnownAssignto is null;
            defect.UpdateCrmSnapshot(status, assignto);
            if (!isFirstPoll)
            {
                var parts = new List<string>();
                if (statusChanged) parts.Add($"Status: {status}");
                if (assigntoChanged) parts.Add($"Assignto: {assignto}");
                await activityService.LogAsync(defect.DefectId, "CrmStatusChanged", $"CRM Ticket #{defect.CrmTicketId} เปลี่ยน — {string.Join(", ", parts)}", null, ct);

                // Phase 3 trigger #2: assignto กลับไปเท่ากับ ownerSubjectId = CRM ส่งเคสกลับมาหาคนที่สร้าง ticket
                // (เจ้าของเรื่องฝั่ง QA Hub) — แจ้งเตือน Assignee ของ Defect นี้ใน QA Hub
                if (assigntoChanged && !string.IsNullOrWhiteSpace(assignto) && assignto == ownerSubjectId)
                {
                    await activityService.LogAsync(defect.DefectId, "CrmReturnedToOwner", $"CRM Ticket #{defect.CrmTicketId} ถูกส่งกลับมาหาเจ้าของเรื่อง", null, ct);
                    await NotifyReturnedToOwnerAsync(defect, ct);
                }
            }
        }

        // ต้องเช็คคอมเมนต์ใหม่ทุก tick แยกจาก status/assignto ข้างบน — คอมเมนต์เกิดขึ้นได้แม้ status/assignto
        // ไม่เปลี่ยนเลยก็ตาม (early-return ตัวเดิมก่อนหน้านี้จะพลาดจุดนี้ไปเลยถ้าลบไม่ครบ)
        await PollCommentsAsync(defect, assigneeUserId, ct);

        await db.SaveChangesAsync(ct); // no-op ราคาถูกถ้าไม่มีอะไรเปลี่ยนจริง (EF change tracker เช็คเองอยู่แล้ว)
    }

    // Phase 2 (CRM → QA Hub) comment sync — เทียบ answerNo ล่าสุดที่เคยเห็นกับที่ CRM มีตอนนี้ ถ้ามีรายการใหม่กว่า
    // ก็ log เป็น DefectActivity ทีละรายการ (เรียงจากเก่าไปใหม่) แล้วเลื่อน marker ไปข้างหน้า — เหมือน pattern
    // isFirstPoll ของ status/assignto ข้างบน: poll ครั้งแรกของ ticket ที่เพิ่งผูกจะไม่ log คอมเมนต์เก่าทั้งหมดย้อนหลัง
    // แค่ตั้ง baseline เงียบๆ ไว้ก่อน
    private async Task PollCommentsAsync(Defect defect, Guid assigneeUserId, CancellationToken ct)
    {
        var answers = await crmApi.GetHelpDeskAnswersAsync(assigneeUserId, defect.CrmTicketId!, ct);
        if (answers.Count == 0) return;

        var isFirstPoll = defect.CrmLastSeenAnswerNo is null;
        long? lastSeen = defect.CrmLastSeenAnswerNo is { } seen && long.TryParse(seen, out var seenNo) ? seenNo : null;

        var parsed = answers
            .Select(a => (Answer: a, No: long.TryParse(a.AnswerNo, out var no) ? no : (long?)null))
            .Where(x => x.No is not null)
            .OrderBy(x => x.No)
            .ToList();
        if (parsed.Count == 0) return;

        if (!isFirstPoll)
        {
            foreach (var (answer, no) in parsed)
            {
                if (no <= lastSeen) continue;
                var text = string.IsNullOrWhiteSpace(answer.Description) ? "[ไฟล์แนบ]" : answer.Description;
                await activityService.LogAsync(defect.DefectId, "CrmComment", $"CRM Ticket #{defect.CrmTicketId} — {answer.Posted}: {text}", null, ct);
            }
        }
        defect.UpdateCrmLastSeenAnswerNo(parsed[^1].No!.Value.ToString());
    }

    private async Task NotifyReturnedToOwnerAsync(Defect defect, CancellationToken ct)
    {
        if (defect.AssigneeUserId is null) return;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == defect.AssigneeUserId, ct);
        if (string.IsNullOrWhiteSpace(user?.Email)) return;
        try
        {
            var projectName = await db.Projects.AsNoTracking().Where(x => x.ProjectId == defect.ProjectId).Select(x => x.ProjectName).SingleOrDefaultAsync(ct) ?? "-";
            var moduleName = defect.ModuleId.HasValue
                ? await db.Modules.AsNoTracking().Where(x => x.ModuleId == defect.ModuleId).Select(x => x.ModuleName).SingleOrDefaultAsync(ct)
                : null;
            var link = $"https://bluesea.seniorsoft.com/bluesea/BookLicence/MA/Support/JobDetailsHD?JobNo={defect.CrmTicketId}&JobType=HD";
            var html = EmailTemplates.CrmReturnedToOwner(defect.DefectCode, defect.Title, defect.Severity, defect.Status, projectName, moduleName,
                defect.CrmLastKnownStatus, defect.CrmTicketId!, link);
            await emailSender.SendAsync(user.Email, $"[QA Hub] CRM Ticket #{defect.CrmTicketId} ถูกส่งกลับมาหาเจ้าของเรื่อง", html, ct, isHtml: true);
        }
        catch (Exception ex) { logger.LogError(ex, "Failed to send CRM-returned-to-owner email for defect {DefectId}", defect.DefectId); }
    }
}
