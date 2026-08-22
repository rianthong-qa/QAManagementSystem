namespace ProMaxx2.QA.Domain.Governance;

public sealed class RiskAcceptance
{
    private RiskAcceptance() { }
    public RiskAcceptance(Guid projectId, Guid releaseId, Guid? defectId, string title, string issue, string impact, string probability, string? workaround, string? targetFix, string? qaRecommendation, Guid? ownerUserId, Guid? createdBy)
    {
        if (projectId == Guid.Empty || releaseId == Guid.Empty || string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Project, release and title are required.");
        RiskAcceptanceId = Guid.NewGuid();
        ProjectId = projectId;
        ReleaseId = releaseId;
        DefectId = defectId;
        RiskCode = "";
        Title = title.Trim();
        Issue = issue?.Trim() ?? "";
        Impact = NormalizeImpact(impact);
        Probability = NormalizeProbability(probability);
        RiskLevel = ComputeRiskLevel(Impact, Probability);
        Workaround = workaround?.Trim();
        TargetFix = targetFix?.Trim();
        QaRecommendation = qaRecommendation?.Trim();
        OwnerUserId = ownerUserId;
        Status = "Draft";
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
    }
    public Guid RiskAcceptanceId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid ReleaseId { get; private set; }
    public Guid? DefectId { get; private set; }
    public string RiskCode { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Issue { get; private set; } = string.Empty;
    public string Impact { get; private set; } = "Medium";
    public string Probability { get; private set; } = "Medium";
    public string RiskLevel { get; private set; } = "Medium";
    public string Status { get; private set; } = "Draft";
    public string? Workaround { get; private set; }
    public string? TargetFix { get; private set; }
    public string? QaRecommendation { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTime? ReviewDate { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public string? ReviewComment { get; private set; }
    public bool IsDeleted { get; private set; }
    public void AssignCode(string code) { RiskCode = code.Trim().ToUpperInvariant(); }

    public void Update(string title, string issue, string impact, string probability, string? workaround, string? targetFix, string? qaRecommendation, Guid? ownerUserId, Guid? updatedBy)
    {
        if (Status is not ("Draft" or "Rejected")) throw new InvalidOperationException("Only Draft or Rejected risks can be edited.");
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.");
        Title = title.Trim(); Issue = issue?.Trim() ?? ""; Impact = NormalizeImpact(impact); Probability = NormalizeProbability(probability); RiskLevel = ComputeRiskLevel(Impact, Probability); Workaround = workaround?.Trim(); TargetFix = targetFix?.Trim(); QaRecommendation = qaRecommendation?.Trim(); OwnerUserId = ownerUserId; UpdatedAt = DateTime.UtcNow; UpdatedBy = updatedBy;
    }

    public void Submit(Guid? userId) { if (Status != "Draft") throw new InvalidOperationException("Only Draft risks can be submitted."); Status = "Submitted"; UpdatedAt = DateTime.UtcNow; UpdatedBy = userId; }
    public void Approve(string? comment, Guid? userId) { if (Status != "Submitted") throw new InvalidOperationException("Only Submitted risks can be approved."); Status = "Approved"; ReviewDate = DateTime.UtcNow; ReviewedBy = userId; ReviewComment = comment?.Trim(); UpdatedAt = DateTime.UtcNow; UpdatedBy = userId; }
    public void Reject(string? comment, Guid? userId) { if (Status != "Submitted") throw new InvalidOperationException("Only Submitted risks can be rejected."); Status = "Rejected"; ReviewDate = DateTime.UtcNow; ReviewedBy = userId; ReviewComment = comment?.Trim(); UpdatedAt = DateTime.UtcNow; UpdatedBy = userId; }
    public void Close(Guid? userId) { if (Status is not ("Approved" or "Rejected")) throw new InvalidOperationException("Only Approved or Rejected risks can be closed."); Status = "Closed"; UpdatedAt = DateTime.UtcNow; UpdatedBy = userId; }
    public void SoftDelete(Guid? userId) { IsDeleted = true; UpdatedAt = DateTime.UtcNow; UpdatedBy = userId; }

    private static string NormalizeImpact(string value) => new[] { "High", "Medium", "Low" }.SingleOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Invalid impact.");
    private static string NormalizeProbability(string value) => new[] { "High", "Medium", "Low" }.SingleOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Invalid probability.");
    private static string ComputeRiskLevel(string impact, string probability)
    {
        if (impact == "High" && probability == "High") return "High";
        if (impact == "Low" && probability == "Low") return "Low";
        if (impact == "Low" || probability == "Low") return "Medium";
        return "High";
    }
}
