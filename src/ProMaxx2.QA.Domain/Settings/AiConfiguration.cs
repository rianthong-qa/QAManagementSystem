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
