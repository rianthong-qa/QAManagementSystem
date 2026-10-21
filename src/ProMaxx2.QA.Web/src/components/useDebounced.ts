import { useEffect, useState } from "react";

/** หน่วงค่าที่พิมพ์ก่อนนำไปยิง API (ค่าเริ่มต้น 300ms) — ใช้กับช่องค้นหาทุกหน้า */
export function useDebounced<T>(value: T, delayMs = 300): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(t);
  }, [value, delayMs]);
  return debounced;
}
