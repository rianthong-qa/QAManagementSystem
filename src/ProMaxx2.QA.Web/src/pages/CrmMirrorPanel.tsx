import { useEffect, useState } from "react";
import { apiFetch, isAbortError } from "../api";
import { Badge } from "../components/Badge";
import { notify } from "../components/dialogStore";
import { formatThaiDateTime } from "../dateTime";
import { CrmMirrorIntervalEditor } from "../components/CrmMirrorIntervalEditor";

type MirrorRun = { crmMirrorRunId: string; kind: string; rangeFrom: string; rangeTo: string; status: string; startedAt: string; completedAt?: string | null; ticketCount: number; newTickets: number; changedTickets: number; newAnswers: number; error?: string | null };
type MirrorStatus = { enabled: boolean; accountUsername: string; tickets: number; events: number; lastSucceededAt?: string | null; runs: MirrorRun[]; intervalMinutes?: number };

const runTone: Record<string, string> = { Succeeded: "green", Failed: "red", Skipped: "yellow", Running: "blue" };
const runLabel: Record<string, string> = { Succeeded: "สำเร็จ", Failed: "ไม่สำเร็จ", Skipped: "ข้าม", Running: "กำลังดึง" };
const when = (value?: string | null) => value ? formatThaiDateTime(value, { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" }) : "-";

/** สถานะการดึงสำเนา CRM Ticket ทั้งทีมมาเก็บใน QA Hub (ใช้วัดผลงาน CRM) — เฉพาะ SYS_ADMIN */
export function CrmMirrorPanel() {
  const [data, setData] = useState<MirrorStatus | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    apiFetch<MirrorStatus>("/crm/mirror/status", { signal: controller.signal })
      .then((d) => { setData(d); setError(""); })
      .catch((e) => { if (!isAbortError(e)) setError(e instanceof Error ? e.message : "โหลดสถานะไม่สำเร็จ"); });
    return () => controller.abort();
  }, [reload]);

  const sync = async (full: boolean) => {
    setBusy(true);
    try {
      const run = await apiFetch<MirrorRun>(`/crm/mirror/sync?full=${full}`, { method: "POST", fallbackMessage: "ดึงข้อมูล CRM ไม่สำเร็จ" });
      notify(run.status === "Succeeded" ? `ดึงข้อมูล CRM แล้ว ${run.ticketCount.toLocaleString("th-TH")} Ticket (ใหม่ ${run.newTickets} · เปลี่ยน ${run.changedTickets})` : run.error || "ดึงข้อมูล CRM ไม่สำเร็จ", run.status === "Succeeded" ? "success" : "error");
    } catch (e) {
      notify(e instanceof Error ? e.message : "ดึงข้อมูล CRM ไม่สำเร็จ", "error");
    } finally {
      setBusy(false);
      setReload((v) => v + 1);
    }
  };

  return (
    <details className="card crm-mirror-panel">
      <summary>
        <span className="material-symbols-outlined" aria-hidden="true">cloud_sync</span>
        <span className="crm-mirror-title"><strong>ข้อมูล CRM ใน QA Hub</strong><small>สำหรับผู้ดูแลระบบ · ใช้วัดผลงานในเมนู ผลงานและการประเมิน</small></span>
        <span className="crm-mirror-last">{data ? (data.lastSucceededAt ? `ดึงล่าสุด ${when(data.lastSucceededAt)}` : "ยังไม่เคยดึงสำเร็จ") : ""}</span>
        <span className="crm-mirror-chevron material-symbols-outlined" aria-hidden="true">expand_more</span>
      </summary>
      {error ? <div className="inline-alert error" role="alert"><span>{error}</span></div> : !data ? <p className="crm-mirror-note">กำลังโหลด...</p> : (
        <div className="crm-mirror-body">
          <dl className="crm-mirror-stats">
            <div><dt>บัญชีที่ใช้ดึง</dt><dd>{data.accountUsername}{!data.enabled && " (ปิดการดึงอัตโนมัติ)"}</dd></div>
            <div><dt>Ticket ที่เก็บไว้</dt><dd>{data.tickets.toLocaleString("th-TH")}</dd></div>
            <div><dt>เหตุการณ์ที่บันทึก</dt><dd>{data.events.toLocaleString("th-TH")}</dd></div>
          </dl>
          <CrmMirrorIntervalEditor minutes={data.intervalMinutes ?? 15} prefix="ดึงอัตโนมัติทุก" onSaved={(minutes) => setData((current) => current && { ...current, intervalMinutes: minutes })} />
          <p className="crm-mirror-note">ดึง Ticket ที่ติดต่อใน 90 วันล่าสุดตามรอบด้านบน และรอบใหญ่ย้อนหลัง 1 ปีวันละครั้ง — เก็บเฉพาะสถานะ/ผู้รับผิดชอบ/วันที่ ไม่เก็บข้อมูลลูกค้าและเนื้อหาข้อความ</p>
          <div className="crm-mirror-actions">
            <button type="button" className="btn primary" disabled={busy} onClick={() => sync(false)}>{busy ? <><span className="spinner inline" aria-hidden="true" /> กำลังดึง...</> : <><span className="material-symbols-outlined" aria-hidden="true">sync</span> ดึงข้อมูลตอนนี้</>}</button>
            <button type="button" className="btn" disabled={busy} onClick={() => sync(true)}><span className="material-symbols-outlined" aria-hidden="true">history</span> ดึงย้อนหลัง 1 ปี</button>
          </div>
          {data.runs.length > 0 && (
            <div className="table-wrap">
              <table className="table-cards crm-mirror-table">
                <thead><tr><th>เริ่ม</th><th>รอบ</th><th>ผล</th><th>Ticket</th><th>ใหม่ / เปลี่ยน / ข้อความตอบ</th><th>หมายเหตุ</th></tr></thead>
                <tbody>
                  {data.runs.map((r) => (
                    <tr key={r.crmMirrorRunId}>
                      <td>{when(r.startedAt)}</td>
                      <td>{r.kind === "Full" ? "ย้อนหลัง 1 ปี" : "90 วัน"}</td>
                      <td><Badge tone={runTone[r.status] ?? "blue"}>{runLabel[r.status] ?? r.status}</Badge></td>
                      <td>{r.ticketCount.toLocaleString("th-TH")}</td>
                      <td>{r.newTickets} / {r.changedTickets} / {r.newAnswers}</td>
                      <td className="crm-mirror-error">{r.error || "-"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </details>
  );
}
