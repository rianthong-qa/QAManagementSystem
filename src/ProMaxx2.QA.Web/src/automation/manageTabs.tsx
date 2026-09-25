import { useEffect, useMemo, useState } from "react";
import { confirmDialog } from "../components/dialogStore";
import { formatThaiDateTime } from "../dateTime";
import { apiUrl } from "../api";
import {
  automationVerificationTone as verificationStatusTone,
  buildObjectKey,
} from "../automationUtils";
import type { AutomationActionItem, AutomationObjectItem, AutomationObjectImportDraft, AutomationObjectImportResult, AutomationObjectVerificationItem, AutomationAgentItem, RetryPolicyItem, AutomationAgentWorkload } from "./types";
import { token, fetchJson } from "./shared";
import { ModalShell, Badge } from "./ui";

const parseAutomationObjectImport = (text: string): Omit<AutomationObjectImportDraft, "clientId" | "status" | "message">[] => {
  const clean = text.trim();
  if (!clean) return [];
  if (clean.startsWith("[") || clean.startsWith("{")) {
    const raw = JSON.parse(clean);
    const rows = Array.isArray(raw) ? raw : Array.isArray(raw.objects) ? raw.objects : [raw];
    return rows.map((x: Record<string, unknown>) => ({
      moduleId: typeof x.moduleId === "string" ? x.moduleId : undefined,
      applicationCode: String(x.applicationCode ?? x.app ?? "Promaxx2"),
      screenCode: String(x.screenCode ?? x.screen ?? "Default"),
      objectCode: String(x.objectCode ?? x.name ?? x.automationId ?? ""),
      objectName: String(x.objectName ?? x.name ?? x.objectCode ?? x.automationId ?? ""),
      controlType: String(x.controlType ?? x.type ?? "Button"),
      automationId: x.automationId == null ? undefined : String(x.automationId),
      selectorJson: typeof x.selectorJson === "string" ? x.selectorJson : JSON.stringify(x.selector ?? { automationId: x.automationId ?? "" }),
    }));
  }
  const lines = clean.split(/\r?\n/).map((x) => x.trim()).filter(Boolean);
  const header = lines[0].split(",").map((x) => x.trim().toLowerCase());
  const hasHeader = header.some((x) => ["applicationcode", "screencode", "objectcode", "objectname", "controltype", "automationid"].includes(x));
  const dataLines = hasHeader ? lines.slice(1) : lines;
  return dataLines.map((line) => {
    const cols = line.split(",").map((x) => x.trim());
    const get = (name: string, index: number) => hasHeader ? cols[header.indexOf(name.toLowerCase())] : cols[index];
    const applicationCode = get("applicationCode", 0) || "Promaxx2";
    const screenCode = get("screenCode", 1) || "Default";
    const objectCode = get("objectCode", 2) || get("automationId", 5) || "";
    const objectName = get("objectName", 3) || objectCode;
    const controlType = get("controlType", 4) || "Button";
    const automationId = get("automationId", 5) || undefined;
    return { applicationCode, screenCode, objectCode, objectName, controlType, automationId, selectorJson: JSON.stringify({ automationId: automationId ?? "" }) };
  });
};

