using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace ProMaxx2.QA.Api.Services;

public sealed record AiChatMessage(string Role, string Content);

/// <summary>แยก interface เพื่อทดสอบ KnowledgeChatService โดยไม่เรียก LLM จริง</summary>
public interface IAiChatClient
{
    Task<AiRuntimeConfiguration> GetRuntimeAsync(CancellationToken ct);
    /// <param name="conversationId">id ที่คงที่ต่อบทสนทนา — opencode ใช้ route/cache (header x-opencode-session)</param>
    IAsyncEnumerable<string> StreamAsync(AiRuntimeConfiguration runtime, string system, IReadOnlyList<AiChatMessage> messages, string? conversationId, CancellationToken ct);
}

/// <summary>
/// แชทแบบ streaming กับ Provider ที่ตั้งค่าใน Setting Center (ใช้ <see cref="SharedAiConfigurationService.GetRuntimeAsync"/> ตัวเดียวกับ AI Generate)
/// OpenAI = Responses API, Anthropic = Messages API, Google = streamGenerateContent (SSE), Local/OpenRouter/opencode = OpenAI-compatible chat/completions
/// คืนข้อความทีละส่วน (delta) — ผู้เรียกต่อข้อความเอง
/// </summary>
public sealed class AiChatClient(SharedAiConfigurationService configuration, IHttpClientFactory clients) : IAiChatClient
{
    public const int MaxOutputTokens = 4096;

    public Task<AiRuntimeConfiguration> GetRuntimeAsync(CancellationToken ct) => configuration.GetRuntimeAsync(ct);

