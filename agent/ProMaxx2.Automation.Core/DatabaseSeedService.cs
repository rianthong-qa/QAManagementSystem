using System.Diagnostics;

namespace ProMaxx2.Automation.Core;

/// <summary>AUT-DATA-003: the result of one seed script execution.</summary>
public sealed record DbSeedResult(bool Success, int? RowsAffected, string? Error, long ElapsedMs);

public interface IDbSeedService
{
    /// <summary>Runs <paramref name="sqlScript"/> against <paramref name="profile"/>'s database in a single
    /// transaction (all-or-nothing — a script that fails partway through should never leave the DB half-seeded,
    /// which would defeat the point of it being repeatable/idempotent).</summary>
    Task<DbSeedResult> RunSeedScriptAsync(DbProfile profile, string sqlScript, CancellationToken ct);
}

/// <summary>Executes a reusable seed SQL script against the DB an agent's local <see cref="DbProfile"/> talks to.
/// The two providers need different execution strategies for a multi-statement script: SQL Server's ADO.NET driver
/// natively executes a whole T-SQL batch (multiple statements separated by <c>;</c>) in one <c>ExecuteNonQuery</c>
/// call, but Firebird's does not — each <c>FbCommand</c> can only hold a single statement, so a Firebird script is
/// split on <c>;</c> and each statement is executed individually inside one shared transaction.</summary>
public sealed class DatabaseSeedService : IDbSeedService
{
    public async Task<DbSeedResult> RunSeedScriptAsync(DbProfile profile, string sqlScript, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var rowsAffected = profile.Kind == DbKind.Firebird
                ? await RunFirebirdScriptAsync(profile, sqlScript, ct)
                : await RunSqlServerScriptAsync(profile, sqlScript, ct);
            sw.Stop();
            return new DbSeedResult(true, rowsAffected, null, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new DbSeedResult(false, null, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static async Task<int> RunFirebirdScriptAsync(DbProfile profile, string sqlScript, CancellationToken ct)
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
        using var tx = con.BeginTransaction();
        var total = 0;
        try
        {
            foreach (var statement in SplitStatements(sqlScript))
            {
                using var cmd = con.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = statement;
                total += await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
        return total;
    }

    private static async Task<int> RunSqlServerScriptAsync(DbProfile profile, string sqlScript, CancellationToken ct)
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
        using var tx = con.BeginTransaction();
        try
        {
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandTimeout = 0; // a large seed script can legitimately take a while; rely on ct, not a fixed budget
            cmd.CommandText = sqlScript; // SQL Server executes a whole multi-statement batch natively — no splitting needed
            var affected = await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return affected;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>Naive <c>;</c>-splitting, same known limitation as any simple SQL-script runner: a semicolon inside
    /// a string literal or a stored-procedure body would be split incorrectly. Acceptable for the plain
    /// INSERT/UPDATE/DELETE-shaped seed scripts this feature targets; a script needing anything more elaborate
    /// (PSQL blocks, etc.) is out of scope here.</summary>
    private static IEnumerable<string> SplitStatements(string sqlScript) =>
        sqlScript.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0);
}
