import { useEffect, useState } from "react";
import { apiUrl, getJson, isAbortError } from "../api";
import { ModalShell } from "./ModalShell";
import { Badge } from "./Badge";
import { useDebounced } from "./useDebounced";
import type { MasterOption } from "../shared/appShared";
import { defectResolutionTypes, type DefectResolutionChoice } from "../shared/defects";
import { formatThaiDateTime } from "../dateTime";
import "./DefectResolutionModal.css";

type DuplicateCandidate = { defectId: string; defectCode: string; title: string; status: string };
type BuildOption = { buildId: string; buildNumber: string; buildDate?: string | null; isActive: boolean };

/**
 * ผลการปิด Defect (Root Cause / Resolution) — เปิดเมื่อเปลี่ยนเป็น Resolved/Closed/Rejected จากหน้า Defect หรือกดแก้ผลการปิดในรายละเอียด
 * ข้อมูลนี้เข้าคลังความรู้ ("สาเหตุ / วิธีแก้") ให้ผู้ช่วย AI และการเตือน Defect ซ้ำใช้ — targetStatus = null คือแก้ผลอย่างเดียว ไม่เปลี่ยนสถานะ
 * วงจรข้าม Build (DEFECT_BUILD_LIFECYCLE.md): เป็น Resolved = เลือก Fix Build (ค่าเริ่มต้น Build ล่าสุดของ Release); Resolved → Closed = เลือก Build ที่ตรวจซ้ำผ่าน;
 * ปิดตรงจาก Open/In Progress ใช้ผล "แก้ไขแล้ว" ไม่ได้ (ต้อง Resolve ให้ QA ตรวจซ้ำก่อน)
 */
