import { toUtcDate } from "../dateTime";

export function defectAgeDays(createdAt: string): number {
  const d = toUtcDate(createdAt); // เติม Z ให้ก่อน — เหตุผลเดียวกับ fmtAgo ด้านบน
  return d ? Math.max(0, Math.floor((Date.now() - d.getTime()) / 86_400_000)) : 0;
}
