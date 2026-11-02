import { useEffect, useMemo, useRef, useState } from "react";
import { DeletedItemsButton } from "../components/DeletedItemsModal";
import { apiFetch, isAbortError } from "../api";
import { Badge } from "../components/Badge";
import { DateField } from "../components/DateField";
import { ModalShell } from "../components/ModalShell";
import { confirmDialog, notify } from "../components/dialogStore";
import { formatThaiDateTime } from "../dateTime";
import type { SessionUser } from "../shared/appShared";
import { MetricGroup } from "./performanceShared";
import { downloadAuthed, employmentStatusLabel as statusLabel, employmentStatusTone as statusTone, errorText, evidenceError, evidenceRules, fetchAuthedObjectUrl, fmtBytes, fmtDate, metricGroups, passRate, thaiToday, workCategoryLabel as categoryLabel, type Metrics } from "./performanceUtils";
import { ImageLightbox, type LightboxImage } from "../components/ImageLightbox";
import { EvaluationComparison, EvaluationsPanel } from "./PerformanceEvaluations";
import { ReportView } from "./PerformanceReport";
import { WeeklyReportView } from "./WeeklyReport";
import "./Performance.css";

type Profile = {
  userId: string; username: string; displayName: string; email?: string | null; isActive: boolean; roles: string[]; hasProfile: boolean;
  employeeCode?: string | null; position?: string | null; level?: string | null; supervisorUserId?: string | null; supervisorName?: string | null;
  hireDate?: string | null; employmentStatus: string; probationEndDate?: string | null; probationDay?: number | null; probationTotalDays: number; notes?: string | null;
};
type TeamMember = { profile: Profile; metrics: Metrics };
type TeamOverview = { from: string; to: string; members: TeamMember[] };
type WeeklyPoint = { weekStart: string; executions: number; defects: number; testCases: number };
type UserPerformance = { from: string; to: string; profile: Profile; metrics: Metrics; weekly: WeeklyPoint[] };
type ActivityItem = { timestamp: string; source: string; action: string; summary?: string | null; reference?: string | null };
type ActivityPage = { items: ActivityItem[]; total: number; page: number; size: number };
type WorkLogAttachment = { attachmentId: string; fileName: string; size: number; contentType: string };
type WorkLog = {
  workLogEntryId: string; userId: string; workDate: string; category: string; kind: string; title: string; description?: string | null;
  effortHours?: number | null; projectId?: string | null; projectName?: string | null; referenceUrl?: string | null; createdAt: string; createdByName?: string | null; canEdit: boolean;
  /** ไม่มีใน API รุ่นก่อนเฟส 3 */
  attachments?: WorkLogAttachment[];
};
type UserOption = { userId: string; displayName: string; canEvaluate?: boolean };
type Period = "month" | "quarter" | "last30" | "custom";

const kindLabel: Record<string, string> = { WorkLog: "บันทึกงาน", Highlight: "ผลงานเด่น", Concern: "ข้อสังเกต (หัวหน้า)" };
const sourceLabel: Record<string, string> = { Execution: "รันเทส", Defect: "Defect", TestCase: "Test Case", Requirement: "Requirement", Regression: "Regression", CRM: "CRM Ticket" };
const sourceIcon: Record<string, string> = { Execution: "play_circle", Defect: "bug_report", TestCase: "fact_check", Requirement: "description", Regression: "replay", CRM: "support_agent" };
const trendLabel = { executions: "Execution", defects: "Defect ที่แจ้ง", testCases: "Test Case ที่สร้าง" } as const;
const timelineSize = 8;
type TeamSort = "name" | "executions" | "defects" | "testCases" | "crm" | "overdue";
const teamSortLabel: Record<TeamSort, string> = { name: "ชื่อ", executions: " Execution", defects: " Defect", testCases: " Test Case", crm: " CRM Ticket", overdue: "งานเลยกำหนด" };
const teamSortValue: Record<Exclude<TeamSort, "name">, (m: Metrics) => number> = {
  executions: (m) => m.executions, defects: (m) => m.defectsReported, testCases: (m) => m.testCasesCreated, crm: (m) => m.crmTicketsHandled, overdue: (m) => m.assignedOverdue,
};

