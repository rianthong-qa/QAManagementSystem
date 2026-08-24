namespace ProMaxx2.Automation.Core;

public interface IActionContext
{
    AgentConfig Config { get; }
    IUiAutomationDriver Driver { get; }
    ObjectDescriptor? Resolve(string? businessKey);
    CancellationToken Cancellation { get; }
    List<EvidenceAttachment> Evidence { get; }
}

public sealed class ActionContext(AgentConfig config, IUiAutomationDriver driver, IReadOnlyList<ObjectDescriptor> objects, CancellationToken cancellation) : IActionContext
{
    public AgentConfig Config => config;
    public IUiAutomationDriver Driver => driver;
    public CancellationToken Cancellation => cancellation;
    public List<EvidenceAttachment> Evidence { get; } = [];
    public ObjectDescriptor? Resolve(string? businessKey)
    {
        if (string.IsNullOrWhiteSpace(businessKey)) return null;
        var key = businessKey.Trim();
        return objects.FirstOrDefault(o => o.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ActionExecutor
{
    private readonly AgentConfig _config;
    private readonly IReadOnlyList<ObjectDescriptor> _objects;

    public ActionExecutor(AgentConfig config, IReadOnlyList<ObjectDescriptor> objects)
    {
        _config = config;
        _objects = objects;
    }

    public async Task<StepOutcome> ExecuteAsync(DslStep step, IUiAutomationDriver driver, CancellationToken cancellation)
    {
        var startedAt = DateTime.UtcNow;
        var ctx = new ActionContext(_config, driver, _objects, cancellation);
        try
        {
            var action = step.Action.Trim().ToUpperInvariant();
            cancellation.ThrowIfCancellationRequested();
            var passed = await DispatchAsync(action, step, ctx);
            var evidence = ctx.Evidence.Count > 0 ? ctx.Evidence.ToList() : null;
            if (!passed && evidence is null)
            {
                var shot = await CaptureFailScreenshot(driver);
                if (shot is not null) evidence = [new EvidenceAttachment(shot, $"step{step.StepNo}.png", "Screenshot")];
            }
            return new StepOutcome(passed, passed ? $"Action {action} completed." : null, passed ? null : "AUT-UI-003", passed ? null : $"Action {action} did not meet the expected condition.", null, startedAt, DateTime.UtcNow, evidence);
        }
        catch (OperationCanceledException)
        {
            return new StepOutcome(false, null, "AUT-UI-003", "Execution cancelled.", null, startedAt, DateTime.UtcNow, [new EvidenceAttachment(await CaptureFailScreenshot(driver) ?? [], $"step{step.StepNo}.png", "Screenshot")]);
        }
        catch (Exception ex)
        {
            var shot = await CaptureFailScreenshot(driver);
            var evidence = shot is not null ? (IReadOnlyList<EvidenceAttachment>)[new EvidenceAttachment(shot, $"step{step.StepNo}.png", "Screenshot")] : null;
            return new StepOutcome(false, null, "AUT-UI-003", $"{step.Action}: {ex.Message}", null, startedAt, DateTime.UtcNow, evidence);
        }
    }

    private async Task<bool> DispatchAsync(string action, DslStep step, IActionContext ctx) => action switch
    {
        "LOGIN" => await LoginAsync(step, ctx),
        "OPEN_MENU" => await OpenMenuAsync(step, ctx),
        "OPEN_SCREEN" => await ClickObjectAsync(step, ctx, fallback: null),
        "CLOSE_SCREEN" => await ClickObjectAsync(step, ctx, fallback: null),
        "NEW_DOCUMENT" => await ClickObjectAsync(step, ctx, fallback: "Document.New"),
        "SEARCH_DOCUMENT" => await ClickObjectAsync(step, ctx, fallback: "Document.Search"),
        "SAVE_DOCUMENT" => await SaveDocumentAsync(step, ctx),
        "APPROVE_DOCUMENT" => await ClickObjectAsync(step, ctx, fallback: "Document.Approve"),
        "CANCEL_DOCUMENT" => await ClickObjectAsync(step, ctx, fallback: "Document.Cancel"),
        "DELETE_DOCUMENT" => await ClickObjectAsync(step, ctx, fallback: "Document.Delete"),
        "SELECT_ITEM" => await SelectItemAsync(step, ctx),
        "SET_QTY" or "SET_PRICE" or "SET_DISCOUNT" or "SET_LOT" => await SetFieldAsync(step, ctx),
        "REMOVE_ITEM" => await ClickObjectAsync(step, ctx, fallback: "Item.Remove"),
        "CLICK" => await ClickObjectAsync(step, ctx, fallback: null),
        "SET_TEXT" => await SetTextFieldAsync(step, ctx),
        "SELECT_COMBO" => await SelectComboAsync(step, ctx),
        "CHECK" => await ToggleAsync(step, ctx, true),
        "UNCHECK" => await ToggleAsync(step, ctx, false),
        "PRESS_KEY" => await PressKeyAsync(step, ctx),
        "WAIT_OBJECT" => await WaitObjectAsync(step, ctx),
        "WAIT_SCREEN" => await WaitScreenAsync(step, ctx),
        "EXPECT_MESSAGE" => await ExpectMessageAsync(step, ctx),
        "EXPECT_TEXT" or "EXPECT_VALUE" => await ExpectTextAsync(step, ctx),
        "EXPECT_VISIBLE" => await ExpectVisibleAsync(step, ctx, true),
        "EXPECT_NOT_VISIBLE" => await ExpectVisibleAsync(step, ctx, false),
        "EXPECT_ENABLED" => await ExpectEnabledAsync(step, ctx),
        "EXPECT_DISABLED" => await ExpectEnabledAsync(step, ctx, expectDisabled: true),
        "EXPECT_DB_VALUE" => await ExpectDbValueAsync(step, ctx),
        "EXPECT_DB_ROW_COUNT" => await ExpectDbRowCountAsync(step, ctx),
        "EXPECT_STOCK" => await ExpectStockAsync(step, ctx),
        "EXPECT_LOT" => await ExpectLotAsync(step, ctx),
        "EXPECT_TRANSACTION" => await ExpectTransactionAsync(step, ctx),
        _ => throw new InvalidOperationException($"Unsupported action '{action}' in this agent version."),
    };

    private async Task<bool> LoginAsync(DslStep step, IActionContext ctx)
    {
        var username = ctx.Resolve("Login.TxtEmpId") ?? ctx.Resolve("Login.TxtUsername") ?? ctx.Resolve("Login.Username") ?? ctx.Resolve("Login.User");
        var password = ctx.Resolve("Login.PwdBox") ?? ctx.Resolve("Login.Password");
        var submit = ctx.Resolve("Login.BtnSignIn") ?? ctx.Resolve("Login.Submit") ?? ctx.Resolve("Login.LoginButton");
        if (username is null || password is null) return false;
        if (!await ctx.Driver.SetTextAsync(username.AutomationId!, username.ControlType, ctx.Config.AutUser, ctx.Config.ActionTimeout)) return false;
        if (!await ctx.Driver.SetTextAsync(password.AutomationId!, password.ControlType, ctx.Config.AutPassword, ctx.Config.ActionTimeout)) return false;
        if (submit is not null) return await ctx.Driver.ClickAsync(submit.AutomationId!, submit.ControlType, ctx.Config.ActionTimeout);
        return await ctx.Driver.PressKeyAsync("{Enter}");
    }

    private async Task<bool> OpenMenuAsync(DslStep step, IActionContext ctx)
    {
        var menu = step.Parameters.GetValueOrDefault("menu");
        var obj = ctx.Resolve($"Menu.{menu}") ?? ctx.Resolve(menu);
        if (obj is not null) return await ctx.Driver.ClickAsync(obj.AutomationId!, obj.ControlType, ctx.Config.ActionTimeout);
        if (!string.IsNullOrWhiteSpace(menu)) return await ctx.Driver.PressKeyAsync($"%{menu.Substring(0, 1)}");
        return false;
    }

    private async Task<bool> ClickObjectAsync(DslStep step, IActionContext ctx, string? fallback)
    {
        var obj = ResolveRequired(step, ctx, fallback);
        if (obj is null) return false;
        return await ctx.Driver.ClickAsync(obj.AutomationId!, obj.ControlType, ctx.Config.ActionTimeout);
    }

    private async Task<bool> SaveDocumentAsync(DslStep step, IActionContext ctx)
    {
        var save = ctx.Resolve("Document.Save") ?? ctx.Resolve("Sales.Save");
        if (save is not null)
        {
            if (!await ctx.Driver.ClickAsync(save.AutomationId!, save.ControlType, ctx.Config.ActionTimeout)) return false;
            await Task.Delay(TimeSpan.FromMilliseconds(400), ctx.Cancellation);
            return true;
        }
        return await ctx.Driver.PressKeyAsync("{F8}");
    }

    private async Task<bool> SelectItemAsync(DslStep step, IActionContext ctx)
    {
        var code = step.Parameters.GetValueOrDefault("itemCode") ?? step.Parameters.GetValueOrDefault("code");
        var field = ctx.Resolve("Item.Code") ?? ctx.Resolve("Item.ItemCode") ?? ctx.Resolve("Sales.ItemCode") ?? ctx.Resolve("Sales.ScanCodeBox");
        if (field is null || string.IsNullOrWhiteSpace(code)) return false;
        if (!await ctx.Driver.SetTextAsync(field.AutomationId!, field.ControlType, code, ctx.Config.ActionTimeout)) return false;
        return await ctx.Driver.PressKeyAsync("{Enter}");
    }

    private async Task<bool> SetFieldAsync(DslStep step, IActionContext ctx)
    {
        var value = step.Parameters.GetValueOrDefault("value");
        var field = ctx.Resolve(step.Parameters.GetValueOrDefault("object"));
        if (field is null || string.IsNullOrWhiteSpace(value)) return false;
        return await ctx.Driver.SetTextAsync(field.AutomationId!, field.ControlType, value, ctx.Config.ActionTimeout);
    }

    private async Task<bool> SetTextFieldAsync(DslStep step, IActionContext ctx)
    {
        var value = step.Parameters.GetValueOrDefault("value");
        var obj = ResolveRequired(step, ctx, null);
        if (obj is null || value is null) return false;
        return await ctx.Driver.SetTextAsync(obj.AutomationId!, obj.ControlType, value, ctx.Config.ActionTimeout);
    }

    private async Task<bool> SelectComboAsync(DslStep step, IActionContext ctx)
    {
        var value = step.Parameters.GetValueOrDefault("value");
        var obj = ResolveRequired(step, ctx, null);
        if (obj is null || value is null) return false;
        return await ctx.Driver.SelectComboAsync(obj.AutomationId!, value, ctx.Config.ActionTimeout);
    }

    private async Task<bool> ToggleAsync(DslStep step, IActionContext ctx, bool check)
    {
        var obj = ResolveRequired(step, ctx, null);
        if (obj is null) return false;
        return await ctx.Driver.ToggleAsync(obj.AutomationId!, check, ctx.Config.ActionTimeout);
    }

    private async Task<bool> PressKeyAsync(DslStep step, IActionContext ctx)
    {
        var key = step.Parameters.GetValueOrDefault("key");
        if (string.IsNullOrWhiteSpace(key)) return false;
        return await ctx.Driver.PressKeyAsync(key);
    }

    private async Task<bool> WaitObjectAsync(DslStep step, IActionContext ctx)
    {
        var obj = ResolveRequired(step, ctx, null);
        if (obj is null) return false;
        return await ctx.Driver.ExistsAsync(obj.AutomationId!, obj.ControlType, ctx.Config.ActionTimeout);
    }

    private Task<bool> WaitScreenAsync(DslStep step, IActionContext ctx)
    {
        var screen = step.Parameters.GetValueOrDefault("screen");
        var window = ctx.Resolve(screen)?.ScreenCode ?? screen;
        if (string.IsNullOrWhiteSpace(window)) return Task.FromResult(false);
        return ctx.Driver.WaitForMainWindowAsync(_config.MachineName, ctx.Config.ActionTimeout);
    }

    private async Task<bool> ExpectMessageAsync(DslStep step, IActionContext ctx)
    {
        var key = step.Parameters.GetValueOrDefault("messageKey") ?? step.Parameters.GetValueOrDefault("key");
        if (string.IsNullOrWhiteSpace(key)) return false;
        return await ctx.Driver.ExpectMessageAsync(key, ctx.Config.ActionTimeout);
    }

    private async Task<bool> ExpectTextAsync(DslStep step, IActionContext ctx)
    {
        var expected = step.Parameters.GetValueOrDefault("value");
        var obj = ResolveRequired(step, ctx, null);
        if (obj is null || expected is null) return false;
        var actual = await ctx.Driver.GetTextAsync(obj.AutomationId!, obj.ControlType, ctx.Config.ActionTimeout);
        return string.Equals(actual?.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> ExpectVisibleAsync(DslStep step, IActionContext ctx, bool visible)
    {
        var obj = ResolveRequired(step, ctx, null);
        if (obj is null) return false;
        var exists = await ctx.Driver.ExistsAsync(obj.AutomationId!, obj.ControlType, ctx.Config.ActionTimeout);
        return exists == visible;
    }

    private async Task<bool> ExpectEnabledAsync(DslStep step, IActionContext ctx, bool expectDisabled = false)
    {
        var obj = ResolveRequired(step, ctx, null);
        if (obj is null) return false;
        var exists = await ctx.Driver.ExistsAsync(obj.AutomationId!, obj.ControlType, ctx.Config.ActionTimeout);
        return expectDisabled ? !exists : exists;
    }

    private ObjectDescriptor? ResolveRequired(DslStep step, IActionContext ctx, string? fallback)
    {
        var key = step.Parameters.GetValueOrDefault("object");
        var obj = ctx.Resolve(key) ?? (fallback is not null ? ctx.Resolve(fallback) : null);
        if (obj is null) throw new InvalidOperationException($"Object '{key ?? fallback}' not found in Object Repository (AUT-UI-001).");
        return obj;
    }

    private async Task<bool> ExpectDbValueAsync(DslStep step, IActionContext ctx)
    {
        var query = step.Parameters.GetValueOrDefault("query");
        var expected = step.Parameters.GetValueOrDefault("expected");
        if (string.IsNullOrWhiteSpace(query) || expected is null) return false;
        return await RunDbAssertionAsync(step, ctx, query, expected, step.Parameters.GetValueOrDefault("column"), step.Parameters.GetValueOrDefault("parameters"));
    }

    private async Task<bool> ExpectDbRowCountAsync(DslStep step, IActionContext ctx)
    {
        var query = step.Parameters.GetValueOrDefault("query");
        var expected = step.Parameters.GetValueOrDefault("expected");
        if (string.IsNullOrWhiteSpace(query) || expected is null) return false;
        return await RunDbAssertionAsync(step, ctx, $"SELECT COUNT(*) FROM ({query})", expected, null, null);
    }

    private async Task<bool> ExpectStockAsync(DslStep step, IActionContext ctx)
    {
        var code = step.Parameters.GetValueOrDefault("itemCode") ?? step.Parameters.GetValueOrDefault("code");
        var expected = step.Parameters.GetValueOrDefault("expected");
        if (string.IsNullOrWhiteSpace(code) || expected is null) return false;
        var query = "SELECT COALESCE(SUM(f.LOTQUANTITY),0) - COALESCE(SUM(f.LOTOUTQUANTITY),0) AS STOCK FROM CALCFIFO f JOIN ITEMBARCODE b ON b.SYSITEMID = f.SYSITEMID WHERE b.BARCODE = @code";
        return await RunDbAssertionAsync(step, ctx, query, expected, "STOCK", System.Text.Json.JsonSerializer.Serialize(new { code }));
    }

    private async Task<bool> ExpectLotAsync(DslStep step, IActionContext ctx)
    {
        var code = step.Parameters.GetValueOrDefault("itemCode") ?? step.Parameters.GetValueOrDefault("code");
        var lot = step.Parameters.GetValueOrDefault("lotNo");
        var expected = step.Parameters.GetValueOrDefault("expected");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(lot) || expected is null) return false;
        var query = "SELECT COUNT(*) FROM CALCLOTNO c JOIN ITEMBARCODE b ON b.SYSITEMID = c.SYSITEMID WHERE b.BARCODE = @code AND c.LOTNO = @lotNo";
        return await RunDbAssertionAsync(step, ctx, query, expected, null, System.Text.Json.JsonSerializer.Serialize(new { code, lotNo = lot }));
    }

    private async Task<bool> ExpectTransactionAsync(DslStep step, IActionContext ctx)
    {
        var transNo = step.Parameters.GetValueOrDefault("transNo") ?? step.Parameters.GetValueOrDefault("no");
        var expected = step.Parameters.GetValueOrDefault("expected") ?? "1";
        if (string.IsNullOrWhiteSpace(transNo)) return false;
        var query = "SELECT COUNT(*) FROM TRANS WHERE TRANNO = @transNo";
        return await RunDbAssertionAsync(step, ctx, query, expected, null, System.Text.Json.JsonSerializer.Serialize(new { transNo }));
    }

    private async Task<bool> RunDbAssertionAsync(DslStep step, IActionContext ctx, string query, string expected, string? column, string? parametersJson)
    {
        var profile = DbProfile.FromEnvironment(ctx.Config);
        if (string.IsNullOrWhiteSpace(profile.Database)) throw new InvalidOperationException("AUT_DB_DATABASE is not configured (AUT-DB-001).");
        var parameters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(parametersJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(parametersJson);
                foreach (var prop in doc.RootElement.EnumerateObject()) parameters[prop.Name] = prop.Value.ToString() ?? "";
            }
            catch { }
        }
        var validator = DbValidatorFactory.Create(profile.Kind);
        var result = await validator.ValidateAsync(new DbValidationRequest(profile, query, parameters, expected, column), ctx.Cancellation);
        var sqlResult = new
        {
            step = step.StepNo,
            action = step.Action,
            query = result.Query,
            parameters,
            expected,
            actual = result.ActualValue,
            passed = result.Passed,
            elapsedMs = result.ElapsedMs,
            error = result.Error,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(sqlResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        ctx.Evidence.Add(new EvidenceAttachment(System.Text.Encoding.UTF8.GetBytes(json), $"sql_{step.StepNo}.json", "SqlResult"));
        if (!result.Passed && result.Error is not null) throw new InvalidOperationException($"DB validation error: {result.Error} (AUT-DB-002).");
        return result.Passed;
    }

    private static async Task<byte[]?> CaptureFailScreenshot(IUiAutomationDriver driver)
    {
        try { return await driver.CaptureScreenshotAsync(); }
        catch { return null; }
    }
}