// Overall Result ของ Test Case คำนวณจากผลของแต่ละ Step เท่านั้น (test-case-execution-ui-spec.md §5) —
// ผู้ใช้ห้ามกำหนด Overall Result เองโดยตรง (§25) ยกเว้น "Skipped" ซึ่งเป็นการกระทำระดับ Test Case
// โดยเฉพาะ (ดูจุดที่เรียกใช้ฟังก์ชันนี้ใน App.tsx — จะข้ามการเรียกนี้ไปเลยถ้าผู้ใช้กด Skip Test Case)
// มิเรอร์ตรรกะเดียวกันกับ OverallResultCalculator.cs ฝั่ง backend (ProMaxx2.QA.Domain/Execution/TestCycle.cs)
// เพื่อให้ UI แสดงผลพรีวิวตรงกับที่ server จะคำนวณจริงหลังบันทึก
export type StepStatus = "Pass" | "Fail" | "Blocked" | "NotRun";
export type OverallResult = "Pass" | "Fail" | "Blocked" | "InProgress" | "NotRun";

export function calculateOverallResult(stepStatuses: StepStatus[]): OverallResult {
  if (stepStatuses.length === 0 || stepStatuses.every((s) => s === "NotRun")) return "NotRun";
  if (stepStatuses.some((s) => s === "Fail")) return "Fail";
  if (stepStatuses.some((s) => s === "Blocked")) return "Blocked";
  if (stepStatuses.some((s) => s === "NotRun")) return "InProgress";
  return "Pass";
}
