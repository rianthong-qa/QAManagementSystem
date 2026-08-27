using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.UnitTests;

public sealed class AutomationManagementTests
{
    [Fact]
    public void Action_update_changes_runtime_metadata_and_status()
    {
        var action = new AutomationAction("CLICK", "Click", "Generic UI", null, "{}", "CLICK", "1.0.0");

        action.Update("Click control", "Interaction", "Updated", "{\"type\":\"object\"}", "CLICK_V2", "1.2.0", false);

        Assert.Equal("Click control", action.ActionName);
        Assert.Equal("Interaction", action.Category);
        Assert.Equal("CLICK_V2", action.HandlerKey);
        Assert.Equal("1.2.0", action.MinimumAgentVersion);
        Assert.False(action.IsActive);
        Assert.NotNull(action.UpdatedAt);
    }

    [Fact]
    public void Action_update_rejects_missing_handler_key()
    {
        var action = new AutomationAction("CLICK", "Click", "Generic UI", null, "{}", "CLICK", null);

        Assert.Throws<ArgumentException>(() => action.Update("Click", "Generic UI", null, "{}", " ", null, true));
    }

    [Fact]
    public void Object_update_changes_business_key_and_increments_version()
    {
        var projectId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var item = new AutomationObject(projectId, null, "Promaxx2", "Sales", "SAVE", "Save", "Button", "btnSave", "{}");

        item.Update(moduleId, "Pos", "Payment", "CONFIRM", "Confirm payment", "Button", "btnConfirm", "{\"framework\":\"uia3\"}");

        Assert.Equal(moduleId, item.ModuleId);
        Assert.Equal("Pos", item.ApplicationCode);
        Assert.Equal("Payment", item.ScreenCode);
        Assert.Equal("CONFIRM", item.ObjectCode);
        Assert.Equal("btnConfirm", item.AutomationId);
        Assert.Equal(2, item.ObjectVersion);
        Assert.NotNull(item.UpdatedAt);
    }

    [Fact]
    public void Object_can_be_deactivated_and_reactivated()
    {
        var item = new AutomationObject(Guid.NewGuid(), null, "Promaxx2", "Sales", "SAVE", "Save", "Button", "btnSave", "{}");

        item.SetActive(false);
        Assert.False(item.IsActive);

        item.SetActive(true);
        Assert.True(item.IsActive);
    }

    [Fact]
    public void Action_update_accepts_retry_safety()
    {
        var action = new AutomationAction("SAVE_DOCUMENT", "Save", "Document", null, "{}", "SAVE_DOCUMENT", "1.0.0");
        Assert.Equal("Unsafe", action.RetrySafety);

        action.Update("Save", "Document", null, "{}", "SAVE_DOCUMENT", "1.0.0", true, "Safe");

        Assert.Equal("Safe", action.RetrySafety);
    }

    [Fact]
    public void Action_update_rejects_invalid_retry_safety()
    {
        var action = new AutomationAction("SAVE_DOCUMENT", "Save", "Document", null, "{}", "SAVE_DOCUMENT", "1.0.0");
        Assert.Throws<ArgumentException>(() => action.Update("Save", "Document", null, "{}", "SAVE_DOCUMENT", "1.0.0", true, "Whatever"));
    }

    [Fact]
    public void Case_require_maintenance_sets_reason_and_opened_at()
    {
        var testCaseId = Guid.NewGuid();
        var item = new AutomationCase(testCaseId, "AUT-001", "WindowsUI", null, null);

        item.RequireMaintenance("Object not found: btnSave", null);

        Assert.Equal("MaintenanceRequired", item.Status);
        Assert.Equal("Object not found: btnSave", item.MaintenanceReason);
        Assert.NotNull(item.MaintenanceOpenedAt);
    }

    [Fact]
    public void Case_resolve_maintenance_clears_fields_and_returns_to_needs_review()
    {
        var item = new AutomationCase(Guid.NewGuid(), "AUT-002", "WindowsUI", null, null);
        item.RequireMaintenance("Object not found", null);
        var ownerId = Guid.NewGuid();
        item.AssignMaintenanceOwner(ownerId);
        Assert.Equal(ownerId, item.MaintenanceOwnerUserId);

        item.ResolveMaintenance(null);

        Assert.Equal("NeedsReview", item.Status);
        Assert.Null(item.MaintenanceReason);
        Assert.Null(item.MaintenanceOwnerUserId);
        Assert.Null(item.MaintenanceOpenedAt);
    }

    [Fact]
    public void Case_resolve_maintenance_rejects_when_not_in_maintenance()
    {
        var item = new AutomationCase(Guid.NewGuid(), "AUT-003", "WindowsUI", null, null);
        Assert.Throws<InvalidOperationException>(() => item.ResolveMaintenance(null));
    }

    [Fact]
    public void Case_quarantine_and_unquarantine_toggle_flag()
    {
        var item = new AutomationCase(Guid.NewGuid(), "AUT-004", "WindowsUI", null, null);
        var ownerId = Guid.NewGuid();
        var expiry = DateTime.UtcNow.AddDays(7);

        item.Quarantine("Flaky: 3 transitions in last 5 runs", ownerId, expiry);
        Assert.True(item.IsQuarantined);
        Assert.Equal("Flaky: 3 transitions in last 5 runs", item.QuarantineReason);
        Assert.Equal(ownerId, item.QuarantineOwnerUserId);
        Assert.Equal(expiry, item.QuarantineExpiresAt);

        item.Unquarantine();
        Assert.False(item.IsQuarantined);
        Assert.Null(item.QuarantineReason);
        Assert.Null(item.QuarantineOwnerUserId);
        Assert.Null(item.QuarantineExpiresAt);
    }

    [Fact]
    public void Execution_set_classification_and_mark_as_retry()
    {
        var caseId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var buildId = Guid.NewGuid();
        var envId = Guid.NewGuid();
        var execution = new AutomationExecution(caseId, versionId, null, buildId, envId, "user1");
        var originalId = Guid.NewGuid();

        execution.SetClassification("AgentFailure", "Retry");
        execution.MarkAsRetry(originalId, 1);

        Assert.Equal("AgentFailure", execution.ClassifiedFailureType);
        Assert.Equal("Retry", execution.ClassifiedRecommendation);
        Assert.Equal(originalId, execution.RetryOfExecutionId);
        Assert.Equal(1, execution.RetryCount);
    }

    [Fact]
    public void Verification_complete_records_actual_result()
    {
        var objectId = Guid.NewGuid();
        var verification = new AutomationObjectVerification(objectId, null, null);
        var agentId = Guid.NewGuid();

        verification.Assign(agentId);
        verification.Complete("ControlTypeMismatch", "TextBox", "btnSave", "Expected Button, found TextBox.");

        Assert.Equal(agentId, verification.AssignedAgentId);
        Assert.Equal("ControlTypeMismatch", verification.Status);
        Assert.Equal("TextBox", verification.ActualControlType);
        Assert.Equal("btnSave", verification.ActualAutomationId);
        Assert.NotNull(verification.CompletedAt);
    }

    [Fact]
    public void RetryPolicySettings_update_clamps_values()
    {
        var settings = new AutomationRetryPolicySettings(2, 30, true);

        settings.Update(999, -5, false, null);

        Assert.Equal(10, settings.MaxAttempts);
        Assert.Equal(0, settings.BackoffSeconds);
        Assert.False(settings.Enabled);
    }
}
