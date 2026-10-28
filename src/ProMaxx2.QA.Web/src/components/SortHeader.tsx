export type SortDirection = "asc" | "desc";
export type SortState<K extends string> = { key: K; dir: SortDirection };

/** หัวคอลัมน์ที่กดเพื่อเรียงข้อมูล — กดคอลัมน์ใหม่ = เรียงตาม `defaultDir` ของคอลัมน์นั้น, กดซ้ำ = สลับน้อย→มาก / มาก→น้อย
 * ใช้ `<button>` ใน `<th aria-sort>` ให้ใช้คีย์บอร์ดได้; ไอคอน ↑/↓ = ทิศที่เรียงอยู่, ⇅ = คอลัมน์ที่ยังไม่ได้ใช้เรียง
 * ใส่ `data-label` ที่ `<td>` เองเสมอ เพราะข้อความใน `<th>` มีชื่อไอคอน (ligature) ปนอยู่ */
export function SortHeader<K extends string>({ label, sortKey, sort, onSort, className, defaultDir = "asc" }: {
  label: string;
  sortKey: K;
  sort: SortState<K>;
  onSort: (next: SortState<K>) => void;
  className?: string;
  /** ทิศเริ่มต้นเมื่อกดคอลัมน์นี้ครั้งแรก — วันที่/จำนวนมักอยากเห็นค่ามากก่อน */
  defaultDir?: SortDirection;
}) {
  const active = sort.key === sortKey;
  const next: SortState<K> = active ? { key: sortKey, dir: sort.dir === "asc" ? "desc" : "asc" } : { key: sortKey, dir: defaultDir };
  return <th className={className} aria-sort={active ? (sort.dir === "asc" ? "ascending" : "descending") : "none"}>
    <button type="button" className={`th-sort${active ? " is-active" : ""}`} onClick={() => onSort(next)} title={`เรียงตาม ${label} ${next.dir === "asc" ? "จากน้อยไปมาก" : "จากมากไปน้อย"}`}>
      {label}<span className="material-symbols-outlined" aria-hidden="true">{active ? (sort.dir === "asc" ? "arrow_upward" : "arrow_downward") : "unfold_more"}</span>
    </button>
  </th>;
}
