namespace ProMaxx2.QA.Application.Common;

public sealed class ProjectAccessContext
{
 public Guid CurrentUserId { get; set; }
 public Guid[] AllowedProjectIds { get; set; } = [];
}
