import { useEffect, useId, useMemo, useRef, useState } from "react";
import type { TestCycleItem } from "../shared/appShared";
import "./CyclePicker.css";

/** ช่องเลือก Test Cycle แบบค้นหาได้ (แทน <select> ที่มีหลายร้อยรายการเรียงยาว) — จัดกลุ่มตาม Module แรกของ Cycle,
 * ในกลุ่มเรียงรหัสใหม่ → เก่า, แต่ละรายการแสดงรหัส/ชื่อ/Release·Build·Environment และความคืบหน้า
 * คีย์บอร์ด: พิมพ์เพื่อค้นหา, ↑/↓ เลื่อน, Enter เลือก, Esc ปิด (ARIA combobox + listbox) */
export function CyclePicker({ cycles, value, onChange, placeholder = "เลือก Test Cycle", label = "Test Cycle", disabled }: {
  cycles: TestCycleItem[];
  value: string;
  onChange: (cycleId: string) => void;
  placeholder?: string;
  /** ชื่อของช่องสำหรับ screen reader — ปุ่มอ่านเป็น "label: ค่าที่เลือก" */
  label?: string;
  disabled?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const rootRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const id = useId();
  const selected = cycles.find((x) => x.testCycleId === value);

  const groups = useMemo(() => {
    const q = query.trim().toLowerCase();
    const matches = cycles.filter((x) => !q || [x.cycleCode, x.cycleName, x.suiteName, x.releaseCode, x.buildNumber, x.environmentName, ...(x.modules ?? []).map((m) => `${m.moduleCode} ${m.moduleName}`)]
      .filter(Boolean).join(" ").toLowerCase().includes(q));
    const byModule = new Map<string, TestCycleItem[]>();
    for (const x of matches) {
      const key = x.modules?.[0]?.moduleName ?? "ไม่ระบุ Module";
      byModule.set(key, [...(byModule.get(key) ?? []), x]);
    }
    return [...byModule.entries()]
      .sort(([a], [b]) => a.localeCompare(b, "th"))
      .map(([name, items]) => ({ name, items: items.sort((a, b) => b.cycleCode.localeCompare(a.cycleCode, undefined, { numeric: true })) }));
  }, [cycles, query]);
  const flat = useMemo(() => groups.flatMap((g) => g.items), [groups]);

  const close = () => { setOpen(false); setQuery(""); };
  const choose = (cycleId: string) => { onChange(cycleId); close(); };
  const openPicker = () => {
    if (disabled) return;
    setOpen(true);
    setQuery("");
  };

  // เปิดแล้วชี้รายการที่เลือกอยู่ (หรือรายการแรก) และ focus ช่องค้นหา; คลิกนอกแผงเพื่อปิด
  useEffect(() => {
    if (!open) return;
    const index = flat.findIndex((x) => x.testCycleId === value);
    setActive(index >= 0 ? index : 0);
    inputRef.current?.focus();
    const onDown = (e: MouseEvent) => { if (!rootRef.current?.contains(e.target as Node)) close(); };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);
  useEffect(() => { setActive(0); }, [query]);
  useEffect(() => {
    if (!open) return;
    listRef.current?.querySelector<HTMLElement>(`[data-index="${active}"]`)?.scrollIntoView({ block: "nearest" });
  }, [active, open]);

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "ArrowDown") { e.preventDefault(); setActive((i) => Math.min(flat.length - 1, i + 1)); }
    else if (e.key === "ArrowUp") { e.preventDefault(); setActive((i) => Math.max(0, i - 1)); }
    else if (e.key === "Enter") { e.preventDefault(); const item = flat[active]; if (item) choose(item.testCycleId); }
    else if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); close(); }
  };

  let index = -1;
  return (
    <div className={`cycle-picker${open ? " is-open" : ""}`} ref={rootRef}>
      <button type="button" className="cycle-picker-trigger" aria-haspopup="listbox" aria-expanded={open} disabled={disabled}
        aria-label={`${label}: ${selected ? `${selected.cycleCode} ${selected.cycleName}` : placeholder}`}
        onClick={() => (open ? close() : openPicker())}
        onKeyDown={(e) => { if (!open && (e.key === "ArrowDown" || e.key === "Enter" || e.key === " ")) { e.preventDefault(); openPicker(); } }}>
        {selected ? (
          <span className="cycle-picker-value"><b>{selected.cycleCode}</b><span>{selected.cycleName}</span></span>
        ) : <span className="cycle-picker-placeholder">{placeholder}</span>}
        <span className="material-symbols-outlined" aria-hidden="true">{open ? "expand_less" : "expand_more"}</span>
      </button>
      {open && (
        <div className="cycle-picker-panel">
          <div className="cycle-picker-search">
            <span className="material-symbols-outlined" aria-hidden="true">search</span>
            <input ref={inputRef} role="combobox" aria-expanded="true" aria-controls={`${id}-list`} aria-autocomplete="list"
              aria-activedescendant={flat[active] ? `${id}-${flat[active].testCycleId}` : undefined} aria-label="ค้นหา Test Cycle"
              placeholder="ค้นหารหัส ชื่อ Suite Module หรือ Build" value={query} onChange={(e) => setQuery(e.target.value)} onKeyDown={onKeyDown} />
            <small>{flat.length === cycles.length ? `${cycles.length} Cycle` : `${flat.length} / ${cycles.length}`}</small>
          </div>
          <div className="cycle-picker-list" role="listbox" id={`${id}-list`} aria-label="Test Cycle" ref={listRef}>
            {groups.length ? groups.map((g) => (
              <div key={g.name} role="group" aria-label={g.name}>
                <div className="cycle-picker-group" aria-hidden="true">{g.name}<span>{g.items.length}</span></div>
                {g.items.map((x) => {
                  index += 1;
                  const i = index;
                  return (
                    <div key={x.testCycleId} id={`${id}-${x.testCycleId}`} data-index={i} role="option" aria-selected={x.testCycleId === value}
                      className={`cycle-picker-option${i === active ? " is-active" : ""}${x.testCycleId === value ? " is-selected" : ""}`}
                      onMouseEnter={() => setActive(i)} onMouseDown={(e) => e.preventDefault()} onClick={() => choose(x.testCycleId)}>
                      <div className="cycle-picker-option-main">
                        <b>{x.cycleCode}</b>
                        <span>{x.cycleName}</span>
                        <small>{[x.releaseCode, x.buildNumber && `Build ${x.buildNumber}`, x.environmentName].filter(Boolean).join(" · ")}</small>
                      </div>
                      <div className="cycle-picker-progress" aria-label={`ความคืบหน้า ${x.progressPercent}%`}>
                        <span className="cycle-picker-track"><span style={{ width: `${Math.min(100, Math.max(0, x.progressPercent))}%` }} /></span>
                        <small><b>{x.progressPercent}%</b> {x.executedCount}/{x.caseCount}</small>
                      </div>
                    </div>
                  );
                })}
              </div>
            )) : <p className="cycle-picker-empty">ไม่พบ Test Cycle ที่ตรงกับคำค้น</p>}
          </div>
        </div>
      )}
    </div>
  );
}
