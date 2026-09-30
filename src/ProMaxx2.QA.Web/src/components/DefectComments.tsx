import { useEffect, useRef, useState } from "react";
import { formatThaiDateTime } from "../dateTime";
import { ImageLightbox } from "./ImageLightbox";
import { type DefectComment, type DefectCommentAttachment, toLightboxImages } from "./defectCommentUtils";
import "./DefectComments.css";

const COMMENT_MAX_IMAGES = 5;
const MAX_IMAGE_BYTES = 5_000_000;
const MAX_TOTAL_BYTES = 20_000_000;
const ALLOWED_TYPES = ["image/png", "image/jpeg", "image/webp"];

const initials = (name: string) => name.replace(/^CRM · /, "").trim().slice(0, 2).toUpperCase() || "?";

/** รายการคอมเมนต์ (เก่า → ใหม่) — แต่ละคอมเมนต์แสดงข้อความและรูปย่อด้านล่าง คลิกรูปเพื่อเปิด ImageLightbox ของชุดรูปในคอมเมนต์นั้น
 * `imageSrc` คืน URL ของรูป (หน้าแชร์ใช้ URL ตรง, หน้าใน QA Hub ใช้ blob URL ที่โหลดพร้อม token — undefined = กำลังโหลด) */
export function DefectCommentList({ comments, imageSrc, emptyText = "ยังไม่มีคอมเมนต์" }: { comments: DefectComment[]; imageSrc: (attachmentId: string) => string | undefined; emptyText?: string }) {
  const [viewer, setViewer] = useState<{ attachments: DefectCommentAttachment[]; index: number } | null>(null);
  if (!comments.length) return <p className="defect-comments-empty">{emptyText}</p>;
  return (
    <>
      <ol className="defect-comments">
        {comments.map((c) => (
          <li key={c.commentId} className={`defect-comment${c.source === "Crm" ? " is-crm" : ""}`}>
            <span className="defect-comment-avatar" aria-hidden="true">{initials(c.authorName)}</span>
            <div className="defect-comment-main">
              <div className="defect-comment-meta">
                <b>{c.authorName}</b>
                {c.source === "Crm" && <span className="defect-comment-source">จาก CRM</span>}
                <time dateTime={c.createdAt}>{formatThaiDateTime(c.createdAt, { dateStyle: "medium", timeStyle: "short" })}</time>
              </div>
              {c.body && <p className="defect-comment-body">{c.body}</p>}
              {c.attachments.length > 0 && (
                <div className="defect-comment-thumbs">
                  {c.attachments.map((a, i) => {
                    const src = imageSrc(a.attachmentId);
                    return (
                      <button type="button" key={a.attachmentId} aria-label={`ดูรูป ${a.fileName} ขนาดใหญ่`} title={a.fileName}
                        onClick={() => setViewer({ attachments: c.attachments, index: i })}>
                        {src ? <img src={src} alt={a.fileName} loading="lazy" /> : <span className="material-symbols-outlined" aria-hidden="true">image</span>}
                      </button>
                    );
                  })}
                </div>
              )}
            </div>
          </li>
        ))}
      </ol>
      {viewer && <ImageLightbox images={toLightboxImages(viewer.attachments, imageSrc)} index={viewer.index} onIndexChange={(index) => setViewer({ ...viewer, index })} onClose={() => setViewer(null)} />}
    </>
  );
}

type PendingImage = { id: string; file: File; url: string };

/** ช่องเขียนคอมเมนต์: ข้อความ + รูปสูงสุด 5 รูป (PNG/JPG/WebP, รูปละ ≤ 5 MB, รวม ≤ 20 MB) — ส่งได้เมื่อมีข้อความหรือรูปอย่างน้อยหนึ่งอย่าง
 * Ctrl/⌘ + Enter = ส่ง; `onSubmit` คืน true เมื่อบันทึกสำเร็จเพื่อล้างช่อง */
