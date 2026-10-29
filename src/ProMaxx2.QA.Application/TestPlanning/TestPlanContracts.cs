using System.Text.Json;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.TestPlanning;

namespace ProMaxx2.QA.Application.TestPlanning;

/// <summary>ตัวนับรวมของ Plan — สูตรตาม TEST_PLAN_CONFIGURATION_MATRIX_PLAN.md §3.4</summary>
public sealed record TestPlanSummaryDto(int RunCount, int TotalCases, int Untested, int InProgress, int Touched, int Completed, int Passed, int Failed, int Blocked, int Skipped, int Retest, int RetestAttempts, decimal ProgressPercent, decimal CompletionPercent, decimal? PassRate)
{
    public static readonly TestPlanSummaryDto Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0m, 0m, null);
}

public sealed record TestPlanListItemDto(Guid TestPlanId, Guid ProjectId, Guid ReleaseId, string? ReleaseCode, string PlanCode, string PlanName, string PlanType, DateTime? StartDate, DateTime? EndDate, Guid? OwnerUserId, string? OwnerName, string Status, string LifecycleStatus, bool IsFrozen, int RunCount, int TotalCases, int Touched, int Completed, int Passed, decimal ProgressPercent, decimal? PassRate, DateTime CreatedAt);
public sealed record TestPlanListResultDto(PagedResult<TestPlanListItemDto> Items, TestPlanSummaryDto Summary);

/// <summary>1 แถวของ Matrix — Build/Environment/สถานะมาจาก Test Cycle ที่ผูกอยู่</summary>
public sealed record TestPlanRunDto(Guid TestPlanRunId, Guid TestPlanId, Guid TestCycleId, string CycleCode, string CycleName, string Status, string RunCode, string RunName, string? ConfigurationKey, string? ConfigurationName, string? ConfigurationJson, Guid? OwnerUserId, string? OwnerName, Guid BuildId, string? BuildNumber, Guid EnvironmentId, string? EnvironmentName, int TotalCases, int Untested, int InProgress, int Passed, int Failed, int Blocked, int Skipped, decimal ProgressPercent, decimal? PassRate, DateTime CreatedAt);

public sealed record TestPlanDto(Guid TestPlanId, Guid ProjectId, Guid ReleaseId, string? ReleaseCode, string PlanCode, string PlanName, string PlanType, DateTime? StartDate, DateTime? EndDate, Guid? OwnerUserId, string? OwnerName, string Status, string LifecycleStatus, bool IsFrozen, DateTime? FrozenAt, Guid? FrozenByUserId, Guid? CopiedFromTestPlanId, string? Notes, DateTime CreatedAt, DateTime? UpdatedAt, TestPlanSummaryDto Summary, IReadOnlyList<TestPlanRunDto> Runs);

public sealed record SaveTestPlanRequest(Guid ProjectId, Guid ReleaseId, string? PlanCode, string PlanName, string? PlanType, DateTime? StartDate, DateTime? EndDate, Guid? OwnerUserId, string? Notes);
public sealed record AttachTestPlanCycleRequest(Guid TestCycleId, string? RunCode, string? RunName, string? ConfigurationKey, string? ConfigurationName, JsonElement? ConfigurationJson, Guid? OwnerUserId);

/// <summary>
/// สร้าง Run — <c>CreateCycleFromSuite = true</c> สร้าง Test Cycle ใหม่จาก Suite (ต้องมี TestSuiteId/BuildId/EnvironmentId)
/// มิฉะนั้นผูก Test Cycle ที่มีอยู่ (<c>TestCycleId</c>)
/// </summary>
public sealed record CreateTestPlanRunRequest(string? RunCode, string? RunName, string? ConfigurationKey, string? ConfigurationName, JsonElement? ConfigurationJson, Guid? OwnerUserId, bool CreateCycleFromSuite, Guid? TestCycleId, Guid? TestSuiteId, Guid? BuildId, Guid? EnvironmentId, string? CycleName, bool RequiredOnly = false);
public sealed record UpdateTestPlanRunRequest(string RunName, string? ConfigurationKey, string? ConfigurationName, JsonElement? ConfigurationJson, Guid? OwnerUserId);
/// <summary>เหตุผลของคำสั่ง Close/Cancel/Reopen/Freeze/Unfreeze — บันทึกใน Audit Log</summary>
public sealed record TestPlanActionRequest(string Reason);

/// <summary>ข้อมูล Test Cycle ที่ต้องใช้ตรวจก่อน Attach</summary>
public sealed record TestPlanCycleRef(Guid TestCycleId, Guid ProjectId, Guid ReleaseId, string CycleCode, string CycleName, bool IsDeleted);

