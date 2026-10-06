using System.Text.Json;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.UnitTests;

public sealed class CrmTicketUpdatePayloadTests
{
    private static readonly DateTime NowThai = new(2026, 10, 3, 10, 30, 0);

    private static JsonElement Job(string status = "Continue", string assignto = "6101", string ownerSubjectId = "4926") =>
        JsonDocument.Parse($$"""
        {
          "jobNo": "BHD-1", "subject": "POS ค้าง", "status": "{{status}}", "assignto": "{{assignto}}",
          "ownerSubjectId": "{{ownerSubjectId}}", "sysDevelop": "6101", "sysserviceType": "S1", "description": "ข้อความเดิมของ Ticket",
          "member": "4926", "sysProductId": "P1", "duedate": "2026-09-09T00:00:00"
        }
        """).RootElement.Clone();

    [Fact]
    public void Reply_sends_only_the_new_message_as_description_and_keeps_other_fields()
    {
        var payload = CrmSendToCrmService.BuildTicketUpdatePayload(Job(), "BHD-1", "6101", "  โทรแจ้งลูกค้าแล้ว รอทดสอบ  ", null, null, false, "QA", NowThai);

        Assert.Equal("โทรแจ้งลูกค้าแล้ว รอทดสอบ", payload.Description); // CRM บันทึกเป็นแถวใหม่ในประวัติ — ไม่ต่อท้ายข้อความเดิม
        Assert.Equal("Continue", payload.Status);
        Assert.Equal("6101", payload.Assignto);
        Assert.Equal("6101", payload.SysDevelop);
        Assert.Equal("4926", payload.OwnerSubjectId);
        Assert.Equal("P1", payload.SysProductId);
        Assert.Equal("6101", payload.Posted);
    }

    [Fact]
    public void Assign_to_owner_sets_assignto_from_owner_subject_without_touching_sysdevelop()
    {
        var payload = CrmSendToCrmService.BuildTicketUpdatePayload(Job(), "BHD-1", "6101", "แก้ไขแล้ว ฝากตรวจสอบ", "Test", null, true, "QA", NowThai);

        Assert.Equal("4926", payload.Assignto);
        Assert.Equal("6101", payload.SysDevelop);
        Assert.Equal("Test", payload.Status);
        Assert.Equal("แก้ไขแล้ว ฝากตรวจสอบ", payload.Description);
    }

    [Fact]
    public void Change_without_message_writes_a_short_audit_note()
    {
        var payload = CrmSendToCrmService.BuildTicketUpdatePayload(Job(), "BHD-1", "6101", null, "Finish", null, true, "สมชาย", NowThai);

        Assert.Equal("[QA Hub] สมชาย (03/10/2569 10:30): สถานะ Continue → Finish, ส่งกลับเจ้าของเรื่อง (4926)", payload.Description);
    }

    [Fact]
    public void Attachment_only_change_writes_a_short_audit_note()
    {
        var payload = CrmSendToCrmService.BuildTicketUpdatePayload(Job(), "BHD-1", "6101", null, null, null, false, "QA", NowThai, attachmentCount: 2);

        Assert.Contains("แนบไฟล์ 2 รายการ", payload.Description);
    }

    [Fact]
    public void Close_uses_the_crm_wire_status_Close()
    {
        var payload = CrmSendToCrmService.BuildTicketUpdatePayload(Job(), "BHD-1", "6101", null, "Close", null, false, "สมชาย", NowThai);

        Assert.Equal("Close", payload.Status);
        Assert.Contains("Body", payload.ToFormFields().Keys);
        Assert.Contains("SubjectEmail", payload.ToFormFields().Keys);
        Assert.Equal(2, payload.CcEmails?.Count);

        var payloadWithNotificationRecipient = CrmSendToCrmService.BuildTicketUpdatePayload(Job(), "BHD-1", "6101", null, "Close", null, false, "QA", NowThai, closeNotificationEmail: "qa@example.com");
        Assert.Equal("qa@example.com", payloadWithNotificationRecipient.ToAdd);
        Assert.Contains("ToAdd", payloadWithNotificationRecipient.ToFormFields().Keys);
        Assert.Contains("สถานะ Continue → Close", payload.Description);
    }

    [Fact]
    public void Change_service_type_carries_the_selected_crm_lookup_id()
    {
        var payload = CrmSendToCrmService.BuildTicketUpdatePayload(Job(), "BHD-1", "6101", null, null, null, false, "สมชาย", NowThai, "S2");

        Assert.Equal("S2", payload.SysserViceType);
        Assert.Contains("Service S1 → S2", payload.Description);
    }

    [Fact]
    public void Change_product_carries_the_selected_crm_lookup_id()
    {
        var payload = CrmSendToCrmService.BuildTicketUpdatePayload(Job(), "BHD-1", "6101", null, null, null, false, "สมชาย", NowThai, null, "P2");

        Assert.Equal("P2", payload.SysProductId);
        Assert.Contains("Product P1 → P2", payload.Description);
    }

    [Fact]
    public void Nothing_to_update_or_missing_owner_is_rejected()
    {
        Assert.Throws<CrmIntegrationException>(() => CrmSendToCrmService.BuildTicketUpdatePayload(Job(), "BHD-1", "6101", "  ", "continue", null, false, "QA", NowThai));
        Assert.Throws<CrmIntegrationException>(() => CrmSendToCrmService.BuildTicketUpdatePayload(Job(ownerSubjectId: ""), "BHD-1", "6101", "ส่งกลับ", null, null, true, "QA", NowThai));
    }

    [Fact]
    public void Editable_statuses_match_the_crm_status_filter()
    {
        Assert.Equal(new[] { "Open", "Continue", "Approve", "Develop", "Planning", "Test", "EditErr", "Finish", "Close" }, CrmController.EditableStatuses);
    }

    [Fact]
    public void Reply_note_contains_only_the_new_comment_and_keeps_the_suffix_within_crm_limit()
    {
        Assert.Equal("[QA Hub] สมชาย (03/10/2569 10:30): แก้ไขแล้ว", CrmSendToCrmService.BuildReplyNote("สมชาย", NowThai, "แก้ไขแล้ว"));

        var suffix = " [แนบรูป 2 รูป ดูได้ที่ https://qahub.store/?d=abcdefgh]";
        var note = CrmSendToCrmService.BuildReplyNote("สมชาย", NowThai, new string('ก', 2000), suffix);
        Assert.Equal(1000, note.Length);
        Assert.EndsWith(suffix, note);
        Assert.StartsWith("[QA Hub] สมชาย (03/10/2569 10:30): ", note);
    }
}
