import { useEffect, useState } from "react";
import { PageSizeInput } from "./PageSizeInput";
import { pageWindow } from "./pageWindow";

// จอกลาง (≤1100px) แสดงรอบหน้าปัจจุบัน ±1 แทน ±2 ไม่ให้แถบแบ่งหน้าขึ้นบรรทัดใหม่
const NARROW_QUERY = "(max-width: 1100px)";
function useNarrow() {
  const [narrow, setNarrow] = useState(() => { try { return window.matchMedia(NARROW_QUERY).matches; } catch { return false; } });
  useEffect(() => {
    let media: MediaQueryList;
    try { media = window.matchMedia(NARROW_QUERY); } catch { return; }
    const update = () => setNarrow(media.matches);
    media.addEventListener("change", update);
    return () => media.removeEventListener("change", update);
  }, []);
  return narrow;
}

/** เลขหน้าให้กดระหว่างปุ่ม ก่อนหน้า/ถัดไป — Mobile (≤760px) ซ่อนเลขหน้า แสดงช่องกรอก "หน้า [n] / ทั้งหมด" แทน (CSS ใน TestManagement.css) */
export function PageNumbers({ page, pageCount, onPage }: { page: number; pageCount: number; onPage: (page: number) => void }) {
  const narrow = useNarrow();
  const current = Math.min(Math.max(1, page), pageCount);
  return (
    <>
      <span className="page-numbers">
        {pageWindow(current, pageCount, narrow ? 1 : 2).map((p, i) => p === "gap"
          ? <span key={`gap-${i}`} className="page-gap" aria-hidden="true">…</span>
          : <button key={p} type="button" className={`page-number${p === current ? " is-current" : ""}`} aria-current={p === current ? "page" : undefined} aria-label={`หน้า ${p}`} onClick={() => { if (p !== current) onPage(p); }}>{p}</button>)}
      </span>
      <span className="page-jump">
        <PageSizeInput value={current} max={pageCount} presets={[]} label="ไปหน้า" className="page-jump-input" onChange={onPage} />
        <span aria-hidden="true">/ {pageCount.toLocaleString()}</span>
      </span>
    </>
  );
}
