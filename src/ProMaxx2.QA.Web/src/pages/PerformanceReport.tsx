import { useEffect, useState } from "react";
import { apiFetch, isAbortError } from "../api";
import { Badge } from "../components/Badge";
import { DateField } from "../components/DateField";
import { ModalShell } from "../components/ModalShell";
import { confirmDialog, notify } from "../components/dialogStore";
import { formatThaiDateTime } from "../dateTime";
import { CopyButton } from "../components/CopyButton";
import { EmployeeReportView } from "./EmployeeReport";
import { errorText, fmtDate, type EmployeeReport, type ShareLink } from "./performanceUtils";

// แท็บ "สรุปผลงาน" (หัวหน้า) — ดูสรุปรายคนแบบหน้าเดียว พิมพ์/บันทึก PDF และสร้างลิงก์อ่านอย่างเดียวส่งให้ HR

type UserOption = { userId: string; displayName: string };
const lifetimes = [7, 14, 30, 60, 90] as const;
const shareStatus: Record<ShareLink["status"], { label: string; tone: string }> = {
  Active: { label: "ใช้งานได้", tone: "green" },
  Expired: { label: "หมดอายุ", tone: "gray" },
  Revoked: { label: "ยกเลิกแล้ว", tone: "red" },
};

const copyLinkFailed = "คัดลอกอัตโนมัติไม่สำเร็จ — เลือกข้อความลิงก์แล้วกด Ctrl+C";

