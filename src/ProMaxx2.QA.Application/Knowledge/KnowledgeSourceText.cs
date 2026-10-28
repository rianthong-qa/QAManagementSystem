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
    string? CrmTicketId, IReadOnlyList<DefectKnowledgeTestCase> TestCases, IReadOnlyList<DefectKnowledgeComment> Comments,
    string? ResolutionType = null, string? RootCause = null, string? RootCauseNote = null, string? ResolutionNote = null, string? DuplicateOf = null);

public sealed record ArticleKnowledgeInput(string Title, string? Tags, string? ProjectName, string BodyMarkdown);
public sealed record FileKnowledgeInput(string FileName, string? Description, string? ProjectName, string Text);
public sealed record CrmKnowledgeAnswer(DateTime? At, string Text);
public sealed record TestCaseKnowledgeStep(int No, string Action, string? TestData, string ExpectedResult);

/// <summary>Test Case ฉบับปัจจุบัน (Revision ล่าสุด) + Defect ที่ลิงก์ — ใช้ตอบ "ทดสอบเรื่องนี้อย่างไร / Test Case ไหนเคยเจอปัญหา"</summary>
public sealed record TestCaseKnowledgeInput(
    string Code, string Title, string? ProjectName, string? ModulePath, string Priority, string? TestType, string Status,
    string? Objective, string? Preconditions, IReadOnlyList<TestCaseKnowledgeStep> Steps, IReadOnlyList<string> LinkedDefects);

/// <summary>CRM Ticket (ข้อความ mask แล้วตั้งแต่ตอนดึง) — ไม่มีชื่อลูกค้า/สาขา/ผู้ติดต่อ</summary>
public sealed record CrmKnowledgeInput(
    string JobNo, string? Subject, string? Status, string? JobType, string? ServiceType, string? Product, DateTime? ContactDate,
    string? Description, IReadOnlyList<CrmKnowledgeAnswer> Answers);

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
    public const int MaxCrmAnswers = 30;
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
        // ผลการปิด (Phase 6) — ใส่เฉพาะเมื่อมี เพื่อไม่ให้ hash ของ Defect ที่ยังไม่ระบุเปลี่ยน
        if (!string.IsNullOrWhiteSpace(d.ResolutionType))
            sb.Append("\nผลการปิด: ").Append(ResolutionLabel(d.ResolutionType)).Append(string.IsNullOrWhiteSpace(d.DuplicateOf) ? "" : $" (ซ้ำกับ {d.DuplicateOf})").Append('\n');
        Line(sb, "หมวดสาเหตุ", d.RootCause);
        Section(sb, "สาเหตุ", d.RootCauseNote);
        Section(sb, "วิธีแก้", d.ResolutionNote);
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

    public static KnowledgeText ForFile(FileKnowledgeInput f)
    {
        var header = $"[เอกสาร] {f.FileName}";
        var sb = new StringBuilder();
        Line(sb, "Project", f.ProjectName ?? "ความรู้กลาง (ทุก Project)");
        Line(sb, "รายละเอียด", f.Description);
        sb.Append('\n').Append(f.Text.Trim());
        return new(header, sb.ToString().Trim());
    }

    public static KnowledgeText ForCrmTicket(CrmKnowledgeInput c)
    {
        var header = $"[CRM] {c.JobNo}: {(string.IsNullOrWhiteSpace(c.Subject) ? "(ไม่มีชื่อเรื่อง)" : c.Subject.Trim())}";
        var sb = new StringBuilder();
        Line(sb, "ประเภทงาน", c.JobType);
        Line(sb, "บริการ", c.ServiceType);
        Line(sb, "สินค้า", c.Product);
        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(c.Status)) meta.Add($"สถานะ: {c.Status.Trim()}");
        if (c.ContactDate.HasValue) meta.Add($"แจ้งเมื่อ: {c.ContactDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        if (meta.Count > 0) sb.Append(string.Join(" | ", meta)).Append('\n');
        Section(sb, "รายละเอียดปัญหา", c.Description);
        // การตอบล่าสุด N รายการ เรียงตามเวลา — วิธีแก้/คำอธิบายสาเหตุมักอยู่ท้าย ๆ
        var answers = c.Answers.Where(x => !string.IsNullOrWhiteSpace(x.Text)).OrderByDescending(x => x.At ?? DateTime.MinValue).Take(MaxCrmAnswers).OrderBy(x => x.At ?? DateTime.MinValue).ToList();
        if (answers.Count > 0)
            Section(sb, "การตอบ / วิธีแก้", string.Join("\n", answers.Select(x => $"- {(x.At.HasValue ? x.At.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ": " : "")}{x.Text.Trim()}")));
        return new(header, sb.ToString().Trim());
    }

    public const int MaxLinkedDefects = 10;

    public static KnowledgeText ForTestCase(TestCaseKnowledgeInput t)
    {
        var header = $"[Test Case] {t.Code}: {t.Title}";
        var sb = new StringBuilder();
        Line(sb, "Project", t.ProjectName);
        Line(sb, "โมดูล", t.ModulePath);
        sb.Append("Priority: ").Append(t.Priority).Append(" | ประเภท: ").Append(string.IsNullOrWhiteSpace(t.TestType) ? "-" : t.TestType.Trim()).Append(" | สถานะ: ").Append(t.Status).Append('\n');
        Section(sb, "วัตถุประสงค์", t.Objective);
        Section(sb, "เงื่อนไขก่อนทดสอบ", t.Preconditions);
        if (t.Steps.Count > 0)
            Section(sb, "ขั้นตอน", string.Join("\n", t.Steps.OrderBy(x => x.No).Select(x =>
                $"{x.No}. {x.Action.Trim()}{(string.IsNullOrWhiteSpace(x.TestData) ? "" : $" (ข้อมูล: {x.TestData.Trim()})")} → ผลที่คาดหวัง: {x.ExpectedResult.Trim()}")));
        if (t.LinkedDefects.Count > 0)
            Section(sb, "Defect ที่เคยพบจาก Test Case นี้", string.Join("\n", t.LinkedDefects.Take(MaxLinkedDefects).Select(x => $"- {x}")));
        return new(header, sb.ToString().Trim());
    }

    public static string ResolutionLabel(string resolutionType) => resolutionType switch
    {
        "Fixed" => "แก้ไขแล้ว", "Duplicate" => "ซ้ำกับ Defect อื่น", "CannotReproduce" => "ทำซ้ำไม่ได้", "ByDesign" => "ทำงานตามที่ออกแบบ",
        "WontFix" => "ไม่แก้ไข", "Deferred" => "เลื่อนไปแก้ภายหลัง", _ => resolutionType,
    };

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
