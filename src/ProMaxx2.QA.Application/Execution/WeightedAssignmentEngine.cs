namespace ProMaxx2.QA.Application.Execution;

public sealed record WeightedAssignmentCase(Guid TestCycleCaseId, string Priority, int ComplexityWeight, int EstimatedMinutes, int RequiredSkillLevel, bool IsCritical, bool ReviewerRequired);
public sealed record WeightedAssignmentCandidate(Guid UserId, string DisplayName, int SkillLevel, int CurrentLoadPercent, int AvailableMinutes, bool IsAvailable, bool IsReviewer, int ExperienceScore = 0);
public sealed record WeightedAssignmentSuggestion(Guid TestCycleCaseId, Guid? RecommendedTesterUserId, string? RecommendedTesterName, int CaseWeight, int Score, int CurrentLoadPercent, int AfterLoadPercent, IReadOnlyList<string> Reasons, string? ErrorCode);

public static class WeightedAssignmentEngine
{
    public const string AlgorithmVersion = "weighted-v1";

    public static IReadOnlyList<WeightedAssignmentSuggestion> Suggest(IEnumerable<WeightedAssignmentCase> cases, IEnumerable<WeightedAssignmentCandidate> candidates)
    {
        var pool = candidates.ToList();
        var load = pool.ToDictionary(x => x.UserId, x => x.CurrentLoadPercent);
        return cases.OrderByDescending(x => PriorityRank(x.Priority)).ThenByDescending(x => x.IsCritical).ThenByDescending(x => x.ComplexityWeight).Select(item => SuggestOne(item, pool, load)).ToList();
    }

    public static IReadOnlyDictionary<Guid, Guid> Rebalance(IEnumerable<WeightedAssignmentCase> cases, IEnumerable<WeightedAssignmentCandidate> candidates, int maxLoadPercent = 100)
    {
        var pool = candidates.Where(x => x.IsAvailable && x.CurrentLoadPercent <= maxLoadPercent).OrderBy(x => x.CurrentLoadPercent).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        var result = new Dictionary<Guid, Guid>(); var load = pool.ToDictionary(x => x.UserId, x => x.CurrentLoadPercent);
        foreach (var item in cases.Where(x => x.IsCritical || x.ReviewerRequired).Concat(cases.Where(x => !x.IsCritical && !x.ReviewerRequired)))
        {
            var candidate = pool.Where(x => x.SkillLevel >= item.RequiredSkillLevel && x.AvailableMinutes >= item.EstimatedMinutes && (!item.ReviewerRequired || x.IsReviewer)).OrderBy(x => load[x.UserId]).ThenByDescending(x => x.ExperienceScore).FirstOrDefault();
            if (candidate is null) continue; result[item.TestCycleCaseId] = candidate.UserId; load[candidate.UserId] += Math.Max(1, item.EstimatedMinutes / 10);
        }
        return result;
    }

    private static WeightedAssignmentSuggestion SuggestOne(WeightedAssignmentCase item, IReadOnlyList<WeightedAssignmentCandidate> pool, IDictionary<Guid, int> load)
    {
        var weight = Math.Clamp(item.ComplexityWeight, 1, 100) + PriorityWeight(item.Priority) + Math.Max(1, item.EstimatedMinutes / 15);
        var eligible = pool.Where(x => x.IsAvailable && x.SkillLevel >= item.RequiredSkillLevel && x.AvailableMinutes >= item.EstimatedMinutes && (!item.ReviewerRequired || x.IsReviewer)).OrderBy(x => load[x.UserId]).ThenByDescending(x => x.ExperienceScore).ThenByDescending(x => x.SkillLevel).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        if (eligible.Count == 0) return new(item.TestCycleCaseId, null, null, weight, 0, 0, 0, ["No eligible tester satisfies skill, availability and capacity"], item.IsCritical ? "AUTOASSIGN_CRITICAL_REVIEWER_REQUIRED" : "AUTOASSIGN_NO_ELIGIBLE_TESTER");
        var selected = eligible[0]; var current = load[selected.UserId]; var after = current + Math.Max(1, item.EstimatedMinutes / 10); load[selected.UserId] = after;
        var workloadScore = Math.Clamp(100 - current, 0, 100); var skillScore = selected.SkillLevel * 20; var experienceScore = Math.Clamp(selected.ExperienceScore, 0, 100); var score = Math.Clamp((int)Math.Round(workloadScore * .45 + skillScore * .35 + experienceScore * .20), 0, 100);
        var reasons = new List<string> { $"Skill requirement passed ({selected.SkillLevel}/{item.RequiredSkillLevel})", $"Lowest eligible workload ({current}%)", $"Capacity available ({selected.AvailableMinutes} min)", $"Experience score ({experienceScore}/100)" };
        if (item.IsCritical) reasons.Add("Critical case skill rule passed"); if (item.ReviewerRequired) reasons.Add("Reviewer requirement passed");
        return new(item.TestCycleCaseId, selected.UserId, selected.DisplayName, weight, score, current, after, reasons, null);
    }

    private static int PriorityRank(string? value) => value?.ToUpperInvariant() switch { "P0" => 4, "P1" => 3, "P2" => 2, _ => 1 };
    private static int PriorityWeight(string? value) => value?.ToUpperInvariant() switch { "P0" => 40, "P1" => 30, "P2" => 20, "P3" => 10, _ => 10 };
}
