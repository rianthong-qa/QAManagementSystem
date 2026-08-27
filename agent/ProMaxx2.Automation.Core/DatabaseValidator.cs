using System.Data;
using System.Globalization;

namespace ProMaxx2.Automation.Core;

public enum DbKind { Firebird, SqlServer }

public sealed record DbProfile(DbKind Kind, string Host, int Port, string User, string Password, string Database, int ConnectTimeoutSeconds = 5)
{
    public static DbProfile FromEnvironment(AgentConfig config) => new(
        config.DbType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? DbKind.SqlServer : DbKind.Firebird,
        config.DbHost,
        config.DbPort,
        config.DbUser,
        config.DbPassword,
        config.DbDatabase,
        // Reuse the agent's action timeout budget as the connect timeout, clamped to 1-5s: a DB assertion step
        // shouldn't spend most of its own timeout budget just waiting to connect. Previously neither provider set
        // this at all, defaulting to the ~15s provider default regardless of how impatient the step itself was.
        // Clamped to a minimum of 1: both ADO.NET providers treat a timeout of 0 as "wait indefinitely", which is
        // the exact opposite of the intent here if ActionTimeoutSeconds is ever misconfigured to 0.
        Math.Clamp(config.ActionTimeoutSeconds, 1, 5));
}

public sealed record DbValidationRequest(DbProfile Profile, string Query, IReadOnlyDictionary<string, string> Parameters, string Expected, string? Column = null);
public sealed record DbValidationResult(bool Passed, string ActualValue, string Query, string? Error, long ElapsedMs);

public interface IDbValidator
{
    Task<DbValidationResult> ValidateAsync(DbValidationRequest request, CancellationToken ct);
}

public sealed class FirebirdDbValidator : IDbValidator
{
    public async Task<DbValidationResult> ValidateAsync(DbValidationRequest r, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var cs = new FirebirdSql.Data.FirebirdClient.FbConnectionStringBuilder
            {
                ServerType = FirebirdSql.Data.FirebirdClient.FbServerType.Default,
                DataSource = r.Profile.Host,
                Port = r.Profile.Port,
                UserID = r.Profile.User,
                Password = r.Profile.Password,
                Database = r.Profile.Database,
                ConnectionTimeout = r.Profile.ConnectTimeoutSeconds,
            }.ToString();
            using var con = new FirebirdSql.Data.FirebirdClient.FbConnection(cs);
            await con.OpenAsync(ct);
            using var cmd = con.CreateCommand();
            cmd.CommandText = r.Query;
            foreach (var (key, value) in r.Parameters)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = DbAssertionComparer.NormalizeParameterName(key);
                p.Value = value;
                cmd.Parameters.Add(p);
            }
            object? actual;
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) actual = null;
                else if (!string.IsNullOrWhiteSpace(r.Column)) actual = reader[r.Column];
                else actual = reader.GetValue(0);
            }
            sw.Stop();
            var actualText = actual?.ToString()?.Trim() ?? "";
            return new DbValidationResult(DbAssertionComparer.Compare(actualText, r.Expected), actualText, r.Query, null, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new DbValidationResult(false, "", r.Query, ex.Message, sw.ElapsedMilliseconds);
        }
    }
}

public sealed class SqlServerDbValidator : IDbValidator
{
    public async Task<DbValidationResult> ValidateAsync(DbValidationRequest r, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = $"{r.Profile.Host},{r.Profile.Port}",
                UserID = r.Profile.User,
                Password = r.Profile.Password,
                InitialCatalog = r.Profile.Database,
                TrustServerCertificate = true,
                ConnectTimeout = r.Profile.ConnectTimeoutSeconds,
                ConnectRetryCount = 0, // don't add SqlClient's own reconnect-retry wait on top of ConnectTimeout
            }.ToString();
            using var con = new Microsoft.Data.SqlClient.SqlConnection(cs);
            await con.OpenAsync(ct);
            using var cmd = con.CreateCommand();
            cmd.CommandText = r.Query;
            foreach (var (key, value) in r.Parameters)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = DbAssertionComparer.NormalizeParameterName(key);
                p.Value = value;
                cmd.Parameters.Add(p);
            }
            object? actual;
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) actual = null;
                else if (!string.IsNullOrWhiteSpace(r.Column)) actual = reader[r.Column];
                else actual = reader.GetValue(0);
            }
            sw.Stop();
            var actualText = actual?.ToString()?.Trim() ?? "";
            return new DbValidationResult(DbAssertionComparer.Compare(actualText, r.Expected), actualText, r.Query, null, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new DbValidationResult(false, "", r.Query, ex.Message, sw.ElapsedMilliseconds);
        }
    }
}

public static class DbValidatorFactory
{
    public static IDbValidator Create(DbKind kind) => kind == DbKind.Firebird ? new FirebirdDbValidator() : new SqlServerDbValidator();
}