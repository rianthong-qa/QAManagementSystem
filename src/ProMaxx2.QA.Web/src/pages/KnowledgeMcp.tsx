import { useCallback, useEffect, useState } from "react";
import { apiFetch, apiUrl } from "../api";
import { Badge } from "../components/Badge";
import { CopyButton } from "../components/CopyButton";
import { ModalShell } from "../components/ModalShell";
import { confirmDialog, notify } from "../components/dialogStore";
import { formatThaiDateTime } from "../dateTime";
import "./KnowledgeMcp.css";

// เชื่อมต่อ Claude ผ่าน MCP (AI_KNOWLEDGE_CHAT_PLAN.md Phase 2B): Personal Access Token + วิธีตั้งค่า Claude Code / Claude Desktop

type McpInfo = { enabled: boolean; canCreateToken: boolean; defaultLifetimeDays: number; maxLifetimeDays: number; maxActiveTokens: number };
type AccessToken = { personalAccessTokenId: string; userId: string; userDisplayName?: string | null; name: string; tokenPrefix: string; scope: string; createdAt: string; expiresAt: string; lastUsedAt?: string | null; lastUsedIp?: string | null; revokedAt?: string | null; isActive: boolean; kind: "Personal" | "OAuth" };

/** URL ของ MCP server — API อยู่คนละโดเมนกับหน้าเว็บ จึงคำนวณจาก VITE_API_URL (…/api/v1 → …/mcp) */
const mcpUrl = (() => {
  try { return new URL(apiUrl.replace(/\/api\/v1\/?$/, "/mcp"), window.location.origin).toString(); } catch { return `${window.location.origin}/mcp`; }
})();
const claudeCodeCommand = (token: string) => `claude mcp add --transport http --scope user qa-hub ${mcpUrl} --header "Authorization: Bearer ${token}"`;
const mcpJson = `{
  "mcpServers": {
    "qa-hub": {
      "type": "http",
      "url": "${mcpUrl}",
      "headers": { "Authorization": "Bearer \${QA_MCP_TOKEN}" }
    }
  }
}`;
const examplePrompts = ["ใช้ qa-hub ค้นว่าเคยมี Defect เรื่องพิมพ์ใบกำกับภาษีไหม และแก้อย่างไร", "ก่อนแจ้ง bug นี้ ช่วยหา Defect ที่คล้ายกันใน qa-hub: กดบันทึกใบเสนอราคาแล้วระบบค้าง", "ดูรายละเอียด PMX2-DEF-1258 จาก qa-hub แล้วเขียน Test Case สำหรับ regression"];
const tokenState = (t: AccessToken) => t.revokedAt ? { label: "ยกเลิกแล้ว", tone: "gray" } : t.isActive ? { label: t.kind === "OAuth" ? "เชื่อมต่ออยู่" : "ใช้งานได้", tone: "green" } : { label: "หมดอายุ", tone: "yellow" };

