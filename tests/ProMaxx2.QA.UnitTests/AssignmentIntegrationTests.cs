using ProMaxx2.QA.Application.Execution;
namespace ProMaxx2.QA.UnitTests;
public sealed class AssignmentIntegrationTests
{
    [Fact]
    public void Retest_prefers_original_tester_when_available_and_skill_passes()
    {
        var id = Guid.NewGuid(); var request = new AssignmentIntegrationRequest(AssignmentWorkKind.DefectRetest, Guid.NewGuid(), id, 4, 30); var decision = AssignmentIntegrationRules.PreferOriginal(request, [new(id, "QA", 4, 20, 60, true, false)]);
        Assert.True(decision.UsedOriginalTester); Assert.Equal(id, decision.TesterUserId);
    }
    [Fact]
    public void Automation_review_falls_back_when_original_tester_is_unavailable()
    {
        var id = Guid.NewGuid(); var request = new AssignmentIntegrationRequest(AssignmentWorkKind.AutomationFailureReview, Guid.NewGuid(), id, 4, 30); var decision = AssignmentIntegrationRules.PreferOriginal(request, [new(id, "QA", 5, 20, 60, false, true)]);
        Assert.False(decision.UsedOriginalTester); Assert.Equal("WeightedFallback", decision.Decision);
    }
}
