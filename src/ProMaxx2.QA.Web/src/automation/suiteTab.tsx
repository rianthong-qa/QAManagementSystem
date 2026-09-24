import { useEffect, useState } from "react";
import { confirmDialog } from "../components/dialogStore";
import { formatThaiDateTime } from "../dateTime";
import { apiUrl } from "../api";
import {
  automationCaseTone as caseStatusTone,
} from "../automationUtils";
import type { AutomationCaseItem, AutomationSuiteCaseItem, AutomationSuiteListItem, AutomationSuiteDetailItem, AutomationSuiteRevisionItem } from "./types";
import { fetchJson, isAbort, useBuildsAndEnvironments, targetTone } from "./shared";
import { ModalShell, Badge } from "./ui";

export function AutomationSuiteTab({ projectId, releaseId, headers, canEdit, canRun, cases }: {
  projectId: string; releaseId?: string; headers: Record<string, string>; canEdit: boolean; canRun: boolean; cases: AutomationCaseItem[];
}) {
  const [suites, setSuites] = useState<AutomationSuiteListItem[]>([]);
  const [search, setSearch] = useState("");
  const [activeFilter, setActiveFilter] = useState<"all" | "active" | "closed">("active");
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [createModal, setCreateModal] = useState(false);
  const [editSuite, setEditSuite] = useState<AutomationSuiteListItem | null>(null);
  const [detail, setDetail] = useState<AutomationSuiteDetailItem | null>(null);
  const [addCasesModal, setAddCasesModal] = useState(false);
  const [history, setHistory] = useState<AutomationSuiteRevisionItem[] | null>(null);
  const [runSuiteFor, setRunSuiteFor] = useState<{ automationSuiteId: string; suiteCode: string; caseCount: number; readyCaseCount: number } | null>(null);
  const [runResult, setRunResult] = useState<{ suiteCode: string; created: number; skipped: string[] } | null>(null);

  useEffect(() => {
    if (!projectId) return;
    const qs = new URLSearchParams({ projectId });
    if (search.trim()) qs.set("search", search.trim());
    if (activeFilter !== "all") qs.set("isActive", activeFilter === "active" ? "true" : "false");
    const ctrl = new AbortController();
    fetchJson(`${apiUrl}/automation/suites?${qs}`, headers, ctrl.signal).then((s) => setSuites(Array.isArray(s) ? s : [])).catch((e) => !isAbort(e) && setError("โหลด Automation Suite ไม่สำเร็จ"));
    return () => ctrl.abort();
  }, [projectId, search, activeFilter, headers, reload]);

  const refreshDetail = async (id: string) => {
    const r = await fetch(`${apiUrl}/automation/suites/${id}?projectId=${projectId}`, { headers });
    if (r.ok) setDetail(await r.json());
  };

  const openDetail = async (row: AutomationSuiteListItem) => { await refreshDetail(row.automationSuiteId); };

  const openHistory = async (id: string) => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${id}/history?projectId=${projectId}`, { headers });
      if (!r.ok) throw new Error("โหลดประวัติไม่สำเร็จ");
      setHistory(await r.json());
    } catch (e) { setError(e instanceof Error ? e.message : "โหลดประวัติไม่สำเร็จ"); }
  };

  const createSuite = async (suiteCode: string, suiteName: string, description: string) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ suiteCode: suiteCode.trim() || null, suiteName: suiteName.trim(), description: description.trim() || null }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Suite ไม่สำเร็จ"); }
      setCreateModal(false); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "สร้าง Suite ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const updateSuite = async (id: string, suiteName: string, description: string, changeReason: string) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${id}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify({ suiteName: suiteName.trim(), description: description.trim() || null, changeReason: changeReason.trim() || null }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "แก้ไข Suite ไม่สำเร็จ"); }
      setEditSuite(null); setReload((v) => v + 1);
      if (detail?.automationSuiteId === id) await refreshDetail(id);
    } catch (e) { setError(e instanceof Error ? e.message : "แก้ไข Suite ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const toggleSuite = async (row: AutomationSuiteListItem) => {
    if (!await confirmDialog(`${row.isActive ? "ปิด" : "เปิด"} Suite "${row.suiteCode}"?`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${row.automationSuiteId}/${row.isActive ? "close" : "reopen"}?projectId=${projectId}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เปลี่ยนสถานะ Suite ไม่สำเร็จ"); }
      setReload((v) => v + 1);
      if (detail?.automationSuiteId === row.automationSuiteId) await refreshDetail(row.automationSuiteId);
    } catch (e) { setError(e instanceof Error ? e.message : "เปลี่ยนสถานะ Suite ไม่สำเร็จ"); }
  };

  const addCases = async (caseIds: string[], isRequired: boolean, changeReason: string) => {
    if (!detail) return;
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${detail.automationSuiteId}/cases?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ automationCaseIds: caseIds, isRequired, changeReason: changeReason.trim() || null }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เพิ่ม Case ไม่สำเร็จ"); }
      setDetail(await r.json()); setAddCasesModal(false); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "เพิ่ม Case ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const removeCase = async (caseId: string) => {
    if (!detail || !await confirmDialog("ลบ Case นี้ออกจาก Suite?")) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${detail.automationSuiteId}/cases/${caseId}?projectId=${projectId}`, { method: "DELETE", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "ลบ Case ไม่สำเร็จ"); }
      setDetail(await r.json()); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "ลบ Case ไม่สำเร็จ"); }
  };

  const toggleRequired = async (row: AutomationSuiteCaseItem) => {
    if (!detail) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${detail.automationSuiteId}/cases/${row.automationCaseId}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify({ sortOrder: row.sortOrder, isRequired: !row.isRequired }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "แก้ไข Case ไม่สำเร็จ"); }
      setDetail(await r.json());
    } catch (e) { setError(e instanceof Error ? e.message : "แก้ไข Case ไม่สำเร็จ"); }
  };

  const runSuite = async (suiteId: string, suiteCode: string, buildId: string, environmentId: string, priority: number) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${suiteId}/run?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ buildId, environmentId, agentId: null, priority }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "รัน Suite ไม่สำเร็จ"); }
      const result: { created: unknown[]; skippedCodes: string[] } = await r.json();
      setRunSuiteFor(null);
      setRunResult({ suiteCode, created: result.created.length, skipped: result.skippedCodes });
    } catch (e) { setError(e instanceof Error ? e.message : "รัน Suite ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const moveCase = async (row: AutomationSuiteCaseItem, direction: -1 | 1) => {
    if (!detail) return;
    const sorted = [...detail.cases].sort((a, b) => a.sortOrder - b.sortOrder);
    const idx = sorted.findIndex((c) => c.automationCaseId === row.automationCaseId);
    const swapWith = sorted[idx + direction];
    if (!swapWith) return;
    setError("");
    try {
      await Promise.all([
        fetch(`${apiUrl}/automation/suites/${detail.automationSuiteId}/cases/${row.automationCaseId}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify({ sortOrder: swapWith.sortOrder, isRequired: row.isRequired }) }),
        fetch(`${apiUrl}/automation/suites/${detail.automationSuiteId}/cases/${swapWith.automationCaseId}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify({ sortOrder: row.sortOrder, isRequired: swapWith.isRequired }) }),
      ]);
      await refreshDetail(detail.automationSuiteId);
    } catch { setError("เรียงลำดับไม่สำเร็จ"); }
  };

  return <section className="automation-cases" aria-label="Automation Suite">
    <header className="automation-section-head"><div><h2>Automation Suite (AUT-P1-001/002)</h2><p>รวม Automation Case เป็นชุดถาวรสำหรับรัน Regression/Smoke ซ้ำได้ — Required/Optional ต่อ Case</p></div>{canEdit && <button className="btn primary" type="button" onClick={() => setCreateModal(true)}><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Suite</button>}</header>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    <div className="filter-toolbar">
      <div className="filter-toolbar-top"><div className="result-count"><strong>{suites.length.toLocaleString()}</strong><span>Automation Suite</span></div></div>
      <div className="filter-toolbar-row automation-case-toolbar">
        <input type="text" aria-label="ค้นหา Suite" placeholder="ค้นหา Suite..." value={search} onChange={(e) => setSearch(e.target.value)} />
        <select aria-label="กรองสถานะ Suite" value={activeFilter} onChange={(e) => setActiveFilter(e.target.value as "all" | "active" | "closed")}>
          <option value="active">เปิดใช้งาน</option>
          <option value="closed">ปิดแล้ว</option>
          <option value="all">ทั้งหมด</option>
        </select>
      </div>
    </div>
    {suites.length ? <div className="table-wrap"><table><thead><tr><th>Code</th><th>ชื่อ</th><th>Case</th><th>สถานะ</th><th>สร้างเมื่อ</th><th></th></tr></thead><tbody>{suites.map((s) => <tr key={s.automationSuiteId}>
      <td><b>{s.suiteCode}</b></td>
      <td><span>{s.suiteName}</span>{s.description && <small>{s.description}</small>}</td>
      <td>{s.readyCaseCount}/{s.caseCount} Ready</td>
      <td><Badge tone={s.isActive ? "green" : "gray"}>{s.isActive ? "เปิดใช้งาน" : "ปิดแล้ว"}</Badge></td>
      <td>{formatThaiDateTime(s.createdAt)}</td>
      <td>{canRun && s.isActive && <button type="button" className="table-action" onClick={() => setRunSuiteFor(s)}><span className="material-symbols-outlined" aria-hidden="true">play_arrow</span> รัน</button>}<button type="button" className="table-action" onClick={() => openDetail(s)}><span className="material-symbols-outlined" aria-hidden="true">info</span> รายละเอียด</button><button type="button" className="table-action" onClick={() => openHistory(s.automationSuiteId)}><span aria-hidden="true">↺</span> ประวัติ</button>{canEdit && s.isActive && <button type="button" className="table-action" onClick={() => setEditSuite(s)}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข</button>}{canEdit && <button type="button" className={`table-action${s.isActive ? " danger" : ""}`} onClick={() => toggleSuite(s)}><span className="material-symbols-outlined" aria-hidden="true">power_settings_new</span> {s.isActive ? "ปิด" : "เปิด"}</button>}</td>
    </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Automation Suite</p><small>สร้าง Suite เพื่อรวม Automation Case ที่ต้องรันซ้ำเป็นชุด (Smoke/Regression)</small></div>}

    {detail && <ModalShell labelledBy="automation-suite-detail-title" onDismiss={() => setDetail(null)}>
      <div className="modal-head"><div><h2 id="automation-suite-detail-title">{detail.suiteCode} · {detail.suiteName}</h2><small>{detail.cases.length} case · Rev {detail.revisionNo} · <Badge tone={detail.isActive ? "green" : "gray"}>{detail.isActive ? "เปิดใช้งาน" : "ปิดแล้ว"}</Badge></small></div><button aria-label="ปิด" onClick={() => setDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {!detail.isActive && <div className="inline-alert"><span>Suite นี้ปิดแล้ว — ต้องเปิดใช้งานก่อนจึงจะแก้ไข Case ได้</span></div>}
      {detail.isActive && (canEdit || canRun) && <div className="acw-action-bar">
        {canRun && <button type="button" className="btn primary" onClick={() => setRunSuiteFor({ automationSuiteId: detail.automationSuiteId, suiteCode: detail.suiteCode, caseCount: detail.cases.length, readyCaseCount: detail.cases.filter((c) => c.status === "Ready").length })}><span className="material-symbols-outlined" aria-hidden="true">play_arrow</span> รัน Suite</button>}
        {canEdit && <button type="button" className="btn" onClick={() => setAddCasesModal(true)}><span className="material-symbols-outlined" aria-hidden="true">add</span> เพิ่ม Case</button>}
        <button type="button" className="btn" onClick={() => openHistory(detail.automationSuiteId)}>🕐 ประวัติ</button>
      </div>}
      {detail.cases.length ? <div className="table-wrap"><table><thead><tr><th>ลำดับ</th><th>Code</th><th>Test Case</th><th>Target</th><th>สถานะ</th><th>Required</th><th></th></tr></thead><tbody>{[...detail.cases].sort((a, b) => a.sortOrder - b.sortOrder).map((c, i, arr) => <tr key={c.automationCaseId}>
        <td>{canEdit && detail.isActive ? <span className="automation-sort-controls"><button type="button" className="table-action icon-btn" aria-label="เลื่อนขึ้น" disabled={i === 0} onClick={() => moveCase(c, -1)}>↑</button><button type="button" className="table-action icon-btn" aria-label="เลื่อนลง" disabled={i === arr.length - 1} onClick={() => moveCase(c, 1)}>↓</button></span> : c.sortOrder}</td>
        <td><b>{c.automationCode}</b></td>
        <td><span>{c.testCaseCode}</span><small>{c.testCaseTitle}</small></td>
        <td><Badge tone={targetTone[c.automationType] ?? "blue"}>{c.automationType}</Badge></td>
        <td><Badge tone={caseStatusTone[c.status] ?? "blue"}>{c.status}</Badge></td>
        <td>{canEdit && detail.isActive ? <button type="button" className="table-action" onClick={() => toggleRequired(c)}>{c.isRequired ? "Required" : "Optional"}</button> : <Badge tone={c.isRequired ? "blue" : "gray"}>{c.isRequired ? "Required" : "Optional"}</Badge>}</td>
        <td>{canEdit && detail.isActive && <button type="button" className="table-action danger" onClick={() => removeCase(c.automationCaseId)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ลบ</button>}</td>
      </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Case ใน Suite นี้</p></div>}
      <div className="modal-actions"><button className="btn" onClick={() => setDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิดหน้าต่าง</button></div>
    </ModalShell>}

    {addCasesModal && detail && <AddSuiteCasesModal cases={cases} existingCaseIds={detail.cases.map((c) => c.automationCaseId)} busy={busy} onClose={() => setAddCasesModal(false)} onAdd={addCases} />}
    {createModal && <SuiteFormModal title="สร้าง Automation Suite" busy={busy} onClose={() => setCreateModal(false)} onSave={createSuite} />}
    {editSuite && <SuiteFormModal title={`แก้ไข ${editSuite.suiteCode}`} initialName={editSuite.suiteName} initialDescription={editSuite.description ?? ""} busy={busy} onClose={() => setEditSuite(null)} onSave={(_, name, desc, reason) => updateSuite(editSuite.automationSuiteId, name, desc, reason ?? "")} />}
    {history && <ModalShell labelledBy="automation-suite-history-title" onDismiss={() => setHistory(null)}>
      <div className="modal-head"><div><h2 id="automation-suite-history-title">ประวัติการแก้ไข (AUT-P1-003)</h2><small>{history.length} รายการ — ล่าสุดก่อน</small></div><button aria-label="ปิด" onClick={() => setHistory(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {history.length ? <div className="automation-result-list">{history.map((h) => <div key={h.automationSuiteRevisionId} className="automation-failure-row">
        <b>Rev {h.revisionNo} · {h.changeType}</b>
        <span>{h.detail}</span>
        {h.changeReason && <span>เหตุผล: {h.changeReason}</span>}
        <span>{h.changedByName ?? (h.changedBy ? h.changedBy : "ระบบ")} · {formatThaiDateTime(h.changedAt)}</span>
      </div>)}</div> : <div className="empty"><p>ยังไม่มีประวัติ</p></div>}
      <div className="modal-actions"><button className="btn" onClick={() => setHistory(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิดหน้าต่าง</button></div>
    </ModalShell>}

    {runSuiteFor && <RunSuiteModal suite={runSuiteFor} releaseId={releaseId} canRun={canRun} busy={busy} onClose={() => setRunSuiteFor(null)} onRun={(buildId, envId, priority) => runSuite(runSuiteFor.automationSuiteId, runSuiteFor.suiteCode, buildId, envId, priority)} onError={setError} />}

    {runResult && <ModalShell labelledBy="automation-suite-run-result-title" onDismiss={() => setRunResult(null)}>
      <div className="modal-head"><div><h2 id="automation-suite-run-result-title">สั่งรัน {runResult.suiteCode} แล้ว</h2></div><button aria-label="ปิด" onClick={() => setRunResult(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      <p>สร้าง Execution {runResult.created} รายการ</p>
      {runResult.skipped.length > 0 && <p>ข้าม {runResult.skipped.length} รายการ (ไม่ Ready หรือ Quarantined): {runResult.skipped.join(", ")}</p>}
      <div className="modal-actions"><button className="btn primary" onClick={() => setRunResult(null)}><span className="material-symbols-outlined" aria-hidden="true">check</span> ตกลง</button></div>
    </ModalShell>}
  </section>;
}

function SuiteFormModal({ title, initialCode = "", initialName = "", initialDescription = "", busy, onClose, onSave }: {
  title: string; initialCode?: string; initialName?: string; initialDescription?: string; busy: boolean; onClose: () => void; onSave: (code: string, name: string, description: string, changeReason?: string) => void;
}) {
  const [suiteCode, setSuiteCode] = useState(initialCode);
  const [suiteName, setSuiteName] = useState(initialName);
  const [description, setDescription] = useState(initialDescription);
  const [changeReason, setChangeReason] = useState("");
  const isEdit = initialName !== "";
  return <ModalShell labelledBy="automation-suite-form-title" onDismiss={() => { if (!busy) onClose(); }} form>
    <div className="modal-head"><div><h2 id="automation-suite-form-title">{title}</h2></div><button aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    <div className="form-grid">
      {!isEdit && <label>รหัส Suite (ไม่บังคับ — เว้นว่างให้ระบบสร้างให้)<input type="text" value={suiteCode} onChange={(e) => setSuiteCode(e.target.value)} placeholder="เช่น AUT-AS-SMOKE" /></label>}
      <label className="full">ชื่อ Suite<input type="text" value={suiteName} onChange={(e) => setSuiteName(e.target.value)} /></label>
      <label className="full">คำอธิบาย (ไม่บังคับ)<textarea rows={3} value={description} onChange={(e) => setDescription(e.target.value)} /></label>
      {isEdit && <label className="full">เหตุผลที่แก้ไข (ไม่บังคับ — บันทึกลงประวัติ)<input type="text" value={changeReason} onChange={(e) => setChangeReason(e.target.value)} placeholder="เช่น ปรับให้ตรงชื่อ Release ใหม่" /></label>}
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={busy || !suiteName.trim()} onClick={() => onSave(suiteCode, suiteName, description, changeReason)}>{busy ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}</button></div>
  </ModalShell>;
}

function RunSuiteModal({ suite, releaseId, canRun, busy, onClose, onRun }: {
  suite: { automationSuiteId: string; suiteCode: string; caseCount: number; readyCaseCount: number }; releaseId?: string; canRun: boolean; busy: boolean; onClose: () => void;
  onRun: (buildId: string, environmentId: string, priority: number) => void; onError: (e: string) => void;
}) {
  const [buildId, setBuildId] = useState("");
  const [envId, setEnvId] = useState("");
  const [priority, setPriority] = useState(5);
  const { builds, environments, loadError } = useBuildsAndEnvironments(releaseId);

  return <ModalShell labelledBy="automation-suite-run-title" onDismiss={() => { if (!busy) onClose(); }} form>
    <div className="modal-head"><div><h2 id="automation-suite-run-title">รัน {suite.suiteCode}</h2><small>{suite.readyCaseCount}/{suite.caseCount} case Ready — ไม่ต้องเลือก Case ใหม่ ใช้ชุดเดิมของ Suite</small></div><button aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    {loadError && <div className="inline-alert error" role="alert"><span>{loadError}</span></div>}
    <div className="form-grid">
      <label>Build<select value={buildId} onChange={(e) => setBuildId(e.target.value)}><option value="">เลือก Build</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}{b.applicationVersion ? ` · App ${b.applicationVersion}` : ""}</option>)}</select></label>
      <label>Environment<select value={envId} onChange={(e) => setEnvId(e.target.value)}><option value="">เลือก Env</option>{environments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>
      <label>Priority<select value={priority} onChange={(e) => setPriority(Number(e.target.value))}>{[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((p) => <option key={p} value={p}>{p}</option>)}</select></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={!canRun || busy || !buildId || !envId || suite.readyCaseCount === 0} onClick={() => onRun(buildId, envId, priority)}>{busy ? "กำลังส่ง..." : `▶ รัน ${suite.readyCaseCount} case`}</button></div>
  </ModalShell>;
}

function AddSuiteCasesModal({ cases, existingCaseIds, busy, onClose, onAdd }: {
  cases: AutomationCaseItem[]; existingCaseIds: string[]; busy: boolean; onClose: () => void; onAdd: (caseIds: string[], isRequired: boolean, changeReason: string) => void;
}) {
  const available = cases.filter((c) => !existingCaseIds.includes(c.automationCaseId));
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [isRequired, setIsRequired] = useState(true);
  const [changeReason, setChangeReason] = useState("");
  const toggle = (id: string) => setSelected((prev) => { const next = new Set(prev); if (next.has(id)) next.delete(id); else next.add(id); return next; });

  return <ModalShell labelledBy="automation-suite-add-cases-title" onDismiss={() => { if (!busy) onClose(); }} form>
    <div className="modal-head"><div><h2 id="automation-suite-add-cases-title">เพิ่ม Automation Case เข้า Suite</h2><small>เลือก Case ที่ยังไม่อยู่ใน Suite นี้</small></div><button aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    <label className="checkbox-field"><input type="checkbox" checked={isRequired} onChange={(e) => setIsRequired(e.target.checked)} /> ตั้งเป็น Required (ต้องผ่านทุกตัว)</label>
    <label>เหตุผล (ไม่บังคับ — บันทึกลงประวัติ)<input type="text" value={changeReason} onChange={(e) => setChangeReason(e.target.value)} placeholder="เช่น เพิ่ม case สำหรับ regression รอบนี้" /></label>
    {available.length ? <div className="automation-batch-list">
      {available.map((c) => <label key={c.automationCaseId} className="automation-batch-row"><input type="checkbox" aria-label={`เลือก ${c.automationCode}`} checked={selected.has(c.automationCaseId)} onChange={() => toggle(c.automationCaseId)} /><span><b>{c.automationCode}</b><small>{c.testCaseCode} · {c.testCaseTitle}</small></span><Badge tone={caseStatusTone[c.status] ?? "blue"}>{c.status}</Badge></label>)}
    </div> : <div className="empty"><p>ทุก Automation Case ถูกเพิ่มเข้า Suite นี้หมดแล้ว</p></div>}
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={busy || !selected.size} onClick={() => onAdd([...selected], isRequired, changeReason)}>{busy ? "กำลังเพิ่ม..." : `เพิ่ม ${selected.size} case`}</button></div>
  </ModalShell>;
}
