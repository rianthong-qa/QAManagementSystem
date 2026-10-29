import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { apiFetch, apiUrl, getJson, isAbortError } from "../api";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
import { DeletedItemsButton } from "../components/DeletedItemsModal";
import { confirmDialog, notify } from "../components/dialogStore";
import { useDebounced } from "../components/useDebounced";
import { formatThaiDateTime } from "../dateTime";
import type { ProjectItem } from "../shared/types";
import { KnowledgeChat, type Citation } from "./KnowledgeChat";
import { KnowledgeMcp } from "./KnowledgeMcp";
import { KnowledgeDocuments } from "./KnowledgeDocuments";
import { CrmMirrorIntervalEditor } from "../components/CrmMirrorIntervalEditor";
import { CopyButton } from "../components/CopyButton";
import { HtmlArticleFrame } from "../components/HtmlArticleFrame";
import { downloadKnowledgeFile } from "./knowledgeFiles";
import "./Knowledge.css";

// คลังความรู้ (AI_KNOWLEDGE_CHAT_PLAN.md): ถามผู้ช่วย AI (Phase 2), ค้นหา Defect + บทความ + เอกสาร + CRM (Phase 4), จัดการบทความ, สถานะการ index (SYS_ADMIN)

type SourceType = "Defect" | "CrmTicket" | "Article" | "File" | "TestCase";
type SearchResult = { knowledgeDocumentId: string; sourceType: SourceType; sourceKey: string; title: string; projectId?: string | null; projectName?: string | null; sourceUrl?: string | null; snippet: string; rank: number; matchedChunks: number; indexedAt?: string | null };
type Article = { knowledgeArticleId: string; title: string; bodyMarkdown?: string | null; excerpt: string; projectId?: string | null; projectName?: string | null; tags?: string | null; status: "Draft" | "Published"; createdAt: string; createdByName?: string | null; updatedAt?: string | null; updatedByName?: string | null; indexState?: string | null; indexedAt?: string | null; contentFormat?: "Markdown" | "Html"; bodyHtml?: string | null; shareUrl?: string | null };
type SourceStatus = { sourceType: SourceType; indexed: number; pending: number; failed: number; excluded: number; chunks: number; lastIndexedAt?: string | null };
type CrmStatus = { enabled: boolean; allowExternalAi: boolean; candidates: number; fetched: number; pending: number; failed: number; lastFetchedAt?: string | null; mirrorIntervalMinutes?: number };
type EmbeddingStatus = { enabled: boolean; provider?: string | null; model?: string | null; isExternal: boolean; eligible: number; embedded: number; error?: string | null; lastErrorAt?: string | null; lastSuccessAt?: string | null };
type IndexStatus = { indexerEnabled: boolean; sources: SourceStatus[]; failures: { knowledgeDocumentId: string; sourceType: SourceType; title: string; error?: string | null; attemptedAt?: string | null }[]; crm?: CrmStatus | null; embedding?: EmbeddingStatus | null };
type CrmTicketView = { jobNo: string; subject?: string | null; status?: string | null; jobType?: string | null; serviceType?: string | null; product?: string | null; contactDate?: string | null; description?: string | null; answers: { answerNo: string; answerType?: string | null; answeredAt?: string | null; text: string }[]; fetchedAt: string; indexState?: string | null; indexedAt?: string | null };
type Tab = "chat" | "search" | "articles" | "documents" | "claude" | "status";
// format: Markdown = เขียนในระบบ, Html = นำเข้าจาก HTML (แสดงผลเหมือนไฟล์ต้นฉบับ)
type ArticleDraft = { id?: string; title: string; bodyMarkdown: string; projectId: string; tags: string; status: "Draft" | "Published"; format: "Markdown" | "Html"; bodyHtml: string };

const sourceLabel: Record<SourceType, string> = { Defect: "Defect", CrmTicket: "CRM Ticket", Article: "บทความ", File: "เอกสาร", TestCase: "Test Case" };
const sourceTone: Record<SourceType, string> = { Defect: "red", CrmTicket: "purple", Article: "blue", File: "green", TestCase: "yellow" };
const sourceIcon: Record<SourceType, string> = { Defect: "bug_report", CrmTicket: "support_agent", Article: "article", File: "description", TestCase: "fact_check" };
const searchSources: { value: string; label: string }[] = [{ value: "", label: "ทั้งหมด" }, { value: "Defect", label: "Defect" }, { value: "Article", label: "บทความ" }, { value: "File", label: "เอกสาร" }, { value: "TestCase", label: "Test Case" }];
const crmClosedStatuses = ["Close", "Closed", "Finish"];
const indexStateInfo: Record<string, { label: string; tone: string }> = { Indexed: { label: "ค้นหาได้แล้ว", tone: "green" }, Stale: { label: "รอสร้างดัชนี", tone: "yellow" }, Failed: { label: "สร้างดัชนีไม่สำเร็จ", tone: "red" }, Excluded: { label: "ไม่อยู่ในคลัง", tone: "gray" } };
const emptyDraft: ArticleDraft = { title: "", bodyMarkdown: "", projectId: "", tags: "", status: "Published", format: "Markdown", bodyHtml: "" };
const errorText = (error: unknown, fallback: string) => error instanceof Error && error.message ? error.message : fallback;

