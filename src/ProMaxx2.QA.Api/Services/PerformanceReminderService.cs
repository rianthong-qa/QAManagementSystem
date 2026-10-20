using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Performance;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// เตือนหัวหน้าทางอีเมลเมื่อการประเมินที่ยังไม่ปิด (Open/SelfSubmitted) เหลือไม่เกิน 7 วันก่อนครบกำหนด และเมื่อเลยกำหนด
/// - ผู้รับ: หัวหน้าในโปรไฟล์พนักงาน (ถ้ามีอีเมลและยัง Active) ไม่เช่นนั้นส่งให้ทุกคนที่มีสิทธิ์ PERFORMANCE.EDIT — ไม่ส่งให้ตัวผู้ถูกประเมินเอง
/// - แต่ละรอบเตือนอย่างละครั้งต่อผู้รับ (DueSoon / Overdue) กันซ้ำด้วยตาราง EvaluationNotifications; ถ้ากำหนดส่งถูกเลื่อนจะเตือนใหม่ตามวันใหม่
/// - รวมเป็นอีเมลเดียวต่อผู้รับต่อรอบการตรวจ และส่งหลัง 08:00 น. (เวลาไทย) เท่านั้น
/// - อีเมลเป็น best-effort: ยังไม่ได้ตั้งค่า SMTP = ข้าม (ไม่บันทึกว่าส่งแล้ว จึงส่งเมื่อตั้งค่าเสร็จ), ส่งไม่สำเร็จ = ลองใหม่รอบถัดไป
/// - เตือนพนักงานด้วย (แยกอีเมล): รอบที่ยังไม่ส่งแบบประเมินตนเอง (Open) และเหลือ ≤ 7 วัน/เลยกำหนด → SelfDueSoon / SelfOverdue ถึงตัวผู้ถูกประเมิน
/// ปิดได้ด้วย config <c>Performance:RemindersEnabled=false</c>
/// </summary>
public sealed class PerformanceReminderService(QaDbContext db, EmailSenderService emailSender, IConfiguration configuration, TimeProvider clock, ILogger<PerformanceReminderService> logger)
{
    public const int DueSoonDays = 7;
    public const int SendFromHour = 8;
    private static readonly TimeSpan ThaiOffset = TimeSpan.FromHours(7);

    public sealed record OpenEvaluation(Guid EvaluationId, Guid UserId, string EmployeeName, string Round, int Year, DateOnly DueDate, string Status, Guid? SupervisorUserId, string? EmployeeEmail = null);
    public sealed record Recipient(Guid UserId, string Name, string Email);
    public sealed record PlannedReminder(Recipient Recipient, OpenEvaluation Evaluation, string Kind, int DaysLeft);

    /// <summary>คำนวณว่าต้องเตือนใครเรื่องอะไร (ไม่มี IO) — แยกออกมาเพื่อทดสอบกฎได้ตรง ๆ</summary>
    public static IReadOnlyList<PlannedReminder> Plan(
        IReadOnlyList<OpenEvaluation> evaluations, IReadOnlyDictionary<Guid, Recipient> supervisors, IReadOnlyList<Recipient> managers,
        IReadOnlySet<(Guid EvaluationId, string Kind, DateOnly DueDate, Guid RecipientUserId)> alreadySent, DateOnly today)
    {
        var planned = new List<PlannedReminder>();
        foreach (var e in evaluations)
        {
            var daysLeft = e.DueDate.DayNumber - today.DayNumber;
            if (daysLeft > DueSoonDays) continue;
            var kind = daysLeft < 0 ? "Overdue" : "DueSoon";
            IEnumerable<Recipient> recipients = e.SupervisorUserId is { } sup && sup != e.UserId && supervisors.TryGetValue(sup, out var supervisor)
                ? [supervisor]
                : managers.Where(m => m.UserId != e.UserId);
            foreach (var r in recipients)
                if (!alreadySent.Contains((e.EvaluationId, kind, e.DueDate, r.UserId))) planned.Add(new PlannedReminder(r, e, kind, daysLeft));
        }
        return planned;
    }

