using ProMaxx2.QA.Domain.Settings;

namespace ProMaxx2.QA.UnitTests;

public sealed class MasterOptionTests
{
    [Fact]
    public void Master_option_requires_category_and_value()
    {
        Assert.Throws<ArgumentException>(() => new MasterOption("", "P1", "P1", 10));
        Assert.Throws<ArgumentException>(() => new MasterOption("TestCasePriority", "", "P1", 10));
    }

    [Fact]
    public void Master_option_can_be_deactivated_without_deletion()
    {
        var option = new MasterOption("TestCasePriority", "P1", "Priority 1", 10);
        option.Update("P1", "Priority 1", 20, false);
        Assert.False(option.IsActive);
        Assert.Equal(20, option.SortOrder);
    }
}
