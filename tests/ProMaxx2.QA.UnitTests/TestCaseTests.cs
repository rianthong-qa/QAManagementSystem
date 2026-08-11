using ProMaxx2.QA.Domain.TestManagement;
namespace ProMaxx2.QA.UnitTests;
public sealed class TestCaseTests
{
 [Fact]public void Test_case_normalizes_code_and_keeps_steps(){var c=new TestCase(Guid.NewGuid(),Guid.NewGuid()," tc-001 ","Sale",null,null,"P0","Functional",false,null,[new(1,"Open sale",null,"Sale opens")],null);Assert.Equal("TC-001",c.TestCaseCode);Assert.Single(c.Steps);}
 [Fact]public void Step_requires_expected_result()=>Assert.Throws<ArgumentException>(()=>new TestCase(Guid.NewGuid(),Guid.NewGuid(),"TC","Sale",null,null,"P1",null,false,null,[new(1,"Action",null,"")],null));
 [Fact]public void Ready_requires_step(){var c=new TestCase(Guid.NewGuid(),Guid.NewGuid(),"TC","Sale",null,null,"P1",null,false,null,[],null);Assert.Throws<InvalidOperationException>(()=>c.ChangeStatus("Ready",null));}
 [Fact]public void Valid_case_can_be_ready(){var c=new TestCase(Guid.NewGuid(),Guid.NewGuid(),"TC","Sale",null,null,"P1",null,false,null,[new(1,"Action",null,"Expected")],null);c.ChangeStatus("Ready",null);Assert.Equal("Ready",c.Status);}
}
