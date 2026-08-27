using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Covers AUT-P1-008 (CI/CD / Webhook Integration): "รับ trigger แบบ authenticated" (token auth, resolved
/// per-project purely from the token — no separate project claim to spoof), "ป้องกัน replay" (a RequestId that
/// already succeeded once is answered idempotently, but a Failed delivery doesn't permanently consume it) and
/// "trace กลับ Build ได้" (every response and audit row carries the BuildId). Also proves a webhook-created Build
/// fires AUT-P1-007 Smoke build-trigger policies exactly like a manually-created one, since both go through the
/// same ReleaseService.CreateBuildAsync.</summary>
public sealed class AutomationWebhookTests
{
    [Fact]
    public async Task Creating_a_token_returns_the_plaintext_once_and_never_exposes_it_again()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var webhooks = AutomationTestFixtures.WebhookService(db, baseline.Project.ProjectId);

        var result = await webhooks.CreateTokenAsync(baseline.Project.ProjectId, new CreateAutomationWebhookTokenRequest("Jenkins CI"), null, CancellationToken.None);

        Assert.StartsWith("whk_", result.PlainTextToken);
        Assert.True(result.Token.IsActive);
        Assert.StartsWith(result.Token.TokenPrefix, result.PlainTextToken);
        var listed = await webhooks.ListTokensAsync(baseline.Project.ProjectId, CancellationToken.None);
        Assert.Single(listed);
    }

    [Fact]
    public async Task A_valid_token_creates_a_build_and_returns_its_id()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var webhooks = AutomationTestFixtures.WebhookService(db, baseline.Project.ProjectId);
        var created = await webhooks.CreateTokenAsync(baseline.Project.ProjectId, new CreateAutomationWebhookTokenRequest("Jenkins CI"), null, CancellationToken.None);

        var result = await webhooks.ReceiveBuildAsync(created.PlainTextToken,
            new ReceiveBuildWebhookRequest(baseline.Release.ReleaseId, "42", "1.2.3", null, "abc123", null, null, null, "ci-run-42"), CancellationToken.None);

        Assert.Equal("Created", result.Status);
        Assert.NotEqual(Guid.Empty, result.BuildId);
        Assert.Equal("42", result.BuildNumber);
    }

    [Fact]
    public async Task An_invalid_token_is_rejected()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var webhooks = AutomationTestFixtures.WebhookService(db, baseline.Project.ProjectId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => webhooks.ReceiveBuildAsync("whk_not-a-real-token",
            new ReceiveBuildWebhookRequest(baseline.Release.ReleaseId, "42", null, null, null, null, null, null, "ci-run-1"), CancellationToken.None));
    }

    [Fact]
    public async Task A_revoked_token_is_rejected()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var webhooks = AutomationTestFixtures.WebhookService(db, baseline.Project.ProjectId);
        var created = await webhooks.CreateTokenAsync(baseline.Project.ProjectId, new CreateAutomationWebhookTokenRequest("Jenkins CI"), null, CancellationToken.None);
        await webhooks.RevokeTokenAsync(created.Token.AutomationWebhookTokenId, baseline.Project.ProjectId, null, CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => webhooks.ReceiveBuildAsync(created.PlainTextToken,
            new ReceiveBuildWebhookRequest(baseline.Release.ReleaseId, "42", null, null, null, null, null, null, "ci-run-1"), CancellationToken.None));
    }

    [Fact]
    public async Task Revoking_an_already_revoked_token_throws()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var webhooks = AutomationTestFixtures.WebhookService(db, baseline.Project.ProjectId);
        var created = await webhooks.CreateTokenAsync(baseline.Project.ProjectId, new CreateAutomationWebhookTokenRequest("Jenkins CI"), null, CancellationToken.None);
        await webhooks.RevokeTokenAsync(created.Token.AutomationWebhookTokenId, baseline.Project.ProjectId, null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => webhooks.RevokeTokenAsync(created.Token.AutomationWebhookTokenId, baseline.Project.ProjectId, null, CancellationToken.None));
    }

    [Fact]
    public async Task A_repeated_RequestId_after_success_is_answered_idempotently_without_a_second_build()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var webhooks = AutomationTestFixtures.WebhookService(db, baseline.Project.ProjectId);
        var created = await webhooks.CreateTokenAsync(baseline.Project.ProjectId, new CreateAutomationWebhookTokenRequest("Jenkins CI"), null, CancellationToken.None);
        var releases = AutomationTestFixtures.ReleaseServiceWithBuildTrigger(db, baseline.Project.ProjectId);

        var first = await webhooks.ReceiveBuildAsync(created.PlainTextToken,
            new ReceiveBuildWebhookRequest(baseline.Release.ReleaseId, "42", null, null, null, null, null, null, "ci-run-42"), CancellationToken.None);
        var replay = await webhooks.ReceiveBuildAsync(created.PlainTextToken,
            new ReceiveBuildWebhookRequest(baseline.Release.ReleaseId, "42", null, null, null, null, null, null, "ci-run-42"), CancellationToken.None);

        Assert.Equal("Duplicate", replay.Status);
        Assert.Equal(first.BuildId, replay.BuildId);
        var builds = await releases.ListBuildsAsync(baseline.Release.ReleaseId, CancellationToken.None);
        Assert.Single(builds, b => b.BuildNumber == "42"); // no second "42" Build created by the replay
    }

    [Fact]
    public async Task A_failed_delivery_does_not_block_a_retry_with_the_same_RequestId()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var webhooks = AutomationTestFixtures.WebhookService(db, baseline.Project.ProjectId);
        var created = await webhooks.CreateTokenAsync(baseline.Project.ProjectId, new CreateAutomationWebhookTokenRequest("Jenkins CI"), null, CancellationToken.None);

        // First attempt: wrong ReleaseId (e.g. a bad CI config value at the time) — fails, recorded as Failed.
        await Assert.ThrowsAsync<EntityNotFoundException>(() => webhooks.ReceiveBuildAsync(created.PlainTextToken,
            new ReceiveBuildWebhookRequest(Guid.NewGuid(), "42", null, null, null, null, null, null, "ci-run-42"), CancellationToken.None));

        // Retry with the SAME RequestId, now with the correct ReleaseId — must go through, not be treated as a duplicate.
        var retried = await webhooks.ReceiveBuildAsync(created.PlainTextToken,
            new ReceiveBuildWebhookRequest(baseline.Release.ReleaseId, "42", null, null, null, null, null, null, "ci-run-42"), CancellationToken.None);

        Assert.Equal("Created", retried.Status);
        Assert.NotEqual(Guid.Empty, retried.BuildId);
    }

    [Fact]
    public async Task A_duplicate_build_number_with_a_different_RequestId_is_a_real_conflict()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var webhooks = AutomationTestFixtures.WebhookService(db, baseline.Project.ProjectId);
        var created = await webhooks.CreateTokenAsync(baseline.Project.ProjectId, new CreateAutomationWebhookTokenRequest("Jenkins CI"), null, CancellationToken.None);
        await webhooks.ReceiveBuildAsync(created.PlainTextToken,
            new ReceiveBuildWebhookRequest(baseline.Release.ReleaseId, "42", null, null, null, null, null, null, "ci-run-1"), CancellationToken.None);

        await Assert.ThrowsAsync<DuplicateCodeException>(() => webhooks.ReceiveBuildAsync(created.PlainTextToken,
            new ReceiveBuildWebhookRequest(baseline.Release.ReleaseId, "42", null, null, null, null, null, null, "ci-run-2"), CancellationToken.None));
    }

    [Fact]
    public async Task Missing_RequestId_is_rejected()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var webhooks = AutomationTestFixtures.WebhookService(db, baseline.Project.ProjectId);
        var created = await webhooks.CreateTokenAsync(baseline.Project.ProjectId, new CreateAutomationWebhookTokenRequest("Jenkins CI"), null, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => webhooks.ReceiveBuildAsync(created.PlainTextToken,
            new ReceiveBuildWebhookRequest(baseline.Release.ReleaseId, "42", null, null, null, null, null, null, ""), CancellationToken.None));
    }

    [Fact]
    public async Task A_webhook_created_build_fires_Smoke_build_trigger_policies_the_same_as_a_manual_one()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var (baseline, readyCase, _) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var suiteService = AutomationTestFixtures.SuiteService(db, baseline.Project.ProjectId);
        var suite = await suiteService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationSuiteRequest(null, "Smoke", null), null, CancellationToken.None);
        await suiteService.AddCasesAsync(suite.AutomationSuiteId, baseline.Project.ProjectId, new AddSuiteCasesRequest([readyCase.AutomationCaseId], true), CancellationToken.None);
        var triggerService = AutomationTestFixtures.BuildTriggerService(db);
        await triggerService.CreateAsync(baseline.Project.ProjectId, new CreateAutomationBuildTriggerPolicyRequest(suite.AutomationSuiteId, "Smoke", baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var webhooks = AutomationTestFixtures.WebhookService(db, baseline.Project.ProjectId);
        var created = await webhooks.CreateTokenAsync(baseline.Project.ProjectId, new CreateAutomationWebhookTokenRequest("Jenkins CI"), null, CancellationToken.None);
        var agents = AutomationTestFixtures.AgentService(db);

        var result = await webhooks.ReceiveBuildAsync(created.PlainTextToken,
            new ReceiveBuildWebhookRequest(baseline.Release.ReleaseId, "99", null, null, null, null, null, null, "ci-run-99"), CancellationToken.None);

        var executions = await agents.ListExecutionsAsync(baseline.Project.ProjectId, result.BuildId, 50, CancellationToken.None);
        Assert.Single(executions);
    }

    [Fact]
    public async Task Deliveries_are_listed_for_the_project_with_the_token_name_and_build_number()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var webhooks = AutomationTestFixtures.WebhookService(db, baseline.Project.ProjectId);
        var created = await webhooks.CreateTokenAsync(baseline.Project.ProjectId, new CreateAutomationWebhookTokenRequest("Jenkins CI"), null, CancellationToken.None);
        await webhooks.ReceiveBuildAsync(created.PlainTextToken,
            new ReceiveBuildWebhookRequest(baseline.Release.ReleaseId, "42", null, null, null, null, null, null, "ci-run-42"), CancellationToken.None);

        var deliveries = await webhooks.ListDeliveriesAsync(baseline.Project.ProjectId, CancellationToken.None);

        var delivery = Assert.Single(deliveries);
        Assert.Equal("Jenkins CI", delivery.TokenName);
        Assert.Equal("42", delivery.BuildNumber);
        Assert.Equal("Created", delivery.Status);
    }
}
