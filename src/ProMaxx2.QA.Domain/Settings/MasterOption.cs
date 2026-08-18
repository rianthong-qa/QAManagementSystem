namespace ProMaxx2.QA.Domain.Settings;

public sealed class MasterOption
{
    private MasterOption() { }

    public MasterOption(string category, string value, string displayName, int sortOrder)
    {
        MasterOptionId = Guid.NewGuid();
        Category = Required(category, nameof(category));
        Value = Required(value, nameof(value));
        DisplayName = Required(displayName, nameof(displayName));
        SortOrder = sortOrder;
        IsActive = true;
    }

    public Guid MasterOptionId { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string value, string displayName, int sortOrder, bool isActive)
    {
        Value = Required(value, nameof(value));
        DisplayName = Required(displayName, nameof(displayName));
        SortOrder = sortOrder;
        IsActive = isActive;
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.") : value.Trim();
}
