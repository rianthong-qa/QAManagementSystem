import { describe, expect, it } from "vitest";
import { pageWindow } from "./pageWindow";

describe("pageWindow", () => {
  it("shows every page when there are 7 or fewer", () => {
    expect(pageWindow(3, 5, 2)).toEqual([1, 2, 3, 4, 5]);
  });
  it("keeps first/last and the window around the current page", () => {
    expect(pageWindow(6, 183, 2)).toEqual([1, "gap", 4, 5, 6, 7, 8, "gap", 183]);
    expect(pageWindow(6, 183, 1)).toEqual([1, "gap", 5, 6, 7, "gap", 183]);
  });
  it("fills a single-page gap with the number instead of an ellipsis", () => {
    expect(pageWindow(4, 183, 1)).toEqual([1, 2, 3, 4, 5, "gap", 183]);
  });
  it("handles the edges", () => {
    expect(pageWindow(1, 183, 2)).toEqual([1, 2, 3, "gap", 183]);
    expect(pageWindow(183, 183, 2)).toEqual([1, "gap", 181, 182, 183]);
  });
});
