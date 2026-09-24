using ProMaxx2.Automation.Core;
using Xunit;

namespace ProMaxx2.Automation.Core.Tests;

/// <summary>AUT-SEC-004: ด่านที่สองบน Agent ต้องปฏิเสธ SQL ที่แก้ข้อมูลได้ แม้ Hub จะส่งมาใน DSL ก็ตาม</summary>
public sealed class DbAssertionSqlGuardTests
{
    [Theory]
    [InlineData("DELETE FROM ITEMS")]
    [InlineData("SELECT 1; DROP TABLE ITEMS")]
    [InlineData("SELECT * INTO BACKUP_ITEMS FROM ITEMS")]
    [InlineData("SELECT 1 /* x */")]
    [InlineData("EXECUTE BLOCK AS BEGIN DELETE FROM ITEMS; END")]
    public void Rejects_queries_that_can_change_data(string query)
        => Assert.False(DbAssertionSqlGuard.IsReadOnlySelect(query, out _));

    [Theory]
    [InlineData("SELECT COUNT(*) FROM ITEMS WHERE STATUS = 'DELETE'")]
    [InlineData("select qty from items where code = @code;")]
    public void Accepts_read_only_selects(string query)
        => Assert.True(DbAssertionSqlGuard.IsReadOnlySelect(query, out var reason), reason);

    [Fact]
    public void Trims_the_terminator_so_the_query_can_be_wrapped_as_a_derived_table()
        => Assert.Equal("SELECT 1 FROM RDB$DATABASE", DbAssertionSqlGuard.TrimTerminator(" SELECT 1 FROM RDB$DATABASE; "));

    [Fact]
    public async Task Validator_refuses_a_data_changing_query_before_connecting()
    {
        var profile = new DbProfile(DbKind.Firebird, "127.0.0.1", 1, "u", "p", "db");
        var result = await new FirebirdDbValidator().ValidateAsync(new DbValidationRequest(profile, "UPDATE ITEMS SET QTY = 0", new Dictionary<string, string>(), "0"), CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("AUT-DB-003", result.Error);
    }
}
