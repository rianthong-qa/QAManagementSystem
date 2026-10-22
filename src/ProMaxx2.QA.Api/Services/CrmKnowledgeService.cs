using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Domain.Knowledge;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>ข้อมูลดิบของ Ticket หนึ่งใบจาก CRM (ยังไม่ mask) — Terms = ชื่อลูกค้า/สาขา/ผู้ติดต่อ/พนักงานที่ต้องปิดในข้อความ</summary>
public sealed record CrmKnowledgeRaw(string? Description, IReadOnlyList<MaskTerm> Terms, IReadOnlyList<CrmHelpDeskAnswer> Answers);

/// <summary>แหล่งดึงข้อความ CRM (แยกเป็น interface เพื่อทดสอบได้โดยไม่เรียก CRM จริง) — คืน null เมื่อไม่พบ Ticket</summary>
public interface ICrmKnowledgeSource
{
    Task<CrmKnowledgeRaw?> FetchAsync(Guid accountUserId, string jobNo, string jobType, CancellationToken ct);
}

public sealed class CrmApiKnowledgeSource(CrmApiClient crm) : ICrmKnowledgeSource
{
    public async Task<CrmKnowledgeRaw?> FetchAsync(Guid accountUserId, string jobNo, string jobType, CancellationToken ct)
    {
        JsonElement? detail;
        try { detail = await crm.FindJobDetailAsync(accountUserId, jobNo, jobType, ct); }
        catch (CrmIntegrationException ex) when (ex.RemoteStatusCode == HttpStatusCode.NotFound) { detail = null; }
        var answers = await crm.GetHelpDeskAnswersAsync(accountUserId, jobNo, ct);
        if (detail is null && answers.Count == 0) return null;
        var description = detail is { } raw ? CrmTicketListParser.ReadString(CrmTicketListParser.TicketRecord(raw), "description", "Description", "detail", "Detail") : null;
        return new(description, CrmKnowledgeParser.MaskTerms(detail, answers), answers);
    }
}

/// <summary>แปลงข้อมูล CRM เป็นข้อความความรู้ — เลือกคำตอบที่เป็นข้อความ, ล้าง HTML, รวบรวมคำที่ต้อง mask</summary>
public static partial class CrmKnowledgeParser
{
    public const int MaxAnswerChars = 4000;

