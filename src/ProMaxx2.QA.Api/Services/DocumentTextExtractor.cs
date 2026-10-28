using System.IO.Compression;
using System.Text;
using System.Xml;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace ProMaxx2.QA.Api.Services;

public sealed record ExtractedDocument(string? Text, string? Error, int Pages = 0);

/// <summary>
/// สกัดข้อความจากไฟล์เอกสารเพื่อเข้าคลังความรู้ (Phase 3) — ตรวจชนิดไฟล์จากเนื้อหา (magic bytes) ไม่ใช่แค่นามสกุล
/// PDF: PdfPig (ContentOrderTextExtractor) — PDF ที่เป็นภาพสแกนไม่มีข้อความให้สกัด (ยังไม่รองรับ OCR)
/// DOCX: อ่าน word/document.xml จาก zip โดยตรง (ย่อหน้า, แท็บ, ขึ้นบรรทัด, ตาราง) ไม่ต้องพึ่งไลบรารีเพิ่ม
/// MD/TXT: UTF-8 (มี/ไม่มี BOM) หรือ TIS-620/Windows-874 สำหรับไฟล์ภาษาไทยรุ่นเก่า
/// </summary>
public static partial class DocumentTextExtractor
{
    public const int MinMeaningfulChars = 20;

    static DocumentTextExtractor() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>เนื้อหาไฟล์ตรงกับนามสกุลหรือไม่ (กันไฟล์อื่นเปลี่ยนชื่อมา)</summary>
    public static bool LooksValid(byte[] data, string extension) => extension switch
    {
        ".pdf" => data.Length > 5 && data[0] == '%' && data[1] == 'P' && data[2] == 'D' && data[3] == 'F' && data[4] == '-',
        ".docx" => data.Length > 4 && data[0] == 'P' && data[1] == 'K' && data[2] == 3 && data[3] == 4,
        ".md" or ".txt" => !data.AsSpan(0, Math.Min(data.Length, 8192)).Contains((byte)0),
        _ => false,
    };

    public static ExtractedDocument Extract(byte[] data, string extension)
    {
        try
        {
            var result = extension switch
            {
                ".pdf" => Pdf(data),
                ".docx" => Docx(data),
                ".md" or ".txt" => new ExtractedDocument(PlainText(data), null),
                _ => new ExtractedDocument(null, "ไม่รองรับไฟล์ชนิดนี้"),
            };
            if (result.Error is not null) return result;
            var text = Normalize(result.Text);
            if (text.Count(c => !char.IsWhiteSpace(c)) < MinMeaningfulChars)
                return new(null, extension == ".pdf" ? "ไม่พบข้อความใน PDF — อาจเป็นเอกสารสแกนเป็นภาพ (ยังไม่รองรับ OCR) ให้แปลงเป็น PDF ที่มีข้อความก่อน" : "ไม่พบข้อความในไฟล์", result.Pages);
            return result with { Text = text };
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException or InvalidOperationException or ArgumentException or FormatException
            || ex.GetType().Namespace?.StartsWith("UglyToad", StringComparison.Ordinal) == true)
        {
            return new(null, $"อ่านไฟล์ไม่สำเร็จ — ไฟล์อาจเสียหายหรือมีรหัสผ่าน ({ex.GetType().Name})");
        }
    }

