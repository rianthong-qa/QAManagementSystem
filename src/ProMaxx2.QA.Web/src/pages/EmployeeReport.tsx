import type { ReactNode } from "react";
import { Badge } from "../components/Badge";
import { formatThaiDateTime } from "../dateTime";
import {
  employmentStatusLabel, employmentStatusTone, evalStatus, fmtDate, metricGroups, passRate, recommendationLabel, roundText, workCategoryLabel,
  type EmployeeReport,
} from "./performanceUtils";
import "./PerformanceReport.css";

// สรุปผลงานพนักงานแบบหน้าเดียว — ใช้ทั้งแท็บ "สรุปผลงาน" ในแอปและหน้าแชร์ให้ HR (`/?p=`) จึงรับข้อมูลเป็น props อย่างเดียว ไม่เรียก API เอง
// และต้องไม่แสดงอะไรที่ API ไม่ได้ส่งมา (ข้อสังเกตของหัวหน้า/ไฟล์แนบ/คะแนนร่างไม่อยู่ใน EmployeeReportDto ตั้งแต่ต้น)

const kindLabel: Record<string, string> = { WorkLog: "บันทึกงาน", Highlight: "ผลงานเด่น" };

/** อายุงานจากวันเริ่มงานถึงวันสิ้นสุดช่วงรายงาน เช่น "1 ปี 3 เดือน" */
function tenure(hireDate?: string | null, until?: string): string | null {
  if (!hireDate || !until) return null;
  const [hy, hm, hd] = hireDate.slice(0, 10).split("-").map(Number);
  const [uy, um, ud] = until.slice(0, 10).split("-").map(Number);
  let months = (uy - hy) * 12 + (um - hm) - (ud < hd ? 1 : 0);
  if (months < 0) return null;
  const years = Math.floor(months / 12);
  months %= 12;
  if (!years && !months) return "ไม่ถึง 1 เดือน";
  return [years ? `${years} ปี` : "", months ? `${months} เดือน` : ""].filter(Boolean).join(" ");
}

