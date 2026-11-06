using System.Text.RegularExpressions;

namespace ProMaxx2.QA.Api.Services;

/// <summary>ข้อมูลของ Defect ที่ใช้สร้าง Description ของ CRM Ticket — ค่าดิบจาก Defect + ชื่อที่ resolve จากฐานข้อมูลแล้ว</summary>
public sealed record CrmDescriptionInput(
    string DefectCode, string Severity, string? ModuleName, string? BuildNumber,
    string? Description, string? StepsToReproduce, string? ExpectedResult, string? ActualResult,
    string? TestCaseCode, int ImageCount, int SkippedImages, string ShareUrl);

/// <summary>
/// Description ของ CRM Ticket ที่ส่งจาก Defect — เรียงตามสิ่งที่ Dev ต้องรู้ก่อน: หัว → [ปัญหาที่พบ] → [ผลที่ควรเป็น] → [ขั้นตอนทำซ้ำ] → [อ้างอิง] + ลิงก์
/// (เดิมส่ง Description ทั้งก้อนที่ขึ้นต้นด้วยข้อมูลประกอบของ Cycle ทำให้ปัญหาจริงอยู่ท้าย และถูกตัดที่ 1000 ตัวอักษรบ่อย)
/// เกินลิมิต CRM: ย่อขั้นตอนเหลือขั้นที่ Fail + ขั้นก่อนหน้า → ตัดข้อมูลประกอบ → ตัดขั้นตอน → ตัดท้ายปัญหา/ผลที่ควรเป็น — ลิงก์อยู่ครบเสมอ
/// Defect ที่ระบบสร้างจาก Execution/Automation อ่านค่าจากบรรทัด "ป้าย: ค่า" ที่ DefectAutoCreateService/AutomationDefectService เขียนไว้;
/// Defect ที่คีย์เองใช้ข้อความในช่องตามเดิม หัวข้อที่ไม่มีข้อมูลไม่แสดง
/// </summary>
public static partial class CrmDefectDescription
{
    public const int MaxLength = 1000;

