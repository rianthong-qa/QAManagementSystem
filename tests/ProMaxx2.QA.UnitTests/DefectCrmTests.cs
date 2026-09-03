using ProMaxx2.QA.Domain.Defects;
namespace ProMaxx2.QA.UnitTests;
public sealed class DefectCrmTests
{
 private static Defect NewDefect()=>new(Guid.NewGuid(),null,null,null,"DEF-1","Sample bug","Medium","Open",null,null,null,null,null,null);
 [Fact]public void New_defect_has_no_crm_link(){var d=NewDefect();Assert.Equal("None",d.CrmSyncStatus);Assert.Null(d.CrmTicketId);Assert.Null(d.CrmLastSyncedAt);}
 [Fact]public void SetCrmTicket_links_and_stamps_sync_time(){var d=NewDefect();var now=DateTime.UtcNow;d.SetCrmTicket("BHD690831000034",now);Assert.Equal("Linked",d.CrmSyncStatus);Assert.Equal("BHD690831000034",d.CrmTicketId);Assert.Equal(now,d.CrmLastSyncedAt);}
 [Fact]public void SetCrmTicket_rejects_blank_ticket_id(){var d=NewDefect();Assert.Throws<ArgumentException>(()=>d.SetCrmTicket("  ",DateTime.UtcNow));}
 [Fact]public void SetCrmSyncFailed_marks_failed_without_a_ticket(){var d=NewDefect();var now=DateTime.UtcNow;d.SetCrmSyncFailed(now);Assert.Equal("Failed",d.CrmSyncStatus);Assert.Null(d.CrmTicketId);Assert.Equal(now,d.CrmLastSyncedAt);}
 [Fact]public void A_failed_resend_does_not_clear_a_prior_successful_link(){var d=NewDefect();d.SetCrmTicket("BHD690831000034",DateTime.UtcNow);d.SetCrmSyncFailed(DateTime.UtcNow);Assert.Equal("Failed",d.CrmSyncStatus);Assert.Equal("BHD690831000034",d.CrmTicketId);}
 [Fact]public void UpdateCrmSnapshot_records_status_and_assignto_without_touching_workflow_status(){var d=NewDefect();d.SetCrmTicket("BHD690831000034",DateTime.UtcNow);d.UpdateCrmSnapshot("Develop","6101");Assert.Equal("Develop",d.CrmLastKnownStatus);Assert.Equal("6101",d.CrmLastKnownAssignto);Assert.Equal("Open",d.Status);}
 [Fact]public void UpdateCrmSnapshot_accepts_nulls(){var d=NewDefect();d.UpdateCrmSnapshot(null,null);Assert.Null(d.CrmLastKnownStatus);Assert.Null(d.CrmLastKnownAssignto);}
}
