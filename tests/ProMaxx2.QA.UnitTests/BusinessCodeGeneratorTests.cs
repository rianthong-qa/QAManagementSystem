using ProMaxx2.QA.Application.Common;

namespace ProMaxx2.QA.UnitTests;

public sealed class BusinessCodeGeneratorTests
{
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
