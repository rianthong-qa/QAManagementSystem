using System.Net.Http.Json;
using System.Text.Json;
using Promaxx2.Automation.Core;

namespace Promaxx2.Automation.Hub;

/// <summary>
/// HTTP client ของ QA Hub API — login (JWT) + export Test Cases
/// </summary>
public sealed class QaHubClient : IDisposable
{
    private const int PageSize = 200;

    private readonly HttpClient _http;

    public AuthenticatedUser? User { get; private set; }

    public QaHubClient(AppConfig config)
    {
        _http = new HttpClient { BaseAddress = new Uri(config.QaHubBaseUrl) };
    }

    public async Task LoginAsync(string username, string password, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync("auth/login", new LoginRequest(username, password), ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>(Json.ApiOptions, ct)
                   ?? throw new InvalidOperationException("Empty login response from QA Hub.");
        User = body.User;
        _http.DefaultRequestHeaders.Authorization = new("Bearer", body.AccessToken);
    }

    public async Task<IReadOnlyList<ProjectDto>> ListProjectsAsync(CancellationToken ct = default)
        => await GetAsync<IReadOnlyList<ProjectDto>>("projects", ct);

    public async Task<IReadOnlyList<ModuleDto>> ListModulesAsync(Guid projectId, CancellationToken ct = default)
        => await GetAsync<IReadOnlyList<ModuleDto>>($"projects/{projectId}/modules", ct);

    /// <summary>
    /// Export test cases พร้อม steps ครบทุกหน้า (server-paginated) + module name map
    /// </summary>
    public async Task<TestPlanSource> ExportCasesAsync(
        Guid projectId,
        string? status = null,
        string? priority = null,
        bool automationOnly = false,
        CancellationToken ct = default)
    {
        var modules = (await ListModulesAsync(projectId, ct))
            .ToDictionary(m => m.ModuleId, m => $"{m.ModuleCode} · {m.ModuleName}");

        var cases = new List<TestCaseDto>();
        int page = 1;
        while (true)
        {
            var url = $"test-cases?projectId={projectId}&page={page}&size={PageSize}";
            if (!string.IsNullOrWhiteSpace(status)) url += $"&status={Uri.EscapeDataString(status)}";
            if (!string.IsNullOrWhiteSpace(priority)) url += $"&priority={Uri.EscapeDataString(priority)}";
            if (automationOnly) url += "&automation=true";

            var result = await GetAsync<PagedResult<TestCaseListDto>>(url, ct);
            foreach (var row in result.Rows)
                cases.Add(await GetAsync<TestCaseDto>($"test-cases/{row.TestCaseId}", ct));

            if (result.Rows.Count < PageSize || cases.Count >= result.Total) break;
            page++;
        }

        return new TestPlanSource(cases, modules);
    }

    /// <summary>รับทั้ง GUID และ ProjectCode — resolve code → id ผ่าน /projects</summary>
    public async Task<ProjectDto> ResolveProjectAsync(string idOrCode, CancellationToken ct = default)
    {
        if (Guid.TryParse(idOrCode, out var id))
        {
            var byId = (await ListProjectsAsync(ct)).FirstOrDefault(p => p.ProjectId == id);
            return byId ?? throw new InvalidOperationException($"Project id {id} not found.");
        }

        var matches = (await ListProjectsAsync(ct))
            .Where(p => p.ProjectCode.Equals(idOrCode, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Project code \"{idOrCode}\" not found."),
            _ => throw new InvalidOperationException($"Project code \"{idOrCode}\" is ambiguous.")
        };
    }

    public async Task<PublishedAutomationRun> PublishAutomationRunAsync(
        PublishAutomationRunRequest request,
        CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync(
            $"automation/runs?projectId={request.ProjectId}", request, Json.ApiOptions, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"QA Hub rejected automation results ({(int)resp.StatusCode}): {detail}",
                null, resp.StatusCode);
        }

        return await resp.Content.ReadFromJsonAsync<PublishedAutomationRun>(Json.ApiOptions, ct)
               ?? throw new InvalidOperationException("Empty automation publish response from QA Hub.");
    }

