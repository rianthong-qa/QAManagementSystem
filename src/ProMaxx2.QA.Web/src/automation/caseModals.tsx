import { useEffect, useState } from "react";
import { formatThaiDateTime } from "../dateTime";
import {
  automationVersionTone as versionStatusTone,
  parseDslSteps,
} from "../automationUtils";
import type { AutomationCaseItem, AutomationVersionItem, AutomationAgentItem, FlakyCandidateItem } from "./types";
import { useBuildsAndEnvironments, AI_GENERATE_TIMEOUT_MS, targetTone, sampleDsl } from "./shared";
import { ModalShell, Badge } from "./ui";

export function VersionEditor({
  selectedCase, versions, canEdit, canValidate, canApprove, canRun, canGenerateAi, createBusy, versionError, onCreate, onValidate, onApprove, onRun, onGenerateAi,
}: {
  selectedCase: AutomationCaseItem; versions: AutomationVersionItem[]; canEdit: boolean; canValidate: boolean; canApprove: boolean; canRun: boolean; canGenerateAi: boolean;
  createBusy: boolean; versionError: string;
  onCreate: (dsl: string, reason: string) => void; onValidate: (v: AutomationVersionItem) => void; onApprove: (v: AutomationVersionItem) => void;
  onRun: () => void; onGenerateAi: () => void;
}) {
  const [dsl, setDsl] = useState(sampleDsl);
  const [reason, setReason] = useState("");

  return <div className="automation-version-editor">
    {versionError && <div className="inline-alert error" role="alert"><span>{versionError}</span></div>}
    <section className="automation-version-list">
      <h3>Version History ({versions.length})</h3>
      {versions.length ? versions.map((v) => <article key={v.automationVersionId} className="automation-version-row">
        <div className="automation-version-meta"><b>Rev {v.versionNo}</b><Badge tone={versionStatusTone[v.validationStatus] ?? "gray"}>{v.validationStatus}</Badge>{v.approvedAt && <Badge tone="green">Approved</Badge>}<span>TestCase Rev {v.testCaseRevisionNo}</span><span>{parseDslSteps(v.dslJson).length} steps</span><time>{formatThaiDateTime(v.createdAt)}</time></div>
        <p className="automation-dsl-preview">{v.dslJson.length > 300 ? `${v.dslJson.slice(0, 300)}…` : v.dslJson}</p>
        {v.validationErrors && <p className="automation-validation-errors">{v.validationErrors}</p>}
        <div className="automation-version-actions">
          {canValidate && v.validationStatus !== "Valid" && <button className="btn" disabled={createBusy} onClick={() => onValidate(v)}><span className="material-symbols-outlined" aria-hidden="true">check</span> Validate</button>}
          {canApprove && v.validationStatus === "Valid" && !v.approvedAt && <button className="btn primary" disabled={createBusy} onClick={() => onApprove(v)}><span className="material-symbols-outlined" aria-hidden="true">check</span> อนุมัติ</button>}
          {canRun && selectedCase.status === "Ready" && <button className="btn primary" disabled={createBusy} onClick={onRun}><span className="material-symbols-outlined" aria-hidden="true">play_arrow</span> สั่งรัน</button>}
        </div>
      </article>) : <div className="empty"><p>ยังไม่มี Version</p><small>สร้าง Version แรกด้วย DSL ด้านล่าง</small></div>}
    </section>
    {canEdit && <section className="automation-version-create">
      <h3>สร้าง Version ใหม่ (DSL v1)</h3>
      <label className="full">DSL JSON<textarea rows={14} value={dsl} onChange={(e) => setDsl(e.target.value)} spellCheck={false} aria-label="DSL JSON" /></label>
      <div className="automation-version-create-actions">
        {canGenerateAi && <button type="button" className="btn primary" title={`เรียก AI Provider จริง อาจใช้เวลาถึง ${AI_GENERATE_TIMEOUT_MS / 1000} วินาที`} disabled={createBusy} onClick={onGenerateAi}>{createBusy ? <><span className="spinner inline" aria-hidden="true" /> AI กำลังสร้าง... (อาจถึง {AI_GENERATE_TIMEOUT_MS / 1000}s)</> : "✦ Generate AI"}</button>}
        <button type="button" className="btn" onClick={() => setDsl(sampleDsl)}><span className="material-symbols-outlined" aria-hidden="true">description</span> โหลดตัวอย่าง</button>
        <label className="automation-reason-field">หมายเหตุการเปลี่ยนแปลง<input type="text" value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} placeholder="เช่น เพิ่ม step ตรวจ stock" /></label>
        <button type="button" className="btn" disabled={createBusy || !dsl.trim()} onClick={() => onCreate(dsl, reason)}>{createBusy ? "กำลังบันทึก..." : "สร้าง Version"}</button>
      </div>
      <p className="muted-text">DSL ต้องเป็น JSON ตามรูปแบบ: <code>{"{ dslVersion, automationType, steps: [{ stepNo, action, parameters }] }"}</code> — Action ต้องมีใน Action Library</p>
    </section>}
  </div>;
}

