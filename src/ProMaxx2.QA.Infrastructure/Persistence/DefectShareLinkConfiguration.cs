using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Defects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class DefectShareLinkConfiguration:IEntityTypeConfiguration<DefectShareLink>
{
    public void Configure(EntityTypeBuilder<DefectShareLink> b)
    {
        b.ToTable("DefectShareLinks");b.HasKey(x=>x.DefectShareLinkId);b.Property(x=>x.DefectShareLinkId).HasDefaultValueSql("NEWSEQUENTIALID()");b.Property(x=>x.Code).HasMaxLength(12).IsRequired();b.HasIndex(x=>x.Code).IsUnique();b.HasIndex(x=>x.DefectId).IsUnique();b.Property(x=>x.CreatedAt).HasPrecision(0);
    }
}
