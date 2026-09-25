// UI รอบ 5: ตารางที่มี class `table-cards` จะแสดงเป็น card บน Mobile (≤760px, ดู styles.css) โดยแต่ละ cell ต้องมี data-label
// เป็นชื่อคอลัมน์ — แทนที่จะเขียน data-label ทีละ cell ในทุกตาราง ตัวนี้เติมให้อัตโนมัติจากข้อความใน <th>
// (cell ที่ใส่ data-label เองอยู่แล้วจะไม่ถูกแก้) และคอยเติมให้แถวที่ render ใหม่ด้วย MutationObserver

function headerLabels(table: HTMLTableElement): string[] {
  const row = table.tHead?.rows[table.tHead.rows.length - 1];
  if (!row) return [];
  const labels: string[] = [];
  for (const cell of Array.from(row.cells)) {
    const text = (cell.textContent ?? "").replace(/\s+/g, " ").trim();
    for (let i = 0; i < Math.max(1, cell.colSpan); i++) labels.push(text);
  }
  return labels;
}

export function labelTable(table: HTMLTableElement) {
  const labels = headerLabels(table);
  if (!labels.length) return;
  for (const body of Array.from(table.tBodies)) {
    for (const row of Array.from(body.rows)) {
      let col = 0;
      for (const cell of Array.from(row.cells)) {
        if (!cell.hasAttribute("data-label")) {
          // cell ที่กินทั้งแถว (เช่น "ไม่มีข้อมูล") ไม่ต้องมีหัวข้อ
          cell.setAttribute("data-label", cell.colSpan >= labels.length ? "" : labels[col] ?? "");
        }
        col += Math.max(1, cell.colSpan);
      }
    }
  }
}

let installed = false;

/** เรียกครั้งเดียวตอนเริ่มแอป (main.tsx) */
export function installTableCardLabels(root: HTMLElement = document.body) {
  if (installed || typeof MutationObserver === "undefined") return;
  installed = true;
  let scheduled = false;
  const run = () => {
    scheduled = false;
    root.querySelectorAll<HTMLTableElement>("table.table-cards").forEach(labelTable);
  };
  // สังเกตเฉพาะ childList — การตั้ง attribute ของเราเองจึงไม่ทำให้ observer วนซ้ำ
  new MutationObserver(() => {
    if (scheduled) return;
    scheduled = true;
    requestAnimationFrame(run);
  }).observe(root, { childList: true, subtree: true });
  run();
}
