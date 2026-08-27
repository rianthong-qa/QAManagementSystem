using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.Releases;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.Application.Automation;

public sealed record AutomationWebhookTokenDto(Guid AutomationWebhookTokenId, Guid ProjectId, string Name, string TokenPrefix, bool IsActive, DateTime? LastUsedAtUtc, Guid? CreatedBy, DateTime CreatedAt, DateTime? RevokedAt);
public sealed record CreateAutomationWebhookTokenResult(AutomationWebhookTokenDto Token, string PlainTextToken);
public sealed record CreateAutomationWebhookTokenRequest(string Name);

public sealed record AutomationWebhookDeliveryDto(Guid AutomationWebhookDeliveryId, Guid ProjectId, Guid AutomationWebhookTokenId, string TokenName, string RequestId, DateTime ReceivedAtUtc, Guid? BuildId, string? BuildNumber, string Status, string? ErrorMessage);

/// <summary>Body of the public build webhook — <see cref="RequestId"/> is a caller-generated idempotency key (e.g.
/// the CI system's own run ID) required on every call so a redelivered webhook is answered idempotently instead of
/// creating a duplicate Build (AUT-P1-008 "ป้องกัน replay").</summary>
public sealed record ReceiveBuildWebhookRequest(Guid ReleaseId, string BuildNumber, string? ApplicationVersion, string? PackageVersion, string? CommitReference, DateTime? BuildDate, string? ChangeNotes, string? KnownIssues, string RequestId);
public sealed record ReceiveBuildWebhookResult(Guid BuildId, string BuildNumber, Guid ReleaseId, string Status);

public interface IAutomationWebhookRepository
{
    Task<IReadOnlyList<AutomationWebhookTokenDto>> ListTokensAsync(Guid projectId, CancellationToken ct);
    Task<AutomationWebhookToken?> FindTokenAsync(Guid id, Guid projectId, CancellationToken ct);
    /// <summary>Looks up an active token purely by its hash — project-agnostic, because the caller (a webhook
    /// request) doesn't separately assert a project; the token itself IS the project scope.</summary>
    Task<AutomationWebhookToken?> FindActiveTokenByHashAsync(string tokenHash, CancellationToken ct);
    Task AddTokenAsync(AutomationWebhookToken entity, CancellationToken ct);

    Task<IReadOnlyList<AutomationWebhookDeliveryDto>> ListDeliveriesAsync(Guid projectId, CancellationToken ct);
    /// <summary>Only ever matches a prior successful ("Created") delivery — a prior "Failed" attempt with the same
    /// RequestId must NOT block a legitimate retry (e.g. the release didn't exist yet, now it does), so failed
    /// deliveries are recorded for audit but never satisfy this lookup.</summary>
    Task<AutomationWebhookDelivery?> FindSuccessfulDeliveryAsync(Guid projectId, string requestId, CancellationToken ct);
    Task AddDeliveryAsync(AutomationWebhookDelivery entity, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>AUT-P1-008: CI/CD → QA Hub build webhook. Token management (<see cref="CreateTokenAsync"/>/
/// <see cref="ListTokensAsync"/>/<see cref="RevokeTokenAsync"/>) is normal JWT-authenticated Automation-module admin
/// work; <see cref="ReceiveBuildAsync"/> is the public endpoint's handler and authenticates the caller itself by
/// hashing the supplied secret and looking it up — it is deliberately NOT behind the app's JWT `[Authorize]`
/// pipeline, since a CI system has no user session.</summary>
public sealed class AutomationWebhookService(IAutomationWebhookRepository repository, ReleaseService releases)
{
    public Task<IReadOnlyList<AutomationWebhookTokenDto>> ListTokensAsync(Guid projectId, CancellationToken ct) => repository.ListTokensAsync(projectId, ct);
    public Task<IReadOnlyList<AutomationWebhookDeliveryDto>> ListDeliveriesAsync(Guid projectId, CancellationToken ct) => repository.ListDeliveriesAsync(projectId, ct);

    public async Task<CreateAutomationWebhookTokenResult> CreateTokenAsync(Guid projectId, CreateAutomationWebhookTokenRequest r, Guid? userId, CancellationToken ct)
    {
        var (entity, plainTextToken) = AutomationWebhookToken.Generate(projectId, r.Name, userId);
        await repository.AddTokenAsync(entity, ct);
        await repository.SaveChangesAsync(ct);
        var dto = (await repository.ListTokensAsync(projectId, ct)).Single(x => x.AutomationWebhookTokenId == entity.AutomationWebhookTokenId);
        return new CreateAutomationWebhookTokenResult(dto, plainTextToken);
    }

    public async Task RevokeTokenAsync(Guid id, Guid projectId, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindTokenAsync(id, projectId, ct) ?? throw new EntityNotFoundException("Webhook token not found.");
        entity.Revoke(userId);
        await repository.SaveChangesAsync(ct);
    }

    /// <summary>Authenticates <paramref name="plainTextToken"/>, replays back the original result if
    /// <see cref="ReceiveBuildWebhookRequest.RequestId"/> was already seen for that token's project (idempotent
    /// replay protection), otherwise creates the Build through the exact same <c>ReleaseService.CreateBuildAsync</c>
    /// a QA engineer's manual "create build" in the UI goes through — so AUT-P1-007's Smoke build-trigger policies
    /// fire for webhook-created builds exactly as they do for manually-created ones, with no separate code path.</summary>
    public async Task<ReceiveBuildWebhookResult> ReceiveBuildAsync(string plainTextToken, ReceiveBuildWebhookRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.RequestId)) throw new ArgumentException("RequestId is required (used for replay protection).");
        var token = await repository.FindActiveTokenByHashAsync(AutomationWebhookToken.Hash(plainTextToken), ct) ?? throw new UnauthorizedAccessException("Invalid or revoked webhook token.");

        var existing = await repository.FindSuccessfulDeliveryAsync(token.ProjectId, r.RequestId, ct);
        if (existing is not null)
            return new ReceiveBuildWebhookResult(existing.BuildId ?? Guid.Empty, r.BuildNumber, r.ReleaseId, "Duplicate");

        token.RecordUse(DateTime.UtcNow);
        try
        {
            var build = await releases.CreateBuildAsync(r.ReleaseId,
                new CreateBuildRequest(r.BuildNumber, r.ApplicationVersion, r.PackageVersion, r.CommitReference, r.BuildDate, r.ChangeNotes, r.KnownIssues), null, ct);
            await repository.AddDeliveryAsync(new AutomationWebhookDelivery(token.ProjectId, token.AutomationWebhookTokenId, r.RequestId, DateTime.UtcNow, build.BuildId, "Created", null), ct);
            await repository.SaveChangesAsync(ct);
            return new ReceiveBuildWebhookResult(build.BuildId, build.BuildNumber, build.ReleaseId, "Created");
        }
        catch (Exception ex) when (ex is EntityNotFoundException or DuplicateCodeException or ArgumentException)
        {
            await repository.AddDeliveryAsync(new AutomationWebhookDelivery(token.ProjectId, token.AutomationWebhookTokenId, r.RequestId, DateTime.UtcNow, null, "Failed", ex.Message), ct);
            await repository.SaveChangesAsync(ct);
            throw;
        }
    }
}
