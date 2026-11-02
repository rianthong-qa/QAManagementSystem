import { useEffect, useState } from "react";
import { apiUrl } from "../api";
import type { AutomationCaseItem, ExecutionTrendBucket } from "./types";
import { fetchJson, isAbort } from "./shared";

const DAYS = 14;
const pad = (n: number) => String(n).padStart(2, "0");
const dateKey = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const thaiMonths = ["ม.ค.", "ก.พ.", "มี.ค.", "เม.ย.", "พ.ค.", "มิ.ย.", "ก.ค.", "ส.ค.", "ก.ย.", "ต.ค.", "พ.ย.", "ธ.ค."];

/// กราฟภาพรวม: ผลการรันรายวัน 14 วันล่าสุด (จาก executions/trend) + สัดส่วนสถานะ Automation Case (จากรายการ Case ที่หน้าโหลดไว้แล้ว)
export function OverviewCharts({ projectId, headers, cases, totalCases, reload, onOpenRuns, onOpenCases }: {
  projectId: string; headers: Record<string, string>; cases: AutomationCaseItem[]; totalCases: number; reload: number;
  onOpenRuns: () => void; onOpenCases: (status: string) => void;
}) {
  return <div className="au-charts">
    <RunTrendCard projectId={projectId} headers={headers} reload={reload} onOpenRuns={onOpenRuns} />
    <CaseStatusCard cases={cases} totalCases={totalCases} onOpenCases={onOpenCases} />
  </div>;
}

function RunTrendCard({ projectId, headers, reload, onOpenRuns }: { projectId: string; headers: Record<string, string>; reload: number; onOpenRuns: () => void }) {
  const [buckets, setBuckets] = useState<ExecutionTrendBucket[] | null>(null);
  const [error, setError] = useState("");
  const [retry, setRetry] = useState(0);
  const [hover, setHover] = useState<number | null>(null);

  useEffect(() => {
    if (!projectId) return;
    const ctrl = new AbortController();
    const from = new Date();
    from.setHours(0, 0, 0, 0);
    from.setDate(from.getDate() - (DAYS - 1));
    const qs = new URLSearchParams({ projectId, groupBy: "day", from: from.toISOString() });
    setError("");
    fetchJson(`${apiUrl}/automation/executions/trend?${qs}`, headers, ctrl.signal)
      .then((d) => setBuckets(Array.isArray(d?.buckets) ? d.buckets : []))
      .catch((e) => { if (!isAbort(e)) setError("โหลดกราฟผลการรันไม่สำเร็จ"); });
    return () => ctrl.abort();
  }, [projectId, headers, reload, retry]);

  // เติมวันที่ไม่มีการรันเป็น 0 ให้แกนเวลาต่อเนื่อง
  const byKey = new Map((buckets ?? []).map((b) => [b.bucketKey, b]));
  const days = Array.from({ length: DAYS }, (_, i) => {
    const d = new Date();
    d.setDate(d.getDate() - (DAYS - 1 - i));
    const b = byKey.get(dateKey(d));
    return { key: dateKey(d), label: `${d.getDate()}`, full: `${d.getDate()} ${thaiMonths[d.getMonth()]}`, month: d.getDate() === 1 || i === 0 ? thaiMonths[d.getMonth()] : "", passed: b?.passed ?? 0, failed: b?.failed ?? 0 };
  });
  const passed = days.reduce((s, d) => s + d.passed, 0);
  const failed = days.reduce((s, d) => s + d.failed, 0);
  const total = passed + failed;
  const rate = total ? Math.round((passed / total) * 100) : null;
  const max = Math.max(1, ...days.map((d) => d.passed + d.failed));
  const niceMax = max <= 4 ? max : Math.ceil(max / 4) * 4;
  const ticks = niceMax <= 4 ? Array.from({ length: niceMax + 1 }, (_, i) => i) : [0, niceMax / 4, niceMax / 2, (niceMax * 3) / 4, niceMax];

  return <section className="au-panel au-chart-card">
    <header className="au-panel-head"><h2>ผลการรัน {DAYS} วันล่าสุด</h2><button type="button" className="au-link" onClick={onOpenRuns}>ดูผลการรัน ›</button></header>
    {error ? <div className="au-chart-state" role="alert">{error}<button type="button" className="btn" onClick={() => setRetry((v) => v + 1)}>ลองใหม่</button></div>
      : buckets === null ? <div className="au-chart-state" role="status"><span className="spinner inline" aria-hidden="true" />กำลังโหลด...</div>
      : <>
        <div className="au-chart-headline">
          <div><strong>{rate == null ? "—" : `${rate}%`}</strong><span>อัตราผ่าน</span></div>
          <dl>
            <div><dt><i className="au-key is-pass" aria-hidden="true" />ผ่าน</dt><dd>{passed.toLocaleString()}</dd></div>
            <div><dt><i className="au-key is-fail" aria-hidden="true" />Fail</dt><dd>{failed.toLocaleString()}</dd></div>
            <div><dt>รันทั้งหมด</dt><dd>{total.toLocaleString()}</dd></div>
          </dl>
        </div>
        {total === 0 ? <div className="au-chart-state">ยังไม่มีผลการรันใน {DAYS} วันที่ผ่านมา</div> : <div className="au-bars" onMouseLeave={() => setHover(null)}>
          <div className="au-bars-grid" aria-hidden="true">{[...ticks].reverse().map((t) => <span key={t} style={{ bottom: `${(t / niceMax) * 100}%` }}><em>{t}</em></span>)}</div>
          <div className="au-bars-plot" role="img" aria-label={`ผลการรันรายวัน ${DAYS} วัน: ผ่าน ${passed} ครั้ง Fail ${failed} ครั้ง อัตราผ่าน ${rate}%`}>
            {days.map((d, i) => <div key={d.key} className={`au-bar-col${hover === i ? " is-hover" : ""}`} onMouseEnter={() => setHover(i)}>
              <div className="au-bar-stack" style={{ height: `${((d.passed + d.failed) / niceMax) * 100}%` }}>
                {d.failed > 0 && <span className="au-bar-seg is-fail" style={{ flexGrow: d.failed }} />}
                {d.passed > 0 && <span className="au-bar-seg is-pass" style={{ flexGrow: d.passed }} />}
              </div>
              <span className="au-bar-label">{d.label}{d.month && <small>{d.month}</small>}</span>
              {hover === i && <div className={`au-tip${i > DAYS - 4 ? " is-left" : i < 3 ? " is-right" : ""}`} role="presentation" style={{ bottom: `calc(32px + (100% - 24px) * ${(d.passed + d.failed) / niceMax})` }}>
                <b>{d.full}</b>
                <span><i className="au-key is-pass" />ผ่าน {d.passed}</span>
                <span><i className="au-key is-fail" />Fail {d.failed}</span>
              </div>}
            </div>)}
          </div>
        </div>}
        <table className="sr-only"><caption>ผลการรันรายวัน</caption><thead><tr><th>วันที่</th><th>ผ่าน</th><th>Fail</th></tr></thead><tbody>{days.map((d) => <tr key={d.key}><td>{d.full}</td><td>{d.passed}</td><td>{d.failed}</td></tr>)}</tbody></table>
      </>}
  </section>;
}

