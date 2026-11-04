import { describe, expect, it } from "vitest";
import { decodeHtmlBytes, htmlArticleDocument, htmlArticleSandbox } from "./htmlArticle";

// สคริปต์ในบทความ HTML ทำงานได้ — ปลอดภัยเฉพาะเมื่อ iframe เป็น origin ทึบ (ไม่มี allow-same-origin)
describe("html article frame isolation", () => {
  const flags = htmlArticleSandbox.split(/\s+/);
  it("never combines allow-scripts with allow-same-origin", () => {
    expect(flags).toContain("allow-scripts");
    expect(flags).not.toContain("allow-same-origin");
    expect(flags).not.toContain("allow-top-navigation");
    expect(flags).not.toContain("allow-forms");
  });
  it("blocks network requests with CSP inside the document", () => {
    const doc = htmlArticleDocument("<p>x</p>");
    const csp = /http-equiv="Content-Security-Policy" content="([^"]+)"/.exec(doc)?.[1] ?? "";
    expect(csp).toContain("default-src 'none'");
    expect(csp).not.toMatch(/connect-src|frame-src|child-src/);
  });
});

describe("decodeHtmlBytes", () => {
  it("decodes UTF-8 by default", () => {
    const bytes = new TextEncoder().encode("<p>ทดสอบ</p>");
    expect(decodeHtmlBytes(bytes)).toBe("<p>ทดสอบ</p>");
  });
  it("uses the charset declared in <meta> for legacy Thai files", () => {
    // "ไทย" ใน windows-874 = 0xE4 0xB7 0xC2
    const head = new TextEncoder().encode('<meta charset="windows-874"><p>');
    const bytes = new Uint8Array([...head, 0xe4, 0xb7, 0xc2]);
    expect(decodeHtmlBytes(bytes)).toBe('<meta charset="windows-874"><p>ไทย');
  });
  it("falls back to UTF-8 for an unknown charset", () => {
    const bytes = new TextEncoder().encode('<meta charset="x-unknown"><p>ok</p>');
    expect(decodeHtmlBytes(bytes)).toBe('<meta charset="x-unknown"><p>ok</p>');
  });
});
