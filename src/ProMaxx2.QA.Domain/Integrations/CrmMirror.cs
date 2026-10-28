namespace ProMaxx2.QA.Domain.Integrations;

/// <summary>
/// สำเนา CRM Ticket ล่าสุดใน QA Hub (ดึงจาก HelpDeskExport ด้วยบัญชีกลาง) — เก็บเฉพาะข้อมูลที่ใช้วัดผลงาน
/// ไม่เก็บข้อมูลลูกค้า (ชื่อ/เบอร์/อีเมล) และเนื้อหาข้อความ ซึ่งดูสดจาก CRM ได้
/// รหัสพนักงาน (…Code) แยกจากค่าดิบของ CRM ที่อาจเป็น "ชื่อ (6101)" เพื่อให้ query ตามรหัสได้ตรง ๆ
/// </summary>
public sealed class CrmTicket
{
    private CrmTicket() { }

    public CrmTicket(string jobNo, DateTime seenAt)
    {
        JobNo = jobNo;
        FirstSeenAt = seenAt;
        LastSyncedAt = seenAt;
        LastChangedAt = seenAt;
    }

    public string JobNo { get; private set; } = string.Empty;
    public string? Subject { get; private set; }
    public string? Status { get; private set; }
    public string? JobType { get; private set; }
    public string? ServiceType { get; private set; }
    public string? Product { get; private set; }
    public string? AssigneeCode { get; private set; }
    public string? OwnerCode { get; private set; }
    public string? DeveloperCode { get; private set; }
    public string? Branch { get; private set; }
    public DateTime? ContactDate { get; private set; }
    public DateTime? DueDate { get; private set; }
    public DateTime? LastReplyAt { get; private set; }
    public DateTime FirstSeenAt { get; private set; }
    public DateTime LastSyncedAt { get; private set; }
    public DateTime LastChangedAt { get; private set; }

    /// <summary>อัปเดตจากข้อมูลล่าสุดของ CRM — คืน true เมื่อสถานะ/ผู้รับผิดชอบ/Dev เปลี่ยน (ต้องบันทึกเหตุการณ์)</summary>
    public bool Apply(string? subject, string? status, string? jobType, string? serviceType, string? product, string? assigneeCode, string? ownerCode, string? developerCode,
        string? branch, DateTime? contactDate, DateTime? dueDate, DateTime? lastReplyAt, DateTime syncedAt)
    {
        var routeChanged = !Same(Status, status) || !Same(AssigneeCode, assigneeCode) || !Same(DeveloperCode, developerCode);
        Subject = Cut(subject, 300); Status = Cut(status, 30); JobType = Cut(jobType, 30); ServiceType = Cut(serviceType, 200); Product = Cut(product, 200);
        AssigneeCode = Cut(assigneeCode, 50); OwnerCode = Cut(ownerCode, 50); DeveloperCode = Cut(developerCode, 50); Branch = Cut(branch, 200);
        ContactDate = contactDate; DueDate = dueDate; LastReplyAt = lastReplyAt; LastSyncedAt = syncedAt;
        if (routeChanged) LastChangedAt = syncedAt;
        return routeChanged;
    }

    private static bool Same(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    private static string? Cut(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length > max ? text[..max] : text;
    }
}

/// <summary>
/// เหตุการณ์ของ Ticket — Snapshot = สถานะ ณ ครั้งแรกที่เห็น, Changed = สถานะ/ผู้รับผิดชอบ/Dev เปลี่ยน (เวลา = เวลาที่ sync เห็น),
/// Answer = มีการตอบใน Ticket (เวลา/ผู้ตอบจาก CRM, ไม่เก็บเนื้อหา); Source = Sync / Legacy (นำเข้าจากประวัติ Flow Tracking เดิม)
/// </summary>
public sealed class CrmTicketEvent
{
    public static readonly string[] Kinds = ["Snapshot", "Changed", "Answer"];

    private CrmTicketEvent() { }

    private CrmTicketEvent(string jobNo, string kind, DateTime occurredAt, string source)
    {
        JobNo = jobNo; Kind = kind; OccurredAt = occurredAt; Source = source; RecordedAt = DateTime.UtcNow;
    }

    public static CrmTicketEvent Route(string jobNo, string kind, DateTime occurredAt, string source, string? fromStatus, string? toStatus, string? fromAssignee, string? toAssignee, string? fromDeveloper, string? toDeveloper, string? ownerCode)
        => new(jobNo, kind, occurredAt, source)
        {
            FromStatus = fromStatus, ToStatus = toStatus, FromAssigneeCode = fromAssignee, ToAssigneeCode = toAssignee,
            FromDeveloperCode = fromDeveloper, ToDeveloperCode = toDeveloper, OwnerCode = ownerCode,
        };

    public static CrmTicketEvent Answer(string jobNo, string answerNo, DateTime occurredAt, string? actorCode)
        => new(jobNo, "Answer", occurredAt, "Sync") { AnswerNo = answerNo, ActorCode = actorCode };

    public long CrmTicketEventId { get; private set; }
    public string JobNo { get; private set; } = string.Empty;
    public string Kind { get; private set; } = "Changed";
    public DateTime OccurredAt { get; private set; }
    public DateTime RecordedAt { get; private set; }
    public string Source { get; private set; } = "Sync";
    public string? FromStatus { get; private set; }
    public string? ToStatus { get; private set; }
    public string? FromAssigneeCode { get; private set; }
    public string? ToAssigneeCode { get; private set; }
    public string? FromDeveloperCode { get; private set; }
    public string? ToDeveloperCode { get; private set; }
    public string? OwnerCode { get; private set; }
    public string? AnswerNo { get; private set; }
    public string? ActorCode { get; private set; }
}

/// <summary>ผลการดึงข้อมูล CRM แต่ละรอบ — ใช้แสดงสถานะในหน้าดูแลระบบและวินิจฉัยปัญหา (DiagnosticsJson = โครงสร้าง field ที่ CRM ส่งมา)</summary>
public sealed class CrmMirrorRun
{
    private CrmMirrorRun() { }

    public CrmMirrorRun(string kind, DateOnly rangeFrom, DateOnly rangeTo, string? accountUsername, Guid? requestedBy)
    {
        CrmMirrorRunId = Guid.NewGuid(); Kind = kind; RangeFrom = rangeFrom; RangeTo = rangeTo; AccountUsername = accountUsername; RequestedBy = requestedBy;
        Status = "Running"; StartedAt = DateTime.UtcNow;
    }

    public Guid CrmMirrorRunId { get; private set; }
    public string Kind { get; private set; } = "Incremental";
    public DateOnly RangeFrom { get; private set; }
    public DateOnly RangeTo { get; private set; }
    public string? AccountUsername { get; private set; }
    public Guid? RequestedBy { get; private set; }
    public string Status { get; private set; } = "Running";
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public int TicketCount { get; private set; }
    public int NewTickets { get; private set; }
    public int ChangedTickets { get; private set; }
    public int NewAnswers { get; private set; }
    public string? Error { get; private set; }
    public string? DiagnosticsJson { get; private set; }

    public void Succeed(int tickets, int newTickets, int changed, int answers, string? diagnosticsJson)
    {
        Status = "Succeeded"; TicketCount = tickets; NewTickets = newTickets; ChangedTickets = changed; NewAnswers = answers; DiagnosticsJson = diagnosticsJson; CompletedAt = DateTime.UtcNow;
    }

    public void Fail(string error, string status = "Failed")
    {
        Status = status; Error = error.Length > 2000 ? error[..2000] : error; CompletedAt = DateTime.UtcNow;
    }
}
