using System.Diagnostics;
using System.Security.Cryptography;

namespace ProMaxx2.Automation.Core;

/// <summary>AUT-DATA-001: the result of one snapshot attempt. <see cref="Success"/> false means the agent should
/// report "Failed" to the Hub with <see cref="Error"/> as the message; the caller never needs to catch an exception
/// from <see cref="IDbSnapshotService.CreateSnapshotAsync"/> — every failure mode (tool missing, connection refused,
/// backup command failed) is captured here instead, same shape as <see cref="DbValidationResult"/>.</summary>
public sealed record DbSnapshotResult(bool Success, string? FilePath, string? Checksum, long? SizeBytes, string? Error, long ElapsedMs);

public interface IDbSnapshotService
{
    Task<DbSnapshotResult> CreateSnapshotAsync(DbProfile profile, string outputDirectory, string fileNameHint, CancellationToken ct);
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
