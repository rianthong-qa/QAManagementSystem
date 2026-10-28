namespace ProMaxx2.QA.Application.Regression;

public sealed record AutomationCaseSnapshot(Guid AutomationCaseId, string Status, bool IsQuarantined);

public static class RegressionAutomationEligibility
{
    public static (bool Eligible, string? SkipReason) Evaluate(string testCaseStatus, bool automationCandidate, string? automationTarget, AutomationCaseSnapshot? automationCase)
    {
        if (testCaseStatus != "Ready") return (false, "Test Case ยังไม่ Ready");
        if (!automationCandidate || string.IsNullOrWhiteSpace(automationTarget)) return (false, "ไม่ได้ตั้งเป็น Automation Candidate");
        if (automationCase is null) return (false, "ยังไม่มี Automation Case ผูกไว้");
        if (automationCase.IsQuarantined) return (false, "Automation Case ถูก Quarantine");
        if (automationCase.Status != "Ready") return (false, $"Automation Case สถานะ {automationCase.Status}");
        return (true, null);
    }
}
