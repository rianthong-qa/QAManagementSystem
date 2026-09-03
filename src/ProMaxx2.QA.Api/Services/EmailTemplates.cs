using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ProMaxx2.QA.Api.Services;

// HTML email templates — kept separate from the services that trigger them so the (fairly long, inline-styled-for-
// email-client-compatibility) markup doesn't clutter the orchestration logic. Every user-supplied string is
// HTML-encoded before being embedded — Description/StepsToReproduce/etc. are free text QA typed into Defect
// fields, never trusted as markup.
public static class EmailTemplates
{
    private const string Primary = "#2457d6";
    private const string Ink = "#1f2430";
    private const string Muted = "#667085";
    private const string Line = "#e5e7eb";
    private const string BgSoft = "#f6f8fb";

    public static string DefectAssignedViaCrm(
        string defectCode, string title, string severity, string status, string projectName, string? moduleName,
        string? description, string? stepsToReproduce, string? expectedResult, string? actualResult,
        string devName, string devStaffCode, string jobNo, string ticketLink)
    {
        var (severityBg, severityFg) = SeverityColors(severity);

        var moduleRow = string.IsNullOrWhiteSpace(moduleName) ? "" : Row("Module", Html(moduleName));
        var sb = new StringBuilder();
        sb.Append($$"""
        <div style="background:{{BgSoft}};padding:32px 16px;font-family:'Segoe UI',Tahoma,Arial,sans-serif;">
          <table role="presentation" width="100%" style="max-width:600px;margin:0 auto;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid {{Line}};">
            <tr><td style="background:{{Primary}};padding:20px 28px;">
              <span style="color:#ffffff;font-size:18px;font-weight:700;letter-spacing:.3px;">QA Hub</span>
              <div style="color:#dbe6ff;font-size:13px;margin-top:2px;">มอบหมายงานใหม่ผ่าน CRM</div>
            </td></tr>
            <tr><td style="padding:28px;">
              <div style="color:{{Muted}};font-size:12px;font-weight:700;letter-spacing:.4px;text-transform:uppercase;">{{Html(defectCode)}}</div>
              <div style="color:{{Ink}};font-size:20px;font-weight:700;margin:4px 0 14px;">{{Html(title)}}</div>
              <div>
                <span style="display:inline-block;background:{{severityBg}};color:{{severityFg}};font-size:12px;font-weight:700;padding:4px 10px;border-radius:999px;margin-right:6px;">{{Html(severity)}}</span>
                <span style="display:inline-block;background:{{BgSoft}};color:{{Muted}};font-size:12px;font-weight:700;padding:4px 10px;border-radius:999px;">{{Html(status)}}</span>
              </div>
              <table role="presentation" width="100%" style="margin-top:18px;border-collapse:collapse;font-size:13px;">
                {{Row("Project", Html(projectName))}}
                {{moduleRow}}
                {{Row("มอบหมายให้", $"{Html(devName)} ({Html(devStaffCode)})")}}
                {{Row("CRM Ticket", $"#{Html(jobNo)}")}}
              </table>
        """);

        AppendSection(sb, "รายละเอียด", description);
        AppendStepsSection(sb, stepsToReproduce);
        AppendSection(sb, "ผลที่คาดหวัง", expectedResult);
        AppendSection(sb, "ผลที่เกิดขึ้นจริง", actualResult);

        sb.Append($$"""
              <div style="text-align:center;margin-top:26px;">
                <a href="{{ticketLink}}" style="display:inline-block;background:{{Primary}};color:#ffffff;font-size:14px;font-weight:700;text-decoration:none;padding:12px 28px;border-radius:8px;">เปิด Ticket ใน CRM →</a>
              </div>
            </td></tr>
            <tr><td style="padding:16px 28px;background:{{BgSoft}};border-top:1px solid {{Line}};">
              <span style="color:{{Muted}};font-size:11px;">อีเมลนี้ส่งอัตโนมัติจาก QA Hub เมื่อมีการส่ง Defect เข้า CRM — ไม่ต้องตอบกลับอีเมลนี้</span>
            </td></tr>
          </table>
        </div>
        """);
        return sb.ToString();
    }

