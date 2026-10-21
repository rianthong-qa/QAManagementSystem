using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProMaxx2.QA.Domain.Automation;

namespace ProMaxx2.QA.Infrastructure.Persistence;
public sealed class AutomationCaptureSessionConfiguration : IEntityTypeConfiguration<AutomationCaptureSession>
{
    public void Configure(EntityTypeBuilder<AutomationCaptureSession> b)
    {
        b.HasKey(x => x.CaptureSessionId); b.Property(x => x.ApplicationCode).HasMaxLength(32).IsRequired(); b.Property(x => x.SourceMachine).HasMaxLength(256).IsRequired(); b.Property(x => x.Status).HasMaxLength(24).IsRequired(); b.Property(x => x.ItemsJson).IsRequired(); b.HasIndex(x => new { x.UserId, x.Status, x.ExpiresAt });
    }
}
