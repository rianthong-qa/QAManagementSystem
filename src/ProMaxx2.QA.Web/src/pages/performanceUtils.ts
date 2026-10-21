// helper/type ที่หน้า ผลงานและการประเมิน ใช้ร่วมกัน (แยกจาก component เพื่อให้ Fast Refresh ทำงาน)
import { ApiError, apiUrl, authHeaders } from "../api";
import { formatThaiDateTime } from "../dateTime";

export type Metrics = {
  executions: number; passed: number; failed: number; blocked: number; skipped: number; retests: number; distinctTestCases: number;
  assignedPending: number; assignedOverdue: number; testCasesCreated: number; testCaseRevisions: number; requirementsCreated: number; requirementRevisions: number;
  testSuitesCreated: number; defectsReported: number; defectsCritical: number; defectsHigh: number; defectsRejected: number; defectComments: number;
  automationCasesCreated: number; automationVersionsCreated: number; automationVersionsApproved: number; automationRuns: number; regressionActions: number;
  workLogs: number; highlights: number; workLogHours: number;
  hasCrmAccount: boolean; crmTicketsHandled: number; crmHandedToDev: number; crmReturnedToSupport: number; crmClosed: number; crmUpdates: number; crmDefectsCreated: number;
  crmEditErr: number; crmAnswers: number; crmAvgQaHours?: number | null;
};

/** วันที่ตามเวลาไทยในรูป yyyy-mm-dd (ใช้กับ <input type="date"> และ query) */
export function thaiToday(): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Bangkok" }).format(new Date());
}

export function fmtDate(value?: string | null): string {
  if (!value) return "-";
  return formatThaiDateTime(`${value.slice(0, 10)}T00:00:00+07:00`, { day: "numeric", month: "short", year: "numeric" });
}

export function errorText(e: unknown, fallback: string): string {
  return e instanceof Error && e.message ? e.message : fallback;
}

/** กฎไฟล์หลักฐานของบันทึกงาน — ต้องตรงกับ PerformanceEvidenceStorage ฝั่ง backend */
export const evidenceRules = { maxFiles: 5, maxFileBytes: 5_000_000, maxTotalBytes: 20_000_000, accept: ".png,.jpg,.jpeg,.webp,.pdf" };

export function fmtBytes(size: number): string {
  return size >= 1_000_000 ? `${(size / 1_000_000).toLocaleString("th-TH", { maximumFractionDigits: 1 })} MB` : `${Math.max(1, Math.round(size / 1000)).toLocaleString("th-TH")} KB`;
}

/** ตรวจไฟล์ที่เลือกก่อนอัปโหลด — คืนข้อความ error หรือ null */
export function evidenceError(files: File[], existingSizes: number[]): string | null {
  if (existingSizes.length + files.length > evidenceRules.maxFiles) return `แนบไฟล์ได้สูงสุด ${evidenceRules.maxFiles} ไฟล์ต่อบันทึก`;
  if (files.some((f) => !/\.(png|jpe?g|webp|pdf)$/i.test(f.name))) return "รองรับเฉพาะ PNG, JPG, WebP และ PDF";
  const total = existingSizes.reduce((a, b) => a + b, 0) + files.reduce((a, f) => a + f.size, 0);
  if (files.some((f) => f.size === 0 || f.size > evidenceRules.maxFileBytes) || total > evidenceRules.maxTotalBytes) return "ไฟล์แต่ละไฟล์ต้องไม่เกิน 5 MB และรวมไม่เกิน 20 MB";
  return null;
}

/** ดาวน์โหลดไฟล์จาก endpoint ที่ต้องใช้ token แล้วบันทึกด้วยชื่อไฟล์เดิม */
/** โหลดไฟล์ที่ต้องใช้ token เป็น object URL สำหรับแสดงตัวอย่าง (<img>/<iframe>) — ผู้เรียกต้อง URL.revokeObjectURL เมื่อเลิกใช้ */
export async function fetchAuthedObjectUrl(path: string): Promise<string> {
  const response = await fetch(`${apiUrl}${path}`, { headers: authHeaders() });
  if (!response.ok) throw new ApiError(response.status, response.status === 404 ? "ไม่พบไฟล์ (อาจถูกลบไปแล้ว)" : `เปิดไฟล์ไม่สำเร็จ (${response.status})`);
  return URL.createObjectURL(await response.blob());
}

