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
}