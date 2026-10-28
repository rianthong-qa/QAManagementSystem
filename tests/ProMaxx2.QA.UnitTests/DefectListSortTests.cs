using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Infrastructure.Persistence;
using Xunit;

namespace ProMaxx2.QA.UnitTests;

/// <summary>หน้า Defect เรียงด้วยหัวคอลัมน์ฝั่ง server (sortBy) — ทุกแบบต้องแปลงเป็น SQL ได้ และค่าไม่รู้จักใช้ลำดับเดิม (สร้างล่าสุดก่อน)</summary>
public sealed class DefectListSortTests
{
    private static QaDbContext SqlServerDb() => new(new DbContextOptionsBuilder<QaDbContext>()
        .UseSqlServer(@"Server=(localdb)\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True").Options);

    [Theory]
    [InlineData("code_asc", "[DefectCode]")]
    [InlineData("title_desc", "[Title]")]
    [InlineData("severity_asc", "N'Critical'")]
    [InlineData("status_asc", "N'In Progress'")]
    [InlineData("createdAt_asc", "[CreatedAt]")]
    public void Every_sort_translates_to_sql(string sortBy, string expected)
    {
        using var db = SqlServerDb();
        var sql = DefectListSort.Apply(db.Defects.AsNoTracking(), sortBy).Skip(0).Take(30).ToQueryString();
        Assert.Contains("ORDER BY", sql);
        Assert.Contains(expected, sql);
    }

    [Theory]
    [InlineData("severity", new[] { "Critical", "High", "Medium", "Low" })]
    [InlineData("severity_asc", new[] { "Critical", "High", "Medium", "Low" })]
    [InlineData("severity_desc", new[] { "Low", "Medium", "High", "Critical" })]
    public void Sort_without_direction_uses_natural_order(string sortBy, string[] expected)
    {
        var project = Guid.NewGuid();
        var defects = new[] { "Low", "Critical", "Medium", "High" }
            .Select((severity, i) => new ProMaxx2.QA.Domain.Defects.Defect(project, null, null, null, $"D-{i}", $"t{i}", severity, "Open", null, null, null, null, null, null))
            .AsQueryable();
        Assert.Equal(expected, DefectListSort.Apply(defects, sortBy).Select(x => x.Severity).ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown_asc")]
    public void Unknown_sort_falls_back_to_newest_first(string? sortBy)
    {
        using var db = SqlServerDb();
        var sql = DefectListSort.Apply(db.Defects.AsNoTracking(), sortBy).ToQueryString();
        Assert.Matches(@"ORDER BY \[\w+\]\.\[CreatedAt\] DESC", sql);
    }
}
