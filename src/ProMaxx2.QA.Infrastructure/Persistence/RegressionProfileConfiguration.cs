using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Execution;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class RegressionProfileConfiguration:IEntityTypeConfiguration<RegressionProfile>
{public void Configure(EntityTypeBuilder<RegressionProfile>b){b.ToTable("RegressionProfiles");b.HasKey(x=>x.RegressionProfileId);b.Property(x=>x.Name).HasMaxLength(160).IsRequired();b.Property(x=>x.Visibility).HasMaxLength(20).IsRequired();b.Property(x=>x.SettingsJson).HasColumnType("nvarchar(max)").IsRequired();b.HasIndex(x=>new{x.ProjectId,x.IsActive});}}
public sealed class RegressionScheduleConfiguration:IEntityTypeConfiguration<RegressionSchedule>
{public void Configure(EntityTypeBuilder<RegressionSchedule>b){b.ToTable("RegressionSchedules");b.HasKey(x=>x.RegressionScheduleId);b.Property(x=>x.Name).HasMaxLength(160).IsRequired();b.HasIndex(x=>new{x.ProjectId,x.IsActive});b.HasIndex(x=>x.ReleaseId);}}