    /// <summary>
    /// เตือนพนักงานให้ประเมินตนเอง (ไม่มี IO) — เฉพาะรอบที่ยังไม่ส่งแบบประเมินตนเอง (Open) ของพนักงานที่มีอีเมล
    /// เหลือ ≤ 7 วัน = SelfDueSoon, เลยกำหนด = SelfOverdue; อย่างละครั้งต่อกำหนดส่ง
    /// </summary>
    public static IReadOnlyList<PlannedReminder> PlanSelf(
        IReadOnlyList<OpenEvaluation> evaluations, IReadOnlySet<(Guid EvaluationId, string Kind, DateOnly DueDate, Guid RecipientUserId)> alreadySent, DateOnly today)
    {
        var planned = new List<PlannedReminder>();
        foreach (var e in evaluations)
        {
            if (e.Status != "Open" || string.IsNullOrWhiteSpace(e.EmployeeEmail)) continue;
            var daysLeft = e.DueDate.DayNumber - today.DayNumber;
            if (daysLeft > DueSoonDays) continue;
            var kind = daysLeft < 0 ? "SelfOverdue" : "SelfDueSoon";
            if (alreadySent.Contains((e.EvaluationId, kind, e.DueDate, e.UserId))) continue;
            planned.Add(new PlannedReminder(new Recipient(e.UserId, e.EmployeeName, e.EmployeeEmail.Trim()), e, kind, daysLeft));
        }
        return planned;
    }

    /// <summary>ตรวจและส่งอีเมลเตือน — คืนจำนวนอีเมลที่ส่งสำเร็จ</summary>
    public async Task<int> SendDueRemindersAsync(CancellationToken ct)
    {
        if (!configuration.GetValue("Performance:RemindersEnabled", true)) return 0;
        var nowThai = clock.GetUtcNow().UtcDateTime + ThaiOffset;
        if (nowThai.Hour < SendFromHour) return 0;
        var today = DateOnly.FromDateTime(nowThai);
        var horizon = today.AddDays(DueSoonDays);

        var evaluations = await OpenEvaluationsQuery(db, horizon).ToListAsync(ct);
        if (evaluations.Count == 0) return 0;

        var supervisorIds = evaluations.Where(x => x.SupervisorUserId != null).Select(x => x.SupervisorUserId!.Value).Distinct().ToList();
        var supervisors = await db.Users.AsNoTracking()
            .Where(u => supervisorIds.Contains(u.UserId) && u.IsActive && u.Email != null && u.Email != "")
            .Select(u => new Recipient(u.UserId, u.DisplayName, u.Email!)).ToDictionaryAsync(x => x.UserId, ct);
        var managers = await ManagersQuery(db).ToListAsync(ct);
        var evaluationIds = evaluations.Select(x => x.EvaluationId).ToList();
        var sent = (await db.EvaluationNotifications.AsNoTracking().Where(x => evaluationIds.Contains(x.EvaluationId))
            .Select(x => new { x.EvaluationId, x.Kind, x.DueDate, x.RecipientUserId }).ToListAsync(ct))
            .Select(x => (x.EvaluationId, x.Kind, x.DueDate, x.RecipientUserId)).ToHashSet();

        var planned = Plan(evaluations, supervisors, managers, sent, today);
        var selfPlanned = PlanSelf(evaluations, sent, today);
        if (planned.Count == 0 && selfPlanned.Count == 0) return 0;

        var link = $"{(configuration["PublicWebBaseUrl"] ?? "https://promaxx2.qahub.store").TrimEnd('/')}/#/performance";
        var delivered = 0;
        // อีเมลถึงหัวหน้า (สรุปทุกคนที่ต้องประเมิน) กับอีเมลถึงพนักงาน (ให้ประเมินตนเอง) เป็นคนละฉบับ แม้ผู้รับเป็นคนเดียวกัน
        var mails = planned.GroupBy(x => x.Recipient.UserId).Select(g => (Self: false, Items: g.ToList()))
            .Concat(selfPlanned.GroupBy(x => x.Recipient.UserId).Select(g => (Self: true, Items: g.ToList())));
        foreach (var mail in mails)
        {
            var recipient = mail.Items[0].Recipient;
            var items = mail.Items.OrderBy(x => x.DaysLeft).ThenBy(x => x.Evaluation.EmployeeName).ToList();
            var overdue = items.Count(x => x.DaysLeft < 0);
            var reminderItems = items.Select(x => new EmailTemplates.EvaluationReminderItem(x.Evaluation.EmployeeName, RoundLabel(x.Evaluation.Round, x.Evaluation.Year), x.Evaluation.DueDate, StatusLabel(x.Evaluation.Status), x.DaysLeft)).ToList();
            var subject = mail.Self
                ? overdue > 0 ? "[QA Hub] แบบประเมินตนเองเลยกำหนดแล้ว — กรุณาประเมินตนเอง" : "[QA Hub] กรุณาประเมินตนเองก่อนครบกำหนด"
                : overdue > 0
                    ? $"[QA Hub] การประเมินพนักงานเลยกำหนด {overdue} รายการ{(items.Count > overdue ? $" · ใกล้ครบกำหนด {items.Count - overdue}" : "")}"
                    : $"[QA Hub] การประเมินพนักงานใกล้ครบกำหนด {items.Count} รายการ";
            var html = mail.Self
                ? EmailTemplates.EvaluationSelfReminder(recipient.Name, reminderItems, link)
                : EmailTemplates.EvaluationReminder(recipient.Name, reminderItems, link);
            try
            {
                await emailSender.SendAsync(recipient.Email, subject, html, ct, isHtml: true);
            }
            catch (EmailNotConfiguredException ex)
            {
                // ยังไม่ได้ตั้งค่า/ปิดการส่งอีเมล — ไม่บันทึกว่าส่งแล้ว เพื่อให้ส่งได้เมื่อเปิดใช้งาน
                logger.LogInformation("ข้ามอีเมลเตือนการประเมิน: {Reason}", ex.Message);
                return delivered;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ส่งอีเมลเตือนการประเมินถึง {Recipient} ไม่สำเร็จ — จะลองใหม่รอบถัดไป", recipient.UserId);
                continue;
            }
            var sentAt = DateTime.UtcNow;
            db.EvaluationNotifications.AddRange(items.Select(x => new EvaluationNotification(x.Evaluation.EvaluationId, x.Kind, x.Evaluation.DueDate, recipient.UserId, sentAt)));
            await db.SaveChangesAsync(ct);
            delivered++;
        }
        return delivered;
    }