export function RunModal({
  item, versions, releaseId, agents, busy, onClose, onRun,
}: {
  item: AutomationCaseItem; versions: AutomationVersionItem[]; releaseId?: string; agents: AutomationAgentItem[];
  busy: boolean; onClose: () => void; onRun: (c: AutomationCaseItem, versionId: string, buildId: string, envId: string, agentId: string, priority: number) => void;
}) {
  const { builds, environments, loadError } = useBuildsAndEnvironments(releaseId);
  const [versionId, setVersionId] = useState("");
  const [buildId, setBuildId] = useState("");
  const [envId, setEnvId] = useState("");
  const [agentId, setAgentId] = useState("");
  const [priority, setPriority] = useState(5);
  const approved = versions.find((v) => v.approvedAt && v.validationStatus === "Valid");
  useEffect(() => { if (!versionId && approved) setVersionId(approved.automationVersionId); }, [versionId, approved]);

  return <ModalShell labelledBy="automation-run-title" onDismiss={() => { if (!busy) onClose(); }} form>
    <div className="modal-head"><div><h2 id="automation-run-title">สั่งรัน {item.automationCode}</h2><small>สร้าง Automation Execution + Job เข้าคิว ให้ Agent รับไปรัน</small></div><button aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    {loadError && <div className="inline-alert error" role="alert"><span>{loadError}</span></div>}
    <div className="form-grid">
      <label className="full">Automation Version<select value={versionId} onChange={(e) => setVersionId(e.target.value)}><option value="">เลือก Version</option>{versions.map((v) => <option key={v.automationVersionId} value={v.automationVersionId}>Rev {v.versionNo} · {v.validationStatus}{v.approvedAt ? " · Approved" : ""}</option>)}</select></label>
      <label>Build<select value={buildId} onChange={(e) => setBuildId(e.target.value)}><option value="">เลือก Build</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}{b.applicationVersion ? ` · App ${b.applicationVersion}` : ""}</option>)}</select></label>
      <label>Environment<select value={envId} onChange={(e) => setEnvId(e.target.value)}><option value="">เลือก Environment</option>{environments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>
      <label>Agent (ไม่บังคับ)<select value={agentId} onChange={(e) => setAgentId(e.target.value)}><option value="">ปล่อยให้คิวจัดสรร</option>{agents.map((a) => <option key={a.agentId} value={a.agentId}>{a.agentCode} · {a.connectivity}</option>)}</select></label>
      <label>Priority<select value={priority} onChange={(e) => setPriority(Number(e.target.value))}>{[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((p) => <option key={p} value={p}>{p}</option>)}</select></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={busy || !versionId || !buildId || !envId} onClick={() => onRun(item, versionId, buildId, envId, agentId, priority)}>{busy ? "กำลังส่งงาน..." : "ส่งเข้าคิว"}</button></div>
  </ModalShell>;
}

export function BatchRunModal({ cases, releaseId, canRun, busy, onClose, onRunBatch }: {
  cases: AutomationCaseItem[]; releaseId?: string; canRun: boolean; busy: boolean; onClose: () => void;
  onRunBatch: (ids: string[], buildId: string, envId: string, priority: number) => Promise<void>; onError: (e: string) => void;
}) {
  const readyCases = cases.filter((c) => c.status === "Ready");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [buildId, setBuildId] = useState("");
  const [envId, setEnvId] = useState("");
  const [priority, setPriority] = useState(5);
  const { builds, environments, loadError } = useBuildsAndEnvironments(releaseId);

  const toggle = (id: string) => setSelected((prev) => { const next = new Set(prev); if (next.has(id)) next.delete(id); else next.add(id); return next; });
  const toggleAll = () => setSelected((prev) => prev.size === readyCases.length ? new Set() : new Set(readyCases.map((c) => c.automationCaseId)));

  return <ModalShell labelledBy="automation-batch-title" onDismiss={() => { if (!busy) onClose(); }} form>
    <div className="modal-head"><div><h2 id="automation-batch-title">รันเป็นกลุ่ม (Regression)</h2><small>เลือก Automation Case ที่พร้อมรัน — งานกระจายไปหลาย Agent พร้อมกัน</small></div><button aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    {loadError && <div className="inline-alert error" role="alert"><span>{loadError}</span></div>}
    <div className="form-grid">
      <label>Build<select value={buildId} onChange={(e) => setBuildId(e.target.value)}><option value="">เลือก Build</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}{b.applicationVersion ? ` · App ${b.applicationVersion}` : ""}</option>)}</select></label>
      <label>Environment<select value={envId} onChange={(e) => setEnvId(e.target.value)}><option value="">เลือก Env</option>{environments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>
      <label>Priority<select value={priority} onChange={(e) => setPriority(Number(e.target.value))}>{[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((p) => <option key={p} value={p}>{p}</option>)}</select></label>
    </div>
    {readyCases.length ? <div className="automation-batch-list">
      <div className="automation-batch-head"><input type="checkbox" aria-label="เลือกทั้งหมด" checked={selected.size === readyCases.length && readyCases.length > 0} disabled={!canRun} onChange={toggleAll} /><b>เลือกทั้งหมด ({readyCases.length})</b><span>{selected.size} เลือก</span></div>
      {readyCases.map((c) => <label key={c.automationCaseId} className="automation-batch-row"><input type="checkbox" aria-label={`เลือก ${c.automationCode}`} checked={selected.has(c.automationCaseId)} disabled={!canRun} onChange={() => toggle(c.automationCaseId)} /><span><b>{c.automationCode}</b><small>{c.testCaseCode} · {c.testCaseTitle}</small></span><Badge tone={targetTone[c.automationType] ?? "blue"}>{c.automationType}</Badge></label>)}
    </div> : <div className="empty"><p>ยังไม่มี Automation Case ที่ Ready</p><small>สร้าง Case แล้ว Validate/อนุมัติให้เป็น Ready ก่อน</small></div>}
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={!canRun || busy || !selected.size || !buildId || !envId} onClick={() => onRunBatch([...selected], buildId, envId, priority)}>{busy ? "กำลังส่ง..." : `▶ รัน ${selected.size} case`}</button></div>
  </ModalShell>;
}

export function QuarantineModal({ candidate, busy, onClose, onConfirm }: {
  candidate: FlakyCandidateItem; busy: boolean; onClose: () => void; onConfirm: (caseId: string, reason: string, ownerUserId: string, expiresAt: string) => void;
}) {
  const [reason, setReason] = useState(`Flaky: ${candidate.transitions} transitions ใน ${candidate.recentRuns} execution ล่าสุด`);
  const [ownerUserId, setOwnerUserId] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  return <ModalShell labelledBy="automation-quarantine-title" onDismiss={() => { if (!busy) onClose(); }} form>
    <div className="modal-head"><div><h2 id="automation-quarantine-title">Quarantine {candidate.automationCode}</h2><small>แยกออกจาก Product Fail ชั่วคราวจนกว่าจะแก้ไข Flaky</small></div><button aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    <div className="form-grid">
      <label className="full">เหตุผล<textarea rows={3} value={reason} onChange={(e) => setReason(e.target.value)} /></label>
      <label>User Id ผู้รับผิดชอบ (ไม่บังคับ)<input type="text" value={ownerUserId} onChange={(e) => setOwnerUserId(e.target.value)} /></label>
      <label>หมดอายุ (ไม่บังคับ)<input type="date" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} /></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={busy || !reason.trim()} onClick={() => onConfirm(candidate.automationCaseId, reason.trim(), ownerUserId, expiresAt ? new Date(expiresAt).toISOString() : "")}>{busy ? "กำลังบันทึก..." : "Quarantine"}</button></div>
  </ModalShell>;
}
