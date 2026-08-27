using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-P1-005 (Persistent Automation Schedule): create/edit/activate/deactivate a recurring
/// (Once/Daily/Weekly, timezone-aware) timetable for re-running an Automation Suite, and the computed next-run time.
/// Does not cover actually firing a run on schedule — that is AUT-P1-006 (Schedule Execution Worker).</summary>
public sealed class AutomationScheduleTests
{
    private static readonly TimeOnly NineAm = new(9, 0);

    private static async Task<(AutomationTestFixtures.Baseline Baseline, Guid SuiteId)> SeedSuiteAsync(ProMaxx2.QA.Infrastructure.Persistence.QaDbContext db)
    {
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suiteService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Smoke", null), null, CancellationToken.None);
        return (baseline, suite.AutomationSuiteId);
    }

    [Fact]
    public async Task Create_daily_schedule_computes_a_next_run_within_the_next_day_at_the_configured_time()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteAsync(db);
        var service = AutomationTestFixtures.ScheduleService(db);

        var created = await service.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suiteId, "Nightly Smoke", null, "Daily", 0, NineAm, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);

        Assert.True(created.IsActive);
        Assert.True(created.NextRunAtUtc > DateTime.UtcNow);
        Assert.True(created.NextRunAtUtc <= DateTime.UtcNow.AddDays(1).AddMinutes(1));
        Assert.Equal(NineAm, TimeOnly.FromDateTime(created.NextRunAtUtc));
        Assert.Equal("Smoke", created.SuiteName);
        Assert.Equal(baseline.Build.BuildNumber, created.BuildNumber);
        Assert.Equal(baseline.Environment.EnvironmentName, created.EnvironmentName);
    }

    [Fact]
    public async Task Weekly_schedule_lands_on_one_of_the_selected_days_at_the_configured_time()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteAsync(db);
        var service = AutomationTestFixtures.ScheduleService(db);
        var selectedDays = new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday };
        var mask = selectedDays.Aggregate(0, (m, d) => m | (1 << (int)d));

        var created = await service.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suiteId, "Weekly Regression", null, "Weekly", mask, NineAm, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);

        Assert.Contains(created.NextRunAtUtc.DayOfWeek, selectedDays);
        Assert.True(created.NextRunAtUtc > DateTime.UtcNow);
        Assert.True(created.NextRunAtUtc <= DateTime.UtcNow.AddDays(7).AddMinutes(1));
        Assert.Equal(NineAm, TimeOnly.FromDateTime(created.NextRunAtUtc));
    }

    [Fact]
    public async Task Weekly_schedule_with_no_days_selected_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteAsync(db);
        var service = AutomationTestFixtures.ScheduleService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suiteId, "Weekly no days", null, "Weekly", 0, NineAm, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None));
    }

    [Fact]
    public async Task Once_schedule_in_the_future_computes_the_exact_configured_instant()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteAsync(db);
        var service = AutomationTestFixtures.ScheduleService(db);
        var onceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));

        var created = await service.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suiteId, "One-off UAT run", null, "Once", 0, NineAm, onceDate, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);

        Assert.Equal(onceDate.ToDateTime(NineAm), created.NextRunAtUtc);
    }

    [Fact]
    public async Task Once_schedule_in_the_past_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteAsync(db);
        var service = AutomationTestFixtures.ScheduleService(db);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suiteId, "Too late", null, "Once", 0, NineAm, yesterday, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_timezone_id_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteAsync(db);
        var service = AutomationTestFixtures.ScheduleService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suiteId, "Bad tz", null, "Daily", 0, NineAm, null, "Not/A_Real_Zone", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None));
    }

    [Fact]
    public async Task A_local_time_of_day_converts_through_a_non_utc_timezone()
    {
        // "SE Asia Standard Time" (Bangkok, UTC+7, no DST) — 09:00 local must be 02:00 UTC the same calendar day.
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteAsync(db);
        var service = AutomationTestFixtures.ScheduleService(db);

        var created = await service.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suiteId, "Bangkok nightly", null, "Daily", 0, NineAm, null, "SE Asia Standard Time", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);

        Assert.Equal(new TimeOnly(2, 0), TimeOnly.FromDateTime(created.NextRunAtUtc));
    }

    [Fact]
    public async Task Create_for_a_suite_that_does_not_exist_throws_not_found()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var service = AutomationTestFixtures.ScheduleService(db);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(Guid.NewGuid(), "Orphan", null, "Daily", 0, NineAm, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None));
    }

    [Fact]
    public async Task Update_changes_frequency_and_recomputes_next_run()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteAsync(db);
        var service = AutomationTestFixtures.ScheduleService(db);
        var created = await service.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suiteId, "Nightly", null, "Daily", 0, NineAm, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);
        var newTime = new TimeOnly(14, 30);

        var updated = await service.UpdateAsync(created.AutomationScheduleId, baseline.Project.ProjectId,
            new UpdateAutomationScheduleRequest("Nightly", null, "Daily", 0, newTime, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);

        Assert.Equal(newTime, TimeOnly.FromDateTime(updated.NextRunAtUtc));
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task Deactivate_then_activate_flips_isActive_and_recomputes_next_run()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteAsync(db);
        var service = AutomationTestFixtures.ScheduleService(db);
        var created = await service.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suiteId, "Nightly", null, "Daily", 0, NineAm, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);

        var deactivated = await service.DeactivateAsync(created.AutomationScheduleId, baseline.Project.ProjectId, null, CancellationToken.None);
        Assert.False(deactivated.IsActive);

        var reactivated = await service.ActivateAsync(created.AutomationScheduleId, baseline.Project.ProjectId, null, CancellationToken.None);
        Assert.True(reactivated.IsActive);
        Assert.True(reactivated.NextRunAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Activating_an_already_active_schedule_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteAsync(db);
        var service = AutomationTestFixtures.ScheduleService(db);
        var created = await service.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suiteId, "Nightly", null, "Daily", 0, NineAm, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ActivateAsync(created.AutomationScheduleId, baseline.Project.ProjectId, null, CancellationToken.None));
    }

    [Fact]
    public async Task Deactivating_an_already_inactive_schedule_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteAsync(db);
        var service = AutomationTestFixtures.ScheduleService(db);
        var created = await service.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suiteId, "Nightly", null, "Daily", 0, NineAm, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);
        await service.DeactivateAsync(created.AutomationScheduleId, baseline.Project.ProjectId, null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeactivateAsync(created.AutomationScheduleId, baseline.Project.ProjectId, null, CancellationToken.None));
    }

    [Fact]
    public async Task List_filters_by_isActive()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, suiteId) = await SeedSuiteAsync(db);
        var service = AutomationTestFixtures.ScheduleService(db);
        var active = await service.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suiteId, "Active", null, "Daily", 0, NineAm, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);
        var inactive = await service.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suiteId, "Inactive", null, "Daily", 0, NineAm, null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None);
        await service.DeactivateAsync(inactive.AutomationScheduleId, baseline.Project.ProjectId, null, CancellationToken.None);

        var activeOnly = await service.ListAsync(baseline.Project.ProjectId, true, CancellationToken.None);
        var all = await service.ListAsync(baseline.Project.ProjectId, null, CancellationToken.None);

        Assert.Single(activeOnly, x => x.AutomationScheduleId == active.AutomationScheduleId);
        Assert.Equal(2, all.Count);
    }
}
