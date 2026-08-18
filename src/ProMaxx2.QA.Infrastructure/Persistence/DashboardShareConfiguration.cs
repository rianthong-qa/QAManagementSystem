using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Dashboard;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class DashboardShareConfiguration:IEntityTypeConfiguration<DashboardShare>
{
    public void Configure(EntityTypeBuilder<DashboardShare> b)
    {
        b.ToTable("DashboardShares");b.HasKey(x=>x.DashboardShareId);b.Property(x=>x.DashboardShareId).HasDefaultValueSql("NEWSEQUENTIALID()");b.Property(x=>x.Code).HasMaxLength(12).IsRequired();b.HasIndex(x=>x.Code).IsUnique();b.Property(x=>x.CreatedAt).HasPrecision(0);b.Property(x=>x.ExpiresAt).HasPrecision(0);
    }
}
