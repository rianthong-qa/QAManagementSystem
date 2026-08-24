using System.Data;
using System.Globalization;

namespace ProMaxx2.Automation.Core;

public enum DbKind { Firebird, SqlServer }

public sealed record DbProfile(DbKind Kind, string Host, int Port, string User, string Password, string Database)
{
    public static DbProfile FromEnvironment(AgentConfig config) => new(
        config.DbType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? DbKind.SqlServer : DbKind.Firebird,
        config.DbHost,
        config.DbPort,
        config.DbUser,
        config.DbPassword,
        config.DbDatabase);
}

public sealed record DbValidationRequest(DbProfile Profile, string Query, IReadOnlyDictionary<string, string> Parameters, string Expected, string? Column = null);
public sealed record DbValidationResult(bool Passed, string ActualValue, string Query, string? Error, long ElapsedMs);

public interface IDbValidator
{
    Task<DbValidationResult> ValidateAsync(DbValidationRequest request, CancellationToken ct);
}

public sealed class FirebirdDbValidator : IDbValidator
{
    public Task<DbValidationResult> ValidateAsync(DbValidationRequest r, CancellationToken ct)
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
            }.ToString();
            using var con = new FirebirdSql.Data.FirebirdClient.FbConnection(cs);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = r.Query;
            foreach (var (key, value) in r.Parameters)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = key.StartsWith("@") ? key : "@" + key;
                p.Value = value;
                cmd.Parameters.Add(p);
            }
            object? actual;
            using (var reader = cmd.ExecuteReader())
            {
                if (!reader.Read()) actual = null;
                else if (!string.IsNullOrWhiteSpace(r.Column)) actual = reader[r.Column];
                else actual = reader.GetValue(0);
            }
            sw.Stop();
            var actualText = actual?.ToString()?.Trim() ?? "";
            return Task.FromResult(new DbValidationResult(Compare(actualText, r.Expected), actualText, r.Query, null, sw.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Task.FromResult(new DbValidationResult(false, "", r.Query, ex.Message, sw.ElapsedMilliseconds));
        }
    }

    private static bool Compare(string actual, string expected)
    {
        var exp = expected.Trim();
        var ops = new[] { ">=", "<=", "!=", ">", "<", "=" };
        foreach (var op in ops)
        {
            if (!exp.StartsWith(op, StringComparison.Ordinal)) continue;
            var rest = exp[op.Length..].Trim();
            if (decimal.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var aNum)
                && decimal.TryParse(rest, NumberStyles.Any, CultureInfo.InvariantCulture, out var eNum))
            {
                return op switch
                {
                    ">=" => aNum >= eNum,
                    "<=" => aNum <= eNum,
                    "!=" => aNum != eNum,
                    ">" => aNum > eNum,
                    "<" => aNum < eNum,
                    _ => aNum == eNum,
                };
            }
            return actual.Equals(rest, StringComparison.OrdinalIgnoreCase);
        }
        if (decimal.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var aNum2)
            && decimal.TryParse(exp, NumberStyles.Any, CultureInfo.InvariantCulture, out var eNum2))
            return aNum2 == eNum2;
        if (exp.Equals("true", StringComparison.OrdinalIgnoreCase)) return actual == "1" || actual.Equals("true", StringComparison.OrdinalIgnoreCase);
        if (exp.Equals("false", StringComparison.OrdinalIgnoreCase)) return actual == "0" || actual.Equals("false", StringComparison.OrdinalIgnoreCase);
        return actual.Equals(exp, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class SqlServerDbValidator : IDbValidator
{
    public Task<DbValidationResult> ValidateAsync(DbValidationRequest r, CancellationToken ct)
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
            }.ToString();
            using var con = new Microsoft.Data.SqlClient.SqlConnection(cs);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = r.Query;
            foreach (var (key, value) in r.Parameters)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = key.StartsWith("@") ? key : "@" + key;
                p.Value = value;
                cmd.Parameters.Add(p);
            }
            object? actual;
            using (var reader = cmd.ExecuteReader())
            {
                if (!reader.Read()) actual = null;
                else if (!string.IsNullOrWhiteSpace(r.Column)) actual = reader[r.Column];
                else actual = reader.GetValue(0);
            }
            sw.Stop();
            var actualText = actual?.ToString()?.Trim() ?? "";
            return Task.FromResult(new DbValidationResult(actualText.Equals(r.Expected.Trim(), StringComparison.OrdinalIgnoreCase), actualText, r.Query, null, sw.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Task.FromResult(new DbValidationResult(false, "", r.Query, ex.Message, sw.ElapsedMilliseconds));
        }
    }
}

public static class DbValidatorFactory
{
    public static IDbValidator Create(DbKind kind) => kind == DbKind.Firebird ? new FirebirdDbValidator() : new SqlServerDbValidator();
}