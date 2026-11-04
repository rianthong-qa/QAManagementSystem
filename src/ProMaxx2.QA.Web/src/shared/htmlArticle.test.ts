import { describe, expect, it } from "vitest";
import { decodeHtmlBytes, externalizeHtmlAssets, htmlArticleDocument, htmlArticleLengthAfterAssets, htmlArticleSandbox } from "./htmlArticle";

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

describe("embedded image extraction", () => {
  const big = (mime: string, char: string) => `data:${mime};base64,${char.repeat(4000)}`;
  const png = big("image/png", "A"), font = big("font/woff2", "B"), icon = "data:image/png;base64,iVBOR";

  it("uploads each large image/font once and leaves small icons inline", async () => {
    const html = `<img src="${png}"><img src="${png}"><style>@font-face{src:url(${font})}</style><img src="${icon}">`;
    const uploaded: number[] = [];
    const result = await externalizeHtmlAssets(html, async (files) => { uploaded.push(files.length); return files.map((_, i) => `${i}`.padStart(32, "a")); });
    expect(uploaded).toEqual([2]);
    expect(result.moved).toBe(2);
    expect(result.html).toBe(`<img src="qa-asset:${"0".padStart(32, "a")}"><img src="qa-asset:${"0".padStart(32, "a")}"><style>@font-face{src:url(qa-asset:${"1".padStart(32, "a")})}</style><img src="${icon}">`);
    expect(htmlArticleLengthAfterAssets(html)).toBe(result.html.length);
  });

  it("keeps files the server rejected embedded", async () => {
    const html = `<img src="${png}">`;
    const result = await externalizeHtmlAssets(html, async (files) => files.map(() => null));
    expect(result).toEqual({ html, moved: 0 });
  });

  it("rewrites asset references to the API and allows that origin in the CSP", () => {
    const doc = htmlArticleDocument(`<img src="qa-asset:${"f".repeat(32)}">`);
    expect(doc).toContain(`src="http://localhost/api/v1/knowledge/assets/${"f".repeat(32)}"`);
    expect(doc).not.toContain("qa-asset:");
    expect(/font-src [^;]*http:\/\/localhost/.test(doc)).toBe(true);
  });
});
