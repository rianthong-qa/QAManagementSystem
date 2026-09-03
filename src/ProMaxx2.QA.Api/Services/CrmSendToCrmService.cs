using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

// Orchestrates "ส่งไป CRM": resolves the project's CRM Product/Version mapping + the "Bug" SysServiceType, builds
// the locked field-mapping payload, and calls CrmApiClient to create the ticket. Deliberately has no
// SaveChangesAsync of its own — persisting the result on the Defect row and logging the DefectActivity stays in
// DefectsController, matching this codebase's "controller drives SaveChangesAsync, no repository layer" convention.
//
// Every method takes an `actingUserId` — CRM tracks work by whoever is actually logged in, so every call talks to
// CRM as the QA Hub user who clicked the button (their own CrmConfiguration row), never a shared Service Account.
public sealed class CrmSendToCrmService(QaDbContext db, CrmApiClient crmApi, CrmConfigurationService crmConfig, CrmTokenService tokenService, EmailSenderService emailSender, ILogger<CrmSendToCrmService> logger)
{
    // BlueID's user directory (DWUserAccountSeniorV2) returns the whole company, most of whom are irrelevant to
    // CRM ticket assignment — จำกัดรายชื่อ "ผู้รับผิดชอบ (Dev)" ให้เหลือแค่ทีม Dev/QA ที่เกี่ยวข้องจริง (รหัสพนักงาน)
    private static readonly HashSet<string> AllowedDevStaffCodes =
    [
        "6101", "6619", "6610", "6914", "6915", "4208", "5636", "5640",
        "5834", "6305", "6318", "6529", "6620", "6913",
    ];

    // CRM's own "New Job" form has maxlength="1000" on this field — truncate our side too instead of letting
    // CRM silently cut it off mid-sentence with no indication anything was lost.
    private const int CrmDescriptionMaxLength = 1000;
    private const string TruncationNotice = "\n\n[...ตัดข้อความ ดูรายละเอียดเต็มที่ QA Hub]";

    public async Task<string> SendAsync(Defect defect, Guid actingUserId, string actingDisplayName, string assignToStaffCode, CancellationToken ct)
    {
        var mapping = await db.CrmProjectMappings.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == defect.ProjectId, ct)
            ?? throw new CrmIntegrationException("โปรเจกต์นี้ยังไม่ได้ตั้งค่า CRM Product/Version Mapping กรุณาตั้งค่าใน Setting Center ก่อนส่งไป CRM");
        var (cfg, password) = await crmConfig.GetRuntimeAsync(actingUserId, ct);
        var serviceTypeId = await crmApi.ResolveBugServiceTypeIdAsync(actingUserId, ct);
        var followupId = await crmApi.ResolveDefaultFollowupIdAsync(actingUserId, ct);
        // resolver ทั้งสองตัวข้างบนเรียก AuthorizedAsync ไปแล้วอย่างน้อย 1 ครั้ง เลย token ของ actingUserId ถูก
        // cache ไว้แล้ว — เรียกซ้ำตรงนี้แค่เพื่อดึง BranchId ออกมา ไม่ได้ยิง login ใหม่ (cache hit)
        var (_, branchId) = await tokenService.GetTokenAsync(actingUserId, cfg.MerchantId, cfg.Username, password, ct);
        var description = TruncateForCrm(string.Join("\n\n", new[] { defect.Description, defect.StepsToReproduce, defect.ExpectedResult, defect.ActualResult }.Where(x => !string.IsNullOrWhiteSpace(x))));
        // CRM ไม่มีค่า default ให้ฟิลด์นี้ (ไม่เคยส่งมาก่อนจะกลาย epoch 0 → โชว์เป็น 01/01/2513) ต้องส่งเวลาปัจจุบัน
        // เสมอ — ใช้เวลาไทย (UTC+7) ตรงกับ format ที่หน้า CRM เองส่ง (yyyy-M-d'T'HH:mm ปีเป็น ค.ศ. ไม่ padding เดือน/วัน)
        var nowThai = DateTime.UtcNow.AddHours(7);
        var contactDate = $"{nowThai:yyyy}-{nowThai.Month}-{nowThai.Day}T{nowThai:HH:mm}";
        // Duedate เดียวกับปัญหา ContactDate ข้างบน — ไม่เคยส่งมาก่อนจะกลาย epoch 0 ค่าเริ่มต้นคือวันนี้เวลาเที่ยงคืน
        var dueDate = $"{nowThai:yyyy}-{nowThai.Month}-{nowThai.Day}T00:00:00";