export async function downloadAuthed(path: string, fileName: string): Promise<void> {
  const response = await fetch(`${apiUrl}${path}`, { headers: authHeaders() });
  if (!response.ok) throw new ApiError(response.status, response.status === 404 ? "ไม่พบไฟล์ (อาจถูกลบไปแล้ว)" : `ดาวน์โหลดไฟล์ไม่สำเร็จ (${response.status})`);
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 30_000);
}

// ป้ายของการประเมินและสถานะพนักงาน — ใช้ร่วมกันระหว่างหน้าการประเมิน หน้าสรุปผลงาน และหน้าแชร์ให้ HR
export const roundLabel: Record<string, string> = { P30: "ทดลองงาน 30 วัน", P60: "ทดลองงาน 60 วัน", P90: "ทดลองงาน 90 วัน", PFinal: "สรุปผลทดลองงาน", Q1: "ไตรมาส 1", Q2: "ไตรมาส 2", Q3: "ไตรมาส 3", Q4: "ไตรมาส 4" };
export const evalStatus: Record<string, { label: string; tone: string }> = {
  Open: { label: "รอประเมิน", tone: "yellow" },
  SelfSubmitted: { label: "รอหัวหน้าประเมิน", tone: "blue" },
  Completed: { label: "ส่งผลแล้ว", tone: "purple" },
  Acknowledged: { label: "รับทราบแล้ว", tone: "green" },
};
export const recommendationLabel: Record<string, string> = { Pass: "ผ่านทดลองงาน", Extend: "ขยายทดลองงาน", NotPass: "ไม่ผ่านทดลองงาน" };
export const employmentStatusLabel: Record<string, string> = { Probation: "ทดลองงาน", Extended: "ขยายทดลองงาน", Permanent: "พนักงานประจำ", Resigned: "ลาออก" };
export const employmentStatusTone: Record<string, string> = { Probation: "yellow", Extended: "red", Permanent: "green", Resigned: "gray" };
export const workCategoryLabel: Record<string, string> = { Testing: "ทดสอบ", Automation: "Automation", Document: "เอกสาร", Support: "ซัพพอร์ต/ช่วยทีม", Training: "อบรม/เรียนรู้", Meeting: "ประชุม", Other: "อื่น ๆ" };

export function roundText(e: { round: string; year: number }): string {
  return e.round.startsWith("Q") ? `${roundLabel[e.round] ?? e.round}/${e.year + 543}` : roundLabel[e.round] ?? e.round;
}

/** ชั่วโมง → "5.5 ชม." หรือ "2.1 วัน" เมื่อเกิน 48 ชม. */
export function formatHours(hours: number): string {
  return hours >= 48 ? `${(hours / 24).toLocaleString("th-TH", { maximumFractionDigits: 1 })} วัน` : `${hours.toLocaleString("th-TH", { maximumFractionDigits: 1 })} ชม.`;
}

export function passRate(m: Pick<Metrics, "executions" | "passed">): string {
  return m.executions ? `${Math.round((m.passed / m.executions) * 100)}%` : "-";
}

/** สรุปผลงานรายคน (`GET performance/users/{id}/report` และหน้าแชร์ HR `GET shared/performance/{code}`) */
export type EmployeeReport = {
  from: string; to: string; generatedAt: string;
  profile: {
    displayName: string; employeeCode?: string | null; position?: string | null; level?: string | null; supervisorName?: string | null;
    hireDate?: string | null; employmentStatus: string; probationEndDate?: string | null; probationDay?: number | null; probationTotalDays: number;
  };
  metrics: Metrics;
  weekly: { weekStart: string; executions: number; defects: number; testCases: number }[];
  includesWorkLogs: boolean;
  workLogs: { workDate: string; kind: string; category: string; title: string; description?: string | null; effortHours?: number | null; projectName?: string | null }[];
  includesEvaluations: boolean;
  evaluations: {
    type: string; round: string; year: number; periodStart: string; periodEnd: string; dueDate: string; status: string;
    overallScore?: number | null; grade?: string | null; recommendation?: string | null; evaluatorName?: string | null;
    completedAt?: string | null; acknowledgedAt?: string | null; isOverdue: boolean;
  }[];
  comparison?: { groups: string[]; rounds: { evaluationId: string; type: string; round: string; year: number; overallScore: number; grade: string; groups: { group: string; score: number }[] }[] } | null;
};

