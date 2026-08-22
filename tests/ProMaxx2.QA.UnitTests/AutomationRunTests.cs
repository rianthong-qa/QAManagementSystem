using ProMaxx2.QA.Domain.Execution;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

public sealed class AutomationRunTests
{
    [Fact]
    public void Run_summarizes_case_results()
    {
        var run = new AutomationRun(
            Guid.NewGuid(), null, null, null, "pos", "runner-01", DateTime.UtcNow,
            [
                new(null, "tc-001", "Passed", 1200, null, null),
                new(null, "tc-002", "Failed", 800, "Expected total mismatch", "evidence/tc-002.png"),
                new(null, "tc-003", "Skipped", 0, null, null)
            ]);

        Assert.Equal("Failed", run.Status);
        Assert.Equal(3, run.TotalCount);
        Assert.Equal(1, run.PassedCount);
        Assert.Equal(1, run.FailedCount);
        Assert.Equal(1, run.SkippedCount);
        Assert.Equal("TC-001", run.Results.First().TestCaseCode);
    }

    [Fact]
    public void Run_rejects_unknown_target_app()
    {
        Assert.Throws<ArgumentException>(() => new AutomationRun(
            Guid.NewGuid(), null, null, null, "unknown", null, DateTime.UtcNow, []));
    }

    [Fact]
    public void Case_rejects_unknown_result_status()
    {
        Assert.Throws<ArgumentException>(() => new AutomationRun(
            Guid.NewGuid(), null, null, null, "app", null, DateTime.UtcNow,
            [new(null, "TC-001", "Unknown", 0, null, null)]));
    }

    [Fact]
    public void Case_can_attach_managed_evidence_path()
    {
        var run = new AutomationRun(Guid.NewGuid(), null, null, null, "pos", null, DateTime.UtcNow,
            [new(null, "TC-EVIDENCE-001", "Failed", 10, "error", null)]);
        var result = Assert.Single(run.Results);

        result.AttachEvidence("run/case.png");

        Assert.Equal("run/case.png", result.EvidencePath);
        Assert.Throws<ArgumentException>(() => result.AttachEvidence(" "));
    }

    [Fact]
    public async Task Publishing_to_cycle_creates_immutable_execution_and_links_result()
    {
        var options = new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new QaDbContext(options);
        var project = new Project("AUT", "Automation", null, null, null);
        var module = new ProductModule(project.ProjectId, "POS", "POS", null, null, null, null);
        var release = new Release(project.ProjectId, "REL-1", "1.0", null, null, null, null, null);
        var build = new Build(release.ReleaseId, "1", "1.0", null, null, DateTime.UtcNow, null, null, null);
        var environment = new TestEnvironment(project.ProjectId, "Automation Lab", null);
        var testCase = new TestCase(project.ProjectId, module.ModuleId, "TC-AUT-001", "Automated case", null, null, "P0", "Smoke", true, null, [new(1, "Run", null, "Pass")], null);
        var cycle = new TestCycle(project.ProjectId, release.ReleaseId, build.BuildId, environment.TestEnvironmentId, null, "AUT-CYCLE-1", "Automation", "Smoke", null, null, null, null, null);
        var cycleCase = new TestCycleCase(cycle.TestCycleId, testCase.TestCaseId, testCase.RevisionNo, testCase.Priority, 1);
        db.AddRange(project, module, release, build, environment, testCase, cycle, cycleCase);
        await db.SaveChangesAsync();

        var run = new AutomationRun(project.ProjectId, release.ReleaseId, build.BuildId, cycle.TestCycleId, "pos", "runner", DateTime.UtcNow,
            [new(testCase.TestCaseId, testCase.TestCaseCode, "Passed", 250, null, null)]);
        await new AutomationRunRepository(db).PublishAsync(run, Guid.NewGuid(), CancellationToken.None);

        var execution = Assert.Single(await db.TestExecutions.ToListAsync());
        Assert.Equal("Pass", execution.Status);
        Assert.Equal("Pass", (await db.TestCycleCases.SingleAsync()).CurrentStatus);
        Assert.Equal(execution.TestExecutionId, (await db.AutomationRunCases.SingleAsync()).TestExecutionId);
    }

