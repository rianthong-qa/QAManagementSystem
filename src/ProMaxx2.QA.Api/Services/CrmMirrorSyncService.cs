using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Integrations;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed record CrmMirrorAnswer(string AnswerNo, string? PostedCode, DateTime? AnsweredAt);
public sealed record CrmMirrorRecord(CrmTicketListItem Ticket, IReadOnlyList<CrmMirrorAnswer> Answers);
public sealed record CrmMirrorDiagnostics(int Records, int WithJobNo, int WithAnswers, int AnswersWithDate, IReadOnlyList<string> TicketFields, IReadOnlyList<string> AnswerFields);

/// <summary>แยก Ticket + ข้อความตอบ (เฉพาะเลข/ผู้ตอบ/วันที่ ไม่เก็บเนื้อหา) จาก body ของ HelpDeskExport</summary>
public static class CrmMirrorParser
{
    public static (IReadOnlyList<CrmMirrorRecord> Records, CrmMirrorDiagnostics Diagnostics) Parse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var raw = CrmTicketListParser.CoerceRecords(document.RootElement);
        var records = new List<CrmMirrorRecord>();
        var ticketFields = new SortedSet<string>(StringComparer.Ordinal);
        var answerFields = new SortedSet<string>(StringComparer.Ordinal);
        int withAnswers = 0, answersWithDate = 0;
        foreach (var record in raw)
        {
            var ticket = CrmTicketListParser.MapTicket(record);
            if (ticket is null) continue;
            var fields = CrmTicketListParser.TicketRecord(record);
            if (fields.ValueKind == JsonValueKind.Object && ticketFields.Count < 80)
                foreach (var p in fields.EnumerateObject()) ticketFields.Add(p.Name);

            var answers = new List<CrmMirrorAnswer>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // ข้อความตอบอยู่ได้ 2 ที่: fd.helpDeskAnswers (ส่วนใหญ่ของ HelpDeskExport, มีวันที่ครบ — อ่านก่อน) และ answers ระดับนอก ({ fd, answers })
            var sources = new List<JsonElement>();
            if (fields.ValueKind == JsonValueKind.Object)
                foreach (var name in new[] { "helpDeskAnswers", "HelpDeskAnswers" })
                    if (fields.TryGetProperty(name, out var inner) && inner.ValueKind == JsonValueKind.Array) { sources.Add(inner); break; }
            if (record.ValueKind == JsonValueKind.Object)
                foreach (var name in new[] { "answers", "Answers" })
                    if (record.TryGetProperty(name, out var outer) && outer.ValueKind == JsonValueKind.Array) { sources.Add(outer); break; }
            foreach (var list in sources)
                {
                    foreach (var a in list.EnumerateArray())
                    {
                        if (a.ValueKind != JsonValueKind.Object) continue;
                        if (answerFields.Count < 40) foreach (var p in a.EnumerateObject()) answerFields.Add(p.Name);
                        var answerNo = CrmTicketListParser.ReadString(a, "answerNo", "AnswerNo");
                        if (string.IsNullOrWhiteSpace(answerNo)) continue;
                        var at = ParseDate(CrmTicketListParser.ReadString(a, "ansDate", "AnsDate", "answerDate", "AnswerDate", "postDate", "createDate"));
                        if (!seen.Add(answerNo.Trim())) continue;
                        if (at is not null) answersWithDate++;
                        answers.Add(new CrmMirrorAnswer(answerNo.Trim(), StaffCode(CrmTicketListParser.ReadString(a, "posted", "Posted")), at));
                    }
                }
            if (answers.Count > 0) withAnswers++;
            records.Add(new CrmMirrorRecord(ticket, answers));
        }
        return (records, new CrmMirrorDiagnostics(raw.Count, records.Count, withAnswers, answersWithDate, ticketFields.ToList(), answerFields.ToList()));
    }

    /// <summary>"เหรียญทอง เจือบุญ (6101)" → "6101"; "0" (ไม่มี Dev) → null; ค่าอื่นใช้ตามเดิม</summary>
    public static string? StaffCode(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text) || text == "0") return null;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\((\d+)\)\s*$");
        var code = match.Success ? match.Groups[1].Value : text;
        return code.Length > 50 ? code[..50] : code;
    }

    public static DateTime? ParseDate(string? value)
        => CrmTicketListParser.NormalizeDate(value) is { } iso && DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc) ? utc : null;
}

