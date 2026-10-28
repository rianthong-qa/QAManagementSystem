import { toUtcDate } from "../dateTime";

export type DefectReproField = { label?: string; value: string };
export type DefectReproStep = { stepNo: number; action: string; status?: "Pass" | "Fail"; detail: string; fields: DefectReproField[]; notes: string[] };

// "Steps to Reproduce" เป็น freeform text — ถ้าเขียนตามรูปแบบ "1. Action (Pass/Fail) | ข้อมูล: ... | คาดหวัง: ... | [หมายเหตุ]"
// (รูปแบบที่ Execution Workspace สร้างให้อัตโนมัติ) จะแยกเป็นขั้นตอน: fields = ส่วน "ป้าย: ค่า" แต่ละส่วนหลัง "|",
// notes = ส่วนที่อยู่ใน [ ] ล้วน; ถ้าไม่ตรงรูปแบบ (ไม่ได้ขึ้นต้นด้วยเลขข้อทุกบรรทัด) คืน null ให้แสดงเป็นข้อความธรรมดาแทน
export function parseReproSteps(text: string): DefectReproStep[] | null {
  const lines = text.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
  if (!lines.length) return null;
  const steps: DefectReproStep[] = [];
  for (const line of lines) {
    const m = line.match(/^(\d+)[.)]\s*(.+)$/);
    if (!m) return null;
    const [head, ...rest] = m[2].split("|").map((part) => part.trim());
    const statusMatch = head.match(/^(.*?)\s*\((Pass|Fail)\)$/);
    const fields: DefectReproField[] = [];
    const notes: string[] = [];
    for (const part of rest.filter(Boolean)) {
      const note = part.match(/^\[(.+)\]$/);
      if (note) { notes.push(note[1].trim()); continue; }
      const labeled = part.match(/^([^:]{1,30}):\s*(.*)$/);
      fields.push(labeled ? { label: labeled[1].trim(), value: labeled[2].trim() } : { value: part });
    }
    steps.push({ stepNo: Number(m[1]), action: (statusMatch ? statusMatch[1] : head).trim(), status: statusMatch?.[2] as "Pass" | "Fail" | undefined, detail: rest.join(" | "), fields, notes });
  }
  return steps;
}

export function defectAgeDays(createdAt: string): number {
  const d = toUtcDate(createdAt); // เติม Z ให้ก่อน — เหตุผลเดียวกับ fmtAgo ด้านบน
  return d ? Math.max(0, Math.floor((Date.now() - d.getTime()) / 86_400_000)) : 0;
}

// ผลการปิด Defect (คลังความรู้ Phase 6) — ต้องตรงกับ Defect.ResolutionTypes ฝั่ง API; หน้า Defect บังคับเลือกเมื่อเปลี่ยนเป็นสถานะปิด
export const defectClosingStatuses = ["Resolved", "Closed", "Rejected"];
export const defectResolutionTypes: { value: string; label: string; hint: string }[] = [
  { value: "Fixed", label: "แก้ไขแล้ว", hint: "แก้โค้ด/ข้อมูล/ค่าตั้งแล้ว — ต้องเลือกหมวดสาเหตุ" },
  { value: "Duplicate", label: "ซ้ำกับ Defect อื่น", hint: "ระบุ Defect ต้นฉบับ" },
  { value: "CannotReproduce", label: "ทำซ้ำไม่ได้", hint: "ทำตามขั้นตอนแล้วไม่เกิดปัญหา" },
  { value: "ByDesign", label: "ทำงานตามที่ออกแบบ", hint: "ไม่ใช่ข้อผิดพลาด" },
  { value: "WontFix", label: "ไม่แก้ไข", hint: "ตัดสินใจไม่แก้ (ผลกระทบต่ำ/ไม่คุ้ม)" },
  { value: "Deferred", label: "เลื่อนไปแก้ภายหลัง", hint: "ยกไป Release ถัดไป" },
];
export const defectResolutionLabel = (value?: string | null) => defectResolutionTypes.find((x) => x.value === value)?.label ?? value ?? "";
export type DefectResolution = { resolutionType: string; rootCauseCategory: string | null; rootCauseNote: string | null; resolutionNote: string | null; duplicateOfDefectId: string | null };
