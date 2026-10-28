using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.UnitTests;

/// <summary>AUT-REL-003 (schedule tick ทนความล้มเหลว) และ AUT-SEC-002 (run-preview กรอง Project)</summary>
public sealed class AutomationRound7Tests
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    [Fact]
    public void Local_time_inside_the_spring_forward_gap_moves_to_the_first_valid_minute()
    {
        // 2026-03-08: US clocks jump 02:00 → 03:00, so 02:30 local does not exist
        var gap = new DateTime(2026, 3, 8, 2, 30, 0);
        Assert.True(Eastern.IsInvalidTime(gap));

        var utc = AutomationSchedule.ToUtcSkippingDstGap(gap, Eastern);

        Assert.Equal(new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc), utc); // 03:00 EDT
    }

    [Fact]
    public void Daily_schedule_at_a_nonexistent_local_time_still_advances_across_the_dst_change()
    {
        var schedule = new AutomationSchedule(Guid.NewGuid(), Guid.NewGuid(), "DST", null, "Daily", 0, new TimeOnly(2, 30), null,
            "Eastern Standard Time", Guid.NewGuid(), Guid.NewGuid(), null, 5, null);

        schedule.RecordFired(new DateTime(2026, 3, 7, 12, 0, 0, DateTimeKind.Utc)); // เดิม throw ArgumentException ตรงนี้

        Assert.Equal(new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc), schedule.NextRunAtUtc);
        Assert.True(schedule.IsActive);
    }

    [Fact]
    public async Task A_schedule_that_cannot_compute_its_next_run_is_deactivated_without_blocking_the_others()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var suites = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suites.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Nightly", null), null, CancellationToken.None);
        await suites.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);
        var schedules = AutomationTestFixtures.ScheduleService(db);
        AutomationScheduleDto Create(string name) => schedules.CreateAsync(baseline.Project.ProjectId,
            new CreateAutomationScheduleRequest(suite.AutomationSuiteId, name, null, "Daily", 0, new TimeOnly(9, 0), null, "UTC", baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5),
            null, CancellationToken.None).GetAwaiter().GetResult();
        var broken = Create("Broken timezone");
        var healthy = Create("Healthy");
        // timezone ถูกลบออกจากเครื่องหลังตั้ง schedule ไว้แล้ว
        var brokenEntity = await db.AutomationSchedules.SingleAsync(x => x.AutomationScheduleId == broken.AutomationScheduleId);
        db.Entry(brokenEntity).Property(x => x.TimeZoneId).CurrentValue = "Removed/Zone";
        await db.SaveChangesAsync();

        var agents = AutomationTestFixtures.AgentService(db);
        await agents.FireDueSchedulesAsync(DateTime.UtcNow.AddDays(2), CancellationToken.None);

        Assert.False((await db.AutomationSchedules.AsNoTracking().SingleAsync(x => x.AutomationScheduleId == broken.AutomationScheduleId)).IsActive);
        var brokenRun = await db.AutomationScheduleRuns.AsNoTracking().SingleAsync(x => x.AutomationScheduleId == broken.AutomationScheduleId);
        Assert.Equal("Failed", brokenRun.Status);
        Assert.Contains("AUT-REL-003", brokenRun.ErrorMessage);
        var healthyRun = await db.AutomationScheduleRuns.AsNoTracking().SingleAsync(x => x.AutomationScheduleId == healthy.AutomationScheduleId);
        Assert.Equal("Succeeded", healthyRun.Status);
    }

    [Fact]
    public async Task Run_preview_hides_test_cases_from_projects_the_caller_cannot_access()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var ids = new[] { baseline.TestCase.TestCaseId };

        var own = await RegressionAutomationRunPlanner.ResolveEligibleAsync(db, ids, CancellationToken.None, [baseline.Project.ProjectId]);
        var other = await RegressionAutomationRunPlanner.ResolveEligibleAsync(db, ids, CancellationToken.None, [Guid.NewGuid()]);

        Assert.Single(own.Items);
        Assert.Empty(other.Items);
        Assert.Empty(other.EligibleAutomationCaseIds);
    }
}
