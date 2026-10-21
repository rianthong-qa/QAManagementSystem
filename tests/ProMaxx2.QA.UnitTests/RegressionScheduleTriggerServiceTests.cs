using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Domain.Execution;

namespace ProMaxx2.QA.UnitTests;

public sealed class RegressionScheduleTriggerServiceTests
{
    [Fact]
    public async Task Schedule_with_environment_configured_auto_runs_eligible_cases_and_acknowledges()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        baseline.TestCase.ChangeStatus("Ready", null); // eligibility also requires the underlying TestCase (not just the AutomationCase) to be Ready
        var schedule = new RegressionSchedule(baseline.Project.ProjectId, baseline.Release.ReleaseId, null, "Nightly", null);
        schedule.ConfigureAutomation(baseline.Environment.TestEnvironmentId, 7);
        db.RegressionSchedules.Add(schedule);
        await db.SaveChangesAsync();
        var service = new RegressionScheduleTriggerService(db, AutomationTestFixtures.AgentService(db));

        await service.FireForBuildAsync(baseline.Release.ReleaseId, baseline.Build.BuildId, CancellationToken.None);

        var executions = await db.AutomationExecutions.Where(x => x.AutomationCaseId == readyCase.AutomationCaseId).ToListAsync();
        var execution = Assert.Single(executions);
        Assert.Equal(baseline.Build.BuildId, execution.BuildId);
        Assert.Equal(baseline.Environment.TestEnvironmentId, execution.EnvironmentId);

        var activity = Assert.Single(await db.RegressionActivities.Where(x => x.Action == "ScheduledAutomationRun").ToListAsync());
        Assert.Contains("1 run", activity.Details);

        var stored = await db.RegressionSchedules.SingleAsync(x => x.RegressionScheduleId == schedule.RegressionScheduleId);
        Assert.Equal(baseline.Build.BuildId, stored.LastNotifiedBuildId);
    }

    [Fact]
    public async Task Schedule_without_environment_only_notifies_no_automation_run()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, _, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var schedule = new RegressionSchedule(baseline.Project.ProjectId, baseline.Release.ReleaseId, null, "Nightly", null);
        db.RegressionSchedules.Add(schedule);
        await db.SaveChangesAsync();
        var service = new RegressionScheduleTriggerService(db, AutomationTestFixtures.AgentService(db));

        await service.FireForBuildAsync(baseline.Release.ReleaseId, baseline.Build.BuildId, CancellationToken.None);

        Assert.Empty(await db.AutomationExecutions.ToListAsync());
        var activity = Assert.Single(await db.RegressionActivities.Where(x => x.Action == "ScheduledAutomationRun").ToListAsync());
        Assert.Contains("NoEnvironmentConfigured", activity.Details);
        var stored = await db.RegressionSchedules.SingleAsync(x => x.RegressionScheduleId == schedule.RegressionScheduleId);
        Assert.Equal(baseline.Build.BuildId, stored.LastNotifiedBuildId); // still acknowledged - silences the passive notification too
    }

    [Fact]
    public async Task Firing_twice_for_the_same_build_does_not_duplicate_the_run()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        baseline.TestCase.ChangeStatus("Ready", null);
        var schedule = new RegressionSchedule(baseline.Project.ProjectId, baseline.Release.ReleaseId, null, "Nightly", null);
        schedule.ConfigureAutomation(baseline.Environment.TestEnvironmentId, 5);
        db.RegressionSchedules.Add(schedule);
        await db.SaveChangesAsync();
        var service = new RegressionScheduleTriggerService(db, AutomationTestFixtures.AgentService(db));

        await service.FireForBuildAsync(baseline.Release.ReleaseId, baseline.Build.BuildId, CancellationToken.None);
        await service.FireForBuildAsync(baseline.Release.ReleaseId, baseline.Build.BuildId, CancellationToken.None);

        Assert.Single(await db.AutomationExecutions.Where(x => x.AutomationCaseId == readyCase.AutomationCaseId).ToListAsync());
        Assert.Single(await db.RegressionActivities.Where(x => x.Action == "ScheduledAutomationRun").ToListAsync());
    }

    [Fact]
    public async Task Malformed_profile_settings_fall_back_to_defaults_instead_of_throwing()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        baseline.TestCase.ChangeStatus("Ready", null);
        var profile = new RegressionProfile(baseline.Project.ProjectId, "Broken profile", "Private", null, "not-valid-json");
        db.RegressionProfiles.Add(profile);
        var schedule = new RegressionSchedule(baseline.Project.ProjectId, baseline.Release.ReleaseId, profile.RegressionProfileId, "Nightly", null);
        schedule.ConfigureAutomation(baseline.Environment.TestEnvironmentId, 5);
        db.RegressionSchedules.Add(schedule);
        await db.SaveChangesAsync();
        var service = new RegressionScheduleTriggerService(db, AutomationTestFixtures.AgentService(db));

        await service.FireForBuildAsync(baseline.Release.ReleaseId, baseline.Build.BuildId, CancellationToken.None);

        // Falls back to defaults (MinimumPriority "P1", IncludeSharedDependencies true) which still catch the P0 seeded case.
        Assert.Single(await db.AutomationExecutions.Where(x => x.AutomationCaseId == readyCase.AutomationCaseId).ToListAsync());
        var activity = Assert.Single(await db.RegressionActivities.Where(x => x.Action == "ScheduledAutomationRun").ToListAsync());
        Assert.DoesNotContain("ล้มเหลว", activity.Details);
    }
}
