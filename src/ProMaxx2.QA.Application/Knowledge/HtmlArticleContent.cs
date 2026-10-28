using System.Net;
using System.Text.RegularExpressions;

namespace ProMaxx2.QA.Application.Knowledge;

/// <summary>
/// บทความที่นำเข้าจาก HTML (คลังความรู้ → บทความ → "นำเข้า HTML"): เก็บ HTML ไว้แสดงผลให้เหมือนไฟล์ต้นฉบับ
/// และสกัดเป็นข้อความธรรมดา (เก็บใน BodyMarkdown) ให้การค้นหา/ผู้ช่วย AI/MCP ใช้เหมือนบทความทั่วไป
///
/// ความปลอดภัย: หน้าเว็บแสดง HTML ใน iframe sandbox ที่**ไม่มี allow-scripts** (HtmlArticleFrame.tsx) + CSP ใน srcDoc ที่ห้ามสคริปต์ทุกชนิด
/// — สองชั้นนี้คือสิ่งที่ปกป้องจริง: สคริปต์ใน HTML ไม่รันเลย; sandbox มี allow-same-origin เพื่อให้หน้าแม่วัดความสูงเนื้อหาได้
/// ซึ่งปลอดภัยเฉพาะเมื่อไม่มี allow-scripts (test ใน htmlArticle.test.ts ล็อกค่า sandbox ไว้)
/// <see cref="Sanitize"/> เป็นชั้นป้องกันเพิ่ม (defense in depth) ที่ตัดสคริปต์, event handler, ลิงก์ javascript: (รวมแบบเข้ารหัส entity)
/// และ element ที่ฝังเนื้อหาภายนอก — เป็น regex ไม่ใช่ HTML parser จึงไม่ได้ออกแบบให้ปลอดภัยเมื่อแสดง HTML ตรง ๆ โดยไม่มี sandbox
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
        s = UrlAttributes().Replace(s, NeutralizeScriptUrl);
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
    /// <summary>ค่า URL ที่ถอด entity (&amp;#x73; ฯลฯ) และตัดช่องว่าง/อักขระควบคุมแล้วขึ้นต้นด้วย javascript:/vbscript:/data:text/html → แทนด้วย "#"</summary>
    private static string NeutralizeScriptUrl(Match m)
    {
        var raw = m.Groups["dq"].Success ? m.Groups["dq"].Value : m.Groups["sq"].Success ? m.Groups["sq"].Value : m.Groups["bare"].Value;
        var decoded = DecodeAttribute(DecodeAttribute(raw));
        var compact = new string(decoded.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray()).ToLowerInvariant();
        return compact.StartsWith("javascript:") || compact.StartsWith("vbscript:") || compact.StartsWith("data:text/html")
            ? m.Groups["pre"].Value + "\"#\""
            : m.Value;
    }

    /// <summary>ถอด entity แบบที่เบราว์เซอร์ถอดในค่า attribute — numeric ไม่มี ";" ก็ได้ และ &amp;colon; / &amp;Tab; / &amp;NewLine; (HTML5)
    /// ซึ่ง WebUtility.HtmlDecode ไม่รู้จัก</summary>
    private static string DecodeAttribute(string value)
    {
        var s = NumericEntity().Replace(value, m =>
        {
            var hex = m.Groups[1].Value.StartsWith('x') || m.Groups[1].Value.StartsWith('X');
            var digits = hex ? m.Groups[1].Value[1..] : m.Groups[1].Value;
            return int.TryParse(digits, hex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer, null, out var code) && code is > 0 and <= 0x10FFFF
                ? char.ConvertFromUtf32(code is >= 0xD800 and <= 0xDFFF ? 0xFFFD : code) : m.Value;
        });
        s = Html5UrlEntity().Replace(s, m => m.Groups[1].Value.Equals("colon", StringComparison.OrdinalIgnoreCase) ? ":" : " ");
        return WebUtility.HtmlDecode(s);
    }

    [GeneratedRegex(@"&#([xX][0-9a-fA-F]{1,6}|[0-9]{1,7});?")]
    private static partial Regex NumericEntity();
    [GeneratedRegex(@"&(colon|tab|newline);", RegexOptions.IgnoreCase)]
    private static partial Regex Html5UrlEntity();

    [GeneratedRegex(@"(?<pre>\b(?:href|src|xlink:href|action|formaction|data|poster|background)\s*=\s*)(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<bare>[^\s>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex UrlAttributes();
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
