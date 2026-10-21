import { Fragment, useCallback, useEffect, useRef, useState, type FormEvent, type KeyboardEvent, type ReactNode } from "react";
import { ApiError, apiFetch, apiUrl, authHeaders, getJson, isAbortError } from "../api";
import { CopyButton } from "../components/CopyButton";
import { confirmDialog, notify } from "../components/dialogStore";
import { formatThaiDateTime } from "../dateTime";
import type { ProjectItem } from "../shared/types";
import "./KnowledgeChat.css";

// ผู้ช่วย AI คลังความรู้ (AI_KNOWLEDGE_CHAT_PLAN.md Phase 2) — คำตอบมาเป็น Server-Sent Events: sources → delta… → done | error

export type Citation = { number: number; knowledgeDocumentId: string; sourceType: "Defect" | "CrmTicket" | "Article" | "File"; sourceKey: string; title: string; projectName?: string | null; sourceUrl?: string | null; cited: boolean };
type Session = { chatSessionId: string; title: string; projectId?: string | null; createdAt: string; updatedAt: string };
type ServerMessage = { chatMessageId: number; role: "user" | "assistant"; content: string; citations: Citation[]; model?: string | null; feedback?: number | null; createdAt: string };
type ChatItem = { key: string; id?: number; role: "user" | "assistant"; content: string; citations: Citation[]; model?: string | null; feedback?: number | null; status: "done" | "streaming" | "error" | "stopped"; error?: string; crmExcluded?: boolean; question?: string };

const sessionStorageKey = "qa.knowledge.chatSession";
const sourceLabel: Record<Citation["sourceType"], string> = { Defect: "Defect", CrmTicket: "CRM", Article: "บทความ", File: "เอกสาร" };
const sourceIcon: Record<Citation["sourceType"], string> = { Defect: "bug_report", CrmTicket: "support_agent", Article: "article", File: "description" };
const chatSources = [{ value: "", label: "ทุกแหล่ง" }, { value: "Defect", label: "Defect" }, { value: "Article", label: "บทความ" }];
const examples = ["ปัญหาพิมพ์ใบกำกับภาษีที่เคยพบมีอะไรบ้าง และแก้อย่างไร", "Defect ระดับ High ที่เกี่ยวกับการบันทึกเอกสารไม่ได้", "สรุปปัญหาที่พบบ่อยในโมดูลเอกสารขาย"];
let keySeed = 0;
const nextKey = () => `m${++keySeed}`;
const readStored = () => { try { return localStorage.getItem(sessionStorageKey) ?? ""; } catch { return ""; } };
const store = (id: string) => { try { if (id) localStorage.setItem(sessionStorageKey, id); else localStorage.removeItem(sessionStorageKey); } catch { /* storage ใช้ไม่ได้ */ } };
const toItem = (m: ServerMessage): ChatItem => ({ key: `s${m.chatMessageId}`, id: m.chatMessageId, role: m.role, content: m.content, citations: m.citations ?? [], model: m.model, feedback: m.feedback, status: "done" });

/** อ่าน SSE จาก fetch (EventSource ส่ง Authorization header ไม่ได้) */
async function streamAsk(sessionId: string, body: object, signal: AbortSignal, onEvent: (event: string, data: Record<string, unknown>) => void) {
  const response = await fetch(`${apiUrl}/knowledge/chat/sessions/${sessionId}/messages`, { method: "POST", headers: authHeaders(true), body: JSON.stringify(body), signal });
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string } | null;
    throw new ApiError(response.status, problem?.detail ?? `ถามผู้ช่วย AI ไม่สำเร็จ (${response.status})`);
  }
  if (!response.body) throw new TypeError("stream not supported");
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    let boundary: number;
    while ((boundary = buffer.indexOf("\n\n")) >= 0) {
      const block = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);
      let event = "message", data = "";
      for (const line of block.split("\n")) {
        if (line.startsWith("event:")) event = line.slice(6).trim();
        else if (line.startsWith("data:")) data += line.slice(5).trim();
      }
      if (data) onEvent(event, JSON.parse(data));
    }
  }
}

