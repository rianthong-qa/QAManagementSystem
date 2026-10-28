using System.Net;
using System.Text.RegularExpressions;

namespace ProMaxx2.QA.Application.Knowledge;

/// <summary>
/// บทความที่นำเข้าจาก HTML (คลังความรู้ → บทความ → "นำเข้า HTML"): เก็บ HTML ไว้แสดงผลให้เหมือนไฟล์ต้นฉบับ
/// และสกัดเป็นข้อความธรรมดา (เก็บใน BodyMarkdown) ให้การค้นหา/ผู้ช่วย AI/MCP ใช้เหมือนบทความทั่วไป
///
/// ความปลอดภัย: หน้าเว็บแสดง HTML ใน iframe sandbox (ไม่มี allow-scripts / allow-same-origin) + CSP ที่ห้ามโหลดสคริปต์
/// อยู่แล้ว — <see cref="Sanitize"/> เป็นชั้นป้องกันเพิ่ม (defense in depth) ที่ตัดสคริปต์, event handler, ลิงก์ javascript:
/// และ element ที่ฝังเนื้อหาภายนอก ไม่ได้ออกแบบให้ปลอดภัยเมื่อแสดง HTML ตรง ๆ ในหน้าเว็บโดยไม่มี sandbox
/// </summary>
public static partial class HtmlArticleContent
{
    /// <summary>ขนาด HTML สูงสุด (ตัวอักษร) — รองรับรูปแบบ base64 ฝังในไฟล์ได้ระดับหนึ่ง</summary>
    public const int MaxLength = 3_000_000;

    public static string Sanitize(string html)
    {
        var s = DangerousBlocks().Replace(html, "");
        s = DangerousTags().Replace(s, "");
        s = EventHandlers().Replace(s, "");
        s = SrcDocAttribute().Replace(s, "");
        s = ScriptUrls().Replace(s, "$1=$2#");
        s = CssExpressions().Replace(s, "");
        return s.Trim();
    }

    /// <summary>ข้อความธรรมดาจาก HTML — หัวข้อ h1–h6 ขึ้นต้นด้วย "## " เพื่อให้ TextChunker แบ่งช่วงตามหัวข้อ</summary>
    public static string ToText(string html)
    {
        var s = NonContentBlocks().Replace(html, " ");
        s = HeadingOpen().Replace(s, "\n\n## ");
        s = CellBreak().Replace(s, " | ");
        s = ListItem().Replace(s, "\n- ");
        s = BlockBreak().Replace(s, "\n");
        s = AnyTag().Replace(s, " ");
        s = WebUtility.HtmlDecode(s).Replace(' ', ' ');
        return TextChunker.Normalize(s);
    }

    /// <summary>ชื่อจาก &lt;title&gt; หรือ &lt;h1&gt; แรก — ใช้เติมหัวข้อบทความอัตโนมัติ</summary>
    public static string? ExtractTitle(string html)
    {
        var m = TitleTag().Match(html);
        if (!m.Success) m = FirstH1().Match(html);
        if (!m.Success) return null;
        var title = TextChunker.Normalize(WebUtility.HtmlDecode(AnyTag().Replace(m.Groups[1].Value, " "))).Replace('\n', ' ').Trim();
        return title.Length == 0 ? null : title.Length > 300 ? title[..300] : title;
    }

    [GeneratedRegex(@"<\s*(script|iframe|object|embed|applet|frameset|frame|noscript|template)\b[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DangerousBlocks();
    [GeneratedRegex(@"<\s*/?\s*(script|iframe|object|embed|applet|frameset|frame|base|link|meta|portal)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex DangerousTags();
    [GeneratedRegex(@"\s+on[a-z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandlers();
    [GeneratedRegex(@"\s+srcdoc\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SrcDocAttribute();
    [GeneratedRegex(@"\b(href|src|xlink:href|action|formaction|data|poster|background)\s*=\s*([""']?)\s*(?:javascript|vbscript|data\s*:\s*text/html)[^""'\s>]*", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptUrls();
    [GeneratedRegex(@"expression\s*\([^)]*\)|-moz-binding\s*:[^;""']*|behavior\s*:[^;""']*", RegexOptions.IgnoreCase)]
    private static partial Regex CssExpressions();
    [GeneratedRegex(@"<\s*(head|style|script|noscript|template|svg)\b[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex NonContentBlocks();
    [GeneratedRegex(@"<\s*h[1-6]\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingOpen();
    [GeneratedRegex(@"<\s*/\s*t[dh]\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex CellBreak();
    [GeneratedRegex(@"<\s*li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItem();
    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/tr|/h\d|/section|/article|/blockquote|/pre|/table|/ul|/ol|hr)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBreak();
    [GeneratedRegex(@"<[^>]{1,2000}>")]
    private static partial Regex AnyTag();
    [GeneratedRegex(@"<\s*title\b[^>]*>(.*?)<\s*/\s*title\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTag();
    [GeneratedRegex(@"<\s*h1\b[^>]*>(.*?)<\s*/\s*h1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FirstH1();
}
