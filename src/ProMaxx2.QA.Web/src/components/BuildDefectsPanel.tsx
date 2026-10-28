import { useEffect, useState } from "react";
import { apiUrl, getJson, isAbortError } from "../api";
import { Badge } from "./Badge";
import { notify } from "./dialogStore";
import { defectStatusTones, type SessionUser } from "../shared/appShared";
import "./BuildDefectsPanel.css";

type Item = { defectId: string; defectCode: string; title: string; severity: string; status: string; reopenCount: number };
type Group = { total: number; bySeverity: Record<string, number>; items: Item[] };
type Carryover = { buildNumber: string; foundHere: Group; carriedOver: Group; retestQueue: Group };
type Environment = { testEnvironmentId: string; environmentName: string; isActive: boolean };
type GroupKey = "retestQueue" | "carriedOver" | "foundHere";

// ลำดับ = ลำดับงานของ QA เมื่อได้ Build ใหม่ (DEFECT_BUILD_LIFECYCLE.md §4)
const groups: { key: GroupKey; label: string; hint: string; icon: string }[] = [
  { key: "retestQueue", label: "รอตรวจซ้ำ", hint: "Dev แก้แล้ว (Resolved) ใน Build นี้หรือก่อนหน้า — ตรวจซ้ำบน Build นี้", icon: "fact_check" },
  { key: "carriedOver", label: "ค้างจาก Build ก่อน", hint: "ยังไม่ได้แก้ (Open / In Progress) และเจอใน Build ก่อนหน้า", icon: "history" },
  { key: "foundHere", label: "เจอใน Build นี้", hint: "Defect ที่แจ้งบน Build นี้ทุกสถานะ", icon: "bug_report" },
];
const severityTone: Record<string, string> = { Critical: "red", High: "red", Medium: "yellow", Low: "gray" };
const severities = ["Critical", "High", "Medium", "Low"];

const canRunExecution = () => {
  try { const user = JSON.parse(localStorage.getItem("qa.user") ?? "{}") as SessionUser; return Boolean(user.roles?.includes("SYS_ADMIN") || user.permissions?.includes("EXECUTION.RUN")); }
  catch { return false; }
};