export function DefectCommentComposer({ onSubmit, disabled }: { onSubmit: (body: string, files: File[]) => Promise<boolean>; disabled?: boolean }) {
  const [text, setText] = useState("");
  const [images, setImages] = useState<PendingImage[]>([]);
  const [error, setError] = useState("");
  const [sending, setSending] = useState(false);
  const imagesRef = useRef<PendingImage[]>([]);
  imagesRef.current = images;
  useEffect(() => () => imagesRef.current.forEach((x) => URL.revokeObjectURL(x.url)), []);

  const add = (selected: File[]) => {
    if (!selected.length) return;
    if (selected.some((f) => !ALLOWED_TYPES.includes(f.type))) { setError("รองรับเฉพาะรูป PNG, JPG และ WebP"); return; }
    if (selected.some((f) => f.size === 0 || f.size > MAX_IMAGE_BYTES)) { setError("รูปแต่ละไฟล์ต้องไม่เกิน 5 MB"); return; }
    if (images.length + selected.length > COMMENT_MAX_IMAGES) { setError(`แนบได้สูงสุด ${COMMENT_MAX_IMAGES} รูปต่อคอมเมนต์`); return; }
    if ([...images.map((x) => x.file), ...selected].reduce((sum, f) => sum + f.size, 0) > MAX_TOTAL_BYTES) { setError("รูปรวมกันต้องไม่เกิน 20 MB"); return; }
    setError("");
    setImages((cur) => [...cur, ...selected.map((file) => ({ id: crypto.randomUUID(), file, url: URL.createObjectURL(file) }))]);
  };
  const remove = (id: string) => setImages((cur) => {
    const target = cur.find((x) => x.id === id);
    if (target) URL.revokeObjectURL(target.url);
    return cur.filter((x) => x.id !== id);
  });
  const canSend = !sending && !disabled && (text.trim().length > 0 || images.length > 0);
  const send = async () => {
    if (!canSend) return;
    setSending(true); setError("");
    try {
      if (await onSubmit(text.trim(), images.map((x) => x.file))) {
        images.forEach((x) => URL.revokeObjectURL(x.url));
        setImages([]); setText("");
      }
    } catch (e) { setError(e instanceof Error ? e.message : "ส่งคอมเมนต์ไม่สำเร็จ"); }
    finally { setSending(false); }
  };

  return (
    <div className="defect-comment-composer"
      onPaste={(e) => { const pasted = Array.from(e.clipboardData.files).filter((f) => f.type.startsWith("image/")); if (pasted.length) { e.preventDefault(); add(pasted); } }}>
      <textarea value={text} onChange={(e) => setText(e.target.value)} rows={3} maxLength={4000} disabled={sending || disabled}
        placeholder="เขียนคอมเมนต์... (แนบรูปได้ หรือวางรูปจากคลิปบอร์ด · Ctrl + Enter เพื่อส่ง)" aria-label="ข้อความคอมเมนต์"
        onKeyDown={(e) => { if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) { e.preventDefault(); void send(); } }} />
      {images.length > 0 && (
        <div className="defect-comment-thumbs is-pending">
          {images.map((x) => (
            <span key={x.id} className="defect-comment-pending">
              <img src={x.url} alt={`รูปที่จะแนบ ${x.file.name}`} />
              <button type="button" onClick={() => remove(x.id)} disabled={sending} aria-label={`นำรูป ${x.file.name} ออก`}>×</button>
            </span>
          ))}
        </div>
      )}
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      <div className="defect-comment-actions">
        <label className={`btn defect-comment-attach${images.length >= COMMENT_MAX_IMAGES || sending ? " is-disabled" : ""}`}>
          <span className="material-symbols-outlined" aria-hidden="true">add_photo_alternate</span> แนบรูป ({images.length}/{COMMENT_MAX_IMAGES})
          <input type="file" accept="image/png,image/jpeg,image/webp" multiple disabled={images.length >= COMMENT_MAX_IMAGES || sending || disabled}
            onChange={(e) => { add(Array.from(e.target.files ?? [])); e.target.value = ""; }} />
        </label>
        <button type="button" className="btn primary" onClick={send} disabled={!canSend}>
          {sending ? <><span className="spinner inline" aria-hidden="true" /> กำลังส่ง...</> : <><span className="material-symbols-outlined" aria-hidden="true">send</span> ส่งคอมเมนต์</>}
        </button>
      </div>
    </div>
  );
}
