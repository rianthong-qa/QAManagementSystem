using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Performance;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Performance;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed record PerformanceShareLinkDto(
    Guid ShareLinkId, string Url, DateOnly From, DateOnly To, bool IncludeEvaluations, bool IncludeWorkLogs, string? Note,
    DateTime ExpiresAt, DateTime CreatedAt, string? CreatedByName, DateTime? RevokedAt, string? RevokedByName, int ViewCount, DateTime? LastViewedAt, string Status);
public sealed record CreatePerformanceShareLinkRequest(DateOnly? From, DateOnly? To, int ExpiresInDays, bool IncludeEvaluations, bool IncludeWorkLogs, string? Note);
public sealed record SharedPerformanceReportDto(EmployeeReportDto Report, DateTime ExpiresAt, DateTime SharedAt, string? SharedByName, string? Note);

/// <summary>ลิงก์หมดอายุ/ถูกยกเลิก/ผู้สร้างไม่มีสิทธิ์แล้ว — ตอบ 410 ให้หน้าแชร์บอกเหตุผลได้ต่างจากลิงก์ที่ไม่มีอยู่ (404)</summary>
public sealed class PerformanceShareLinkGoneException(string message) : Exception(message);

/// <summary>
/// ลิงก์อ่านอย่างเดียวของสรุปผลงานพนักงานสำหรับ HR (`/?p=&lt;code&gt;`) — สร้าง/ยกเลิกได้เฉพาะผู้มี PERFORMANCE.EDIT
///
/// ข้อมูลส่วนบุคคลจึงเข้มกว่าลิงก์ Defect: code สุ่ม 10 ตัว, มีวันหมดอายุเสมอ (≤ 90 วัน), ยกเลิกได้, นับจำนวนครั้งที่เปิด,
/// บันทึก AuditLog ตอนสร้าง/ยกเลิก และใช้สิทธิ์ของผู้สร้าง ณ ตอนเปิด — ผู้สร้างถูกปิดบัญชีหรือหมดสิทธิ์ PERFORMANCE.EDIT ลิงก์ก็ใช้ไม่ได้
/// และ metric กรองตาม Project ที่ผู้สร้างเป็นสมาชิกอยู่ตอนนั้น
/// </summary>
public sealed class PerformanceShareLinkService(QaDbContext db, PerformanceService performance, PerformanceReportService reports, ProjectAccessService projectAccess, IConfiguration configuration, TimeProvider clock)
{
    public const string AuditEntityType = "PerformanceShareLink";
    private const string CodeAlphabet = "23456789abcdefghjkmnpqrstuvwxyz";
    private const int CodeLength = 10;
    private static readonly int[] AllowedLifetimes = [7, 14, 30, 60, 90];

    public static bool IsAllowedLifetime(int days) => AllowedLifetimes.Contains(days);

    public async Task<IReadOnlyList<PerformanceShareLinkDto>> ListAsync(PerformanceViewer viewer, Guid userId, CancellationToken ct)
    {
        RequireManage(viewer);
        var links = await db.PerformanceShareLinks.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync(ct);
        return await ToDtosAsync(links, ct);
    }

    public async Task<PerformanceShareLinkDto> CreateAsync(PerformanceViewer viewer, Guid userId, CreatePerformanceShareLinkRequest r, CancellationToken ct)
    {
        RequireManage(viewer);
        if (!IsAllowedLifetime(r.ExpiresInDays)) throw new ArgumentException($"อายุลิงก์ต้องเป็น {string.Join("/", AllowedLifetimes)} วัน");
        var (from, to) = performance.ResolveRange(r.From, r.To);
        var employee = await db.Users.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.DisplayName).FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException("ไม่พบผู้ใช้");

