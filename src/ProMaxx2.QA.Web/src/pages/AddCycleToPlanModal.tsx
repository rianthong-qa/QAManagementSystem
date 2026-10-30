import { useEffect, useState } from "react";
import { apiFetch, apiUrl, getJson, isAbortError } from "../api";
import { notify } from "../components/dialogStore";
import { ModalShell } from "../components/ModalShell";
import type { TestCycleItem } from "../shared/appShared";
import "./TestPlansPage.css";

// "เพิ่มเข้า Test Plan" จากหน้า Test Cycle (TEST_PLAN_CONFIGURATION_MATRIX_PLAN.md §5.5) — เพิ่ม Cycle นี้เป็น Run ของแผนเดิม
// ใช้ endpoint attach-cycle ชุดเดียวกับหน้าต่าง "เพิ่ม Run" ในหน้า Test Plan; Server ตรวจ Release/Project/Cycle ซ้ำอีกชั้น

type PlanOption = { testPlanId: string; releaseId: string; planCode: string; planName: string; lifecycleStatus: string; isFrozen: boolean; runCount: number };

export function AddCycleToPlanModal({ cycle, onClose, onAdded }: { cycle: TestCycleItem; onClose: () => void; onAdded: (planCode: string) => void }) {
  const [plans, setPlans] = useState<PlanOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [planId, setPlanId] = useState("");
  const [configurationName, setConfigurationName] = useState("");
  const [configurationKey, setConfigurationKey] = useState("");

  useEffect(() => {
    const ctrl = new AbortController();
    const params = new URLSearchParams({ projectId: cycle.projectId, releaseId: cycle.releaseId, size: "500" });
    getJson<{ items: { rows: PlanOption[] } }>(`${apiUrl}/test-plans?${params}`, ctrl.signal)
      .then((result) => {
        // แผนที่ปิด/ยกเลิก/Freeze เพิ่ม Run ไม่ได้ — ไม่แสดงเป็นตัวเลือก
        const open = result.items.rows.filter((x) => x.releaseId === cycle.releaseId && x.lifecycleStatus === "Active" && !x.isFrozen);
        setPlans(open);
        if (open.length === 1) setPlanId(open[0].testPlanId);
      })
      .catch((e) => { if (!isAbortError(e)) setError(e instanceof Error && e.message ? e.message : "โหลด Test Plan ไม่สำเร็จ"); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [cycle.projectId, cycle.releaseId]);

  const keyInvalid = configurationKey.trim() !== "" && !/^[a-z0-9]+(-[a-z0-9]+)*$/i.test(configurationKey.trim());
  const missing = [!planId && "Test Plan"].filter(Boolean) as string[];

  const save = async () => {
    const plan = plans.find((x) => x.testPlanId === planId);
    if (!plan) return;
    setSaving(true); setError("");
    try {
      await apiFetch(`/test-plans/${plan.testPlanId}/runs/attach-cycle`, {
        method: "POST",
        json: { testCycleId: cycle.testCycleId, runCode: null, runName: null, configurationKey: configurationKey.trim().toLowerCase() || null, configurationName: configurationName.trim() || null, configurationJson: null, ownerUserId: null },
        fallbackMessage: "เพิ่มเข้า Test Plan ไม่สำเร็จ",
      });
      notify(`เพิ่ม ${cycle.cycleCode} เข้า ${plan.planCode} แล้ว`, "success");
      onAdded(plan.planCode);
    } catch (e) { setError(e instanceof Error && e.message ? e.message : "เพิ่มเข้า Test Plan ไม่สำเร็จ"); }
    finally { setSaving(false); }
  };

  return (
    <ModalShell labelledBy="cy-add-plan-title" className="tp-form" onDismiss={() => { if (!saving) onClose(); }}>
      <div className="modal-head"><div><h2 id="cy-add-plan-title">เพิ่มเข้า Test Plan</h2><small>{cycle.cycleCode} · Release {cycle.releaseCode || "-"} · Build {cycle.buildNumber || "-"} · {cycle.environmentName || "-"}</small></div><button disabled={saving} aria-label="ปิดหน้าต่าง" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      <div className="form-grid">
        <label className="full">Test Plan <span className="required">*</span>
          <select value={planId} disabled={saving || loading} onChange={(e) => setPlanId(e.target.value)}>
            <option value="">{loading ? "กำลังโหลด Test Plan..." : plans.length ? "เลือก Test Plan ของ Release นี้" : "ไม่มี Test Plan ที่เพิ่มได้ใน Release นี้"}</option>
            {plans.map((x) => <option key={x.testPlanId} value={x.testPlanId}>{x.planCode} · {x.planName} ({x.runCount} Run)</option>)}
          </select>
          <small>แสดงเฉพาะแผนของ Release {cycle.releaseCode || "นี้"} ที่ยังไม่ปิด/ไม่ถูก Freeze — รหัสและชื่อ Run ใช้ของ Test Cycle (แก้ภายหลังได้ในหน้า Test Plan)</small>
        </label>
        <label>Configuration Name<input value={configurationName} disabled={saving} onChange={(e) => setConfigurationName(e.target.value)} placeholder="เช่น Windows 11 · Chrome" maxLength={200} /></label>
        <label>Configuration Key<input value={configurationKey} disabled={saving} aria-invalid={keyInvalid || undefined} onChange={(e) => setConfigurationKey(e.target.value)} placeholder="เช่น windows-chrome" maxLength={100} /><small className={keyInvalid ? "tp-field-error" : undefined}>{keyInvalid ? "ใช้ได้เฉพาะ a-z, 0-9 และ - (เช่น windows-chrome)" : "ไม่บังคับ — ใช้กรอง/เทียบผลข้าม Run"}</small></label>
      </div>
      <div className="modal-actions">
        <small className={`tp-form-status${missing.length || keyInvalid ? " is-missing" : ""}`} role="status">{missing.length ? `ยังขาด: ${missing.join(", ")}` : keyInvalid ? "Configuration Key ไม่ถูกต้อง" : "ข้อมูลครบ พร้อมบันทึก"}</small>
        <button className="btn" disabled={saving} onClick={onClose}>ยกเลิก</button>
        <button className="btn primary" disabled={saving || loading || missing.length > 0 || keyInvalid} onClick={save}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : "เพิ่มเข้า Test Plan"}</button>
      </div>
    </ModalShell>
  );
}
