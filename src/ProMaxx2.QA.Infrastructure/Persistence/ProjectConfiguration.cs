using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class ProjectConfiguration:IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b){b.ToTable("Projects");b.HasKey(x=>x.ProjectId);b.Property(x=>x.ProjectId).HasDefaultValueSql("NEWSEQUENTIALID()");b.Property(x=>x.ProjectCode).HasMaxLength(50).IsRequired();b.HasIndex(x=>x.ProjectCode).IsUnique();b.Property(x=>x.ProjectName).HasMaxLength(200).IsRequired();b.Property(x=>x.Status).HasMaxLength(30).IsRequired();b.Property(x=>x.CreatedAt).HasPrecision(0);b.Property(x=>x.UpdatedAt).HasPrecision(0);b.HasOne<User>().WithMany().HasForeignKey(x=>x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);}
}
public sealed class ProductModuleConfiguration:IEntityTypeConfiguration<ProductModule>
{
    public void Configure(EntityTypeBuilder<ProductModule> b){b.ToTable("Modules");b.HasKey(x=>x.ModuleId);b.Property(x=>x.ModuleId).HasDefaultValueSql("NEWSEQUENTIALID()");b.Property(x=>x.ModuleCode).HasMaxLength(50).IsRequired();b.HasIndex(x=>new{x.ProjectId,x.ModuleCode}).IsUnique();b.Property(x=>x.ModuleName).HasMaxLength(200).IsRequired();b.Property(x=>x.CreatedAt).HasPrecision(0);b.Property(x=>x.UpdatedAt).HasPrecision(0);b.HasOne(x=>x.Project).WithMany(x=>x.Modules).HasForeignKey(x=>x.ProjectId).OnDelete(DeleteBehavior.Restrict);b.HasOne(x=>x.ParentModule).WithMany().HasForeignKey(x=>x.ParentModuleId).OnDelete(DeleteBehavior.Restrict);b.HasOne<User>().WithMany().HasForeignKey(x=>x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);}
}
