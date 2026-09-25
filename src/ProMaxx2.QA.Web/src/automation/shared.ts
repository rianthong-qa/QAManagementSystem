import { useEffect, useState } from "react";
import { apiUrl } from "../api";
import type { BuildOption, EnvironmentOption } from "./types";

export const token = () => localStorage.getItem("qa.accessToken");

// AUT-UI-001: โหลดข้อมูลแบบแยก "โหลดไม่สำเร็จ" ออกจาก "ไม่มีข้อมูล" — เดิม `r.ok ? r.json() : []` ทำให้ 401/403/500 แสดงเป็น empty state
export const fetchJson = async (url: string, headers: Record<string, string>, signal?: AbortSignal) => {
  const r = await fetch(url, { headers, signal });
  if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? `HTTP ${r.status}`); }
  return r.json();
};

export const isAbort = (e: unknown) => e instanceof DOMException && e.name === "AbortError";

/** AUT-UI-002: รายการ Build (ของ Release ที่เลือก) + Environment ที่ Active สำหรับ dropdown — เดิมเขียนซ้ำ 7 ที่ และโหลดล้มแล้วแสดงเป็นรายการว่าง */
export function useBuildsAndEnvironments(releaseId: string | undefined, options: { builds?: boolean; enabled?: boolean } = {}) {
  const wantBuilds = options.builds ?? true;
  const enabled = options.enabled ?? true;
  const [state, setState] = useState<{ builds: BuildOption[]; environments: EnvironmentOption[]; loadError: string }>({ builds: [], environments: [], loadError: "" });
  useEffect(() => {
    if (!enabled) return;
    const ctrl = new AbortController();
    const h = { Authorization: `Bearer ${token()}` };
    Promise.all([
      wantBuilds && releaseId ? fetchJson(`${apiUrl}/releases/${releaseId}/builds`, h, ctrl.signal) : Promise.resolve([]),
      fetchJson(`${apiUrl}/master-settings/environments`, h, ctrl.signal),
    ]).then(([b, e]) => setState({ builds: Array.isArray(b) ? b : [], environments: Array.isArray(e) ? (e as EnvironmentOption[]).filter((x) => x.isActive) : [], loadError: "" }))
      .catch((err) => { if (!isAbort(err)) setState((prev) => ({ ...prev, loadError: "โหลดรายการ Build/Environment ไม่สำเร็จ — ปิดหน้าต่างแล้วลองใหม่" })); });
    return () => ctrl.abort();
  }, [releaseId, wantBuilds, enabled]);
  return state;
}

export { useDebounced } from "../components/useDebounced";

// Generate-with-AI เรียก AI provider จริง (opencode/OpenAI/ฯลฯ) ซึ่งบาง provider/model ตอบช้ามาก หรือค้างไม่ตอบเลย
// backend เองมี timeout อยู่แล้วที่ 5 นาที แต่ฝั่งนี้ไม่เคยมี timeout เลยมาก่อน ทำให้ปุ่มค้างแบบไม่มี feedback
// ใดๆ ให้ผู้ใช้เห็นเลยถ้า provider ไม่ตอบ — ตัดที่ 90 วินาทีแทน ให้พอสำหรับ AI ทั่วไปแต่ไม่ปล่อยให้ค้างเป็นนาทีๆ
export const AI_GENERATE_TIMEOUT_MS = 90_000;

export const targetTone: Record<string, string> = { Pos: "blue", App: "purple", WindowsUI: "gray" };

export const failureTone: Record<string, string> = { ApplicationFailure: "red", AssertionFailure: "yellow", TestDataFailure: "yellow", AutomationFailure: "blue", EnvironmentFailure: "yellow", AgentFailure: "blue", Unknown: "gray" };

export const sampleDsl = JSON.stringify({
  dslVersion: "1.0",
  automationType: "WindowsUI",
  steps: [
    { stepNo: 1, action: "LOGIN", parameters: { userRef: "QA_STANDARD_USER" } },
    { stepNo: 2, action: "OPEN_MENU", parameters: { menu: "SALES" } },
    { stepNo: 3, action: "NEW_DOCUMENT", parameters: { documentType: "SALES" } },
    { stepNo: 4, action: "SELECT_ITEM", parameters: { itemCode: "A001" } },
    { stepNo: 5, action: "SET_QTY", parameters: { object: "Sales.Quantity", value: "20" } },
    { stepNo: 6, action: "SAVE_DOCUMENT", parameters: {} },
    { stepNo: 7, action: "EXPECT_MESSAGE", parameters: { messageKey: "STOCK_NOT_ENOUGH" } },
  ],
}, null, 2);

export const DAY_LABELS: { value: number; label: string }[] = [
  { value: 0, label: "อา" }, { value: 1, label: "จ" }, { value: 2, label: "อ" }, { value: 3, label: "พ" },
  { value: 4, label: "พฤ" }, { value: 5, label: "ศ" }, { value: 6, label: "ส" },
];
