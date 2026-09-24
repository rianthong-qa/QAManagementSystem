using Xunit;

namespace ProMaxx2.Automation.Core.Tests;

/// <summary>AUT-AGT-002: key string แบบ SendKeys ต้องถูกแปลงเป็นปุ่มจริง ไม่ใช่ถูกพิมพ์ออกไปเป็นตัวอักษร "{Enter}"</summary>
public sealed class KeySequenceTests
{
    [Theory]
    [InlineData("{Enter}", "ENTER")]
    [InlineData("{F8}", "F8")]
    [InlineData("{esc}", "ESC")]
    [InlineData("{Escape}", "ESC")]
    [InlineData("{DEL}", "DELETE")]
    public void Special_keys_become_key_strokes(string input, string expectedKey)
    {
        var stroke = Assert.Single(KeySequence.Parse(input));
        Assert.Null(stroke.Text);
        Assert.Equal(expectedKey, stroke.Key);
        Assert.False(stroke.HasModifier);
    }

    [Fact]
    public void Text_and_special_keys_are_split_in_order()
    {
        var strokes = KeySequence.Parse("P001{ENTER}2{TAB}");

        Assert.Equal(["P001", null, "2", null], strokes.Select(s => s.Text));
        Assert.Equal([null, "ENTER", null, "TAB"], strokes.Select(s => s.Key));
    }

    [Fact]
    public void Modifiers_apply_to_the_next_character_or_key_only()
    {
        var strokes = KeySequence.Parse("%Fx^s+{TAB}");

        Assert.Equal(4, strokes.Count);
        Assert.True(strokes[0].Alt); Assert.Equal("F", strokes[0].Text);
        Assert.False(strokes[1].HasModifier); Assert.Equal("x", strokes[1].Text);
        Assert.True(strokes[2].Ctrl); Assert.Equal("s", strokes[2].Text);
        Assert.True(strokes[3].Shift); Assert.Equal("TAB", strokes[3].Key);
    }

    [Fact]
    public void Escaped_braces_are_literal_text()
        => Assert.Equal("{a}", Assert.Single(KeySequence.Parse("{{}a{}}")).Text);

    [Theory]
    [InlineData("{NOPE}")]
    [InlineData("{ENTER")]
    [InlineData("abc^")]
    public void Invalid_sequences_are_reported_as_dsl_errors(string input)
    {
        var ex = Assert.Throws<ArgumentException>(() => KeySequence.Parse(input));
        Assert.Contains("AUT-DSL-002", ex.Message);
    }
}
