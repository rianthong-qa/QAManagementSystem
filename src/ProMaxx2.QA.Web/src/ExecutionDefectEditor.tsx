import { useEffect, useRef, useState, type DragEvent } from "react";
import { confirmDialog } from "./components/dialogStore";
import { ModalShell } from "./components/ModalShell";
import { ImageLightbox } from "./components/ImageLightbox";
import { authHeaders } from "./api";
import "./ExecutionDefectEditor.css";

export type WorkspaceDefect = {
  defectId: string;
  defectCode: string;
  title: string;
  severity: string;
  status: string;
  assigneeUserId?: string | null;
  description?: string | null;
  stepsToReproduce?: string | null;
  expectedResult?: string | null;
  actualResult?: string | null;
};

export type WorkspaceDefectContext = {
  projectId: string;
  releaseId?: string;
  buildId?: string;
  moduleId?: string;
  testCaseId: string;
  testCaseCode: string;
  testCaseTitle: string;
  cycleCode: string;
  buildNumber: string;
  environmentName: string;
  testerName: string;
  step?: { stepNo: number; action: string; expectedResult: string; actualResult: string };
};

type Attachment = { attachmentId: string; fileName: string; size: number; url?: string };
type PendingFile = { id: string; file: File; url: string };
const allowedTypes = ["image/png", "image/jpeg", "image/webp"];
const maxImages = 5;
const maxTotalBytes = 20_000_000;
const formatMb = (bytes: number) => bytes > 0 && bytes < 100_000 ? "0.1" : (bytes / 1_000_000).toLocaleString("th-TH", { maximumFractionDigits: 1 });
// คำอธิบายระดับความรุนแรงให้ผู้ทดสอบเลือกได้ตรงกัน — tone ตรงกับ defectSeverityTones ของหน้า Defect
const severities = [
  { value: "Critical", tone: "red", hint: "ระบบใช้งานไม่ได้ ข้อมูลเสียหาย หรือไม่มีทางเลี่ยง" },
  { value: "High", tone: "yellow", hint: "ฟังก์ชันหลักทำงานผิด กระทบงานของผู้ใช้มาก" },
  { value: "Medium", tone: "blue", hint: "ทำงานผิดแต่มีทางเลี่ยง หรือกระทบบางกรณี" },
  { value: "Low", tone: "green", hint: "เล็กน้อย เช่น ข้อความ การจัดวาง หรือความสวยงาม" },
];

