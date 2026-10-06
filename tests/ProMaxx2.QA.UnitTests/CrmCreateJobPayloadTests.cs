using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.UnitTests;

public sealed class CrmCreateJobPayloadTests
{
    private static CrmCreateJobPayload Payload(string nickName = "", string lineId = "", string refJobNo = "") => new(
        Subject: "S", Member: "6101", FName: "F", LName: "", Tel: "02", Email: "", SysCustomerType: "1", RecipientId: "6101",
        OwnerSubjectId: "6101", Assignto: "6101", SysDevelop: "", Status: "Open", Source: "Call", BranchId: "00000",
        SysserViceType: "1", SysFollowupId: "1", SysProductId: "P1", SysVersionId: "0", SysOsId: "0", Description: "D",
        Posted: "6101", JobType: "HD", ContactDate: "2026-10-4T10:00", Duedate: "2026-10-4T00:00:00",
        NickName: nickName, LineId: lineId, RefJobNo: refJobNo);

    [Fact]
    public void ToFormFields_omits_optional_new_job_fields_when_empty()
    {
        var fields = Payload().ToFormFields();

        Assert.Equal(24, fields.Count);
        Assert.False(fields.ContainsKey("NickName"));
        Assert.False(fields.ContainsKey("Fax"));
        Assert.False(fields.ContainsKey("RefJobNo"));
    }

    [Fact]
    public void ToFormFields_includes_optional_new_job_fields_when_filled()
    {
        var fields = Payload("Nok", "line-1", "BHD-1").ToFormFields();

        Assert.Equal("Nok", fields["NickName"]);
        // LineID ของฟอร์ม CRM คือ field "Fax"
        Assert.Equal("line-1", fields["Fax"]);
        Assert.False(fields.ContainsKey("LineId"));
        Assert.Equal("BHD-1", fields["RefJobNo"]);
        Assert.Equal("Open", fields["Status"]);
    }

    [Fact]
    public void AttachmentRules_accept_crm_file_types_within_limits()
    {
        Assert.Null(CrmAttachmentRules.Validate([("a.JPG", 100), ("b.pdf", CrmAttachmentRules.MaxFileBytes), ("c.docx", 1)]));
    }

    [Theory]
    [InlineData("x.exe", 10, "ไม่รองรับ")]
    [InlineData("x.png", 0, "ว่างเปล่า")]
    [InlineData("x.png", 5 * 1024 * 1024 + 1, "เกิน 5 MB")]
    public void AttachmentRules_reject_invalid_file(string name, long length, string expected)
    {
        Assert.Contains(expected, CrmAttachmentRules.Validate([(name, length)]));
    }

    [Fact]
    public void AttachmentRules_reject_too_many_files_and_total_size()
    {
        Assert.Contains("สูงสุด 10", CrmAttachmentRules.Validate(Enumerable.Range(0, 11).Select(i => ($"{i}.png", 10L)).ToList()));
        Assert.Contains("25 MB", CrmAttachmentRules.Validate(Enumerable.Range(0, 6).Select(i => ($"{i}.png", CrmAttachmentRules.MaxFileBytes)).ToList()));
    }
}