/** Defect ของ Build (หน้าต่างรายละเอียด Build) — คิวตรวจซ้ำ / ค้างจาก Build ก่อน / เจอใน Build นี้ + สร้าง Retest Cycle จากคิวตรวจซ้ำ */
export function BuildDefectsPanel({ buildId, projectId, isActive, onOpenCycle }: { buildId: string; projectId?: string; isActive: boolean; onOpenCycle?: (cycleId: string) => void }) {
  const [data, setData] = useState<Carryover | null>(null);
  const [error, setError] = useState("");
  const [reload, setReload] = useState(0);
  const [active, setActive] = useState<GroupKey>("retestQueue");
  const [environments, setEnvironments] = useState<Environment[]>([]);
  const [environmentId, setEnvironmentId] = useState("");
  const [creating, setCreating] = useState(false);
  const canCreate = canRunExecution() && isActive;

  useEffect(() => {
    const controller = new AbortController();
    setError("");
    getJson<Carryover>(`${apiUrl}/builds/${buildId}/defect-carryover`, controller.signal)
      .then(setData)
      .catch((e) => { if (!isAbortError(e)) setError(e instanceof Error ? e.message : "โหลด Defect ของ Build นี้ไม่สำเร็จ"); });
    return () => controller.abort();
  }, [buildId, reload]);

  useEffect(() => {
    if (!canCreate || !projectId) return;
    const controller = new AbortController();
    getJson<Environment[]>(`${apiUrl}/test-environments?projectId=${projectId}`, controller.signal)
      .then((rows) => { const usable = (rows ?? []).filter((x) => x.isActive); setEnvironments(usable); setEnvironmentId((current) => current || usable[0]?.testEnvironmentId || ""); })
      .catch((e) => { if (!isAbortError(e)) setEnvironments([]); });
    return () => controller.abort();
  }, [canCreate, projectId]);

  const createRetestCycle = async () => {
    if (!environmentId) return;
    setCreating(true);
    try {
      const response = await fetch(`${apiUrl}/builds/${buildId}/retest-cycle`, { method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }, body: JSON.stringify({ environmentId }) });
      if (!response.ok) {
        const text = await response.text().catch(() => "");
        let message = text.trim();
        try { const parsed = JSON.parse(text); message = typeof parsed === "string" ? parsed : parsed?.detail ?? parsed?.title ?? message; } catch { /* ข้อความธรรมดา */ }
        throw new Error(response.status === 403 ? "ต้องมีสิทธิ์รัน Test Execution เพื่อสร้าง Retest Cycle" : message || "สร้าง Retest Cycle ไม่สำเร็จ");
      }
      const result = await response.json() as { testCycleId: string; cycleCode: string; caseCount: number };
      notify(`สร้าง ${result.cycleCode} แล้ว (${result.caseCount} Test Case) — ผลผ่าน/ไม่ผ่านของเคสใช้ปิดหรือ Reopen Defect`, "success");
      onOpenCycle?.(result.testCycleId);
    } catch (e) { notify(e instanceof Error ? e.message : "สร้าง Retest Cycle ไม่สำเร็จ", "error"); }
    finally { setCreating(false); }
  };

  if (error) return <section className="rb-detail-section bdp"><h3>Defect ของ Build นี้</h3><div className="inline-alert error" role="alert"><span>{error}</span><button type="button" onClick={() => setReload((x) => x + 1)}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></div></section>;
  if (!data) return <section className="rb-detail-section bdp" aria-busy="true"><h3>Defect ของ Build นี้</h3><p className="rb-detail-text empty"><span className="spinner inline" aria-hidden="true" /> กำลังโหลด...</p></section>;

  const current = data[active];
  const meta = groups.find((g) => g.key === active)!;
  return (
    <section className="rb-detail-section bdp">
      <h3>Defect ของ Build นี้ <small>ใช้ตอนได้ Build ใหม่ — ตรวจซ้ำก่อน แล้วไล่ของค้าง</small></h3>
      <div className="bdp-tabs" role="tablist" aria-label="กลุ่ม Defect ของ Build">
        {groups.map((g) => <button key={g.key} type="button" role="tab" id={`bdp-tab-${g.key}`} aria-selected={active === g.key} aria-controls="bdp-panel" className={active === g.key ? "active" : ""} onClick={() => setActive(g.key)}>
          <span className="material-symbols-outlined" aria-hidden="true">{g.icon}</span>
          <span className="bdp-tab-text"><b>{data[g.key].total.toLocaleString()}</b><small>{g.label}</small></span>
        </button>)}
      </div>
      <div id="bdp-panel" role="tabpanel" aria-labelledby={`bdp-tab-${active}`} className="bdp-panel">
        <p className="bdp-hint">{meta.hint}{current.total > 0 && <span className="bdp-sev">{severities.filter((s) => current.bySeverity[s]).map((s) => <Badge key={s} tone={severityTone[s]}>{`${s} ${current.bySeverity[s]}`}</Badge>)}</span>}</p>
        {active === "retestQueue" && current.total > 0 && (canCreate
          ? <div className="bdp-actions">
            <label><span>Environment</span><select value={environmentId} onChange={(e) => setEnvironmentId(e.target.value)} disabled={!environments.length}>
              {environments.length ? environments.map((x) => <option key={x.testEnvironmentId} value={x.testEnvironmentId}>{x.environmentName}</option>) : <option value="">ไม่มี Environment ที่ใช้งาน</option>}
            </select></label>
            <button type="button" className="btn primary" disabled={creating || !environmentId} onClick={() => void createRetestCycle()}>
              {creating ? <span className="spinner inline" aria-hidden="true" /> : <span className="material-symbols-outlined" aria-hidden="true">playlist_add_check</span>}สร้าง Retest Cycle
            </button>
          </div>
          : <p className="bdp-note">{isActive ? "สร้าง Retest Cycle ได้เมื่อมีสิทธิ์รัน Test Execution" : "Build นี้ถูกปิดใช้งาน — สร้าง Retest Cycle บน Build ที่ใช้งานอยู่"}</p>)}
        {current.items.length === 0
          ? <p className="rb-detail-text empty">ไม่มี Defect ในกลุ่มนี้</p>
          : <ul className="bdp-list">{current.items.map((x) => <li key={x.defectId}>
            <span className="bdp-code">{x.defectCode}</span>
            <span className="bdp-title" title={x.title}>{x.title}</span>
            <span className="bdp-badges"><Badge tone={severityTone[x.severity] ?? "gray"}>{x.severity}</Badge><Badge tone={defectStatusTones[x.status] ?? "gray"}>{x.status}</Badge>{x.reopenCount > 0 && <Badge tone="red">{`Reopen ${x.reopenCount}`}</Badge>}</span>
          </li>)}</ul>}
        {current.total > current.items.length && <p className="bdp-note">แสดง {current.items.length} รายการแรก (รุนแรงสุดและค้างนานสุดก่อน) จาก {current.total.toLocaleString()} — ดูทั้งหมดที่หน้า Defect</p>}
      </div>
    </section>
  );
}
