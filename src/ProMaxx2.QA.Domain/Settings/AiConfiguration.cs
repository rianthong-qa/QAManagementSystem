namespace ProMaxx2.QA.Domain.Settings;

public sealed class AiConfiguration
{
    private AiConfiguration() { }

    public AiConfiguration(string provider, string model, string? baseUrl, string encryptedApiKey, string? apiKeyHint, bool isEnabled)
    {
        AiConfigurationId = Guid.NewGuid();
        Update(provider, model, baseUrl, encryptedApiKey, apiKeyHint, isEnabled);
    }

    public Guid AiConfigurationId { get; private set; }
    public string Provider { get; private set; } = "OpenAI";
    public string Model { get; private set; } = "gpt-5-mini";
    public string? BaseUrl { get; private set; }
    public string EncryptedApiKey { get; private set; } = string.Empty;
    public string? ApiKeyHint { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // คลังความรู้ (AI Chat): Embedding provider แยกจาก Chat เพราะ Anthropic/Google ไม่มี embedding API ในระบบนี้ — None = ค้นด้วย Full-Text อย่างเดียว
    public string EmbeddingProvider { get; private set; } = "None";
    public string? EmbeddingModel { get; private set; }
    public string? EmbeddingBaseUrl { get; private set; }
    public string EncryptedEmbeddingApiKey { get; private set; } = string.Empty;
    public string? EmbeddingApiKeyHint { get; private set; }
    /// <summary>อนุญาตส่งข้อความจาก CRM (mask แล้ว) ให้ AI ที่ไม่ใช่ Local — ค่าเริ่มต้นปิด</summary>
    public bool AllowCrmDataToExternalAi { get; private set; }
    /// <summary>เปิดให้เชื่อมต่อคลังความรู้จาก Claude ผ่าน MCP (/mcp + Personal Access Token) — ค่าเริ่มต้นปิด</summary>
    public bool McpEnabled { get; private set; }

    public static readonly string[] EmbeddingProviders = ["None", "OpenAI", "OpenRouter", "Local"];

    public void UpdateKnowledge(string embeddingProvider, string? embeddingModel, string? embeddingBaseUrl, string encryptedEmbeddingApiKey, string? embeddingApiKeyHint, bool allowCrmDataToExternalAi, bool mcpEnabled)
    {
        EmbeddingProvider = EmbeddingProviders.FirstOrDefault(x => string.Equals(x, embeddingProvider?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Embedding provider ไม่ถูกต้อง");
        EmbeddingModel = EmbeddingProvider == "None" || string.IsNullOrWhiteSpace(embeddingModel) ? null : embeddingModel.Trim();
        if (EmbeddingProvider != "None" && EmbeddingModel is null) throw new ArgumentException("กรุณาระบุ Embedding model");
        EmbeddingBaseUrl = EmbeddingProvider == "None" || string.IsNullOrWhiteSpace(embeddingBaseUrl) ? null : embeddingBaseUrl.Trim().TrimEnd('/');
        EncryptedEmbeddingApiKey = encryptedEmbeddingApiKey;
        EmbeddingApiKeyHint = embeddingApiKeyHint;
        AllowCrmDataToExternalAi = allowCrmDataToExternalAi;
        McpEnabled = mcpEnabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Update(string provider, string model, string? baseUrl, string encryptedApiKey, string? apiKeyHint, bool isEnabled)
    {
        Provider = Required(provider, nameof(provider));
        Model = Required(model, nameof(model));
        BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/');
        EncryptedApiKey = encryptedApiKey;
        ApiKeyHint = apiKeyHint;
        IsEnabled = isEnabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.") : value.Trim();
}
