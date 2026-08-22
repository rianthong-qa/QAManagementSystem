using FlaUI.Core;
using Promaxx2.Automation.Core;
using Promaxx2.Automation.Pages.App;

namespace Promaxx2.Automation.Tests;

/// <summary>
/// Smoke: Login เข้า Promaxxs.App.exe สำหรับงาน Master Data
/// Selectors verified at runtime against 1.0.0-beta.2.
/// </summary>
public sealed class AppLoginSmoke : SmokeCaseBase
{
    public override string TestCaseCode => "TC-APP-LOGIN-001";
    public override string TargetApp => "app";

    protected override async Task RunCoreAsync(CaseContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.Config.AppUsername) ||
            string.IsNullOrWhiteSpace(ctx.Config.AppPassword))
            throw new InvalidOperationException(
                "Set AUT_APP_USERNAME and AUT_APP_PASSWORD for TC-APP-LOGIN-001.");

        using var page = new AppLoginPage(ctx.App
            ?? throw new InvalidOperationException("Unable to attach to Promaxxs.App."));
        page.WaitUntilReady();
        page.TypeUsername(ctx.Config.AppUsername);
        page.TypePassword(ctx.Config.AppPassword);
        page.ClickLogin();

        if (!await page.WaitUntilSignedInAsync(TimeSpan.FromSeconds(30)))
        {
            var toast = page.GetToastMessage();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(toast)
                ? "Master Data login panel remained visible after sign-in."
                : $"Promaxxs.App rejected sign-in: {toast}");
        }
    }
}

public sealed class AppLoginPage : AppPageBase
{
    private const string LoginPanelId = "LoginPanel";
    private const string UsernameFieldId = "TxtUsername";
    private const string PasswordFieldId = "PwdBox";
    private const string LoginButtonId = "BtnSignIn";
    private const string ToastTextId = "ToastText";

    public AppLoginPage(Application app) : base(app) { }

    public void WaitUntilReady()
    {
        if (!Exists(LoginPanelId, 30_000))
            throw new TimeoutException("Promaxxs.App login panel was not ready within 30 seconds.");
        if (!Exists(UsernameFieldId) || !Exists(PasswordFieldId) || !Exists(LoginButtonId))
            throw new InvalidOperationException("Promaxxs.App login controls do not match the selector contract.");
    }

    public void TypeUsername(string username) => TypeText(UsernameFieldId, username);
    public void TypePassword(string password) => TypeText(PasswordFieldId, password);
    public void ClickLogin() => Click(LoginButtonId);
    public string? GetToastMessage() => GetText(ToastTextId);

    public async Task<bool> WaitUntilSignedInAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (!Exists(LoginPanelId, 250)) return true;
            await Task.Delay(250);
        }
        return false;
    }
}
