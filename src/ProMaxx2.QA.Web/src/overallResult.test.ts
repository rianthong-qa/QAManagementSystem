import { describe, expect, it } from "vitest";
import { calculateOverallResult } from "./overallResult";

describe("calculateOverallResult", () => {
  it("All Pass -> Pass", () => {
    expect(calculateOverallResult(["Pass", "Pass", "Pass"])).toBe("Pass");
  });

  it("Pass + Fail -> Fail (Fail wins over everything)", () => {
    expect(calculateOverallResult(["Pass", "Fail", "Pass"])).toBe("Fail");
  });

  it("Pass + Blocked (no Fail) -> Blocked", () => {
    expect(calculateOverallResult(["Pass", "Pass", "Blocked"])).toBe("Blocked");
  });

  it("Pass + NotRun (no Fail/Blocked, some already tested) -> InProgress", () => {
    expect(calculateOverallResult(["Pass", "Pass", "NotRun"])).toBe("InProgress");
  });

  it("All NotRun (nothing tested yet) -> NotRun", () => {
    expect(calculateOverallResult(["NotRun", "NotRun", "NotRun"])).toBe("NotRun");
  });

  it("empty step list -> NotRun", () => {
    expect(calculateOverallResult([])).toBe("NotRun");
  });

  it("Skipped is a Test-Case-level action, not derived from steps — never returned by this function", () => {
    // ไม่มี case สำหรับ "Skipped" เพราะ App.tsx ไม่เรียก calculateOverallResult เลยตอนผู้ใช้กด
    // "Skip Test Case" — ส่ง status:"Skipped" ตรงไปที่ backend โดยไม่ผ่านการคำนวณจาก Step ใดๆ
    const result = calculateOverallResult(["Pass", "Fail", "Blocked", "NotRun"]);
    expect(result).not.toBe("Skipped");
  });

  it("priority order holds even when Fail, Blocked, and NotRun all present -> Fail", () => {
    expect(calculateOverallResult(["Fail", "Blocked", "NotRun", "Pass"])).toBe("Fail");
  });

  it("priority order holds when Blocked and NotRun present but no Fail -> Blocked", () => {
    expect(calculateOverallResult(["Blocked", "NotRun", "Pass"])).toBe("Blocked");
  });
});