export function ActionLibraryTab({ actions, canManage, headers, onReload, onError, actionModal, setActionModal }: {
  actions: AutomationActionItem[]; canManage: boolean; headers: Record<string, string>; onReload: () => void; onError: (e: string) => void;
  actionModal: boolean; setActionModal: (v: boolean) => void;
}) {
  const [category, setCategory] = useState("ทั้งหมด");
  const emptyForm = { actionCode: "", actionName: "", category: "Generic", description: "", parameterSchemaJson: "{}", handlerKey: "", minimumAgentVersion: "1.0.0", isActive: true, retrySafety: "Unsafe" };
  const [form, setForm] = useState(emptyForm);
  const [editing, setEditing] = useState<AutomationActionItem | null>(null);
  const [busy, setBusy] = useState(false);
  const cats = ["ทั้งหมด", ...Array.from(new Set(actions.map((a) => a.category)))];
  const filtered = category === "ทั้งหมด" ? actions : actions.filter((a) => a.category === category);

  const openCreate = () => { setEditing(null); setForm(emptyForm); setActionModal(true); };
  const openEdit = (item: AutomationActionItem) => {
    setEditing(item);
    setForm({ actionCode: item.actionCode, actionName: item.actionName, category: item.category, description: item.description ?? "", parameterSchemaJson: item.parameterSchemaJson || "{}", handlerKey: item.handlerKey, minimumAgentVersion: item.minimumAgentVersion ?? "", isActive: item.isActive, retrySafety: item.retrySafety || "Unsafe" });
    setActionModal(true);
  };

  const save = async () => {
    setBusy(true);
    try {
      JSON.parse(form.parameterSchemaJson || "{}");
      const body = editing
        ? { actionName: form.actionName, category: form.category, description: form.description, parameterSchemaJson: form.parameterSchemaJson, handlerKey: form.handlerKey, minimumAgentVersion: form.minimumAgentVersion, isActive: form.isActive, retrySafety: form.retrySafety }
        : { actionCode: form.actionCode, actionName: form.actionName, category: form.category, description: form.description, parameterSchemaJson: form.parameterSchemaJson, minimumAgentVersion: form.minimumAgentVersion };
      const r = await fetch(editing ? `${apiUrl}/automation/actions/${editing.automationActionId}` : `${apiUrl}/automation/actions`, { method: editing ? "PUT" : "POST", headers, body: JSON.stringify(body) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? `${editing ? "แก้ไข" : "สร้าง"} Action ไม่สำเร็จ`);
      }
      setActionModal(false);
      onReload();
    } catch (e) {
      onError(e instanceof SyntaxError ? "Parameter Schema ต้องเป็น JSON ที่ถูกต้อง" : e instanceof Error ? e.message : "บันทึก Action ไม่สำเร็จ");
    } finally {
      setBusy(false);
    }
  };

  const toggle = async (item: AutomationActionItem) => {
    if (!await confirmDialog(`${item.isActive ? "ปิด" : "เปิด"} Action ${item.actionCode}?`)) return;
    setBusy(true);
    try {
      const r = await fetch(`${apiUrl}/automation/actions/${item.automationActionId}`, { method: "PUT", headers, body: JSON.stringify({ actionName: item.actionName, category: item.category, description: item.description, parameterSchemaJson: item.parameterSchemaJson, handlerKey: item.handlerKey, minimumAgentVersion: item.minimumAgentVersion, isActive: !item.isActive, retrySafety: item.retrySafety }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เปลี่ยนสถานะ Action ไม่สำเร็จ"); }
      onReload();
    } catch (e) { onError(e instanceof Error ? e.message : "เปลี่ยนสถานะ Action ไม่สำเร็จ"); }
    finally { setBusy(false); }
  };

  return <section className="automation-actions" aria-label="Action Library">
    <header className="automation-section-head"><div><h2>Action Library</h2><p>ชุดคำสั่งที่ Agent รองรับ · <code>ActionCode</code> ต้องตรงกับ DSL</p></div>{canManage && <button className="btn primary" onClick={openCreate}>+ เพิ่ม Action</button>}</header>
    <div className="automation-cand-filters" role="group" aria-label="กรอง Action ตาม Category">{cats.map((c) => <button key={c} type="button" className={"chip" + (category === c ? " active" : "")} onClick={() => setCategory(c)}>{c}</button>)}</div>
    {filtered.length ? <div className="table-wrap"><table className="table-cards"><thead><tr><th>Action Code</th><th>Name</th><th>Category</th><th>Handler</th><th>Min Agent</th><th>Retry Safety</th><th>Active</th>{canManage && <th>Actions</th>}</tr></thead><tbody>{filtered.map((a) => <tr key={a.automationActionId}><td><b>{a.actionCode}</b></td><td>{a.actionName}</td><td><Badge tone="blue">{a.category}</Badge></td><td><code>{a.handlerKey}</code></td><td>{a.minimumAgentVersion ?? "-"}</td><td><Badge tone={a.retrySafety === "Safe" ? "green" : a.retrySafety === "Conditional" ? "yellow" : "red"}>{a.retrySafety}</Badge></td><td><Badge tone={a.isActive ? "green" : "gray"}>{a.isActive ? "Active" : "Inactive"}</Badge></td>{canManage && <td><div className="automation-row-actions"><button type="button" className="table-action" disabled={busy} onClick={() => openEdit(a)}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข</button><button type="button" className={`table-action${a.isActive ? " danger" : ""}`} disabled={busy} onClick={() => toggle(a)}><span className="material-symbols-outlined" aria-hidden="true">power_settings_new</span> {a.isActive ? "ปิด" : "เปิด"}</button></div></td>}</tr>)}</tbody></table></div> : <div className="empty"><p>ไม่พบ Action</p></div>}

    {actionModal && <ModalShell label="Action Library" onDismiss={() => { if (!busy) setActionModal(false); }} form>
      <div className="modal-head"><div><h2>{editing ? `แก้ไข ${editing.actionCode}` : "เพิ่ม Action"}</h2><small>Action จะถูกใช้ตรวจสอบ (Validate) ว่า DSL ถูกต้อง</small></div><button aria-label="ปิด" disabled={busy} onClick={() => setActionModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      <div className="form-grid">
        <label>Action Code<input type="text" value={form.actionCode} disabled={Boolean(editing)} onChange={(e) => setForm({ ...form, actionCode: e.target.value.toUpperCase() })} placeholder="เช่น SET_QTY" /></label>
        <label>Name<input type="text" value={form.actionName} onChange={(e) => setForm({ ...form, actionName: e.target.value })} /></label>
        <label>Category<select value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })}><option>Authentication</option><option>Navigation</option><option>Document</option><option>Item</option><option>Generic UI</option><option>Validation</option></select></label>
        <label>Minimum Agent Version<input type="text" value={form.minimumAgentVersion} onChange={(e) => setForm({ ...form, minimumAgentVersion: e.target.value })} /></label>
        <label>Handler Key<input type="text" value={form.handlerKey || form.actionCode} disabled={!editing} onChange={(e) => setForm({ ...form, handlerKey: e.target.value.toUpperCase() })} /></label>
        {editing && <label>Retry Safety<select value={form.retrySafety} onChange={(e) => setForm({ ...form, retrySafety: e.target.value })}><option value="Safe">Safe — retry ได้เสมอ</option><option value="Conditional">Conditional — retry ถ้ายังไม่สำเร็จ</option><option value="Unsafe">Unsafe — ห้าม retry (เช่น บันทึกเอกสาร)</option></select></label>}
        {editing && <label className="checkbox-field"><input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} /> Active</label>}
        <label className="full">Description<textarea value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} /></label>
        <label className="full">Parameter Schema JSON<textarea rows={6} spellCheck={false} value={form.parameterSchemaJson} onChange={(e) => setForm({ ...form, parameterSchemaJson: e.target.value })} /></label>
      </div>
      <div className="modal-actions"><button className="btn" disabled={busy} onClick={() => setActionModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={busy || !form.actionCode.trim() || !form.actionName.trim() || (Boolean(editing) && !form.handlerKey.trim())} onClick={save}>{busy ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}</button></div>
    </ModalShell>}
  </section>;
}

