using ProMaxx2.QA.Application.Regression;

namespace ProMaxx2.QA.UnitTests;

public sealed class RegressionAutomationEligibilityTests
{
    private static readonly AutomationCaseSnapshot ReadyCase = new(Guid.NewGuid(), "Ready", false);

    [Fact]
    public void Ready_candidate_with_ready_automation_case_is_eligible()
    {
        var (eligible, reason) = RegressionAutomationEligibility.Evaluate("Ready", true, "pos", ReadyCase);
        Assert.True(eligible);
        Assert.Null(reason);
    }

    [Fact]
    public void TestCase_not_ready_is_not_eligible()
    {
        var (eligible, reason) = RegressionAutomationEligibility.Evaluate("Review", true, "pos", ReadyCase);
        Assert.False(eligible);
        Assert.Equal("Test Case ยังไม่ Ready", reason);
    }

    [Fact]
    public void Not_automation_candidate_is_not_eligible()
    {
        var (eligible, reason) = RegressionAutomationEligibility.Evaluate("Ready", false, null, ReadyCase);
        Assert.False(eligible);
        Assert.Equal("ไม่ได้ตั้งเป็น Automation Candidate", reason);
    }

    [Fact]
    public void Candidate_without_target_is_not_eligible()
    {
        var (eligible, reason) = RegressionAutomationEligibility.Evaluate("Ready", true, null, ReadyCase);
        Assert.False(eligible);
        Assert.Equal("ไม่ได้ตั้งเป็น Automation Candidate", reason);
    }

    [Fact]
    public void Missing_automation_case_is_not_eligible()
    {
        var (eligible, reason) = RegressionAutomationEligibility.Evaluate("Ready", true, "pos", null);
        Assert.False(eligible);
        Assert.Equal("ยังไม่มี Automation Case ผูกไว้", reason);
    }

    [Fact]
    public void Quarantined_automation_case_is_not_eligible()
    {
        var quarantined = new AutomationCaseSnapshot(Guid.NewGuid(), "Ready", true);
        var (eligible, reason) = RegressionAutomationEligibility.Evaluate("Ready", true, "pos", quarantined);
        Assert.False(eligible);
        Assert.Equal("Automation Case ถูก Quarantine", reason);
    }

    [Fact]
    public void Automation_case_not_ready_is_not_eligible()
    {
        var draft = new AutomationCaseSnapshot(Guid.NewGuid(), "Draft", false);
        var (eligible, reason) = RegressionAutomationEligibility.Evaluate("Ready", true, "pos", draft);
        Assert.False(eligible);
        Assert.Equal("Automation Case สถานะ Draft", reason);
    }
}
