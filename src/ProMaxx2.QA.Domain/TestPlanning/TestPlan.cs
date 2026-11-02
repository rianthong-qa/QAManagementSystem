using System.Text.Json;
using System.Text.RegularExpressions;
using ProMaxx2.QA.Domain.Execution;

namespace ProMaxx2.QA.Domain.TestPlanning;

public static class TestPlanTypes { public static readonly string[] All = ["Smoke", "Regression", "UAT", "Retest", "Other"]; }

/// <summary>สถานะที่เก็บลงตาราง — Draft/InProgress/Completed คำนวณจาก Run ใน Query (TEST_PLAN_CONFIGURATION_MATRIX_PLAN.md §2.3)</summary>
public static class TestPlanLifecycleStatuses
{
    public const string Active = "Active";
    public const string Closed = "Closed";
    public const string Cancelled = "Cancelled";
}

/// <summary>สถานะที่แสดงของ Plan (คำนวณ) — ใช้เป็นค่า filter และค่าใน DTO</summary>
public static class TestPlanStatuses
{
    public const string Draft = "Draft";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Closed = "Closed";
    public const string Cancelled = "Cancelled";
    public static readonly string[] All = [Draft, InProgress, Completed, Closed, Cancelled];
    /// <summary>สถานะ Test Cycle ที่ถือว่า Run จบแล้ว</summary>
    public static readonly string[] TerminalRunStatuses = ["Completed", "Closed", "Cancelled"];
}

/// <summary>รหัสข้อผิดพลาดใน ProblemDetails (<c>code</c>) ของ Test Plan API</summary>
public static class TestPlanErrorCodes
{
    public const string Invalid = "TEST_PLAN_INVALID";
    public const string NotFound = "TEST_PLAN_NOT_FOUND";
    public const string DuplicateCode = "TEST_PLAN_DUPLICATE_CODE";
    public const string Frozen = "TEST_PLAN_FROZEN";
    public const string NotActive = "TEST_PLAN_NOT_ACTIVE";
    public const string InvalidState = "TEST_PLAN_INVALID_STATE";
    public const string RunsNotTerminal = "TEST_PLAN_RUNS_NOT_TERMINAL";
    public const string HasRuns = "TEST_PLAN_HAS_RUNS";
    public const string CycleLinked = "TEST_PLAN_CYCLE_LINKED";
    public const string RunHasExecutions = "TEST_PLAN_RUN_HAS_EXECUTIONS";
    public const string NoRuns = "TEST_PLAN_NO_RUNS";
    public const string CycleLinkedToPlan = "TEST_CYCLE_LINKED_TO_PLAN";
}

/// <summary>ละเมิดกฎของ Test Plan — Controller ตอบ 409 พร้อม <see cref="Code"/></summary>
public sealed class TestPlanRuleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public static class TestPlanRunRoles { public static readonly string[] All = ["Tester", "Reviewer", "Observer"]; }

