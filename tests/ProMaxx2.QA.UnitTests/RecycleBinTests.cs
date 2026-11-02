using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Performance;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>รายการที่ลบแล้ว (soft delete) — SYS_ADMIN ดูและกู้คืนได้; บันทึกงาน/เอกสารความรู้เลิกลบถาวร; สิทธิ์ลบ Risk ตามสถานะ</summary>
public sealed class RecycleBinTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "qa-recycle-bin-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } }

    private KnowledgeFileService Files(QaDbContext db) => new(db, new TestEnvironment(root), new ConfigurationBuilder().Build(), new KnowledgeIndexTrigger());
    private RecycleBinService Bin(QaDbContext db) => new(db, new DefectActivityService(db), Files(db), new KnowledgeIndexTrigger());

    [Fact]
    public async Task TestCase_ListsDeletedWithDeleterAndRestores()
    {
        var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var b = await AutomationTestFixtures.SeedBaselineAsync(db, automationCandidate: false);
        var admin = new User("admin", "Admin One", null, "hash");
        db.Add(admin);
        b.TestCase.SoftDelete(admin.UserId);
        await db.SaveChangesAsync();

        var rows = await Bin(db).ListAsync("test-cases", b.Project.ProjectId, default);
        var row = Assert.Single(rows);
        Assert.Equal(b.TestCase.TestCaseCode, row.Code);
        Assert.Equal("Admin One", row.DeletedByName);
        Assert.NotNull(row.DeletedAt);
        Assert.Empty(await Bin(db).ListAsync("test-cases", Guid.NewGuid(), default));

        Assert.True(await Bin(db).RestoreAsync("test-cases", b.TestCase.TestCaseId, admin.UserId, null, default));

        var saved = await db.TestCases.AsNoTracking().SingleAsync(x => x.TestCaseId == b.TestCase.TestCaseId);
        Assert.False(saved.IsDeleted);
        Assert.Null(saved.DeletedAt);
        Assert.True(await db.AuditLogs.AnyAsync(x => x.Action == "Restored" && x.EntityId == b.TestCase.TestCaseId.ToString()));
        // กู้คืนซ้ำ = ไม่พบรายการที่ลบแล้ว
        Assert.False(await Bin(db).RestoreAsync("test-cases", b.TestCase.TestCaseId, admin.UserId, null, default));
    }

    [Fact]
    public async Task TestPlan_DeletedPlanListedAndRestoredSoItCanBeReopened()
    {
        var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var b = await AutomationTestFixtures.SeedBaselineAsync(db);
        var plan = new ProMaxx2.QA.Domain.TestPlanning.TestPlan(b.Project.ProjectId, b.Release.ReleaseId, "TP-001", "Regression", "Regression", null, null, null, null, null);
        plan.Cancel(null);
        plan.SoftDelete(hasRuns: true, null);
        db.Add(plan);
        await db.SaveChangesAsync();

        var row = Assert.Single(await Bin(db).ListAsync("test-plans", b.Project.ProjectId, default));
        Assert.Equal("TP-001", row.Code);
        Assert.True(await Bin(db).RestoreAsync("test-plans", plan.TestPlanId, null, null, default));
        Assert.False(plan.IsDeleted);
        plan.Reopen(null); // กู้คืนแล้ว Reopen ปลดล็อก Cycle ได้
        Assert.False(plan.IsFrozen);
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.EntityType == "TestPlan" && x.Action == "Restored"));
    }

    [Fact]
    public async Task Defect_RestoreLogsActivity()
    {
        var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var b = await AutomationTestFixtures.SeedBaselineAsync(db, automationCandidate: false);
        var defect = new Defect(b.Project.ProjectId, null, null, null, "DEF-1", "Broken", "High", "Open", null, null, null, null, null, null);
        defect.SoftDelete(null);
        db.Add(defect); await db.SaveChangesAsync();

        Assert.True(await Bin(db).RestoreAsync("defects", defect.DefectId, null, null, default));

        Assert.False((await db.Defects.AsNoTracking().SingleAsync(x => x.DefectId == defect.DefectId)).IsDeleted);
        Assert.True(await db.DefectActivities.AnyAsync(x => x.DefectId == defect.DefectId && x.ActionType == "Restored"));
    }

    [Theory]
    [InlineData("Draft", true)]
    [InlineData("Rejected", true)]
    [InlineData("Submitted", false)]
    [InlineData("Approved", false)]
    public void RiskAcceptance_DeleteOnlyWhileEditable(string status, bool allowed)
    {
        var risk = new RiskAcceptance(Guid.NewGuid(), Guid.NewGuid(), null, "Risk", "Issue", "High", "High", null, null, null, null, null);
        if (status != "Draft") risk.Submit(null);
        if (status == "Rejected") risk.Reject(null, null);
        if (status == "Approved") risk.Approve(null, null);

        if (allowed) { risk.SoftDelete(null); Assert.True(risk.IsDeleted); }
        else { Assert.Throws<InvalidOperationException>(() => risk.SoftDelete(null)); Assert.False(risk.IsDeleted); }
    }

    [Fact]
    public async Task WorkLog_SoftDeletedHiddenFromListsAndRestorable()
    {
        var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var owner = new User("tester", "Tester", null, "hash");
        var day = new DateOnly(2026, 10, 1);
        var entry = new WorkLogEntry(owner.UserId, day, "Testing", "WorkLog", "Regression run", null, 2, null, null, owner.UserId);
        db.AddRange(owner, entry); await db.SaveChangesAsync();

        entry.SoftDelete(owner.UserId); await db.SaveChangesAsync();

        Assert.Empty(await PerformanceQueries.WorkLogList(db, owner.UserId, day, day, false, owner.UserId, true).ToListAsync());
        Assert.Empty(await PerformanceQueries.WorkLogs(db, [owner.UserId], day, day.AddDays(1)).ToListAsync());
        var row = Assert.Single(await Bin(db).ListAsync("work-logs", null, default));
        Assert.Equal("Tester · 2026-10-01", row.Detail);

        Assert.True(await Bin(db).RestoreAsync("work-logs", entry.WorkLogEntryId, null, null, default));
        Assert.Single(await PerformanceQueries.WorkLogList(db, owner.UserId, day, day, false, owner.UserId, true).ToListAsync());
    }

    [Fact]
    public async Task KnowledgeFile_DeleteKeepsFileOnDiskSoItCanBeRestored()
    {
        var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var userId = Guid.NewGuid();
        var upload = await Files(db).UploadAsync([new("guide.md", Encoding.UTF8.GetBytes("# คู่มือ\nเนื้อหา"))], null, null, userId, null, default);
        var fileId = Assert.Single(upload).Item!.KnowledgeFileId;

        Assert.True(await Files(db).DeleteAsync(fileId, [], userId, null, default));
        var file = await db.KnowledgeFiles.AsNoTracking().SingleAsync(x => x.KnowledgeFileId == fileId);
        Assert.True(Files(db).StoredFileExists(file));
        Assert.Equal(userId, file.DeletedBy);

        Assert.True(await Bin(db).RestoreAsync("knowledge-files", fileId, userId, null, default));
        Assert.False((await db.KnowledgeFiles.AsNoTracking().SingleAsync(x => x.KnowledgeFileId == fileId)).IsDeleted);
    }

    [Fact]
    public async Task KnowledgeFile_RestoreRefusesWhenSameContentWasUploadedAgain()
    {
        var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var userId = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes("# คู่มือโปรโมชั่น\nขั้นตอนการตั้งค่าโปรโมชั่นส่วนลดสำหรับสาขา และการตรวจสอบผลก่อนเปิดใช้งานจริง");
        var fileId = Assert.Single(await Files(db).UploadAsync([new("guide.md", bytes)], null, null, userId, null, default)).Item!.KnowledgeFileId;
        await Files(db).DeleteAsync(fileId, [], userId, null, default);
        var again = Assert.Single(await Files(db).UploadAsync([new("guide-v2.md", bytes)], null, null, userId, null, default));
        Assert.True(again.Item is not null, again.Message);

        await Assert.ThrowsAsync<RestoreConflictException>(() => Bin(db).RestoreAsync("knowledge-files", fileId, userId, null, default));
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
    }
}