export function KnowledgePage({ canEdit, isSysAdmin, canConnectClaude, canViewCrm, onOpenDefect, onOpenTestCase }: { canEdit: boolean; isSysAdmin: boolean; canConnectClaude: boolean; canViewCrm: boolean; onOpenDefect: (defectId: string) => void; onOpenTestCase: (testCaseId: string) => void }) {
  const tabs = useMemo<{ id: Tab; label: string; icon: string }[]>(() => [
    { id: "chat", label: "ถามผู้ช่วย AI", icon: "forum" },
    { id: "search", label: "ค้นหาความรู้", icon: "search" },
    { id: "articles", label: "บทความ", icon: "article" },
    { id: "documents", label: "เอกสาร", icon: "folder_open" },
    ...(canConnectClaude || isSysAdmin ? [{ id: "claude" as Tab, label: "เชื่อมต่อ Claude", icon: "hub" }] : []),
    ...(isSysAdmin ? [{ id: "status" as Tab, label: "สถานะคลัง", icon: "monitoring" }] : []),
  ], [isSysAdmin, canConnectClaude]);
  const hashParams = useMemo(() => new URLSearchParams(window.location.hash.split("?")[1] ?? ""), []);
  const highlightFileId = hashParams.get("fileId");
  const [tab, setTab] = useState<Tab>(() => hashParams.get("articleId") ? "articles" : highlightFileId ? "documents" : "chat");
  const tabRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const [projects, setProjects] = useState<ProjectItem[]>([]);
  const [viewing, setViewing] = useState<Article | null>(null);
  const [viewLoading, setViewLoading] = useState(false);
  const [crmView, setCrmView] = useState<CrmTicketView | null>(null);
  const [draft, setDraft] = useState<ArticleDraft | null>(null);
  const [articlesVersion, setArticlesVersion] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    getJson<ProjectItem[]>(`${apiUrl}/projects`, controller.signal).then((rows) => setProjects(Array.isArray(rows) ? rows : [])).catch((error) => { if (!isAbortError(error)) setProjects([]); });
    return () => controller.abort();
  }, []);

  const openArticle = useCallback(async (articleId: string) => {
    setViewLoading(true);
    try { setViewing(await apiFetch<Article>(`/knowledge/articles/${encodeURIComponent(articleId)}`, { fallbackMessage: "เปิดบทความไม่สำเร็จ" })); }
    catch (error) { notify(errorText(error, "เปิดบทความไม่สำเร็จ — อาจถูกลบหรือคุณไม่มีสิทธิ์"), "error"); }
    finally { setViewLoading(false); }
  }, []);

  // CRM Ticket ในคลัง = ข้อความที่ mask แล้ว (หน้า CRM เปิดได้เฉพาะ Ticket ในงานของผู้ใช้ จึงแสดงจากคลังแทน)
  const openCrm = useCallback(async (jobNo: string) => {
    setViewLoading(true);
    try { setCrmView(await apiFetch<CrmTicketView>(`/knowledge/crm/${encodeURIComponent(jobNo)}`, { fallbackMessage: "เปิด CRM Ticket ไม่สำเร็จ" })); }
    catch (error) { notify(errorText(error, "เปิด CRM Ticket ไม่สำเร็จ — อาจไม่มีสิทธิ์ CRM"), "error"); }
    finally { setViewLoading(false); }
  }, []);

  // ลิงก์ตรง #/knowledge?articleId= หรือ ?jobNo= (เช่น จากผลค้นหา คำตอบของผู้ช่วย AI หรือ Claude)
  useEffect(() => {
    const params = new URLSearchParams(window.location.hash.split("?")[1] ?? "");
    const articleId = params.get("articleId"), jobNo = params.get("jobNo");
    if (articleId) void openArticle(articleId);
    else if (jobNo) void openCrm(jobNo);
  }, [openArticle, openCrm]);

  const onTabKey = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key !== "ArrowRight" && event.key !== "ArrowLeft") return;
    const index = tabs.findIndex((x) => x.id === tab);
    const next = tabs[(index + (event.key === "ArrowRight" ? 1 : tabs.length - 1)) % tabs.length];
    setTab(next.id);
    tabRefs.current[next.id]?.focus();
  };

  const openResult = (result: SearchResult) => {
    if (result.sourceType === "Defect") onOpenDefect(result.sourceKey);
    else if (result.sourceType === "Article") void openArticle(result.sourceKey);
    else if (result.sourceType === "File") void openFile(result.sourceKey, result.title);
    else if (result.sourceType === "CrmTicket") void openCrm(result.sourceKey);
    else if (result.sourceType === "TestCase") onOpenTestCase(result.sourceKey);
  };

  const openFile = async (fileId: string, fileName: string) => {
    try { await downloadKnowledgeFile(fileId, fileName); } catch (e) { notify(errorText(e, "เปิดเอกสารไม่สำเร็จ"), "error"); }
  };

  const openCitation = (citation: Citation) => {
    if (citation.sourceType === "Defect") onOpenDefect(citation.sourceKey);
    else if (citation.sourceType === "Article") void openArticle(citation.sourceKey);
    else if (citation.sourceType === "File") void openFile(citation.sourceKey, citation.title);
    else if (citation.sourceType === "CrmTicket") void openCrm(citation.sourceKey);
    else if (citation.sourceType === "TestCase") onOpenTestCase(citation.sourceKey);
  };

  const saveDraft = async (value: ArticleDraft) => {
    const html = value.format === "Html";
    const body = { title: value.title.trim(), bodyMarkdown: html ? null : value.bodyMarkdown, bodyHtml: html ? value.bodyHtml : null, projectId: value.projectId || null, tags: value.tags.trim() || null, status: value.status };
    const saved = await apiFetch<Article>(value.id ? `/knowledge/articles/${value.id}` : "/knowledge/articles", { method: value.id ? "PUT" : "POST", json: body, fallbackMessage: "บันทึกบทความไม่สำเร็จ" });
    notify(value.status === "Published" ? "บันทึกบทความแล้ว — ค้นหาได้ภายในไม่กี่นาที" : "บันทึกฉบับร่างแล้ว (ยังไม่เข้าคลังความรู้)", "success");
    setDraft(null);
    setArticlesVersion((x) => x + 1);
    if (viewing?.knowledgeArticleId === saved.knowledgeArticleId) setViewing(saved);
  };

  const deleteArticle = async (article: Article) => {
    if (!await confirmDialog({ title: "ลบบทความ", message: `ลบ "${article.title}" ออกจากคลังความรู้? ผู้ช่วย AI และการค้นหาจะไม่ใช้บทความนี้อีก`, confirmLabel: "ลบบทความ", tone: "danger" })) return;
    try {
      await apiFetch(`/knowledge/articles/${article.knowledgeArticleId}`, { method: "DELETE", fallbackMessage: "ลบบทความไม่สำเร็จ" });
      notify("ลบบทความแล้ว", "success");
      if (viewing?.knowledgeArticleId === article.knowledgeArticleId) setViewing(null);
      setArticlesVersion((x) => x + 1);
    } catch (error) { notify(errorText(error, "ลบบทความไม่สำเร็จ"), "error"); }
  };

  const editArticle = async (article: Article) => {
    try {
      const full = article.bodyMarkdown != null ? article : await apiFetch<Article>(`/knowledge/articles/${article.knowledgeArticleId}`);
      const isHtml = full.contentFormat === "Html" && Boolean(full.bodyHtml);
      // บทความ HTML: bodyMarkdown คือข้อความที่สกัดไว้ — ใช้เป็นจุดเริ่มถ้าผู้ใช้สลับไป "เขียนเอง"
      setDraft({ id: full.knowledgeArticleId, title: full.title, bodyMarkdown: full.bodyMarkdown ?? "", projectId: full.projectId ?? "", tags: full.tags ?? "", status: full.status, format: isHtml ? "Html" : "Markdown", bodyHtml: isHtml ? full.bodyHtml ?? "" : "" });
    } catch (error) { notify(errorText(error, "เปิดบทความเพื่อแก้ไขไม่สำเร็จ"), "error"); }
  };

  // ลิงก์แชร์สาธารณะ (ใครมีลิงก์ก็อ่านได้ ไม่ต้อง Login) — สร้าง/ยกเลิกได้เฉพาะผู้แก้ไขคลังความรู้ และเฉพาะบทความที่เผยแพร่แล้ว
  const [shareBusy, setShareBusy] = useState(false);
  const updateShareUrl = (articleId: string, shareUrl: string | null) => {
    setViewing((current) => current?.knowledgeArticleId === articleId ? { ...current, shareUrl } : current);
    setArticlesVersion((x) => x + 1);
  };
  const createShare = async (article: Article) => {
    setShareBusy(true);
    try {
      const result = await apiFetch<{ url: string }>(`/knowledge/articles/${article.knowledgeArticleId}/share`, { method: "POST", fallbackMessage: "สร้างลิงก์แชร์ไม่สำเร็จ" });
      updateShareUrl(article.knowledgeArticleId, result.url);
      notify("สร้างลิงก์แชร์แล้ว — คัดลอกส่งให้ผู้อ่านได้เลย", "success");
    } catch (error) { notify(errorText(error, "สร้างลิงก์แชร์ไม่สำเร็จ"), "error"); }
    finally { setShareBusy(false); }
  };
  const revokeShare = async (article: Article) => {
    if (!await confirmDialog({ title: "ยกเลิกลิงก์แชร์", message: `ผู้ที่ได้รับลิงก์ของ "${article.title}" จะเปิดอ่านไม่ได้อีก (แชร์ใหม่จะได้ลิงก์ใหม่)`, confirmLabel: "ยกเลิกลิงก์", tone: "danger" })) return;
    setShareBusy(true);
    try {
      await apiFetch(`/knowledge/articles/${article.knowledgeArticleId}/share`, { method: "DELETE", fallbackMessage: "ยกเลิกลิงก์แชร์ไม่สำเร็จ" });
      updateShareUrl(article.knowledgeArticleId, null);
      notify("ยกเลิกลิงก์แชร์แล้ว", "success");
    } catch (error) { notify(errorText(error, "ยกเลิกลิงก์แชร์ไม่สำเร็จ"), "error"); }
    finally { setShareBusy(false); }
  };

  return <div className={`kn-page${tab === "chat" ? " is-chat" : ""}`}>
    <div className="kn-tabs" role="tablist" aria-label="คลังความรู้">
      {tabs.map((t) => <button key={t.id} ref={(el) => { tabRefs.current[t.id] = el; }} type="button" role="tab" id={`kn-tab-${t.id}`} aria-controls={`kn-panel-${t.id}`} aria-selected={tab === t.id} tabIndex={tab === t.id ? 0 : -1} className={tab === t.id ? "active" : ""} onClick={() => setTab(t.id)} onKeyDown={onTabKey}>
        <span className="material-symbols-outlined" aria-hidden="true">{t.icon}</span>{t.label}
      </button>)}
      {viewLoading && <span className="kn-tab-status" role="status"><span className="spinner inline" aria-hidden="true" /> กำลังเปิด...</span>}
    </div>
    <section id={`kn-panel-${tab}`} role="tabpanel" aria-labelledby={`kn-tab-${tab}`}>
      {tab === "chat" && <KnowledgeChat projects={projects} canViewCrm={canViewCrm} onOpenCitation={openCitation} />}
      {tab === "search" && <SearchPanel projects={projects} canViewCrm={canViewCrm} onOpen={openResult} />}
      {tab === "articles" && <ArticlesPanel projects={projects} canEdit={canEdit} version={articlesVersion} onView={(a) => void openArticle(a.knowledgeArticleId)} onCreate={() => setDraft({ ...emptyDraft })} onImportHtml={() => setDraft({ ...emptyDraft, format: "Html" })} onEdit={(a) => void editArticle(a)} onDelete={(a) => void deleteArticle(a)} />}
      {tab === "documents" && <KnowledgeDocuments projects={projects} canEdit={canEdit} isSysAdmin={isSysAdmin} highlightId={highlightFileId} />}
      {tab === "claude" && <KnowledgeMcp isSysAdmin={isSysAdmin} />}
      {tab === "status" && isSysAdmin && <StatusPanel />}
    </section>
    {viewing && <ModalShell labelledBy="kn-article-title" className="kn-article-view" onDismiss={() => setViewing(null)}>
      <div className="modal-head">
        <div>
          <span className="kn-eyebrow">บทความ · {viewing.projectName ?? "ความรู้กลาง (ทุก Project)"}</span>
          <h2 id="kn-article-title">{viewing.title}</h2>
          <div className="kn-badges">
            <Badge tone={viewing.status === "Published" ? "green" : "yellow"}>{viewing.status === "Published" ? "เผยแพร่" : "ฉบับร่าง"}</Badge>
            {viewing.indexState && indexStateInfo[viewing.indexState] && <Badge tone={indexStateInfo[viewing.indexState].tone}>{indexStateInfo[viewing.indexState].label}</Badge>}
            {viewing.contentFormat === "Html" && <Badge tone="blue">HTML</Badge>}
          </div>
        </div>
        <button type="button" className="modal-close" aria-label="ปิด" onClick={() => setViewing(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>
      <div className="kn-article-meta">
        {viewing.tags && <span><span className="material-symbols-outlined" aria-hidden="true">sell</span>{viewing.tags}</span>}
        <span><span className="material-symbols-outlined" aria-hidden="true">edit_calendar</span>{viewing.updatedAt ? `แก้ไขล่าสุด ${formatThaiDateTime(viewing.updatedAt)}${viewing.updatedByName ? ` โดย ${viewing.updatedByName}` : ""}` : `สร้าง ${formatThaiDateTime(viewing.createdAt)}${viewing.createdByName ? ` โดย ${viewing.createdByName}` : ""}`}</span>
      </div>
      {viewing.contentFormat === "Html" && viewing.bodyHtml
        ? <div className="kn-html-view"><HtmlArticleFrame html={viewing.bodyHtml} title={`เนื้อหาบทความ ${viewing.title}`} /></div>
        : <div className="kn-article-body">{viewing.bodyMarkdown}</div>}
      {(viewing.shareUrl || (canEdit && viewing.status === "Published")) && <section className="kn-share" aria-labelledby="kn-share-title">
        <div className="kn-share-head">
          <span className="material-symbols-outlined" aria-hidden="true">{viewing.shareUrl ? "public" : "share"}</span>
          <div>
            <h3 id="kn-share-title">{viewing.shareUrl ? "แชร์อยู่ — ใครมีลิงก์ก็อ่านได้" : "แชร์ให้คนอื่นอ่าน"}</h3>
            <p>{viewing.shareUrl ? "ผู้อ่านไม่ต้อง Login และแก้ไขไม่ได้ · ลิงก์ไม่หมดอายุจนกว่าจะยกเลิก" : "สร้างลิงก์อ่านอย่างเดียว ส่งให้คนนอกทีมเปิดได้โดยไม่ต้อง Login"}</p>
          </div>
        </div>
        {viewing.shareUrl ? <div className="kn-share-link">
          <input readOnly value={viewing.shareUrl} aria-label="ลิงก์แชร์บทความ" onFocus={(e) => e.currentTarget.select()} />
          <CopyButton text={viewing.shareUrl} className="btn primary">คัดลอกลิงก์</CopyButton>
          <a className="btn" href={viewing.shareUrl} target="_blank" rel="noreferrer"><span className="material-symbols-outlined" aria-hidden="true">open_in_new</span>เปิดดู</a>
          {canEdit && <button type="button" className="btn kn-danger" disabled={shareBusy} onClick={() => void revokeShare(viewing)}><span className="material-symbols-outlined" aria-hidden="true">link_off</span>ยกเลิกลิงก์</button>}
        </div> : <button type="button" className="btn primary" disabled={shareBusy} onClick={() => void createShare(viewing)}>{shareBusy ? <><span className="spinner inline" aria-hidden="true" /> กำลังสร้าง...</> : <><span className="material-symbols-outlined" aria-hidden="true">add_link</span>สร้างลิงก์แชร์</>}</button>}
      </section>}
      {canEdit && <div className="modal-actions">
        <button type="button" className="btn kn-danger" onClick={() => void deleteArticle(viewing)}><span className="material-symbols-outlined" aria-hidden="true">delete</span>ลบ</button>
        <button type="button" className="btn primary" onClick={() => { void editArticle(viewing); setViewing(null); }}><span className="material-symbols-outlined" aria-hidden="true">edit</span>แก้ไข</button>
      </div>}
    </ModalShell>}
    {crmView && <CrmTicketModal ticket={crmView} onClose={() => setCrmView(null)} />}
    {draft && <ArticleEditor draft={draft} projects={projects} onCancel={() => setDraft(null)} onSave={saveDraft} />}
  </div>;
}

// เริ่มที่ "ทุก Project ที่มีสิทธิ์" เสมอ — หน้านี้ซ่อนตัวเลือก Project ด้านบน ถ้าใช้ Project ของ context ผู้ใช้จะไม่รู้ว่าผลถูกจำกัด
function SearchPanel({ projects, canViewCrm, onOpen }: { projects: ProjectItem[]; canViewCrm: boolean; onOpen: (result: SearchResult) => void }) {
  const sources = canViewCrm ? [...searchSources, { value: "CrmTicket", label: "CRM" }] : searchSources;
  const [query, setQuery] = useState("");
  const [projectId, setProjectId] = useState("");
  const [source, setSource] = useState("");
  const [submitted, setSubmitted] = useState<{ q: string; projectId: string; source: string } | null>(null);
  const [results, setResults] = useState<SearchResult[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [attempt, setAttempt] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!submitted) return;
    const controller = new AbortController();
    const params = new URLSearchParams({ q: submitted.q, take: "20" });
    if (submitted.projectId) params.set("projectId", submitted.projectId);
    if (submitted.source) params.set("sources", submitted.source);
    setLoading(true); setError("");
    getJson<SearchResult[]>(`${apiUrl}/knowledge/search?${params}`, controller.signal)
      .then((rows) => setResults(Array.isArray(rows) ? rows : []))
      .catch((e) => { if (!isAbortError(e)) { setResults([]); setError(errorText(e, "ค้นหาไม่สำเร็จ")); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [submitted, attempt]);

  const submit = (event?: FormEvent) => {
    event?.preventDefault();
    const q = query.trim();
    if (q.length < 2) { inputRef.current?.focus(); return; }
    setSubmitted({ q, projectId, source });
  };
  // เปลี่ยนตัวกรองหลังค้นแล้ว = ค้นใหม่ทันที
  const changeFilter = (next: { projectId?: string; source?: string }) => {
    const p = next.projectId ?? projectId, s = next.source ?? source;
    setProjectId(p); setSource(s);
    if (submitted) setSubmitted({ ...submitted, projectId: p, source: s });
  };

  return <div className="kn-search">
    <form className="kn-search-bar" onSubmit={submit} role="search">
      <label className="kn-search-input">
        <span className="material-symbols-outlined" aria-hidden="true">search</span>
        <input ref={inputRef} autoFocus value={query} onChange={(e) => setQuery(e.target.value)} placeholder="เช่น พิมพ์ใบกำกับภาษีไม่ได้, ปิดวันยอดไม่ตรง, รหัส Defect" aria-label="คำค้นความรู้" maxLength={200} />
      </label>
      <select value={projectId} onChange={(e) => changeFilter({ projectId: e.target.value })} aria-label="Project">
        <option value="">ทุก Project ที่มีสิทธิ์</option>
        {projects.map((p) => <option key={p.projectId} value={p.projectId}>{p.projectName}</option>)}
      </select>
      <button type="submit" className="btn primary" disabled={loading || query.trim().length < 2}>{loading ? <><span className="spinner inline" aria-hidden="true" /> กำลังค้นหา</> : "ค้นหา"}</button>
    </form>
    <div className="kn-chips" role="group" aria-label="แหล่งความรู้">
      {sources.map((s) => <button key={s.value} type="button" aria-pressed={source === s.value} className={source === s.value ? "active" : ""} onClick={() => changeFilter({ source: s.value })}>{s.label}</button>)}
    </div>
    {!submitted ? <div className="kn-empty">
      <span className="material-symbols-outlined" aria-hidden="true">travel_explore</span>
      <b>ค้นหาจาก Defect Test Case บทความ เอกสาร{canViewCrm ? " และ CRM Ticket" : ""}ในทุก Project ที่คุณมีสิทธิ์</b>
      <span>พิมพ์อาการ ข้อความ error ชื่อเมนู หรือรหัส Defect — ระบบตัดคำภาษาไทยให้อัตโนมัติ</span>
    </div> : error ? <div className="kn-empty kn-error" role="alert">
      <span className="material-symbols-outlined" aria-hidden="true">error</span><b>{error}</b>
      <button type="button" className="btn" onClick={() => setAttempt((x) => x + 1)}>ลองใหม่</button>
    </div> : !loading && results.length === 0 ? <div className="kn-empty">
      <span className="material-symbols-outlined" aria-hidden="true">search_off</span>
      <b>ไม่พบความรู้ที่ตรงกับ "{submitted.q}"</b>
      <span>ลองใช้คำที่สั้นลง คำพ้องความหมาย หรือเลือก "ทุก Project"</span>
    </div> : <>
      <p className="kn-result-count" role="status">{loading ? "กำลังค้นหา..." : `พบ ${results.length} รายการ สำหรับ "${submitted.q}"`}</p>
      <ol className={`kn-results${loading ? " is-loading" : ""}`} aria-busy={loading}>
        {results.map((r) => <li key={r.knowledgeDocumentId} className="kn-result" data-source={r.sourceType}>
          <span className={`kn-result-icon tone-${sourceTone[r.sourceType]}`} aria-hidden="true"><span className="material-symbols-outlined">{sourceIcon[r.sourceType]}</span></span>
          <div className="kn-result-main">
            <div className="kn-result-head">
              <Badge tone={sourceTone[r.sourceType]}>{sourceLabel[r.sourceType]}</Badge>
              <button type="button" className="kn-result-title" onClick={() => onOpen(r)}>{r.title}</button>
            </div>
            <p className="kn-result-snippet">{r.snippet}</p>
            <div className="kn-result-meta">
              <span>{r.projectName ?? "ความรู้กลาง"}</span>
              {r.matchedChunks > 1 && <span>ตรงกัน {r.matchedChunks} ช่วง</span>}
              {r.indexedAt && <span title={formatThaiDateTime(r.indexedAt)}>อัปเดตดัชนี {formatThaiDateTime(r.indexedAt)}</span>}
            </div>
          </div>
          <button type="button" className="btn kn-result-open" onClick={() => onOpen(r)} aria-label={`เปิด ${r.title}`}>เปิด<span className="material-symbols-outlined" aria-hidden="true">arrow_forward</span></button>
        </li>)}
      </ol>
    </>}
  </div>;
}

function CrmTicketModal({ ticket, onClose }: { ticket: CrmTicketView; onClose: () => void }) {
  const closed = crmClosedStatuses.includes(ticket.status ?? "");
  const meta = [{ icon: "category", value: ticket.jobType }, { icon: "support_agent", value: ticket.serviceType }, { icon: "inventory_2", value: ticket.product }].filter((x) => x.value);
  return <ModalShell labelledBy="kn-crm-title" className="kn-article-view" onDismiss={onClose}>
    <div className="modal-head">
      <div>
        <span className="kn-eyebrow">CRM Ticket · {ticket.jobNo}</span>
        <h2 id="kn-crm-title">{ticket.subject || "(ไม่มีชื่อเรื่อง)"}</h2>
        <div className="kn-badges">
          {ticket.status && <Badge tone={closed ? "green" : "blue"}>{ticket.status}</Badge>}
          {ticket.indexState && indexStateInfo[ticket.indexState] && <Badge tone={indexStateInfo[ticket.indexState].tone}>{indexStateInfo[ticket.indexState].label}</Badge>}
        </div>
      </div>
      <button type="button" className="modal-close" aria-label="ปิด" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
    </div>
    <div className="kn-article-meta">
      {meta.map((m) => <span key={m.icon}><span className="material-symbols-outlined" aria-hidden="true">{m.icon}</span>{m.value}</span>)}
      {ticket.contactDate && <span><span className="material-symbols-outlined" aria-hidden="true">event</span>แจ้งเมื่อ {formatThaiDateTime(ticket.contactDate)}</span>}
      <span><span className="material-symbols-outlined" aria-hidden="true">sync</span>ดึงจาก CRM {formatThaiDateTime(ticket.fetchedAt)}</span>
    </div>
    <p className="kn-crm-note"><span className="material-symbols-outlined" aria-hidden="true">shield_person</span>ชื่อลูกค้า สาขา พนักงาน เบอร์โทร และอีเมลถูกปิดไว้ก่อนเข้าคลังความรู้ — ข้อความเต็มดูได้ที่หน้า CRM เมื่อ Ticket อยู่ในงานของคุณ</p>
    <h3 className="kn-crm-heading">รายละเอียดปัญหา</h3>
    <div className="kn-article-body">{ticket.description || "ไม่มีรายละเอียด"}</div>
    {ticket.answers.length > 0 && <>
      <h3 className="kn-crm-heading">การตอบ ({ticket.answers.length.toLocaleString("th-TH")})</h3>
      <ol className="kn-crm-answers">{ticket.answers.map((a) => <li key={a.answerNo}>
        <small>{a.answeredAt ? formatThaiDateTime(a.answeredAt) : "ไม่ทราบวันที่"}</small>
        <p>{a.text}</p>
      </li>)}</ol>
    </>}
  </ModalShell>;
}

function ArticlesPanel({ projects, canEdit, version, onView, onCreate, onImportHtml, onEdit, onDelete }: { projects: ProjectItem[]; canEdit: boolean; version: number; onView: (a: Article) => void; onCreate: () => void; onImportHtml: () => void; onEdit: (a: Article) => void; onDelete: (a: Article) => void }) {
  const [search, setSearch] = useState("");
  const debounced = useDebounced(search, 300);
  const [status, setStatus] = useState("");
  const [projectId, setProjectId] = useState("");
  const [rows, setRows] = useState<Article[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    const params = new URLSearchParams();
    if (debounced.trim()) params.set("search", debounced.trim());
    if (status) params.set("status", status);
    if (projectId) params.set("projectId", projectId);
    setLoading(true); setError("");
    getJson<Article[]>(`${apiUrl}/knowledge/articles?${params}`, controller.signal)
      .then((data) => setRows(Array.isArray(data) ? data : []))
      .catch((e) => { if (!isAbortError(e)) { setRows([]); setError(errorText(e, "โหลดบทความไม่สำเร็จ")); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [debounced, status, projectId, version, attempt]);

  const filtered = Boolean(debounced.trim() || status || projectId);
  return <div className="kn-articles">
    <div className="kn-toolbar">
      <label className="kn-search-input"><span className="material-symbols-outlined" aria-hidden="true">search</span><input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="ค้นหาชื่อบทความหรือ Tag" aria-label="ค้นหาบทความ" /></label>
      <select value={projectId} onChange={(e) => setProjectId(e.target.value)} aria-label="Project"><option value="">ทุก Project</option>{projects.map((p) => <option key={p.projectId} value={p.projectId}>{p.projectName}</option>)}</select>
      {canEdit && <select value={status} onChange={(e) => setStatus(e.target.value)} aria-label="สถานะ"><option value="">ทุกสถานะ</option><option value="Published">เผยแพร่</option><option value="Draft">ฉบับร่าง</option></select>}
      {canEdit && <button type="button" className="btn" onClick={onImportHtml}><span className="material-symbols-outlined" aria-hidden="true">upload_file</span>นำเข้า HTML</button>}
      <DeletedItemsButton type="knowledge-articles" label="บทความ" onRestored={() => setAttempt((x) => x + 1)} />
      {canEdit && <button type="button" className="btn primary" onClick={onCreate}><span className="material-symbols-outlined" aria-hidden="true">add</span>เขียนบทความ</button>}
    </div>
    {error ? <div className="kn-empty kn-error" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><b>{error}</b><button type="button" className="btn" onClick={() => setAttempt((x) => x + 1)}>ลองใหม่</button></div>
      : loading && rows.length === 0 ? <div className="kn-empty" role="status"><span className="spinner" aria-hidden="true" /><span>กำลังโหลดบทความ...</span></div>
      : rows.length === 0 ? <div className="kn-empty">
        <span className="material-symbols-outlined" aria-hidden="true">menu_book</span>
        <b>{filtered ? "ไม่พบบทความตามตัวกรอง" : "ยังไม่มีบทความในคลังความรู้"}</b>
        <span>{filtered ? "ลองล้างคำค้นหรือเปลี่ยน Project" : canEdit ? "เขียนวิธีแก้ปัญหา ขั้นตอนงาน หรือ FAQ ที่ทีมใช้บ่อย เพื่อให้ค้นหาและให้ผู้ช่วย AI ใช้ตอบได้" : "ผู้มีสิทธิ์แก้ไขคลังความรู้ (QA Lead) เพิ่มบทความได้"}</span>
        {canEdit && !filtered && <button type="button" className="btn primary" onClick={onCreate}>เขียนบทความแรก</button>}
      </div>
      : <div className="table-wrap kn-table-wrap"><table className="table-cards kn-table" aria-busy={loading}>
        <thead><tr><th>บทความ</th><th>Project</th><th>สถานะ</th><th>แก้ไขล่าสุด</th><th><span className="sr-only">การจัดการ</span></th></tr></thead>
        <tbody>{rows.map((a) => <tr key={a.knowledgeArticleId}>
          <td><button type="button" className="kn-link" onClick={() => onView(a)}>{a.title}</button>{(a.contentFormat === "Html" || a.shareUrl) && <span className="kn-row-flags">{a.contentFormat === "Html" && <span title="นำเข้าจาก HTML — แสดงผลเหมือนไฟล์ต้นฉบับ"><span className="material-symbols-outlined" aria-hidden="true">code</span>HTML</span>}{a.shareUrl && <span className="is-shared" title="มีลิงก์แชร์ — ใครมีลิงก์ก็อ่านได้"><span className="material-symbols-outlined" aria-hidden="true">public</span>แชร์อยู่</span>}</span>}<small className="kn-excerpt">{a.excerpt}</small>{a.tags && <small className="kn-tags"><span className="material-symbols-outlined" aria-hidden="true">sell</span>{a.tags}</small>}</td>
          <td>{a.projectName ?? "ความรู้กลาง"}</td>
          <td><div className="kn-badges"><Badge tone={a.status === "Published" ? "green" : "yellow"}>{a.status === "Published" ? "เผยแพร่" : "ฉบับร่าง"}</Badge>{a.status === "Published" && a.indexState && indexStateInfo[a.indexState] && a.indexState !== "Indexed" && <Badge tone={indexStateInfo[a.indexState].tone}>{indexStateInfo[a.indexState].label}</Badge>}</div></td>
          <td><span className="kn-updated">{formatThaiDateTime(a.updatedAt ?? a.createdAt)}</span><small>{a.updatedByName ?? a.createdByName ?? ""}</small></td>
          <td className="kn-row-actions">
            <button type="button" className="table-action icon-only" title="ดู" aria-label={`ดู ${a.title}`} onClick={() => onView(a)}><span className="material-symbols-outlined" aria-hidden="true">visibility</span></button>
            {canEdit && <button type="button" className="table-action icon-only" title="แก้ไข" aria-label={`แก้ไข ${a.title}`} onClick={() => onEdit(a)}><span className="material-symbols-outlined" aria-hidden="true">edit</span></button>}
            {canEdit && <button type="button" className="table-action danger-action icon-only" title="ลบ" aria-label={`ลบ ${a.title}`} onClick={() => onDelete(a)}><span className="material-symbols-outlined" aria-hidden="true">delete</span></button>}
          </td>
        </tr>)}</tbody>
      </table></div>}
  </div>;
}

// แบบฟอร์มเนื้อหาสำหรับผู้เริ่มเขียน — บรรทัดที่ขึ้นต้นด้วย "›" คือคำแนะนำให้พิมพ์ทับ ต้องแทนที่ให้หมดก่อนบันทึก (ไม่ให้หลุดเข้าคลังความรู้)
// หัวข้อใช้ "## " เพราะ TextChunker ตัดช่วงข้อความตามหัวข้อ Markdown — ค้นเจอแม่นขึ้น
const hintLine = /^(\s*(?:[-*]\s+|\d{1,2}[.)]\s+)?)› .*$/gm;
const articleTemplates = [
  { id: "fix", icon: "build", name: "วิธีแก้ปัญหา", flow: "อาการ → สาเหตุ → วิธีแก้", body: "## อาการ\n› สิ่งที่ผู้ใช้เห็น เช่น ข้อความ error หรือหน้าจอที่เกิดปัญหา\n\n## สาเหตุ\n› ทำไมถึงเกิด (ถ้ายังไม่ทราบ เขียนว่า \"ยังไม่ทราบสาเหตุ\")\n\n## วิธีแก้\n1. › ขั้นตอนแรก\n2. › ขั้นตอนถัดไป" },
  { id: "howto", icon: "checklist", name: "ขั้นตอนการทำงาน", flow: "ใช้เมื่อ → ขั้นตอน → ผลลัพธ์", body: "## ใช้เมื่อ\n› งานนี้ทำตอนไหน และใครเป็นคนทำ\n\n## สิ่งที่ต้องเตรียม\n- › สิทธิ์ ข้อมูล หรือไฟล์ที่ต้องมีก่อนเริ่ม\n\n## ขั้นตอน\n1. › ขั้นตอนแรก\n2. › ขั้นตอนถัดไป\n\n## ผลลัพธ์ที่ถูกต้อง\n› ทำเสร็จแล้วควรเห็นอะไร" },
  { id: "faq", icon: "help", name: "คำถามที่พบบ่อย", flow: "คำถาม → คำตอบ", body: "## คำถาม\n› เขียนคำถามแบบที่ผู้ใช้ถามจริง\n\n## คำตอบ\n› ตอบให้สั้นและตรงคำถาม" },
];
// ตำแหน่ง [เริ่ม, จบ] ของข้อความแนะนำถัดไปหลัง from (วนกลับต้นเมื่อไม่เจอ) — เลือกไว้ให้พิมพ์ทับได้ทันที
const findHint = (text: string, from: number): [number, number] | null => {
  const all = [...text.matchAll(hintLine)].map((m) => [m.index! + m[1].length, m.index! + m[0].length] as [number, number]);
  return all.find(([start]) => start >= from) ?? all[0] ?? null;
};
const listMarker = /^(#{1,4}\s+|[-*]\s+|\d{1,2}[.)]\s+)/;

function ArticleEditor({ draft, projects, onCancel, onSave }: { draft: ArticleDraft; projects: ProjectItem[]; onCancel: () => void; onSave: (value: ArticleDraft) => Promise<void> }) {
  const [value, setValue] = useState<ArticleDraft>(draft);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const bodyRef = useRef<HTMLTextAreaElement>(null);
  const hintCount = (value.bodyMarkdown.match(hintLine) ?? []).length;
  const missing = [!value.title.trim() && "หัวข้อ", !value.bodyMarkdown.trim() && "เนื้อหา", hintCount > 0 && `แทนที่ข้อความแนะนำ (›) ${hintCount} จุด`].filter(Boolean) as string[];
  const set = (patch: Partial<ArticleDraft>) => setValue((current) => ({ ...current, ...patch }));
  // แก้เนื้อหาจากปุ่ม แล้วคืน focus + ตำแหน่งเลือกให้ textarea (ต้องรอ render ก่อน)
  const applyBody = (text: string, selStart: number, selEnd = selStart) => {
    set({ bodyMarkdown: text });
    requestAnimationFrame(() => { const el = bodyRef.current; if (!el) return; el.focus(); el.setSelectionRange(selStart, selEnd); });
  };
  const selection = () => { const el = bodyRef.current, text = value.bodyMarkdown; return { text, start: el?.selectionStart ?? text.length, end: el?.selectionEnd ?? text.length }; };
  const applyTemplate = async (template: typeof articleTemplates[number]) => {
    if (value.bodyMarkdown.trim() && !await confirmDialog({ title: "แทนที่เนื้อหาเดิม?", message: `เนื้อหาที่เขียนไว้จะถูกแทนที่ด้วยแบบฟอร์ม "${template.name}"`, confirmLabel: "แทนที่", tone: "danger" })) return;
    const hint = findHint(template.body, 0);
    applyBody(template.body, hint?.[0] ?? 0, hint?.[1] ?? 0);
  };
  const nextHint = () => { const { text, end } = selection(); const hint = findHint(text, end); if (hint) applyBody(text, hint[0], hint[1]); };
  // ทำบรรทัดที่เลือกเป็นหัวข้อ/รายการ (แทนเครื่องหมายเดิมของบรรทัดนั้น)
  const prefixLines = (marker: (index: number) => string) => {
    const { text, start, end } = selection();
    const lineStart = text.lastIndexOf("\n", start - 1) + 1;
    const lineEndAt = text.indexOf("\n", end);
    const lineEnd = lineEndAt === -1 ? text.length : lineEndAt;
    const block = text.slice(lineStart, lineEnd).split("\n").map((line, i) => marker(i) + line.replace(listMarker, "")).join("\n");
    applyBody(text.slice(0, lineStart) + block + text.slice(lineEnd), lineStart + block.length);
  };
  const wrapBold = () => {
    const { text, start, end } = selection();
    const selected = text.slice(start, end) || "ข้อความสำคัญ";
    applyBody(`${text.slice(0, start)}**${selected}**${text.slice(end)}`, start + 2, start + 2 + selected.length);
  };
  const formatTools = [
    { icon: "title", label: "หัวข้อ", title: "ทำบรรทัดนี้เป็นหัวข้อ (## ) — แบ่งเรื่องเป็นช่วง ๆ ช่วยให้ค้นเจอแม่นขึ้น", run: () => prefixLines(() => "## ") },
    { icon: "format_bold", label: "ตัวหนา", title: "ทำข้อความที่เลือกเป็นตัวหนา (**ข้อความ**)", run: wrapBold },
    { icon: "format_list_bulleted", label: "รายการ", title: "ทำบรรทัดที่เลือกเป็นรายการ (- )", run: () => prefixLines(() => "- ") },
    { icon: "format_list_numbered", label: "ขั้นตอน 1, 2, 3", title: "ทำบรรทัดที่เลือกเป็นขั้นตอนเรียงเลข (1. 2. 3.)", run: () => prefixLines((i) => `${i + 1}. `) },
  ];
  const save = async () => {
    if (missing.length) return;
    setSaving(true); setError("");
    try { await onSave(value); } catch (e) { setError(errorText(e, "บันทึกบทความไม่สำเร็จ")); } finally { setSaving(false); }
  };
  return <ModalShell labelledBy="kn-editor-title" className="kn-editor" onDismiss={onCancel}>
    <div className="modal-head">
      <div>
        <span className="kn-eyebrow">{value.id ? "แก้ไขบทความ" : "เขียนบทความใหม่"}</span>
        <h2 id="kn-editor-title">{value.title.trim() || "บทความใหม่"}</h2>
      </div>
      <button type="button" className="modal-close" aria-label="ปิด" onClick={onCancel}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
    </div>
    <div className="form-grid kn-editor-grid">
      <label className="kn-span-2">หัวข้อ <span className="required">*</span><input autoFocus value={value.title} maxLength={300} onChange={(e) => set({ title: e.target.value })} placeholder="เช่น วิธีแก้ปิดวันแล้วยอดเงินสดไม่ตรง" /></label>
      <label>Project<select value={value.projectId} onChange={(e) => set({ projectId: e.target.value })}><option value="">ความรู้กลาง (ทุก Project)</option>{projects.map((p) => <option key={p.projectId} value={p.projectId}>{p.projectName}</option>)}</select><small>ความรู้กลาง = ทุกคนที่ใช้คลังความรู้เห็น</small></label>
      <label>Tag<input value={value.tags} maxLength={500} onChange={(e) => set({ tags: e.target.value })} placeholder="คั่นด้วยจุลภาค เช่น ปิดวัน, รายงาน" /></label>
      <fieldset className="kn-span-2 kn-status-field">
        <legend>สถานะ</legend>
        <label><input type="radio" name="kn-status" checked={value.status === "Published"} onChange={() => set({ status: "Published" })} />เผยแพร่ <small>ค้นหาได้และผู้ช่วย AI ใช้ตอบได้</small></label>
        <label><input type="radio" name="kn-status" checked={value.status === "Draft"} onChange={() => set({ status: "Draft" })} />ฉบับร่าง <small>เห็นเฉพาะผู้แก้ไขคลังความรู้</small></label>
      </fieldset>
      <div className="kn-span-2 kn-body-field">
        <div className="kn-body-head"><label htmlFor="kn-body">เนื้อหา <span className="required">*</span></label><small aria-live="off">{value.bodyMarkdown.trim().length.toLocaleString("th-TH")} ตัวอักษร</small></div>
        <div className="kn-templates" role="group" aria-labelledby="kn-templates-title">
          <p id="kn-templates-title"><span className="kn-step-no" aria-hidden="true">1</span>เลือกแบบฟอร์ม <small>{value.bodyMarkdown.trim() ? "(เลือกใหม่จะแทนที่เนื้อหาเดิม)" : "(แนะนำ — หรือพิมพ์เองในช่องด้านล่างได้เลย)"}</small></p>
          <div className="kn-template-list">{articleTemplates.map((t) => <button key={t.id} type="button" className="kn-template" onClick={() => void applyTemplate(t)}><span className="material-symbols-outlined" aria-hidden="true">{t.icon}</span><span><b>{t.name}</b><small>{t.flow}</small></span></button>)}</div>
        </div>
        <p className="kn-step-title"><span className="kn-step-no" aria-hidden="true">2</span>พิมพ์ทับข้อความแนะนำ (บรรทัดที่ขึ้นต้นด้วย ›)</p>
        <div className="kn-body-box">
          <div className="kn-format-bar" role="toolbar" aria-label="จัดรูปแบบเนื้อหา">
            {formatTools.map((tool) => <button key={tool.label} type="button" title={tool.title} onMouseDown={(e) => e.preventDefault()} onClick={tool.run}><span className="material-symbols-outlined" aria-hidden="true">{tool.icon}</span>{tool.label}</button>)}
          </div>
          <textarea id="kn-body" ref={bodyRef} className="kn-body-input" value={value.bodyMarkdown} aria-describedby="kn-body-help" onChange={(e) => set({ bodyMarkdown: e.target.value })} placeholder={"เลือกแบบฟอร์มด้านบน หรือพิมพ์เนื้อหาได้เลย\n\nตัวอย่าง:\n## อาการ\nปิดวันแล้วยอดเงินสดในรายงานไม่ตรงกับในลิ้นชัก\n\n## วิธีแก้\n1. เปิดเมนู ..."} />
        </div>
        <div id="kn-body-help" className="kn-body-help">
          {hintCount > 0
            ? <p className="kn-hint-left" role="status"><span className="material-symbols-outlined" aria-hidden="true">edit_note</span>เหลือข้อความแนะนำที่ยังไม่ได้แทนที่ {hintCount} จุด<button type="button" className="kn-link-button" onMouseDown={(e) => e.preventDefault()} onClick={nextHint}>ไปจุดถัดไป</button></p>
            : <p><span className="material-symbols-outlined" aria-hidden="true">lock</span>ไม่ต้องกังวลข้อมูลส่วนตัว — ระบบปิดอีเมล เบอร์โทร และ password ให้อัตโนมัติก่อนเข้าคลัง</p>}
          <details className="kn-tips">
            <summary>เคล็ดลับ: เขียนอย่างไรให้ค้นเจอและผู้ช่วย AI ตอบได้ดี</summary>
            <ul>
              <li><b>1 บทความ = 1 เรื่อง</b> — ถ้ามีหลายปัญหา แยกเป็นหลายบทความ</li>
              <li><b>ใช้คำที่ผู้ใช้พิมพ์ค้นจริง</b> เช่น ข้อความ error ชื่อเมนู ชื่อรายงาน</li>
              <li><b>แบ่งเป็นหัวข้อสั้น ๆ</b> ด้วยปุ่ม "หัวข้อ" — ระบบตัดเนื้อหาเป็นช่วงตามหัวข้อ ทำให้ค้นเจอแม่นขึ้น</li>
              <li><b>เขียนขั้นตอนเป็นข้อ ๆ</b> ด้วยปุ่ม "ขั้นตอน 1, 2, 3" — ผู้ช่วย AI ยกไปตอบต่อได้ทันที</li>
            </ul>
          </details>
        </div>
      </div>
    </div>
    {error && <p className="kn-form-error" role="alert">{error}</p>}
    <div className="modal-actions">
      <span className={missing.length ? "kn-missing" : "kn-ready"} role="status">{missing.length ? `ยังขาด: ${missing.join(", ")}` : "ข้อมูลครบ พร้อมบันทึก"}</span>
      <button type="button" className="btn" onClick={onCancel}>ยกเลิก</button>
      <button type="button" className="btn primary" disabled={saving || missing.length > 0} onClick={() => void save()}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span>{value.status === "Published" ? "บันทึกและเผยแพร่" : "บันทึกฉบับร่าง"}</>}</button>
    </div>
  </ModalShell>;
}

// ค้นตามความหมาย (Phase 5): ความคืบหน้าการสร้างเวกเตอร์ของช่วงข้อความ + ข้อผิดพลาดล่าสุดของ Embedding provider
function EmbeddingStatusBox({ status }: { status: EmbeddingStatus }) {
  const percent = status.eligible ? Math.floor((status.embedded / status.eligible) * 100) : 0;
  return <div className="kn-crm-status kn-embed-status" role="note">
    <span className="material-symbols-outlined" aria-hidden="true">hub</span>
    <div>
      <b>ค้นตามความหมาย (Embedding)</b>
      {status.enabled ? <>
        <span>{status.provider} · {status.model}{status.isExternal ? " (ภายนอกองค์กร)" : ""} — สร้างเวกเตอร์แล้ว {status.embedded.toLocaleString("th-TH")} จาก {status.eligible.toLocaleString("th-TH")} ช่วงข้อความ ({percent}%)</span>
        <span className="kn-embed-bar" aria-hidden="true"><span style={{ width: `${percent}%` }} /></span>
        {status.error ? <small className="kn-embed-error" role="alert">ล่าสุดไม่สำเร็จ{status.lastErrorAt ? ` (${formatThaiDateTime(status.lastErrorAt)})` : ""}: {status.error}</small>
          : <small>{status.embedded < status.eligible ? "ระบบสร้างเวกเตอร์ต่อเนื่องทุกรอบ — ระหว่างนี้ช่วงที่ยังไม่มีเวกเตอร์ค้นได้ด้วยคำ" : "ค้นด้วยคำและความหมายรวมกัน (Reciprocal Rank Fusion)"}{status.isExternal ? " · ไม่ส่งเอกสารที่ห้ามส่งให้ AI และ CRM ที่ไม่ได้อนุญาตไปสร้างเวกเตอร์" : ""}</small>}
      </> : <>
        <span>ยังไม่เปิด — ค้นด้วยคำ (Full-Text ภาษาไทย) อย่างเดียว</span>
        <small>{status.error ?? "เลือก Embedding Provider (AI Local เช่น bge-m3 หรือ OpenAI) ได้ที่ Setting Center → AI Configuration → คลังความรู้"}</small>
      </>}
    </div>
  </div>;
}

function StatusPanel() {
  const [status, setStatus] = useState<IndexStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError("");
    getJson<IndexStatus>(`${apiUrl}/knowledge/status`, controller.signal)
      .then(setStatus)
      .catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลดสถานะคลังความรู้ไม่สำเร็จ")); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [attempt]);

  const reindex = async () => {
    if (!await confirmDialog({ title: "สร้างดัชนีใหม่ทั้งหมด", message: "สร้าง chunk ใหม่ให้ทุกเอกสารในคลัง (ใช้เมื่อเปลี่ยนวิธีแบ่งข้อความหรือ Embedding) — ระหว่างนี้ยังค้นหาได้ แต่ผลอาจไม่ครบจนกว่าจะเสร็จ", confirmLabel: "สร้างใหม่ทั้งหมด" })) return;
    setBusy(true);
    try { const result = await apiFetch<{ documents: number }>("/knowledge/reindex", { method: "POST", fallbackMessage: "สั่งสร้างดัชนีใหม่ไม่สำเร็จ" }); notify(`สั่งสร้างดัชนีใหม่ ${result.documents.toLocaleString("th-TH")} เอกสารแล้ว`, "success"); setAttempt((x) => x + 1); }
    catch (e) { notify(errorText(e, "สั่งสร้างดัชนีใหม่ไม่สำเร็จ"), "error"); }
    finally { setBusy(false); }
  };

  if (error) return <div className="kn-empty kn-error" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><b>{error}</b><button type="button" className="btn" onClick={() => setAttempt((x) => x + 1)}>ลองใหม่</button></div>;
  if (!status) return <div className="kn-empty" role="status"><span className="spinner" aria-hidden="true" /><span>กำลังโหลดสถานะ...</span></div>;
  const totals = status.sources.reduce((acc, s) => ({ indexed: acc.indexed + s.indexed, pending: acc.pending + s.pending, failed: acc.failed + s.failed, chunks: acc.chunks + s.chunks }), { indexed: 0, pending: 0, failed: 0, chunks: 0 });
  return <div className={`kn-status${loading ? " is-loading" : ""}`} aria-busy={loading}>
    <div className="kn-status-head">
      <div>
        <h3>สถานะคลังความรู้</h3>
        <p>{status.indexerEnabled ? "ระบบสร้างดัชนีอัตโนมัติทุก 5 นาที (และทันทีเมื่อบันทึกบทความ)" : "ปิดการสร้างดัชนีอัตโนมัติอยู่ (Knowledge:IndexerEnabled=false)"}</p>
      </div>
      <div className="kn-status-actions">
        <button type="button" className="btn" onClick={() => setAttempt((x) => x + 1)} disabled={loading}><span className="material-symbols-outlined" aria-hidden="true">refresh</span>รีเฟรช</button>
        <button type="button" className="btn" onClick={() => void reindex()} disabled={busy}><span className="material-symbols-outlined" aria-hidden="true">restart_alt</span>สร้างดัชนีใหม่ทั้งหมด</button>
      </div>
    </div>
    {status.embedding && <EmbeddingStatusBox status={status.embedding} />}
    {status.crm && <div className="kn-crm-status" role="note">
      <span className="material-symbols-outlined" aria-hidden="true">support_agent</span>
      <div>
        <b>ข้อความ CRM Ticket</b>
        <span>{status.crm.enabled ? `ดึงแล้ว ${status.crm.fetched.toLocaleString("th-TH")} จาก ${status.crm.candidates.toLocaleString("th-TH")} Ticket (ปิดแล้วหรือมีการตอบ) · รอดึง ${status.crm.pending.toLocaleString("th-TH")}${status.crm.failed ? ` · ไม่สำเร็จ ${status.crm.failed.toLocaleString("th-TH")}` : ""}${status.crm.lastFetchedAt ? ` · ล่าสุด ${formatThaiDateTime(status.crm.lastFetchedAt)}` : ""}` : "ปิดการดึงข้อความ CRM อยู่ (Knowledge:CrmFetchEnabled=false)"}</span>
        <small>{status.crm.allowExternalAi ? "ผู้ดูแลอนุญาตส่งข้อมูล CRM (mask แล้ว) ให้ AI ภายนอก รวมถึง Claude" : "ไม่ส่งข้อมูล CRM ให้ AI ภายนอก — ใช้ได้กับ AI แบบ Local และการค้นหาเท่านั้น (เปลี่ยนได้ที่ Setting Center)"}</small>
      </div>
    </div>}
    {!status.indexerEnabled && <div className="kn-notice" role="note"><span className="material-symbols-outlined" aria-hidden="true">pause_circle</span>เอกสารใหม่และที่แก้ไขจะยังไม่ถูกค้นเจอจนกว่าจะเปิดการสร้างดัชนี</div>}
    <dl className="kn-totals">
      <div><dt>ค้นหาได้</dt><dd>{totals.indexed.toLocaleString("th-TH")}</dd><small>เอกสาร · {totals.chunks.toLocaleString("th-TH")} ช่วงข้อความ</small></div>
      <div><dt>รอสร้างดัชนี</dt><dd className={totals.pending ? "is-warning" : ""}>{totals.pending.toLocaleString("th-TH")}</dd><small>เอกสาร</small></div>
      <div><dt>ไม่สำเร็จ</dt><dd className={totals.failed ? "is-danger" : ""}>{totals.failed.toLocaleString("th-TH")}</dd><small>เอกสาร</small></div>
    </dl>
    <div className="table-wrap"><table className="table-cards kn-table">
      <thead><tr><th>แหล่งข้อมูล</th><th>ค้นหาได้</th><th>รอ</th><th>ไม่สำเร็จ</th><th>ไม่อยู่ในคลัง</th><th>ช่วงข้อความ</th><th>อัปเดตล่าสุด</th></tr></thead>
      <tbody>{status.sources.map((s) => <tr key={s.sourceType}>
        <td><span className="kn-source"><span className="material-symbols-outlined" aria-hidden="true">{sourceIcon[s.sourceType]}</span>{sourceLabel[s.sourceType]}{s.sourceType === "CrmTicket" && status.crm?.enabled && <span className="kn-source-sub"><CrmMirrorIntervalEditor minutes={status.crm.mirrorIntervalMinutes ?? 15} onSaved={(minutes) => setStatus((current) => current?.crm ? { ...current, crm: { ...current.crm, mirrorIntervalMinutes: minutes } } : current)} /></span>}</span></td>
        <td>{s.indexed.toLocaleString("th-TH")}</td><td>{s.pending.toLocaleString("th-TH")}</td><td>{s.failed.toLocaleString("th-TH")}</td><td>{s.excluded.toLocaleString("th-TH")}</td><td>{s.chunks.toLocaleString("th-TH")}</td>
        <td>{s.lastIndexedAt ? formatThaiDateTime(s.lastIndexedAt) : "—"}</td>
      </tr>)}</tbody>
    </table></div>
    {status.failures.length > 0 && <div className="kn-failures">
      <h4>เอกสารที่สร้างดัชนีไม่สำเร็จ</h4>
      <ul>{status.failures.map((f) => <li key={f.knowledgeDocumentId}><b>{sourceLabel[f.sourceType]} · {f.title}</b><span>{f.error ?? "ไม่ทราบสาเหตุ"}</span>{f.attemptedAt && <small>{formatThaiDateTime(f.attemptedAt)}</small>}</li>)}</ul>
    </div>}
  </div>;
}
