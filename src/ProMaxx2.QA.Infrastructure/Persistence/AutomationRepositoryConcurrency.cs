using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.Infrastructure.Persistence;

/// <summary>AUT-REL-001/002: การบันทึกพร้อมกัน (concurrency) และการเก็บกวาดงานค้างของ Automation</summary>
public sealed partial class AutomationRepository
{
    /// <summary>AUT-REL-002: snapshot/restore ไม่ส่ง heartbeat ระหว่าง backup — ใช้อายุงานนับจากตอน Agent รับงานแทน
    /// ตั้งไว้ยาวเพราะ backup/restore DB ใหญ่ใช้เวลานานได้จริง</summary>
    public static readonly TimeSpan DataJobStaleAfter = TimeSpan.FromHours(2);
    /// <summary>AUT-REL-002: verification ทั้ง batch เป็นแค่การ scan UI — ถ้าเกิน 1 ชั่วโมงหลังขอแล้วยังไม่รายงาน ถือว่า Agent หาย</summary>
    public static readonly TimeSpan VerificationStaleAfter = TimeSpan.FromHours(1);

    /// <summary>แปลง exception ของ EF เป็น exception ของ Application layer (ซึ่งไม่รู้จัก EF) —
    /// rowversion ไม่ตรง → <see cref="ConcurrencyConflictException"/>, unique index ชน → <see cref="DuplicateCodeException"/></summary>
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("ข้อมูลถูกแก้ไขโดยคำขออื่นพร้อมกัน — โหลดข้อมูลล่าสุดแล้วลองใหม่", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new DuplicateCodeException("มีการบันทึกข้อมูลเดียวกันซ้ำพร้อมกัน — โหลดข้อมูลล่าสุดแล้วลองใหม่");
        }
    }

    public void DiscardChanges() => db.ChangeTracker.Clear();

    private static bool IsDeadlock(Exception ex) => ex is SqlException { Number: 1205 } || ex.InnerException is SqlException { Number: 1205 };

    /// <summary>AUT-REL-001: claim ทุกชนิดใช้ Serializable transaction — Agent สองตัว poll พร้อมกันอาจทำให้ SQL Server
    /// เลือกคำขอหนึ่งเป็น deadlock victim (1205) ซึ่งเดิมหลุดเป็น 500; ตอนนี้ตอบว่า "ไม่มีงาน" แล้ว Agent จะ poll ใหม่รอบถัดไปเอง</summary>
    private async Task<T?> NullOnDeadlockAsync<T>(Func<Task<T?>> claim) where T : class
    {
        try
        {
            return await claim();
        }
        catch (Exception ex) when (IsDeadlock(ex))
        {
            db.ChangeTracker.Clear();
            return null;
        }
    }

    public Task<AutomationJobPackageDto?> ClaimNextJobAsync(string agentCode, string agentVersion, IReadOnlyList<string> capabilities, string targetApp, CancellationToken ct)
        => NullOnDeadlockAsync(() => ClaimNextJobCoreAsync(agentCode, agentVersion, capabilities, targetApp, ct));

    public Task<VerificationBatchPackageDto?> ClaimVerificationBatchAsync(string agentCode, CancellationToken ct)
        => NullOnDeadlockAsync(() => ClaimVerificationBatchCoreAsync(agentCode, ct));

    public Task<ClaimSnapshotPackageDto?> ClaimNextSnapshotRequestAsync(string agentCode, CancellationToken ct)
        => NullOnDeadlockAsync(() => ClaimNextSnapshotRequestCoreAsync(agentCode, ct));

    public Task<ClaimRestorePackageDto?> ClaimNextRestoreRequestAsync(string agentCode, CancellationToken ct)
        => NullOnDeadlockAsync(() => ClaimNextRestoreRequestCoreAsync(agentCode, ct));

    public Task<ClaimSeedRunPackageDto?> ClaimNextSeedRunRequestAsync(string agentCode, CancellationToken ct)
        => NullOnDeadlockAsync(() => ClaimNextSeedRunRequestCoreAsync(agentCode, ct));

    public async Task<int> GetMaxVersionNoAsync(Guid caseId, CancellationToken ct)
        => await db.AutomationVersions.Where(x => x.AutomationCaseId == caseId).MaxAsync(x => (int?)x.VersionNo, ct) ?? 0;

    public async Task<IReadOnlyList<StaleExecutionDto>> ListStaleRunningExecutionsAsync(DateTime heartbeatBefore, DateTime startedBefore, CancellationToken ct)
        => await db.AutomationExecutions.AsNoTracking()
            .Where(x => x.Status == "Running")
            .Select(x => new
            {
                x.AutomationExecutionId,
                x.StartedAt,
                LastHeartbeatAt = x.Agent != null && !x.Agent.IsDeleted ? (DateTime?)x.Agent.LastHeartbeatAt : null,
            })
            .Where(x => x.LastHeartbeatAt == null || x.LastHeartbeatAt < heartbeatBefore || (x.StartedAt != null && x.StartedAt < startedBefore))
            .Select(x => new StaleExecutionDto(x.AutomationExecutionId, x.StartedAt != null && x.StartedAt < startedBefore && x.LastHeartbeatAt != null && x.LastHeartbeatAt >= heartbeatBefore))
            .ToListAsync(ct);

    public async Task<StaleDataWorkResultDto> FailStaleDataWorkAsync(DateTime nowUtc, CancellationToken ct)
    {
        var dataCutoff = nowUtc - DataJobStaleAfter;
        var snapshots = await db.AutomationDbSnapshots.Where(x => x.Status == "Running" && x.StartedAt < dataCutoff).ToListAsync(ct);
        foreach (var s in snapshots) s.Fail(s.DbKind, $"Agent ไม่รายงานผลภายใน {DataJobStaleAfter.TotalHours:0} ชั่วโมงหลังรับงาน — ถือว่า Agent หาย (AUT-REL-002) ขอ Snapshot ใหม่ได้");
        var restores = await db.AutomationDbRestores.Where(x => x.Status == "Running" && x.StartedAt < dataCutoff).ToListAsync(ct);
        // restore ไม่ถูกดึงกลับไปให้ Agent อื่นรันซ้ำโดยอัตโนมัติ — การ restore ทับ DB สองครั้งพร้อมกันอันตรายกว่า; ให้ผู้ใช้ตรวจ DB แล้วสั่งใหม่เอง
        foreach (var r in restores) r.Fail(r.ChecksumVerified, r.AvailabilityVerified, $"Agent ไม่รายงานผลภายใน {DataJobStaleAfter.TotalHours:0} ชั่วโมงหลังรับงาน — ตรวจสถานะ DB ของ Environment ก่อนสั่ง Restore ใหม่ (AUT-REL-002)");
        var verificationCutoff = nowUtc - VerificationStaleAfter;
        var verifications = await db.AutomationObjectVerifications.Where(x => x.Status == "Assigned" && x.RequestedAt < verificationCutoff).ToListAsync(ct);
        foreach (var v in verifications) v.Complete("Error", null, null, $"Agent ไม่รายงานผลภายใน {VerificationStaleAfter.TotalHours:0} ชั่วโมง (AUT-REL-002) — สั่งตรวจสอบใหม่ได้");
        var seedRuns = await db.AutomationDataSeedRuns.Where(x => x.Status == "Running" && x.StartedAt < nowUtc - SeedRunStaleAfter).ToListAsync(ct);
        foreach (var run in seedRuns) run.ReclaimIfStale(nowUtc, SeedRunStaleAfter);
        if (snapshots.Count + restores.Count + verifications.Count + seedRuns.Count > 0) await SaveChangesAsync(ct);
        return new StaleDataWorkResultDto(snapshots.Count, restores.Count, verifications.Count, seedRuns.Count);
    }
}
