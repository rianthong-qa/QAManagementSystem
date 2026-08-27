namespace ProMaxx2.Automation.Core.Tests;

/// <summary>
/// Covers AUT-TEST-010 (Database Validator): Firebird/SQL Server, parameter binding, timeout, query failure.
/// <see cref="FirebirdDbValidator"/>/<see cref="SqlServerDbValidator"/> instantiate a concrete ADO.NET connection
/// type directly (no injectable connection factory), so there is no seam to swap in a fake connection for testing
/// query results — the real DB-facing exercise here is a connection failure against a closed local port, which
/// fails fast (TCP RST plus, since AUT-P0-013, an explicit <see cref="DbProfile.ConnectTimeoutSeconds"/> cap) and
/// exercises the real catch-all error path end to end for both providers. The provider-agnostic comparison and
/// parameter-naming logic was extracted into <see cref="DbAssertionComparer"/> specifically so it's unit testable
/// without any connection at all. Not covered: actual query execution/parameter binding against a live database, and
/// genuine mid-query cancellation (AUT-P0-013 made both providers pass the CancellationToken through to
/// OpenAsync/ExecuteReaderAsync/ReadAsync, but proving it actually interrupts a slow query needs a real DB).
/// </summary>
public sealed class DatabaseValidatorTests
{
    [Theory]
    [InlineData(">=", "10", "5", true)]
    [InlineData(">=", "5", "10", false)]
    [InlineData("<=", "5", "10", true)]
    [InlineData("<=", "10", "5", false)]
    [InlineData("!=", "5", "10", true)]
    [InlineData("!=", "5", "5", false)]
    [InlineData(">", "10", "5", true)]
    [InlineData("<", "5", "10", true)]
    [InlineData("=", "5", "5", true)]
    public void Compare_applies_the_leading_operator_against_numeric_values(string op, string actual, string expectedOperand, bool expectedResult)
    {
        Assert.Equal(expectedResult, DbAssertionComparer.Compare(actual, op + expectedOperand));
    }

    [Fact]
    public void Compare_with_an_operator_against_a_non_numeric_operand_falls_back_to_string_comparison()
    {
        Assert.True(DbAssertionComparer.Compare("Active", ">=Active"));
        Assert.False(DbAssertionComparer.Compare("Active", ">=Closed"));
    }

    [Theory]
    [InlineData("100", "100", true)]
    [InlineData("100", "100.0", true)] // numeric compare, not string-exact
    [InlineData("100", "99", false)]
    public void Compare_without_an_operator_does_numeric_equality_when_both_sides_parse(string actual, string expected, bool result)
    {
        Assert.Equal(result, DbAssertionComparer.Compare(actual, expected));
    }

    [Theory]
    [InlineData("1", "true", true)]
    [InlineData("true", "true", true)]
    [InlineData("0", "true", false)]
    [InlineData("0", "false", true)]
    [InlineData("false", "false", true)]
    [InlineData("1", "false", false)]
    public void Compare_handles_boolean_style_expected_values(string actual, string expected, bool result)
    {
        Assert.Equal(result, DbAssertionComparer.Compare(actual, expected));
    }

    [Theory]
    [InlineData("Ready", "ready", true)] // case-insensitive
    [InlineData("Ready", "Draft", false)]
    public void Compare_falls_back_to_case_insensitive_string_equality(string actual, string expected, bool result)
    {
        Assert.Equal(result, DbAssertionComparer.Compare(actual, expected));
    }

    [Theory]
    [InlineData("code", "@code")]
    [InlineData("@code", "@code")]
    [InlineData("itemCode", "@itemCode")]
    public void NormalizeParameterName_ensures_an_at_prefix_without_doubling_it(string input, string expected)
    {
        Assert.Equal(expected, DbAssertionComparer.NormalizeParameterName(input));
    }

    [Fact]
    public void Factory_creates_a_firebird_validator_for_firebird_kind()
    {
        Assert.IsType<FirebirdDbValidator>(DbValidatorFactory.Create(DbKind.Firebird));
    }

    [Fact]
    public void Factory_creates_a_sql_server_validator_for_sql_server_kind()
    {
        Assert.IsType<SqlServerDbValidator>(DbValidatorFactory.Create(DbKind.SqlServer));
    }

    [Fact]
    public async Task Firebird_validator_reports_a_failure_result_when_the_connection_is_refused()
    {
        // 127.0.0.1 with nothing listening -> the OS refuses the connection immediately (no real DB/network needed,
        // and no multi-second timeout wait, unlike an unreachable/black-holed host would cause).
        var profile = new DbProfile(DbKind.Firebird, "127.0.0.1", 1, "SYSDBA", "wrong", "nonexistent.fdb");
        var validator = new FirebirdDbValidator();
        var request = new DbValidationRequest(profile, "SELECT 1 FROM RDB$DATABASE", new Dictionary<string, string>(), "1");

        var result = await validator.ValidateAsync(request, CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("", result.ActualValue);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.True(result.ElapsedMs >= 0);
    }

    [Fact]
    public async Task Sql_server_validator_reports_a_failure_result_when_the_connection_is_refused()
    {
        // AUT-P0-013 fix: ConnectTimeoutSeconds now caps at 10s (was unset, defaulting to the ~15s provider
        // default and taking ~14s to fail here) so this is fast enough to run alongside the Firebird test above.
        var profile = new DbProfile(DbKind.SqlServer, "127.0.0.1", 1, "sa", "wrong", "nonexistent");
        var validator = new SqlServerDbValidator();
        var request = new DbValidationRequest(profile, "SELECT 1", new Dictionary<string, string>(), "1");

        var result = await validator.ValidateAsync(request, CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("", result.ActualValue);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Connect_timeout_is_capped_at_five_seconds_from_a_longer_action_timeout()
    {
        var profile = DbProfile.FromEnvironment(new AgentConfig { ActionTimeoutSeconds = 30 });
        Assert.Equal(5, profile.ConnectTimeoutSeconds);
    }

    [Fact]
    public void Connect_timeout_follows_a_shorter_action_timeout()
    {
        var profile = DbProfile.FromEnvironment(new AgentConfig { ActionTimeoutSeconds = 5 });
        Assert.Equal(5, profile.ConnectTimeoutSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Connect_timeout_never_drops_to_zero_or_below(int actionTimeoutSeconds)
    {
        // A connect timeout of 0 means "wait indefinitely" to both ADO.NET providers — the exact opposite of the
        // fail-fast intent — so a misconfigured (or programmatically zero/negative) ActionTimeoutSeconds must not
        // propagate through.
        var profile = DbProfile.FromEnvironment(new AgentConfig { ActionTimeoutSeconds = actionTimeoutSeconds });
        Assert.Equal(1, profile.ConnectTimeoutSeconds);
    }
}
