using ProMaxx2.QA.Domain.Defects;

namespace ProMaxx2.QA.Domain.Governance;

public enum ReleaseDecision { Go, ConditionalGo, NoGo }

/// <summary>Defect ของ Release ที่ Gate นับ (DEFECT_BUILD_LIFECYCLE.md เฟส 4) — Blocker = High ที่ยังไม่ได้แก้ หรือ Resolved ที่ยังไม่ตรวจซ้ำ (ทุก Severity)</summary>
public sealed record DefectGateCounts(int OpenCritical, int OpenHigh, int UnverifiedResolved, int BlockersWithoutRisk, int BlockersWithRisk)
{
    /// <param name="defects">Defect ของ Release (ไม่รวมที่ลบ) พร้อมบอกว่ามี Risk Acceptance ที่อนุมัติแล้วผูกกับ Defect นั้นหรือไม่</param>
    public static DefectGateCounts From(IEnumerable<(string Severity, string Status, bool HasApprovedRisk)> defects)
    {
        int critical = 0, high = 0, unverified = 0, withoutRisk = 0, withRisk = 0;
        foreach (var (severity, status, hasRisk) in defects)
        {
            var unresolved = DefectStatusGroups.Unresolved.Contains(status);
            var awaiting = DefectStatusGroups.AwaitingVerification.Contains(status);
            if (unresolved && severity == "Critical") { critical++; continue; } // Critical ที่ยังไม่ได้แก้ปลดด้วย Risk ไม่ได้
            if (unresolved && severity == "High") high++;
            if (awaiting) unverified++;
            if (!(unresolved && severity == "High") && !awaiting) continue;
            if (hasRisk) withRisk++; else withoutRisk++;
        }
        return new(critical, high, unverified, withoutRisk, withRisk);
    }
}

/// <param name="Defects">null = ปิด Defect Gate (config <c>Defects:GateEnabled</c>)</param>
public sealed record ReleaseGateInput(int OpenP0, int OpenP1Blockers, bool HasApprovedRisk, bool SmokePassed = true, DefectGateCounts? Defects = null);

public static class ReleaseGate
{
    public static ReleaseDecision Evaluate(ReleaseGateInput input)
    {
        if (!input.SmokePassed) return ReleaseDecision.NoGo;
        if (input.OpenP0 > 0) return ReleaseDecision.NoGo;
        // Critical ค้าง = NO-GO; High ค้าง / Resolved ที่ยังไม่ตรวจซ้ำ ต้องมี Risk Acceptance ของ Defect นั้น ไม่งั้น NO-GO
        if (input.Defects is { } d && (d.OpenCritical > 0 || d.BlockersWithoutRisk > 0)) return ReleaseDecision.NoGo;
        if (input.OpenP1Blockers > 0) return input.HasApprovedRisk ? ReleaseDecision.ConditionalGo : ReleaseDecision.NoGo;
        if (input.Defects is { BlockersWithRisk: > 0 }) return ReleaseDecision.ConditionalGo;
        return ReleaseDecision.Go;
    }
}