const caseGroups: { id: string; label: string; statuses: string[]; tone: string; filter: string }[] = [
  { id: "ready", label: "พร้อมรัน", statuses: ["Ready", "Running"], tone: "ready", filter: "Ready" },
  { id: "validated", label: "ตรวจผ่าน / อนุมัติแล้ว", statuses: ["Validated", "Approved"], tone: "validated", filter: "Validated" },
  { id: "review", label: "รอตรวจ", statuses: ["NeedsReview"], tone: "review", filter: "NeedsReview" },
  { id: "draft", label: "ฉบับร่าง", statuses: ["Draft"], tone: "draft", filter: "Draft" },
  { id: "maint", label: "ต้องซ่อม", statuses: ["MaintenanceRequired"], tone: "maint", filter: "MaintenanceRequired" },
];

function CaseStatusCard({ cases, totalCases, onOpenCases }: { cases: AutomationCaseItem[]; totalCases: number; onOpenCases: (status: string) => void }) {
  const [hover, setHover] = useState<string | null>(null);
  const rows = caseGroups.map((g) => ({ ...g, count: cases.filter((c) => g.statuses.includes(c.status)).length }));
  const counted = rows.reduce((s, r) => s + r.count, 0);
  const pct = (n: number) => (counted ? Math.round((n / counted) * 100) : 0);
  const ready = rows[0].count;

  return <section className="au-panel au-chart-card">
    <header className="au-panel-head"><h2>สถานะ Automation Case</h2><button type="button" className="au-link" onClick={() => onOpenCases("all")}>ดู Case ›</button></header>
    {counted === 0 ? <div className="au-chart-state">ยังไม่มี Automation Case</div> : <>
      <div className="au-chart-headline">
        <div><strong>{pct(ready)}%</strong><span>พร้อมรัน ({ready} จาก {counted})</span></div>
      </div>
      <div className="au-share" role="img" aria-label={rows.filter((r) => r.count).map((r) => `${r.label} ${r.count}`).join(", ")} onMouseLeave={() => setHover(null)}>
        {rows.filter((r) => r.count > 0).map((r) => <span key={r.id} className={`au-share-seg t-${r.tone}${hover && hover !== r.id ? " is-dim" : ""}`} style={{ flexGrow: r.count }} onMouseEnter={() => setHover(r.id)} title={`${r.label}: ${r.count} (${pct(r.count)}%)`} />)}
      </div>
      <ul className="au-share-legend">
        {rows.map((r) => <li key={r.id} className={hover && hover !== r.id ? "is-dim" : ""} onMouseEnter={() => setHover(r.id)} onMouseLeave={() => setHover(null)}>
          <button type="button" disabled={!r.count} onClick={() => onOpenCases(r.filter)}>
            <i className={`au-key t-${r.tone}`} aria-hidden="true" /><span>{r.label}</span><b>{r.count}</b><small>{pct(r.count)}%</small>
          </button>
        </li>)}
      </ul>
      {totalCases > counted && <p className="au-chart-note">นับจาก {counted} Case ล่าสุด (ทั้งหมด {totalCases})</p>}
    </>}
  </section>;
}