export function ExecutionDefectEditor({ apiUrl, context, existing, onClose, onSaved, onPartialSave }: {
  apiUrl: string;
  context: WorkspaceDefectContext;
  existing?: WorkspaceDefect;
  onClose: () => void;
  onSaved: (defect: WorkspaceDefect) => void;
  // เรียกเมื่อ Defect ถูกสร้าง/บันทึกแล้วแต่ขั้นต่อไป (link/อัปโหลดรูป) ล้มเหลว — ให้หน้าหลัก refresh รายการ
  // Linked Defects ได้ทันทีโดยไม่ปิด modal ผู้ใช้ยังกดบันทึกซ้ำเพื่อทำขั้นที่ค้างได้
  onPartialSave?: (defect: WorkspaceDefect) => void;
}) {
  const step = context.step;
  const [record, setRecord] = useState<WorkspaceDefect | undefined>(existing);
  const [linked, setLinked] = useState(Boolean(existing));
  const [title, setTitle] = useState(existing?.title ?? (step ? `${context.testCaseCode} Step ${step.stepNo} Fail: ${step.action}`.slice(0, 300) : `${context.testCaseCode} ล้มเหลว: ${context.testCaseTitle}`.slice(0, 300)));
  const [severity, setSeverity] = useState(existing?.severity ?? "Medium");
  const [status, setStatus] = useState(existing?.status ?? "Open");
  const [description, setDescription] = useState(existing?.description ?? [
    `Test Case: ${context.testCaseCode} - ${context.testCaseTitle}`,
    `Test Cycle: ${context.cycleCode}`,
    `Build: ${context.buildNumber || "-"}`,
    `Environment: ${context.environmentName || "-"}`,
    `Tester: ${context.testerName || "-"}`,
    ...(step ? [`Step: ${step.stepNo}`] : []),
  ].join("\n"));
  const [steps, setSteps] = useState(existing?.stepsToReproduce ?? step?.action ?? "");
  const [expected, setExpected] = useState(existing?.expectedResult ?? step?.expectedResult ?? "");
  const [actual, setActual] = useState(existing?.actualResult ?? step?.actualResult ?? "");
  const [files, setFiles] = useState<PendingFile[]>([]);
  const [attachments, setAttachments] = useState<Attachment[]>([]);
  const [saving, setSaving] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [error, setError] = useState("");
  const [preview, setPreview] = useState<number | null>(null);
  const titleRef = useRef<HTMLInputElement>(null);
  // Defect ที่เพิ่งสร้างใน modal นี้ — ไม่ต้องโหลดรูปจาก server ซ้ำระหว่างที่การอัปโหลดรอบแรกยังทำงานอยู่
  const justCreatedId = useRef<string | null>(null);
  const filesRef = useRef<PendingFile[]>([]);
  filesRef.current = files;
  const auth = authHeaders();
  const edit = <T,>(setter: (value: T) => void) => (value: T) => { setter(value); setDirty(true); };

  useEffect(() => { titleRef.current?.focus(); }, []);
  useEffect(() => () => filesRef.current.forEach((item) => URL.revokeObjectURL(item.url)), []);
  useEffect(() => {
    if (!record || justCreatedId.current === record.defectId) return;
    const controller = new AbortController();
    const urls: string[] = [];
    fetch(`${apiUrl}/defects/${record.defectId}/attachments`, { headers: auth, signal: controller.signal })
      .then((response) => response.ok ? response.json() as Promise<Attachment[]> : Promise.reject(new Error("โหลดรูปภาพไม่สำเร็จ")))
      .then(async (rows) => {
        const loaded = await Promise.all(rows.map(async (item) => {
          const response = await fetch(`${apiUrl}/defects/${record.defectId}/attachments/${item.attachmentId}`, { headers: auth, signal: controller.signal });
          if (!response.ok) return item;
          const url = URL.createObjectURL(await response.blob());
          urls.push(url);
          return { ...item, url };
        }));
        if (!controller.signal.aborted) setAttachments(loaded);
      }).catch(() => { if (!controller.signal.aborted) setError("โหลดรูปภาพที่แนบไว้ไม่สำเร็จ"); });
    return () => { controller.abort(); urls.forEach((url) => URL.revokeObjectURL(url)); };
  // The auth token and record ID are fixed for the lifetime of this modal.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiUrl, record?.defectId]);


  // ModalShell ถามยืนยันเองเมื่อกด Escape/คลิกพื้นหลังตอนมีข้อมูลค้าง — ปุ่ม "ยกเลิก"/✕ ใช้ทางนี้
  const requestClose = async () => {
    if (saving) return;
    if ((dirty || files.length > 0) && !await confirmDialog({ title: "ปิดโดยไม่บันทึก?", message: "ข้อมูลและรูปที่ยังไม่บันทึกจะหายไป", confirmLabel: "ปิดหน้าต่าง", cancelLabel: "กลับไปแก้ไข", tone: "danger" })) return;
    onClose();
  };

  const addFiles = (selected: File[]) => {
    if (!selected.length) return;
    if (selected.some((file) => !allowedTypes.includes(file.type))) { setError("รองรับเฉพาะรูป PNG, JPG และ WebP"); return; }
    if (selected.some((file) => file.size === 0 || file.size > 5_000_000)) { setError("รูปแต่ละไฟล์ต้องไม่เกิน 5 MB"); return; }
    const pendingSize = [...files.map((item) => item.file), ...selected].reduce((sum, file) => sum + file.size, 0);
    const existingSize = attachments.reduce((sum, item) => sum + item.size, 0);
    if (files.length + selected.length + attachments.length > maxImages || pendingSize + existingSize > maxTotalBytes) { setError(`แนบได้สูงสุด ${maxImages} รูป รวมไม่เกิน 20 MB (นับรวมรูปที่แนบไว้แล้ว)`); return; }
    setError("");
    setFiles((current) => [...current, ...selected.map((file) => ({ id: crypto.randomUUID(), file, url: URL.createObjectURL(file) }))]);
  };
  const addFilesRef = useRef(addFiles);
  addFilesRef.current = addFiles;
  // วางรูปจากคลิปบอร์ด (Ctrl+V) ได้ทุกที่ในหน้าต่าง — เช่น หลังจับภาพหน้าจอด้วย Win+Shift+S / PrtSc
  // ดักเฉพาะเมื่อมีรูป ข้อความยังวางลงช่องกรอกได้ตามปกติ; ชื่อไฟล์จากคลิปบอร์ดมักเป็น image.png ทุกรูป จึงตั้งชื่อใหม่
  useEffect(() => {
    const onPaste = (event: ClipboardEvent) => {
      const images = Array.from(event.clipboardData?.items ?? []).filter((item) => item.kind === "file" && item.type.startsWith("image/")).map((item) => item.getAsFile()).filter((file): file is File => !!file);
      if (!images.length) return;
      event.preventDefault();
      const stamp = new Date().toTimeString().slice(0, 8).replace(/:/g, "");
      addFilesRef.current(images.map((file, i) => new File([file], `screenshot-${stamp}${images.length > 1 ? `-${i + 1}` : ""}.${file.type.split("/")[1] === "jpeg" ? "jpg" : file.type.split("/")[1]}`, { type: file.type })));
    };
    window.addEventListener("paste", onPaste);
    return () => window.removeEventListener("paste", onPaste);
  }, []);
  // ลากไฟล์มาวางได้ทั้งหน้าต่าง: นับ enter/leave เพราะ dragleave ยิงทุกครั้งที่เมาส์ข้าม element ลูก
  const dragDepth = useRef(0);
  const [dragging, setDragging] = useState(false);
  const hasFiles = (event: DragEvent) => Array.from(event.dataTransfer.types).includes("Files");
  const dragHandlers = {
    onDragEnter: (event: DragEvent) => { if (!hasFiles(event)) return; event.preventDefault(); dragDepth.current += 1; setDragging(true); },
    onDragOver: (event: DragEvent) => { if (!hasFiles(event)) return; event.preventDefault(); event.dataTransfer.dropEffect = "copy"; },
    onDragLeave: (event: DragEvent) => { if (!hasFiles(event)) return; dragDepth.current = Math.max(0, dragDepth.current - 1); if (!dragDepth.current) setDragging(false); },
    onDrop: (event: DragEvent) => { if (!hasFiles(event)) return; event.preventDefault(); dragDepth.current = 0; setDragging(false); addFiles(Array.from(event.dataTransfer.files)); },
  };
  const removeFile = (id: string) => setFiles((current) => {
    const target = current.find((item) => item.id === id);
    if (target) URL.revokeObjectURL(target.url);
    return current.filter((item) => item.id !== id);
  });
  const deleteAttachment = async (item: Attachment) => {
    if (!record || !await confirmDialog({ title: "ลบรูปภาพ?", message: `ลบรูป ${item.fileName} ออกจาก ${record.defectCode} ทันที`, confirmLabel: "ลบรูป", tone: "danger" })) return;
    try {
      const response = await fetch(`${apiUrl}/defects/${record.defectId}/attachments/${item.attachmentId}`, { method: "DELETE", headers: auth });
      if (!response.ok) { setError("ลบรูปภาพไม่สำเร็จ"); return; }
      if (item.url) URL.revokeObjectURL(item.url);
      setAttachments((current) => current.filter((x) => x.attachmentId !== item.attachmentId));
    } catch { setError("ลบรูปภาพไม่สำเร็จ กรุณาตรวจสอบการเชื่อมต่อ"); }
  };

  const save = async () => {
    if (!title.trim() || saving) return;
    setSaving(true); setError("");
    let defect = record;
    let saved = false;
    try {
      // ส่ง assigneeUserId เดิมกลับไปเสมอ — PUT แทนค่าทั้งก้อน ถ้าไม่ส่งจะล้างผู้รับผิดชอบของ Defect ทิ้ง
      const body = { projectId: context.projectId, releaseId: context.releaseId || null, buildId: context.buildId || null, moduleId: context.moduleId || null, title: title.trim(), severity, status, description: description.trim() || null, stepsToReproduce: steps.trim() || null, expectedResult: expected.trim() || null, actualResult: actual.trim() || null, assigneeUserId: defect?.assigneeUserId ?? null };
      const response = await fetch(defect ? `${apiUrl}/defects/${defect.defectId}` : `${apiUrl}/defects`, { method: defect ? "PUT" : "POST", headers: { ...auth, "Content-Type": "application/json" }, body: JSON.stringify(body) });
      if (!response.ok) { const problem = await response.json().catch(() => null); throw new Error(problem?.detail ?? "บันทึก Defect ไม่สำเร็จ"); }
      const wasNew = !defect;
      defect = await response.json() as WorkspaceDefect;
      saved = true;
      if (wasNew) justCreatedId.current = defect.defectId;
      setRecord(defect);
      setDirty(false);
      if (!linked) {
        const link = await fetch(`${apiUrl}/defects/${defect.defectId}/test-cases`, { method: "POST", headers: { ...auth, "Content-Type": "application/json" }, body: JSON.stringify({ testCaseId: context.testCaseId }) });
        if (!link.ok && link.status !== 409) throw new Error(`สร้าง ${defect.defectCode} แล้ว แต่เชื่อม Test Case ไม่สำเร็จ กรุณากดบันทึกอีกครั้ง`);
        setLinked(true);
      }
      if (files.length > 0) {
        const form = new FormData();
        files.forEach((item) => form.append("files", item.file));
        const upload = await fetch(`${apiUrl}/defects/${defect.defectId}/attachments`, { method: "POST", headers: auth, body: form });
        if (!upload.ok) { const problem = await upload.json().catch(() => null); throw new Error(`${defect.defectCode} บันทึกแล้ว แต่แนบรูปไม่สำเร็จ: ${problem?.detail ?? "กรุณาลองใหม่"}`); }
        files.forEach((item) => URL.revokeObjectURL(item.url));
        setFiles([]);
      }
      onSaved(defect);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "บันทึก Defect ไม่สำเร็จ");
      if (saved && defect) onPartialSave?.(defect);
    }
    finally { setSaving(false); }
  };

  const images: { key: string; name: string; url?: string; size: number; isNew: boolean; remove: () => void }[] = [
    ...attachments.map((item) => ({ key: item.attachmentId, name: item.fileName, url: item.url, size: item.size, isNew: false, remove: () => void deleteAttachment(item) })),
    ...files.map((item) => ({ key: item.id, name: item.file.name, url: item.url, size: item.file.size, isNew: true, remove: () => removeFile(item.id) })),
  ];
  const usedBytes = images.reduce((sum, item) => sum + item.size, 0);
  const full = images.length >= maxImages;
  const severityInfo = severities.find((x) => x.value === severity) ?? severities[2];

  return <>
  <ModalShell labelledBy="edm-title" className="edm" dirty={dirty || files.length > 0} onDismiss={saving ? undefined : onClose}>
    <div className={`edm-frame${dragging ? " is-dragging" : ""}`} {...dragHandlers}>
      <header className="edm-head">
        <div className="edm-head-text">
          <h2 id="edm-title">{record ? `จัดการ Defect ${record.defectCode}` : "แจ้ง Defect จากการทดสอบ"}</h2>
          <dl className="edm-context">
            <div><dt>Test Case</dt><dd title={context.testCaseTitle}>{context.testCaseCode}</dd></div>
            {step && <div><dt>Step</dt><dd>{step.stepNo}</dd></div>}
            <div><dt>Cycle</dt><dd>{context.cycleCode}</dd></div>
            <div><dt>Build</dt><dd>{context.buildNumber || "-"}</dd></div>
            <div><dt>Environment</dt><dd>{context.environmentName || "-"}</dd></div>
          </dl>
        </div>
        <button type="button" className="edm-close" aria-label="ปิดแบบฟอร์ม Defect" disabled={saving} onClick={() => void requestClose()}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </header>
      <div className="edm-body">
        <div className="edm-main">
          {error && <div className="inline-alert error" role="alert">{error}</div>}
          <label className="edm-field"><span>ชื่อ Defect <span className="required">*</span></span><input ref={titleRef} value={title} maxLength={300} onChange={(event) => edit(setTitle)(event.target.value)} placeholder="สรุปปัญหาที่พบในบรรทัดเดียว" aria-invalid={!title.trim() || undefined} /></label>
          <div className="edm-row">
            <fieldset className="edm-severity">
              <legend>ความรุนแรง (Severity)</legend>
              <div className="edm-segment">{severities.map((x) => <label key={x.value} className={`tone-${x.tone}${severity === x.value ? " selected" : ""}`} title={x.hint}><input type="radio" name="edm-severity" value={x.value} checked={severity === x.value} onChange={() => edit(setSeverity)(x.value)} /><span className="edm-dot" aria-hidden="true" />{x.value}</label>)}</div>
              <small>{severityInfo.hint}</small>
            </fieldset>
            <label className="edm-field edm-status">สถานะ<select value={status} onChange={(event) => edit(setStatus)(event.target.value)}>{["Open", "In Progress", "Resolved", "Closed", "Rejected"].map((value) => <option key={value}>{value}</option>)}</select></label>
          </div>
          <div className="edm-compare">
            <label className="edm-field edm-actual"><span><span className="material-symbols-outlined" aria-hidden="true">cancel</span>ผลที่เกิดขึ้นจริง</span><textarea rows={4} value={actual} onChange={(event) => edit(setActual)(event.target.value)} placeholder="สิ่งที่ระบบแสดงหรือทำจริง เช่น ข้อความ error" /></label>
            <label className="edm-field edm-expected"><span><span className="material-symbols-outlined" aria-hidden="true">check_circle</span>ผลที่คาดหวัง</span><textarea rows={4} value={expected} onChange={(event) => edit(setExpected)(event.target.value)} placeholder="สิ่งที่ควรเกิดขึ้น" /></label>
          </div>
          <label className="edm-field">ขั้นตอนการทำซ้ำ<textarea rows={3} value={steps} onChange={(event) => edit(setSteps)(event.target.value)} placeholder={"1. เปิดเมนู ...\n2. กด ..."} /></label>
          <label className="edm-field">รายละเอียดเพิ่มเติม <small>(ระบบใส่ข้อมูลการทดสอบให้แล้ว เพิ่มเติมได้)</small><textarea rows={4} value={description} onChange={(event) => edit(setDescription)(event.target.value)} placeholder="อธิบายปัญหาและบริบทที่พบ" /></label>
        </div>
        <aside className="edm-side" aria-labelledby="edm-images-title">
          <div className="edm-side-head"><h3 id="edm-images-title">รูปภาพประกอบ</h3><span className={full ? "is-full" : undefined}>{images.length}/{maxImages} รูป · {formatMb(usedBytes)}/20 MB</span></div>
          <label className={`edm-drop${full ? " is-disabled" : ""}`}>
            <input type="file" accept="image/png,image/jpeg,image/webp" multiple disabled={full} onChange={(event) => { addFiles(Array.from(event.target.files ?? [])); event.target.value = ""; }} />
            <span className="material-symbols-outlined" aria-hidden="true">add_photo_alternate</span>
            <b>{full ? `แนบครบ ${maxImages} รูปแล้ว` : <>ลากรูปมาวาง หรือ <u>เลือกไฟล์</u></>}</b>
            <small className="edm-paste-hint">วางภาพหน้าจอได้เลยด้วย <kbd>Ctrl</kbd>+<kbd>V</kbd></small>
            <small>PNG, JPG, WebP · รูปละไม่เกิน 5 MB</small>
          </label>
          {images.length > 0 ? <ul className="edm-images">{images.map((item, i) => <li key={item.key}>
            <button type="button" className="edm-thumb" onClick={() => setPreview(i)} aria-label={`ดูรูป ${item.name}`}>{item.url ? <img src={item.url} alt="" /> : <span className="material-symbols-outlined" aria-hidden="true">image</span>}</button>
            <span className="edm-image-name" title={item.name}>{item.isNew && <em>ใหม่</em>}{item.name}</span>
            <button type="button" className="edm-image-remove" aria-label={item.isNew ? `นำรูป ${item.name} ออก` : `ลบรูป ${item.name}`} title={item.isNew ? "นำออก" : "ลบรูป"} onClick={item.remove}><span className="material-symbols-outlined" aria-hidden="true">delete</span></button>
          </li>)}</ul> : <p className="edm-no-image">ยังไม่มีรูป — ภาพหน้าจอตอนเกิดปัญหาช่วยให้ Dev แก้ได้เร็วขึ้น</p>}
        </aside>
        {dragging && <div className="edm-drop-overlay" aria-hidden="true"><span className="material-symbols-outlined">download</span>ปล่อยเพื่อแนบรูป</div>}
      </div>
      <footer className="edm-foot">
        <span className={title.trim() ? "edm-ready" : "edm-missing"} role="status">{!title.trim() ? "ยังขาด: ชื่อ Defect" : files.length ? `รูปใหม่ ${files.length} รูปจะอัปโหลดเมื่อบันทึก` : "ข้อมูลครบ พร้อมบันทึก"}</span>
        <button className="btn" type="button" disabled={saving} onClick={() => void requestClose()}>ยกเลิก</button>
        <button className="btn primary" type="button" disabled={saving || !title.trim()} onClick={save}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> {record ? "บันทึกการแก้ไข" : "สร้าง Defect"}</>}</button>
      </footer>
    </div>
  </ModalShell>
  {preview !== null && images[preview] && <ImageLightbox images={images.map((item) => ({ src: item.url, name: item.name }))} index={preview} onIndexChange={setPreview} onClose={() => setPreview(null)} />}
  </>;
}
