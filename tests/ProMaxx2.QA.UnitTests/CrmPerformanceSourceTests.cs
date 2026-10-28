using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Integrations;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

public sealed class CrmPerformanceSourceTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Counts_tickets_by_crm_route_of_each_user()
    {
        await using var db = CreateDb();
        var qa = Guid.NewGuid();
        var viewer = Guid.NewGuid(); // ผู้ที่เปิดหน้า CRM — ต้องไม่ถูกนับเป็นผู้ทำ
        var noAccount = Guid.NewGuid();
        var project = Guid.NewGuid();
        db.CrmConfigurations.Add(new CrmConfiguration(qa, "M01", "6101", "enc", null, true));

        // BHD-1: QA 6101 → Dev 3003
        Flow(viewer, "BHD-1", null, State("BHD-1", "Test", "1001", "6101", "0"));
        Flow(viewer, "BHD-1", State("BHD-1", "Test", "1001", "6101", "0"), State("BHD-1", "Develop", "1001", "3003", "3003"));
        // BHD-2: QA 6101 → กลับ Support 1001
        Flow(viewer, "BHD-2", State("BHD-2", "Test", "1001", "Somchai (6101)", "0"), State("BHD-2", "Continue", "1001", "1001", "0"));
        // BHD-3: QA 6101 ปิดงาน
        Flow(viewer, "BHD-3", State("BHD-3", "Test", "1001", "6101", "0"), State("BHD-3", "Close", "1001", "6101", "0"));
        // BHD-4: รหัส 61010 ต้องไม่ match 6101
        Flow(viewer, "BHD-4", null, State("BHD-4", "Test", "1001", "61010", "0"));
        // BHD-5: 6101 เป็นเจ้าของเรื่อง (Support) ไม่ใช่ขั้น QA
        Flow(viewer, "BHD-5", null, State("BHD-5", "Continue", "6101", "6101", "0"));

        db.AuditLogs.Add(new AuditLog(qa, "Updated", CrmPerformanceSource.UpdateEntityType, "BHD-1", "x", null, null, null));
        db.AuditLogs.Add(new AuditLog(qa, "Updated", CrmPerformanceSource.UpdateEntityType, "BHD-2", "x", null, null, null));
        var defect = new Defect(project, null, null, null, "D-1", "จาก CRM", "High", "Open", qa, null, null, null, null, null);
        db.Defects.Add(defect);
        db.DefectActivities.Add(new DefectActivity(defect.DefectId, "CreatedFromCrm", "สร้าง Defect จาก CRM Ticket BHD-1", qa));
        await db.SaveChangesAsync();

        var result = await new CrmPerformanceSource(db).GetAsync([qa, viewer, noAccount], DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), [project], CancellationToken.None);

        var m = result[qa];
        Assert.True(m.HasCrmAccount);
        Assert.Equal(3, m.TicketsHandled);
        Assert.Equal(1, m.HandedToDev);
        Assert.Equal(1, m.ReturnedToSupport);
        Assert.Equal(1, m.Closed);
        Assert.Equal(2, m.Updates);
        Assert.Equal(1, m.DefectsCreated);
        Assert.False(result[noAccount].HasCrmAccount);
        Assert.Equal(0, result[viewer].TicketsHandled);

        var past = await new CrmPerformanceSource(db).GetAsync([qa], new DateTime(2020, 1, 1), new DateTime(2020, 2, 1), [project], CancellationToken.None);
        Assert.Equal(0, past[qa].TicketsHandled);
        var otherProject = await new CrmPerformanceSource(db).GetAsync([qa], DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), [Guid.NewGuid()], CancellationToken.None);
        Assert.Equal(0, otherProject[qa].DefectsCreated);

        void Flow(Guid actor, string job, CrmFlowState? before, CrmFlowState after)
            => db.AuditLogs.Add(new AuditLog(actor, before is null ? "FlowStarted" : "FlowChanged", "CrmTicketFlow", job, "flow",
                before is null ? null : JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(after, Json), null));
    }

    [Fact]
    public void Crm_performance_queries_translate_to_sql()
    {
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
        Guid?[] users = [Guid.NewGuid()];
        var from = DateTime.UtcNow.AddDays(-30);
        var to = DateTime.UtcNow;

        Assert.Contains("GROUP BY", CrmPerformanceSource.UpdateCounts(db, users, from, to).ToQueryString());
        Assert.Contains("GROUP BY", CrmPerformanceSource.DefectCounts(db, users, from, to, [Guid.NewGuid()]).ToQueryString());
        Assert.Contains("LIKE", CrmPerformanceSource.FlowRows(db, "6101", from, to).ToQueryString());
    }

    private static CrmFlowState State(string job, string status, string support, string qa, string developer) => new(job, status, support, qa, developer);

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
