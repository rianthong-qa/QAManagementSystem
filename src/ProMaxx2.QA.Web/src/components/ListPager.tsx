import { PageNumbers } from "./PageNumbers";
import { PageSizeInput } from "./PageSizeInput";
import { LIST_PAGE_SIZE_MAX } from "./usePageSize";

/** แถบแบ่งหน้าของหน้ารายการ (Test Case / Test Suite / Test Cycle / Defect) — วางทั้งบน (`position="top"`) และล่างตาราง:
 *  จำนวนต่อหน้ากรอกเอง · หน้า x / y · ทั้งหมด · ก่อนหน้า · เลขหน้า · ถัดไป; เปลี่ยนจำนวนต่อหน้าแล้วกลับหน้า 1 (ผู้เรียกจัดการใน onPageSize) */
export function ListPager({ position, page, pageCount, totalCount, pageSize, onPage, onPageSize, disabled = false }: {
  position: "top" | "bottom";
  page: number;
  pageCount: number;
  totalCount: number;
  pageSize: number;
  onPage: (page: number) => void;
  onPageSize: (size: number) => void;
  disabled?: boolean;
}) {
  const current = Math.min(Math.max(1, page), pageCount);
  return (
    <nav className={`pagination suite-pagination is-${position}`} aria-label={position === "top" ? "แบ่งหน้า (บน)" : "แบ่งหน้า (ล่าง)"}>
      <label>แสดง<PageSizeInput value={pageSize} max={LIST_PAGE_SIZE_MAX} onChange={onPageSize} /> รายการ</label>
      <span>หน้า {current} / {pageCount} · ทั้งหมด {totalCount.toLocaleString()} รายการ</span>
      <button type="button" className="btn" disabled={disabled || current <= 1} onClick={() => onPage(current - 1)}><span className="material-symbols-outlined" aria-hidden="true">chevron_left</span> ก่อนหน้า</button>
      <PageNumbers page={current} pageCount={pageCount} onPage={onPage} />
      <button type="button" className="btn" disabled={disabled || current >= pageCount} onClick={() => onPage(current + 1)}>ถัดไป <span className="material-symbols-outlined" aria-hidden="true">chevron_right</span></button>
    </nav>
  );
}