    // ช่องชื่อบุคคล (แยกคำเพื่อปิดชื่อเล่น/ชื่อต้นที่ถูกเรียกในข้อความด้วย) และช่ององค์กร/สาขา (ปิดทั้งค่า)
    private static readonly string[] PersonFields = ["fname", "lname", "nickName", "nickname", "followUpNane", "followUpName", "contactName"];
    private static readonly string[] CustomerFields = ["customerName", "customerFrs", "member", "companyName"];
    private static readonly string[] BranchFields = ["branchName", "BranchName"];
    private static readonly string[] ContactFields = ["email", "tel", "fax", "mobile", "phone"];
    private static readonly string[] StaffFields = ["assignto", "assignTo", "posted", "owner", "ownerName", "sysDevelop", "developer"];
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "คุณ", "นาย", "นาง", "นางสาว", "บริษัท", "จำกัด", "มหาชน", "ห้างหุ้นส่วน", "ร้าน", "สาขา", "สำนักงาน", "ใหญ่", "co.", "ltd.", "co.,ltd.", "company", "limited", "the", "shop",
    };

    public static IReadOnlyList<MaskTerm> MaskTerms(JsonElement? detail, IReadOnlyList<CrmHelpDeskAnswer> answers)
    {
        var terms = new List<MaskTerm>();
        if (detail is { } raw)
        {
            var record = CrmTicketListParser.TicketRecord(raw);
            if (record.ValueKind == JsonValueKind.Object)
            {
                var first = CrmTicketListParser.ReadString(record, "fname");
                var last = CrmTicketListParser.ReadString(record, "lname");
                if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last)) AddName(terms, $"{first.Trim()} {last.Trim()}", "CUSTOMER");
                foreach (var field in PersonFields) AddName(terms, CrmTicketListParser.ReadString(record, field), "CUSTOMER");
                foreach (var field in CustomerFields) AddWhole(terms, CrmTicketListParser.ReadString(record, field), "CUSTOMER");
                foreach (var field in BranchFields) AddWhole(terms, CrmTicketListParser.ReadString(record, field), "BRANCH");
                foreach (var field in ContactFields) AddWhole(terms, CrmTicketListParser.ReadString(record, field), "CONTACT");
                foreach (var field in StaffFields) AddName(terms, StaffName(CrmTicketListParser.ReadString(record, field)), "STAFF");
            }
        }
        foreach (var answer in answers) AddName(terms, StaffName(answer.Posted), "STAFF");
        return terms.DistinctBy(x => x.Term, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>"เหรียญทอง เจือบุญ (6101)" → "เหรียญทอง เจือบุญ"; รหัสล้วน → null</summary>
    public static string? StaffName(string? raw)
    {
        var text = StaffCodeSuffix().Replace(raw?.Trim() ?? "", "").Trim();
        return text.Length == 0 || text.All(c => char.IsAsciiDigit(c) || c == '-') ? null : text;
    }

    private static void AddWhole(List<MaskTerm> terms, string? value, string label)
    {
        var text = value?.Trim();
        // ข้ามค่าว่าง/รหัสตัวเลข/คำสั้น — การแทนเป็นแบบ substring คำไทยสั้น ๆ (เช่นชื่อเล่น "ชาย") จะไปทับคำอื่นในข้อความ
        if (string.IsNullOrEmpty(text) || text.Length < 4 || !text.Any(char.IsLetter)) return;
        terms.Add(new(text, label));
    }

    private static void AddName(List<MaskTerm> terms, string? value, string label)
    {
        AddWhole(terms, value, label);
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var word in value.Split([' ', '\t', ',', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!CommonWords.Contains(word)) AddWhole(terms, word, label);
    }

    /// <summary>คำตอบที่เป็นข้อความ (ไม่ใช่ไฟล์แนบ "P", ไม่ว่าง, ไม่ใช่ประวัติแก้ไขที่ QA Hub บันทึก "[QA Hub] …") เรียงเก่า → ใหม่</summary>
    public static IReadOnlyList<CrmHelpDeskAnswer> TextAnswers(IEnumerable<CrmHelpDeskAnswer> answers)
        => answers.Where(x => !string.Equals(x.FAnswerType?.Trim(), "P", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(CleanText(x.Description))
                && !x.Description.TrimStart().StartsWith("[QA Hub]", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => CrmMirrorParser.ParseDate(x.AnsDate) ?? DateTime.MaxValue).ThenBy(x => x.AnswerNo, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>ล้าง HTML (CRM เก็บข้อความจาก rich text editor) → ข้อความธรรมดา</summary>
    public static string CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = LineBreakTags().Replace(value, "\n");
        text = Tags().Replace(text, " ");
        text = WebUtility.HtmlDecode(text).Replace((char)160, ' ');
        return TextChunker.Normalize(text);
    }

    [GeneratedRegex(@"\s*\(\s*\d+\s*\)\s*$")]
    private static partial Regex StaffCodeSuffix();

    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/tr|/h\d)\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTags();

    [GeneratedRegex(@"<[^>]{1,500}>")]
    private static partial Regex Tags();
}

public sealed record CrmKnowledgeAnswerDto(string AnswerNo, string? AnswerType, DateTime? AnsweredAt, string Text);
public sealed record CrmKnowledgeTicketDto(string JobNo, string? Subject, string? Status, string? JobType, string? ServiceType, string? Product, DateTime? ContactDate,
    string? Description, IReadOnlyList<CrmKnowledgeAnswerDto> Answers, DateTime FetchedAt, string? IndexState, DateTime? IndexedAt);
public sealed record KnowledgeCrmStatusDto(bool Enabled, bool AllowExternalAi, int Candidates, int Fetched, int Pending, int Failed, DateTime? LastFetchedAt);

/// <summary>
/// ดึงรายละเอียด + คำตอบของ CRM Ticket มาเก็บ (mask แล้ว) เพื่อเข้าคลังความรู้ — AI_KNOWLEDGE_CHAT_PLAN.md Phase 4
/// ใช้บัญชีกลางเดียวกับ CrmMirrorSyncService; เลือก Ticket ที่ปิดแล้วหรือมีการตอบ, ดึงใหม่เมื่อมีการตอบ/สถานะเปลี่ยนหลังดึงครั้งก่อน
/// จำกัดต่อรอบ (Knowledge:CrmFetchPerRun, ค่าเริ่มต้น 50 ใบ = 100 คำขอ) และหยุดทันทีเมื่อ CRM ล่ม — ต้นฉบับไม่เก็บ
/// </summary>
public sealed class CrmKnowledgeFetcher(QaDbContext db, ICrmKnowledgeSource source, KnowledgeIndexTrigger trigger, IConfiguration configuration, ILogger<CrmKnowledgeFetcher> logger)
{
    private static readonly string[] ClosedStatuses = ["Close", "Closed", "Finish"];
    private static readonly TimeSpan RefetchCooldown = TimeSpan.FromHours(1);
    private static readonly TimeSpan FailureRetry = TimeSpan.FromHours(24);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public bool Enabled => configuration.GetValue("Knowledge:CrmFetchEnabled", true);
    public int PerRun => Math.Clamp(configuration.GetValue("Knowledge:CrmFetchPerRun", 50), 1, 500);
    public string AccountUsername => (configuration["CrmMirror:AccountUsername"] ?? "6101").Trim();

    /// <summary>Ticket ที่ต้องดึง: ยังไม่เคยดึง (ใหม่ก่อน) หรือมีการตอบ/เปลี่ยนสถานะหลังดึงครั้งก่อน หรือดึงไม่สำเร็จเกิน 24 ชม.</summary>
    public static IQueryable<CrmFetchCandidate> Candidates(QaDbContext db, DateTime now)
    {
        var cooldown = now - RefetchCooldown;
        var retry = now - FailureRetry;
        return from t in db.CrmTickets.AsNoTracking()
               where ClosedStatuses.Contains(t.Status!) || t.LastReplyAt != null
               join c0 in db.CrmTicketContents.AsNoTracking() on t.JobNo equals c0.JobNo into cs
               from c in cs.DefaultIfEmpty()
               where c == null
                   || (c.FetchError != null ? c.FetchedAt < retry
                       : c.FetchedAt < cooldown && (c.FetchedAt < t.LastChangedAt || (t.LastReplyAt != null && c.FetchedAt < t.LastReplyAt)))
               orderby c == null ? 1 : 0, t.LastReplyAt ?? t.LastChangedAt descending
               select new CrmFetchCandidate(t.JobNo, t.JobType, c != null);
    }

    public async Task<int> FetchPendingAsync(CancellationToken ct)
    {
        if (!await Gate.WaitAsync(0, ct)) return 0;
        try
        {
            var account = (await db.CrmConfigurations.AsNoTracking().Where(x => x.Username == AccountUsername && x.IsEnabled && x.EncryptedPassword != "").ToListAsync(ct))
                .OrderByDescending(x => x.UpdatedAt).FirstOrDefault();
            if (account is null) return 0;
            var now = DateTime.UtcNow;
            var candidates = await Candidates(db, now).Take(PerRun).ToListAsync(ct);
            var done = 0;
            foreach (var candidate in candidates)
            {
                CrmKnowledgeRaw? raw;
                try { raw = await source.FetchAsync(account.UserId, candidate.JobNo, string.IsNullOrWhiteSpace(candidate.JobType) ? "HD" : candidate.JobType.Trim(), ct); }
                catch (Exception ex) when (ex is CrmTimeoutException or CrmNotConfiguredException || ex is CrmIntegrationException { IsTransient: true })
                {
                    logger.LogWarning("CRM knowledge: หยุดดึงรอบนี้ — {Reason}", ex.Message);
                    break;
                }
                catch (Exception ex) when (ex is CrmIntegrationException or CrmBadResponseException or HttpRequestException or JsonException)
                {
                    logger.LogWarning("CRM knowledge: ดึง {JobNo} ไม่สำเร็จ — {Reason}", candidate.JobNo, ex.Message);
                    await FailAsync(candidate.JobNo, $"ดึงจาก CRM ไม่สำเร็จ: {ex.Message}", DateTime.UtcNow, ct);
                    done++;
                    continue;
                }
                if (raw is null) await FailAsync(candidate.JobNo, "ไม่พบ Ticket ใน CRM", DateTime.UtcNow, ct);
                else await ApplyAsync(candidate.JobNo, raw, DateTime.UtcNow, ct);
                done++;
            }
            if (done > 0)
            {
                logger.LogInformation("CRM knowledge: ดึง {Count} Ticket", done);
                trigger.Signal();
            }
            return done;
        }
        finally { Gate.Release(); }
    }

    /// <summary>
    /// mask แล้วบันทึกรายละเอียด + คำตอบของ Ticket — รายละเอียดว่าง (HelpDesksJob ส่วนใหญ่ไม่มี) ใช้ข้อความตอบเก่าสุดแทน ตามที่หน้า CRM แสดง
    /// คืน true เมื่อเนื้อหาเปลี่ยน
    /// </summary>
    public async Task<bool> ApplyAsync(string jobNo, CrmKnowledgeRaw raw, DateTime now, CancellationToken ct)
    {
        var ticket = await db.CrmTickets.AsNoTracking().FirstOrDefaultAsync(x => x.JobNo == jobNo, ct);
        if (ticket is null) return false;
        var terms = raw.Terms;
        var answers = CrmKnowledgeParser.TextAnswers(raw.Answers).ToList();
        var description = CrmKnowledgeParser.CleanText(raw.Description);
        if (description.Length == 0 && answers.Count > 0)
        {
            description = CrmKnowledgeParser.CleanText(answers[0].Description);
            answers.RemoveAt(0);
        }
        var subject = SensitiveDataMasker.Mask(ticket.Subject, terms);
        var maskedDescription = SensitiveDataMasker.Mask(description, terms);
        var maskedAnswers = answers.Select(a =>
        {
            var text = SensitiveDataMasker.Mask(CrmKnowledgeParser.CleanText(a.Description), terms);
            return new CrmTicketAnswer(jobNo, Cut(a.AnswerNo, 40)!, Cut(a.FAnswerType, 10), text.Length > CrmKnowledgeParser.MaxAnswerChars ? text[..CrmKnowledgeParser.MaxAnswerChars] : text, CrmMirrorParser.ParseDate(a.AnsDate));
        }).DistinctBy(x => x.AnswerNo, StringComparer.OrdinalIgnoreCase).ToList();
        var hash = KnowledgeHash.Compute(string.Join("\n\u001F", new[] { subject, maskedDescription }.Concat(maskedAnswers.Select(x => $"{x.AnswerNo}|{x.AnsweredAt:O}|{x.TextMasked}"))));

        var content = await db.CrmTicketContents.FirstOrDefaultAsync(x => x.JobNo == jobNo, ct);
        if (content is null) { content = new CrmTicketContent(jobNo); db.CrmTicketContents.Add(content); }
        var changed = content.Apply(subject, maskedDescription, hash, now);
        if (changed)
        {
            db.CrmTicketAnswers.RemoveRange(await db.CrmTicketAnswers.Where(x => x.JobNo == jobNo).ToListAsync(ct));
            db.CrmTicketAnswers.AddRange(maskedAnswers);
        }
        await db.SaveChangesAsync(ct);
        return changed;
    }

    private async Task FailAsync(string jobNo, string error, DateTime now, CancellationToken ct)
    {
        var content = await db.CrmTicketContents.FirstOrDefaultAsync(x => x.JobNo == jobNo, ct);
        if (content is null) { content = new CrmTicketContent(jobNo); db.CrmTicketContents.Add(content); }
        content.Fail(error, now);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>ข้อมูล mask แล้วของ Ticket (สำหรับหน้าคลังความรู้และ MCP) — null เมื่อยังไม่ได้ดึงหรือไม่มีเนื้อหา</summary>
    public async Task<CrmKnowledgeTicketDto?> GetAsync(string jobNo, CancellationToken ct)
    {
        var key = jobNo?.Trim() ?? "";
        if (key.Length == 0) return null;
        var row = await (from c in db.CrmTicketContents.AsNoTracking()
                         join t in db.CrmTickets.AsNoTracking() on c.JobNo equals t.JobNo
                         where c.JobNo == key && c.ContentHash != ""
                         select new { c, t }).FirstOrDefaultAsync(ct);
        if (row is null) return null;
        var answers = await db.CrmTicketAnswers.AsNoTracking().Where(x => x.JobNo == row.c.JobNo).OrderBy(x => x.AnsweredAt).ThenBy(x => x.AnswerNo)
            .Select(x => new CrmKnowledgeAnswerDto(x.AnswerNo, x.AnswerType, x.AnsweredAt, x.TextMasked ?? "")).ToListAsync(ct);
        var doc = await db.KnowledgeDocuments.AsNoTracking().Where(x => x.SourceType == "CrmTicket" && x.SourceKey == row.c.JobNo).Select(x => new { x.State, x.IndexedAt }).FirstOrDefaultAsync(ct);
        return new(row.c.JobNo, row.c.SubjectMasked, row.t.Status, row.t.JobType, row.t.ServiceType, row.t.Product, row.t.ContactDate,
            row.c.DescriptionMasked, answers, row.c.FetchedAt, doc?.State, doc?.IndexedAt);
    }

    public async Task<KnowledgeCrmStatusDto> GetStatusAsync(bool allowExternalAi, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var candidates = await db.CrmTickets.AsNoTracking().CountAsync(t => ClosedStatuses.Contains(t.Status!) || t.LastReplyAt != null, ct);
        var fetched = await db.CrmTicketContents.AsNoTracking().CountAsync(x => x.ContentHash != "", ct);
        var failed = await db.CrmTicketContents.AsNoTracking().CountAsync(x => x.FetchError != null, ct);
        var pending = await Candidates(db, now).CountAsync(ct);
        var last = await db.CrmTicketContents.AsNoTracking().MaxAsync(x => (DateTime?)x.FetchedAt, ct);
        return new(Enabled, allowExternalAi, candidates, fetched, pending, failed, last);
    }

    private static string? Cut(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length > max ? text[..max] : text;
    }
}

public sealed record CrmFetchCandidate(string JobNo, string? JobType, bool Refetch);
