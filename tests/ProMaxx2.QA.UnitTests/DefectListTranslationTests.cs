using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Infrastructure.Persistence;
using Xunit;

namespace ProMaxx2.QA.UnitTests;

public sealed class DefectListTranslationTests
{
    private static QaDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new QaDbContext(options);
    }

    [Fact]
    public void Defect_list_projection_translates_to_sql()
    {
        var db = CreateDb();
        var q = db.Defects.AsNoTracking().Where(x => !x.IsDeleted);
        var p = 1;
        var s = 20;
        _ = q.OrderByDescending(x => x.CreatedAt).Skip((p - 1) * s).Take(s)
            .Select(x => new { x.DefectId, x.Severity, x.Status, x.CreatedAt, x.UpdatedAt, Name = x.CreatedBy == null ? null : db.Users.Where(u => u.UserId == x.CreatedBy).Select(u => u.DisplayName).FirstOrDefault(), UpdatedName = x.UpdatedBy == null ? null : db.Users.Where(u => u.UserId == x.UpdatedBy).Select(u => u.DisplayName).FirstOrDefault(), ReleaseCode = x.ReleaseId == null ? null : db.Releases.Where(r => r.ReleaseId == x.ReleaseId).Select(r => r.ReleaseCode).FirstOrDefault(), BuildNumber = x.BuildId == null ? null : db.Builds.Where(b => b.BuildId == x.BuildId).Select(b => b.BuildNumber).FirstOrDefault(), AssigneeName = x.AssigneeUserId == null ? null : db.Users.Where(u => u.UserId == x.AssigneeUserId).Select(u => u.DisplayName).FirstOrDefault() }).ToQueryString();
    }
}