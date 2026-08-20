namespace ProMaxx2.QA.Api.Services;

public static class AiJsonParser
{
    public static string Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var value = text.Trim();
        if (value.StartsWith("```"))
        {
            var lineBreak = value.IndexOf('\n');
            if (lineBreak >= 0) value = value[(lineBreak + 1)..];
            if (value.EndsWith("```")) value = value[..^3];
            value = value.Trim();
        }
        var first = value.IndexOfAny(new[] { '{', '[' });
        var last = Math.Max(value.LastIndexOf('}'), value.LastIndexOf(']'));
        return first >= 0 && last > first ? value[first..(last + 1)] : value;
    }
}