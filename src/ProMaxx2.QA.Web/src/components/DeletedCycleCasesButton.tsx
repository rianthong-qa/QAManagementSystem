import { useEffect, useState } from "react";
import { apiFetch, apiUrl, getJson, isAbortError } from "../api";
import { confirmDialog, notify } from "./dialogStore";
import { type RemoveDeletedCasesResult, isSysAdminUser, removeDeletedCasesMessage } from "../shared/recycleBin";

type Summary = { cycles: number; removableCases: number };

/**
 * ปุ่ม "เอาเคสที่ถูกลบออก (n)" ระดับ Project — SYS_ADMIN เท่านั้น (API SysAdminOnly) และแสดงเมื่อมีเคสที่เอาออกได้
 * เอาออกเฉพาะเคสที่ Test Case ถูกลบและยังไม่เคยมีผลทดสอบ ใน Cycle ที่ยังไม่ปิดและไม่อยู่ใน Test Plan ที่ Frozen
 */
export function DeletedCycleCasesButton({ projectId, refreshKey, onRemoved }: { projectId?: string; refreshKey?: number; onRemoved: () => void }) {
  const [isAdmin] = useState(isSysAdminUser);
  const [summary, setSummary] = useState<Summary | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    if (!isAdmin || !projectId) { setSummary(null); return; }
    const controller = new AbortController();
    getJson<Summary>(`${apiUrl}/test-cycles/deleted-cases-summary?projectId=${projectId}`, controller.signal)
      .then((data) => { if (!controller.signal.aborted) setSummary(data); })
      .catch((e) => { if (!isAbortError(e)) setSummary(null); });
    return () => controller.abort();
  }, [isAdmin, projectId, refreshKey]);
  if (!isAdmin || !projectId || !summary?.removableCases) return null;
  const remove = async () => {
    if (busy) return;
    const ok = await confirmDialog({
      title: `เอาเคสที่ Test Case ถูกลบออก ${summary.removableCases.toLocaleString()} เคส?`,
      message: `จาก ${summary.cycles.toLocaleString()} Cycle ใน Project นี้ — เอาออกเฉพาะเคสที่ยังไม่มีผลทดสอบ เคสที่มีผลแล้วคงไว้เป็นหลักฐาน และข้าม Cycle ที่ปิดแล้วหรืออยู่ใน Test Plan ที่ Freeze การเอาออกย้อนกลับไม่ได้ (บันทึกไว้ใน Audit Log)`,
      confirmLabel: "เอาออก",
      tone: "danger",
    });
    if (!ok) return;
    setBusy(true);
    try {
      const result = await apiFetch<RemoveDeletedCasesResult>(`/test-cycles/remove-deleted-cases?projectId=${projectId}`, { method: "POST", fallbackMessage: "เอาเคสที่ถูกลบออกไม่สำเร็จ" });
      notify(removeDeletedCasesMessage(result), "success");
      setSummary(null);
      onRemoved();
    } catch (e) {
      notify(e instanceof Error ? e.message : "เอาเคสที่ถูกลบออกไม่สำเร็จ กรุณาลองใหม่", "error");
    } finally {
      setBusy(false);
    }
  };
  return (
    <button type="button" className="btn" disabled={busy} title="เอาเคสที่ Test Case ถูกลบและยังไม่มีผลทดสอบออกจากทุก Cycle ใน Project นี้" onClick={() => void remove()}>
      {busy ? <><span className="spinner inline" aria-hidden="true" />กำลังเอาออก...</> : <><span className="material-symbols-outlined" aria-hidden="true">playlist_remove</span>เคสที่ถูกลบ ({summary.removableCases.toLocaleString()})</>}
    </button>
  );
}