public interface ITestPlanRepository
{
    Task<TestPlanListResultDto> ListAsync(Guid? projectId, Guid? releaseId, string? status, string? planType, Guid? ownerUserId, string? search, int page, int size, CancellationToken ct);
    Task<TestPlanDto?> GetAsync(Guid id, CancellationToken ct);
    /// <summary>แถว Matrix ของ Plan แบบ filter + แบ่งหน้า — status คือสถานะ Run (= สถานะ Test Cycle)</summary>
    Task<PagedResult<TestPlanRunDto>> ListRunsAsync(Guid planId, string? status, string? configurationKey, Guid? ownerUserId, int page, int size, CancellationToken ct);
    Task<TestPlanRunDto?> GetRunAsync(Guid planId, Guid runId, CancellationToken ct);
    Task<TestPlan?> FindAsync(Guid id, CancellationToken ct);
    Task<TestPlanRun?> FindRunAsync(Guid planId, Guid runId, CancellationToken ct);
    Task<bool> CodeExistsAsync(Guid projectId, string code, Guid? excludeId, CancellationToken ct);
    Task<IReadOnlyList<string>> ListCodesAsync(Guid projectId, string prefix, CancellationToken ct);
    /// <summary>คืนสถานะ Release เมื่อ Release อยู่ใน Project นั้น ไม่พบคืน null</summary>
    Task<string?> ReleaseStatusAsync(Guid projectId, Guid releaseId, CancellationToken ct);
    Task<TestPlanCycleRef?> FindCycleAsync(Guid testCycleId, CancellationToken ct);
    /// <summary>Cycle ผูกกับ Run ใดอยู่แล้วหรือไม่ — รวม Run ของ Plan ที่ถูก soft delete</summary>
    Task<bool> IsCycleLinkedAsync(Guid testCycleId, CancellationToken ct);
    Task<bool> RunCodeExistsAsync(Guid testPlanId, string runCode, CancellationToken ct);
    /// <summary>จำนวน Run ทั้งหมด และจำนวนที่ Test Cycle ยังไม่อยู่ในสถานะจบ (Completed/Closed/Cancelled)</summary>
    Task<(int Total, int NotTerminal)> RunStatusCountsAsync(Guid testPlanId, CancellationToken ct);
    /// <summary>Cycle เคยมี Execution หรือไม่ — นับรวมแถวที่ถูกลบ เพราะเคยเป็นผลที่บันทึกจริง</summary>
    Task<bool> CycleHasExecutionsAsync(Guid testCycleId, CancellationToken ct);
    Task AddAsync(TestPlan entity, CancellationToken ct);
    Task AddRunAsync(TestPlanRun entity, CancellationToken ct);
    void RemoveRun(TestPlanRun entity);
    void AddAudit(AuditLog log);
    /// <summary>รันงานหลายขั้นใน Transaction เดียว (Create Cycle จาก Suite + Attach Run) — ฐานข้อมูลที่ไม่รองรับ Transaction รันตรง ๆ</summary>
    Task RunInTransactionAsync(Func<Task> work, CancellationToken ct);
    /// <summary>บันทึก — Unique index ชน (Attach Cycle เดียวกันพร้อมกัน/Run code ซ้ำ) แปลงเป็น <see cref="TestPlanRuleException"/></summary>
    Task SaveAsync(CancellationToken ct);
}

public sealed class TestPlanService(ITestPlanRepository repository, IProjectRepository projects, TestCycleService cycles)
{
    private const string AuditEntity = "TestPlan";

    public Task<TestPlanListResultDto> ListAsync(Guid? projectId, Guid? releaseId, string? status, string? planType, Guid? ownerUserId, string? search, int page, int size, CancellationToken ct) => repository.ListAsync(projectId, releaseId, status, planType, ownerUserId, search, page, size, ct);

    public async Task<TestPlanDto> GetAsync(Guid id, CancellationToken ct) => await repository.GetAsync(id, ct) ?? throw new EntityNotFoundException("Test plan not found.");

    public async Task<PagedResult<TestPlanRunDto>> MatrixAsync(Guid planId, string? status, string? configurationKey, Guid? ownerUserId, int page, int size, CancellationToken ct)
    {
        _ = await repository.FindAsync(planId, ct) ?? throw new EntityNotFoundException("Test plan not found.");
        return await repository.ListRunsAsync(planId, status, configurationKey, ownerUserId, page, size, ct);
    }

