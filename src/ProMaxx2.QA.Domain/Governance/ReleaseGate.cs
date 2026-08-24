namespace ProMaxx2.QA.Domain.Governance;

public enum ReleaseDecision { Go, ConditionalGo, NoGo }

public sealed record ReleaseGateInput(int OpenP0, int OpenP1Blockers, bool HasApprovedRisk);

public static class ReleaseGate
{
    public static ReleaseDecision Evaluate(ReleaseGateInput input)
    {
        if (input.OpenP0 > 0) return ReleaseDecision.NoGo;
        if (input.OpenP1Blockers > 0) return input.HasApprovedRisk ? ReleaseDecision.ConditionalGo : ReleaseDecision.NoGo;
        return ReleaseDecision.Go;
    }
}
