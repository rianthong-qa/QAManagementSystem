/** หน้าแรก + หน้าสุดท้าย + รอบหน้าปัจจุบัน ±radius; ช่องว่างหน้าเดียวแสดงเลขแทน "…" (ไม่มีประโยชน์ที่จะซ่อน 1 หน้า) */
export function pageWindow(current: number, count: number, radius: number): (number | "gap")[] {
  if (count <= 7) return Array.from({ length: count }, (_, i) => i + 1);
  const pages = new Set([1, count]);
  for (let p = current - radius; p <= current + radius; p++) if (p >= 1 && p <= count) pages.add(p);
  const sorted = [...pages].sort((a, b) => a - b);
  const result: (number | "gap")[] = [];
  sorted.forEach((p, i) => {
    const prev = sorted[i - 1];
    if (prev !== undefined && p - prev === 2) result.push(p - 1);
    else if (prev !== undefined && p - prev > 2) result.push("gap");
    result.push(p);
  });
  return result;
}
