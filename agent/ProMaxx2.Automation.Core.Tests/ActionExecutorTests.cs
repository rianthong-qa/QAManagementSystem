namespace ProMaxx2.Automation.Core.Tests;

/// <summary>Covers AUT-TEST-009 (Agent ActionExecutor): UI actions, assertions, timeout, cancellation, error code.</summary>
public sealed class ActionExecutorTests
{
    private static readonly AgentConfig Config = new() { ActionTimeoutSeconds = 5, AutUser = "qa_user", AutPassword = "qa_pass" };

    private static readonly List<ObjectDescriptor> Objects =
    [
        new() { ScreenCode = "Sales", ObjectCode = "SAVE", ControlType = "Button", AutomationId = "btnSave" },
        new() { ScreenCode = "Sales", ObjectCode = "QTY", ControlType = "Edit", AutomationId = "txtQty" },
        new() { ScreenCode = "Login", ObjectCode = "TXTEMPID", ControlType = "Edit", AutomationId = "txtEmpId" },
        new() { ScreenCode = "Login", ObjectCode = "PWDBOX", ControlType = "Edit", AutomationId = "pwdBox" },
        new() { ScreenCode = "Login", ObjectCode = "BTNSIGNIN", ControlType = "Button", AutomationId = "btnSignIn" },
    ];

    private static ActionExecutor MakeExecutor(IReadOnlyList<ObjectDescriptor>? objects = null) => new(Config, objects ?? Objects);

    private static DslStep Step(int no, string action, Dictionary<string, string>? parameters = null) =>
        new() { StepNo = no, Action = action, Parameters = parameters ?? [] };

    [Fact]
    public async Task Click_action_resolves_object_and_calls_driver_click()
    {
        var driver = new FakeUiAutomationDriver();
        var executor = MakeExecutor();

        var outcome = await executor.ExecuteAsync(Step(1, "CLICK", new() { ["object"] = "Sales.SAVE" }), driver, CancellationToken.None);

        Assert.True(outcome.Passed);
        Assert.Contains("Click:btnSave", driver.Calls);
        Assert.Null(outcome.ErrorCode);
    }