/** Markdown แบบย่อที่ปลอดภัย (ไม่ใช้ innerHTML): หัวข้อ #, รายการ -/1., **ตัวหนา**, `code` และเลขอ้างอิง [n] */
function renderInline(text: string, onCite: (n: number) => void, citations: Citation[]): ReactNode[] {
  return text.split(/(\*\*[^*\n]+\*\*|`[^`\n]+`|\[\d{1,2}(?:\s*,\s*\d{1,2})*\])/g).map((part, i) => {
    if (/^\*\*[^*]+\*\*$/.test(part)) return <strong key={i}>{part.slice(2, -2)}</strong>;
    if (/^`[^`]+`$/.test(part)) return <code key={i}>{part.slice(1, -1)}</code>;
    const cite = part.match(/^\[(\d{1,2}(?:\s*,\s*\d{1,2})*)\]$/);
    if (cite) return <Fragment key={i}>{cite[1].split(",").map((x) => Number(x.trim())).map((n) => {
      const c = citations.find((x) => x.number === n);
      return c ? <button key={n} type="button" className="kn-cite" onClick={() => onCite(n)} title={`${sourceLabel[c.sourceType]} · ${c.title}`} aria-label={`แหล่งอ้างอิง ${n}: ${c.title}`}>{n}</button> : <sup key={n}>[{n}]</sup>;
    })}</Fragment>;
    return part;
  });
}

function ChatMarkdown({ text, citations, onCite }: { text: string; citations: Citation[]; onCite: (n: number) => void }) {
  const blocks: ReactNode[] = [];
  let list: { ordered: boolean; items: string[] } | null = null;
  let paragraph: string[] = [];
  const flushParagraph = () => { if (paragraph.length) { blocks.push(<p key={blocks.length}>{renderInline(paragraph.join("\n"), onCite, citations)}</p>); paragraph = []; } };
  const flushList = () => {
    if (!list) return;
    const items = list.items.map((x, i) => <li key={i}>{renderInline(x, onCite, citations)}</li>);
    blocks.push(list.ordered ? <ol key={blocks.length}>{items}</ol> : <ul key={blocks.length}>{items}</ul>);
    list = null;
  };
  for (const raw of text.split("\n")) {
    const line = raw.trimEnd();
    const heading = line.match(/^#{1,4}\s+(.*)$/);
    const bullet = line.match(/^\s*[-*•]\s+(.*)$/);
    const numbered = line.match(/^\s*\d{1,2}[.)]\s+(.*)$/);
    if (!line.trim()) { flushParagraph(); flushList(); continue; }
    if (heading) { flushParagraph(); flushList(); blocks.push(<h4 key={blocks.length}>{renderInline(heading[1], onCite, citations)}</h4>); continue; }
    if (bullet || numbered) {
      flushParagraph();
      const ordered = Boolean(numbered);
      const current = list as { ordered: boolean; items: string[] } | null;
      if (current && current.ordered !== ordered) flushList();
      if (!list) list = { ordered, items: [] };
      (list as { ordered: boolean; items: string[] }).items.push((bullet ?? numbered)![1]);
      continue;
    }
    flushList();
    paragraph.push(line);
  }
  flushParagraph(); flushList();
  return <>{blocks}</>;
}

