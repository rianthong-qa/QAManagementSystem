using System.Text.Json;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Projects;
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

/// <summary>ข้อมูล Test Cycle ที่ต้องใช้ตรวจก่อน Attach</summary>
public sealed record TestPlanCycleRef(Guid TestCycleId, Guid ProjectId, Guid ReleaseId, string CycleCode, string CycleName, bool IsDeleted);

public interface ITestPlanRepository
{
    Task<TestPlanListResultDto> ListAsync(Guid? projectId, Guid? releaseId, string? status, string? planType, Guid? ownerUserId, string? search, int page, int size, CancellationToken ct);
    Task<TestPlanDto?> GetAsync(Guid id, CancellationToken ct);
    Task<TestPlan?> FindAsync(Guid id, CancellationToken ct);
    Task<bool> CodeExistsAsync(Guid projectId, string code, Guid? excludeId, CancellationToken ct);
    Task<IReadOnlyList<string>> ListCodesAsync(Guid projectId, string prefix, CancellationToken ct);
    /// <summary>คืนสถานะ Release เมื่อ Release อยู่ใน Project นั้น ไม่พบคืน null</summary>
    Task<string?> ReleaseStatusAsync(Guid projectId, Guid releaseId, CancellationToken ct);
    Task<TestPlanCycleRef?> FindCycleAsync(Guid testCycleId, CancellationToken ct);
    /// <summary>Cycle ผูกกับ Run ใดอยู่แล้วหรือไม่ — รวม Run ของ Plan ที่ถูก soft delete</summary>
    Task<bool> IsCycleLinkedAsync(Guid testCycleId, CancellationToken ct);
    Task<bool> RunCodeExistsAsync(Guid testPlanId, string runCode, CancellationToken ct);
    Task AddAsync(TestPlan entity, CancellationToken ct);
    Task AddRunAsync(TestPlanRun entity, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
}

public sealed class TestPlanService(ITestPlanRepository repository, IProjectRepository projects)
{
    public Task<TestPlanListResultDto> ListAsync(Guid? projectId, Guid? releaseId, string? status, string? planType, Guid? ownerUserId, string? search, int page, int size, CancellationToken ct) => repository.ListAsync(projectId, releaseId, status, planType, ownerUserId, search, page, size, ct);

    public async Task<TestPlanDto> GetAsync(Guid id, CancellationToken ct) => await repository.GetAsync(id, ct) ?? throw new EntityNotFoundException("Test plan not found.");

    public async Task<TestPlanDto> CreateAsync(SaveTestPlanRequest r, Guid? userId, CancellationToken ct)
    {
        var project = await projects.GetAsync(r.ProjectId, ct) ?? throw new EntityNotFoundException("Project not found.");
        await EnsureReleaseAsync(r.ProjectId, r.ReleaseId, ct);
        var prefix = $"{project.ProjectCode}-TP";
        var code = string.IsNullOrWhiteSpace(r.PlanCode) ? BusinessCodeGenerator.NextAvailable(prefix, await repository.ListCodesAsync(r.ProjectId, prefix, ct)) : r.PlanCode.Trim().ToUpperInvariant();
        if (await repository.CodeExistsAsync(r.ProjectId, code, null, ct)) throw new DuplicateCodeException("Plan code already exists.");
        var entity = new TestPlan(r.ProjectId, r.ReleaseId, code, r.PlanName, r.PlanType, r.StartDate, r.EndDate, r.OwnerUserId, r.Notes, userId);
        await repository.AddAsync(entity, ct);
        await repository.SaveAsync(ct);
        return (await repository.GetAsync(entity.TestPlanId, ct))!;
    }

    /// <summary>แก้ข้อมูลหลัก — Project/Release ของ Plan เปลี่ยนไม่ได้เพราะ Run ทุกแถวผูกกับ Release นี้</summary>
    public async Task<TestPlanDto> UpdateAsync(Guid id, SaveTestPlanRequest r, Guid? userId, CancellationToken ct)
    {
        var entity = await repository.FindAsync(id, ct) ?? throw new EntityNotFoundException("Test plan not found.");
        if (r.ProjectId != entity.ProjectId || r.ReleaseId != entity.ReleaseId) throw new ArgumentException("Project and release of a test plan cannot be changed.");
        if (!string.IsNullOrWhiteSpace(r.PlanCode) && !r.PlanCode.Trim().Equals(entity.PlanCode, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Plan code cannot be changed.");
        entity.Update(r.PlanName, r.PlanType, r.StartDate, r.EndDate, r.OwnerUserId, r.Notes, userId);
        await repository.SaveAsync(ct);
        return (await repository.GetAsync(id, ct))!;
    }

    /// <summary>ผูก Test Cycle ที่มีอยู่เข้า Plan โดยไม่คัดลอก Test Case หรือ Execution History</summary>
    public async Task<TestPlanRunDto> AttachCycleAsync(Guid planId, AttachTestPlanCycleRequest r, Guid? userId, CancellationToken ct)
    {
        var plan = await repository.FindAsync(planId, ct) ?? throw new EntityNotFoundException("Test plan not found.");
        plan.EnsureEditable();
        var cycle = await repository.FindCycleAsync(r.TestCycleId, ct);
        if (cycle is null || cycle.IsDeleted) throw new EntityNotFoundException("Test cycle not found.");
        if (cycle.ProjectId != plan.ProjectId || cycle.ReleaseId != plan.ReleaseId) throw new ArgumentException("Test cycle must belong to the same project and release as the test plan.");
        if (await repository.IsCycleLinkedAsync(cycle.TestCycleId, ct)) throw new InvalidOperationException("Test cycle นี้อยู่ใน Test Plan อื่นแล้ว — Clone Cycle ก่อนหากต้องการใช้ในแผนใหม่");
        var runCode = string.IsNullOrWhiteSpace(r.RunCode) ? cycle.CycleCode : r.RunCode.Trim().ToUpperInvariant();
        if (await repository.RunCodeExistsAsync(plan.TestPlanId, runCode, ct)) throw new DuplicateCodeException("Run code already exists in this test plan.");
        var json = r.ConfigurationJson is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element ? element.GetRawText() : null;
        var run = new TestPlanRun(plan.TestPlanId, cycle.TestCycleId, runCode, string.IsNullOrWhiteSpace(r.RunName) ? cycle.CycleName : r.RunName, r.ConfigurationKey, r.ConfigurationName, json, r.OwnerUserId, userId);
        await repository.AddRunAsync(run, ct);
        plan.Touch(userId);
        await repository.SaveAsync(ct);
        return (await repository.GetAsync(plan.TestPlanId, ct))!.Runs.Single(x => x.TestPlanRunId == run.TestPlanRunId);
    }

    private async Task EnsureReleaseAsync(Guid projectId, Guid releaseId, CancellationToken ct)
    {
        var status = await repository.ReleaseStatusAsync(projectId, releaseId, ct) ?? throw new EntityNotFoundException("Release not found in project.");
        if (status == "Cancelled") throw new ArgumentException("Cannot create a test plan for a cancelled release.");
    }
}