export function KnowledgeMcp({ isSysAdmin }: { isSysAdmin: boolean }) {
  const [info, setInfo] = useState<McpInfo | null>(null);
  const [tokens, setTokens] = useState<AccessToken[]>([]);
  const [allTokens, setAllTokens] = useState<AccessToken[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [creating, setCreating] = useState(false);
  const [created, setCreated] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true); setError("");
    try {
      const nextInfo = await apiFetch<McpInfo>("/knowledge/mcp/info", { fallbackMessage: "โหลดข้อมูลการเชื่อมต่อไม่สำเร็จ" });
      setInfo(nextInfo);
      const [mine, all] = await Promise.all([
        nextInfo.canCreateToken ? apiFetch<AccessToken[]>("/knowledge/mcp/tokens") : Promise.resolve([] as AccessToken[]),
        isSysAdmin ? apiFetch<AccessToken[]>("/knowledge/mcp/tokens/all") : Promise.resolve(null),
      ]);
      setTokens(mine); setAllTokens(all);
    } catch (e) { setError(e instanceof Error ? e.message : "โหลดข้อมูลการเชื่อมต่อไม่สำเร็จ"); }
    finally { setLoading(false); }
  }, [isSysAdmin]);
  useEffect(() => { void load(); }, [load]);

  const revoke = async (t: AccessToken, asAdmin: boolean) => {
    if (!await confirmDialog({ title: "ยกเลิก Token", message: `ยกเลิก "${t.name}"${asAdmin && t.userDisplayName ? ` ของ ${t.userDisplayName}` : ""}? Claude ที่ใช้ Token นี้จะเชื่อมต่อไม่ได้ทันที`, confirmLabel: "ยกเลิก Token", tone: "danger" })) return;
    try { await apiFetch(`/knowledge/mcp/tokens/${asAdmin ? "all/" : ""}${t.personalAccessTokenId}`, { method: "DELETE", fallbackMessage: "ยกเลิก Token ไม่สำเร็จ" }); notify("ยกเลิก Token แล้ว", "success"); void load(); }
    catch (e) { notify(e instanceof Error ? e.message : "ยกเลิก Token ไม่สำเร็จ", "error"); }
  };

  if (loading && !info) return <div className="kn-empty" role="status"><span className="spinner" aria-hidden="true" /><span>กำลังโหลด...</span></div>;
  if (error || !info) return <div className="kn-empty kn-error" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><b>{error || "โหลดข้อมูลไม่สำเร็จ"}</b><button type="button" className="btn" onClick={() => void load()}>ลองใหม่</button></div>;
  const activeCount = tokens.filter((t) => t.isActive && t.kind === "Personal").length;

  return <div className="kn-mcp">
    <div className="kn-mcp-head">
      <span className="kn-mcp-mark" aria-hidden="true"><span className="material-symbols-outlined">hub</span></span>
      <div>
        <h3>เชื่อมต่อ Claude (MCP)</h3>
        <p>ให้ Claude Code หรือ Claude Desktop ค้นคลังความรู้ QA ได้โดยตรง — ใช้สิทธิ์ของคุณ (เห็นเฉพาะ Project ที่เป็นสมาชิก) อ่านอย่างเดียว ไม่รวมข้อมูล CRM</p>
      </div>
      <Badge tone={info.enabled ? "green" : "yellow"}>{info.enabled ? "เปิดใช้งาน" : "ปิดอยู่"}</Badge>
    </div>
    {!info.enabled && <div className="kn-notice" role="note"><span className="material-symbols-outlined" aria-hidden="true">pause_circle</span>ผู้ดูแลระบบยังไม่เปิดการเชื่อมต่อ{isSysAdmin ? " — เปิดได้ที่ Setting Center → AI Configuration → คลังความรู้" : ""}</div>}
    {!info.canCreateToken && <div className="kn-notice" role="note"><span className="material-symbols-outlined" aria-hidden="true">lock</span>บัญชีของคุณยังไม่มีสิทธิ์ KNOWLEDGE.MCP — ติดต่อผู้ดูแลระบบ</div>}

    {info.canCreateToken && <section className="kn-mcp-section" aria-labelledby="kn-mcp-step1">
      <div className="kn-mcp-section-head">
        <h4 id="kn-mcp-step1"><span className="kn-step">1</span>การเชื่อมต่อของฉัน <small>(Token {activeCount}/{info.maxActiveTokens} · การเชื่อมต่อแบบ Login จาก claude.ai แสดงที่นี่ด้วย)</small></h4>
        <button type="button" className="btn primary" disabled={!info.enabled || activeCount >= info.maxActiveTokens} onClick={() => setCreating(true)}><span className="material-symbols-outlined" aria-hidden="true">key</span>สร้าง Token</button>
      </div>
      {tokens.length === 0 ? <p className="kn-chat-muted">ยังไม่มีการเชื่อมต่อ — claude.ai ใช้การ Login (ไม่ต้องสร้าง Token); Claude Code ใช้ได้ทั้ง Login และ Token (สร้าง 1 ใบต่ออุปกรณ์)</p>
        : <TokenTable rows={tokens} onRevoke={(t) => void revoke(t, false)} />}
    </section>}

    <section className="kn-mcp-section" aria-labelledby="kn-mcp-step2">
      <div className="kn-mcp-section-head"><h4 id="kn-mcp-step2"><span className="kn-step">2</span>ตั้งค่า Claude</h4></div>
      <div className="kn-mcp-setup">
        <div className="kn-mcp-card">
          <b><span className="material-symbols-outlined" aria-hidden="true">terminal</span>Claude Code</b>
          <p><b>แบบ Login (แนะนำ):</b> <code>claude mcp add --transport http --scope user qa-hub {mcpUrl}</code> แล้วพิมพ์ <code>/mcp</code> ใน Claude Code → Authenticate</p>
          <CopyButton text={`claude mcp add --transport http --scope user qa-hub ${mcpUrl}`} className="btn">คัดลอกคำสั่ง (Login)</CopyButton>
          <p><b>แบบ Token</b> (เช่น เครื่อง CI หรือไม่สะดวก Login ผ่านเบราว์เซอร์) — แทน <code>&lt;TOKEN&gt;</code> ด้วย Token ของคุณ (ตอนสร้าง Token ระบบเติมให้)</p>
          <pre className="kn-code">{claudeCodeCommand("<TOKEN>")}</pre>
          <CopyButton text={claudeCodeCommand("<TOKEN>")} className="btn">คัดลอกคำสั่ง (Token)</CopyButton>
          <details>
            <summary>หรือใช้ไฟล์ .mcp.json ในโปรเจกต์ (ไม่เก็บ Token ในไฟล์)</summary>
            <pre className="kn-code">{mcpJson}</pre>
            <p>ตั้งค่า environment variable <code>QA_MCP_TOKEN</code> เป็น Token ของคุณ — ห้าม commit Token ลง repository</p>
            <CopyButton text={mcpJson} className="btn">คัดลอก .mcp.json</CopyButton>
          </details>
        </div>
        <div className="kn-mcp-card">
          <b><span className="material-symbols-outlined" aria-hidden="true">groups</span>claude.ai / Claude Desktop — แผน Team (Login รายคน)</b>
          <p><b>Owner ทำครั้งเดียว:</b></p>
          <ol>
            <li>Organization settings → Connectors → <b>Add → Custom → Web</b></li>
            <li>URL: <code>{mcpUrl}</code> <CopyButton text={mcpUrl} className="kn-inline-copy">คัดลอก</CopyButton></li>
            <li>วิธียืนยันตัวตนเลือก <b>OAuth</b> → <b>Register automatically</b> (ไม่ต้องใส่ Client ID / Secret)</li>
          </ol>
          <p><b>สมาชิกแต่ละคน:</b> Customize → Connectors → ProMaxx2 QA Hub → <b>Connect</b> → Login QA Hub → กด <b>อนุญาต</b> — Claude จะเห็นข้อมูลตามสิทธิ์ของคุณเท่านั้น</p>
          <p className="kn-mcp-warn"><span className="material-symbols-outlined" aria-hidden="true">info</span>แผน Pro/Max เพิ่ม connector เองได้ที่ Customize → Connectors → Add custom connector (เลือก OAuth เหมือนกัน)</p>
        </div>
      </div>
    </section>

    <section className="kn-mcp-section" aria-labelledby="kn-mcp-step3">
      <div className="kn-mcp-section-head"><h4 id="kn-mcp-step3"><span className="kn-step">3</span>ลองถาม Claude</h4></div>
      <ul className="kn-mcp-examples">{examplePrompts.map((x) => <li key={x}><span>{x}</span><CopyButton text={x} className="kn-inline-copy">คัดลอก</CopyButton></li>)}</ul>
      <p className="kn-chat-muted">เครื่องมือที่ Claude ใช้ได้: search_knowledge, get_defect, get_article, get_document, find_similar_defects, list_projects — ทุกการเรียกบันทึกใน Audit Log</p>
    </section>

    {isSysAdmin && allTokens && <section className="kn-mcp-section" aria-labelledby="kn-mcp-all">
      <div className="kn-mcp-section-head"><h4 id="kn-mcp-all">Token ทั้งหมดในระบบ <small>(SYS_ADMIN)</small></h4></div>
      {allTokens.length === 0 ? <p className="kn-chat-muted">ยังไม่มีใครสร้าง Token</p> : <TokenTable rows={allTokens} showUser onRevoke={(t) => void revoke(t, true)} />}
    </section>}

    {creating && <CreateTokenModal info={info} created={created} onCreated={(token) => { setCreated(token); void load(); }} onClose={() => { setCreating(false); setCreated(null); }} />}
  </div>;
}

