using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Regression;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>AUT-REG-002: when a Build is marked a Release Candidate, automatically run Regression Impact Analysis for
/// every active Regression Schedule on that release and — for schedules that have an Environment configured — queue
/// an Automation run for the eligible recommended cases. Mirrors <see cref="AutomationBuildTriggerService"/>'s
/// "fire on a Build event, best-effort per schedule, record an audit trail" shape, reusing the existing
/// <see cref="RegressionActivity"/> feed (already shown in the Regression page's Recent Activity panel) instead of
/// adding new UI. De-duplication reuses <see cref="RegressionSchedule.LastNotifiedBuildId"/>/
/// <see cref="RegressionSchedule.Acknowledge"/> — the same field the passive notification already relies on, so
/// firing here also silences that notification for the same build. <c>Acknowledge</c> is only called on
/// non-exceptional outcomes, so an unexpected failure still leaves the build visible as a pending manual
/// notification instead of silently vanishing.</summary>
public sealed class RegressionScheduleTriggerService(QaDbContext db, AutomationAgentService agentService)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task FireForBuildAsync(Guid releaseId, Guid buildId, CancellationToken ct)
    {
        var release = await db.Releases.AsNoTracking().SingleOrDefaultAsync(x => x.ReleaseId == releaseId, ct);
        if (release is null) return;
        var schedules = await db.RegressionSchedules.Where(x => x.ReleaseId == releaseId && x.IsActive && x.LastNotifiedBuildId != buildId).ToListAsync(ct);

        foreach (var schedule in schedules)
        {
            try
            {
                var settings = await ResolveSettingsAsync(schedule.RegressionProfileId, ct);
                var request = new RegressionImpactRequest(buildId, [], settings.IncludeSharedDependencies, settings.MinimumPriority,
                    settings.DatabaseChange, settings.ApiChange, settings.CalculationChange, settings.PermissionChange, settings.InstallerChange, settings.DefectFix,
                    null, $"Auto-triggered by Scheduled Regression '{schedule.Name}'", 1, 200,
                    settings.DirectImpactWeight, settings.HistoricalDefectWeight, settings.CriticalPriorityWeight, settings.SharedDependencyWeight,
                    RecordAnalysis: true, IncludeAllCaseIds: true);
                var impact = await RegressionImpactAnalyzer.RunAsync(db, release, request, null, ct);
                var requiredCaseIds = impact.Cases.Where(x => x.IsRequired).Select(x => x.TestCaseId).ToArray();
                var plan = await RegressionAutomationRunPlanner.ResolveEligibleAsync(db, requiredCaseIds, ct);

                string status; int created = 0, skipped = 0;
                if (!schedule.EnvironmentId.HasValue) status = "NoEnvironmentConfigured";
                else if (plan.EligibleAutomationCaseIds.Count == 0) status = "NoEligibleCases";
                else
                {
                    var result = await agentService.BatchRunAsync(release.ProjectId, new BatchRunRequest(plan.EligibleAutomationCaseIds, buildId, schedule.EnvironmentId.Value, null, schedule.Priority), null, ct);
                    created = result.Created.Count; skipped = result.SkippedCodes.Count;
                    status = created > 0 ? "Succeeded" : "NoEligibleCases";
                }

                db.RegressionActivities.Add(new RegressionActivity(release.ProjectId, releaseId, buildId, "ScheduledAutomationRun",
                    $"{schedule.Name}: {impact.Metrics.RecommendedCases} recommended, {plan.EligibleCount} eligible, {created} run, {skipped} skipped ({status})", null));
                schedule.Acknowledge(buildId);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort, mirrors AutomationBuildTriggerService: one schedule failing must never block the RC-marking
                // request or the other schedules. Deliberately NOT acknowledging here leaves the build visible via the
                // existing passive notification, so a real failure still surfaces for manual follow-up.
                db.RegressionActivities.Add(new RegressionActivity(release.ProjectId, releaseId, buildId, "ScheduledAutomationRun", $"{schedule.Name}: ล้มเหลว — {ex.Message}", null));
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task<RegressionProfileSettings> ResolveSettingsAsync(Guid? profileId, CancellationToken ct)
    {
        if (!profileId.HasValue) return new RegressionProfileSettings();
        var json = await db.RegressionProfiles.AsNoTracking().Where(x => x.RegressionProfileId == profileId.Value && x.IsActive).Select(x => x.SettingsJson).SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return new RegressionProfileSettings();
        try { return JsonSerializer.Deserialize<RegressionProfileSettings>(json, JsonOptions) ?? new RegressionProfileSettings(); }
        catch (JsonException) { return new RegressionProfileSettings(); }
    }

    /// <summary>Mirrors the 12 keys `RegressionPage.currentSettings()` (frontend) serializes into
    /// `RegressionProfile.SettingsJson`. Defaults match the frontend form's initial state.</summary>
    private sealed record RegressionProfileSettings(
        string MinimumPriority = "P1", bool IncludeSharedDependencies = true,
        bool DatabaseChange = false, bool ApiChange = false, bool CalculationChange = false,
        bool PermissionChange = false, bool InstallerChange = false, bool DefectFix = false,
        int DirectImpactWeight = 40, int HistoricalDefectWeight = 30, int CriticalPriorityWeight = 20, int SharedDependencyWeight = 10);
}
