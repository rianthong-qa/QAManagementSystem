using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.UnitTests;

public sealed class AutomationDslValidatorTests
{
    private static DslDocument ValidDsl() => new()
    {
        DslVersion = "1.0",
        AutomationType = "WindowsUI",
        Steps =
        [
            new DslStep { StepNo = 1, Action = "LOGIN", Parameters = new() { ["userRef"] = "QA_STANDARD_USER" } },
            new DslStep { StepNo = 2, Action = "SET_QTY", Parameters = new() { ["object"] = "Sales.Quantity" } },
        ],
    };

    private static readonly string[] KnownActions = ["LOGIN", "SET_QTY", "SAVE_DOCUMENT"];
    private static readonly string[] KnownObjects = ["Sales.Quantity", "Sales.Save"];
    private static readonly string[] KnownTestData = ["TEST_BRANCH_001", "ITEM_A001"];

    [Fact]
    public void Valid_dsl_passes_with_no_errors()
    {
        var result = AutomationValidator.Validate(ValidDsl(), KnownActions, KnownObjects, KnownTestData);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Null_dsl_is_invalid()
    {
        var result = AutomationValidator.Validate(null!, KnownActions, KnownObjects, KnownTestData);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Missing_dsl_version_is_invalid(string? version)
    {
        var dsl = ValidDsl();
        dsl.DslVersion = version!;
        var result = AutomationValidator.Validate(dsl, KnownActions, KnownObjects, KnownTestData);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("DslVersion is required", StringComparison.Ordinal));
    }

    [Fact]
    public void Unsupported_dsl_version_is_invalid()
    {
        var dsl = ValidDsl();
        dsl.DslVersion = "2.0";
        var result = AutomationValidator.Validate(dsl, KnownActions, KnownObjects, KnownTestData);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Unsupported DslVersion", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_automation_type_is_invalid()
    {
        var dsl = ValidDsl();
        dsl.AutomationType = "";
        var result = AutomationValidator.Validate(dsl, KnownActions, KnownObjects, KnownTestData);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("AutomationType is required", StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_steps_is_invalid()
    {
        var dsl = ValidDsl();
        dsl.Steps = [];
        var result = AutomationValidator.Validate(dsl, KnownActions, KnownObjects, KnownTestData);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at least one step", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Non_positive_step_number_is_invalid()
    {
        var dsl = ValidDsl();
        dsl.Steps[0].StepNo = 0;
        var result = AutomationValidator.Validate(dsl, KnownActions, KnownObjects, KnownTestData);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("positive integer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_step_numbers_are_invalid()
    {
        var dsl = ValidDsl();
        dsl.Steps[1].StepNo = 1;
        var result = AutomationValidator.Validate(dsl, KnownActions, KnownObjects, KnownTestData);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("must be unique", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Non_contiguous_step_numbers_are_invalid()
    {
        var dsl = ValidDsl();
        dsl.Steps[1].StepNo = 5;
        var result = AutomationValidator.Validate(dsl, KnownActions, KnownObjects, KnownTestData);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("sequential", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Blank_action_is_invalid()
    {
        var dsl = ValidDsl();
        dsl.Steps[0].Action = " ";
        var result = AutomationValidator.Validate(dsl, KnownActions, KnownObjects, KnownTestData);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Action is required", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_action_is_invalid_when_action_library_is_not_empty()
    {
        var dsl = ValidDsl();
        dsl.Steps[0].Action = "CONFIRM_SALE_X";
        var result = AutomationValidator.Validate(dsl, KnownActions, KnownObjects, KnownTestData);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("not in the Action Library", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_action_is_allowed_when_action_library_is_empty()
    {
        var dsl = ValidDsl();
        dsl.Steps[0].Action = "CONFIRM_SALE_X";
        var result = AutomationValidator.Validate(dsl, [], KnownObjects, KnownTestData);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Unknown_object_reference_is_invalid_when_object_repository_is_not_empty()
    {
        var dsl = ValidDsl();
        dsl.Steps[1].Parameters["object"] = "Sales.Unknown";
        var result = AutomationValidator.Validate(dsl, KnownActions, KnownObjects, KnownTestData);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("not in the Object Repository", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_object_reference_is_allowed_when_object_repository_is_empty()
    {
        var dsl = ValidDsl();
        dsl.Steps[1].Parameters["object"] = "Sales.Unknown";
        var result = AutomationValidator.Validate(dsl, KnownActions, [], KnownTestData);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("TEST_UNKNOWN")]
    [InlineData("ITEM_UNKNOWN")]
    public void Unknown_test_data_reference_is_invalid(string value)
    {
        var dsl = ValidDsl();
        dsl.Steps[1].Parameters["itemCode"] = value;
        var result = AutomationValidator.Validate(dsl, KnownActions, KnownObjects, KnownTestData);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Test data reference", StringComparison.Ordinal));
    }

    [Fact]
    public void Known_test_data_reference_is_valid()
    {
        var dsl = ValidDsl();
        dsl.Steps[1].Parameters["itemCode"] = "ITEM_A001";
        var result = AutomationValidator.Validate(dsl, KnownActions, KnownObjects, KnownTestData);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Test_data_reference_is_not_checked_when_known_test_data_is_null()
    {
        var dsl = ValidDsl();
        dsl.Steps[1].Parameters["itemCode"] = "TEST_UNKNOWN";
        var result = AutomationValidator.Validate(dsl, KnownActions, KnownObjects, null);
        Assert.True(result.IsValid);
    }
}
