using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

public sealed class TestCaseQueryTranslationTests
{
    [Fact]
    public void Test_case_requirements_query_translates_to_sql()
    {
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

        var sql = TestCaseRepository.RequirementsQuery(db, Guid.NewGuid()).ToQueryString();

        Assert.Contains("ORDER BY", sql);
    }
}
