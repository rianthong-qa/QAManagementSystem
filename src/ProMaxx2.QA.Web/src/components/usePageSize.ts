import { useEffect, useState } from "react";
import { currentUserId } from "../shared/appShared";

/** สูงสุดที่ API รายการ Test Case / Test Cycle / Defect รับ (clamp 1–500 ฝั่ง server) */
export const LIST_PAGE_SIZE_MAX = 500;

/** จำนวนรายการต่อหน้าที่จำแยกตาม User ที่ login (`{baseKey}.{userId}` ใน localStorage) — เครื่องเดียวกันใช้หลายบัญชีได้ */
export function useUserPageSize(baseKey: string, fallback = 30) {
  const [key] = useState(() => `${baseKey}.${currentUserId() || "anonymous"}`);
  const [size, setSize] = useState(() => {
    try { const saved = Math.round(Number(localStorage.getItem(key))); return saved >= 1 && saved <= LIST_PAGE_SIZE_MAX ? saved : fallback; }
    catch { return fallback; }
  });
  useEffect(() => { try { localStorage.setItem(key, String(size)); } catch { /* เก็บไม่ได้ (private mode) ใช้ค่าในหน้านี้ต่อ */ } }, [key, size]);
  return [size, setSize] as const;
}
