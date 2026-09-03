namespace ProMaxx2.QA.Api.Services;

/// <summary>CRM integration Phase 2: polls every Defect linked to a CRM ticket (<c>CrmSyncStatus == "Linked"</c>)
/// and reflects Status/Assignto changes back into QA Hub (snapshot + DefectActivity log + email — see
/// CrmSyncService). Same shape as AutomationScheduleWorker — a plain polling BackgroundService, since there is no
/// queue/webhook from CRM to push into.
///
/// Interval is configurable (Setting Center → CRM Sync → "รอบ Poll") instead of a fixed constant — read fresh from
/// CrmSyncSettingsService.GetPollIntervalAsync on every tick (not cached), so an admin's change takes effect on
/// the very next tick, not after an API restart. A plain <c>Task.Delay</c> loop is used instead of PeriodicTimer
/// specifically because PeriodicTimer's interval is fixed at construction and can't be changed mid-flight.
/// Default 2 minutes matches the legacy Google-Sheets-export flow this replaces
/// (see Document/03-Architecture-and-Plan/CRM_INTEGRATION_PLAN.md §1/§3).</summary>
public sealed class CrmSyncWorker(IServiceScopeFactory scopeFactory, ILogger<CrmSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(ProMaxx2.QA.Domain.Integrations.CrmSyncSettings.DefaultPollIntervalMinutes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = DefaultInterval;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var crmSyncSettings = scope.ServiceProvider.GetRequiredService<CrmSyncSettingsService>();
                interval = await crmSyncSettings.GetPollIntervalAsync(stoppingToken);
                var syncService = scope.ServiceProvider.GetRequiredService<CrmSyncService>();
                await syncService.PollLinkedDefectsAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; } // normal shutdown
            catch (Exception ex)
            {
                // A tick failing (e.g. a transient CRM/DB error) must not stop the worker permanently — log and
                // retry on the next poll instead of letting the exception propagate out of ExecuteAsync and kill
                // the service. interval falls back to DefaultInterval if reading the configured value itself failed.
                logger.LogError(ex, "CrmSyncWorker tick failed — will retry in {Interval}.", interval);
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
