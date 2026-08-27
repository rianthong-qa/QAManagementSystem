namespace ProMaxx2.QA.Domain.Automation;

public sealed class AutomationObject
{
    private AutomationObject() { }
    public AutomationObject(Guid projectId, Guid? moduleId, string applicationCode, string screenCode, string objectCode, string objectName, string controlType, string? automationId, string selectorJson)
    {
        if (projectId == Guid.Empty || string.IsNullOrWhiteSpace(objectCode)) throw new ArgumentException("Project and object code are required.");
        AutomationObjectId = Guid.NewGuid();
        ProjectId = projectId;
        ModuleId = moduleId;
        ApplicationCode = string.IsNullOrWhiteSpace(applicationCode) ? "Promaxx2" : applicationCode.Trim();
        ScreenCode = string.IsNullOrWhiteSpace(screenCode) ? "Default" : screenCode.Trim();
        ObjectCode = objectCode.Trim().ToUpperInvariant();
        ObjectName = objectName.Trim();
        ControlType = controlType.Trim();
        AutomationId = automationId?.Trim();
        SelectorJson = string.IsNullOrWhiteSpace(selectorJson) ? "{}" : selectorJson;
        ObjectVersion = 1;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }
    public Guid AutomationObjectId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid? ModuleId { get; private set; }
    public string ApplicationCode { get; private set; } = "Promaxx2";
    public string ScreenCode { get; private set; } = "Default";
    public string ObjectCode { get; private set; } = string.Empty;
    public string ObjectName { get; private set; } = string.Empty;
    public string ControlType { get; private set; } = "Control";
    public string? AutomationId { get; private set; }
    public string SelectorJson { get; private set; } = "{}";
    public int ObjectVersion { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Projects.ProductModule? Module { get; private set; }

    public void Update(Guid? moduleId, string applicationCode, string screenCode, string objectCode, string objectName, string controlType, string? automationId, string selectorJson)
    {
        if (string.IsNullOrWhiteSpace(screenCode) || string.IsNullOrWhiteSpace(objectCode) || string.IsNullOrWhiteSpace(objectName) || string.IsNullOrWhiteSpace(controlType))
            throw new ArgumentException("Screen code, object code, object name and control type are required.");
        ModuleId = moduleId;
        ApplicationCode = string.IsNullOrWhiteSpace(applicationCode) ? "Promaxx2" : applicationCode.Trim();
        ScreenCode = screenCode.Trim();
        ObjectCode = objectCode.Trim().ToUpperInvariant();
        ObjectName = objectName.Trim();
        ControlType = controlType.Trim();
        AutomationId = automationId?.Trim();
        SelectorJson = string.IsNullOrWhiteSpace(selectorJson) ? "{}" : selectorJson;
        ObjectVersion += 1;
        UpdatedAt = DateTime.UtcNow;
    }
    public void SetActive(bool active)
    {
        IsActive = active;
        UpdatedAt = DateTime.UtcNow;
    }
}
