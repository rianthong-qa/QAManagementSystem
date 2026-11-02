import { describe, expect, it } from "vitest";
import { decodeHtmlBytes, htmlArticleDocument, htmlArticleSandbox } from "./htmlArticle";

// iframe บทความ HTML ใช้ allow-same-origin เพื่อวัดความสูง — ปลอดภัยเฉพาะเมื่อไม่มีสคริปต์รันใน iframe
describe("html article frame isolation", () => {
  it("never allows scripts in the sandbox", () => {
    expect(htmlArticleSandbox.split(/\s+/)).not.toContain("allow-scripts");
  });
  it("blocks scripts with CSP inside the document", () => {
    const doc = htmlArticleDocument("<p>x</p>");
    const csp = /http-equiv="Content-Security-Policy" content="([^"]+)"/.exec(doc)?.[1] ?? "";
    expect(csp).toContain("default-src 'none'");
    expect(csp).not.toMatch(/script-src/);
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
