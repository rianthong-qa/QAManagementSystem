namespace ProMaxx2.QA.Domain.Automation;

public sealed record AutomationValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public static class AutomationValidator
{
    public static AutomationValidationResult Validate(
        DslDocument dsl,
        IReadOnlyList<string> knownActions,
        IReadOnlyList<string> knownObjects,
        IReadOnlyList<string>? knownTestData = null)
    {
        var errors = new List<string>();
        if (dsl is null) return new(false, ["DSL is empty."]);

        if (string.IsNullOrWhiteSpace(dsl.DslVersion)) errors.Add("DslVersion is required.");
        else if (dsl.DslVersion != "1.0") errors.Add($"Unsupported DslVersion '{dsl.DslVersion}'. Only 1.0 is supported.");

        if (string.IsNullOrWhiteSpace(dsl.AutomationType)) errors.Add("AutomationType is required.");

        if (dsl.Steps is null || dsl.Steps.Count == 0)
        {
            errors.Add("DSL must contain at least one step.");
        }
        else
        {
            var numbers = dsl.Steps.Select(x => x.StepNo).ToList();
            if (numbers.Any(x => x < 1)) errors.Add("StepNo must be a positive integer.");
            if (numbers.Count != numbers.Distinct().Count()) errors.Add("StepNo must be unique.");
            if (numbers.Count > 0)
            {
                var expected = Enumerable.Range(numbers.Min(), numbers.Count).ToList();
                if (!numbers.OrderBy(x => x).SequenceEqual(expected)) errors.Add("StepNo must be sequential starting from the first step.");
            }

            foreach (var step in dsl.Steps)
            {
                if (string.IsNullOrWhiteSpace(step.Action))
                {
                    errors.Add($"Step {step.StepNo}: Action is required.");
                    continue;
                }
                var action = step.Action.Trim().ToUpperInvariant();
                if (knownActions.Count > 0 && !knownActions.Contains(action, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"Step {step.StepNo}: Action '{step.Action}' is not in the Action Library.");

                if (step.Parameters?.ContainsKey("object") == true && !string.IsNullOrWhiteSpace(step.Parameters["object"]))
                {
                    var obj = step.Parameters["object"].Trim();
                    if (knownObjects.Count > 0 && !knownObjects.Contains(obj, StringComparer.OrdinalIgnoreCase))
                        errors.Add($"Step {step.StepNo}: Object '{obj}' is not in the Object Repository.");
                }
                if (knownTestData is { Count: > 0 } && step.Parameters is not null)
                {
                    foreach (var (key, value) in step.Parameters)
                    {
                        if (string.IsNullOrWhiteSpace(value)) continue;
                        if (value.StartsWith("TEST_", StringComparison.Ordinal) || value.StartsWith("ITEM_", StringComparison.Ordinal))
                        {
                            if (!knownTestData.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
                                errors.Add($"Step {step.StepNo}: Test data reference '{value}' is not defined.");
                        }
                    }
                }
            }
        }

        return new(errors.Count == 0, errors);
    }
}