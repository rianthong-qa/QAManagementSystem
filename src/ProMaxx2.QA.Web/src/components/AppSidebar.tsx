import { useEffect, useRef, useState } from "react";
import { nav, type Page } from "../shared/appShared";
import { ModalShell } from "./ModalShell";
import { apiFetch, apiUrl, authHeaders } from "../api";
import "./AppSidebar.css";

/* Sidebar แบบ Jira (เลือกแบบ C, 2026-11-18) — prefix sb-, UI_DESIGN_SYSTEM §4
   บนลงล่าง: โลโก้ → ปุ่มค้นหาเมนู (Ctrl/⌘ + K) → ปักหมุด → ล่าสุด → เมนูทั้งหมด (หมวดพับได้)
   แต่ละหมวดมีสีประจำ (ไอคอนในกรอบสี + เมนูที่เลือกใช้สีหมวด); โหมดย่อ = ไอคอนอย่างเดียว + tooltip
   ปักหมุด / ล่าสุด / หมวดที่พับ เก็บต่อผู้ใช้ที่ server (ตามไปทุกเครื่อง, 2026-11-19) + แคชใน localStorage (try/catch) */

const groupTones = ["blue", "purple", "green", "orange", "magenta"] as const;
const defaultPins: Page[] = ["execution", "defects", "test-cases"];
const maxRecent = 3;
const prefKey = "sidebar";
const saveDelayMs = 1500;
type SidebarPrefs = { pins: string[]; recent: string[]; closed: string[] };

type NavEntry = { id: Page; icon: string; label: string; group: string; tone: string };

const readList = (key: string): string[] | null => {
  try { const raw = localStorage.getItem(key); const value = raw ? JSON.parse(raw) : null; return Array.isArray(value) ? value.filter((x) => typeof x === "string") : null; } catch { return null; }
};
const writeList = (key: string, value: string[]) => { try { localStorage.setItem(key, JSON.stringify(value)); } catch { /* storage ใช้ไม่ได้ — จำแค่ในหน้านี้ */ } };

