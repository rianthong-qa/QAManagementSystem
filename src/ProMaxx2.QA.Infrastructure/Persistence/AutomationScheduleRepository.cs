using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;
using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

/// <summary>AUT-P1-005 persistence for <see cref="AutomationSchedule"/> — split into its own file as a partial of
/// <see cref="AutomationRepository"/>, same pattern as AutomationSuiteRepository.cs. Mapped to table
/// "AutomationSuiteSchedules" (not "AutomationSchedules") because that name is already occupied on the shared dev DB
/// by an orphaned table from an earlier, superseded Automation module version that no current entity maps to.</summary>
public sealed partial class AutomationRepository
{
    public async Task<IReadOnlyList<AutomationScheduleListDto>> ListSchedulesAsync(Guid projectId, bool? isActive, CancellationToken ct)
    {
        var q = db.AutomationSchedules.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (isActive.HasValue) q = q.Where(x => x.IsActive == isActive.Value);
        return await q.OrderBy(x => x.NextRunAtUtc)
            .Select(x => new AutomationScheduleListDto(x.AutomationScheduleId, x.ProjectId, x.AutomationSuiteId, x.Suite.SuiteCode, x.Suite.SuiteName, x.Name, x.Description,
                x.Frequency, x.DaysOfWeekMask, x.RunAtTime, x.OnceOnDate, x.TimeZoneId,
                db.Builds.Where(b => b.BuildId == x.BuildId).Select(b => b.BuildNumber).FirstOrDefault() ?? "-",
                db.TestEnvironments.Where(e => e.TestEnvironmentId == x.EnvironmentId).Select(e => e.EnvironmentName).FirstOrDefault() ?? "-",
                x.IsActive, x.NextRunAtUtc, x.LastRunAtUtc, x.CreatedAt))
            .ToListAsync(ct);
    }

    public Task<AutomationScheduleDto?> GetScheduleAsync(Guid id, Guid projectId, CancellationToken ct)
        => db.AutomationSchedules.AsNoTracking().Where(x => x.AutomationScheduleId == id && x.ProjectId == projectId)
            .Select(x => new AutomationScheduleDto(x.AutomationScheduleId, x.ProjectId, x.AutomationSuiteId, x.Suite.SuiteCode, x.Suite.SuiteName, x.Name, x.Description,
                x.Frequency, x.DaysOfWeekMask, x.RunAtTime, x.OnceOnDate, x.TimeZoneId,
                x.BuildId, db.Builds.Where(b => b.BuildId == x.BuildId).Select(b => b.BuildNumber).FirstOrDefault() ?? "-",
                x.EnvironmentId, db.TestEnvironments.Where(e => e.TestEnvironmentId == x.EnvironmentId).Select(e => e.EnvironmentName).FirstOrDefault() ?? "-",
                x.AgentId, x.AgentId != null ? db.AutomationAgents.Where(a => a.AgentId == x.AgentId).Select(a => a.AgentCode).FirstOrDefault() : null,
                x.Priority, x.IsActive, x.NextRunAtUtc, x.LastRunAtUtc, x.CreatedBy, x.CreatedAt, x.UpdatedAt))
            .SingleOrDefaultAsync(ct);

    public Task<AutomationSchedule?> FindScheduleAsync(Guid id, Guid projectId, CancellationToken ct)
        => db.AutomationSchedules.SingleOrDefaultAsync(x => x.AutomationScheduleId == id && x.ProjectId == projectId, ct);

    public Task AddScheduleAsync(AutomationSchedule entity, CancellationToken ct) => db.AutomationSchedules.AddAsync(entity, ct).AsTask();

    public async Task<IReadOnlyList<DueScheduleDto>> ClaimDueSchedulesAsync(DateTime nowUtc, CancellationToken ct)
    {
        // Serializable, same pattern as ClaimNextJobAsync/ClaimVerificationBatchAsync: without this, two overlapping
        // worker ticks (or, if this ever scales to multiple app instances) could both read the same due schedules
        // before either commits, and both fire the same schedule for the same due instant.
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var due = await db.AutomationSchedules.Where(x => x.IsActive && x.NextRunAtUtc <= nowUtc).ToListAsync(ct);
        if (due.Count == 0) { if (transaction is not null) await transaction.CommitAsync(ct); return []; }
        var claimed = due.Select(x => new DueScheduleDto(x.AutomationScheduleId, x.ProjectId, x.AutomationSuiteId, x.Name, x.BuildId, x.EnvironmentId, x.AgentId, x.Priority)).ToList();
        foreach (var schedule in due) schedule.RecordFired(nowUtc);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return claimed;
    }

    public Task AddScheduleRunAsync(AutomationScheduleRun entity, CancellationToken ct) => db.AutomationScheduleRuns.AddAsync(entity, ct).AsTask();

