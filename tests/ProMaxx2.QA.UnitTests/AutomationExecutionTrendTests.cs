namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-P2-003 (Pass/Fail/Flaky trend): <c>AutomationAgentService.GetExecutionTrendAsync</c> buckets
/// Passed/Failed executions by day (default)/Build/Release, plus a "Flaky" count per bucket that reuses the same
/// status-transition concept as <see cref="AutomationCaseService.GetFlakyCandidatesAsync"/> — a case's status
/// differing from its immediately preceding execution — attributed to the bucket of the later execution.</summary>
public sealed class AutomationExecutionTrendTests
{
    [Fact]
    public async Task Trend_by_build_buckets_Pass_and_Fail_counts_correctly_per_build()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new ProMaxx2.QA.Application.Automation.RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        var buildB = new ProMaxx2.QA.Domain.Releases.Build(baseline.Release.ReleaseId, "2", "1.0", null, null, DateTime.UtcNow, null, null, null);
        db.Add(buildB);
        await db.SaveChangesAsync();

        // Build 1: one Passed run.
        var exec1 = await agents.RequestExecutionAsync(baseline.Project.ProjectId, new ProMaxx2.QA.Application.Automation.RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim1 = await agents.ClaimNextJobAsync(new ProMaxx2.QA.Application.Automation.ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);
        await agents.CompleteExecutionAsync(claim1!.AutomationExecutionId, new ProMaxx2.QA.Application.Automation.CompleteExecutionRequest("Passed", null, null, null), CancellationToken.None);

        // Build 2: one Failed run.
        var exec2 = await agents.RequestExecutionAsync(baseline.Project.ProjectId, new ProMaxx2.QA.Application.Automation.RequestExecutionRequest(readyCase.AutomationCaseId, versionId, buildB.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim2 = await agents.ClaimNextJobAsync(new ProMaxx2.QA.Application.Automation.ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);
        await agents.CompleteExecutionAsync(claim2!.AutomationExecutionId, new ProMaxx2.QA.Application.Automation.CompleteExecutionRequest("Failed", "AutomationFailure", "AUT-UI-001", "boom"), CancellationToken.None);

        var trend = await agents.GetExecutionTrendAsync(baseline.Project.ProjectId, "build", null, null, null, CancellationToken.None);

        Assert.Equal("build", trend.GroupBy);
        Assert.Equal(2, trend.Buckets.Count);
        var bucket1 = Assert.Single(trend.Buckets, b => b.BucketLabel == "1");
        var bucket2 = Assert.Single(trend.Buckets, b => b.BucketLabel == "2");
        Assert.Equal(1, bucket1.Passed);
        Assert.Equal(0, bucket1.Failed);
        Assert.Equal(0, bucket2.Passed);
        Assert.Equal(1, bucket2.Failed);
    }

    [Fact]
    public async Task Trend_by_release_filters_to_the_requested_release_only()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new ProMaxx2.QA.Application.Automation.RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new ProMaxx2.QA.Application.Automation.RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ProMaxx2.QA.Application.Automation.ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);
        await agents.CompleteExecutionAsync(claim!.AutomationExecutionId, new ProMaxx2.QA.Application.Automation.CompleteExecutionRequest("Passed", null, null, null), CancellationToken.None);

        var matching = await agents.GetExecutionTrendAsync(baseline.Project.ProjectId, "release", null, null, baseline.Release.ReleaseId, CancellationToken.None);
        var notMatching = await agents.GetExecutionTrendAsync(baseline.Project.ProjectId, "release", null, null, Guid.NewGuid(), CancellationToken.None);

        Assert.Single(matching.Buckets);
        Assert.Equal(baseline.Release.ReleaseCode, matching.Buckets[0].BucketLabel);
        Assert.Empty(notMatching.Buckets);
    }

    [Fact]
    public async Task Trend_counts_a_flip_between_consecutive_runs_of_the_same_case_as_Flaky_in_the_later_buckets_bucket()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new ProMaxx2.QA.Application.Automation.RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);

        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new ProMaxx2.QA.Application.Automation.RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim1 = await agents.ClaimNextJobAsync(new ProMaxx2.QA.Application.Automation.ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);
        await agents.CompleteExecutionAsync(claim1!.AutomationExecutionId, new ProMaxx2.QA.Application.Automation.CompleteExecutionRequest("Passed", null, null, null), CancellationToken.None);

        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new ProMaxx2.QA.Application.Automation.RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim2 = await agents.ClaimNextJobAsync(new ProMaxx2.QA.Application.Automation.ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);
        await agents.CompleteExecutionAsync(claim2!.AutomationExecutionId, new ProMaxx2.QA.Application.Automation.CompleteExecutionRequest("Failed", "AutomationFailure", "AUT-UI-001", "boom"), CancellationToken.None);

        var trend = await agents.GetExecutionTrendAsync(baseline.Project.ProjectId, "day", null, null, null, CancellationToken.None);

        var bucket = Assert.Single(trend.Buckets);
        Assert.Equal(1, bucket.Passed);
        Assert.Equal(1, bucket.Failed);
        Assert.Equal(1, bucket.Flaky);
        Assert.Equal(2, bucket.Total);
    }

    [Fact]
    public async Task Trend_defaults_to_day_grouping_for_an_unrecognized_groupBy_value()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new ProMaxx2.QA.Application.Automation.RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new ProMaxx2.QA.Application.Automation.RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ProMaxx2.QA.Application.Automation.ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);
        await agents.CompleteExecutionAsync(claim!.AutomationExecutionId, new ProMaxx2.QA.Application.Automation.CompleteExecutionRequest("Passed", null, null, null), CancellationToken.None);

        var trend = await agents.GetExecutionTrendAsync(baseline.Project.ProjectId, "nonsense", null, null, null, CancellationToken.None);

        Assert.Equal("day", trend.GroupBy);
    }

    [Fact]
    public async Task Trend_respects_an_explicit_from_to_range()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var agents = AutomationTestFixtures.AgentService(db);
        await agents.RegisterAsync(new ProMaxx2.QA.Application.Automation.RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(baseline.Project.ProjectId, new ProMaxx2.QA.Application.Automation.RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ProMaxx2.QA.Application.Automation.ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None);
        await agents.CompleteExecutionAsync(claim!.AutomationExecutionId, new ProMaxx2.QA.Application.Automation.CompleteExecutionRequest("Passed", null, null, null), CancellationToken.None);

        var future = await agents.GetExecutionTrendAsync(baseline.Project.ProjectId, "day", DateTime.UtcNow.AddDays(1), null, null, CancellationToken.None);
        var wideRange = await agents.GetExecutionTrendAsync(baseline.Project.ProjectId, "day", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), null, CancellationToken.None);

        Assert.Empty(future.Buckets);
        Assert.Single(wideRange.Buckets);
    }
}