    public async IAsyncEnumerable<string> StreamAsync(AiRuntimeConfiguration runtime, string system, IReadOnlyList<AiChatMessage> messages, string? conversationId, [EnumeratorCancellation] CancellationToken ct)
    {
        using var request = BuildRequest(runtime, system, messages, conversationId);
        using var client = clients.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"{runtime.Provider} ตอบกลับไม่สำเร็จ ({(int)response.StatusCode}): {Cut(body, 500)}");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? eventName = null;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0) { eventName = null; continue; }
            if (line.StartsWith("event:", StringComparison.Ordinal)) { eventName = line[6..].Trim(); continue; }
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var result = AiStreamParser.Parse(runtime.Provider, eventName, line[5..].Trim());
            if (result.Error is not null) throw new InvalidOperationException($"{runtime.Provider}: {result.Error}");
            if (!string.IsNullOrEmpty(result.Delta)) yield return result.Delta;
            if (result.Done) yield break;
        }
    }

    private static HttpRequestMessage BuildRequest(AiRuntimeConfiguration runtime, string system, IReadOnlyList<AiChatMessage> messages, string? conversationId)
    {
        HttpRequestMessage request;
        switch (runtime.Provider)
        {
            case "OpenAI":
                request = new(HttpMethod.Post, $"{(runtime.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/')}/responses");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", runtime.ApiKey);
                request.Content = Json(new { model = runtime.Model, instructions = system, input = messages.Select(m => new { role = m.Role, content = m.Content }), stream = true, store = false, max_output_tokens = MaxOutputTokens });
                break;
            case "Anthropic":
                request = new(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
                request.Headers.Add("x-api-key", runtime.ApiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
                request.Content = Json(new { model = runtime.Model, max_tokens = MaxOutputTokens, system, messages = messages.Select(m => new { role = m.Role, content = m.Content }), stream = true });
                break;
            case "Google":
                request = new(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(runtime.Model)}:streamGenerateContent?alt=sse");
                request.Headers.Add("x-goog-api-key", runtime.ApiKey);
                request.Content = Json(new
                {
                    system_instruction = new { parts = new[] { new { text = system } } },
                    contents = messages.Select(m => new { role = m.Role == "assistant" ? "model" : "user", parts = new[] { new { text = m.Content } } }),
                    generationConfig = new { maxOutputTokens = MaxOutputTokens, temperature = 0.2 },
                });
                break;
            default: // Local (Ollama/LM Studio), OpenRouter, opencode
                request = new(HttpMethod.Post, $"{runtime.BaseUrl!.TrimEnd('/')}/chat/completions");
                if (!string.IsNullOrWhiteSpace(runtime.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", runtime.ApiKey);
                request.Content = Json(new
                {
                    model = runtime.Model,
                    messages = new[] { new { role = "system", content = system } }.Concat(messages.Select(m => new { role = m.Role, content = m.Content })),
                    stream = true, temperature = 0.2, max_tokens = MaxOutputTokens,
                });
                break;
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        // ระบุตัวด้วย User-Agent ของระบบเอง และ session ที่คงที่ต่อบทสนทนา (opencode Go ปฏิเสธคำขอที่ไม่มี x-opencode-session — 400 MissingSessionID)
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ProMaxx2-QAHub", "1.0"));
        if (runtime.Provider == "opencode" && !string.IsNullOrWhiteSpace(conversationId)) request.Headers.Add("x-opencode-session", conversationId);
        return request;
    }

    private static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    private static string Cut(string value, int max) => value.Length > max ? value[..max] + "..." : value;
}

public readonly record struct AiStreamChunk(string? Delta, bool Done, string? Error);

/// <summary>แปลงบรรทัด SSE <c>data:</c> ของแต่ละ Provider เป็นข้อความ (แยกเป็น static เพื่อทดสอบด้วยตัวอย่างจริงได้)</summary>
public static class AiStreamParser
{
    public static AiStreamChunk Parse(string provider, string? eventName, string data)
    {
        if (data == "[DONE]") return new(null, true, null);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(data); }
        catch (JsonException) { return default; } // keep-alive / บรรทัดที่ไม่ใช่ JSON
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return default;
            return provider switch
            {
                "OpenAI" => OpenAi(root),
                "Anthropic" => Anthropic(root, eventName),
                "Google" => Google(root),
                _ => OpenAiCompatible(root),
            };
        }
    }

    private static AiStreamChunk OpenAi(JsonElement root)
    {
        var type = Str(root, "type");
        return type switch
        {
            "response.output_text.delta" => new(Str(root, "delta"), false, null),
            "response.completed" => new(null, true, null),
            "response.incomplete" => new(null, true, null),
            "response.failed" => new(null, true, ErrorMessage(root.TryGetProperty("response", out var r) ? r : root) ?? "การตอบล้มเหลว"),
            "error" => new(null, true, Str(root, "message") ?? ErrorMessage(root) ?? "การตอบล้มเหลว"),
            _ => default,
        };
    }

    private static AiStreamChunk Anthropic(JsonElement root, string? eventName)
    {
        var type = Str(root, "type") ?? eventName;
        if (type == "content_block_delta" && root.TryGetProperty("delta", out var delta) && Str(delta, "type") == "text_delta") return new(Str(delta, "text"), false, null);
        if (type == "message_stop") return new(null, true, null);
        if (type == "error") return new(null, true, ErrorMessage(root) ?? "การตอบล้มเหลว");
        return default;
    }

    private static AiStreamChunk Google(JsonElement root)
    {
        if (root.TryGetProperty("error", out _)) return new(null, true, ErrorMessage(root));
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0) return default;
        var candidate = candidates[0];
        var text = new StringBuilder();
        if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            foreach (var part in parts.EnumerateArray())
                if (part.TryGetProperty("text", out var t) && !(part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)) text.Append(t.GetString());
        // finishReason มากับ chunk สุดท้าย — สตรีมจบเองเมื่อ connection ปิด
        return new(text.Length > 0 ? text.ToString() : null, false, null);
    }

    private static AiStreamChunk OpenAiCompatible(JsonElement root)
    {
        if (root.TryGetProperty("error", out _)) return new(null, true, ErrorMessage(root));
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return default;
        var choice = choices[0];
        // reasoning_content (โมเดลที่คิดก่อนตอบ) ไม่แสดงให้ผู้ใช้ — ใช้เฉพาะ content
        var delta = choice.TryGetProperty("delta", out var d) ? Str(d, "content") : null;
        return new(delta, false, null);
    }

    private static string? ErrorMessage(JsonElement root) =>
        root.TryGetProperty("error", out var error) ? error.ValueKind == JsonValueKind.Object ? Str(error, "message") ?? error.GetRawText() : error.ToString() : null;

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
