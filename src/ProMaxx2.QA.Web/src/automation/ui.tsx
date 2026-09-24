import { useEffect, useRef } from "react";

const modalStack: symbol[] = [];

const MODAL_FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

const MODAL_FORM_CLOSE_CONFIRM = "ปิดหน้าต่างนี้? ข้อมูลที่กรอกไว้และยังไม่บันทึกจะหายไป";

/** AUT-UI-002: modal มาตรฐานของหน้า Automation (backdrop + `.modal-box` ตาม UI_DESIGN_SYSTEM §9)
 * - เปิดแล้ว focus ช่องกรอกแรก (หรือปุ่มแรก), Tab วนอยู่ใน modal, ปิดแล้วคืน focus ให้ปุ่มที่เปิด
 * - Escape ปิดเฉพาะ modal บนสุด; `form` = มีข้อมูลที่กรอก → ถามยืนยันก่อนปิดด้วย Escape และคลิกพื้นหลังไม่ปิด
 * - modal อ่านอย่างเดียวปิดด้วยคลิกพื้นหลังได้ (ยกเว้น `backdropDismiss={false}`); การกันปิดระหว่าง busy อยู่ใน `onDismiss` ของผู้เรียก */
export function ModalShell({ labelledBy, label, className, onDismiss, form = false, backdropDismiss = true, children }: {
  labelledBy?: string; label?: string; className?: string; onDismiss?: () => void; form?: boolean; backdropDismiss?: boolean; children: React.ReactNode;
}) {
  const boxRef = useRef<HTMLDivElement>(null);
  const dismissRef = useRef(onDismiss);
  useEffect(() => { dismissRef.current = onDismiss; });
  useEffect(() => {
    const id = Symbol("modal");
    modalStack.push(id);
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const box = boxRef.current;
    const first = box?.querySelector<HTMLElement>("[autofocus]")
      ?? box?.querySelector<HTMLElement>('input:not([disabled]):not([type="hidden"]):not([readonly]), select:not([disabled]), textarea:not([disabled]):not([readonly])')
      ?? box?.querySelector<HTMLElement>(MODAL_FOCUSABLE);
    first?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (!box || modalStack[modalStack.length - 1] !== id) return;
      if (e.key === "Escape") {
        const dismiss = dismissRef.current;
        if (!dismiss) return;
        e.preventDefault();
        if (form && !window.confirm(MODAL_FORM_CLOSE_CONFIRM)) return;
        dismiss();
      } else if (e.key === "Tab") {
        const items = [...box.querySelectorAll<HTMLElement>(MODAL_FOCUSABLE)].filter((el) => el.offsetParent !== null);
        if (!items.length) return;
        const firstItem = items[0];
        const lastItem = items[items.length - 1];
        if (e.shiftKey && (document.activeElement === firstItem || !box.contains(document.activeElement))) { e.preventDefault(); lastItem.focus(); }
        else if (!e.shiftKey && (document.activeElement === lastItem || !box.contains(document.activeElement))) { e.preventDefault(); firstItem.focus(); }
      }
    };
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("keydown", onKey);
      modalStack.splice(modalStack.indexOf(id), 1);
      if (previous?.isConnected) previous.focus();
    };
  }, [form]);
  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby={labelledBy} aria-label={labelledBy ? undefined : label}
    onMouseDown={(e) => { if (e.target === e.currentTarget && !form && backdropDismiss) onDismiss?.(); }}>
    <div ref={boxRef} className={className ? `modal-box ${className}` : "modal-box"}>{children}</div>
  </div>;
}

export function Badge({ children, tone = "blue" }: { children: React.ReactNode; tone?: string }) {
  return <span className={`badge ${tone}`}>{children}</span>;
}

export function Pager({ page, count, total, pageSize, onPrev, onNext }: { page: number; count: number; total: number; pageSize: number; onPrev: () => void; onNext: () => void }) {
  return <div className="automation-pager" role="navigation" aria-label="แบ่งหน้า">
    <button type="button" className="pager-btn" disabled={page <= 1} onClick={onPrev} aria-label="หน้าก่อนหน้า">‹ ก่อนหน้า</button>
    <span className="pager-info">หน้า {page} / {count} · {total.toLocaleString()} รายการ · {pageSize}/หน้า</span>
    <button type="button" className="pager-btn" disabled={page >= count} onClick={onNext} aria-label="หน้าถัดไป">ถัดไป ›</button>
  </div>;
}
