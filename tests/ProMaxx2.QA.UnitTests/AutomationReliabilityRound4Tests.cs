using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Domain.Automation;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>AUT-REL-001/002. หมายเหตุ: rowversion (complete ชน cancel/complete ซ้อน) ตรวจได้เฉพาะบน SQL Server —
/// EF InMemory ไม่สร้างค่า rowversion จึงทดสอบที่นี่เฉพาะพฤติกรรมเชิงลำดับ (idempotency, reaper, เลข version)</summary>
public sealed class AutomationReliabilityRound4Tests
{
    private static async Task<(AutomationTestFixtures.Baseline Baseline, AutomationAgentService Agents, Guid ExecutionId)> SeedRunningAsync(QaDbContext db)
    {
        var agents = AutomationTestFixtures.AgentService(db);
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException("Expected a job.");
        return (baseline, agents, claim.AutomationExecutionId);
    }

    private static async Task SetAgentHeartbeatAsync(QaDbContext db, DateTime at)
    {
        var agent = await db.AutomationAgents.SingleAsync(x => x.AgentCode == "AGENT-A");
        agent.Heartbeat(at, agent.CurrentExecutionId);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task New_version_number_continues_from_the_highest_even_after_approving_an_older_version()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, v1) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var service = AutomationTestFixtures.CaseService(db, baseline.Project.ProjectId);
        var v2 = await service.CreateVersionAsync(readyCase.AutomationCaseId, baseline.Project.ProjectId, new CreateAutomationVersionRequest(AutomationTestFixtures.SampleDsl, "v2"), null, CancellationToken.None);
        Assert.Equal(2, v2.VersionNo);

        await service.ApproveVersionAsync(v1, baseline.Project.ProjectId, null, CancellationToken.None); // CurrentVersionNo ถอยกลับเป็น 1
        var v3 = await service.CreateVersionAsync(readyCase.AutomationCaseId, baseline.Project.ProjectId, new CreateAutomationVersionRequest(AutomationTestFixtures.SampleDsl, "v3"), null, CancellationToken.None);

        Assert.Equal(3, v3.VersionNo); // เดิมได้ 2 ซ้ำกับ version ที่มีอยู่
    }

    [Fact]
    public async Task Execution_and_job_are_created_together()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (_, _, executionId) = await SeedRunningAsync(db);
        var execution = await db.AutomationExecutions.AsNoTracking().SingleAsync(x => x.AutomationExecutionId == executionId);
        var job = await db.AutomationJobs.AsNoTracking().SingleAsync(x => x.AutomationExecutionId == executionId);
        Assert.Equal(job.JobId, execution.JobId);
    }

    [Fact]
    public async Task Duplicate_step_report_is_ignored_so_evidence_can_still_be_attached()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (_, agents, executionId) = await SeedRunningAsync(db);
        var now = DateTime.UtcNow;
        var report = new ReportStepResultRequest(1, "LOGIN", "Pass", "first", null, null, null, now, now, "AGENT-A");

        await agents.ReportStepResultAsync(executionId, report, CancellationToken.None);
        await agents.ReportStepResultAsync(executionId, report with { ActualResult = "retried" }, CancellationToken.None);

        var steps = await db.AutomationStepResults.AsNoTracking().Where(x => x.AutomationExecutionId == executionId).ToListAsync();
        Assert.Single(steps);
        Assert.Equal("first", steps[0].ActualResult);
        await agents.UploadStepEvidenceAsync(executionId, 1, "evidence/step1.png", CancellationToken.None); // เดิม SingleOrDefault พังเมื่อมีสองแถว
    }

    [Fact]
    public async Task Reaper_marks_execution_AgentLost_when_the_agent_stops_sending_heartbeats_and_queues_a_retry()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, agents, executionId) = await SeedRunningAsync(db);
        var now = DateTime.UtcNow;
        await SetAgentHeartbeatAsync(db, now - AutomationAgentService.AgentLostAfter - TimeSpan.FromMinutes(1));

        var result = await agents.ReapStaleWorkAsync(now, CancellationToken.None);

        Assert.Equal(1, result.ExecutionsLost);
        var execution = await agents.GetExecutionAsync(executionId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal("AgentLost", execution.Status);
        Assert.Equal("AUT-AGENT-001", execution.ErrorCode);
        Assert.Equal("AgentLost", (await db.AutomationJobs.AsNoTracking().SingleAsync(x => x.AutomationExecutionId == executionId)).Status);
        Assert.True(await db.AutomationExecutions.AnyAsync(x => x.RetryOfExecutionId == executionId)); // classifier: AgentFailure → Retry

        // Agent กลับมารายงานผลช้า — ต้องไม่เขียนทับ AgentLost
        await agents.CompleteExecutionAsync(executionId, new CompleteExecutionRequest("Passed", null, null, null, "AGENT-A"), CancellationToken.None);
        Assert.Equal("AgentLost", (await agents.GetExecutionAsync(executionId, baseline.Project.ProjectId, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Reaper_leaves_running_executions_alone_while_the_agent_is_alive()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, agents, executionId) = await SeedRunningAsync(db);
        await SetAgentHeartbeatAsync(db, DateTime.UtcNow);

        var result = await agents.ReapStaleWorkAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, result.ExecutionsLost + result.ExecutionsTimedOut);
        Assert.Equal("Running", (await agents.GetExecutionAsync(executionId, baseline.Project.ProjectId, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Reaper_times_out_an_execution_stuck_past_the_hard_cap_even_if_the_agent_is_alive()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, agents, executionId) = await SeedRunningAsync(db);
        var later = DateTime.UtcNow + AutomationAgentService.MaxExecutionDuration + TimeSpan.FromMinutes(5);
        await SetAgentHeartbeatAsync(db, later); // agent ยังมีชีวิต แต่ execution เริ่มไปนานเกิน hard cap แล้ว

        var result = await agents.ReapStaleWorkAsync(later, CancellationToken.None);

        Assert.Equal(1, result.ExecutionsTimedOut);
        var execution = await agents.GetExecutionAsync(executionId, baseline.Project.ProjectId, CancellationToken.None);
        Assert.Equal("Timeout", execution.Status);
        Assert.Equal("AUT-JOB-001", execution.ErrorCode);
    }

    [Fact]
    public async Task Reaper_fails_a_snapshot_whose_agent_never_reported_back()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, agents, _) = await SeedRunningAsync(db);
        await SetAgentHeartbeatAsync(db, DateTime.UtcNow);
        var agentId = (await db.AutomationAgents.SingleAsync()).AgentId;
        var fresh = new AutomationDbSnapshot(baseline.Project.ProjectId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId, null);
        var stuck = new AutomationDbSnapshot(baseline.Project.ProjectId, baseline.Environment.TestEnvironmentId, baseline.Build.BuildId, null);
        fresh.Claim(agentId);
        stuck.Claim(agentId);
        db.AddRange(fresh, stuck);
        await db.SaveChangesAsync();

        // เดินเวลาไปเกิน DataJobStaleAfter — heartbeat ของ agent ก็เดินตามเพื่อไม่ให้ execution ถูกปิดไปด้วย
        var later = DateTime.UtcNow + AutomationRepository.DataJobStaleAfter + TimeSpan.FromMinutes(1);
        await SetAgentHeartbeatAsync(db, later);
        var result = await agents.ReapStaleWorkAsync(later, CancellationToken.None);

        Assert.Equal(2, result.DataWork.Snapshots);
        Assert.All(await db.AutomationDbSnapshots.AsNoTracking().ToListAsync(), s => Assert.Equal("Failed", s.Status));
    }
}
