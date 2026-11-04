// บทความที่นำเข้าจาก HTML (คลังความรู้) — ตัวช่วยฝั่งหน้าเว็บ; เซิร์ฟเวอร์ทำความสะอาด HTML และสกัดข้อความไว้ค้นหาเอง (HtmlArticleContent.cs)
import { apiUrl } from "../api";

/** ขนาด HTML สูงสุด (หลังแยกรูป/ฟอนต์ที่ฝังออกไปเก็บต่างหาก) — ตรงกับ HtmlArticleContent.MaxLength */
export const htmlArticleMaxLength = 20_000_000;
/** ขนาดไฟล์ .html สูงสุดที่เลือกได้ (รวมรูปที่ฝัง) — กันเบราว์เซอร์อ่านไฟล์ใหญ่มากจนค้าง */
export const htmlArticleMaxFileBytes = 200_000_000;

// สคริปต์ inline/จาก https (เช่น CDN) ทำงานได้ แต่ไม่มี connect-src = fetch/XHR/WebSocket ถูกบล็อก (default-src 'none'); รูป/สไตล์/ฟอนต์จากภายนอกโหลดได้ให้หน้าตาเหมือนไฟล์
// รูป/ฟอนต์ที่แยกเก็บ (qa-asset:) โหลดจาก API — ใส่ origin ของ API ไว้ด้วยเพราะตอนพัฒนา API เป็น http://
const csp = (assetOrigin: string) => `default-src 'none'; script-src 'unsafe-inline' https:; img-src data: blob: https: http:; style-src 'unsafe-inline' https:; font-src data: https: ${assetOrigin}; media-src data: https: ${assetOrigin}`;
// ค่าตั้งต้นแบบเอกสารทั่วไป (พื้นขาว) — วางก่อนเนื้อหาเพื่อให้สไตล์ของไฟล์ต้นฉบับทับได้
const baseStyle = "html{background:#fff;color:#1f2933}body{margin:20px;font-family:system-ui,-apple-system,'Segoe UI',Tahoma,sans-serif;line-height:1.6;overflow-wrap:anywhere}img,video{max-width:100%;height:auto}table{border-collapse:collapse;max-width:100%}pre{white-space:pre-wrap}";

/** ชนิดข้อความที่เอกสารใน iframe ส่งความสูงเนื้อหามาให้หน้าแม่ (หน้าแม่อ่าน contentDocument ไม่ได้เพราะเป็น origin ทึบ) */
export const htmlArticleHeightMessage = "qa-html-article-height";
// รันก่อนสคริปต์ของบทความ — รายงานความสูงเมื่อโหลดเสร็จ รูปโหลดเสร็จ และเมื่อขนาดเปลี่ยน (สลับแท็บ/ข้อความตัดบรรทัดใหม่)
// ใช้ความสูงกล่องของ <html> (ไม่ใช่ scrollHeight ที่ไม่ต่ำกว่าความสูงกรอบ) เพื่อให้กรอบหดลงได้เมื่อเนื้อหาสั้นลง
// ลิงก์ในหน้าเดียวกัน (href="#หัวข้อ" เช่น สารบัญ) — <base target="_blank"> ทำให้เปิดแท็บใหม่ จึงเลื่อนไปที่หัวข้อในกรอบเดิมแทน
// ฟังที่ window (ทำงานหลังสคริปต์ของไฟล์) และข้ามเมื่อสคริปต์ของไฟล์จัดการคลิกเองแล้ว (defaultPrevented); "#" / "#top" = กลับด้านบน
const inPageLinks = `addEventListener("click",function(e){if(e.defaultPrevented||e.button!==0||e.ctrlKey||e.metaKey||e.shiftKey)return;var a=e.target&&e.target.closest?e.target.closest("a[href]"):null;if(!a)return;var h=a.getAttribute("href")||"";if(h.charAt(0)!=="#")return;e.preventDefault();var id=h.slice(1);try{id=decodeURIComponent(id);}catch(x){}var t=id?document.getElementById(id)||document.getElementsByName(id)[0]:null;if(t)t.scrollIntoView();else if(!id||id.toLowerCase()==="top")window.scrollTo(0,0);});`;
const heightReporter = `(function(){var last=-1;function send(){var d=document.documentElement,b=document.body,h=Math.ceil(Math.max(d.getBoundingClientRect().height,b?b.scrollHeight:0));if(h!==last){last=h;parent.postMessage({type:"${htmlArticleHeightMessage}",height:h},"*");}}addEventListener("load",send,true);new ResizeObserver(send).observe(document.documentElement);})();`;

