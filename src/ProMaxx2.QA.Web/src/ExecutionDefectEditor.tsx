import { useEffect, useRef, useState } from "react";
import { authHeaders } from "./api";

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

  const requestClose = () => {
    if (saving) return;
    if ((dirty || files.length > 0) && !window.confirm("ยังไม่ได้บันทึกข้อมูลที่แก้ไข ต้องการปิดแบบฟอร์มหรือไม่?")) return;
    onClose();
  };
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === "Escape") { event.preventDefault(); requestClose(); } };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  });

  const addFiles = (selected: File[]) => {
    if (selected.some((file) => !allowedTypes.includes(file.type))) { setError("รองรับเฉพาะรูป PNG, JPG และ WebP"); return; }
    if (selected.some((file) => file.size === 0 || file.size > 5_000_000)) { setError("รูปแต่ละไฟล์ต้องไม่เกิน 5 MB"); return; }
    const pendingSize = [...files.map((item) => item.file), ...selected].reduce((sum, file) => sum + file.size, 0);
    const existingSize = attachments.reduce((sum, item) => sum + item.size, 0);
    if (files.length + selected.length + attachments.length > 5 || pendingSize + existingSize > 20_000_000) { setError("แนบได้สูงสุด 5 รูป รวมไม่เกิน 20 MB (นับรวมรูปที่แนบไว้แล้ว)"); return; }
    setError("");
    setFiles((current) => [...current, ...selected.map((file) => ({ id: crypto.randomUUID(), file, url: URL.createObjectURL(file) }))]);
  };
  const removeFile = (id: string) => setFiles((current) => {
    const target = current.find((item) => item.id === id);
    if (target) URL.revokeObjectURL(target.url);
    return current.filter((item) => item.id !== id);
  });
  const deleteAttachment = async (item: Attachment) => {
    if (!record || !window.confirm(`ลบรูป ${item.fileName} ใช่หรือไม่?`)) return;
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

  return <div className="modal" role="presentation" onMouseDown={requestClose}>
    <div className="modal-box execution-defect-modal" role="dialog" aria-modal="true" aria-labelledby="execution-defect-title" onMouseDown={(event) => event.stopPropagation()}>
      <div className="modal-head"><div><h2 id="execution-defect-title">{record ? `จัดการ Defect ${record.defectCode}` : "แจ้ง Defect จากการทดสอบ"}</h2><small>{context.testCaseCode}{step ? ` · Step ${step.stepNo}` : ""} · {context.cycleCode}</small></div><button type="button" aria-label="ปิดแบบฟอร์ม Defect" disabled={saving} onClick={requestClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      <div className="execution-defect-content">
        {error && <div className="inline-alert error" role="alert">{error}</div>}
        <div className="execution-defect-context"><span>Test Case <b>{context.testCaseCode}</b></span><span>Build <b>{context.buildNumber || "-"}</b></span><span>Environment <b>{context.environmentName || "-"}</b></span></div>
        <div className="form-grid">
          <label className="full">ชื่อ Defect <span className="required">*</span><input ref={titleRef} value={title} maxLength={300} onChange={(event) => edit(setTitle)(event.target.value)} placeholder="สรุปปัญหาที่พบ" /></label>
          <label>Severity<select value={severity} onChange={(event) => edit(setSeverity)(event.target.value)}>{["Critical", "High", "Medium", "Low"].map((value) => <option key={value}>{value}</option>)}</select></label>
          <label>Status<select value={status} onChange={(event) => edit(setStatus)(event.target.value)}>{["Open", "In Progress", "Resolved", "Closed", "Rejected"].map((value) => <option key={value}>{value}</option>)}</select></label>
          <label className="full">รายละเอียด<textarea rows={4} value={description} onChange={(event) => edit(setDescription)(event.target.value)} placeholder="อธิบายปัญหาและบริบทที่พบ" /></label>
          <label className="full">ขั้นตอนการทำซ้ำ<textarea rows={3} value={steps} onChange={(event) => edit(setSteps)(event.target.value)} placeholder="ระบุขั้นตอนที่ทำให้เกิดปัญหา" /></label>
          <label>ผลที่คาดหวัง<textarea rows={3} value={expected} onChange={(event) => edit(setExpected)(event.target.value)} /></label>
          <label>ผลที่เกิดขึ้นจริง<textarea rows={3} value={actual} onChange={(event) => edit(setActual)(event.target.value)} /></label>
        </div>
        <section className="execution-defect-images" aria-labelledby="execution-defect-images-title">
          <div><h3 id="execution-defect-images-title">รูปภาพประกอบ</h3><small>PNG, JPG หรือ WebP · สูงสุด 5 รูป · รูปละไม่เกิน 5 MB · รวมไม่เกิน 20 MB</small></div>
          <label className="btn execution-defect-file-button"><span className="material-symbols-outlined" aria-hidden="true">add_photo_alternate</span> เพิ่มรูป<input type="file" accept="image/png,image/jpeg,image/webp" multiple onChange={(event) => { addFiles(Array.from(event.target.files ?? [])); event.target.value = ""; }} /></label>
          {(attachments.length > 0 || files.length > 0) && <div className="execution-defect-image-grid">
            {attachments.map((item) => <div className="execution-defect-image" key={item.attachmentId}>{item.url ? <img src={item.url} alt={item.fileName} /> : <span className="material-symbols-outlined" aria-hidden="true">image</span>}<span title={item.fileName}>{item.fileName}</span><button type="button" aria-label={`ลบรูป ${item.fileName}`} onClick={() => deleteAttachment(item)}>×</button></div>)}
            {files.map((item) => <div className="execution-defect-image" key={item.id}><img src={item.url} alt={`ตัวอย่าง ${item.file.name}`} /><span title={item.file.name}>{item.file.name}</span><button type="button" aria-label={`นำรูป ${item.file.name} ออก`} onClick={() => removeFile(item.id)}>×</button></div>)}
          </div>}
        </section>
      </div>
      <div className="modal-actions"><button className="btn" type="button" disabled={saving} onClick={requestClose}>ยกเลิก</button><button className="btn primary" type="button" disabled={saving || !title.trim()} onClick={save}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> {record ? "บันทึกการแก้ไข" : "สร้าง Defect"}</>}</button></div>
    </div>
  </div>;
}
