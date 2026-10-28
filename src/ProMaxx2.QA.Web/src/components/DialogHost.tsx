import { useState, useSyncExternalStore } from "react";
import { ModalShell } from "./ModalShell";
import { dialogStore, type DialogRequest } from "./dialogStore";

/** แสดงกล่องยืนยัน/กรอกข้อความ (ทีละกล่องตามลำดับ) และ toast แจ้งผล — mount ครั้งเดียวที่ราก App */
export function DialogHost() {
  const { dialogs, toasts } = useSyncExternalStore(dialogStore.subscribe, dialogStore.getSnapshot);
  const current = dialogs[0];
  return <>
    {current && <DialogBox key={current.id} request={current} />}
    <div className="app-toasts" aria-live="polite" aria-atomic="false">
      {toasts.map((t) => <div key={t.id} className={`app-toast ${t.tone}`} role={t.tone === "error" ? "alert" : "status"}>
        <span className="material-symbols-outlined" aria-hidden="true">{t.tone === "error" ? "error" : t.tone === "success" ? "check_circle" : "info"}</span>
        <span className="app-toast-text">{t.message}</span>
        <button type="button" aria-label="ปิดข้อความ" onClick={() => dialogStore.dismissToast(t.id)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>)}
    </div>
  </>;
}

function DialogBox({ request }: { request: DialogRequest }) {
  const o = request.options;
  const [value, setValue] = useState(request.kind === "prompt" ? request.options.initialValue ?? "" : "");
  const isPrompt = request.kind === "prompt";
  const required = isPrompt && request.options.required;
  const cancel = () => dialogStore.settle(request.id, isPrompt ? null : false);
  const accept = () => {
    if (required && !value.trim()) return;
    dialogStore.settle(request.id, isPrompt ? value : true);
  };
  const titleId = `app-dialog-title-${request.id}`;
  return <ModalShell labelledBy={titleId} className="app-dialog" onDismiss={cancel} confirmDirtyClose={false}>
    <div className="modal-head"><h2 id={titleId}>{o.title ?? (isPrompt ? "กรอกข้อมูล" : o.tone === "danger" ? "ยืนยันการดำเนินการ" : "ยืนยัน")}</h2></div>
    <p className="app-dialog-message">{o.message}</p>
    {request.kind === "prompt" && (request.options.multiline
      ? <textarea id={`${titleId}-input`} rows={3} value={value} placeholder={request.options.placeholder} aria-label={o.title ?? "ข้อความ"} onChange={(e) => setValue(e.target.value)} />
      : <input id={`${titleId}-input`} value={value} placeholder={request.options.placeholder} aria-label={o.title ?? "ข้อความ"} onChange={(e) => setValue(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter") accept(); }} />)}
    {required && !value.trim() && <small className="field-error">จำเป็นต้องกรอก</small>}
    <div className="modal-actions">
      <button type="button" className="btn" onClick={cancel}>{o.cancelLabel ?? "ยกเลิก"}</button>
      <button type="button" className={`btn ${o.tone === "danger" ? "danger" : "primary"}`} disabled={!!required && !value.trim()} onClick={accept}>{o.confirmLabel ?? "ยืนยัน"}</button>
    </div>
  </ModalShell>;
}
