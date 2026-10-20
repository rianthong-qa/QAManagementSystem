import { useEffect, useMemo, useState } from "react";
import { apiFetch, isAbortError } from "../api";
import { Badge } from "../components/Badge";
import { DateField } from "../components/DateField";
import { confirmDialog, notify } from "../components/dialogStore";
import { formatThaiDateTime } from "../dateTime";
import { CopyButton } from "../components/CopyButton";
import { employmentStatusLabel, employmentStatusTone, errorText, evalStatus, fmtDate, roundText, thaiToday, workCategoryLabel, type Metrics } from "./performanceUtils";
import "./PerformanceReport.css";
import "./WeeklyReport.css";

// แท็บ "สรุปประจำสัปดาห์" (หัวหน้า) — รายงานหน้าเดียวที่ส่งผู้บริหารทุกวันศุกร์: ข้อความของหัวหน้า + ตัวเลขทีมเทียบสัปดาห์ก่อน/ค่าปกติ
// + งานรายคน (ไม่มีคะแนน/อันดับ) + สิ่งที่ต้องติดตาม + แผนสัปดาห์หน้า; พิมพ์/PDF และคัดลอกเป็นข้อความสำหรับแชท/อีเมล

type Totals = {
  executions: number; passed: number; failed: number; blocked: number; defectsReported: number; defectsCriticalHigh: number; defectsRejected: number;
  testCasesCreated: number; testCaseRevisions: number; crmTicketsHandled: number; crmClosed: number; crmHandedToDev: number; crmAnswers: number;
  automationRuns: number; workLogs: number; highlights: number;
};
type WorkItem = { workDate: string; kind: string; category: string; title: string; description?: string | null; projectName?: string | null };
type Member = { userId: string; displayName: string; position?: string | null; employmentStatus: string; probationDay?: number | null; probationTotalDays: number; current: Metrics; previous: Metrics; workItems: WorkItem[] };
type EvaluationDue = { evaluationId: string; userId: string; userName?: string | null; type: string; round: string; year: number; dueDate: string; status: string; isOverdue: boolean };
type Probation = { userId: string; displayName: string; employmentStatus: string; probationDay: number; probationTotalDays: number; probationEndDate?: string | null };
type Anomaly = { date: string; kind: string; count: number; typicalDaily: number; inReportWeek: boolean };
type Narrative = { health?: Health | null; headline?: string | null; keyRisk?: string | null; ask?: string | null; nextWeekPlan?: string | null; memberNotes: Record<string, string>; updatedAt: string; updatedByName?: string | null };
type WeeklyReport = {
  weekStart: string; weekEnd: string; isCurrentWeek: boolean; generatedAt: string;
  current: Totals; previous: Totals; typical: Totals; typicalFrom: string; typicalTo: string;
  assignedPending: number; assignedOverdue: number;
  members: Member[]; probation: Probation[]; evaluationsDue: EvaluationDue[]; anomalies: Anomaly[]; crmDataSince?: string | null; narrative?: Narrative | null;
};
type Health = "Green" | "Yellow" | "Red";
type Draft = { health: Health | ""; headline: string; keyRisk: string; ask: string; nextWeekPlan: string; memberNotes: Record<string, string> };

const healthInfo: Record<Health, { label: string; icon: string; tone: string; emoji: string }> = {
  Green: { label: "ปกติ", icon: "check_circle", tone: "green", emoji: "🟢" },
  Yellow: { label: "ต้องจับตา", icon: "warning", tone: "yellow", emoji: "🟡" },
  Red: { label: "มีปัญหา", icon: "error", tone: "red", emoji: "🔴" },
};
const anomalyKind: Record<string, string> = { Execution: "Execution", Defect: "Defect", TestCase: "Test Case" };
const limits = { short: 500, long: 2000, member: 300 };

