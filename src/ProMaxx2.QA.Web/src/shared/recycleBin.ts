/** ดู/กู้คืนรายการที่ลบแล้ว และเอาเคสที่ถูกลบออกทั้ง Project เป็นสิทธิ์ SYS_ADMIN เท่านั้น (API ใช้ policy SysAdminOnly) */
export function isSysAdminUser(): boolean {
  try { return (JSON.parse(localStorage.getItem("qa.user") ?? "{}") as { roles?: string[] }).roles?.includes("SYS_ADMIN") ?? false; }
  catch { return false; }
}

/** ผลของ `POST /test-cycles/{cycleId}/remove-deleted-cases` และ `POST /test-cycles/remove-deleted-cases?projectId=` */
export type RemoveDeletedCasesResult = { cycles: number; removedCases: number; skippedFrozenCycles: number };

export function removeDeletedCasesMessage(result: RemoveDeletedCasesResult): string {
  const frozen = result.skippedFrozenCycles ? ` · ข้าม ${result.skippedFrozenCycles} Cycle ที่อยู่ใน Test Plan ที่ Freeze/ปิดแล้ว` : "";
  return result.removedCases
    ? `เอาเคสที่ Test Case ถูกลบออกแล้ว ${result.removedCases.toLocaleString()} เคส จาก ${result.cycles.toLocaleString()} Cycle${frozen}`
    : `ไม่มีเคสที่เอาออกได้ — เคสที่มีผลทดสอบแล้วคงไว้เป็นหลักฐาน${frozen}`;
}
