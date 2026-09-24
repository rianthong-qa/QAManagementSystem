using ProMaxx2.QA.Application.Automation;

namespace ProMaxx2.QA.Api.Services;

/// <summary>AUT-REL-002: เก็บกวาดงาน Automation ที่ค้างเพราะ Agent หาย — execution Running ที่ Agent เงียบเกิน 10 นาที
/// (หรือรันนานเกิน hard cap), snapshot/restore/verification ที่ Agent รับไปแล้วไม่รายงานผล และ seed run ที่ค้าง;
/// เดิมงานเหล่านี้ค้าง Running/Assigned ถาวร และ server ไม่เคยตั้งสถานะ AgentLost เลย.
/// ตรรกะทั้งหมดอยู่ใน <see cref="AutomationAgentService.ReapStaleWorkAsync"/> — class นี้ดูแลแค่ timer และไม่ให้ tick
/// ที่ล้มหยุด worker (รูปแบบเดียวกับ <see cref="AutomationScheduleWorker"/>)</summary>
public sealed class AutomationReaperWorker(IServiceScopeFactory scopeFactory, ILogger<AutomationReaperWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var agentService = scope.ServiceProvider.GetRequiredService<AutomationAgentService>();
                var result = await agentService.ReapStaleWorkAsync(DateTime.UtcNow, stoppingToken);
                var data = result.DataWork;
                if (result.ExecutionsLost + result.ExecutionsTimedOut + data.Snapshots + data.Restores + data.Verifications + data.SeedRunsReclaimed > 0)
                    logger.LogWarning("AutomationReaper closed stale work: {Lost} execution(s) AgentLost, {TimedOut} Timeout, {Snapshots} snapshot(s), {Restores} restore(s), {Verifications} verification(s), {Seeds} seed run(s) reclaimed.",
                        result.ExecutionsLost, result.ExecutionsTimedOut, data.Snapshots, data.Restores, data.Verifications, data.SeedRunsReclaimed);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "AutomationReaperWorker tick failed — will retry on the next poll.");
            }

            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
