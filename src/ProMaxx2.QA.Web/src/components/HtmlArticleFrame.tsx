import { useEffect, useRef, useState } from "react";
import { htmlArticleDocument, htmlArticleHeightMessage, htmlArticleSandbox } from "../shared/htmlArticle";
import "./HtmlArticleFrame.css";

// กันบทความที่รายงานความสูงผิดปกติ (สคริปต์ในบทความส่งข้อความชนิดเดียวกันได้)
const maxReportedHeight = 100_000;

/**
 * แสดงบทความ HTML ให้เหมือนไฟล์ต้นฉบับ (ใช้ทั้งหน้าคลังความรู้ ตัวอย่างในหน้าแก้ไข และหน้าแชร์สาธารณะ)
 * ความปลอดภัย: สคริปต์ในบทความทำงานใน sandbox ที่ไม่มี allow-same-origin (origin ทึบ — ดู htmlArticleSandbox)
 * หน้าแม่จึงอ่าน contentDocument ไม่ได้ ความสูงมาจาก postMessage ของสคริปต์ที่ htmlArticleDocument ใส่ไว้ (รับเฉพาะจาก iframe นี้)
 * ลิงก์ในบทความเปิดแท็บใหม่ (base target=_blank + allow-popups-to-escape-sandbox)
 */
export function HtmlArticleFrame({ html, title, minHeight = 240, maxHeight }: { html: string; title: string; minHeight?: number; maxHeight?: number }) {
  const ref = useRef<HTMLIFrameElement>(null);
  const [reported, setReported] = useState(0);

  useEffect(() => {
    const onMessage = (event: MessageEvent) => {
      if (event.source !== ref.current?.contentWindow) return;
      const data = event.data as { type?: unknown; height?: unknown } | null;
      if (data?.type !== htmlArticleHeightMessage || typeof data.height !== "number" || !Number.isFinite(data.height)) return;
      setReported(Math.min(Math.max(0, data.height), maxReportedHeight));
    };
    window.addEventListener("message", onMessage);
    return () => window.removeEventListener("message", onMessage);
  }, []);

  const height = Math.max(minHeight, reported);
  return <iframe ref={ref} className="html-article-frame" title={title} sandbox={htmlArticleSandbox}
    referrerPolicy="no-referrer" srcDoc={htmlArticleDocument(html)} style={{ height: maxHeight ? Math.min(height, maxHeight) : height }} />;
}
