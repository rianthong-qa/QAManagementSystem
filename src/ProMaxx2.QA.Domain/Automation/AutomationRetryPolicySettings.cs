namespace ProMaxx2.QA.Domain.Automation;

public sealed class AutomationRetryPolicySettings
{
    public const int SingletonId = 1;

    private AutomationRetryPolicySettings() { }
    public AutomationRetryPolicySettings(int maxAttempts, int backoffSeconds, bool enabled)
    {
        Id = SingletonId;
        MaxAttempts = Math.Clamp(maxAttempts, 0, 10);
        BackoffSeconds = Math.Clamp(backoffSeconds, 0, 3600);
        Enabled = enabled;
        UpdatedAt = DateTime.UtcNow;
    }
    public int Id { get; private set; } = SingletonId;
    public int MaxAttempts { get; private set; }
    public int BackoffSeconds { get; private set; }
    public bool Enabled { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public void Update(int maxAttempts, int backoffSeconds, bool enabled, Guid? userId)
    {
        MaxAttempts = Math.Clamp(maxAttempts, 0, 10);
        BackoffSeconds = Math.Clamp(backoffSeconds, 0, 3600);
        Enabled = enabled;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = userId;
    }
}
