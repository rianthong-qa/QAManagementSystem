using ProMaxx2.QA.Domain.Releases;
namespace ProMaxx2.QA.UnitTests;
public sealed class ReleaseTests
{
 [Fact]public void New_release_is_draft(){var r=new Release(Guid.NewGuid(),"2026.08","10.0","Major",null,null,null,null);Assert.Equal("Draft",r.Status);}
 [Fact]public void Released_status_sets_actual_date(){var r=new Release(Guid.NewGuid(),"2026.08","10.0",null,null,null,null,null);r.ChangeStatus("released",null);Assert.Equal("Released",r.Status);Assert.NotNull(r.ActualReleaseDate);}
 [Fact]public void Invalid_status_is_rejected(){var r=new Release(Guid.NewGuid(),"2026.08","10.0",null,null,null,null,null);Assert.Throws<ArgumentException>(()=>r.ChangeStatus("Unknown",null));}
 [Fact]public void Build_can_be_marked_rc(){var b=new Build(Guid.NewGuid(),"10.0.228",null,null,null,null,null,null,null);b.MarkReleaseCandidate();Assert.True(b.IsReleaseCandidate);}
}
