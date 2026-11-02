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

/// <summary>ข้อมูลที่ใช้ตัดสินความพร้อมของ Release — กฎชุดเดียวที่ Dashboard / Test Summary / Release Sign-off ใช้ร่วมกัน</summary>
/// <param name="SmokePassed">false = มี Smoke policy ของ Project และผลล่าสุดของ Build ที่เลือกไม่ผ่าน (ไม่ได้ตั้ง Smoke / ไม่ได้เลือก Build = ไม่ประเมิน → true)</param>
/// <param name="Defects">null = ไม่นับ Defect เลย</param>
/// <param name="Coverage">null = ไม่มี Requirement ให้ประเมิน</param>
/// <param name="PassRate">null = ยังไม่มีผลทดสอบให้ประเมิน</param>
/// <param name="StrictDefectGate">config <c>Defects:GateEnabled</c> — true: High ค้าง/Resolved รอตรวจซ้ำที่ไม่มี Risk = NO-GO; false = CONDITIONAL GO</param>
public sealed record ReleaseGateInput(int OpenP0, int OpenP1Blockers, bool HasApprovedRisk, bool SmokePassed = true, DefectGateCounts? Defects = null, decimal? Coverage = null, decimal? PassRate = null, bool StrictDefectGate = true);

public static class ReleaseGate
{
    /// <summary>เกณฑ์ขั้นต่ำของ Requirement Coverage และ Pass Rate (%) — ต่ำกว่านี้ = CONDITIONAL GO</summary>
    public const decimal QualityThreshold = 90m;

    public static ReleaseDecision Evaluate(ReleaseGateInput input) => Assess(input).Decision;

    /// <summary>คำตัดสิน + เหตุผลข้อแรกที่ทำให้ไม่ GO (ภาษาไทย) — หน้าเว็บแสดงเหตุผลนี้ตรง ๆ ไม่คำนวณซ้ำเอง</summary>
    public static (ReleaseDecision Decision, string Reason) Assess(ReleaseGateInput input)
    {
        var d = input.Defects;
        if (!input.SmokePassed) return (ReleaseDecision.NoGo, "Smoke Test ของ Build นี้ยังไม่ผ่าน");
        if (input.OpenP0 > 0) return (ReleaseDecision.NoGo, $"พบผลทดสอบ P0 ค้าง (Fail/Blocked) {input.OpenP0} รายการ");
        // Critical ที่ยังไม่ได้แก้ปลดด้วย Risk Acceptance ไม่ได้
        if (d is { OpenCritical: > 0 }) return (ReleaseDecision.NoGo, $"พบ Critical Defect ที่ยังไม่ได้แก้ {d.OpenCritical} รายการ");
        if (input.StrictDefectGate && d is { BlockersWithoutRisk: > 0 }) return (ReleaseDecision.NoGo, $"พบ High Defect ค้าง/Defect รอตรวจซ้ำ {d.BlockersWithoutRisk} รายการที่ยังไม่มี Risk Acceptance");
        if (input.OpenP1Blockers > 0) return (ReleaseDecision.ConditionalGo, input.HasApprovedRisk ? $"พบผลทดสอบ P1 ค้าง {input.OpenP1Blockers} รายการ (มี Risk Acceptance อนุมัติแล้ว)" : $"พบผลทดสอบ P1 ค้าง {input.OpenP1Blockers} รายการ ยังไม่มี Risk Acceptance");
        if (d is { BlockersWithoutRisk: > 0 }) return (ReleaseDecision.ConditionalGo, $"พบ High Defect ค้าง/Defect รอตรวจซ้ำ {d.BlockersWithoutRisk} รายการ");
        if (d is { BlockersWithRisk: > 0 }) return (ReleaseDecision.ConditionalGo, $"มี Defect ที่ยอมรับความเสี่ยงไว้ {d.BlockersWithRisk} รายการ");
        if (input.Coverage < QualityThreshold) return (ReleaseDecision.ConditionalGo, $"Requirement Coverage {input.Coverage}% ต่ำกว่าเกณฑ์ {QualityThreshold:0}%");
        if (input.PassRate < QualityThreshold) return (ReleaseDecision.ConditionalGo, $"Pass Rate {input.PassRate}% ต่ำกว่าเกณฑ์ {QualityThreshold:0}%");
        return (ReleaseDecision.Go, "ผ่านทุกเกณฑ์ความพร้อม");
    }
}