        var now = clock.GetUtcNow().UtcDateTime;
        string code;
        do code = string.Concat(Enumerable.Range(0, CodeLength).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]));
        while (await db.PerformanceShareLinks.AnyAsync(x => x.Code == code, ct));

        var link = new PerformanceShareLink(code, userId, from, to, r.IncludeEvaluations, r.IncludeWorkLogs, r.Note, now.AddDays(r.ExpiresInDays), viewer.UserId, now);
        db.PerformanceShareLinks.Add(link);
        db.AuditLogs.Add(new AuditLog(viewer.UserId, "Created", AuditEntityType, link.PerformanceShareLinkId.ToString(),
            $"สร้างลิงก์สรุปผลงานของ {employee} ช่วง {from:yyyy-MM-dd} ถึง {to:yyyy-MM-dd} อายุ {r.ExpiresInDays} วัน (การประเมิน: {(r.IncludeEvaluations ? "รวม" : "ไม่รวม")}, บันทึกงาน: {(r.IncludeWorkLogs ? "รวม" : "ไม่รวม")})",
            null, null, null));
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([link], ct))[0];
    }

    public async Task<PerformanceShareLinkDto> RevokeAsync(PerformanceViewer viewer, Guid shareLinkId, CancellationToken ct)
    {
        RequireManage(viewer);
        var link = await db.PerformanceShareLinks.FirstOrDefaultAsync(x => x.PerformanceShareLinkId == shareLinkId, ct) ?? throw new EntityNotFoundException("ไม่พบลิงก์แชร์");
        if (link.RevokedAt is null)
        {
            link.Revoke(viewer.UserId, clock.GetUtcNow().UtcDateTime);
            db.AuditLogs.Add(new AuditLog(viewer.UserId, "Revoked", AuditEntityType, link.PerformanceShareLinkId.ToString(), "ยกเลิกลิงก์สรุปผลงาน", null, null, null));
            await db.SaveChangesAsync(ct);
        }
        return (await ToDtosAsync([link], ct))[0];
    }

    /// <summary>เปิดลิงก์จากหน้าแชร์ (anonymous) — null = ไม่มี code นี้</summary>
    public async Task<SharedPerformanceReportDto?> OpenAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 12) return null;
        var link = await db.PerformanceShareLinks.FirstOrDefaultAsync(x => x.Code == code, ct);
        if (link is null) return null;
        var now = clock.GetUtcNow().UtcDateTime;
        if (link.RevokedAt is not null) throw new PerformanceShareLinkGoneException("ลิงก์นี้ถูกยกเลิกแล้ว กรุณาขอลิงก์ใหม่จากหัวหน้าทีม QA");
        if (link.ExpiresAt <= now) throw new PerformanceShareLinkGoneException("ลิงก์นี้หมดอายุแล้ว กรุณาขอลิงก์ใหม่จากหัวหน้าทีม QA");
        if (!await CanManageAsync(link.CreatedBy, ct)) throw new PerformanceShareLinkGoneException("ผู้สร้างลิงก์ไม่มีสิทธิ์ดูผลงานทีมแล้ว ลิงก์จึงใช้ไม่ได้ กรุณาขอลิงก์ใหม่จากหัวหน้าทีม QA");

        var viewer = new PerformanceViewer(link.CreatedBy, true, await projectAccess.GetAllowedProjectIdsAsync(link.CreatedBy, ct));
        var report = await reports.BuildAsync(viewer, link.UserId, link.FromDate, link.ToDate, link.IncludeEvaluations, link.IncludeWorkLogs, ct);
        link.RecordView(now);
        await db.SaveChangesAsync(ct);
        var sharedBy = await db.Users.AsNoTracking().Where(x => x.UserId == link.CreatedBy).Select(x => x.DisplayName).FirstOrDefaultAsync(ct);
        return new SharedPerformanceReportDto(report, link.ExpiresAt, link.CreatedAt, sharedBy, link.Note);
    }

    /// <summary>ผู้ใช้ยัง Active และยังมี PERFORMANCE.EDIT (หรือ SYS_ADMIN) จาก Role ที่เปิดใช้งาน — ตรวจจากฐานข้อมูลเพราะไม่มี token ของผู้สร้าง</summary>
    private Task<bool> CanManageAsync(Guid userId, CancellationToken ct)
        => db.Users.AsNoTracking().Where(u => u.UserId == userId && u.IsActive)
            .AnyAsync(u => db.UserRoles.Any(ur => ur.UserId == u.UserId && db.Roles.Any(r => r.RoleId == ur.RoleId && r.IsActive &&
                (r.RoleCode == "SYS_ADMIN" || db.RolePermissions.Any(rp => rp.RoleId == r.RoleId && db.Permissions.Any(p => p.PermissionId == rp.PermissionId && p.PermissionCode == "PERFORMANCE.EDIT"))))), ct);

    private async Task<IReadOnlyList<PerformanceShareLinkDto>> ToDtosAsync(IReadOnlyList<PerformanceShareLink> links, CancellationToken ct)
    {
        var userIds = links.Select(x => x.CreatedBy).Concat(links.Where(x => x.RevokedBy is not null).Select(x => x.RevokedBy!.Value)).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, x => x.DisplayName, ct);
        var baseUrl = (configuration["PublicWebBaseUrl"] ?? "https://promaxx2.qahub.store").TrimEnd('/');
        var now = clock.GetUtcNow().UtcDateTime;
        return links.Select(x => new PerformanceShareLinkDto(
            x.PerformanceShareLinkId, $"{baseUrl}/?p={x.Code}", x.FromDate, x.ToDate, x.IncludeEvaluations, x.IncludeWorkLogs, x.Note,
            x.ExpiresAt, x.CreatedAt, names.GetValueOrDefault(x.CreatedBy), x.RevokedAt, x.RevokedBy is { } r ? names.GetValueOrDefault(r) : null,
            x.ViewCount, x.LastViewedAt, x.RevokedAt is not null ? "Revoked" : x.ExpiresAt <= now ? "Expired" : "Active")).ToList();
    }

    private static void RequireManage(PerformanceViewer viewer)
    {
        if (!viewer.CanManage) throw new PerformanceAccessDeniedException("ต้องมีสิทธิ์จัดการผลงานทีม (PERFORMANCE.EDIT) จึงจะแชร์สรุปผลงานได้");
    }
}
