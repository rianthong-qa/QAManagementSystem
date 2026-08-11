using ProMaxx2.QA.Domain.TestManagement;
namespace ProMaxx2.QA.UnitTests;
public sealed class TestCaseTests
{
 [Fact]public void Test_case_normalizes_code_and_keeps_steps(){var c=new TestCase(Guid.NewGuid(),Guid.NewGuid()," tc-001 ","Sale",null,null,"P0","Functional",false,null,[new(1,"Open sale",null,"Sale opens")],null);Assert.Equal("TC-001",c.TestCaseCode);Assert.Single(c.Steps);}
 [Fact]public void Step_requires_expected_result()=>Assert.Throws<ArgumentException>(()=>new TestCase(Guid.NewGuid(),Guid.NewGuid(),"TC","Sale",null,null,"P1",null,false,null,[new(1,"Action",null,"")],null));
 [Fact]public void Ready_requires_step(){var c=new TestCase(Guid.NewGuid(),Guid.NewGuid(),"TC","Sale",null,null,"P1",null,false,null,[],null);Assert.Throws<InvalidOperationException>(()=>c.ChangeStatus("Ready",null));}
 [Fact]public void Valid_case_can_be_ready(){var c=new TestCase(Guid.NewGuid(),Guid.NewGuid(),"TC","Sale",null,null,"P1",null,false,null,[new(1,"Action",null,"Expected")],null);c.ChangeStatus("Ready",null);Assert.Equal("Ready",c.Status);}
 [Fact]public void Revision_keeps_old_steps(){var c=new TestCase(Guid.NewGuid(),Guid.NewGuid(),"TC","Sale",null,null,"P1",null,false,null,[new(1,"Old",null,"Old result")],null);c.CreateRevision("Sale TH",null,null,[new(1,"ใหม่",null,"ผลลัพธ์ใหม่")],"แปลภาษา",null);Assert.Equal(2,c.RevisionNo);Assert.Equal(2,c.Steps.Count);}
 [Fact]public void Update_changes_metadata_and_creates_revision(){var c=new TestCase(Guid.NewGuid(),Guid.NewGuid(),"TC","Sale",null,null,"P1","Functional",false,null,[new(1,"Old",null,"Old result")],null);var moduleId=Guid.NewGuid();c.Update(moduleId,"Sale updated",null,null,"P0","Regression",true,null,[new(1,"New",null,"New result")],"Requirement changed",null);Assert.Equal(2,c.RevisionNo);Assert.Equal(moduleId,c.ModuleId);Assert.Equal("P0",c.Priority);Assert.True(c.AutomationCandidate);}
}
