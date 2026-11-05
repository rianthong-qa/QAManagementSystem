import type { TestCycleItem } from "./appShared";

/** ทุกเคสมีผลแล้ว (ไม่เหลือ Not Run) — Cycle ยัง InProgress จนกว่าจะมีคนปิด จึงยังอยู่ใน CyclePicker แต่แยกไปกลุ่มท้าย */
export const isCycleFullyExecuted = (x: Pick<TestCycleItem, "caseCount" | "executedCount">) => x.caseCount > 0 && x.executedCount >= x.caseCount;
