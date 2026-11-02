import { useEffect, useId, useRef, useState } from "react";

// เมนูบัญชีที่มุมขวาของ Topbar — รวม "บัญชี CRM ของฉัน" และ "ออกจากระบบ" ไว้ใต้ avatar
// พฤติกรรมคีย์บอร์ดตามมาตรฐานเมนูคำสั่งรองใน UI_DESIGN_SYSTEM.md §8 (reference: ActionMenu ใน pages/TestPlansPage.tsx)
export function AccountMenu({ displayName, roleLabel, onOpenCrm, onLogout }: {
  displayName: string;
  roleLabel: string;
  onOpenCrm: () => void;
  onLogout: () => void;
}) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuId = useId();
  const initials = displayName.slice(0, 2).toUpperCase();
  const items = () => [...(rootRef.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not(:disabled)') ?? [])];

  useEffect(() => {
    if (!open) return;
    items()[0]?.focus();
    const onPointerDown = (e: PointerEvent) => { if (!rootRef.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, [open]);

  const close = () => { setOpen(false); buttonRef.current?.focus(); };
  const select = (action: () => void) => { setOpen(false); action(); };
  const onMenuKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); close(); return; }
    if (e.key === "Tab") { setOpen(false); return; }
    const list = items();
    if (!list.length || !["ArrowDown", "ArrowUp", "Home", "End"].includes(e.key)) return;
    e.preventDefault();
    const index = list.indexOf(document.activeElement as HTMLButtonElement);
    const next = e.key === "Home" ? 0 : e.key === "End" ? list.length - 1 : (index + (e.key === "ArrowDown" ? 1 : -1) + list.length) % list.length;
    list[next].focus();
  };

  return (
    <div className="acct" ref={rootRef}>
      <button ref={buttonRef} type="button" className="acct-trigger" aria-haspopup="menu" aria-expanded={open} aria-controls={open ? menuId : undefined}
        aria-label={`บัญชีของ ${displayName}`} title={`${displayName} · ${roleLabel}`}
        onClick={() => setOpen((x) => !x)} onKeyDown={(e) => { if (e.key === "ArrowDown" && !open) { e.preventDefault(); setOpen(true); } }}>
        <span className="avatar" aria-hidden="true">{initials}</span>
        <span className="acct-copy" aria-hidden="true"><b>{displayName}</b><small>{roleLabel}</small></span>
        <span className="material-symbols-outlined acct-caret" aria-hidden="true">expand_more</span>
      </button>
      {open && <div id={menuId} className="acct-menu" role="menu" aria-label="บัญชีของฉัน" onKeyDown={onMenuKeyDown}>
        <div className="acct-menu-head" aria-hidden="true">
          <span className="avatar">{initials}</span>
          <span><b>{displayName}</b><small>{roleLabel}</small></span>
        </div>
        <button type="button" role="menuitem" onClick={() => select(onOpenCrm)}>
          <span className="material-symbols-outlined" aria-hidden="true">support_agent</span>
          <span><b>บัญชี CRM ของฉัน</b><small>Login เข้า CRM สำหรับส่ง Defect และคอมเมนต์</small></span>
        </button>
        <div className="acct-menu-sep" role="separator" />
        <button type="button" role="menuitem" className="is-danger" onClick={() => select(onLogout)}>
          <span className="material-symbols-outlined" aria-hidden="true">logout</span>
          <span><b>ออกจากระบบ</b></span>
        </button>
      </div>}
    </div>
  );
}