function addDays(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

function mondayOf(date: string): string {
  const day = new Date(`${date}T00:00:00Z`).getUTCDay();
  return addDays(date, -((day + 6) % 7));
}

const num = (value: number) => value.toLocaleString("th-TH", { maximumFractionDigits: 1 });
const rate = (passed: number, total: number) => (total ? `${Math.round((passed / total) * 100)}%` : "-");

function toDraft(n?: Narrative | null): Draft {
  return { health: n?.health ?? "", headline: n?.headline ?? "", keyRisk: n?.keyRisk ?? "", ask: n?.ask ?? "", nextWeekPlan: n?.nextWeekPlan ?? "", memberNotes: { ...(n?.memberNotes ?? {}) } };
}

function hasActivity(m: Member): boolean {
  const c = m.current;
  return m.workItems.length > 0 || c.executions + c.defectsReported + c.testCasesCreated + c.testCaseRevisions + c.requirementsCreated + c.crmTicketsHandled + c.crmAnswers + c.automationRuns + c.regressionActions > 0;
}

/** สรุปงานรายคนเป็นข้อความสั้น — ใช้ทั้งในการ์ดรายคนและข้อความที่คัดลอก (เฉพาะหมวดที่มีงาน) */
function workParts(m: Metrics): { icon: string; text: string }[] {
  const parts: { icon: string; text: string }[] = [];
  if (m.executions) parts.push({ icon: "play_circle", text: `รันเทส ${num(m.executions)} (Pass ${rate(m.passed, m.executions)})` });
  if (m.defectsReported) parts.push({ icon: "bug_report", text: `แจ้ง Defect ${num(m.defectsReported)}${m.defectsCritical + m.defectsHigh ? ` (Critical/High ${m.defectsCritical + m.defectsHigh})` : ""}` });
  if (m.hasCrmAccount && (m.crmTicketsHandled || m.crmAnswers)) parts.push({ icon: "support_agent", text: `CRM ดูแล ${num(m.crmTicketsHandled)} · ปิด ${num(m.crmClosed)} · ส่ง Dev ${num(m.crmHandedToDev)}` });
  if (m.testCasesCreated || m.testCaseRevisions) parts.push({ icon: "fact_check", text: `Test Case สร้าง ${num(m.testCasesCreated)} / แก้ ${num(m.testCaseRevisions)}` });
  if (m.requirementsCreated) parts.push({ icon: "description", text: `Requirement ${num(m.requirementsCreated)}` });
  if (m.automationRuns || m.automationCasesCreated) parts.push({ icon: "smart_toy", text: `Automation ${num(m.automationCasesCreated + m.automationRuns)}` });
  return parts;
}

export function WeeklyReportView({ refreshKey }: { refreshKey: number }) {
  const thisWeek = mondayOf(thaiToday());
  const [week, setWeek] = useState(thisWeek);
  const [data, setData] = useState<WeeklyReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState<Draft>(() => toDraft(null));
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState("");
  const [showIdle, setShowIdle] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError("");
    apiFetch<WeeklyReport>(`/performance/weekly?weekStart=${week}`, { signal: controller.signal })
      .then((r) => { setData(r); setDraft(toDraft(r.narrative)); setEditing(false); setSaveError(""); })
      .catch((e) => { if (!isAbortError(e)) { setData(null); setError(errorText(e, "โหลดสรุปประจำสัปดาห์ไม่สำเร็จ")); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [week, refreshKey]);

  const dirty = useMemo(() => data != null && JSON.stringify(draft) !== JSON.stringify(toDraft(data.narrative)), [draft, data]);

  const goToWeek = async (next: string) => {
    const target = mondayOf(next);
    if (target === week || target > thisWeek) return;
    if (editing && dirty && !(await confirmDialog({ title: "ทิ้งข้อความที่ยังไม่บันทึก?", message: "ข้อความที่แก้ไว้ของสัปดาห์นี้จะหายไป", confirmLabel: "ทิ้งและเปลี่ยนสัปดาห์", tone: "danger" }))) return;
    setWeek(target);
  };

  const cancelEdit = async () => {
    if (dirty && !(await confirmDialog({ title: "ยกเลิกการแก้ไข?", message: "ข้อความที่แก้ไว้จะหายไป", confirmLabel: "ยกเลิกการแก้ไข", tone: "danger" }))) return;
    setDraft(toDraft(data?.narrative));
    setSaveError("");
    setEditing(false);
  };

  const save = async () => {
    if (!data) return;
    setSaving(true);
    setSaveError("");
    try {
      const narrative = await apiFetch<Narrative>(`/performance/weekly/${data.weekStart}/note`, {
        method: "PUT",
        fallbackMessage: "บันทึกสรุปประจำสัปดาห์ไม่สำเร็จ",
        json: { health: draft.health || null, headline: draft.headline, keyRisk: draft.keyRisk, ask: draft.ask, nextWeekPlan: draft.nextWeekPlan, memberNotes: draft.memberNotes },
      });
      setData({ ...data, narrative });
      setDraft(toDraft(narrative));
      setEditing(false);
      notify("บันทึกสรุปประจำสัปดาห์แล้ว", "success");
    } catch (e) {
      setSaveError(errorText(e, "บันทึกไม่สำเร็จ"));
    } finally { setSaving(false); }
  };

  const members = data?.members ?? [];
  const idleCount = members.filter((m) => !hasActivity(m)).length;
  const shownMembers = showIdle ? members : members.filter(hasActivity);
  const narrative = data?.narrative;
  const health = (editing ? draft.health : narrative?.health) || "";
  const withoutLogs = members.filter((m) => hasActivity(m) && m.workItems.length === 0);

  return (
    <div className="perf-report perf-weekly">
      <section className="content-card perf-weekly-head">
        <div className="perf-weekly-head-top">
          <div className="perf-weekly-title">
            <span className="perf-weekly-eyebrow"><span className="material-symbols-outlined" aria-hidden="true">event_note</span>สรุปงานประจำสัปดาห์ · ทีม QA<span className="perf-weekly-audience">ส่งผู้บริหาร</span></span>
            <h2>{fmtDate(week)} – {fmtDate(addDays(week, 6))}{week === thisWeek && <span className="perf-weekly-now">สัปดาห์นี้</span>}</h2>
            <p>{data ? <>ข้อมูล ณ {formatThaiDateTime(data.generatedAt)}{narrative && <> · ข้อความแก้ล่าสุดโดย {narrative.updatedByName ?? "-"} {formatThaiDateTime(narrative.updatedAt)}</>}</> : "จันทร์ – อาทิตย์ ตามเวลาไทย"}</p>
          </div>
          <div className="perf-report-actions perf-weekly-actions perf-noprint">
            {!editing && <button className="btn primary" type="button" onClick={() => setEditing(true)} disabled={!data}><span className="material-symbols-outlined" aria-hidden="true">edit</span>เขียนสรุป</button>}
            <CopyButton text={() => (data ? summaryText(data) : "")} disabled={!data || editing} title={editing ? "บันทึกหรือยกเลิกการแก้ไขก่อน" : "คัดลอกข้อความสรุปไปวางในอีเมลหรือแชท"}>คัดลอกข้อความ</CopyButton>
            <button className="btn" type="button" onClick={() => window.print()} disabled={!data || editing} title={editing ? "บันทึกหรือยกเลิกการแก้ไขก่อน" : undefined}><span className="material-symbols-outlined" aria-hidden="true">print</span>พิมพ์ / PDF</button>
          </div>
        </div>
        <div className="perf-weekly-nav perf-noprint" role="group" aria-label="เลือกสัปดาห์">
          <div className="perf-weekly-stepper">
            <button type="button" onClick={() => goToWeek(addDays(week, -7))} disabled={loading} aria-label="สัปดาห์ก่อน" title="สัปดาห์ก่อน"><span className="material-symbols-outlined" aria-hidden="true">chevron_left</span></button>
            <label className="perf-weekly-pick"><span className="sr-only">เลือกวันที่ในสัปดาห์</span><DateField value={week} max={thisWeek} ariaLabel="เลือกวันที่ในสัปดาห์" onChange={(v) => v && goToWeek(v)} /></label>
            <button type="button" onClick={() => goToWeek(addDays(week, 7))} disabled={loading || week >= thisWeek} aria-label="สัปดาห์ถัดไป" title="สัปดาห์ถัดไป"><span className="material-symbols-outlined" aria-hidden="true">chevron_right</span></button>
          </div>
          {week !== thisWeek && <button className="btn perf-weekly-today" type="button" onClick={() => goToWeek(thisWeek)} disabled={loading}><span className="material-symbols-outlined" aria-hidden="true">today</span>กลับสัปดาห์นี้</button>}
          <span className="perf-weekly-nav-hint">เลือกวันใดก็ได้ ระบบปัดเป็นวันจันทร์ของสัปดาห์นั้น</span>
        </div>
      </section>

      {loading ? (
        <article className="card empty" role="status"><div className="spinner" /><h3>กำลังรวบรวมข้อมูลสัปดาห์นี้...</h3></article>
      ) : error ? (
        <div className="inline-alert error" role="alert"><span>{error}</span></div>
      ) : data && (
        <>
          <section className="content-card perf-weekly-exec" aria-labelledby="weekly-exec">
            <h3 id="weekly-exec" className="perf-weekly-h"><span className="material-symbols-outlined" aria-hidden="true">campaign</span>สรุปสำหรับผู้บริหาร</h3>
            {editing ? (
              <div className="form-grid perf-weekly-form">
                <fieldset className="perf-weekly-health-pick full">
                  <legend>สถานะภาพรวมของสัปดาห์</legend>
                  {(Object.keys(healthInfo) as Health[]).map((h) => (
                    <label key={h} data-health={h}>
                      <input type="radio" name="weekly-health" value={h} checked={draft.health === h} onChange={() => setDraft({ ...draft, health: h })} />
                      <span className="material-symbols-outlined" aria-hidden="true">{healthInfo[h].icon}</span>{healthInfo[h].label}
                    </label>
                  ))}
                  <label><input type="radio" name="weekly-health" value="" checked={draft.health === ""} onChange={() => setDraft({ ...draft, health: "" })} />ยังไม่ระบุ</label>
                </fieldset>
                <TextArea label="สรุปภาพรวม (1–3 บรรทัด)" value={draft.headline} max={limits.short} onChange={(headline) => setDraft({ ...draft, headline })} placeholder="เช่น ทดสอบ Release 2.4 ครบ 92% พบ Critical 1 เรื่องที่รอ Dev แก้" />
                <TextArea label="ความเสี่ยงหลัก" value={draft.keyRisk} max={limits.long} onChange={(keyRisk) => setDraft({ ...draft, keyRisk })} placeholder="เรื่องที่อาจกระทบกำหนดส่งหรือคุณภาพ" />
                <TextArea label="เรื่องที่ต้องให้ผู้บริหารตัดสินใจ / ช่วยเหลือ" value={draft.ask} max={limits.long} onChange={(ask) => setDraft({ ...draft, ask })} placeholder="ถ้าไม่มี เว้นว่างได้" full />
              </div>
            ) : (
              <div className="perf-weekly-exec-body">
                <div className={`perf-weekly-health ${health ? health.toLowerCase() : "none"}`}>
                  <span className="material-symbols-outlined" aria-hidden="true">{health ? healthInfo[health].icon : "help"}</span>
                  <div className="perf-weekly-health-label">
                    <small>สถานะภาพรวม</small>
                    <b>{health ? healthInfo[health].label : "ยังไม่ระบุ"}</b>
                  </div>
                  <p>{narrative?.headline || (health ? "ไม่มีข้อความสรุปภาพรวม" : <span className="perf-noprint">ยังไม่ได้เขียนสรุป — กด "เขียนสรุป" เพื่อเพิ่มภาพรวม ความเสี่ยง และเรื่องที่ต้องการให้ผู้บริหารช่วย</span>)}</p>
                </div>
                {(narrative?.keyRisk || narrative?.ask) && (
                  <div className="perf-weekly-callouts">
                    {narrative.keyRisk && <div className="risk"><span className="perf-weekly-callout-label"><span className="material-symbols-outlined" aria-hidden="true">report</span>ความเสี่ยงหลัก</span><p>{narrative.keyRisk}</p></div>}
                    {narrative.ask && <div className="ask"><span className="perf-weekly-callout-label"><span className="material-symbols-outlined" aria-hidden="true">front_hand</span>ขอให้ผู้บริหารช่วย</span><p>{narrative.ask}</p></div>}
                  </div>
                )}
              </div>
            )}
          </section>

          <section className="perf-weekly-kpi-block" aria-labelledby="weekly-kpis">
            <div className="perf-weekly-block-head">
              <h3 id="weekly-kpis" className="perf-weekly-h"><span className="material-symbols-outlined" aria-hidden="true">analytics</span>ภาพรวมตัวเลขทีม</h3>
              <span className="perf-weekly-legend-key"><span className="perf-weekly-key-bar" aria-hidden="true" />สัปดาห์นี้<span className="perf-weekly-key-tick" aria-hidden="true" />ค่าปกติ</span>
            </div>
            <div className="perf-weekly-kpis">
              <WeeklyKpi icon="play_circle" label="รันเทส (Execution)" value={data.current.executions} previous={data.previous.executions} typical={data.typical.executions}
                note={`Pass ${rate(data.current.passed, data.current.executions)} · Fail ${num(data.current.failed)} · Blocked ${num(data.current.blocked)}`} />
              <WeeklyKpi icon="bug_report" label="Defect ที่พบ" value={data.current.defectsReported} previous={data.previous.defectsReported} typical={data.typical.defectsReported}
                note={`Critical/High ${num(data.current.defectsCriticalHigh)} · ถูก Reject ${num(data.current.defectsRejected)}`} />
              <WeeklyKpi icon="support_agent" label="CRM Ticket ที่ดูแล" value={data.current.crmTicketsHandled} previous={data.previous.crmTicketsHandled} typical={data.typical.crmTicketsHandled}
                note={`ปิดงาน ${num(data.current.crmClosed)} · ส่งต่อ Dev ${num(data.current.crmHandedToDev)} · ตอบ ${num(data.current.crmAnswers)}`} />
              <WeeklyKpi icon="fact_check" label="Test Case ที่สร้าง" value={data.current.testCasesCreated} previous={data.previous.testCasesCreated} typical={data.typical.testCasesCreated}
                note={`แก้ไข ${num(data.current.testCaseRevisions)} ครั้ง`} />
              <WeeklyKpi icon="edit_note" label="ผลงานเด่น / บันทึกงาน" value={data.current.highlights + data.current.workLogs} previous={data.previous.highlights + data.previous.workLogs} typical={data.typical.highlights + data.typical.workLogs}
                note={`ผลงานเด่น ${num(data.current.highlights)} · บันทึกงาน ${num(data.current.workLogs)}`} />
              <div className={`perf-weekly-kpi${data.assignedOverdue ? " perf-overdue" : ""}`} data-perf-category="schedule">
                <div className="perf-weekly-kpi-head"><span className="material-symbols-outlined" aria-hidden="true">schedule</span><small>งานเลยกำหนด (ตอนนี้)</small></div>
                <div className="perf-weekly-kpi-value"><b>{num(data.assignedOverdue)}</b></div>
                <span className="perf-weekly-compare">งานค้างทั้งหมด <b>{num(data.assignedPending)}</b> เคส</span>
                <span className="perf-weekly-kpi-note">สถานะ ณ ปัจจุบัน ไม่ใช่ย้อนหลัง</span>
              </div>
            </div>
            <p className="perf-report-note">ค่าปกติ = ค่ากลาง (median) รายสัปดาห์ของ 4 สัปดาห์ก่อนหน้า ({fmtDate(data.typicalFrom)} – {fmtDate(data.typicalTo)}) ซึ่งไม่เพี้ยนตามสัปดาห์ที่มีการนำเข้าข้อมูลจำนวนมาก · ลูกศรบอกว่าสูง/ต่ำกว่าปกติเกิน 20% ไม่ได้บอกว่าดีหรือแย่ · นับเฉพาะ Project ที่คุณเป็นสมาชิก</p>
          </section>

          <DataNotes data={data} />

          <section className="content-card perf-weekly-members-block" aria-labelledby="weekly-members">
            <div className="perf-weekly-block-head">
              <h3 id="weekly-members" className="perf-weekly-h"><span className="material-symbols-outlined" aria-hidden="true">groups</span>งานรายคน<small>เรียงตามชื่อ · ไม่ใช่การจัดอันดับ</small></h3>
              {idleCount > 0 && (
                <label className="perf-weekly-idle perf-noprint"><input type="checkbox" checked={showIdle} onChange={(e) => setShowIdle(e.target.checked)} />แสดงคนที่ไม่มีกิจกรรม ({idleCount} คน)</label>
              )}
            </div>
            {shownMembers.length === 0 ? (
              <p className="perf-report-empty">ไม่มีกิจกรรมในระบบของสัปดาห์นี้</p>
            ) : (
              <ul className="perf-weekly-members">
                {shownMembers.map((m) => {
                  const parts = workParts(m.current);
                  const prev = m.previous;
                  const note = editing ? draft.memberNotes[m.userId] ?? "" : narrative?.memberNotes[m.userId] ?? "";
                  return (
                    <li key={m.userId} className="perf-weekly-member">
                      <div className="perf-weekly-member-head">
                        <span className="perf-person"><span className="perf-avatar" aria-hidden="true">{m.displayName.slice(0, 1).toUpperCase()}</span><span><b>{m.displayName}</b><small>{m.position || "-"}</small></span></span>
                        {m.employmentStatus !== "Permanent" && <Badge tone={employmentStatusTone[m.employmentStatus]}>{employmentStatusLabel[m.employmentStatus] ?? m.employmentStatus}{m.probationDay != null ? ` · วันที่ ${m.probationDay}` : ""}</Badge>}
                      </div>
                      <div className="perf-weekly-member-section">
                        <span className="perf-weekly-member-label">งานในระบบ</span>
                        {parts.length ? (
                          <ul className="perf-weekly-chips">{parts.map((x) => <li key={x.text} data-perf-category={x.icon}><span className="material-symbols-outlined" aria-hidden="true">{x.icon}</span>{x.text}</li>)}</ul>
                        ) : <span className="perf-sub">ไม่มีงานที่บันทึกในระบบ</span>}
                        <small className="perf-sub">สัปดาห์ก่อน: รันเทส {num(prev.executions)} · Defect {num(prev.defectsReported)}{prev.hasCrmAccount ? ` · CRM ${num(prev.crmTicketsHandled)}` : ""}{!m.current.hasCrmAccount ? " · ยังไม่ได้ผูกบัญชี CRM" : ""}</small>
                      </div>
                      <div className="perf-weekly-member-section">
                        <span className="perf-weekly-member-label">ผลงานเด่น / บันทึกงาน</span>
                        {m.workItems.length ? (
                          <ul className="perf-weekly-items">
                            {m.workItems.slice(0, 4).map((w, i) => (
                              <li key={i} className={w.kind === "Highlight" ? "highlight" : ""}>
                                <span>{w.kind === "Highlight" && <span className="perf-weekly-star"><span className="material-symbols-outlined" aria-hidden="true">star</span>ผลงานเด่น</span>}{w.title}</span>
                                <small>{fmtDate(w.workDate)} · {workCategoryLabel[w.category] ?? w.category}{w.projectName ? ` · ${w.projectName}` : ""}</small>
                              </li>
                            ))}
                            {m.workItems.length > 4 && <li className="more">และอีก {m.workItems.length - 4} รายการ</li>}
                          </ul>
                        ) : <span className="perf-sub">ยังไม่มีบันทึกงานสัปดาห์นี้</span>}
                      </div>
                      {editing ? (
                        <label className="perf-weekly-note-field">
                          <span className="perf-weekly-member-label">ความเห็นหัวหน้า<span className="sr-only"> ของ {m.displayName}</span></span>
                          <textarea value={note} maxLength={limits.member} rows={2} placeholder="ไม่บังคับ"
                            onChange={(e) => setDraft({ ...draft, memberNotes: { ...draft.memberNotes, [m.userId]: e.target.value } })} />
                          <small>{note.length}/{limits.member}</small>
                        </label>
                      ) : note ? (
                        <div className="perf-weekly-note"><span className="perf-weekly-member-label">ความเห็นหัวหน้า</span><p>{note}</p></div>
                      ) : null}
                    </li>
                  );
                })}
              </ul>
            )}
          </section>

          <div className="perf-weekly-bottom">
            <section className="content-card" aria-labelledby="weekly-watch">
              <h3 id="weekly-watch" className="perf-weekly-h"><span className="material-symbols-outlined" aria-hidden="true">visibility</span>สิ่งที่ต้องติดตาม</h3>
              <ul className="perf-weekly-watch">
                {data.assignedOverdue > 0 && <li className="danger"><span className="material-symbols-outlined" aria-hidden="true">schedule</span><span>งานทดสอบเลยกำหนด {num(data.assignedOverdue)} เคส (จากงานค้าง {num(data.assignedPending)} เคส)</span></li>}
                {data.evaluationsDue.map((e) => (
                  <li key={e.evaluationId} className={e.isOverdue ? "danger" : ""}>
                    <span className="material-symbols-outlined" aria-hidden="true">rate_review</span>
                    <span>ประเมิน {roundText(e)} ของ {e.userName ?? "-"} {e.isOverdue ? "เลยกำหนด" : "ครบกำหนด"} {fmtDate(e.dueDate)} <Badge tone={evalStatus[e.status]?.tone ?? "gray"}>{evalStatus[e.status]?.label ?? e.status}</Badge></span>
                  </li>
                ))}
                {data.probation.map((p) => (
                  <li key={p.userId}>
                    <span className="material-symbols-outlined" aria-hidden="true">hourglass_top</span>
                    <span className="perf-weekly-probation">
                      <span>{p.displayName} ทดลองงานวันที่ {p.probationDay} จาก {p.probationTotalDays}{p.probationEndDate ? ` · สิ้นสุด ${fmtDate(p.probationEndDate)}` : ""}</span>
                      <span className="perf-report-track" aria-hidden="true"><span style={{ width: `${Math.min(100, (p.probationDay / p.probationTotalDays) * 100)}%` }} /></span>
                    </span>
                  </li>
                ))}
                {withoutLogs.length > 0 && <li className="info"><span className="material-symbols-outlined" aria-hidden="true">edit_note</span><span>ยังไม่มีบันทึกงานสัปดาห์นี้: {withoutLogs.map((m) => m.displayName).join(", ")} — งานนอกระบบ (ประชุม ช่วยทีม ซัพพอร์ต) จะไม่ปรากฏในรายงาน</span></li>}
                {data.assignedOverdue === 0 && data.evaluationsDue.length === 0 && data.probation.length === 0 && withoutLogs.length === 0 && <li className="ok"><span className="material-symbols-outlined" aria-hidden="true">check_circle</span><span>ไม่มีรายการที่ต้องติดตาม</span></li>}
              </ul>
            </section>

            <section className="content-card" aria-labelledby="weekly-next">
              <h3 id="weekly-next" className="perf-weekly-h"><span className="material-symbols-outlined" aria-hidden="true">event_upcoming</span>แผนสัปดาห์หน้า</h3>
              {editing ? (
                <div className="form-grid perf-weekly-form">
                  <TextArea label="แผนสัปดาห์หน้า" value={draft.nextWeekPlan} max={limits.long} onChange={(nextWeekPlan) => setDraft({ ...draft, nextWeekPlan })} placeholder={"2–3 ข้อ เช่น\n- Regression Release 2.4 รอบสุดท้าย\n- ประเมินทดลองงานรอบ 60 วัน"} full hideLabel />
                </div>
              ) : narrative?.nextWeekPlan ? <p className="perf-weekly-plan">{narrative.nextWeekPlan}</p> : <p className="perf-report-empty">ยังไม่ได้ระบุ</p>}
            </section>
          </div>

          {editing && (
            <div className="perf-weekly-savebar perf-noprint">
              {saveError && <div className="inline-alert error" role="alert"><span>{saveError}</span></div>}
              <span className="perf-sub">{dirty ? "มีข้อความที่ยังไม่บันทึก" : "ยังไม่มีการเปลี่ยนแปลง"}</span>
              <button className="btn" type="button" onClick={cancelEdit} disabled={saving}>ยกเลิก</button>
              <button className="btn primary" type="button" onClick={save} disabled={saving || !dirty}>{saving ? "กำลังบันทึก..." : "บันทึกสรุป"}</button>
            </div>
          )}

          <p className="perf-report-note">ที่มาของตัวเลข: ข้อมูลที่บันทึกใน QA Hub (Execution, Defect, Test Case, Requirement, Automation) และ CRM Ticket ที่ QA Hub ดึงเก็บทุก 15 นาที · ใช้เป็นข้อมูลประกอบ ไม่ใช่คะแนนผลงาน เพราะงานแต่ละชิ้นยากไม่เท่ากัน · งานนอกระบบแสดงเฉพาะที่พนักงานบันทึกไว้ (ไม่รวมข้อสังเกตของหัวหน้า)</p>
        </>
      )}
    </div>
  );
}

function WeeklyKpi({ icon, label, value, previous, typical, note }: { icon: string; label: string; value: number; previous: number; typical: number; note: string }) {
  // ไม่ใช้สีเขียว/แดง — ตัวเลขสูงขึ้นไม่ได้แปลว่าดีหรือแย่เสมอ (เช่น Defect) จึงบอกเป็นข้อความเทียบกับค่าปกติ
  const diff = typical ? (value - typical) / typical : value ? 1 : 0;
  const trend = Math.abs(diff) < 0.2 ? { icon: "remove", text: "ใกล้เคียงปกติ" } : diff > 0 ? { icon: "arrow_upward", text: "สูงกว่าปกติ" } : { icon: "arrow_downward", text: "ต่ำกว่าปกติ" };
  // แถบ bullet: ความยาว = สัปดาห์นี้, ขีดตั้ง = ค่าปกติ — สเกลเดียวกันจากค่ามากสุดของสามค่า
  const scale = Math.max(value, previous, typical, 1);
  return (
    <div className="perf-weekly-kpi" data-perf-category={icon}>
      <div className="perf-weekly-kpi-head"><span className="material-symbols-outlined" aria-hidden="true">{icon}</span><small>{label}</small></div>
      <div className="perf-weekly-kpi-value">
        <b>{num(value)}</b>
        <span className="perf-weekly-trend"><span className="material-symbols-outlined" aria-hidden="true">{trend.icon}</span>{trend.text}</span>
      </div>
      <span className="perf-weekly-bullet" role="img" aria-label={`สัปดาห์นี้ ${num(value)} ค่าปกติ ${num(typical)}`}>
        <span className="perf-weekly-bullet-fill" style={{ width: `${(value / scale) * 100}%` }} />
        <span className="perf-weekly-bullet-tick" style={{ left: `${(typical / scale) * 100}%` }} />
      </span>
      <span className="perf-weekly-compare">สัปดาห์ก่อน <b>{num(previous)}</b> · ปกติ <b>{num(typical)}</b></span>
      <span className="perf-weekly-kpi-note">{note}</span>
    </div>
  );
}

function TextArea({ label, value, max, onChange, placeholder, full, hideLabel }: { label: string; value: string; max: number; onChange: (v: string) => void; placeholder?: string; full?: boolean; hideLabel?: boolean }) {
  return (
    <label className={full ? "full" : undefined}>
      <span className={hideLabel ? "sr-only" : undefined}>{label}</span>
      <textarea value={value} maxLength={max} rows={3} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} />
      <small>{value.length}/{max}</small>
    </label>
  );
}

/** หมายเหตุคุณภาพข้อมูล — วันที่มีการนำเข้าจำนวนมาก และช่วงที่ตัวเลข CRM ยังไม่ครบ */
function DataNotes({ data }: { data: WeeklyReport }) {
  const crmPartial = data.crmDataSince ? data.typicalFrom < data.crmDataSince : true;
  if (data.anomalies.length === 0 && !crmPartial) return null;
  return (
    <div className="perf-weekly-datanote" role="note">
      <span className="material-symbols-outlined" aria-hidden="true">info</span>
      <div>
        <b>หมายเหตุเรื่องข้อมูล</b>
        <ul>
          {data.anomalies.map((a) => (
            <li key={`${a.kind}-${a.date}`}>
              {fmtDate(a.date)} มี {anomalyKind[a.kind] ?? a.kind} {num(a.count)} รายการในวันเดียว (วันปกติราว {num(a.typicalDaily)}) — น่าจะเป็นการนำเข้าข้อมูล
              {a.inReportWeek ? " ตัวเลขสัปดาห์นี้จึงสูงกว่าความจริง ควรอธิบายในสรุป" : " ค่าปกติใช้ค่ากลางจึงไม่เพี้ยนตาม"}
            </li>
          ))}
          {crmPartial && <li>ตัวเลข CRM ครบทั้งทีมตั้งแต่ {data.crmDataSince ? fmtDate(data.crmDataSince) : "เปิดใช้การดึงข้อมูล CRM"} — สัปดาห์ก่อนหน้านั้นนับเฉพาะ Ticket ที่เปิดผ่าน QA Hub จึงเทียบกันไม่ได้</li>}
        </ul>
      </div>
    </div>
  );
}

/** ข้อความสรุปสำหรับวางในอีเมล/แชท — ใช้ข้อมูลชุดเดียวกับหน้าจอ */
function summaryText(d: WeeklyReport): string {
  const n = d.narrative;
  const lines: string[] = [`สรุปงานทีม QA สัปดาห์ ${fmtDate(d.weekStart)} – ${fmtDate(d.weekEnd)}`];
  if (n?.health) lines.push(`สถานะ: ${healthInfo[n.health].emoji} ${healthInfo[n.health].label}`);
  if (n?.headline) lines.push(`สรุป: ${n.headline}`);
  if (n?.keyRisk) lines.push(`ความเสี่ยงหลัก: ${n.keyRisk}`);
  if (n?.ask) lines.push(`ขอให้ผู้บริหารช่วย: ${n.ask}`);
  const c = d.current, p = d.previous, t = d.typical;
  lines.push("", "ตัวเลขทีม (สัปดาห์นี้ | สัปดาห์ก่อน | ปกติ)",
    `- รันเทส ${num(c.executions)} | ${num(p.executions)} | ${num(t.executions)} · Pass ${rate(c.passed, c.executions)}`,
    `- Defect ที่พบ ${num(c.defectsReported)} | ${num(p.defectsReported)} | ${num(t.defectsReported)} · Critical/High ${num(c.defectsCriticalHigh)}`,
    `- CRM Ticket ที่ดูแล ${num(c.crmTicketsHandled)} | ${num(p.crmTicketsHandled)} | ${num(t.crmTicketsHandled)} · ปิด ${num(c.crmClosed)} · ส่ง Dev ${num(c.crmHandedToDev)}`,
    `- Test Case ที่สร้าง ${num(c.testCasesCreated)} | ${num(p.testCasesCreated)} | ${num(t.testCasesCreated)}`,
    `- งานเลยกำหนด (ตอนนี้) ${num(d.assignedOverdue)} จากงานค้าง ${num(d.assignedPending)}`);
  const active = d.members.filter(hasActivity);
  if (active.length) {
    lines.push("", "งานรายคน");
    for (const m of active) {
      const highlights = m.workItems.filter((w) => w.kind === "Highlight").map((w) => w.title);
      const note = n?.memberNotes[m.userId];
      lines.push(`- ${m.displayName}: ${workParts(m.current).map((x) => x.text).join(" · ") || "ไม่มีงานในระบบ"}${highlights.length ? ` | ผลงานเด่น: ${highlights.join(", ")}` : ""}${note ? ` | ${note}` : ""}`);
    }
  }
  const watch = [
    ...d.evaluationsDue.map((e) => `ประเมิน ${roundText(e)} ของ ${e.userName ?? "-"} ${e.isOverdue ? "เลยกำหนด" : "ครบกำหนด"} ${fmtDate(e.dueDate)}`),
    ...d.probation.map((x) => `${x.displayName} ทดลองงานวันที่ ${x.probationDay}/${x.probationTotalDays}`),
  ];
  if (watch.length) lines.push("", "ติดตาม", ...watch.map((x) => `- ${x}`));
  if (n?.nextWeekPlan) lines.push("", "แผนสัปดาห์หน้า", n.nextWeekPlan);
  const imports = d.anomalies.filter((a) => a.inReportWeek);
  if (imports.length) lines.push("", `หมายเหตุ: ${imports.map((a) => `${fmtDate(a.date)} มีการบันทึก ${anomalyKind[a.kind] ?? a.kind} ${num(a.count)} รายการในวันเดียว (น่าจะเป็นการนำเข้าข้อมูล)`).join("; ")}`);
  return lines.join("\n");
}
