using ProMaxx2.QA.Domain.Execution;
namespace ProMaxx2.QA.UnitTests;
public sealed class TestCycleTests
{
 [Fact]public void New_cycle_is_draft_and_normalizes_code(){var cycle=new TestCycle(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),null," cycle-01 ","Regression",null,null,null,null,null,null);Assert.Equal("CYCLE-01",cycle.CycleCode);Assert.Equal("Draft",cycle.Status);}
 [Fact]public void Cycle_rejects_end_before_start(){var start=DateTime.UtcNow;Assert.Throws<ArgumentException>(()=>new TestCycle(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),null,"C1","Cycle",null,start,start.AddDays(-1),null,null,null));}
 [Fact]public void Cycle_accepts_supported_status(){var cycle=new TestCycle(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),null,"C1","Cycle",null,null,null,null,null,null);cycle.ChangeStatus("InProgress");Assert.Equal("InProgress",cycle.Status);Assert.Throws<ArgumentException>(()=>cycle.ChangeStatus("Unknown"));}
 [Fact]public void Environment_requires_name(){Assert.Throws<ArgumentException>(()=>new TestEnvironment(Guid.NewGuid(),"",null));}
}