public sealed class TestPlan
{
    private TestPlan() { }
    public TestPlan(Guid projectId, Guid releaseId, string code, string name, string? type, DateTime? start, DateTime? end, Guid? ownerUserId, string? notes, Guid? createdBy, Guid? copiedFromTestPlanId = null)
    {
        if (projectId == Guid.Empty || releaseId == Guid.Empty) throw new ArgumentException("Project and release are required.");
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Plan code is required.");
        TestPlanId = Guid.NewGuid();
        ProjectId = projectId;
        ReleaseId = releaseId;
        PlanCode = code.Trim().ToUpperInvariant();
        Apply(name, type, start, end, ownerUserId, notes);
        LifecycleStatus = TestPlanLifecycleStatuses.Active;
        CopiedFromTestPlanId = copiedFromTestPlanId;
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
    }
    public Guid TestPlanId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid ReleaseId { get; private set; }
    public string PlanCode { get; private set; } = string.Empty;
    public string PlanName { get; private set; } = string.Empty;
    public string PlanType { get; private set; } = "Other";
    public DateTime? StartDate { get; private set; }
    public DateTime? EndDate { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public string LifecycleStatus { get; private set; } = TestPlanLifecycleStatuses.Active;
    public bool IsFrozen { get; private set; }
    public DateTime? FrozenAt { get; private set; }
    public Guid? FrozenByUserId { get; private set; }
    public Guid? CopiedFromTestPlanId { get; private set; }
    public string? Notes { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }
    public ICollection<TestPlanRun> Runs { get; private set; } = [];

    public bool IsActive => LifecycleStatus == TestPlanLifecycleStatuses.Active;

    /// <summary>แก้ข้อมูลหลัก/Run ได้เฉพาะ Plan ที่ Active และยังไม่ Freeze</summary>
    public void EnsureEditable()
    {
        if (!IsActive) throw new TestPlanRuleException(TestPlanErrorCodes.NotActive, "Test Plan ที่ปิดหรือยกเลิกแล้วแก้ไขไม่ได้");
        if (IsFrozen) throw new TestPlanRuleException(TestPlanErrorCodes.Frozen, "Test Plan ถูก Freeze อยู่ แก้ไขไม่ได้");
    }

    public void Update(string name, string? type, DateTime? start, DateTime? end, Guid? ownerUserId, string? notes, Guid? updatedBy)
    {
        EnsureEditable();
        Apply(name, type, start, end, ownerUserId, notes);
        Touch(updatedBy);
    }

    /// <summary>ปิด Plan และ Freeze อัตโนมัติ — การตรวจว่าทุก Run จบแล้วทำที่ Service เพราะต้องอ่านสถานะ Test Cycle</summary>
    public void Close(Guid? userId)
    {
        if (!IsActive) throw new TestPlanRuleException(TestPlanErrorCodes.NotActive, "ปิดได้เฉพาะ Test Plan ที่ยัง Active");
        LifecycleStatus = TestPlanLifecycleStatuses.Closed;
        FreezeCore(userId);
        Touch(userId);
    }

    public void Cancel(Guid? userId)
    {
        if (!IsActive) throw new TestPlanRuleException(TestPlanErrorCodes.NotActive, "ยกเลิกได้เฉพาะ Test Plan ที่ยัง Active");
        LifecycleStatus = TestPlanLifecycleStatuses.Cancelled;
        FreezeCore(userId);
        Touch(userId);
    }

    /// <summary>เปิด Plan ที่ปิด/ยกเลิกกลับมา Active และ Unfreeze พร้อมกัน</summary>
    public void Reopen(Guid? userId)
    {
        if (IsActive) throw new TestPlanRuleException(TestPlanErrorCodes.InvalidState, "Reopen ได้เฉพาะ Test Plan ที่ปิดหรือยกเลิกแล้ว");
        LifecycleStatus = TestPlanLifecycleStatuses.Active;
        UnfreezeCore();
        Touch(userId);
    }

    public void Freeze(Guid? userId)
    {
        if (!IsActive) throw new TestPlanRuleException(TestPlanErrorCodes.NotActive, "Freeze/Unfreeze ได้เฉพาะ Test Plan ที่ Active");
        if (IsFrozen) throw new TestPlanRuleException(TestPlanErrorCodes.InvalidState, "Test Plan ถูก Freeze อยู่แล้ว");
        FreezeCore(userId);
        Touch(userId);
    }

    /// <summary>Plan ที่ Closed/Cancelled ต้องใช้ Reopen เท่านั้น</summary>
    public void Unfreeze(Guid? userId)
    {
        if (!IsActive) throw new TestPlanRuleException(TestPlanErrorCodes.NotActive, "Freeze/Unfreeze ได้เฉพาะ Test Plan ที่ Active — Plan ที่ปิดหรือยกเลิกให้ใช้ Reopen");
        if (!IsFrozen) throw new TestPlanRuleException(TestPlanErrorCodes.InvalidState, "Test Plan ไม่ได้ถูก Freeze");
        UnfreezeCore();
        Touch(userId);
    }

    /// <summary>Soft delete เท่านั้น — Plan ที่มี Run ต้องปิดหรือยกเลิกก่อน; Run ยังล็อก Test Cycle ไว้ตามเดิม</summary>
    public void SoftDelete(bool hasRuns, Guid? userId)
    {
        if (IsDeleted) return;
        if (hasRuns && IsActive) throw new TestPlanRuleException(TestPlanErrorCodes.HasRuns, "Test Plan ที่มี Run ลบได้เมื่อปิดหรือยกเลิกแล้วเท่านั้น");
        IsDeleted = true;
        DeletedAt = UpdatedAt = DateTime.UtcNow;
        DeletedBy = UpdatedBy = userId;
    }

    public void Restore(Guid? userId) { IsDeleted = false; DeletedAt = null; DeletedBy = null; Touch(userId); }

    public void Touch(Guid? userId) { UpdatedAt = DateTime.UtcNow; UpdatedBy = userId; }

    private void FreezeCore(Guid? userId) { if (IsFrozen) return; IsFrozen = true; FrozenAt = DateTime.UtcNow; FrozenByUserId = userId; }
    private void UnfreezeCore() { IsFrozen = false; FrozenAt = null; FrozenByUserId = null; }

    private void Apply(string name, string? type, DateTime? start, DateTime? end, Guid? ownerUserId, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Plan name is required.");
        if (start.HasValue && end.HasValue && end < start) throw new ArgumentException("End date must be after start date.");
        PlanName = name.Trim();
        PlanType = string.IsNullOrWhiteSpace(type) ? "Other" : TestPlanTypes.All.SingleOrDefault(x => x.Equals(type.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Invalid plan type.");
        StartDate = start;
        EndDate = end;
        OwnerUserId = ownerUserId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}

/// <summary>1 แถวใน Configuration Matrix — ผูกกับ Test Cycle เดิม 1 ชุด (Build/Environment ใช้ค่าจาก Cycle ไม่เก็บซ้ำ)</summary>
public sealed partial class TestPlanRun
{
    private TestPlanRun() { }
    public TestPlanRun(Guid testPlanId, Guid testCycleId, string runCode, string runName, string? configurationKey, string? configurationName, string? configurationJson, Guid? ownerUserId, Guid? createdBy)
    {
        if (testPlanId == Guid.Empty || testCycleId == Guid.Empty) throw new ArgumentException("Plan and test cycle are required.");
        if (string.IsNullOrWhiteSpace(runCode)) throw new ArgumentException("Run code is required.");
        TestPlanRunId = Guid.NewGuid();
        TestPlanId = testPlanId;
        TestCycleId = testCycleId;
        RunCode = runCode.Trim().ToUpperInvariant();
        Apply(runName, configurationKey, configurationName, configurationJson, ownerUserId);
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
    }
    public Guid TestPlanRunId { get; private set; }
    public Guid TestPlanId { get; private set; }
    public Guid TestCycleId { get; private set; }
    public string RunCode { get; private set; } = string.Empty;
    public string RunName { get; private set; } = string.Empty;
    public string? ConfigurationKey { get; private set; }
    public string? ConfigurationName { get; private set; }
    public string? ConfigurationJson { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public TestPlan Plan { get; private set; } = null!;
    public TestCycle Cycle { get; private set; } = null!;
    public ICollection<TestPlanRunAssignment> Assignments { get; private set; } = [];

    public void Update(string runName, string? configurationKey, string? configurationName, string? configurationJson, Guid? ownerUserId, Guid? updatedBy)
    {
        Apply(runName, configurationKey, configurationName, configurationJson, ownerUserId);
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>Configuration Key เป็นตัวพิมพ์เล็ก a-z, 0-9 และ - เพื่อใช้ filter/report ได้สม่ำเสมอ เช่น windows-chrome</summary>
    public static string? NormalizeConfigurationKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var normalized = key.Trim().ToLowerInvariant();
        if (normalized.Length > 100 || !ConfigurationKeyPattern().IsMatch(normalized)) throw new ArgumentException("Configuration key must contain only a-z, 0-9 and '-' (max 100 characters).");
        return normalized;
    }

    /// <summary>ConfigurationJson ต้องเป็น JSON object ระดับบน (เช่น {"os":"Windows 11"}) — เก็บแบบย่อ</summary>
    public static string? NormalizeConfigurationJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Configuration JSON must be an object.");
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException) { throw new ArgumentException("Configuration JSON is not valid JSON."); }
    }

    private void Apply(string runName, string? configurationKey, string? configurationName, string? configurationJson, Guid? ownerUserId)
    {
        if (string.IsNullOrWhiteSpace(runName)) throw new ArgumentException("Run name is required.");
        RunName = runName.Trim();
        ConfigurationKey = NormalizeConfigurationKey(configurationKey);
        ConfigurationName = string.IsNullOrWhiteSpace(configurationName) ? null : configurationName.Trim();
        ConfigurationJson = NormalizeConfigurationJson(configurationJson);
        OwnerUserId = ownerUserId;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ConfigurationKeyPattern();
}

/// <summary>ทีมระดับ Run — ใช้เป็นค่าเริ่มต้นของ Bulk assign เท่านั้น ไม่แทน Assignment รายเคส</summary>
public sealed class TestPlanRunAssignment
{
    private TestPlanRunAssignment() { }
    public TestPlanRunAssignment(Guid testPlanRunId, Guid testerUserId, string role, Guid assignedBy)
    {
        if (testPlanRunId == Guid.Empty || testerUserId == Guid.Empty || assignedBy == Guid.Empty) throw new ArgumentException("Run, tester and assigner are required.");
        TestPlanRunAssignmentId = Guid.NewGuid();
        TestPlanRunId = testPlanRunId;
        TesterUserId = testerUserId;
        Role = TestPlanRunRoles.All.SingleOrDefault(x => x.Equals(role?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Role must be Tester, Reviewer or Observer.");
        AssignedAt = DateTime.UtcNow;
        AssignedBy = assignedBy;
    }
    public Guid TestPlanRunAssignmentId { get; private set; }
    public Guid TestPlanRunId { get; private set; }
    public Guid TesterUserId { get; private set; }
    public string Role { get; private set; } = "Tester";
    public DateTime AssignedAt { get; private set; }
    public Guid AssignedBy { get; private set; }
}
