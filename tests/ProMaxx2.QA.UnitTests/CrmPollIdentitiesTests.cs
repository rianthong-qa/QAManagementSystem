using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Integrations;

namespace ProMaxx2.QA.UnitTests;

/// <summary>CRM poller เลือกบัญชีตามลำดับ Assignee → ผู้ส่งไป CRM → บัญชีกลาง — Defect ที่ไม่มี Assignee ต้องยังถูก poll</summary>
public sealed class CrmPollIdentitiesTests
{
    [Fact]
    public async Task Falls_back_from_assignee_to_sender_to_mirror_account()
    {
        using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var projectId = Guid.NewGuid();
        Guid assignee = Guid.NewGuid(), sender = Guid.NewGuid(), oldSender = Guid.NewGuid(), mirror = Guid.NewGuid();
        var assigned = Defect(projectId, "DF-1", assignee);
        var sentOnly = Defect(projectId, "DF-2", null);
        var nobody = Defect(projectId, "DF-3", null);
        db.Defects.AddRange(assigned, sentOnly, nobody);
        db.DefectActivities.AddRange(
            new DefectActivity(assigned.DefectId, "CrmSent", "sent", sender),
            new DefectActivity(sentOnly.DefectId, "CrmSent", "sent", oldSender),
            new DefectActivity(sentOnly.DefectId, "Updated", "edit", Guid.NewGuid()));
        db.CrmConfigurations.AddRange(
            new CrmConfiguration(mirror, "M", "6101", "enc", null, true),
            new CrmConfiguration(Guid.NewGuid(), "M", "6101", "", null, true));
        await db.SaveChangesAsync();

        var result = await CrmPollIdentities.ResolveAsync(db, [assigned, sentOnly, nobody], "6101", CancellationToken.None);

        Assert.Equal([assignee, sender, mirror], result[assigned.DefectId]);
        Assert.Equal([oldSender, mirror], result[sentOnly.DefectId]);
        Assert.Equal([mirror], result[nobody.DefectId]);
        Assert.Empty((await CrmPollIdentities.ResolveAsync(db, [nobody], "9999", CancellationToken.None))[nobody.DefectId]);
        Assert.Equal(oldSender, await CrmPollIdentities.SenderAsync(db, sentOnly.DefectId, CancellationToken.None));
    }

    private static Defect Defect(Guid projectId, string code, Guid? assignee) => new(projectId, null, null, null, code, code, "Low", "Open", null, null, null, null, null, assignee);
}
