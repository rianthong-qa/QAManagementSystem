import { useEffect, useRef } from "react";
import { ModalShell } from "./ModalShell";
import "./ImageLightbox.css";

export type LightboxImage = { src?: string; name: string };

/** ดูรูปขนาดใหญ่ทีละรูปพร้อมเลื่อนดูรูปอื่นในชุดเดียวกัน — ปุ่ม ‹ ›, ปุ่มลูกศรซ้าย/ขวาบนคีย์บอร์ด, ปัดซ้าย/ขวาบนมือถือ
 * และแถบรูปย่อด้านล่าง; Escape/ปุ่ม ✕/คลิกพื้นหลังปิด (ผ่าน ModalShell) */
export function ImageLightbox({ images, index, onIndexChange, onClose }: { images: LightboxImage[]; index: number; onIndexChange: (index: number) => void; onClose: () => void }) {
  const count = images.length;
  const current = images[index];
  const touchX = useRef<number | null>(null);
  const go = (delta: number) => { if (count > 1) onIndexChange((index + delta + count) % count); };
  const goRef = useRef(go);
  useEffect(() => { goRef.current = go; });
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "ArrowLeft") { e.preventDefault(); goRef.current(-1); }
      else if (e.key === "ArrowRight") { e.preventDefault(); goRef.current(1); }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, []);
  // ให้รูปย่อที่เลือกอยู่เลื่อนเข้ามาในแถบเสมอเมื่อมีหลายรูป
  const stripRef = useRef<HTMLDivElement>(null);
  useEffect(() => { stripRef.current?.querySelector<HTMLElement>(".is-active")?.scrollIntoView({ block: "nearest", inline: "center" }); }, [index]);
  if (!current) return null;

  return (
    <ModalShell label={`รูปภาพ ${index + 1} จาก ${count}: ${current.name}`} boxClassName="image-lightbox" onDismiss={onClose}>
      <div className="image-lightbox-head">
        <span className="image-lightbox-count">{index + 1} / {count}</span>
        <span className="image-lightbox-name" title={current.name}>{current.name}</span>
        {current.src && <a className="image-lightbox-icon" href={current.src} target="_blank" rel="noreferrer" aria-label="เปิดรูปต้นฉบับในแท็บใหม่"><span className="material-symbols-outlined" aria-hidden="true">open_in_new</span></a>}
        <button type="button" className="image-lightbox-icon" onClick={onClose} aria-label="ปิดรูปภาพ" autoFocus><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>
      <div className="image-lightbox-stage"
        onTouchStart={(e) => { touchX.current = e.touches[0]?.clientX ?? null; }}
        onTouchEnd={(e) => { const start = touchX.current; touchX.current = null; const end = e.changedTouches[0]?.clientX; if (start != null && end != null && Math.abs(end - start) > 50) go(end < start ? 1 : -1); }}>
        {current.src ? <img src={current.src} alt={current.name} /> : <span className="spinner" role="status" aria-label="กำลังโหลดรูป" />}
        {count > 1 && <>
          <button type="button" className="image-lightbox-nav prev" onClick={() => go(-1)} aria-label="รูปก่อนหน้า"><span className="material-symbols-outlined" aria-hidden="true">chevron_left</span></button>
          <button type="button" className="image-lightbox-nav next" onClick={() => go(1)} aria-label="รูปถัดไป"><span className="material-symbols-outlined" aria-hidden="true">chevron_right</span></button>
        </>}
      </div>
      {count > 1 && (
        <div className="image-lightbox-strip" ref={stripRef}>
          {images.map((img, i) => (
            <button type="button" key={`${i}-${img.name}`} className={i === index ? "is-active" : undefined} onClick={() => onIndexChange(i)} aria-label={`ดูรูปที่ ${i + 1}: ${img.name}`} aria-current={i === index ? "true" : undefined}>
              {img.src ? <img src={img.src} alt="" /> : <span className="material-symbols-outlined" aria-hidden="true">image</span>}
            </button>
          ))}
        </div>
      )}
    </ModalShell>
  );
}