export function ObjectRepositoryTab({ projectId, objects, canManage, headers, onReload, onError, objectModal, setObjectModal }: {
  projectId: string; objects: AutomationObjectItem[]; canManage: boolean; headers: Record<string, string>; onReload: () => void; onError: (e: string) => void;
  objectModal: boolean; setObjectModal: (v: boolean) => void;
}) {
  const [screen, setScreen] = useState("");
  const emptyForm = { moduleId: "", applicationCode: "Promaxx2", screenCode: "Sales", objectCode: "", objectName: "", controlType: "Button", automationId: "", selectorJson: "{}", isActive: true };
  const [form, setForm] = useState(emptyForm);
  const [editing, setEditing] = useState<AutomationObjectItem | null>(null);
  const [busy, setBusy] = useState(false);
  const [importModal, setImportModal] = useState(false);
  const [importText, setImportText] = useState("");
  const [importRows, setImportRows] = useState<AutomationObjectImportDraft[]>([]);
  const [selectedImport, setSelectedImport] = useState<Set<string>>(new Set());
  const [verifySelected, setVerifySelected] = useState<Set<string>>(new Set());
  const [verifications, setVerifications] = useState<AutomationObjectVerificationItem[]>([]);
  const [verifyModal, setVerifyModal] = useState(false);
  const [verifyReload, setVerifyReload] = useState(0);
  const screens = ["", ...Array.from(new Set(objects.map((o) => o.screenCode)))];
  const filtered = screen ? objects.filter((o) => o.screenCode === screen) : objects;
  const readyImportRows = importRows.filter((r) => r.status === "Ready");
  const latestVerificationByObject = useMemo(() => {
    const map = new Map<string, AutomationObjectVerificationItem>();
    for (const v of verifications) {
      const existing = map.get(v.automationObjectId);
      if (!existing || new Date(v.requestedAt) > new Date(existing.requestedAt)) map.set(v.automationObjectId, v);
    }
    return map;
  }, [verifications]);

  useEffect(() => {
    if (!projectId) return;
    fetchJson(`${apiUrl}/automation/objects/verifications?projectId=${projectId}`, { Authorization: `Bearer ${token()}` })
      .then((v) => setVerifications(Array.isArray(v) ? v : []))
      .catch(() => onError("โหลดผลตรวจสอบ Object ไม่สำเร็จ"));
  }, [projectId, verifyReload, onError]);

  const toggleVerifySelect = (id: string) => setVerifySelected((prev) => { const next = new Set(prev); if (next.has(id)) next.delete(id); else next.add(id); return next; });

  const requestVerification = async () => {
    if (!verifySelected.size) return;
    setBusy(true);
    try {
      const r = await fetch(`${apiUrl}/automation/objects/verify?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ objectIds: [...verifySelected], agentId: null }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "ขอตรวจสอบ Object ไม่สำเร็จ"); }
      setVerifySelected(new Set());
      setVerifyReload((x) => x + 1);
      setVerifyModal(true);
      onError(`ส่งคำขอตรวจสอบ ${verifySelected.size} Object แล้ว — รัน "runner verify --exe <path>" บนเครื่อง Agent เพื่อสแกนและรายงานผล`);
    } catch (e) { onError(e instanceof Error ? e.message : "ขอตรวจสอบ Object ไม่สำเร็จ"); }
    finally { setBusy(false); }
  };

  const openCreate = () => { setEditing(null); setForm(emptyForm); setObjectModal(true); };
  const openEdit = (item: AutomationObjectItem) => {
    setEditing(item);
    setForm({ moduleId: item.moduleId ?? "", applicationCode: item.applicationCode, screenCode: item.screenCode, objectCode: item.objectCode, objectName: item.objectName, controlType: item.controlType, automationId: item.automationId ?? "", selectorJson: item.selectorJson || "{}", isActive: item.isActive });
    setObjectModal(true);
  };

  const save = async () => {
    setBusy(true);
    try {
      JSON.parse(form.selectorJson || "{}");
      const body = { moduleId: form.moduleId || null, applicationCode: form.applicationCode, screenCode: form.screenCode, objectCode: form.objectCode, objectName: form.objectName, controlType: form.controlType, automationId: form.automationId || null, selectorJson: form.selectorJson };
      const r = await fetch(editing ? `${apiUrl}/automation/objects/${editing.automationObjectId}?projectId=${projectId}` : `${apiUrl}/automation/objects`, { method: editing ? "PUT" : "POST", headers, body: JSON.stringify(editing ? body : { projectId, ...body }) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? `${editing ? "แก้ไข" : "สร้าง"} Object ไม่สำเร็จ`);
      }
      setObjectModal(false);
      onReload();
    } catch (e) {
      onError(e instanceof SyntaxError ? "Selector JSON ต้องเป็น JSON ที่ถูกต้อง" : e instanceof Error ? e.message : "บันทึก Object ไม่สำเร็จ");
    } finally {
      setBusy(false);
    }
  };

  const toggle = async (item: AutomationObjectItem) => {
    if (!await confirmDialog(`${item.isActive ? "ปิด" : "เปิด"} Object ${buildObjectKey(item.screenCode, item.objectCode)}?`)) return;
    setBusy(true);
    try {
      const action = item.isActive ? "deactivate" : "activate";
      const r = await fetch(`${apiUrl}/automation/objects/${item.automationObjectId}/${action}?projectId=${projectId}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เปลี่ยนสถานะ Object ไม่สำเร็จ"); }
      onReload();
    } catch (e) { onError(e instanceof Error ? e.message : "เปลี่ยนสถานะ Object ไม่สำเร็จ"); }
    finally { setBusy(false); }
  };

  const previewImport = (text = importText) => {
    try {
      const existingKeys = new Set(objects.map((o) => `${o.applicationCode}.${o.screenCode}.${o.objectCode}`.toUpperCase()));
      const existingAutomationIds = new Set(objects.filter((o) => o.automationId).map((o) => `${o.applicationCode}.${o.automationId}`.toUpperCase()));
      const batchKeys = new Set<string>();
      const batchAutomationIds = new Set<string>();
      const rows = parseAutomationObjectImport(text).map((r, index) => {
        const applicationCode = r.applicationCode.trim() || "Promaxx2";
        const screenCode = r.screenCode.trim() || "Default";
        const objectCode = r.objectCode.trim().toUpperCase();
        const automationId = r.automationId?.trim();
        const key = `${applicationCode}.${screenCode}.${objectCode}`.toUpperCase();
        const automationKey = automationId ? `${applicationCode}.${automationId}`.toUpperCase() : "";
        let status: AutomationObjectImportDraft["status"] = "Ready";
        let message = "Ready to import.";
        try { JSON.parse(r.selectorJson || "{}"); } catch { status = "Invalid"; message = "Selector JSON is invalid."; }
        if (!objectCode || !r.objectName.trim() || !r.controlType.trim()) { status = "Invalid"; message = "Required fields are missing."; }
        else if (existingKeys.has(key) || batchKeys.has(key)) { status = "DuplicateKey"; message = "Business key already exists."; }
        else if (automationKey && (existingAutomationIds.has(automationKey) || batchAutomationIds.has(automationKey))) { status = "DuplicateAutomationId"; message = "AutomationId already exists."; }
        batchKeys.add(key);
        if (automationKey) batchAutomationIds.add(automationKey);
        return { ...r, applicationCode, screenCode, objectCode, automationId, clientId: `${index}-${key}`, status, message };
      });
      setImportRows(rows);
      setSelectedImport(new Set(rows.filter((r) => r.status === "Ready").map((r) => r.clientId)));
    } catch (e) {
      onError(e instanceof Error ? e.message : "Cannot parse import data.");
    }
  };

  const importSelected = async () => {
    const items = importRows.filter((r) => selectedImport.has(r.clientId) && r.status === "Ready");
    if (!items.length) return;
    setBusy(true);
    try {
      const r = await fetch(`${apiUrl}/automation/objects/import`, { method: "POST", headers, body: JSON.stringify({ projectId, items }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "Import Object failed"); }
      const result = await r.json() as AutomationObjectImportResult;
      setImportModal(false);
      setImportText("");
      setImportRows([]);
      setSelectedImport(new Set());
      onError(`Import complete: ${result.imported} imported, ${result.skipped} skipped.`);
      onReload();
    } catch (e) { onError(e instanceof Error ? e.message : "Import Object failed"); }
    finally { setBusy(false); }
  };

  return <section className="automation-objects" aria-label="Object Repository">
    <header className="automation-section-head"><div><h2>Object Repository</h2><p>Mapping ชื่อ Business (<code>Screen.Object</code>) ไปยัง Windows Control (<code>AutomationId</code>)</p></div>{canManage && <div className="automation-row-actions"><button className="btn" disabled={busy || !verifySelected.size} onClick={requestVerification}>⌕ ตรวจสอบที่เลือก ({verifySelected.size})</button><button className="btn" onClick={() => setVerifyModal(true)}><span className="material-symbols-outlined" aria-hidden="true">info</span> ผลตรวจสอบ</button><button className="btn" onClick={() => setImportModal(true)}><span className="material-symbols-outlined" aria-hidden="true">upload</span> Import Scanner</button><button className="btn primary" onClick={openCreate}>+ เพิ่ม Object</button></div>}</header>
    <div className="automation-cand-filters" role="group" aria-label="กรอง Object ตาม Screen">{screens.map((s) => <button key={s || "all"} type="button" className={"chip" + (screen === s ? " active" : "")} onClick={() => setScreen(s)}>{s || "ทุก Screen"}</button>)}</div>
    {filtered.length ? <div className="table-wrap"><table className="table-cards"><thead><tr>{canManage && <th aria-label="เลือก"></th>}<th>Business Key</th><th>Name</th><th>Screen</th><th>ControlType</th><th>AutomationId</th><th>Verification</th><th>Version</th><th>Active</th>{canManage && <th>Actions</th>}</tr></thead><tbody>{filtered.map((o) => { const lastVerify = latestVerificationByObject.get(o.automationObjectId); return <tr key={o.automationObjectId}>
      {canManage && <td><input type="checkbox" aria-label={`เลือกตรวจสอบ ${o.objectCode}`} checked={verifySelected.has(o.automationObjectId)} onChange={() => toggleVerifySelect(o.automationObjectId)} /></td>}
      <td><b>{buildObjectKey(o.screenCode, o.objectCode)}</b></td><td>{o.objectName}</td><td><Badge tone="blue">{o.screenCode}</Badge></td><td>{o.controlType}</td><td><code>{o.automationId ?? "-"}</code></td>
      <td>{lastVerify ? <Badge tone={verificationStatusTone[lastVerify.status] ?? "gray"}>{lastVerify.status}</Badge> : <span className="muted-text">ยังไม่ตรวจ</span>}</td>
      <td>v{o.objectVersion}</td><td><Badge tone={o.isActive ? "green" : "gray"}>{o.isActive ? "Active" : "Inactive"}</Badge></td>
      {canManage && <td><div className="automation-row-actions"><button type="button" className="table-action" disabled={busy} onClick={() => openEdit(o)}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข</button><button type="button" className={`table-action${o.isActive ? " danger" : ""}`} disabled={busy} onClick={() => toggle(o)}><span className="material-symbols-outlined" aria-hidden="true">power_settings_new</span> {o.isActive ? "ปิด" : "เปิด"}</button></div></td>}
    </tr>; })}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Object</p><small>Agent จะใช้ <code>AutomationId</code> นี้หาคอนโทรลบน Windows UI</small></div>}

    {verifyModal && <ModalShell label="ผลตรวจสอบ Object" onDismiss={() => setVerifyModal(false)}>
      <div className="modal-head"><div><h2>ผลตรวจสอบ Object (AUT-P0-006)</h2><small>รัน <code>runner verify --exe &lt;path&gt;</code> บนเครื่อง Agent เพื่อสแกนและรายงานผล Found/NotFound/Duplicate/ControlTypeMismatch</small></div><button aria-label="ปิด" onClick={() => setVerifyModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {verifications.length ? <div className="table-wrap"><table className="table-cards"><thead><tr><th>Business Key</th><th>Expected AutomationId</th><th>Actual</th><th>Status</th><th>Agent</th><th>เวลา</th></tr></thead><tbody>{verifications.map((v) => <tr key={v.automationObjectVerificationId}><td><b>{buildObjectKey(v.screenCode, v.objectCode)}</b></td><td><code>{v.expectedAutomationId ?? "-"}</code></td><td>{v.actualAutomationId ? <code>{v.actualAutomationId}</code> : "-"}{v.actualControlType ? ` (${v.actualControlType})` : ""}</td><td><Badge tone={verificationStatusTone[v.status] ?? "gray"}>{v.status}</Badge>{v.message && <small>{v.message}</small>}</td><td>{v.assignedAgentCode ?? "-"}</td><td>{formatThaiDateTime(v.completedAt ?? v.requestedAt)}</td></tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มีการขอตรวจสอบ</p></div>}
      <div className="modal-actions"><button className="btn primary" onClick={() => setVerifyModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิด</button></div>
    </ModalShell>}

    {objectModal && <ModalShell label="Object Repository" onDismiss={() => { if (!busy) setObjectModal(false); }} form>
      <div className="modal-head"><div><h2>{editing ? `แก้ไข ${buildObjectKey(editing.screenCode, editing.objectCode)}` : "เพิ่ม Object"}</h2><small>Business Key = <code>ScreenCode.ObjectCode</code> — DSL อ้างอิงด้วยค่านี้</small></div><button aria-label="ปิด" disabled={busy} onClick={() => setObjectModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      <div className="form-grid">
        <label>Application Code<input type="text" value={form.applicationCode} onChange={(e) => setForm({ ...form, applicationCode: e.target.value })} /></label>
        <label>Screen Code<input type="text" value={form.screenCode} onChange={(e) => setForm({ ...form, screenCode: e.target.value })} placeholder="เช่น Sales" /></label>
        <label>Object Code<input type="text" value={form.objectCode} onChange={(e) => setForm({ ...form, objectCode: e.target.value.toUpperCase() })} placeholder="เช่น SAVE" /></label>
        <label>Object Name<input type="text" value={form.objectName} onChange={(e) => setForm({ ...form, objectName: e.target.value })} /></label>
        <label>Control Type<select value={form.controlType} onChange={(e) => setForm({ ...form, controlType: e.target.value })}><option>Button</option><option>TextBox</option><option>ComboBox</option><option>CheckBox</option><option>Menu</option><option>Window</option></select></label>
        <label>AutomationId<input type="text" value={form.automationId} onChange={(e) => setForm({ ...form, automationId: e.target.value })} placeholder="เช่น btnSave" /></label>
        <label className="full">Selector JSON<textarea rows={5} spellCheck={false} value={form.selectorJson} onChange={(e) => setForm({ ...form, selectorJson: e.target.value })} /></label>
      </div>
      <div className="modal-actions"><button className="btn" disabled={busy} onClick={() => setObjectModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={busy || !form.screenCode.trim() || !form.objectCode.trim() || !form.objectName.trim() || !form.automationId.trim()} onClick={save}>{busy ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}</button></div>
    </ModalShell>}
    {importModal && <ModalShell label="Import Objects" onDismiss={() => { if (!busy) setImportModal(false); }} form>
      <div className="modal-head"><div><h2>Import Objects from Scanner</h2><small>Paste JSON or CSV, preview duplicates, then import selected rows.</small></div><button aria-label="Close" disabled={busy} onClick={() => setImportModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      <div className="form-grid">
        <label className="full">Scanner Output<textarea rows={8} spellCheck={false} value={importText} onChange={(e) => setImportText(e.target.value)} placeholder={'applicationCode,screenCode,objectCode,objectName,controlType,automationId\nPromaxx2,Sales,SAVE,Save Button,Button,btnSave'} /></label>
      </div>
      <div className="automation-row-actions" style={{ marginBottom: 10 }}>
        <button className="btn" disabled={busy || !importText.trim()} onClick={() => previewImport()}><span aria-hidden="true">⇄</span> Preview Diff</button>
        <label className="btn import-button">Load File<input type="file" accept=".json,.csv,.txt" disabled={busy} onChange={(e) => { const f = e.target.files?.[0]; if (!f) return; void f.text().then((text) => { setImportText(text); previewImport(text); }); e.target.value = ""; }} /></label>
        {importRows.length > 0 && <button className="table-action" type="button" onClick={() => setSelectedImport(new Set(readyImportRows.map((r) => r.clientId)))}><span aria-hidden="true">☑</span> Select Ready</button>}
        {importRows.length > 0 && <button className="table-action" type="button" onClick={() => setSelectedImport(new Set())}><span className="material-symbols-outlined" aria-hidden="true">close</span> Clear</button>}
      </div>
      {importRows.length > 0 && <div className="table-wrap"><table className="table-cards"><thead><tr><th></th><th>Business Key</th><th>Name</th><th>Control</th><th>AutomationId</th><th>Status</th></tr></thead><tbody>{importRows.map((r) => <tr key={r.clientId}><td><input type="checkbox" aria-label={`Select ${r.objectCode}`} checked={selectedImport.has(r.clientId)} disabled={busy || r.status !== "Ready"} onChange={() => setSelectedImport((prev) => { const next = new Set(prev); if (next.has(r.clientId)) next.delete(r.clientId); else next.add(r.clientId); return next; })} /></td><td><b>{buildObjectKey(r.screenCode, r.objectCode)}</b><small>{r.applicationCode}</small></td><td>{r.objectName}</td><td>{r.controlType}</td><td><code>{r.automationId ?? "-"}</code></td><td><Badge tone={r.status === "Ready" ? "green" : r.status === "Invalid" ? "red" : "yellow"}>{r.status}</Badge><small>{r.message}</small></td></tr>)}</tbody></table></div>}
      <div className="modal-actions"><button className="btn" disabled={busy} onClick={() => setImportModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span> Cancel</button><button className="btn primary" disabled={busy || selectedImport.size === 0} onClick={importSelected}>{busy ? <><span className="spinner inline" aria-hidden="true" /> Importing...</> : <><span className="material-symbols-outlined" aria-hidden="true">upload</span> Import {selectedImport.size} rows</>}</button></div>
    </ModalShell>}
  </section>;
}

export function RetryPolicyTab({ policy, canManage, busy, onSave }: {
  policy: RetryPolicyItem | null; canManage: boolean; busy: boolean; onSave: (policy: RetryPolicyItem) => void;
}) {
  const [maxAttempts, setMaxAttempts] = useState(policy?.maxAttempts ?? 2);
  const [backoffSeconds, setBackoffSeconds] = useState(policy?.backoffSeconds ?? 30);
  const [enabled, setEnabled] = useState(policy?.enabled ?? true);
  const [dirty, setDirty] = useState(false);
  useEffect(() => { if (policy && !dirty) { setMaxAttempts(policy.maxAttempts); setBackoffSeconds(policy.backoffSeconds); setEnabled(policy.enabled); } }, [policy, dirty]);

  return <section className="automation-actions" aria-label="Retry Policy">
    <header className="automation-section-head"><div><h2>Retry Policy (AUT-P0-009)</h2><p>กำหนดจำนวนครั้ง/ระยะเวลา backoff สำหรับ auto-retry เมื่อ Execution ล้มเหลวจาก Environment/Agent — ไม่ retry เมื่อมี Step ที่ไม่ปลอดภัย (Unsafe) สำเร็จไปแล้ว</p></div></header>
    <div className="form-grid">
      <label>Max Attempts<input type="number" min={0} max={10} value={maxAttempts} disabled={!canManage} onChange={(e) => { setMaxAttempts(Number(e.target.value)); setDirty(true); }} /></label>
      <label>Backoff (วินาที)<input type="number" min={0} max={3600} value={backoffSeconds} disabled={!canManage} onChange={(e) => { setBackoffSeconds(Number(e.target.value)); setDirty(true); }} /></label>
      <label className="checkbox-field"><input type="checkbox" checked={enabled} disabled={!canManage} onChange={(e) => { setEnabled(e.target.checked); setDirty(true); }} /> เปิดใช้งาน Auto-Retry</label>
    </div>
    {policy?.updatedAt && <p className="muted-text">แก้ไขล่าสุด {formatThaiDateTime(policy.updatedAt)}</p>}
    {canManage && <div className="acw-action-bar"><button type="button" className="btn primary" disabled={busy} onClick={() => { setDirty(false); onSave({ maxAttempts, backoffSeconds, enabled }); }}>{busy ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}</button></div>}
  </section>;
}

export function AgentsSection({ agents, agentsOnline, canManage, headers, onToggle, onDelete }: {
  agents: AutomationAgentItem[]; agentsOnline: number; canManage: boolean; headers: Record<string, string>; onToggle: (a: AutomationAgentItem, enable: boolean) => void; onDelete: (a: AutomationAgentItem) => void;
}) {
  const [workloadFor, setWorkloadFor] = useState<AutomationAgentItem | null>(null);
  return <section className="automation-agents" aria-label="Automation Agents">
    <header className="automation-section-head"><div><h2>Central Windows Agents</h2><p>Agent ลงทะเบียนอัตโนมัติและส่ง heartbeat ทุก 15 วินาที · Offline เมื่อเงียบเกิน 60 วินาที</p></div><span className="automation-agent-count">{agentsOnline} Online</span></header>
    {agents.length ? <div className="automation-agent-grid">{agents.map((a) => <article key={a.agentId}><div className="automation-agent-top"><div><Badge tone={a.connectivity === "Online" ? "green" : a.connectivity === "Disabled" ? "gray" : "yellow"}>{a.connectivity}</Badge><Badge tone={a.status === "Busy" ? "blue" : "green"}>{a.status}</Badge></div><div className="automation-agent-actions"><button type="button" className="table-action icon-btn" title="Workload / History" aria-label={`Workload ${a.agentCode}`} onClick={() => setWorkloadFor(a)}>📊</button>{canManage && <button type="button" className={`table-action icon-btn${a.isEnabled ? "" : " danger"}`} title={a.isEnabled ? "ปิดใช้งาน" : "เปิดใช้งาน"} aria-label={a.isEnabled ? "ปิดใช้งาน" : "เปิดใช้งาน"} onClick={() => onToggle(a, !a.isEnabled)}>{a.isEnabled ? "⏻" : "⏼"}</button>}{canManage && <button type="button" className="table-action danger icon-btn" title="ลบ Agent" aria-label="ลบ Agent" onClick={() => onDelete(a)}>🗑</button>}</div></div><b>{a.agentCode}</b><span>{a.machineName} · v{a.agentVersion}</span><small>{a.operatingSystem} · {a.architecture}</small><small>รองรับ {a.capabilities.join(" + ") || "-"}</small><time dateTime={a.lastHeartbeatAt}>ล่าสุด {formatThaiDateTime(a.lastHeartbeatAt)}</time></article>)}</div> : <div className="empty"><p>ยังไม่มี Agent ลงทะเบียน</p><small>ติดตั้งบนเครื่อง Windows: ตั้งค่า env แล้วรัน <code>agent\\run-agent.ps1</code> — Agent จะ register + ส่ง heartbeat อัตโนมัติ</small></div>}
    {workloadFor && <AgentWorkloadModal agent={workloadFor} headers={headers} onClose={() => setWorkloadFor(null)} />}
  </section>;
}

/// AUT-P2-004: utilization/queue time/runtime/failure over the last 30 days (server default), plus the capped
/// recent-heartbeat log (see AutomationAgentHeartbeatEvent — literal heartbeat history, not an online/offline
/// timeline; that would need a background worker this system doesn't have).
function AgentWorkloadModal({ agent, headers, onClose }: { agent: AutomationAgentItem; headers: Record<string, string>; onClose: () => void }) {
  const [workload, setWorkload] = useState<AutomationAgentWorkload | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    fetch(`${apiUrl}/automation/agents/${agent.agentId}/workload`, { headers })
      .then((r) => (r.ok ? r.json() : Promise.reject()))
      .then((d) => setWorkload(d))
      .catch(() => setError("โหลดข้อมูล Workload ไม่สำเร็จ"));
  }, [agent.agentId, headers]);

  const fmtMs = (ms?: number) => ms == null ? "-" : ms < 1000 ? `${Math.round(ms)} ms` : `${(ms / 1000).toFixed(1)} s`;

  return <ModalShell labelledBy="automation-agent-workload-title" onDismiss={onClose}>
    <div className="modal-head"><div><h2 id="automation-agent-workload-title">Workload — {agent.agentCode}</h2><small>{workload ? `${formatThaiDateTime(workload.windowFrom)} – ${formatThaiDateTime(workload.windowTo)}` : "ช่วง 30 วันล่าสุด"}</small></div><button aria-label="ปิด" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    {workload ? <>
      <div className="automation-kpis">
        <div><small>Utilization</small><strong>{workload.utilizationPercent.toFixed(1)}%</strong><span>สัดส่วนเวลาที่ทำงาน</span></div>
        <div><small>Queue Time เฉลี่ย</small><strong>{fmtMs(workload.avgQueueTimeMs)}</strong><span>รอ Agent รับงาน</span></div>
        <div><small>Runtime เฉลี่ย</small><strong>{fmtMs(workload.avgRuntimeMs)}</strong><span>ต่อ Execution</span></div>
        <div><small>Total</small><strong>{workload.totalExecutions}</strong><span>Execution</span></div>
        <div className={workload.failedExecutions ? "needs-review" : ""}><small>Failure Rate</small><strong>{workload.failureRatePercent.toFixed(1)}%</strong><span>{workload.failedExecutions} Failed</span></div>
      </div>
      <h3>ประวัติ Heartbeat ({workload.recentHeartbeats.length} รายการล่าสุด)</h3>
      {workload.recentHeartbeats.length ? <div className="table-wrap"><table className="table-cards"><thead><tr><th>สถานะ</th><th>Execution ที่กำลังรัน</th><th>เวลา</th></tr></thead><tbody>
        {workload.recentHeartbeats.map((h, i) => <tr key={i}><td><Badge tone={h.status === "Busy" ? "blue" : "green"}>{h.status}</Badge></td><td>{h.currentExecutionId ?? "-"}</td><td>{formatThaiDateTime(h.occurredAt)}</td></tr>)}
      </tbody></table></div> : <div className="empty"><p>ยังไม่มีประวัติ Heartbeat</p></div>}
    </> : !error && <div className="empty"><div className="spinner" /><p>กำลังโหลด...</p></div>}
    <div className="modal-actions"><button className="btn" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิดหน้าต่าง</button></div>
  </ModalShell>;
}
