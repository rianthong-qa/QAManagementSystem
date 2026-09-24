import { useEffect, useRef } from "react";
import { confirmDialog } from "./dialogStore";

const modalStack: symbol[] = [];

const MODAL_FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

const MODAL_DIRTY_CLOSE_CONFIRM = "ปิดหน้าต่างนี้? ข้อมูลที่กรอกไว้และยังไม่บันทึกจะหายไป";

/** Modal มาตรฐานของทั้งแอป (backdrop `.modal` + `.modal-box` ตาม UI_DESIGN_SYSTEM §9)
 * - เปิดแล้ว focus ช่องกรอกแรก (หรือปุ่มแรก), Tab วนอยู่ใน modal, ปิดแล้วคืน focus ให้ปุ่มที่เปิด
 * - "มีข้อมูลที่ยังไม่บันทึก" (dirty) = ผู้ใช้พิมพ์/เลือกค่าใน modal แล้ว (ตรวจจาก event input/change เอง ไม่ต้องให้ทุกฟอร์มเก็บ state)
 *   หรือผู้เรียกส่ง `dirty` มา (เช่น AI draft ที่สร้างขึ้นโดยไม่มีการพิมพ์)
 * - Escape ปิดเฉพาะ modal บนสุด — ถ้า dirty ถามยืนยันก่อน; คลิกพื้นหลังปิดได้เฉพาะตอนยังไม่ dirty (กันข้อมูลหายจากการคลิกพลาด)
 * - `backdropDismiss={false}` = คลิกพื้นหลังไม่ปิดเลย (เช่น modal แสดง secret ครั้งเดียว); การกันปิดระหว่าง busy อยู่ใน `onDismiss` ของผู้เรียก
 * - `boxClassName` แทน class ทั้งหมดของกล่อง (ใช้เมื่อ class เป็น expression) ส่วน `className` ต่อท้าย `modal-box` */
export function ModalShell({ labelledBy, label, className, boxClassName, boxStyle, onDismiss, dirty = false, backdropDismiss = true, confirmDirtyClose = true, children }: {
  labelledBy?: string; label?: string; className?: string; boxClassName?: string; boxStyle?: React.CSSProperties; onDismiss?: () => void;
  /** @deprecated ไม่มีผลแล้ว — dirty ตรวจจากการกรอกจริง; คงไว้ให้โค้ดเดิมที่ส่ง `form` ยัง compile ได้ */
  form?: boolean;
  dirty?: boolean; backdropDismiss?: boolean;
  /** false = Escape ปิดทันทีแม้มีการกรอก (ใช้กับกล่องยืนยัน/กรอกข้อความของ DialogHost เอง) */
  confirmDirtyClose?: boolean;
  children: React.ReactNode;
}) {
  const boxRef = useRef<HTMLDivElement>(null);
  const typedRef = useRef(false);
  const dismissRef = useRef(onDismiss);
  const dirtyRef = useRef(dirty);
  const confirmRef = useRef(confirmDirtyClose);
  useEffect(() => { dismissRef.current = onDismiss; dirtyRef.current = dirty; confirmRef.current = confirmDirtyClose; });
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
        if ((typedRef.current || dirtyRef.current) && confirmRef.current) {
          // กล่องยืนยันเป็น ModalShell อีกตัวที่ขึ้นบนสุด จึงรับ Escape/Enter ของตัวเอง
          void confirmDialog({ title: "ปิดโดยไม่บันทึก?", message: MODAL_DIRTY_CLOSE_CONFIRM, confirmLabel: "ปิดหน้าต่าง", cancelLabel: "กลับไปแก้ไข", tone: "danger" })
            .then((ok) => { if (ok) dismissRef.current?.(); });
          return;
        }
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
  }, []);
  const markTyped = () => { typedRef.current = true; };
  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby={labelledBy} aria-label={labelledBy ? undefined : label}
    onMouseDown={(e) => { if (e.target === e.currentTarget && backdropDismiss && !typedRef.current && !dirtyRef.current) onDismiss?.(); }}>
    <div ref={boxRef} className={boxClassName ?? (className ? `modal-box ${className}` : "modal-box")} style={boxStyle} onInput={markTyped} onChange={markTyped}>{children}</div>
  </div>;
}
