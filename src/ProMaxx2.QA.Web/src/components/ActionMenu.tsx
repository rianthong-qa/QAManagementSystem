import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import "./ActionMenu.css";

/** เมนูคำสั่งรอง (UI_DESIGN_SYSTEM §8) — ใช้ในหน้าต่าง Test Plan ("จัดการแผน") และ Execution Workspace ("ตั้งทุก Step") */
export type ActionMenuItem = { key: string; icon: string; label: string; hint?: string; danger?: boolean; disabled?: boolean; onSelect: () => void };

/** `hideLabel` = ปุ่มไอคอนอย่างเดียว (ชื่อเป็น sr-only) เช่นเมนู ⋯ ในแถวตาราง/การ์ด Board ของหน้า Defect */
export function ActionMenu({ label, items, disabled, icon = "more_horiz", className, hideLabel = false }: { label: string; items: ActionMenuItem[]; disabled: boolean; icon?: string; className?: string; hideLabel?: boolean }) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuId = useId();
  const enabledItems = () => [...(rootRef.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not(:disabled)') ?? [])];

  useEffect(() => {
    if (!open) return;
    enabledItems()[0]?.focus();
    const onPointerDown = (e: PointerEvent) => { if (!rootRef.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, [open]);

  if (!items.length) return null;
  const close = () => { setOpen(false); buttonRef.current?.focus(); };
  const onMenuKeyDown = (e: KeyboardEvent) => {
    // ModalShell ฟัง Escape ที่ document — หยุดไว้ที่นี่ไม่ให้หน้าต่างรายละเอียดปิดตาม
    if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); close(); return; }
    if (e.key === "Tab") { setOpen(false); return; }
    const list = enabledItems();
    if (!list.length || !["ArrowDown", "ArrowUp", "Home", "End"].includes(e.key)) return;
    e.preventDefault();
    const index = list.indexOf(document.activeElement as HTMLButtonElement);
    const next = e.key === "Home" ? 0 : e.key === "End" ? list.length - 1 : (index + (e.key === "ArrowDown" ? 1 : -1) + list.length) % list.length;
    list[next].focus();
  };

  return (
    <div className={`action-menu${className ? ` ${className}` : ""}`} ref={rootRef}>
      <button ref={buttonRef} type="button" className="btn" disabled={disabled} aria-haspopup="menu" aria-expanded={open} aria-controls={open ? menuId : undefined}
        onClick={() => setOpen((x) => !x)} onKeyDown={(e) => { if (e.key === "ArrowDown" && !open) { e.preventDefault(); setOpen(true); } }}>
        <span className="material-symbols-outlined" aria-hidden="true">{icon}</span>{hideLabel ? <span className="sr-only">{label}</span> : <> {label}</>}
      </button>
      {open && <div id={menuId} className="action-menu-list" role="menu" aria-label={label} onKeyDown={onMenuKeyDown}>
        {items.map((item) => (
          <button key={item.key} type="button" role="menuitem" className={item.danger ? "is-danger" : undefined} disabled={item.disabled} onClick={() => { setOpen(false); item.onSelect(); }}>
            <span className="material-symbols-outlined" aria-hidden="true">{item.icon}</span>
            <span><b>{item.label}</b>{item.hint && <small>{item.hint}</small>}</span>
          </button>
        ))}
      </div>}
    </div>
  );
}