        // ตอนนี้แต่ละคน login ด้วยบัญชี CRM ของตัวเอง — cfg.Username (รหัสพนักงานจริงของคนที่กดปุ่ม) จึงใช้แทนที่
        // ได้ทั้ง Member/RecipientId/OwnerSubjectId/Posted อย่างสม่ำเสมอ (แต่ก่อนต้องผสมกับ QA Hub Username เพราะ
        // ใช้ Service Account กลาง คนละบัญชีกับคนที่กดปุ่มจริง)
        var payload = new CrmCreateJobPayload(
            Subject: defect.Title,
            Member: cfg.Username,
            FName: actingDisplayName,
            LName: "",
            Tel: "999",
            Email: "",
            SysCustomerType: "1",
            RecipientId: cfg.Username,
            OwnerSubjectId: cfg.Username,
            Assignto: assignToStaffCode,
            SysDevelop: assignToStaffCode,
            Status: "Continue",
            // ticket ทุกใบที่ QA Hub สร้างมาจากการรับแจ้งบั๊กทางโทรศัพท์/แชทภายในทีม ไม่ใช่ลูกค้า remote เข้ามาโดยตรง
            Source: "Call",
            // BranchId ที่ CRM ต้องการคือรหัสสาขาของ user ที่ login เข้าไปจริง (claim "branchid" ใน JWT เอง) ไม่ใช่ค่าคงที่
            BranchId: branchId ?? "00000",
            SysserViceType: serviceTypeId,
            // CRM bind field พวกนี้เป็น number ฝั่งเขา — ส่ง "" ไม่ได้ (validation error "The value '' is invalid.")
            // SysFollowupId ต้อง resolve ID จริงจาก /Support/Followup (ไม่มี "0/ไม่ระบุ" ให้ใช้ และตัวเลข 1-5
            // ที่เคยเดาไว้ก็ไม่ตรงกับ FK จริง — ดู ResolveDefaultFollowupIdAsync)
            SysFollowupId: followupId,
            SysProductId: mapping.CrmProductId,
            SysVersionId: mapping.CrmVersionId ?? "0",
            SysOsId: "0",
            Description: description,
            Posted: cfg.Username,
            JobType: "HD",
            ContactDate: contactDate,
            Duedate: dueDate);

        var jobNo = await crmApi.CreateSupportJobAsync(actingUserId, payload, ct);

        // Phase 3 trigger #1: แจ้ง Dev ที่ถูก assign ทางอีเมล — best-effort เท่านั้น ห้ามทำให้การสร้าง ticket ที่
        // สำเร็จไปแล้วกลายเป็นล้มเหลวเพราะ SMTP มีปัญหา (ดู EmailSenderService — เมล์ไม่ได้ตั้งค่าไว้ก็แค่ throw
        // EmailNotConfiguredException ให้ catch เงียบๆ ตรงนี้)
        try
        {
            var dev = (await GetAssignableUsersAsync(actingUserId, ct)).FirstOrDefault(x => x.StaffCode == assignToStaffCode);
            if (dev?.Email is { } email && !string.IsNullOrWhiteSpace(email))
            {
                var link = $"https://bluesea.seniorsoft.com/bluesea/BookLicence/MA/Support/JobDetailsHD?JobNo={jobNo}&JobType=HD";
                var projectName = await db.Projects.AsNoTracking().Where(x => x.ProjectId == defect.ProjectId).Select(x => x.ProjectName).SingleOrDefaultAsync(ct) ?? "-";
                var moduleName = defect.ModuleId.HasValue
                    ? await db.Modules.AsNoTracking().Where(x => x.ModuleId == defect.ModuleId).Select(x => x.ModuleName).SingleOrDefaultAsync(ct)
                    : null;
                var html = EmailTemplates.DefectAssignedViaCrm(defect.DefectCode, defect.Title, defect.Severity, defect.Status, projectName, moduleName,
                    defect.Description, defect.StepsToReproduce, defect.ExpectedResult, defect.ActualResult, dev.Name, dev.StaffCode, jobNo, link);
                await emailSender.SendAsync(email, $"[QA Hub] มอบหมายงานใหม่ผ่าน CRM Ticket #{jobNo}", html, ct, isHtml: true);
            }
        }
        catch (Exception ex) { logger.LogError(ex, "Failed to send CRM-assignment email for defect {DefectId} ticket {TicketId}", defect.DefectId, jobNo); }

