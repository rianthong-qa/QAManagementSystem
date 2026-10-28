// SQL Server datetime2 ไม่เก็บ DateTimeKind ทำให้ EF Core/System.Text.Json serialize DateTime ที่เป็น
// UTC จริงๆ (DateTime.UtcNow) ออกมาแบบไม่มี "Z" ต่อท้าย (เช่น "2026-08-30T10:15:00") — ถ้าไม่เติม Z ก่อน
// `new Date(...)` เบราว์เซอร์จะตีความ string นี้เป็นเวลา local ผิดไปเท่ากับ timezone offset ของเครื่อง
// เอ็กซ์พอร์ตไว้ให้ที่อื่น (เช่น fmtAgo/defectAgeDays) ใช้คำนวณ epoch ได้ถูกต้อง ไม่ใช่แค่ตอน format แสดงผล
export function toUtcDate(value?: string | number | null): Date | null {
  if (value === null || value === undefined || value === "") return null;
  let date = value as string | number;
  if (typeof value === "string" && !/(z|Z|[+-]\d{2}:?\d{2})$/.test(value)) date = `${value}Z`;
  const d = new Date(date);
  return Number.isNaN(d.getTime()) ? null : d;
}

// คืนค่า epoch ms ของ "เที่ยงคืนตามปฏิทินกรุงเทพ" ของค่าที่ให้มา — ใช้เทียบ/นับวันแบบไม่ขึ้นกับ timezone
// ของเครื่อง client (เช่น นับวันที่เหลือถึงกำหนดส่งของ Test Cycle) แทนการทำ d.setHours(0,0,0,0) ตรงๆ ซึ่ง
// จะอิง timezone ของเครื่อง client เอง ไม่ใช่กรุงเทพเสมอไป
export function bangkokMidnightMs(value?: string | number | Date | null): number | null {
  const d = value instanceof Date ? value : toUtcDate(value);
  if (!d) return null;
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Bangkok", year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(d);
  const get = (type: string) => Number(parts.find(p => p.type === type)?.value ?? 0);
  return Date.UTC(get("year"), get("month") - 1, get("day"));
}

export function formatThaiDateTime(value?: string | number | null, options?: Intl.DateTimeFormatOptions): string {
  if (value === null || value === undefined || value === "") return "-";
  const d = toUtcDate(value);
  if (!d) return String(value);
  return d.toLocaleString("th-TH", { ...options, timeZone: "Asia/Bangkok" });
}