export function ReportView({ userId, currentUserId, range, refreshKey, onSelectUser }: {
  userId: string; currentUserId: string; range: { from: string; to: string }; refreshKey: number; onSelectUser: (userId: string) => void;
}) {
  const [users, setUsers] = useState<UserOption[]>([]);
  const [report, setReport] = useState<EmployeeReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [links, setLinks] = useState<ShareLink[] | null>(null);
  const [linksError, setLinksError] = useState("");
  const [linksKey, setLinksKey] = useState(0);
  const [shareOpen, setShareOpen] = useState(false);
  const [revoking, setRevoking] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    apiFetch<UserOption[]>("/performance/users", { signal: controller.signal }).then(setUsers).catch(() => setUsers([]));
    return () => controller.abort();
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError("");
    apiFetch<EmployeeReport>(`/performance/users/${userId}/report?from=${range.from}&to=${range.to}`, { signal: controller.signal })
      .then(setReport)
      .catch((e) => { if (!isAbortError(e)) { setReport(null); setError(errorText(e, "โหลดสรุปผลงานไม่สำเร็จ")); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [userId, range.from, range.to, refreshKey]);

  useEffect(() => {
    const controller = new AbortController();
    setLinksError("");
    apiFetch<ShareLink[]>(`/performance/users/${userId}/share-links`, { signal: controller.signal })
      .then(setLinks)
      .catch((e) => { if (!isAbortError(e)) { setLinks(null); setLinksError(errorText(e, "โหลดรายการลิงก์ที่แชร์ไม่สำเร็จ")); } });
    return () => controller.abort();
  }, [userId, refreshKey, linksKey]);

  const revoke = async (link: ShareLink) => {
    const ok = await confirmDialog({
      title: "ยกเลิกลิงก์นี้?",
      message: `ผู้ที่ได้รับลิงก์ (ช่วง ${fmtDate(link.from)} – ${fmtDate(link.to)}) จะเปิดดูไม่ได้ทันที และยกเลิกแล้วเปิดใช้ใหม่ไม่ได้ ต้องสร้างลิงก์ใหม่แทน`,
      confirmLabel: "ยกเลิกลิงก์", tone: "danger",
    });
    if (!ok) return;
    setRevoking(link.shareLinkId);
    try {
      await apiFetch(`/performance/share-links/${link.shareLinkId}/revoke`, { method: "POST" });
      notify("ยกเลิกลิงก์แล้ว", "success");
      setLinksKey((k) => k + 1);
    } catch (e) {
      notify(errorText(e, "ยกเลิกลิงก์ไม่สำเร็จ"), "error");
    } finally { setRevoking(""); }
  };

  const activeLinks = links?.filter((x) => x.status === "Active").length ?? 0;

  return (
    <>
      <section className="content-card perf-report-toolbar perf-noprint" aria-label="เลือกพนักงานสำหรับสรุปผลงาน">
        <label className="perf-report-picker">
          <span className="material-symbols-outlined" aria-hidden="true">person_search</span>
          <span className="perf-report-picker-label">สรุปผลงานของ</span>
          <select value={userId} onChange={(e) => onSelectUser(e.target.value)} aria-label="เลือกพนักงาน">
            {!users.some((u) => u.userId === userId) && <option value={userId}>{report?.profile.displayName ?? "ผู้ใช้ที่เลือก"}</option>}
            {users.map((u) => <option key={u.userId} value={u.userId}>{u.displayName}{u.userId === currentUserId ? " (ฉัน)" : ""}</option>)}
          </select>
        </label>
        <span className="perf-report-toolbar-hint">ช่วงข้อมูลเลือกจากแถบด้านบน · พิมพ์ได้ทั้งหน้าเป็น PDF</span>
        {links && <span className={`perf-report-links-chip${activeLinks ? " active" : ""}`}><span className="material-symbols-outlined" aria-hidden="true">link</span>ลิงก์ HR ที่ใช้งานได้ {activeLinks.toLocaleString("th-TH")}</span>}
      </section>

      {loading && !report ? (
        <article className="card empty" role="status"><div className="spinner" /><h3>กำลังโหลดสรุปผลงาน...</h3></article>
      ) : error ? (
        <div className="inline-alert error" role="alert"><span>{error}</span></div>
      ) : report ? (
        <EmployeeReportView
          report={report}
          actions={<>
            <button className="btn" type="button" onClick={() => window.print()}><span className="material-symbols-outlined" aria-hidden="true">print</span>พิมพ์ / PDF</button>
            <button className="btn primary" type="button" onClick={() => setShareOpen(true)}><span className="material-symbols-outlined" aria-hidden="true">share</span>แชร์ให้ HR</button>
          </>}
        />
      ) : null}

      <section className="content-card perf-share-links perf-noprint" aria-labelledby="perf-share-links-title">
        <div className="perf-share-links-head">
          <div>
            <h3 id="perf-share-links-title"><span className="material-symbols-outlined" aria-hidden="true">link</span>ลิงก์ที่แชร์แล้ว</h3>
            <p>ลิงก์อ่านอย่างเดียวที่ส่งให้ HR ของพนักงานคนนี้ — ใช้งานได้ {activeLinks.toLocaleString("th-TH")} ลิงก์</p>
          </div>
        </div>
        {linksError ? (
          <div className="inline-alert error" role="alert"><span>{linksError}</span><button className="btn" type="button" onClick={() => setLinksKey((k) => k + 1)}>ลองใหม่</button></div>
        ) : links === null ? (
          <p className="perf-report-empty" role="status">กำลังโหลด...</p>
        ) : links.length === 0 ? (
          <p className="perf-report-empty">ยังไม่เคยแชร์ — กด "แชร์ให้ HR" เพื่อสร้างลิงก์</p>
        ) : (
          <div className="table-wrap">
            <table className="table-cards perf-share-table">
              <thead><tr><th>ช่วงข้อมูล</th><th>สถานะ</th><th>หมายเหตุ</th><th>สร้างโดย</th><th>หมดอายุ</th><th className="num">เปิดดู</th><th aria-label="การทำงาน" /></tr></thead>
              <tbody>
                {links.map((x) => (
                  <tr key={x.shareLinkId}>
                    <td><b>{fmtDate(x.from)} – {fmtDate(x.to)}</b><small className="perf-sub">{[x.includeEvaluations && "การประเมิน", x.includeWorkLogs && "บันทึกงาน"].filter(Boolean).join(" · ") || "เฉพาะตัวเลขผลงาน"}</small></td>
                    <td><Badge tone={shareStatus[x.status].tone}>{shareStatus[x.status].label}</Badge>{x.revokedAt && <small className="perf-sub">{x.revokedByName ?? "-"} · {formatThaiDateTime(x.revokedAt, { dateStyle: "medium" })}</small>}</td>
                    <td>{x.note || "-"}</td>
                    <td>{x.createdByName ?? "-"}<small className="perf-sub">{formatThaiDateTime(x.createdAt, { dateStyle: "medium", timeStyle: "short" })}</small></td>
                    <td>{formatThaiDateTime(x.expiresAt, { dateStyle: "medium", timeStyle: "short" })}</td>
                    <td className="num">{x.viewCount.toLocaleString("th-TH")}{x.lastViewedAt && <small className="perf-sub">ล่าสุด {formatThaiDateTime(x.lastViewedAt, { dateStyle: "short", timeStyle: "short" })}</small>}</td>
                    <td className="perf-row-action">
                      {x.status === "Active" && (
                        <span className="perf-share-actions">
                          <CopyButton text={x.url} failMessage={copyLinkFailed} ariaLabel={`คัดลอกลิงก์ช่วง ${fmtDate(x.from)} – ${fmtDate(x.to)}`}>คัดลอก</CopyButton>
                          <button className="btn danger" type="button" disabled={revoking === x.shareLinkId} onClick={() => void revoke(x)} aria-label={`ยกเลิกลิงก์ช่วง ${fmtDate(x.from)} – ${fmtDate(x.to)}`}><span className="material-symbols-outlined" aria-hidden="true">link_off</span>ยกเลิก</button>
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {shareOpen && report && (
        <ShareModal
          userId={userId}
          employeeName={report.profile.displayName}
          range={range}
          onClose={() => setShareOpen(false)}
          onCreated={() => setLinksKey((k) => k + 1)}
        />
      )}
    </>
  );
}

function ShareModal({ userId, employeeName, range, onClose, onCreated }: {
  userId: string; employeeName: string; range: { from: string; to: string }; onClose: () => void; onCreated: () => void;
}) {
  const [form, setForm] = useState({ from: range.from, to: range.to, expiresInDays: 30, includeEvaluations: true, includeWorkLogs: true, note: "" });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [created, setCreated] = useState<ShareLink | null>(null);
  const rangeInvalid = !form.from || !form.to || form.from > form.to;

  const create = async () => {
    if (rangeInvalid || saving) return;
    setSaving(true);
    setError("");
    try {
      const link = await apiFetch<ShareLink>(`/performance/users/${userId}/share-links`, { method: "POST", json: { ...form, note: form.note.trim() || null } });
      setCreated(link);
      onCreated();
    } catch (e) {
      setError(errorText(e, "สร้างลิงก์ไม่สำเร็จ"));
    } finally { setSaving(false); }
  };

  return (
    <ModalShell labelledBy="perf-share-title" className="perf-modal perf-share-modal" dirty={!created && form.note.trim() !== ""} onDismiss={() => { if (!saving) onClose(); }}>
      <div className="modal-head">
        <div><h2 id="perf-share-title">แชร์สรุปผลงานให้ HR</h2><small>{employeeName}</small></div>
        <button type="button" aria-label="ปิด" disabled={saving} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>
      {created ? (
        <div className="perf-share-created">
          <div className="inline-alert success" role="status"><span className="material-symbols-outlined" aria-hidden="true">check_circle</span><span>สร้างลิงก์แล้ว ใช้ได้ถึง {formatThaiDateTime(created.expiresAt, { dateStyle: "long", timeStyle: "short" })}</span></div>
          <label className="perf-share-url">
            <span>ลิงก์สำหรับส่งให้ HR</span>
            <span className="perf-share-url-row">
              <input readOnly value={created.url} onFocus={(e) => e.currentTarget.select()} aria-label="ลิงก์สำหรับส่งให้ HR" />
              <CopyButton className="btn primary" text={created.url} failMessage={copyLinkFailed}>คัดลอก</CopyButton>
            </span>
          </label>
          <p className="perf-hint">ส่งลิงก์ผ่านช่องทางภายในองค์กรเท่านั้น — ใครมีลิงก์ก็เปิดดูได้โดยไม่ต้อง login จนกว่าจะหมดอายุหรือถูกยกเลิก ดูจำนวนครั้งที่เปิดและยกเลิกได้ที่ "ลิงก์ที่แชร์แล้ว"</p>
        </div>
      ) : (
        <div className="form-grid">
          <label>ข้อมูลผลงานตั้งแต่<span className="required-mark"> *</span><DateField value={form.from} max={form.to} invalid={rangeInvalid} onChange={(from) => setForm((f) => ({ ...f, from }))} /></label>
          <label>ถึง<span className="required-mark"> *</span><DateField value={form.to} min={form.from} invalid={rangeInvalid} onChange={(to) => setForm((f) => ({ ...f, to }))} /></label>
          {rangeInvalid && <small className="full field-error" role="alert">วันที่เริ่มต้องไม่หลังวันที่สิ้นสุด</small>}
          <label>อายุลิงก์
            <select value={form.expiresInDays} onChange={(e) => setForm((f) => ({ ...f, expiresInDays: Number(e.target.value) }))}>
              {lifetimes.map((d) => <option key={d} value={d}>{d} วัน</option>)}
            </select>
          </label>
          <label>หมายเหตุ (ส่งให้ใคร / เพื่ออะไร)
            <input value={form.note} maxLength={200} onChange={(e) => setForm((f) => ({ ...f, note: e.target.value }))} placeholder="เช่น HR — ประกอบการผ่านทดลองงาน" />
          </label>
          <fieldset className="full perf-share-options">
            <legend>ข้อมูลที่แชร์</legend>
            <p className="perf-share-always"><span className="material-symbols-outlined" aria-hidden="true">check_circle</span>ข้อมูลพนักงานและตัวเลขผลงานจากระบบ (แชร์เสมอ)</p>
            <label className="perf-check"><input type="checkbox" checked={form.includeEvaluations} onChange={(e) => setForm((f) => ({ ...f, includeEvaluations: e.target.checked }))} /> ผลการประเมิน (เฉพาะรอบที่ส่งผลแล้วจึงมีคะแนน)</label>
            <label className="perf-check"><input type="checkbox" checked={form.includeWorkLogs} onChange={(e) => setForm((f) => ({ ...f, includeWorkLogs: e.target.checked }))} /> ผลงานเด่นและบันทึกงาน</label>
          </fieldset>
          <div className="full perf-share-warning">
            <span className="material-symbols-outlined" aria-hidden="true">lock</span>
            <span>ผู้ที่มีลิงก์เปิดดูได้โดยไม่ต้อง login จนหมดอายุ · ไม่แชร์ข้อสังเกตของหัวหน้า บันทึกภายใน ไฟล์แนบ และคะแนนร่าง · ข้อมูลคำนวณใหม่ทุกครั้งที่เปิดตามสิทธิ์ของคุณ</span>
          </div>
        </div>
      )}
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      <div className="modal-actions">
        {created ? (
          <button className="btn primary" type="button" onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">check</span> เสร็จสิ้น</button>
        ) : (
          <>
            <button className="btn" type="button" disabled={saving} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button>
            <button className="btn primary" type="button" disabled={saving || rangeInvalid} onClick={() => void create()}>
              {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังสร้าง...</> : <><span className="material-symbols-outlined" aria-hidden="true">add_link</span> สร้างลิงก์</>}
            </button>
          </>
        )}
      </div>
    </ModalShell>
  );
}