    /// <summary>การประเมินที่ยังไม่ปิดและครบกำหนดภายใน <paramref name="horizon"/> ของพนักงานที่ยัง Active และไม่ได้ลาออก</summary>
    public static IQueryable<OpenEvaluation> OpenEvaluationsQuery(QaDbContext db, DateOnly horizon)
        => from e in db.Evaluations.AsNoTracking()
           join u in db.Users.AsNoTracking() on e.UserId equals u.UserId
           join p in db.EmployeeProfiles.AsNoTracking() on e.UserId equals p.UserId into profiles
           from p in profiles.DefaultIfEmpty()
           where (e.Status == "Open" || e.Status == "SelfSubmitted") && e.DueDate <= horizon && u.IsActive && (p == null || p.EmploymentStatus != "Resigned")
           select new OpenEvaluation(e.EvaluationId, e.UserId, u.DisplayName, e.Round, e.Year, e.DueDate, e.Status, p == null ? null : p.SupervisorUserId, u.Email);

    /// <summary>ผู้รับสำรองเมื่อพนักงานไม่มีหัวหน้าในโปรไฟล์ — ผู้ใช้ Active ที่มีอีเมลและได้สิทธิ์ PERFORMANCE.EDIT จาก Role</summary>
    public static IQueryable<Recipient> ManagersQuery(QaDbContext db)
        => db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Email != null && u.Email != ""
                && u.UserRoles.Any(ur => ur.Role.IsActive && ur.Role.RolePermissions.Any(rp => rp.Permission.PermissionCode == "PERFORMANCE.EDIT")))
            .Select(u => new Recipient(u.UserId, u.DisplayName, u.Email!));

    public static string RoundLabel(string round, int year) => round switch
    {
        "P30" => "ทดลองงาน 30 วัน",
        "P60" => "ทดลองงาน 60 วัน",
        "P90" => "ทดลองงาน 90 วัน",
        "PFinal" => "สรุปผลทดลองงาน",
        _ when round.StartsWith('Q') => $"ไตรมาส {round[1..]}/{year + 543}",
        _ => round,
    };

    private static string StatusLabel(string status) => status == "SelfSubmitted" ? "พนักงานประเมินตนเองแล้ว รอหัวหน้าประเมิน" : "รอประเมิน (พนักงานยังไม่ส่งแบบประเมินตนเอง)";
}

/// <summary>ตรวจการประเมินที่ใกล้/เลยกำหนดทุก 1 ชั่วโมง (รูปแบบเดียวกับ CrmSyncWorker) — รอบแรกรอ 2 นาทีหลัง API สตาร์ท</summary>
public sealed class PerformanceReminderWorker(IServiceScopeFactory scopeFactory, ILogger<PerformanceReminderWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sent = await scope.ServiceProvider.GetRequiredService<PerformanceReminderService>().SendDueRemindersAsync(stoppingToken);
                if (sent > 0) logger.LogInformation("ส่งอีเมลเตือนการประเมิน {Count} ฉบับ", sent);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            // รอบที่ล้มเหลว (เช่น DB ขัดข้องชั่วคราว) ต้องไม่ทำให้ worker หยุดถาวร
            catch (Exception ex) { logger.LogError(ex, "PerformanceReminderWorker tick failed — will retry in {Interval}.", Interval); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
