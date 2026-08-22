namespace ProMaxx2.QA.Domain.Governance;

public sealed class ReleaseSignoff
{
    private ReleaseSignoff() { }
    public ReleaseSignoff(Guid releaseId, Guid buildId, string signoffType, string decision, string? comment, Guid? signoffBy)
    {
        if (releaseId == Guid.Empty || buildId == Guid.Empty) throw new ArgumentException("Release and build are required.");
        ReleaseSignoffId = Guid.NewGuid();
        ReleaseId = releaseId;
        BuildId = buildId;
        SignoffType = NormalizeType(signoffType);
        Decision = NormalizeDecision(decision);
        Comment = comment?.Trim();
        SignoffByUserId = signoffBy;
        CreatedAt = DateTime.UtcNow;
    }
    public Guid ReleaseSignoffId { get; private set; }
    public Guid ReleaseId { get; private set; }
    public Guid BuildId { get; private set; }
    public string SignoffType { get; private set; } = "QA";
    public string Decision { get; private set; } = "GO";
    public string? Comment { get; private set; }
    public Guid? SignoffByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static string NormalizeDecision(string value) => new[] { "GO", "CONDITIONAL_GO", "NO_GO" }.SingleOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Invalid signoff decision.");
    private static string NormalizeType(string value) => new[] { "QA", "RELEASE_OWNER", "PRODUCT_OWNER", "DEVELOPMENT" }.SingleOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Invalid signoff type.");
}
