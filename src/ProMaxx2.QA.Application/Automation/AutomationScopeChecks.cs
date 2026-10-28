namespace ProMaxx2.QA.Application.Automation;

/// <summary>AUT-SEC-003: ตรวจว่า Build/Environment ที่ผู้ใช้ส่งมากับคำสั่งรัน/Schedule/Build Trigger/Seed/Snapshot
/// เป็นของ Project เดียวกับงานนั้นจริง — เดิมรับ id ข้าม Project ได้ ทำให้ execution ผูกกับ Build/Environment ของ Project อื่น</summary>
public interface IAutomationScopeChecks
{
    Task<bool> BuildBelongsToProjectAsync(Guid buildId, Guid projectId, CancellationToken ct);
    Task<bool> EnvironmentExistsAsync(Guid environmentId, Guid projectId, CancellationToken ct);
    /// <summary>AUT-SEC-005: หา AgentId จาก AgentCode (ไม่รวม agent ที่ถูกลบ) เพื่อตรวจว่าผู้รายงานผลคือ agent ที่รับงานนั้นจริง</summary>
    Task<Guid?> FindActiveAgentIdByCodeAsync(string agentCode, CancellationToken ct);
}

/// <summary>AUT-SEC-005: agent ที่รายงานผล/อัปโหลดหลักฐานไม่ใช่ agent ที่ได้รับงานนั้น</summary>
public sealed class AgentMismatchException(string message) : Exception(message);

public static class AutomationScopeCheckExtensions
{
    /// <summary>โยน <see cref="ArgumentException"/> เมื่อ Build หรือ Environment ที่ระบุไม่ใช่ของ Project นี้ (ค่า null/empty ข้ามได้)</summary>
    public static async Task EnsureBuildAndEnvironmentAsync(this IAutomationScopeChecks checks, Guid projectId, Guid? buildId, Guid? environmentId, CancellationToken ct)
    {
        if (buildId is { } b && b != Guid.Empty && !await checks.BuildBelongsToProjectAsync(b, projectId, ct))
            throw new ArgumentException("Build ที่เลือกไม่ได้อยู่ใน Project นี้");
        if (environmentId is { } e && e != Guid.Empty && !await checks.EnvironmentExistsAsync(e, projectId, ct))
            throw new ArgumentException("Environment ที่เลือกไม่ได้อยู่ใน Project นี้");
    }

    /// <summary>ถ้าระบุ <paramref name="agentCode"/> มา ต้องเป็น agent เดียวกับ <paramref name="assignedAgentId"/> ของงาน.
    /// Controller ฝั่ง agent บังคับให้ส่ง agentCode เสมอ; service caller ภายใน (worker/test) ที่ไม่ระบุจะข้ามการตรวจ</summary>
    public static async Task EnsureReportingAgentAsync(this IAutomationScopeChecks checks, Guid? assignedAgentId, string? agentCode, CancellationToken ct)
    {
        if (agentCode is null) return;
        var agentId = await checks.FindActiveAgentIdByCodeAsync(agentCode, ct);
        if (agentId is null || assignedAgentId != agentId)
            throw new AgentMismatchException("Agent นี้ไม่ได้เป็นผู้รับงานนี้ จึงรายงานผลแทนไม่ได้");
    }
}
