import { describe, it, expect } from "vitest";
import { csvEscape, buildAutomationPlanCsv, mapAutomationStatusToExecution, formatThaiDateTime, type PlanRow } from "./automationHelpers";

describe("csvEscape", () => {
  it("wraps value in quotes", () => {
    expect(csvEscape("TC-01")).toBe('"TC-01"');
  });
  it("escapes embedded quotes by doubling them", () => {
    expect(csvEscape('Login "ok"')).toBe('"Login ""ok"""');
  });
  it("keeps commas and Thai text intact inside quotes", () => {
    expect(csvEscape("POS, งานขาย")).toBe('"POS, งานขาย"');
  });
});

describe("buildAutomationPlanCsv", () => {
  const rows: PlanRow[] = [
    {
      caseItem: { testCaseCode: "TC-01", title: 'Login "ok"', priority: "High", status: "Ready", automationTarget: "pos", stepCount: 3 },
      module: { moduleCode: "M1", moduleName: "Auth" },
    },
    {
      caseItem: { testCaseCode: "TC-02", title: "No module", priority: "Low", status: "Draft", stepCount: 0 },
      module: undefined,
    },
  ];
  const csv = buildAutomationPlanCsv(rows);

  it("starts with UTF-8 BOM followed by the header row", () => {
    expect(csv.startsWith("﻿")).toBe(true);
    expect(csv.split("\r\n")[0]).toBe('﻿"Test Case Code","Title","Module","Priority","Status","Readiness","Target","Steps"');
  });

  it("maps a row with module, Ready status, target and steps", () => {
    expect(csv.split("\r\n")[1]).toBe('"TC-01","Login ""ok""","M1 · Auth","High","Ready","Ready","pos","3"');
  });

  it("handles missing module, Not Ready status and empty target", () => {
    expect(csv.split("\r\n")[2]).toBe('"TC-02","No module","","Low","Draft","Not Ready","","0"');
  });

  it("produces header plus one line per input row", () => {
    expect(csv.split("\r\n").length).toBe(3);
  });
});

describe("mapAutomationStatusToExecution", () => {
  it("maps Passed to Pass", () => {
    expect(mapAutomationStatusToExecution("Passed")).toBe("Pass");
  });
  it("maps Failed to Fail", () => {
    expect(mapAutomationStatusToExecution("Failed")).toBe("Fail");
  });
  it("maps Blocked to Blocked", () => {
    expect(mapAutomationStatusToExecution("Blocked")).toBe("Blocked");
  });
  it("maps Skipped and any unknown status to Skipped", () => {
    expect(mapAutomationStatusToExecution("Skipped")).toBe("Skipped");
    expect(mapAutomationStatusToExecution("Weird")).toBe("Skipped");
  });
});

describe("formatThaiDateTime", () => {
  const utc = "2026-08-22T09:30:00";

  it("treats an offset-less API timestamp as UTC and renders Bangkok time (+7h)", () => {
    expect(formatThaiDateTime(utc)).toContain("16:30");
  });
  it("handles a timestamp already carrying a Z suffix", () => {
    expect(formatThaiDateTime(`${utc}Z`)).toContain("16:30");
  });
  it("returns - for null, undefined and empty", () => {
    expect(formatThaiDateTime(null)).toBe("-");
    expect(formatThaiDateTime(undefined)).toBe("-");
    expect(formatThaiDateTime("")).toBe("-");
  });
});
