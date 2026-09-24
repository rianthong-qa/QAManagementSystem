using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Infrastructure.Persistence;
using Xunit;

namespace ProMaxx2.QA.UnitTests;

/// <summary>รอบที่ 5: projection ร่วม <see cref="ExecutionRow.Projection"/> ต้องแปลงเป็น SQL ของ SQL Server ได้ (InMemory ไม่ตรวจเรื่องนี้)</summary>
public sealed class AutomationExecutionRowTranslationTests
{
    [Fact]
    public void Execution_row_projection_translates_to_sql()
    {
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer(@"Server=(localdb)\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True").Options);
        var sql = db.AutomationExecutions.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(20).Select(ExecutionRow.Projection).ToQueryString();
        Assert.Contains("[AutomationCode]", sql);
        Assert.Contains("[EnvironmentName]", sql);
    }
}
