namespace ProMaxx2.QA.Domain.Defects;

public sealed class Defect
{
    private Defect() { }
    public Defect(Guid projectId, Guid? releaseId, Guid? buildId, Guid? moduleId, string code, string title, string severity, string status, Guid? createdBy, string? description, string? stepsToReproduce, string? expectedResult, string? actualResult, Guid? assigneeUserId)
    {
        if (projectId == Guid.Empty || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Project, defect code and title are required.");
        DefectId=Guid.NewGuid();ProjectId=projectId;ReleaseId=releaseId;BuildId=buildId;ModuleId=moduleId;DefectCode=code.Trim().ToUpperInvariant();Title=title.Trim();Severity=NormalizeSeverity(severity);Status=NormalizeStatus(status);CreatedBy=createdBy;CreatedAt=DateTime.UtcNow;Description=description?.Trim();StepsToReproduce=stepsToReproduce?.Trim();ExpectedResult=expectedResult?.Trim();ActualResult=actualResult?.Trim();AssigneeUserId=assigneeUserId;
    }
    public Guid DefectId{get;private set;} public Guid ProjectId{get;private set;} public Guid? ReleaseId{get;private set;} public Guid? BuildId{get;private set;} public Guid? ModuleId{get;private set;} public string DefectCode{get;private set;}=string.Empty; public string Title{get;private set;}=string.Empty; public string Severity{get;private set;}="Medium"; public string Status{get;private set;}="Open"; public string? Description{get;private set;} public string? StepsToReproduce{get;private set;} public string? ExpectedResult{get;private set;} public string? ActualResult{get;private set;} public Guid? AssigneeUserId{get;private set;} public bool IsDeleted{get;private set;} public DateTime?DeletedAt{get;private set;} public Guid?DeletedBy{get;private set;} public DateTime CreatedAt{get;private set;} public Guid? CreatedBy{get;private set;} public DateTime? UpdatedAt{get;private set;} public Guid? UpdatedBy{get;private set;}
    // CRM (BlueSea Helpdesk) integration — Phase 1 (see Document/03-Architecture-and-Plan/CRM_INTEGRATION_PLAN.md).
    // CrmLastKnownStatus/CrmLastKnownAssignto are reserved for the Phase 2 poller (unused by Phase 1 code) — added
    // now so the schema only needs one migration instead of two.
    public string? CrmTicketId{get;private set;} public string CrmSyncStatus{get;private set;}="None"; public DateTime? CrmLastSyncedAt{get;private set;} public string? CrmLastKnownStatus{get;private set;} public string? CrmLastKnownAssignto{get;private set;}
    // marker ของ answerNo ล่าสุดที่ poller เห็นแล้วจาก /Support/HelpDeskAnswerMain (CRM → QA Hub comment sync) —
    // เก็บเป็น string เหมือน field CRM อื่นๆ (บาง field ของ CRM สลับมาเป็น JSON number บ้าง) แปลงเป็นตัวเลขตอนเทียบ
    public string? CrmLastSeenAnswerNo{get;private set;}
    public void Update(string title, string severity, string? description, string? stepsToReproduce, string? expectedResult, string? actualResult, Guid? assigneeUserId, Guid? updatedBy){if(string.IsNullOrWhiteSpace(title))throw new ArgumentException("Title is required.");Title=title.Trim();Severity=NormalizeSeverity(severity);Description=description?.Trim();StepsToReproduce=stepsToReproduce?.Trim();ExpectedResult=expectedResult?.Trim();ActualResult=actualResult?.Trim();AssigneeUserId=assigneeUserId;UpdatedAt=DateTime.UtcNow;UpdatedBy=updatedBy;}
    // ---------- วงจร Defect ข้าม Build (Document/03-Architecture-and-Plan/DEFECT_BUILD_LIFECYCLE.md) ----------
    // Open/In Progress = Dev ยังไม่ได้แก้, Resolved = Dev แก้แล้วรอ QA ตรวจซ้ำบน Fix Build, Closed = QA ตรวจซ้ำผ่าน (หรือปิดด้วยผลที่ไม่ใช่ Fixed), Rejected = ไม่ใช่ Defect
    // ReleaseId/BuildId = เจอใน Build ไหน (ตั้งตอนสร้างเท่านั้น); FixedInBuildId = แก้ใน Build ไหน; VerifiedInBuildId = ตรวจซ้ำผ่านใน Build ไหน
    public static readonly string[] Statuses=["Open","In Progress","Resolved","Closed","Rejected"];
    public static readonly string[] UnresolvedStatuses=DefectStatusGroups.Unresolved;
    public Guid? FixedInBuildId{get;private set;} public Guid? VerifiedInBuildId{get;private set;} public DateTime? ResolvedAt{get;private set;} public DateTime? ClosedAt{get;private set;} public int ReopenCount{get;private set;}
    public DateTime? LastRetestAt{get;private set;} public string? LastRetestResult{get;private set;}
    /// <summary>ปิดเพราะแก้แล้ว (Fixed หรือ QA ตรวจซ้ำผ่าน) — ปัญหาเดียวกันกลับมาจึงนับเป็น regression; ปิดด้วยผลอื่น (Won't Fix ฯลฯ) ไม่ใช่</summary>
    public static bool WasFixed(string? resolutionType,Guid? verifiedInBuildId)=>resolutionType=="Fixed"||verifiedInBuildId.HasValue;
    /// <summary>Defect เดิมที่ปิดไปแล้วแต่ปัญหากลับมาอีก (regression) — สร้าง Defect ใหม่ที่อ้างตัวเดิมแทนการเปิดตัวที่ปิดแล้ว</summary>
    public Guid? RegressionOfDefectId{get;private set;}

    /// <summary>เปลี่ยนสถานะได้หรือไม่ — null = ได้, ไม่งั้นคืนข้อความภาษาไทยที่บอกว่าต้องทำอะไรต่อ (ใช้ทั้งหน้าเว็บ, Bulk, CRM และ auto-create)</summary>
    public static string? TransitionError(string from,string to,string? resolutionType)
    {
        if(from==to)return null;
        var directClose=to=="Closed"&&from!="Resolved";
        return (from,to) switch
        {
            ("Closed","Resolved")=>"Defect ที่ Closed แล้วเปลี่ยนเป็น Resolved ตรง ๆ ไม่ได้ — ถ้าปัญหายังอยู่ให้ Reopen (Open) ก่อน",
            ("Closed","Rejected")=>"Defect ที่ Closed แล้วเปลี่ยนเป็น Rejected ไม่ได้ — ให้ Reopen ก่อนถ้าต้องการเปลี่ยนผล",
            ("Rejected","Resolved")=>"Defect ที่ Rejected เปลี่ยนเป็น Resolved ตรง ๆ ไม่ได้ — เปิดกลับเป็น Open ก่อน",
            _ when directClose&&resolutionType is null=>"ปิด Defect ที่ยังไม่ผ่าน Resolved ต้องระบุผลการปิด (Duplicate, Cannot Reproduce, By Design, Won't Fix หรือ Deferred)",
            _ when directClose&&resolutionType=="Fixed"=>"Defect ที่แก้แล้ว (Fixed) ต้องเปลี่ยนเป็น Resolved พร้อม Fix Build ให้ QA ตรวจซ้ำก่อน แล้วจึง Closed",
            _=>null,
        };
    }

    /// <summary>เปลี่ยนสถานะตามกฎ — เรียกหลังบันทึกผลการปิด (SetResolution) แล้ว; ผิดกฎ = DefectTransitionException</summary>
    /// <param name="fixedInBuildId">Build ที่แก้ (ตอนเป็น Resolved) — null = คงค่าเดิม</param>
    /// <param name="verifiedInBuildId">Build ที่ QA ตรวจซ้ำผ่าน (ตอน Resolved → Closed) — null = ใช้ Fix Build</param>
    public void TransitionTo(string status,Guid? fixedInBuildId,Guid? verifiedInBuildId,Guid? updatedBy)
    {
        var target=NormalizeStatus(status);var from=Status;
        if(target==from)
        {
            if(target=="Resolved"&&fixedInBuildId is {} sameFix)FixedInBuildId=sameFix;
            if(target=="Closed"&&verifiedInBuildId is {} sameVerified)VerifiedInBuildId=sameVerified;
            UpdatedAt=DateTime.UtcNow;UpdatedBy=updatedBy;return;
        }
        if(TransitionError(from,target,ResolutionType) is {} error)throw new DefectTransitionException(error);
        var now=DateTime.UtcNow;
        // ออกจาก Resolved = ผลตรวจซ้ำ: Closed = ผ่าน, กลับไปยังไม่แก้ = ไม่ผ่าน (Rejected ไม่นับเป็นการตรวจซ้ำ)
        if(from=="Resolved"&&target!="Rejected"){LastRetestAt=now;LastRetestResult=target=="Closed"?"Pass":"Fail";}
        if(UnresolvedStatuses.Contains(target))
        {
            // เปิดกลับมา = ยังไม่ได้แก้ — ล้างผลการปิดเดิม ไม่ให้คลังความรู้/ผู้ช่วย AI/MCP ตอบว่า "แก้แล้ว" จากรอบก่อน (ผลการปิดเก่ายังอยู่ใน Activity)
            // Reopen นับเฉพาะจาก Resolved/Closed (แก้แล้วแต่ไม่ผ่าน); Fix Build เดิมเก็บไว้เป็นประวัติ
            if(from is "Resolved" or "Closed")ReopenCount++;
            ResolutionType=null;RootCauseCategory=null;RootCauseNote=null;ResolutionNote=null;DuplicateOfDefectId=null;
            ResolvedAt=null;ClosedAt=null;VerifiedInBuildId=null;
        }
        // Resolve รอบใหม่ (รวมหลัง Reopen) ตั้ง Fix Build ใหม่เสมอ — ค่ารอบก่อนอยู่ใน activity แล้ว ไม่ค้างไว้ให้ตัดสิน Reopen ผิด Build
        else if(target=="Resolved"){ResolvedAt=now;ClosedAt=null;VerifiedInBuildId=null;FixedInBuildId=fixedInBuildId;}
        else if(target=="Closed"){ClosedAt=now;ResolvedAt??=now;VerifiedInBuildId=verifiedInBuildId??(from=="Resolved"?FixedInBuildId:null);}
        else ClosedAt=now; // Rejected
        Status=target;UpdatedAt=now;UpdatedBy=updatedBy;
    }

    /// <summary>ผลตรวจซ้ำบน Build — Pass = Closed (Verified Build = build นี้), Fail = Reopen; Defect ต้องเป็น Resolved</summary>
    public void Retest(Guid buildId,bool passed,Guid? updatedBy)
    {
        if(Status!="Resolved")throw new DefectTransitionException("ตรวจซ้ำได้เฉพาะ Defect ที่เป็น Resolved (Dev แก้แล้ว รอ QA ตรวจ)");
        TransitionTo(passed?"Closed":"Open",null,passed?buildId:null,updatedBy);
    }

    public void MarkRegressionOf(Guid originalDefectId){if(originalDefectId==DefectId)throw new ArgumentException("Defect cannot be a regression of itself.");RegressionOfDefectId=originalDefectId;}

    public void UpdateStatus(string status,Guid?updatedBy)=>TransitionTo(status,null,null,updatedBy);
    public void UpdateSeverity(string severity,Guid?updatedBy){Severity=NormalizeSeverity(severity);UpdatedAt=DateTime.UtcNow;UpdatedBy=updatedBy;}
    public void Assign(Guid assigneeUserId,Guid?updatedBy){AssigneeUserId=assigneeUserId;UpdatedAt=DateTime.UtcNow;UpdatedBy=updatedBy;}
    public void UpdateCode(string code){if(string.IsNullOrWhiteSpace(code))throw new ArgumentException("Defect code is required.");DefectCode=code.Trim().ToUpperInvariant();}
    public void SoftDelete(Guid?userId){IsDeleted=true;DeletedAt=UpdatedAt=DateTime.UtcNow;DeletedBy=UpdatedBy=userId;}
    public void Restore(Guid?userId){IsDeleted=false;DeletedAt=null;DeletedBy=null;UpdatedAt=DateTime.UtcNow;UpdatedBy=userId;}
    // ผลสำเร็จของการส่งไป CRM (Phase 1) — ไม่แตะ CrmLastKnownStatus/Assignto เพราะยังไม่มี poller (Phase 2) มาเทียบค่า
    public void SetCrmTicket(string ticketId,DateTime syncedAt){if(string.IsNullOrWhiteSpace(ticketId))throw new ArgumentException("CRM ticket id is required.");CrmTicketId=ticketId.Trim();CrmSyncStatus="Linked";CrmLastSyncedAt=syncedAt;}
    public void UnlinkCrmTicket(DateTime updatedAt, Guid? updatedBy){CrmTicketId=null;CrmSyncStatus="None";CrmLastSyncedAt=null;CrmLastKnownStatus=null;CrmLastKnownAssignto=null;CrmLastSeenAnswerNo=null;UpdatedAt=updatedAt;UpdatedBy=updatedBy;}
    // ตั้งใจไม่แตะ CrmTicketId ตรงนี้ — การส่งซ้ำที่ล้มเหลวต้องไม่ไปลบ ticket ที่เคยผูกสำเร็จไว้ก่อนหน้า
    public void SetCrmSyncFailed(DateTime attemptedAt){CrmSyncStatus="Failed";CrmLastSyncedAt=attemptedAt;}
    // Phase 2 poller (CrmSyncService) — pure snapshot of CRM's own Status/Assignto fields, deliberately never
    // touches Status/AssigneeUserId above: QA Hub's own workflow stays 100% QA-driven, CRM's 9-state ticket
    // status has no clean 1:1 mapping onto QA Hub's 4-state Defect workflow.
    public void UpdateCrmSnapshot(string? status,string? assignto,DateTime? syncedAt=null){CrmLastKnownStatus=status;CrmLastKnownAssignto=assignto;if(syncedAt.HasValue)CrmLastSyncedAt=syncedAt.Value;}
    // Phase 2 poller — เลื่อน marker ไปข้างหน้าหลังประมวลผลคอมเมนต์ใหม่จาก CRM แล้ว (หรือตั้ง baseline ตอน poll ครั้งแรก)
    public void UpdateCrmLastSeenAnswerNo(string? answerNo){CrmLastSeenAnswerNo=answerNo;}
    // ผลการปิด Defect (DEFECT_KNOWLEDGE_PLAN.md §4.1 / AI_KNOWLEDGE_CHAT_PLAN.md Phase 6) — ความรู้ "สาเหตุ/วิธีแก้" สำหรับคลังความรู้และเตือน Defect ซ้ำ
    // หน้า Defect บังคับกรอกตอนปิด (Resolved/Closed/Rejected); ช่องทางอื่น (Bulk, CRM Sync, Automation) ปิดได้โดยไม่มีค่า
    public static readonly string[] ResolutionTypes=["Fixed","Duplicate","CannotReproduce","ByDesign","WontFix","Deferred"];
    public static readonly string[] ClosingStatuses=["Resolved","Closed","Rejected"];
    public string? ResolutionType{get;private set;} public string? RootCauseCategory{get;private set;} public string? RootCauseNote{get;private set;} public string? ResolutionNote{get;private set;} public Guid? DuplicateOfDefectId{get;private set;}
    /// <summary>Fixed ต้องมีหมวดสาเหตุ, Duplicate ต้องระบุ Defect ต้นฉบับ (ไม่ใช่ตัวเอง); หมวดสาเหตุตรวจกับ Master Option ที่ชั้น API</summary>
    public void SetResolution(string resolutionType,string? rootCauseCategory,string? rootCauseNote,string? resolutionNote,Guid? duplicateOfDefectId,Guid? updatedBy)
    {
        var type=ResolutionTypes.SingleOrDefault(x=>x.Equals(resolutionType?.Trim(),StringComparison.OrdinalIgnoreCase))??throw new ArgumentException("Invalid resolution type.");
        var category=string.IsNullOrWhiteSpace(rootCauseCategory)?null:rootCauseCategory.Trim();
        if(type=="Fixed"&&category is null)throw new ArgumentException("Root cause category is required when the defect is fixed.");
        if(type=="Duplicate"&&(duplicateOfDefectId is null||duplicateOfDefectId==DefectId))throw new ArgumentException("Duplicate requires the original defect.");
        ResolutionType=type;RootCauseCategory=category?.Length>50?category[..50]:category;RootCauseNote=Cut(rootCauseNote,2000);ResolutionNote=Cut(resolutionNote,2000);DuplicateOfDefectId=type=="Duplicate"?duplicateOfDefectId:null;
        UpdatedAt=DateTime.UtcNow;UpdatedBy=updatedBy;
    }
    private static string? Cut(string? value,int max){if(string.IsNullOrWhiteSpace(value))return null;var text=value.Trim();return text.Length>max?text[..max]:text;}
    private static string NormalizeSeverity(string value)=>new[]{"Critical","High","Medium","Low"}.SingleOrDefault(x=>x.Equals(value,StringComparison.OrdinalIgnoreCase))??throw new ArgumentException("Invalid defect severity.");
    private static string NormalizeStatus(string value)=>Statuses.SingleOrDefault(x=>x.Equals(value,StringComparison.OrdinalIgnoreCase))??throw new ArgumentException("Invalid defect status.");
}

/// <summary>เปลี่ยนสถานะ Defect ผิดกฎ (เช่น ปิด Defect ที่แก้แล้วโดยไม่ผ่าน Resolved) — ข้อความเป็นภาษาไทยแสดงให้ผู้ใช้ได้ตรง ๆ</summary>
public sealed class DefectTransitionException(string message) : InvalidOperationException(message);