/**
 * sandbox ของ iframe บทความ — สคริปต์ในบทความทำงานได้แต่ **ห้ามเพิ่ม allow-same-origin**: ไม่มีค่านี้เอกสารเป็น origin ทึบ
 * เข้าถึงหน้าแม่ / localStorage / token ไม่ได้ (ถ้ามีทั้งสองอย่าง สคริปต์ในบทความถอด sandbox ตัวเองและอ่าน token ได้) — ล็อกด้วย htmlArticle.test.ts
 * ไม่มี allow-top-navigation (เปลี่ยนหน้าแม่ไม่ได้) และ allow-forms (ส่งฟอร์มออกนอกระบบไม่ได้)
 */
export const htmlArticleSandbox = "allow-scripts allow-popups allow-popups-to-escape-sandbox";

/** เอกสารเต็มสำหรับ iframe srcDoc */
export const htmlArticleDocument = (html: string) => {
  const base = htmlAssetBaseUrl();
  return `<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="${csp(new URL(base).origin)}"><base target="_blank"><style>${baseStyle}</style><script>${heightReporter}${inPageLinks}</script></head><body>${html.replace(assetReference, (_, id: string) => base + id)}</body></html>`;
};

// ---------- รูป/ฟอนต์ที่แยกเก็บนอก HTML (KnowledgeAssetsController) ----------
// ไฟล์ HTML ที่ export จาก Word/Notion/Chrome ฝังรูปเป็น data: base64 จนใหญ่เกินเพดาน — ตอนบันทึกจึงอัปโหลดรูปแยกแล้วใส่ qa-asset:{id} แทน
// HTML ที่เก็บไม่ผูกกับโดเมน API (dev/production ต่างกัน) — แปลงเป็น URL ตอนแสดงผลใน htmlArticleDocument

const assetReference = /\bqa-asset:([0-9a-f]{32})\b/g;
/** data: base64 ของรูป/ฟอนต์ — application/octet-stream ด้วยเพราะบางเครื่องมือฝังฟอนต์แบบนี้ (เซิร์ฟเวอร์ตรวจชนิดจากเนื้อไฟล์และปฏิเสธที่ไม่ใช่รูป/ฟอนต์) */
const embeddedAsset = /data:((?:image|font)\/[\w.+-]+|application\/(?:font-[\w.+-]+|x-font-[\w.+-]+|vnd\.ms-fontobject|octet-stream))(?:;[\w.=-]+)*;base64,([A-Za-z0-9+/]+={0,2})/gi;
/** รูปเล็ก (ไอคอน) ฝังต่อได้ — แยกออกไม่คุ้มคำขอที่เพิ่ม */
const minExtractLength = 2_048;
/** ตรงกับ KnowledgeAssetService: ไฟล์ละ ≤ 15 MB, ต่อคำขอ ≤ 60 ไฟล์ / 50 MB */
const assetMaxBytes = 15_000_000, batchMaxFiles = 60, batchMaxBytes = 45_000_000;

const htmlAssetBaseUrl = () => new URL(`${apiUrl.replace(/\/$/, "")}/knowledge/assets/`, globalThis.location?.href ?? "http://localhost/").href;

/** data URI ที่จะแยกเก็บ (ไม่ซ้ำ) */
function embeddedAssets(html: string): string[] {
  const found = new Set<string>();
  for (const m of html.matchAll(embeddedAsset)) if (m[0].length >= minExtractLength && (m[2].length * 3) / 4 <= assetMaxBytes) found.add(m[0]);
  return [...found];
}

/** ความยาว HTML หลังแยกรูป/ฟอนต์ออก (ประมาณ — ใช้ตรวจเพดานตอนเลือกไฟล์ก่อนอัปโหลดจริง) */
export function htmlArticleLengthAfterAssets(html: string): number {
  let length = html.length;
  for (const m of html.matchAll(embeddedAsset)) if (m[0].length >= minExtractLength && (m[2].length * 3) / 4 <= assetMaxBytes) length -= m[0].length - "qa-asset:".length - 32;
  return length;
}

const toBlob = (dataUri: string) => {
  const comma = dataUri.indexOf(",");
  const binary = atob(dataUri.slice(comma + 1));
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return new Blob([bytes]);
};

/** อัปโหลดชุดไฟล์ — คืน assetId (32 hex) ตามลำดับ หรือ null สำหรับไฟล์ที่เซิร์ฟเวอร์ไม่รับ */
export type HtmlAssetUploader = (files: Blob[]) => Promise<(string | null)[]>;

/**
 * แยกรูป/ฟอนต์ที่ฝังใน HTML ออกไปเก็บที่เซิร์ฟเวอร์แล้วแทนด้วย qa-asset:{id} — ไฟล์ที่เซิร์ฟเวอร์ไม่รับ (ชนิดไม่รองรับ) ฝังต่อตามเดิม
 * อัปโหลดทีละชุด (≤ 60 ไฟล์ / 45 MB) เพราะ Cloudflare รับคำขอได้ไม่เกิน 100 MB
 */