function TokenTable({ rows, showUser, onRevoke }: { rows: AccessToken[]; showUser?: boolean; onRevoke: (t: AccessToken) => void }) {
  return <div className="table-wrap"><table className="table-cards kn-table kn-token-table">
    <thead><tr><th>ชื่อ</th>{showUser && <th>ผู้ใช้</th>}<th>สถานะ</th><th>เชื่อมต่อถึง</th><th>ใช้ล่าสุด</th><th><span className="sr-only">การจัดการ</span></th></tr></thead>
    <tbody>{rows.map((t) => { const state = tokenState(t); return <tr key={t.personalAccessTokenId}>
      <td><b>{t.name}</b><small className="kn-excerpt">{t.kind === "OAuth" ? "Login ผ่าน OAuth" : <code>{t.tokenPrefix}…</code>} · สร้าง {formatThaiDateTime(t.createdAt)}</small></td>
      {showUser && <td>{t.userDisplayName ?? "-"}</td>}
      <td><Badge tone={state.tone}>{state.label}</Badge></td>
      <td>{formatThaiDateTime(t.expiresAt)}</td>
      <td>{t.lastUsedAt ? <>{formatThaiDateTime(t.lastUsedAt)}{t.lastUsedIp && <small className="kn-excerpt">{t.lastUsedIp}</small>}</> : "ยังไม่เคยใช้"}</td>
      <td className="kn-row-actions">{!t.revokedAt && <button type="button" className="btn kn-danger-text" onClick={() => onRevoke(t)} aria-label={`ยกเลิก Token ${t.name}`}>ยกเลิก</button>}</td>
    </tr>; })}</tbody>
  </table></div>;
}

