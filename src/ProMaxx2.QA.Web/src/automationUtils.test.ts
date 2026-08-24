import { describe, expect, it } from "vitest";
import { automationCoverage, buildObjectKey, parseDslSteps } from "./automationUtils";

describe("parseDslSteps", () => {
  it("parses a valid DSL JSON and uppercases action codes", () => {
    const dsl = JSON.stringify({
      dslVersion: "1.0",
      steps: [
        { stepNo: 1, action: "login", parameters: { userRef: "QA_STANDARD_USER" } },
        { stepNo: 2, action: "EXPECT_TEXT", parameters: { object: "Dashboard.Title", value: "hi" } },
      ],
    });
    const steps = parseDslSteps(dsl);
    expect(steps).toHaveLength(2);
    expect(steps[0]).toEqual({ stepNo: 1, action: "LOGIN", parameters: { userRef: "QA_STANDARD_USER" } });
    expect(steps[1].action).toBe("EXPECT_TEXT");
  });

  it("returns [] for empty, invalid JSON, or missing steps", () => {
    expect(parseDslSteps("")).toEqual([]);
    expect(parseDslSteps("{not json")).toEqual([]);
    expect(parseDslSteps('{"dslVersion":"1.0"}')).toEqual([]);
    expect(parseDslSteps('{"steps":[]}')).toEqual([]);
  });

  it("filters out steps without action or with stepNo <= 0", () => {
    const dsl = JSON.stringify({ steps: [{ stepNo: 0, action: "" }, { stepNo: 1, action: "CLICK" }] });
    expect(parseDslSteps(dsl)).toEqual([{ stepNo: 1, action: "CLICK", parameters: {} }]);
  });
});

describe("buildObjectKey", () => {
  it("joins screen and object codes with a dot", () => {
    expect(buildObjectKey("Sales", "Save")).toBe("Sales.Save");
    expect(buildObjectKey(" Dashboard ", "Title ")).toBe("Dashboard.Title");
  });
});

describe("automationCoverage", () => {
  it("computes ready coverage percentage", () => {
    expect(automationCoverage([])).toBe(0);
    expect(automationCoverage([{ status: "Ready" }, { status: "Ready" }, { status: "Draft" }])).toBe(67);
    expect(automationCoverage([{ status: "Ready" }])).toBe(100);
  });
});