    public static string Build(CrmDescriptionInput x)
    {
        var parsed = Parse(x.Description);
        var actual = Clean(x.ActualResult);
        var problem = new List<string>();
        if (parsed.Generated)
        {
            if (actual is not null) problem.Add(actual);
            problem.AddRange(parsed.FailedSteps.Where(s => actual is null || !s.Contains(actual, StringComparison.Ordinal)));
        }
        else
        {
            if (Clean(x.Description) is { } text) problem.Add(text);
            // มีรายละเอียดอยู่แล้วค่อยใส่ป้าย "ผลจริง:" แยก — มีแต่ผลจริงอย่างเดียวแสดงเป็นข้อความปัญหาเลย
            if (actual is not null && !problem.Any(p => p.Contains(actual, StringComparison.Ordinal))) problem.Add(problem.Count > 0 ? $"ผลจริง: {actual}" : actual);
        }
        var steps = ParseSteps(x.StepsToReproduce);
        var extras = new List<string>();
        if (steps.TestData.Count > 0) extras.Add($"ข้อมูลทดสอบ: {string.Join(", ", steps.TestData)}");
        if (parsed.Get("คอมเมนต์") is { } comment) extras.Add($"หมายเหตุผู้ทดสอบ: {comment}");
        var tested = string.Join(" ", new[] { parsed.Get("ผู้ทดสอบ") is { } t ? $"ทดสอบโดย {t}" : null, parsed.Get("เวลาที่ทดสอบ") }.Where(v => v is not null));

        var header = $"[QA Hub] {x.DefectCode} · ความรุนแรง {x.Severity}";
        var context = Join(" · ", x.ModuleName is { } m && m.Length > 0 ? $"โมดูล {m}" : null, Clean(x.BuildNumber ?? parsed.Get("Build")) is { } b ? $"Build {b}" : null, parsed.Get("Environment"));
        var reference = Join(" · ", Clean(x.TestCaseCode), parsed.Reference);
        var images = Join(" · ", x.ImageCount > 0 ? $"แนบรูป {x.ImageCount} รูป" : null, x.SkippedImages > 0 ? $"รูปอีก {x.SkippedImages} รูปดูในลิงก์" : null);

        string Compose(string problemText, string? expectedText, IReadOnlyList<string>? stepLines, bool withExtras, bool shortened)
        {
            var parts = new List<string> { context is null ? header : $"{header}\n{context}" };
            if (problemText.Length > 0) parts.Add($"[ปัญหาที่พบ]\n{problemText}");
            if (expectedText is not null) parts.Add($"[ผลที่ควรเป็น]\n{expectedText}");
            var pre = withExtras ? parsed.Get("เงื่อนไขก่อนเริ่ม") : null;
            if (stepLines is { Count: > 0 } || pre is not null)
                parts.Add("[ขั้นตอนทำซ้ำ]\n" + string.Join("\n", new[] { pre is null ? null : $"ก่อนเริ่ม: {pre}" }.Concat(stepLines ?? []).Concat(withExtras ? extras : []).Where(v => v is not null)));
            else if (withExtras && extras.Count > 0) parts.Add(string.Join("\n", extras));
            var refLines = new[] { reference, withExtras && tested.Length > 0 ? tested : null, images }.Where(v => v is not null).ToList();
            if (refLines.Count > 0) parts.Add("[อ้างอิง]\n" + string.Join("\n", refLines));
            parts.Add(shortened ? $"บางส่วนถูกย่อ — ดูฉบับเต็ม: {x.ShareUrl}" : $"รายละเอียดเต็ม: {x.ShareUrl}");
            return string.Join("\n\n", parts);
        }

        var problemText = string.Join("\n", problem);
        var expected = Clean(x.ExpectedResult);
        foreach (var (stepLines, withExtras, shortened) in new (IReadOnlyList<string>?, bool, bool)[]
        {
            (steps.Lines, true, false), (steps.Reduced, true, true), (steps.Reduced, false, true), (null, false, true),
        })
        {
            var text = Compose(problemText, expected, stepLines, withExtras, shortened || (stepLines is null && steps.Lines.Count > 0));
            if (text.Length <= MaxLength) return text;
        }
        // ปัญหา/ผลที่ควรเป็นยาวเกินเอง (มักเป็น Defect ที่คีย์เอง) — แบ่งที่ว่างให้ผลที่ควรเป็นไม่เกิน 1/3 แล้วตัดท้ายทั้งสองส่วน
        // วัด overhead ด้วยข้อความ 1 ตัวอักษร (ข้อความว่าง = ไม่มีหัวข้อ [ปัญหาที่พบ] จะนับที่ว่างเกินแล้วไปตัดลิงก์ทิ้ง)
        var room = MaxLength - Compose("x", expected is null ? null : "x", null, false, true).Length + (expected is null ? 1 : 2);
        var expectedRoom = expected is null ? 0 : Math.Min(expected.Length, Math.Max(0, room / 3));
        var final = Compose(Cut(problemText, room - expectedRoom), expected is null ? null : Cut(expected, expectedRoom), null, false, true);
        return final.Length <= MaxLength ? final : final[..MaxLength];
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : max <= 1 ? "" : text[..(max - 1)].TrimEnd() + "…";
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) || value.Trim() == "-" ? null : value.Trim();
    private static string? Join(string separator, params string?[] values)
    {
        var kept = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return kept.Count == 0 ? null : string.Join(separator, kept);
    }

    private sealed record ParsedDescription(bool Generated, Dictionary<string, string> Fields, List<string> FailedSteps, string? Reference)
    {
        public string? Get(string label) => Fields.TryGetValue(label, out var v) ? Clean(v) : null;
    }

    // ป้ายไม่มี ":" หรือ "/" — กันเวลา "10:26" และ URL ถูกตัดเป็นป้าย (กติกาเดียวกับ components/DefectDescription.tsx)
    [GeneratedRegex(@"^([^:/]{1,40}):\s*(.*)$")] private static partial Regex LabeledLine();
    [GeneratedRegex(@"^ขั้นที่\s*(\d+)\s*:\s*(.*)$")] private static partial Regex FailedStepLine();
    [GeneratedRegex(@"^(\S+)\s+\((.*)\)\s*—\s*(Run #\d+)$")] private static partial Regex CycleLine();

