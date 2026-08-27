using ProMaxx2.QA.Application.Automation;

namespace ProMaxx2.QA.Api.Services;

/// <summary>AUT-P1-006: polls for Automation Schedules whose <c>NextRunAtUtc</c> has arrived and fires them. This is
/// the app's first background worker — there is no existing scheduler/queue infra to plug into, so a simple
/// polling <see cref="BackgroundService"/> is the whole implementation. All of the actual exactly-once-claim and
/// recovery-after-restart logic lives in <c>AutomationAgentService.FireDueSchedulesAsync</c> (and, underneath it,
/// <c>IAutomationScheduleRepository.ClaimDueSchedulesAsync</c>'s atomic claim) — this class only owns the timer loop
/// and makes sure one bad tick can never take the worker down.</summary>
public sealed class AutomationScheduleWorker(IServiceScopeFactory scopeFactory, ILogger<AutomationScheduleWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var agentService = scope.ServiceProvider.GetRequiredService<AutomationAgentService>();
                await agentService.FireDueSchedulesAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) { break; } // normal shutdown
            catch (Exception ex)
            {
                // A tick failing (e.g. a transient DB error) must not stop the worker permanently — log and retry
                // on the next poll instead of letting the exception propagate out of ExecuteAsync and kill the service.
                logger.LogError(ex, "AutomationScheduleWorker tick failed — will retry on the next poll.");
            }

            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