    // Phase 3 trigger #2 — CrmSyncService.NotifyReturnedToOwnerAsync ยิงตอน poller เจอ Assignto บน CRM ticket
    // กลับไปเท่ากับ OwnerSubjectId (คนที่กด "ส่งไป CRM" ตอนแรก) แปลว่า Dev ส่งงานกลับมาให้ตรวจ — สีหัวเป็นเหลือง/ส้ม
    // (ไม่ใช่น้ำเงินเหมือน trigger #1) เพื่อให้แยกออกจากอีเมล "มอบหมายงานใหม่" ได้ทันทีจากสีตอนเปิดกล่องจดหมาย
    public static string CrmReturnedToOwner(
        string defectCode, string title, string severity, string status, string projectName, string? moduleName,
        string? crmStatus, string jobNo, string ticketLink)
    {
        const string AmberBg = "#d97706";
        var (severityBg, severityFg) = SeverityColors(severity);
        var moduleRow = string.IsNullOrWhiteSpace(moduleName) ? "" : Row("Module", Html(moduleName));
        var crmStatusRow = string.IsNullOrWhiteSpace(crmStatus) ? "" : Row("สถานะล่าสุดใน CRM", CrmStatusPillHtml(crmStatus));

        var sb = new StringBuilder();
        sb.Append($$"""
        <div style="background:{{BgSoft}};padding:32px 16px;font-family:'Segoe UI',Tahoma,Arial,sans-serif;">
          <table role="presentation" width="100%" style="max-width:600px;margin:0 auto;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid {{Line}};">
            <tr><td style="background:{{AmberBg}};padding:20px 28px;">
              <span style="color:#ffffff;font-size:18px;font-weight:700;letter-spacing:.3px;">QA Hub</span>
              <div style="color:#fff3e0;font-size:13px;margin-top:2px;">↩ CRM ส่งเคสกลับมาหาคุณ</div>
            </td></tr>
            <tr><td style="padding:28px;">
              <div style="color:{{Muted}};font-size:12px;font-weight:700;letter-spacing:.4px;text-transform:uppercase;">{{Html(defectCode)}}</div>
              <div style="color:{{Ink}};font-size:20px;font-weight:700;margin:4px 0 14px;">{{Html(title)}}</div>
              <div>
                <span style="display:inline-block;background:{{severityBg}};color:{{severityFg}};font-size:12px;font-weight:700;padding:4px 10px;border-radius:999px;margin-right:6px;">{{Html(severity)}}</span>
                <span style="display:inline-block;background:{{BgSoft}};color:{{Muted}};font-size:12px;font-weight:700;padding:4px 10px;border-radius:999px;">{{Html(status)}}</span>
              </div>
              <table role="presentation" width="100%" style="margin-top:18px;border-collapse:collapse;font-size:13px;">
                {{Row("Project", Html(projectName))}}
                {{moduleRow}}
                {{Row("CRM Ticket", $"#{Html(jobNo)}")}}
                {{crmStatusRow}}
              </table>
              <div style="margin-top:18px;background:#fff8ec;border:1px solid #f5dfb8;border-radius:8px;padding:12px 14px;font-size:13px;line-height:1.6;color:#8a5a08;">
                Ticket นี้ถูกโอนความรับผิดชอบกลับมาที่คุณแล้ว (คนที่ส่ง Defect นี้เข้า CRM) — กรุณาเข้าไปตรวจสอบความคืบหน้าและดำเนินการต่อ
              </div>
              <div style="text-align:center;margin-top:26px;">
                <a href="{{ticketLink}}" style="display:inline-block;background:{{Primary}};color:#ffffff;font-size:14px;font-weight:700;text-decoration:none;padding:12px 28px;border-radius:8px;">เปิด Ticket ใน CRM →</a>
              </div>
            </td></tr>
            <tr><td style="padding:16px 28px;background:{{BgSoft}};border-top:1px solid {{Line}};">
              <span style="color:{{Muted}};font-size:11px;">อีเมลนี้ส่งอัตโนมัติจาก QA Hub เมื่อ CRM ส่งเคสกลับมาหาเจ้าของเรื่อง — ไม่ต้องตอบกลับอีเมลนี้</span>
            </td></tr>
          </table>
        </div>
        """);
        return sb.ToString();
    }

    // สีของสถานะ ticket ฝั่ง CRM (9 สถานะ ดู JobDetail.txt: Open/Continue/Approve/Develop/Planning/Test/EditErr/
    // Finish/Close) — เขียว = จบงานแล้ว, แดง = ติดปัญหา, ส้ม = กำลังดำเนินการ, น้ำเงิน = เพิ่งเข้าคิว/รับเรื่อง
    private static (string Bg, string Fg) CrmStatusColors(string status) => status switch
    {
        "Finish" or "Close" => ("#eaf8f1", "#168b58"),
        "EditErr" => ("#fdecec", "#c83a3a"),
        "Approve" or "Develop" or "Planning" or "Test" => ("#fff4e5", "#b45309"),
        "Open" or "Continue" => ("#eaf0ff", Primary),
        _ => ("#eef1f6", Muted),
    };

    private static string CrmStatusPillHtml(string status)
    {
        var (bg, fg) = CrmStatusColors(status);
        return $"""<span style="display:inline-block;background:{bg};color:{fg};font-size:12px;font-weight:700;padding:3px 10px;border-radius:999px;">{Html(status)}</span>""";
    }

    private static (string Bg, string Fg) SeverityColors(string severity) => severity switch
    {
        "Critical" or "High" => ("#fdecec", "#d64545"),
        "Medium" => ("#fff4e5", "#b45309"),
        _ => ("#eef1f6", Muted),
    };

