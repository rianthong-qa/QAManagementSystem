import { useId, useState } from "react";

/** ช่องจำนวนรายการต่อหน้าแบบกรอกตัวเลขเองได้ (มีค่าแนะนำใน datalist) — ยืนยันค่าเมื่อกด Enter หรือออกจากช่อง ไม่ยิงคำขอทุกตัวอักษร
 *  ใช้เป็นช่อง "ไปหน้า" ใน PageNumbers ด้วย (presets ว่าง = ไม่มี datalist) */
export function PageSizeInput({ value, onChange, min = 1, max = 500, presets = [30, 50, 100, 150], label = "จำนวนรายการต่อหน้า", className = "page-size-input" }: {
  value: number;
  onChange: (size: number) => void;
  min?: number;
  max?: number;
  presets?: number[];
  label?: string;
  className?: string;
}) {
  const listId = useId();
  const [draft, setDraft] = useState<string | null>(null);
  const commit = (text = draft) => {
    if (text === null) return;
    const parsed = Math.round(Number(text));
    setDraft(null);
    if (!text.trim() || !Number.isFinite(parsed)) return;
    const next = Math.min(max, Math.max(min, parsed));
    if (next !== value) onChange(next);
  };
  return (
    <>
      <input
        type="number"
        inputMode="numeric"
        className={className}
        min={min}
        max={max}
        step={1}
        list={presets.length ? listId : undefined}
        aria-label={`${label} (${min}–${max})`}
        title={`กรอกจำนวน ${min}–${max} แล้วกด Enter`}
        value={draft ?? String(value)}
        // เลือกจาก datalist (ไม่ใช่การพิมพ์) = ยืนยันทันที
        onChange={e => { const inputType = (e.nativeEvent as InputEvent).inputType; if (!inputType || inputType === "insertReplacementText") commit(e.target.value); else setDraft(e.target.value); }}
        onBlur={() => commit()}
        onKeyDown={e => { if (e.key === "Enter") { e.preventDefault(); commit(); } else if (e.key === "Escape") setDraft(null); }}
      />
      {presets.length > 0 && <datalist id={listId}>{presets.map(size => <option key={size} value={size} />)}</datalist>}
    </>
  );
}