export function AppSidebar({ page, userId, collapsed, canView, onGo, onToggleCollapse }: {
  page: Page; userId: string; collapsed: boolean;
  /** ย่อ/กาง Sidebar (เฉพาะ Desktop — ไม่ส่ง = ไม่แสดงปุ่ม เช่น ตอนเป็น drawer บนแท็บเล็ต/มือถือ) */
  onToggleCollapse?: () => void;
  /** หน้าที่ผู้ใช้มีสิทธิ์เห็น — หมวดที่ไม่เหลือเมนูถูกซ่อน */
  canView: (id: Page) => boolean;
  onGo: (id: Page) => void;
}) {
  const groups = nav.map((g, index) => ({
    label: g.label,
    tone: groupTones[index] ?? "blue",
    items: g.items.filter((i) => canView(i.id)).map((i): NavEntry => ({ ...i, group: g.label, tone: groupTones[index] ?? "blue" })),
  })).filter((g) => g.items.length);
  const all = groups.flatMap((g) => g.items);
  const byId = (id: string) => all.find((x) => x.id === id);

  const pinKey = `qa.sidebar.pins.${userId}`, recentKey = `qa.sidebar.recent.${userId}`, closedKey = `qa.sidebar.closed.${userId}`;
  const [pins, setPins] = useState<string[]>(() => readList(pinKey) ?? defaultPins);
  const [recent, setRecent] = useState<string[]>(() => readList(recentKey) ?? []);
  const [closed, setClosed] = useState<string[]>(() => readList(closedKey) ?? []);
  const [searchOpen, setSearchOpen] = useState(false);

  // หน้าล่าสุด: ใส่หน้าที่เปิดไว้หน้าสุด (ไม่ซ้ำ) เก็บ maxRecent + 1 รายการ — แสดงเฉพาะที่ไม่ใช่หน้าปัจจุบัน
  useEffect(() => {
    setRecent((cur) => {
      const next = [page, ...cur.filter((x) => x !== page)].slice(0, maxRecent + 1);
      writeList(recentKey, next);
      return next;
    });
  }, [page, recentKey]);

  // ซิงก์กับบัญชีผู้ใช้ (GET/PUT /auth/me/preferences/sidebar) ให้ตามไปทุกเครื่อง — localStorage เป็นแค่แคชให้แสดงทันทีตอนเปิดหน้า
  // โหลดจาก server ก่อนแล้วจึงเริ่มบันทึก (กันค่าเริ่มต้นของเครื่องนี้ทับค่าจริง); ยังไม่เคยบันทึก (204) = ส่งค่าของเครื่องนี้ขึ้นไปเป็นค่าแรก
  const [synced, setSynced] = useState<string | null>(null);
  const lastSavedRef = useRef("");
  useEffect(() => {
    const controller = new AbortController();
    setSynced(null);
    lastSavedRef.current = "";
    apiFetch<{ value?: SidebarPrefs } | undefined>(`/auth/me/preferences/${prefKey}`, { signal: controller.signal })
      .then((res) => {
        const v = res?.value;
        if (v) {
          const list = (x: unknown) => Array.isArray(x) ? x.filter((i): i is string => typeof i === "string") : null;
          const nextPins = list(v.pins), nextClosed = list(v.closed), serverRecent = list(v.recent);
          if (nextPins) { setPins(nextPins); writeList(pinKey, nextPins); }
          if (nextClosed) { setClosed(nextClosed); writeList(closedKey, nextClosed); }
          // หน้าที่เปิดอยู่ (ตัวแรกของรายการในเครื่อง) ยังต้องอยู่หน้าสุด
          if (serverRecent) setRecent((cur) => {
            const current = cur[0];
            const next = (current ? [current, ...serverRecent.filter((x) => x !== current)] : serverRecent).slice(0, maxRecent + 1);
            writeList(recentKey, next);
            return next;
          });
          lastSavedRef.current = JSON.stringify(v);
        }
        setSynced(userId);
      })
      .catch(() => { if (!controller.signal.aborted) setSynced(userId); /* server ใช้ไม่ได้ — ใช้ค่าในเครื่องต่อ แล้วลองบันทึกครั้งถัดไป */ });
    return () => controller.abort();
  }, [userId, pinKey, recentKey, closedKey]);

  const pendingRef = useRef<SidebarPrefs | null>(null);
  useEffect(() => {
    if (synced !== userId) return;
    const body: SidebarPrefs = { pins, recent, closed };
    const json = JSON.stringify(body);
    if (json === lastSavedRef.current) { pendingRef.current = null; return; }
    pendingRef.current = body;
    const timer = window.setTimeout(() => {
      if (!pendingRef.current) return;
      pendingRef.current = null;
      lastSavedRef.current = json;
      void apiFetch(`/auth/me/preferences/${prefKey}`, { method: "PUT", json: { value: body } })
        .catch(() => { lastSavedRef.current = ""; /* บันทึกไม่สำเร็จ — ค่ายังอยู่ในเครื่อง ลองใหม่เมื่อมีการเปลี่ยนครั้งถัดไป */ });
    }, saveDelayMs);
    return () => window.clearTimeout(timer);
  }, [pins, recent, closed, synced, userId]);

  // ปิดแท็บ/ออกจากระบบก่อนครบเวลาหน่วง — ส่งค่าที่ค้างด้วย keepalive
  useEffect(() => {
    const flush = () => {
      const body = pendingRef.current;
      if (!body) return;
      pendingRef.current = null;
      lastSavedRef.current = JSON.stringify(body);
      try { void fetch(`${apiUrl}/auth/me/preferences/${prefKey}`, { method: "PUT", keepalive: true, headers: authHeaders(true), body: JSON.stringify({ value: body }) }).catch(() => undefined); } catch { /* ignore */ }
    };
    window.addEventListener("pagehide", flush);
    return () => { window.removeEventListener("pagehide", flush); flush(); };
  }, [userId]);

  // Ctrl/⌘ + K เปิดค้นหาเมนูได้จากทุกหน้า
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === "k") { e.preventDefault(); setSearchOpen(true); }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const togglePin = (id: Page) => setPins((cur) => { const next = cur.includes(id) ? cur.filter((x) => x !== id) : [...cur, id]; writeList(pinKey, next); return next; });
  const toggleGroup = (label: string) => setClosed((cur) => { const next = cur.includes(label) ? cur.filter((x) => x !== label) : [...cur, label]; writeList(closedKey, next); return next; });

  const pinned = pins.map(byId).filter((x): x is NavEntry => !!x);
  const recentItems = recent.filter((id) => id !== page).map(byId).filter((x): x is NavEntry => !!x).slice(0, maxRecent);

  const icon = (item: { icon: string; tone: string }) => <span className={`sb-tile tone-${item.tone}`} aria-hidden="true"><span className="material-symbols-outlined">{item.icon}</span></span>;
  const star = (item: NavEntry) => {
    const on = pins.includes(item.id);
    return <button type="button" className={`sb-star${on ? " is-on" : ""}`} aria-pressed={on} aria-label={`${on ? "เลิกปักหมุด" : "ปักหมุด"} ${item.label}`} title={on ? "เลิกปักหมุด" : "ปักหมุด"} onClick={() => togglePin(item.id)}>
      <span className="material-symbols-outlined" aria-hidden="true">{on ? "keep" : "keep_off"}</span>
    </button>;
  };
  const row = (item: NavEntry, key: string, meta?: string) => <li key={key} className="sb-row">
    <button type="button" className={`sb-item tone-${item.tone}${page === item.id ? " is-active" : ""}`} aria-current={page === item.id ? "page" : undefined}
      title={collapsed ? item.label : undefined} data-tooltip={collapsed ? item.label : undefined} onClick={() => onGo(item.id)}>
      {icon(item)}
      <span className="sb-item-text"><span className="sb-item-label">{item.label}</span>{meta && <small>{meta}</small>}</span>
    </button>
    {!collapsed && star(item)}
  </li>;

  return <>
    <div className="sb-head">
      <div className="sb-logo" aria-hidden="true">QA</div>
      <div className="sb-brand"><b>ProMaxx2 QA Hub</b><small>Quality Assurance</small></div>
      {onToggleCollapse && <button type="button" className="sb-collapse" onClick={onToggleCollapse} aria-label={collapsed ? "กางเมนู" : "ย่อเมนู"} aria-expanded={!collapsed}
        title={collapsed ? undefined : "ย่อเมนู"} data-tooltip={collapsed ? "กางเมนู" : undefined}>
        <span className="material-symbols-outlined" aria-hidden="true">{collapsed ? "left_panel_open" : "left_panel_close"}</span>
      </button>}
    </div>
    <div className="sb-search-wrap">
      <button type="button" className="sb-search" onClick={() => setSearchOpen(true)} aria-label="ค้นหาเมนู (Ctrl + K)" title={collapsed ? "ค้นหาเมนู · Ctrl + K" : undefined} data-tooltip={collapsed ? "ค้นหาเมนู · Ctrl + K" : undefined} aria-haspopup="dialog">
        <span className="material-symbols-outlined" aria-hidden="true">search</span>
        <span className="sb-search-text">ค้นหาเมนู…</span>
        <kbd>Ctrl K</kbd>
      </button>
    </div>
    <nav className="sb-scroll" aria-label="เมนูหลัก">
      <section className="sb-section" aria-labelledby="sb-pinned-title">
        <h2 id="sb-pinned-title" className="sb-label"><span className="material-symbols-outlined" aria-hidden="true">keep</span>ปักหมุด</h2>
        {pinned.length
          ? <ul className="sb-list">{pinned.map((x) => row(x, `pin-${x.id}`))}</ul>
          : !collapsed && <p className="sb-empty">ชี้ที่เมนูแล้วกดหมุด เพื่อปักหน้าที่ใช้บ่อยไว้ตรงนี้</p>}
      </section>
      {!collapsed && recentItems.length > 0 && <section className="sb-section" aria-labelledby="sb-recent-title">
        <h2 id="sb-recent-title" className="sb-label"><span className="material-symbols-outlined" aria-hidden="true">history</span>ล่าสุด</h2>
        <ul className="sb-list">{recentItems.map((x) => row(x, `recent-${x.id}`, x.group))}</ul>
      </section>}
      <section className="sb-section" aria-labelledby="sb-all-title">
        <h2 id="sb-all-title" className="sb-label"><span className="material-symbols-outlined" aria-hidden="true">menu</span>เมนูทั้งหมด</h2>
        {groups.map((g) => {
          const open = collapsed || !closed.includes(g.label);
          const listId = `sb-group-${g.tone}`;
          return <div key={g.label} className="sb-group">
            {!collapsed && <button type="button" className="sb-group-btn" aria-expanded={open} aria-controls={listId} onClick={() => toggleGroup(g.label)}>
              <span className="material-symbols-outlined sb-chevron" aria-hidden="true">chevron_right</span>
              <span className={`sb-dot tone-${g.tone}`} aria-hidden="true" />
              <span className="sb-group-name">{g.label}</span>
              <span className="sb-group-count">{g.items.length}</span>
            </button>}
            {open && <ul id={listId} className="sb-list" aria-label={g.label}>{g.items.map((x) => row(x, x.id))}</ul>}
          </div>;
        })}
      </section>
    </nav>
    {searchOpen && <NavSearch items={all} pinned={pinned} onClose={() => setSearchOpen(false)} onGo={(id) => { setSearchOpen(false); onGo(id); }} />}
  </>;
}

