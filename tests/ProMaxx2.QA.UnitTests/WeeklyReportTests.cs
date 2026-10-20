using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Performance;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Performance;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

public sealed class WeeklyReportTests
{
    // ศุกร์ 9 ต.ค. 2026 เวลาไทย → สัปดาห์นี้เริ่มจันทร์ 5 ต.ค.
    private static readonly DateOnly ThisWeek = new(2026, 10, 5);

    [Fact]
    public async Task Only_managers_see_the_report_and_weeks_resolve_to_monday_and_not_future()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var weekly = CreateService(db);

        await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => weekly.BuildAsync(new PerformanceViewer(tester, false, []), null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => weekly.BuildAsync(Manager(lead), ThisWeek.AddDays(7), CancellationToken.None));

        var current = await weekly.BuildAsync(Manager(lead), null, CancellationToken.None);
        Assert.Equal(ThisWeek, current.WeekStart);
        Assert.Equal(new DateOnly(2026, 10, 11), current.WeekEnd);
        Assert.True(current.IsCurrentWeek);
        Assert.Equal(new DateOnly(2026, 9, 7), current.TypicalFrom);
        Assert.Equal(new DateOnly(2026, 10, 4), current.TypicalTo);

        var past = await weekly.BuildAsync(Manager(lead), new DateOnly(2026, 9, 30), CancellationToken.None);
        Assert.Equal(new DateOnly(2026, 9, 28), past.WeekStart);
        Assert.False(past.IsCurrentWeek);
    }

    [Fact]
    public async Task Report_compares_weeks_lists_work_items_without_concerns_and_skips_resigned_members()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var resigned = new User("old", "Resigned person", null, "hash");
        db.Users.Add(resigned);
        var resignedProfile = new EmployeeProfile(resigned.UserId, null);
        resignedProfile.Update(null, null, null, null, null, "Resigned", null, null, null);
        db.EmployeeProfiles.Add(resignedProfile);
        db.WorkLogEntries.AddRange(
            new WorkLogEntry(tester, ThisWeek, "Testing", "WorkLog", "ทดสอบ Release 2.4", null, 4, null, null, tester),
            new WorkLogEntry(tester, ThisWeek.AddDays(1), "Support", "Highlight", "หา root cause ปัญหาปิดยอด", null, 2, null, null, tester),
            new WorkLogEntry(tester, ThisWeek.AddDays(2), "Other", "Concern", "ส่งงานช้า", null, null, null, null, lead),
            new WorkLogEntry(tester, ThisWeek.AddDays(-3), "Testing", "Highlight", "สัปดาห์ก่อน", null, null, null, null, tester));
        await db.SaveChangesAsync();

        var report = await CreateService(db).BuildAsync(Manager(lead), null, CancellationToken.None);

        Assert.DoesNotContain(report.Members, m => m.DisplayName == "Resigned person");
        var member = report.Members.Single(m => m.UserId == tester);
        Assert.Equal(["Highlight", "WorkLog"], member.WorkItems.Select(x => x.Kind));
        Assert.DoesNotContain(member.WorkItems, x => x.Title == "ส่งงานช้า");
        Assert.Equal(1, member.Current.WorkLogs);
        Assert.Equal(1, member.Current.Highlights);
        Assert.Equal(1, member.Previous.Highlights);
        Assert.Equal(1, report.Current.WorkLogs);
        Assert.Equal(1, report.Previous.Highlights);
        // 4 สัปดาห์ก่อนหน้า: ผลงานเด่น 1, 0, 0, 0 → ค่ากลาง 0
        Assert.Equal(0, report.Typical.Highlights);
        Assert.Null(report.Narrative);
    }

    [Fact]
    public async Task Notes_are_validated_saved_per_week_and_audited()
    {
        await using var db = CreateDb();
        var (tester, lead) = await SeedUsersAsync(db);
        var weekly = CreateService(db);
        var request = new SaveWeeklyReportNoteRequest("yellow", "Release 2.4 เลื่อน 2 วัน", "Dev แก้ Critical ไม่ทัน", null, "Regression รอบสุดท้าย",
            new Dictionary<Guid, string?> { [tester] = "  ช่วยทีมหา root cause  ", [Guid.NewGuid()] = " " });

        await Assert.ThrowsAsync<PerformanceAccessDeniedException>(() => weekly.SaveNoteAsync(new PerformanceViewer(tester, false, []), ThisWeek, request, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => weekly.SaveNoteAsync(Manager(lead), ThisWeek.AddDays(1), request, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => weekly.SaveNoteAsync(Manager(lead), ThisWeek, request with { Health = "Blue" }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => weekly.SaveNoteAsync(Manager(lead), ThisWeek, request with { Headline = new string('x', 501) }, CancellationToken.None));

        var saved = await weekly.SaveNoteAsync(Manager(lead), ThisWeek, request, CancellationToken.None);
        Assert.Equal("Yellow", saved.Health);
        Assert.Equal("Lead", saved.UpdatedByName);
        Assert.Equal("ช่วยทีมหา root cause", Assert.Single(saved.MemberNotes).Value);

        await weekly.SaveNoteAsync(Manager(lead), ThisWeek, request with { Health = "Green", MemberNotes = null }, CancellationToken.None);
        var report = await weekly.BuildAsync(Manager(lead), ThisWeek, CancellationToken.None);
        Assert.Equal("Green", report.Narrative!.Health);
        Assert.Empty(report.Narrative.MemberNotes);
        Assert.Null((await weekly.BuildAsync(Manager(lead), ThisWeek.AddDays(-7), CancellationToken.None)).Narrative);
        Assert.Equal(1, await db.WeeklyReportNotes.CountAsync());
        Assert.Equal(["Created", "Updated"], await db.AuditLogs.Where(x => x.EntityType == WeeklyReportService.AuditEntityType).OrderBy(x => x.CreatedAt).Select(x => x.Action).ToListAsync());
    }

    [Fact]
    public void Typical_week_uses_median_so_one_import_week_does_not_skew_it()
    {
        static WeeklyTotalsDto Week(decimal defects) => new(0, 0, 0, 0, defects, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var typical = WeeklyReportService.Median([Week(985), Week(3), Week(2), Week(1)]);

        Assert.Equal(2.5m, typical.DefectsReported);
    }

    [Fact]
    public void Bulk_days_are_flagged_against_the_median_of_other_days()
    {
        var reportWeek = ThisWeek;
        (DateOnly, int)[] days = [(new DateOnly(2026, 9, 15), 1052), (new DateOnly(2026, 10, 7), 203), (new DateOnly(2026, 10, 8), 195), (new DateOnly(2026, 10, 9), 459), (new DateOnly(2026, 9, 18), 11)];

        var flagged = Assert.Single(WeeklyReportService.FindAnomalies("Execution", days, reportWeek));

        Assert.Equal(new DateOnly(2026, 9, 15), flagged.Date);
        Assert.Equal(199m, flagged.TypicalDaily);
        Assert.False(flagged.InReportWeek);
        // วันเดียวที่มีข้อมูล = ไม่มีฐานเทียบ, ต่ำกว่า 100 รายการ = ไม่แจ้งแม้จะสูงกว่าวันอื่นมาก
        Assert.Empty(WeeklyReportService.FindAnomalies("Defect", [(reportWeek, 900)], reportWeek));
        Assert.Empty(WeeklyReportService.FindAnomalies("Defect", [(reportWeek, 90), (reportWeek.AddDays(1), 1)], reportWeek));
    }

    [Fact]
    public void Daily_volume_queries_translate_to_sql()
    {
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
        Guid[] users = [Guid.NewGuid()];
        Guid[] projects = [Guid.NewGuid()];
        var from = new DateTime(2026, 9, 6, 17, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(35);

        foreach (var sql in new[]
        {
            WeeklyReportService.DailyExecutions(db, users, from, to, projects).ToQueryString(),
            WeeklyReportService.DailyDefects(db, users, from, to, projects).ToQueryString(),
            WeeklyReportService.DailyTestCases(db, users, from, to, projects).ToQueryString(),
        })
        {
            Assert.Contains("GROUP BY", sql);
            Assert.Contains("DATEADD(hour", sql);
        }
    }

    private static PerformanceViewer Manager(Guid lead) => new(lead, true, []);

    private static WeeklyReportService CreateService(QaDbContext db)
    {
        var clock = new FixedClock(new DateTime(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc));
        var repo = new PerformanceRepository(db, clock);
        var performance = new PerformanceService(repo, clock);
        var evaluations = new EvaluationService(new EvaluationRepository(db), repo, performance);
        return new WeeklyReportService(db, performance, evaluations, repo, clock);
    }

    private static async Task<(Guid Tester, Guid Lead)> SeedUsersAsync(QaDbContext db)
    {
        var tester = new User("tester", "Tester", null, "hash");
        var lead = new User("lead", "Lead", null, "hash");
        db.Users.AddRange(tester, lead);
        await db.SaveChangesAsync();
        return (tester.UserId, lead.UserId);
    }

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
