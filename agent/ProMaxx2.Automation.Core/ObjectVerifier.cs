namespace ProMaxx2.Automation.Core;

public sealed record VerifierOutcome(string Status, string? ActualAutomationId, string? ActualControlType, string? Message);

public static class ObjectVerifier
{
    public static VerifierOutcome Verify(UiInspectResult scan, string? expectedAutomationId, string expectedControlType)
    {
        if (string.IsNullOrWhiteSpace(expectedAutomationId))
            return new VerifierOutcome("Error", null, null, "Object has no AutomationId configured to verify against.");

        var matches = scan.Nodes.Where(n => string.Equals(n.AutomationId, expectedAutomationId, StringComparison.Ordinal)).ToList();
        if (matches.Count == 0)
            return new VerifierOutcome("NotFound", null, null, $"AutomationId '{expectedAutomationId}' not found in current UI tree ({scan.Nodes.Count} nodes scanned).");
        if (matches.Count > 1)
            return new VerifierOutcome("Duplicate", matches[0].AutomationId, matches[0].ControlType, $"{matches.Count} elements share AutomationId '{expectedAutomationId}'.");

        var match = matches[0];
        if (!string.IsNullOrWhiteSpace(expectedControlType) && !string.Equals(match.ControlType, expectedControlType, StringComparison.OrdinalIgnoreCase))
            return new VerifierOutcome("ControlTypeMismatch", match.AutomationId, match.ControlType, $"Expected ControlType '{expectedControlType}', found '{match.ControlType}'.");

        return new VerifierOutcome("Found", match.AutomationId, match.ControlType, null);
    }
}
