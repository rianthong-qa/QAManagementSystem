using System.Globalization;
using System.Text;

namespace ProMaxx2.QA.Application.Knowledge;

public sealed record DefectKnowledgeComment(DateTime At, string Text);
public sealed record DefectKnowledgeTestCase(string Code, string Title);

/// <summary>ข้อมูลที่ใช้สร้างความรู้จาก Defect หนึ่งรายการ (รวบรวมจากหลายตารางโดย KnowledgeIndexService)</summary>
public sealed record DefectKnowledgeInput(
    string DefectCode, string Title, string Severity, string Status, DateTime CreatedAt,
    string? ProjectName, string? ModulePath, string? Release, string? Build,
    string? Description, string? StepsToReproduce, string? ExpectedResult, string? ActualResult,
    string? CrmTicketId, IReadOnlyList<DefectKnowledgeTestCase> TestCases, IReadOnlyList<DefectKnowledgeComment> Comments);

public sealed record ArticleKnowledgeInput(string Title, string? Tags, string? ProjectName, string BodyMarkdown);

/// <summary>ข้อความความรู้ของแหล่งหนึ่ง: Header = บรรทัดระบุตัวตน (ติดไปกับทุก chunk), Body = เนื้อหาที่ถูกแบ่ง</summary>
public sealed record KnowledgeText(string Header, string Body)
{
    /// <summary>ข้อความเต็มที่ใช้คำนวณ ContentHash (เปลี่ยนเมื่อ header หรือเนื้อหาเปลี่ยน)</summary>
    public string Full => $"{Header}\n\n{Body}";
}

/// <summary>
/// สร้างข้อความความรู้รูปแบบคงที่ (canonical) ต่อแหล่งข้อมูล — รูปแบบคงที่ช่วยให้ค้น/เทียบ embedding ได้ยุติธรรม และ AI อ้างอิงได้ตรง
/// ไม่เรียก AI และไม่ mask (mask ทำใน <see cref="KnowledgeChunkPlanner"/>)
/// </summary>
public static class KnowledgeSourceText
{
    public const int MaxComments = 20;
    // วันที่แบบ ค.ศ. คงที่ทุกเครื่อง (culture th-TH ของ server จะได้ พ.ศ. และทำให้ hash เปลี่ยนตามเครื่อง)

    public static KnowledgeText ForDefect(DefectKnowledgeInput d)
    {
        var header = $"[Defect] {d.DefectCode}: {d.Title}";
        var sb = new StringBuilder();
        Line(sb, "Project", d.ProjectName);
        Line(sb, "โมดูล", d.ModulePath);
        Line(sb, "Release", d.Release);
        Line(sb, "Build", d.Build);
        sb.Append("ความรุนแรง: ").Append(d.Severity).Append(" | สถานะ: ").Append(d.Status).Append(" | แจ้งเมื่อ: ").Append(d.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        Line(sb, "CRM Ticket", d.CrmTicketId);
        Section(sb, "รายละเอียด", d.Description);
        Section(sb, "ขั้นตอนการทำซ้ำ", d.StepsToReproduce);
        Section(sb, "ผลที่คาดหวัง", d.ExpectedResult);
        Section(sb, "ผลที่เกิดขึ้นจริง", d.ActualResult);
        if (d.TestCases.Count > 0)
            Section(sb, "Test Case ที่เกี่ยวข้อง", string.Join("\n", d.TestCases.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase).Select(x => $"- {x.Code} {x.Title}")));
        // คอมเมนต์ล่าสุด N รายการ เรียงตามเวลา (การสนทนาเรื่องสาเหตุ/วิธีแก้มักอยู่ท้าย ๆ)
        var comments = d.Comments.Where(x => !string.IsNullOrWhiteSpace(x.Text)).OrderByDescending(x => x.At).Take(MaxComments).OrderBy(x => x.At).ToList();
        if (comments.Count > 0)
            Section(sb, "คอมเมนต์", string.Join("\n", comments.Select(x => $"- {x.At.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}: {x.Text.Trim()}")));
        return new(header, sb.ToString().Trim());
    }

    public static KnowledgeText ForArticle(ArticleKnowledgeInput a)
    {
        var header = $"[บทความ] {a.Title}";
        var sb = new StringBuilder();
        Line(sb, "Project", a.ProjectName ?? "ความรู้กลาง (ทุก Project)");
        Line(sb, "Tag", a.Tags);
        sb.Append('\n').Append(a.BodyMarkdown.Trim());
        return new(header, sb.ToString().Trim());
    }

    private static void Line(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) sb.Append(label).Append(": ").Append(value.Trim()).Append('\n');
    }

    private static void Section(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append('\n').Append(label).Append(":\n").Append(value.Trim()).Append('\n');
    }
}

/// <summary>mask → แบ่ง chunk → ติด header ให้ทุก chunk (เพื่อให้ผลค้นแต่ละช่วงรู้ว่ามาจากแหล่งไหน)</summary>
public static class KnowledgeChunkPlanner
{
    public static IReadOnlyList<string> Plan(KnowledgeText text, IEnumerable<MaskTerm>? terms = null, int maxChars = TextChunker.DefaultMaxChars, int overlapChars = TextChunker.DefaultOverlapChars)
    {
        var termList = terms?.ToList();
        var header = SensitiveDataMasker.Mask(TextChunker.Normalize(text.Header), termList);
        var body = SensitiveDataMasker.Mask(text.Body, termList);
        var bodyMax = Math.Max(200, maxChars - header.Length - 2);
        var pieces = TextChunker.Split(body, bodyMax, Math.Min(overlapChars, bodyMax / 2 - 1));
        if (pieces.Count == 0) return [header];
        return pieces.Select(p => $"{header}\n{p}").ToList();
    }
}
