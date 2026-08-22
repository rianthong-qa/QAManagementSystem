using FlaUI.Core;
using Promaxx2.Automation.Core;
using Promaxx2.Automation.Pages.Pos;
using Promaxx2.Automation.Tests;

namespace Promaxx2.Automation.Tests;

/// <summary>
/// Smoke: Login บน PromaxxsPos.exe
/// Selectors verified at runtime against 1.0.0-beta.2 (SELECTOR_CONTRACT.md §4)
/// </summary>
public sealed class LoginSmoke : SmokeCaseBase
{
    public override string TestCaseCode => "TC-LOGIN-001";
    public override string TargetApp => "pos";

    protected override async Task RunCoreAsync(CaseContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.Config.PosUsername) ||
            string.IsNullOrWhiteSpace(ctx.Config.PosPassword))
            throw new InvalidOperationException(
                "Set AUT_POS_USERNAME and AUT_POS_PASSWORD for TC-LOGIN-001.");

        using var page = new LoginPage(ctx.App
            ?? throw new InvalidOperationException("Unable to attach to PromaxxsPos."));
        page.WaitUntilReady();
        page.TypeUsername(ctx.Config.PosUsername);
        page.TypePassword(ctx.Config.PosPassword);
        page.ClickLogin();

        if (!await page.WaitUntilSignedInAsync(TimeSpan.FromSeconds(30)))
        {
            var toast = page.GetToastMessage();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(toast)
                ? "Login overlay remained visible after sign-in."
                : $"POS rejected sign-in: {toast}");
        }
    }
}

/// <summary>
/// Login PageObject skeleton
/// </summary>
public class LoginPage : PosPageBase
{
    private const string LoginOverlayId = "LoginOverlay";
    private const string UserFieldId = "TxtEmpId";
    private const string PasswordFieldId = "PwdBox";
    private const string LoginButtonId = "BtnSignIn";
    private const string ToastTextId = "ToastText";

    public LoginPage(Application app) : base(app) { }

    public void WaitUntilReady()
    {
        if (!Exists(LoginOverlayId, 30_000))
            throw new TimeoutException("POS login overlay was not ready within 30 seconds.");
        if (!Exists(UserFieldId) || !Exists(PasswordFieldId) || !Exists(LoginButtonId))
            throw new InvalidOperationException("POS login controls do not match the selector contract.");
    }

    public void TypeUsername(string username) => TypeText(UserFieldId, username);
    public void TypePassword(string password) => TypeText(PasswordFieldId, password);
    public void ClickLogin() => Click(LoginButtonId);
    public string? GetToastMessage() => GetText(ToastTextId);

    public async Task<bool> WaitUntilSignedInAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (!Exists(LoginOverlayId, 250)) return true;
            await Task.Delay(250);
        }
        return false;
    }
}
