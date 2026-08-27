using System.Diagnostics;
using System.Security.Cryptography;

namespace ProMaxx2.Automation.Core;

/// <summary>AUT-DATA-001: the result of one snapshot attempt. <see cref="Success"/> false means the agent should
/// report "Failed" to the Hub with <see cref="Error"/> as the message; the caller never needs to catch an exception
/// from <see cref="IDbSnapshotService.CreateSnapshotAsync"/> — every failure mode (tool missing, connection refused,
/// backup command failed) is captured here instead, same shape as <see cref="DbValidationResult"/>.</summary>
public sealed record DbSnapshotResult(bool Success, string? FilePath, string? Checksum, long? SizeBytes, string? Error, long ElapsedMs);

/// <summary>AUT-DATA-002: the result of one restore attempt. <see cref="ChecksumVerified"/>/<see cref="AvailabilityVerified"/>
/// are reported individually (not just an overall Success flag) so a failure clearly shows which of the two checks —
/// or the restore command itself, in between them — is where it went wrong.</summary>
public sealed record DbRestoreResult(bool Success, bool ChecksumVerified, bool AvailabilityVerified, string? Error, long ElapsedMs);

public interface IDbSnapshotService
{
    Task<DbSnapshotResult> CreateSnapshotAsync(DbProfile profile, string outputDirectory, string fileNameHint, CancellationToken ct);

    /// <summary>AUT-DATA-002: restores <paramref name="profile"/>'s database from the backup file at
    /// <paramref name="snapshotPath"/>, verifying its checksum against <paramref name="expectedChecksum"/> first (the
    /// restore command is never even attempted on a mismatch) and the database's availability afterward.</summary>
    Task<DbRestoreResult> RestoreSnapshotAsync(DbProfile profile, string snapshotPath, string expectedChecksum, CancellationToken ct);
}

