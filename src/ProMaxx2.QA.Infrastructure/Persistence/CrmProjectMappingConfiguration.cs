using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Integrations;
using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class CrmProjectMappingConfiguration : IEntityTypeConfiguration<CrmProjectMapping>
{
    public void Configure(EntityTypeBuilder<CrmProjectMapping> b)
    {
        b.ToTable("CrmProjectMappings");
        b.HasKey(x => x.CrmProjectMappingId);
        b.Property(x => x.CrmProjectMappingId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.CrmProductId).HasMaxLength(50).IsRequired();
        b.Property(x => x.CrmVersionId).HasMaxLength(50);
        b.HasIndex(x => x.ProjectId).IsUnique();
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}
