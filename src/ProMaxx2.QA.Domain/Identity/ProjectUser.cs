using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.Domain.Identity;

public sealed class ProjectUser
{
    private ProjectUser() { }
    public ProjectUser(Guid projectId, Guid userId, Guid? assignedBy)
    {
        ProjectId = projectId;
        UserId = userId;
        AssignedBy = assignedBy;
        AssignedAt = DateTime.UtcNow;
    }

    public Guid ProjectId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime AssignedAt { get; private set; }
    public Guid? AssignedBy { get; private set; }

    public User User { get; private set; } = null!;
    public Project Project { get; private set; } = null!;
}
