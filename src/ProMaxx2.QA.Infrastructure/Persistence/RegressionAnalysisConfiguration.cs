using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Execution;

namespace ProMaxx2.QA.Infrastructure.Persistence;
public sealed class RegressionAnalysisConfiguration:IEntityTypeConfiguration<RegressionAnalysis>{public void Configure(EntityTypeBuilder<RegressionAnalysis>b){b.ToTable("RegressionAnalyses");b.HasKey(x=>x.RegressionAnalysisId);b.Property(x=>x.RegressionAnalysisId).HasDefaultValueSql("NEWSEQUENTIALID()");b.Property(x=>x.MinimumPriority).HasMaxLength(10).IsRequired();b.Property(x=>x.ChangeNotes).HasMaxLength(2000);b.Property(x=>x.AnalyzedAt).HasPrecision(0);b.HasIndex(x=>new{x.ReleaseId,x.AnalyzedAt});}}
