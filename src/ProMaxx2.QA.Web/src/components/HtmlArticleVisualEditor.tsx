import { useEffect, useMemo, useRef, useState } from "react";
import { ModalShell } from "./ModalShell";
import { promptDialog } from "./dialogStore";
import { htmlArticleEditDocument, htmlArticleEditSandbox, htmlArticleFromEditDocument } from "../shared/htmlArticle";
import "./HtmlArticleVisualEditor.css";

type Command = { icon: string; label: string; run: (doc: Document) => void | Promise<void> };

const exec = (command: string, value?: string) => (doc: Document) => { doc.execCommand(command, false, value); };
const blocks = [
  { value: "p", label: "ย่อหน้า" }, { value: "h1", label: "หัวข้อ 1" }, { value: "h2", label: "หัวข้อ 2" }, { value: "h3", label: "หัวข้อ 3" }, { value: "pre", label: "โค้ด" },
];
const readAsDataUrl = (file: Blob) => new Promise<string>((resolve, reject) => {
  const reader = new FileReader();
  reader.onload = () => resolve(String(reader.result));
  reader.onerror = () => reject(reader.error);
  reader.readAsDataURL(file);
});

/**
 * แก้ไขบทความ HTML บนหน้าตาจริง (designMode ใน iframe ที่ไม่รันสคริปต์ — ดู htmlArticleEditSandbox)
 * รูปที่แทรก/วางเป็น data: base64 แล้วแยกเก็บตอนบันทึก (externalizeHtmlAssets) เหมือนตอนนำเข้าไฟล์
 * onApply คืน HTML ใหม่ — throw เพื่อแสดง error ในหน้าต่างนี้ (เช่น บันทึกไม่สำเร็จ)
 */