export function DefectResolutionModal({ defect, targetStatus, currentStatus, releaseId, fixedInBuildId, rootCauses, initial, onCancel, onConfirm }: {
  defect: { defectId?: string; defectCode?: string; title: string; projectId: string };
  targetStatus: string | null;
  currentStatus?: string | null;
  releaseId?: string | null;
  fixedInBuildId?: string | null;
  rootCauses: MasterOption[];
  initial?: { resolutionType?: string | null; rootCauseCategory?: string | null; rootCauseNote?: string | null; resolutionNote?: string | null; duplicateOfDefectId?: string | null; duplicateOfDefectCode?: string | null } | null;
  onCancel: () => void;
  onConfirm: (value: DefectResolutionChoice) => Promise<void>;
}) {
  const directClose = targetStatus === "Closed" && currentStatus !== "Resolved";
  const buildMode = targetStatus === "Resolved" && currentStatus !== "Resolved" ? "fix" : targetStatus === "Closed" && currentStatus === "Resolved" ? "verify" : null;
  const types = directClose ? defectResolutionTypes.filter((t) => t.value !== "Fixed") : defectResolutionTypes;
  const initialType = initial?.resolutionType && types.some((t) => t.value === initial.resolutionType) ? initial.resolutionType : targetStatus === "Rejected" ? "ByDesign" : directClose ? "WontFix" : "Fixed";
  const [type, setType] = useState(initialType);
  const [builds, setBuilds] = useState<BuildOption[]>([]);
  const [buildId, setBuildId] = useState(fixedInBuildId ?? "");
  const [rootCause, setRootCause] = useState(initial?.rootCauseCategory ?? "");
  const [rootCauseNote, setRootCauseNote] = useState(initial?.rootCauseNote ?? "");
  const [resolutionNote, setResolutionNote] = useState(initial?.resolutionNote ?? "");
  const [duplicate, setDuplicate] = useState<{ id: string; code: string } | null>(initial?.duplicateOfDefectId ? { id: initial.duplicateOfDefectId, code: initial.duplicateOfDefectCode ?? "" } : null);
  const [duplicateQuery, setDuplicateQuery] = useState("");
  const debouncedQuery = useDebounced(duplicateQuery, 300);
  const [candidates, setCandidates] = useState<DuplicateCandidate[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    const q = debouncedQuery.trim();
    if (type !== "Duplicate" || q.length < 2) { setCandidates([]); return; }
    const controller = new AbortController();
    getJson<{ rows: DuplicateCandidate[] }>(`${apiUrl}/defects?${new URLSearchParams({ projectId: defect.projectId, search: q, size: "6" })}`, controller.signal)
      .then((result) => setCandidates((result?.rows ?? []).filter((x) => x.defectId !== defect.defectId)))
      .catch((e) => { if (!isAbortError(e)) setCandidates([]); });
    return () => controller.abort();
  }, [debouncedQuery, type, defect.projectId, defect.defectId]);

  // Build ของ Release ที่ Defect อยู่ — ใหม่สุดก่อน; ค่าเริ่มต้น = Fix Build เดิม หรือ Build ล่าสุด
  useEffect(() => {
    if (!buildMode || !releaseId) { setBuilds([]); return; }
    const controller = new AbortController();
    getJson<BuildOption[]>(`${apiUrl}/releases/${releaseId}/builds`, controller.signal)
      .then((rows) => {
        const active = (rows ?? []).filter((b) => b.isActive).sort((a, b) => (b.buildDate ?? "").localeCompare(a.buildDate ?? ""));
        setBuilds(active);
        setBuildId((current) => current || active[0]?.buildId || "");
      })
      .catch((e) => { if (!isAbortError(e)) setBuilds([]); });
    return () => controller.abort();
  }, [buildMode, releaseId]);

  const missing = [type === "Fixed" && !rootCause && "หมวดสาเหตุ", type === "Duplicate" && !duplicate && "Defect ต้นฉบับ"].filter(Boolean) as string[];
  const confirm = async () => {
    if (missing.length) return;
    setSaving(true); setError("");
    try {
      await onConfirm({
        resolutionType: type, rootCauseCategory: rootCause || null, rootCauseNote: rootCauseNote.trim() || null, resolutionNote: resolutionNote.trim() || null, duplicateOfDefectId: type === "Duplicate" ? duplicate?.id ?? null : null,
        fixedInBuildId: buildMode === "fix" ? buildId || null : undefined, verifiedInBuildId: buildMode === "verify" ? buildId || null : undefined,
      });
    } catch (e) { setError(e instanceof Error ? e.message : "บันทึกผลการปิดไม่สำเร็จ"); }
    finally { setSaving(false); }
  };

  return <ModalShell labelledBy="defect-resolution-title" className="defect-resolution-modal" onDismiss={onCancel}>
    <div className="modal-head">
      <div>
        <span className="cycle-detail-eyebrow">{defect.defectCode ? `${defect.defectCode} · ` : ""}{targetStatus ? `เปลี่ยนเป็น ${targetStatus}` : "ผลการปิด"}</span>
        <h2 id="defect-resolution-title">ผลการปิด Defect</h2>
        <small className="defect-resolution-subtitle">{defect.title}</small>
      </div>
      <button type="button" className="modal-close" aria-label="ปิด" onClick={onCancel}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
    </div>
    <p className="defect-resolution-why"><span className="material-symbols-outlined" aria-hidden="true">neurology</span>สาเหตุและวิธีแก้จะเข้าคลังความรู้ ช่วยให้ทีมและผู้ช่วย AI ตอบปัญหาเดิมได้เร็วขึ้น และเตือน Defect ซ้ำได้แม่นขึ้น</p>
    <fieldset className="defect-resolution-types">
      <legend>ผลการปิด <span className="required">*</span></legend>
      {types.map((t) => <label key={t.value} className={type === t.value ? "active" : ""}>
        <input type="radio" name="defect-resolution-type" value={t.value} checked={type === t.value} onChange={() => setType(t.value)} />
        <span><b>{t.label}</b><small>{t.hint}</small></span>
      </label>)}
    </fieldset>
    {directClose && <p className="defect-resolution-hint"><span className="material-symbols-outlined" aria-hidden="true">info</span>ถ้า Dev แก้แล้ว ให้เปลี่ยนเป็น Resolved พร้อม Fix Build เพื่อให้ QA ตรวจซ้ำบน Build นั้นก่อนปิด</p>}
    <div className="defect-resolution-grid">
      {buildMode && <label className="full"><span className="defect-resolution-label">{buildMode === "fix" ? "แก้ใน Build (Fix Build)" : "ตรวจซ้ำผ่านใน Build"} <small>{buildMode === "fix" ? "QA จะตรวจซ้ำบน Build นี้หรือ Build ที่ใหม่กว่า" : "Build ที่ QA ทดสอบแล้วผ่าน"}</small></span>
        <select value={buildId} onChange={(e) => setBuildId(e.target.value)}>
          <option value="">{releaseId ? (builds.length ? "ไม่ระบุ Build" : "Release นี้ยังไม่มี Build") : "Defect นี้ไม่ได้ผูก Release"}</option>
          {builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}{b.buildDate ? ` · ${formatThaiDateTime(b.buildDate, { day: "2-digit", month: "2-digit", year: "numeric" })}` : ""}</option>)}
        </select>
      </label>}
      <label><span className="defect-resolution-label">หมวดสาเหตุ {type === "Fixed" ? <span className="required">*</span> : <small>(ไม่บังคับ)</small>}</span>
        <select value={rootCause} onChange={(e) => setRootCause(e.target.value)}>
          <option value="">{type === "Fixed" ? "เลือกหมวดสาเหตุ" : "ไม่ระบุ"}</option>
          {rootCause && !rootCauses.some((x) => x.value === rootCause) && <option value={rootCause}>{rootCause} (ปิดใช้งาน)</option>}
          {rootCauses.map((x) => <option key={x.masterOptionId} value={x.value}>{x.displayName}</option>)}
        </select>
      </label>
      {type === "Duplicate" && <div className="defect-resolution-duplicate">
        <span className="defect-resolution-label">Defect ต้นฉบับ <span className="required">*</span></span>
        {duplicate ? <div className="defect-resolution-picked"><Badge tone="blue">{duplicate.code || "Defect ที่เลือก"}</Badge><button type="button" className="btn" onClick={() => setDuplicate(null)}>เปลี่ยน</button></div>
          : <>
            <input value={duplicateQuery} onChange={(e) => setDuplicateQuery(e.target.value)} placeholder="ค้นด้วยรหัสหรือชื่อ Defect" aria-label="ค้นหา Defect ต้นฉบับ" />
            {candidates.length > 0 && <ul className="defect-resolution-candidates">{candidates.map((c) => <li key={c.defectId}>
              <button type="button" title={`${c.defectCode} ${c.title}`} onClick={() => { setDuplicate({ id: c.defectId, code: c.defectCode }); setCandidates([]); }}><b>{c.defectCode}</b><span>{c.title}</span><small>{c.status}</small></button>
            </li>)}</ul>}
            {debouncedQuery.trim().length >= 2 && candidates.length === 0 && <small className="defect-resolution-empty">ไม่พบ Defect ที่ตรงกันใน Project นี้</small>}
          </>}
      </div>}
      <label className="full"><span className="defect-resolution-label">สาเหตุ <small>(ไม่บังคับ)</small></span><textarea rows={3} maxLength={2000} value={rootCauseNote} onChange={(e) => setRootCauseNote(e.target.value)} placeholder="เช่น คำนวณภาษีจากยอดก่อนส่วนลด ทำให้ยอด VAT เกิน" /></label>
      <label className="full"><span className="defect-resolution-label">วิธีแก้ <small>(ไม่บังคับ)</small></span><textarea rows={3} maxLength={2000} value={resolutionNote} onChange={(e) => setResolutionNote(e.target.value)} placeholder="เช่น แก้สูตรให้คำนวณหลังหักส่วนลด, เพิ่ม Test Case ตรวจยอด VAT" /></label>
    </div>
    {error && <p className="defect-resolution-error" role="alert">{error}</p>}
    <div className="modal-actions">
      <span className={missing.length ? "defect-resolution-missing" : "defect-resolution-ready"} role="status">{missing.length ? `ยังขาด: ${missing.join(", ")}` : "ข้อมูลครบ พร้อมบันทึก"}</span>
      <button type="button" className="btn" onClick={onCancel}>ยกเลิก</button>
      <button type="button" className="btn primary" disabled={saving || missing.length > 0} onClick={() => void confirm()}>
        {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span>{targetStatus ? `บันทึกและเปลี่ยนเป็น ${targetStatus}` : "บันทึกผลการปิด"}</>}
      </button>
    </div>
  </ModalShell>;
}