export async function externalizeHtmlAssets(html: string, upload: HtmlAssetUploader): Promise<{ html: string; moved: number }> {
  const uris = embeddedAssets(html);
  const ids = new Map<string, string>();
  for (let start = 0; start < uris.length;) {
    const batch: string[] = [];
    let bytes = 0;
    while (start < uris.length && batch.length < batchMaxFiles) {
      const size = (uris[start].length * 3) / 4;
      if (batch.length > 0 && bytes + size > batchMaxBytes) break;
      batch.push(uris[start++]); bytes += size;
    }
    const result = await upload(batch.map(toBlob));
    batch.forEach((uri, i) => { const id = result[i]; if (id && /^[0-9a-f]{32}$/.test(id)) ids.set(uri, id); });
  }
  if (ids.size === 0) return { html, moved: 0 };
  return { html: html.replace(embeddedAsset, (uri) => ids.has(uri) ? `qa-asset:${ids.get(uri)}` : uri), moved: ids.size };
}

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

/**
 * ถอดไฟล์ HTML เป็นข้อความ — ใช้ charset จาก <meta> ถ้าระบุไว้ (ไฟล์ภาษาไทยเก่ามักเป็น windows-874 / TIS-620
 * ถ้าอ่านเป็น UTF-8 ตรง ๆ ตัวอักษรไทยจะเพี้ยนทั้งหน้า) ไม่ระบุหรือไม่รู้จัก charset = UTF-8
 */
export function decodeHtmlBytes(bytes: ArrayBuffer | Uint8Array): string {
  const utf8 = new TextDecoder("utf-8").decode(bytes);
  const charset = /<meta[^>]+charset\s*=\s*["']?\s*([\w.:-]+)/i.exec(utf8.slice(0, 4096))?.[1]?.toLowerCase();
  if (!charset || charset === "utf-8" || charset === "utf8") return utf8;
  try { return new TextDecoder(charset).decode(bytes); } catch { return utf8; }
}

// ---------- แก้ไขบนหน้าตาจริง (HtmlArticleVisualEditor) ----------
// iframe แก้ไขใช้ sandbox "allow-same-origin" อย่างเดียว (ไม่มี allow-scripts): สคริปต์และ event handler ในไฟล์ไม่ทำงานเลย
// หน้าแม่จึงเข้าถึง DOM ได้อย่างปลอดภัย (designMode) และสิ่งที่บันทึกคือ markup เดิม + การแก้ ไม่ปน DOM ที่สคริปต์สร้างระหว่างแสดงผล

/** sandbox ของ iframe แก้ไข — **ห้ามเพิ่ม allow-scripts** (คู่กับ allow-same-origin สคริปต์ในบทความจะเข้าถึงหน้าแม่/token ได้) */
export const htmlArticleEditSandbox = "allow-same-origin";
const editorMarker = "data-qa-editor";
const editCsp = (assetOrigin: string) => `default-src 'none'; script-src 'none'; img-src data: blob: https: http:; style-src 'unsafe-inline' https:; font-src data: https: ${assetOrigin}; media-src data: https: ${assetOrigin}`;
// เส้นประรอบส่วนที่ชี้ ให้เห็นว่าแก้ตรงไหน — ถูกลบออกก่อนบันทึก
const editStyle = "html{cursor:text}body :hover{outline:1px dashed rgba(36,87,214,.35);outline-offset:2px}body :focus{outline:none}";

/** เอกสารสำหรับ iframe แก้ไข: HTML ทั้งไฟล์ (คง head/style ของไฟล์) + CSP/สไตล์ของ editor ที่ติด data-qa-editor ไว้ลบตอนบันทึก */
export function htmlArticleEditDocument(html: string): string {
  const base = htmlAssetBaseUrl();
  const doc = parse(html.replace(assetReference, (_, id: string) => base + id));
  const meta = doc.createElement("meta");
  meta.setAttribute("http-equiv", "Content-Security-Policy");
  meta.setAttribute("content", editCsp(new URL(base).origin));
  const style = doc.createElement("style");
  style.textContent = editStyle;
  for (const el of [style, meta]) { el.setAttribute(editorMarker, ""); doc.head.prepend(el); }
  return "<!doctype html>" + doc.documentElement.outerHTML;
}

/** HTML ที่จะบันทึกจากเอกสารใน iframe แก้ไข — ลบส่วนของ editor และแปลง URL ของรูปที่แยกเก็บกลับเป็น qa-asset:{id} */
export function htmlArticleFromEditDocument(doc: Document, hadDoctype: boolean): string {
  const root = doc.documentElement.cloneNode(true) as HTMLElement;
  root.querySelectorAll(`[${editorMarker}]`).forEach((el) => el.remove());
  root.querySelectorAll("[contenteditable]").forEach((el) => el.removeAttribute("contenteditable"));
  const base = htmlAssetBaseUrl();
  const html = root.outerHTML.split(base).join("qa-asset:");
  return (hadDoctype ? "<!doctype html>" : "") + html;
}