    public async Task<TestPlanRunDto> GetRunAsync(Guid planId, Guid runId, CancellationToken ct) => await repository.GetRunAsync(planId, runId, ct) ?? throw new EntityNotFoundException("Test plan run not found.");

    public async Task<TestPlanDto> CreateAsync(SaveTestPlanRequest r, Guid? userId, CancellationToken ct)
    {
        var project = await projects.GetAsync(r.ProjectId, ct) ?? throw new EntityNotFoundException("Project not found.");
        await EnsureReleaseAsync(r.ProjectId, r.ReleaseId, ct);
        var prefix = $"{project.ProjectCode}-TP";
        var code = string.IsNullOrWhiteSpace(r.PlanCode) ? BusinessCodeGenerator.NextAvailable(prefix, await repository.ListCodesAsync(r.ProjectId, prefix, ct)) : r.PlanCode.Trim().ToUpperInvariant();
        if (await repository.CodeExistsAsync(r.ProjectId, code, null, ct)) throw new DuplicateCodeException("Plan code already exists.");
        var entity = new TestPlan(r.ProjectId, r.ReleaseId, code, r.PlanName, r.PlanType, r.StartDate, r.EndDate, r.OwnerUserId, r.Notes, userId);
        await repository.AddAsync(entity, ct);
        Audit(userId, "Created", entity, $"สร้าง Test Plan {entity.PlanCode}");
        await repository.SaveAsync(ct);
        return (await repository.GetAsync(entity.TestPlanId, ct))!;
    }