export function EmployeeReportView({ report, actions, banner }: { report: EmployeeReport; actions?: ReactNode; banner?: ReactNode }) {
  const p = report.profile;
  const m = report.metrics;
  const highlights = report.workLogs.filter((x) => x.kind === "Highlight");
  const otherLogs = report.workLogs.filter((x) => x.kind !== "Highlight");
  const closedRounds = report.comparison?.rounds ?? [];
  const probationPct = p.probationDay != null ? Math.min(100, (p.probationDay / p.probationTotalDays) * 100) : null;
  const detailGroups = metricGroups(m).filter((g) => g.title === "การทดสอบ" || g.title === "งาน CRM" || g.items.some(([, value]) => {
    const text = String(value).trim();
    return text !== "0" && text !== "0 / 0" && text !== "-" && text !== "ยังไม่ได้ผูก";
  }));
  const kpis: { icon: string; label: string; value: string; note?: string }[] = [
    { icon: "play_circle", label: "Execution", value: m.executions.toLocaleString("th-TH"), note: `Pass rate ${passRate(m)}` },
    { icon: "bug_report", label: "Defect ที่แจ้ง", value: m.defectsReported.toLocaleString("th-TH"), note: `Critical/High ${m.defectsCritical + m.defectsHigh}` },
    { icon: "fact_check", label: "Test Case ที่สร้าง", value: m.testCasesCreated.toLocaleString("th-TH"), note: `แก้ไข ${m.testCaseRevisions.toLocaleString("th-TH")} ครั้ง` },
    { icon: "support_agent", label: "CRM Ticket ที่ดูแล", value: m.hasCrmAccount ? m.crmTicketsHandled.toLocaleString("th-TH") : "-", note: m.hasCrmAccount ? `ปิดงาน ${m.crmClosed.toLocaleString("th-TH")}` : "ยังไม่ได้ผูกบัญชี CRM" },
    { icon: "star", label: "ผลงานเด่น", value: m.highlights.toLocaleString("th-TH"), note: `บันทึกงาน ${m.workLogs.toLocaleString("th-TH")} รายการ` },
  ];

  const weeklyMax = {
    executions: Math.max(1, ...report.weekly.map((w) => w.executions)),
    defects: Math.max(1, ...report.weekly.map((w) => w.defects)),
    testCases: Math.max(1, ...report.weekly.map((w) => w.testCases)),
  };
  const tenureText = tenure(p.hireDate, report.to);

  return (
    <article className="perf-report perf-report-v2" aria-labelledby="perf-report-title">
      {banner}
      <section className="content-card perf-report-head">
        <div className="perf-report-docbar">
          <span className="perf-report-eyebrow"><span className="material-symbols-outlined" aria-hidden="true">summarize</span>สรุปผลงานพนักงาน</span>
          <span className="perf-report-period"><span className="material-symbols-outlined" aria-hidden="true">date_range</span>{fmtDate(report.from)} – {fmtDate(report.to)}</span>
          <span className="perf-report-generated">ข้อมูล ณ {formatThaiDateTime(report.generatedAt, { dateStyle: "medium", timeStyle: "short" })}</span>
        </div>
        <div className="perf-report-title">
          <span className="perf-report-avatar" aria-hidden="true">{p.displayName.slice(0, 1).toUpperCase()}</span>
          <div>
            <h2 id="perf-report-title">{p.displayName}</h2>
            <p>{[p.position, p.level].filter(Boolean).join(" · ") || "ยังไม่ได้ระบุตำแหน่ง"}</p>
          </div>
          {actions && <div className="perf-report-actions perf-noprint">{actions}</div>}
        </div>
        <dl className="perf-report-meta">
          <div><dt><span className="material-symbols-outlined" aria-hidden="true">badge</span>รหัสพนักงาน</dt><dd>{p.employeeCode || "-"}</dd></div>
          <div><dt><span className="material-symbols-outlined" aria-hidden="true">how_to_reg</span>สถานะ</dt><dd><Badge tone={employmentStatusTone[p.employmentStatus] ?? "gray"}>{employmentStatusLabel[p.employmentStatus] ?? p.employmentStatus}</Badge></dd></div>
          <div><dt><span className="material-symbols-outlined" aria-hidden="true">supervisor_account</span>หัวหน้า (ผู้ประเมิน)</dt><dd>{p.supervisorName || "-"}</dd></div>
          <div><dt><span className="material-symbols-outlined" aria-hidden="true">event</span>วันเริ่มงาน</dt><dd>{fmtDate(p.hireDate)}{tenureText && <small>อายุงาน {tenureText}</small>}</dd></div>
        </dl>
        {probationPct != null && (
          <div className="perf-report-probation">
            <div className="perf-report-probation-head"><b><span className="material-symbols-outlined" aria-hidden="true">hourglass_top</span>ช่วงทดลองงาน</b><span>วันที่ {p.probationDay} จาก {p.probationTotalDays} · สิ้นสุด {fmtDate(p.probationEndDate)}</span></div>
            <div className="perf-report-track" role="progressbar" aria-valuemin={1} aria-valuemax={p.probationTotalDays} aria-valuenow={Math.min(p.probationDay!, p.probationTotalDays)} aria-label={`ทดลองงานวันที่ ${p.probationDay} จาก ${p.probationTotalDays}`}>
              <span style={{ width: `${probationPct}%` }} />
            </div>
          </div>
        )}
        <dl className="perf-report-kpis" aria-label="ตัวเลขสำคัญ">
          {kpis.map((k) => (
            <div key={k.label} className="perf-report-kpi" data-perf-category={k.icon}>
              <dt><span className="material-symbols-outlined" aria-hidden="true">{k.icon}</span>{k.label}</dt>
              <dd><b>{k.value}</b>{k.note && <small>{k.note}</small>}</dd>
            </div>
          ))}
        </dl>
      </section>

      {report.includesWorkLogs && highlights.length > 0 && (
        <section className="content-card perf-report-section perf-report-highlights">
          <h3><span className="material-symbols-outlined" aria-hidden="true">workspace_premium</span>ผลงานเด่น<small>{highlights.length.toLocaleString("th-TH")} ผลงาน</small></h3>
          <ul className="perf-report-highlight-grid">
            {highlights.map((x, i) => (
              <li key={`${x.workDate}-${i}`}>
                <span className="perf-report-highlight-meta"><span className="material-symbols-outlined" aria-hidden="true">star</span>{fmtDate(x.workDate)} · {workCategoryLabel[x.category] ?? x.category}</span>
                <b>{x.title}</b>
                {x.description && <p>{x.description}</p>}
                {(x.projectName || x.effortHours != null) && <small>{[x.projectName, x.effortHours != null ? `${x.effortHours.toLocaleString("th-TH")} ชม.` : ""].filter(Boolean).join(" · ")}</small>}
              </li>
            ))}
          </ul>
        </section>
      )}

      <section className="perf-report-detail-block" aria-labelledby="perf-report-details-title">
        <h3 id="perf-report-details-title" className="sr-only">รายละเอียดผลงาน</h3>
        <div className="perf-report-metric-groups">
          {detailGroups.map((g) => <ReportMetricGroup key={g.icon} icon={g.icon} title={g.title} items={g.items} />)}
        </div>
      </section>

      {report.weekly.length > 0 && (
        <section className="content-card perf-report-section">
          <h3><span className="material-symbols-outlined" aria-hidden="true">insights</span>แนวโน้มรายสัปดาห์<small>แถบเทียบกับสัปดาห์ที่มากที่สุดในช่วงนี้</small></h3>
          <div className="table-wrap">
            <table className="table-cards perf-report-table perf-report-weekly">
              <thead><tr><th>สัปดาห์ที่เริ่ม</th><th>Execution</th><th>Defect ที่แจ้ง</th><th>Test Case ที่สร้าง</th></tr></thead>
              <tbody>
                {report.weekly.map((w) => (
                  <tr key={w.weekStart}>
                    <td>{fmtDate(w.weekStart)}</td>
                    <td><WeekBar category="play_circle" value={w.executions} max={weeklyMax.executions} /></td>
                    <td><WeekBar category="bug_report" value={w.defects} max={weeklyMax.defects} /></td>
                    <td><WeekBar category="fact_check" value={w.testCases} max={weeklyMax.testCases} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {report.includesWorkLogs && (
        <section className="content-card perf-report-section">
          <h3><span className="material-symbols-outlined" aria-hidden="true">edit_note</span>บันทึกงาน<small>{otherLogs.length.toLocaleString("th-TH")} รายการ{highlights.length ? ` · ผลงานเด่นแสดงด้านบน` : ""}</small></h3>
          {otherLogs.length === 0 ? (
            <p className="perf-report-empty">{highlights.length ? "ไม่มีบันทึกงานอื่นในช่วงนี้" : "ไม่มีบันทึกงานหรือผลงานเด่นในช่วงนี้"}</p>
          ) : (
            <ul className="perf-report-logs">
              {otherLogs.map((x, i) => (
                <li key={`${x.workDate}-${i}`}>
                  <div className="perf-report-log-head">
                    <Badge tone="blue">{kindLabel[x.kind] ?? x.kind}</Badge>
                    <b>{x.title}</b>
                  </div>
                  <small>{fmtDate(x.workDate)} · {workCategoryLabel[x.category] ?? x.category}{x.effortHours != null && ` · ${x.effortHours.toLocaleString("th-TH")} ชม.`}{x.projectName && ` · ${x.projectName}`}</small>
                  {x.description && <p>{x.description}</p>}
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      {report.includesEvaluations && (
        <section className="content-card perf-report-section">
          <h3><span className="material-symbols-outlined" aria-hidden="true">rate_review</span>ผลการประเมิน<small>ทุกรอบ ไม่จำกัดตามช่วงข้อมูลผลงาน</small></h3>
          {report.evaluations.length === 0 ? (
            <p className="perf-report-empty">ยังไม่มีรอบการประเมิน</p>
          ) : (
            <ul className="perf-report-evals">
              {report.evaluations.map((x) => (
                <li key={`${x.round}-${x.year}-${x.periodStart}`} className={x.grade ? "closed" : undefined}>
                  <span className="perf-report-eval-grade" aria-hidden={!x.grade}>{x.grade ?? "–"}</span>
                  <span className="perf-report-eval-main">
                    <b>{roundText(x)}</b>
                    <small>{fmtDate(x.periodStart)} – {fmtDate(x.periodEnd)} · ผู้ประเมิน {x.evaluatorName ?? "-"}{x.completedAt ? ` · ส่งผล ${formatThaiDateTime(x.completedAt, { dateStyle: "medium" })}` : ""}</small>
                  </span>
                  <span className="perf-report-eval-score">{x.overallScore != null ? <><b>{x.overallScore.toFixed(2)}</b><small>/ 5</small></> : <small>ยังไม่ปิดรอบ</small>}</span>
                  <span className="perf-report-eval-badges">
                    <Badge tone={evalStatus[x.status]?.tone ?? "gray"}>{evalStatus[x.status]?.label ?? x.status}</Badge>
                    {x.recommendation && <Badge tone={x.recommendation === "Pass" ? "green" : x.recommendation === "Extend" ? "yellow" : "red"}>{recommendationLabel[x.recommendation] ?? x.recommendation}</Badge>}
                    {x.isOverdue && <small className="perf-overdue">เลยกำหนด {fmtDate(x.dueDate)}</small>}
                  </span>
                </li>
              ))}
            </ul>
          )}
          {closedRounds.length > 0 && report.comparison && (
            <>
              <h4>คะแนนหัวหน้ารายกลุ่มเกณฑ์ (รอบที่ปิดแล้ว ล่าสุด {closedRounds.length} รอบ)</h4>
              <div className="table-wrap">
                <table className="table-cards perf-report-table">
                  <thead><tr><th>กลุ่มเกณฑ์</th>{closedRounds.map((r) => <th key={r.evaluationId} className="num">{roundText(r)}</th>)}</tr></thead>
                  <tbody>
                    <tr className="perf-report-total"><td><b>คะแนนรวม</b></td>{closedRounds.map((r) => <td key={r.evaluationId} className="num"><b>{r.overallScore.toFixed(2)}</b> ({r.grade})</td>)}</tr>
                    {report.comparison.groups.map((g) => (
                      <tr key={g}><td>{g}</td>{closedRounds.map((r) => { const v = r.groups.find((x) => x.group === g)?.score; return <td key={r.evaluationId} className="num">{v != null ? v.toFixed(2) : "-"}</td>; })}</tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </section>
      )}

      <p className="perf-hint perf-report-note">
        ตัวเลขจาก QA Hub และ CRM (ดึงทุก 15 นาที) ใช้ประกอบการประเมิน ไม่ใช่คะแนน · คะแนนประเมินแสดงเฉพาะรอบที่ส่งผลแล้ว (1–5; A ≥ 4.5, B ≥ 3.5, C ≥ 2.5, D ≥ 1.5)
      </p>
    </article>
  );
}

/** ตัวเลขรายสัปดาห์ + แถบบางเทียบกับสัปดาห์ที่มากที่สุด (ตัวเลขเป็นข้อมูลหลัก แถบช่วยสแกน) */
function WeekBar({ category, value, max }: { category: string; value: number; max: number }) {
  return (
    <span className="perf-report-weekbar" data-perf-category={category}>
      <b>{value.toLocaleString("th-TH")}</b>
      <span aria-hidden="true"><span style={{ width: `${value ? Math.max(4, (value / max) * 100) : 0}%` }} /></span>
    </span>
  );
}

/** กลุ่มตัวเลขในสรุปผลงาน — แถบเต็มความกว้าง: ชื่อกลุ่มซ้าย | ช่องตัวเลข (ชื่อบน ค่าล่าง) เรียงเป็นตารางขวา
 * ทุกกลุ่มสูงตามจำนวนแถวของช่องจึงไม่เหลือช่องว่างแบบการ์ดหลายคอลัมน์; ค่าที่เป็นศูนย์/ไม่มีข้อมูลเป็นสีจาง */
function ReportMetricGroup({ icon, title, items }: { icon: string; title: string; items: [string, number | string][] }) {
  const isEmpty = (value: number | string) => ["0", "0 / 0", "-", "ยังไม่ได้ผูก", "0%"].includes(String(value).trim());
  return (
    <section className="perf-report-metric-group" data-perf-category={icon} aria-label={title}>
      <h4 className="perf-report-metric-title"><span className="material-symbols-outlined" aria-hidden="true">{icon}</span>{title}</h4>
      <dl>
        {items.map(([label, value]) => (
          <div key={label} className={isEmpty(value) ? "is-empty" : undefined}><dt>{label}</dt><dd>{typeof value === "number" ? value.toLocaleString("th-TH") : value}</dd></div>
        ))}
      </dl>
    </section>
  );
}
