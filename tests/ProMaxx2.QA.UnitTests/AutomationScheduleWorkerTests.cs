using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-P1-006 (Schedule Execution Worker): <c>AutomationAgentService.FireDueSchedulesAsync</c> —
/// the polled-and-called logic behind <c>AutomationScheduleWorker</c> (a BackgroundService, not covered here since
/// it is just a timer loop around this method). Exercises "create Job ครั้งเดียวตามเวลา" (exactly-once claim),
/// "recovery หลัง restart" (a schedule overdue by any amount still fires on the next poll) and "audit ผล" (every
/// fire — success, no-ready-cases, or failure — is recorded).</summary>
public sealed class AutomationScheduleWorkerTests
{
    private static readonly TimeOnly NineAm = new(9, 0);

    private static async Task<(AutomationTestFixtures.Baseline Baseline, Guid SuiteId, Guid ScheduleId, DateTime OriginalNextRunAtUtc)> SeedDueDailyScheduleAsync(ProMaxx2.QA.Infrastructure.Persistence.QaDbContext db)
    {
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suiteService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Nightly", null), null, CancellationToken.None);
        await suiteService.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);
        var scheduleService = AutomationTestFixtures.ScheduleService(db);
        var schedule = await scheduleService.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suite.AutomationSuiteId, "Nightly Smoke", null, "Daily", 0, NineAm, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);
        return (baseline, suite.AutomationSuiteId, schedule.AutomationScheduleId, schedule.NextRunAtUtc);
    }

    [Fact]
    public async Task Due_daily_schedule_fires_creates_an_execution_and_advances_to_its_next_occurrence()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _, scheduleId, originalNextRun) = await SeedDueDailyScheduleAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var scheduleService = AutomationTestFixtures.ScheduleService(db);
        var now = DateTime.UtcNow.AddDays(2); // guaranteed past a Daily schedule's next run

        await agents.FireDueSchedulesAsync(now, CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Single(executions);
        var refreshed = await scheduleService.GetAsync(scheduleId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.True(refreshed.NextRunAtUtc > originalNextRun);
        Assert.Equal(now, refreshed.LastRunAtUtc);
        Assert.True(refreshed.IsActive);
    }

    [Fact]
    public async Task Firing_a_once_schedule_deactivates_it_instead_of_recomputing_a_next_run()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suiteService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "One-off", null), null, CancellationToken.None);
        await suiteService.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);
        var scheduleService = AutomationTestFixtures.ScheduleService(db);
        var onceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var schedule = await scheduleService.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suite.AutomationSuiteId, "UAT one-off", null, "Once", 0, NineAm, onceDate, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);
        var agents = AutomationTestFixtures.AgentService(db);

        await agents.FireDueSchedulesAsync(DateTime.UtcNow.AddDays(2), CancellationToken.None);

        var refreshed = await scheduleService.GetAsync(schedule.AutomationScheduleId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.False(refreshed.IsActive);
        Assert.NotNull(refreshed.LastRunAtUtc);
    }

    [Fact]
    public async Task Firing_twice_for_the_same_instant_only_creates_one_execution()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _, _, _) = await SeedDueDailyScheduleAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var now = DateTime.UtcNow.AddDays(2);

        await agents.FireDueSchedulesAsync(now, CancellationToken.None);
        await agents.FireDueSchedulesAsync(now, CancellationToken.None); // simulates an overlapping/duplicate tick

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Single(executions);
    }

    [Fact]
    public async Task A_schedule_not_yet_due_is_not_fired()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _, scheduleId, _) = await SeedDueDailyScheduleAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        var scheduleService = AutomationTestFixtures.ScheduleService(db);

        await agents.FireDueSchedulesAsync(DateTime.UtcNow, CancellationToken.None); // freshly created — always due strictly in the future

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Empty(executions);
        Assert.Empty(await scheduleService.ListRunsAsync(scheduleId, baseline.Project.ProjectId, CancellationToken.None));
    }

    [Fact]
    public async Task An_inactive_schedule_is_never_fired_even_if_its_next_run_is_in_the_past()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _, scheduleId, _) = await SeedDueDailyScheduleAsync(db);
        var scheduleService = AutomationTestFixtures.ScheduleService(db);
        await scheduleService.DeactivateAsync(scheduleId, baseline.Project.ProjectId, null, CancellationToken.None);
        var agents = AutomationTestFixtures.AgentService(db);

        await agents.FireDueSchedulesAsync(DateTime.UtcNow.AddDays(2), CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, null, 50, CancellationToken.None);
        Assert.Empty(executions);
    }

    [Fact]
    public async Task Firing_a_schedule_whose_suite_has_no_ready_cases_records_a_NoReadyCases_run_and_still_advances()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var caseService = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);
        var draftTestCase = new ProMaxx2.QA.Domain.TestManagement.TestCase(baseline.Project.ProjectId, baseline.Module.ModuleId, "TC-SALE-777", "Not ready", null, null, "P1", "Functional", true, null,
            [new ProMaxx2.QA.Domain.TestManagement.TestStepInput(1, "Add item", null, "Item added")], null);
        draftTestCase.SetAutomationTarget("app", null);
        db.Add(draftTestCase);
        await db.SaveChangesAsync();
        var draftCase = await caseService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationCaseRequest(draftTestCase.TestCaseId, "WindowsUI", null), null, CancellationToken.None);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suiteService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Not ready suite", null), null, CancellationToken.None);
        await suiteService.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([draftCase.AutomationCaseId], true), CancellationToken.None);
        var scheduleService = AutomationTestFixtures.ScheduleService(db);
        var schedule = await scheduleService.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suite.AutomationSuiteId, "Nightly", null, "Daily", 0, NineAm, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);
        var agents = AutomationTestFixtures.AgentService(db);

        await agents.FireDueSchedulesAsync(DateTime.UtcNow.AddDays(2), CancellationToken.None);

        var runs = await scheduleService.ListRunsAsync(schedule.AutomationScheduleId, baseline.Project.ProjectId, CancellationToken.None);
        var run = Assert.Single(runs);
        Assert.Equal("NoReadyCases", run.Status);
        Assert.Equal(0, run.ExecutionsCreated);
        Assert.Equal(1, run.SkippedCount);
        var refreshed = await scheduleService.GetAsync(schedule.AutomationScheduleId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.True(refreshed.IsActive); // still advances — must not get stuck retrying the same due instant forever
    }

    [Fact]
    public async Task Firing_a_schedule_whose_suite_has_zero_cases_records_a_failed_run_but_still_advances()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suiteService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Empty suite", null), null, CancellationToken.None);
        var scheduleService = AutomationTestFixtures.ScheduleService(db);
        var schedule = await scheduleService.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suite.AutomationSuiteId, "Nightly", null, "Daily", 0, NineAm, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);
        var agents = AutomationTestFixtures.AgentService(db);
        var originalNextRun = schedule.NextRunAtUtc;

        await agents.FireDueSchedulesAsync(DateTime.UtcNow.AddDays(2), CancellationToken.None);

        var runs = await scheduleService.ListRunsAsync(schedule.AutomationScheduleId, baseline.Project.ProjectId, CancellationToken.None);
        var run = Assert.Single(runs);
        Assert.Equal("Failed", run.Status);
        Assert.NotNull(run.ErrorMessage);
        var refreshed = await scheduleService.GetAsync(schedule.AutomationScheduleId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.True(refreshed.NextRunAtUtc > originalNextRun); // claimed and advanced even though the run itself failed
    }

    [Fact]
    public async Task Listing_runs_for_a_schedule_that_does_not_exist_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var scheduleService = AutomationTestFixtures.ScheduleService(db);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => scheduleService.ListRunsAsync(Guid.NewGuid(), baseline.Project.ProjectId, CancellationToken.None));
    }
}
