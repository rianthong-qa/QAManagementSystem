namespace ProMaxx2.Automation.Core.Tests;

/// <summary>Covers AUT-DATA-003's agent-side seed execution. No real Firebird/SQL Server instance is available in
/// CI, so — same approach as DatabaseValidatorTests/DatabaseSnapshotServiceTests — these cover the deterministic
/// connection-refused failure path for both providers.</summary>
public sealed class DatabaseSeedServiceTests
{
    private const string SampleSql = "INSERT INTO Products (Code, Name) VALUES ('P001', 'Test');";

    [Fact]
    public async Task Firebird_seed_reports_a_failure_result_when_the_connection_is_refused()
    {
        var profile = new DbProfile(DbKind.Firebird, "127.0.0.1", 1, "SYSDBA", "wrong", "nonexistent.fdb");
        var service = new DatabaseSeedService();

        var result = await service.RunSeedScriptAsync(profile, SampleSql, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.RowsAffected);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.True(result.ElapsedMs >= 0);
    }

    [Fact]
    public async Task Sql_server_seed_reports_a_failure_result_when_the_connection_is_refused()
    {
        var profile = new DbProfile(DbKind.SqlServer, "127.0.0.1", 1, "sa", "wrong", "nonexistent");
        var service = new DatabaseSeedService();

        var result = await service.RunSeedScriptAsync(profile, SampleSql, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.RowsAffected);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
