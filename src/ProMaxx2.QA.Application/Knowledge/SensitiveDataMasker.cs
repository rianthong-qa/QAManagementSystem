using System.Text.RegularExpressions;

namespace ProMaxx2.QA.Application.Knowledge;

/// <summary>คำที่ต้องแทนที่เพิ่มเติมจากบริบทของแหล่งข้อมูล เช่น ชื่อลูกค้า → [CUSTOMER], สาขา → [BRANCH], ชื่อพนักงาน Support → [STAFF]</summary>
public sealed record MaskTerm(string Term, string Label);

/// <summary>
/// ปิดข้อมูลอ่อนไหวก่อนข้อความเข้าคลังความรู้ (ทำก่อนบันทึก chunk ทุกครั้ง) — ตามกติกา DEFECT_KNOWLEDGE_PLAN.md §5.4
/// ลำดับมีผล: secret → อีเมล → บัตรประชาชน 13 หลัก → บัตรเครดิต → เบอร์โทร → IP ภายใน → คำเฉพาะของแหล่งข้อมูล
/// </summary>
public static partial class SensitiveDataMasker
{
    public static string Mask(string? text, IEnumerable<MaskTerm>? terms = null)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var result = ConnectionSecret().Replace(text, m => $"{m.Groups["key"].Value}{m.Groups["sep"].Value}[SECRET]");
        result = BearerToken().Replace(result, "Bearer [SECRET]");
        result = Jwt().Replace(result, "[SECRET]");
        result = Email().Replace(result, "[EMAIL]");
        result = NationalId().Replace(result, "[NATIONAL_ID]");
        result = CreditCard().Replace(result, "[CARD]");
        result = Phone().Replace(result, "[PHONE]");
        result = PrivateIp().Replace(result, "[IP]");
        if (terms is not null)
        {
            // คำยาวก่อน เพื่อไม่ให้คำสั้นที่เป็นส่วนหนึ่งของคำยาวถูกแทนก่อน
            foreach (var term in terms.Where(x => !string.IsNullOrWhiteSpace(x.Term) && x.Term.Trim().Length >= 2).OrderByDescending(x => x.Term.Trim().Length))
                result = result.Replace(term.Term.Trim(), $"[{term.Label.Trim('[', ']').ToUpperInvariant()}]", StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    // password=..., pwd: ..., api_key=..., token=... (รวมใน connection string) — คงชื่อ key ไว้ให้อ่านบริบทได้
    [GeneratedRegex(@"(?<key>\b(?:password|passwd|pwd|secret|token|api[_-]?key|access[_-]?key|client[_-]?secret)\b)(?<sep>\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s;,""']+)", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionSecret();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9\-_\.=+/]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{5,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    // เลขบัตรประชาชน: 13 หลักติดกัน หรือรูปแบบ x-xxxx-xxxxx-xx-x
    [GeneratedRegex(@"(?<![\d.])(?:\d{13}|\d[ -]\d{4}[ -]\d{5}[ -]\d{2}[ -]\d)(?![\d.])")]
    private static partial Regex NationalId();

    // บัตรเครดิต 15–16 หลัก แบ่งกลุ่มด้วยช่องว่าง/ขีดได้
    [GeneratedRegex(@"(?<![\d.])(?:\d{4}[ -]?){3}\d{3,4}(?![\d.])")]
    private static partial Regex CreditCard();

    // เบอร์โทรไทย: มือถือ 0x-xxxx-xxxx / 0xx-xxx-xxxx, บ้าน 02-xxx-xxxx, +66
    [GeneratedRegex(@"(?<![\d.])(?:\+66[ -]?|0)(?:\d[ -]?){7,8}\d(?![\d.])")]
    private static partial Regex Phone();

    // IP ภายในองค์กร (10.x, 172.16–31.x, 192.168.x, 127.x) — เลข version 3 ส่วน เช่น 10.0.228 ไม่ถูกปิด
    [GeneratedRegex(@"(?<![\d.])(?:10|127)(?:\.\d{1,3}){3}(?![\d.])|(?<![\d.])172\.(?:1[6-9]|2\d|3[01])(?:\.\d{1,3}){2}(?![\d.])|(?<![\d.])192\.168(?:\.\d{1,3}){2}(?![\d.])")]
    private static partial Regex PrivateIp();
}
