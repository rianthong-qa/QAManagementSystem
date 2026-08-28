namespace ProMaxx2.QA.Application.Execution;

public enum AssignmentWorkKind { DefectRetest, AutomationFailureReview, Regression }
public sealed record AssignmentIntegrationRequest(AssignmentWorkKind Kind, Guid TestCycleCaseId, Guid? OriginalTesterUserId, int RequiredSkillLevel, int EstimatedMinutes, bool PreferOriginalTester = true);
public sealed record AssignmentIntegrationDecision(Guid TestCycleCaseId, Guid? TesterUserId, bool UsedOriginalTester, string Decision, string Reason);

public static class AssignmentIntegrationRules
{
    public static AssignmentIntegrationDecision PreferOriginal(AssignmentIntegrationRequest request, IEnumerable<WeightedAssignmentCandidate> candidates)
    {
        if (request.PreferOriginalTester && request.OriginalTesterUserId is Guid original)
        {
            var candidate = candidates.FirstOrDefault(x => x.UserId == original && x.IsAvailable && x.SkillLevel >= request.RequiredSkillLevel && x.AvailableMinutes >= request.EstimatedMinutes);
            if (candidate is not null) return new(request.TestCycleCaseId, original, true, "OriginalTester", $"Original tester available, skill {candidate.SkillLevel}/{request.RequiredSkillLevel}, capacity available");
        }
        return new(request.TestCycleCaseId, null, false, "WeightedFallback", request.Kind switch { AssignmentWorkKind.DefectRetest => "Original tester unavailable or skill/capacity requirement not met", AssignmentWorkKind.AutomationFailureReview => "Automation review requires weighted reviewer selection", _ => "Regression case requires weighted workload balancing" });
    }
}
