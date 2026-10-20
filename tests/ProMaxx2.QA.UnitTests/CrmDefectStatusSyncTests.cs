using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Domain.Defects;

namespace ProMaxx2.QA.UnitTests;

public sealed class CrmDefectStatusSyncTests
{
    [Theory]
    [InlineData("Open", "Finish", "Resolved")]
    [InlineData("In Progress", "finish", "Resolved")]
    [InlineData("Open", "Close", "Closed")]
    [InlineData("Resolved", "Close", "Closed")]
    public void Finished_or_closed_ticket_moves_defect_forward(string defectStatus, string crmStatus, string expected)
    {
        var defect = NewDefect(defectStatus);
        var actor = Guid.NewGuid();

        Assert.Equal(expected, CrmDefectStatusSync.Apply(defect, crmStatus, actor));
        Assert.Equal(expected, defect.Status);
        Assert.Equal(actor, defect.UpdatedBy);
    }

    [Theory]
    [InlineData("Open", "Test")]        // ยังไม่จบงาน — ไม่เปลี่ยน
    [InlineData("Open", null)]
    [InlineData("Closed", "Finish")]    // ไม่ย้อน Closed → Resolved
    [InlineData("Rejected", "Close")]   // QA ตัดสินแล้วว่าไม่ใช่ Defect
    [InlineData("Resolved", "Finish")]  // ตรงอยู่แล้ว
    public void Other_cases_leave_defect_unchanged(string defectStatus, string? crmStatus)
    {
        var defect = NewDefect(defectStatus);

        Assert.Null(CrmDefectStatusSync.Apply(defect, crmStatus, Guid.NewGuid()));
        Assert.Equal(defectStatus, defect.Status);
    }

    private static Defect NewDefect(string status)
        => new(Guid.NewGuid(), null, null, null, "D-1", "Bug", "High", status, Guid.NewGuid(), null, null, null, null, null);
}
