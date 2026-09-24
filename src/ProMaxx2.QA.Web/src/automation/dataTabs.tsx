import { useEffect, useState } from "react";
import { formatThaiDateTime } from "../dateTime";
import { apiUrl } from "../api";
import type { AutomationDbSnapshotItem, AutomationDbRestoreItem, AutomationDataSeedScriptListItem, AutomationDataSeedScriptDetailItem, AutomationDataSeedRunItem, AutomationEnvironmentDataProfileItem } from "./types";
import { fetchJson, isAbort, useBuildsAndEnvironments } from "./shared";
import { ModalShell, Badge } from "./ui";

const snapshotStatusTone: Record<string, string> = { Requested: "gray", Running: "blue", Succeeded: "green", Failed: "red" };

function formatBytes(bytes?: number): string {
  if (bytes === undefined || bytes === null) return "-";
  if (bytes < 1024) return `${bytes} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let value = bytes / 1024;
  let i = 0;
  while (value >= 1024 && i < units.length - 1) { value /= 1024; i++; }
  return `${value.toFixed(1)} ${units[i]}`;
}

export function AutomationDataSnapshotTab({ projectId, releaseId, headers, canRun }: {
  projectId: string; releaseId?: string; headers: Record<string, string>; canRun: boolean;
}) {
  const [snapshots, setSnapshots] = useState<AutomationDbSnapshotItem[]>([]);
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [requestModal, setRequestModal] = useState(false);
  const [detail, setDetail] = useState<AutomationDbSnapshotItem | null>(null);
  const [restoreHistory, setRestoreHistory] = useState<AutomationDbRestoreItem[]>([]);

  useEffect(() => {
    if (!projectId) return;
    const ctrl = new AbortController();
    setError("");
    fetchJson(`${apiUrl}/automation/data/snapshots?projectId=${projectId}`, headers, ctrl.signal).then((s) => setSnapshots(Array.isArray(s) ? s : [])).catch((e) => !isAbort(e) && setError("โหลด Snapshot ไม่สำเร็จ"));
    return () => ctrl.abort();
  }, [projectId, headers, reload]);

  const requestSnapshot = async (environmentId: string, buildId: string) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/snapshots?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ environmentId, buildId }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "ขอ Snapshot ไม่สำเร็จ"); }
      setRequestModal(false); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "ขอ Snapshot ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const openDetail = async (s: AutomationDbSnapshotItem) => {
    setDetail(s); setError(""); setRestoreHistory([]);
    try {
      const rows = await fetchJson(`${apiUrl}/automation/data/restores?projectId=${projectId}&automationDbSnapshotId=${s.automationDbSnapshotId}`, headers);
      setRestoreHistory(Array.isArray(rows) ? rows : []);
    } catch { setError("โหลดประวัติการ Restore ไม่สำเร็จ"); }
  };

  const requestRestore = async (s: AutomationDbSnapshotItem) => {
    if (!window.confirm(`ยืนยัน restore ฐานข้อมูลจริงของ "${s.environmentName}" กลับไปที่ snapshot นี้ (build ${s.buildNumber})?\n\n⚠ ข้อมูลปัจจุบันใน DB ของ Environment นี้จะถูกทับทั้งหมด — ย้อนกลับไม่ได้`)) return;
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/restores?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ automationDbSnapshotId: s.automationDbSnapshotId }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "ขอ Restore ไม่สำเร็จ"); }
      if (detail?.automationDbSnapshotId === s.automationDbSnapshotId) await openDetail(s);
    } catch (e) { setError(e instanceof Error ? e.message : "ขอ Restore ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  return <section className="automation-cases" aria-label="Automation DB Snapshot">
    <header className="automation-section-head"><div><h2>Database Snapshot &amp; Restore (AUT-DATA-001/002)</h2><p>ขอ backup ฐานข้อมูลจริงของ Environment ก่อนรัน และ restore กลับได้ภายหลัง — Windows Agent เป็นผู้ backup/restore จริง (gbak สำหรับ Firebird / BACKUP-RESTORE DATABASE สำหรับ SQL Server) ผ่านคำสั่ง <code>runner snapshot</code>/<code>runner restore</code> บนเครื่อง Agent</p></div>{canRun && <button className="btn primary" type="button" onClick={() => setRequestModal(true)}><span className="material-symbols-outlined" aria-hidden="true">add</span> ขอ Snapshot</button>}</header>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    {snapshots.length ? <div className="table-wrap"><table><thead><tr><th>Environment</th><th>Build</th><th>สถานะ</th><th>DB</th><th>Agent</th><th>ขนาด</th><th>ขอเมื่อ</th><th></th></tr></thead><tbody>{snapshots.map((s) => <tr key={s.automationDbSnapshotId}>
      <td>{s.environmentName}</td>
      <td>{s.buildNumber}</td>
      <td><Badge tone={snapshotStatusTone[s.status] ?? "blue"}>{s.status}</Badge></td>
      <td>{s.dbKind ?? "-"}</td>
      <td>{s.agentCode ?? "-"}</td>
      <td>{formatBytes(s.sizeBytes)}</td>
      <td>{formatThaiDateTime(s.requestedAt)}</td>
      <td><button type="button" className="table-action" onClick={() => openDetail(s)}><span className="material-symbols-outlined" aria-hidden="true">info</span> รายละเอียด</button>{canRun && s.status === "Succeeded" && <button type="button" className="table-action danger" onClick={() => requestRestore(s)}>↺ Restore</button>}</td>
    </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Snapshot</p><small>ขอ Snapshot ก่อนรันชุด Automation เพื่อให้เริ่มจาก data state ที่รู้จักได้แน่นอน และ restore ได้ภายหลัง (AUT-DATA-002)</small></div>}

    {requestModal && <SnapshotRequestModal projectId={projectId} releaseId={releaseId} headers={headers} busy={busy} onClose={() => setRequestModal(false)} onSave={requestSnapshot} />}

    {detail && <ModalShell labelledBy="automation-snapshot-detail-title" onDismiss={() => setDetail(null)}>
      <div className="modal-head"><div><h2 id="automation-snapshot-detail-title">Snapshot — {detail.environmentName} / {detail.buildNumber}</h2><small><Badge tone={snapshotStatusTone[detail.status] ?? "blue"}>{detail.status}</Badge></small></div><button aria-label="ปิด" onClick={() => setDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      <div className="automation-result-list">
        <div className="automation-failure-row"><b>ขอเมื่อ</b><span>{formatThaiDateTime(detail.requestedAt)}{detail.requestedBy ? ` · โดย ${detail.requestedBy}` : ""}</span></div>
        {detail.startedAt && <div className="automation-failure-row"><b>Agent เริ่ม backup</b><span>{formatThaiDateTime(detail.startedAt)}{detail.agentCode ? ` · ${detail.agentCode}` : ""}</span></div>}
        {detail.completedAt && <div className="automation-failure-row"><b>เสร็จสิ้น</b><span>{formatThaiDateTime(detail.completedAt)}</span></div>}
        {detail.status === "Succeeded" && <>
          <div className="automation-failure-row"><b>DB</b><span>{detail.dbKind} · {formatBytes(detail.sizeBytes)}</span></div>
          <div className="automation-failure-row"><b>ไฟล์ (บนเครื่อง Agent)</b><span><code>{detail.snapshotPath}</code></span></div>
          <div className="automation-failure-row"><b>Checksum (SHA-256)</b><span><code>{detail.checksum}</code></span></div>
        </>}
        {detail.status === "Failed" && <div className="automation-failure-row"><b>Error</b><span>{detail.errorMessage}</span></div>}
      </div>

      <h3>ประวัติการ Restore</h3>
      {restoreHistory.length ? <div className="automation-result-list">{restoreHistory.map((r) => <div key={r.automationDbRestoreId} className="automation-failure-row">
        <b><Badge tone={snapshotStatusTone[r.status] ?? "blue"}>{r.status}</Badge> {formatThaiDateTime(r.requestedAt)}{r.agentCode ? ` · ${r.agentCode}` : ""}</b>
        <span>Checksum: <Badge tone={r.checksumVerified ? "green" : "gray"}>{r.checksumVerified ? "ตรวจแล้ว" : "-"}</Badge> · ความพร้อมใช้งาน: <Badge tone={r.availabilityVerified ? "green" : "gray"}>{r.availabilityVerified ? "ตรวจแล้ว" : "-"}</Badge></span>
        {r.errorMessage && <span>{r.errorMessage}</span>}
      </div>)}</div> : <div className="empty"><p>ยังไม่เคย Restore จาก Snapshot นี้</p></div>}
      {canRun && detail.status === "Succeeded" && <div className="modal-actions" style={{ justifyContent: "flex-start" }}><button className="btn danger" disabled={busy} type="button" onClick={() => requestRestore(detail)}>↺ ขอ Restore จาก Snapshot นี้</button></div>}

      <div className="modal-actions"><button className="btn" onClick={() => setDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิดหน้าต่าง</button></div>
    </ModalShell>}
  </section>;
}

function SnapshotRequestModal({ releaseId, busy, onClose, onSave, title = "ขอ Database Snapshot", subtitle = "Windows Agent จะ backup ฐานข้อมูลจริงและรายงานผลกลับมาที่นี่", submitLabel = "ขอ Snapshot", warning, confirmMessage }: {
  projectId: string; releaseId?: string; headers: Record<string, string>; busy: boolean; onClose: () => void; onSave: (environmentId: string, buildId: string) => void;
  title?: string; subtitle?: string; submitLabel?: string; warning?: string; confirmMessage?: (environmentName: string, buildNumber: string) => string;
}) {
  const { builds, environments, loadError } = useBuildsAndEnvironments(releaseId);
  const [environmentId, setEnvironmentId] = useState("");
  const [buildId, setBuildId] = useState("");

  return <ModalShell labelledBy="automation-snapshot-request-title" onDismiss={() => { if (!busy) onClose(); }} form>
    <div className="modal-head"><div><h2 id="automation-snapshot-request-title">{title}</h2><small>{subtitle}</small></div><button aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    {warning && <div className="inline-alert" role="note">⚠ {warning}</div>}
    {loadError && <div className="inline-alert error" role="alert"><span>{loadError}</span></div>}
    {!releaseId && <p className="muted-text">เลือก Release ที่ Topbar ก่อนเพื่อแสดงรายการ Build</p>}
    <div className="form-grid">
      <label>Environment<select value={environmentId} onChange={(e) => setEnvironmentId(e.target.value)}><option value="">เลือก Environment</option>{environments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>
      <label>Build<select value={buildId} onChange={(e) => setBuildId(e.target.value)}><option value="">เลือก Build</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}{b.applicationVersion ? ` · App ${b.applicationVersion}` : ""}</option>)}</select></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={busy || !environmentId || !buildId} onClick={() => {
      const envName = environments.find((x) => x.testEnvironmentId === environmentId)?.environmentName ?? "";
      const buildNo = builds.find((x) => x.buildId === buildId)?.buildNumber ?? "";
      if (confirmMessage && !window.confirm(confirmMessage(envName, buildNo))) return;
      onSave(environmentId, buildId);
    }}>{busy ? "กำลังส่งคำขอ..." : submitLabel}</button></div>
  </ModalShell>;
}

const scriptTypeTone: Record<string, string> = { Seed: "blue", Cleanup: "orange", MasterData: "purple" };

const approvalStatusTone: Record<string, string> = { Pending: "gray", Approved: "green", Rejected: "red" };

export function AutomationDataSeedTab({ projectId, releaseId, headers, canEdit, canRun }: {
  projectId: string; releaseId?: string; headers: Record<string, string>; canEdit: boolean; canRun: boolean;
}) {
  const [scripts, setScripts] = useState<AutomationDataSeedScriptListItem[]>([]);
  const [typeFilter, setTypeFilter] = useState<"all" | "Seed" | "Cleanup" | "MasterData">("all");
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [createModal, setCreateModal] = useState(false);
  const [editScript, setEditScript] = useState<AutomationDataSeedScriptDetailItem | null>(null);
  const [runModal, setRunModal] = useState<AutomationDataSeedScriptListItem | null>(null);
  const [runHistory, setRunHistory] = useState<{ name: string; runs: AutomationDataSeedRunItem[] } | null>(null);
  const [notice, setNotice] = useState("");

  useEffect(() => {
    if (!projectId) return;
    const qs = new URLSearchParams({ projectId });
    if (typeFilter !== "all") qs.set("scriptType", typeFilter);
    const ctrl = new AbortController();
    setError("");
    fetchJson(`${apiUrl}/automation/data/seed-scripts?${qs}`, headers, ctrl.signal).then((s) => setScripts(Array.isArray(s) ? s : [])).catch((e) => !isAbort(e) && setError("โหลด Script ไม่สำเร็จ"));
    return () => ctrl.abort();
  }, [projectId, typeFilter, headers, reload]);

  const openEdit = async (id: string) => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-scripts/${id}?projectId=${projectId}`, { headers });
      if (!r.ok) throw new Error("โหลด Seed Script ไม่สำเร็จ");
      setEditScript(await r.json());
    } catch (e) { setError(e instanceof Error ? e.message : "โหลด Seed Script ไม่สำเร็จ"); }
  };

  const createScript = async (body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-scripts?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Seed Script ไม่สำเร็จ"); }
      setCreateModal(false); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "สร้าง Seed Script ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const updateScript = async (id: string, body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-scripts/${id}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "แก้ไข Seed Script ไม่สำเร็จ"); }
      setEditScript(null); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "แก้ไข Seed Script ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const toggleActive = async (row: AutomationDataSeedScriptListItem) => {
    if (!window.confirm(`${row.isActive ? "ปิด" : "เปิด"}ใช้งาน Seed Script "${row.name}"?`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-scripts/${row.automationDataSeedScriptId}/${row.isActive ? "deactivate" : "activate"}?projectId=${projectId}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เปลี่ยนสถานะไม่สำเร็จ"); }
      setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "เปลี่ยนสถานะไม่สำเร็จ"); }
  };

  const requestRun = async (environmentId: string, buildId: string) => {
    if (!runModal) return;
    const script = runModal;
    setBusy(true); setError(""); setNotice("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-runs?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ automationDataSeedScriptId: runModal.automationDataSeedScriptId, environmentId, buildId }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สั่งรัน Seed Script ไม่สำเร็จ"); }
      setRunModal(null);
      setNotice(`ส่ง "${script.name}" เข้าคิวแล้ว — Agent จะรับไปรันและรายงานผลใน "ประวัติการรัน"`);
    } catch (e) { setError(e instanceof Error ? e.message : "สั่งรัน Seed Script ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const openRunHistory = async (row: AutomationDataSeedScriptListItem) => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-runs?projectId=${projectId}&automationDataSeedScriptId=${row.automationDataSeedScriptId}`, { headers });
      if (!r.ok) throw new Error("โหลดประวัติการรันไม่สำเร็จ");
      setRunHistory({ name: row.name, runs: await r.json() });
    } catch (e) { setError(e instanceof Error ? e.message : "โหลดประวัติการรันไม่สำเร็จ"); }
  };

  const approveScript = async (row: AutomationDataSeedScriptListItem) => {
    if (!window.confirm(`อนุมัติ Master Data Script "${row.name}"? หลังอนุมัติจึงจะสั่งรันได้`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-scripts/${row.automationDataSeedScriptId}/approve?projectId=${projectId}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "อนุมัติไม่สำเร็จ"); }
      setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "อนุมัติไม่สำเร็จ"); }
  };

  const rejectScript = async (row: AutomationDataSeedScriptListItem) => {
    const reason = window.prompt(`เหตุผลที่ไม่อนุมัติ "${row.name}" (ไม่บังคับ):`);
    if (reason === null) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-scripts/${row.automationDataSeedScriptId}/reject?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ reason: reason.trim() || null }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "ไม่อนุมัติไม่สำเร็จ"); }
      setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "ไม่อนุมัติไม่สำเร็จ"); }
  };

  return <section className="automation-cases" aria-label="Automation Seed Cleanup Data">
    <header className="automation-section-head"><div><h2>Seed, Cleanup &amp; Master Data (AUT-DATA-003/004/005)</h2><p>เก็บ SQL script สำหรับ seed ข้อมูลพื้นฐาน (เช่นสินค้า/ราคา/โปรโมชั่น) ก่อนรัน, cleanup ข้อมูลที่ทิ้งไว้หลังรัน, และเตรียม Master Data (สินค้า/ราคา/โปรโมชั่น) ก่อน POS scenario แบบ repeatable/idempotent — Windows Agent เป็นผู้รัน SQL จริงผ่านคำสั่ง <code>runner seed</code> โดยไม่มี credential ของ DB เก็บอยู่ในนี้เลย; ถ้า Agent ที่รับงานหายไประหว่างรัน ระบบจะดึงงานกลับมาให้ Agent อื่นรับต่อได้อัตโนมัติหลัง 30 นาที (AUT-DATA-004) — Script ประเภท "Master Data" ต้องผ่านการอนุมัติก่อนจึงจะสั่งรันได้ (AUT-DATA-005)</p></div>{canEdit && <button className="btn primary" type="button" onClick={() => setCreateModal(true)}><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Script</button>}</header>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    {notice && <div className="inline-alert success" role="status"><span>{notice}</span><button type="button" aria-label="ปิดข้อความ" onClick={() => setNotice("")}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>}
    <div className="automation-case-toolbar">
      <select aria-label="กรองประเภท Script" value={typeFilter} onChange={(e) => setTypeFilter(e.target.value as "all" | "Seed" | "Cleanup" | "MasterData")}>
        <option value="all">ทุกประเภท</option>
        <option value="Seed">Seed</option>
        <option value="Cleanup">Cleanup</option>
        <option value="MasterData">Master Data</option>
      </select>
    </div>
    {scripts.length ? <div className="table-wrap"><table><thead><tr><th>ชื่อ</th><th>ประเภท</th><th>DB</th><th>สถานะ</th><th>การอนุมัติ</th><th>สร้างเมื่อ</th><th></th></tr></thead><tbody>{scripts.map((s) => {
      const isMasterData = s.scriptType === "MasterData";
      const canRunNow = s.isActive && (!isMasterData || s.approvalStatus === "Approved");
      return <tr key={s.automationDataSeedScriptId}>
      <td><b>{s.name}</b>{s.description && <small>{s.description}</small>}</td>
      <td><Badge tone={scriptTypeTone[s.scriptType] ?? "blue"}>{s.scriptType}</Badge></td>
      <td>{s.dbKind}</td>
      <td><Badge tone={s.isActive ? "green" : "gray"}>{s.isActive ? "เปิดใช้งาน" : "ปิดแล้ว"}</Badge></td>
      <td>{isMasterData ? <Badge tone={approvalStatusTone[s.approvalStatus] ?? "gray"}>{s.approvalStatus}</Badge> : <span className="muted-text">-</span>}</td>
      <td>{formatThaiDateTime(s.createdAt)}</td>
      <td>
        {canEdit && <button type="button" className="table-action" onClick={() => openEdit(s.automationDataSeedScriptId)}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข</button>}
        {canEdit && isMasterData && s.approvalStatus !== "Approved" && <button type="button" className="table-action" onClick={() => approveScript(s)}>✓ อนุมัติ</button>}
        {canEdit && isMasterData && s.approvalStatus !== "Rejected" && <button type="button" className="table-action danger" onClick={() => rejectScript(s)}>✗ ไม่อนุมัติ</button>}
        {canRun && canRunNow && <button type="button" className="table-action" onClick={() => setRunModal(s)}><span className="material-symbols-outlined" aria-hidden="true">play_arrow</span> รัน</button>}
        <button type="button" className="table-action" onClick={() => openRunHistory(s)}><span aria-hidden="true">↺</span> ประวัติการรัน</button>
        {canEdit && <button type="button" className={`table-action${s.isActive ? " danger" : ""}`} onClick={() => toggleActive(s)}><span className="material-symbols-outlined" aria-hidden="true">power_settings_new</span> {s.isActive ? "ปิด" : "เปิด"}</button>}
      </td>
    </tr>;
    })}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Script</p><small>สร้าง SQL script ที่รันได้ซ้ำโดยไม่พัง (เช่นใช้ MERGE/UPSERT หรือเช็คก่อน insert/delete) — Seed สั่งก่อนชุด Automation ที่ต้องการ master data, Cleanup สั่งหลังรันเพื่อล้างข้อมูลที่ทิ้งไว้, Master Data เตรียมสินค้า/ราคา/โปรโมชั่นก่อน POS scenario (ต้องอนุมัติก่อนรัน)</small></div>}

    {createModal && <SeedScriptFormModal busy={busy} onClose={() => setCreateModal(false)} onSave={createScript} />}
    {editScript && <SeedScriptFormModal script={editScript} busy={busy} onClose={() => setEditScript(null)} onSave={(body) => updateScript(editScript.automationDataSeedScriptId, body)} />}
    {runModal && <SnapshotRequestModal projectId={projectId} releaseId={releaseId} headers={headers} busy={busy} onClose={() => setRunModal(null)} onSave={requestRun}
      title={`สั่งรัน ${runModal.scriptType} Script — ${runModal.name}`}
      subtitle={`Windows Agent จะรัน SQL นี้บนฐานข้อมูลจริง (${runModal.dbKind}) ของ Environment ที่เลือก`}
      submitLabel="สั่งรัน Script"
      warning={runModal.scriptType === "Cleanup" ? "Cleanup Script จะลบ/แก้ข้อมูลใน DB จริงของ Environment ที่เลือก — ตรวจ Environment ให้ถูกก่อนสั่งรัน" : "Script จะเขียนข้อมูลลง DB จริงของ Environment ที่เลือก"}
      confirmMessage={(env, build) => `ยืนยันรัน ${runModal.scriptType} Script "${runModal.name}" บน DB จริงของ "${env}" (Build ${build})?\n\n⚠ SQL จะถูกรันทันทีที่ Agent รับงาน — ย้อนกลับไม่ได้ถ้าไม่มี Snapshot`} />}

    {runHistory && <ModalShell labelledBy="automation-seed-run-history-title" onDismiss={() => setRunHistory(null)}>
      <div className="modal-head"><div><h2 id="automation-seed-run-history-title">ประวัติการรัน — {runHistory.name}</h2><small>{runHistory.runs.length} รายการ — ล่าสุดก่อน</small></div><button aria-label="ปิด" onClick={() => setRunHistory(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {runHistory.runs.length ? <div className="automation-result-list">{runHistory.runs.map((r) => <div key={r.automationDataSeedRunId} className="automation-failure-row">
        <b><Badge tone={snapshotStatusTone[r.status] ?? "blue"}>{r.status}</Badge> <Badge tone={scriptTypeTone[r.scriptType] ?? "blue"}>{r.scriptType}</Badge> {r.environmentName} / {r.buildNumber} · {formatThaiDateTime(r.requestedAt)}</b>
        <span>{r.status === "Succeeded" ? `Rows affected: ${r.rowsAffected ?? 0}` : (r.agentCode ? `Agent: ${r.agentCode}` : "")}</span>
        {r.errorMessage && <span>{r.errorMessage}</span>}
      </div>)}</div> : <div className="empty"><p>ยังไม่เคยถูกรัน</p></div>}
      <div className="modal-actions"><button className="btn" onClick={() => setRunHistory(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิดหน้าต่าง</button></div>
    </ModalShell>}
  </section>;
}

function SeedScriptFormModal({ script, busy, onClose, onSave }: {
  script?: AutomationDataSeedScriptDetailItem; busy: boolean; onClose: () => void; onSave: (body: Record<string, unknown>) => void;
}) {
  const isEdit = !!script;
  const [name, setName] = useState(script?.name ?? "");
  const [description, setDescription] = useState(script?.description ?? "");
  const [scriptType, setScriptType] = useState(script?.scriptType ?? "Seed");
  const [dbKind, setDbKind] = useState(script?.dbKind ?? "Firebird");
  const [sqlScript, setSqlScript] = useState(script?.sqlScript ?? "");

  const canSave = name.trim() && sqlScript.trim();
  const save = () => onSave({ name: name.trim(), description: description.trim() || null, scriptType, dbKind, sqlScript });

  return <ModalShell labelledBy="automation-seed-form-title" onDismiss={() => { if (!busy) onClose(); }} form>
    <div className="modal-head"><div><h2 id="automation-seed-form-title">{isEdit ? `แก้ไข ${script!.name}` : "สร้าง Script"}</h2></div><button aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    <div className="form-grid">
      <label className="full">ชื่อ<input type="text" value={name} onChange={(e) => setName(e.target.value)} placeholder="เช่น Baseline Products" /></label>
      <label className="full">คำอธิบาย (ไม่บังคับ)<textarea rows={2} value={description} onChange={(e) => setDescription(e.target.value)} /></label>
      <label>ประเภท<select value={scriptType} onChange={(e) => setScriptType(e.target.value)}><option value="Seed">Seed (ใส่ข้อมูลก่อนรัน)</option><option value="Cleanup">Cleanup (ล้างข้อมูลหลังรัน)</option><option value="MasterData">Master Data (สินค้า/ราคา/โปรโมชั่นก่อน POS scenario — ต้องอนุมัติก่อนรัน)</option></select></label>
      {isEdit && script!.scriptType === "MasterData" && <p className="muted-text">แก้ไข SQL แล้วจะต้องขออนุมัติใหม่อีกครั้งก่อนสั่งรันได้ (สถานะอนุมัติปัจจุบันจะถูกรีเซ็ตเป็น Pending)</p>}
      <label>ฐานข้อมูล<select value={dbKind} onChange={(e) => setDbKind(e.target.value)}><option value="Firebird">Firebird</option><option value="SqlServer">SQL Server</option></select></label>
      <label className="full">SQL Script (ต้อง repeatable/idempotent เอง เช่นเช็คก่อน insert — ห้ามใส่ connection string/credential)<textarea rows={10} className="mono" value={sqlScript} onChange={(e) => setSqlScript(e.target.value)} placeholder={"INSERT INTO Products (Code, Name)\nSELECT 'P001', 'Test Product'\nFROM RDB$DATABASE\nWHERE NOT EXISTS (SELECT 1 FROM Products WHERE Code='P001');"} /></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={busy || !canSave} onClick={save}>{busy ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}</button></div>
  </ModalShell>;
}

export function AutomationEnvironmentDataProfileTab({ projectId, headers, canEdit }: {
  projectId: string; headers: Record<string, string>; canEdit: boolean;
}) {
  const [profiles, setProfiles] = useState<AutomationEnvironmentDataProfileItem[]>([]);
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [createModal, setCreateModal] = useState(false);
  const [editProfile, setEditProfile] = useState<AutomationEnvironmentDataProfileItem | null>(null);

  useEffect(() => {
    if (!projectId) return;
    const ctrl = new AbortController();
    fetchJson(`${apiUrl}/automation/data/environment-data-profiles?projectId=${projectId}`, headers, ctrl.signal).then((p) => setProfiles(Array.isArray(p) ? p : [])).catch((e) => !isAbort(e) && setError("โหลด Environment Data Profile ไม่สำเร็จ"));
    return () => ctrl.abort();
  }, [projectId, headers, reload]);

  const createProfile = async (body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/environment-data-profiles?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Profile ไม่สำเร็จ"); }
      setCreateModal(false); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "สร้าง Profile ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const updateProfile = async (id: string, body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/environment-data-profiles/${id}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "แก้ไข Profile ไม่สำเร็จ"); }
      setEditProfile(null); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "แก้ไข Profile ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  return <section className="automation-cases" aria-label="Automation Environment Data Profile">
    <header className="automation-section-head"><div><h2>Environment Data Profile (AUT-DATA-006)</h2><p>เก็บ metadata ที่ไม่ใช่ secret ต่อ Environment (ตอนนี้มีแค่ประเภทฐานข้อมูล) เพื่อให้ Hub เช็คความไม่ตรงกันของ DbKind ระหว่าง Environment กับ Seed script/DB Snapshot ได้ตั้งแต่ตอนสั่งงาน แทนที่จะรอให้ Agent claim งานไปแล้วค่อย fail — <b>ไม่มี field เก็บ connection string/credential ในนี้เลย</b> credential ของ DB จริงยังอยู่ที่เครื่อง Windows Agent เท่านั้นเหมือนเดิมทุกประการ (Environment ที่ยังไม่สร้าง Profile จะไม่ถูกเช็คอะไรเลย เป็น opt-in)</p></div>{canEdit && <button className="btn primary" type="button" onClick={() => setCreateModal(true)}><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Profile</button>}</header>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    {profiles.length ? <div className="table-wrap"><table><thead><tr><th>Environment</th><th>DbKind</th><th>หมายเหตุ</th><th>แก้ไขล่าสุด</th><th></th></tr></thead><tbody>{profiles.map((p) => <tr key={p.automationEnvironmentDataProfileId}>
      <td><b>{p.environmentName}</b></td>
      <td><Badge tone={p.dbKind === "Firebird" ? "blue" : "purple"}>{p.dbKind}</Badge></td>
      <td>{p.notes ?? <span className="muted-text">-</span>}</td>
      <td>{formatThaiDateTime(p.updatedAt ?? p.createdAt)}</td>
      <td>{canEdit && <button type="button" className="table-action" onClick={() => setEditProfile(p)}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข</button>}</td>
    </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Environment Data Profile</p><small>สร้าง Profile ต่อ Environment เพื่อระบุว่าเป็น Firebird หรือ SQL Server — Hub จะใช้เทียบกับ Seed script/Snapshot ก่อนสั่งงานให้อัตโนมัติ</small></div>}

    {createModal && <EnvironmentDataProfileFormModal busy={busy} onClose={() => setCreateModal(false)} onSave={createProfile} />}
    {editProfile && <EnvironmentDataProfileFormModal profile={editProfile} busy={busy} onClose={() => setEditProfile(null)} onSave={(body) => updateProfile(editProfile.automationEnvironmentDataProfileId, body)} />}
  </section>;
}

function EnvironmentDataProfileFormModal({ profile, busy, onClose, onSave }: {
  profile?: AutomationEnvironmentDataProfileItem; busy: boolean; onClose: () => void; onSave: (body: Record<string, unknown>) => void;
}) {
  const isEdit = !!profile;
  const { environments, loadError } = useBuildsAndEnvironments(undefined, { builds: false, enabled: !isEdit });

  const [environmentId, setEnvironmentId] = useState(profile?.environmentId ?? "");
  const [dbKind, setDbKind] = useState(profile?.dbKind ?? "Firebird");
  const [notes, setNotes] = useState(profile?.notes ?? "");


  const canSave = isEdit ? true : !!environmentId;
  const save = () => onSave(isEdit ? { dbKind, notes: notes.trim() || null } : { environmentId, dbKind, notes: notes.trim() || null });

  return <ModalShell labelledBy="automation-data-profile-form-title" onDismiss={() => { if (!busy) onClose(); }} form>
    <div className="modal-head"><div><h2 id="automation-data-profile-form-title">{isEdit ? `แก้ไข ${profile!.environmentName}` : "สร้าง Environment Data Profile"}</h2></div><button aria-label="ปิด" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    {loadError && <div className="inline-alert error" role="alert"><span>{loadError}</span></div>}
    <div className="form-grid">
      {isEdit ? <label>Environment<input type="text" value={profile!.environmentName} disabled /></label> :
        <label>Environment<select value={environmentId} onChange={(e) => setEnvironmentId(e.target.value)}><option value="">เลือก Environment</option>{environments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>}
      <label>DbKind<select value={dbKind} onChange={(e) => setDbKind(e.target.value)}><option value="Firebird">Firebird</option><option value="SqlServer">SQL Server</option></select></label>
      <label className="full">หมายเหตุ (ไม่บังคับ — ห้ามใส่ connection string/credential)<textarea rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="เช่น เครื่อง UAT ทีม Sales" /></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={busy || !canSave} onClick={save}>{busy ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}</button></div>
  </ModalShell>;
}
