import { describe, expect, it } from "vitest";
import { htmlArticleDocument, htmlArticleSandbox } from "./htmlArticle";

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