    private static ParsedDescription Parse(string? description)
    {
        var lines = (description ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var generated = lines.Count > 0 && (lines[0].StartsWith("Test Cycle:", StringComparison.Ordinal) || lines[0].StartsWith("Automation:", StringComparison.Ordinal));
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var failed = new List<string>();
        if (!generated) return new(false, fields, failed, null);
        foreach (var line in lines)
        {
            if (line.StartsWith('-'))
            {
                var item = line.TrimStart('-', ' ');
                var step = FailedStepLine().Match(item);
                // "ขั้นที่ 1: ลบจังหวัด… — ผลจริง: ระบบแจ้ง…" → "ขั้นที่ 1 ลบจังหวัด…: ระบบแจ้ง…"
                failed.Add(step.Success ? FormatFailedStep(step.Groups[1].Value, step.Groups[2].Value) : item);
                continue;
            }
            var labeled = LabeledLine().Match(line);
            if (!labeled.Success) continue;
            // "Build: x | Environment: y" (Automation) = หลายค่าในบรรทัดเดียว
            foreach (var part in labeled.Value.Split(" | "))
            {
                var pair = LabeledLine().Match(part);
                if (pair.Success) fields.TryAdd(pair.Groups[1].Value.Trim(), pair.Groups[2].Value.Trim());
            }
        }
        string? reference = null;
        if (fields.TryGetValue("Test Cycle", out var cycle))
        {
            var m = CycleLine().Match(cycle);
            reference = m.Success ? $"{m.Groups[1].Value} {m.Groups[3].Value}" : cycle;
        }
        else if (fields.TryGetValue("Automation", out var automation)) reference = automation.Split(" (")[0];
        return new(true, fields, failed, reference);
    }

    private static string FormatFailedStep(string stepNo, string detail)
    {
        var at = detail.IndexOf(" — ผลจริง:", StringComparison.Ordinal);
        if (at < 0) return $"ขั้นที่ {stepNo} {detail} — ไม่ผ่าน";
        return $"ขั้นที่ {stepNo} {detail[..at].Trim()}: {detail[(at + " — ผลจริง:".Length)..].Trim()}";
    }

    private sealed record ParsedSteps(List<string> Lines, List<string> Reduced, List<string> TestData);

    // "1. ลบจังหวัด… (Fail) | ข้อมูล: x | คาดหวัง: y | [ขั้นนี้ล้มเหลว]" (Execution) / "1. click_save (Fail) | error" (Automation)
    // / "1. เปิดบิลขาย | ข้อมูล: x | คาดหวัง: y" (สร้างจาก Test Case ไม่มีสถานะ) — เหลือชื่อขั้น, ข้อมูลรวมไว้ที่ "ข้อมูลทดสอบ", คาดหวังรายขั้นไม่ส่ง
    [GeneratedRegex(@"^(\d+)\.\s*(.*?)\s*\((Pass|Fail|Blocked|NotRun|Skipped|InProgress)\)\s*(?:\|\s*(.*))?$")] private static partial Regex GeneratedStepLine();

    private static ParsedSteps ParseSteps(string? text)
    {
        var raw = (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var lines = new List<string>();
        var failedIndexes = new List<int>();
        var data = new List<string>();
        foreach (var line in raw)
        {
            var m = GeneratedStepLine().Match(line);
            var pipe = m.Success ? -1 : line.IndexOf(" | ", StringComparison.Ordinal);
            if (!m.Success && (pipe < 0 || !char.IsDigit(line[0]))) { lines.Add(line); continue; }
            var failed = m.Success && m.Groups[3].Value == "Fail";
            foreach (var part in (m.Success ? m.Groups[4].Value : line[(pipe + 3)..]).Split(" | ").Select(x => x.Trim()))
                if (part.StartsWith("ข้อมูล:", StringComparison.Ordinal) && Clean(part["ข้อมูล:".Length..]) is { } d && !data.Contains(d)) data.Add(d);
            if (failed) failedIndexes.Add(lines.Count);
            lines.Add(m.Success ? $"{m.Groups[1].Value}. {m.Groups[2].Value}{(failed ? "  ← Fail" : "")}" : line[..pipe].Trim());
        }
        // ย่อ: เก็บขั้นที่ Fail กับขั้นก่อนหน้า ขั้นอื่นรวมเป็น "(อีก n ขั้น ดูในลิงก์)" — ไม่รู้ว่าขั้นไหน Fail (คีย์เอง) เก็บ 3 ขั้นแรก
        var keep = failedIndexes.Count > 0 ? failedIndexes.SelectMany(i => new[] { i - 1, i }).Where(i => i >= 0).ToHashSet() : Enumerable.Range(0, Math.Min(3, lines.Count)).ToHashSet();
        var reduced = new List<string>();
        var skipped = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            if (keep.Contains(i))
            {
                if (skipped > 0) { reduced.Add($"(อีก {skipped} ขั้น ดูในลิงก์)"); skipped = 0; }
                reduced.Add(lines[i]);
            }
            else skipped++;
        }
        if (skipped > 0) reduced.Add($"(อีก {skipped} ขั้น ดูในลิงก์)");
        return new(lines, reduced, data);
    }
}