/// <summary>
/// ดึง CRM Ticket ทั้งทีมด้วยบัญชีกลาง (config <c>CrmMirror:AccountUsername</c>, ค่าเริ่มต้น 6101) มาเก็บใน CrmTickets และบันทึกการเปลี่ยน
/// สถานะ/ผู้รับผิดชอบ/Dev เป็น CrmTicketEvents (เวลา = เวลาที่ sync เห็นการเปลี่ยน — ละเอียดเท่ารอบการดึง) ใช้วัดผลงาน CRM รายคน
/// รอบแรกนำเข้าประวัติ Flow Tracking เดิมจาก AuditLogs (Source = Legacy) ครั้งเดียว; รันได้ทีละรอบ (กันกดซ้ำ/worker ซ้อน)
/// </summary>
public sealed class CrmMirrorSyncService(QaDbContext db, CrmApiClient crm, IConfiguration configuration, ILogger<CrmMirrorSyncService> logger)
{
    public const int IncrementalDays = 90;
    public const int FullDays = 366;
    /// <summary>HelpDeskExport ส่งประวัติการตอบมาไม่ครบ — ดึงรายใบเฉพาะที่มีการตอบใหม่ใน N วันล่าสุด ไม่เกิน M ใบต่อรอบ</summary>
    public const int AnswerLookbackDays = 30;
    public const int MaxAnswerFetchesPerRun = 60;
    private static readonly TimeSpan AnswerRefetchCooldown = TimeSpan.FromHours(6);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> AnswerFetchedAt = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string AccountUsername => (configuration["CrmMirror:AccountUsername"] ?? "6101").Trim();
    public bool Enabled => configuration.GetValue("CrmMirror:Enabled", true);