    private static void AppendSection(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var html = Html(value).Replace("\n", "<br/>");
        sb.Append($$"""
              <div style="margin-top:18px;">
                <div style="color:{{Muted}};font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.4px;margin-bottom:4px;">{{label}}</div>
                <div style="color:{{Ink}};font-size:13px;line-height:1.6;background:{{BgSoft}};border:1px solid {{Line}};border-radius:8px;padding:12px 14px;">{{html}}</div>
              </div>
        """);
    }

    private sealed record ReproStep(int StepNo, string Action, string? Status, string Detail);

    // "Steps to Reproduce" ในหน้า Defect Detail (App.tsx: parseReproSteps) — ถ้าเขียนตามรูปแบบ
    // "1. Action (Pass/Fail) | รายละเอียด" จะ parse แล้วเรนเดอร์เป็นการ์ดลำดับขั้นตอนพร้อม badge ผลลัพธ์
    // (เหมือนที่เห็นในหน้า Defect Detail จริง) — ต้อง mirror logic เดียวกันฝั่ง C# เพราะอีเมลสร้างที่ backend
    // ไม่ผ่าน React เลยเรียกฟังก์ชันเดิมไม่ได้ ถ้า parse ไม่ได้ (ไม่ใช่ freeform text ตามรูปแบบนี้) ก็ fallback
    // ไปใช้กล่องข้อความธรรมดาแบบเดียวกับ AppendSection
    private static List<ReproStep>? ParseReproSteps(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return null;
        var steps = new List<ReproStep>();
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"^(\d+)[.)]\s*(.+)$");
            if (!m.Success) return null;
            var stepNo = int.Parse(m.Groups[1].Value);
            var rest = m.Groups[2].Value;
            var statusMatch = Regex.Match(rest, @"^(.*?)\s*\((Pass|Fail)\)\s*(?:\|\s*(.*))?$");
            if (statusMatch.Success)
                steps.Add(new ReproStep(stepNo, statusMatch.Groups[1].Value.Trim(), statusMatch.Groups[2].Value, statusMatch.Groups[3].Value.Trim()));
            else
            {
                var parts = rest.Split('|');
                steps.Add(new ReproStep(stepNo, parts[0].Trim(), null, string.Join("|", parts.Skip(1)).Trim()));
            }
        }
        return steps;
    }

    private static void AppendStepsSection(StringBuilder sb, string? stepsToReproduce)
    {
        if (string.IsNullOrWhiteSpace(stepsToReproduce)) return;
        var steps = ParseReproSteps(stepsToReproduce);
        if (steps is null) { AppendSection(sb, "ขั้นตอนการทำซ้ำ", stepsToReproduce); return; }

        sb.Append($$"""
              <div style="margin-top:18px;">
                <div style="color:{{Muted}};font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.4px;margin-bottom:8px;">ขั้นตอนการทำซ้ำ</div>
        """);
        foreach (var s in steps)
        {
            var isFail = s.Status == "Fail";
            var rowBorder = isFail ? "#f3c6c6" : Line;
            var rowBg = isFail ? "#fef6f6" : "#ffffff";
            var detailHtml = s.Detail.Length > 0 ? $"""<span style="color:{Muted};"> {Html(s.Detail)}</span>""" : "";
            var badgeHtml = s.Status is { } status ? BadgeHtml(status) : "";
            sb.Append($$"""
                <div style="display:table;width:100%;table-layout:fixed;border:1px solid {{rowBorder}};border-radius:10px;background:{{rowBg}};padding:9px 10px;margin-bottom:8px;">
                  <div style="display:table-cell;width:30px;vertical-align:top;">
                    <span style="display:inline-block;width:22px;height:22px;line-height:22px;text-align:center;border-radius:50%;background:#eef3ff;color:{{Primary}};font-size:11px;font-weight:800;">{{s.StepNo}}</span>
                  </div>
                  <div style="display:table-cell;vertical-align:top;font-size:13px;line-height:1.55;color:{{Ink}};padding-right:8px;">
                    <b>{{Html(s.Action)}}</b>{{detailHtml}}
                  </div>
                  <div style="display:table-cell;width:56px;vertical-align:top;text-align:right;white-space:nowrap;">{{badgeHtml}}</div>
                </div>
            """);
        }
        sb.Append("      </div>\n");
    }

    private static string BadgeHtml(string status)
    {
        var (bg, fg) = status == "Pass" ? ("#eaf8f1", "#168b58") : ("#fdecec", "#c83a3a");
        return $"""<span style="display:inline-block;background:{bg};color:{fg};font-size:11px;font-weight:700;padding:3px 8px;border-radius:999px;">{Html(status)}</span>""";
    }

    private static string Row(string label, string valueHtml) => $"""
        <tr>
          <td style="padding:6px 0;color:{Muted};width:120px;vertical-align:top;">{label}</td>
          <td style="padding:6px 0;color:{Ink};font-weight:600;">{valueHtml}</td>
        </tr>
        """;

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
