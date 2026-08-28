using ProMaxx2.QA.Application.Execution;

namespace ProMaxx2.QA.UnitTests;

public sealed class WeightedAssignmentTests
{
    [Fact]
    public void P0_is_assigned_before_lower_priority_and_lowest_eligible_load_wins()
    {
        var qa1 = new WeightedAssignmentCandidate(Guid.NewGuid(), "QA01", 4, 70, 120, true, false);
        var qa2 = new WeightedAssignmentCandidate(Guid.NewGuid(), "QA02", 4, 40, 120, true, false);
        var cases = new[] { new WeightedAssignmentCase(Guid.NewGuid(), "P2", 2, 30, 4, false, false), new WeightedAssignmentCase(Guid.NewGuid(), "P0", 2, 30, 4, false, false) };
        var result = WeightedAssignmentEngine.Suggest(cases, new[] { qa1, qa2 });
        Assert.Equal("P0", cases.Single(x => x.TestCycleCaseId == result[0].TestCycleCaseId).Priority);
        Assert.Equal(qa2.UserId, result[0].RecommendedTesterUserId);
    }

    [Fact]
    public void Insufficient_skill_is_excluded_even_when_load_is_lower()
    {
        var good = new WeightedAssignmentCandidate(Guid.NewGuid(), "Good", 4, 70, 60, true, false);
        var weak = new WeightedAssignmentCandidate(Guid.NewGuid(), "Weak", 2, 10, 60, true, false);
        var result = WeightedAssignmentEngine.Suggest([new(Guid.NewGuid(), "P1", 2, 30, 4, false, false)], [good, weak]);
        Assert.Equal(good.UserId, result[0].RecommendedTesterUserId);
    }

    [Fact]
    public void Preview_expiry_and_version_mismatch_are_rejected()
    {
        var preview = new AssignmentPreviewState(Guid.NewGuid(), "v1", DateTime.UtcNow.AddMinutes(-1));
        var request = new AutoAssignConfirmRequest(preview.PreviewId, "v1", []);
        Assert.Throws<InvalidOperationException>(() => WeightedAssignmentWorkflow.ValidateConfirm(request, preview, DateTime.UtcNow));
        var valid = preview with { ExpiresAt = DateTime.UtcNow.AddMinutes(1) };
        Assert.Throws<InvalidOperationException>(() => WeightedAssignmentWorkflow.ValidateConfirm(request with { PreviewVersion = "v2" }, valid, DateTime.UtcNow));
    }

    [Fact]
    public void Rebalance_keeps_reviewer_required_cases_with_reviewers_and_balances_load()
    {
        var reviewer = new WeightedAssignmentCandidate(Guid.NewGuid(), "Reviewer", 5, 80, 120, true, true, 90);
        var tester = new WeightedAssignmentCandidate(Guid.NewGuid(), "Tester", 4, 20, 120, true, false, 20);
        var reviewCase = new WeightedAssignmentCase(Guid.NewGuid(), "P1", 3, 30, 4, true, true);
        var normalCase = new WeightedAssignmentCase(Guid.NewGuid(), "P2", 2, 30, 4, false, false);
        var result = WeightedAssignmentEngine.Rebalance([reviewCase, normalCase], [reviewer, tester]);
        Assert.Equal(reviewer.UserId, result[reviewCase.TestCycleCaseId]);
        Assert.Equal(tester.UserId, result[normalCase.TestCycleCaseId]);
    }
}