    public async Task<CrmMirrorRun> RunAsync(bool full, Guid? requestedBy, CancellationToken ct)
    {
        if (!await Gate.WaitAsync(0, ct)) throw new InvalidOperationException("กำลังดึงข้อมูล CRM อยู่ กรุณารอให้รอบปัจจุบันเสร็จก่อน");
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
            var run = new CrmMirrorRun(full ? "Full" : "Incremental", today.AddDays(-(full ? FullDays : IncrementalDays)), today, AccountUsername, requestedBy);
            db.CrmMirrorRuns.Add(run);
            await db.SaveChangesAsync(ct);

            // ผู้ใช้หลายคนอาจผูกบัญชี CRM เดียวกัน (เช่น บัญชีทดสอบ) — ใช้แถวที่บันทึกรหัสผ่านล่าสุด
            var account = (await db.CrmConfigurations.AsNoTracking().Where(x => x.Username == AccountUsername && x.IsEnabled && x.EncryptedPassword != "").ToListAsync(ct))
                .OrderByDescending(x => x.UpdatedAt).FirstOrDefault();
            if (account is null)
            {
                run.Fail($"ไม่พบบัญชี CRM {AccountUsername} ที่เปิดใช้งานใน QA Hub — ผู้ใช้เจ้าของบัญชีต้องตั้งค่า \"บัญชี CRM ของฉัน\" ก่อน", "Skipped");
                await db.SaveChangesAsync(ct);
                return run;
            }

            string body;
            try { body = await crm.ExportHelpDeskRawAsync(account.UserId, run.RangeFrom, run.RangeTo, ct); }
            catch (Exception ex) when (ex is CrmIntegrationException or CrmNotConfiguredException or CrmTimeoutException or CrmBadResponseException or HttpRequestException)
            {
                logger.LogWarning(ex, "CRM mirror: ดึง HelpDeskExport ไม่สำเร็จ");
                run.Fail(ex.Message);
                await db.SaveChangesAsync(ct);
                return run;
            }

            await ImportLegacyFlowOnceAsync(ct);
            var (records, diagnostics) = CrmMirrorParser.Parse(body);
            var (created, changed, answers) = await ApplyAsync(records, DateTime.UtcNow, ct);
            answers += await SyncRecentAnswersAsync(account.UserId, ct);
            run.Succeed(records.Count, created, changed, answers, JsonSerializer.Serialize(diagnostics, Json));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("CRM mirror {Kind}: {Count} tickets, {New} new, {Changed} changed, {Answers} answers", run.Kind, records.Count, created, changed, answers);
            return run;
        }
        finally { Gate.Release(); }
    }

    /// <summary>บันทึกผลดึงลงฐานข้อมูล (แยกออกมาเพื่อทดสอบได้โดยไม่ต้องเรียก CRM)</summary>
    public async Task<(int Created, int Changed, int Answers)> ApplyAsync(IReadOnlyList<CrmMirrorRecord> records, DateTime now, CancellationToken ct)
    {
        int created = 0, changed = 0, answers = 0;
        foreach (var chunk in records.DistinctBy(x => x.Ticket.JobNo, StringComparer.OrdinalIgnoreCase).Chunk(500))
        {
            var jobNos = chunk.Select(x => x.Ticket.JobNo).ToArray();
            var existing = await db.CrmTickets.Where(x => jobNos.Contains(x.JobNo)).ToDictionaryAsync(x => x.JobNo, StringComparer.OrdinalIgnoreCase, ct);
            var seenAnswers = (await db.CrmTicketEvents.AsNoTracking().Where(x => x.AnswerNo != null && jobNos.Contains(x.JobNo)).Select(x => new { x.JobNo, x.AnswerNo }).ToListAsync(ct))
                .Select(x => $"{x.JobNo}|{x.AnswerNo}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var record in chunk)
            {
                var t = record.Ticket;
                var assignee = CrmMirrorParser.StaffCode(t.Assignee);
                var owner = CrmMirrorParser.StaffCode(t.Owner);
                var developer = CrmMirrorParser.StaffCode(t.Developer);
                var status = CrmTicketListParser.NormalizeStatus(t.Status) ?? t.Status;
                if (!existing.TryGetValue(t.JobNo, out var ticket))
                {
                    ticket = new CrmTicket(t.JobNo, now);
                    db.CrmTickets.Add(ticket);
                    Apply(ticket);
                    db.CrmTicketEvents.Add(CrmTicketEvent.Route(t.JobNo, "Snapshot", now, "Sync", null, ticket.Status, null, ticket.AssigneeCode, null, ticket.DeveloperCode, ticket.OwnerCode));
                    created++;
                }
                else
                {
                    var (oldStatus, oldAssignee, oldDeveloper) = (ticket.Status, ticket.AssigneeCode, ticket.DeveloperCode);
                    if (Apply(ticket))
                    {
                        db.CrmTicketEvents.Add(CrmTicketEvent.Route(t.JobNo, "Changed", now, "Sync", oldStatus, ticket.Status, oldAssignee, ticket.AssigneeCode, oldDeveloper, ticket.DeveloperCode, ticket.OwnerCode));
                        changed++;
                    }
                }
                foreach (var a in record.Answers)
                {
                    if (!seenAnswers.Add($"{t.JobNo}|{a.AnswerNo}")) continue;
                    db.CrmTicketEvents.Add(CrmTicketEvent.Answer(t.JobNo, a.AnswerNo.Length > 40 ? a.AnswerNo[..40] : a.AnswerNo, a.AnsweredAt ?? now, a.PostedCode));
                    answers++;
                }

                bool Apply(CrmTicket x) => x.Apply(t.Subject, status, t.JobType, t.ServiceType, t.Product, assignee, owner, developer, t.Branch,
                    CrmMirrorParser.ParseDate(t.ContactDate), CrmMirrorParser.ParseDate(t.DueDate), CrmMirrorParser.ParseDate(t.LastReplyAt), now);
            }
            await db.SaveChangesAsync(ct);
        }
        return (created, changed, answers);
    }

    /// <summary>
    /// ดึงข้อความตอบรายใบ (เลข/ผู้ตอบ/วันที่ ไม่เก็บเนื้อหา) ของ Ticket ที่ตอบล่าสุดภายใน 30 วันแต่ยังไม่มีข้อความตอบนั้นในฐานข้อมูล
    /// ไม่เกิน 60 ใบต่อรอบ และไม่ดึงใบเดิมซ้ำภายใน 6 ชม. (กันวนดึงใบที่ CRM ไม่ส่งข้อความตอบให้) — หยุดทันทีเมื่อ CRM ล่ม/ช้า
    /// </summary>
    public async Task<int> SyncRecentAnswersAsync(Guid accountUserId, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-AnswerLookbackDays);
        var candidates = await PendingAnswerTickets(db, since).Take(MaxAnswerFetchesPerRun * 3).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var added = 0;
        var fetched = 0;
        foreach (var jobNo in candidates)
        {
            if (fetched >= MaxAnswerFetchesPerRun) break;
            if (AnswerFetchedAt.TryGetValue(jobNo, out var last) && now - last < AnswerRefetchCooldown) continue;
            IReadOnlyList<CrmHelpDeskAnswer> list;
            try { list = await crm.GetHelpDeskAnswersAsync(accountUserId, jobNo, ct); }
            catch (CrmIntegrationException ex) when (ex.IsTransient) { logger.LogWarning("CRM mirror: หยุดดึงข้อความตอบ — {Reason}", ex.Message); break; }
            catch (Exception ex) when (ex is CrmIntegrationException or CrmTimeoutException or CrmBadResponseException or HttpRequestException) { logger.LogWarning(ex, "CRM mirror: ดึงข้อความตอบ {JobNo} ไม่สำเร็จ", jobNo); AnswerFetchedAt[jobNo] = now; fetched++; continue; }
            AnswerFetchedAt[jobNo] = now;
            fetched++;
            var known = (await db.CrmTicketEvents.AsNoTracking().Where(x => x.JobNo == jobNo && x.AnswerNo != null).Select(x => x.AnswerNo!).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var a in list)
            {
                var no = a.AnswerNo.Trim();
                if (no.Length == 0 || no.Length > 40 || !known.Add(no)) continue;
                db.CrmTicketEvents.Add(CrmTicketEvent.Answer(jobNo, no, CrmMirrorParser.ParseDate(a.AnsDate) ?? now, CrmMirrorParser.StaffCode(a.Posted)));
                added++;
            }
            await db.SaveChangesAsync(ct);
        }
        return added;
    }

    /// <summary>Ticket ที่ตอบล่าสุดหลัง <paramref name="since"/> แต่ยังไม่มีเหตุการณ์ Answer ที่เวลาตรงกับการตอบล่าสุด (ล่าสุดก่อน)</summary>
    public static IQueryable<string> PendingAnswerTickets(QaDbContext db, DateTime since)
        => db.CrmTickets.AsNoTracking()
            .Where(t => t.LastReplyAt != null && t.LastReplyAt >= since
                && !db.CrmTicketEvents.Any(e => e.JobNo == t.JobNo && e.Kind == "Answer" && e.OccurredAt >= t.LastReplyAt.Value.AddMinutes(-1)))
            .OrderByDescending(t => t.LastReplyAt)
            .Select(t => t.JobNo);

    /// <summary>นำประวัติ Flow Tracking เดิม (AuditLogs CrmTicketFlow) เข้าเป็นเหตุการณ์ครั้งเดียว — เวลาตามที่บันทึกไว้เดิม</summary>
    public async Task<int> ImportLegacyFlowOnceAsync(CancellationToken ct)
    {
        if (await db.CrmTicketEvents.AnyAsync(x => x.Source == "Legacy", ct)) return 0;
        var rows = await db.AuditLogs.AsNoTracking().Where(x => x.EntityType == CrmFlowTrackingService.EntityType)
            .OrderBy(x => x.CreatedAt).Select(x => new { x.EntityId, x.Action, x.CreatedAt, x.BeforeJson, x.AfterJson }).ToListAsync(ct);
        var count = 0;
        foreach (var row in rows)
        {
            var after = CrmFlowTrackingService.Deserialize(row.AfterJson);
            if (after is null) continue;
            var before = CrmFlowTrackingService.Deserialize(row.BeforeJson);
            db.CrmTicketEvents.Add(CrmTicketEvent.Route(row.EntityId, before is null ? "Snapshot" : "Changed", row.CreatedAt, "Legacy",
                before is null ? null : CrmTicketListParser.NormalizeStatus(before.Status), CrmTicketListParser.NormalizeStatus(after.Status),
                CrmMirrorParser.StaffCode(before?.Qa), CrmMirrorParser.StaffCode(after.Qa),
                CrmMirrorParser.StaffCode(before?.Developer), CrmMirrorParser.StaffCode(after.Developer), CrmMirrorParser.StaffCode(after.Support)));
            count++;
        }
        if (count > 0) await db.SaveChangesAsync(ct);
        return count;
    }
}

