using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProMaxx2.Automation.Core;

namespace ProMaxx2.Automation.Hub;

public sealed record JobPackage(
    Guid JobId, Guid AutomationExecutionId, Guid AutomationCaseId, string AutomationCode,
    Guid AutomationVersionId, int VersionNo, string DslVersion, string DslJson,
    Guid BuildId, string BuildNumber, Guid EnvironmentId, string EnvironmentName,
    IReadOnlyList<string> Actions, IReadOnlyList<ObjectDescriptor> Objects);

public sealed record AgentInfo(Guid AgentId, string AgentCode, string Status, string Connectivity, IReadOnlyList<string> Capabilities);

public sealed record VerificationObjectItem(Guid VerificationId, string ObjectCode, string ApplicationCode, string ScreenCode, string? ExpectedAutomationId, string ExpectedControlType);
public sealed record VerificationBatchPackage(IReadOnlyList<VerificationObjectItem> Items);

/// <summary>AUT-DATA-001.</summary>
public sealed record SnapshotPackage(Guid AutomationDbSnapshotId, Guid EnvironmentId, string EnvironmentName, Guid BuildId, string BuildNumber);

/// <summary>AUT-DATA-002.</summary>
public sealed record RestorePackage(Guid AutomationDbRestoreId, Guid AutomationDbSnapshotId, string SnapshotPath, string ExpectedChecksum);

/// <summary>AUT-DATA-003.</summary>
public sealed record SeedRunPackage(Guid AutomationDataSeedRunId, string ScriptName, string DbKind, string SqlScript);

public sealed class QaHubClient : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly AgentConfig _config;
    private string? _token;

    public QaHubClient(AgentConfig config) => _config = config;

    public void Dispose() => _http.Dispose();

    public async Task<bool> LoginAsync(CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"{_config.HubBaseUrl}/auth/login", new { username = _config.Username, password = _config.Password }, ct);
        if (!response.IsSuccessStatusCode) return false;
        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        var token = doc.TryGetProperty("accessToken", out var at) ? at.GetString() : doc.TryGetProperty("token", out var tk) ? tk.GetString() : null;
        if (string.IsNullOrWhiteSpace(token)) return false;
        _token = token;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return true;
    }

    public async Task<AgentInfo> RegisterAsync(CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"{_config.HubBaseUrl}/automation/agents/register", new
        {
            agentCode = _config.AgentCode,
            machineName = _config.MachineName,
            agentVersion = _config.AgentVersion,
            operatingSystem = Environment.OSVersion.VersionString,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            capabilities = new[] { "WindowsUI", "Screenshot" }
        }, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AgentInfo>(ct) ?? throw new InvalidOperationException("Register failed.");
        return result;
    }

    public async Task HeartbeatAsync(string status, Guid? currentExecutionId, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"{_config.HubBaseUrl}/automation/agents/heartbeat", new
        {
            agentCode = _config.AgentCode,
            machineName = _config.MachineName,
            agentVersion = _config.AgentVersion,
            status,
            currentExecutionId
        }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JobPackage?> ClaimJobAsync(CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"{_config.HubBaseUrl}/automation/jobs/claim", new
        {
            agentCode = _config.AgentCode,
            agentVersion = _config.AgentVersion,
            capabilities = new[] { "WindowsUI", "Screenshot" },
            targetApp = _config.TargetApp
        }, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobPackage>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
    }

    public async Task ReportStepAsync(Guid executionId, int stepNo, string actionCode, string status, string? actual, string? errorCode, string? errorMessage, DateTime startedAt, DateTime completedAt, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"{_config.HubBaseUrl}/automation/executions/{executionId}/steps/{stepNo}/result", new
        {
            stepNo,
            actionCode,
            status,
            actualResult = actual,
            errorCode,
            errorMessage,
            evidencePath = (string?)null,
            startedAt,
            completedAt
        }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UploadEvidenceAsync(Guid executionId, int stepNo, byte[] screenshot, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        using var stream = new MemoryStream(screenshot);
        var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", $"step{stepNo}.png");
        var response = await _http.PostAsync($"{_config.HubBaseUrl}/automation/executions/{executionId}/steps/{stepNo}/evidence", content, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UploadGenericEvidenceAsync(Guid executionId, int? stepNo, string evidenceType, string fileName, byte[] data, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        using var stream = new MemoryStream(data);
        var ext = Path.GetExtension(fileName);
        var mime = ext switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".csv" => "text/csv", _ => "application/octet-stream" };
        var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue(mime);
        content.Add(file, "file", fileName);
        if (stepNo.HasValue) content.Add(new StringContent(stepNo.Value.ToString()), "stepNo");
        content.Add(new StringContent(evidenceType), "evidenceType");
        var response = await _http.PostAsync($"{_config.HubBaseUrl}/automation/executions/{executionId}/evidence/upload", content, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task CompleteAsync(Guid executionId, string status, string? failureType, string? errorCode, string? errorMessage, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"{_config.HubBaseUrl}/automation/executions/{executionId}/complete", new
        {
            status,
            failureType,
            errorCode,
            errorMessage
        }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<VerificationBatchPackage?> ClaimVerificationBatchAsync(CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"{_config.HubBaseUrl}/automation/verifications/claim", new { agentCode = _config.AgentCode }, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<VerificationBatchPackage>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
    }

    public async Task ReportVerificationResultAsync(Guid verificationId, string status, string? actualAutomationId, string? actualControlType, string? message, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"{_config.HubBaseUrl}/automation/verifications/result", new
        {
            verificationId,
            status,
            actualAutomationId,
            actualControlType,
            message
        }, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>AUT-DATA-001.</summary>
    public async Task<SnapshotPackage?> ClaimSnapshotAsync(CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"{_config.HubBaseUrl}/automation/snapshots/claim", new { agentCode = _config.AgentCode, agentVersion = _config.AgentVersion }, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SnapshotPackage>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
    }

    public async Task CompleteSnapshotAsync(Guid snapshotId, string status, string? dbKind, string? snapshotPath, string? checksum, long? sizeBytes, string? errorMessage, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"{_config.HubBaseUrl}/automation/snapshots/{snapshotId}/complete", new
        {
            status,
            dbKind,
            snapshotPath,
            checksum,
            sizeBytes,
            errorMessage
        }, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>AUT-DATA-002.</summary>
    public async Task<RestorePackage?> ClaimRestoreAsync(CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"{_config.HubBaseUrl}/automation/restores/claim", new { agentCode = _config.AgentCode, agentVersion = _config.AgentVersion }, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RestorePackage>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
    }

    public async Task CompleteRestoreAsync(Guid restoreId, string status, bool checksumVerified, bool availabilityVerified, string? errorMessage, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"{_config.HubBaseUrl}/automation/restores/{restoreId}/complete", new
        {
            status,
            checksumVerified,
            availabilityVerified,
            errorMessage
        }, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>AUT-DATA-003.</summary>
    public async Task<SeedRunPackage?> ClaimSeedRunAsync(CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"{_config.HubBaseUrl}/automation/seed-runs/claim", new { agentCode = _config.AgentCode, agentVersion = _config.AgentVersion }, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SeedRunPackage>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
    }

    public async Task CompleteSeedRunAsync(Guid seedRunId, string status, int? rowsAffected, string? errorMessage, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"{_config.HubBaseUrl}/automation/seed-runs/{seedRunId}/complete", new
        {
            status,
            rowsAffected,
            errorMessage
        }, ct);
        response.EnsureSuccessStatusCode();
    }
}