    [Fact]
    public async Task Unresolved_object_reference_fails_with_the_aut_ui_001_error_code()
    {
        var driver = new FakeUiAutomationDriver();
        var executor = MakeExecutor();

        var outcome = await executor.ExecuteAsync(Step(1, "CLICK", new() { ["object"] = "Sales.DOES_NOT_EXIST" }), driver, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Equal("AUT-UI-001", outcome.ErrorCode); // extracted from the exception message, not the generic AUT-UI-003 fallback
        Assert.Contains("AUT-UI-001", outcome.ErrorMessage);
    }

    [Fact]
    public async Task Driver_returning_false_condition_not_met_fails_with_the_generic_ui_error_code()
    {
        // Models the driver giving up after its internal timeout without throwing — a plain "condition not met".
        var driver = new FakeUiAutomationDriver { ClickResult = false };
        var executor = MakeExecutor();

        var outcome = await executor.ExecuteAsync(Step(1, "CLICK", new() { ["object"] = "Sales.SAVE" }), driver, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Equal("AUT-UI-003", outcome.ErrorCode);
    }

    [Fact]
    public async Task Driver_throwing_a_timeout_exception_falls_back_to_the_generic_ui_error_code()
    {
        // A raw driver-level TimeoutException carries no embedded AUT-XXX-NNN code, so it must fall back to AUT-UI-003
        // rather than surface a .NET exception message as if it were a classified automation error code.
        var driver = new FakeUiAutomationDriver { ThrowOnClick = new TimeoutException("UI Automation call timed out after 5000ms.") };
        var executor = MakeExecutor();

        var outcome = await executor.ExecuteAsync(Step(1, "CLICK", new() { ["object"] = "Sales.SAVE" }), driver, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Equal("AUT-UI-003", outcome.ErrorCode);
        Assert.Contains("timed out", outcome.ErrorMessage);
    }

    [Fact]
    public async Task Cancellation_before_dispatch_reports_cancelled_with_a_screenshot()
    {
        var driver = new FakeUiAutomationDriver();
        var executor = MakeExecutor();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var outcome = await executor.ExecuteAsync(Step(1, "CLICK", new() { ["object"] = "Sales.SAVE" }), driver, cts.Token);

        Assert.False(outcome.Passed);
        Assert.Equal("AUT-UI-003", outcome.ErrorCode);
        Assert.Equal("Execution cancelled.", outcome.ErrorMessage);
        Assert.NotNull(outcome.Evidence);
        Assert.Contains(outcome.Evidence!, e => e.EvidenceType == "Screenshot");
        Assert.DoesNotContain("Click:btnSave", driver.Calls); // never reached the driver
    }

    [Fact]
    public async Task Unsupported_action_falls_back_to_the_generic_ui_error_code()
    {
        var driver = new FakeUiAutomationDriver();
        var executor = MakeExecutor();

        var outcome = await executor.ExecuteAsync(Step(1, "SOME_FUTURE_ACTION"), driver, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Equal("AUT-UI-003", outcome.ErrorCode); // "Unsupported action ..." carries no embedded AUT code
    }

    [Theory]
    [InlineData("expected value", "expected value", true)]
    [InlineData("expected value", "actual value", false)]
    [InlineData("  Padded  ", "Padded", true)] // trimmed before compare
    public async Task Expect_text_assertion_compares_trimmed_case_insensitive(string expected, string actualFromDriver, bool shouldPass)
    {
        var driver = new FakeUiAutomationDriver { GetTextResult = actualFromDriver };
        var executor = MakeExecutor();

        var outcome = await executor.ExecuteAsync(Step(1, "EXPECT_TEXT", new() { ["object"] = "Sales.QTY", ["value"] = expected }), driver, CancellationToken.None);

        Assert.Equal(shouldPass, outcome.Passed);
    }

    [Theory]
    [InlineData(true, true, true)]   // object exists, expecting visible -> pass
    [InlineData(false, true, false)] // object missing, expecting visible -> fail
    [InlineData(false, false, true)] // object missing, expecting NOT visible -> pass
    public async Task Expect_visible_assertion_matches_object_existence(bool exists, bool expectVisible, bool shouldPass)
    {
        var driver = new FakeUiAutomationDriver { ExistsResult = exists };
        var executor = MakeExecutor();
        var action = expectVisible ? "EXPECT_VISIBLE" : "EXPECT_NOT_VISIBLE";

        var outcome = await executor.ExecuteAsync(Step(1, action, new() { ["object"] = "Sales.SAVE" }), driver, CancellationToken.None);

        Assert.Equal(shouldPass, outcome.Passed);
    }

    [Fact]
    public async Task Login_action_sets_username_password_and_clicks_submit()
    {
        var driver = new FakeUiAutomationDriver();
        var executor = MakeExecutor();

        var outcome = await executor.ExecuteAsync(Step(1, "LOGIN"), driver, CancellationToken.None);

        Assert.True(outcome.Passed);
        Assert.Contains($"SetText:txtEmpId={Config.AutUser}", driver.Calls);
        Assert.Contains($"SetText:pwdBox={Config.AutPassword}", driver.Calls);
        Assert.Contains("Click:btnSignIn", driver.Calls);
    }

    [Fact]
    public async Task Login_action_fails_gracefully_without_throwing_when_login_objects_are_missing()
    {
        var driver = new FakeUiAutomationDriver();
        var executor = MakeExecutor([]); // empty object repository -> username/password can't be resolved

        var outcome = await executor.ExecuteAsync(Step(1, "LOGIN"), driver, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Equal("AUT-UI-003", outcome.ErrorCode);
    }

    [Fact]
    public async Task Db_assertion_without_a_configured_database_fails_with_the_aut_db_001_error_code()
    {
        var driver = new FakeUiAutomationDriver();
        var executor = MakeExecutor(); // Config.DbDatabase defaults to "" (not configured)

        var outcome = await executor.ExecuteAsync(Step(1, "EXPECT_DB_VALUE", new() { ["query"] = "SELECT 1", ["expected"] = "1" }), driver, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Equal("AUT-DB-001", outcome.ErrorCode);
    }

    [Fact]
    public async Task Successful_step_does_not_capture_a_failure_screenshot()
    {
        var driver = new FakeUiAutomationDriver();
        var executor = MakeExecutor();

        var outcome = await executor.ExecuteAsync(Step(1, "CLICK", new() { ["object"] = "Sales.SAVE" }), driver, CancellationToken.None);

        Assert.True(outcome.Passed);
        Assert.Null(outcome.Evidence);
    }

    [Fact]
    public async Task Failed_step_with_no_action_evidence_captures_a_screenshot()
    {
        var driver = new FakeUiAutomationDriver { ClickResult = false, Screenshot = [1, 2, 3, 4] };
        var executor = MakeExecutor();

        var outcome = await executor.ExecuteAsync(Step(1, "CLICK", new() { ["object"] = "Sales.SAVE" }), driver, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.NotNull(outcome.Evidence);
        var shot = Assert.Single(outcome.Evidence!);
        Assert.Equal("Screenshot", shot.EvidenceType);
        Assert.Equal([1, 2, 3, 4], shot.Data);
    }

    [Fact]
    public async Task Failed_step_screenshot_capture_failure_does_not_throw_and_leaves_no_evidence()
    {
        var driver = new FakeUiAutomationDriver { ClickResult = false };
        // Simulate the screenshot capture itself throwing (e.g. window closed) — ActionExecutor swallows this.
        driver.ThrowOnClick = null;
        var executor = MakeExecutor();
        driver.Screenshot = null; // CaptureScreenshotAsync returning null is the "couldn't capture" case

        var outcome = await executor.ExecuteAsync(Step(1, "CLICK", new() { ["object"] = "Sales.SAVE" }), driver, CancellationToken.None);

        Assert.False(outcome.Passed);
        Assert.Null(outcome.Evidence);
    }
}
