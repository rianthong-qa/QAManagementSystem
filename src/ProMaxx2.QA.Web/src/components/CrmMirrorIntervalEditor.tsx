import { useState, type FormEvent } from "react";
import { apiFetch } from "../api";
import { notify } from "./dialogStore";
import "./CrmMirrorIntervalEditor.css";

// ช่วงที่ backend ยอมรับ (CrmSyncSettings.Min/MaxMirrorIntervalMinutes) — แต่ละรอบ Login CRM และดึง Ticket 90 วัน จึงไม่ให้ถี่กว่า 5 นาที
const mirrorIntervalMin = 5;
const mirrorIntervalMax = 1440;
const presets = [5, 10, 15, 30, 60, 120, 240, 720, 1440];

/** 15 → "15 นาที", 90 → "1 ชั่วโมง 30 นาที", 1440 → "1 วัน" */
function formatMirrorInterval(minutes: number): string {
  if (minutes >= 1440 && minutes % 1440 === 0) return `${minutes / 1440} วัน`;
  const hours = Math.floor(minutes / 60), rest = minutes % 60;
  return [hours && `${hours} ชั่วโมง`, rest && `${rest} นาที`].filter(Boolean).join(" ") || "0 นาที";
}

/** รอบดึงข้อมูล CRM Ticket ทั้งทีม (CrmMirrorWorker) — แสดงค่าปัจจุบัน + ปุ่ม "ตั้งเวลา" แก้ได้ในที่ (เลือกค่าที่ใช้บ่อยหรือกำหนดเองเป็นนาที)
 * บันทึกผ่าน PUT /master-settings/crm-sync (ส่งเฉพาะ mirrorIntervalMinutes — รอบ Poll คงค่าเดิม), มีผลกับรอบถัดไปภายใน 1 นาที */
export function CrmMirrorIntervalEditor({ minutes, onSaved, prefix = "ดึงจาก CRM ทุก" }: { minutes: number; onSaved: (minutes: number) => void; prefix?: string }) {
  const [editing, setEditing] = useState(false);
  const [choice, setChoice] = useState("");
  const [custom, setCustom] = useState("");
  const [saving, setSaving] = useState(false);
  const value = choice === "custom" ? Number(custom) : Number(choice);
  const valid = Number.isInteger(value) && value >= mirrorIntervalMin && value <= mirrorIntervalMax;
  const start = () => { const preset = presets.includes(minutes); setChoice(preset ? String(minutes) : "custom"); setCustom(preset ? "" : String(minutes)); setEditing(true); };
  const save = async (event: FormEvent) => {
    event.preventDefault();
    if (!valid || saving) return;
    if (value === minutes) { setEditing(false); return; }
    setSaving(true);
    try {
      const saved = await apiFetch<{ mirrorIntervalMinutes: number }>("/master-settings/crm-sync", { method: "PUT", json: { mirrorIntervalMinutes: value }, fallbackMessage: "บันทึกรอบดึงข้อมูล CRM ไม่สำเร็จ" });
      onSaved(saved.mirrorIntervalMinutes);
      setEditing(false);
      notify(`ตั้งรอบดึงข้อมูล CRM เป็นทุก ${formatMirrorInterval(saved.mirrorIntervalMinutes)} แล้ว — มีผลภายใน 1 นาที`, "success");
    } catch (e) {
      notify(e instanceof Error ? e.message : "บันทึกรอบดึงข้อมูล CRM ไม่สำเร็จ", "error");
    } finally { setSaving(false); }
  };

  if (!editing) return <span className="crm-interval">
    <span className="material-symbols-outlined" aria-hidden="true">schedule</span>
    <span>{prefix} <b>{formatMirrorInterval(minutes)}</b></span>
    <button type="button" className="crm-interval-edit" onClick={start} aria-label={`ตั้งเวลารอบดึงข้อมูล CRM (ตอนนี้ทุก ${formatMirrorInterval(minutes)})`}><span className="material-symbols-outlined" aria-hidden="true">edit</span>ตั้งเวลา</button>
  </span>;

  return <form className="crm-interval-form" onSubmit={(e) => void save(e)} onKeyDown={(e) => { if (e.key === "Escape") { e.stopPropagation(); setEditing(false); } }}>
    <select autoFocus value={choice} onChange={(e) => setChoice(e.target.value)} aria-label="รอบดึงข้อมูล CRM">
      {presets.map((m) => <option key={m} value={m}>ทุก {formatMirrorInterval(m)}{m === 15 ? " (ค่าเริ่มต้น)" : ""}</option>)}
      <option value="custom">กำหนดเอง (นาที)</option>
    </select>
    {choice === "custom" && <input type="number" inputMode="numeric" min={mirrorIntervalMin} max={mirrorIntervalMax} step={1} value={custom} onChange={(e) => setCustom(e.target.value)} placeholder={`${mirrorIntervalMin}–${mirrorIntervalMax}`} aria-label="จำนวนนาที" aria-invalid={!valid || undefined} autoFocus />}
    <button type="submit" className="btn primary" disabled={!valid || saving}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก</> : "บันทึก"}</button>
    <button type="button" className="btn" disabled={saving} onClick={() => setEditing(false)}>ยกเลิก</button>
    <small className={valid ? undefined : "is-error"} role="status">{valid ? `ดึงทุก ${formatMirrorInterval(value)} · มีผลภายใน 1 นาที` : `กำหนดได้ ${mirrorIntervalMin}–${mirrorIntervalMax.toLocaleString("th-TH")} นาที`}</small>
  </form>;
}
