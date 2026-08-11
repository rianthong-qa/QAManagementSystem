using ProMaxx2.QA.Domain.Execution;
namespace ProMaxx2.QA.UnitTests;
public sealed class ExecutionTests
{
 [Theory][InlineData("Pass")][InlineData("Fail")][InlineData("Blocked")][InlineData("Skipped")]public void Execution_accepts_final_status(string status){var e=new TestExecution(Guid.NewGuid(),1,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),status,"actual",null,[]);Assert.Equal(status,e.Status);Assert.NotNull(e.CompletedAt);}
 [Fact]public void Execution_rejects_invalid_status(){Assert.Throws<ArgumentException>(()=>new TestExecution(Guid.NewGuid(),1,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Running",null,null,[]));}
 [Fact]public void Execution_captures_step_results(){var e=new TestExecution(Guid.NewGuid(),1,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Fail",null,null,[new StepResultInput(1,"Pass","ok",null),new StepResultInput(2,"Fail","wrong","check")]);Assert.Equal(2,e.StepResults.Count);}
 [Fact]public void Execution_soft_delete_preserves_record_and_audit(){var userId=Guid.NewGuid();var e=new TestExecution(Guid.NewGuid(),1,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Pass",null,null,[]);e.SoftDelete(userId);Assert.True(e.IsDeleted);Assert.Equal(userId,e.DeletedBy);Assert.NotNull(e.DeletedAt);}
}
