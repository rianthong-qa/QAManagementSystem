using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Regression;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>Resolves which of a set of Test Case ids are eligible to run as Automation right now — extracted
/// (AUT-REG-002) from <c>RegressionController.AutomationRunPreview</c> (AUT-REG-001) so the interactive preview and
/// the automatic <see cref="RegressionScheduleTriggerService"/> share one implementation.</summary>
public static class RegressionAutomationRunPlanner
{
    public static async Task<RegressionAutomationPreviewDto> ResolveEligibleAsync(QaDbContext db, IReadOnlyList<Guid> testCaseIds, CancellationToken ct)
    {
        var ids = testCaseIds.Distinct().ToArray();
        var testCases = await db.TestCases.AsNoTracking().Where(x => ids.Contains(x.TestCaseId))
            .Select(x => new { x.TestCaseId, x.TestCaseCode, x.Title, x.Status, x.AutomationCandidate, x.AutomationTarget }).ToListAsync(ct);
        var automationCases = await db.AutomationCases.AsNoTracking().Where(x => ids.Contains(x.TestCaseId))
            .Select(x => new { x.AutomationCaseId, x.TestCaseId, x.Status, x.IsQuarantined }).ToListAsync(ct);
        var automationByTestCase = automationCases.ToDictionary(x => x.TestCaseId, x => new AutomationCaseSnapshot(x.AutomationCaseId, x.Status, x.IsQuarantined));

        var items = testCases.Select(x =>
        {
            var automationCase = automationByTestCase.GetValueOrDefault(x.TestCaseId);
            var (eligible, skipReason) = RegressionAutomationEligibility.Evaluate(x.Status, x.AutomationCandidate, x.AutomationTarget, automationCase);
            return new RegressionAutomationPreviewItemDto(x.TestCaseId, x.TestCaseCode, x.Title, automationCase?.AutomationCaseId, automationCase?.Status, eligible, skipReason);
        }).OrderBy(x => x.TestCaseCode).ToList();

        var eligibleIds = items.Where(x => x.Eligible && x.AutomationCaseId.HasValue).Select(x => x.AutomationCaseId!.Value).ToArray();
        return new RegressionAutomationPreviewDto(items, eligibleIds, eligibleIds.Length, items.Count);
    }
}
