using System.Globalization;
using System.Text.Json;
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
public sealed class CrmSendToCrmService(QaDbContext db, CrmApiClient crmApi, CrmConfigurationService crmConfig, CrmTokenService tokenService, EmailSenderService emailSender, DefectShareLinkService shareLinks, ILogger<CrmSendToCrmService> logger)
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
    private static readonly CultureInfo ThaiCulture = CultureInfo.GetCultureInfo("th-TH");

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
        var shareUrl = await shareLinks.GetOrCreateUrlAsync(defect.DefectId, ct);
        var description = BuildCrmDescription(string.Join("\n\n", new[] { defect.Description, defect.StepsToReproduce, defect.ExpectedResult, defect.ActualResult }.Where(x => !string.IsNullOrWhiteSpace(x))), shareUrl);
        // CRM ไม่มีค่า default ให้ฟิลด์นี้ (ไม่เคยส่งมาก่อนจะกลาย epoch 0 → โชว์เป็น 01/01/2513) ต้องส่งเวลาปัจจุบัน
        // เสมอ — ใช้เวลาไทย (UTC+7) ตรงกับ format ที่หน้า CRM เองส่ง (yyyy-M-d'T'HH:mm ปีเป็น ค.ศ. ไม่ padding เดือน/วัน)
        // ต้อง format ด้วย InvariantCulture — เครื่อง server เป็น th-TH ถ้าใช้ culture ปัจจุบัน "yyyy" จะได้ปี พ.ศ.
        // (2569) แล้ว CRM บวก 543 ซ้ำจนแสดงเป็น 3112
        var nowThai = DateTime.UtcNow.AddHours(7);
        var contactDate = string.Create(CultureInfo.InvariantCulture, $"{nowThai.Year}-{nowThai.Month}-{nowThai.Day}T{nowThai:HH:mm}");
        // Duedate เดียวกับปัญหา ContactDate ข้างบน — ไม่เคยส่งมาก่อนจะกลาย epoch 0 ค่าเริ่มต้นคือวันนี้เวลาเที่ยงคืน
        var dueDate = string.Create(CultureInfo.InvariantCulture, $"{nowThai.Year}-{nowThai.Month}-{nowThai.Day}T00:00:00");

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
                    defect.Description, defect.StepsToReproduce, defect.ExpectedResult, defect.ActualResult, dev.Name, dev.StaffCode, jobNo, link, shareUrl);
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
    // กลไกเดียวที่มีคือปุ่ม Update ของ CRM เอง ซึ่ง PUT /Support ทั้งใบ จึงต้อง GET job ปัจจุบันมาก่อนแล้ว carry-over ทุก
    // field เดิมกลับไป; Description ของการ Update คือข้อความตอบ ซึ่ง CRM บันทึกเป็น "แถวใหม่ในประวัติการติดต่อ"
    // (ยืนยันกับผู้ใช้ 2026-10-03) จึงส่งเฉพาะคอมเมนต์ใหม่ — เดิมต่อท้ายข้อความสะสม ทำให้ทุกคอมเมนต์สร้างแถวที่มีข้อความ
    // เก่าซ้ำมาด้วย. เรียกจาก DefectsController.AddComment แบบ best-effort (ล้มเหลวได้โดยไม่ทำให้คอมเมนต์ใน QA Hub หาย)
    public async Task AppendCommentAsync(Defect defect, Guid actingUserId, string commentBody, string commentAuthorDisplayName, CancellationToken ct, int imageCount = 0)
    {
        var ticketId = defect.CrmTicketId;
        if (string.IsNullOrWhiteSpace(ticketId)) return;
        var (cfg, _) = await crmConfig.GetRuntimeAsync(actingUserId, ct);
        var job = await crmApi.GetJobDetailAsync(actingUserId, ticketId, "HD", ct);
        var nowThai = DateTime.UtcNow.AddHours(7);
        // CRM ไม่ได้รับรูปของคอมเมนต์ — บอกจำนวนรูปพร้อมลิงก์หน้าแชร์ (ซึ่งแสดงคอมเมนต์และรูปทั้งหมด) แทน
        var imageNote = imageCount > 0 ? $" [แนบรูป {imageCount} รูป ดูได้ที่ {await shareLinks.GetOrCreateUrlAsync(defect.DefectId, ct)}]" : "";
        var newDescription = BuildReplyNote(commentAuthorDisplayName, nowThai, commentBody.Trim(), imageNote);

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

    // อัปเดต Ticket จากหน้า CRM ของ QA Hub (ไม่ต้องผูก Defect): เพิ่มประวัติการติดต่อ / เปลี่ยนสถานะ / ส่งกลับเจ้าของเรื่อง
    // ใน PUT เดียว — ฟอร์ม Update ของ CRM บันทึก Description ที่ส่งไปเป็น "แถวใหม่ในประวัติการติดต่อ" (ยืนยันกับผู้ใช้
    // 2026-10-03) จึงส่งเฉพาะข้อความใหม่ ไม่ต่อท้าย Description เดิม (แบบเดียวกับ AppendCommentAsync)
    public async Task<CrmTicketMutationResult> UpdateTicketAsync(Guid actingUserId, string jobNo, string? message, string? requestedStatus, string? assignToStaffCode, bool assignToOwner, string actorDisplayName, CancellationToken ct)
    {
        var (cfg, _) = await crmConfig.GetRuntimeAsync(actingUserId, ct);
        var job = await crmApi.GetJobDetailAsync(actingUserId, jobNo, "HD", ct);
        var payload = BuildTicketUpdatePayload(job, jobNo, cfg.Username, message, requestedStatus, assignToStaffCode, assignToOwner, actorDisplayName, DateTime.UtcNow.AddHours(7));
        await crmApi.UpdateSupportJobAsync(actingUserId, payload, ct);
        return new(payload.Status, payload.Assignto);
    }

    /// <summary>
    /// สร้าง payload ของ <see cref="UpdateTicketAsync"/> — carry-over ทุก field เดิมของ job; ส่งกลับเจ้าของเรื่อง = Assignto ←
    /// OwnerSubjectId (เหมือน checkbox "to เจ้าของเรื่อง" ของ CRM จึงไม่แตะ SysDevelop); เลือก Dev คนอื่น = Assignto/SysDevelop
    /// ← รหัสนั้นตาม convention ของ SendAsync; ไม่มีข้อความแต่มีการเปลี่ยนแปลง จะเขียนบันทึกสั้น ๆ แทนเพื่อให้มีร่องรอยในประวัติ
    /// </summary>
    public static CrmUpdateJobPayload BuildTicketUpdatePayload(JsonElement job, string jobNo, string username, string? message, string? requestedStatus, string? assignToStaffCode, bool assignToOwner, string actorDisplayName, DateTime nowThai)
    {
        var currentStatus = CrmApiClient.GetFieldAsString(job, "status");
        var currentAssignee = CrmApiClient.GetFieldAsString(job, "assignto", "assignTo", "assigneeCode");
        var status = string.IsNullOrWhiteSpace(requestedStatus) ? currentStatus : requestedStatus.Trim();
        var assignee = currentAssignee;
        var sysDevelop = CrmApiClient.GetFieldAsString(job, "sysDevelop");
        if (assignToOwner)
        {
            var owner = CrmApiClient.GetFieldAsString(job, "ownerSubjectId", "OwnerSubjectId");
            if (string.IsNullOrWhiteSpace(owner)) throw new CrmIntegrationException("Ticket นี้ไม่มีข้อมูลเจ้าของเรื่องใน CRM จึงส่งกลับเจ้าของเรื่องไม่ได้");
            assignee = owner.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(assignToStaffCode))
        {
            assignee = assignToStaffCode.Trim();
            sysDevelop = assignee;
        }
        if (string.IsNullOrWhiteSpace(status) || string.IsNullOrWhiteSpace(assignee))
            throw new CrmIntegrationException("CRM Ticket ไม่มีค่า Status หรือ Assignee ที่ใช้อัปเดตได้");

        var changes = new List<string>();
        if (!string.Equals(currentStatus, status, StringComparison.OrdinalIgnoreCase)) changes.Add($"สถานะ {currentStatus} → {status}");
        if (!string.Equals(currentAssignee, assignee, StringComparison.OrdinalIgnoreCase)) changes.Add(assignToOwner ? $"ส่งกลับเจ้าของเรื่อง ({assignee})" : $"ผู้รับผิดชอบ → {assignee}");
        var text = message?.Trim() ?? "";
        if (text.Length == 0)
        {
            if (changes.Count == 0) throw new CrmIntegrationException("ไม่มีข้อความหรือการเปลี่ยนแปลงที่จะบันทึกไป CRM");
            text = BuildReplyNote(actorDisplayName, nowThai, string.Join(", ", changes));
        }
        if (text.Length > CrmDescriptionMaxLength) text = text[..CrmDescriptionMaxLength];
        return BuildUpdatePayload(job, jobNo, username, status, assignee, text, sysDevelop);
    }

    private static CrmUpdateJobPayload BuildUpdatePayload(JsonElement job, string ticketId, string username, string status, string assignee, string description, string sysDevelop) => new(
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
        Assignto: assignee,
        RecipientId: CrmApiClient.GetFieldAsString(job, "recipientId"),
        OwnerSubjectId: CrmApiClient.GetFieldAsString(job, "ownerSubjectId"),
        Status: status,
        Source: CrmApiClient.GetFieldAsString(job, "source"),
        RefJobNo: CrmApiClient.GetFieldAsString(job, "refjobNo"),
        SysBranchId: "00000",
        Description: description,
        SysserViceType: CrmApiClient.GetFieldAsString(job, "sysserviceType"),
        SysProductId: CrmApiClient.GetFieldAsString(job, "sysProductId"),
        Duedate: CrmApiClient.GetFieldAsString(job, "duedate"),
        SysVersionId: CrmApiClient.GetFieldAsString(job, "sysVersionId"),
        BuildDetail: CrmApiClient.GetFieldAsString(job, "buildDetail"),
        SysOsId: CrmApiClient.GetFieldAsString(job, "sysosId"),
        SysFollowupId: CrmApiClient.GetFieldAsString(job, "sysFollowupId"),
        SysDevelop: sysDevelop,
        Posted: username,
        JobType: "HD");

    public async Task ChangeAssigneeAsync(Defect defect, Guid actingUserId, string newAssignToStaffCode, string actorDisplayName, CancellationToken ct)
    {
        var ticketId = defect.CrmTicketId;
        if (string.IsNullOrWhiteSpace(ticketId)) throw new CrmIntegrationException("Defect นี้ยังไม่ได้เชื่อมโยงกับ CRM Ticket");
        var (cfg, _) = await crmConfig.GetRuntimeAsync(actingUserId, ct);
        var job = await crmApi.GetJobDetailAsync(actingUserId, ticketId, "HD", ct);
        var nowThai = DateTime.UtcNow.AddHours(7);
        var newDescription = BuildReplyNote(actorDisplayName, nowThai, $"เปลี่ยนผู้รับผิดชอบเป็น {newAssignToStaffCode}");

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

    // ต่อท้ายลิงก์อ่านอย่างเดียว (ไม่ต้อง login) เสมอ — CRM ไม่ได้รับรูปแนบ และข้อความยาวเกินลิมิตจะถูกตัด; ลิงก์ต้องอยู่ครบ
    // ภายใน 1000 ตัวอักษรเสมอ จึงตัดเนื้อหาก่อนแล้วค่อยต่อท้าย
    public static string BuildCrmDescription(string text, string shareUrl)
    {
        var footer = $"\n\nดูรายละเอียดเต็มและรูปภาพที่ QA Hub: {shareUrl}";
        if (text.Length + footer.Length <= CrmDescriptionMaxLength) return text + footer;
        var truncatedFooter = $"\n\n[...ตัดข้อความ] ดูรายละเอียดเต็มและรูปภาพที่ QA Hub: {shareUrl}";
        var keep = Math.Max(0, CrmDescriptionMaxLength - truncatedFooter.Length);
        return text[..Math.Min(keep, text.Length)] + truncatedFooter;
    }

    /// <summary>
    /// ข้อความตอบที่ส่งเป็น Description ของการ Update (CRM บันทึกเป็นแถวใหม่ในประวัติการติดต่อ):
    /// `[QA Hub] {ชื่อ} (dd/MM/yyyy HH:mm พ.ศ.): {ข้อความ}{ท้าย}` — ปี พ.ศ. แบบตายตัว (ไม่ขึ้นกับ culture ของเครื่องที่รัน API)
    /// และไม่เกิน maxlength 1000 ของ CRM โดยตัดเนื้อหาก่อน เพื่อให้ส่วนท้าย (เช่น ลิงก์รูปภาพ) อยู่ครบ
    /// </summary>
    public static string BuildReplyNote(string actorDisplayName, DateTime nowThai, string body, string suffix = "")
    {
        var prefix = string.Create(ThaiCulture, $"[QA Hub] {actorDisplayName} ({nowThai:dd/MM/yyyy HH:mm}): ");
        var room = CrmDescriptionMaxLength - prefix.Length - suffix.Length;
        if (room <= 0) return (prefix + body + suffix)[..CrmDescriptionMaxLength];
        return prefix + (body.Length > room ? body[..room] : body) + suffix;
    }
}

public sealed record CrmTicketMutationResult(string Status, string Assignee);
