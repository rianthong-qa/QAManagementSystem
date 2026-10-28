namespace ProMaxx2.QA.Domain.Integrations;

// System-wide scheduling knob for CrmSyncWorker (Phase 2 poller) — split out from CrmConfiguration when that
// became per-user (one row per QA Hub user's own CRM login) since the poll interval is a system setting, not a
// credential, and needs to be readable without knowing which user's row to look at. Single row, admin-managed
// (Setting Center → CRM Sync) — mirrors AiConfiguration/EmailConfiguration's single-row shape.
public sealed class CrmSyncSettings
{
    public const int MinPollIntervalMinutes = 1;
    public const int MaxPollIntervalMinutes = 60;
    public const int DefaultPollIntervalMinutes = 2;
    // รอบดึงสำเนา CRM Ticket ทั้งทีม (CrmMirrorWorker → ผลงาน CRM + คลังความรู้) — แต่ละรอบ Login BlueID และดึง Ticket 90 วัน
    // จึงไม่ให้ถี่กว่า 5 นาที; สูงสุด 1 วัน (รอบใหญ่ 366 วันยังคงวันละครั้งตามเดิม)
    public const int MinMirrorIntervalMinutes = 5;
    public const int MaxMirrorIntervalMinutes = 1440;
    public const int DefaultMirrorIntervalMinutes = 15;

    private CrmSyncSettings() { }

    public CrmSyncSettings(int pollIntervalMinutes)
    {
        CrmSyncSettingsId = Guid.NewGuid();
        Update(pollIntervalMinutes);
    }

    public Guid CrmSyncSettingsId { get; private set; }
    public int PollIntervalMinutes { get; private set; } = DefaultPollIntervalMinutes;
    public int MirrorIntervalMinutes { get; private set; } = DefaultMirrorIntervalMinutes;
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(int pollIntervalMinutes)
    {
        if (pollIntervalMinutes < MinPollIntervalMinutes || pollIntervalMinutes > MaxPollIntervalMinutes)
            throw new ArgumentException($"รอบ Poll ต้องอยู่ระหว่าง {MinPollIntervalMinutes}-{MaxPollIntervalMinutes} นาที");
        PollIntervalMinutes = pollIntervalMinutes;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateMirrorInterval(int mirrorIntervalMinutes)
    {
        if (mirrorIntervalMinutes < MinMirrorIntervalMinutes || mirrorIntervalMinutes > MaxMirrorIntervalMinutes)
            throw new ArgumentException($"รอบดึงข้อมูล CRM ต้องอยู่ระหว่าง {MinMirrorIntervalMinutes}-{MaxMirrorIntervalMinutes} นาที");
        MirrorIntervalMinutes = mirrorIntervalMinutes;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
