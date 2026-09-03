using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.UnitTests;

public sealed class AutomationCaptureSessionTests
{
    private static AutomationCaptureSession NewSession() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "pos", "QA-PC", "1.0", "[]");

    [Fact]
    public void New_session_is_draft_and_expires_in_future()
    {
        var session = NewSession();
        Assert.Equal("Draft", session.Status);
        Assert.True(session.ExpiresAt > DateTime.UtcNow);
        Assert.Null(session.CompletedAt);
    }

    [Fact]
    public void Session_can_be_committed_once()
    {
        var session = NewSession();
        session.Complete("Committed");
        Assert.Equal("Committed", session.Status);
        Assert.NotNull(session.CompletedAt);
        Assert.Throws<InvalidOperationException>(() => session.Complete("Committed"));
    }

    [Fact]
    public void Session_can_be_discarded_but_rejects_invalid_status()
    {
        var session = NewSession();
        Assert.Throws<ArgumentException>(() => session.Complete("Ready"));
        session.Complete("Discarded");
        Assert.Equal("Discarded", session.Status);
    }
}
