import { useCallback, useEffect, useRef, useState, type ChangeEvent, type DragEvent } from "react";
import { apiFetch, apiUrl, getJson, isAbortError } from "../api";
import { downloadKnowledgeFile } from "./knowledgeFiles";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
import { confirmDialog, notify } from "../components/dialogStore";
import { useDebounced } from "../components/useDebounced";
import { formatThaiDateTime } from "../dateTime";
import type { ProjectItem } from "../shared/types";
import "./KnowledgeDocuments.css";

// คลังเอกสาร (AI_KNOWLEDGE_CHAT_PLAN.md Phase 3): PDF / DOCX / MD / TXT — สกัดข้อความเข้าคลังความรู้ให้ค้นหา แชท และ Claude (MCP) ใช้ได้

type KnowledgeFile = { knowledgeFileId: string; fileName: string; extension: string; sizeBytes: number; projectId?: string | null; projectName?: string | null; description?: string | null; origin: "Upload" | "Import"; importPath?: string | null; extractedChars: number; extractError?: string | null; uploadedAt: string; uploadedByName?: string | null; updatedAt?: string | null; indexState?: string | null; indexedAt?: string | null };
type UploadResult = { fileName: string; success: boolean; message?: string | null };
type ImportResult = { added: number; updated: number; unchanged: number; failed: number; messages: string[] };

const maxFiles = 10, maxFileBytes = 20_000_000, maxTotalBytes = 50_000_000;
const accept = ".pdf,.docx,.md,.txt";
const typeIcon: Record<string, string> = { ".pdf": "picture_as_pdf", ".docx": "description", ".md": "article", ".txt": "notes" };
const formatSize = (bytes: number) => bytes >= 1_000_000 ? `${(bytes / 1_000_000).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1000))} KB`;

const indexInfo = (f: KnowledgeFile) => f.extractError ? { label: "สกัดข้อความไม่ได้", tone: "red" }
  : f.indexState === "Indexed" ? { label: "ค้นหาได้แล้ว", tone: "green" }
  : f.indexState === "Failed" ? { label: "สร้างดัชนีไม่สำเร็จ", tone: "red" }
  : { label: "รอสร้างดัชนี", tone: "yellow" };