/// <summary>ดึง CRM ทุก 15 นาที (ย้อนหลัง 90 วัน) และรอบใหญ่ 366 วันวันละครั้ง — ปิดได้ด้วย CrmMirror:Enabled=false</summary>
public sealed class CrmMirrorWorker(IServiceScopeFactory scopeFactory, ILogger<CrmMirrorWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<CrmMirrorSyncService>();
                if (service.Enabled)
                {
                    var db = scope.ServiceProvider.GetRequiredService<QaDbContext>();
                    var lastFull = await db.CrmMirrorRuns.AsNoTracking().Where(x => x.Kind == "Full" && x.Status == "Succeeded").MaxAsync(x => (DateTime?)x.StartedAt, stoppingToken);
                    await service.RunAsync(lastFull is null || lastFull < DateTime.UtcNow.AddHours(-23), null, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (InvalidOperationException ex) { logger.LogInformation("CRM mirror: {Reason}", ex.Message); }
            catch (Exception ex) { logger.LogError(ex, "CRM mirror worker failed — will retry next interval"); }
            // ข้อความ CRM เข้าคลังความรู้ (Phase 4) — หลังรอบ mirror เพื่อรู้ว่า Ticket ไหนมีการตอบ/ปิดใหม่; ล้มเหลวไม่กระทบรอบถัดไป
            try
            {
                using var scope = scopeFactory.CreateScope();
                var fetcher = scope.ServiceProvider.GetRequiredService<CrmKnowledgeFetcher>();
                if (scope.ServiceProvider.GetRequiredService<CrmMirrorSyncService>().Enabled && fetcher.Enabled) await fetcher.FetchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "CRM knowledge fetch failed — will retry next interval"); }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
