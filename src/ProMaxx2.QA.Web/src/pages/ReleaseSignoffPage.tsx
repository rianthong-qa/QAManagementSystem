import { useCallback, useEffect, useMemo, useState } from "react";
import { ModalShell } from "../components/ModalShell";
import { apiUrl } from "../api";
import { formatThaiDateTime } from "../dateTime";
import type { ReleaseItem, BuildItem } from "../shared/types";
import "../ReleaseSignoff.css";

type ReleaseGateData = { openP0: number; p1Blockers: number; requirementCoverage: number; regressionPassRate: number; updateTestPassed: boolean; approvedRisks: number; recommendedDecision: string; smokeStatus?: string };
type SignoffItem = { releaseSignoffId: string; releaseId: string; buildId: string; buildNumber: string; signoffType: string; decision: string; comment?: string | null; signoffBy?: string | null; createdAt: string };

const signoffRoles = ["QA", "DEVELOPMENT", "PRODUCT_OWNER", "RELEASE_OWNER"] as const;
const signoffRoleLabels: Record<string, string> = { QA: "QA", DEVELOPMENT: "Development", PRODUCT_OWNER: "Product", RELEASE_OWNER: "Release Owner" };

export function ReleaseSignoffPage({ projectId, releaseId: contextReleaseId, canSignoff }: { projectId?: string; releaseId?: string; canSignoff: boolean }) {
  const [releases, setReleases] = useState<ReleaseItem[]>([]);
  const [releaseId, setReleaseId] = useState(contextReleaseId ?? "");
  const [builds, setBuilds] = useState<BuildItem[]>([]);
  const [buildId, setBuildId] = useState("");
  const [gate, setGate] = useState<ReleaseGateData | null>(null);
  const [signoffs, setSignoffs] = useState<SignoffItem[]>([]);
 const [modalOpen, setModalOpen] = useState(false);
  const [signoffType, setSignoffType] = useState("QA");
  const [decision, setDecision] = useState("GO");
  const [comment, setComment] = useState("");
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [formError, setFormError] = useState("");
  const [releasesLoaded, setReleasesLoaded] = useState(false);
  const headers = useMemo(() => ({ "Content-Type": "application/json", Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }), []);
  // โหลดไม่สำเร็จต้องแจ้ง error — เดิมคืน null แล้วแสดงเป็น "ยังไม่มีรายการ" / Gate ว่าง
  const getJson = useCallback((url: string) => fetch(url, { headers }).then((r) => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.json(); }), [headers]);
  const loadFailed = useCallback((what: string) => () => setError(`โหลด${what}ไม่สำเร็จ — ลองเลือก Release ใหม่หรือรีเฟรชหน้า`), []);
  useEffect(() => { if (!projectId) return; setReleasesLoaded(false); getJson(`${apiUrl}/releases?projectId=${projectId}`).then((rs) => setReleases(Array.isArray(rs) ? (rs as ReleaseItem[]).filter((x) => x.status !== "Cancelled") : [])).catch(loadFailed("รายการ Release")).finally(() => setReleasesLoaded(true)); }, [projectId, getJson, loadFailed]);
  useEffect(() => { if (contextReleaseId && !releaseId) setReleaseId(contextReleaseId); }, [contextReleaseId, releaseId]);
  useEffect(() => { if (releasesLoaded && releaseId && !releases.some((x) => x.releaseId === releaseId)) { setReleaseId(""); setBuildId(""); } }, [releasesLoaded, releaseId, releases]);
  useEffect(() => { if (!releaseId) { setBuilds([]); setBuildId(""); return; } getJson(`${apiUrl}/releases/${releaseId}/builds`).then((b) => { const list = Array.isArray(b) ? b : []; setBuilds(list); if (list.length && !buildId) setBuildId(list[0].buildId); }).catch(loadFailed("รายการ Build")); }, [releaseId, buildId, getJson, loadFailed]);
  useEffect(() => { if (!releaseId) { setGate(null); setSignoffs([]); setLoading(false); return; } setLoading(true); setError(""); getJson(`${apiUrl}/releases/${releaseId}/release-gate${buildId ? `?buildId=${buildId}` : ""}`).then((g) => setGate((g as ReleaseGateData) ?? null)).catch(loadFailed("Release Gate")).finally(() => setLoading(false)); getJson(`${apiUrl}/releases/${releaseId}/signoffs`).then((s) => setSignoffs(Array.isArray(s) ? s : [])).catch(loadFailed("ประวัติ Sign-off")); }, [releaseId, buildId, getJson, loadFailed]);
  const commentRequired = decision !== "GO";
  const submit = async () => { if (!buildId) { setFormError("กรุณาเลือก Build"); return; } if (commentRequired && !comment.trim()) { setFormError("กรุณาระบุเหตุผล/เงื่อนไขสำหรับการตัดสินใจ " + decision.replaceAll("_", " ")); return; } setSaving(true); setFormError(""); try { const r = await fetch(`${apiUrl}/releases/${releaseId}/signoffs`, { method: "POST", headers, body: JSON.stringify({ buildId, signoffType, decision, comment: comment || null }) }); if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Sign-off ไม่สำเร็จ"); } setModalOpen(false); setComment(""); getJson(`${apiUrl}/releases/${releaseId}/signoffs`).then((s) => setSignoffs(Array.isArray(s) ? s : [])).catch(loadFailed("ประวัติ Sign-off")); } catch (e) { setFormError(e instanceof Error ? e.message : "สร้าง Sign-off ไม่สำเร็จ"); } finally { setSaving(false); } };
  useEffect(() => { const activeBuilds = builds.filter((b) => b.isActive && b.status.toLowerCase() !== "cancelled"); if (activeBuilds.length !== builds.length) { setBuilds(activeBuilds); if (buildId && !activeBuilds.some((b) => b.buildId === buildId)) setBuildId(activeBuilds[0]?.buildId ?? ""); } }, [builds, buildId]);
  const decisionClass = (d: string) => d === "GO" ? "go" : d === "CONDITIONAL_GO" ? "conditional" : "nogo";
  // เดิมทุกค่าที่ไม่ใช่ Succeeded/NOT_RUN (รวม null ตอนยังไม่มีข้อมูล และ Running) แสดงเป็น "Fail"
  const smokeLabel = (status?: string | null) => !status || status === "NOT_RUN" ? "Not Run" : status === "Succeeded" || status === "Passed" ? "Pass" : status === "Failed" ? "Fail" : status;
  const gateLabels: { key: keyof ReleaseGateData; label: string; hint: (d: ReleaseGateData) => string }[] = [
    { key: "requirementCoverage", label: "Requirement Coverage", hint: (d) => `${d.requirementCoverage}% Covered` },
    { key: "regressionPassRate", label: "Regression / Pass Rate", hint: (d) => `${d.regressionPassRate}% Pass` },
    { key: "approvedRisks", label: "Approved Risks", hint: (d) => `${d.approvedRisks} รายการ` },
  ];
  return (
    <article className="signoff-page">
      <div className="signoff-toolbar">
        <div className="signoff-selects">
          <b className="signoff-toolbar-label">Release</b><select aria-label="Release" value={releaseId} onChange={(e) => { setReleaseId(e.target.value); setBuildId(""); }}><option value="">เลือก Release</option>{releases.map((r) => <option key={r.releaseId} value={r.releaseId}>{r.releaseCode} · Version {r.version}</option>)}</select>
          <b className="signoff-toolbar-label">Build</b><select aria-label="Build" value={buildId} onChange={(e) => setBuildId(e.target.value)}><option value="">เลือก Build</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber} · {b.applicationVersion || "-"}</option>)}</select>
        </div>
        {canSignoff && <button className="btn primary" disabled={!releaseId || !buildId} onClick={() => { setDecision("GO"); setComment(""); setFormError(""); setModalOpen(true); }}><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Sign-off</button>}
      </div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      {loading && !gate ? <div className="empty"><div className="spinner" /><p>กำลังโหลด Release Gate...</p></div> : !releaseId ? <div className="empty"><p>เลือก Release เพื่อดู Release Gate</p></div> : (
        <>
          <section className="card">
            <div className="test-summary-head">
              <div><h3 style={{ margin: 0 }}>Release Gate Panel</h3><small style={{ color: "var(--muted)" }}>ตรวจสอบเกณฑ์ก่อน Sign-off ตามขั้นตอน Release Governance</small></div>
              {gate && <span className={`signoff-decision ${decisionClass(gate.recommendedDecision)}`}>{gate.recommendedDecision.replaceAll("_", " ")}</span>}
            </div>
            <div className="gate-grid"><div className="gate-cell"><small>Smoke</small><b>{smokeLabel(gate?.smokeStatus)}</b><span>{gate?.smokeStatus ?? "NOT_RUN"}</span></div>
              {gateLabels.map((g) => <div className="gate-cell" key={g.key}><small>{g.label}</small><b>{typeof gate?.[g.key] === "boolean" ? (gate?.[g.key] ? "Pass" : "Fail") : String(gate?.[g.key] ?? "–")}</b><span>{gate ? g.hint(gate) : "…"}</span></div>)}
            </div>
            {gate && <div className="ts-progress" style={{ marginTop: 12 }}><div className="ts-progress-row"><span>P0 ยังไม่ผ่าน/ถูกบล็อก</span><b>{gate.openP0}</b></div><div className="ts-progress-row"><span>P1 Blocker</span><b>{gate.p1Blockers}</b></div><div className="ts-progress-row"><span>Update Test Passed</span><b>{gate.updateTestPassed ? "Passed" : "Not passed"}</b></div></div>}
          </section>
          <section className="card signoff-final-card"><div className="test-summary-head"><div><h3 style={{ margin: 0 }}>Final Decision</h3><small style={{ color: "var(--muted)" }}>สถานะรวมของ Release จาก Gate และผู้อนุมัติ</small></div>{gate && <span className={`signoff-decision ${decisionClass(gate.recommendedDecision)}`}>{gate.recommendedDecision.replaceAll("_", " ")}</span>}</div></section>
          <section className="signoff-cards">{signoffRoles.map((role) => { const item = signoffs.find((x) => x.signoffType === role && x.buildId === buildId); return <article className="card signoff-role-card" key={role}><div className="signoff-role-head"><div className="signoff-role-avatar">{role === "QA" ? "Q" : role === "DEVELOPMENT" ? "D" : role === "PRODUCT_OWNER" ? "P" : "R"}</div><div><h3>{signoffRoleLabels[role]}</h3><small>{item ? item.signoffBy || "บันทึกแล้ว" : "Pending sign-off"}</small></div></div>{item ? <><span className={`signoff-decision ${decisionClass(item.decision)}`}>{item.decision.replaceAll("_", " ")}</span><p>{item.comment || "ไม่มี comment"}</p><small>{formatThaiDateTime(item.createdAt)}</small></> : <span className="signoff-pending">ยังไม่มีการอนุมัติ</span>}</article>; })}</section>
          <section className="card">
            <div className="test-summary-head"><div><h3 style={{ margin: 0 }}>Sign-off History</h3></div><span className="count-pill">{signoffs.length} รายการ</span></div>
            {signoffs.length ? <div className="table-wrap"><table className="table-cards"><thead><tr><th>Build</th><th>Type</th><th>Decision</th><th>Sign-off By</th><th>Comment</th><th>Date</th></tr></thead><tbody>{signoffs.map((x) => <tr key={x.releaseSignoffId}><td>{x.buildNumber}</td><td>{x.signoffType}</td><td><span className={`signoff-decision ${decisionClass(x.decision)}`}>{x.decision.replaceAll("_", " ")}</span></td><td>{x.signoffBy || "-"}</td><td>{x.comment || "-"}</td><td>{formatThaiDateTime(x.createdAt)}</td></tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มีรายการ Sign-off</p></div>}
          </section>
        </>
      )}
      {modalOpen && <ModalShell labelledBy="signoff-form-title" className="risk-modal" onDismiss={() => { if (!saving) setModalOpen(false); }}><div className="modal-head"><div><h2 id="signoff-form-title">สร้าง Release Sign-off</h2><small>{builds.find((b) => b.buildId === buildId)?.buildNumber ?? ""}</small></div><button aria-label="ปิดแบบฟอร์ม" disabled={saving} onClick={() => setModalOpen(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div><div className="form-grid"><label className="full">Sign-off Role<select value={signoffType} onChange={(e) => setSignoffType(e.target.value)}><option value="QA">QA</option><option value="DEVELOPMENT">Development</option><option value="PRODUCT_OWNER">Product</option><option value="RELEASE_OWNER">Release Owner</option></select></label><label className="full">Decision<select value={decision} onChange={(e) => setDecision(e.target.value)}>{["GO", "CONDITIONAL_GO", "NO_GO"].map((x) => <option key={x} value={x}>{x.replaceAll("_", " ")}</option>)}</select></label><label className="full">Comment{commentRequired && <span className="required-mark"> *</span>}<textarea rows={3} value={comment} maxLength={2000} onChange={(e) => setComment(e.target.value)} placeholder={commentRequired ? "จำเป็นสำหรับ NO GO / CONDITIONAL GO — ระบุเหตุผลหรือเงื่อนไข" : "เหตุผล/เงื่อนไขประกอบการตัดสินใจ"} /></label></div>{formError && <div className="inline-alert error" role="alert"><span>{formError}</span></div>}<div className="modal-actions"><button className="btn" disabled={saving} onClick={() => setModalOpen(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn primary" disabled={saving} onClick={submit}>{saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> ยืนยัน Sign-off</>}</button></div></ModalShell>}
    </article>
  );
}
