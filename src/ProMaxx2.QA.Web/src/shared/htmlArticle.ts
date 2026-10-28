// บทความที่นำเข้าจาก HTML (คลังความรู้) — ตัวช่วยฝั่งหน้าเว็บ; เซิร์ฟเวอร์ทำความสะอาด HTML และสกัดข้อความไว้ค้นหาเอง (HtmlArticleContent.cs)

/** ขนาด HTML สูงสุด — ตรงกับ HtmlArticleContent.MaxLength */
export const htmlArticleMaxLength = 3_000_000;

// ไม่มี script-src = ห้ามสคริปต์ทุกชนิด (ซ้อนกับ sandbox ที่ไม่มี allow-scripts); รูป/สไตล์/ฟอนต์จากภายนอกยังโหลดได้ให้หน้าตาเหมือนไฟล์
const csp = "default-src 'none'; img-src data: blob: https: http:; style-src 'unsafe-inline' https:; font-src data: https:; media-src data: https:";
// ค่าตั้งต้นแบบเอกสารทั่วไป (พื้นขาว) — วางก่อนเนื้อหาเพื่อให้สไตล์ของไฟล์ต้นฉบับทับได้
const baseStyle = "html{background:#fff;color:#1f2933}body{margin:20px;font-family:system-ui,-apple-system,'Segoe UI',Tahoma,sans-serif;line-height:1.6;overflow-wrap:anywhere}img,video{max-width:100%;height:auto}table{border-collapse:collapse;max-width:100%}pre{white-space:pre-wrap}";

/**
 * sandbox ของ iframe บทความ — **ห้ามเพิ่ม allow-scripts**: allow-same-origin มีไว้ให้หน้าแม่อ่าน contentDocument เพื่อวัดความสูง
 * ซึ่งปลอดภัยเฉพาะเมื่อไม่มีสคริปต์ใดรันใน iframe (ถ้ามีทั้งสองอย่าง สคริปต์ในบทความจะเข้าถึงหน้าแม่และ token ได้) — ล็อกด้วย htmlArticle.test.ts
 */
export const htmlArticleSandbox = "allow-same-origin allow-popups allow-popups-to-escape-sandbox";

/** เอกสารเต็มสำหรับ iframe srcDoc */
export const htmlArticleDocument = (html: string) =>
  `<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="${csp}"><base target="_blank"><style>${baseStyle}</style></head><body>${html}</body></html>`;

const parse = (html: string) => new DOMParser().parseFromString(html, "text/html");

/** ชื่อจาก <title> หรือ <h1> แรก (ใช้เติมหัวข้อบทความเมื่อยังว่าง) */
export function htmlArticleTitle(html: string): string {
  const doc = parse(html);
  const text = (doc.querySelector("title")?.textContent || doc.querySelector("h1")?.textContent || "").replace(/\s+/g, " ").trim();
  return text.slice(0, 300);
}

/** มีข้อความให้ค้นหาได้หรือไม่ (ไม่นับ style/script) — HTML ที่มีแต่รูปบันทึกไม่ได้ */
export function htmlArticleHasText(html: string): boolean {
  const doc = parse(html);
  doc.querySelectorAll("style,script,noscript,template,title").forEach((el) => el.remove());
  return Boolean(doc.body?.textContent?.trim());
}
