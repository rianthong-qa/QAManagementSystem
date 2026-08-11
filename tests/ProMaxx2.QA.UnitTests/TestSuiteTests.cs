using ProMaxx2.QA.Domain.TestManagement;
namespace ProMaxx2.QA.UnitTests;
public sealed class TestSuiteTests
{
 [Fact]public void New_suite_normalizes_code_and_is_active(){var suite=new TestSuite(Guid.NewGuid()," smoke-p0 ","Smoke P0","Smoke",null,"P0");Assert.Equal("SMOKE-P0",suite.SuiteCode);Assert.True(suite.IsActive);}
 [Fact]public void Suite_can_be_updated_and_deactivated(){var suite=new TestSuite(Guid.NewGuid(),"REG","Regression",null,null,null);suite.Update("Critical Regression","Regression","Core cases","P1",false);Assert.Equal("Critical Regression",suite.SuiteName);Assert.False(suite.IsActive);}
 [Fact]public void Suite_requires_code_and_name(){Assert.Throws<ArgumentException>(()=>new TestSuite(Guid.NewGuid(),"","Name",null,null,null));Assert.Throws<ArgumentException>(()=>new TestSuite(Guid.NewGuid(),"CODE","",null,null,null));}
}