    public async Task UploadAutomationEvidenceAsync(Guid projectId,Guid runId,Guid caseId,string filePath,CancellationToken ct=default)
    {
        await using var stream=File.OpenRead(filePath);using var content=new MultipartFormDataContent();using var file=new StreamContent(stream);content.Add(file,"file",Path.GetFileName(filePath));
        using var resp=await _http.PostAsync($"automation/runs/{runId}/cases/{caseId}/evidence?projectId={projectId}",content,ct);
        if(!resp.IsSuccessStatusCode)throw new HttpRequestException($"QA Hub rejected evidence upload ({(int)resp.StatusCode}): {await resp.Content.ReadAsStringAsync(ct)}",null,resp.StatusCode);
    }

    public async Task<AutomationQueueJob?>ClaimQueueJobAsync(Guid projectId,string runnerName,IReadOnlyList<string>targets,CancellationToken ct=default)
    {
        using var resp=await _http.PostAsJsonAsync($"automation/queue/claim?projectId={projectId}",new ClaimAutomationQueueJobRequest(runnerName,targets),Json.ApiOptions,ct);if(resp.StatusCode==System.Net.HttpStatusCode.NoContent)return null;resp.EnsureSuccessStatusCode();return await resp.Content.ReadFromJsonAsync<AutomationQueueJob>(Json.ApiOptions,ct);
    }

    public async Task UpdateQueueJobAsync(AutomationQueueJob job,string status,string?errorMessage=null,Guid?automationRunId=null,string?errorType=null,CancellationToken ct=default)
    {
        using var resp=await _http.PostAsJsonAsync($"automation/queue/{job.AutomationQueueJobId}/status?projectId={job.ProjectId}",new UpdateAutomationQueueJobRequest(job.LeaseToken??throw new InvalidOperationException("Queue job has no lease token."),status,errorMessage,automationRunId,errorType),Json.ApiOptions,ct);resp.EnsureSuccessStatusCode();
    }

    public async Task HeartbeatRunnerAsync(Guid projectId,string runnerName,string version,IReadOnlyList<string>targets,AutomationQueueJob?job=null,CancellationToken ct=default)
    {
        var request=new AutomationRunnerHeartbeatRequest(runnerName,Environment.MachineName,version,targets,job is null?"Idle":"Busy",job?.AutomationQueueJobId,job?.LeaseToken);
        using var resp=await _http.PostAsJsonAsync($"automation/agents/heartbeat?projectId={projectId}",request,Json.ApiOptions,ct);resp.EnsureSuccessStatusCode();
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(relativeUrl, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<T>(Json.ApiOptions, ct)
               ?? throw new InvalidOperationException($"Empty response from GET {relativeUrl}.");
    }

    public async Task<IReadOnlyList<CycleCaseMap>> GetCycleCaseMapAsync(Guid cycleId, CancellationToken ct = default)
    {
        var ws = await GetAsync<CycleWorkspaceDto>($"test-cycles/{cycleId}/execution", ct);
        return ws.Cases;
    }

    public async Task WriteBackExecutionAsync(Guid cycleCaseId, string status, string? actualResult, string? comment, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync(
            $"test-cycle-cases/{cycleCaseId}/executions",
            new WriteBackExecutionRequest(status, actualResult, comment, Array.Empty<object>()),
            Json.ApiOptions, ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"QA Hub rejected write-back execution ({(int)resp.StatusCode}): {await resp.Content.ReadAsStringAsync(ct)}", null, resp.StatusCode);
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>ผลลัพธ์ export — cases + mapping moduleId → "CODE · Name"</summary>
public sealed record TestPlanSource(IReadOnlyList<TestCaseDto> Cases, IReadOnlyDictionary<Guid, string> Modules);

/// <summary>Workspace ของ Test Cycle ใช้แมป testCaseId → testCycleCaseId สำหรับ write-back</summary>
public sealed record CycleWorkspaceDto(IReadOnlyList<CycleCaseMap> Cases);
public sealed record CycleCaseMap(Guid TestCycleCaseId, Guid TestCaseId);
public sealed record WriteBackExecutionRequest(string Status, string? ActualResult, string? Comment, IReadOnlyList<object> StepResults);
