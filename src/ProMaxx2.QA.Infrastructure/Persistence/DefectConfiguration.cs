using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Releases;

namespace ProMaxx2.QA.Infrastructure.Persistence;
public sealed class DefectConfiguration:IEntityTypeConfiguration<Defect>{public void Configure(EntityTypeBuilder<Defect>b){b.ToTable("Defects");b.HasKey(x=>x.DefectId);b.Property(x=>x.DefectId).HasDefaultValueSql("NEWSEQUENTIALID()");b.Property(x=>x.DefectCode).HasMaxLength(100).IsRequired();b.HasIndex(x=>new{x.ProjectId,x.DefectCode}).IsUnique();b.Property(x=>x.Title).HasMaxLength(300).IsRequired();b.Property(x=>x.Severity).HasMaxLength(20).IsRequired();b.Property(x=>x.Status).HasMaxLength(30).IsRequired();b.Property(x=>x.CreatedAt).HasPrecision(0);b.HasOne<Project>().WithMany().HasForeignKey(x=>x.ProjectId).OnDelete(DeleteBehavior.Restrict);b.HasOne<Release>().WithMany().HasForeignKey(x=>x.ReleaseId).OnDelete(DeleteBehavior.Restrict);b.HasOne<Build>().WithMany().HasForeignKey(x=>x.BuildId).OnDelete(DeleteBehavior.Restrict);b.HasOne<ProductModule>().WithMany().HasForeignKey(x=>x.ModuleId).OnDelete(DeleteBehavior.Restrict);}}
