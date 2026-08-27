namespace ProMaxx2.QA.Domain.Automation;

/// <summary>AUT-P1-005: a persistent, recurring configuration for running an <see cref="AutomationSuite"/> against a
/// fixed Build/Environment on a timetable ("Once"/"Daily"/"Weekly") in a chosen timezone. Owns only the schedule
/// config and its computed <see cref="NextRunAtUtc"/> — actually firing a run when that time arrives is AUT-P1-006
/// (Schedule Execution Worker), not this entity.</summary>
public sealed class AutomationSchedule
{
    private static readonly string[] AllowedFrequencies = ["Once", "Daily", "Weekly"];

    private AutomationSchedule() { }

    public AutomationSchedule(Guid projectId, Guid automationSuiteId, string name, string? description,
        string frequency, int daysOfWeekMask, TimeOnly runAtTime, DateOnly? onceOnDate, string timeZoneId,
        Guid buildId, Guid environmentId, Guid? agentId, int priority, Guid? createdBy)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project is required.");
        if (automationSuiteId == Guid.Empty) throw new ArgumentException("Automation suite is required.");
        Validate(name);
        AutomationScheduleId = Guid.NewGuid();
        ProjectId = projectId;
        AutomationSuiteId = automationSuiteId;
        Name = name.Trim();
        Description = description?.Trim();
        IsActive = true;
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
        SetTarget(buildId, environmentId, agentId, priority);
        SetSchedule(frequency, daysOfWeekMask, runAtTime, onceOnDate, timeZoneId);
    }

    public Guid AutomationScheduleId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid AutomationSuiteId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Frequency { get; private set; } = "Daily";
    /// <summary>Bitmask of <see cref="DayOfWeek"/> values as <c>1 &lt;&lt; (int)day</c> — only meaningful when <see cref="Frequency"/> is "Weekly".</summary>
    public int DaysOfWeekMask { get; private set; }
    public TimeOnly RunAtTime { get; private set; }
    /// <summary>Only set when <see cref="Frequency"/> is "Once" — the single local date to run on.</summary>
    public DateOnly? OnceOnDate { get; private set; }
    public string TimeZoneId { get; private set; } = "UTC";
    public Guid BuildId { get; private set; }
    public Guid EnvironmentId { get; private set; }
    public Guid? AgentId { get; private set; }
    public int Priority { get; private set; } = 5;
    public bool IsActive { get; private set; }
    public DateTime NextRunAtUtc { get; private set; }
    public DateTime? LastRunAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public AutomationSuite Suite { get; private set; } = null!;

    public void Update(string name, string? description, string frequency, int daysOfWeekMask, TimeOnly runAtTime, DateOnly? onceOnDate, string timeZoneId,
        Guid buildId, Guid environmentId, Guid? agentId, int priority, Guid? userId)
    {
        Validate(name);
        Name = name.Trim();
        Description = description?.Trim();
        SetTarget(buildId, environmentId, agentId, priority);
        SetSchedule(frequency, daysOfWeekMask, runAtTime, onceOnDate, timeZoneId);
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }

    public void Activate(Guid? userId)
    {
        if (IsActive) throw new InvalidOperationException("Schedule is already active.");
        IsActive = true;
        NextRunAtUtc = ComputeNextRunUtc(DateTime.UtcNow); // stale while inactive — recompute from now
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }

    public void Deactivate(Guid? userId)
    {
        if (!IsActive) throw new InvalidOperationException("Schedule is already inactive.");
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }

    /// <summary>AUT-P1-006: called by the worker's atomic claim (see AutomationScheduleRepository.ClaimDueSchedulesAsync)
    /// the instant a due fire is claimed — before the run is actually attempted — so the slot for
    /// <paramref name="firedAtUtc"/> is consumed and a concurrent/late tick can't claim it again. A one-shot "Once"
    /// schedule has no further occurrence and is deactivated; Daily/Weekly advance to their next occurrence
    /// relative to <paramref name="firedAtUtc"/> (not the original due time), so a run missed while the app was
    /// down catches up exactly once instead of flooding queued fires for every missed interval.</summary>
    public void RecordFired(DateTime firedAtUtc)
    {
        LastRunAtUtc = firedAtUtc;
        if (Frequency == "Once") { IsActive = false; return; }
        NextRunAtUtc = ComputeNextRunUtc(firedAtUtc);
    }

    private void SetTarget(Guid buildId, Guid environmentId, Guid? agentId, int priority)
    {
        if (buildId == Guid.Empty || environmentId == Guid.Empty) throw new ArgumentException("Build and environment are required.");
        BuildId = buildId;
        EnvironmentId = environmentId;
        AgentId = agentId;
        Priority = priority is >= 1 and <= 10 ? priority : 5;
    }

    private void SetSchedule(string frequency, int daysOfWeekMask, TimeOnly runAtTime, DateOnly? onceOnDate, string timeZoneId)
    {
        if (!AllowedFrequencies.Contains(frequency)) throw new ArgumentException("Frequency must be Once, Daily or Weekly.");
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new ArgumentException("Invalid timezone id.", ex); }
        if (frequency == "Once" && onceOnDate is null) throw new ArgumentException("กรุณาระบุวันที่สำหรับ Frequency แบบ Once");
        if (frequency == "Weekly" && daysOfWeekMask <= 0) throw new ArgumentException("กรุณาเลือกวันในสัปดาห์อย่างน้อย 1 วัน สำหรับ Frequency แบบ Weekly");

        Frequency = frequency;
        DaysOfWeekMask = frequency == "Weekly" ? daysOfWeekMask : 0;
        RunAtTime = runAtTime;
        OnceOnDate = frequency == "Once" ? onceOnDate : null;
        TimeZoneId = tz.Id;
        NextRunAtUtc = ComputeNextRunUtc(DateTime.UtcNow);
    }

    /// <summary>Computes the next UTC instant this schedule should fire at, strictly after <paramref name="fromUtc"/>.
    /// Runs the wall-clock math in <see cref="TimeZoneId"/> so the configured local time of day (e.g. "09:00") stays
    /// correct across DST transitions, then converts the result back to UTC.</summary>
    private DateTime ComputeNextRunUtc(DateTime fromUtc)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var fromLocal = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, tz);
        DateTime? candidateLocal = Frequency switch
        {
            "Once" => OnceOnDate!.Value.ToDateTime(RunAtTime),
            "Daily" => NextDaily(fromLocal),
            "Weekly" => NextWeekly(fromLocal),
            _ => null,
        };
        if (candidateLocal is null || candidateLocal <= fromLocal)
            throw new ArgumentException(Frequency == "Once" ? "วันเวลาที่กำหนด (Once) ต้องเป็นเวลาในอนาคต" : "ไม่สามารถคำนวณรอบถัดไปได้ — ตรวจสอบวันในสัปดาห์ที่เลือก");
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidateLocal.Value, DateTimeKind.Unspecified), tz);
    }

    private DateTime NextDaily(DateTime fromLocal)
    {
        var candidate = fromLocal.Date.Add(RunAtTime.ToTimeSpan());
        return candidate <= fromLocal ? candidate.AddDays(1) : candidate;
    }

    private DateTime? NextWeekly(DateTime fromLocal)
    {
        for (var offset = 0; offset <= 7; offset++)
        {
            var candidate = fromLocal.Date.AddDays(offset).Add(RunAtTime.ToTimeSpan());
            if (IsDaySelected(candidate.DayOfWeek) && candidate > fromLocal) return candidate;
        }
        return null;
    }

    private bool IsDaySelected(DayOfWeek day) => (DaysOfWeekMask & (1 << (int)day)) != 0;

    private static void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Schedule name is required.");
    }
}