export function KnowledgeChat({ projects, onOpenCitation }: { projects: ProjectItem[]; onOpenCitation: (citation: Citation) => void }) {
  const [sessions, setSessions] = useState<Session[]>([]);
  const [sessionsLoading, setSessionsLoading] = useState(true);
  const [activeId, setActiveId] = useState("");
  const [items, setItems] = useState<ChatItem[]>([]);
  const [loadingSession, setLoadingSession] = useState(false);
  const [question, setQuestion] = useState("");
  const [source, setSource] = useState("");
  const [projectId, setProjectId] = useState("");
  const [busy, setBusy] = useState(false);
  const [showSessions, setShowSessions] = useState(false);
  const [liveStatus, setLiveStatus] = useState("");
  const abortRef = useRef<AbortController | null>(null);
  const logRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const stickToBottom = useRef(true);

  const loadSessions = useCallback(async () => {
    try { setSessions(await apiFetch<Session[]>("/knowledge/chat/sessions", { fallbackMessage: "โหลดบทสนทนาไม่สำเร็จ" })); }
    catch (error) { notify(error instanceof Error ? error.message : "โหลดบทสนทนาไม่สำเร็จ", "error"); }
    finally { setSessionsLoading(false); }
  }, []);

  const openSession = useCallback(async (id: string) => {
    abortRef.current?.abort();
    setActiveId(id); store(id); setShowSessions(false); setLoadingSession(true);
    try {
      const detail = await getJson<{ session: Session; messages: ServerMessage[] }>(`${apiUrl}/knowledge/chat/sessions/${id}`);
      setItems(detail.messages.map(toItem));
      stickToBottom.current = true;
    } catch (error) {
      setItems([]); setActiveId(""); store("");
      if (!(error instanceof ApiError && error.status === 404)) notify(error instanceof Error ? error.message : "เปิดบทสนทนาไม่สำเร็จ", "error");
    } finally { setLoadingSession(false); }
  }, []);

  useEffect(() => {
    void loadSessions();
    const stored = readStored();
    if (stored) void openSession(stored);
    return () => abortRef.current?.abort();
  }, [loadSessions, openSession]);

  // เลื่อนตามคำตอบเฉพาะเมื่อผู้ใช้อยู่ล่างสุด (ไม่ดึงจอกลับถ้ากำลังเลื่อนอ่านข้างบน)
  useEffect(() => { const log = logRef.current; if (log && stickToBottom.current) log.scrollTop = log.scrollHeight; }, [items]);
  const onLogScroll = () => { const log = logRef.current; if (log) stickToBottom.current = log.scrollHeight - log.scrollTop - log.clientHeight < 80; };

  const newChat = () => { abortRef.current?.abort(); setActiveId(""); store(""); setItems([]); setShowSessions(false); setTimeout(() => inputRef.current?.focus(), 0); };

  const deleteSession = async (s: Session) => {
    if (!await confirmDialog({ title: "ลบบทสนทนา", message: `ลบ "${s.title}"? ลบแล้วกู้คืนไม่ได้`, confirmLabel: "ลบ", tone: "danger" })) return;
    try {
      await apiFetch(`/knowledge/chat/sessions/${s.chatSessionId}`, { method: "DELETE", fallbackMessage: "ลบบทสนทนาไม่สำเร็จ" });
      setSessions((list) => list.filter((x) => x.chatSessionId !== s.chatSessionId));
      if (s.chatSessionId === activeId) newChat();
    } catch (error) { notify(error instanceof Error ? error.message : "ลบบทสนทนาไม่สำเร็จ", "error"); }
  };

  const patch = (key: string, update: (item: ChatItem) => ChatItem) => setItems((list) => list.map((x) => x.key === key ? update(x) : x));

  const ask = async (text: string) => {
    const q = text.trim();
    if (!q || busy) return;
    setBusy(true); setQuestion(""); stickToBottom.current = true; setLiveStatus("ผู้ช่วย AI กำลังค้นหาความรู้และเรียบเรียงคำตอบ");
    const userKey = nextKey(), answerKey = nextKey();
    setItems((list) => [...list, { key: userKey, role: "user", content: q, citations: [], status: "done" }, { key: answerKey, role: "assistant", content: "", citations: [], status: "streaming", question: q }]);
    const controller = new AbortController();
    abortRef.current = controller;
    let sessionId = activeId;
    let received = false;
    const body = { question: q, projectId: projectId || null, sources: source ? [source] : null };
    const onEvent = (event: string, data: Record<string, unknown>) => {
      received = true;
      if (event === "sources") patch(answerKey, (x) => ({ ...x, citations: (data.citations as Citation[]) ?? [], crmExcluded: Boolean(data.crmExcluded) }));
      else if (event === "delta") patch(answerKey, (x) => ({ ...x, content: x.content + String(data.text ?? "") }));
      else if (event === "done") {
        patch(answerKey, (x) => ({ ...x, id: Number(data.assistantMessageId), citations: (data.citations as Citation[]) ?? x.citations, model: (data.model as string) ?? null, status: "done" }));
        patch(userKey, (x) => ({ ...x, id: Number(data.userMessageId) }));
        setSessions((list) => list.map((s) => s.chatSessionId === sessionId ? { ...s, title: String(data.title ?? s.title), updatedAt: new Date().toISOString() } : s).sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)));
      } else if (event === "error") patch(answerKey, (x) => ({ ...x, status: "error", error: String(data.message ?? "ผู้ช่วย AI ตอบไม่สำเร็จ") }));
    };
    try {
      if (!sessionId) {
        const created = await apiFetch<Session>("/knowledge/chat/sessions", { method: "POST", json: { projectId: projectId || null }, fallbackMessage: "เริ่มบทสนทนาไม่สำเร็จ" });
        sessionId = created.chatSessionId;
        setActiveId(sessionId); store(sessionId);
        setSessions((list) => [created, ...list.filter((x) => x.chatSessionId !== created.chatSessionId)]);
      }
      try { await streamAsk(sessionId, body, controller.signal, onEvent); }
      catch (error) {
        // proxy/เครือข่ายตัด stream ก่อนได้ข้อมูล → ขอแบบรอคำตอบครบครั้งเดียว
        if (received || isAbortError(error) || error instanceof ApiError) throw error;
        const result = await apiFetch<{ answer: string; sources?: { citations?: Citation[] }; done?: { assistantMessageId: number; userMessageId: number; citations: Citation[]; title: string; model?: string } }>(
          `/knowledge/chat/sessions/${sessionId}/messages?stream=false`, { method: "POST", json: body, signal: controller.signal, fallbackMessage: "ถามผู้ช่วย AI ไม่สำเร็จ" });
        onEvent("sources", { citations: result.sources?.citations ?? [] });
        onEvent("delta", { text: result.answer });
        if (result.done) onEvent("done", result.done as unknown as Record<string, unknown>);
      }
      setItems((list) => list.map((x) => x.key === answerKey && x.status === "streaming" ? { ...x, status: "done" } : x));
      setLiveStatus("ผู้ช่วย AI ตอบเสร็จแล้ว");
    } catch (error) {
      if (isAbortError(error)) { patch(answerKey, (x) => ({ ...x, status: "stopped" })); setLiveStatus("หยุดการตอบแล้ว"); }
      else { patch(answerKey, (x) => ({ ...x, status: "error", error: error instanceof Error ? error.message : "ผู้ช่วย AI ตอบไม่สำเร็จ" })); setLiveStatus("ผู้ช่วย AI ตอบไม่สำเร็จ"); }
    } finally {
      abortRef.current = null;
      setBusy(false);
      inputRef.current?.focus();
    }
  };

  const retry = (item: ChatItem) => {
    if (!item.question) return;
    setItems((list) => { const index = list.findIndex((x) => x.key === item.key); return index > 0 ? list.slice(0, index - 1) : list; });
    void ask(item.question);
  };

  const feedback = async (item: ChatItem, value: 1 | -1) => {
    if (!item.id) return;
    const next = item.feedback === value ? null : value;
    patch(item.key, (x) => ({ ...x, feedback: next }));
    try { await apiFetch(`/knowledge/chat/messages/${item.id}/feedback`, { method: "POST", json: { feedback: next }, fallbackMessage: "บันทึกความเห็นไม่สำเร็จ" }); }
    catch (error) { patch(item.key, (x) => ({ ...x, feedback: item.feedback })); notify(error instanceof Error ? error.message : "บันทึกความเห็นไม่สำเร็จ", "error"); }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) { event.preventDefault(); void ask(question); }
  };
  const submit = (event: FormEvent) => { event.preventDefault(); void ask(question); };
  const activeSession = sessions.find((s) => s.chatSessionId === activeId);

  return <div className={`kn-chat${showSessions ? " sessions-open" : ""}`}>
    <aside className="kn-chat-sessions" aria-label="บทสนทนา">
      <div className="kn-chat-sessions-head">
        <b>บทสนทนา</b>
        <button type="button" className="btn primary" onClick={newChat}><span className="material-symbols-outlined" aria-hidden="true">add</span>ใหม่</button>
      </div>
      {sessionsLoading ? <p className="kn-chat-muted" role="status">กำลังโหลด...</p>
        : sessions.length === 0 ? <p className="kn-chat-muted">ยังไม่มีบทสนทนา — เริ่มถามได้เลย</p>
        : <ul>{sessions.map((s) => <li key={s.chatSessionId} className={s.chatSessionId === activeId ? "active" : ""}>
          <button type="button" className="kn-chat-session" aria-current={s.chatSessionId === activeId ? "true" : undefined} onClick={() => void openSession(s.chatSessionId)}>
            <span>{s.title}</span><small>{formatThaiDateTime(s.updatedAt)}</small>
          </button>
          <button type="button" className="kn-chat-session-delete" aria-label={`ลบบทสนทนา ${s.title}`} title="ลบ" onClick={() => void deleteSession(s)}><span className="material-symbols-outlined" aria-hidden="true">delete</span></button>
        </li>)}</ul>}
    </aside>
    {showSessions && <button type="button" className="kn-chat-backdrop" aria-label="ปิดรายการบทสนทนา" onClick={() => setShowSessions(false)} />}
    <section className="kn-chat-main" aria-label="แชทกับผู้ช่วย AI">
      <div className="kn-chat-bar">
        <button type="button" className="btn kn-chat-sessions-toggle" onClick={() => setShowSessions(true)} aria-expanded={showSessions}><span className="material-symbols-outlined" aria-hidden="true">forum</span>บทสนทนา ({sessions.length})</button>
        <b className="kn-chat-title">{activeSession?.title ?? "บทสนทนาใหม่"}</b>
        {activeId && <button type="button" className="btn kn-chat-new" onClick={newChat}><span className="material-symbols-outlined" aria-hidden="true">add_comment</span>ใหม่</button>}
      </div>
      <div className="kn-chat-log" ref={logRef} onScroll={onLogScroll} aria-busy={loadingSession}>
        {loadingSession ? <div className="kn-empty" role="status"><span className="spinner" aria-hidden="true" /><span>กำลังเปิดบทสนทนา...</span></div>
          : items.length === 0 ? <div className="kn-chat-welcome">
            <span className="material-symbols-outlined" aria-hidden="true">neurology</span>
            <b>ถามผู้ช่วย AI จากคลังความรู้</b>
            <p>ตอบจาก Defect และบทความใน Project ที่คุณมีสิทธิ์ พร้อมเลขอ้างอิงแหล่งที่มาทุกครั้ง</p>
            <div className="kn-chat-examples">{examples.map((x) => <button key={x} type="button" onClick={() => void ask(x)} disabled={busy}>{x}</button>)}</div>
          </div>
          : items.map((item) => item.role === "user"
            ? <div key={item.key} className="kn-msg kn-msg-user"><div className="kn-bubble">{item.content}</div></div>
            : <AssistantMessage key={item.key} item={item} onCite={(n) => { const c = item.citations.find((x) => x.number === n); if (c) onOpenCitation(c); }} onOpen={onOpenCitation} onFeedback={(v) => void feedback(item, v)} onRetry={() => retry(item)} />)}
      </div>
      <p className="sr-only" role="status" aria-live="polite">{liveStatus}</p>
      <form className="kn-chat-composer" onSubmit={submit}>
        <div className="kn-chat-filters">
          <div className="kn-chips" role="group" aria-label="แหล่งความรู้ที่ใช้ตอบ">{chatSources.map((s) => <button key={s.value} type="button" aria-pressed={source === s.value} className={source === s.value ? "active" : ""} onClick={() => setSource(s.value)}>{s.label}</button>)}</div>
          <select value={projectId} onChange={(e) => setProjectId(e.target.value)} aria-label="Project ที่ใช้ตอบ"><option value="">ทุก Project ที่มีสิทธิ์</option>{projects.map((p) => <option key={p.projectId} value={p.projectId}>{p.projectName}</option>)}</select>
        </div>
        <div className="kn-chat-input">
          <textarea ref={inputRef} value={question} onChange={(e) => setQuestion(e.target.value)} onKeyDown={onKeyDown} rows={1} maxLength={2000} placeholder="ถามเกี่ยวกับปัญหา วิธีแก้ หรือ Defect ที่เคยพบ..." aria-label="คำถามถึงผู้ช่วย AI" />
          {busy
            ? <button type="button" className="btn kn-chat-stop" onClick={() => abortRef.current?.abort()}><span className="material-symbols-outlined" aria-hidden="true">stop_circle</span>หยุด</button>
            : <button type="submit" className="btn primary" disabled={!question.trim()}><span className="material-symbols-outlined" aria-hidden="true">send</span>ส่ง</button>}
        </div>
        <small className="kn-chat-hint">Enter ส่ง · Shift+Enter ขึ้นบรรทัดใหม่ · AI อาจสรุปผิดได้ ตรวจสอบจากแหล่งอ้างอิงก่อนนำไปใช้</small>
      </form>
    </section>
  </div>;
}

