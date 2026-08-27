using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-P1-009 (Schedule Notifications): Started/Completed/Failed/NoAgent notifications about
/// executions a persistent Automation Schedule fired, each linked to the ExecutionId (AC "แจ้ง ... พร้อมลิงก์ Execution").</summary>
public sealed class AutomationScheduleNotificationTests
{
    private static readonly TimeOnly NineAm = new(9, 0);

    private static async Task<(AutomationTestFixtures.Baseline Baseline, Guid ScheduleId)> SeedDueDailyScheduleAsync(ProMaxx2.QA.Infrastructure.Persistence.QaDbContext db)
    {
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suiteService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Nightly", null), null, CancellationToken.None);
        await suiteService.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);
        var scheduleService = AutomationTestFixtures.ScheduleService(db);
        var schedule = await scheduleService.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suite.AutomationSuiteId, "Nightly Smoke", null, "Daily", 0, NineAm, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);
        return (baseline, schedule.AutomationScheduleId);
    }

    [Fact]
    public async Task Firing_a_schedule_with_no_registered_agent_creates_a_Started_and_a_NoAgent_notification_linked_to_the_execution()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, scheduleId) = await SeedDueDailyScheduleAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var scheduleService = AutomationTestFixtures.ScheduleService(db);

        await agents.FireDueSchedulesAsync(DateTime.UtcNow.AddDays(2), CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        var execution = Assert.Single(executions);
        var notifications = await scheduleService.ListNotificationsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Equal(2, notifications.Count);
        var started = Assert.Single(notifications, n => n.EventType == "Started");
        var noAgent = Assert.Single(notifications, n => n.EventType == "NoAgent");
        Assert.Equal(execution.AutomationExecutionId, started.AutomationExecutionId);
        Assert.Equal(execution.AutomationExecutionId, noAgent.AutomationExecutionId);
        Assert.Equal(scheduleId, started.AutomationScheduleId);
        Assert.All(notifications, n => Assert.False(n.IsRead));
    }

    [Fact]
    public async Task Firing_a_schedule_with_an_enabled_agent_registered_creates_only_a_Started_notification()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _) = await SeedDueDailyScheduleAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var scheduleService = AutomationTestFixtures.ScheduleService(db);

        await agents.FireDueSchedulesAsync(DateTime.UtcNow.AddDays(2), CancellationToken.None);

        var notifications = await scheduleService.ListNotificationsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        var notification = Assert.Single(notifications);
        Assert.Equal("Started", notification.EventType);
    }

    [Fact]
    public async Task Completing_a_schedule_fired_execution_as_Passed_creates_a_Completed_notification()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _) = await SeedDueDailyScheduleAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.FireDueSchedulesAsync(DateTime.UtcNow.AddDays(2), CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException("Expected a job to be claimable.");
        var scheduleService = AutomationTestFixtures.ScheduleService(db);

        await agents.CompleteExecutionAsync(claim.AutomationExecutionId, new CompleteExecutionRequest("Passed", null, null, null), CancellationToken.None);

        var notifications = await scheduleService.ListNotificationsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        var completed = Assert.Single(notifications, n => n.EventType == "Completed");
        Assert.Equal(claim.AutomationExecutionId, completed.AutomationExecutionId);
    }

    [Fact]
    public async Task Completing_a_schedule_fired_execution_as_Failed_creates_a_Failed_notification_with_the_error_in_the_message()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _) = await SeedDueDailyScheduleAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.FireDueSchedulesAsync(DateTime.UtcNow.AddDays(2), CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException("Expected a job to be claimable.");
        var scheduleService = AutomationTestFixtures.ScheduleService(db);

        await agents.CompleteExecutionAsync(claim.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AutomationFailure", "AUT-UI-001", "Object not found."), CancellationToken.None);

        var notifications = await scheduleService.ListNotificationsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        var failed = Assert.Single(notifications, n => n.EventType == "Failed");
        Assert.Contains("Object not found.", failed.Message);
    }

    [Fact]
    public async Task Completing_a_manually_created_execution_not_started_by_a_schedule_creates_no_notification()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var execution = await agents.RequestExecutionAsync(baseline.Project.ProjectId,
            new RequestExecutionRequest(readyCase.AutomationCaseId, Guid.Empty, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var scheduleService = AutomationTestFixtures.ScheduleService(db);

        await agents.CompleteExecutionAsync(execution.AutomationExecutionId, new CompleteExecutionRequest("Passed", null, null, null), CancellationToken.None);

        var notifications = await scheduleService.ListNotificationsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task A_late_duplicate_completion_report_does_not_create_a_second_Completed_notification()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _) = await SeedDueDailyScheduleAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.FireDueSchedulesAsync(DateTime.UtcNow.AddDays(2), CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException("Expected a job to be claimable.");
        var scheduleService = AutomationTestFixtures.ScheduleService(db);

        await agents.CompleteExecutionAsync(claim.AutomationExecutionId, new CompleteExecutionRequest("Passed", null, null, null), CancellationToken.None);
        await agents.CompleteExecutionAsync(claim.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AutomationFailure", "AUT-UI-001", "late report"), CancellationToken.None); // late/duplicate — ignored idempotently

        var notifications = await scheduleService.ListNotificationsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Single(notifications, n => n.EventType == "Completed");
        Assert.DoesNotContain(notifications, n => n.EventType == "Failed");
    }

    [Fact]
    public async Task Marking_a_notification_read_removes_it_from_the_unread_filter_and_the_unread_count()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _) = await SeedDueDailyScheduleAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.FireDueSchedulesAsync(DateTime.UtcNow.AddDays(2), CancellationToken.None); // no agent registered -> Started + NoAgent = 2 unread
        var scheduleService = AutomationTestFixtures.ScheduleService(db);
        var all = await scheduleService.ListNotificationsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Equal(2, await scheduleService.CountUnreadNotificationsAsync(baseline.Project.ProjectId, CancellationToken.None));

        await scheduleService.MarkNotificationReadAsync(all[0].AutomationScheduleNotificationId, baseline.Project.ProjectId, CancellationToken.None);

        Assert.Equal(1, await scheduleService.CountUnreadNotificationsAsync(baseline.Project.ProjectId, CancellationToken.None));
        var unreadOnly = await scheduleService.ListNotificationsAsync(baseline.Project.ProjectId, true, 50, CancellationToken.None);
        Assert.DoesNotContain(unreadOnly, n => n.AutomationScheduleNotificationId == all[0].AutomationScheduleNotificationId);
    }

    [Fact]
    public async Task Marking_all_notifications_read_clears_the_unread_count_to_zero()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _) = await SeedDueDailyScheduleAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.FireDueSchedulesAsync(DateTime.UtcNow.AddDays(2), CancellationToken.None);
        var scheduleService = AutomationTestFixtures.ScheduleService(db);

        await scheduleService.MarkAllNotificationsReadAsync(baseline.Project.ProjectId, CancellationToken.None);

        Assert.Equal(0, await scheduleService.CountUnreadNotificationsAsync(baseline.Project.ProjectId, CancellationToken.None));
    }

    [Fact]
    public async Task Marking_a_notification_read_for_a_different_project_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _) = await SeedDueDailyScheduleAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.FireDueSchedulesAsync(DateTime.UtcNow.AddDays(2), CancellationToken.None);
        var scheduleService = AutomationTestFixtures.ScheduleService(db);
        var notification = (await scheduleService.ListNotificationsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None))[0];

        await Assert.ThrowsAsync<EntityNotFoundException>(() => scheduleService.MarkNotificationReadAsync(notification.AutomationScheduleNotificationId, Guid.NewGuid(), CancellationToken.None));
    }
}
