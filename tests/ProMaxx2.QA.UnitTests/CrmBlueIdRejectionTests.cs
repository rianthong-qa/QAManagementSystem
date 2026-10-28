using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.UnitTests;

public sealed class CrmBlueIdRejectionTests
{
    [Fact]
    public void Missing_bluesea_licence_is_explained_in_thai()
    {
        var message = CrmTokenService.BlueIdRejectionMessage("{\"error\":\"invalid_grant\",\"error_description\":\"C0003 : NotCloudProductLicence\"}", "6915");

        Assert.NotNull(message);
        Assert.StartsWith("บัญชี CRM 6915 ยังไม่ได้รับสิทธิ์ใช้งาน CRM", message);
        Assert.Contains("Merchant ID", message);
        Assert.EndsWith("(รหัสจาก BlueID: C0003)", message);
        Assert.DoesNotContain("NotCloudProductLicence", message);
    }

    [Fact]
    public void Other_oidc_errors_show_blueid_description()
    {
        var message = CrmTokenService.BlueIdRejectionMessage("Pretty-print\n{\"error\":\"invalid_grant\",\"error_description\":\"C0001 : InvalidUser\"}", null);

        Assert.NotNull(message);
        Assert.StartsWith("เข้าสู่ระบบ CRM ด้วยบัญชี CRM ของคุณไม่สำเร็จ", message);
        Assert.EndsWith("(รหัสจาก BlueID: C0001)", message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Support list · Ticket BHD690930000003")]
    public void Normal_pages_are_not_treated_as_rejection(string? body)
    {
        Assert.Null(CrmTokenService.BlueIdRejectionMessage(body, "6915"));
    }
}