    [Fact]
    public async Task Quality_gate_is_linked_to_matching_project_release_and_build()
    {
        var options = new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new QaDbContext(options);
        var project = new Project("GATE", "Gate", null, null, null);
        var release = new Release(project.ProjectId, "REL-GATE", "1.0", null, null, null, null, null);
        var build = new Build(release.ReleaseId, "100", "1.0", null, null, DateTime.UtcNow, null, null, null);
        db.AddRange(project, release, build);await db.SaveChangesAsync();
        var gate = new AutomationQualityGateRun(project.ProjectId, release.ReleaseId, build.BuildId, "pos", "baseline", "100", false, 1, 0, 0, 0, "new missing AutomationId", "runner", DateTime.UtcNow);

        var saved = await new AutomationRunRepository(db).PublishGateAsync(gate, CancellationToken.None);

        Assert.Equal("Failed", saved.Status);
        Assert.Equal(1, saved.NewMissingCount);
        Assert.Single(await db.AutomationQualityGateRuns.ToListAsync());
    }

    [Fact]
    public void Quality_gate_rejects_negative_finding_count()
    {
        Assert.Throws<ArgumentException>(() => new AutomationQualityGateRun(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "app", "baseline", "current", true, -1, 0, 0, 0, null, null, DateTime.UtcNow));
    }

    [Fact]
    public void Queue_job_enforces_lease_and_state_transitions()
    {
        var job = new AutomationQueueJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "pos", Guid.NewGuid(), "Smoke");
        var lease = job.Claim("runner-01");
        Assert.Throws<InvalidOperationException>(() => job.Update("wrong", "Running", null, null));
        job.Update(lease, "Running", null, null);
        var runId = Guid.NewGuid();job.Update(lease, "Completed", null, runId);
        Assert.Equal("Completed", job.Status);Assert.Equal(runId, job.AutomationRunId);Assert.NotNull(job.StartedAt);Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public async Task Queue_claim_returns_oldest_matching_target_once()
    {
        var options = new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new QaDbContext(options);var projectId=Guid.NewGuid();
        var first=new AutomationQueueJob(projectId,Guid.NewGuid(),Guid.NewGuid(),null,"app",null,null);var second=new AutomationQueueJob(projectId,Guid.NewGuid(),Guid.NewGuid(),null,"pos",null,null);db.AddRange(first,second);await db.SaveChangesAsync();
        var repository=new AutomationRunRepository(db);var claimed=await repository.ClaimQueueJobAsync(projectId,"runner",["pos"],CancellationToken.None);var none=await repository.ClaimQueueJobAsync(projectId,"runner",["pos"],CancellationToken.None);
        Assert.Equal(second.AutomationQueueJobId,claimed?.AutomationQueueJobId);Assert.NotNull(claimed?.LeaseToken);Assert.Null(none);
    }

    [Fact]
    public void Expired_queue_lease_returns_job_for_retry()
    {
        var now=DateTime.UtcNow;var job=new AutomationQueueJob(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),null,"pos",null,null);job.Claim("runner",now.AddMinutes(-3));job.Update(job.LeaseToken!,"Running",null,null);
        Assert.True(job.RecoverExpiredLease(now));Assert.Equal("Queued",job.Status);Assert.Equal(1,job.AttemptCount);Assert.Null(job.LeaseToken);
    }

    [Fact]
    public void Runner_heartbeat_renews_active_lease()
    {
        var now=DateTime.UtcNow;var job=new AutomationQueueJob(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),null,"app",null,null);var lease=job.Claim("runner",now);job.RenewLease("runner",lease,now.AddMinutes(1));
        Assert.Equal(now.AddMinutes(3),job.LeaseExpiresAt);
    }

    [Fact]
    public void Retryable_failure_returns_job_to_queue_but_assertion_is_terminal()
    {
        var retry=new AutomationQueueJob(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),null,"pos",null,null,"Smoke",2);var lease=retry.Claim("runner");retry.Update(lease,"Running",null,null);retry.Update(lease,"Failed","network",null,"Infrastructure");
        Assert.Equal("Queued",retry.Status);Assert.Equal("Infrastructure",retry.ErrorType);
        var assertion=new AutomationQueueJob(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),null,"app",null,null,"Regression",3);lease=assertion.Claim("runner");assertion.Update(lease,"Running",null,null);assertion.Update(lease,"Failed","expected mismatch",null,"Assertion");Assert.Equal("Failed",assertion.Status);
    }

    [Fact]
    public void Weekday_schedule_skips_weekend()
    {
        var friday=new DateTime(2026,8,21,19,0,0,DateTimeKind.Utc);var schedule=new AutomationSchedule(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Nightly","pos","Smoke","Weekdays",new TimeOnly(18,0),3,null,friday);
        Assert.Equal(DayOfWeek.Monday,schedule.NextRunAt.DayOfWeek);Assert.Equal(18,schedule.NextRunAt.Hour);
    }
}