export function HtmlArticleVisualEditor({ html, title, applyLabel, onApply, onCancel }: {
  html: string; title: string; applyLabel: string; onApply: (html: string) => Promise<void> | void; onCancel: () => void;
}) {
  const frameRef = useRef<HTMLIFrameElement>(null);
  const imageRef = useRef<HTMLInputElement>(null);
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  // srcDoc คำนวณครั้งเดียว — ถ้าเปลี่ยนระหว่างแก้ iframe จะโหลดใหม่และการแก้หาย
  const srcDoc = useMemo(() => htmlArticleEditDocument(html), [html]);
  const hadDoctype = useMemo(() => /^\s*<!doctype/i.test(html), [html]);
  const doc = () => frameRef.current?.contentDocument ?? null;

  useEffect(() => {
    const frame = frameRef.current;
    if (!frame) return;
    const onLoad = () => {
      const d = frame.contentDocument;
      if (!d) return;
      d.designMode = "on";
      try { d.execCommand("styleWithCSS", false, "false"); } catch { /* เบราว์เซอร์เก่า */ }
      d.addEventListener("input", () => setDirty(true));
      // วางรูปจากคลิปบอร์ด → แทรกเป็น data: (แยกเก็บตอนบันทึก); ข้อความวางตามปกติ
      d.addEventListener("paste", (e) => {
        const file = [...(e.clipboardData?.files ?? [])].find((f) => f.type.startsWith("image/"));
        if (!file) return;
        e.preventDefault();
        void readAsDataUrl(file).then((url) => { d.execCommand("insertImage", false, url); setDirty(true); });
      });
      // Escape ใน iframe ไม่ถึง ModalShell — ส่งต่อให้ปิดหน้าต่างแบบเดียวกับหน้าแม่
      d.addEventListener("keydown", (e) => { if (e.key === "Escape") document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })); });
    };
    frame.addEventListener("load", onLoad);
    // srcdoc อาจโหลดเสร็จก่อน effect นี้ทำงาน
    if (frame.contentDocument?.readyState === "complete" && frame.contentDocument.body?.childNodes.length) onLoad();
    return () => frame.removeEventListener("load", onLoad);
  }, []);

  const run = async (command: Command["run"]) => {
    const d = doc();
    if (!d || busy) return;
    frameRef.current?.contentWindow?.focus();
    await command(d);
    setDirty(true);
  };
  const insertLink = async (d: Document) => {
    const selection = d.getSelection();
    const range = selection && selection.rangeCount > 0 ? selection.getRangeAt(0).cloneRange() : null;
    const url = await promptDialog({ title: "ใส่ลิงก์", message: "ลิงก์จะเปิดในแท็บใหม่เมื่อผู้อ่านกด", placeholder: "https://", initialValue: "https://", confirmLabel: "ใส่ลิงก์", required: true });
    if (!url?.trim() || !/^(https?:|mailto:|#)/i.test(url.trim())) return;
    frameRef.current?.contentWindow?.focus();
    if (range) { selection?.removeAllRanges(); selection?.addRange(range); }
    d.execCommand(range && !range.collapsed ? "createLink" : "insertHTML", false, range && !range.collapsed ? url.trim() : `<a href="${url.trim().replace(/"/g, "&quot;")}">${url.trim().replace(/</g, "&lt;")}</a>`);
  };
  const commands: Command[] = [
    { icon: "undo", label: "เลิกทำ", run: exec("undo") },
    { icon: "redo", label: "ทำซ้ำ", run: exec("redo") },
    { icon: "format_bold", label: "ตัวหนา", run: exec("bold") },
    { icon: "format_italic", label: "ตัวเอียง", run: exec("italic") },
    { icon: "format_underlined", label: "ขีดเส้นใต้", run: exec("underline") },
    { icon: "format_list_bulleted", label: "รายการ", run: exec("insertUnorderedList") },
    { icon: "format_list_numbered", label: "ลำดับเลข", run: exec("insertOrderedList") },
    { icon: "link", label: "ลิงก์", run: insertLink },
    { icon: "link_off", label: "เอาลิงก์ออก", run: exec("unlink") },
    { icon: "image", label: "แทรกรูป", run: () => imageRef.current?.click() },
    { icon: "format_clear", label: "ล้างรูปแบบ", run: exec("removeFormat") },
  ];

  const onImage = async (file: File | undefined) => {
    const d = doc();
    if (!file || !d) return;
    if (!file.type.startsWith("image/")) { setError(`"${file.name}" ไม่ใช่ไฟล์รูป`); return; }
    setError("");
    const url = await readAsDataUrl(file);
    frameRef.current?.contentWindow?.focus();
    d.execCommand("insertImage", false, url);
    setDirty(true);
  };

  const apply = async () => {
    const d = doc();
    if (!d) return;
    setBusy(true); setError("");
    try { await onApply(htmlArticleFromEditDocument(d, hadDoctype)); }
    catch (e) { setError(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ"); }
    finally { setBusy(false); }
  };

  return <ModalShell labelledBy="kn-visual-title" className="kn-visual-editor" dirty={dirty} backdropDismiss={false} onDismiss={() => { if (!busy) onCancel(); }}>
    <div className="modal-head">
      <div>
        <span className="kn-eyebrow">แก้ไขบนหน้าตาจริง</span>
        <h2 id="kn-visual-title">{title}</h2>
      </div>
      <button type="button" className="modal-close" aria-label="ปิด" disabled={busy} onClick={onCancel}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
    </div>
    <div className="kn-visual-toolbar" role="toolbar" aria-label="จัดรูปแบบเนื้อหา">
      <select aria-label="รูปแบบย่อหน้า" defaultValue="" disabled={busy} onChange={(e) => { const value = e.target.value; e.target.value = ""; if (value) void run(exec("formatBlock", value)); }}>
        <option value="" disabled>รูปแบบย่อหน้า</option>
        {blocks.map((b) => <option key={b.value} value={b.value}>{b.label}</option>)}
      </select>
      {commands.map((c) => <button key={c.icon} type="button" title={c.label} aria-label={c.label} disabled={busy}
        onMouseDown={(e) => e.preventDefault()} onClick={() => void run(c.run)}>
        <span className="material-symbols-outlined" aria-hidden="true">{c.icon}</span>
      </button>)}
      <input ref={imageRef} type="file" accept="image/*" hidden onChange={(e) => { void onImage(e.target.files?.[0]); e.target.value = ""; }} />
    </div>
    <p className="kn-visual-note"><span className="material-symbols-outlined" aria-hidden="true">info</span>คลิกที่ข้อความเพื่อพิมพ์แก้ · วางรูปด้วย <kbd>Ctrl</kbd>+<kbd>V</kbd> ได้ · สคริปต์ในไฟล์ไม่ทำงานระหว่างแก้ ส่วนที่สคริปต์สร้าง (เช่น แท็บที่ซ่อนอยู่) จึงอาจไม่แสดงในหน้านี้</p>
    <div className="kn-visual-canvas">
      <iframe ref={frameRef} title={`แก้ไขเนื้อหา ${title}`} sandbox={htmlArticleEditSandbox} referrerPolicy="no-referrer" srcDoc={srcDoc} />
    </div>
    {error && <p className="kn-form-error" role="alert">{error}</p>}
    <div className="modal-actions">
      <span className="kn-visual-status" role="status">{dirty ? "มีการแก้ไขที่ยังไม่บันทึก" : "ยังไม่มีการแก้ไข"}</span>
      <button type="button" className="btn" disabled={busy} onClick={onCancel}>ยกเลิก</button>
      <button type="button" className="btn primary" disabled={busy || !dirty} onClick={() => void apply()}>
        {busy ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span>{applyLabel}</>}
      </button>
    </div>
  </ModalShell>;
}
