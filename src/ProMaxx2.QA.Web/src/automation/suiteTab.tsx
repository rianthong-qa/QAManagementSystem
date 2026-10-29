import { useEffect, useState } from "react";
import { confirmDialog, notify } from "../components/dialogStore";
import { formatThaiDateTime } from "../dateTime";
import { apiUrl } from "../api";
import {
  automationCaseTone as caseStatusTone,
} from "../automationUtils";
import type { AutomationCaseItem, AutomationSuiteCaseItem, AutomationSuiteListItem, AutomationSuiteDetailItem, AutomationSuiteRevisionItem } from "./types";
import { fetchJson, isAbort, useBuildsAndEnvironments, useDebounced, caseStatusTh } from "./shared";
import { ModalShell, Badge } from "./ui";

export function AutomationSuiteTab({ projectId, releaseId, headers, canEdit, canRun, cases }: {
  projectId: string; releaseId?: string; headers: Record<string, string>; canEdit: boolean; canRun: boolean; cases: AutomationCaseItem[];
}) {
  const [suites, setSuites] = useState<AutomationSuiteListItem[]>([]);
  const [searchInput, setSearchInput] = useState("");
  const search = useDebounced(searchInput.trim());
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

  const toggleSuite = async (row: { automationSuiteId: string; suiteCode: string; isActive: boolean }) => {
    const closing = row.isActive;
    if (!await confirmDialog({
      title: closing ? "ปิด Suite" : "เปิดใช้งาน Suite",
      message: closing ? `ปิด Suite "${row.suiteCode}" ใช่หรือไม่?
Suite ที่ปิดแล้วจะรันและแก้ไข Case ไม่ได้ (เปิดใช้งานอีกครั้งได้ภายหลัง) และจะย้ายไปอยู่ในตัวกรอง "ปิดแล้ว"` : `เปิดใช้งาน Suite "${row.suiteCode}" อีกครั้ง?`,
      confirmLabel: closing ? "ปิด Suite" : "เปิดใช้งาน",
      tone: closing ? "danger" : undefined,
    })) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${row.automationSuiteId}/${closing ? "close" : "reopen"}?projectId=${projectId}`, { method: "POST", headers, body: "{}" });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? (r.status === 403 ? "ไม่มีสิทธิ์เปลี่ยนสถานะ Suite (ต้องมีสิทธิ์ AUTOMATION.EDIT)" : `เปลี่ยนสถานะ Suite ไม่สำเร็จ (HTTP ${r.status})`));
      }
      setReload((v) => v + 1);
      if (detail?.automationSuiteId === row.automationSuiteId) await refreshDetail(row.automationSuiteId);
      notify(closing ? `ปิด Suite ${row.suiteCode} แล้ว — ดูได้ที่ตัวกรอง "ปิดแล้ว"` : `เปิดใช้งาน Suite ${row.suiteCode} แล้ว`, "success");
    } catch (e) {
      // แจ้งเป็น toast ด้วย — ข้อความ error ด้านบนรายการอาจถูกหน้าต่างรายละเอียดบังหรือเลื่อนพ้นจอ
      const msg = e instanceof Error ? e.message : "เปลี่ยนสถานะ Suite ไม่สำเร็จ";
      setError(msg);
      notify(msg, "error");
    }
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

  const sortedDetailCases = detail ? [...detail.cases].sort((a, b) => a.sortOrder - b.sortOrder) : [];
  const detailReady = sortedDetailCases.filter((c) => c.status === "Ready").length;
  const detailRequired = sortedDetailCases.filter((c) => c.isRequired).length;
  const detailEditable = !!detail?.isActive && canEdit;
  const filterChips: { id: "active" | "closed" | "all"; label: string }[] = [{ id: "active", label: "เปิดใช้งาน" }, { id: "closed", label: "ปิดแล้ว" }, { id: "all", label: "ทั้งหมด" }];

  return <section className="au-runs" aria-label="Automation Suite">
    {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
    <article className="au-runs-card">
      <header className="au-runs-head">
        <h2><strong>{suites.length.toLocaleString()}</strong> Suite</h2>
        <label className="au-runs-search">
          <span className="material-symbols-outlined" aria-hidden="true">search</span>
          <input type="search" aria-label="ค้นหา Suite" placeholder="ค้นหารหัส / ชื่อ Suite" value={searchInput} onChange={(e) => setSearchInput(e.target.value)} />
        </label>
        {canEdit && <button className="btn primary au-runs-more" type="button" onClick={() => setCreateModal(true)}><span className="material-symbols-outlined" aria-hidden="true">add</span>สร้าง Suite</button>}
      </header>
      <div className="au-runs-chips" role="group" aria-label="กรองสถานะ Suite">
        {filterChips.map((c) => <button key={c.id} type="button" aria-pressed={activeFilter === c.id} className={activeFilter === c.id ? "active" : ""} onClick={() => setActiveFilter(c.id)}>{c.label}{activeFilter === c.id && <b>{suites.length}</b>}</button>)}
      </div>

      {suites.length ? <div className="table-wrap au-runs-wrap"><table className="au-suites-table table-cards">
        <thead><tr><th>Suite</th><th>ความพร้อม</th><th>สถานะ</th><th>สร้างเมื่อ</th><th className="actions-col"><span className="sr-only">จัดการ</span></th></tr></thead>
        <tbody>{suites.map((s) => { const pct = s.caseCount ? Math.round((s.readyCaseCount / s.caseCount) * 100) : 0; return <tr key={s.automationSuiteId} className={`au-runs-tr${s.isActive ? "" : " is-closed"}`} onClick={() => openDetail(s)}>
          <td data-label="Suite"><b>{s.suiteCode}</b><span title={s.description ?? s.suiteName}>{s.suiteName}{s.description && <small> · {s.description}</small>}</span></td>
          <td data-label="ความพร้อม"><div className="au-ready" title={`พร้อมรัน ${s.readyCaseCount} จาก ${s.caseCount} Case`}><span className="au-ready-bar" role="img" aria-label={`พร้อมรัน ${s.readyCaseCount} จาก ${s.caseCount}`}><i style={{ width: `${pct}%` }} /></span><span className="au-ready-text">{s.readyCaseCount}/{s.caseCount} พร้อมรัน</span></div></td>
          <td data-label="สถานะ"><Badge tone={s.isActive ? "green" : "gray"}>{s.isActive ? "เปิดใช้งาน" : "ปิดแล้ว"}</Badge></td>
          <td data-label="สร้างเมื่อ" className="au-runs-time">{formatThaiDateTime(s.createdAt)}</td>
          <td className="actions-col" onClick={(e) => e.stopPropagation()}><div className="au-runs-actions">
            {canRun && s.isActive && <button type="button" className="btn au-run-btn" disabled={!s.readyCaseCount} title={s.readyCaseCount ? `รัน ${s.readyCaseCount} Case ที่พร้อม` : "ยังไม่มี Case ที่พร้อมรัน"} onClick={() => setRunSuiteFor(s)}><span className="material-symbols-outlined" aria-hidden="true">play_arrow</span>รัน</button>}
            {canEdit && s.isActive && <button type="button" className="au-icon-btn" title="แก้ไขชื่อ / คำอธิบาย" aria-label={`แก้ไข ${s.suiteCode}`} onClick={() => setEditSuite(s)}><span className="material-symbols-outlined" aria-hidden="true">edit</span></button>}
            <button type="button" className="au-icon-btn" title="ประวัติการแก้ไข" aria-label={`ประวัติ ${s.suiteCode}`} onClick={() => openHistory(s.automationSuiteId)}><span className="material-symbols-outlined" aria-hidden="true">history</span></button>
            {canEdit && <button type="button" className={`au-icon-btn${s.isActive ? " danger" : ""}`} title={s.isActive ? "ปิด Suite" : "เปิดใช้งานอีกครั้ง"} aria-label={`${s.isActive ? "ปิด" : "เปิด"} ${s.suiteCode}`} onClick={() => toggleSuite(s)}><span className="material-symbols-outlined" aria-hidden="true">{s.isActive ? "block" : "restart_alt"}</span></button>}
          </div></td>
        </tr>; })}</tbody>
      </table></div> : <div className="au-runs-empty">{search ? "ไม่พบ Suite ที่ตรงคำค้นหา" : activeFilter === "closed" ? "ไม่มี Suite ที่ปิดแล้ว" : "ยังไม่มี Suite — สร้าง Suite เพื่อรวม Automation Case ที่ต้องรันซ้ำเป็นชุด เช่น Smoke / Regression"}</div>}
    </article>

    {detail && <ModalShell labelledBy="automation-suite-detail-title" className="au-suite-modal" onDismiss={() => setDetail(null)}>
      <div className="modal-head au-suite-head">
        <div>
          <span className="au-eyebrow">AUTOMATION SUITE · Rev {detail.revisionNo}</span>
          <h2 id="automation-suite-detail-title">{detail.suiteCode} · {detail.suiteName}</h2>
          {detail.description && <p>{detail.description}</p>}
        </div>
        <button aria-label="ปิด" onClick={() => setDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>
      <div className="au-suite-bar">
        <Badge tone={detail.isActive ? "green" : "gray"}>{detail.isActive ? "เปิดใช้งาน" : "ปิดแล้ว"}</Badge>
        <span><b>{sortedDetailCases.length}</b> Case</span>
        <span><b>{detailReady}</b> พร้อมรัน</span>
        <span><b>{detailRequired}</b> Required</span>
        <span className="au-chips-spacer" />
        <button type="button" className="au-icon-btn" title="ประวัติการแก้ไข" aria-label="ประวัติการแก้ไข" onClick={() => openHistory(detail.automationSuiteId)}><span className="material-symbols-outlined" aria-hidden="true">history</span></button>
        {canEdit && <button type="button" className={`btn au-runs-more${detail.isActive ? " au-btn-danger" : ""}`} onClick={() => toggleSuite(detail)}><span className="material-symbols-outlined" aria-hidden="true">{detail.isActive ? "block" : "restart_alt"}</span>{detail.isActive ? "ปิด Suite" : "เปิดใช้งาน"}</button>}
        {detailEditable && <button type="button" className="btn au-runs-more" onClick={() => setAddCasesModal(true)}><span className="material-symbols-outlined" aria-hidden="true">add</span>เพิ่ม Case</button>}
        {detail.isActive && canRun && <button type="button" className="btn primary au-runs-more" disabled={!detailReady} onClick={() => setRunSuiteFor({ automationSuiteId: detail.automationSuiteId, suiteCode: detail.suiteCode, caseCount: detail.cases.length, readyCaseCount: detailReady })}><span className="material-symbols-outlined" aria-hidden="true">play_arrow</span>รัน {detailReady} Case</button>}
      </div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      {!detail.isActive && <div className="inline-alert"><span>Suite นี้ปิดแล้ว — กด "เปิดใช้งาน" ก่อนจึงจะแก้ไขหรือรันได้</span></div>}
      {sortedDetailCases.length ? <ol className="au-suite-cases">{sortedDetailCases.map((c, i) => <li key={c.automationCaseId} className={c.status === "Ready" ? "" : "is-notready"}>
        <span className="au-suite-no">{i + 1}</span>
        <span className="au-suite-main"><b>{c.automationCode}</b><small title={`${c.testCaseCode} · ${c.testCaseTitle}`}>{c.testCaseCode} · {c.testCaseTitle}</small></span>
        <span className="au-suite-target">{c.automationType}</span>
        <Badge tone={caseStatusTone[c.status] ?? "blue"}>{caseStatusTh[c.status] ?? c.status}</Badge>
        {detailEditable ? <button type="button" className={`au-req${c.isRequired ? " is-on" : ""}`} aria-pressed={c.isRequired} title="กดเพื่อสลับ Required / Optional" onClick={() => toggleRequired(c)}>{c.isRequired ? "Required" : "Optional"}</button> : <span className={`au-req is-static${c.isRequired ? " is-on" : ""}`}>{c.isRequired ? "Required" : "Optional"}</span>}
        {detailEditable && <span className="au-suite-tools">
          <button type="button" className="au-icon-btn" aria-label={`เลื่อน ${c.automationCode} ขึ้น`} title="เลื่อนขึ้น" disabled={i === 0} onClick={() => moveCase(c, -1)}><span className="material-symbols-outlined" aria-hidden="true">arrow_upward</span></button>
          <button type="button" className="au-icon-btn" aria-label={`เลื่อน ${c.automationCode} ลง`} title="เลื่อนลง" disabled={i === sortedDetailCases.length - 1} onClick={() => moveCase(c, 1)}><span className="material-symbols-outlined" aria-hidden="true">arrow_downward</span></button>
          <button type="button" className="au-icon-btn danger" aria-label={`นำ ${c.automationCode} ออก`} title="นำออกจาก Suite" onClick={() => removeCase(c.automationCaseId)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
        </span>}
      </li>)}</ol> : <div className="au-runs-empty">ยังไม่มี Case ใน Suite นี้{detailEditable && " — กด \"เพิ่ม Case\""}</div>}
      <p className="au-suite-note">รัน Suite จะส่งเฉพาะ Case ที่พร้อมรัน (Ready และไม่ถูก Quarantine)</p>
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
      {available.map((c) => <label key={c.automationCaseId} className="automation-batch-row"><input type="checkbox" aria-label={`เลือก ${c.automationCode}`} checked={selected.has(c.automationCaseId)} onChange={() => toggle(c.automationCaseId)} /><span><b>{c.automationCode}</b><small>{c.testCaseCode} · {c.testCaseTitle}</small></span><Badge tone={caseStatusTone[c.status] ?? "blue"}>{caseStatusTh[c.status] ?? c.status}</Badge></label>)}
    </div> : <div className="empty"><p>ทุก Automation Case ถูกเพิ่มเข้า Suite นี้หมดแล้ว</p></div>}
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={busy || !selected.size} onClick={() => onAdd([...selected], isRequired, changeReason)}>{busy ? "กำลังเพิ่ม..." : `เพิ่ม ${selected.size} case`}</button></div>
  </ModalShell>;
}
