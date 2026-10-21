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
