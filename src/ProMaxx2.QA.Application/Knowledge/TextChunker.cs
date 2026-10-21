using System.Security.Cryptography;
using System.Text;

namespace ProMaxx2.QA.Application.Knowledge;

/// <summary>
/// แบ่งข้อความเป็นช่วง (chunk) สำหรับค้นหา — ตัดตามย่อหน้า/หัวข้อก่อน ถ้าย่อหน้ายาวเกินจึงตัดที่ช่องว่าง/ขึ้นบรรทัดที่ใกล้ขีดจำกัดที่สุด
/// (ภาษาไทยไม่มีจุดจบประโยค จึงใช้ช่องว่างเป็นจุดตัด) และต่อท้ายช่วงก่อนหน้า (overlap) ให้บริบทไม่ขาดตอน
/// </summary>
public static class TextChunker
{
    public const int DefaultMaxChars = 1000;
    public const int DefaultOverlapChars = 150;

    public static IReadOnlyList<string> Split(string? text, int maxChars = DefaultMaxChars, int overlapChars = DefaultOverlapChars)
    {
        if (maxChars < 100) throw new ArgumentOutOfRangeException(nameof(maxChars));
        if (overlapChars < 0 || overlapChars >= maxChars / 2) throw new ArgumentOutOfRangeException(nameof(overlapChars));
        var normalized = Normalize(text);
        if (normalized.Length == 0) return [];
        if (normalized.Length <= maxChars) return [normalized];

        var pieces = Paragraphs(normalized).SelectMany(p => p.Length <= maxChars ? [p] : HardSplit(p, maxChars - overlapChars));
        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var piece in pieces)
        {
            if (current.Length > 0 && current.Length + 2 + piece.Length > maxChars)
            {
                var done = current.ToString();
                chunks.Add(done);
                current.Clear();
                var tail = Tail(done, overlapChars);
                if (tail.Length > 0 && tail.Length + 2 + piece.Length <= maxChars) current.Append(tail).Append("\n\n");
            }
            if (current.Length > 0 && !current.ToString().EndsWith("\n\n", StringComparison.Ordinal)) current.Append("\n\n");
            current.Append(piece);
        }
        if (current.Length > 0) chunks.Add(current.ToString());
        return chunks;
    }

    /// <summary>รวมช่องว่าง/บรรทัดว่างซ้ำ และตัดอักขระควบคุม</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lines = text.ReplaceLineEndings("\n").Split('\n').Select(line => CollapseSpaces(line).TrimEnd());
        var sb = new StringBuilder();
        var blank = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0) { blank++; continue; }
            if (sb.Length > 0) sb.Append(blank > 0 ? "\n\n" : "\n");
            sb.Append(line);
            blank = 0;
        }
        return sb.ToString().Trim();
    }

    /// <summary>ย่อหน้า = คั่นด้วยบรรทัดว่าง หรือเริ่มหัวข้อ Markdown (#) ใหม่</summary>
    private static IEnumerable<string> Paragraphs(string text)
    {
        foreach (var block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var sb = new StringBuilder();
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith('#') && sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
            }
            if (sb.Length > 0) yield return sb.ToString();
        }
    }

    private static IEnumerable<string> HardSplit(string text, int size)
    {
        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(size, text.Length - start);
            if (start + length < text.Length)
            {
                var cut = text.LastIndexOfAny([' ', '\n'], start + length - 1, length);
                if (cut > start + size / 2) length = cut - start;
            }
            var piece = text.Substring(start, length).Trim();
            if (piece.Length > 0) yield return piece;
            start += length;
        }
    }

    private static string Tail(string text, int chars)
    {
        if (chars == 0 || text.Length <= chars) return chars == 0 ? string.Empty : text;
        var start = text.Length - chars;
        var space = text.IndexOfAny([' ', '\n'], start);
        return (space >= 0 && space < text.Length - 1 ? text[(space + 1)..] : text[start..]).Trim();
    }

    private static string CollapseSpaces(string line)
    {
        var sb = new StringBuilder(line.Length);
        var space = false;
        foreach (var ch in line)
        {
            if (char.IsControl(ch) && ch != '\t') continue;
            if (ch is ' ' or '\t' or ' ') { if (!space && sb.Length > 0) sb.Append(' '); space = true; continue; }
            sb.Append(ch);
            space = false;
        }
        return sb.ToString();
    }
}

public static class KnowledgeHash
{
    /// <summary>SHA-256 (hex ตัวพิมพ์เล็ก 64 ตัว) ของข้อความ — ใช้ตรวจว่าต้นทางเปลี่ยนจริงก่อนสร้าง chunk ใหม่</summary>
    public static string Compute(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)));
}