/// <summary>Takes a real backup of the DB an agent's local <see cref="DbProfile"/> talks to — Firebird via the
/// <c>gbak</c> command-line tool (shelled out to, since there is no managed API for it), SQL Server via a plain
/// <c>BACKUP DATABASE</c> statement (no external tool needed). Mirrors <see cref="FirebirdDbValidator"/>/
/// <see cref="SqlServerDbValidator"/>'s connection-building for consistency, but this is a one-shot operation, not a
/// query — there is no result set to read, just success/failure and the file it produced.</summary>
public sealed class DatabaseSnapshotService(string gbakPath = "gbak") : IDbSnapshotService
{
    public async Task<DbSnapshotResult> CreateSnapshotAsync(DbProfile profile, string outputDirectory, string fileNameHint, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var ext = profile.Kind == DbKind.Firebird ? ".fbk" : ".bak";
            var outputPath = Path.Combine(outputDirectory, $"{Sanitize(fileNameHint)}_{DateTime.UtcNow:yyyyMMddHHmmss}{ext}");

            if (profile.Kind == DbKind.Firebird) await RunGbakBackupAsync(profile, outputPath, ct);
            else await RunSqlServerBackupAsync(profile, outputPath, ct);

            if (!File.Exists(outputPath)) throw new InvalidOperationException("Backup command reported success but the output file was not found.");
            var checksum = await ComputeChecksumAsync(outputPath, ct);
            var sizeBytes = new FileInfo(outputPath).Length;
            sw.Stop();
            return new DbSnapshotResult(true, outputPath, checksum, sizeBytes, null, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new DbSnapshotResult(false, null, null, null, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    public async Task<DbRestoreResult> RestoreSnapshotAsync(DbProfile profile, string snapshotPath, string expectedChecksum, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!File.Exists(snapshotPath))
        {
            sw.Stop();
            return new DbRestoreResult(false, false, false, $"Backup file not found: {snapshotPath}", sw.ElapsedMilliseconds);
        }

        var actualChecksum = await ComputeChecksumAsync(snapshotPath, ct);
        if (!string.Equals(actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
        {
            sw.Stop();
            return new DbRestoreResult(false, false, false, "Checksum mismatch — the backup file may be corrupted or has been replaced since it was taken.", sw.ElapsedMilliseconds);
        }

        try
        {
            if (profile.Kind == DbKind.Firebird) await RunGbakRestoreAsync(profile, snapshotPath, ct);
            else await RunSqlServerRestoreAsync(profile, snapshotPath, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new DbRestoreResult(false, true, false, ex.Message, sw.ElapsedMilliseconds);
        }

        var available = await VerifyAvailabilityAsync(profile, ct);
        sw.Stop();
        return available
            ? new DbRestoreResult(true, true, true, null, sw.ElapsedMilliseconds)
            : new DbRestoreResult(false, true, false, "Restore command completed but the database did not respond to a basic availability check afterward.", sw.ElapsedMilliseconds);
    }

    private async Task RunGbakRestoreAsync(DbProfile profile, string snapshotPath, CancellationToken ct)
    {
        var target = $"{profile.Host}/{profile.Port}:{profile.Database}";
        var psi = new ProcessStartInfo
        {
            FileName = gbakPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // "-rep" (replace_database): create-and-overwrite in one step, since the target database already exists —
        // a plain "-c" (create) would fail on a database name that is already in use.
        foreach (var arg in new[] { "-rep", "-user", profile.User, "-password", profile.Password, snapshotPath, target }) psi.ArgumentList.Add(arg);

        Process process;
        try { process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the gbak process."); }
        catch (Exception ex) when (ex is not InvalidOperationException) { throw new InvalidOperationException($"Could not start gbak ('{gbakPath}'): {ex.Message}", ex); }
        using var _ = process;
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException($"gbak exited with code {process.ExitCode}: {(string.IsNullOrWhiteSpace(stderr) ? await stdoutTask : stderr)}");
        }
    }

    private static async Task RunSqlServerRestoreAsync(DbProfile profile, string snapshotPath, CancellationToken ct)
    {
        var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = $"{profile.Host},{profile.Port}",
            UserID = profile.User,
            Password = profile.Password,
            InitialCatalog = "master", // RESTORE DATABASE doesn't require connecting to the target DB itself either
            TrustServerCertificate = true,
            ConnectTimeout = profile.ConnectTimeoutSeconds,
            ConnectRetryCount = 0,
        }.ToString();
        using var con = new Microsoft.Data.SqlClient.SqlConnection(cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.CommandTimeout = 0; // same rationale as the backup command — rely on ct, not a fixed budget
        // Same escaping approach as RunSqlServerBackupAsync, for the same reason.
        var safeDatabase = profile.Database.Replace("]", "]]");
        var safePath = snapshotPath.Replace("'", "''");
        // WITH REPLACE overwrites the existing database. Note (documented limitation, not fixed here): if the
        // target database currently has other open connections, RESTORE DATABASE will fail until they are closed —
        // there is no automatic "kick everyone off" step, since forcibly disconnecting other sessions is a
        // destructive action this agent should not take unilaterally.
        cmd.CommandText = $"RESTORE DATABASE [{safeDatabase}] FROM DISK = N'{safePath}' WITH REPLACE, STATS = 10;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>AUT-DATA-002's "ยืนยันความพร้อมใช้งาน" (availability) check — a trivial query proving the restored
    /// database actually accepts connections and responds, not just that the restore command exited 0.</summary>
    private static async Task<bool> VerifyAvailabilityAsync(DbProfile profile, CancellationToken ct)
    {
        try
        {
            if (profile.Kind == DbKind.Firebird)
            {
                var cs = new FirebirdSql.Data.FirebirdClient.FbConnectionStringBuilder
                {
                    ServerType = FirebirdSql.Data.FirebirdClient.FbServerType.Default,
                    DataSource = profile.Host,
                    Port = profile.Port,
                    UserID = profile.User,
                    Password = profile.Password,
                    Database = profile.Database,
                    ConnectionTimeout = profile.ConnectTimeoutSeconds,
                }.ToString();
                using var con = new FirebirdSql.Data.FirebirdClient.FbConnection(cs);
                await con.OpenAsync(ct);
                using var cmd = con.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM RDB$DATABASE";
                await cmd.ExecuteScalarAsync(ct);
            }
            else
            {
                var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
                {
                    DataSource = $"{profile.Host},{profile.Port}",
                    UserID = profile.User,
                    Password = profile.Password,
                    InitialCatalog = profile.Database,
                    TrustServerCertificate = true,
                    ConnectTimeout = profile.ConnectTimeoutSeconds,
                    ConnectRetryCount = 0,
                }.ToString();
                using var con = new Microsoft.Data.SqlClient.SqlConnection(cs);
                await con.OpenAsync(ct);
                using var cmd = con.CreateCommand();
                cmd.CommandText = "SELECT 1";
                await cmd.ExecuteScalarAsync(ct);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task RunGbakBackupAsync(DbProfile profile, string outputPath, CancellationToken ct)
    {
        var source = $"{profile.Host}/{profile.Port}:{profile.Database}";
        var psi = new ProcessStartInfo
        {
            FileName = gbakPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // ArgumentList (not a concatenated string) so the password/paths never need manual shell-quoting/escaping.
        foreach (var arg in new[] { "-backup", "-user", profile.User, "-password", profile.Password, source, outputPath }) psi.ArgumentList.Add(arg);

        Process process;
        try { process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the gbak process."); }
        catch (Exception ex) when (ex is not InvalidOperationException) { throw new InvalidOperationException($"Could not start gbak ('{gbakPath}'): {ex.Message}", ex); }
        using var _ = process;
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException($"gbak exited with code {process.ExitCode}: {(string.IsNullOrWhiteSpace(stderr) ? await stdoutTask : stderr)}");
        }
    }

    private static async Task RunSqlServerBackupAsync(DbProfile profile, string outputPath, CancellationToken ct)
    {
        var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = $"{profile.Host},{profile.Port}",
            UserID = profile.User,
            Password = profile.Password,
            InitialCatalog = "master", // BACKUP DATABASE doesn't require connecting to the target DB itself
            TrustServerCertificate = true,
            ConnectTimeout = profile.ConnectTimeoutSeconds,
            ConnectRetryCount = 0,
        }.ToString();
        using var con = new Microsoft.Data.SqlClient.SqlConnection(cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.CommandTimeout = 0; // a large DB's backup can legitimately take a long time; rely on ct, not a fixed budget
        // Built via escaped string interpolation rather than a SqlParameter: BACKUP DATABASE's TO DISK clause is not
        // reliably parameterizable the way ordinary DML is, so this follows the conventional pattern for admin/DDL
        // statements — double-up the identifier/string-literal delimiters instead.
        var safeDatabase = profile.Database.Replace("]", "]]");
        var safePath = outputPath.Replace("'", "''");
        cmd.CommandText = $"BACKUP DATABASE [{safeDatabase}] TO DISK = N'{safePath}' WITH INIT, CHECKSUM, STATS = 10;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string> ComputeChecksumAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Sanitize(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}