/** ค้นหาเมนู (Ctrl/⌘ + K): พิมพ์ชื่อหน้า / หมวด — ↑↓ เลือก, Enter เปิด, Esc ปิด (ModalShell) */
function NavSearch({ items, pinned, onClose, onGo }: { items: NavEntry[]; pinned: NavEntry[]; onClose: () => void; onGo: (id: Page) => void }) {
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const listRef = useRef<HTMLUListElement>(null);
  const q = query.trim().toLowerCase();
  const results = q ? items.filter((x) => `${x.label} ${x.group} ${x.id}`.toLowerCase().includes(q)) : (pinned.length ? pinned : items.slice(0, 6));
  const current = Math.min(active, Math.max(results.length - 1, 0));
  useEffect(() => { listRef.current?.querySelector<HTMLElement>(`[data-index="${current}"]`)?.scrollIntoView({ block: "nearest" }); }, [current]);
  return <ModalShell label="ค้นหาเมนู" className="sb-palette" onDismiss={onClose} confirmDirtyClose={false}>
    <div className="sb-palette-input">
      <span className="material-symbols-outlined" aria-hidden="true">search</span>
      <input type="search" value={query} autoFocus placeholder="พิมพ์ชื่อหน้า เช่น Defect, Test Case, ตั้งค่า" aria-label="ค้นหาเมนู"
        role="combobox" aria-expanded="true" aria-controls="sb-palette-list" aria-activedescendant={results.length ? `sb-palette-opt-${current}` : undefined}
        onChange={(e) => { setQuery(e.target.value); setActive(0); }}
        onKeyDown={(e) => {
          if (e.key === "ArrowDown") { e.preventDefault(); setActive((current + 1) % Math.max(results.length, 1)); }
          else if (e.key === "ArrowUp") { e.preventDefault(); setActive((current - 1 + results.length) % Math.max(results.length, 1)); }
          else if (e.key === "Enter" && results[current]) { e.preventDefault(); onGo(results[current].id); }
        }} />
      <kbd>Esc</kbd>
    </div>
    <p className="sb-palette-caption">{q ? `ผลการค้นหา ${results.length} หน้า` : pinned.length ? "หน้าที่ปักหมุด" : "ไปที่หน้า"}</p>
    {results.length
      ? <ul id="sb-palette-list" ref={listRef} role="listbox" aria-label="หน้า" className="sb-palette-list">
        {results.map((x, i) => <li key={x.id} id={`sb-palette-opt-${i}`} role="option" aria-selected={i === current} data-index={i}
          className={i === current ? "is-active" : undefined} onMouseMove={() => setActive(i)} onClick={() => onGo(x.id)}>
          <span className={`sb-tile tone-${x.tone}`} aria-hidden="true"><span className="material-symbols-outlined">{x.icon}</span></span>
          <span className="sb-palette-name">{x.label}</span>
          <small>{x.group}</small>
        </li>)}
      </ul>
      : <p className="sb-palette-empty">ไม่พบหน้า “{query.trim()}” — ลองพิมพ์ชื่อหน้าหรือชื่อหมวด</p>}
    <div className="sb-palette-foot" aria-hidden="true"><span><kbd>↑</kbd><kbd>↓</kbd> เลือก</span><span><kbd>Enter</kbd> เปิด</span><span><kbd>Esc</kbd> ปิด</span></div>
  </ModalShell>;
}