        return jobNo;
    }

    public async Task<IReadOnlyList<BlueIdUserDto>> GetAssignableUsersAsync(Guid actingUserId, CancellationToken ct)
    {
        var all = await crmApi.GetSeniorUserDirectoryAsync(actingUserId, ct);
        return all.Where(x => AllowedDevStaffCodes.Contains(x.StaffCode)).ToList();
    }

    // CRM ไม่มี endpoint "เพิ่มโน้ต/ตอบกลับ" แยกต่างหาก (HelpDeskAnswerMain เป็น GET อย่างเดียวในหน้า JobDetailsHD) —
    // กลไกเดียวที่มีคือปุ่ม Update ของ CRM เอง ซึ่ง PUT /Support ทั้งใบทับของเดิม จึงต้อง GET job ปัจจุบันมาก่อน
    // แล้ว carry-over ทุก field เดิมกลับไป ยกเว้น Description ที่ต่อท้ายด้วยคอมเมนต์ใหม่ — เรียกจาก
    // DefectsController.AddComment แบบ best-effort เท่านั้น (ล้มเหลวได้โดยไม่ทำให้คอมเมนต์ใน QA Hub หายไปด้วย)
    public async Task AppendCommentAsync(Defect defect, Guid actingUserId, string commentBody, string commentAuthorDisplayName, CancellationToken ct)
    {
        var ticketId = defect.CrmTicketId;
        if (string.IsNullOrWhiteSpace(ticketId)) return;
        var (cfg, _) = await crmConfig.GetRuntimeAsync(actingUserId, ct);
        var job = await crmApi.GetJobDetailAsync(actingUserId, ticketId, "HD", ct);
        var nowThai = DateTime.UtcNow.AddHours(7);
        var note = $"[QA Hub] {commentAuthorDisplayName} ({nowThai:dd/MM/yyyy HH:mm}): {commentBody.Trim()}";
        var newDescription = AppendBoundedDescription(CrmApiClient.GetFieldAsString(job, "description"), note);

        var payload = new CrmUpdateJobPayload(
            JobNo: ticketId,
            Subject: CrmApiClient.GetFieldAsString(job, "subject"),
            Member: CrmApiClient.GetFieldAsString(job, "member"),
            SysCustomerType: CrmApiClient.GetFieldAsString(job, "sysCustomerType"),
            FName: CrmApiClient.GetFieldAsString(job, "fname"),
            LName: CrmApiClient.GetFieldAsString(job, "lname"),
            NickName: CrmApiClient.GetFieldAsString(job, "nickName"),
            Fax: CrmApiClient.GetFieldAsString(job, "fax"),
            Tel: CrmApiClient.GetFieldAsString(job, "tel"),
            Email: CrmApiClient.GetFieldAsString(job, "email"),
            Assignto: CrmApiClient.GetFieldAsString(job, "assignto"),
            RecipientId: CrmApiClient.GetFieldAsString(job, "recipientId"),
            OwnerSubjectId: CrmApiClient.GetFieldAsString(job, "ownerSubjectId"),
            Status: CrmApiClient.GetFieldAsString(job, "status"),
            Source: CrmApiClient.GetFieldAsString(job, "source"),
            RefJobNo: CrmApiClient.GetFieldAsString(job, "refjobNo"),
            // ฟอร์ม Update ของ CRM เองส่งค่าคงที่ "00000" เสมอ ไม่เคยอ่านจาก job ที่ดึงมา (#UpdateBranch เป็น
            // hidden input value="00000" ตายตัวในหน้า JobDetailsHD ไม่ใช่ field ที่ผูกกับข้อมูลใดๆ)
            SysBranchId: "00000",
            Description: newDescription,
            SysserViceType: CrmApiClient.GetFieldAsString(job, "sysserviceType"),
            SysProductId: CrmApiClient.GetFieldAsString(job, "sysProductId"),
            // ยังไม่เคยเจอ Defect ที่มี Duedate ตั้งไว้จริง (Phase 1 create flow ไม่ส่ง Duedate) — ส่งค่าดิบที่ CRM
            // คืนมาตรงๆ กลับไปเพื่อไม่ให้เผลอไปเคลียร์ค่าที่ CRM staff อาจตั้งไว้เองทีหลัง ยังไม่ได้ทดสอบ round-trip จริง
            Duedate: CrmApiClient.GetFieldAsString(job, "duedate"),
            SysVersionId: CrmApiClient.GetFieldAsString(job, "sysVersionId"),
            BuildDetail: CrmApiClient.GetFieldAsString(job, "buildDetail"),
            SysOsId: CrmApiClient.GetFieldAsString(job, "sysosId"),
            SysFollowupId: CrmApiClient.GetFieldAsString(job, "sysFollowupId"),
            SysDevelop: CrmApiClient.GetFieldAsString(job, "sysDevelop"),
            Posted: cfg.Username,
            JobType: "HD");

        await crmApi.UpdateSupportJobAsync(actingUserId, payload, ct);
    }

    // "Resend/Relink" ที่ผู้ใช้ต้องการจริงๆ คือแก้ผู้รับผิดชอบบน ticket เดิม ไม่ใช่สร้าง ticket ใหม่ซ้ำใน CRM —
    // GET+PUT เหมือน AppendCommentAsync แต่แก้ Assignto/SysDevelop แทน Description (SysDevelop = Assignto เสมอ
    // ตาม convention เดียวกับตอนสร้าง ticket ใน SendAsync) พร้อมทิ้งโน้ตสั้นๆ ไว้ใน Description เป็นหลักฐานว่า
    // เปลี่ยนจาก QA Hub เมื่อไหร่/ใครเปลี่ยน
    public async Task ChangeAssigneeAsync(Defect defect, Guid actingUserId, string newAssignToStaffCode, string actorDisplayName, CancellationToken ct)
    {
        var ticketId = defect.CrmTicketId;
        if (string.IsNullOrWhiteSpace(ticketId)) throw new CrmIntegrationException("Defect นี้ยังไม่ได้เชื่อมโยงกับ CRM Ticket");
        var (cfg, _) = await crmConfig.GetRuntimeAsync(actingUserId, ct);
        var job = await crmApi.GetJobDetailAsync(actingUserId, ticketId, "HD", ct);
        var nowThai = DateTime.UtcNow.AddHours(7);
        var note = $"[QA Hub] {actorDisplayName} ({nowThai:dd/MM/yyyy HH:mm}): เปลี่ยนผู้รับผิดชอบเป็น {newAssignToStaffCode}";
        var newDescription = AppendBoundedDescription(CrmApiClient.GetFieldAsString(job, "description"), note);

        var payload = new CrmUpdateJobPayload(
            JobNo: ticketId,
            Subject: CrmApiClient.GetFieldAsString(job, "subject"),
            Member: CrmApiClient.GetFieldAsString(job, "member"),
            SysCustomerType: CrmApiClient.GetFieldAsString(job, "sysCustomerType"),
            FName: CrmApiClient.GetFieldAsString(job, "fname"),
            LName: CrmApiClient.GetFieldAsString(job, "lname"),
            NickName: CrmApiClient.GetFieldAsString(job, "nickName"),
            Fax: CrmApiClient.GetFieldAsString(job, "fax"),
            Tel: CrmApiClient.GetFieldAsString(job, "tel"),
            Email: CrmApiClient.GetFieldAsString(job, "email"),
            Assignto: newAssignToStaffCode,
            RecipientId: CrmApiClient.GetFieldAsString(job, "recipientId"),
            OwnerSubjectId: CrmApiClient.GetFieldAsString(job, "ownerSubjectId"),
            Status: CrmApiClient.GetFieldAsString(job, "status"),
            Source: CrmApiClient.GetFieldAsString(job, "source"),
            RefJobNo: CrmApiClient.GetFieldAsString(job, "refjobNo"),
            SysBranchId: "00000", // ดู comment เดียวกันใน AppendCommentAsync — ฟอร์ม Update ของ CRM เองส่งค่าคงที่นี้เสมอ
            Description: newDescription,
            SysserViceType: CrmApiClient.GetFieldAsString(job, "sysserviceType"),
            SysProductId: CrmApiClient.GetFieldAsString(job, "sysProductId"),
            Duedate: CrmApiClient.GetFieldAsString(job, "duedate"),
            SysVersionId: CrmApiClient.GetFieldAsString(job, "sysVersionId"),
            BuildDetail: CrmApiClient.GetFieldAsString(job, "buildDetail"),
            SysOsId: CrmApiClient.GetFieldAsString(job, "sysosId"),
            SysFollowupId: CrmApiClient.GetFieldAsString(job, "sysFollowupId"),
            SysDevelop: newAssignToStaffCode,
            Posted: cfg.Username,
            JobType: "HD");

        await crmApi.UpdateSupportJobAsync(actingUserId, payload, ct);
    }

    private static string TruncateForCrm(string text)
    {
        if (text.Length <= CrmDescriptionMaxLength) return text;
        var keep = CrmDescriptionMaxLength - TruncationNotice.Length;
        return text[..Math.Max(0, keep)] + TruncationNotice;
    }

    // Description ของ CRM มี maxlength="1000" เหมือนตอนสร้าง — คอมเมนต์สะสมมาเรื่อยๆ เกินได้ง่าย ถ้าเกินให้ตัด
    // ข้อความเก่าสุด (ต้นๆ) ทิ้งก่อน ไม่ใช่ตัดคอมเมนต์ใหม่ล่าสุดที่เพิ่งกดส่ง (นั่นคือสิ่งที่ผู้ใช้ต้องการเห็นแน่ๆ)
    private static string AppendBoundedDescription(string current, string note)
    {
        var combined = string.IsNullOrWhiteSpace(current) ? note : $"{current}\n\n{note}";
        if (combined.Length <= CrmDescriptionMaxLength) return combined;
        if (note.Length >= CrmDescriptionMaxLength) return note[..CrmDescriptionMaxLength]; // คอมเมนต์เดียวก็ยาวเกินลิมิตแล้ว
        return combined[(combined.Length - CrmDescriptionMaxLength)..];
    }
}