function CreateTokenModal({ info, created, onCreated, onClose }: { info: McpInfo; created: string | null; onCreated: (token: string) => void; onClose: () => void }) {
  const [name, setName] = useState("Claude Code");
  const [days, setDays] = useState(info.defaultLifetimeDays);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const create = async () => {
    setSaving(true); setError("");
    try { onCreated((await apiFetch<{ token: string }>("/knowledge/mcp/tokens", { method: "POST", json: { name: name.trim(), lifetimeDays: days }, fallbackMessage: "สร้าง Token ไม่สำเร็จ" })).token); }
    catch (e) { setError(e instanceof Error ? e.message : "สร้าง Token ไม่สำเร็จ"); }
    finally { setSaving(false); }
  };
  const lifetimes = [30, 90, 180, 365].filter((x) => x <= info.maxLifetimeDays);
  return <ModalShell labelledBy="kn-token-title" className="kn-token-modal" onDismiss={onClose} backdropDismiss={false} dirty={Boolean(created)}>
    <div className="modal-head">
      <div><span className="kn-eyebrow">เชื่อมต่อ Claude (MCP)</span><h2 id="kn-token-title">{created ? "Token ของคุณ" : "สร้าง Token"}</h2></div>
      <button type="button" className="modal-close" aria-label="ปิด" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
    </div>
    {created ? <div className="kn-token-result">
      <p className="kn-mcp-warn is-strong" role="status"><span className="material-symbols-outlined" aria-hidden="true">warning</span>คัดลอกเก็บไว้ตอนนี้ — ระบบแสดง Token นี้ครั้งเดียว ปิดหน้าต่างแล้วดูอีกไม่ได้ (ถ้าหายให้ยกเลิกแล้วสร้างใหม่)</p>
      <label className="kn-token-field">Token<input readOnly value={created} onFocus={(e) => e.currentTarget.select()} aria-label="Token" /></label>
      <CopyButton text={created} className="btn primary">คัดลอก Token</CopyButton>
      <b className="kn-token-sub">คำสั่งสำหรับ Claude Code (เติม Token แล้ว)</b>
      <pre className="kn-code">{claudeCodeCommand(created)}</pre>
      <CopyButton text={claudeCodeCommand(created)} className="btn">คัดลอกคำสั่ง</CopyButton>
    </div> : <div className="form-grid kn-editor-grid kn-token-form">
      <label className="kn-span-2">ชื่อ Token <span className="required">*</span><input autoFocus value={name} maxLength={100} onChange={(e) => setName(e.target.value)} placeholder="เช่น Claude Code — โน้ตบุ๊กที่ทำงาน" /><small>ตั้งชื่อตามอุปกรณ์/โปรแกรม เพื่อรู้ว่าจะยกเลิกใบไหน</small></label>
      <label>อายุ Token<select value={days} onChange={(e) => setDays(Number(e.target.value))}>{lifetimes.map((x) => <option key={x} value={x}>{x} วัน</option>)}</select></label>
      {error && <p className="kn-form-error kn-span-2" role="alert">{error}</p>}
    </div>}
    <div className="modal-actions">
      {created ? <button type="button" className="btn primary" onClick={onClose}>เสร็จสิ้น</button> : <>
        <button type="button" className="btn" onClick={onClose}>ยกเลิก</button>
        <button type="button" className="btn primary" disabled={saving || !name.trim()} onClick={() => void create()}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังสร้าง...</> : "สร้าง Token"}</button>
      </>}
    </div>
  </ModalShell>;
}
