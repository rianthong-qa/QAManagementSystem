using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Infrastructure.Persistence;
using Xunit;

namespace ProMaxx2.QA.UnitTests;

/// <summary>หน้า Test Cycle เรียงด้วยหัวคอลัมน์ฝั่ง server (sortBy) — ทุกแบบต้องแปลงเป็น SQL ได้ และค่าไม่รู้จักใช้ลำดับเดิม (สร้างล่าสุดก่อน)</summary>
public sealed class TestCycleListSortTests
{
    private static QaDbContext SqlServerDb() => new(new DbContextOptionsBuilder<QaDbContext>()
        .UseSqlServer(@"Server=(localdb)\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True").Options);

    [Theory]
    [InlineData("code_asc", "[CycleCode]")]
    [InlineData("name_desc", "[CycleName]")]
    [InlineData("progress_desc", "[CurrentStatus]")]
    [InlineData("cases_asc", "COUNT(*)")]
    [InlineData("status_asc", "N'InProgress'")]
    [InlineData("startDate_desc", "[StartDate]")]
    [InlineData("createdAt_asc", "[CreatedAt]")]
    public void Every_sort_translates_to_sql(string sortBy, string expected)
    {
        using var db = SqlServerDb();
        var sql = TestCycleRepository.Sort(db.TestCycles.AsNoTracking(), sortBy).Skip(0).Take(30).ToQueryString();
        Assert.Contains("ORDER BY", sql);
        Assert.Contains(expected, sql);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown_asc")]
    public void Unknown_sort_falls_back_to_newest_first(string? sortBy)
    {
        using var db = SqlServerDb();
        var sql = TestCycleRepository.Sort(db.TestCycles.AsNoTracking(), sortBy).ToQueryString();
        var orderBy = sql[sql.IndexOf("ORDER BY", StringComparison.Ordinal)..];
        Assert.StartsWith("ORDER BY [t].[CreatedAt] DESC", orderBy);
    }
}
