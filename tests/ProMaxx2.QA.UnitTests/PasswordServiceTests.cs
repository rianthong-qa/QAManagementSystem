using ProMaxx2.QA.Infrastructure.Identity;

namespace ProMaxx2.QA.UnitTests;

public sealed class PasswordServiceTests
{
    private readonly PasswordService service = new();
    [Fact] public void Hash_and_verify_round_trip() { var hash=service.Hash("Correct-Horse-42"); Assert.True(service.Verify("Correct-Horse-42",hash)); }
    [Fact] public void Wrong_password_is_rejected() { var hash=service.Hash("Correct-Horse-42"); Assert.False(service.Verify("wrong",hash)); }
    [Fact] public void Invalid_hash_is_rejected() => Assert.False(service.Verify("anything","invalid"));
}
