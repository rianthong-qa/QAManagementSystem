// ModalShell ย้ายไปเป็น component กลางของทั้งแอป (UI รอบ 2) — re-export ไว้ให้ไฟล์ในหน้า Automation import ที่เดิมได้
export { ModalShell } from "../components/ModalShell";

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
