using System.Text.RegularExpressions;

namespace ProMaxx2.QA.Domain.Automation;

/// <summary>AUT-SEC-004: SQL ใน step ตรวจฐานข้อมูล (parameter <c>query</c> ของ EXPECT_DB_VALUE / EXPECT_DB_ROW_COUNT)
/// ถูกส่งไปรันบน Windows Agent ด้วยสิทธิ์ของ DB user ในเครื่องนั้น จึงต้องเป็นคำสั่งอ่านอย่างเดียวคำสั่งเดียวเท่านั้น.
/// Guard นี้ตั้งใจเข้มแบบ allow-list (เริ่มด้วย SELECT/WITH, ห้าม comment/หลายคำสั่ง/keyword ที่แก้ข้อมูล) — Agent ตรวจซ้ำ
/// และรันใน read-only transaction ที่ rollback เสมออีกชั้น</summary>
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
        if (string.IsNullOrWhiteSpace(sql)) { reason = "query ว่าง"; return false; }
        var text = sql.Trim();
        if (text.EndsWith(';')) text = text[..^1].TrimEnd();
        if (text.Contains("--") || text.Contains("/*")) { reason = "ห้ามมี comment ใน query"; return false; }
        // ตัด string literal ออกก่อนตรวจ keyword เพื่อไม่ให้ค่าอย่าง WHERE STATUS = 'DELETE' ถูกปฏิเสธผิด
        var withoutLiterals = StringLiteral().Replace(text, "''");
        if (withoutLiterals.Contains(';')) { reason = "อนุญาตเพียงคำสั่งเดียว (ห้ามมี ; กลางคำสั่ง)"; return false; }
        if (!LeadingSelect().IsMatch(withoutLiterals)) { reason = "query ต้องขึ้นต้นด้วย SELECT หรือ WITH"; return false; }
        foreach (var keyword in ForbiddenKeywords)
        {
            if (Regex.IsMatch(withoutLiterals, $@"\b{keyword}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                reason = $"ห้ามใช้คำสั่ง {keyword} ใน query ตรวจฐานข้อมูล (อ่านอย่างเดียว)";
                return false;
            }
        }
        return true;
    }

    [GeneratedRegex("'(?:[^']|'')*'", RegexOptions.CultureInvariant)]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"^\s*(SELECT|WITH)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingSelect();
}
