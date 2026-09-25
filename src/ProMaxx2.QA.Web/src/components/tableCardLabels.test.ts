import { describe, expect, it } from "vitest";
import { labelTable } from "./tableCardLabels";

// labelTable ใช้เฉพาะ tHead/tBodies/rows/cells/textContent/colSpan/attribute — จำลองด้วย object เล็ก ๆ ได้โดยไม่ต้องมี jsdom
type FakeCell = { textContent: string; colSpan: number; attrs: Record<string, string>; hasAttribute(n: string): boolean; setAttribute(n: string, v: string): void };
const cell = (text = "", colSpan = 1, label?: string): FakeCell => {
  const attrs: Record<string, string> = label === undefined ? {} : { "data-label": label };
  return { textContent: text, colSpan, attrs, hasAttribute: (n) => n in attrs, setAttribute: (n, v) => { attrs[n] = v; } };
};
const table = (head: FakeCell[], rows: FakeCell[][]) => ({
  tHead: { rows: [{ cells: head }] },
  tBodies: [{ rows: rows.map((cells) => ({ cells })) }],
}) as unknown as HTMLTableElement;

describe("labelTable", () => {
  it("copies each column header into data-label of the body cells", () => {
    const row = [cell("DEF-1"), cell("Open"), cell("")];
    labelTable(table([cell("Code"), cell(" Status "), cell("")], [row]));
    expect(row.map((c) => c.attrs["data-label"])).toEqual(["Code", "Status", ""]);
  });

  it("keeps labels that were written by hand", () => {
    const row = [cell("x", 1, "Custom"), cell("y")];
    labelTable(table([cell("A"), cell("B")], [row]));
    expect(row[0].attrs["data-label"]).toBe("Custom");
    expect(row[1].attrs["data-label"]).toBe("B");
  });

  it("follows colspan and leaves full-width message rows without a label", () => {
    const spanned = [cell("a", 2), cell("c")];
    const empty = [cell("ยังไม่มีข้อมูล", 3)];
    labelTable(table([cell("A"), cell("B"), cell("C")], [spanned, empty]));
    expect(spanned.map((c) => c.attrs["data-label"])).toEqual(["A", "C"]);
    expect(empty[0].attrs["data-label"]).toBe("");
  });
});
