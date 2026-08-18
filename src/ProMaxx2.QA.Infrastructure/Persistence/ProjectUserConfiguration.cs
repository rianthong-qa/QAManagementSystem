using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Identity;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class ProjectUserConfiguration : IEntityTypeConfiguration<ProjectUser>
{
    public void Configure(EntityTypeBuilder<ProjectUser> b)
    {
        b.ToTable("ProjectUsers");
        b.HasKey(x => new { x.ProjectId, x.UserId });
        b.Property(x => x.AssignedAt).HasPrecision(0);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}