/// <summary>AUT-P1-006: an audit-trail entry for one time the worker fired an <see cref="AutomationSchedule"/> —
/// recorded regardless of outcome (including when the target suite had no Ready cases, or had been closed/deleted
/// since the schedule was set up) so the schedule's actual firing history is always visible, not just its config.</summary>
public sealed class AutomationScheduleRun
{
    private AutomationScheduleRun() { }
    public AutomationScheduleRun(Guid automationScheduleId, DateTime firedAtUtc, string status, int executionsCreated, int skippedCount, string? errorMessage)
    {
        AutomationScheduleRunId = Guid.NewGuid();
        AutomationScheduleId = automationScheduleId;
        FiredAtUtc = firedAtUtc;
        Status = status;
        ExecutionsCreated = executionsCreated;
        SkippedCount = skippedCount;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim();
    }
    public Guid AutomationScheduleRunId { get; private set; }
    public Guid AutomationScheduleId { get; private set; }
    public DateTime FiredAtUtc { get; private set; }
    /// <summary>"Succeeded" (created at least one execution) / "NoReadyCases" (ran, nothing to create) / "Failed" (the run threw — suite closed/deleted, etc.).</summary>
    public string Status { get; private set; } = string.Empty;
    public int ExecutionsCreated { get; private set; }
    public int SkippedCount { get; private set; }
    public string? ErrorMessage { get; private set; }
    public AutomationSchedule Schedule { get; private set; } = null!;
}