    /// <summary>แก้ข้อมูลหลัก — Project/Release ของ Plan เปลี่ยนไม่ได้เพราะ Run ทุกแถวผูกกับ Release นี้</summary>
    public async Task<TestPlanDto> UpdateAsync(Guid id, SaveTestPlanRequest r, Guid? userId, CancellationToken ct)
    {
        var entity = await FindPlanAsync(id, ct);
        if (r.ProjectId != entity.ProjectId || r.ReleaseId != entity.ReleaseId) throw new ArgumentException("Project and release of a test plan cannot be changed.");
        if (!string.IsNullOrWhiteSpace(r.PlanCode) && !r.PlanCode.Trim().Equals(entity.PlanCode, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Plan code cannot be changed.");
        entity.Update(r.PlanName, r.PlanType, r.StartDate, r.EndDate, r.OwnerUserId, r.Notes, userId);
        Audit(userId, "Updated", entity, $"แก้ไข Test Plan {entity.PlanCode}");
        await repository.SaveAsync(ct);
        return (await repository.GetAsync(id, ct))!;
    }

    /// <summary>ปิด Plan — ทุก Run ต้องจบแล้ว (Completed/Closed/Cancelled) และ Freeze อัตโนมัติ</summary>
    public async Task<TestPlanDto> CloseAsync(Guid id, string? reason, Guid? userId, CancellationToken ct)
    {
        var entity = await FindPlanAsync(id, ct);
        var text = RequireReason(reason);
        var (total, notTerminal) = await repository.RunStatusCountsAsync(id, ct);
        if (total == 0) throw new TestPlanRuleException(TestPlanErrorCodes.RunsNotTerminal, "Test Plan ที่ยังไม่มี Run ปิดไม่ได้ — ใช้ยกเลิก (Cancel) แทน");
        if (notTerminal > 0) throw new TestPlanRuleException(TestPlanErrorCodes.RunsNotTerminal, $"ยังมี {notTerminal} Run ที่ Test Cycle ยังไม่จบ (Completed/Closed/Cancelled)");
        return await LifecycleAsync(entity, () => entity.Close(userId), "Closed", "ปิด", text, userId, ct);
    }

    public async Task<TestPlanDto> CancelAsync(Guid id, string? reason, Guid? userId, CancellationToken ct)
    {
        var entity = await FindPlanAsync(id, ct);
        return await LifecycleAsync(entity, () => entity.Cancel(userId), "Cancelled", "ยกเลิก", RequireReason(reason), userId, ct);
    }

    public async Task<TestPlanDto> ReopenAsync(Guid id, string? reason, Guid? userId, CancellationToken ct)
    {
        var entity = await FindPlanAsync(id, ct);
        return await LifecycleAsync(entity, () => entity.Reopen(userId), "Reopened", "เปิดใหม่", RequireReason(reason), userId, ct);
    }

    public async Task<TestPlanDto> FreezeAsync(Guid id, string? reason, Guid? userId, CancellationToken ct)
    {
        var entity = await FindPlanAsync(id, ct);
        return await LifecycleAsync(entity, () => entity.Freeze(userId), "Frozen", "Freeze", RequireReason(reason), userId, ct);
    }

    public async Task<TestPlanDto> UnfreezeAsync(Guid id, string? reason, Guid? userId, CancellationToken ct)
    {
        var entity = await FindPlanAsync(id, ct);
        return await LifecycleAsync(entity, () => entity.Unfreeze(userId), "Unfrozen", "Unfreeze", RequireReason(reason), userId, ct);
    }

    /// <summary>Soft delete — Plan ที่มี Run ต้องปิด/ยกเลิกก่อน และ Run ยังล็อก Test Cycle ไว้ (§7.2.1)</summary>
    public async Task DeleteAsync(Guid id, Guid? userId, CancellationToken ct)
    {
        var entity = await FindPlanAsync(id, ct);
        var (total, _) = await repository.RunStatusCountsAsync(id, ct);
        entity.SoftDelete(total > 0, userId);
        Audit(userId, "Deleted", entity, $"ลบ Test Plan {entity.PlanCode}");
        await repository.SaveAsync(ct);
    }

    /// <summary>สร้าง Run — สร้าง Test Cycle ใหม่จาก Suite แล้วผูก (Transaction เดียว) หรือผูก Cycle ที่มีอยู่</summary>
    public async Task<TestPlanRunDto> CreateRunAsync(Guid planId, CreateTestPlanRunRequest r, Guid? userId, CancellationToken ct)
    {
        if (!r.CreateCycleFromSuite)
        {
            if (r.TestCycleId is not { } cycleId || cycleId == Guid.Empty) throw new ArgumentException("Test cycle is required when not creating a cycle from suite.");
            return await AttachCycleAsync(planId, new AttachTestPlanCycleRequest(cycleId, r.RunCode, r.RunName, r.ConfigurationKey, r.ConfigurationName, r.ConfigurationJson, r.OwnerUserId), userId, ct);
        }
        if (r.TestSuiteId is not { } suiteId || r.BuildId is not { } buildId || r.EnvironmentId is not { } environmentId) throw new ArgumentException("Test suite, build and environment are required to create a cycle from suite.");
        var plan = await FindPlanAsync(planId, ct);
        plan.EnsureEditable();
        var runCode = string.IsNullOrWhiteSpace(r.RunCode) ? null : r.RunCode.Trim().ToUpperInvariant();
        if (runCode is not null && await repository.RunCodeExistsAsync(plan.TestPlanId, runCode, ct)) throw new DuplicateCodeException("Run code already exists in this test plan.");
        var cycleName = FirstNonBlank(r.CycleName, r.RunName, $"{plan.PlanName} · {FirstNonBlank(r.ConfigurationName, r.ConfigurationKey, "Run")}");
        TestPlanRunDto? result = null;
        await repository.RunInTransactionAsync(async () =>
        {
            var cycle = await cycles.CreateAsync(new SaveTestCycleRequest(plan.ProjectId, plan.ReleaseId, buildId, environmentId, suiteId, "", cycleName, plan.PlanType, plan.StartDate, plan.EndDate, r.OwnerUserId ?? plan.OwnerUserId, null, true, r.RequiredOnly), userId, ct);
            result = await AttachCycleAsync(planId, new AttachTestPlanCycleRequest(cycle.TestCycleId, runCode, r.RunName ?? cycle.CycleName, r.ConfigurationKey, r.ConfigurationName, r.ConfigurationJson, r.OwnerUserId), userId, ct);
        }, ct);
        return result!;
    }

    /// <summary>ผูก Test Cycle ที่มีอยู่เข้า Plan โดยไม่คัดลอก Test Case หรือ Execution History</summary>
    public async Task<TestPlanRunDto> AttachCycleAsync(Guid planId, AttachTestPlanCycleRequest r, Guid? userId, CancellationToken ct)
    {
        var plan = await FindPlanAsync(planId, ct);
        plan.EnsureEditable();
        var cycle = await repository.FindCycleAsync(r.TestCycleId, ct);
        if (cycle is null || cycle.IsDeleted) throw new EntityNotFoundException("Test cycle not found.");
        if (cycle.ProjectId != plan.ProjectId || cycle.ReleaseId != plan.ReleaseId) throw new ArgumentException("Test cycle must belong to the same project and release as the test plan.");
        if (await repository.IsCycleLinkedAsync(cycle.TestCycleId, ct)) throw new TestPlanRuleException(TestPlanErrorCodes.CycleLinked, "Test cycle นี้อยู่ใน Test Plan อื่นแล้ว — Clone Cycle ก่อนหากต้องการใช้ในแผนใหม่");
        var runCode = string.IsNullOrWhiteSpace(r.RunCode) ? cycle.CycleCode : r.RunCode.Trim().ToUpperInvariant();
        if (await repository.RunCodeExistsAsync(plan.TestPlanId, runCode, ct)) throw new DuplicateCodeException("Run code already exists in this test plan.");
        var run = new TestPlanRun(plan.TestPlanId, cycle.TestCycleId, runCode, string.IsNullOrWhiteSpace(r.RunName) ? cycle.CycleName : r.RunName, r.ConfigurationKey, r.ConfigurationName, JsonText(r.ConfigurationJson), r.OwnerUserId, userId);
        await repository.AddRunAsync(run, ct);
        plan.Touch(userId);
        Audit(userId, "RunAttached", plan, $"เพิ่ม Run {run.RunCode} (Test Cycle {cycle.CycleCode}) เข้า Test Plan {plan.PlanCode}");
        await repository.SaveAsync(ct);
        return (await repository.GetRunAsync(plan.TestPlanId, run.TestPlanRunId, ct))!;
    }

    public async Task<TestPlanRunDto> UpdateRunAsync(Guid planId, Guid runId, UpdateTestPlanRunRequest r, Guid? userId, CancellationToken ct)
    {
        var plan = await FindPlanAsync(planId, ct);
        plan.EnsureEditable();
        var run = await repository.FindRunAsync(planId, runId, ct) ?? throw new EntityNotFoundException("Test plan run not found.");
        run.Update(r.RunName, r.ConfigurationKey, r.ConfigurationName, JsonText(r.ConfigurationJson), r.OwnerUserId, userId);
        plan.Touch(userId);
        Audit(userId, "RunUpdated", plan, $"แก้ไข Run {run.RunCode} ใน Test Plan {plan.PlanCode}");
        await repository.SaveAsync(ct);
        return (await repository.GetRunAsync(planId, runId, ct))!;
    }

    /// <summary>ถอด Run ออกจาก Plan — ทำได้เฉพาะ Run ที่ไม่เคยมี Execution และ Plan ยังแก้ไขได้ (Test Cycle ไม่ถูกลบ)</summary>
    public async Task DetachRunAsync(Guid planId, Guid runId, Guid? userId, CancellationToken ct)
    {
        var plan = await FindPlanAsync(planId, ct);
        plan.EnsureEditable();
        var run = await repository.FindRunAsync(planId, runId, ct) ?? throw new EntityNotFoundException("Test plan run not found.");
        if (await repository.CycleHasExecutionsAsync(run.TestCycleId, ct)) throw new TestPlanRuleException(TestPlanErrorCodes.RunHasExecutions, "Run นี้มีผลการทดสอบแล้ว ถอดออกไม่ได้ — เปลี่ยน Test Cycle เป็น Cancelled หรือปิด Test Plan แทน");
        repository.RemoveRun(run);
        plan.Touch(userId);
        Audit(userId, "RunDetached", plan, $"ถอด Run {run.RunCode} ออกจาก Test Plan {plan.PlanCode}");
        await repository.SaveAsync(ct);
    }

    private async Task<TestPlanDto> LifecycleAsync(TestPlan entity, Action change, string action, string label, string reason, Guid? userId, CancellationToken ct)
    {
        change();
        Audit(userId, action, entity, $"{label} Test Plan {entity.PlanCode} — เหตุผล: {reason}");
        await repository.SaveAsync(ct);
        return (await repository.GetAsync(entity.TestPlanId, ct))!;
    }

    private async Task<TestPlan> FindPlanAsync(Guid id, CancellationToken ct) => await repository.FindAsync(id, ct) ?? throw new EntityNotFoundException("Test plan not found.");

    private void Audit(Guid? userId, string action, TestPlan plan, string summary) => repository.AddAudit(new AuditLog(userId, action, AuditEntity, plan.TestPlanId.ToString(), summary, null, null, null));

    private static string RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason is required.");
        var text = reason.Trim();
        if (text.Length > 500) throw new ArgumentException("Reason must not exceed 500 characters.");
        return text;
    }

    private static string? JsonText(JsonElement? json) => json is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element ? element.GetRawText() : null;

    private static string FirstNonBlank(params string?[] values) => values.First(x => !string.IsNullOrWhiteSpace(x))!.Trim();

    private async Task EnsureReleaseAsync(Guid projectId, Guid releaseId, CancellationToken ct)
    {
        var status = await repository.ReleaseStatusAsync(projectId, releaseId, ct) ?? throw new EntityNotFoundException("Release not found in project.");
        if (status == "Cancelled") throw new ArgumentException("Cannot create a test plan for a cancelled release.");
    }
}
