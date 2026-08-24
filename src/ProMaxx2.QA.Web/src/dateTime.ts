export function formatThaiDateTime(value?: string | number | null, options?: Intl.DateTimeFormatOptions): string {
  if (value === null || value === undefined || value === "") return "-";
  let date = value as string | number;
  if (typeof value === "string" && !/(z|Z|[+-]\d{2}:?\d{2})$/.test(value)) date = `${value}Z`;
  const d = new Date(date);
  if (Number.isNaN(d.getTime())) return String(value);
  return d.toLocaleString("th-TH", { ...options, timeZone: "Asia/Bangkok" });
}