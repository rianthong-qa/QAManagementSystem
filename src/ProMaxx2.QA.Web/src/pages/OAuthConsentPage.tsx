import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import type { SessionUser } from "../shared/appShared";
import "./OAuthConsent.css";

// หน้ายืนยันการเชื่อมต่อ OAuth (?oauth=<requestId>) — claude.ai / Claude Desktop / Claude Code ส่งผู้ใช้มาที่นี่จาก GET /oauth/authorize
// ผู้ใช้ต้อง Login QA Hub ก่อน (App แสดงหน้า Login ให้ถ้ายังไม่ได้ Login) แล้วกดอนุญาต/ไม่อนุญาต → กลับไปที่ Claude พร้อม code

type Consent = { requestId: string; clientName: string; redirectHost: string; isLoopback: boolean; scope: string; expiresAt: string; mcpEnabled: boolean; canApprove: boolean };

export default function OAuthConsentPage({ requestId, user }: { requestId: string; user: SessionUser }) {
  const [consent, setConsent] = useState<Consent | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState<"" | "approve" | "deny">("");
  const [done, setDone] = useState("");

  useEffect(() => {
    let alive = true;
    apiFetch<Consent>(`/oauth/requests/${encodeURIComponent(requestId)}`, { fallbackMessage: "โหลดคำขอเชื่อมต่อไม่สำเร็จ" })
      .then((x) => { if (alive) setConsent(x); })
      .catch((e) => { if (alive) setError(e instanceof Error ? e.message : "โหลดคำขอเชื่อมต่อไม่สำเร็จ"); });
    return () => { alive = false; };
  }, [requestId]);

  const decide = async (approve: boolean) => {
    setBusy(approve ? "approve" : "deny"); setError("");
    try {
      const result = await apiFetch<{ redirectUrl: string }>(`/oauth/requests/${encodeURIComponent(requestId)}/${approve ? "approve" : "deny"}`, { method: "POST", fallbackMessage: "บันทึกการตัดสินใจไม่สำเร็จ" });
      setDone(approve ? "อนุญาตแล้ว — กำลังกลับไปที่ Claude..." : "ไม่อนุญาตแล้ว — กำลังกลับไปที่ Claude...");
      window.location.assign(result.redirectUrl);
    } catch (e) { setError(e instanceof Error ? e.message : "บันทึกการตัดสินใจไม่สำเร็จ"); setBusy(""); }
  };

  const switchAccount = () => {
    try { localStorage.removeItem("qa.accessToken"); localStorage.removeItem("qa.user"); } catch { /* storage ใช้ไม่ได้ */ }
    window.location.reload();
  };

  const destination = consent ? (consent.isLoopback ? "โปรแกรมบนเครื่องนี้ (เช่น Claude Code)" : consent.redirectHost) : "";
  const blocked = consent && (!consent.mcpEnabled || !consent.canApprove);

  return <div className="oauth-consent-page">
    <main className="oauth-consent-card" aria-labelledby="oauth-consent-title">
      <div className="oauth-consent-brand" aria-hidden="true">
        <span className="oauth-logo">QA</span>
        <span className="material-symbols-outlined">sync_alt</span>
        <span className="oauth-logo oauth-logo-client"><span className="material-symbols-outlined">neurology</span></span>
      </div>
      {!consent && !error && <p className="oauth-muted" role="status"><span className="spinner inline" aria-hidden="true" /> กำลังโหลดคำขอเชื่อมต่อ...</p>}
      {!consent && error && <>
        <h1 id="oauth-consent-title">เชื่อมต่อไม่สำเร็จ</h1>
        <p className="oauth-error" role="alert">{error}</p>
        <p className="oauth-muted">กลับไปที่ Claude แล้วกด Connect อีกครั้ง</p>
      </>}
      {consent && <>
        <h1 id="oauth-consent-title">อนุญาตให้ <b>{consent.clientName}</b> เชื่อมต่อ ProMaxx2 QA Hub?</h1>
        <p className="oauth-account">เข้าสู่ระบบเป็น <b>{user.displayName}</b> ({user.username}) · <button type="button" className="oauth-link" onClick={switchAccount}>ไม่ใช่คุณ? เปลี่ยนบัญชี</button></p>
        <ul className="oauth-scopes">
          <li><span className="material-symbols-outlined ok" aria-hidden="true">check_circle</span>ค้นหาและอ่าน Defect และบทความความรู้ ใน Project ที่คุณเป็นสมาชิก</li>
          <li><span className="material-symbols-outlined ok" aria-hidden="true">check_circle</span>อ่านอย่างเดียว — แก้ไขหรือลบข้อมูลใน QA Hub ไม่ได้</li>
          <li><span className="material-symbols-outlined no" aria-hidden="true">block</span>ไม่รวมข้อมูล CRM และข้อมูลที่ตั้งว่า "ไม่ส่งให้ AI"; อีเมล/เบอร์โทร/รหัสผ่านถูกปิดก่อนส่ง</li>
          <li><span className="material-symbols-outlined" aria-hidden="true">schedule</span>เชื่อมต่ออยู่ 30 วันนับจากใช้งานล่าสุด — ยกเลิกได้ทุกเมื่อที่ ผู้ช่วย AI / คลังความรู้ → เชื่อมต่อ Claude</li>
        </ul>
        <p className="oauth-muted">หลังจากนี้จะกลับไปที่ <b>{destination}</b> · การเชื่อมต่อทุกครั้งบันทึกใน Audit Log</p>
        {blocked && <p className="oauth-warning" role="alert"><span className="material-symbols-outlined" aria-hidden="true">lock</span>{!consent.mcpEnabled ? "ผู้ดูแลระบบปิดการเชื่อมต่อจาก Claude อยู่" : "บัญชีของคุณยังไม่มีสิทธิ์เชื่อมต่อ Claude (KNOWLEDGE.MCP) — ติดต่อผู้ดูแลระบบ"}</p>}
        {error && <p className="oauth-error" role="alert">{error}</p>}
        {done ? <p className="oauth-done" role="status">{done}</p> : <div className="oauth-actions">
          <button type="button" className="btn" disabled={Boolean(busy)} onClick={() => void decide(false)}>{busy === "deny" ? <><span className="spinner inline" aria-hidden="true" /> กำลังส่ง...</> : "ไม่อนุญาต"}</button>
          <button type="button" className="btn primary" disabled={Boolean(busy) || Boolean(blocked)} onClick={() => void decide(true)}>{busy === "approve" ? <><span className="spinner inline" aria-hidden="true" /> กำลังเชื่อมต่อ...</> : "อนุญาต"}</button>
        </div>}
      </>}
    </main>
    <footer>ProMaxx2 QA Hub · ตรวจสอบชื่อโปรแกรมและปลายทางก่อนกดอนุญาต — QA Hub ไม่เคยขอรหัสผ่านผ่านหน้านี้</footer>
  </div>;
}
