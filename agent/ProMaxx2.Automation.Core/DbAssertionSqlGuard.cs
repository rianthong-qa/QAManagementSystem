using System.Text.RegularExpressions;

namespace ProMaxx2.Automation.Core;

/// <summary>AUT-SEC-004: ด่านที่สองของการจำกัด SQL ใน step ตรวจฐานข้อมูล (ด่านแรกคือ Hub validator
/// <c>ProMaxx2.QA.Domain.Automation.DbAssertionSqlGuard</c> — กฎต้องตรงกัน). Agent ไม่เชื่อ DSL ที่ได้จาก Hub ทั้งหมด:
/// รับเฉพาะ SELECT/WITH คำสั่งเดียว ห้าม comment และ keyword ที่แก้ข้อมูล จากนั้นยังรันใน transaction ที่ rollback เสมอ</summary>
public static partial class DbAssertionSqlGuard
{
    private static readonly string[] ForbiddenKeywords =
    [
        "INSERT", "UPDATE", "DELETE", "MERGE", "UPSERT", "DROP", "ALTER", "CREATE", "RECREATE", "TRUNCATE", "EXEC", "EXECUTE",
        "GRANT", "REVOKE", "INTO", "CALL", "SET", "DECLARE", "BACKUP", "RESTORE", "SHUTDOWN", "COMMIT", "ROLLBACK", "SAVEPOINT",
        "KILL", "OPENROWSET", "OPENQUERY", "OPENDATASOURCE", "BULK", "DBCC", "WAITFOR", "RECONFIGURE",
    ];

    public static bool IsReadOnlySelect(string? sql, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(sql)) { reason = "query is empty"; return false; }
        var text = sql.Trim();
        if (text.EndsWith(';')) text = text[..^1].TrimEnd();
        if (text.Contains("--") || text.Contains("/*")) { reason = "comments are not allowed in a DB assertion query"; return false; }
        var withoutLiterals = StringLiteral().Replace(text, "''");
        if (withoutLiterals.Contains(';')) { reason = "only a single statement is allowed"; return false; }
        if (!LeadingSelect().IsMatch(withoutLiterals)) { reason = "query must start with SELECT or WITH"; return false; }
        foreach (var keyword in ForbiddenKeywords)
        {
            if (Regex.IsMatch(withoutLiterals, $@"\b{keyword}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                reason = $"{keyword} is not allowed in a read-only DB assertion query";
                return false;
            }
        }
        return true;
    }

    /// <summary>ตัด ; ท้ายคำสั่งออก เพื่อให้นำไปครอบเป็น derived table ได้ (เช่น COUNT(*) ของ EXPECT_DB_ROW_COUNT)</summary>
    public static string TrimTerminator(string sql)
    {
        var text = sql.Trim();
        return text.EndsWith(';') ? text[..^1].TrimEnd() : text;
    }

    [GeneratedRegex("'(?:[^']|'')*'", RegexOptions.CultureInvariant)]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"^\s*(SELECT|WITH)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingSelect();
}
