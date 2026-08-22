using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProMaxx2.QA.Application.Execution;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// Background worker that periodically processes due automation schedules (creates queue jobs
/// on time) and recovers expired runner leases (unsticks Claimed/Running jobs whose runner died).
/// Without this, schedules only fire when someone opens the Automation page or a runner heartbeats.
/// </summary>
public sealed class AutomationScheduleWorker(IServiceScopeFactory scopeFactory, ILogger<AutomationScheduleWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IAutomationRunRepository>();
                var changes = await repository.RunScheduledWorkAsync(stoppingToken);
                if (changes > 0) logger.LogInformation("Automation scheduled work processed {Changes} item(s).", changes);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Automation scheduled work processing failed.");
            }
        }
    }
}
