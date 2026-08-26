using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Settings;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed record AiRuntimeConfiguration(string Provider, string ApiKey, string Model, string? BaseUrl);
public sealed class AiNotConfiguredException(string message) : InvalidOperationException(message);

public sealed class SharedAiConfigurationService(QaDbContext db, IDataProtectionProvider protectionProvider, IConfiguration fallbackConfiguration, IHttpClientFactory clients)
{
    private static readonly string[] Providers = ["OpenAI", "Google", "Anthropic", "OpenRouter", "Local", "opencode"];
    private const string OpenRouterBaseUrl = "https://openrouter.ai/api/v1";
    private readonly IDataProtector protector = protectionProvider.CreateProtector("ProMaxx2.QA.AiConfiguration.ApiKey.v1");
    private string currentModel = "gpt-5-mini";
    public bool IsConfigured { get; private set; } = true;
    public string? this[string key] => key == "OpenAI:Model" ? currentModel : null;

    public async Task<AiRuntimeConfiguration> GetRuntimeAsync(CancellationToken ct)
    {
        var setting = await db.AiConfigurations.AsNoTracking().OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
        if (setting is not null)
        {
            if (!setting.IsEnabled) { IsConfigured = false; throw new AiNotConfiguredException("AI ถูกปิดใช้งานใน Setting Center"); }
            if (setting.Provider == "Local" && string.IsNullOrWhiteSpace(setting.BaseUrl)) { IsConfigured = false; throw new AiNotConfiguredException("กรุณาระบุ Base URL สำหรับ AI Local"); }
            if (setting.Provider == "opencode" && string.IsNullOrWhiteSpace(setting.BaseUrl)) { IsConfigured = false; throw new AiNotConfiguredException("กรุณาระบุ Base URL สำหรับ opencode"); }
            if (setting.Provider == "OpenRouter" && string.IsNullOrWhiteSpace(setting.BaseUrl)) { IsConfigured = false; throw new AiNotConfiguredException("กรุณาระบุ Base URL สำหรับ OpenRouter"); }
            var key = string.Empty;
            if (!string.IsNullOrWhiteSpace(setting.EncryptedApiKey))
            {
                try { key = protector.Unprotect(setting.EncryptedApiKey); }
                catch { IsConfigured = false; throw new AiNotConfiguredException("ไม่สามารถอ่าน API key ที่เข้ารหัสไว้ได้ กรุณาบันทึก API key ใหม่"); }
            }
            if (setting.Provider != "Local" && setting.Provider != "opencode" && string.IsNullOrWhiteSpace(key)) { IsConfigured = false; throw new AiNotConfiguredException($"กรุณาตั้งค่า API key สำหรับ {setting.Provider}"); }
            currentModel = setting.Model; IsConfigured = true;
            var baseUrl = setting.Provider == "OpenRouter" ? (string.IsNullOrWhiteSpace(setting.BaseUrl) ? OpenRouterBaseUrl : setting.BaseUrl) : setting.BaseUrl;
            return new(setting.Provider, key, setting.Model, baseUrl);
        }
        var fallbackKey = fallbackConfiguration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(fallbackKey)) { IsConfigured = false; throw new AiNotConfiguredException("AI ยังไม่พร้อมใช้งาน กรุณาตั้งค่าใน Setting Center"); }
        currentModel = fallbackConfiguration["OpenAI:Model"] ?? "gpt-5-mini"; IsConfigured = true;
        return new("OpenAI", fallbackKey, currentModel, "https://api.openai.com/v1");
    }

    public async Task<string> SendStructuredAsync(object openAiPayload, CancellationToken ct)
    {
        var runtime = await GetRuntimeAsync(ct);
        using var source = JsonDocument.Parse(JsonSerializer.Serialize(openAiPayload));
        var root = source.RootElement;
        var instructions = root.GetProperty("instructions").GetString() ?? "";
        var content = root.GetProperty("input")[0].GetProperty("content");
        var format = root.GetProperty("text").GetProperty("format");
        var schema = format.GetProperty("schema").Clone();
        return runtime.Provider switch
        {
            "Google" => await SendGoogleAsync(runtime, instructions, content, schema, ct),
            "Anthropic" => await SendAnthropicAsync(runtime, instructions, content, schema, ct),
            "Local" or "OpenRouter" or "opencode" => await SendOpenAiCompatibleAsync(runtime, instructions, content, schema, ct),
            _ => await SendOpenAiAsync(runtime, openAiPayload, ct)
        };
    }

    public async Task<IReadOnlyList<AiModelView>> ListModelsAsync(string provider, string? baseUrl, string? apiKey, CancellationToken ct)
    {
        if (!Providers.Contains(provider)) throw new ArgumentException("Provider ไม่ถูกต้อง");
        var key = apiKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            var setting = await db.AiConfigurations.AsNoTracking().OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
            if (setting?.Provider == provider && !string.IsNullOrWhiteSpace(setting.EncryptedApiKey))
                try { key = protector.Unprotect(setting.EncryptedApiKey); } catch { throw new AiNotConfiguredException("ไม่สามารถอ่าน API key ที่บันทึกไว้ได้"); }
            else if (provider == "OpenAI") key = fallbackConfiguration["OpenAI:ApiKey"] ?? "";
        }
        if (provider != "Local" && provider != "OpenRouter" && provider != "opencode" && string.IsNullOrWhiteSpace(key)) throw new AiNotConfiguredException($"กรุณากรอก API key ของ {provider} เพื่อโหลด Model");
        var url = provider switch { "OpenAI" => "https://api.openai.com/v1/models", "Google" => "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000", "Anthropic" => "https://api.anthropic.com/v1/models?limit=1000", "OpenRouter" => $"{RequireLocalUrl(baseUrl ?? OpenRouterBaseUrl)}/models", _ => $"{RequireLocalUrl(baseUrl)}/models" };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (provider == "Google") request.Headers.Add("x-goog-api-key", key);
        else if (provider == "Anthropic") { request.Headers.Add("x-api-key", key); request.Headers.Add("anthropic-version", "2023-06-01"); }
        else if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var body = await SendAsync(request, provider, ct); using var doc = JsonDocument.Parse(body);
        var data = provider == "Google" ? doc.RootElement.GetProperty("models") : doc.RootElement.GetProperty("data");
        return data.EnumerateArray().Select(x => provider == "Google"
                ? new AiModelView((x.GetProperty("name").GetString() ?? "").Replace("models/", ""), x.TryGetProperty("displayName", out var googleDisplay) ? googleDisplay.GetString() ?? "" : "")
                : new AiModelView(x.GetProperty("id").GetString() ?? "", x.TryGetProperty("display_name", out var providerDisplay) ? providerDisplay.GetString() ?? "" : ""))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .DistinctBy(x => x.Id).OrderBy(x => x.Id).ToList();
    }

    private static string RequireLocalUrl(string? baseUrl) { if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("กรุณาระบุ Base URL ของ AI Local"); return baseUrl!.TrimEnd('/'); }

    private async Task<string> SendOpenAiAsync(AiRuntimeConfiguration runtime, object payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{(runtime.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/')}/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", runtime.ApiKey);
        request.Content = Json(payload);
        var body = await SendAsync(request, runtime.Provider, ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("output").EnumerateArray().SelectMany(x => x.TryGetProperty("content", out var value) ? value.EnumerateArray() : []).First(x => x.TryGetProperty("type", out var type) && type.GetString() == "output_text").GetProperty("text").GetString() ?? "";
    }

    private async Task<string> SendGoogleAsync(AiRuntimeConfiguration runtime, string instructions, JsonElement content, JsonElement schema, CancellationToken ct)
    {
        var parts = new List<object>();
        foreach (var item in content.EnumerateArray())
        {
            var type = item.GetProperty("type").GetString();
            if (type == "input_text") parts.Add(new { text = item.GetProperty("text").GetString() });
            else if (type is "input_image" or "input_file")
            {
                var data = type == "input_image" ? item.GetProperty("image_url").GetString() ?? "" : item.GetProperty("file_data").GetString() ?? "";
                var mime = type == "input_image" && data.StartsWith("data:") ? data[5..data.IndexOf(';')] : GuessMime(item.TryGetProperty("filename", out var name) ? name.GetString() : null);
                var base64 = data.Contains(',') ? data[(data.IndexOf(',') + 1)..] : data;
                parts.Add(new { inline_data = new { mime_type = mime, data = base64 } });
            }
        }
        var payload = new { system_instruction = new { parts = new[] { new { text = instructions } } }, contents = new[] { new { role = "user", parts } }, generationConfig = new { responseMimeType = "application/json", responseSchema = schema } };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(runtime.Model)}:generateContent");
        request.Headers.Add("x-goog-api-key", runtime.ApiKey); request.Content = Json(payload);
        var body = await SendAsync(request, runtime.Provider, ct); using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
    }

    private async Task<string> SendAnthropicAsync(AiRuntimeConfiguration runtime, string instructions, JsonElement content, JsonElement schema, CancellationToken ct)
    {
        var blocks = new List<object>();
        foreach (var item in content.EnumerateArray())
        {
            var type = item.GetProperty("type").GetString();
            if (type == "input_text") blocks.Add(new { type = "text", text = item.GetProperty("text").GetString() });
            else if (type == "input_image") { var data = item.GetProperty("image_url").GetString() ?? ""; var split = data.IndexOf(','); var mime = data.StartsWith("data:") ? data[5..data.IndexOf(';')] : "image/png"; blocks.Add(new { type = "image", source = new { type = "base64", media_type = mime, data = split >= 0 ? data[(split + 1)..] : data } }); }
            else blocks.Add(new { type = "text", text = $"[ไฟล์แนบ {item.GetProperty("filename").GetString()} ไม่สามารถส่งตรงไปยัง Provider นี้ได้]" });
        }
        var payload = new { model = runtime.Model, max_tokens = 8192, system = instructions, messages = new[] { new { role = "user", content = blocks } }, output_config = new { format = new { type = "json_schema", schema } } };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages"); request.Headers.Add("x-api-key", runtime.ApiKey); request.Headers.Add("anthropic-version", "2023-06-01"); request.Content = Json(payload);
        var body = await SendAsync(request, runtime.Provider, ct); using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
    }

    private async Task<string> SendOpenAiCompatibleAsync(AiRuntimeConfiguration runtime, string instructions, JsonElement content, JsonElement schema, CancellationToken ct)
    {
        var prompt = string.Join("\n", content.EnumerateArray().Where(x => x.GetProperty("type").GetString() == "input_text").Select(x => x.GetProperty("text").GetString()));
        var payload = new { model = runtime.Model, messages = new[] { new { role = "system", content = instructions }, new { role = "user", content = prompt } }, response_format = new { type = "json_schema", json_schema = new { name = "qa_result", strict = true, schema } }, temperature = 0.2 };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{runtime.BaseUrl!.TrimEnd('/')}/chat/completions"); if (!string.IsNullOrWhiteSpace(runtime.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", runtime.ApiKey); request.Content = Json(payload);
        var body = await SendAsync(request, runtime.Provider, ct); using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    private async Task<string> SendAsync(HttpRequestMessage request, string provider, CancellationToken ct) { using var client = clients.CreateClient(); client.Timeout = TimeSpan.FromMinutes(5); using var response = await client.SendAsync(request, ct); var body = await response.Content.ReadAsStringAsync(ct); if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{provider} ตอบกลับไม่สำเร็จ ({(int)response.StatusCode}): {body}"); return body; }
    private static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    private static string GuessMime(string? name) => Path.GetExtension(name)?.ToLowerInvariant() switch { ".pdf" => "application/pdf", ".txt" => "text/plain", ".csv" => "text/csv", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => "application/octet-stream" };

    public async Task<AiConfigurationView> GetViewAsync(CancellationToken ct) { var s = await db.AiConfigurations.AsNoTracking().OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(ct); if (s is not null) return new(s.Provider, s.Model, s.BaseUrl, s.IsEnabled, !string.IsNullOrWhiteSpace(s.EncryptedApiKey), s.ApiKeyHint, s.UpdatedAt); var has = !string.IsNullOrWhiteSpace(fallbackConfiguration["OpenAI:ApiKey"]); return new("OpenAI", fallbackConfiguration["OpenAI:Model"] ?? "gpt-5-mini", "https://api.openai.com/v1", true, has, has ? "Server configuration" : null, null); }
    public async Task<AiConfigurationView> SaveAsync(string provider, string model, string? baseUrl, string? apiKey, bool isEnabled, bool clearApiKey, CancellationToken ct)
    {
        if (!Providers.Contains(provider)) throw new ArgumentException("Provider ไม่ถูกต้อง");
        if (provider == "Local" && (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))) throw new ArgumentException("กรุณาระบุ Base URL ของ AI Local เป็น http:// หรือ https://");
        if (provider == "OpenRouter" && !string.IsNullOrWhiteSpace(baseUrl) && (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var orUri) || orUri.Scheme is not ("http" or "https"))) throw new ArgumentException("กรุณาระบุ Base URL ของ OpenRouter เป็น http:// หรือ https://");
        var s = await db.AiConfigurations.OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(ct); var providerChanged = s is not null && s.Provider != provider;
        var encrypted = clearApiKey || providerChanged ? "" : s?.EncryptedApiKey ?? ""; var hint = clearApiKey || providerChanged ? null : s?.ApiKeyHint;
        if (!string.IsNullOrWhiteSpace(apiKey)) { var key = apiKey.Trim(); encrypted = protector.Protect(key); hint = key.Length > 4 ? $"••••{key[^4..]}" : "••••"; }
        if (isEnabled && provider != "Local" && string.IsNullOrWhiteSpace(encrypted) && !(provider == "OpenAI" && !string.IsNullOrWhiteSpace(fallbackConfiguration["OpenAI:ApiKey"]))) throw new ArgumentException($"กรุณาระบุ API key สำหรับ {provider}");
        var normalizedBaseUrl = provider switch { "OpenAI" => "https://api.openai.com/v1", "Google" => "https://generativelanguage.googleapis.com", "Anthropic" => "https://api.anthropic.com", "OpenRouter" => string.IsNullOrWhiteSpace(baseUrl) ? OpenRouterBaseUrl : baseUrl, _ => baseUrl };
        if (s is null) { s = new(provider, model, normalizedBaseUrl, encrypted, hint, isEnabled); db.AiConfigurations.Add(s); } else s.Update(provider, model, normalizedBaseUrl, encrypted, hint, isEnabled); await db.SaveChangesAsync(ct);
        return new(s.Provider, s.Model, s.BaseUrl, s.IsEnabled, !string.IsNullOrWhiteSpace(s.EncryptedApiKey), s.ApiKeyHint, s.UpdatedAt);
    }
}

public sealed record AiConfigurationView(string Provider, string Model, string? BaseUrl, bool IsEnabled, bool HasApiKey, string? ApiKeyHint, DateTimeOffset? UpdatedAt);
public sealed record AiModelView(string Id, string DisplayName);
