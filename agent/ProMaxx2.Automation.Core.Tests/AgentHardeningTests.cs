using Xunit;

namespace ProMaxx2.Automation.Core.Tests;

/// <summary>AUT-AGT-003/004: secret handling, Hub URL policy, desktop lock และ gbak credential</summary>
public sealed class AgentHardeningTests
{
    [Theory]
    [InlineData("https://api-promaxx2.qahub.store/api/v1", false)]
    [InlineData("http://localhost:5038/api/v1", false)]
    [InlineData("http://127.0.0.1:5038/api/v1", false)]
    [InlineData("http://192.168.200.219:5038/api/v1", true)]
    [InlineData("http://qahub.internal/api/v1", true)]
    [InlineData("ftp://hub/api", true)]
    [InlineData("not a url", true)]
    public void Hub_url_policy_rejects_plain_http_to_other_machines(string url, bool rejected)
        => Assert.Equal(rejected, HubUrlPolicy.Validate(url, allowInsecureHttp: false) is not null);

    [Fact]
    public void Plain_http_is_allowed_only_when_explicitly_opted_in()
    {
        Assert.Null(HubUrlPolicy.Validate("http://192.168.200.219:5038/api/v1", allowInsecureHttp: true));
        Assert.True(HubUrlPolicy.IsInsecureAllowed(k => k == HubUrlPolicy.AllowInsecureEnv ? "true" : null));
        Assert.False(HubUrlPolicy.IsInsecureAllowed(_ => null));
    }

    [Fact]
    public void Dpapi_protected_secret_takes_precedence_over_plaintext()
    {
        var env = new Dictionary<string, string?>
        {
            ["QAHUB_PASSWORD_DPAPI"] = AgentSecrets.Protect("s3cret-ทดสอบ"),
            ["QAHUB_PASSWORD"] = "old-plaintext",
        };
        Assert.Equal("s3cret-ทดสอบ", AgentSecrets.Read("QAHUB_PASSWORD", k => env.GetValueOrDefault(k)));
    }

    [Fact]
    public void Plaintext_secret_is_still_read_when_no_protected_value_exists()
        => Assert.Equal("plain", AgentSecrets.Read("AUT_PASSWORD", k => k == "AUT_PASSWORD" ? "plain" : null));

    [Fact]
    public void Undecryptable_secret_fails_loudly_instead_of_returning_garbage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AgentSecrets.Read("AUT_PASSWORD", k => k == "AUT_PASSWORD_DPAPI" ? "01000000D08C9DDF0115D1118C7A00C04FC297EB" : null));
        Assert.Contains("set-agent-env.ps1", ex.Message);
    }

    [Fact]
    public void Only_one_runner_can_hold_the_desktop_at_a_time()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ui-lock-test-{Guid.NewGuid():N}.lock");
        try
        {
            using (var first = UiSessionLock.TryAcquire(path))
            {
                Assert.NotNull(first);
                Assert.Null(UiSessionLock.TryAcquire(path));
            }
            using var again = UiSessionLock.TryAcquire(path);
            Assert.NotNull(again);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Gbak_receives_credentials_through_environment_not_command_line()
    {
        var profile = new DbProfile(DbKind.Firebird, "127.0.0.1", 3050, "SYSDBA", "masterkey-secret", "C:/db/FBMAXX2.FDB");
        var psi = DatabaseSnapshotService.CreateGbakStartInfo("gbak", profile, "-backup", "127.0.0.1/3050:C:/db/FBMAXX2.FDB", "out.fbk");

        Assert.DoesNotContain("masterkey-secret", psi.ArgumentList);
        Assert.DoesNotContain("-password", psi.ArgumentList);
        Assert.Equal(new[] { "-backup", "127.0.0.1/3050:C:/db/FBMAXX2.FDB", "out.fbk" }, psi.ArgumentList);
        Assert.Equal("SYSDBA", psi.Environment["ISC_USER"]);
        Assert.Equal("masterkey-secret", psi.Environment["ISC_PASSWORD"]);
    }

    [Fact]
    public void Finding_instances_of_an_exe_that_is_not_running_returns_nothing()
        => Assert.Empty(AutProcess.FindInstances(Path.Combine(Path.GetTempPath(), $"not-running-{Guid.NewGuid():N}.exe")));
}