export type ShareLink = {
  shareLinkId: string; url: string; from: string; to: string; includeEvaluations: boolean; includeWorkLogs: boolean; note?: string | null;
  expiresAt: string; createdAt: string; createdByName?: string | null; revokedAt?: string | null; revokedByName?: string | null;
  viewCount: number; lastViewedAt?: string | null; status: "Active" | "Expired" | "Revoked";
};

/** กลุ่มตัวเลขผลงานจากระบบ — หน้ารายบุคคลและหน้าสรุปผลงาน (รวมหน้าแชร์ HR) ใช้ชุดเดียวกันเพื่อให้ตัวเลขตรงกัน */
export function metricGroups(m: Metrics): { icon: string; title: string; items: [string, number | string][] }[] {
  return [
    { icon: "play_circle", title: "การทดสอบ", items: [
      ["Execution", m.executions], ["Pass rate", passRate(m)], ["Fail / Blocked", `${m.failed} / ${m.blocked}`],
      ["Retest", m.retests], ["Test Case ที่รัน (ไม่ซ้ำ)", m.distinctTestCases], ["งานค้าง / เลยกำหนด", `${m.assignedPending} / ${m.assignedOverdue}`],
    ] },
    { icon: "fact_check", title: "ออกแบบเทส", items: [
      ["Test Case ที่สร้าง", m.testCasesCreated], ["แก้ไข Test Case", m.testCaseRevisions], ["Requirement ที่สร้าง", m.requirementsCreated],
      ["แก้ไข Requirement", m.requirementRevisions], ["Test Suite ที่สร้าง", m.testSuitesCreated],
    ] },
    { icon: "bug_report", title: "Defect", items: [
      ["Defect ที่แจ้ง", m.defectsReported], ["Critical / High", `${m.defectsCritical} / ${m.defectsHigh}`], ["ถูก Reject", m.defectsRejected], ["คอมเมนต์", m.defectComments],
    ] },
    { icon: "smart_toy", title: "Automation & Regression", items: [
      ["Automation Case ที่สร้าง", m.automationCasesCreated], ["Version ที่สร้าง / อนุมัติ", `${m.automationVersionsCreated} / ${m.automationVersionsApproved}`],
      ["สั่งรัน Automation", m.automationRuns], ["กิจกรรม Regression", m.regressionActions],
    ] },
    { icon: "support_agent", title: "งาน CRM", items: m.hasCrmAccount ? [
      ["Ticket ที่ดูแล (ขั้น QA)", m.crmTicketsHandled], ["ส่งต่อ Dev / ส่งกลับ Support", `${m.crmHandedToDev} / ${m.crmReturnedToSupport}`],
      ["ปิดงาน", m.crmClosed], ["ส่งกลับแก้ (EditErr)", m.crmEditErr],
      ["เวลาเฉลี่ยที่อยู่กับ QA", m.crmAvgQaHours != null ? formatHours(m.crmAvgQaHours) : "-"], ["ตอบใน Ticket", m.crmAnswers],
      ["อัปเดต Ticket ผ่าน QA Hub", m.crmUpdates], ["Defect ที่สร้างจาก CRM", m.crmDefectsCreated],
    ] : [["บัญชี CRM", "ยังไม่ได้ผูก"]] },
    { icon: "edit_note", title: "บันทึกงานเพิ่มเติม", items: [
      ["บันทึกงาน", m.workLogs], ["ผลงานเด่น", m.highlights], ["ชั่วโมงที่บันทึก", m.workLogHours.toLocaleString("th-TH", { maximumFractionDigits: 1 })],
    ] },
  ];
}
