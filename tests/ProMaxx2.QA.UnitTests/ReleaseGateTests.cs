using ProMaxx2.QA.Domain.Governance;

namespace ProMaxx2.QA.UnitTests;

public sealed class ReleaseGateTests
{
    [Fact]
    public void P0_always_blocks_release() => Assert.Equal(ReleaseDecision.NoGo, ReleaseGate.Evaluate(new(1, 0, false, true)));

    [Fact]
    public void P1_with_approved_risk_is_conditional_go() => Assert.Equal(ReleaseDecision.ConditionalGo, ReleaseGate.Evaluate(new(0, 2, true, true)));

    [Fact]
    public void Clear_gate_is_go() => Assert.Equal(ReleaseDecision.Go, ReleaseGate.Evaluate(new(0, 0, false, true)));
}
