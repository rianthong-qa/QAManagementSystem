using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.Application.Automation;

public sealed record AutomationScheduleListDto(Guid AutomationScheduleId, Guid ProjectId, Guid AutomationSuiteId, string SuiteCode, string SuiteName, string Name, string? Description,
    string Frequency, int DaysOfWeekMask, TimeOnly RunAtTime, DateOnly? OnceOnDate, string TimeZoneId, string BuildNumber, string EnvironmentName, bool IsActive, DateTime NextRunAtUtc, DateTime? LastRunAtUtc, DateTime CreatedAt);

public sealed record AutomationScheduleDto(Guid AutomationScheduleId, Guid ProjectId, Guid AutomationSuiteId, string SuiteCode, string SuiteName, string Name, string? Description,
    string Frequency, int DaysOfWeekMask, TimeOnly RunAtTime, DateOnly? OnceOnDate, string TimeZoneId, Guid BuildId, string BuildNumber, Guid EnvironmentId, string EnvironmentName,
    Guid? AgentId, string? AgentCode, int Priority, bool IsActive, DateTime NextRunAtUtc, DateTime? LastRunAtUtc, Guid? CreatedBy, DateTime CreatedAt, DateTime? UpdatedAt);

public sealed record CreateAutomationScheduleRequest(Guid AutomationSuiteId, string Name, string? Description, string Frequency, int DaysOfWeekMask, TimeOnly RunAtTime, DateOnly? OnceOnDate, string TimeZoneId, Guid BuildId, Guid EnvironmentId, Guid? AgentId, int Priority);
public sealed record UpdateAutomationScheduleRequest(string Name, string? Description, string Frequency, int DaysOfWeekMask, TimeOnly RunAtTime, DateOnly? OnceOnDate, string TimeZoneId, Guid BuildId, Guid EnvironmentId, Guid? AgentId, int Priority);

/// <summary>AUT-P1-006: one Automation Schedule that <see cref="IAutomationScheduleRepository.ClaimDueSchedulesAsync"/>
/// found due and atomically claimed — just enough to actually fire it via <c>AutomationAgentService.RunSuiteAsync</c>.</summary>
public sealed record DueScheduleDto(Guid AutomationScheduleId, Guid ProjectId, Guid AutomationSuiteId, string Name, Guid BuildId, Guid EnvironmentId, Guid? AgentId, int Priority);

public sealed record AutomationScheduleRunDto(Guid AutomationScheduleRunId, Guid AutomationScheduleId, DateTime FiredAtUtc, string Status, int ExecutionsCreated, int SkippedCount, string? ErrorMessage);

/// <summary>AUT-P1-009: one Started/Completed/Failed/NoAgent notification about a schedule-fired execution, joined
/// with the schedule name and automation code so the UI can render it without a second round-trip.</summary>
public sealed record AutomationScheduleNotificationDto(Guid AutomationScheduleNotificationId, Guid ProjectId, Guid AutomationScheduleId, string ScheduleName,
    Guid AutomationExecutionId, string AutomationCode, string EventType, string Message, DateTime CreatedAtUtc, bool IsRead, DateTime? ReadAtUtc);

public interface IAutomationScheduleRepository
{
    Task<IReadOnlyList<AutomationScheduleListDto>> ListSchedulesAsync(Guid projectId, bool? isActive, CancellationToken ct);
    Task<AutomationScheduleDto?> GetScheduleAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<AutomationSchedule?> FindScheduleAsync(Guid id, Guid projectId, CancellationToken ct);
    Task AddScheduleAsync(AutomationSchedule entity, CancellationToken ct);

    /// <summary>AUT-P1-006: atomically (Serializable transaction, same pattern as ClaimNextJobAsync) selects every
    /// active schedule whose NextRunAtUtc has arrived by <paramref name="nowUtc"/> and immediately advances each to
    /// its next occurrence (or deactivates a one-shot "Once" schedule) before returning — so the due instant is
    /// consumed right here and a concurrent/late caller can never claim the same firing twice.</summary>
    Task<IReadOnlyList<DueScheduleDto>> ClaimDueSchedulesAsync(DateTime nowUtc, CancellationToken ct);
    Task AddScheduleRunAsync(AutomationScheduleRun entity, CancellationToken ct);
    Task<IReadOnlyList<AutomationScheduleRunDto>> ListScheduleRunsAsync(Guid scheduleId, CancellationToken ct);

