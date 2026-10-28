using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// Build ที่เกี่ยวกับวงจร Defect (DEFECT_BUILD_LIFECYCLE.md) — ลำดับ Build ใน Release ใช้ BuildDate (ไม่มี = วันที่ลงทะเบียน)
/// ใช้กำหนด Fix Build เริ่มต้น (Build ล่าสุดของ Release) และตัดสินว่า Build ที่ Fail ใหม่ "อยู่หลัง" Fix Build หรือไม่
/// </summary>
public static class DefectBuilds
{
    /// <summary>Build ล่าสุดที่ยังใช้งานของ Release — null เมื่อไม่มี Release หรือยังไม่ลงทะเบียน Build</summary>
    public static Task<Guid?> LatestActiveBuildIdAsync(QaDbContext db, Guid? releaseId, CancellationToken ct) =>
        releaseId is null ? Task.FromResult<Guid?>(null)
            : db.Builds.AsNoTracking().Where(b => b.ReleaseId == releaseId && b.IsActive)
                .OrderByDescending(b => b.BuildDate ?? b.CreatedAt).ThenByDescending(b => b.CreatedAt)
                .Select(b => (Guid?)b.BuildId).FirstOrDefaultAsync(ct);

    /// <summary>Build ทุกตัวใน Release ของ build นี้ เรียงเก่า → ใหม่ (ลำดับเดียวกับ LatestActiveBuildIdAsync) — null เมื่อไม่พบ Build</summary>
    public static async Task<ReleaseBuildOrder?> ReleaseOrderAsync(QaDbContext db, Guid buildId, CancellationToken ct)
    {
        var build = await db.Builds.AsNoTracking().Where(b => b.BuildId == buildId)
            .Join(db.Releases.AsNoTracking(), b => b.ReleaseId, r => r.ReleaseId, (b, r) => new { b.BuildId, b.BuildNumber, b.ReleaseId, r.ProjectId, r.ReleaseCode })
            .SingleOrDefaultAsync(ct);
        if (build is null) return null;
        var ordered = await db.Builds.AsNoTracking().Where(b => b.ReleaseId == build.ReleaseId)
            .OrderBy(b => b.BuildDate ?? b.CreatedAt).ThenBy(b => b.CreatedAt).Select(b => b.BuildId).ToListAsync(ct);
        var index = ordered.IndexOf(buildId);
        return new(build.BuildId, build.BuildNumber, build.ReleaseId, build.ReleaseCode, build.ProjectId, ordered.Take(index).ToList(), ordered.Take(index + 1).ToList());
    }

    /// <summary>คิวตรวจซ้ำของ Build: Resolved ที่ Fix Build คือ Build นี้หรือก่อนหน้า หรือไม่ระบุ — ส่วน "Defect ของ Build" และ Retest Cycle ใช้กฎนี้ที่เดียว</summary>
    public static IQueryable<Defect> RetestQueue(IQueryable<Defect> releaseDefects, ReleaseBuildOrder order)
    {
        var upTo = order.UpTo;
        return releaseDefects.Where(d => DefectStatusGroups.AwaitingVerification.Contains(d.Status) && (!d.FixedInBuildId.HasValue || upTo.Contains(d.FixedInBuildId.Value)));
    }

    /// <summary>Build อยู่ใน Project เดียวกับ Defect — กันการเลือก Fix/Verified Build ข้าม Project</summary>
    public static Task<bool> BelongsToProjectAsync(QaDbContext db, Guid buildId, Guid projectId, CancellationToken ct) =>
        db.Builds.AsNoTracking().Where(b => b.BuildId == buildId)
            .Join(db.Releases.AsNoTracking(), b => b.ReleaseId, r => r.ReleaseId, (b, r) => r.ProjectId)
            .AnyAsync(p => p == projectId, ct);

    /// <summary>build อยู่ตรงหรือหลัง reference หรือไม่ (เทียบ BuildDate แล้วตามด้วยวันที่ลงทะเบียน) — reference ไม่ระบุ = ถือว่าใช่</summary>
    public static async Task<bool> IsSameOrLaterAsync(QaDbContext db, Guid? buildId, Guid? referenceBuildId, CancellationToken ct)
    {
        if (referenceBuildId is null || buildId == referenceBuildId) return true;
        if (buildId is null) return false;
        var keys = await db.Builds.AsNoTracking().Where(b => b.BuildId == buildId || b.BuildId == referenceBuildId)
            .Select(b => new { b.BuildId, Order = b.BuildDate ?? b.CreatedAt, b.CreatedAt }).ToListAsync(ct);
        var build = keys.SingleOrDefault(x => x.BuildId == buildId);
        var reference = keys.SingleOrDefault(x => x.BuildId == referenceBuildId);
        if (build is null || reference is null) return true;
        return build.Order != reference.Order ? build.Order > reference.Order : build.CreatedAt >= reference.CreatedAt;
    }
}

/// <summary>Earlier = Build ก่อนหน้าใน Release (ไม่รวมตัวเอง), UpTo = Build ก่อนหน้า + ตัวเอง</summary>
public sealed record ReleaseBuildOrder(Guid BuildId, string BuildNumber, Guid ReleaseId, string ReleaseCode, Guid ProjectId, IReadOnlyList<Guid> Earlier, IReadOnlyList<Guid> UpTo);
