import { useCallback, useEffect, useRef, useState } from "react";
import { htmlArticleDocument } from "../shared/htmlArticle";

/**
 * แสดงบทความ HTML ให้เหมือนไฟล์ต้นฉบับ (ใช้ทั้งหน้าคลังความรู้ ตัวอย่างในหน้าแก้ไข และหน้าแชร์สาธารณะ)
 * ความปลอดภัย: sandbox ไม่มี allow-scripts → สคริปต์ใน HTML ไม่ทำงานเลย และ CSP ใน srcDoc ห้ามโหลดสคริปต์ซ้ำอีกชั้น
 * allow-same-origin มีไว้ให้หน้านี้วัดความสูงเนื้อหาได้ (ปลอดภัยเพราะไม่มีสคริปต์ใดรันใน iframe)
 * ลิงก์ในบทความเปิดแท็บใหม่ (base target=_blank + allow-popups-to-escape-sandbox)
 */
export function HtmlArticleFrame({ html, title, minHeight = 240, maxHeight }: { html: string; title: string; minHeight?: number; maxHeight?: number }) {
  const ref = useRef<HTMLIFrameElement>(null);
  const [height, setHeight] = useState(minHeight);
  const measure = useCallback(() => {
    const doc = ref.current?.contentDocument;
    if (!doc?.documentElement) return;
    const next = Math.max(minHeight, Math.ceil(doc.documentElement.scrollHeight));
    setHeight(maxHeight ? Math.min(next, maxHeight) : next);
  }, [minHeight, maxHeight]);

  // วัดใหม่เมื่อ iframe โหลดเสร็จ รูปภายในโหลดเสร็จ และเมื่อความกว้างเปลี่ยน (ข้อความตัดบรรทัดใหม่)
  useEffect(() => {
    const frame = ref.current;
    if (!frame) return;
    let observer: ResizeObserver | undefined;
    const onLoad = () => {
      measure();
      const doc = frame.contentDocument;
      if (!doc?.body) return;
      doc.addEventListener("load", measure, true);
      observer?.disconnect();
      observer = new ResizeObserver(measure);
      observer.observe(doc.body);
    };
    frame.addEventListener("load", onLoad);
    if (frame.contentDocument?.readyState === "complete") onLoad();
    return () => { frame.removeEventListener("load", onLoad); observer?.disconnect(); };
  }, [measure, html]);

  return <iframe ref={ref} className="html-article-frame" title={title} sandbox="allow-same-origin allow-popups allow-popups-to-escape-sandbox"
    referrerPolicy="no-referrer" srcDoc={htmlArticleDocument(html)} style={{ height }} />;
}
