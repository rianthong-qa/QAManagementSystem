using ProMaxx2.QA.Application.Common;

namespace ProMaxx2.QA.UnitTests;

public sealed class BusinessCodeGeneratorTests
{
    [Fact]
    public async Task Next_available_skips_hidden_or_inactive_codes()
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PMX2-MOD-001", "PMX2-MOD-002" };
        var result = await BusinessCodeGenerator.NextAvailableAsync("PMX2-MOD", code => Task.FromResult(reserved.Contains(code)));
        Assert.Equal("PMX2-MOD-003", result);
    }

    [Fact]
    public void Next_available_skips_existing_codes_sync()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PMX2-MOD-001", "PMX2-MOD-002" };
        var result = BusinessCodeGenerator.NextAvailable("PMX2-MOD", existing);
        Assert.Equal("PMX2-MOD-003", result);
    }

    [Fact]
    public void Next_available_returns_001_when_no_existing()
    {
        var result = BusinessCodeGenerator.NextAvailable("PMX2-MOD", []);
        Assert.Equal("PMX2-MOD-001", result);
    }

    [Fact]
    public void Next_available_normalizes_prefix()
    {
        var result = BusinessCodeGenerator.NextAvailable(" pmx2-mod ", []);
        Assert.Equal("PMX2-MOD-001", result);
    }

    [Fact]
    public void Contextual_prefix_does_not_duplicate_project_code()
    {
        Assert.Equal("PMX2-MOD-001-TC", BusinessCodeGenerator.ContextualPrefix("PMX2", "PMX2-MOD-001", "TC"));
    }

    [Fact]
    public void Next_uses_highest_matching_sequence()
    {
        var code=BusinessCodeGenerator.Next("PMX2-SALE-TC",["PMX2-SALE-TC-001","OTHER-999","PMX2-SALE-TC-004"]);
        Assert.Equal("PMX2-SALE-TC-005",code);
    }

    [Fact]
    public void Next_starts_at_one_and_normalizes_prefix()
    {
        Assert.Equal("PRJ-001",BusinessCodeGenerator.Next(" prj ",[]));
    }
}