    /// <summary>AUT-P1-009.</summary>
    Task AddNotificationAsync(AutomationScheduleNotification entity, CancellationToken ct);
    /// <summary>AUT-P1-009: looks up the "Started" notification for <paramref name="executionId"/> (if any) so
    /// <c>AutomationAgentService.CompleteExecutionAsync</c> can tell whether an execution was created by a schedule
    /// fire and, if so, which schedule/project it belongs to, without threading that context through every
    /// execution-creation call path (RunSuiteAsync/BatchRunAsync are shared with manual runs and Build Trigger).</summary>
    Task<AutomationScheduleNotification?> FindStartedNotificationByExecutionAsync(Guid executionId, CancellationToken ct);
    Task<AutomationScheduleNotification?> FindNotificationAsync(Guid id, Guid projectId, CancellationToken ct);
    Task<IReadOnlyList<AutomationScheduleNotificationDto>> ListNotificationsAsync(Guid projectId, bool? unreadOnly, int take, CancellationToken ct);
    Task<int> CountUnreadNotificationsAsync(Guid projectId, CancellationToken ct);
    Task MarkAllNotificationsReadAsync(Guid projectId, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>AUT-P1-005: create/edit/activate/deactivate a persistent Automation Schedule — the recurring
/// Build/Environment target and timetable for re-running an <see cref="AutomationSuite"/>. Does not itself fire
/// runs on schedule; that is AUT-P1-006 (Schedule Execution Worker — see <c>AutomationAgentService.FireDueSchedulesAsync</c>),
/// which polls <see cref="AutomationScheduleDto.NextRunAtUtc"/>.</summary>
public sealed class AutomationScheduleService(IAutomationScheduleRepository repository, IAutomationSuiteRepository suites)
{
    public Task<IReadOnlyList<AutomationScheduleListDto>> ListAsync(Guid projectId, bool? isActive, CancellationToken ct)
        => repository.ListSchedulesAsync(projectId, isActive, ct);

    public async Task<AutomationScheduleDto> GetAsync(Guid id, Guid projectId, CancellationToken ct)
        => await repository.GetScheduleAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation schedule not found.");

    public async Task<IReadOnlyList<AutomationScheduleRunDto>> ListRunsAsync(Guid id, Guid projectId, CancellationToken ct)
    {
        _ = await repository.FindScheduleAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation schedule not found.");
        return await repository.ListScheduleRunsAsync(id, ct);
    }

    public async Task<AutomationScheduleDto> CreateAsync(Guid projectId, CreateAutomationScheduleRequest r, Guid? userId, CancellationToken ct)
    {
        var suite = await suites.FindSuiteAsync(r.AutomationSuiteId, projectId, ct) ?? throw new EntityNotFoundException("Automation suite not found.");
        var entity = new AutomationSchedule(projectId, suite.AutomationSuiteId, r.Name, r.Description, r.Frequency, r.DaysOfWeekMask, r.RunAtTime, r.OnceOnDate, r.TimeZoneId,
            r.BuildId, r.EnvironmentId, r.AgentId, r.Priority, userId);
        await repository.AddScheduleAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        return await repository.GetScheduleAsync(entity.AutomationScheduleId, projectId, ct) ?? throw new EntityNotFoundException("Automation schedule not found.");
    }

    public async Task<AutomationScheduleDto> UpdateAsync(Guid id, Guid projectId, UpdateAutomationScheduleRequest r, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindScheduleAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation schedule not found.");
        entity.Update(r.Name, r.Description, r.Frequency, r.DaysOfWeekMask, r.RunAtTime, r.OnceOnDate, r.TimeZoneId, r.BuildId, r.EnvironmentId, r.AgentId, r.Priority, userId);
        await repository.SaveChangesAsync(ct);
        return await repository.GetScheduleAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation schedule not found.");
    }

    public async Task<AutomationScheduleDto> ActivateAsync(Guid id, Guid projectId, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindScheduleAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation schedule not found.");
        entity.Activate(userId);
        await repository.SaveChangesAsync(ct);
        return await repository.GetScheduleAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation schedule not found.");
    }

    public async Task<AutomationScheduleDto> DeactivateAsync(Guid id, Guid projectId, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindScheduleAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation schedule not found.");
        entity.Deactivate(userId);
        await repository.SaveChangesAsync(ct);
        return await repository.GetScheduleAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Automation schedule not found.");
    }

    /// <summary>AUT-P1-009.</summary>
    public Task<IReadOnlyList<AutomationScheduleNotificationDto>> ListNotificationsAsync(Guid projectId, bool? unreadOnly, int take, CancellationToken ct)
        => repository.ListNotificationsAsync(projectId, unreadOnly, Math.Clamp(take, 1, 200), ct);

    public Task<int> CountUnreadNotificationsAsync(Guid projectId, CancellationToken ct) => repository.CountUnreadNotificationsAsync(projectId, ct);

    public async Task MarkNotificationReadAsync(Guid id, Guid projectId, CancellationToken ct)
    {
        var entity = await repository.FindNotificationAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Notification not found.");
        entity.MarkRead(DateTime.UtcNow);
        await repository.SaveChangesAsync(ct);
    }

    public async Task MarkAllNotificationsReadAsync(Guid projectId, CancellationToken ct)
    {
        await repository.MarkAllNotificationsReadAsync(projectId, ct);
        await repository.SaveChangesAsync(ct);
    }
}
