namespace ProMaxx2.Automation.Core.Tests;

/// <summary>Covers AUT-DATA-001's agent-side backup service. Neither a real Firebird server nor SQL Server instance
/// is available in CI, so — same approach as <c>DatabaseValidatorTests</c>'s connection-refused tests — these cover
/// the deterministic, fast-failing paths (refused connection, missing tool) rather than a real backup. A real
/// gbak/BACKUP DATABASE run against live Firebird/SQL Server instances is field-tested by QA, same caveat already
/// recorded for AUT-P0-006 (Object Verification).</summary>
public sealed class DatabaseSnapshotServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "aut-snapshot-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Sql_server_snapshot_reports_a_failure_result_when_the_connection_is_refused()
    {
        // 127.0.0.1 with nothing listening -> refused immediately, same trick DatabaseValidatorTests uses.
        var profile = new DbProfile(DbKind.SqlServer, "127.0.0.1", 1, "sa", "wrong", "nonexistent");
        var service = new DatabaseSnapshotService();

        var result = await service.CreateSnapshotAsync(profile, _tempDir, "Env_Build1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Null(result.FilePath);
        Assert.Null(result.Checksum);
        Assert.True(result.ElapsedMs >= 0);
    }

    [Fact]
    public async Task Firebird_snapshot_reports_a_failure_result_when_gbak_cannot_be_started()
    {
        var profile = new DbProfile(DbKind.Firebird, "127.0.0.1", 3050, "SYSDBA", "wrong", "nonexistent.fdb");
        var service = new DatabaseSnapshotService(gbakPath: "gbak-does-not-exist-" + Guid.NewGuid().ToString("N"));

        var result = await service.CreateSnapshotAsync(profile, _tempDir, "Env_Build1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("gbak", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.FilePath);
    }

    [Fact]
    public async Task A_failed_snapshot_does_not_leave_a_partial_output_file_behind()
    {
        var profile = new DbProfile(DbKind.SqlServer, "127.0.0.1", 1, "sa", "wrong", "nonexistent");
        var service = new DatabaseSnapshotService();

        await service.CreateSnapshotAsync(profile, _tempDir, "Env_Build1", CancellationToken.None);

        Assert.True(!Directory.Exists(_tempDir) || Directory.EnumerateFiles(_tempDir).Count() == 0);
    }
}
