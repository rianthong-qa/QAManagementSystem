namespace ProMaxx2.Automation.Core.Tests;

/// <summary>
/// Covers AUT-TEST-010 (Database Validator): Firebird/SQL Server, parameter binding, timeout, query failure.
/// <see cref="FirebirdDbValidator"/>/<see cref="SqlServerDbValidator"/> instantiate a concrete ADO.NET connection
/// type directly (no injectable connection factory), so there is no seam to swap in a fake connection for testing
/// query results — the real DB-facing exercise here is a connection failure against a closed local port, which
/// fails fast (TCP RST) and exercises the real catch-all error path end to end. The provider-agnostic comparison
/// and parameter-naming logic was extracted into <see cref="DbAssertionComparer"/> specifically so it's unit
/// testable without any connection at all — see AUTOMATION_TODO.md for the AUT-TEST-010 note on what "timeout" does
/// NOT cover here (the CancellationToken passed to ValidateAsync is accepted but never actually observed).
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

    // No SQL Server equivalent of the connection-refused test above: Microsoft.Data.SqlClient does its own
    // retry/backoff around a refused TCP connection and takes ~14s to give up because SqlServerDbValidator never
    // sets ConnectTimeout on the connection string (defaults to 15s) — see the AUT-TEST-010 tracker note. Adding
    // that test here would make the whole suite noticeably slower for one assertion; SqlServerDbValidator's
    // catch(Exception) path is structurally identical to Firebird's (both wrap any connection/query exception into
    // the same DbValidationResult(false, "", query, ex.Message, elapsed) shape), so the Firebird test above already
    // covers that shape.
}