function AssistantMessage({ item, onCite, onOpen, onFeedback, onRetry }: { item: ChatItem; onCite: (n: number) => void; onOpen: (c: Citation) => void; onFeedback: (value: 1 | -1) => void; onRetry: () => void }) {
  const cited = item.citations.filter((c) => c.cited);
  const others = item.citations.filter((c) => !c.cited);
  const waiting = item.status === "streaming" && !item.content;
  return <div className={`kn-msg kn-msg-ai${item.status === "error" ? " is-error" : ""}`} aria-busy={item.status === "streaming"}>
    <span className="kn-msg-avatar" aria-hidden="true"><span className="material-symbols-outlined">neurology</span></span>
    <div className="kn-msg-body">
      {waiting ? <div className="kn-typing" role="status"><span /><span /><span />{item.citations.length ? `พบ ${item.citations.length} แหล่งที่เกี่ยวข้อง กำลังเรียบเรียงคำตอบ...` : "กำลังค้นหาความรู้..."}</div>
        : item.content && <div className="kn-answer"><ChatMarkdown text={item.content} citations={item.citations} onCite={onCite} />{item.status === "streaming" && <span className="kn-caret" aria-hidden="true" />}</div>}
      {item.status === "error" && <div className="kn-msg-error" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span>{item.error}<button type="button" className="btn" onClick={onRetry}>ลองใหม่</button></div>}
      {item.status === "stopped" && <small className="kn-chat-muted">หยุดการตอบแล้ว</small>}
      {item.crmExcluded && <small className="kn-chat-note"><span className="material-symbols-outlined" aria-hidden="true">shield</span>ไม่ได้ใช้ข้อมูล CRM เพราะ AI ที่ตั้งค่าไว้อยู่นอกองค์กร</small>}
      {(cited.length > 0 || (item.status === "done" && others.length > 0)) && <div className="kn-sources">
        {cited.length > 0 && <><small>แหล่งอ้างอิง</small><ol>{cited.map((c) => <SourceItem key={c.number} citation={c} onOpen={onOpen} />)}</ol></>}
        {item.status === "done" && others.length > 0 && <details><summary>{cited.length ? `แหล่งที่ค้นพบแต่ไม่ได้อ้างอิง (${others.length})` : `แหล่งที่ค้นพบ (${others.length})`}</summary><ol>{others.map((c) => <SourceItem key={c.number} citation={c} onOpen={onOpen} />)}</ol></details>}
      </div>}
      {item.status === "done" && item.id && <div className="kn-msg-actions">
        <CopyButton text={item.content} className="kn-copy" ariaLabel="คัดลอกคำตอบ">คัดลอก</CopyButton>
        <button type="button" className={item.feedback === 1 ? "active" : ""} aria-pressed={item.feedback === 1} aria-label="คำตอบมีประโยชน์" title="มีประโยชน์" onClick={() => onFeedback(1)}><span className="material-symbols-outlined" aria-hidden="true">thumb_up</span></button>
        <button type="button" className={item.feedback === -1 ? "active" : ""} aria-pressed={item.feedback === -1} aria-label="คำตอบไม่มีประโยชน์" title="ไม่มีประโยชน์" onClick={() => onFeedback(-1)}><span className="material-symbols-outlined" aria-hidden="true">thumb_down</span></button>
        {item.model && <small className="kn-chat-muted">{item.model}</small>}
      </div>}
    </div>
  </div>;
}

function SourceItem({ citation, onOpen }: { citation: Citation; onOpen: (c: Citation) => void }) {
  return <li><button type="button" onClick={() => onOpen(citation)}>
    <span className="kn-source-no">{citation.number}</span>
    <span className="material-symbols-outlined" aria-hidden="true">{sourceIcon[citation.sourceType]}</span>
    <span className="kn-source-title">{citation.title}</span>
    <small>{sourceLabel[citation.sourceType]}{citation.projectName ? ` · ${citation.projectName}` : ""}</small>
  </button></li>;
}
