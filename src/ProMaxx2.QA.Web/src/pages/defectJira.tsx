import { useState } from "react";
import type { DefectItem } from "../shared/types";
import { ActionMenu } from "../components/ActionMenu";
import { defectStatusKey } from "../shared/defects";
import { Avatar } from "../components/JiraParts";

/* ชิ้นส่วนหน้า Defect แบบ Jira (ปรับ 2026-11-04, prefix dj- ใน DefectsPage.css):
   ป้ายสถานะ (lozenge), ไอคอน Severity แบบลูกศร, ไอคอนประเภท Defect และ Board ตามสถานะ — avatar / ป้ายแบบกลางอยู่ใน components/JiraParts */

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

/** Board ตามสถานะ — ลากการ์ดไปคอลัมน์อื่นเพื่อเปลี่ยนสถานะ (onMove ใช้ flow เดียวกับปุ่มเปลี่ยนสถานะ: ผลการปิด/ยืนยัน Reopen);
    ทางเลือกสำหรับคีย์บอร์ด/จอสัมผัส = เมนู ⋯ "ย้ายไป …" บนการ์ด */
export function DefectBoard({ items, statuses, canEdit, moduleLabel, onOpen, onMove, crmDevName }: {
  items: DefectItem[]; statuses: string[]; canEdit: boolean; moduleLabel: (item: DefectItem) => string | null;
  onOpen: (item: DefectItem) => void; onMove: (item: DefectItem, status: string) => void;
  /** ชื่อ Dev ที่รับงานใน CRM (null = ไม่ได้ผูก CRM) — ใช้แทนผู้รับผิดชอบเมื่อยังไม่มอบหมายใน QA Hub */
  crmDevName?: (item: DefectItem) => string | null;
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
                {/* ยังไม่มอบหมายใน QA Hub แต่ CRM มี Dev รับงาน = ใช้ Dev จาก CRM (Dev ส่วนใหญ่ไม่มีบัญชี QA Hub) */}
                {(() => { const crmDev = x.assigneeName ? null : crmDevName?.(x); const who = x.assigneeName ?? crmDev; return <span className="dj-card-end" title={who ? `ผู้รับผิดชอบ ${who}${crmDev ? " (Dev ใน CRM)" : ""}` : undefined}><Avatar name={who} /><span className="sr-only">{who ? `ผู้รับผิดชอบ ${who}${crmDev ? " ใน CRM" : ""}` : "ยังไม่มอบหมาย"}</span></span>; })()}
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
