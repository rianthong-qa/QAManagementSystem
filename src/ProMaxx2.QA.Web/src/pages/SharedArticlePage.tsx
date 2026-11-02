import { useEffect, useState } from "react";
import { apiUrl, getJson, isAbortError, ApiError } from "../api";
import { HtmlArticleFrame } from "../components/HtmlArticleFrame";
import { formatThaiDateTime } from "../dateTime";
import "./SharedArticle.css";

// หน้าอ่านอย่างเดียวของบทความคลังความรู้จากลิงก์ `?ka=<code>` (สร้างในหน้าต่างอ่านบทความ → "สร้างลิงก์แชร์")
// เปิดได้โดยไม่ต้อง login — endpoint /shared/articles/{code} เป็น anonymous และคืนเฉพาะบทความที่ยังเผยแพร่อยู่
// บทความ HTML แสดงใน HtmlArticleFrame (sandbox ไม่มีสคริปต์) ให้หน้าตาเหมือนไฟล์ต้นฉบับ
type SharedArticle = {
  title: string; projectName?: string | null; tags?: string | null; contentFormat: "Markdown" | "Html";
  bodyMarkdown?: string | null; bodyHtml?: string | null; authorName?: string | null; updatedAt: string;
};

const errorMessage = (e: unknown) =>
  e instanceof ApiError && e.status === 404 ? "ลิงก์ไม่ถูกต้อง ถูกยกเลิกแล้ว หรือบทความนี้ไม่ได้เผยแพร่แล้ว"
    : e instanceof ApiError && e.status === 429 ? "เปิดลิงก์ถี่เกินไป กรุณารอสักครู่แล้วลองใหม่"
      : "โหลดบทความไม่สำเร็จ กรุณาลองใหม่อีกครั้ง";

export default function SharedArticlePage({ code }: { code: string }) {
  const [data, setData] = useState<SharedArticle | null>(null);
  const [error, setError] = useState("");
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    getJson<SharedArticle>(`${apiUrl}/shared/articles/${encodeURIComponent(code)}`, controller.signal)
      .then((result) => { setData(result); setError(""); })
      .catch((e) => { if (!isAbortError(e)) setError(errorMessage(e)); });
    return () => controller.abort();
  }, [code, attempt]);

  useEffect(() => { if (data) document.title = `${data.title} · ProMaxx2 QA Hub`; }, [data]);

  if (error) return <div className="card shared-article-state" role="alert">
    <span className="material-symbols-outlined" aria-hidden="true">link_off</span>
    <p>{error}</p>
    <button type="button" className="btn" onClick={() => { setError(""); setAttempt((x) => x + 1); }}>ลองใหม่</button>
  </div>;
  if (!data) return <div className="card shared-article-state" role="status"><span className="spinner" aria-hidden="true" /><p>กำลังโหลดบทความ...</p></div>;

  const tags = (data.tags ?? "").split(",").map((t) => t.trim()).filter(Boolean);
  const isHtml = data.contentFormat === "Html" && Boolean(data.bodyHtml);
  return (
    <article className="shared-article">
      <header className="card shared-article-hero">
        <span className="shared-article-eyebrow">บทความ · {data.projectName ?? "ความรู้กลาง"}</span>
        <h1>{data.title}</h1>
        <p className="shared-article-meta">
          <span><span className="material-symbols-outlined" aria-hidden="true">edit_calendar</span>อัปเดต {formatThaiDateTime(data.updatedAt)}</span>
          {data.authorName && <span><span className="material-symbols-outlined" aria-hidden="true">person</span>{data.authorName}</span>}
        </p>
        {tags.length > 0 && <ul className="shared-article-tags" aria-label="Tag">{tags.map((t) => <li key={t}>{t}</li>)}</ul>}
      </header>
      {isHtml
        ? <div className="card shared-article-html"><HtmlArticleFrame html={data.bodyHtml!} title={`เนื้อหาบทความ ${data.title}`} minHeight={320} /></div>
        : <div className="card shared-article-body">{data.bodyMarkdown}</div>}
    </article>
  );
}