    private static ExtractedDocument Pdf(byte[] data)
    {
        using var document = PdfDocument.Open(data);
        if (document.IsEncrypted) return new(null, "PDF มีรหัสผ่าน — ให้ปลดรหัสผ่านก่อนอัปโหลด");
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page);
            if (!string.IsNullOrWhiteSpace(text)) sb.Append(text.Trim()).Append("\n\n");
        }
        return new(RepairThaiPdfText(sb.ToString()), null, document.NumberOfPages);
    }

    // อักขระ Private Use Area ที่ font ภาษาไทยบางตัว (Windows/Mac) ใช้แทนสระ/วรรณยุกต์ตำแหน่งพิเศษ → อักษรไทยมาตรฐาน
    private static readonly Dictionary<char, char> ThaiPresentationForms = new()
    {
        ['\uF700'] = '\u0E10', ['\uF701'] = '\u0E34', ['\uF702'] = '\u0E35', ['\uF703'] = '\u0E36', ['\uF704'] = '\u0E37',
        ['\uF705'] = '\u0E48', ['\uF706'] = '\u0E49', ['\uF707'] = '\u0E4A', ['\uF708'] = '\u0E4B', ['\uF709'] = '\u0E4C',
        ['\uF70A'] = '\u0E48', ['\uF70B'] = '\u0E49', ['\uF70C'] = '\u0E4A', ['\uF70D'] = '\u0E4B', ['\uF70E'] = '\u0E4C',
        ['\uF70F'] = '\u0E0D', ['\uF710'] = '\u0E31', ['\uF711'] = '\u0E4D', ['\uF712'] = '\u0E47',
        ['\uF713'] = '\u0E48', ['\uF714'] = '\u0E49', ['\uF715'] = '\u0E4A', ['\uF716'] = '\u0E4B', ['\uF717'] = '\u0E4C',
        ['\uF718'] = '\u0E38', ['\uF719'] = '\u0E39', ['\uF71A'] = '\u0E3A',
    };

    /// <summary>
    /// ซ่อมข้อความไทยจาก PDF: (1) แปลงอักขระ PUA เป็นอักษรมาตรฐาน (2) สระอำที่ font เก็บเป็นนิคหิต + สระอา มักถูกสกัดเป็น
    /// "พยัญชนะ(+วรรณยุกต์) ช่องว่าง า" หรือ "ํ า" — ในภาษาไทย "า" ขึ้นต้นพยางค์หลังช่องว่างไม่ได้ จึงรวมกลับเป็น "ำ" ได้อย่างปลอดภัย
    /// (ไม่ซ่อม = ค้นคำอย่าง "นำเสนอ" ไม่เจอ)
    /// </summary>
    public static string RepairThaiPdfText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var chars = text.Select(c => ThaiPresentationForms.TryGetValue(c, out var mapped) ? mapped : c).ToArray();
        var fixedText = new string(chars).Replace("\u0E4D\u0E32", "\u0E33").Replace("\u0E4D \u0E32", "\u0E33");
        return ThaiDetachedSaraAm().Replace(fixedText, "$1\u0E33");
    }

    /// <summary>พยัญชนะ ก–ฮ (+ วรรณยุกต์) ตามด้วยช่องว่างแล้วสระอา</summary>
    [System.Text.RegularExpressions.GeneratedRegex("([\u0E01-\u0E2E][\u0E48-\u0E4B]?) \u0E32")]
    private static partial System.Text.RegularExpressions.Regex ThaiDetachedSaraAm();

    private static ExtractedDocument Docx(byte[] data)
    {
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("word/document.xml not found");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true });
        const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var sb = new StringBuilder();
        while (reader.Read())
        {
            if (reader.NamespaceURI != W) continue;
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "t": sb.Append(reader.ReadElementContentAsString()); break;
                    case "tab": sb.Append('\t'); break;
                    case "br" or "cr": sb.Append('\n'); break;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                switch (reader.LocalName)
                {
                    case "p": sb.Append('\n'); break;
                    case "tc": if (sb.Length > 0 && sb[^1] == '\n') sb.Length--; sb.Append(" | "); break;
                    case "tr": sb.Append('\n'); break;
                    case "tbl": sb.Append('\n'); break;
                }
            }
        }
        return new(sb.ToString(), null);
    }

    private static string PlainText(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(874).GetString(data); }
    }

    /// <summary>รวมบรรทัดว่างซ้ำ ตัดช่องว่างท้ายบรรทัด และอักขระควบคุม (คง tab / ขึ้นบรรทัด)</summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var lines = text.ReplaceLineEndings("\n").Split('\n').Select(l => new string(l.Where(c => !char.IsControl(c) || c == '\t').ToArray()).TrimEnd());
        var sb = new StringBuilder();
        var blank = 0;
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0) { blank++; continue; }
            if (sb.Length > 0) sb.Append(blank > 0 ? "\n\n" : "\n");
            sb.Append(line);
            blank = 0;
        }
        return sb.ToString();
    }
}
