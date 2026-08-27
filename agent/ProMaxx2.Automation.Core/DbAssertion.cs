using System.Globalization;

namespace ProMaxx2.Automation.Core;

/// <summary>
/// Comparison and parameter-naming logic shared by <see cref="FirebirdDbValidator"/> and
/// <see cref="SqlServerDbValidator"/>. Extracted out of FirebirdDbValidator (where it used to live as a private
/// method reachable only after a live DB connection succeeded) so it is unit-testable without a database, and so
/// both providers get identical operator-comparison semantics — SQL Server previously only did a plain
/// case-insensitive string equals and silently ignored the ">="/"&lt;="/"!="/"&gt;"/"&lt;" operators that Firebird
/// already supported.
/// </summary>
public static class DbAssertionComparer
{
    private static readonly string[] Operators = [">=", "<=", "!=", ">", "<", "="];

    public static bool Compare(string actual, string expected)
    {
        var exp = expected.Trim();
        foreach (var op in Operators)
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

    /// <summary>DB providers require parameter names to start with "@" (or accept it optionally) — normalize once so both validators bind parameters identically.</summary>
    public static string NormalizeParameterName(string key) => key.StartsWith("@", StringComparison.Ordinal) ? key : "@" + key;
}
