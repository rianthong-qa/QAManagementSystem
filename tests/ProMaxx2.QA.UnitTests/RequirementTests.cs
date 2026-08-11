using ProMaxx2.QA.Domain.Requirements;
namespace ProMaxx2.QA.UnitTests;
public sealed class RequirementTests
{
 private static Requirement New()=>new(Guid.NewGuid(),null,Guid.NewGuid(),"req-001","Original",null,null,"P1","High",null,null,true,null);
 [Fact]public void New_requirement_has_initial_revision(){var r=New();Assert.Equal(1,r.RevisionNo);Assert.Single(r.Revisions);Assert.Equal("REQ-001",r.RequirementCode);}
 [Fact]public void Revision_preserves_history(){var r=New();r.CreateRevision("Changed","Description","Criteria","Scope changed",Guid.NewGuid());Assert.Equal(2,r.RevisionNo);Assert.Equal(2,r.Revisions.Count);Assert.Equal("Changed",r.Title);}
 [Fact]public void Revision_requires_reason(){var r=New();Assert.Throws<ArgumentException>(()=>r.CreateRevision("Changed",null,null,"",null));}
 [Fact]public void Invalid_priority_is_rejected()=>Assert.Throws<ArgumentException>(()=>new Requirement(Guid.NewGuid(),null,Guid.NewGuid(),"REQ","Title",null,null,"Critical",null,null,null,true,null));
 [Fact]public void Delete_is_soft(){var r=New();r.SoftDelete(null);Assert.True(r.IsDeleted);}
}