export function KnowledgeDocuments({ projects, canEdit, isSysAdmin, highlightId }: { projects: ProjectItem[]; canEdit: boolean; isSysAdmin: boolean; highlightId?: string | null }) {
  const [search, setSearch] = useState("");
  const debounced = useDebounced(search, 300);
  const [projectId, setProjectId] = useState("");
  const [rows, setRows] = useState<KnowledgeFile[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [version, setVersion] = useState(0);
  const [uploading, setUploading] = useState(false);
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<ImportResult | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    const params = new URLSearchParams();
    if (debounced.trim()) params.set("search", debounced.trim());
    if (projectId) params.set("projectId", projectId);
    setLoading(true); setError("");
    getJson<KnowledgeFile[]>(`${apiUrl}/knowledge/files?${params}`, controller.signal)
      .then((data) => setRows(Array.isArray(data) ? data : []))
      .catch((e) => { if (!isAbortError(e)) { setRows([]); setError(e instanceof Error ? e.message : "โหลดเอกสารไม่สำเร็จ"); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [debounced, projectId, version]);

  const download = async (f: KnowledgeFile) => {
    try { await downloadKnowledgeFile(f.knowledgeFileId, f.fileName); } catch (e) { notify(e instanceof Error ? e.message : "ดาวน์โหลดไม่สำเร็จ", "error"); }
  };

  const remove = async (f: KnowledgeFile) => {
    if (!await confirmDialog({ title: "ลบเอกสาร", message: `ลบ "${f.fileName}" ออกจากคลังความรู้? การค้นหา ผู้ช่วย AI และ Claude จะไม่ใช้เอกสารนี้อีก${f.origin === "Import" ? " (เอกสารระบบจะถูกนำเข้าใหม่เมื่อกดนำเข้าอีกครั้ง)" : ""}`, confirmLabel: "ลบเอกสาร", tone: "danger" })) return;
    try { await apiFetch(`/knowledge/files/${f.knowledgeFileId}`, { method: "DELETE", fallbackMessage: "ลบเอกสารไม่สำเร็จ" }); notify("ลบเอกสารแล้ว", "success"); setVersion((x) => x + 1); }
    catch (e) { notify(e instanceof Error ? e.message : "ลบเอกสารไม่สำเร็จ", "error"); }
  };

  const importSystem = async () => {
    if (!await confirmDialog({ title: "นำเข้าเอกสารระบบ", message: "นำเข้าไฟล์ .md / .txt จากโฟลเดอร์เอกสารของระบบ (Document/ProMaxx2_MD) เป็นความรู้กลาง — ไฟล์ที่นำเข้าแล้วและไม่เปลี่ยนจะถูกข้าม", confirmLabel: "นำเข้า" })) return;
    setImporting(true);
    try { const result = await apiFetch<ImportResult>("/knowledge/files/import-system", { method: "POST", fallbackMessage: "นำเข้าไม่สำเร็จ" }); setImportResult(result); setVersion((x) => x + 1); }
    catch (e) { notify(e instanceof Error ? e.message : "นำเข้าไม่สำเร็จ", "error"); }
    finally { setImporting(false); }
  };

  const filtered = Boolean(debounced.trim() || projectId);
  return <div className="kn-docs">
    <div className="kn-toolbar">
      <label className="kn-search-input"><span className="material-symbols-outlined" aria-hidden="true">search</span><input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="ค้นหาชื่อไฟล์หรือรายละเอียด" aria-label="ค้นหาเอกสาร" /></label>
      <select value={projectId} onChange={(e) => setProjectId(e.target.value)} aria-label="Project"><option value="">ทุก Project</option>{projects.map((p) => <option key={p.projectId} value={p.projectId}>{p.projectName}</option>)}</select>
      {isSysAdmin && <button type="button" className="btn" onClick={() => void importSystem()} disabled={importing}>{importing ? <><span className="spinner inline" aria-hidden="true" /> กำลังนำเข้า...</> : <><span className="material-symbols-outlined" aria-hidden="true">drive_folder_upload</span>นำเข้าเอกสารระบบ</>}</button>}
      {canEdit && <button type="button" className="btn primary" onClick={() => setUploading(true)}><span className="material-symbols-outlined" aria-hidden="true">upload_file</span>อัปโหลดเอกสาร</button>}
    </div>
    {importResult && <div className="kn-docs-import" role="status">
      <span className="material-symbols-outlined" aria-hidden="true">task_alt</span>
      <div><b>นำเข้าเอกสารระบบแล้ว — ใหม่ {importResult.added} · อัปเดต {importResult.updated} · ไม่เปลี่ยน {importResult.unchanged}{importResult.failed ? ` · ไม่สำเร็จ ${importResult.failed}` : ""}</b>
        {importResult.messages.length > 0 && <ul>{importResult.messages.slice(0, 8).map((m) => <li key={m}>{m}</li>)}</ul>}</div>
      <button type="button" className="kn-docs-dismiss" aria-label="ปิดผลการนำเข้า" onClick={() => setImportResult(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
    </div>}
    {error ? <div className="kn-empty kn-error" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><b>{error}</b><button type="button" className="btn" onClick={() => setVersion((x) => x + 1)}>ลองใหม่</button></div>
      : loading && rows.length === 0 ? <div className="kn-empty" role="status"><span className="spinner" aria-hidden="true" /><span>กำลังโหลดเอกสาร...</span></div>
      : rows.length === 0 ? <div className="kn-empty">
        <span className="material-symbols-outlined" aria-hidden="true">folder_open</span>
        <b>{filtered ? "ไม่พบเอกสารตามตัวกรอง" : "ยังไม่มีเอกสารในคลังความรู้"}</b>
        <span>{filtered ? "ลองล้างคำค้นหรือเปลี่ยน Project" : "อัปโหลดคู่มือ สเปกโมดูล Requirement หรือขั้นตอนงาน (PDF, Word, Markdown, Text) เพื่อให้ค้นหาและผู้ช่วย AI ใช้ตอบได้"}</span>
        {canEdit && !filtered && <button type="button" className="btn primary" onClick={() => setUploading(true)}>อัปโหลดเอกสารแรก</button>}
      </div>
      : <div className="table-wrap"><table className="table-cards kn-table kn-docs-table" aria-busy={loading}>
        <thead><tr><th>เอกสาร</th><th>Project</th><th>สถานะ</th><th>อัปเดต</th><th><span className="sr-only">การจัดการ</span></th></tr></thead>
        <tbody>{rows.map((f) => { const state = indexInfo(f); return <tr key={f.knowledgeFileId} className={f.knowledgeFileId === highlightId ? "is-highlight" : ""}>
          <td>
            <div className="kn-docs-name">
              <span className="material-symbols-outlined" aria-hidden="true">{typeIcon[f.extension] ?? "draft"}</span>
              <div><button type="button" className="kn-link" onClick={() => void download(f)} title="ดาวน์โหลด">{f.fileName}</button>
                <small className="kn-excerpt">{f.extension.slice(1).toUpperCase()} · {formatSize(f.sizeBytes)}{f.extractedChars ? ` · ${f.extractedChars.toLocaleString("th-TH")} ตัวอักษร` : ""}{f.origin === "Import" ? " · เอกสารระบบ" : ""}</small>
                {f.description && f.origin !== "Import" && <small className="kn-excerpt">{f.description}</small>}
                {f.extractError && <small className="kn-docs-error">{f.extractError}</small>}</div>
            </div>
          </td>
          <td>{f.projectName ?? "ความรู้กลาง"}</td>
          <td><Badge tone={state.tone}>{state.label}</Badge></td>
          <td><span className="kn-updated">{formatThaiDateTime(f.updatedAt ?? f.uploadedAt)}</span><small>{f.uploadedByName ?? ""}</small></td>
          <td className="kn-row-actions">
            <button type="button" className="table-action icon-only" title="ดาวน์โหลด" aria-label={`ดาวน์โหลด ${f.fileName}`} onClick={() => void download(f)}><span className="material-symbols-outlined" aria-hidden="true">download</span></button>
            {canEdit && <button type="button" className="table-action danger-action icon-only" title="ลบ" aria-label={`ลบ ${f.fileName}`} onClick={() => void remove(f)}><span className="material-symbols-outlined" aria-hidden="true">delete</span></button>}
          </td>
        </tr>; })}</tbody>
      </table></div>}
    {uploading && <UploadModal projects={projects} onClose={() => setUploading(false)} onUploaded={() => setVersion((x) => x + 1)} />}
  </div>;
}

function UploadModal({ projects, onClose, onUploaded }: { projects: ProjectItem[]; onClose: () => void; onUploaded: () => void }) {
  const [files, setFiles] = useState<File[]>([]);
  const [projectId, setProjectId] = useState("");
  const [description, setDescription] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [results, setResults] = useState<UploadResult[] | null>(null);
  const [dragging, setDragging] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const total = files.reduce((sum, f) => sum + f.size, 0);
  const problems = [
    files.length > maxFiles && `เลือกได้สูงสุด ${maxFiles} ไฟล์`,
    files.some((f) => f.size > maxFileBytes) && "มีไฟล์ใหญ่เกิน 20 MB",
    total > maxTotalBytes && "ขนาดรวมเกิน 50 MB",
    files.some((f) => !accept.split(",").includes(f.name.slice(f.name.lastIndexOf(".")).toLowerCase())) && "มีไฟล์ชนิดที่ไม่รองรับ",
  ].filter(Boolean) as string[];

  const addFiles = useCallback((list: FileList | null) => {
    if (!list) return;
    setFiles((current) => [...current, ...Array.from(list)].filter((f, i, all) => all.findIndex((x) => x.name === f.name && x.size === f.size) === i));
  }, []);
  const onPick = (e: ChangeEvent<HTMLInputElement>) => { addFiles(e.target.files); e.target.value = ""; };
  const onDrop = (e: DragEvent<HTMLDivElement>) => { e.preventDefault(); setDragging(false); addFiles(e.dataTransfer.files); };

  const upload = async () => {
    setSaving(true); setError("");
    const form = new FormData();
    files.forEach((f) => form.append("files", f, f.name));
    if (projectId) form.append("projectId", projectId);
    if (description.trim()) form.append("description", description.trim());
    try {
      const response = await apiFetch<UploadResult[]>("/knowledge/files", { method: "POST", form, fallbackMessage: "อัปโหลดไม่สำเร็จ" });
      setResults(response);
      onUploaded();
      const ok = response.filter((r) => r.success).length;
      notify(ok === response.length ? `อัปโหลด ${ok} ไฟล์แล้ว — ค้นหาได้ภายในไม่กี่นาที` : `อัปโหลดสำเร็จ ${ok}/${response.length} ไฟล์`, ok === response.length ? "success" : "info");
    } catch (e) { setError(e instanceof Error ? e.message : "อัปโหลดไม่สำเร็จ"); }
    finally { setSaving(false); }
  };

  return <ModalShell labelledBy="kn-upload-title" className="kn-upload-modal" onDismiss={onClose} dirty={files.length > 0 && !results}>
    <div className="modal-head">
      <div><span className="kn-eyebrow">คลังความรู้</span><h2 id="kn-upload-title">{results ? "ผลการอัปโหลด" : "อัปโหลดเอกสาร"}</h2></div>
      <button type="button" className="modal-close" aria-label="ปิด" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
    </div>
    {results ? <ul className="kn-upload-results">{results.map((r, i) => <li key={`${r.fileName}-${i}`} className={r.success ? "ok" : "fail"}>
      <span className="material-symbols-outlined" aria-hidden="true">{r.success ? "check_circle" : "error"}</span>
      <div><b>{r.fileName}</b><small>{r.message}</small></div>
    </li>)}</ul> : <div className="kn-upload-body">
      <div className={`kn-dropzone${dragging ? " is-dragging" : ""}`} onDragOver={(e) => { e.preventDefault(); setDragging(true); }} onDragLeave={() => setDragging(false)} onDrop={onDrop}>
        <span className="material-symbols-outlined" aria-hidden="true">cloud_upload</span>
        <b>ลากไฟล์มาวางที่นี่</b>
        <span>หรือ <button type="button" className="kn-link-inline" onClick={() => inputRef.current?.click()}>เลือกไฟล์</button> — PDF, Word (.docx), Markdown, Text · ครั้งละ ≤ {maxFiles} ไฟล์ · ไฟล์ละ ≤ 20 MB</span>
        <input ref={inputRef} type="file" multiple accept={accept} onChange={onPick} className="sr-only" tabIndex={-1} aria-hidden="true" />
      </div>
      {files.length > 0 && <ul className="kn-upload-list">{files.map((f) => <li key={`${f.name}-${f.size}`}>
        <span className="material-symbols-outlined" aria-hidden="true">{typeIcon[f.name.slice(f.name.lastIndexOf(".")).toLowerCase()] ?? "draft"}</span>
        <span className="kn-upload-file">{f.name}</span><small>{formatSize(f.size)}</small>
        <button type="button" className="table-action icon-only" aria-label={`นำ ${f.name} ออก`} title="นำออก" onClick={() => setFiles((current) => current.filter((x) => x !== f))}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </li>)}</ul>}
      <div className="form-grid kn-editor-grid">
        <label>Project<select value={projectId} onChange={(e) => setProjectId(e.target.value)}><option value="">ความรู้กลาง (ทุก Project)</option>{projects.map((p) => <option key={p.projectId} value={p.projectId}>{p.projectName}</option>)}</select></label>
        <label>รายละเอียด<input value={description} maxLength={1000} onChange={(e) => setDescription(e.target.value)} placeholder="เช่น คู่มือการใช้งานโมดูลขาย v2" /></label>
      </div>
      <p className="kn-upload-note"><span className="material-symbols-outlined" aria-hidden="true">info</span>PDF ที่สแกนเป็นภาพจะสกัดข้อความไม่ได้ (ยังไม่รองรับ OCR) · อีเมล/เบอร์โทร/รหัสผ่านถูกปิดก่อนเข้าคลังความรู้</p>
      {error && <p className="kn-form-error" role="alert">{error}</p>}
    </div>}
    <div className="modal-actions">
      {results ? <button type="button" className="btn primary" onClick={onClose}>เสร็จสิ้น</button> : <>
        <span className={files.length === 0 || problems.length ? "kn-missing" : "kn-ready"} role="status">{files.length === 0 ? "ยังไม่ได้เลือกไฟล์" : problems.length ? problems.join(" · ") : `${files.length} ไฟล์ · ${formatSize(total)}`}</span>
        <button type="button" className="btn" onClick={onClose}>ยกเลิก</button>
        <button type="button" className="btn primary" disabled={saving || files.length === 0 || problems.length > 0} onClick={() => void upload()}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังอัปโหลด...</> : <><span className="material-symbols-outlined" aria-hidden="true">upload</span>อัปโหลด</>}</button>
      </>}
    </div>
  </ModalShell>;
}
