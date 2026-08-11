namespace ProMaxx2.QA.Application.Common;

public static class BusinessCodeGenerator
{
    public static async Task<string> NextAvailableAsync(string prefix,Func<string,Task<bool>> exists)
    {
        var normalized=prefix.Trim().ToUpperInvariant();
        for(var number=1;number<=999999;number++)
        {
            var candidate=$"{normalized}-{number:000}";
            if(!await exists(candidate))return candidate;
        }
        throw new InvalidOperationException($"No available code remains for {normalized}.");
    }

    public static string ContextualPrefix(string projectCode,string moduleCode,string kind)
    {
        var project=projectCode.Trim().ToUpperInvariant();
        var module=moduleCode.Trim().ToUpperInvariant();
        var moduleSegment=module.StartsWith(project+"-",StringComparison.OrdinalIgnoreCase)
            ? module[(project.Length+1)..]
            : module;
        return $"{project}-{moduleSegment}-{kind.Trim().ToUpperInvariant()}";
    }

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
