namespace ProMaxx2.QA.Application.Common;

public static class BusinessCodeGenerator
{
    public static string Next(string prefix,IEnumerable<string> existingCodes)
    {
        prefix=prefix.Trim().ToUpperInvariant();
        var marker=prefix+"-";
        var next=existingCodes
            .Where(x=>x.StartsWith(marker,StringComparison.OrdinalIgnoreCase))
            .Select(x=>int.TryParse(x[marker.Length..],out var number)?number:0)
            .DefaultIfEmpty(0)
            .Max()+1;
        return $"{prefix}-{next:000}";
    }
}