function rangeFor(period: Exclude<Period, "custom">): { from: string; to: string } {
  const today = thaiToday();
  const [y, m] = today.split("-").map(Number);
  const pad = (n: number) => String(n).padStart(2, "0");
  if (period === "month") return { from: `${y}-${pad(m)}-01`, to: today };
  if (period === "quarter") return { from: `${y}-${pad(Math.floor((m - 1) / 3) * 3 + 1)}-01`, to: today };
  const d = new Date(`${today}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() - 29);
  return { from: d.toISOString().slice(0, 10), to: today };
}

/** หมุดประเมินช่วงทดลองงาน: วันที่ 30 / 60 / 90 และรอบสรุปก่อนครบกำหนด 14 วัน */
function probationMilestones(total: number): { day: number; label: string; short: string }[] {
  return [
    { day: 30, label: "30 วัน", short: "30" },
    { day: 60, label: "60 วัน", short: "60" },
    { day: 90, label: "90 วัน", short: "90" },
    { day: Math.max(91, total - 14), label: "สรุปผล", short: "สรุป" },
  ];
}

function nextMilestone(p: Profile): { label: string; daysLeft: number } | null {
  if (p.probationDay == null) return null;
  const next = probationMilestones(p.probationTotalDays).find((x) => x.day >= p.probationDay!);
  return next ? { label: next.label, daysLeft: next.day - p.probationDay } : { label: "ครบกำหนด", daysLeft: p.probationTotalDays - p.probationDay };
}

export function PerformancePage({ user, canManage }: { user: SessionUser; canManage: boolean }) {
  const [period, setPeriod] = useState<Period>("month");
  const [range, setRange] = useState(() => rangeFor("month"));
  const [view, setView] = useState<"team" | "weekly" | "member" | "report" | "evaluations">(canManage ? "team" : "member");
  const [selectedUserId, setSelectedUserId] = useState(user.userId);
  const [refreshKey, setRefreshKey] = useState(0);

  const choosePeriod = (next: Period) => {
    setPeriod(next);
    if (next !== "custom") setRange(rangeFor(next));
  };
  const openMember = (userId: string) => { setSelectedUserId(userId); setView("member"); };
  const openReport = (userId: string) => { setSelectedUserId(userId); setView("report"); };
  const rangeInvalid = !range.from || !range.to || range.from > range.to;

  return (
    <div className="performance-page">
      <section className="content-card perf-toolbar" aria-label="มุมมองและช่วงเวลา">
        <div className="perf-tabs" role="group" aria-label="มุมมอง">
          {canManage && (
            <button type="button" data-perf-category="groups" aria-pressed={view === "team"} className={view === "team" ? "active" : ""} onClick={() => setView("team")}>
              <span className="material-symbols-outlined" aria-hidden="true">groups</span>ภาพรวมทีม
            </button>
          )}
          {canManage && (
            <button type="button" data-perf-category="event_note" aria-pressed={view === "weekly"} className={view === "weekly" ? "active" : ""} onClick={() => setView("weekly")}>
              <span className="material-symbols-outlined" aria-hidden="true">event_note</span>สรุปประจำสัปดาห์
            </button>
          )}
          <button type="button" data-perf-category="person" aria-pressed={view === "member"} className={view === "member" ? "active" : ""} onClick={() => setView("member")}>
            <span className="material-symbols-outlined" aria-hidden="true">person</span>{canManage ? "รายบุคคล" : "ผลงานของฉัน"}
          </button>
          {canManage && (
            <button type="button" data-perf-category="summarize" aria-pressed={view === "report"} className={view === "report" ? "active" : ""} onClick={() => setView("report")}>
              <span className="material-symbols-outlined" aria-hidden="true">summarize</span>สรุปผลงาน
            </button>
          )}
          <button type="button" data-perf-category="rate_review" aria-pressed={view === "evaluations"} className={view === "evaluations" ? "active" : ""} onClick={() => setView("evaluations")}>
            <span className="material-symbols-outlined" aria-hidden="true">rate_review</span>{canManage ? "การประเมิน" : "การประเมินของฉัน"}
          </button>
        </div>
        <div className="perf-toolbar-actions">
          {view !== "evaluations" && view !== "weekly" ? (
            <div className="perf-period" role="group" aria-label="ช่วงเวลา">
              <label className="perf-period-select">
                <span className="material-symbols-outlined" aria-hidden="true">date_range</span>
                <select value={period} onChange={(e) => choosePeriod(e.target.value as Period)} aria-label="ช่วงเวลา">
                  <option value="month">เดือนนี้</option>
                  <option value="quarter">ไตรมาสนี้</option>
                  <option value="last30">30 วันล่าสุด</option>
                  <option value="custom">กำหนดเอง</option>
                </select>
              </label>
              {period === "custom" ? (
                <span className="perf-period-custom">
                  <DateField value={range.from} max={range.to} ariaLabel="ตั้งแต่วันที่" onChange={(from) => setRange((r) => ({ ...r, from }))} />
                  <span aria-hidden="true">–</span>
                  <DateField value={range.to} min={range.from} ariaLabel="ถึงวันที่" onChange={(to) => setRange((r) => ({ ...r, to }))} />
                </span>
              ) : (
                <span className="perf-period-range">{fmtDate(range.from)} – {fmtDate(range.to)}</span>
              )}
            </div>
          ) : null}
          <button className="btn perf-refresh" type="button" onClick={() => setRefreshKey((k) => k + 1)} aria-label="โหลดข้อมูลใหม่" title="โหลดข้อมูลใหม่"><span className="material-symbols-outlined" aria-hidden="true">refresh</span><span className="perf-refresh-text">รีเฟรช</span></button>
        </div>
      </section>
      {view === "weekly" && canManage ? (
        <WeeklyReportView refreshKey={refreshKey} />
      ) : view === "evaluations" ? (
        <EvaluationsPanel canManage={canManage} refreshKey={refreshKey} onChanged={() => setRefreshKey((k) => k + 1)} />
      ) : rangeInvalid ? (
        <div className="inline-alert error" role="alert"><span>ช่วงวันที่ไม่ถูกต้อง — วันที่เริ่มต้องไม่หลังวันที่สิ้นสุด</span></div>
      ) : view === "team" && canManage ? (
        <TeamView range={range} refreshKey={refreshKey} onOpen={openMember} />
      ) : view === "report" && canManage ? (
        <ReportView key={selectedUserId} userId={selectedUserId} currentUserId={user.userId} range={range} refreshKey={refreshKey} onSelectUser={setSelectedUserId} />
      ) : (
        <MemberView
          key={selectedUserId}
          userId={selectedUserId}
          currentUserId={user.userId}
          canManage={canManage}
          range={range}
          refreshKey={refreshKey}
          onSelectUser={canManage ? setSelectedUserId : undefined}
          onOpenReport={canManage ? openReport : undefined}
          onChanged={() => setRefreshKey((k) => k + 1)}
        />
      )}
    </div>
  );
}

function TeamView({ range, refreshKey, onOpen }: { range: { from: string; to: string }; refreshKey: number; onOpen: (userId: string) => void }) {
  const [data, setData] = useState<TeamOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [search, setSearch] = useState("");
  const [employmentStatus, setEmploymentStatus] = useState("");
  const [sortBy, setSortBy] = useState<TeamSort>("name");

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError("");
    apiFetch<TeamOverview>(`/performance/team?from=${range.from}&to=${range.to}`, { signal: controller.signal })
      .then(setData)
      .catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลดภาพรวมทีมไม่สำเร็จ")); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [range.from, range.to, refreshKey]);

  const members = useMemo(() => data?.members ?? [], [data]);
  const query = search.trim().toLocaleLowerCase();
  const filteredMembers = members
    .filter(({ profile: p }) =>
      (!employmentStatus || p.employmentStatus === employmentStatus) &&
      (!query || [p.displayName, p.username, p.employeeCode, p.position].some((value) => value?.toLocaleLowerCase().includes(query))))
    .sort((a, b) => sortBy === "name"
      ? a.profile.displayName.localeCompare(b.profile.displayName, "th")
      : teamSortValue[sortBy](b.metrics) - teamSortValue[sortBy](a.metrics) || a.profile.displayName.localeCompare(b.profile.displayName, "th"));
  const probation = members.filter((m) => m.profile.probationDay != null);
  const totals = members.reduce(
    (acc, m) => ({
      executions: acc.executions + m.metrics.executions, passed: acc.passed + m.metrics.passed, defects: acc.defects + m.metrics.defectsReported,
      severe: acc.severe + m.metrics.defectsCritical + m.metrics.defectsHigh, testCases: acc.testCases + m.metrics.testCasesCreated,
      revisions: acc.revisions + m.metrics.testCaseRevisions, crm: acc.crm + m.metrics.crmTicketsHandled, crmToDev: acc.crmToDev + m.metrics.crmHandedToDev,
      overdue: acc.overdue + m.metrics.assignedOverdue,
    }),
    { executions: 0, passed: 0, defects: 0, severe: 0, testCases: 0, revisions: 0, crm: 0, crmToDev: 0, overdue: 0 },
  );
  // ค่าสูงสุดของแต่ละคอลัมน์ในทีม — ใช้วาดแถบเทียบสัดส่วนในตาราง (ไม่เปลี่ยนตามตัวกรอง)
  const max = {
    executions: Math.max(0, ...members.map((m) => m.metrics.executions)),
    defects: Math.max(0, ...members.map((m) => m.metrics.defectsReported)),
    testCases: Math.max(0, ...members.map((m) => m.metrics.testCasesCreated)),
    crm: Math.max(0, ...members.map((m) => m.metrics.crmTicketsHandled)),
  };
  const top = (pick: (m: Metrics) => number) => {
    const best = members.reduce<TeamMember | null>((acc, m) => (pick(m.metrics) > (acc ? pick(acc.metrics) : 0) ? m : acc), null);
    return best ? `สูงสุด ${best.profile.displayName} ${pick(best.metrics).toLocaleString("th-TH")}` : "ยังไม่มีข้อมูลในช่วงนี้";
  };
  const perHead = (value: number) => members.length ? (value / members.length).toLocaleString("th-TH", { maximumFractionDigits: 1 }) : "0";

  // สิ่งที่ต้องติดตาม: งานเลยกำหนด → ทดลองงานที่ถึงรอบประเมินใน 7 วัน/เลยกำหนด → คนที่ไม่มีกิจกรรมในช่วงนี้
  const overdueMembers = members.filter((m) => m.metrics.assignedOverdue > 0).sort((a, b) => b.metrics.assignedOverdue - a.metrics.assignedOverdue);
  // ทุกคนที่ทดลองงานอยู่ในรายการเดียว เรียงรอบที่ใกล้/เลยกำหนดก่อน — นับเป็น "เรื่องที่ต้องติดตาม" เฉพาะที่ถึงรอบใน 7 วันหรือเลยกำหนด
  const probationRows = probation
    .map((m) => ({ member: m, next: nextMilestone(m.profile) }))
    .sort((a, b) => (a.next?.daysLeft ?? 9999) - (b.next?.daysLeft ?? 9999));
  const probationDue = probationRows.filter((x) => x.next != null && x.next.daysLeft <= 7);
  const idleMembers = members.filter(({ profile: p, metrics: m }) =>
    p.employmentStatus !== "Resigned" && m.executions + m.defectsReported + m.testCasesCreated + m.testCaseRevisions + m.crmTicketsHandled + m.workLogs + m.highlights === 0);
  const attentionCount = overdueMembers.length + probationDue.length + (idleMembers.length ? 1 : 0);

  if (loading) return <article className="card empty" role="status"><div className="spinner" /><h3>กำลังโหลดภาพรวมทีม...</h3></article>;
  if (error) return <div className="inline-alert error" role="alert"><span>{error}</span></div>;

  return (
    <>
      <section className="perf-kpis" aria-label="สรุปทีม">
        <Kpi icon="play_circle" label="Execution" value={totals.executions} meta={`Pass rate ${passRate(totals)} · เฉลี่ย ${perHead(totals.executions)} / คน`} note={top((m) => m.executions)} />
        <Kpi icon="bug_report" label="Defect ที่แจ้ง" value={totals.defects} meta={`Critical/High ${totals.severe.toLocaleString("th-TH")} · เฉลี่ย ${perHead(totals.defects)} / คน`} note={top((m) => m.defectsReported)} />
        <Kpi icon="fact_check" label="Test Case ที่สร้าง" value={totals.testCases} meta={`แก้ไข ${totals.revisions.toLocaleString("th-TH")} ครั้ง · เฉลี่ย ${perHead(totals.testCases)} / คน`} note={top((m) => m.testCasesCreated)} />
        <Kpi icon="support_agent" label="CRM Ticket ที่ดูแล" value={totals.crm} meta={`ส่งต่อ Dev ${totals.crmToDev.toLocaleString("th-TH")} · ข้อมูลดึงจาก CRM อัตโนมัติ`} note={top((m) => m.crmTicketsHandled)} />
      </section>

      <section className="content-card perf-attention" aria-label="สิ่งที่ต้องติดตาม">
        <div className="perf-section-head">
          <div><h3><span className="material-symbols-outlined" aria-hidden="true">notification_important</span>สิ่งที่ต้องติดตาม</h3><p>งานเลยกำหนด ช่วงทดลองงาน (ประเมินวันที่ 30, 60, 90 และรอบสรุป) และคนที่ยังไม่มีกิจกรรม</p></div>
          <span className={`perf-count${attentionCount ? " warn" : ""}`}>{attentionCount ? `${attentionCount} เรื่อง` : "เรียบร้อย"}</span>
        </div>
        {overdueMembers.length + probationRows.length + idleMembers.length === 0 ? (
          <p className="perf-attention-clear"><span className="material-symbols-outlined" aria-hidden="true">task_alt</span>ไม่มีเรื่องที่ต้องติดตามในช่วงนี้</p>
        ) : (
          <ul className="perf-attention-list">
            {overdueMembers.map(({ profile: p, metrics: m }) => (
              <li key={`overdue-${p.userId}`} className="tone-danger">
                <span className="material-symbols-outlined" aria-hidden="true">schedule</span>
                <span><b>{p.displayName}</b><small>งานเลยกำหนด {m.assignedOverdue.toLocaleString("th-TH")} งาน · ค้างทั้งหมด {m.assignedPending.toLocaleString("th-TH")}</small></span>
                <button className="btn" type="button" onClick={() => onOpen(p.userId)} aria-label={`ดูผลงานของ ${p.displayName}`}>ดู</button>
              </li>
            ))}
            {probationRows.map(({ member: { profile: p }, next }) => (
              <li key={`probation-${p.userId}`} className={next && next.daysLeft < 0 ? "tone-danger" : next && next.daysLeft <= 7 ? "tone-warning" : "tone-info"}>
                <span className="material-symbols-outlined" aria-hidden="true">hourglass_top</span>
                <span className="perf-attention-probation">
                  <b>{p.displayName}</b>
                  <small>{!next ? "ไม่มีรอบประเมินถัดไป" : next.daysLeft < 0 ? `เลยกำหนดทดลองงาน ${-next.daysLeft} วัน — ควรสรุปผล` : `ประเมินรอบ ${next.label} ${next.daysLeft === 0 ? "วันนี้" : `ในอีก ${next.daysLeft} วัน`}`}</small>
                  <ProbationBar profile={p} compact />
                </span>
                <button className="btn" type="button" onClick={() => onOpen(p.userId)} aria-label={`ดูผลงานและการประเมินของ ${p.displayName}`}>ดู</button>
              </li>
            ))}
            {idleMembers.length > 0 && (
              <li className="tone-muted">
                <span className="material-symbols-outlined" aria-hidden="true">bedtime</span>
                <span><b>ไม่มีกิจกรรมในช่วงนี้ {idleMembers.length} คน</b><small>{idleMembers.map((m) => m.profile.displayName).join(", ")}</small></span>
              </li>
            )}
          </ul>
        )}
      </section>

      <section className="content-card perf-team-panel" aria-label="ผลงานรายคน">
        <div className="perf-section-head">
          <div><h3><span className="material-symbols-outlined" aria-hidden="true">groups</span>ผลงานรายคน</h3><p>นับเฉพาะ Project ที่คุณเป็นสมาชิก · แถบสีเทียบกับคนที่มากที่สุดในทีม</p></div><span className="perf-count" role="status">{filteredMembers.length} / {members.length} คน</span>
        </div>
        <div className="perf-team-filters">
          <label className="perf-search"><span className="material-symbols-outlined" aria-hidden="true">search</span><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="ค้นหาพนักงาน" placeholder="ค้นหาชื่อ รหัสพนักงาน หรือตำแหน่ง" /></label>
          <label className="perf-status-filter"><span className="sr-only">สถานะพนักงาน</span><select value={employmentStatus} onChange={(e) => setEmploymentStatus(e.target.value)}><option value="">ทุกสถานะพนักงาน</option>{Array.from(new Set(members.map((m) => m.profile.employmentStatus))).map((status) => <option key={status} value={status}>{statusLabel[status] ?? status}</option>)}</select></label>
          <label className="perf-status-filter"><span className="sr-only">เรียงตาม</span><select value={sortBy} onChange={(e) => setSortBy(e.target.value as TeamSort)} aria-label="เรียงตาม">{Object.entries(teamSortLabel).map(([value, label]) => <option key={value} value={value}>เรียงตาม{label}</option>)}</select></label>
          {(search || employmentStatus) && <button className="btn" type="button" onClick={() => { setSearch(""); setEmploymentStatus(""); }}>ล้างตัวกรอง</button>}
        </div>
        {members.length === 0 ? (
          <div className="empty-state"><span className="material-symbols-outlined" aria-hidden="true">group_off</span><b>ยังไม่มีสมาชิกทีม QA</b><small>ผู้ใช้ที่มี Role QA_LEAD / QA_TESTER หรือมีโปรไฟล์พนักงานจะแสดงที่นี่</small></div>
        ) : filteredMembers.length === 0 ? (
          <div className="empty-state"><span className="material-symbols-outlined" aria-hidden="true">search_off</span><b>ไม่พบพนักงานที่ตรงกับตัวกรอง</b><small>ลองเปลี่ยนคำค้นหรือเลือกสถานะอื่น</small></div>
        ) : (
          <div className="table-wrap">
            <table className="table-cards perf-team-table">
              <thead>
                <tr><th>พนักงาน</th><th>Execution</th><th>Defect</th><th>Test Case</th><th>CRM Ticket</th><th>งานค้าง / เลยกำหนด</th><th>Work Log</th><th><span className="sr-only">ดูรายละเอียด</span></th></tr>
              </thead>
              <tbody>
                {filteredMembers.map(({ profile: p, metrics: m }) => (
                  <tr key={p.userId}>
                    <td>
                      <span className="perf-person">
                        <span className="perf-avatar" aria-hidden="true">{p.displayName.slice(0, 1).toUpperCase()}</span>
                        <span><b>{p.displayName}</b>
                          <span className="perf-person-status"><Badge tone={statusTone[p.employmentStatus]}>{statusLabel[p.employmentStatus] ?? p.employmentStatus}</Badge><span>{p.position || p.roles.join(", ") || p.username}{p.probationDay != null && ` · วันที่ ${p.probationDay}/${p.probationTotalDays}`}</span></span>
                        </span>
                      </span>
                    </td>
                    <td><MetricCell category="play_circle" value={m.executions} max={max.executions} sub={m.executions ? `Pass ${passRate(m)}` : undefined} /></td>
                    <td><MetricCell category="bug_report" value={m.defectsReported} max={max.defects} sub={m.defectsCritical + m.defectsHigh > 0 ? `Critical/High ${m.defectsCritical + m.defectsHigh}` : undefined} /></td>
                    <td><MetricCell category="fact_check" value={m.testCasesCreated} max={max.testCases} sub={m.testCaseRevisions > 0 ? `แก้ไข ${m.testCaseRevisions}` : undefined} /></td>
                    <td>{m.hasCrmAccount ? <MetricCell category="support_agent" value={m.crmTicketsHandled} max={max.crm} sub={m.crmHandedToDev > 0 ? `ส่ง Dev ${m.crmHandedToDev}` : undefined} /> : <small className="perf-sub" title="ยังไม่ได้ผูกบัญชี CRM">ไม่ได้ผูก CRM</small>}</td>
                    <td>
                      <span className="perf-pending">{m.assignedPending.toLocaleString("th-TH")}</span>
                      {m.assignedOverdue > 0 ? <span className="perf-overdue-chip"><span className="material-symbols-outlined" aria-hidden="true">schedule</span>เลย {m.assignedOverdue}</span> : <small className="perf-sub">ไม่มีเลยกำหนด</small>}
                    </td>
                    <td><span className="perf-pending">{(m.workLogs + m.highlights).toLocaleString("th-TH")}</span>{m.highlights > 0 && <small className="perf-sub">ผลงานเด่น {m.highlights}</small>}</td>
                    <td className="perf-row-action"><button className="btn" type="button" onClick={() => onOpen(p.userId)} aria-label={`ดูรายละเอียดของ ${p.displayName}`}>ดูผลงาน<span className="material-symbols-outlined" aria-hidden="true">arrow_forward</span></button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}

function Kpi({ icon, label, value, meta, note }: { icon: string; label: string; value: number; meta: string; note: string }) {
  return (
    <div className="perf-kpi" data-perf-category={icon}>
      <div className="perf-kpi-head"><span className="material-symbols-outlined" aria-hidden="true">{icon}</span><small>{label}</small></div>
      <b>{value.toLocaleString("th-TH")}</b>
      <span className="perf-kpi-meta">{meta}</span>
      <span className="perf-kpi-note"><span className="material-symbols-outlined" aria-hidden="true">military_tech</span>{note}</span>
    </div>
  );
}

/** ตัวเลขในตารางทีม + แถบบางเทียบกับค่าสูงสุดของทีม (ตัวเลขยังเป็นข้อมูลหลัก แถบเป็นตัวช่วยสแกน) */
function MetricCell({ category, value, max, sub }: { category: string; value: number; max: number; sub?: string }) {
  return (
    <span className="perf-metric-cell" data-perf-category={category}>
      <b>{value.toLocaleString("th-TH")}</b>
      <span className="perf-metric-bar" aria-hidden="true"><span style={{ width: `${max ? Math.max(value ? 4 : 0, (value / max) * 100) : 0}%` }} /></span>
      {sub && <small>{sub}</small>}
    </span>
  );
}

function ProbationBar({ profile, compact = false }: { profile: Profile; compact?: boolean }) {
  if (profile.probationDay == null) return null;
  const total = profile.probationTotalDays;
  const pct = Math.min(100, (profile.probationDay / total) * 100);
  return (
    <div className={`perf-probation-bar${compact ? " compact" : ""}`}>
      <div className="perf-bar-track" role="progressbar" aria-valuemin={1} aria-valuemax={total} aria-valuenow={Math.min(profile.probationDay, total)} aria-label={`ทดลองงานวันที่ ${profile.probationDay} จาก ${total}`}>
        <div className="perf-bar-fill" style={{ width: `${pct}%` }} />
        {probationMilestones(total).map((x) => (
          <span key={x.label} className={`perf-bar-pin${profile.probationDay! >= x.day ? " passed" : ""}`} style={{ left: `${(x.day / total) * 100}%` }} title={`${x.label} (วันที่ ${x.day})`} />
        ))}
      </div>
      {!compact && (
        <div className="perf-bar-labels">
          {probationMilestones(total).map((x) => <span key={x.label} style={{ left: `${(x.day / total) * 100}%` }}>{x.short}</span>)}
        </div>
      )}
      <small className="perf-bar-caption">วันที่ {profile.probationDay} จาก {total} · สิ้นสุด {fmtDate(profile.probationEndDate)}</small>
    </div>
  );
}

function MemberView({ userId, currentUserId, canManage, range, refreshKey, onSelectUser, onOpenReport, onChanged }: {
  userId: string; currentUserId: string; canManage: boolean; range: { from: string; to: string }; refreshKey: number;
  onSelectUser?: (userId: string) => void; onOpenReport?: (userId: string) => void; onChanged: () => void;
}) {
  const [data, setData] = useState<UserPerformance | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [users, setUsers] = useState<UserOption[]>([]);
  const [profileOpen, setProfileOpen] = useState(false);

  useEffect(() => {
    if (!canManage) return;
    const controller = new AbortController();
    apiFetch<UserOption[]>("/performance/users", { signal: controller.signal }).then(setUsers).catch(() => setUsers([]));
    return () => controller.abort();
  }, [canManage]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError("");
    apiFetch<UserPerformance>(`/performance/users/${userId}/summary?from=${range.from}&to=${range.to}`, { signal: controller.signal })
      .then(setData)
      .catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลดผลงานไม่สำเร็จ")); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [userId, range.from, range.to, refreshKey]);

  const workLogs = useWorkLogs(userId, range, refreshKey);
  const p = data?.profile;
  const m = data?.metrics;
  const highlights = workLogs.items.filter((x) => x.kind === "Highlight");
  const tenure = tenureText(p?.hireDate);

  return (
    <>
      {loading && !data ? (
        <article className="card empty" role="status"><div className="spinner" /><h3>กำลังโหลดผลงาน...</h3></article>
      ) : error ? (
        <div className="inline-alert error" role="alert"><span>{error}</span></div>
      ) : p && m && data ? (
        <>
          <section className="content-card perf-hero" aria-label={`โปรไฟล์ผลงานของ ${p.displayName}`}>
            <div className="perf-hero-cover">
              {onSelectUser && users.length > 0 ? (
                <label className="perf-hero-picker">
                  <span className="material-symbols-outlined" aria-hidden="true">person_search</span>
                  <select value={userId} onChange={(e) => onSelectUser(e.target.value)} aria-label="เลือกพนักงาน">
                    {!users.some((u) => u.userId === userId) && <option value={userId}>{p.displayName}</option>}
                    {users.map((u) => <option key={u.userId} value={u.userId}>{u.displayName}{u.userId === currentUserId ? " (ฉัน)" : ""}</option>)}
                  </select>
                </label>
              ) : <span />}
              {canManage && (
                <div className="perf-hero-actions">
                  <button className="btn" type="button" onClick={() => setProfileOpen(true)}>
                    <span className="material-symbols-outlined" aria-hidden="true">edit</span>{p.hasProfile ? "แก้ไขข้อมูล" : "ตั้งค่าข้อมูลพนักงาน"}
                  </button>
                  {onOpenReport && (
                    <button className="btn" type="button" onClick={() => onOpenReport(userId)}>
                      <span className="material-symbols-outlined" aria-hidden="true">summarize</span>สรุปผลงาน / แชร์ HR
                    </button>
                  )}
                </div>
              )}
            </div>
            <div className="perf-hero-body">
              <span className="perf-hero-avatar" aria-hidden="true">{p.displayName.slice(0, 1).toUpperCase()}</span>
              <div className="perf-hero-id">
                <h2>{p.displayName}</h2>
                <p>{[p.position, p.level].filter(Boolean).join(" · ") || p.roles.join(", ") || p.username}</p>
                <ul className="perf-hero-tags">
                  <li><Badge tone={statusTone[p.employmentStatus]}>{statusLabel[p.employmentStatus] ?? p.employmentStatus}</Badge></li>
                  {p.employeeCode && <li><span className="material-symbols-outlined" aria-hidden="true">badge</span>{p.employeeCode}</li>}
                  <li><span className="material-symbols-outlined" aria-hidden="true">event</span>เริ่มงาน {fmtDate(p.hireDate)}{tenure && <> · {tenure}</>}</li>
                  <li><span className="material-symbols-outlined" aria-hidden="true">supervisor_account</span>หัวหน้า {p.supervisorName ?? "-"}</li>
                </ul>
              </div>
            </div>
            {p.probationDay != null && <div className="perf-hero-probation"><span className="perf-hero-probation-label"><span className="material-symbols-outlined" aria-hidden="true">hourglass_top</span>ช่วงทดลองงาน</span><ProbationBar profile={p} /></div>}
            {!p.hasProfile && canManage && <p className="perf-hint perf-hero-hint">ยังไม่ได้ตั้งค่าวันเริ่มงานและสถานะ — ตั้งค่าเพื่อติดตามช่วงทดลองงาน</p>}
          </section>

          {(highlights.length > 0 || workLogs.error) && <section className="content-card perf-showcase" aria-labelledby="perf-showcase-title">
            <div className="perf-section-head">
              <div><h3 id="perf-showcase-title"><span className="material-symbols-outlined" aria-hidden="true">workspace_premium</span>ผลงานเด่น</h3><p>เพิ่มได้จาก "บันทึกงาน" โดยเลือกประเภทผลงานเด่น</p></div>
              {highlights.length > 0 && <span className="perf-count">{highlights.length} ผลงาน</span>}
            </div>
            {workLogs.error ? <div className="inline-alert error" role="alert"><span>{workLogs.error}</span></div>
              : workLogs.loading ? <div className="perf-feedback" role="status">กำลังโหลด...</div>
                : highlights.length === 0 ? (
                  <div className="perf-showcase-empty"><span className="material-symbols-outlined" aria-hidden="true">auto_awesome</span><span><b>ยังไม่มีผลงานเด่นในช่วงนี้</b><small>บันทึกงานที่ภูมิใจ เช่น ปิด Regression ก่อนกำหนด พบ Defect สำคัญ หรือช่วยทีมแก้ปัญหาลูกค้า</small></span></div>
                ) : (
                  <ul className="perf-showcase-grid">
                    {highlights.map((x) => (
                      <li key={x.workLogEntryId} className="perf-showcase-card">
                        <span className="perf-showcase-meta"><span className="material-symbols-outlined" aria-hidden="true">star</span>{fmtDate(x.workDate)} · {categoryLabel[x.category] ?? x.category}</span>
                        <b>{x.title}</b>
                        {x.description && <p>{x.description}</p>}
                        {(x.projectName || x.effortHours != null || x.attachments?.length || x.referenceUrl) && <span className="perf-showcase-foot">
                          {x.projectName && <span><span className="material-symbols-outlined" aria-hidden="true">folder</span>{x.projectName}</span>}
                          {x.effortHours != null && <span><span className="material-symbols-outlined" aria-hidden="true">schedule</span>{x.effortHours} ชม.</span>}
                          {x.attachments?.length ? <span><span className="material-symbols-outlined" aria-hidden="true">attach_file</span>หลักฐาน {x.attachments.length} ไฟล์</span> : null}
                          {x.referenceUrl && <a href={x.referenceUrl} target="_blank" rel="noopener noreferrer"><span className="material-symbols-outlined" aria-hidden="true">open_in_new</span>ลิงก์อ้างอิง</a>}
                        </span>}
                      </li>
                    ))}
                  </ul>
                )}
          </section>}

          <section className="perf-skills" aria-labelledby="perf-skills-title">
            <div className="perf-skills-head">
              <h3 id="perf-skills-title"><span className="material-symbols-outlined" aria-hidden="true">insights</span>ผลงานตามด้าน</h3>
              <p>นับเฉพาะ Project ที่คุณเป็นสมาชิก · ใช้เป็นหลักฐานประกอบการประเมิน ไม่ใช่คะแนน</p>
            </div>
            <div className="perf-metric-groups">
              {metricGroups(m).map((g) => <MetricGroup key={g.icon} icon={g.icon} title={g.title} items={g.items} />)}
            </div>
            <p className="perf-hint">งาน CRM ดึงจาก CRM อัตโนมัติ และต้องผูกบัญชี CRM ก่อนจึงจะนับให้พนักงานได้</p>
          </section>

          <WeeklyTrend points={data.weekly} />

          <div className="perf-two-col">
            <WorkLogSection userId={userId} isSelf={userId === currentUserId} canManage={canManage} items={workLogs.items} loading={workLogs.loading} error={workLogs.error} onChanged={onChanged} />
            <Timeline userId={userId} range={range} refreshKey={refreshKey} />
          </div>

          <EvaluationsPanel canManage={canManage} userId={userId} refreshKey={refreshKey} onChanged={onChanged} compact />
          <EvaluationComparison userId={userId} refreshKey={refreshKey} />

          {profileOpen && <ProfileModal profile={p} users={users} onClose={() => setProfileOpen(false)} onSaved={() => { setProfileOpen(false); onChanged(); }} />}
        </>
      ) : null}
    </>
  );
}

/** อายุงานจากวันเริ่มงานถึงวันนี้ (เวลาไทย) เช่น "อายุงาน 1 ปี 3 เดือน" */
function tenureText(hireDate?: string | null): string {
  if (!hireDate) return "";
  const [hy, hm, hd] = hireDate.slice(0, 10).split("-").map(Number);
  const [ty, tm, td] = thaiToday().split("-").map(Number);
  const months = (ty - hy) * 12 + (tm - hm) - (td < hd ? 1 : 0);
  if (months < 0) return "";
  if (months === 0) return "อายุงานไม่ถึง 1 เดือน";
  const y = Math.floor(months / 12), mo = months % 12;
  return `อายุงาน ${y ? `${y} ปี` : ""}${y && mo ? " " : ""}${mo ? `${mo} เดือน` : ""}`;
}

function WeeklyTrend({ points }: { points: WeeklyPoint[] }) {
  const [metric, setMetric] = useState<keyof typeof trendLabel>("executions");
  const [showTable, setShowTable] = useState(false);
  const max = Math.max(1, ...points.map((x) => x[metric]));
  if (points.length === 0) return null;
  return (
    <section className="content-card perf-trend" aria-label="แนวโน้มรายสัปดาห์">
      <div className="perf-section-head">
        <div><h3>แนวโน้มรายสัปดาห์: {trendLabel[metric]}</h3><p>สัปดาห์เริ่มวันจันทร์</p></div>
        <div className="perf-trend-controls">
          <select value={metric} onChange={(e) => setMetric(e.target.value as keyof typeof trendLabel)} aria-label="เลือกข้อมูลที่แสดงในกราฟ">
            {Object.entries(trendLabel).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
          <button className="btn" type="button" onClick={() => setShowTable((v) => !v)} aria-pressed={showTable}>
            <span className="material-symbols-outlined" aria-hidden="true">{showTable ? "bar_chart" : "table"}</span>{showTable ? "กราฟ" : "ตาราง"}
          </button>
        </div>
      </div>
      {showTable ? (
        <div className="table-wrap">
          <table className="perf-trend-table">
            <thead><tr><th>สัปดาห์</th><th>Execution</th><th>Defect ที่แจ้ง</th><th>Test Case ที่สร้าง</th></tr></thead>
            <tbody>{points.map((x) => <tr key={x.weekStart}><td>{fmtDate(x.weekStart)}</td><td className="num">{x.executions}</td><td className="num">{x.defects}</td><td className="num">{x.testCases}</td></tr>)}</tbody>
          </table>
        </div>
      ) : (
        <div className="perf-bars" role="img" aria-label={`${trendLabel[metric]} รายสัปดาห์ สูงสุด ${max}`}>
          {points.map((x) => (
            <div key={x.weekStart} className="perf-bar-col" tabIndex={0} aria-label={`สัปดาห์ ${fmtDate(x.weekStart)}: ${x[metric]}`}>
              <span className="perf-bar-tip">{fmtDate(x.weekStart)}<b>{x[metric].toLocaleString("th-TH")}</b></span>
              <div className="perf-bar-area"><div className="perf-bar" style={{ height: `${(x[metric] / max) * 100}%` }} /></div>
              <small>{fmtDate(x.weekStart).replace(/ \d{4}$/, "")}</small>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}

function Timeline({ userId, range, refreshKey }: { userId: string; range: { from: string; to: string }; refreshKey: number }) {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<ActivityPage | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => { setPage(1); }, [userId, range.from, range.to]);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError("");
    apiFetch<ActivityPage>(`/performance/users/${userId}/timeline?from=${range.from}&to=${range.to}&page=${page}&size=${timelineSize}`, { signal: controller.signal })
      .then(setData)
      .catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลดกิจกรรมไม่สำเร็จ")); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [userId, range.from, range.to, page, refreshKey]);

  const pageCount = Math.max(1, Math.ceil((data?.total ?? 0) / timelineSize));
  return (
    <section className="content-card perf-timeline" aria-label="กิจกรรมในระบบ">
      <div className="perf-section-head"><div><h3>กิจกรรมในระบบ</h3><p>{data ? `${data.total.toLocaleString("th-TH")} รายการ · ล่าสุดก่อน` : " "}</p></div></div>
      {error ? <div className="inline-alert error" role="alert"><span>{error}</span></div>
        : loading && !data ? <div className="perf-feedback" role="status">กำลังโหลด...</div>
          : !data || data.items.length === 0 ? <div className="empty-state"><span className="material-symbols-outlined" aria-hidden="true">history</span><b>ไม่มีกิจกรรมในช่วงนี้</b></div>
            : (
              <>
                <ol className="perf-timeline-list">
                  {data.items.map((x, i) => (
                    <li key={`${x.timestamp}-${i}`}>
                      <span className="material-symbols-outlined perf-timeline-icon" aria-hidden="true">{sourceIcon[x.source] ?? "radio_button_checked"}</span>
                      <div>
                        <p><b>{sourceLabel[x.source] ?? x.source}</b> · {x.action}{x.reference && <span className="perf-ref">{x.reference}</span>}</p>
                        {x.summary && <small className="perf-timeline-summary">{x.summary}</small>}
                        <time dateTime={x.timestamp}>{formatThaiDateTime(x.timestamp, { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" })} น.</time>
                      </div>
                    </li>
                  ))}
                </ol>
                {pageCount > 1 && (
                  <div className="perf-pager">
                    <span>หน้า {page} จาก {pageCount}</span>
                    <div>
                      <button className="btn" type="button" disabled={page <= 1 || loading} onClick={() => setPage((v) => v - 1)} aria-label="หน้าก่อนหน้า"><span className="material-symbols-outlined" aria-hidden="true">chevron_left</span></button>
                      <button className="btn" type="button" disabled={page >= pageCount || loading} onClick={() => setPage((v) => v + 1)} aria-label="หน้าถัดไป"><span className="material-symbols-outlined" aria-hidden="true">chevron_right</span></button>
                    </div>
                  </div>
                )}
              </>
            )}
    </section>
  );
}

function evidenceIcon(contentType: string): string {
  return contentType === "application/pdf" ? "picture_as_pdf" : "image";
}

/** รายการไฟล์หลักฐาน — กดเพื่อดาวน์โหลด (ไฟล์ต้องใช้ token จึงโหลดผ่าน fetch); onRemove = แสดงปุ่มลบ */
const isImage = (item: WorkLogAttachment) => item.contentType.startsWith("image/");
const isPdf = (item: WorkLogAttachment) => item.contentType === "application/pdf";

/** รายการไฟล์หลักฐาน — รูปเปิดใน ImageLightbox (เลื่อนดูรูปอื่นในบันทึกเดียวกันได้), PDF เปิดดูในหน้าต่างภายในหน้า, ปุ่มดาวน์โหลดแยก */
function EvidenceList({ workLogId, items, onRemove, disabled = false }: { workLogId: string; items: WorkLogAttachment[]; onRemove?: (item: WorkLogAttachment) => void; disabled?: boolean }) {
  const [busyId, setBusyId] = useState<string | null>(null);
  const [lightbox, setLightbox] = useState<{ images: LightboxImage[]; index: number } | null>(null);
  const [pdf, setPdf] = useState<{ url: string; item: WorkLogAttachment } | null>(null);
  const objectUrls = useRef<string[]>([]);
  const path = (item: WorkLogAttachment) => `/performance/work-logs/${workLogId}/attachments/${item.attachmentId}`;
  const release = () => { objectUrls.current.forEach((u) => URL.revokeObjectURL(u)); objectUrls.current = []; };
  useEffect(() => release, []);

  const download = async (item: WorkLogAttachment) => {
    setBusyId(item.attachmentId);
    try {
      await downloadAuthed(path(item), item.fileName);
    } catch (e) {
      notify(errorText(e, "ดาวน์โหลดไฟล์ไม่สำเร็จ"), "error");
    } finally {
      setBusyId(null);
    }
  };
  const preview = async (item: WorkLogAttachment) => {
    if (!isImage(item) && !isPdf(item)) { await download(item); return; }
    setBusyId(item.attachmentId);
    try {
      release();
      if (isImage(item)) {
        const images = items.filter(isImage);
        const urls = await Promise.all(images.map((x) => fetchAuthedObjectUrl(path(x))));
        objectUrls.current = urls;
        setLightbox({ images: images.map((x, i) => ({ src: urls[i], name: x.fileName })), index: images.indexOf(item) });
      } else {
        const url = await fetchAuthedObjectUrl(path(item));
        objectUrls.current = [url];
        setPdf({ url, item });
      }
    } catch (e) {
      release();
      notify(errorText(e, "เปิดไฟล์ไม่สำเร็จ"), "error");
    } finally {
      setBusyId(null);
    }
  };
  const close = () => { setLightbox(null); setPdf(null); release(); };
  return (
    <>
    <ul className="perf-evidence-list" aria-label="ไฟล์หลักฐาน">
      {items.map((item) => (
        <li key={item.attachmentId}>
          <button type="button" className="perf-evidence-file" disabled={busyId === item.attachmentId} onClick={() => preview(item)} aria-label={`${isImage(item) || isPdf(item) ? "ดูไฟล์" : "ดาวน์โหลด"} ${item.fileName} (${fmtBytes(item.size)})`}>
            <span className="material-symbols-outlined" aria-hidden="true">{busyId === item.attachmentId ? "hourglass_top" : evidenceIcon(item.contentType)}</span>
            <span className="perf-evidence-name">{item.fileName}</span>
            <small>{fmtBytes(item.size)}</small>
          </button>
          {(isImage(item) || isPdf(item)) && (
            <button type="button" className="perf-evidence-remove perf-evidence-download" disabled={busyId === item.attachmentId} onClick={() => download(item)} aria-label={`ดาวน์โหลด ${item.fileName}`} title="ดาวน์โหลด">
              <span className="material-symbols-outlined" aria-hidden="true">download</span>
            </button>
          )}
          {onRemove && (
            <button type="button" className="perf-evidence-remove" disabled={disabled} onClick={() => onRemove(item)} aria-label={`ลบไฟล์ ${item.fileName}`}>
              <span className="material-symbols-outlined" aria-hidden="true">delete</span>
            </button>
          )}
        </li>
      ))}
    </ul>
      {lightbox && <ImageLightbox images={lightbox.images} index={lightbox.index} onIndexChange={(index) => setLightbox((v) => v ? { ...v, index } : v)} onClose={close} />}
      {pdf && (
        <ModalShell labelledBy="perf-pdf-title" className="perf-modal perf-eval-modal perf-pdf-modal" onDismiss={close}>
          <div className="modal-head">
            <div><h2 id="perf-pdf-title">{pdf.item.fileName}</h2><small>{fmtBytes(pdf.item.size)}</small></div>
            <button type="button" aria-label="ปิด" onClick={close}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
          </div>
          <iframe className="perf-pdf-frame" src={pdf.url} title={`ตัวอย่าง ${pdf.item.fileName}`} />
          <div className="modal-actions">
            <button className="btn" type="button" onClick={close}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิด</button>
            <a className="btn" href={pdf.url} target="_blank" rel="noopener noreferrer"><span className="material-symbols-outlined" aria-hidden="true">open_in_new</span> เปิดในแท็บใหม่</a>
            <button className="btn primary" type="button" onClick={() => download(pdf.item)}><span className="material-symbols-outlined" aria-hidden="true">download</span> ดาวน์โหลด</button>
          </div>
        </ModalShell>
      )}
    </>
  );
}

type WorkLogForm = { workDate: string; category: string; kind: string; title: string; description: string; effortHours: string; referenceUrl: string };

/** บันทึกงานของช่วงที่เลือก — โหลดครั้งเดียวที่ MemberView แล้วใช้ทั้งส่วนผลงานเด่นและรายการบันทึกงาน */
function useWorkLogs(userId: string, range: { from: string; to: string }, refreshKey: number) {
  const [items, setItems] = useState<WorkLog[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError("");
    apiFetch<WorkLog[]>(`/performance/users/${userId}/work-logs?from=${range.from}&to=${range.to}`, { signal: controller.signal })
      .then(setItems)
      .catch((e) => { if (!isAbortError(e)) setError(errorText(e, "โหลดบันทึกงานไม่สำเร็จ")); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [userId, range.from, range.to, refreshKey]);
  return { items, loading, error };
}

function WorkLogSection({ userId, isSelf, canManage, items, loading, error, onChanged }: {
  userId: string; isSelf: boolean; canManage: boolean; items: WorkLog[]; loading: boolean; error: string; onChanged: () => void;
}) {
  const [editing, setEditing] = useState<WorkLog | "new" | null>(null);

  const remove = async (item: WorkLog) => {
    if (!await confirmDialog({ title: "ลบบันทึกงาน", message: `ลบ "${item.title}"? บันทึกจะไม่แสดงและไม่นับในการประเมิน (ผู้ดูแลระบบกู้คืนได้)`, confirmLabel: "ลบ", tone: "danger" })) return;
    try {
      await apiFetch(`/performance/work-logs/${item.workLogEntryId}`, { method: "DELETE", fallbackMessage: "ลบบันทึกงานไม่สำเร็จ" });
      notify("ลบบันทึกงานแล้ว", "success");
      onChanged();
    } catch (e) {
      notify(errorText(e, "ลบบันทึกงานไม่สำเร็จ"), "error");
    }
  };

  return (
    <section className="content-card perf-worklog" aria-label="บันทึกงานและผลงานเด่น">
      <div className="perf-section-head">
        <div><h3>บันทึกงานและผลงานเด่น</h3><p>งานนอกระบบ เช่น ซัพพอร์ต อบรม เอกสาร หรือผลงานที่ควรถูกจดจำ</p></div>
        <DeletedItemsButton type="work-logs" label="บันทึกงาน" onRestored={onChanged} />
        {(isSelf || canManage) && (
          <button className="btn primary" type="button" onClick={() => setEditing("new")}><span className="material-symbols-outlined" aria-hidden="true">add</span>เพิ่มบันทึก</button>
        )}
      </div>
      {error ? <div className="inline-alert error" role="alert"><span>{error}</span></div>
        : loading ? <div className="perf-feedback" role="status">กำลังโหลด...</div>
          : items.length === 0 ? <div className="empty-state"><span className="material-symbols-outlined" aria-hidden="true">edit_note</span><b>ยังไม่มีบันทึกในช่วงนี้</b></div>
            : (
              <ul className="perf-worklog-list">
                {items.map((x) => (
                  <li key={x.workLogEntryId} className={`perf-worklog-item kind-${x.kind.toLowerCase()}`}>
                    <div className="perf-worklog-head">
                      <Badge tone={x.kind === "Highlight" ? "green" : x.kind === "Concern" ? "red" : "blue"}>{kindLabel[x.kind] ?? x.kind}</Badge>
                      <span className="perf-worklog-meta">{fmtDate(x.workDate)} · {categoryLabel[x.category] ?? x.category}{x.effortHours != null && ` · ${x.effortHours} ชม.`}{x.projectName && ` · ${x.projectName}`}</span>
                    </div>
                    <b>{x.title}</b>
                    {x.description && <p>{x.description}</p>}
                    {x.attachments?.length ? <EvidenceList workLogId={x.workLogEntryId} items={x.attachments} /> : null}
                    <div className="perf-worklog-foot">
                      <small>บันทึกโดย {x.createdByName ?? "-"}</small>
                      {x.referenceUrl && <a href={x.referenceUrl} target="_blank" rel="noopener noreferrer">ลิงก์อ้างอิง<span className="material-symbols-outlined" aria-hidden="true">open_in_new</span></a>}
                      {x.canEdit && (
                        <span className="perf-worklog-actions">
                          <button type="button" onClick={() => setEditing(x)} aria-label={`แก้ไข ${x.title}`}><span className="material-symbols-outlined" aria-hidden="true">edit</span></button>
                          <button type="button" onClick={() => remove(x)} aria-label={`ลบ ${x.title}`}><span className="material-symbols-outlined" aria-hidden="true">delete</span></button>
                        </span>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
            )}
      {editing && (
        <WorkLogModal
          userId={userId}
          canManage={canManage}
          item={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); onChanged(); }}
        />
      )}
    </section>
  );
}

function WorkLogModal({ userId, canManage, item, onClose, onSaved }: { userId: string; canManage: boolean; item: WorkLog | null; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState<WorkLogForm>(() => ({
    workDate: item?.workDate?.slice(0, 10) ?? thaiToday(),
    category: item?.category ?? "Testing",
    kind: item?.kind ?? "WorkLog",
    title: item?.title ?? "",
    description: item?.description ?? "",
    effortHours: item?.effortHours != null ? String(item.effortHours) : "",
    referenceUrl: item?.referenceUrl ?? "",
  }));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [attachments, setAttachments] = useState<WorkLogAttachment[]>(item?.attachments ?? []);
  const [pending, setPending] = useState<File[]>([]);
  const [fileError, setFileError] = useState("");
  const set = (patch: Partial<WorkLogForm>) => setForm((f) => ({ ...f, ...patch }));
  const existingSizes = attachments.map((a) => a.size);

  const pickFiles = (list: FileList | null) => {
    const files = [...(list ?? [])];
    if (files.length === 0) return;
    const next = [...pending, ...files];
    const problem = evidenceError(next, existingSizes);
    setFileError(problem ?? "");
    if (!problem) setPending(next);
  };

  // ลบไฟล์ที่บันทึกแล้วมีผลทันที (ไม่รอปุ่มบันทึก) จึงต้องยืนยันก่อน
  const removeSaved = async (file: WorkLogAttachment) => {
    if (!item || !await confirmDialog({ title: "ลบไฟล์หลักฐาน", message: `ลบ "${file.fileName}"? การลบย้อนกลับไม่ได้`, confirmLabel: "ลบ", tone: "danger" })) return;
    setSaving(true);
    try {
      await apiFetch(`/performance/work-logs/${item.workLogEntryId}/attachments/${file.attachmentId}`, { method: "DELETE", fallbackMessage: "ลบไฟล์ไม่สำเร็จ" });
      setAttachments((list) => list.filter((a) => a.attachmentId !== file.attachmentId));
      notify("ลบไฟล์หลักฐานแล้ว", "success");
    } catch (e) {
      setFileError(errorText(e, "ลบไฟล์ไม่สำเร็จ"));
    } finally {
      setSaving(false);
    }
  };
  const hours = form.effortHours.trim() === "" ? null : Number(form.effortHours);
  const hoursInvalid = hours != null && (Number.isNaN(hours) || hours < 0 || hours > 24);
  const urlInvalid = form.referenceUrl.trim() !== "" && !/^https?:\/\//i.test(form.referenceUrl.trim());
  const valid = form.title.trim() !== "" && form.workDate !== "" && !hoursInvalid && !urlInvalid;

  const save = async () => {
    setSaving(true);
    setError("");
    const body = { userId, workDate: form.workDate, category: form.category, kind: form.kind, title: form.title.trim(), description: form.description.trim() || null, effortHours: hours, projectId: item?.projectId ?? null, referenceUrl: form.referenceUrl.trim() || null };
    try {
      const saved = await apiFetch<WorkLog>(item ? `/performance/work-logs/${item.workLogEntryId}` : "/performance/work-logs", { method: item ? "PUT" : "POST", json: body, fallbackMessage: "บันทึกไม่สำเร็จ" });
      if (pending.length > 0) {
        const files = new FormData();
        pending.forEach((f) => files.append("files", f));
        try {
          await apiFetch(`/performance/work-logs/${saved.workLogEntryId}/attachments`, { method: "POST", form: files, fallbackMessage: "แนบไฟล์ไม่สำเร็จ" });
        } catch (e) {
          // บันทึกงานถูกบันทึกแล้ว — แจ้งให้แนบไฟล์ใหม่จากการแก้ไข แทนการค้าง modal ไว้กับบันทึกที่สร้างไปแล้ว
          notify(`บันทึกงานแล้ว แต่แนบไฟล์ไม่สำเร็จ: ${errorText(e, "ลองแนบใหม่จากปุ่มแก้ไข")}`, "error");
          onSaved();
          return;
        }
      }
      notify(item ? "แก้ไขบันทึกงานแล้ว" : "เพิ่มบันทึกงานแล้ว", "success");
      onSaved();
    } catch (e) {
      setError(errorText(e, "บันทึกไม่สำเร็จ"));
    } finally {
      setSaving(false);
    }
  };

  return (
    <ModalShell labelledBy="worklog-title" className="perf-modal" onDismiss={() => { if (!saving) onClose(); }}>
      <div className="modal-head">
        <div><h2 id="worklog-title">{item ? "แก้ไขบันทึกงาน" : "เพิ่มบันทึกงาน"}</h2><small>งานที่ไม่อยู่ในระบบ หรือผลงานที่อยากให้หัวหน้าเห็น</small></div>
        <button type="button" aria-label="ปิด" disabled={saving} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>
      <div className="form-grid">
        <label>วันที่<span className="required-mark"> *</span><DateField value={form.workDate} onChange={(workDate) => set({ workDate })} /></label>
        <label>ประเภท
          <select value={form.kind} disabled={!!item} onChange={(e) => set({ kind: e.target.value })}>
            <option value="WorkLog">{kindLabel.WorkLog}</option>
            <option value="Highlight">{kindLabel.Highlight}</option>
            {canManage && <option value="Concern">{kindLabel.Concern}</option>}
          </select>
        </label>
        <label>หมวดงาน
          <select value={form.category} onChange={(e) => set({ category: e.target.value })}>
            {Object.entries(categoryLabel).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </label>
        <label>ชั่วโมงที่ใช้
          <input type="number" min={0} max={24} step={0.5} inputMode="decimal" value={form.effortHours} onChange={(e) => set({ effortHours: e.target.value })} placeholder="ไม่ระบุ" aria-invalid={hoursInvalid} />
          {hoursInvalid && <small className="field-error">ระบุ 0–24 ชั่วโมง</small>}
        </label>
        <label className="full">หัวข้อ<span className="required-mark"> *</span><input value={form.title} maxLength={200} onChange={(e) => set({ title: e.target.value })} placeholder="เช่น ช่วยทีม Support ตรวจสอบปัญหาลูกค้า" /></label>
        <label className="full">รายละเอียด<textarea value={form.description} maxLength={4000} rows={4} onChange={(e) => set({ description: e.target.value })} /></label>
        <label className="full">ลิงก์อ้างอิง
          <input type="url" value={form.referenceUrl} maxLength={500} onChange={(e) => set({ referenceUrl: e.target.value })} placeholder="https://..." aria-invalid={urlInvalid} />
          {urlInvalid && <small className="field-error">ต้องขึ้นต้นด้วย http:// หรือ https://</small>}
        </label>
        <div className="full perf-evidence-field">
          <span className="perf-evidence-label">ไฟล์หลักฐาน <small>({attachments.length + pending.length}/{evidenceRules.maxFiles})</small></span>
          {item && attachments.length > 0 && <EvidenceList workLogId={item.workLogEntryId} items={attachments} onRemove={removeSaved} disabled={saving} />}
          {pending.length > 0 && (
            <ul className="perf-evidence-list" aria-label="ไฟล์ที่จะแนบ">
              {pending.map((f, i) => (
                <li key={`${f.name}-${i}`}>
                  <span className="perf-evidence-file">
                    <span className="material-symbols-outlined" aria-hidden="true">{/\.pdf$/i.test(f.name) ? "picture_as_pdf" : "image"}</span>
                    <span className="perf-evidence-name">{f.name}</span>
                    <small>{fmtBytes(f.size)} · รอบันทึก</small>
                  </span>
                  <button type="button" className="perf-evidence-remove" disabled={saving} onClick={() => { setPending((list) => list.filter((_, idx) => idx !== i)); setFileError(""); }} aria-label={`ไม่แนบ ${f.name}`}>
                    <span className="material-symbols-outlined" aria-hidden="true">close</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
          {attachments.length + pending.length < evidenceRules.maxFiles && (
            <label className="btn perf-evidence-pick">
              <span className="material-symbols-outlined" aria-hidden="true">attach_file</span>เลือกไฟล์
              <input type="file" multiple accept={evidenceRules.accept} disabled={saving} onChange={(e) => { pickFiles(e.target.files); e.target.value = ""; }} />
            </label>
          )}
          <small className="perf-hint">เช่น ใบประกาศอบรม, ภาพหน้าจอผลงาน — PNG/JPG/WebP/PDF ไฟล์ละไม่เกิน 5 MB รวมไม่เกิน 20 MB · ไฟล์ใหม่อัปโหลดเมื่อกดบันทึก</small>
          {fileError && <small className="field-error" role="alert">{fileError}</small>}
        </div>
        {form.kind === "Concern" && <p className="full perf-hint">ข้อสังเกตจะเห็นได้เฉพาะหัวหน้า และไม่นับรวมเป็นผลงาน</p>}
      </div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      <div className="modal-actions">
        <button className="btn" type="button" disabled={saving} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button>
        <button className="btn primary" type="button" disabled={saving || !valid} onClick={save}>
          {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}
        </button>
      </div>
    </ModalShell>
  );
}

function ProfileModal({ profile, users, onClose, onSaved }: { profile: Profile; users: UserOption[]; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({
    employeeCode: profile.employeeCode ?? "",
    position: profile.position ?? "",
    level: profile.level ?? "",
    supervisorUserId: profile.supervisorUserId ?? "",
    hireDate: profile.hireDate?.slice(0, 10) ?? "",
    employmentStatus: profile.hasProfile ? profile.employmentStatus : "Probation",
    probationEndDate: profile.probationEndDate?.slice(0, 10) ?? "",
    notes: profile.notes ?? "",
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const set = (patch: Partial<typeof form>) => setForm((f) => ({ ...f, ...patch }));
  const onProbation = form.employmentStatus === "Probation" || form.employmentStatus === "Extended";
  const valid = !(onProbation && !form.hireDate) && !(form.hireDate && form.probationEndDate && form.probationEndDate < form.hireDate);

  const save = async () => {
    setSaving(true);
    setError("");
    try {
      await apiFetch(`/performance/users/${profile.userId}/profile`, {
        method: "PUT",
        fallbackMessage: "บันทึกข้อมูลพนักงานไม่สำเร็จ",
        json: {
          employeeCode: form.employeeCode || null, position: form.position || null, level: form.level || null,
          supervisorUserId: form.supervisorUserId || null, hireDate: form.hireDate || null, employmentStatus: form.employmentStatus,
          probationEndDate: onProbation ? form.probationEndDate || null : null, notes: form.notes || null,
        },
      });
      notify("บันทึกข้อมูลพนักงานแล้ว", "success");
      onSaved();
    } catch (e) {
      setError(errorText(e, "บันทึกข้อมูลพนักงานไม่สำเร็จ"));
    } finally {
      setSaving(false);
    }
  };

  return (
    <ModalShell labelledBy="profile-title" className="perf-modal" onDismiss={() => { if (!saving) onClose(); }}>
      <div className="modal-head">
        <div><h2 id="profile-title">ข้อมูลพนักงาน</h2><small>{profile.displayName}</small></div>
        <button type="button" aria-label="ปิด" disabled={saving} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
      </div>
      <div className="form-grid">
        <label>สถานะ<span className="required-mark"> *</span>
          <select value={form.employmentStatus} onChange={(e) => set({ employmentStatus: e.target.value })}>
            {Object.entries(statusLabel).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </label>
        <label>วันเริ่มงาน{onProbation && <span className="required-mark"> *</span>}<DateField value={form.hireDate} allowEmpty onChange={(hireDate) => set({ hireDate })} invalid={onProbation && !form.hireDate} /></label>
        {onProbation && (
          <label className="full">วันสิ้นสุดทดลองงาน
            <DateField value={form.probationEndDate} min={form.hireDate || undefined} allowEmpty onChange={(probationEndDate) => set({ probationEndDate })} />
            <small className="perf-hint">เว้นว่าง = ครบ 119 วันนับจากวันเริ่มงาน</small>
          </label>
        )}
        <label>ตำแหน่ง<input value={form.position} maxLength={100} onChange={(e) => set({ position: e.target.value })} placeholder="เช่น QA Engineer" /></label>
        <label>ระดับ
          <select value={form.level} onChange={(e) => set({ level: e.target.value })}>
            <option value="">ไม่ระบุ</option>
            {["Junior", "Mid", "Senior", "Lead"].map((x) => <option key={x} value={x}>{x}</option>)}
          </select>
        </label>
        <label>รหัสพนักงาน<input value={form.employeeCode} maxLength={50} onChange={(e) => set({ employeeCode: e.target.value })} /></label>
        <label>หัวหน้า (ผู้ประเมิน)
          <select value={form.supervisorUserId} onChange={(e) => set({ supervisorUserId: e.target.value })}>
            <option value="">ไม่ระบุ — ประเมินไม่ได้จนกว่าจะตั้ง</option>
            {users.filter((u) => u.userId !== profile.userId && (u.canEvaluate || u.userId === form.supervisorUserId)).map((u) => <option key={u.userId} value={u.userId}>{u.displayName}{u.canEvaluate ? "" : " (ไม่มีสิทธิ์ประเมิน)"}</option>)}
          </select>
          <small>เฉพาะหัวหน้าที่ระบุเท่านั้นที่ให้คะแนน/ส่งผล/เปิดผลใหม่ได้ · เลือกได้เฉพาะผู้มีสิทธิ์ PERFORMANCE.EDIT</small>
        </label>
        <label className="full">หมายเหตุ<textarea value={form.notes} maxLength={2000} rows={3} onChange={(e) => set({ notes: e.target.value })} /></label>
      </div>
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      <div className="modal-actions">
        <button className="btn" type="button" disabled={saving} onClick={onClose}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button>
        <button className="btn primary" type="button" disabled={saving || !valid} onClick={save}>
          {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}
        </button>
      </div>
    </ModalShell>
  );
}
