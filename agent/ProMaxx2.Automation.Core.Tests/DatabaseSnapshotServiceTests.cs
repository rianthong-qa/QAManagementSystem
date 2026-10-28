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

    [Fact]
    public async Task Restore_reports_a_failure_with_no_checks_verified_when_the_backup_file_does_not_exist()
    {
        var profile = new DbProfile(DbKind.SqlServer, "127.0.0.1", 1, "sa", "wrong", "nonexistent");
        var service = new DatabaseSnapshotService();
        var missingPath = Path.Combine(_tempDir, "does-not-exist.bak");

        var result = await service.RestoreSnapshotAsync(profile, missingPath, "deadbeef", CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.ChecksumVerified);
        Assert.False(result.AvailabilityVerified);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Restore_reports_a_checksum_mismatch_without_ever_attempting_the_restore_command()
    {
        Directory.CreateDirectory(_tempDir);
        var backupPath = Path.Combine(_tempDir, "snapshot.bak");
        await File.WriteAllTextAsync(backupPath, "not the real backup content");
        // Deliberately bogus host/port: if the restore command were attempted despite the checksum mismatch, this
        // would fail for a *different* reason (connection refused) — the test would still fail, just misleadingly.
        var profile = new DbProfile(DbKind.SqlServer, "127.0.0.1", 1, "sa", "wrong", "nonexistent");
        var service = new DatabaseSnapshotService();

        var result = await service.RestoreSnapshotAsync(profile, backupPath, "0000000000000000000000000000000000000000000000000000000000000000", CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.ChecksumVerified);
        Assert.False(result.AvailabilityVerified);
        Assert.Contains("Checksum mismatch", result.Error);
    }

    [Fact]
    public async Task Restore_with_a_matching_checksum_but_a_refused_connection_reports_checksum_verified_and_availability_not()
    {
        Directory.CreateDirectory(_tempDir);
        var backupPath = Path.Combine(_tempDir, "snapshot.bak");
        var content = "fake backup bytes"u8.ToArray();
        await File.WriteAllBytesAsync(backupPath, content);
        var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
        var profile = new DbProfile(DbKind.SqlServer, "127.0.0.1", 1, "sa", "wrong", "nonexistent");
        var service = new DatabaseSnapshotService();

        var result = await service.RestoreSnapshotAsync(profile, backupPath, checksum, CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.ChecksumVerified); // the checksum step passed — failure happened after it
        Assert.False(result.AvailabilityVerified);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
