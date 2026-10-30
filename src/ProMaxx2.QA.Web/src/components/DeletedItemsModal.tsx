import { useEffect, useState } from "react";
import { apiFetch, apiUrl, getJson, isAbortError } from "../api";
import { confirmDialog, notify } from "./dialogStore";
import { ModalShell } from "./ModalShell";
import { fmtDateTimeBE } from "../shared/appShared";
import "./DeletedItemsModal.css";

/** ประเภทที่ API `/recycle-bin/{type}` รองรับ — ดู RecycleBinService.Types */
export type DeletedItemType = "test-cases" | "test-cycles" | "requirements" | "defects" | "risk-acceptances" | "knowledge-articles" | "knowledge-files" | "work-logs" | "test-plans";

type DeletedItem = { id: string; code?: string | null; title: string; detail?: string | null; projectName?: string | null; deletedAt?: string | null; deletedByName?: string | null };

/** ดู/กู้คืนรายการที่ลบแล้วเป็นสิทธิ์ SYS_ADMIN เท่านั้น (API ใช้ policy SysAdminOnly) */
function isSysAdminUser(): boolean {
  try { return (JSON.parse(localStorage.getItem("qa.user") ?? "{}") as { roles?: string[] }).roles?.includes("SYS_ADMIN") ?? false; }
  catch { return false; }
}

/**
 * ปุ่ม "ลบแล้ว" + หน้าต่างรายการที่ลบแล้ว (soft delete) ของหน้านั้น — แสดงเฉพาะ SYS_ADMIN
 * การลบในหน้าเหล่านี้เก็บประวัติไว้ (รายงานไม่นับ) จึงกู้คืนจากที่นี่ได้; กู้คืนแล้วเรียก onRestored ให้หน้าโหลดรายการใหม่
 */
export function DeletedItemsButton({ type, label, projectId, onRestored, className = "btn" }: { type: DeletedItemType; label: string; projectId?: string; onRestored: () => void; className?: string }) {
  const [open, setOpen] = useState(false);
  const [isAdmin] = useState(isSysAdminUser);
  if (!isAdmin) return null;
  return (
    <>
      <button type="button" className={className} title={`${label}ที่ลบแล้ว — ดูและกู้คืน`} onClick={() => setOpen(true)}>
        <span className="material-symbols-outlined" aria-hidden="true">restore_from_trash</span>ลบแล้ว
      </button>
      {open && <DeletedItemsModal type={type} label={label} projectId={projectId} onRestored={onRestored} onClose={() => setOpen(false)} />}
    </>
  );
}

function DeletedItemsModal({ type, label, projectId, onRestored, onClose }: { type: DeletedItemType; label: string; projectId?: string; onRestored: () => void; onClose: () => void }) {
  const [rows, setRows] = useState<DeletedItem[] | null>(null);
  const [error, setError] = useState("");
  const [search, setSearch] = useState("");
  const [busyId, setBusyId] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setError("");
    getJson<DeletedItem[]>(`${apiUrl}/recycle-bin/${type}${projectId ? `?projectId=${projectId}` : ""}`, controller.signal)
      .then(setRows)
      .catch((e) => { if (!isAbortError(e)) { setRows([]); setError(e instanceof Error ? e.message : "โหลดรายการที่ลบแล้วไม่สำเร็จ"); } });
    return () => controller.abort();
  }, [type, projectId, reload]);
  const term = search.trim().toLowerCase();
  const visible = (rows ?? []).filter((x) => !term || `${x.code ?? ""} ${x.title} ${x.detail ?? ""}`.toLowerCase().includes(term));
  const restore = async (item: DeletedItem) => {
    const name = item.code ?? item.title;
    if (!await confirmDialog({ title: `กู้คืน ${name}`, message: `${label}นี้จะกลับมาแสดงในรายการ และนับในรายงานอีกครั้ง`, confirmLabel: "กู้คืน" })) return;
    setBusyId(item.id);
    try {
      await apiFetch(`/recycle-bin/${type}/${item.id}/restore`, { method: "POST", fallbackMessage: "กู้คืนไม่สำเร็จ" });
      notify(`กู้คืน ${name} แล้ว`, "success");
      setRows((current) => current?.filter((x) => x.id !== item.id) ?? null);
      onRestored();
    } catch (e) {
      notify(`กู้คืน ${name} ไม่สำเร็จ: ${e instanceof Error ? e.message : "ไม่ทราบสาเหตุ"}`, "error");
    } finally {
      setBusyId(null);
    }
  };
  return (
    <ModalShell labelledBy="deleted-items-title" className="dim-modal" onDismiss={onClose}>
      <div className="modal-head">
        <div><h2 id="deleted-items-title">{label}ที่ลบแล้ว</h2><small>ยังเก็บประวัติไว้ครบ แต่ไม่แสดงในรายการและรายงานจนกว่าจะกู้คืน</small></div>
        <button type="button" aria-label="ปิดรายการที่ลบแล้ว" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>
      <div className="dim-body">
        <label className="dim-search">
          <span className="material-symbols-outlined" aria-hidden="true">search</span>
          <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="ค้นหารหัส / ชื่อ" aria-label={`ค้นหา${label}ที่ลบแล้ว`} />
        </label>
        {error && <div className="inline-alert error" role="alert"><span>{error}</span><button type="button" onClick={() => { setRows(null); setReload((x) => x + 1); }}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></div>}
        {rows === null ? (
          <div className="dim-empty" role="status"><span className="spinner" aria-hidden="true" /> กำลังโหลด...</div>
        ) : visible.length === 0 ? (
          !error && <div className="dim-empty"><span className="material-symbols-outlined" aria-hidden="true">delete_sweep</span>{rows.length === 0 ? `ไม่มี${label}ที่ลบแล้ว` : "ไม่พบรายการที่ตรงกับคำค้น"}</div>
        ) : (
          <ul className="dim-list">
            {visible.map((x) => (
              <li key={x.id}>
                <div className="dim-info">
                  <b>{x.code ? <span className="dim-code">{x.code}</span> : null}{x.title}</b>
                  <small>{[x.detail, x.projectName].filter(Boolean).join(" · ") || "-"}</small>
                  <small className="dim-meta">ลบเมื่อ {x.deletedAt ? fmtDateTimeBE(x.deletedAt) : "-"}{x.deletedByName ? ` โดย ${x.deletedByName}` : ""}</small>
                </div>
                <button type="button" className="btn" disabled={busyId !== null} onClick={() => void restore(x)} aria-label={`กู้คืน ${x.code ?? x.title}`}>
                  {busyId === x.id ? <span className="spinner inline" aria-hidden="true" /> : <span className="material-symbols-outlined" aria-hidden="true">restore_from_trash</span>}กู้คืน
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
      <div className="modal-actions">
        <small className="dim-count">{rows ? `${visible.length.toLocaleString()} รายการ${rows.length >= 500 ? " (แสดง 500 รายการล่าสุด)" : ""}` : ""}</small>
        <button type="button" className="btn" onClick={onClose}>ปิด</button>
      </div>
    </ModalShell>
  );
}
