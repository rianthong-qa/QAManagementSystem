namespace ProMaxx2.QA.Application.Knowledge;

/// <summary>
/// ขอบเขตการค้นที่ผู้ใช้มีสิทธิ์ — กรองใน SQL ก่อนจัดอันดับ (ไม่ใช่กรองหลังได้ top-N เพราะผู้ใช้ที่มีสิทธิ์น้อยจะได้ผลว่าง)
/// AllowedProjectIds = Project ที่เป็นสมาชิก (ว่าง = เห็นเฉพาะความรู้กลาง), IncludeCrm = มีสิทธิ์ CRM.VIEW
/// </summary>
public sealed record KnowledgeSearchScope(IReadOnlyCollection<Guid> AllowedProjectIds, bool IncludeCrm, Guid? ProjectId = null, IReadOnlyCollection<string>? SourceTypes = null);

public sealed record KnowledgeChunkHit(int KnowledgeChunkId, int Rank);

/// <summary>ค้นข้อความใน KnowledgeChunks — แยกเป็น interface เพราะ Full-Text (FREETEXTTABLE) ใช้ได้เฉพาะ SQL Server ไม่ใช่ EF InMemory</summary>
public interface IKnowledgeTextSearch
{
    Task<IReadOnlyList<KnowledgeChunkHit>> SearchAsync(string query, KnowledgeSearchScope scope, int take, CancellationToken ct);
}

/// <summary>ค้นตามความหมายด้วย embedding (Phase 5) — ไม่ได้ตั้งค่า/ใช้งานไม่ได้ = คืนรายการว่าง (ค้นด้วยคำอย่างเดียว); Rank = similarity × 1000</summary>
public interface IKnowledgeVectorSearch
{
    Task<IReadOnlyList<KnowledgeChunkHit>> SearchAsync(string query, KnowledgeSearchScope scope, int take, CancellationToken ct);

    /// <summary>
    /// ช่วงที่มีเวกเตอร์แต่ similarity กับคำถามต่ำกว่าเกณฑ์บริบทของผู้ช่วย AI (`Knowledge:ContextMinSimilarity`)
    /// null = ประเมินไม่ได้ (ไม่ได้ตั้งค่า/embedding ล่ม) — ช่วงที่ยังไม่มีเวกเตอร์ไม่อยู่ในผลลัพธ์ (ถือว่ายังไม่รู้)
    /// </summary>
    Task<IReadOnlySet<int>?> WeakMatchesAsync(string query, IReadOnlyCollection<int> chunkIds, CancellationToken ct);
}

/// <summary>
/// เกณฑ์ว่าเอกสารเกี่ยวข้องพอจะส่งเป็นบริบทให้ผู้ช่วย AI — FREETEXT นำทุกคำมา OR กัน จึงคืนผลแทบทุกคำถาม (เช่น "สวัสดี", "55")
/// และ RANK ของ Full-Text เทียบข้ามคำถามไม่ได้ จึงตัดด้วย similarity ของ embedding แทน
/// ช่วงสั้น ๆ (มีแต่ header เช่น Ticket "test") ใกล้กับทุกคำถามจึงไม่นับ; รหัสที่ผู้ใช้พิมพ์ (PMX2-DEF-1858) embedding ให้คะแนนต่ำจึงยกเว้นเมื่อพบในเอกสาร
/// </summary>
public static partial class KnowledgeRelevance
{
    public const int MinChunkChars = 150;

    /// <summary>คำในคำถามที่เป็นรหัส (ยาว ≥ 5 ตัวและมีตัวเลข) เช่น PMX2-DEF-1858, BHD690520000008</summary>
    public static IReadOnlyList<string> Identifiers(string query) =>
        IdentifierToken().Matches(query).Select(m => m.Value.Trim('-', '_', '.', '/')).Where(x => x.Length >= 5 && x.Any(char.IsAsciiDigit)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static bool MentionsAny(string text, IReadOnlyList<string> identifiers) =>
        identifiers.Any(id => text.Contains(id, StringComparison.OrdinalIgnoreCase));

    [System.Text.RegularExpressions.GeneratedRegex(@"[A-Za-z0-9][A-Za-z0-9\-_./]+")]
    private static partial System.Text.RegularExpressions.Regex IdentifierToken();
}

public static class KnowledgeQuery
{
    public const int MinLength = 2;
    public const int MaxLength = 200;

    /// <summary>ตัดช่องว่าง/อักขระควบคุม คืน null เมื่อสั้นเกินไป — ยาวเกินตัดที่ MaxLength</summary>
    public static string? Normalize(string? query)
    {
        var text = TextChunker.Normalize(query).Replace('\n', ' ');
        if (text.Length < MinLength) return null;
        return text.Length > MaxLength ? text[..MaxLength] : text;
    }
}

public static class KnowledgeSnippet
{
    /// <summary>
    /// ข้อความตัวอย่างรอบคำค้น (ไม่รวมบรรทัด header ที่ติดทุก chunk) — หาเจอทั้งคำค้นหรือคำแรกที่ยาว ≥ 2 ตัว ถ้าไม่เจอเริ่มต้นเนื้อหา
    /// ภาษาไทยไม่มีช่องว่างระหว่างคำ จึงลองทั้งวลีก่อนแล้วค่อยแยกตามช่องว่าง
    /// </summary>
    public static string Build(string chunkText, string query, int length = 280)
    {
        var newline = chunkText.IndexOf('\n');
        var body = (newline >= 0 ? chunkText[(newline + 1)..] : chunkText).Trim();
        if (body.Length <= length) return body;
        var terms = new[] { query.Trim() }.Concat(query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length >= 2).OrderByDescending(x => x.Length));
        var at = terms.Select(t => body.IndexOf(t, StringComparison.OrdinalIgnoreCase)).Where(i => i >= 0).DefaultIfEmpty(0).First();
        var start = Math.Max(0, Math.Min(at - length / 4, body.Length - length));
        if (start > 0) { var space = body.IndexOfAny([' ', '\n'], start); if (space >= 0 && space < at) start = space + 1; }
        var end = Math.Min(body.Length, start + length);
        return $"{(start > 0 ? "…" : "")}{body[start..end].Trim()}{(end < body.Length ? "…" : "")}";
    }
}
