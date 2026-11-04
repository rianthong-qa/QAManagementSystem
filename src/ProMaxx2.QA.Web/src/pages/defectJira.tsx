import { useState } from "react";
import type { DefectItem } from "../shared/types";
import { ActionMenu } from "../components/ActionMenu";
import { defectStatusKey } from "../shared/defects";

/* ชิ้นส่วนหน้า Defect แบบ Jira (ปรับ 2026-11-04, prefix dj- ใน DefectsPage.css):
   ป้ายสถานะ (lozenge), ไอคอน Severity แบบลูกศร, ไอคอนประเภท Defect, avatar ผู้ใช้ และ Board ตามสถานะ */

const severityIcons: Record<string, string> = { Critical: "keyboard_double_arrow_up", High: "keyboard_arrow_up", Medium: "drag_handle", Low: "keyboard_arrow_down" };

export function StatusLozenge({ status }: { status: string }) {
  return <span className={`dj-lozenge is-${defectStatusKey(status)}`}>{status}</span>;
}

/** ลูกศรสีตามระดับ + ชื่อระดับ (ไม่สื่อด้วยสีอย่างเดียว — ซ่อนชื่อได้เฉพาะเมื่อมีข้อความสำหรับ screen reader) */
export function SeverityMark({ severity, showLabel = true }: { severity: string; showLabel?: boolean }) {
  return <span className={`dj-sev is-${severity.toLowerCase()}`} title={`Severity: ${severity}`}>
    <span className="material-symbols-outlined" aria-hidden="true">{severityIcons[severity] ?? "drag_handle"}</span>
    {showLabel ? <span>{severity}</span> : <span className="sr-only">Severity {severity}</span>}
  </span>;
}

export function DefectTypeIcon() {
  return <span className="dj-type" aria-hidden="true" title="Defect" />;
}

// ตัวอักษรแรกของชื่อ (ข้ามสระหน้าภาษาไทย เ แ โ ใ ไ) สูงสุด 2 คำ; สีเลือกจากชื่อให้คนเดิมได้สีเดิมเสมอ
const initials = (name: string) => name.trim().split(/\s+/).slice(0, 2).map((word) => word.replace(/^[เแโใไ]/, "").charAt(0)).join("").toUpperCase();
const avatarTone = (name: string) => [...name].reduce((sum, ch) => sum + ch.charCodeAt(0), 0) % 6;

export function Avatar({ name, size = 24 }: { name?: string | null; size?: number }) {
  if (!name) return <span className="dj-avatar is-empty" style={{ width: size, height: size }} title="ยังไม่มอบหมาย" aria-hidden="true"><span className="material-symbols-outlined">person</span></span>;
  return <span className={`dj-avatar tone-${avatarTone(name)}`} style={{ width: size, height: size, fontSize: Math.max(11, Math.round(size * 0.42)) }} title={name} aria-hidden="true">{initials(name) || "?"}</span>;
}

export function AssigneeCell({ name }: { name?: string | null }) {
  return <span className="dj-person"><Avatar name={name} /><span className={name ? undefined : "dj-muted"}>{name ?? "ยังไม่มอบหมาย"}</span></span>;
}

/** Board ตามสถานะ — ลากการ์ดไปคอลัมน์อื่นเพื่อเปลี่ยนสถานะ (onMove ใช้ flow เดียวกับปุ่มเปลี่ยนสถานะ: ผลการปิด/ยืนยัน Reopen);
    ทางเลือกสำหรับคีย์บอร์ด/จอสัมผัส = เมนู ⋯ "ย้ายไป …" บนการ์ด */
export function DefectBoard({ items, statuses, canEdit, moduleLabel, onOpen, onMove }: {
  items: DefectItem[]; statuses: string[]; canEdit: boolean; moduleLabel: (item: DefectItem) => string | null;
  onOpen: (item: DefectItem) => void; onMove: (item: DefectItem, status: string) => void;
}) {
  const [dragId, setDragId] = useState<string | null>(null);
  const [overStatus, setOverStatus] = useState<string | null>(null);
  const endDrag = () => { setDragId(null); setOverStatus(null); };
  return <div className="dj-board">
    {statuses.map((status) => {
      const cards = items.filter((x) => x.status === status);
      const dragging = items.find((x) => x.defectId === dragId);
      const canDrop = !!dragging && dragging.status !== status;
      return <section key={status} className={`dj-col is-${defectStatusKey(status)}${overStatus === status && canDrop ? " is-over" : ""}`} aria-label={`${status} ${cards.length} รายการ`}
        onDragOver={canEdit ? (e) => { if (!canDrop) return; e.preventDefault(); e.dataTransfer.dropEffect = "move"; setOverStatus(status); } : undefined}
        onDragLeave={(e) => { if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setOverStatus(null); }}
        onDrop={(e) => { e.preventDefault(); const item = dragging; endDrag(); if (item && item.status !== status) onMove(item, status); }}>
        <header className="dj-col-head"><h3>{status}</h3><span>{cards.length.toLocaleString()}</span></header>
        <div className="dj-col-cards">
          {cards.map((x) => {
            const module = moduleLabel(x);
            return <article key={x.defectId} className={`dj-card${dragId === x.defectId ? " is-dragging" : ""}`} draggable={canEdit}
              onDragStart={(e) => { setDragId(x.defectId); e.dataTransfer.effectAllowed = "move"; e.dataTransfer.setData("text/plain", x.defectId); }} onDragEnd={endDrag}>
              <button type="button" className="dj-card-open" onClick={() => onOpen(x)} aria-label={`เปิด ${x.defectCode} ${x.title}`}>{x.title}</button>
              {module && <span className="dj-card-module" title={module}>{module}</span>}
              <footer>
                <DefectTypeIcon />
                <span className={`dj-card-key${x.status === "Closed" ? " is-done" : ""}`}>{x.defectCode}</span>
                <SeverityMark severity={x.severity} showLabel={false} />
                {x.crmSyncStatus === "Linked" && <span className="dj-card-crm" title={`ผูก CRM Ticket #${x.crmTicketId}`}><span className="material-symbols-outlined" aria-hidden="true">link</span><span className="sr-only">ผูก CRM แล้ว</span></span>}
                <span className="dj-card-end"><Avatar name={x.assigneeName} /><span className="sr-only">{x.assigneeName ? `ผู้รับผิดชอบ ${x.assigneeName}` : "ยังไม่มอบหมาย"}</span></span>
              </footer>
              {canEdit && <ActionMenu className="dj-card-menu" label={`ย้าย ${x.defectCode}`} hideLabel disabled={false}
                items={statuses.filter((s) => s !== x.status).map((s) => ({ key: s, icon: "arrow_forward", label: `ย้ายไป ${s}`, onSelect: () => onMove(x, s) }))} />}
            </article>;
          })}
          {!cards.length && <p className="dj-col-empty">{canEdit && dragId ? "ปล่อยที่นี่" : "ไม่มี Defect"}</p>}
        </div>
      </section>;
    })}
  </div>;
}
