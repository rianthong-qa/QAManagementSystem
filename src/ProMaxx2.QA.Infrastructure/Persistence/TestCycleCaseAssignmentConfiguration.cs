using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Execution;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class TestCycleCaseAssignmentConfiguration : IEntityTypeConfiguration<TestCycleCaseAssignment>
{
    public void Configure(EntityTypeBuilder<TestCycleCaseAssignment> b)
    {
        b.ToTable("TestCycleCaseAssignments");
        b.HasKey(x => x.TestCycleCaseAssignmentId);
        b.Property(x => x.Status).HasMaxLength(30).IsRequired();
        b.HasIndex(x => x.TestCycleCaseId).IsUnique();
        b.HasOne<TestCycleCase>().WithMany().HasForeignKey(x => x.TestCycleCaseId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ProMaxx2.QA.Domain.Identity.User>().WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