    public async Task<IReadOnlyList<AutomationScheduleRunDto>> ListScheduleRunsAsync(Guid scheduleId, CancellationToken ct)
        => await db.AutomationScheduleRuns.AsNoTracking().Where(x => x.AutomationScheduleId == scheduleId).OrderByDescending(x => x.FiredAtUtc)
            .Select(x => new AutomationScheduleRunDto(x.AutomationScheduleRunId, x.AutomationScheduleId, x.FiredAtUtc, x.Status, x.ExecutionsCreated, x.SkippedCount, x.ErrorMessage))
            .ToListAsync(ct);

    public Task AddNotificationAsync(AutomationScheduleNotification entity, CancellationToken ct) => db.AutomationScheduleNotifications.AddAsync(entity, ct).AsTask();

    public Task<AutomationScheduleNotification?> FindStartedNotificationByExecutionAsync(Guid executionId, CancellationToken ct)
        => db.AutomationScheduleNotifications.AsNoTracking().Where(x => x.AutomationExecutionId == executionId && x.EventType == "Started").FirstOrDefaultAsync(ct);

    public Task<AutomationScheduleNotification?> FindNotificationAsync(Guid id, Guid projectId, CancellationToken ct)
        => db.AutomationScheduleNotifications.SingleOrDefaultAsync(x => x.AutomationScheduleNotificationId == id && x.ProjectId == projectId, ct);

    public async Task<IReadOnlyList<AutomationScheduleNotificationDto>> ListNotificationsAsync(Guid projectId, bool? unreadOnly, int take, CancellationToken ct)
    {
        var q = db.AutomationScheduleNotifications.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (unreadOnly == true) q = q.Where(x => !x.IsRead);
        return await q.OrderByDescending(x => x.CreatedAtUtc).Take(take)
            .Select(x => new AutomationScheduleNotificationDto(x.AutomationScheduleNotificationId, x.ProjectId, x.AutomationScheduleId, x.Schedule.Name,
                x.AutomationExecutionId, db.AutomationExecutions.Where(e => e.AutomationExecutionId == x.AutomationExecutionId).Select(e => e.AutomationCase.AutomationCode).FirstOrDefault() ?? "-",
                x.EventType, x.Message, x.CreatedAtUtc, x.IsRead, x.ReadAtUtc))
            .ToListAsync(ct);
    }

    public Task<int> CountUnreadNotificationsAsync(Guid projectId, CancellationToken ct)
        => db.AutomationScheduleNotifications.AsNoTracking().CountAsync(x => x.ProjectId == projectId && !x.IsRead, ct);

    public async Task MarkAllNotificationsReadAsync(Guid projectId, CancellationToken ct)
    {
        var unread = await db.AutomationScheduleNotifications.Where(x => x.ProjectId == projectId && !x.IsRead).ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var n in unread) n.MarkRead(now);
    }
}

public sealed class AutomationScheduleConfiguration : IEntityTypeConfiguration<AutomationSchedule>
{
    public void Configure(EntityTypeBuilder<AutomationSchedule> b)
    {
        b.ToTable("AutomationSuiteSchedules");
        b.HasKey(x => x.AutomationScheduleId);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Frequency).HasMaxLength(20).IsRequired();
        b.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.ProjectId, x.IsActive, x.NextRunAtUtc });
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Suite).WithMany().HasForeignKey(x => x.AutomationSuiteId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AutomationScheduleRunConfiguration : IEntityTypeConfiguration<AutomationScheduleRun>
{
    public void Configure(EntityTypeBuilder<AutomationScheduleRun> b)
    {
        b.ToTable("AutomationScheduleRuns");
        b.HasKey(x => x.AutomationScheduleRunId);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.HasIndex(x => new { x.AutomationScheduleId, x.FiredAtUtc });
        b.HasOne(x => x.Schedule).WithMany().HasForeignKey(x => x.AutomationScheduleId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AutomationScheduleNotificationConfiguration : IEntityTypeConfiguration<AutomationScheduleNotification>
{
    public void Configure(EntityTypeBuilder<AutomationScheduleNotification> b)
    {
        b.ToTable("AutomationScheduleNotifications");
        b.HasKey(x => x.AutomationScheduleNotificationId);
        b.Property(x => x.EventType).HasMaxLength(20).IsRequired();
        b.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        // Not unique on (AutomationExecutionId, EventType): Started is looked up per execution (at most one is ever
        // written per execution so this is de-facto unique for that row), but an execution can legitimately end up
        // with more than one Failed/Completed row over time is NOT possible either (CompleteExecutionAsync's terminal
        // guard prevents a second Complete call from reaching the notification code at all) — kept non-unique purely
        // because EF Core cannot express "unique only for EventType='Started'" as a plain index without raw SQL, and
        // a full uniqueness guarantee isn't needed for correctness here (FindStartedNotificationByExecutionAsync
        // already takes FirstOrDefault).
        b.HasIndex(x => new { x.AutomationExecutionId, x.EventType });
        b.HasIndex(x => new { x.ProjectId, x.IsRead, x.CreatedAtUtc });
        b.HasOne(x => x.Schedule).WithMany().HasForeignKey(x => x.AutomationScheduleId).OnDelete(DeleteBehavior.Cascade);
    }
}
