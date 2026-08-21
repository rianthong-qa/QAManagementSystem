import { Fragment as _F, useCallback, useEffect, useMemo, useState, type ReactElement } from "react";
import "./App.css";
import "./styles.css";
import "./DragDrop.css";
import "./ReleaseBuild.css";
import "./TestManagement.css";
import "./Dashboard.css";
import "./DashboardExecutive.css";
import "./Rtm.css";
import "./Regression.css";

type Page =
  | "dashboard"
  | "projects"
  | "releases"
  | "requirements"
  | "rtm"
  | "test-cases"
  | "test-suites"
  | "test-cycles"
  | "execution"
  | "defects"
  | "regression"
  | "summary"
  | "risks"
  | "signoff"
  | "users"
  | "settings"
  | "system-monitor"
  | "audit";
type SessionUser = {
  userId: string;
  username: string;
  displayName: string;
  roles: string[];
  permissions: string[];
  assignedProjectIds: string[];
};
type DashboardSummary = {
  totalRequirements: number; coveredRequirements: number; requirementCoverage: number;
  totalCases: number; executedCases: number; executionProgress: number; passedCases: number; passRate: number;
  openP0: number; openP1: number; overallScore?: number; totalDefects: number; openDefects: number; criticalDefects: number; highDefects: number; defectQuality: number; recommendedDecision: string; generatedAt: string;
  modules: { moduleId: string; parentModuleId?: string; moduleCode?: string; moduleName: string; sortOrder?: number; requirements: number; coveredRequirements: number; testCases: number; executed: number; passed: number; failed: number; blocked: number; coveragePercent: number; executionPercent: number; passRate: number; health: string }[];
  users: { userId: string; displayName: string; executions: number; passed: number; failed: number; blocked: number; passRate: number; lastExecutedAt?: string }[];
  statusDistribution: { status: string; count: number; color: string }[];
  defectSeverityDistribution: { severity: string; count: number; color: string }[];
};
const apiUrl = import.meta.env.VITE_API_URL ?? "/api/v1";

function moduleTreeComparator(a: ModuleItem, b: ModuleItem): number {
  return (a.sortOrder ?? 999) - (b.sortOrder ?? 999) || (a.moduleCode ?? "").localeCompare(b.moduleCode ?? "");
}
type ModuleTreeEntry = { module: ModuleItem; depth: number };
function buildModuleTree(modules: ModuleItem[]): ModuleTreeEntry[] {
  const rows: ModuleTreeEntry[] = [];
  const visited = new Set<string>();
  const appendChildren = (parentId: string | undefined, depth: number) => {
    modules
      .filter((x) => (x.parentModuleId || undefined) === parentId && !visited.has(x.moduleId))
      .sort(moduleTreeComparator)
      .forEach((m) => {
        visited.add(m.moduleId);
        rows.push({ module: m, depth });
        appendChildren(m.moduleId, depth + 1);
      });
  };
  appendChildren(undefined, 0);
  modules.forEach((m) => { if (!visited.has(m.moduleId)) { visited.add(m.moduleId); rows.push({ module: m, depth: 0 }); } });
  return rows;
}
function renderModuleSelectOptions(modules: ModuleItem[]): ReactElement[] {
  return buildModuleTree(modules).map(({ module, depth }) => (
    <option key={module.moduleId} value={module.moduleId} className={depth === 0 ? "module-root-option" : "module-child-option"}>
      {depth ? `${"　".repeat(depth)}└ ` : "▾ "}{module.moduleCode ? `${module.moduleCode} · ` : ""}{module.moduleName}
    </option>
  ));
}

type MasterOption = { masterOptionId: string; category: string; value: string; displayName: string; sortOrder: number; isActive: boolean };
function useMasterOptions() {
  const [options, setOptions] = useState<MasterOption[]>([]);
  useEffect(() => {
    fetch(`${apiUrl}/master-settings`, { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` } })
      .then((r) => r.ok ? r.json() : [])
      .then((data: MasterOption[]) => setOptions(Array.isArray(data) ? data : []));
  }, []);
  return (category: string) => options.filter((x) => x.category === category && x.isActive);
}
function masterOptionElements(options: MasterOption[], current: string) {
  return <>{current && !options.some((x) => x.value === current) && <option value={current}>{current} (ปิดใช้งาน)</option>}{options.map((x) => <option key={x.masterOptionId} value={x.value}>{x.displayName}</option>)}</>;
}

// Global fetch wrapper: redirect to login on 401 Unauthorized
if (typeof window !== "undefined") {
  const __origFetch = window.fetch.bind(window);
  window.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    const resp = await __origFetch(input, init);
    if (resp.status === 401) {
      try { localStorage.removeItem("qa.accessToken"); localStorage.removeItem("qa.user"); } catch {}
      // determine request url (string)
      let reqUrl = "";
      try {
        if (typeof input === "string") reqUrl = input;
        else if (input instanceof Request) reqUrl = input.url;
        else reqUrl = String(input);
      } catch {}
      // Don't redirect when the failing request is the login call itself
      if (reqUrl.includes("/auth/login")) return resp;
      const isLoginPath = window.location.pathname === "/" || window.location.pathname.startsWith("/login");
      if (!isLoginPath) {
        // add a query flag so login page can show a message if desired
        window.location.href = "/?sessionExpired=1";
      }
    }
    return resp;
  };
}

function isTokenExpiredLocal(): boolean {
  try {
    const token = localStorage.getItem("qa.accessToken");
    if (!token) return true;
    const parts = token.split(".");
    if (parts.length < 2) return true;
    const payload = JSON.parse(atob(parts[1]));
    if (!payload || !payload.exp) return true;
    return Date.now() / 1000 >= payload.exp;
  } catch {
    return true;
  }
}

async function copyText(text: string) {
  if (window.isSecureContext && navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // Continue with the HTTP-compatible fallback below.
    }
  }
  const textarea = document.createElement("textarea");
  textarea.value = text;
  textarea.setAttribute("readonly", "");
  textarea.style.position = "fixed";
  textarea.style.opacity = "0";
  document.body.appendChild(textarea);
  textarea.select();
  let copied = false;
  try { copied = document.execCommand("copy"); } finally { document.body.removeChild(textarea); }
  return copied;
}

function nextBusinessCode(prefix: string, existingCodes: string[]) {
  const normalized = prefix.trim().toUpperCase();
  const marker = `${normalized}-`;
  const next =
    Math.max(
      0,
      ...existingCodes
        .filter((code) => code.toUpperCase().startsWith(marker))
        .map((code) => Number.parseInt(code.slice(marker.length), 10) || 0),
    ) + 1;
  return `${normalized}-${String(next).padStart(3, "0")}`;
}

function contextualCode(projectCode: string, moduleCode: string, kind: string) {
  const project = projectCode.toUpperCase();
  const module = moduleCode.toUpperCase().startsWith(`${project}-`)
    ? moduleCode.slice(projectCode.length + 1)
    : moduleCode;
  return `${projectCode}-${module}-${kind}`;
}

const nav: {
  label: string;
  items: { id: Page; icon: string; label: string }[];
}[] = [
  {
    label: "ภาพรวม",
    items: [
      { id: "dashboard", icon: "▦", label: "Dashboard" },
      { id: "projects", icon: "P", label: "Project / Module" },
      { id: "releases", icon: "◫", label: "Release / Build" },
    ],
  },
  {
    label: "REQUIREMENT & TEST DESIGN",
    items: [
      { id: "requirements", icon: "R", label: "Requirement" },
      { id: "rtm", icon: "⇄", label: "RTM" },
      { id: "test-cases", icon: "TC", label: "Test Case" },
      { id: "test-suites", icon: "▤", label: "Test Suite" },
    ],
  },
  {
    label: "TEST EXECUTION",
    items: [
      { id: "test-cycles", icon: "◎", label: "Test Cycle" },
      { id: "execution", icon: "▶", label: "Execution Workspace" },
      { id: "defects", icon: "!", label: "Defect" },
      { id: "regression", icon: "↻", label: "Regression" },
    ],
  },
  {
    label: "RELEASE GOVERNANCE",
    items: [
      { id: "summary", icon: "Σ", label: "Test Summary" },
      { id: "risks", icon: "⚠", label: "Risk Acceptance" },
      { id: "signoff", icon: "✓", label: "Release Sign-off" },
    ],
  },
  {
    label: "ADMINISTRATION",
    items: [
      { id: "users", icon: "U", label: "User / Role" },
      { id: "settings", icon: "⚙", label: "Setting Center" },
      { id: "system-monitor", icon: "M", label: "System Monitor" },
      { id: "audit", icon: "⌕", label: "Audit Log" },
    ],
  },
];

const pageNames: Record<Page, string> = Object.fromEntries(
  nav.flatMap((g) => g.items.map((i) => [i.id, i.label])),
) as Record<Page, string>;
const pageIds = new Set<Page>(Object.keys(pageNames) as Page[]);
function restoredActivePage(): Page {
  const hashPage = window.location.hash.match(/^#\/([^/?#]+)/)?.[1];
  if (hashPage && pageIds.has(hashPage as Page)) return hashPage as Page;
  const savedPage = localStorage.getItem("qa.activePage");
  return savedPage && pageIds.has(savedPage as Page) ? savedPage as Page : "dashboard";
}
const viewPermission: Record<Page, string> = {
  dashboard: "PROJECT.VIEW",
  projects: "PROJECT.VIEW",
  releases: "PROJECT.VIEW",
  requirements: "REQUIREMENT.VIEW",
  rtm: "REQUIREMENT.VIEW",
  "test-cases": "TESTCASE.VIEW",
  "test-suites": "TESTCASE.VIEW",
  "test-cycles": "EXECUTION.RUN",
  execution: "EXECUTION.RUN",
  defects: "DEFECT.EDIT",
  regression: "REGRESSION.VIEW",
  summary: "REPORT.EXPORT",
  risks: "RISK.APPROVE",
  signoff: "RELEASE.SIGNOFF",
  users: "ADMIN.USER",
  settings: "ADMIN.USER",
  "system-monitor": "SYSTEM.MONITOR",
  audit: "ADMIN.PERMISSION",
};
const editPermission: Partial<Record<Page, string>> = {
  requirements: "REQUIREMENT.EDIT",
  users: "ADMIN.USER",
};

const releases = [
  [
    "REL-2026.08",
    "10.0.228",
    "Major",
    "28 ส.ค. 2026",
    "Testing",
    "สมชาย ใจดี",
    "Conditional",
  ],
  [
    "REL-2026.09",
    "10.0.240",
    "Minor",
    "25 ก.ย. 2026",
    "Planning",
    "วิภา แสงทอง",
    "Pending",
  ],
  [
    "HOTFIX-226",
    "10.0.226.1",
    "Hotfix",
    "12 ส.ค. 2026",
    "Ready",
    "สมชาย ใจดี",
    "Go",
  ],
];
const requirements = [
  [
    "REQ-SALE-142",
    "รองรับส่วนลดหลายระดับ",
    "Sales / POS",
    "P0",
    "100%",
    "Passed",
    "Ready",
  ],
  [
    "REQ-STK-088",
    "ปรับยอดสต็อกแบบ Real-time",
    "Stock",
    "P1",
    "80%",
    "Failed",
    "Testing",
  ],
  [
    "REQ-RPT-071",
    "ส่งออกรายงาน PDF/Excel",
    "Report",
    "P1",
    "50%",
    "Blocked",
    "Testing",
  ],
  [
    "REQ-UPD-031",
    "Auto update ผ่าน Velopack",
    "Update",
    "P0",
    "100%",
    "Passed",
    "Ready",
  ],
];
const defects = [
  [
    "DEF-1042",
    "ยอดคงเหลือไม่อัปเดตหลัง Void",
    "Stock",
    "P1",
    "Open",
    "10.0.228 RC2",
    "กิตติ",
  ],
  [
    "DEF-1038",
    "PDF ภาษาไทยตัดคำผิด",
    "Report",
    "P1",
    "Ready for Retest",
    "10.0.227 RC1",
    "ณัฐพล",
  ],
  [
    "DEF-1021",
    "Token หมดอายุเร็วกว่ากำหนด",
    "Authentication",
    "P2",
    "Resolved",
    "10.0.226",
    "กิตติ",
  ],
];

function Badge({
  children,
  tone = "blue",
}: {
  children: React.ReactNode;
  tone?: string;
}) {
  const effectiveTone = children === "No Data" ? "blue" : tone;
  return <span className={`badge ${effectiveTone}`}>{children}</span>;
}

type DefectItem = { defectId:string; defectCode:string; title:string; severity:string; status:string; createdAt:string; projectId?:string; releaseId?:string|null; buildId?:string|null; moduleId?:string|null; description?:string|null; stepsToReproduce?:string|null; expectedResult?:string|null; actualResult?:string|null; assigneeUserId?:string|null; updatedAt?:string|null; createdByName?:string|null; updatedByName?:string|null; releaseCode?:string|null; buildNumber?:string|null; assigneeName?:string|null };
type DefectActivityItem = { activityId: string; actionType: string; message: string; actorUserId?: string | null; actorName?: string | null; createdAt: string; performedByUserId?: string | null; performedAt?: string };
type DefectTestCaseItem = { testCaseId: string; testCaseCode: string; title: string; priority?: string; status?: string; linkedAt?: string };
const defectSeverities = ["Critical", "High", "Medium", "Low"];
const defectStatuses = ["Open", "In Progress", "Resolved", "Closed", "Rejected"];
const defectSeverityTones: Record<string, string> = { Critical: "red", High: "yellow", Medium: "blue", Low: "green" };
const defectStatusTones: Record<string, string> = { Open: "yellow", "In Progress": "blue", Resolved: "green", Closed: "green", Rejected: "gray" };
const testCaseStatusTones: Record<string, string> = { Draft: "gray", Review: "yellow", Ready: "green", Deprecated: "red" };
const defectActionLabels: Record<string, string> = { Created: "สร้าง", Updated: "แก้ไข", StatusChanged: "สถานะ", SeverityChanged: "Severity", Comment: "คอมเมนต์", TestLinked: "เชื่อมโยง Test Case", TestUnlinked: "ยกเลิก Test Case", BulkUpdated: "อัปเดตกลุ่ม", Deleted: "ลบ" };

function QualityOverviewCharts({ data }: { data: DashboardSummary }) {
  const statusDist = data.statusDistribution || [];
  const totalStatus = Math.max(1, statusDist.reduce((s, x) => s + x.count, 0));

  const sevDist = data.defectSeverityDistribution || [];
  const sevOrder = ["Critical","High","Medium","Low"];
  const sevColor: Record<string, string> = { Critical: "#dc2626", High: "#f59e0b", Medium: "#2563eb", Low: "#94a3b8" };
  const sevCounts = sevOrder.map(s => { const found = sevDist.find(x => x.severity === s); return { sev: s, count: found?.count ?? 0, color: sevColor[s] }; });
  const totalDefects = sevCounts.reduce((s, x) => s + x.count, 0);
  const maxSev = Math.max(1, ...sevCounts.map(x => x.count));

  // Build conic gradient for donut
  let angle = 0;
  const donutSegments = statusDist.filter(x => x.count > 0).map(x => {
    const start = angle;
    const pct = x.count / totalStatus * 100;
    angle += pct / 100 * 360;
    return `${x.color} ${start}deg ${angle}deg`;
  }).join(", ") || "#e2e8f0 0deg 360deg";

  return <div className="charts-grid">
    <article className="card chart-card">
      <div className="chart-card-head">
        <h3>Test Execution Status</h3>
        <span>{totalStatus.toLocaleString()} Total Cases</span>
      </div>
      <div className="chart-donut-wrap">
        <div className="chart-donut" style={{background:`conic-gradient(${donutSegments})`}}>
          <div className="chart-donut-hole">
            <b>{data.passRate}%</b>
            <span>Pass Rate</span>
          </div>
        </div>
        <div className="chart-donut-legend">
          {statusDist.map(x => <div key={x.status} className="legend-item">
            <i style={{background:x.color}} />
            <span className="legend-label">{x.status}</span>
            <span className="legend-count">{x.count}</span>
            <span className="legend-pct">{Math.round(x.count / totalStatus * 100)}%</span>
          </div>)}
        </div>
      </div>
    </article>
    <article className="card chart-card">
      <div className="chart-card-head">
        <h3>Defects by Severity</h3>
        <span>{totalDefects.toLocaleString()} Total</span>
      </div>
      <div className="chart-bars">
        {sevCounts.map(x => <div key={x.sev} className="bar-row">
          <div className="bar-label">{x.sev}</div>
          <div className="bar-track">
            <div className="bar-fill" style={{width:`${Math.max(x.count / maxSev * 100, x.count > 0 ? 8 : 0)}%`,background:x.color}}>
              {x.count > 0 && <span>{x.count}</span>}
            </div>
          </div>
        </div>)}
      </div>
      {totalDefects === 0 && <p className="chart-empty">ยังไม่มีข้อมูล Defect</p>}
    </article>
  </div>;
}

type TimelineRelease = { releaseId: string; releaseCode: string; version: string; plannedReleaseDate?: string; actualReleaseDate?: string; status: string };
type TimelineCycle = { testCycleId: string; cycleCode: string; cycleName: string; startDate?: string; endDate?: string; status: string; progressPercent: number };

const TH_OFFSET = 7;
function nowTH(): Date { const u = new Date(); return new Date(u.getTime() + (u.getTimezoneOffset() + TH_OFFSET * 60) * 60000); }
function startOfDayTH(d: Date): Date { return new Date(d.getFullYear(), d.getMonth(), d.getDate()); }
function parseDateTH(s: string): Date { const d = new Date(s); return new Date(d.getTime() + (d.getTimezoneOffset() + TH_OFFSET * 60) * 60000); }
function isWeekday(d: Date): boolean { const day = d.getDay(); return day >= 1 && day <= 5; }
function addDays(d: Date, n: number): Date { const r = new Date(d); r.setDate(r.getDate() + n); return r; }
function sameDay(a: Date, b: Date): boolean { return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate(); }
function fmtShort(d: Date): string { return `${d.getDate()}/${d.getMonth() + 1}`; }
function fmtAgo(iso?: string | null): string {
  if (!iso) return "-";
  const ms = Date.now() - new Date(iso).getTime();
  if (ms < 60_000) return "เมื่อสักครู่";
  const m = Math.floor(ms / 60_000);
  if (m < 60) return `${m} นาทีที่แล้ว`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h} ชม.ที่แล้ว`;
  const d = Math.floor(h / 24);
  if (d < 30) return `${d} วันที่แล้ว`;
  const mo = Math.floor(d / 30);
  if (mo < 12) return `${mo} เดือนที่แล้ว`;
  return `${Math.floor(mo / 12)} ปีที่แล้ว`;
}
function defectAgeDays(createdAt: string): number {
  return Math.max(0, Math.floor((Date.now() - new Date(createdAt).getTime()) / 86_400_000));
}

function ExecutiveTimeline({ projectId, releaseId, buildId, shareCode, shareToken }: { projectId?: string; releaseId?: string; buildId?: string; shareCode?: string; shareToken?: string }) {
  const [releases, setReleases] = useState<TimelineRelease[]>([]);
  const [cycles, setCycles] = useState<TimelineCycle[]>([]);
  const headers = useMemo(() => ({ "Content-Type": "application/json", Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }), []);
  useEffect(() => {
    const apply = (t: { releases?: TimelineRelease[]; cycles?: TimelineCycle[] } | null) => {
      if (!t) return;
      const aR = new Set(["Draft","Testing","Ready"]);
      const aC = new Set(["Draft","InProgress"]);
      setReleases((t.releases ?? []).filter(r => aR.has(r.status)));
      setCycles((t.cycles ?? []).filter(c => aC.has(c.status)));
    };
    if (shareCode || shareToken) {
      const url = shareCode ? `${apiUrl}/dashboard/shared/${encodeURIComponent(shareCode)}/timeline` : `${apiUrl}/dashboard/shared/timeline?token=${encodeURIComponent(shareToken ?? "")}`;
      fetch(url).then(r => r.ok ? r.json() : null).then(apply).catch(() => {});
      return;
    }
    if (!projectId) return;
    const q = new URLSearchParams({ projectId, ...(releaseId && { releaseId }), ...(buildId && { buildId }) });
    Promise.all([
      fetch(`${apiUrl}/releases?${q}`, { headers }).then(r => r.ok ? r.json() : []),
      fetch(`${apiUrl}/test-cycles?${q}&page=1&size=100`, { headers }).then(r => r.ok ? r.json() : null),
    ]).then(([rel, cyc]) => {
      const cycleRows = cyc && typeof cyc === "object" && "items" in cyc ? (cyc as { items: { rows: TimelineCycle[] } }).items.rows : (cyc as TimelineCycle[] | null) ?? [];
      apply({ releases: rel as TimelineRelease[], cycles: cycleRows });
    }).catch(() => {});
  }, [projectId, releaseId, buildId, headers, shareCode, shareToken]);

  const todayTH = startOfDayTH(nowTH());
  const allDates: Date[] = [];
  releases.forEach(r => { if (r.plannedReleaseDate) allDates.push(parseDateTH(r.plannedReleaseDate)); if (r.actualReleaseDate) allDates.push(parseDateTH(r.actualReleaseDate)); });
  cycles.forEach(c => { if (c.startDate) allDates.push(parseDateTH(c.startDate)); if (c.endDate) allDates.push(parseDateTH(c.endDate)); });
  allDates.push(todayTH);
  if (!releases.length && !cycles.length) return null;

  const minD = new Date(Math.min(...allDates.map(d => d.getTime())));
  const maxD = new Date(Math.max(...allDates.map(d => d.getTime())));
  const rangeStart = addDays(minD, -7);
  const rangeEnd = addDays(maxD, 14);

  // Build working days array (Mon-Fri only)
  const workDays: Date[] = [];
  const d = new Date(rangeStart);
  while (d <= rangeEnd) { if (isWeekday(d)) workDays.push(new Date(d)); d.setDate(d.getDate() + 1); }
  const totalCols = workDays.length;
  if (totalCols === 0) return null;

  const dayIndex = (target: Date) => workDays.findIndex(w => sameDay(w, target));
  const colStart = (target: Date) => { const i = dayIndex(target); return i >= 0 ? i + 1 : -1; };
  const colSpan = (from: Date, to: Date) => { const s = dayIndex(from); const e = dayIndex(to); return s >= 0 && e >= 0 ? Math.max(1, e - s + 1) : 0; };

  // Build week groups (Mon-Fri chunks)
  type WeekGroup = { weekNo: number; start: Date; end: Date; dayCount: number; label: string };
  const weekGroups: WeekGroup[] = [];
  let wg: WeekGroup | null = null;
  workDays.forEach(w => {
    if (!wg || w.getDay() === 1) {
      if (wg) weekGroups.push(wg);
      wg = { weekNo: weekGroups.length + 1, start: new Date(w), end: new Date(w), dayCount: 0, label: "" };
    }
    wg.end = new Date(w);
    wg.dayCount++;
  });
  if (wg) weekGroups.push(wg);
  weekGroups.forEach(wg => { wg.label = `${fmtShort(wg.start)} – ${fmtShort(wg.end)}`; });

  // Today column
  const cycleColor = (s: string) => s === "Completed" ? "#16a34a" : s === "InProgress" ? "#2563eb" : s === "Cancelled" ? "#94a3b8" : "#d97706";
  const releaseColor = (s: string) => s === "Released" ? "#16a34a" : s === "Ready" ? "#2563eb" : s === "Testing" ? "#d97706" : "#64748b";

  // Build week header spans (how many cols each week has)
  const weekSpans = weekGroups.map(wg => wg.dayCount);

  return <article className="card panel" style={{padding:24,overflowX:"auto"}}>
    <h3 className="title" style={{fontSize:16}}>Executive Timeline</h3>
    <p className="subtitle">Weekly Delivery View — Release milestones & Test Cycle progress</p>
    <div className="tl-head">
      <div></div>
      <div>
        <div className="weeks">
          {weekGroups.map((wg, i) => <span key={i} style={{gridColumn:`span ${weekSpans[i]}`}}>{wg.label}</span>)}
        </div>
        <div className="days">
          {workDays.map((w, i) => <span key={i} className={sameDay(w, todayTH) ? "today-col" : ""}>{w.getDate()}</span>)}
        </div>
      </div>
    </div>
    {releases.length > 0 && <>
      <div className="week-group-header"><div><span>RELEASES</span><b>Milestones</b></div><strong>{releases.length} Active</strong></div>
      {releases.map((r, idx) => {
        const pd = r.plannedReleaseDate ? parseDateTH(r.plannedReleaseDate) : null;
        const ad = r.actualReleaseDate ? parseDateTH(r.actualReleaseDate) : null;
        const sd = pd || ad; const ed = ad || pd;
        const gc = sd && ed ? colStart(sd) : -1;
        const gs = sd && ed ? colSpan(sd, ed) : 0;
        return <div key={r.releaseId} className={`tl-row animated-row${r.status === "Testing" ? " critical-row" : ""}`} style={{"--delay":`${idx * 0.06}s`} as React.CSSProperties}>
          <div className="tl-label">
            <div className="tl-title">{r.releaseCode}</div>
            <div className="tl-meta">{r.version} • {r.status}</div>
          </div>
          <div className="tl-track">
            <div className="day-grid">{workDays.map((_, i) => <i key={i} />)}</div>
            <div className="tl-bar-grid">
              {gc > 0 && <span className="animated-bar" style={{gridColumn:`${gc}/span ${Math.max(gs,1)}`,background:releaseColor(r.status),["--bar-delay" as string]:`${0.15 + idx * 0.08}s`}}>
                <em>{pd ? fmtShort(pd) : ""}{ad && pd ? "–" : ""}{ad ? fmtShort(ad) : ""}</em>
              </span>}
            </div>
          </div>
        </div>;
      })}
    </>}
    {cycles.length > 0 && <>
      <div className="week-group-header"><div><span>TEST CYCLES</span><b>Execution Progress</b></div><strong>{cycles.length} Active</strong></div>
      {cycles.map((c, idx) => {
        const sd = c.startDate ? parseDateTH(c.startDate) : null;
        const ed = c.endDate ? parseDateTH(c.endDate) : null;
        const gc = sd && ed ? colStart(sd) : -1;
        const gs = sd && ed ? colSpan(sd, ed) : 0;
        const color = cycleColor(c.status);
        return <div key={c.testCycleId} className="tl-row animated-row" style={{"--delay":`${idx * 0.06}s`} as React.CSSProperties}>
          <div className="tl-label">
            <div className="tl-title">{c.cycleCode}</div>
            <div className="tl-meta">{c.cycleName} • {c.progressPercent}%</div>
          </div>
          <div className="tl-track">
            <div className="day-grid">{workDays.map((_, i) => <i key={i} />)}</div>
            <div className="tl-bar-grid">
              {gc > 0 && gs > 0 && <span className="animated-bar" style={{gridColumn:`${gc}/span ${gs}`,background:color,["--bar-delay" as string]:`${0.15 + idx * 0.08}s`}}>
                <em>{sd ? fmtShort(sd) : ""}{ed && sd ? "–" : ""}{ed ? fmtShort(ed) : ""}</em>
              </span>}
            </div>
          </div>
        </div>;
      })}
    </>}
  </article>;
}

function Dashboard({ projectId, releaseId, buildId, shareCode, shareToken, projectName }: { projectId?: string; releaseId?: string; buildId?: string; shareCode?: string; shareToken?: string; projectName?: string }) {
  const [expandedModules, setExpandedModules] = useState<Set<string>>(new Set());
  const [data, setData] = useState<DashboardSummary | null>(null), [loading, setLoading] = useState(true), [error, setError] = useState("");
  useEffect(() => {
    setLoading(true); setError("");
    const params = new URLSearchParams({ ...(projectId && { projectId }), ...(releaseId && { releaseId }), ...(buildId && { buildId }) });
    const url = shareCode ? `${apiUrl}/dashboard/shared/${encodeURIComponent(shareCode)}` : shareToken ? `${apiUrl}/dashboard/shared?token=${encodeURIComponent(shareToken)}` : `${apiUrl}/dashboard/summary?${params}`;
    fetch(url, shareCode || shareToken ? {} : { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` } })
      .then(async r => { if (!r.ok) throw new Error(r.status === 401 ? "ลิงก์แชร์ไม่ถูกต้องหรือหมดอายุ" : "ไม่สามารถโหลดข้อมูล Dashboard ได้"); return r.json(); })
      .then(setData).catch(e => setError(e.message)).finally(() => setLoading(false));
  }, [projectId, releaseId, buildId, shareCode, shareToken]);
  if (loading) return <div className="executive-loading">กำลังประมวลผลข้อมูลคุณภาพ...</div>;
  if (error || !data) return <div className="executive-error">{error || "ไม่พบข้อมูล"}</div>;
  const decisionReason = data.recommendedDecision === "NO DATA" ? "ยังไม่มี Requirement หรือ Test Cycle สำหรับประเมิน"
    : data.criticalDefects > 0 ? `พบ Critical Defect ค้าง ${data.criticalDefects} รายการ`
    : data.openP0 > 0 ? `พบ P0 ค้าง ${data.openP0} รายการ`
    : data.highDefects > 0 ? `พบ High Defect ค้าง ${data.highDefects} รายการ`
    : data.openP1 > 0 ? `พบ P1 ค้าง ${data.openP1} รายการ`
    : data.requirementCoverage < 90 ? `Requirement Coverage ${data.requirementCoverage}% ต่ำกว่าเกณฑ์ 90%`
    : data.passRate < 90 ? `Pass Rate ${data.passRate}% ต่ำกว่าเกณฑ์ 90%`
    : "ผ่านเกณฑ์ P0/P1, Coverage, Pass Rate และ Defect";
  const sortModules = (list: DashboardSummary["modules"]) => [...list].sort((a,b) => (a.sortOrder ?? 999) - (b.sortOrder ?? 999) || (a.moduleCode ?? "").localeCompare(b.moduleCode ?? ""));
  const rootModules = sortModules(data.modules.filter(x => !x.parentModuleId));
  const childModules = new Map<string, DashboardSummary["modules"]>();
  for (const m of data.modules) if (m.parentModuleId) { const list = childModules.get(m.parentModuleId) ?? []; list.push(m); childModules.set(m.parentModuleId, list); }
  type ModuleRoll = { cases: number; executed: number; passed: number; failed: number; blocked: number; subs: number };
  const rollup = new Map<string, ModuleRoll>();
  const rollOf = (id: string): ModuleRoll => {
    const cached = rollup.get(id);
    if (cached) return cached;
    const m = data.modules.find(x => x.moduleId === id);
    const acc: ModuleRoll = { cases: m?.testCases ?? 0, executed: m?.executed ?? 0, passed: m?.passed ?? 0, failed: m?.failed ?? 0, blocked: m?.blocked ?? 0, subs: 0 };
    for (const c of childModules.get(id) ?? []) { const r = rollOf(c.moduleId); acc.cases += r.cases; acc.executed += r.executed; acc.passed += r.passed; acc.failed += r.failed; acc.blocked += r.blocked; acc.subs += 1 + r.subs; }
    rollup.set(id, acc);
    return acc;
  };
  data.modules.forEach(m => rollOf(m.moduleId));
  const totalCasesAll = data.modules.reduce((s,m) => s + m.testCases, 0);
  const renderModule = (m: DashboardSummary["modules"][number], depth: number): ReactElement => {
    const children = sortModules(childModules.get(m.moduleId) ?? []);
    const hasChildren = children.length > 0;
    const isExpanded = expandedModules.has(m.moduleId);
    const agg = rollOf(m.moduleId);
    const childCases = Math.max(0, agg.cases - m.testCases);
    const den = Math.max(1, agg.executed);
    const pPct = Math.round(agg.passed / den * 100);
    const fPct = Math.round(agg.failed / den * 100);
    const bPct = Math.round(agg.blocked / den * 100);
    const healthClass = m.health.toLowerCase().replace(/\s+/g, "");
    const hasSubCases = hasChildren && childCases > 0;
    return <_F key={m.moduleId}>
      <div className={"module-tree-row" + (hasChildren ? " has-children" : "")} style={{ paddingLeft: depth * 24 }}>
        {hasChildren ? <button type="button" className={"tree-expand-btn" + (isExpanded ? " open" : "")} aria-expanded={isExpanded} aria-label={(isExpanded ? "ย่อ " : "ขยาย ") + m.moduleName} onClick={() => setExpandedModules(prev => { const next = new Set(prev); if (next.has(m.moduleId)) next.delete(m.moduleId); else next.add(m.moduleId); return next; })}>▸</button> : <span className="tree-expand-spacer" />}
        <div className="module-tree-info">
          <div className="module-tree-name">{m.moduleName}{m.moduleCode && <span className="module-code-chip">{m.moduleCode}</span>}<span className={`health-badge health-${healthClass}`}>{m.health}</span></div>
          <small>{hasSubCases ? `${m.testCases.toLocaleString()} ในโมดูลนี้ + ${childCases.toLocaleString()} จาก ${agg.subs} Submodules` : `${m.testCases.toLocaleString()} Cases`}</small>
          <div className="module-tree-bars">
            <div className="status-bar-track">{agg.executed > 0 && <><span style={{width:`${pPct}%`,background:"#16a34a"}} /><span style={{width:`${fPct}%`,background:"#dc2626"}} /><span style={{width:`${bPct}%`,background:"#d97706"}} /></>}</div>
            <div className="status-bar-labels">{agg.executed > 0 ? <><span className="sb-pass">Pass {pPct}%</span><span className="sb-fail">Fail {fPct}%</span><span className="sb-block">Blocked {bPct}%</span></> : <span className="sb-none">ยังไม่มีผล Execution</span>}</div>
          </div>
        </div>
        <div className="module-cases-pill" title={hasSubCases ? `รวม ${agg.cases.toLocaleString()} Cases (${m.testCases.toLocaleString()} ในโมดูลนี้ + ${childCases.toLocaleString()} จาก Submodules)` : `${agg.cases.toLocaleString()} Cases`}><b>{agg.cases.toLocaleString()}</b><span>Cases</span></div>
      </div>
      {isExpanded && children.map(c => renderModule(c, depth + 1))}
    </_F>;
  };
  return <div className="executive-dashboard">
    <section className="exec-hero">
      <div className="exec-hero-accent" />
      <div className="exec-hero-body">
        <div className="exec-hero-top">
          <div className="exec-hero-info">
            <span className="exec-hero-eyebrow">QUALITY EXECUTIVE OVERVIEW</span>
            <h2 className="exec-hero-title">{projectName || "Release Readiness Dashboard"}</h2>
          </div>
          <div className="exec-hero-score">
            <strong>{data.overallScore == null ? "N/A" : `${data.overallScore}%`}</strong>
            <small>Overall Score</small>
          </div>
        </div>
        <div className="exec-hero-bottom">
          <div className={`exec-hero-decision decision-${data.recommendedDecision.toLowerCase().replace(" ", "-")}`}>
            <span className="decision-icon">{data.recommendedDecision === "GO" ? "✓" : data.recommendedDecision === "NO GO" ? "✕" : "!"}</span>
            <div className="decision-text">
              <strong>{data.recommendedDecision}</strong>
              <span>{decisionReason}</span>
            </div>
          </div>
          <div className="exec-hero-context">
            <span>{data.totalRequirements} Requirements</span>
            <span>{data.totalCases} Test Cases</span>
            <span>{data.executionProgress}% Execution</span>
            <span>{data.passRate}% Pass Rate</span>
            {data.openDefects > 0 && <span className="ctx-alert">{data.openDefects} Open Defects</span>}
            {data.criticalDefects > 0 && <span className="ctx-alert">{data.criticalDefects} Critical</span>}
            <span className="ctx-time">{new Date(data.generatedAt).toLocaleString("th-TH", {timeZone:"Asia/Bangkok", day:"numeric", month:"short", year:"numeric", hour:"2-digit", minute:"2-digit"})}</span>
          </div>
        </div>
      </div>
    </section>
    <div className="kpi-grid">{[
      ["Requirement Coverage", `${data.requirementCoverage}%`, `${data.coveredRequirements.toLocaleString()} / ${data.totalRequirements.toLocaleString()} Covered`, "green"],
      ["Execution Progress", `${data.executionProgress}%`, `${data.executedCases.toLocaleString()} / ${data.totalCases.toLocaleString()} Cases`, "blue"],
      ["Pass Rate", `${data.passRate}%`, `${data.passedCases.toLocaleString()} Passed`, "green"],
      ["Defect Quality", `${data.defectQuality}%`, `${data.openDefects} Open · Critical ${data.criticalDefects} · High ${data.highDefects}`, data.criticalDefects ? "red" : data.highDefects ? "yellow" : "green"],
      ["Release Blockers", data.openP0 + data.openP1, `P0 ${data.openP0} • P1 ${data.openP1}`, data.openP0 + data.openP1 ? "red" : "green"],
    ].map(x => <article className="card kpi" key={x[0]}><span>{x[0]}</span><strong>{x[1]}</strong><small className={String(x[3])}>{x[2]}</small></article>)}</div>
    <QualityOverviewCharts data={data} />
    <ExecutiveTimeline projectId={projectId} releaseId={releaseId} buildId={buildId} shareCode={shareCode} shareToken={shareToken} />
    <article className="card" style={{padding:24}}>
      <div className="module-overview-head">
        <div className="module-overview-title">
          <h3>Module Overview</h3>
          <p>โครงสร้าง Module แบบ Tree พร้อมจำนวน Test Case รวมทุก Submodule และสถานะการทดสอบ</p>
        </div>
        <div className="module-overview-total">
          <strong>{totalCasesAll.toLocaleString()}</strong>
          <span>Test Cases ทั้งหมด</span>
          <small>{data.modules.length} Modules · {rootModules.length} Root</small>
        </div>
      </div>
      <div className="module-tree-list">
        {rootModules.map(m => renderModule(m, 0))}
      </div>
    </article>
    <div className="dashboard-two-col">
      <article className="card" style={{padding:24}}>
        <h3 style={{margin:"0 0 4px",fontSize:16,fontWeight:800,color:"#1f2937"}}>QA Performance</h3>
        <p style={{margin:"0 0 20px",fontSize:12,color:"#697386",lineHeight:1.5}}>ผลการดำเนินงานของผู้ทดสอบแต่ละคน</p>
        <div className="qa-list">
          {data.users.length ? data.users.map((u, i) => <div className="qa-card" key={u.userId}><div className="qa-icon">{i + 1}</div><div className="qa-body"><div className="qa-top"><b>{u.displayName}</b><span>{u.passRate}%</span></div><div className="qa-desc">{u.executions} Executions · {u.passed} Passed · {u.failed} Failed</div><div className="qa-progress"><span style={{width:`${u.passRate}%`}} /></div></div></div>) : <p className="muted-row">ยังไม่มีข้อมูลการทดสอบ</p>}
        </div>
      </article>
      <article className="card" style={{padding:24}}>
        <h3 style={{margin:"0 0 4px",fontSize:16,fontWeight:800,color:"#1f2937"}}>Risks &amp; Blockers</h3>
        <p style={{margin:"0 0 20px",fontSize:12,color:"#697386",lineHeight:1.5}}>ความเสี่ยงและสิ่งกีดขวางที่ต้องติดตาม</p>
        <div className="risks-grid">
          {data.criticalDefects > 0 && <div className="risk-card"><div className="risk-icon">!</div><div className="risk-body"><b>Critical Defects</b><span>พบ Critical Defect ค้าง {data.criticalDefects} รายการ ต้องแก้ไขก่อน Release</span></div></div>}
          {data.openP0 > 0 && <div className="risk-card"><div className="risk-icon">!</div><div className="risk-body"><b>P0 Blockers</b><span>พบ P0 ค้าง {data.openP0} รายการ เป็น Blocker สำหรับ Release</span></div></div>}
          {data.highDefects > 0 && <div className="risk-card risk-warning"><div className="risk-icon">⚠</div><div className="risk-body"><b>High Defects</b><span>พบ High Defect ค้าง {data.highDefects} รายการ ควรตรวจสอบและจัดลำดับ</span></div></div>}
          {data.openP1 > 0 && <div className="risk-card risk-warning"><div className="risk-icon">⚠</div><div className="risk-body"><b>P1 Issues</b><span>พบ P1 ค้าง {data.openP1} รายการ ตรวจสอบว่าต้องแก้ก่อน Release หรือไม่</span></div></div>}
          {data.modules.filter(x => !x.parentModuleId && x.coveragePercent < 50).length > 0 && <div className="risk-card risk-info"><div className="risk-icon">i</div><div className="risk-body"><b>Low Coverage Modules</b><span>{data.modules.filter(x => !x.parentModuleId && x.coveragePercent < 50).map(x => x.moduleName).join(", ")} มี Coverage ต่ำกว่า 50%</span></div></div>}
          {data.requirementCoverage < 80 && <div className="risk-card risk-info"><div className="risk-icon">i</div><div className="risk-body"><b>Low Requirement Coverage</b><span>Requirement Coverage อยู่ที่ {data.requirementCoverage}% ต่ำกว่าเกณฑ์ 80%</span></div></div>}
          {data.criticalDefects === 0 && data.openP0 === 0 && data.highDefects === 0 && data.openP1 === 0 && <div className="risk-card" style={{background:"#f0fdf4",borderColor:"#bbf7d0"}}><div className="risk-icon" style={{background:"#dcfce7",color:"#16a34a"}}>✓</div><div className="risk-body"><b>No Critical Risks</b><span>ไม่พบ Critical Defect, P0 หรือ High Defect ค้าง — สถานะปกติ</span></div></div>}
        </div>
      </article>
    </div>
  </div>;
}

function DefectsPage({ projectId, releaseId, buildId, search, canEdit }: { projectId?: string; releaseId?: string; buildId?: string; search: string; canEdit?: boolean }) {
  const [items, setItems] = useState<DefectItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [moduleFilter, setModuleFilter] = useState("");
  const [severityFilter, setSeverityFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [assigneeFilter, setAssigneeFilter] = useState("");
  const [modules, setModules] = useState<ModuleItem[]>([]);
  const [users, setUsers] = useState<UserLookup[]>([]);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [detail, setDetail] = useState<DefectItem | null>(null);
  const [activities, setActivities] = useState<DefectActivityItem[]>([]);
  const [linkedCases, setLinkedCases] = useState<DefectTestCaseItem[]>([]);
  const [_detailLoading, setDetailLoading] = useState(false);
  const [commentText, setCommentText] = useState("");
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<DefectItem | null>(null);
  const [formTitle, setFormTitle] = useState("");
  const [formSeverity, setFormSeverity] = useState("Medium");
  const [formStatus, setFormStatus] = useState("Open");
  const [formModuleId, setFormModuleId] = useState("");
  const [formDescription, setFormDescription] = useState("");
  const [formStepsToReproduce, setFormStepsToReproduce] = useState("");
  const [formExpectedResult, setFormExpectedResult] = useState("");
  const [formActualResult, setFormActualResult] = useState("");
  const [formAssigneeUserId, setFormAssigneeUserId] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [reload, setReload] = useState(0);
  const [summaryStats, setSummaryStats] = useState({ total: 0, open: 0, inProgress: 0, resolved: 0, critical: 0 });
  const headers = useMemo(() => ({ "Content-Type": "application/json", Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }), []);
  const load = useCallback(() => {
    void reload; // refresh the list after create, edit, delete, or bulk updates
    if (!projectId) { setItems([]); setTotalCount(0); setLoading(false); setSummaryStats({ total: 0, open: 0, inProgress: 0, resolved: 0, critical: 0 }); return; }
    setLoading(true); setError("");
    const q = new URLSearchParams({ projectId, ...(releaseId && { releaseId }), ...(buildId && { buildId }), ...(search && { search }), ...(moduleFilter && { moduleId: moduleFilter }), ...(severityFilter && { severity: severityFilter }), ...(statusFilter && { status: statusFilter }), ...(assigneeFilter && { assigneeUserId: assigneeFilter }), page: String(page), size: String(pageSize) });
    fetch(`${apiUrl}/defects?${q}`, { headers }).then(async r => {
      if (!r.ok) throw new Error("โหลด Defect ไม่สำเร็จ");
      const json = await r.json();
      const response = json && typeof json === "object" ? json as Record<string, unknown> : null;
      const nestedItems = response?.items && typeof response.items === "object" ? response.items as Record<string, unknown> : null;
      const rows = Array.isArray(json) ? json
        : Array.isArray(response?.rows) ? response.rows
        : Array.isArray(response?.items) ? response.items
        : Array.isArray(nestedItems?.rows) ? nestedItems.rows
        : [];
      const total = Number(response?.total ?? response?.totalCount ?? nestedItems?.total ?? nestedItems?.totalCount ?? rows.length);
      setItems(rows as DefectItem[]);
      setTotalCount(Number.isFinite(total) ? total : rows.length);
      if (typeof response?.open === "number") {
        setSummaryStats({ total, open: response.open, inProgress: Number(response.inProgress ?? 0), resolved: Number(response.closed ?? 0), critical: rows.filter((x: DefectItem) => x.severity === "Critical").length });
      }
    }).catch(e => setError(e instanceof Error ? e.message : "โหลด Defect ไม่สำเร็จ")).finally(() => setLoading(false));
  }, [projectId, releaseId, buildId, search, moduleFilter, severityFilter, statusFilter, assigneeFilter, page, pageSize, headers, reload]);
  useEffect(load, [load]);
  useEffect(() => {
    if (!projectId) { setSummaryStats({ total: 0, open: 0, inProgress: 0, resolved: 0, critical: 0 }); return; }
    const q = new URLSearchParams({ projectId, ...(releaseId && { releaseId }), ...(buildId && { buildId }) });
    fetch(`${apiUrl}/defects/stats?${q}`, { headers }).then(r => r.ok ? r.json() : null).then(d => {
      if (d && typeof d === "object") setSummaryStats({ total: d.total ?? 0, open: d.open ?? 0, inProgress: d.inProgress ?? 0, resolved: d.resolved ?? 0, critical: d.critical ?? 0 });
    }).catch(() => {});
  }, [projectId, releaseId, buildId, headers, reload]);
  useEffect(() => {
    if (!projectId) return;
    const h = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
    Promise.all([
      fetch(`${apiUrl}/projects/${projectId}/modules`, { headers: h }).then(r => r.ok ? r.json() : []),
      fetch(`${apiUrl}/lookups/users`, { headers: h }).then(r => r.ok ? r.json() : []),
    ]).then(([m, u]) => { setModules((m as ModuleItem[]).filter(x => x.isActive)); setUsers(u as UserLookup[]); }).catch(() => {});
  }, [projectId]);
  useEffect(() => { setPage(1); }, [moduleFilter, severityFilter, statusFilter, assigneeFilter]);
  const openForm = (item?: DefectItem) => {
    setEditing(item ?? null);
    setFormTitle(item?.title ?? "");
    setFormSeverity(item?.severity ?? "Medium");
    setFormStatus(item?.status ?? "Open");
    setFormModuleId(item?.moduleId ?? "");
    setFormDescription(item?.description ?? "");
    setFormStepsToReproduce(item?.stepsToReproduce ?? "");
    setFormExpectedResult(item?.expectedResult ?? "");
    setFormActualResult(item?.actualResult ?? "");
    setFormAssigneeUserId(item?.assigneeUserId ?? "");
    setFormOpen(true);
  };
  const saveForm = async () => {
    setSaving(true); setError("");
    try {
      const body = { moduleId: formModuleId || null, title: formTitle, severity: formSeverity, status: formStatus, description: formDescription || null, stepsToReproduce: formStepsToReproduce || null, expectedResult: formExpectedResult || null, actualResult: formActualResult || null, assigneeUserId: formAssigneeUserId || null, releaseId: releaseId || null, buildId: buildId || null };
      const response = await fetch(editing ? `${apiUrl}/defects/${editing.defectId}` : `${apiUrl}/defects`, { method: editing ? "PUT" : "POST", headers, body: JSON.stringify(body) });
      if (!response.ok) { const p = await response.json().catch(() => null); throw new Error(p?.detail ?? "บันทึก Defect ไม่สำเร็จ"); }
      setFormOpen(false);
      setNotice(editing ? "แก้ไข Defect แล้ว" : "สร้าง Defect แล้ว");
      setReload(x => x + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ"); } finally { setSaving(false); }
  };
  const removeDefect = async (item: DefectItem) => {
    if (!window.confirm(`ลบ ${item.defectCode} ใช่หรือไม่?`)) return;
    const response = await fetch(`${apiUrl}/defects/${item.defectId}`, { method: "DELETE", headers });
    if (response.ok) { setNotice(`ลบ ${item.defectCode} แล้ว`); setReload(x => x + 1); }
  };
  const quickStatus = async (item: DefectItem, status: string) => {
    const response = await fetch(`${apiUrl}/defects/${item.defectId}/status`, { method: "PATCH", headers, body: JSON.stringify({ status }) });
    if (response.ok) { setNotice(`เปลี่ยนสถานะ ${item.defectCode} เป็น ${status}`); setReload(x => x + 1); }
  };
  const openDetail = async (item: DefectItem) => {
    setDetail(item); setActivities([]); setLinkedCases([]); setCommentText(""); setDetailLoading(true);
    try {
      const h = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
      const [actRes, tcRes] = await Promise.all([
        fetch(`${apiUrl}/defects/${item.defectId}/activities`, { headers: h }).then(r => r.ok ? r.json() : []),
        fetch(`${apiUrl}/defects/${item.defectId}/test-cases`, { headers: h }).then(r => r.ok ? r.json() : []),
      ]);
      setActivities(Array.isArray(actRes) ? actRes.map((a: any) => ({ activityId: a.activityId ?? a.defectActivityId ?? "", actionType: a.actionType ?? a.activityType ?? "", message: a.message ?? a.description ?? "", actorUserId: a.actorUserId ?? a.performedByUserId ?? null, actorName: a.actorName ?? null, createdAt: a.createdAt ?? a.performedAt ?? "", performedAt: a.performedAt ?? a.createdAt ?? "" })) : []);
      setLinkedCases(Array.isArray(tcRes) ? tcRes : []);
    } catch {} finally { setDetailLoading(false); }
  };
  const postComment = async () => {
    if (!detail || !commentText.trim()) return;
    const response = await fetch(`${apiUrl}/defects/${detail.defectId}/comments`, { method: "POST", headers, body: JSON.stringify({ body: commentText.trim() }) });
    if (response.ok) { setCommentText(""); openDetail(detail); }
  };
  const bulkStatus = async (status: string) => {
    if (!selectedIds.length) return;
    const response = await fetch(`${apiUrl}/defects/bulk`, { method: "POST", headers, body: JSON.stringify({ ids: selectedIds, status }) });
    if (response.ok) { setNotice(`เปลี่ยนสถานะ ${selectedIds.length} รายการ`); setSelectedIds([]); setReload(x => x + 1); }
  };
  const exportCsv = () => {
    const rows = [["Defect ID", "Title", "Severity", "Status", "Module", "Created", "Assignee"], ...items.map(x => [x.defectCode, x.title, x.severity, x.status, modules.find(m => m.moduleId === x.moduleId)?.moduleName ?? "", new Date(x.createdAt).toLocaleDateString("th-TH"), x.assigneeName ?? ""])];
    const csv = "\ufeff" + rows.map(row => row.map(v => `"${String(v).replaceAll('"', '""')}"`).join(",")).join("\r\n");
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
    const a = document.createElement("a"); a.href = url; a.download = "defects.csv"; a.click(); URL.revokeObjectURL(url);
  };
  const toggleSelectAll = () => { setSelectedIds(selectedIds.length === items.length ? [] : items.map(x => x.defectId)); };
  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize));
  if (loading && !items.length) return <article className="card empty"><p>กำลังโหลด Defect...</p></article>;
  return <>
    {error && <div className="inline-alert error"><span>{error}</span><button onClick={() => setError("")}>×</button></div>}
    {notice && <div className="inline-alert success"><span>{notice}</span><button onClick={() => setNotice("")}>×</button></div>}
    <div className="kpi-grid defect-summary-grid">
      <article className="card kpi"><span>Total</span><strong>{summaryStats.total}</strong><small>Defects</small></article>
      <article className="card kpi"><span>Open</span><strong>{summaryStats.open}</strong><small className="yellow">ต้องแก้ไข</small></article>
      <article className="card kpi"><span>In Progress</span><strong>{summaryStats.inProgress}</strong><small className="blue">กำลังแก้ไข</small></article>
      <article className="card kpi"><span>Resolved</span><strong>{summaryStats.resolved}</strong><small className="green">แก้ไขแล้ว</small></article>
      <article className="card kpi"><span>Critical</span><strong>{summaryStats.critical}</strong><small className={summaryStats.critical > 0 ? "red" : "green"}>สำคัญสูง</small></article>
    </div>
    <article className="card">
      <div className="table-tools">
        <div>
          <select aria-label="กรองตาม Module" value={moduleFilter} onChange={e => setModuleFilter(e.target.value)}><option value="">ทุก Module</option>{renderModuleSelectOptions(modules)}</select>
          <select value={severityFilter} onChange={e => setSeverityFilter(e.target.value)}><option value="">ทุก Severity</option>{defectSeverities.map(s => <option key={s}>{s}</option>)}</select>
          <select value={statusFilter} onChange={e => setStatusFilter(e.target.value)}><option value="">ทุก Status</option>{defectStatuses.map(s => <option key={s}>{s}</option>)}</select>
          <select value={assigneeFilter} onChange={e => setAssigneeFilter(e.target.value)}><option value="">ทุก Assignee</option>{users.map(u => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}</select>
        </div>
        <div>
          {selectedIds.length > 0 && <>
            <button className="btn" onClick={() => bulkStatus("Resolved")}>Resolve ({selectedIds.length})</button>
            <button className="btn" onClick={() => bulkStatus("Closed")}>Close ({selectedIds.length})</button>
          </>}
          <button className="btn" onClick={exportCsv}>Export</button>
          {canEdit !== false && <button className="btn primary" disabled={!projectId} onClick={() => openForm()}>+ Defect</button>}
        </div>
      </div>
      <div className="table-wrap">
        <table>
          <thead><tr>
            <th><input type="checkbox" checked={selectedIds.length === items.length && items.length > 0} onChange={toggleSelectAll} /></th>
            <th>Defect ID</th><th>Title</th><th>Severity</th><th>Status</th><th>Module</th><th>Age</th><th>Created</th><th>จัดการ</th>
          </tr></thead>
          <tbody>
            {items.map(x => <tr key={x.defectId}>
              <td><input type="checkbox" checked={selectedIds.includes(x.defectId)} onChange={() => setSelectedIds(prev => prev.includes(x.defectId) ? prev.filter(id => id !== x.defectId) : [...prev, x.defectId])} /></td>
              <td><button className="link-button" onClick={() => openDetail(x)}>{x.defectCode}</button></td>
              <td>{x.title}</td>
              <td><Badge tone={defectSeverityTones[x.severity] ?? "blue"}>{x.severity}</Badge></td>
              <td><Badge tone={defectStatusTones[x.status] ?? "gray"}>{x.status}</Badge></td>
              <td>{modules.find(m => m.moduleId === x.moduleId)?.moduleName ?? "-"}</td>
              <td>{defectAgeDays(x.createdAt)} วัน</td>
              <td>{fmtAgo(x.createdAt)}</td>
              <td><div className="row-actions">
                <button className="table-action" onClick={() => openDetail(x)}>ดู</button>
                {canEdit !== false && <>
                  <button className="table-action" onClick={() => openForm(x)}>แก้ไข</button>
                  {x.status === "Open" && <button className="table-action" onClick={() => quickStatus(x, "In Progress")}>เริ่ม</button>}
                  {x.status === "In Progress" && <button className="table-action" onClick={() => quickStatus(x, "Resolved")}>Resolve</button>}
                  <button className="table-action danger-action" onClick={() => removeDefect(x)}>ลบ</button>
                </>}
              </div></td>
            </tr>)}
            {!loading && !items.length && <tr><td colSpan={9} className="muted-row">ยังไม่มี Defect ในขอบเขตที่เลือก</td></tr>}
          </tbody>
        </table>
      </div>
      <div className="pagination">
        <label>แสดง<select value={pageSize} onChange={e => { setPageSize(Number(e.target.value)); setPage(1); }}><option>10</option><option>20</option><option>50</option></select> รายการ</label>
        <span>หน้า {Math.min(page, pageCount)} / {pageCount} ({totalCount} รายการ)</span>
        <button className="btn" disabled={page <= 1} onClick={() => setPage(x => x - 1)}>ก่อนหน้า</button>
        <button className="btn" disabled={page >= pageCount} onClick={() => setPage(x => x + 1)}>ถัดไป</button>
      </div>
    </article>
    {formOpen && <div className="modal" onMouseDown={() => setFormOpen(false)}>
      <div className="modal-box" onMouseDown={e => e.stopPropagation()}>
        <div className="modal-head"><h2>{editing ? "แก้ไข" : "สร้าง"} Defect</h2><button onClick={() => setFormOpen(false)}>×</button></div>
        <div className="form-grid">
          <label className="full">Title<input value={formTitle} onChange={e => setFormTitle(e.target.value)} placeholder="ระบุชื่อ Defect" /></label>
          <label>Module<select value={formModuleId} onChange={e => setFormModuleId(e.target.value)}><option value="">เลือก Module</option>{renderModuleSelectOptions(modules)}</select></label>
          <label>Severity<select value={formSeverity} onChange={e => setFormSeverity(e.target.value)}>{defectSeverities.map(s => <option key={s}>{s}</option>)}</select></label>
          <label>Status<select value={formStatus} onChange={e => setFormStatus(e.target.value)}>{defectStatuses.map(s => <option key={s}>{s}</option>)}</select></label>
          <label>Assignee<select value={formAssigneeUserId} onChange={e => setFormAssigneeUserId(e.target.value)}><option value="">ไม่ระบุ</option>{users.map(u => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}</select></label>
          <label className="full">Description<textarea rows={3} value={formDescription} onChange={e => setFormDescription(e.target.value)} placeholder="รายละเอียด Defect" /></label>
          <label className="full">Steps to Reproduce<textarea rows={3} value={formStepsToReproduce} onChange={e => setFormStepsToReproduce(e.target.value)} /></label>
          <label className="full">Expected Result<input value={formExpectedResult} onChange={e => setFormExpectedResult(e.target.value)} /></label>
          <label className="full">Actual Result<input value={formActualResult} onChange={e => setFormActualResult(e.target.value)} /></label>
        </div>
        <div className="modal-actions">
          <button className="btn" onClick={() => setFormOpen(false)}>ยกเลิก</button>
          <button className="btn primary" disabled={saving || !formTitle.trim()} onClick={saveForm}>{saving ? "กำลังบันทึก..." : "บันทึก"}</button>
        </div>
      </div>
    </div>}
    {detail && <div className="modal" onMouseDown={() => setDetail(null)}>
      <div className="modal-box" style={{ maxWidth: 800 }} onMouseDown={e => e.stopPropagation()}>
        <div className="modal-head"><div><h2>{detail.defectCode}</h2><small>{detail.title}</small></div><button onClick={() => setDetail(null)}>×</button></div>
        <div className="detail-grid" style={{ gridTemplateColumns: "1fr 1fr", gap: 12, marginBottom: 16 }}>
          <span>Severity <Badge tone={defectSeverityTones[detail.severity]}>{detail.severity}</Badge></span>
          <span>Status <Badge tone={defectStatusTones[detail.status]}>{detail.status}</Badge></span>
          <span>Module <b>{modules.find(m => m.moduleId === detail.moduleId)?.moduleName ?? "-"}</b></span>
          <span>Age <b>{defectAgeDays(detail.createdAt)} วัน</b></span>
          <span>Created <b>{fmtAgo(detail.createdAt)}</b></span>
          <span>Assignee <b>{detail.assigneeName ?? "ไม่ระบุ"}</b></span>
        </div>
        {detail.description && <section style={{ marginBottom: 16 }}><h4>Description</h4><p style={{ fontSize: 13, color: "#374151", lineHeight: 1.6 }}>{detail.description}</p></section>}
        {detail.stepsToReproduce && <section style={{ marginBottom: 16 }}><h4>Steps to Reproduce</h4><p style={{ fontSize: 13, color: "#374151", lineHeight: 1.6, whiteSpace: "pre-wrap" }}>{detail.stepsToReproduce}</p></section>}
        {canEdit !== false && <section style={{ marginBottom: 16 }}><h4>Quick Actions</h4><div className="row-actions" style={{ gap: 4 }}>
          {detail.status !== "In Progress" && detail.status !== "Closed" && <button className="btn" onClick={() => { quickStatus(detail, "In Progress"); setDetail(null); }}>→ In Progress</button>}
          {detail.status !== "Resolved" && detail.status !== "Closed" && <button className="btn" onClick={() => { quickStatus(detail, "Resolved"); setDetail(null); }}>✓ Resolve</button>}
          {detail.status !== "Closed" && <button className="btn" onClick={() => { quickStatus(detail, "Closed"); setDetail(null); }}>Closed</button>}
          <button className="btn" onClick={() => { openForm(detail); setDetail(null); }}>แก้ไข</button>
        </div></section>}
        <section style={{ marginBottom: 16 }}><h4>Linked Test Cases ({linkedCases.length})</h4>
          {linkedCases.length ? <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>{linkedCases.map(tc => <div key={tc.testCaseId} style={{ display: "flex", alignItems: "center", gap: 8, padding: "6px 8px", background: "#f9fafb", borderRadius: 6 }}><b style={{ fontSize: 12 }}>{tc.testCaseCode}</b><span style={{ fontSize: 12, flex: 1 }}>{tc.title}</span><Badge tone={tc.status ? (testCaseStatusTones[tc.status] ?? "gray") : "gray"}>{tc.status ?? "-"}</Badge></div>)}</div> : <p style={{ fontSize: 12, color: "#9ca3af" }}>ยังไม่มี Test Case ที่เชื่อมโยง</p>}
        </section>
        <section style={{ marginBottom: 16 }}><h4>Activities ({activities.length})</h4>
          <div style={{ display: "flex", flexDirection: "column", gap: 8, maxHeight: 200, overflowY: "auto" }}>
            {activities.length ? activities.map(a => <div key={a.activityId} style={{ display: "flex", gap: 8, alignItems: "flex-start" }}><Badge tone="blue">{defectActionLabels[a.actionType] ?? a.actionType}</Badge><div style={{ flex: 1 }}><p style={{ fontSize: 12, margin: 0 }}>{a.message ?? a.actionType}</p><small style={{ fontSize: 10, color: "#9ca3af" }}>{a.actorName ?? "System"} · {fmtAgo(a.performedAt ?? a.createdAt)}</small></div></div>) : <p style={{ fontSize: 12, color: "#9ca3af" }}>ยังไม่มี Activity</p>}
          </div>
          {canEdit !== false && <div style={{ display: "flex", gap: 8, marginTop: 8 }}><input style={{ flex: 1 }} value={commentText} onChange={e => setCommentText(e.target.value)} placeholder="เพิ่มคอมเมนต์..." onKeyDown={e => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); postComment(); } }} /><button className="btn primary" onClick={postComment} disabled={!commentText.trim()}>ส่ง</button></div>}
        </section>
        <div className="modal-actions"><button className="btn primary" onClick={() => setDetail(null)}>ปิด</button></div>
      </div>
    </div>}
  </>;
}

function DataPage({ page, search, projectId, releaseId, buildId, canAssignExecution = false, canExport = false }: { page: Page; search: string; projectId?: string; releaseId?: string; buildId?: string; canAssignExecution?: boolean; canExport?: boolean }) {
  if (page === "execution") return <ExecutionWorkspacePage contextProjectId={projectId} contextReleaseId={releaseId} contextBuildId={buildId} />;
  if (page === "test-cycles") return <TestCyclesPage search={search} canEdit={canAssignExecution} canExport={canExport} contextProjectId={projectId} contextReleaseId={releaseId} contextBuildId={buildId} />;
  if (page === "test-suites") {
    let canEdit = false;
    try {
      const current: SessionUser = JSON.parse(
        localStorage.getItem("qa.user") ?? "{}",
      );
      canEdit =
        current.roles?.includes("SYS_ADMIN") ||
        current.permissions?.includes("TESTCASE.EDIT");
    } catch {
      canEdit = false;
    }
    return <TestSuitesPage search={search} canEdit={canEdit} contextProjectId={projectId} />;
  }
  let headers: string[] = [],
    rows: string[][] = [];
  if (page === "releases") {
    headers = [
      "Release Code",
      "Version",
      "Type",
      "Planned Date",
      "Status",
      "Owner",
      "Readiness",
    ];
    rows = releases;
  } else if (page === "requirements" || page === "rtm") {
    headers = [
      "Requirement",
      "Title",
      "Module",
      "Priority",
      "Coverage",
      "Latest Result",
      "Status",
    ];
    rows = requirements;
  } else if (page === "defects") {
    headers = [
      "Defect ID",
      "Title",
      "Module",
      "Severity",
      "Status",
      "Build Found",
      "Assignee",
    ];
    rows = defects;
  } else if (page === "test-cases") {
    headers = [
      "Test Case ID",
      "Title",
      "Module",
      "Priority",
      "Type",
      "Revision",
      "Last Result",
    ];
    rows = [
      [
        "TC-SALE-201",
        "ใช้ส่วนลดสมาชิกและคูปอง",
        "Sales / POS",
        "P0",
        "Functional",
        "4",
        "Passed",
      ],
      [
        "TC-STK-114",
        "Void แล้วคืนยอดสต็อก",
        "Stock",
        "P1",
        "Regression",
        "2",
        "Failed",
      ],
      [
        "TC-RPT-089",
        "Export PDF ภาษาไทย",
        "Report",
        "P1",
        "Functional",
        "3",
        "Blocked",
      ],
    ];
  } else {
    return <EmptyPage page={page} />;
  }
  const filtered = rows.filter((r) =>
    r.join(" ").toLowerCase().includes(search.toLowerCase()),
  );
  return (
    <article className="card">
      <div className="table-tools">
        <div>
          <select aria-label="สถานะ">
            <option>ทุกสถานะ</option>
          </select>
          <select aria-label="โมดูล">
            <option>ทุกโมดูล</option>
          </select>
        </div>
        <span>{filtered.length} รายการ</span>
      </div>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              {headers.map((h) => (
                <th key={h}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {filtered.map((r) => (
              <tr key={r[0]}>
                {r.map((c, i) => (
                  <td key={`${r[0]}-${i}`}>
                    {[
                      "P0",
                      "P1",
                      "Failed",
                      "Blocked",
                      "Open",
                      "Conditional",
                    ].includes(c) ? (
                      <Badge
                        tone={
                          ["P0", "P1", "Failed", "Open"].includes(c)
                            ? "red"
                            : "yellow"
                        }
                      >
                        {c}
                      </Badge>
                    ) : [
                        "Passed",
                        "Go",
                        "Ready",
                        "Resolved",
                        "Closed",
                      ].includes(c) ? (
                      <Badge tone="green">{c}</Badge>
                    ) : (
                      c
                    )}
                  </td>
                ))}
                </tr>
              ))}
            </tbody>
        </table>
      </div>
    </article>
  );
}

function EmptyPage({ page }: { page: Page }) {
  return (
    <article className="card empty">
      <div className="empty-icon">
        {nav.flatMap((n) => n.items).find((i) => i.id === page)?.icon}
      </div>
      <h3>{pageNames[page]}</h3>
      <p>
        โมดูลนี้เตรียมไว้ตาม Screen Specification และจะเปิดการจัดการข้อมูลเมื่อ
        API ของโมดูลพร้อมใช้งาน
      </p>
    </article>
  );
}

type ProjectItem = {
  projectId: string;
  projectCode: string;
  projectName: string;
  description?: string;
  status: string;
  isActive: boolean;
  createdAt: string;
};
type ModuleItem = {
  moduleId: string;
  projectId: string;
  parentModuleId?: string | null;
  moduleCode: string;
  moduleName: string;
  description?: string;
  isActive: boolean;
  sortOrder: number;
};
function ProjectsPage({ search }: { search: string; refresh?: number }) {
  let canEdit = false;
  try {
    const current: SessionUser = JSON.parse(
      localStorage.getItem("qa.user") ?? "{}",
    );
    canEdit =
      current.roles?.includes("SYS_ADMIN") ||
      current.permissions?.includes("PROJECT.EDIT");
  } catch {
    canEdit = false;
  }
  const [items, setItems] = useState<ProjectItem[]>([]),
    [modules, setModules] = useState<ModuleItem[]>([]),
    [selectedId, setSelectedId] = useState(""),
    [loading, setLoading] = useState(true),
    [error, setError] = useState(""),
    [reload, setReload] = useState(0),
    [modal, setModal] = useState<"project" | "module" | null>(null),
    [editProject, setEditProject] = useState<ProjectItem | null>(null),
    [editModule, setEditModule] = useState<ModuleItem | null>(null),
    [code, setCode] = useState(""),
    [name, setName] = useState(""),
    [description, setDescription] = useState(""),
    [parentId, setParentId] = useState(""),
    [saving, setSaving] = useState(false),
    [expanded, setExpanded] = useState<string[]>([]),
    [draggingId, setDraggingId] = useState(""),
    [dropHint, setDropHint] = useState("");
  const headers = useMemo(() => ({
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  }), []);
  useEffect(() => {
    setLoading(true);
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetch(`${apiUrl}/projects`, { headers: h })
      .then(async (r) => {
        if (!r.ok)
          throw new Error(
            r.status === 401
              ? "Session หมดอายุ กรุณาเข้าสู่ระบบใหม่"
              : "โหลดข้อมูลโครงการไม่สำเร็จ",
          );
        return r.json();
      })
      .then((data: ProjectItem[]) => {
        const activeProjects = data.filter((x) => x.isActive);
        setItems(activeProjects);
        setSelectedId((current) =>
          activeProjects.some((x) => x.projectId === current)
            ? current
            : (activeProjects[0]?.projectId ?? ""),
        );
      })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [reload]);
  useEffect(() => {
    if (!selectedId) {
      setModules([]);
      return;
    }
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetch(`${apiUrl}/projects/${selectedId}/modules`, { headers: h })
      .then((r) => r.json())
      .then((data: ModuleItem[]) => setModules(data.filter((x) => x.isActive)));
  }, [selectedId, reload]);
  const selected = items.find((x) => x.projectId === selectedId),
    term = search.toLowerCase();
  const filteredProjects = items.filter((x) =>
    `${x.projectCode} ${x.projectName} ${x.description ?? ""}`
      .toLowerCase()
      .includes(term),
  );
  const includedIds = new Set(
    modules
      .filter(
        (x) =>
          !term ||
          `${x.moduleCode} ${x.moduleName} ${x.description ?? ""}`
            .toLowerCase()
            .includes(term),
      )
      .map((x) => x.moduleId),
  );
  if (term) {
    for (const module of modules.filter((x) => includedIds.has(x.moduleId))) {
      let parent = modules.find((x) => x.moduleId === module.parentModuleId);
      while (parent) {
        includedIds.add(parent.moduleId);
        parent = modules.find((x) => x.moduleId === parent?.parentModuleId);
      }
    }
  }
  const visibleModules: {
    item: ModuleItem;
    level: number;
    childCount: number;
  }[] = [];
  const visited = new Set<string>();
  const appendModules = (parent: string | null | undefined, level: number) => {
    for (const item of modules
      .filter((x) => x.parentModuleId === parent && includedIds.has(x.moduleId))
      .sort(
        (a, b) =>
          a.sortOrder - b.sortOrder || a.moduleCode.localeCompare(b.moduleCode),
      )) {
      if (visited.has(item.moduleId)) continue;
      visited.add(item.moduleId);
      const children = modules.filter(
        (x) =>
          x.parentModuleId === item.moduleId && includedIds.has(x.moduleId),
      );
      visibleModules.push({ item, level, childCount: children.length });
      if (term || expanded.includes(item.moduleId))
        appendModules(item.moduleId, level + 1);
    }
  };
  appendModules(null, 0);
  appendModules(undefined, 0);
  for (const orphan of modules.filter(
    (x) =>
      includedIds.has(x.moduleId) &&
      !visited.has(x.moduleId) &&
      !modules.some((parent) => parent.moduleId === x.parentModuleId),
  ))
    appendModules(orphan.parentModuleId, 0);
  const openProject = (item?: ProjectItem) => {
    setEditProject(item ?? null);
    setEditModule(null);
    setCode(
      item?.projectCode ??
        nextBusinessCode("PRJ", items.map((x) => x.projectCode)),
    );
    setName(item?.projectName ?? "");
    setDescription(item?.description ?? "");
    setModal("project");
  };
  const openModule = (item?: ModuleItem) => {
    setEditModule(item ?? null);
    setEditProject(null);
    setCode(
      item?.moduleCode ??
        nextBusinessCode(
          `${selected?.projectCode ?? "PRJ"}-MOD`,
          modules.map((x) => x.moduleCode),
        ),
    );
    setName(item?.moduleName ?? "");
    setDescription(item?.description ?? "");
    setParentId(item?.parentModuleId ?? "");
    setModal("module");
  };
  const save = async () => {
    if (!modal) return;
    setSaving(true);
    try {
      let url = "",
        method = "POST",
        body: object;
      if (modal === "project") {
        url = editProject
          ? `${apiUrl}/projects/${editProject.projectId}`
          : `${apiUrl}/projects`;
        method = editProject ? "PUT" : "POST";
        body = editProject
          ? {
              projectName: name,
              description: description || null,
              ownerUserId: null,
            }
          : {
              projectCode: "",
              projectName: name,
              description: description || null,
              ownerUserId: null,
            };
      } else {
        url = editModule
          ? `${apiUrl}/modules/${editModule.moduleId}`
          : `${apiUrl}/projects/${selectedId}/modules`;
        method = editModule ? "PUT" : "POST";
        body = editModule
          ? {
              moduleName: name,
              parentModuleId: parentId || null,
              description: description || null,
              ownerUserId: null,
            }
          : {
              moduleCode: "",
              moduleName: name,
              parentModuleId: parentId || null,
              description: description || null,
              ownerUserId: null,
            };
      }
      const response = await fetch(url, {
        method,
        headers,
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        const p = await response.json();
        throw new Error(p.detail ?? "บันทึกข้อมูลไม่สำเร็จ");
      }
      setModal(null);
      setReload((x) => x + 1);
    } catch (e) {
      window.alert(e instanceof Error ? e.message : "บันทึกข้อมูลไม่สำเร็จ");
    } finally {
      setSaving(false);
    }
  };
  const deactivate = async (
    kind: "project" | "module",
    id: string,
    label: string,
  ) => {
    if (!window.confirm(`ยืนยันปิดใช้งาน ${label}?`)) return;
    const response = await fetch(
      `${apiUrl}/${kind === "project" ? "projects" : "modules"}/${id}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) {
      window.alert("ไม่สามารถปิดใช้งานข้อมูลได้");
      return;
    }
    setReload((x) => x + 1);
  };
  const moveModule = async (
    target: ModuleItem,
    position: "before" | "inside" | "after",
  ) => {
    if (!draggingId || draggingId === target.moduleId) return;
    const dragged = modules.find((x) => x.moduleId === draggingId);
    if (!dragged) return;
    const parentModuleId =
      position === "inside" ? target.moduleId : (target.parentModuleId ?? null);
    const siblings = modules
      .filter(
        (x) =>
          x.moduleId !== dragged.moduleId &&
          (x.parentModuleId ?? null) === parentModuleId,
      )
      .sort(
        (a, b) =>
          a.sortOrder - b.sortOrder || a.moduleCode.localeCompare(b.moduleCode),
      );
    const targetIndex =
      position === "inside"
        ? siblings.length
        : Math.max(
            0,
            siblings.findIndex((x) => x.moduleId === target.moduleId) +
              (position === "after" ? 1 : 0),
          );
    const response = await fetch(`${apiUrl}/modules/${dragged.moduleId}/move`, {
      method: "POST",
      headers,
      body: JSON.stringify({ parentModuleId, sortOrder: targetIndex }),
    });
    setDraggingId("");
    setDropHint("");
    if (!response.ok) {
      const problem = await response.json();
      window.alert(problem.detail ?? "ไม่สามารถย้าย Module ได้");
      return;
    }
    if (parentModuleId)
      setExpanded((current) =>
        current.includes(parentModuleId)
          ? current
          : [...current, parentModuleId],
      );
    setReload((x) => x + 1);
  };
  if (loading)
    return (
      <article className="card empty">
        <p>กำลังโหลดข้อมูลโครงการ...</p>
      </article>
    );
  if (error)
    return (
      <article className="card empty">
        <div className="login-error">{error}</div>
      </article>
    );
  return (
    <div className="project-layout">
      <article className="card project-list">
        <div className="card-title">
          <div>
            <h3>Projects</h3>
            <p>{filteredProjects.length} โครงการ · คลิกเพื่อดู Module</p>
          </div>
          {canEdit && (
            <button className="btn primary" onClick={() => openProject()}>
              + Project
            </button>
          )}
        </div>
        <div className="project-cards">
          {filteredProjects.map((x) => (
            <button
              key={x.projectId}
              className={selectedId === x.projectId ? "active" : ""}
              onClick={() => setSelectedId(x.projectId)}
            >
              <span className="project-code">{x.projectCode}</span>
              <b>{x.projectName}</b>
              <small>{x.description || "ไม่มีรายละเอียด"}</small>
              <Badge tone={x.isActive ? "green" : "red"}>
                {x.isActive ? "ใช้งาน" : "ปิดใช้งาน"}
              </Badge>
            </button>
          ))}
        </div>
      </article>
      <article className="card module-panel">
        <div className="card-title">
          <div>
            <h3>Modules {selected && <span>· {selected.projectCode}</span>}</h3>
            <p>
              {modules.length} Module ในโครงการที่เลือก{" "}
              {canEdit && "· ลากเพื่อจัดลำดับหรือวางซ้อนเป็น Module ลูก"}
            </p>
          </div>
          {canEdit && selected?.isActive && (
            <div className="row-actions">
              <button className="btn" onClick={() => openProject(selected)}>
                แก้ไข Project
              </button>
              <button className="btn primary" onClick={() => openModule()}>
                + Module
              </button>
            </div>
          )}
        </div>
        {selected ? (
          <div className="table-wrap">
            <table className="module-tree">
              <thead>
                <tr>
                  <th>Module</th>
                  <th>Description</th>
                  <th>Status</th>
                  {canEdit && <th>จัดการ</th>}
                </tr>
              </thead>
              <tbody>
                {visibleModules.map(({ item: x, level, childCount }) => {
                  const hint = dropHint.startsWith(`${x.moduleId}:`)
                    ? dropHint.split(":")[1]
                    : "";
                  return (
                    <tr
                      key={x.moduleId}
                      draggable={canEdit}
                      className={`${level ? "child-row " : ""}${draggingId === x.moduleId ? "dragging " : ""}${hint ? `drop-${hint}` : ""}`}
                      onDragStart={(e) => {
                        setDraggingId(x.moduleId);
                        e.dataTransfer.effectAllowed = "move";
                      }}
                      onDragEnd={() => {
                        setDraggingId("");
                        setDropHint("");
                      }}
                      onDragOver={(e) => {
                        if (!canEdit || draggingId === x.moduleId) return;
                        e.preventDefault();
                        const box = e.currentTarget.getBoundingClientRect(),
                          ratio = (e.clientY - box.top) / box.height,
                          position =
                            ratio < 0.28
                              ? "before"
                              : ratio > 0.72
                                ? "after"
                                : "inside";
                        setDropHint(`${x.moduleId}:${position}`);
                      }}
                      onDrop={(e) => {
                        e.preventDefault();
                        const position = (dropHint.split(":")[1] ||
                          "inside") as "before" | "inside" | "after";
                        moveModule(x, position);
                      }}
                    >
                      <td>
                        <div
                          className="tree-module"
                          style={{ paddingLeft: `${level * 24}px` }}
                        >
                          {canEdit && (
                            <span
                              className="drag-handle"
                              title="ลากเพื่อย้ายตำแหน่ง"
                            >
                              ⋮⋮
                            </span>
                          )}
                          {childCount ? (
                            <button
                              className="tree-toggle"
                              onClick={() =>
                                setExpanded((current) =>
                                  current.includes(x.moduleId)
                                    ? current.filter((id) => id !== x.moduleId)
                                    : [...current, x.moduleId],
                                )
                              }
                              aria-label={
                                expanded.includes(x.moduleId)
                                  ? "ย่อ Module"
                                  : "ขยาย Module"
                              }
                            >
                              {term || expanded.includes(x.moduleId)
                                ? "▾"
                                : "▸"}
                            </button>
                          ) : (
                            <span className="tree-spacer" />
                          )}
                          <span>
                            <b>{x.moduleName}</b>
                          </span>
                          {childCount > 0 && (
                            <span className="child-count">
                              {childCount} Module
                            </span>
                          )}
                        </div>
                      </td>
                      <td>{x.description || "-"}</td>
                      <td>
                        <Badge tone="green">ใช้งาน</Badge>
                      </td>
                      {canEdit && (
                        <td>
                          <div className="row-actions">
                            <button
                              className="table-action"
                              onClick={() => openModule(x)}
                            >
                              แก้ไข
                            </button>
                            <button
                              className="table-action danger-action"
                              onClick={() =>
                                deactivate("module", x.moduleId, x.moduleName)
                              }
                            >
                              ลบ
                            </button>
                          </div>
                        </td>
                      )}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="empty">
            <p>เลือก Project เพื่อดู Modules</p>
          </div>
        )}
        {canEdit && selected?.isActive && (
          <div className="project-footer">
            <button
              className="danger-link"
              onClick={() =>
                deactivate("project", selected.projectId, selected.projectName)
              }
            >
              ปิดใช้งาน Project นี้
            </button>
          </div>
        )}
      </article>
      {modal && (
        <div className="modal" onMouseDown={() => setModal(null)}>
          <div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
            <div className="modal-head">
              <h2>
                {editProject || editModule ? "แก้ไข" : "เพิ่ม"}{" "}
                {modal === "project" ? "Project" : "Module"}
              </h2>
              <button onClick={() => setModal(null)}>×</button>
            </div>
            <div className="form-grid">
              <label>
                {modal === "project" ? "Project Code" : "Module Code"}
                <input
                  disabled
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  placeholder="ระบบสร้างรหัสอัตโนมัติ"
                />
              </label>
              <label>
                ชื่อ
                <input
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="ระบุชื่อ"
                />
              </label>
              {modal === "module" && (
                <label className="full">
                  Parent Module
                  <select
                    value={parentId}
                    onChange={(e) => setParentId(e.target.value)}
                  >
                    <option value="">ไม่มี Parent</option>
                    {modules
                      .filter(
                        (x) =>
                          x.moduleId !== editModule?.moduleId && x.isActive,
                      )
                      .map((x) => (
                        <option key={x.moduleId} value={x.moduleId}>
                          {x.moduleCode} · {x.moduleName}
                        </option>
                      ))}
                  </select>
                </label>
              )}
              <label className="full">
                รายละเอียด
                <textarea
                  rows={4}
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                />
              </label>
            </div>
            <div className="modal-actions">
              <button className="btn" onClick={() => setModal(null)}>
                ยกเลิก
              </button>
              <button
                className="btn primary"
                disabled={saving || !code.trim() || !name.trim()}
                onClick={save}
              >
                {saving ? "กำลังบันทึก..." : "บันทึก"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
type ReleaseItem = {
  releaseId: string;
  projectId: string;
  releaseCode: string;
  version: string;
  releaseType?: string;
  scope?: string;
  plannedReleaseDate?: string;
  actualReleaseDate?: string;
  status: string;
  createdAt: string;
};
type BuildItem = {
  buildId: string;
  releaseId: string;
  buildNumber: string;
  applicationVersion?: string;
  packageVersion?: string;
  commitReference?: string;
  buildDate?: string;
  changeNotes?: string;
  knownIssues?: string;
  isReleaseCandidate: boolean;
  isActive: boolean;
  status: string;
};
function ReleasesPage({ search, contextProjectId }: { search: string; refresh?: number; contextProjectId?: string }) {
  const masterOptions = useMasterOptions(), releaseTypes = masterOptions("ReleaseType");
  let canEdit = false;
  try {
    const current: SessionUser = JSON.parse(
      localStorage.getItem("qa.user") ?? "{}",
    );
    canEdit =
      current.roles?.includes("SYS_ADMIN") ||
      current.permissions?.includes("PROJECT.EDIT");
  } catch {
    canEdit = false;
  }
  const [items, setItems] = useState<ReleaseItem[]>([]),
    [allItems, setAllItems] = useState<ReleaseItem[]>([]),
    [projects, setProjects] = useState<ProjectItem[]>([]),
    [builds, setBuilds] = useState<BuildItem[]>([]),
    [selectedId, setSelectedId] = useState(""),
    [loading, setLoading] = useState(true),
    [error, setError] = useState(""),
    [reload, setReload] = useState(0),
    [modal, setModal] = useState<"release" | "build" | null>(null),
    [releaseDetail, setReleaseDetail] = useState<ReleaseItem | null>(null),
    [buildDetail, setBuildDetail] = useState<BuildItem | null>(null),
    [editRelease, setEditRelease] = useState<ReleaseItem | null>(null),
    [editBuild, setEditBuild] = useState<BuildItem | null>(null),
    [projectId, setProjectId] = useState(""),
    [code, setCode] = useState(""),
    [name, setName] = useState(""),
    [type, setType] = useState(""),
    [releaseStatus, setReleaseStatus] = useState("Draft"),
    [buildStatus, setBuildStatus] = useState("Ready"),
    [date, setDate] = useState(""),
    [details, setDetails] = useState(""),
    [packageVersion, setPackageVersion] = useState(""),
    [commit, setCommit] = useState(""),
    [issues, setIssues] = useState(""),
    [saving, setSaving] = useState(false);
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
  useEffect(() => {
    setLoading(true);
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    Promise.all([
      fetch(`${apiUrl}/releases`, { headers: h }).then((r) => r.json()),
      fetch(`${apiUrl}/projects`, { headers: h }).then((r) => r.json()),
    ])
      .then(([releaseData, projectData]) => {
        const allReleases = releaseData as ReleaseItem[];
        const active = allReleases.filter(
          (x) => x.status !== "Cancelled",
        );
        setAllItems(allReleases);
        setItems(active);
        setProjects((projectData as ProjectItem[]).filter((x) => x.isActive));
        setSelectedId((current) =>
          active.some((x) => x.releaseId === current)
            ? current
            : (active[0]?.releaseId ?? ""),
        );
      })
      .catch(() => setError("โหลดข้อมูล Release ไม่สำเร็จ"))
      .finally(() => setLoading(false));
  }, [reload]);
  useEffect(() => {
    if (!selectedId) {
      setBuilds([]);
      return;
    }
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetch(`${apiUrl}/releases/${selectedId}/builds`, { headers: h })
      .then((r) => r.json())
      .then((data: BuildItem[]) => setBuilds(data.filter((x) => x.isActive)));
  }, [selectedId, reload]);
  const selected = items.find((x) => x.releaseId === selectedId),
    term = search.toLowerCase(),
    filteredReleases = items.filter((x) =>
      (!contextProjectId || x.projectId === contextProjectId) &&
      `${x.releaseCode} ${x.version} ${x.releaseType ?? ""} ${x.status}`
        .toLowerCase()
        .includes(term),
    ),
    filteredBuilds = builds.filter((x) =>
      `${x.buildNumber} ${x.applicationVersion ?? ""} ${x.commitReference ?? ""}`
        .toLowerCase()
        .includes(term),
    );
  useEffect(() => {
    if (contextProjectId && filteredReleases.length && !filteredReleases.some((x) => x.releaseId === selectedId)) {
      setSelectedId(filteredReleases[0].releaseId);
    }
  }, [contextProjectId, filteredReleases, selectedId]);
  const openRelease = (item?: ReleaseItem) => {
    setEditRelease(item ?? null);
    setEditBuild(null);
    setProjectId(item?.projectId ?? projects[0]?.projectId ?? "");
    const targetProjectId = item?.projectId ?? projects[0]?.projectId ?? "";
    const project = projects.find((x) => x.projectId === targetProjectId);
    setCode(
      item?.releaseCode ??
        nextBusinessCode(
          `${project?.projectCode ?? "PRJ"}-REL`,
          allItems
            .filter((x) => x.projectId === targetProjectId)
            .map((x) => x.releaseCode),
        ),
    );
    setName(item?.version ?? "");
    setType(item?.releaseType ?? releaseTypes[0]?.value ?? "");
    setReleaseStatus(item?.status ?? "Draft");
    setDate(item?.plannedReleaseDate?.slice(0, 10) ?? "");
    setDetails(item?.scope ?? "");
    setModal("release");
  };
  const openBuild = (item?: BuildItem) => {
    setEditBuild(item ?? null);
    setEditRelease(null);
    setCode(item?.buildNumber ?? "");
    setName(item?.applicationVersion ?? "");
    setBuildStatus(item?.status ?? "Ready");
    setPackageVersion(item?.packageVersion ?? "");
    setCommit(item?.commitReference ?? "");
    setDate(item?.buildDate?.slice(0, 10) ?? "");
    setDetails(item?.changeNotes ?? "");
    setIssues(item?.knownIssues ?? "");
    setModal("build");
  };
  const save = async () => {
    if (!modal) return;
    setSaving(true);
    try {
      let url = "",
        method = "POST",
        body: object;
      if (modal === "release") {
        url = editRelease
          ? `${apiUrl}/releases/${editRelease.releaseId}`
          : `${apiUrl}/projects/${projectId}/releases`;
        method = editRelease ? "PUT" : "POST";
        body = editRelease
          ? {
              version: name,
              releaseType: type,
              plannedReleaseDate: date || null,
              scope: details || null,
              releaseOwnerUserId: null,
            }
          : {
              releaseCode: "",
              version: name,
              releaseType: type,
              plannedReleaseDate: date || null,
              scope: details || null,
              releaseOwnerUserId: null,
            };
      } else {
        url = editBuild
          ? `${apiUrl}/builds/${editBuild.buildId}`
          : `${apiUrl}/releases/${selectedId}/builds`;
        method = editBuild ? "PUT" : "POST";
        body = editBuild
          ? {
              applicationVersion: name || null,
              packageVersion: packageVersion || null,
              commitReference: commit || null,
              buildDate: date || null,
              changeNotes: details || null,
              knownIssues: issues || null,
            }
          : {
              buildNumber: code,
              applicationVersion: name || null,
              packageVersion: packageVersion || null,
              commitReference: commit || null,
              buildDate: date || null,
              changeNotes: details || null,
              knownIssues: issues || null,
            };
      }
      const response = await fetch(url, {
        method,
        headers,
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        const p = await response.json();
        throw new Error(p.detail ?? "บันทึกไม่สำเร็จ");
      }
      if (
        modal === "release" &&
        editRelease &&
        releaseStatus !== editRelease.status
      ) {
        const statusResponse = await fetch(
          `${apiUrl}/releases/${editRelease.releaseId}/status`,
          {
            method: "POST",
            headers,
            body: JSON.stringify({ status: releaseStatus }),
          },
        );
        if (!statusResponse.ok) {
          const problem = await statusResponse.json();
          throw new Error(problem.detail ?? "เปลี่ยนสถานะ Release ไม่สำเร็จ");
        }
      }
      if (modal === "build" && editBuild && buildStatus !== editBuild.status) {
        const statusResponse = await fetch(
          `${apiUrl}/builds/${editBuild.buildId}/status`,
          {
            method: "POST",
            headers,
            body: JSON.stringify({ status: buildStatus }),
          },
        );
        if (!statusResponse.ok) {
          const problem = await statusResponse.json();
          throw new Error(problem.detail ?? "เปลี่ยนสถานะ Build ไม่สำเร็จ");
        }
      }
      setModal(null);
      setReload((x) => x + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ");
    } finally {
      setSaving(false);
    }
  };
  const remove = async (
    kind: "release" | "build",
    id: string,
    label: string,
  ) => {
    if (
      !window.confirm(
        `ยืนยันลบ ${label}? ข้อมูลจะถูกปิดใช้งานและไม่แสดงในรายการ`,
      )
    )
      return;
    const response = await fetch(
      `${apiUrl}/${kind === "release" ? "releases" : "builds"}/${id}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) {
      window.alert("ไม่สามารถลบข้อมูลได้");
      return;
    }
    setReload((x) => x + 1);
  };
  const markRc = async (item: BuildItem) => {
    await fetch(`${apiUrl}/builds/${item.buildId}/mark-release-candidate`, {
      method: "POST",
      headers,
    });
    setReload((x) => x + 1);
  };
  if (loading)
    return (
      <article className="card empty">
        <p>กำลังโหลดข้อมูล Release...</p>
      </article>
    );
  if (error)
    return (
      <article className="card empty">
        <div className="login-error">{error}</div>
      </article>
    );
  return (
    <div className="release-layout">
      <article className="card release-list">
        <div className="card-title">
          <div>
            <h3>Releases</h3>
            <p>{filteredReleases.length} Release ที่ใช้งาน</p>
          </div>
          {canEdit && (
            <button className="btn primary" onClick={() => openRelease()}>
              + Release
            </button>
          )}
        </div>
        <div className="release-cards">
          {filteredReleases.map((x) => (
            <div key={x.releaseId} className={`release-card${selectedId === x.releaseId ? " active" : ""}`}>
              <button className="release-card-select" aria-label={`เลือก ${x.releaseCode} Version ${x.version}`} aria-pressed={selectedId === x.releaseId} onClick={() => setSelectedId(x.releaseId)}>
                <span>{x.releaseCode}</span>
                <b>Version {x.version}</b>
                <small><span>{x.releaseType || "ไม่ระบุประเภท"}</span><span>{x.plannedReleaseDate ? new Date(x.plannedReleaseDate).toLocaleDateString("th-TH") : "ไม่ระบุวัน"}</span></small>
                <Badge tone={x.status === "Ready" || x.status === "Released" ? "green" : "yellow"}>{x.status}</Badge>
              </button>
              <button className="release-card-detail" aria-label={`ดูรายละเอียด ${x.releaseCode}`} onClick={() => setReleaseDetail(x)}>รายละเอียด <span aria-hidden="true">›</span></button>
            </div>
          ))}
        </div>
      </article>
      <article className="card build-panel">
        <div className="card-title">
          <div>
            <h3>Builds {selected && <span>· {selected.releaseCode}</span>}</h3>
            <p>{filteredBuilds.length} Build ใน Release ที่เลือก</p>
          </div>
          {selected && (
            <div className="row-actions">
              <button className="btn" onClick={() => setReleaseDetail(selected)}>
                รายละเอียด Release
              </button>
              {canEdit && <><button className="btn" onClick={() => openRelease(selected)}>
                  แก้ไข Release
                </button>
                <button className="btn primary" onClick={() => openBuild()}>
                  + Build
                </button></>}
            </div>
          )}
        </div>
        {selected ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Build Number</th>
                  <th>App Version</th>
                  <th>Package</th>
                  <th>Commit</th>
                  <th>Build Date</th>
                  <th>Status</th>
                  {canEdit && <th>จัดการ</th>}
                </tr>
              </thead>
              <tbody>
                {filteredBuilds.map((x) => (
                  <tr key={x.buildId}>
                    <td>
                      <button className="release-build-link" onClick={() => setBuildDetail(x)}>{x.buildNumber}</button>
                      {x.isReleaseCandidate && <Badge tone="blue">RC</Badge>}
                    </td>
                    <td>{x.applicationVersion || "-"}</td>
                    <td>{x.packageVersion || "-"}</td>
                    <td>{x.commitReference || "-"}</td>
                    <td>
                      {x.buildDate
                        ? new Date(x.buildDate).toLocaleDateString("th-TH")
                        : "-"}
                    </td>
                    <td>
                      <Badge tone="green">{x.status}</Badge>
                    </td>
                    {canEdit && (
                      <td>
                        <div className="row-actions">
                          <button
                            className="table-action"
                            onClick={() => openBuild(x)}
                          >
                            แก้ไข
                          </button>
                          {!x.isReleaseCandidate && (
                            <button
                              className="table-action"
                              onClick={() => markRc(x)}
                            >
                              Mark RC
                            </button>
                          )}
                          <button
                            className="table-action danger-action"
                            onClick={() =>
                              remove("build", x.buildId, x.buildNumber)
                            }
                          >
                            ลบ
                          </button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="empty">
            <p>เลือก Release เพื่อดู Builds</p>
          </div>
        )}
        {canEdit && selected && (
          <div className="project-footer">
            <button
              className="danger-link"
              onClick={() =>
                remove("release", selected.releaseId, selected.releaseCode)
              }
            >
              ยกเลิกและซ่อน Release นี้
            </button>
          </div>
        )}
      </article>
      {releaseDetail && (
        <div className="modal" role="dialog" aria-modal="true" aria-labelledby="release-detail-title" onMouseDown={() => setReleaseDetail(null)}>
          <div className="modal-box release-build-detail" onMouseDown={(e) => e.stopPropagation()}>
            <div className="modal-head"><div><h2 id="release-detail-title">รายละเอียด Release</h2><small>{releaseDetail.releaseCode}</small></div><button aria-label="ปิดรายละเอียด Release" onClick={() => setReleaseDetail(null)}>×</button></div>
            <div className="release-detail-hero"><div><span className="release-detail-eyebrow">Release</span><b>{releaseDetail.releaseCode}</b><h3>Version {releaseDetail.version}</h3><div className="release-detail-badges"><Badge tone={releaseDetail.status === "Ready" || releaseDetail.status === "Released" ? "green" : "yellow"}>{releaseDetail.status}</Badge>{releaseDetail.releaseType && <Badge tone="blue">{releaseDetail.releaseType}</Badge>}</div></div><div className="release-date-card"><span aria-hidden="true">◫</span><small>Planned Release</small><b>{releaseDetail.plannedReleaseDate ? new Date(releaseDetail.plannedReleaseDate).toLocaleDateString("th-TH", { day: "numeric", month: "short", year: "numeric" }) : "ไม่ระบุวัน"}</b></div></div>
            <div className="release-detail-meta"><div><span aria-hidden="true">P</span><small>Project<b>{projects.find((x) => x.projectId === releaseDetail.projectId)?.projectName || "-"}</b></small></div><div><span aria-hidden="true">#</span><small>Builds<b>{releaseDetail.releaseId === selectedId ? builds.length : "เลือก Release เพื่อดู"}</b></small></div><div><span aria-hidden="true">S</span><small>Status<b>{releaseDetail.status}</b></small></div></div>
            <section className="release-detail-section"><div className="release-detail-heading"><span aria-hidden="true">≡</span><div><h3>Release Scope</h3><small>ขอบเขตและเป้าหมายของ Release</small></div></div><p>{releaseDetail.scope || "ยังไม่ได้ระบุขอบเขตของ Release"}</p></section>
            <section className="release-detail-section"><div className="release-detail-heading"><span aria-hidden="true">▤</span><div><h3>Builds ใน Release</h3><small>รายการ Build ที่พร้อมใช้งาน</small></div></div>{releaseDetail.releaseId === selectedId && builds.length ? <div className="release-detail-builds">{builds.map((build) => <button key={build.buildId} onClick={() => { setReleaseDetail(null); setBuildDetail(build); }}><span><b>{build.buildNumber}</b><small>{build.applicationVersion || "ไม่ระบุ Application Version"}</small></span><span><Badge tone={build.status === "Ready" ? "green" : "yellow"}>{build.status}</Badge>{build.isReleaseCandidate && <Badge tone="blue">RC</Badge>}<i aria-hidden="true">›</i></span></button>)}</div> : <div className="release-detail-empty">ยังไม่มี Build ที่ใช้งานใน Release นี้</div>}</section>
            <div className="modal-actions"><button className="btn" onClick={() => setReleaseDetail(null)}>ปิด</button>{canEdit && <button className="btn primary" onClick={() => { const item = releaseDetail; setReleaseDetail(null); openRelease(item); }}>แก้ไข Release</button>}</div>
          </div>
        </div>
      )}
      {buildDetail && (
        <div className="modal" role="dialog" aria-modal="true" aria-labelledby="build-detail-title" onMouseDown={() => setBuildDetail(null)}>
          <div className="modal-box release-build-detail build-read-detail" onMouseDown={(e) => e.stopPropagation()}>
            <div className="modal-head"><div><h2 id="build-detail-title">รายละเอียด Build</h2><small>{selected?.releaseCode || "Release"}</small></div><button aria-label="ปิดรายละเอียด Build" onClick={() => setBuildDetail(null)}>×</button></div>
            <div className="build-detail-hero"><div><span className="release-detail-eyebrow">Build Number</span><h3>{buildDetail.buildNumber}</h3><div className="release-detail-badges"><Badge tone={buildDetail.status === "Ready" ? "green" : "yellow"}>{buildDetail.status}</Badge>{buildDetail.isReleaseCandidate && <Badge tone="blue">Release Candidate</Badge>}</div></div><div className="build-version-card"><small>Application Version</small><b>{buildDetail.applicationVersion || "-"}</b><span>Package {buildDetail.packageVersion || "-"}</span></div></div>
            <div className="release-detail-meta build-detail-meta"><div><span aria-hidden="true">◫</span><small>Build Date<b>{buildDetail.buildDate ? new Date(buildDetail.buildDate).toLocaleDateString("th-TH") : "ไม่ระบุ"}</b></small></div><div><span aria-hidden="true">C</span><small>Commit Reference<b>{buildDetail.commitReference || "ไม่ระบุ"}</b></small></div><div><span aria-hidden="true">S</span><small>Status<b>{buildDetail.status}</b></small></div></div>
            <section className="release-detail-section"><div className="release-detail-heading"><span aria-hidden="true">+</span><div><h3>Change Notes</h3><small>รายการเปลี่ยนแปลงใน Build นี้</small></div></div><p>{buildDetail.changeNotes || "ไม่มี Change Notes"}</p></section>
            <section className="release-detail-section known-issues"><div className="release-detail-heading"><span aria-hidden="true">!</span><div><h3>Known Issues</h3><small>ปัญหาที่ทราบและควรระวัง</small></div></div><p>{buildDetail.knownIssues || "ไม่พบ Known Issues"}</p></section>
            <div className="modal-actions"><button className="btn" onClick={() => setBuildDetail(null)}>ปิด</button>{canEdit && <button className="btn primary" onClick={() => { const item = buildDetail; setBuildDetail(null); openBuild(item); }}>แก้ไข Build</button>}</div>
          </div>
        </div>
      )}
      {modal && (
        <div className="modal" onMouseDown={() => setModal(null)}>
          <div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
            <div className="modal-head">
              <h2>
                {editRelease || editBuild ? "แก้ไข" : "เพิ่ม"}{" "}
                {modal === "release" ? "Release" : "Build"}
              </h2>
              <button onClick={() => setModal(null)}>×</button>
            </div>
            <div className="form-grid">
              {modal === "release" && !editRelease && (
                <label>
                  Project
                  <select
                    value={projectId}
                    onChange={(e) => {
                      const value = e.target.value;
                      setProjectId(value);
                      const project = projects.find(
                        (x) => x.projectId === value,
                      );
                      setCode(
                        nextBusinessCode(
                          `${project?.projectCode ?? "PRJ"}-REL`,
                          allItems
                            .filter((x) => x.projectId === value)
                            .map((x) => x.releaseCode),
                        ),
                      );
                    }}
                  >
                    {projects.map((x) => (
                      <option key={x.projectId} value={x.projectId}>
                        {x.projectName}
                      </option>
                    ))}
                  </select>
                </label>
              )}
              <label>
                {modal === "release" ? "Release Code" : "Build Number"}
                <input
                  disabled={modal === "release" || !!editBuild}
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                />
              </label>
              <label>
                {modal === "release" ? "Version" : "Application Version"}
                <input value={name} onChange={(e) => setName(e.target.value)} />
              </label>
              {modal === "release" ? (
                <>
                  <label>
                    Release Type
                    <select
                      value={type}
                      onChange={(e) => setType(e.target.value)}
                    >
                      {masterOptionElements(releaseTypes, type)}
                    </select>
                  </label>
                  {editRelease && (
                    <label>
                      สถานะ Release
                      <select
                        value={releaseStatus}
                        onChange={(e) => setReleaseStatus(e.target.value)}
                      >
                        <option value="Draft">Draft</option>
                        <option value="Testing">Testing</option>
                        <option value="Ready">Ready</option>
                        <option value="Released">Released</option>
                      </select>
                    </label>
                  )}
                  <label>
                    Planned Date
                    <input
                      type="date"
                      value={date}
                      onChange={(e) => setDate(e.target.value)}
                    />
                  </label>
                  <label className="full">
                    Scope
                    <textarea
                      rows={4}
                      value={details}
                      onChange={(e) => setDetails(e.target.value)}
                    />
                  </label>
                </>
              ) : (
                <>
                  {editBuild && (
                    <label>
                      สถานะ Build
                      <select
                        value={buildStatus}
                        onChange={(e) => setBuildStatus(e.target.value)}
                      >
                        <option value="Ready">Ready</option>
                        <option value="Testing">Testing</option>
                        <option value="Passed">Passed</option>
                        <option value="Failed">Failed</option>
                        <option value="Blocked">Blocked</option>
                      </select>
                    </label>
                  )}
                  <label>
                    Package Version
                    <input
                      value={packageVersion}
                      onChange={(e) => setPackageVersion(e.target.value)}
                    />
                  </label>
                  <label>
                    Commit Reference
                    <input
                      value={commit}
                      onChange={(e) => setCommit(e.target.value)}
                    />
                  </label>
                  <label>
                    Build Date
                    <input
                      type="date"
                      value={date}
                      onChange={(e) => setDate(e.target.value)}
                    />
                  </label>
                  <label className="full">
                    Change Notes
                    <textarea
                      rows={3}
                      value={details}
                      onChange={(e) => setDetails(e.target.value)}
                    />
                  </label>
                  <label className="full">
                    Known Issues
                    <textarea
                      rows={3}
                      value={issues}
                      onChange={(e) => setIssues(e.target.value)}
                    />
                  </label>
                </>
              )}
            </div>
            <div className="modal-actions">
              <button className="btn" onClick={() => setModal(null)}>
                ยกเลิก
              </button>
              <button
                className="btn primary"
                disabled={
                  saving ||
                  !code.trim() ||
                  (modal === "release" && !name.trim())
                }
                onClick={save}
              >
                {saving ? "กำลังบันทึก..." : "บันทึก"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
type RequirementItem = {
  requirementId: string;
  projectId: string;
  requirementCode: string;
  title: string;
  description?: string;
  acceptanceCriteria?: string;
  priority: string;
  riskLevel?: string;
  source?: string;
  ownerUserId?: string;
  status: string;
  revisionNo: number;
  isInScope: boolean;
  moduleId: string;
  releaseId?: string;
  testCaseCount?: number;
  createdAt?: string;
};
type RequirementRevisionItem = {
  revisionNo: number;
  title: string;
  description?: string;
  acceptanceCriteria?: string;
  changedBy?: string;
  changedAt: string;
  changeReason?: string;
};
const requirementStatusInformation = [
  { value: "Draft", label: "ฉบับร่าง", meaning: "Requirement ยังอยู่ระหว่างจัดทำและแก้ไขรายละเอียด", impact: "ยังไม่ถือว่าผ่านการตรวจสอบ เหมาะสำหรับเก็บข้อมูลเริ่มต้นก่อนส่งให้ทีม Review" },
  { value: "Review", label: "รอตรวจสอบ", meaning: "Requirement พร้อมให้ผู้เกี่ยวข้องตรวจความครบถ้วนและความถูกต้อง", impact: "ทีมควรตรวจ Description, Acceptance Criteria, Priority และ Scope ก่อนอนุมัติ" },
  { value: "Approved", label: "อนุมัติแล้ว", meaning: "Requirement ผ่านการรับรองและใช้เป็นข้อตกลงอ้างอิงของทีม", impact: "พร้อมนำไปออกแบบ Test Case และวางแผนพัฒนา การแก้สาระสำคัญควรระบุเหตุผลและสร้าง Revision" },
  { value: "Implemented", label: "พัฒนาแล้ว", meaning: "ความสามารถตาม Requirement ถูกนำไปพัฒนาหรือส่งมอบแล้ว", impact: "ไม่ได้หมายความว่าทดสอบผ่านโดยอัตโนมัติ ยังต้องมี Test Case, Execution และผลทดสอบรองรับ" },
  { value: "Cancelled", label: "ยกเลิก", meaning: "Requirement นี้เลิกใช้งานหรือไม่นำไปดำเนินการต่อ", impact: "ระบบยังนับใน RTM/Coverage หากเลือก In Scope อยู่ หากไม่ต้องการให้นับต้องยกเลิก In Scope ด้วย" },
] as const;
function RequirementsPage({
  search,
  refresh,
  canEdit,
  contextProjectId,
}: {
  search: string;
  refresh: number;
  canEdit: boolean;
  contextProjectId?: string;
}) {
  const [items, setItems] = useState<RequirementItem[]>([]),
    [testCaseCounts, setTestCaseCounts] = useState<Record<string, number>>({}),
    [error, setError] = useState(""),
    [loading, setLoading] = useState(true),
    [reload, setReload] = useState(0),
    [editing, setEditing] = useState<RequirementItem | null>(null),
    [users, setUsers] = useState<AdminUser[]>([]),
    [ownerUserId, setOwnerUserId] = useState(""),
    [statusFilter, setStatusFilter] = useState(""),
    [priorityFilter, setPriorityFilter] = useState(""),
    [scopeFilter, setScopeFilter] = useState(""),
    [moduleFilter, setModuleFilter] = useState(""),
    [releaseFilter, setReleaseFilter] = useState(""),
    [filterReleases, setFilterReleases] = useState<ReleaseItem[]>([]),
    [filterModules, setFilterModules] = useState<ModuleItem[]>([]),
    [filterProjects, setFilterProjects] = useState<ProjectItem[]>([]),
    [viewing, setViewing] = useState<RequirementItem | null>(null),
    [viewRelease, setViewRelease] = useState<ReleaseItem | null>(null),
    [historyItem, setHistoryItem] = useState<RequirementItem | null>(null),
    [revisions, setRevisions] = useState<RequirementRevisionItem[]>([]),
    [historyLoading, setHistoryLoading] = useState(false),
    [modules, setModules] = useState<ModuleItem[]>([]),
    [releases, setReleases] = useState<ReleaseItem[]>([]),
    [moduleId, setModuleId] = useState(""),
    [releaseId, setReleaseId] = useState(""),
    [title, setTitle] = useState(""),
    [description, setDescription] = useState(""),
    [criteria, setCriteria] = useState(""),
    [priority, setPriority] = useState("P2"),
    [risk, setRisk] = useState("Medium"),
    [source, setSource] = useState(""),
    [status, setStatus] = useState("Draft"),
    [inScope, setInScope] = useState(true),
    [saving, setSaving] = useState(false);
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
  useEffect(() => {
    fetch(`${apiUrl}/requirements`, {
      headers: {
        Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
      },
    })
      .then(async (r) => {
        if (!r.ok) throw new Error("โหลด Requirement ไม่สำเร็จ");
        return r.json();
      })
      .then(async (data: RequirementItem[] | { items?: { rows: RequirementItem[] }; rows?: RequirementItem[] }) => {
        const rows = Array.isArray(data) ? data : (data as { items?: { rows: RequirementItem[] } }).items?.rows ?? (data as { rows?: RequirementItem[] }).rows ?? [];
        setItems(rows);
        const releaseIds = [...new Set(rows.map((x) => x.releaseId).filter((x): x is string => !!x))];
        const rtmRows = await Promise.all(releaseIds.map((id) => fetch(`${apiUrl}/releases/${id}/rtm`, { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` } }).then((r) => r.ok ? r.json() : [])));
        const rtmItems = rtmRows.map((r: unknown) => Array.isArray(r) ? r : (r as { items?: { rows: unknown[] } }).items?.rows ?? []).flat();
        const counts: Record<string, number> = {};
        rtmItems.forEach((x: RtmItem) => { counts[x.requirementId] = x.testCaseCount; });
        setTestCaseCounts(counts);
      })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [refresh, reload]);
  useEffect(() => {
    fetch(`${apiUrl}/admin/users`, { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` } })
      .then((r) => r.ok ? r.json() : [])
      .then((data: AdminUser[] | { items?: { rows: AdminUser[] } }) => {
        const rows = Array.isArray(data) ? data : (data as { items?: { rows: AdminUser[] } }).items?.rows ?? [];
        setUsers(rows.filter((x) => x.isActive));
      });
  }, []);
  useEffect(() => {
    const authHeaders = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
    fetch(`${apiUrl}/projects`, { headers: authHeaders }).then((r) => r.ok ? r.json() : []).then(async (projects: ProjectItem[]) => {
      setFilterProjects(projects);
      const moduleGroups = await Promise.all(projects.map((project) => fetch(`${apiUrl}/projects/${project.projectId}/modules`, { headers: authHeaders }).then((r) => r.ok ? r.json() : [])));
      setFilterModules(moduleGroups.flat().filter((module: ModuleItem) => module.isActive));
    });
  }, []);
  useEffect(() => {
    setModuleFilter("");
    setReleaseFilter("");
  }, [contextProjectId]);
  useEffect(() => {
    if (!contextProjectId) {
      setFilterReleases([]);
      return;
    }
    fetch(`${apiUrl}/projects/${contextProjectId}/releases`, { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` } })
      .then((r) => r.ok ? r.json() : [])
      .then((data: ReleaseItem[]) => setFilterReleases((data as ReleaseItem[]).filter((x) => x.status !== "Cancelled")))
      .catch(() => setFilterReleases([]));
  }, [contextProjectId]);
  const openEdit = async (item: RequirementItem) => {
    const [moduleData, releaseData] = await Promise.all([
      fetch(`${apiUrl}/projects/${item.projectId}/modules`, { headers }).then((r) => r.json()),
      fetch(`${apiUrl}/projects/${item.projectId}/releases`, { headers }).then((r) => r.json()),
    ]);
    setModules((moduleData as ModuleItem[]).filter((x) => x.isActive || x.moduleId === item.moduleId));
    setReleases((releaseData as ReleaseItem[]).filter((x) => x.status !== "Cancelled" || x.releaseId === item.releaseId));
    setEditing(item);
    setModuleId(item.moduleId);
    setReleaseId(item.releaseId ?? "");
    setTitle(item.title);
    setDescription(item.description ?? "");
    setCriteria(item.acceptanceCriteria ?? "");
    setPriority(item.priority);
    setRisk(item.riskLevel ?? "Medium");
    setSource(item.source ?? "");
    setOwnerUserId(item.ownerUserId ?? "");
    setStatus(item.status);
    setInScope(item.isInScope);
  };
  const saveEdit = async () => {
    if (!editing) return;
    setSaving(true);
    try {
      const response = await fetch(`${apiUrl}/requirements/${editing.requirementId}`, {
        method: "PUT",
        headers,
        body: JSON.stringify({ releaseId: releaseId || null, moduleId, title, description: description || null, acceptanceCriteria: criteria || null, priority, riskLevel: risk || null, source: source || null, ownerUserId: ownerUserId || null, isInScope: inScope }),
      });
      if (!response.ok) throw new Error((await response.json()).detail ?? "แก้ไข Requirement ไม่สำเร็จ");
      if (status !== editing.status) {
        const statusResponse = await fetch(`${apiUrl}/requirements/${editing.requirementId}/status`, { method: "POST", headers, body: JSON.stringify({ status }) });
        if (!statusResponse.ok) throw new Error((await statusResponse.json()).detail ?? "เปลี่ยนสถานะไม่สำเร็จ");
      }
      setEditing(null);
      setReload((x) => x + 1);
    } catch (e) {
      window.alert(e instanceof Error ? e.message : "แก้ไข Requirement ไม่สำเร็จ");
    } finally { setSaving(false); }
  };
  const openHistory = async (item: RequirementItem) => {
    setHistoryItem(item);
    setHistoryLoading(true);
    try {
      const response = await fetch(`${apiUrl}/requirements/${item.requirementId}/revisions`, { headers });
      setRevisions(response.ok ? await response.json() : []);
    } finally { setHistoryLoading(false); }
  };
  const openDetail = async (item: RequirementItem) => {
    setViewing(item);
    setViewRelease(null);
    if (!item.releaseId) return;
    const response = await fetch(`${apiUrl}/projects/${item.projectId}/releases`, { headers });
    if (response.ok) {
      const data: ReleaseItem[] = await response.json();
      setViewRelease(data.find((x) => x.releaseId === item.releaseId) ?? null);
    }
  };
  const remove = async (item: RequirementItem) => {
    if (!window.confirm(`ยืนยันลบ ${item.requirementCode}?\nข้อมูลจะถูกซ่อนและยังเก็บประวัติไว้`)) return;
    const response = await fetch(`${apiUrl}/requirements/${item.requirementId}`, { method: "DELETE", headers });
    if (!response.ok) { window.alert("ลบ Requirement ไม่สำเร็จ"); return; }
    setReload((x) => x + 1);
  };
  const moduleOrderMap = useMemo(() => {
    const scoped = contextProjectId ? filterModules.filter((x) => x.projectId === contextProjectId && x.isActive) : filterModules.filter((x) => x.isActive);
    return new Map(buildModuleTree(scoped).map(({ module }, index) => [module.moduleId, index]));
  }, [filterModules, contextProjectId]);
  const moduleLookup = useMemo(() => new Map(filterModules.map((m) => [m.moduleId, m])), [filterModules]);
  if (loading)
    return (
      <article className="card empty">
        <p>กำลังโหลด Requirement...</p>
      </article>
    );
  if (error)
    return (
      <article className="card empty">
        <div className="login-error">{error}</div>
      </article>
    );
  const scopedItems = contextProjectId ? items.filter((x) => x.projectId === contextProjectId) : items;
  const statusOrder = ["Draft", "Review", "Approved", "Implemented", "Cancelled"];
  const priorityOrder = ["P0", "P1", "P2", "P3"];
  const statusOptions = [
    ...statusOrder.filter((s) => scopedItems.some((x) => x.status === s)),
    ...[...new Set(scopedItems.map((x) => x.status))].filter((s) => !statusOrder.includes(s)).sort(),
  ];
  const priorityOptions = [
    ...priorityOrder.filter((p) => scopedItems.some((x) => x.priority === p)),
    ...[...new Set(scopedItems.map((x) => x.priority))].filter((p) => !priorityOrder.includes(p)).sort(),
  ];
  const countBy = (key: "status" | "priority", value: string) => scopedItems.filter((x) => x[key] === value).length;
  const filtered = items
    .filter((x) =>
      (!contextProjectId || x.projectId === contextProjectId) &&
      `${x.requirementCode} ${x.title} ${x.priority} ${x.status} ${moduleLookup.get(x.moduleId)?.moduleName ?? ""} ${moduleLookup.get(x.moduleId)?.moduleCode ?? ""}`.toLowerCase().includes(search.toLowerCase()) &&
      (!statusFilter || x.status === statusFilter) &&
      (!priorityFilter || x.priority === priorityFilter) &&
      (!scopeFilter || String(x.isInScope) === scopeFilter) &&
      (!releaseFilter || x.releaseId === releaseFilter) &&
      (!moduleFilter || x.moduleId === moduleFilter),
    )
    .sort((a, b) => {
      if (!moduleOrderMap.size) return a.requirementCode.localeCompare(b.requirementCode, undefined, { numeric: true });
      const ia = moduleOrderMap.get(a.moduleId) ?? Number.MAX_SAFE_INTEGER;
      const ib = moduleOrderMap.get(b.moduleId) ?? Number.MAX_SAFE_INTEGER;
      return ia - ib || a.requirementCode.localeCompare(b.requirementCode, undefined, { numeric: true });
    });
  return (
    <article className="card requirement-page-card">
      <div className="table-tools">
        <div>
          <select aria-label="กรองตามสถานะ" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
            <option value="">ทุกสถานะ</option>
            {statusOptions.map((x) => <option key={x} value={x}>{x} ({countBy("status", x)})</option>)}
          </select>
          <select aria-label="กรองตาม Priority" value={priorityFilter} onChange={(e) => setPriorityFilter(e.target.value)}>
            <option value="">ทุก Priority</option>
            {priorityOptions.map((x) => <option key={x} value={x}>{x} ({countBy("priority", x)})</option>)}
          </select>
          <select aria-label="กรองตามขอบเขต" value={scopeFilter} onChange={(e) => setScopeFilter(e.target.value)}>
            <option value="">ทุก Scope</option><option value="true">In Scope</option><option value="false">Out of Scope</option>
          </select>
          {contextProjectId && filterReleases.length > 0 && (
            <select aria-label="กรองตาม Release" value={releaseFilter} onChange={(e) => setReleaseFilter(e.target.value)}>
              <option value="">ทุก Release</option>
              {filterReleases.map((x) => <option key={x.releaseId} value={x.releaseId}>{x.releaseCode} · {x.version}</option>)}
            </select>
          )}
          <select aria-label="กรองตาม Module" value={moduleFilter} onChange={(e) => setModuleFilter(e.target.value)}>
            <option value="">ทุก Module</option>
            {filterProjects.map((project) => {
              const options = renderModuleSelectOptions(filterModules.filter((x) => x.projectId === project.projectId && x.isActive));
              return options.length ? <optgroup key={project.projectId} label={`${project.projectCode ? `${project.projectCode} · ` : ""}${project.projectName}`}>{options}</optgroup> : null;
            })}
          </select>
        </div>
        <span>{filtered.length} Requirements</span>
      </div>
      <div className="table-wrap">
        <table className="requirement-table">
          <thead>
            <tr>
              <th>Requirement ID</th>
              <th>Title</th>
              <th>Module</th>
              <th>Priority</th>
              <th>Risk</th>
              <th>Revision</th>
              <th>In Scope</th>
              <th>Status</th>
              <th>Test Cases</th>
              <th>จัดการ</th>
            </tr>
          </thead>
          <tbody>
            {filtered.map((x) => (
              <tr key={x.requirementId}>
                <td data-label="Requirement">
                  <button className="requirement-id-link" onClick={() => openDetail(x)} aria-label={`ดูรายละเอียด ${x.requirementCode}`}>{x.requirementCode}</button>
                </td>
                <td data-label="Title">{x.title}</td>
                <td data-label="Module">
                  {(() => {
                    const module = moduleLookup.get(x.moduleId);
                    return module ? <small className="requirement-module">{module.moduleCode ? `${module.moduleCode} · ` : ""}{module.moduleName}</small> : <small className="requirement-module">-</small>;
                  })()}
                </td>
                <td data-label="Priority">
                  <Badge
                    tone={
                      x.priority === "P0" || x.priority === "P1"
                        ? "red"
                        : "blue"
                    }
                  >
                    {x.priority}
                  </Badge>
                </td>
                <td data-label="Risk">{x.riskLevel ?? "-"}</td>
                <td data-label="Revision">Rev. {x.revisionNo}</td>
                <td data-label="Scope">{x.isInScope ? "In Scope" : "Out of Scope"}</td>
                <td data-label="Status">
                  <Badge
                    tone={
                      x.status === "Approved" || x.status === "Implemented"
                        ? "green"
                        : "yellow"
                    }
                  >
                    {x.status}
                  </Badge>
                </td>
                <td data-label="Test Cases"><Badge tone={(testCaseCounts[x.requirementId] ?? 0) > 0 ? "green" : "red"}>{testCaseCounts[x.requirementId] ?? 0} Cases</Badge></td>
                <td data-label="จัดการ"><div className="row-actions"><button className="table-action" onClick={() => openHistory(x)}>Revision</button>{canEdit && <><button className="table-action" onClick={() => openEdit(x)}>แก้ไข</button><button className="table-action danger-action" onClick={() => remove(x)}>ลบ</button></>}</div></td>
              </tr>
            ))}
            {filtered.length === 0 && <tr><td colSpan={10}><div className="empty"><p>ไม่พบ Requirement ตามตัวกรองที่เลือก</p></div></td></tr>}
          </tbody>
        </table>
      </div>
      {viewing && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="requirement-detail-title" onMouseDown={() => setViewing(null)}>
        <div className="modal-box requirement-detail-modal" onMouseDown={(e) => e.stopPropagation()}>
          <div className="modal-head"><div><h2 id="requirement-detail-title">รายละเอียด Requirement</h2><small>{viewing.requirementCode}</small></div><button aria-label="ปิดหน้าต่างรายละเอียด" onClick={() => setViewing(null)}>×</button></div>
          <div className="requirement-detail-title"><div className="requirement-detail-hero-copy"><span>Requirement</span><b>{viewing.requirementCode}</b><h3>{viewing.title}</h3><div className="requirement-detail-badges"><Badge tone={viewing.priority === "P0" || viewing.priority === "P1" ? "red" : "blue"}>{viewing.priority}</Badge><Badge tone={viewing.status === "Approved" || viewing.status === "Implemented" ? "green" : "yellow"}>{viewing.status}</Badge></div></div><div className={`requirement-scope-card ${viewing.isInScope ? "in-scope" : "out-scope"}`}><span aria-hidden="true">{viewing.isInScope ? "✓" : "–"}</span><div><small>Release Scope</small><b>{viewing.isInScope ? "In Scope" : "Out of Scope"}</b></div></div></div>
          <dl className="requirement-detail-grid requirement-detail-meta">
            <div><span className="requirement-meta-icon" aria-hidden="true">P</span><span><dt>Project</dt><dd>{filterProjects.find((x) => x.projectId === viewing.projectId)?.projectName ?? "-"}</dd></span></div>
            <div><span className="requirement-meta-icon" aria-hidden="true">M</span><span><dt>Module</dt><dd>{filterModules.find((x) => x.moduleId === viewing.moduleId)?.moduleName ?? "-"}</dd></span></div>
            <div><span className="requirement-meta-icon" aria-hidden="true">R</span><span><dt>Release</dt><dd>{viewRelease ? `${viewRelease.releaseCode} · Version ${viewRelease.version}` : viewing.releaseId ? "กำลังโหลด..." : "ไม่ระบุ Release"}</dd></span></div>
            <div><span className="requirement-meta-icon" aria-hidden="true">#</span><span><dt>Revision</dt><dd>Rev. {viewing.revisionNo}</dd></span></div>
            <div><span className="requirement-meta-icon risk" aria-hidden="true">!</span><span><dt>Risk</dt><dd>{viewing.riskLevel || "ไม่ระบุ"}</dd></span></div>
            <div><span className="requirement-meta-icon" aria-hidden="true">O</span><span><dt>Owner</dt><dd>{users.find((x) => x.userId === viewing.ownerUserId)?.displayName ?? "ไม่ระบุผู้รับผิดชอบ"}</dd></span></div>
          </dl>
          <section className="requirement-detail-section"><div className="requirement-section-heading"><span aria-hidden="true">S</span><div><h3>Source</h3><small>แหล่งที่มาของ Requirement</small></div></div><p className="requirement-detail-copy">{viewing.source || "ไม่ระบุแหล่งที่มา"}</p></section>
          <section className="requirement-detail-section"><div className="requirement-section-heading"><span aria-hidden="true">D</span><div><h3>Description</h3><small>รายละเอียดและขอบเขตของความต้องการ</small></div></div><p className="requirement-detail-copy">{viewing.description || "ไม่มีรายละเอียด"}</p></section>
          <section className="requirement-detail-section criteria"><div className="requirement-section-heading"><span aria-hidden="true">✓</span><div><h3>Acceptance Criteria</h3><small>เงื่อนไขที่ใช้ยืนยันว่า Requirement สำเร็จ</small></div></div><p className="requirement-detail-copy">{viewing.acceptanceCriteria || "ไม่มี Acceptance Criteria"}</p></section>
          <div className={`requirement-detail-status status-${viewing.status.toLowerCase()}`}><span className="information-icon" aria-hidden="true">i</span><div><span className="requirement-status-label">สถานะปัจจุบัน</span><b>{viewing.status} · {requirementStatusInformation.find((x) => x.value === viewing.status)?.label}</b><p>{requirementStatusInformation.find((x) => x.value === viewing.status)?.meaning}</p><small>{requirementStatusInformation.find((x) => x.value === viewing.status)?.impact}</small></div></div>
          <div className="modal-actions"><button className="btn" onClick={() => setViewing(null)}>ปิด</button>{canEdit && <button className="btn primary" onClick={() => { const item = viewing; setViewing(null); openEdit(item); }}>แก้ไข Requirement</button>}</div>
        </div>
      </div>}
      {editing && (
        <div className="modal" onMouseDown={() => setEditing(null)}>
          <div className="modal-box requirement-editor" onMouseDown={(e) => e.stopPropagation()}>
            <div className="modal-head"><h2>แก้ไข Requirement</h2><button onClick={() => setEditing(null)}>×</button></div>
            <div className="form-grid">
              <label>Requirement Code<input value={editing.requirementCode} disabled /></label>
              <label>Module<select value={moduleId} onChange={(e) => setModuleId(e.target.value)}>{renderModuleSelectOptions(modules.filter((x) => x.isActive))}</select></label>
              <label>Release<select value={releaseId} onChange={(e) => setReleaseId(e.target.value)}><option value="">ไม่ระบุ Release</option>{releases.map((x) => <option key={x.releaseId} value={x.releaseId}>{x.releaseCode} · Version {x.version}</option>)}</select></label>
              <label>Title<input value={title} onChange={(e) => setTitle(e.target.value)} /></label>
              <label>Priority<select value={priority} onChange={(e) => setPriority(e.target.value)}>{["P0","P1","P2","P3"].map((x) => <option key={x}>{x}</option>)}</select></label>
              <label>Risk<select value={risk} onChange={(e) => setRisk(e.target.value)}>{["Critical","High","Medium","Low"].map((x) => <option key={x}>{x}</option>)}</select></label>
              <label>Status<select value={status} onChange={(e) => setStatus(e.target.value)}>{["Draft","Review","Approved","Implemented","Cancelled"].map((x) => <option key={x}>{x}</option>)}</select></label>
              <label>Source<input value={source} onChange={(e) => setSource(e.target.value)} /></label>
              <label>Owner<select value={ownerUserId} onChange={(e) => setOwnerUserId(e.target.value)}><option value="">ไม่ระบุผู้รับผิดชอบ</option>{users.map((x) => <option key={x.userId} value={x.userId}>{x.displayName}</option>)}</select></label>
              <label className="check-line"><input type="checkbox" checked={inScope} onChange={(e) => setInScope(e.target.checked)} /> In Scope</label>
              <details className="requirement-status-information full">
                <summary>
                  <span className="information-icon" aria-hidden="true">i</span>
                  <span><b>{status} · {requirementStatusInformation.find((x) => x.value === status)?.label}</b><small>{requirementStatusInformation.find((x) => x.value === status)?.meaning}</small></span>
                  <em>ดูความหมายทั้งหมด</em>
                </summary>
                <div className="requirement-status-list">
                  {requirementStatusInformation.map((item) => <article key={item.value} className={status === item.value ? "active" : ""}>
                    <div><b>{item.value}</b><span>{item.label}</span></div>
                    <p>{item.meaning}</p>
                    <small><strong>ผลต่อการใช้งาน:</strong> {item.impact}</small>
                  </article>)}
                  <p className="status-scope-note"><b>หมายเหตุ:</b> Status ใช้บอกขั้นตอนการทำงานและใช้กรองรายการ ส่วนการนำ Requirement ไปคำนวณ RTM และ Coverage พิจารณาจากช่อง <b>In Scope</b></p>
                </div>
              </details>
              <label className="full">Description<textarea rows={3} value={description} onChange={(e) => setDescription(e.target.value)} /></label>
              <label className="full">Acceptance Criteria<textarea rows={3} value={criteria} onChange={(e) => setCriteria(e.target.value)} /></label>
            </div>
            <div className="modal-actions"><button className="btn" onClick={() => setEditing(null)}>ยกเลิก</button><button className="btn primary" disabled={saving || !title.trim() || !moduleId} onClick={saveEdit}>{saving ? "กำลังบันทึก..." : "บันทึก"}</button></div>
          </div>
        </div>
      )}
      {historyItem && <div className="modal" onMouseDown={() => setHistoryItem(null)}><div className="modal-box requirement-history" onMouseDown={(e) => e.stopPropagation()}><div className="modal-head"><div><h2>Revision History</h2><small>{historyItem.requirementCode} · {historyItem.title}</small></div><button onClick={() => setHistoryItem(null)}>×</button></div>{historyLoading ? <div className="empty"><p>กำลังโหลดประวัติ...</p></div> : <div className="revision-list">{revisions.length === 0 ? <div className="empty"><p>ยังไม่มีประวัติ Revision</p></div> : revisions.map((x) => <article key={x.revisionNo}><div><b>Rev. {x.revisionNo}</b><time>{new Date(x.changedAt).toLocaleString("th-TH")}</time></div><h3>{x.title}</h3><p>{x.changeReason || "ไม่ระบุเหตุผลการเปลี่ยนแปลง"}</p>{x.acceptanceCriteria && <small>Acceptance Criteria: {x.acceptanceCriteria}</small>}</article>)}</div>}<div className="modal-actions"><button className="btn primary" onClick={() => setHistoryItem(null)}>ปิด</button></div></div></div>}
    </article>
  );
}
type TestCaseItem = {
  testCaseId: string;
  projectId: string;
  moduleId: string;
  testCaseCode: string;
  title: string;
  objective?: string;
  preconditions?: string;
  priority: string;
  testType?: string;
  status: string;
  revisionNo: number;
  automationCandidate: boolean;
  ownerUserId?: string;
  steps: {
    stepNo: number;
    action: string;
    testData?: string;
    expectedResult: string;
  }[];
};
type TestCaseRequirement = { requirementId:string; requirementCode:string; title:string; status:string; coverageType?:string };
type TestCaseRevision = { revisionNo:number; changeReason:string; changedBy?:string; changedByName?:string; changedAt:string; steps:TestCaseItem["steps"] };
type UserLookup = { userId:string; displayName:string };
const testCaseStatusInfo = [
  {value:"Draft",label:"ฉบับร่าง",detail:"อยู่ระหว่างออกแบบและยังไม่นำไปนับ Coverage",impact:"แก้ไขได้ และยังไม่พร้อมสำหรับ Execution"},
  {value:"Review",label:"รอตรวจสอบ",detail:"ส่งให้ผู้เกี่ยวข้องตรวจความครบถ้วนของขั้นตอน",impact:"เชื่อม Requirement ได้ แต่ RTM จะแสดง Partial"},
  {value:"Ready",label:"พร้อมใช้งาน",detail:"ผ่านการตรวจและพร้อมนำไปจัด Suite หรือ Execution",impact:"Test Case ที่เชื่อมจะทำให้ Requirement เป็น Covered"},
  {value:"Deprecated",label:"เลิกใช้งาน",detail:"เก็บไว้เพื่อประวัติและไม่ควรนำไปใช้รอบใหม่",impact:"ไม่ควรเพิ่มเข้า Test Suite หรือ Cycle ใหม่"},
];
type GeneratedTestCase = { title:string; objective:string; preconditions:string; priority:string; testType:string; automationCandidate:boolean; steps:{ stepNo:number; action:string; testData?:string; expectedResult:string }[] };
function TestCasesPage({
  search,
  canEdit,
  contextProjectId,
}: {
  search: string;
  canEdit: boolean;
  contextProjectId?: string;
}) {
  const masterOptions = useMasterOptions(), testCasePriorities = masterOptions("TestCasePriority"), testCaseTypes = masterOptions("TestCaseType");
  const [items, setItems] = useState<TestCaseItem[]>([]),
    [projects, setProjects] = useState<ProjectItem[]>([]),
    [modules, setModules] = useState<ModuleItem[]>([]),
    [filterModules,setFilterModules]=useState<ModuleItem[]>([]),
    [loading, setLoading] = useState(true),
    [totalCount,setTotalCount]=useState(0),
    [reload, setReload] = useState(0),
    [form, setForm] = useState(false),
    [editing, setEditing] = useState<TestCaseItem | null>(null),
    [saving, setSaving] = useState(false),
    [statusFilter, setStatusFilter] = useState(""),
    [projectFilter,setProjectFilter]=useState(""),[moduleFilter,setModuleFilter]=useState(""),
    [priorityFilter,setPriorityFilter]=useState(""),[typeFilter,setTypeFilter]=useState(""),[automationFilter,setAutomationFilter]=useState(""),
    [users,setUsers]=useState<UserLookup[]>([]),[ownerUserId,setOwnerUserId]=useState(""),
    [error,setError]=useState(""),[notice,setNotice]=useState(""),[page,setPage]=useState(1),[pageSize,setPageSize]=useState(10),
    [detail,setDetail]=useState<TestCaseItem|null>(null),[detailRequirements,setDetailRequirements]=useState<TestCaseRequirement[]>([]),
    [revisions,setRevisions]=useState<TestCaseRevision[]>([]),[confirmDelete,setConfirmDelete]=useState<TestCaseItem|null>(null),
    [importing,setImporting]=useState(false),[templateDownloading,setTemplateDownloading]=useState(false),
    [testCaseAiModal,setTestCaseAiModal]=useState(false),
    [testCaseAiPrompt,setTestCaseAiPrompt]=useState(""),
    [testCaseAiFiles,setTestCaseAiFiles]=useState<File[]>([]),
    [testCaseAiGenerating,setTestCaseAiGenerating]=useState(false),
    [testCaseAiError,setTestCaseAiError]=useState(""),
    [caseAiDrafts,setCaseAiDrafts]=useState<GeneratedTestCase[]>([]),
    [caseAiExpanded,setCaseAiExpanded]=useState<number|undefined>(undefined);
  const [projectId, setProjectId] = useState(""),
    [moduleId, setModuleId] = useState(""),
    [code, setCode] = useState(""),
    [title, setTitle] = useState(""),
    [objective, setObjective] = useState(""),
    [preconditions, setPreconditions] = useState(""),
    [priority, setPriority] = useState(""),
    [testType, setTestType] = useState(""),
    [automation, setAutomation] = useState(false),
    [status, setStatus] = useState("Draft"),
    [changeReason, setChangeReason] = useState(""),
    [steps, setSteps] = useState([
      { stepNo: 1, action: "", testData: "", expectedResult: "" },
    ]);
  const [debouncedSearch, setDebouncedSearch] = useState(search);
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(timer);
  }, [search]);
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
  useEffect(() => {
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    setLoading(true); setError("");
    const readJson=(url:string)=>fetch(url,{headers:h}).then(async r=>{if(!r.ok)throw new Error((await r.text())||`HTTP ${r.status}`);return r.json();});
    Promise.all([readJson(`${apiUrl}/projects`),readJson(`${apiUrl}/lookups/users`)])
      .then(([projectData, userData]) => {
        setUsers(userData);
        const activeProjects = (projectData as ProjectItem[]).filter(
          (x) => x.isActive,
        );
        setProjects(activeProjects);
        setProjectId(
          (current) => current || activeProjects[0]?.projectId || "",
        );
      }).catch(e=>setError(e instanceof Error?e.message:"โหลดข้อมูล Test Case ไม่สำเร็จ"))
      .finally(() => setLoading(false));
  }, [reload]);
  useEffect(() => {
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    let cancelled = false;
    setLoading(true);
    const params = new URLSearchParams({ page: String(page), size: String(pageSize) });
    if (projectFilter) params.set("projectId", projectFilter);
    if (moduleFilter) params.set("moduleId", moduleFilter);
    if (statusFilter) params.set("status", statusFilter);
    if (priorityFilter) params.set("priority", priorityFilter);
    if (typeFilter) params.set("testType", typeFilter);
    if (automationFilter) params.set("automation", automationFilter === "yes" ? "true" : "false");
    if (debouncedSearch.trim()) params.set("search", debouncedSearch.trim());
    fetch(`${apiUrl}/test-cases?${params}`, { headers: h })
      .then(async r => { if (!r.ok) throw new Error((await r.text()) || `HTTP ${r.status}`); return r.json(); })
      .then((body) => {
        if (cancelled) return;
        setItems(Array.isArray(body) ? body : body?.rows ?? []);
        setTotalCount(Array.isArray(body) ? body.length : body?.total ?? 0);
      })
      .catch(e => { if (!cancelled) setError(e instanceof Error ? e.message : "โหลดข้อมูล Test Case ไม่สำเร็จ"); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [reload, page, pageSize, projectFilter, moduleFilter, statusFilter, priorityFilter, typeFilter, automationFilter, debouncedSearch]);
  useEffect(() => {
    if (!projectId) {
      setModules([]);
      return;
    }
    fetch(`${apiUrl}/projects/${projectId}/modules`, {
      headers: {
        Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
      },
    })
      .then((r) => r.json())
      .then((data: ModuleItem[]) => {
        const active = data.filter((x) => x.isActive);
        setModules(active);
        setModuleId((current) =>
          active.some((x) => x.moduleId === current)
            ? current
            : active[0]?.moduleId || "",
        );
      });
  }, [projectId]);
  useEffect(()=>{
    if(!projects.length){setFilterModules([]);return;}
    let cancelled=false;
    const targets=projectFilter?projects.filter(x=>x.projectId===projectFilter):projects;
    const authHeaders={Authorization:`Bearer ${localStorage.getItem("qa.accessToken")}`};
    Promise.all(targets.map(project=>fetch(`${apiUrl}/projects/${project.projectId}/modules`,{headers:authHeaders}).then(async response=>response.ok?response.json():Promise.reject(new Error(`โหลด Module ของ ${project.projectName} ไม่สำเร็จ`)))))
      .then(groups=>{if(!cancelled)setFilterModules((groups.flat() as ModuleItem[]).filter(x=>x.isActive));})
      .catch(error=>{if(!cancelled){setFilterModules([]);setError(error instanceof Error?error.message:"โหลดข้อมูล Module ไม่สำเร็จ");}});
    return()=>{cancelled=true};
  },[projects,projectFilter,reload]);
  useEffect(() => { setProjectFilter(contextProjectId ?? ""); setModuleFilter(""); setPage(1); }, [contextProjectId]);
  useEffect(() => {
    if (!form || editing || !projectId || !moduleId) return;
    const project = projects.find((x) => x.projectId === projectId);
    const module = modules.find((x) => x.moduleId === moduleId);
    setCode(
      nextBusinessCode(
        contextualCode(
          project?.projectCode ?? "PRJ",
          module?.moduleCode ?? "MOD",
          "TC",
        ),
        items.map((x) => x.testCaseCode),
      ),
    );
  }, [form, editing, projectId, moduleId, projects, modules, items]);
  const openForm = (item?: TestCaseItem) => {
    setEditing(item ?? null);
    setProjectId(item?.projectId ?? projects[0]?.projectId ?? "");
    setModuleId(item?.moduleId ?? "");
    const targetProjectId = item?.projectId ?? projects[0]?.projectId ?? "";
    const targetModuleId = item?.moduleId ?? modules[0]?.moduleId ?? "";
    const project = projects.find((x) => x.projectId === targetProjectId);
    const module = modules.find((x) => x.moduleId === targetModuleId);
    setCode(
      item?.testCaseCode ??
        nextBusinessCode(
          contextualCode(
            project?.projectCode ?? "PRJ",
            module?.moduleCode ?? "MOD",
            "TC",
          ),
          items.map((x) => x.testCaseCode),
        ),
    );
    setTitle(item?.title ?? "");
    setObjective(item?.objective ?? "");
    setPreconditions(item?.preconditions ?? "");
    setPriority(item?.priority ?? testCasePriorities[0]?.value ?? "");
    setTestType(item?.testType ?? testCaseTypes[0]?.value ?? "");
    setAutomation(item?.automationCandidate ?? false);
    setOwnerUserId(item?.ownerUserId ?? "");
    setStatus(item?.status ?? "Draft");
    setChangeReason(item ? "ปรับปรุงข้อมูล Test Case" : "");
    setSteps(
      item?.steps.length
        ? item.steps.map((x) => ({ ...x, testData: x.testData ?? "" }))
        : [{ stepNo: 1, action: "", testData: "", expectedResult: "" }],
    );
    setForm(true);
  };
  const updateStep = (
    index: number,
    field: "action" | "testData" | "expectedResult",
    value: string,
  ) =>
    setSteps((current) =>
      current.map((x, i) => (i === index ? { ...x, [field]: value } : x)),
    );
  const save = async () => {
    setSaving(true);
    try {
      const body = editing
        ? {
            moduleId,
            title,
            objective: objective || null,
            preconditions: preconditions || null,
            priority,
            testType,
            automationCandidate: automation,
            ownerUserId: ownerUserId || null,
            changeReason: changeReason.trim(),
            steps,
          }
        : {
            projectId,
            moduleId,
            testCaseCode: "",
            title,
            objective: objective || null,
            preconditions: preconditions || null,
            priority,
            testType,
            automationCandidate: automation,
            ownerUserId: ownerUserId || null,
            steps,
          };
      const response = await fetch(
        `${apiUrl}/test-cases${editing ? `/${editing.testCaseId}` : ""}`,
        {
          method: editing ? "PUT" : "POST",
          headers,
          body: JSON.stringify(body),
        },
      );
      if (!response.ok) {
        const problem = await response.json();
        throw new Error(problem.detail ?? "บันทึก Test Case ไม่สำเร็จ");
      }
      if (editing && status !== editing.status) {
        const statusResponse = await fetch(
          `${apiUrl}/test-cases/${editing.testCaseId}/status`,
          { method: "POST", headers, body: JSON.stringify({ status }) },
        );
        if (!statusResponse.ok) {
          const problem = await statusResponse.json();
          throw new Error(problem.detail ?? "เปลี่ยนสถานะไม่สำเร็จ");
        }
      }
      setForm(false); setNotice(editing ? "บันทึกและสร้าง Revision ใหม่แล้ว" : "เพิ่ม Test Case แล้ว");
      setReload((x) => x + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ");
    } finally {
      setSaving(false);
    }
  };
  const remove = async (item: TestCaseItem) => {
    const response = await fetch(`${apiUrl}/test-cases/${item.testCaseId}`, {
      method: "DELETE",
      headers,
    });
    if (!response.ok) {
      setError("ลบ Test Case ไม่สำเร็จ");
      return;
    }
    setConfirmDelete(null);setNotice(`ลบ ${item.testCaseCode} แล้ว`);setReload((x) => x + 1);
  };
  const openDetail=async(item:TestCaseItem)=>{try{const h={Authorization:`Bearer ${localStorage.getItem("qa.accessToken")}`};const read=(url:string)=>fetch(url,{headers:h}).then(r=>r.ok?r.json():null);const [full,reqs,history]=await Promise.all([read(`${apiUrl}/test-cases/${item.testCaseId}`),read(`${apiUrl}/test-cases/${item.testCaseId}/requirements`),read(`${apiUrl}/test-cases/${item.testCaseId}/revisions`)]);setDetail(full?{...item,...full}:item);setDetailRequirements(Array.isArray(reqs)?reqs:[]);setRevisions(Array.isArray(history)?history:[]);}catch{setDetail(item);}};
  const cloneCase=async(item:TestCaseItem)=>{try{const r=await fetch(`${apiUrl}/test-cases/${item.testCaseId}/clone`,{method:"POST",headers});if(!r.ok)throw new Error("คัดลอก Test Case ไม่สำเร็จ");setNotice(`สร้างสำเนาจาก ${item.testCaseCode} แล้ว`);setReload(x=>x+1);}catch(e){setError(e instanceof Error?e.message:"คัดลอกไม่สำเร็จ");}};
  const importFile=async(file:File)=>{setImporting(true);setError("");try{const data=new FormData();data.append("file",file);data.append("projectId",projectFilter||projects[0]?.projectId||"");const r=await fetch(`${apiUrl}/test-cases/import`,{method:"POST",headers:{Authorization:`Bearer ${localStorage.getItem("qa.accessToken")}`},body:data});if(!r.ok)throw new Error((await r.text())||"นำเข้าข้อมูลไม่สำเร็จ");const result=await r.json();setNotice(`นำเข้าสำเร็จ ${result.imported} รายการ${result.failed?` ไม่สำเร็จ ${result.failed} รายการ`:""}`);setReload(x=>x+1);}catch(e){setError(e instanceof Error?e.message:"นำเข้าข้อมูลไม่สำเร็จ");}finally{setImporting(false);}};
  const downloadImportTemplate=async()=>{const selectedProjectId=projectFilter||projects[0]?.projectId||"";if(!selectedProjectId){setError("กรุณาเลือก Project ก่อนดาวน์โหลด Template");return;}setTemplateDownloading(true);setError("");try{const response=await fetch(`${apiUrl}/test-cases/import-template?projectId=${selectedProjectId}`,{headers:{Authorization:`Bearer ${localStorage.getItem("qa.accessToken")}`}});if(!response.ok){const problem=await response.json().catch(()=>null);throw new Error(problem?.detail??"ดาวน์โหลด Template ไม่สำเร็จ");}const blob=await response.blob();const url=URL.createObjectURL(blob);const link=document.createElement("a");link.href=url;link.download="TestCase_Import_Template.xlsx";link.click();URL.revokeObjectURL(url);}catch(e){setError(e instanceof Error?e.message:"ดาวน์โหลด Template ไม่สำเร็จ");}finally{setTemplateDownloading(false);}};
  const openTestCaseAi=()=>{setProjectId(projectFilter||projectId||projects[0]?.projectId||"");setModuleId(moduleFilter||moduleId||"");setTestCaseAiPrompt("");setTestCaseAiFiles([]);setTestCaseAiError("");setCaseAiDrafts([]);setCaseAiExpanded(undefined);setTestCaseAiModal(true);};
  const addTestCaseAiFiles=(selected:File[])=>{const next=[...testCaseAiFiles,...selected].slice(0,5);if(next.reduce((sum,file)=>sum+file.size,0)>20_000_000){setTestCaseAiError("ขนาดไฟล์รวมต้องไม่เกิน 20 MB");return;}setTestCaseAiError(selected.length+testCaseAiFiles.length>5?"แนบไฟล์ได้ไม่เกิน 5 ไฟล์":"");setTestCaseAiFiles(next);};
  const generateTestCaseWithAi=async()=>{
    if(!projectId||!moduleId||!testCaseAiPrompt.trim())return;
    setTestCaseAiGenerating(true);setTestCaseAiError("");
    try{
      const body=new FormData();body.append("prompt",testCaseAiPrompt.trim());body.append("projectName",projects.find(x=>x.projectId===projectId)?.projectName??"");body.append("moduleName",modules.find(x=>x.moduleId===moduleId)?.moduleName??"");body.append("moduleId",moduleId);body.append("projectId",projectId);testCaseAiFiles.forEach(file=>body.append("files",file));
      const response=await fetch(`${apiUrl}/test-cases/generate-ai`,{method:"POST",headers:{Authorization:`Bearer ${localStorage.getItem("qa.accessToken")}`},body});
      if(!response.ok){const problem=await response.json().catch(()=>null);throw new Error(problem?.detail??"AI Generate Test Case ไม่สำเร็จ");}
      const drafts:GeneratedTestCase[]=await response.json();
      if(!Array.isArray(drafts)||!drafts.length)throw new Error("AI ไม่ได้สร้าง Test Case กลับมา");
      setCaseAiDrafts(drafts);setCaseAiExpanded(0);
    }catch(e){setTestCaseAiError(e instanceof Error?e.message:"AI Generate Test Case ไม่สำเร็จ");}finally{setTestCaseAiGenerating(false);}
  };
  const removeCaseAiDraft=(index:number)=>setCaseAiDrafts(drafts=>{const next=drafts.filter((_,i)=>i!==index);if(next.length===0){setTestCaseAiModal(false);}return next;});
  const saveAllCaseDrafts=async()=>{if(!caseAiDrafts.length)return;setTestCaseAiGenerating(true);setTestCaseAiError("");try{let created=0;for(const draft of caseAiDrafts){const priority=testCasePriorities.some(x=>x.value===draft.priority)?draft.priority:(testCasePriorities[0]?.value??"");const testType=testCaseTypes.some(x=>x.value===draft.testType)?draft.testType:(testCaseTypes[0]?.value??"");const res=await fetch(`${apiUrl}/test-cases`,{method:"POST",headers,body:JSON.stringify({projectId,moduleId,testCaseCode:"",title:draft.title,objective:draft.objective,preconditions:draft.preconditions,priority,testType,automationCandidate:draft.automationCandidate,ownerUserId:null,steps:draft.steps.map((x,i)=>({stepNo:i+1,action:x.action,testData:x.testData??"",expectedResult:x.expectedResult}))})});if(!res.ok){const problem=await res.json().catch(()=>null);throw new Error(`สร้าง Test Case "${draft.title}" ไม่สำเร็จ: ${problem?.detail??""}`);}await res.json();created++;}setCaseAiDrafts([]);setTestCaseAiModal(false);setReload(x=>x+1);}catch(e){setTestCaseAiError(e instanceof Error?e.message:"บันทึก Test Case ไม่สำเร็จ");}finally{setTestCaseAiGenerating(false);}};
  if (loading && !items.length)
    return (
      <article className="card empty">
        <p>กำลังโหลด Test Case...</p>
      </article>
    );
  const rows = items;
  const pageCount=Math.max(1,Math.ceil(totalCount/pageSize));
  const pagedRows=rows;
  const moduleFilterGroups=projects.filter(project=>!projectFilter||project.projectId===projectFilter).map(project=>({
    project,
    ordered:buildModuleTree(filterModules.filter(module=>module.projectId===project.projectId)),
  })).filter(group=>group.ordered.length);
  return (
    <>
      <article className="card">
        {error&&<div className="inline-alert error"><span>{error}</span><button onClick={()=>{setError("");setReload(x=>x+1)}}>ลองใหม่</button></div>}
        {notice&&<div className="inline-alert success"><span>{notice}</span><button onClick={()=>setNotice("")}>×</button></div>}
        <div className="testcase-toolbar">
          <div className="testcase-toolbar-head">
            <div className="testcase-result-count"><strong>{totalCount.toLocaleString()}</strong><span>Test Cases</span></div>
            <div className="testcase-toolbar-actions">
              {canEdit&&<button className="btn ai-button" onClick={openTestCaseAi}><span aria-hidden="true">✦</span> AI Generate</button>}
              {canEdit&&<button className="btn" disabled={templateDownloading||projects.length===0} onClick={downloadImportTemplate}>{templateDownloading?"กำลังดาวน์โหลด...":"↓ Template"}</button>}
              {canEdit&&<label className="btn import-button">{importing?"กำลังนำเข้า...":"↑ Import"}<small>CSV/XLSX</small><input type="file" accept=".csv,.xlsx" disabled={importing} onChange={e=>{const file=e.target.files?.[0];if(file)void importFile(file);e.target.value=""}} /></label>}
              {canEdit&&<button className="btn primary" onClick={()=>openForm()}>+ Test Case</button>}
            </div>
          </div>
          <div className="testcase-filter-section">
            <span className="testcase-filter-label">ตัวกรอง</span>
            <div className="testcase-filters">
            <select value={projectFilter} onChange={e=>{setProjectFilter(e.target.value);setModuleFilter("");setPage(1)}}><option value="">ทุก Project</option>{projects.map(x=><option key={x.projectId} value={x.projectId}>{x.projectName}</option>)}</select>
            <select className="testcase-module-filter" value={moduleFilter} onChange={e=>{setModuleFilter(e.target.value);setPage(1)}} disabled={!moduleFilterGroups.length}><option value="">ทุก Module</option>{moduleFilterGroups.map(({project,ordered})=><optgroup key={project.projectId} label={`${project.projectCode} · ${project.projectName}`}>{renderModuleSelectOptions(ordered.map(entry=>entry.module))}</optgroup>)}</select>
            <select
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
            >
              <option value="">ทุกสถานะ</option>
              <option>Draft</option>
              <option>Review</option>
              <option>Ready</option>
              <option>Deprecated</option>
            </select>
            <select value={priorityFilter} onChange={e=>{setPriorityFilter(e.target.value);setPage(1)}}><option value="">ทุก Priority</option>{testCasePriorities.map(x=><option key={x.value} value={x.value}>{x.displayName}</option>)}</select>
            <select value={typeFilter} onChange={e=>{setTypeFilter(e.target.value);setPage(1)}}><option value="">ทุก Type</option>{testCaseTypes.map(x=><option key={x.value} value={x.value}>{x.displayName}</option>)}</select>
            <select value={automationFilter} onChange={e=>{setAutomationFilter(e.target.value);setPage(1)}}><option value="">ทุก Automation</option><option value="yes">Automation Candidate</option><option value="no">Manual</option></select>
            </div>
          </div>
        </div>
        <div className="table-wrap">
          <table className="testcase-list-table">
            <thead>
              <tr>
                <th>Test Case ID</th>
                <th>Title</th>
                <th>Priority</th>
                <th>Type</th>
                <th>Revision</th>
                <th>Steps</th>
                <th>Status</th>
                {canEdit && <th>จัดการ</th>}
              </tr>
            </thead>
            <tbody>
              {pagedRows.map((x) => (
                <tr key={x.testCaseId}>
                  <td data-label="Test Case ID">
                    <button className="link-button" onClick={()=>openDetail(x)}>{x.testCaseCode}</button>
                  </td>
                  <td data-label="Title">{x.title}</td>
                  <td data-label="Priority">
                    <Badge
                      tone={
                        x.priority === "P0" || x.priority === "P1"
                          ? "red"
                          : "blue"
                      }
                    >
                      {x.priority}
                    </Badge>
                  </td>
                  <td data-label="Type">{x.testType ?? "-"}</td>
                  <td data-label="Revision">Rev. {x.revisionNo}</td>
                  <td data-label="Steps">{(x as any).steps?.length ?? (x as any).stepCount ?? 0}</td>
                  <td data-label="Status">
                    <Badge tone={x.status === "Ready" ? "green" : "yellow"}>
                      {x.status}
                    </Badge>
                  </td>
                  {canEdit && (
                    <td data-label="จัดการ">
                      <div className="row-actions">
                        <button
                          className="table-action"
                          onClick={() => openForm(x)}
                        >
                          แก้ไข
                        </button>
                        <button className="table-action" onClick={() => cloneCase(x)}>สำเนา</button>
                        <button
                          className="table-action danger-action"
                          onClick={() => setConfirmDelete(x)}
                        >
                          ลบ
                        </button>
                      </div>
                    </td>
                  )}
                </tr>
              ))}
              {!pagedRows.length && !loading && <tr><td colSpan={canEdit ? 8 : 7}><div className="empty"><p>ไม่พบ Test Case ตามตัวกรองที่เลือก</p></div></td></tr>}
            </tbody>
          </table>
        </div>
        <div className="pagination"><label>แสดง<select value={pageSize} onChange={e=>{setPageSize(Number(e.target.value));setPage(1)}}><option>10</option><option>25</option><option>50</option></select> รายการ</label><span>หน้า {Math.min(page,pageCount)} / {pageCount}</span><button className="btn" disabled={page<=1} onClick={()=>setPage(x=>x-1)}>ก่อนหน้า</button><button className="btn" disabled={page>=pageCount} onClick={()=>setPage(x=>x+1)}>ถัดไป</button></div>
      </article>
      {testCaseAiModal&&<div className="modal" onMouseDown={()=>{if(!testCaseAiGenerating)setTestCaseAiModal(false)}}><div className="modal-box requirement-ai-modal" role="dialog" aria-modal="true" aria-labelledby="testcase-ai-title" onMouseDown={e=>e.stopPropagation()} style={{position:"relative"}}>{testCaseAiGenerating&&<div className="ai-loading-overlay"><div className="ai-spinner"/>{caseAiDrafts.length?<p>กำลังบันทึก Test Cases...</p>:<p>AI กำลังออกแบบ Test Case...</p>}<small>{caseAiDrafts.length?"กรุณารอสักครู่ อย่าปิดหน้าต่างนี้":"รอสักครู่ ระบบกำลังสร้าง Test Steps และ Expected Results"}</small></div>}
        <div className="modal-head"><div><h2 id="testcase-ai-title">AI Generate Test Case</h2><small>{caseAiDrafts.length?`พบ ${caseAiDrafts.length} Test Cases ที่ AI สร้าง — ตรวจสอบและบันทึก`:"สร้าง Draft พร้อม Test Steps จากคำอธิบายและไฟล์อ้างอิง"}</small></div><button aria-label="ปิดหน้าต่าง AI Generate" disabled={testCaseAiGenerating} onClick={()=>setTestCaseAiModal(false)}>×</button></div>
        {caseAiDrafts.length===0?(
        <section className="requirement-ai-panel">
          <div className="requirement-ai-head"><div><span className="ai-spark">AI</span><p><strong>ผู้ช่วยออกแบบ Test Case</strong><small>AI จะสร้าง Test Case หลายชุดจากคำอธิบายเดียว</small></p></div><span className="ai-review-badge">ต้องตรวจสอบก่อนบันทึก</span></div>
          {testCaseAiError&&<div className="inline-alert error"><span>{testCaseAiError}</span></div>}
          <div className="form-grid">
            <label>Project<select value={projectId} disabled={testCaseAiGenerating} onChange={e=>{setProjectId(e.target.value);setModuleId("")}}><option value="">เลือก Project</option>{projects.map(x=><option key={x.projectId} value={x.projectId}>{x.projectName}</option>)}</select></label>
            <label>Module<select value={moduleId} disabled={testCaseAiGenerating||!projectId} onChange={e=>setModuleId(e.target.value)}><option value="">เลือก Module</option>{renderModuleSelectOptions(modules.filter(x=>x.isActive))}</select></label>
            <label className="full">อธิบายสิ่งที่ต้องการทดสอบ<textarea rows={5} value={testCaseAiPrompt} disabled={testCaseAiGenerating} onChange={e=>setTestCaseAiPrompt(e.target.value)} placeholder="เช่น ตรวจสอบ Dashboard หลัง Login โดยครอบคลุม KPI, กราฟ และกรณีไม่มีข้อมูล"/><small>{testCaseAiPrompt.length} ตัวอักษร</small></label>
          </div>
          <div className="ai-attachments"><div><strong>ไฟล์อ้างอิง (ไม่บังคับ)</strong><small>รองรับ PDF, Word, Excel, CSV, Text และรูปภาพ สูงสุด 5 ไฟล์ รวมไม่เกิน 20 MB</small></div><label className="ai-file-picker">+ เลือกไฟล์<input type="file" multiple accept=".pdf,.txt,.md,.csv,.docx,.xlsx,.png,.jpg,.jpeg,.webp" disabled={testCaseAiGenerating||testCaseAiFiles.length>=5} onChange={e=>{addTestCaseAiFiles(Array.from(e.target.files??[]));e.target.value=""}}/></label>{testCaseAiFiles.length>0&&<div className="ai-file-list">{testCaseAiFiles.map((file,index)=><div key={`${file.name}-${index}`}><span aria-hidden="true">▧</span><p><b>{file.name}</b><small>{(file.size/1024/1024).toFixed(2)} MB</small></p><button aria-label={`ลบไฟล์ ${file.name}`} disabled={testCaseAiGenerating} onClick={()=>setTestCaseAiFiles(current=>current.filter((_,i)=>i!==index))}>×</button></div>)}</div>}</div>
          <div className="ai-draft-note"><span aria-hidden="true">i</span><p><strong>AI จะยังไม่บันทึกข้อมูล</strong><small>AI จะสร้าง Test Case หลายชุดให้ตรวจสอบก่อนบันทึก ผลลัพธ์ยังไม่ถูกบันทึกลงระบบ</small></p></div>
          <div className="requirement-ai-actions"><small>ไฟล์ใช้วิเคราะห์เฉพาะคำขอนี้และไม่บันทึกลงระบบ</small><div className="row-actions"><button className="btn" disabled={testCaseAiGenerating} onClick={()=>setTestCaseAiModal(false)}>ยกเลิก</button><button className="btn primary" disabled={testCaseAiGenerating||!projectId||!moduleId||!testCaseAiPrompt.trim()} onClick={generateTestCaseWithAi}>{testCaseAiGenerating?"AI กำลังวิเคราะห์...":"✦ สร้าง Test Cases"}</button></div></div>
        </section>
        ):(
        <section className="requirement-ai-panel case-ai-review">
          <div className="case-ai-review-head"><div><h3>Test Cases ที่ AI สร้าง ({caseAiDrafts.length})</h3></div></div>
          {testCaseAiError&&<div className="inline-alert error" style={{marginBottom:8}}><span>{testCaseAiError}</span></div>}
          <div className="case-ai-draft-list">{caseAiDrafts.map((draft,index)=>{const isExpanded=caseAiExpanded===index;return<div key={index} className={`case-ai-draft-card${isExpanded?" expanded":""}`}><div className="case-ai-draft-head" onClick={()=>setCaseAiExpanded(isExpanded?undefined:index)}><div className="case-ai-draft-title"><b>{draft.title}</b><div className="case-ai-draft-tags"><Badge tone={draft.priority==="P0"||draft.priority==="P1"?"red":"blue"}>{draft.priority}</Badge><Badge tone="yellow">{draft.testType}</Badge>{draft.automationCandidate&&<Badge tone="green">Auto</Badge>}<span className="case-ai-step-count">{draft.steps.length} Steps</span></div></div><span className="case-ai-expand-icon">{isExpanded?"▾":"▸"}</span></div>{isExpanded&&<div className="case-ai-draft-body"><p className="case-ai-draft-desc"><strong>Objective:</strong> {draft.objective}</p>{draft.preconditions&&<p className="case-ai-draft-desc"><strong>Preconditions:</strong> {draft.preconditions}</p>}<div className="case-ai-steps-list">{draft.steps.map(step=><div key={step.stepNo}><b>{step.stepNo}</b><span><strong>{step.action}</strong>{step.testData&&<small>Test Data: {step.testData}</small>}<small>Expected: {step.expectedResult}</small></span></div>)}</div><button className="table-action danger-action" style={{marginTop:8}} onClick={()=>removeCaseAiDraft(index)}>นำ Test Case นี้ออก</button></div>}</div>})}</div>
          <div className="requirement-ai-actions"><small>{caseAiDrafts.length} Test Cases พร้อมบันทึก</small><div className="row-actions"><button className="btn" disabled={testCaseAiGenerating} onClick={()=>setCaseAiDrafts([])}>สร้างใหม่</button><button className="btn primary" disabled={testCaseAiGenerating||!caseAiDrafts.length} onClick={saveAllCaseDrafts}>{testCaseAiGenerating?"กำลังบันทึก...":`✦ บันทึกทั้งหมด (${caseAiDrafts.length} Cases)`}</button></div></div>
        </section>
        )}
      </div></div>}
      {form && (
        <div className="modal" onMouseDown={() => setForm(false)}>
          <div
            className="modal-box testcase-modal"
            onMouseDown={(e) => e.stopPropagation()}
          >
            <div className="modal-head">
              <h2>{editing ? "แก้ไข" : "เพิ่ม"} Test Case</h2>
              <button onClick={() => setForm(false)}>×</button>
            </div>
            <div className="form-grid">
              <label className="tc-span-2">
                Project
                <select
                  disabled
                  value={projectId}
                  onChange={(e) => setProjectId(e.target.value)}
                >
                  {projects.map((x) => (
                    <option key={x.projectId} value={x.projectId}>
                      {x.projectName}
                    </option>
                  ))}
                </select>
              </label>
              <label className="tc-span-2">
                Module
                <select
                  value={moduleId}
                  onChange={(e) => setModuleId(e.target.value)}
                >
                  {renderModuleSelectOptions(modules.filter((x) => x.isActive))}
                </select>
              </label>
              <label className="tc-span-2">
                Test Case Code
                <input
                  disabled
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                />
              </label>
              <label className="tc-span-2">
                Title
                <input
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                />
              </label>
              <label className="tc-compact-field">
                Priority
                <select
                  value={priority}
                  onChange={(e) => setPriority(e.target.value)}
                >
                  {masterOptionElements(testCasePriorities, priority)}
                </select>
              </label>
              <label className="tc-compact-field">
                Type
                <select
                  value={testType}
                  onChange={(e) => setTestType(e.target.value)}
                >
                  {masterOptionElements(testCaseTypes, testType)}
                </select>
              </label>
              {editing && (
                <label className="tc-compact-field">
                  สถานะ
                  <select
                    value={status}
                    onChange={(e) => setStatus(e.target.value)}
                  >
                    <option>Draft</option>
                    <option>Review</option>
                    <option>Ready</option>
                    <option>Deprecated</option>
                  </select>
                </label>
              )}
              <label className="check-line tc-automation">
                <input
                  type="checkbox"
                  checked={automation}
                  onChange={(e) => setAutomation(e.target.checked)}
                />{" "}
                Automation Candidate
              </label>
              <label className="tc-span-2">Owner<select value={ownerUserId} onChange={e=>setOwnerUserId(e.target.value)}><option value="">ไม่ระบุผู้รับผิดชอบ</option>{users.map(x=><option key={x.userId} value={x.userId}>{x.displayName}</option>)}</select></label>
              <div className="status-information tc-span-2"><strong>ข้อมูลสถานะ</strong><span>{testCaseStatusInfo.find(x=>x.value===status)?.label}</span><small>{testCaseStatusInfo.find(x=>x.value===status)?.detail}</small><small><b>ผลกระทบ:</b> {testCaseStatusInfo.find(x=>x.value===status)?.impact}</small></div>
              <label className="tc-span-2">
                Objective
                <textarea
                  rows={2}
                  value={objective}
                  onChange={(e) => setObjective(e.target.value)}
                />
              </label>
              <label className="tc-span-2">
                Preconditions
                <textarea
                  rows={2}
                  value={preconditions}
                  onChange={(e) => setPreconditions(e.target.value)}
                />
              </label>
              {editing && (
                <label className="full tc-span-4">
                  <span>
                    เหตุผลที่แก้ไข <b className="required-mark">*</b>
                  </span>
                  <input
                    value={changeReason}
                    onChange={(e) => setChangeReason(e.target.value)}
                    placeholder="ระบุเหตุผลเพื่อสร้าง Revision ใหม่"
                  />
                  {!changeReason.trim() && (
                    <small className="field-error">
                      กรุณาระบุเหตุผลก่อนบันทึก เพื่อสร้าง Revision ใหม่
                    </small>
                  )}
                </label>
              )}
            </div>
            <div className="testcase-steps">
              <div className="card-title">
                <div>
                  <h3>Test Steps</h3>
                  <p>{steps.length} ขั้นตอน</p>
                </div>
                <button
                  className="btn"
                  onClick={() =>
                    setSteps((current) => [
                      ...current,
                      {
                        stepNo: current.length + 1,
                        action: "",
                        testData: "",
                        expectedResult: "",
                      },
                    ])
                  }
                >
                  + Step
                </button>
              </div>
              <div className="testcase-step-head" aria-hidden="true">
                <span>#</span>
                <span>Action</span>
                <span>Test Data</span>
                <span>Expected Result</span>
                <span>จัดการ</span>
              </div>
              {steps.map((step, index) => (
                <div className="testcase-step" key={index}>
                  <b>{index + 1}</b>
                  <input
                    placeholder="Action"
                    value={step.action}
                    onChange={(e) =>
                      updateStep(index, "action", e.target.value)
                    }
                  />
                  <input
                    placeholder="Test Data"
                    value={step.testData ?? ""}
                    onChange={(e) =>
                      updateStep(index, "testData", e.target.value)
                    }
                  />
                  <input
                    placeholder="Expected Result"
                    value={step.expectedResult}
                    onChange={(e) =>
                      updateStep(index, "expectedResult", e.target.value)
                    }
                  />
                  {steps.length > 1 && (
                    <button
                      className="table-action danger-action"
                      onClick={() =>
                        setSteps((current) =>
                          current
                            .filter((_, i) => i !== index)
                            .map((x, i) => ({ ...x, stepNo: i + 1 })),
                        )
                      }
                    >
                      ลบ
                    </button>
                  )}
                </div>
              ))}
            </div>
            <div className="modal-actions">
              <button className="btn" onClick={() => setForm(false)}>
                ยกเลิก
              </button>
              <button
                className="btn primary"
                disabled={
                  saving ||
                  !projectId ||
                  !moduleId ||
                  !code.trim() ||
                  !title.trim() ||
                  (editing && !changeReason.trim()) ||
                  steps.some(
                    (x) => !x.action.trim() || !x.expectedResult.trim(),
                  )
                }
                onClick={save}
              >
                {saving ? "กำลังบันทึก..." : "บันทึก"}
              </button>
            </div>
          </div>
        </div>
      )}
 {detail&&<div className="modal" onMouseDown={()=>setDetail(null)}><div className="modal-box testcase-detail" onMouseDown={e=>e.stopPropagation()}> <div className="modal-head"><div><h2>{detail.testCaseCode}</h2><small>{modules.find(x=>x.moduleId===detail.moduleId)?.moduleName||"-"} · {projects.find(x=>x.projectId===detail.projectId)?.projectName||""}</small></div><button onClick={()=>setDetail(null)}>×</button></div> <div className="tc-detail-hero"><h3>{detail.title}</h3> <div className="tc-detail-badges"> <Badge tone={detail.priority==="P0"||detail.priority==="P1"?"red":"blue"}>{detail.priority}</Badge> <Badge tone={detail.status==="Ready"?"green":detail.status==="Deprecated"?"yellow":"blue"}>{detail.status}</Badge> {detail.testType&&<Badge tone="yellow">{detail.testType}</Badge>} <Badge tone={detail.automationCandidate?"green":"gray"}>{detail.automationCandidate?"Automation Candidate":"Manual"}</Badge> </div> </div> <div className="tc-detail-meta"> <div className="tc-detail-meta-item"><span>Owner</span><b>{users.find(x=>x.userId===detail.ownerUserId)?.displayName||"ไม่ระบุ"}</b></div> <div className="tc-detail-meta-item"><span>Revision</span><b>Rev. {detail.revisionNo}</b></div> <div className="tc-detail-meta-item"><span>Module</span><b>{modules.find(x=>x.moduleId===detail.moduleId)?.moduleCode||"-"}</b></div> </div> <section className="tc-detail-section"> <h3>Objective</h3> <p className="tc-detail-body">{detail.objective||"ไม่ระบุวัตถุประสงค์"}</p> </section> {detail.preconditions&&<section className="tc-detail-section"> <h3>Preconditions</h3> <p className="tc-detail-body">{detail.preconditions}</p> </section>} <section className="tc-detail-section"> <h3>Test Steps ({detail.steps.length})</h3> <div className="tc-detail-steps">{detail.steps.map(x=><div key={x.stepNo} className="tc-detail-step"><div className="tc-detail-step-no">{x.stepNo}</div><div className="tc-detail-step-body"><div className="tc-detail-step-action"><strong>Action</strong><p>{x.action}</p></div>{x.testData&&<div className="tc-detail-step-data"><strong>Test Data</strong><p>{x.testData}</p></div>}<div className="tc-detail-step-expect"><strong>Expected Result</strong><p>{x.expectedResult}</p></div></div></div>)}</div> </section> <section className="tc-detail-section"> <h3>Requirements ที่เชื่อมโยง ({detailRequirements.length})</h3> {detailRequirements.length?<div className="tc-detail-reqs">{detailRequirements.map(x=><div key={x.requirementId} className="tc-detail-req-card"><div className="tc-detail-req-head"><b>{x.requirementCode}</b><Badge tone={x.status==="Approved"?"green":x.status==="Draft"?"yellow":"blue"}>{x.status}</Badge></div><p className="tc-detail-req-title">{x.title}</p>{x.coverageType&&<span className="tc-detail-req-coverage">Coverage: {x.coverageType}</span>}</div>)}</div>:<p className="muted-text">ยังไม่มี Requirement ที่เชื่อมโยง — สามารถเชื่อมได้จากหน้า Requirement</p>} </section> {revisions.length?<section className="tc-detail-section"> <h3>Revision History ({revisions.length})</h3> <div className="tc-detail-revisions">{revisions.map(x=><div key={x.revisionNo} className="tc-detail-revision"><div className="tc-detail-rev-head"><b>Rev. {x.revisionNo}</b><small>{x.changedByName||"ไม่ระบุผู้แก้ไข"} · {new Date(x.changedAt).toLocaleString("th-TH")}</small></div><p className="tc-detail-rev-reason">{x.changeReason||"-"}</p></div>)}</div> </section>:null} <div className="modal-actions"> <button className="btn" onClick={()=>setDetail(null)}>ปิด</button> {canEdit&&<button className="btn primary" onClick={()=>{const item=detail;setDetail(null);openForm(item);}}>แก้ไข</button>} </div> </div></div>}       {confirmDelete&&<div className="modal" onMouseDown={()=>setConfirmDelete(null)}><div className="modal-box confirm-box" onMouseDown={e=>e.stopPropagation()}><div className="modal-head"><h2>ยืนยันการลบ Test Case</h2><button onClick={()=>setConfirmDelete(null)}>×</button></div><p>ต้องการลบ <b>{confirmDelete.testCaseCode}</b> ใช่หรือไม่? ข้อมูลประวัติจะยังคงอยู่ในระบบ</p><div className="modal-actions"><button className="btn" onClick={()=>setConfirmDelete(null)}>ยกเลิก</button><button className="btn danger" onClick={()=>remove(confirmDelete)}>ยืนยันลบ</button></div></div></div>}
    </>
  );
}
type RegressionMetrics = { impactedModules:number;recommendedCases:number;regressionCycles:number;totalCycleCases:number;executedCases:number;passedCases:number;failedCases:number;progressPercent:number;passRate:number;openDefects:number;overallStatus:string };
type RegressionCase = { testCaseId:string;testCaseCode:string;title:string;moduleId:string;moduleName:string;priority:string;testType?:string;revisionNo:number;status:string;lastResult?:string;impactType:string;reason:string;isRequired:boolean;riskScore:number };
type RegressionImpact = { releaseId:string;buildId:string;metrics:RegressionMetrics;cases:RegressionCase[];page:number;pageSize:number;totalItems:number;totalPages:number;allCaseIds?:string[] };
type RegressionEnvironment = { testEnvironmentId:string;projectId:string;environmentName:string;isActive:boolean };
type RegressionHistory = {regressionAnalysisId:string;releaseId:string;buildId:string;buildNumber:string;impactedModules:number;recommendedCases:number;minimumPriority:string;changeNotes?:string;analyzedByName?:string;analyzedAt:string};
type RegressionBuildMetrics = {buildId:string;buildNumber:string;totalCases:number;executedCases:number;passedCases:number;failedCases:number;blockedCases:number;notRunCases:number;passRate:number};
type RegressionBaseline = {baseline:RegressionBuildMetrics;target:RegressionBuildMetrics;executedDelta:number;passedDelta:number;failedDelta:number;passRateDelta:number};
type RegressionActivity = {regressionActivityId:string;action:string;details?:string;actorName?:string;createdAt:string};
type RegressionProfile = {id:string;name:string;visibility?:string;isOwner?:boolean;minimumPriority:string;includeSharedDependencies:boolean;databaseChange:boolean;apiChange:boolean;calculationChange:boolean;permissionChange:boolean;installerChange:boolean;defectFix:boolean;directImpactWeight:number;historicalDefectWeight:number;criticalPriorityWeight:number;sharedDependencyWeight:number};
type RegressionSchedule = {regressionScheduleId:string;releaseId:string;regressionProfileId?:string;name:string;isActive:boolean;createdAt:string};
type RegressionNotification = {regressionScheduleId:string;buildId:string;buildNumber:string;scheduleName:string;message:string;createdAt:string};
function RegressionPage({projectId,releaseId,buildId,search,canEdit,onOpenCycle}:{projectId?:string;releaseId?:string;buildId?:string;search:string;canEdit:boolean;onOpenCycle:(page:"test-cycles"|"execution",cycleId:string)=>void}){
  const [releases,setReleases]=useState<ReleaseItem[]>([]),[builds,setBuilds]=useState<BuildItem[]>([]),[modules,setModules]=useState<ModuleItem[]>([]),[environments,setEnvironments]=useState<RegressionEnvironment[]>([]),[cycles,setCycles]=useState<TestCycleItem[]>([]);
  const [selectedRelease,setSelectedRelease]=useState(releaseId??""),[selectedBuild,setSelectedBuild]=useState(buildId??""),[changedModules,setChangedModules]=useState<string[]>([]),[minimumPriority,setMinimumPriority]=useState("P1"),[shared,setShared]=useState(true),[databaseChange,setDatabaseChange]=useState(false),[apiChange,setApiChange]=useState(false),[calculationChange,setCalculationChange]=useState(false),[permissionChange,setPermissionChange]=useState(false),[installerChange,setInstallerChange]=useState(false),[defectFix,setDefectFix]=useState(false),[sharedComponents,setSharedComponents]=useState(""),[changeNotes,setChangeNotes]=useState("");
  const [impact,setImpact]=useState<RegressionImpact|null>(null),[selectedCases,setSelectedCases]=useState<string[]>([]),[loading,setLoading]=useState(false),[initialLoading,setInitialLoading]=useState(true),[error,setError]=useState(""),[success,setSuccess]=useState(""),[impactFilter,setImpactFilter]=useState(""),[moduleFilter,setModuleFilter]=useState(""),[priorityFilter,setPriorityFilter]=useState("");
  const [caseDetail,setCaseDetail]=useState<TestCaseItem|null>(null),[caseDetailLoading,setCaseDetailLoading]=useState(false);
  const [history,setHistory]=useState<RegressionHistory[]>([]),[baselineBuild,setBaselineBuild]=useState(""),[baseline,setBaseline]=useState<RegressionBaseline|null>(null),[resultFilter,setResultFilter]=useState(""),[defectOnly,setDefectOnly]=useState(false);
  const [activities,setActivities]=useState<RegressionActivity[]>([]),[pageSize,setPageSize]=useState(50),[profileName,setProfileName]=useState(""),[profileVisibility,setProfileVisibility]=useState("Private"),[selectedProfileId,setSelectedProfileId]=useState(""),[profiles,setProfiles]=useState<RegressionProfile[]>([]),[schedules,setSchedules]=useState<RegressionSchedule[]>([]),[notifications,setNotifications]=useState<RegressionNotification[]>([]),[scheduleName,setScheduleName]=useState("Regression เมื่อมี Build ใหม่"),[directImpactWeight,setDirectImpactWeight]=useState(40),[historicalDefectWeight,setHistoricalDefectWeight]=useState(30),[criticalPriorityWeight,setCriticalPriorityWeight]=useState(20),[sharedDependencyWeight,setSharedDependencyWeight]=useState(10);
  const [suiteModal,setSuiteModal]=useState(false),[suiteName,setSuiteName]=useState(""),[suiteDescription,setSuiteDescription]=useState(""),[riskTier,setRiskTier]=useState("High"),[createCycle,setCreateCycle]=useState(true),[environmentId,setEnvironmentId]=useState(""),[cycleName,setCycleName]=useState(""),[startDate,setStartDate]=useState(""),[endDate,setEndDate]=useState(""),[existingCycle,setExistingCycle]=useState(""),[saving,setSaving]=useState(false);
  const headers=useMemo(()=>({"Content-Type":"application/json",Authorization:`Bearer ${localStorage.getItem("qa.accessToken")}`}),[]);
  const read=useCallback(async(url:string)=>{const r=await fetch(url,{headers});if(!r.ok)throw new Error("โหลดข้อมูล Regression ไม่สำเร็จ");return r.json();},[headers]);
  useEffect(()=>{setSelectedRelease(releaseId??"")},[releaseId]);useEffect(()=>{setSelectedBuild(buildId??"")},[buildId]);
  useEffect(()=>{if(!projectId){setInitialLoading(false);return}setInitialLoading(true);Promise.all([read(`${apiUrl}/releases?projectId=${projectId}`),read(`${apiUrl}/projects/${projectId}/modules`),read(`${apiUrl}/test-environments?projectId=${projectId}`),read(`${apiUrl}/test-cycles?projectId=${projectId}&size=100`)]).then(([releaseRows,moduleRows,environmentRows,cycleRows])=>{setReleases(Array.isArray(releaseRows)?releaseRows:[]);setModules((Array.isArray(moduleRows)?moduleRows:[]).filter((x:ModuleItem)=>x.isActive));setEnvironments((Array.isArray(environmentRows)?environmentRows:[]).filter((x:RegressionEnvironment)=>x.isActive));setCycles(((cycleRows?.items?.rows??cycleRows?.items??[]) as TestCycleItem[]).filter(x=>x.cycleType==="Regression"));}).catch(e=>setError(e.message)).finally(()=>setInitialLoading(false));},[projectId,read]);
  useEffect(()=>{const active=releases.filter(x=>x.status!=="Cancelled");if(active.length===releases.length)return;setReleases(active);if(selectedRelease&&!active.some(x=>x.releaseId===selectedRelease)){setSelectedRelease(active[0]?.releaseId??"");setSelectedBuild("");setImpact(null)}},[releases,selectedRelease]);
  useEffect(()=>{if(!selectedRelease){setBuilds([]);return}read(`${apiUrl}/releases/${selectedRelease}/builds`).then(rows=>setBuilds((Array.isArray(rows)?rows:[]).filter((x:BuildItem)=>x.isActive))).catch(e=>setError(e.message));},[selectedRelease,read]);
  useEffect(()=>{if(!selectedRelease){setHistory([]);return}read(`${apiUrl}/releases/${selectedRelease}/regression-history`).then(setHistory).catch(e=>setError(e.message));},[selectedRelease,read,impact]);
  useEffect(()=>{if(!selectedRelease){setActivities([]);return}read(`${apiUrl}/releases/${selectedRelease}/regression-activities?size=20`).then(setActivities).catch(e=>setError(e.message));},[selectedRelease,read,impact,success]);
  useEffect(()=>{if(!projectId)return;Promise.all([read(`${apiUrl}/projects/${projectId}/regression-profiles`),read(`${apiUrl}/projects/${projectId}/regression-schedules`),read(`${apiUrl}/projects/${projectId}/regression-notifications`)]).then(([profileRows,scheduleRows,notificationRows])=>{setProfiles((profileRows as {regressionProfileId:string;name:string;visibility:string;isOwner:boolean;settingsJson:string}[]).map(x=>({id:x.regressionProfileId,name:x.name,visibility:x.visibility,isOwner:x.isOwner,...JSON.parse(x.settingsJson)})));setSchedules(scheduleRows);setNotifications(notificationRows)}).catch(e=>setError(e.message));},[projectId,read,success]);
  useEffect(()=>{if(!selectedRelease||!selectedBuild||!baselineBuild||baselineBuild===selectedBuild){setBaseline(null);return}read(`${apiUrl}/releases/${selectedRelease}/regression-baseline?baselineBuildId=${baselineBuild}&targetBuildId=${selectedBuild}`).then(setBaseline).catch(e=>setError(e.message));},[selectedRelease,selectedBuild,baselineBuild,read]);
  useEffect(()=>{if(builds.length&&!builds.some(x=>x.buildId===selectedBuild))setSelectedBuild(builds[0].buildId)},[builds,selectedBuild]);
  const analyze=async(page=1,recordAnalysis=true)=>{if(!selectedRelease||!selectedBuild){setError("กรุณาเลือก Release และ Build");return}setLoading(true);setError("");setSuccess("");try{const r=await fetch(`${apiUrl}/releases/${selectedRelease}/regression-impact`,{method:"POST",headers,body:JSON.stringify({buildId:selectedBuild,changedModuleIds:changedModules,includeSharedDependencies:shared,minimumPriority,databaseChange,apiChange,calculationChange,permissionChange,installerChange,defectFix,sharedComponents,changeNotes,page,pageSize,directImpactWeight,historicalDefectWeight,criticalPriorityWeight,sharedDependencyWeight,recordAnalysis})});if(!r.ok){const p=await r.json().catch(()=>null);throw new Error(p?.detail??"วิเคราะห์ Regression Impact ไม่สำเร็จ")}const data=await r.json() as RegressionImpact;setImpact(data);setSelectedCases(current=>recordAnalysis?data.cases.filter(x=>x.isRequired).map(x=>x.testCaseId):[...new Set([...current,...data.cases.filter(x=>x.isRequired).map(x=>x.testCaseId)])]);}catch(e){setError(e instanceof Error?e.message:"วิเคราะห์ไม่สำเร็จ")}finally{setLoading(false)}};
  const currentSettings=()=>({minimumPriority,includeSharedDependencies:shared,databaseChange,apiChange,calculationChange,permissionChange,installerChange,defectFix,directImpactWeight,historicalDefectWeight,criticalPriorityWeight,sharedDependencyWeight});
  const saveProfile=async()=>{const name=profileName.trim();if(!name||!projectId)return;const settings=currentSettings();setSaving(true);try{const r=await fetch(`${apiUrl}/regression-profiles`,{method:"POST",headers,body:JSON.stringify({projectId,name,visibility:profileVisibility,settingsJson:JSON.stringify(settings)})});if(!r.ok)throw new Error("บันทึก Regression Profile ไม่สำเร็จ");const row=await r.json();setProfiles(current=>[{id:row.regressionProfileId,name,visibility:profileVisibility,isOwner:true,...settings},...current]);setSelectedProfileId(row.regressionProfileId);setProfileName("");setSuccess(`บันทึก Profile “${name}” ลงฐานข้อมูลแล้ว`)}catch(e){setError(e instanceof Error?e.message:"บันทึก Profile ไม่สำเร็จ")}finally{setSaving(false)}};
  const updateProfile=async()=>{const name=profileName.trim();const profile=profiles.find(x=>x.id===selectedProfileId);if(!profile?.isOwner||!name)return;setSaving(true);try{const settings=currentSettings();const r=await fetch(`${apiUrl}/regression-profiles/${profile.id}`,{method:"PUT",headers,body:JSON.stringify({name,visibility:profileVisibility,settingsJson:JSON.stringify(settings)})});if(!r.ok)throw new Error("อัปเดต Regression Profile ไม่สำเร็จ");setProfiles(current=>current.map(x=>x.id===profile.id?{...x,name,visibility:profileVisibility,...settings}:x));setSuccess(`อัปเดต Profile “${name}” แล้ว`)}catch(e){setError(e instanceof Error?e.message:"อัปเดต Profile ไม่สำเร็จ")}finally{setSaving(false)}};
  const applyProfile=(id:string)=>{const p=profiles.find(x=>x.id===id);if(!p)return;setMinimumPriority(p.minimumPriority);setShared(p.includeSharedDependencies);setDatabaseChange(p.databaseChange);setApiChange(p.apiChange);setCalculationChange(p.calculationChange);setPermissionChange(p.permissionChange);setInstallerChange(p.installerChange);setDefectFix(p.defectFix);setDirectImpactWeight(p.directImpactWeight);setHistoricalDefectWeight(p.historicalDefectWeight);setCriticalPriorityWeight(p.criticalPriorityWeight);setSharedDependencyWeight(p.sharedDependencyWeight);if(p.isOwner){setProfileName(p.name);setProfileVisibility(p.visibility??"Private")}setImpact(null);setSuccess(`ใช้ Profile “${p.name}” แล้ว`)};
  const deleteProfile=async()=>{if(!selectedProfileId||!window.confirm("ยืนยันลบ Regression Profile นี้?"))return;const r=await fetch(`${apiUrl}/regression-profiles/${selectedProfileId}`,{method:"DELETE",headers});if(!r.ok){setError("ลบ Profile ไม่สำเร็จหรือคุณไม่ใช่เจ้าของ");return}setProfiles(current=>current.filter(x=>x.id!==selectedProfileId));setSelectedProfileId("");setSuccess("ลบ Regression Profile แล้ว")};
  const acknowledgeNotification=async(item:RegressionNotification)=>{if(!canEdit)return;setSaving(true);try{const r=await fetch(`${apiUrl}/regression-schedules/${item.regressionScheduleId}/acknowledge/${item.buildId}`,{method:"POST",headers});if(!r.ok)throw new Error();setNotifications(current=>current.filter(x=>!(x.regressionScheduleId===item.regressionScheduleId&&x.buildId===item.buildId)));setSuccess("รับทราบการแจ้งเตือนแล้ว")}catch{setError("รับทราบการแจ้งเตือนไม่สำเร็จ")}finally{setSaving(false)}};
  const removeSchedule=async(id:string)=>{if(!window.confirm("ยืนยันปิด Scheduled Regression นี้?"))return;setSaving(true);try{const r=await fetch(`${apiUrl}/regression-schedules/${id}`,{method:"DELETE",headers});if(!r.ok)throw new Error();setSchedules(current=>current.filter(x=>x.regressionScheduleId!==id));setNotifications(current=>current.filter(x=>x.regressionScheduleId!==id));setSuccess("ปิด Scheduled Regression แล้ว")}catch{setError("ปิด Schedule ไม่สำเร็จหรือคุณไม่ใช่เจ้าของ")}finally{setSaving(false)}};
  const selectAllPages=async()=>{if(!selectedRelease||!selectedBuild)return;setLoading(true);try{const r=await fetch(`${apiUrl}/releases/${selectedRelease}/regression-impact`,{method:"POST",headers,body:JSON.stringify({buildId:selectedBuild,changedModuleIds:changedModules,includeSharedDependencies:shared,minimumPriority,databaseChange,apiChange,calculationChange,permissionChange,installerChange,defectFix,sharedComponents,changeNotes,page:1,pageSize,directImpactWeight,historicalDefectWeight,criticalPriorityWeight,sharedDependencyWeight,recordAnalysis:false,includeAllCaseIds:true})});if(!r.ok)throw new Error();const data=await r.json() as RegressionImpact;setSelectedCases(data.allCaseIds??[]);setSuccess(`เลือก Test Case ทั้งหมด ${data.totalItems} รายการจากทุกหน้าแล้ว`)}catch{setError("เลือก Test Case ทั้งหมดไม่สำเร็จ")}finally{setLoading(false)}};
  const exportAllPages=async()=>{if(!impact)return;setLoading(true);try{const all:RegressionCase[]=[];for(let page=1;page<=impact.totalPages;page++){const r=await fetch(`${apiUrl}/releases/${selectedRelease}/regression-impact`,{method:"POST",headers,body:JSON.stringify({buildId:selectedBuild,changedModuleIds:changedModules,includeSharedDependencies:shared,minimumPriority,databaseChange,apiChange,calculationChange,permissionChange,installerChange,defectFix,sharedComponents,changeNotes,page,pageSize:200,directImpactWeight,historicalDefectWeight,criticalPriorityWeight,sharedDependencyWeight,recordAnalysis:false})});if(!r.ok)throw new Error();const data=await r.json() as RegressionImpact;all.push(...data.cases);if(page===1&&data.totalPages!==impact.totalPages){page=0;(impact as RegressionImpact).totalPages=data.totalPages;all.length=0}}const headings=["Test Case Code","Title","Module","Priority","Impact Type","Risk Score","Last Result","Reason"];const escape=(v:string)=>`"${v.replaceAll('"','""')}"`;const body="\ufeff"+[headings,...all.map(x=>[x.testCaseCode,x.title,x.moduleName,x.priority,x.impactType,String(x.riskScore),x.lastResult||"Not Run",x.reason])].map(row=>row.map(escape).join(",")).join("\r\n");const url=URL.createObjectURL(new Blob([body],{type:"text/csv;charset=utf-8"}));const a=document.createElement("a");a.href=url;a.download="Regression_All_Pages.csv";a.click();URL.revokeObjectURL(url)}catch{setError("Export รายงานทุกหน้าไม่สำเร็จ")}finally{setLoading(false)}};
  const saveSchedule=async()=>{if(!selectedRelease||!projectId)return;setSaving(true);try{const r=await fetch(`${apiUrl}/regression-schedules`,{method:"POST",headers,body:JSON.stringify({releaseId:selectedRelease,regressionProfileId:selectedProfileId||null,name:scheduleName})});if(!r.ok)throw new Error();const row=await r.json();setSchedules(current=>[...current,row]);setSuccess("เปิด Scheduled Regression สำหรับ Build ใหม่แล้ว")}catch{setError("สร้าง Scheduled Regression ไม่สำเร็จ")}finally{setSaving(false)}};
  const visibleCases=useMemo(()=>{const term=search.trim().toLowerCase();return(impact?.cases??[]).filter(x=>(!impactFilter||x.impactType===impactFilter)&&(!moduleFilter||x.moduleId===moduleFilter)&&(!priorityFilter||x.priority===priorityFilter)&&(!resultFilter||(x.lastResult||"Not Run")===resultFilter)&&(!defectOnly||x.impactType==="Historical Defect")&&(!term||`${x.testCaseCode} ${x.title} ${x.moduleName}`.toLowerCase().includes(term)))},[impact,impactFilter,moduleFilter,priorityFilter,resultFilter,defectOnly,search]);
  const toggleCase=(id:string)=>setSelectedCases(current=>current.includes(id)?current.filter(x=>x!==id):[...current,id]);
  const openCaseDetail=async(item:RegressionCase)=>{setCaseDetailLoading(true);setError("");try{setCaseDetail(await read(`${apiUrl}/test-cases/${item.testCaseId}`) as TestCaseItem);}catch(e){setError(e instanceof Error?e.message:"โหลดรายละเอียด Test Case ไม่สำเร็จ")}finally{setCaseDetailLoading(false)}};
  const downloadRegression=(format:"csv"|"xls")=>{if(!impact)return;const rows=visibleCases.map(x=>[x.testCaseCode,x.title,x.moduleName,x.priority,x.impactType,x.lastResult||"Not Run",x.reason]);const headings=["Test Case Code","Title","Module","Priority","Impact Type","Last Result","Reason"];const escape=(value:string)=>`"${value.replaceAll('"','""')}"`;let body:string,mime:string,file:string;if(format==="csv"){body="\ufeff"+[headings,...rows].map(row=>row.map(escape).join(",")).join("\r\n");mime="text/csv;charset=utf-8";file="Regression_Report.csv";}else{body=`<html><head><meta charset="utf-8"></head><body><table><thead><tr>${headings.map(x=>`<th>${x}</th>`).join("")}</tr></thead><tbody>${rows.map(row=>`<tr>${row.map(x=>`<td>${x.replaceAll("&","&amp;").replaceAll("<","&lt;")}</td>`).join("")}</tr>`).join("")}</tbody></table></body></html>`;mime="application/vnd.ms-excel";file="Regression_Report.xls";}const url=URL.createObjectURL(new Blob([body],{type:mime}));const a=document.createElement("a");a.href=url;a.download=file;a.click();URL.revokeObjectURL(url)};
  const toggleVisible=()=>setSelectedCases(current=>visibleCases.every(x=>current.includes(x.testCaseId))?current.filter(id=>!visibleCases.some(x=>x.testCaseId===id)):[...new Set([...current,...visibleCases.map(x=>x.testCaseId)])]);
  const openSuite=()=>{const rel=releases.find(x=>x.releaseId===selectedRelease);setSuiteName(`${rel?.releaseCode??"Release"} Regression`);setCycleName(`${rel?.releaseCode??"Release"} Regression Cycle`);setSuiteDescription(changeNotes);setEnvironmentId(environments[0]?.testEnvironmentId??"");setSuiteModal(true)};
  const generateSuite=async()=>{if(!selectedCases.length||!suiteName.trim())return;setSaving(true);setError("");try{const suiteResponse=await fetch(`${apiUrl}/regression-suites/generate`,{method:"POST",headers,body:JSON.stringify({releaseId:selectedRelease,suiteName,description:suiteDescription,riskTier,testCaseIds:selectedCases})});if(!suiteResponse.ok){const p=await suiteResponse.json().catch(()=>null);throw new Error(p?.detail??"สร้าง Regression Suite ไม่สำเร็จ")}const suite=await suiteResponse.json();let message=`สร้าง ${suite.suiteCode} พร้อม ${suite.caseCount} Test Cases แล้ว`;if(createCycle){if(!environmentId)throw new Error("กรุณาเลือก Environment ก่อนสร้าง Cycle");const cycleResponse=await fetch(`${apiUrl}/test-cycles`,{method:"POST",headers,body:JSON.stringify({projectId,releaseId:selectedRelease,buildId:selectedBuild,environmentId,testSuiteId:suite.testSuiteId,cycleCode:"",cycleName:cycleName||suiteName,cycleType:"Regression",startDate:startDate||null,endDate:endDate||null,ownerUserId:null,notes:suiteDescription,populateFromSuite:true,requiredOnly:false})});if(!cycleResponse.ok){const p=await cycleResponse.json().catch(()=>null);throw new Error(p?.detail??"สร้าง Regression Cycle ไม่สำเร็จ")}const cycle=await cycleResponse.json();message+=` และสร้าง ${cycle.cycleCode} แล้ว`;setCycles(current=>[cycle,...current]);}setSuccess(message);setSuiteModal(false);await analyze();}catch(e){setError(e instanceof Error?e.message:"บันทึกไม่สำเร็จ")}finally{setSaving(false)}};
  const addToCycle=async()=>{if(!existingCycle||!selectedCases.length)return;setSaving(true);setError("");try{const r=await fetch(`${apiUrl}/test-cycles/${existingCycle}/add-impact-cases`,{method:"POST",headers,body:JSON.stringify({testCaseIds:selectedCases})});if(!r.ok)throw new Error("เพิ่ม Impact Cases เข้า Cycle ไม่สำเร็จ");setSuccess(`เพิ่ม ${selectedCases.length} Test Cases เข้า Regression Cycle แล้ว`);setExistingCycle("");await analyze();}catch(e){setError(e instanceof Error?e.message:"บันทึกไม่สำเร็จ")}finally{setSaving(false)}};
  const metrics=impact?.metrics??{impactedModules:0,recommendedCases:0,regressionCycles:0,totalCycleCases:0,executedCases:0,passedCases:0,failedCases:0,progressPercent:0,passRate:0,openDefects:0,overallStatus:"Not Started"};
  const stepDone1=!!selectedRelease&&!!selectedBuild,stepDone2=!!impact,stepDone3=selectedCases.length>0;
  const activeStepIndex=[stepDone1,stepDone2,stepDone3].findIndex(v=>!v);
  if(initialLoading)return <article className="card regression-empty"><p>กำลังโหลด Regression workspace...</p></article>;
  return <div className="regression-page">
    <section className="regression-summary" aria-label="Regression summary"><article><span className="regression-summary-icon blue">M</span><div><small>Impacted Modules</small><b>{metrics.impactedModules}</b><span>Module ที่เปลี่ยนแปลง</span></div></article><article><span className="regression-summary-icon violet">TC</span><div><small>Recommended Cases</small><b>{metrics.recommendedCases}</b><span>{selectedCases.length} รายการที่เลือก</span></div></article><article><span className="regression-summary-icon green">%</span><div><small>Regression Progress</small><b>{metrics.progressPercent}%</b><span>{metrics.executedCases}/{metrics.totalCycleCases} Executed</span></div></article><article><span className="regression-summary-icon amber">✓</span><div><small>Pass Rate</small><b>{metrics.passRate}%</b><span>{metrics.failedCases} Failed/Blocked</span></div></article><article><span className="regression-summary-icon red">!</span><div><small>Open Defects</small><b>{metrics.openDefects}</b><span className={`regression-health ${metrics.overallStatus.toLowerCase().replaceAll(" ","-")}`}>{metrics.overallStatus}</span></div></article></section>
    <nav className="regression-steps" aria-label="ขั้นตอนการทำ Regression">{([["เลือกบริบทและการเปลี่ยนแปลง","Release · Target Build · Changed Modules",stepDone1,"regression-analysis"],["วิเคราะห์และเลือก Test Case","กด “วิเคราะห์ Impact” แล้วติ๊กรายการที่จะทดสอบ",stepDone2,impact?"regression-results":"regression-analysis"],["สร้าง Suite / Cycle","เพิ่มเข้า Cycle เดิมหรือสร้างใหม่",stepDone3,"regression-results"]] as [string,string,boolean,string][]).map(([title,desc,done,target],index)=>(<button key={String(index)} type="button" aria-current={!done&&index===activeStepIndex?"step":undefined} className={done?"done":index===activeStepIndex?"active":""} onClick={()=>document.getElementById(target)?.scrollIntoView({behavior:"smooth",block:"start"})}><span className="regression-step-no" aria-hidden="true">{done?"✓":String(index+1)}</span><span className="regression-step-text"><b>{title}</b><small>{desc}</small></span></button>))}</nav>
    {error&&<div className="inline-alert error"><span>{error}</span><button onClick={()=>setError("")}>×</button></div>}{success&&<div className="inline-alert success"><span>{success}</span><button onClick={()=>setSuccess("")}>×</button></div>}
    <section id="regression-analysis" className="card regression-analysis"><div className="regression-section-head"><div><span className="regression-title-icon">◎</span><div><h2><span className="regression-step-chip">ขั้นตอน 1</span>Impact Analysis</h2><p>ระบุส่วนที่เปลี่ยนแปลงเพื่อค้นหา Test Case ที่ควร Regression</p></div></div><span className="regression-analyze-action"><button className="btn primary" disabled={loading||!selectedBuild} onClick={()=>analyze()}>{loading?"กำลังวิเคราะห์...":"วิเคราะห์ Impact"}</button>{!selectedBuild&&<small className="regression-analyze-hint">เลือก Release และ Target Build ก่อน</small>}</span></div>
      <div className="regression-profile-bar"><select aria-label="Regression Profile" value={selectedProfileId} onChange={e=>{setSelectedProfileId(e.target.value);applyProfile(e.target.value)}}><option value="">เลือก Profile / Template</option>{profiles.map(x=><option key={x.id} value={x.id}>{x.name}{x.isOwner?"":" (Shared)"}</option>)}</select><input aria-label="ชื่อ Regression Profile" value={profileName} onChange={e=>setProfileName(e.target.value)} placeholder="ชื่อ Profile"/><select aria-label="การมองเห็น Regression Profile" value={profileVisibility} onChange={e=>setProfileVisibility(e.target.value)}><option value="Private">Owner / Private</option><option value="Shared">Shared with Team</option></select><button className="btn" disabled={!profileName.trim()||saving} onClick={saveProfile}>บันทึกใหม่</button><button className="btn" disabled={!profiles.find(x=>x.id===selectedProfileId)?.isOwner||!profileName.trim()||saving} onClick={updateProfile}>อัปเดต Profile</button><button className="btn danger" disabled={!selectedProfileId} onClick={deleteProfile}>ลบ Profile</button></div>
      <div className="regression-context-grid"><label>Release<select value={selectedRelease} onChange={e=>{setSelectedRelease(e.target.value);setImpact(null)}}><option value="">เลือก Release</option>{releases.filter(x=>!projectId||x.projectId===projectId).map(x=><option key={x.releaseId} value={x.releaseId}>{x.releaseCode} · {x.version}</option>)}</select></label><label>Target Build<select value={selectedBuild} onChange={e=>{setSelectedBuild(e.target.value);setImpact(null)}}><option value="">เลือก Build</option>{builds.map(x=><option key={x.buildId} value={x.buildId}>{x.buildNumber} · {x.applicationVersion||"-"}</option>)}</select></label><label>Minimum Priority<select value={minimumPriority} onChange={e=>setMinimumPriority(e.target.value)}><option>P0</option><option>P1</option><option>P2</option><option>P3</option></select></label></div>
      <div className="regression-analysis-grid"><div className="regression-module-picker"><div className="regression-field-title"><b>Changed Modules</b><small>เลือกได้มากกว่า 1 Module</small></div><div className="regression-module-options">{modules.map(x=><label key={x.moduleId} className={changedModules.includes(x.moduleId)?"selected":""}><input type="checkbox" checked={changedModules.includes(x.moduleId)} onChange={()=>setChangedModules(v=>v.includes(x.moduleId)?v.filter(id=>id!==x.moduleId):[...v,x.moduleId])}/><span><b>{x.moduleCode}</b><small>{x.moduleName}</small></span></label>)}</div></div><div className="regression-change-panel"><div className="regression-field-title"><b>Change Impact</b><small>เลือกประเภทการเปลี่ยนแปลงที่เกี่ยวข้อง</small></div><div className="regression-impact-options">{[["Database / Schema",databaseChange,setDatabaseChange],["API Contract",apiChange,setApiChange],["Calculation",calculationChange,setCalculationChange],["Permission",permissionChange,setPermissionChange],["Update / Installer",installerChange,setInstallerChange],["Defect Fix",defectFix,setDefectFix]] .map(([label,value,setter])=><label key={label as string}><input type="checkbox" checked={value as boolean} onChange={e=>(setter as (v:boolean)=>void)(e.target.checked)}/><span>{label as string}</span></label>)}</div><label>Shared Components<input value={sharedComponents} onChange={e=>setSharedComponents(e.target.value)} placeholder="เช่น Auth, Pricing, Shared Library"/></label><label className="regression-shared-check"><input type="checkbox" checked={shared} onChange={e=>setShared(e.target.checked)}/><span>รวม Shared Dependencies และ Critical P0/P1</span></label></div></div>
      <label className="regression-notes">Change Notes<textarea rows={3} value={changeNotes} onChange={e=>setChangeNotes(e.target.value)} placeholder="สรุปสิ่งที่เปลี่ยนแปลง เพื่อใช้เป็นบริบทของ Suite และ Cycle"/></label>
      <details className="regression-risk-config"><summary>ตั้งค่า Risk Score</summary><p>กำหนดน้ำหนักเพื่อจัดลำดับ Test Case ที่มีความเสี่ยงสูงก่อน (คะแนนสูงสุด 100)</p><div>{[["Direct Impact",directImpactWeight,setDirectImpactWeight],["Historical Defect",historicalDefectWeight,setHistoricalDefectWeight],["Critical P0/P1",criticalPriorityWeight,setCriticalPriorityWeight],["Shared Dependency",sharedDependencyWeight,setSharedDependencyWeight]].map(([label,value,setter])=><label key={label as string}><span>{label as string}<b>{value as number}</b></span><input type="range" min="0" max="60" step="5" value={value as number} onChange={e=>(setter as (v:number)=>void)(Number(e.target.value))}/></label>)}</div></details>
    </section>
    <section id="regression-results" className="card regression-results"><div className="regression-section-head"><div><span className="regression-title-icon">⇄</span><div><h2><span className="regression-step-chip">ขั้นตอน 2</span>Recommended Test Cases</h2><p>{impact?`${impact.cases.length} รายการจากผลวิเคราะห์ · แสดง ${visibleCases.length} · เลือกแล้ว ${selectedCases.length} รายการ`:"เริ่มจากเลือก Module หรือประเภทการเปลี่ยนแปลง แล้วกดวิเคราะห์ Impact"}</p></div></div>{impact&&<div className="regression-result-actions"><button className="btn" onClick={()=>downloadRegression("csv")}>Export CSV</button><button className="btn" onClick={()=>downloadRegression("xls")}>Export Excel</button><button className="btn" onClick={toggleVisible}>{visibleCases.length&&visibleCases.every(x=>selectedCases.includes(x.testCaseId))?"ยกเลิกที่แสดง":"เลือกทั้งหมดที่แสดง"}</button></div>}</div>
      {impact&&<div className="regression-server-actions"><button className="btn" disabled={loading} onClick={selectAllPages}>เลือกทั้งหมดทุกหน้า ({impact.totalItems})</button><button className="btn" disabled={loading} onClick={exportAllPages}>Export ทุกหน้าพร้อม Risk</button></div>}
      {impact&&<div className="regression-filters"><select aria-label="กรอง Impact Type" value={impactFilter} onChange={e=>setImpactFilter(e.target.value)}><option value="">ทุก Impact Type</option>{[...new Set(impact.cases.map(x=>x.impactType))].map(x=><option key={x}>{x}</option>)}</select><select aria-label="กรอง Module" value={moduleFilter} onChange={e=>setModuleFilter(e.target.value)}><option value="">ทุก Module</option>{[...new Map(impact.cases.map(x=>[x.moduleId,x.moduleName])).entries()].map(([id,name])=><option key={id} value={id}>{name}</option>)}</select><select aria-label="กรอง Priority" value={priorityFilter} onChange={e=>setPriorityFilter(e.target.value)}><option value="">ทุก Priority</option><option>P0</option><option>P1</option><option>P2</option><option>P3</option></select><select aria-label="กรอง Last Result" value={resultFilter} onChange={e=>setResultFilter(e.target.value)}><option value="">ทุก Last Result</option><option>Fail</option><option>Blocked</option><option>Not Run</option><option>Pass</option></select><label className="regression-defect-filter"><input type="checkbox" checked={defectOnly} onChange={e=>setDefectOnly(e.target.checked)}/><span>เคยพบ Defect</span></label></div>}
      {!impact?<div className="regression-empty"><span>◎</span><b>ยังไม่มีผลการวิเคราะห์</b><p>ระบบจะแนะนำ Direct Impact, Shared Dependency, Critical P0/P1 และ Historical Defect Cases</p></div>:visibleCases.length===0?<div className="regression-empty"><span>⌕</span><b>ไม่พบ Test Case ตามตัวกรอง</b><p>ลองเปลี่ยน Impact Type, Module หรือ Priority</p></div>:<div className="regression-case-list">{visibleCases.map(x=><div key={x.testCaseId} className={`regression-case ${selectedCases.includes(x.testCaseId)?"selected":""}`}><input aria-label={`เลือก ${x.testCaseCode} ${x.title}`} type="checkbox" checked={selectedCases.includes(x.testCaseId)} onChange={()=>toggleCase(x.testCaseId)}/><span className="regression-case-main"><span className="regression-case-code"><button className="regression-case-link" disabled={caseDetailLoading} onClick={()=>openCaseDetail(x)} aria-label={`ดูรายละเอียด ${x.testCaseCode}`}>{x.testCaseCode}</button><Badge tone={x.priority==="P0"||x.priority==="P1"?"red":"blue"}>{x.priority}</Badge>{x.isRequired&&<Badge tone="yellow">Required</Badge>}<Badge tone={x.riskScore>=60?"red":x.riskScore>=30?"yellow":"blue"}>Risk {x.riskScore}</Badge></span><strong>{x.title}</strong><small>{x.moduleName} · {x.testType||"ไม่ระบุประเภท"} · Rev. {x.revisionNo}</small></span><span className="regression-case-impact"><Badge tone={x.impactType==="Direct Impact"?"blue":x.impactType==="Historical Defect"?"red":"yellow"}>{x.impactType}</Badge><small>{x.reason}</small></span><span className="regression-last-result"><small>Last Result</small><b className={(x.lastResult||"not-run").toLowerCase()}>{x.lastResult||"Not Run"}</b></span></div>)}</div>}
      {impact&&impact.totalPages>1&&<nav className="regression-pagination" aria-label="หน้ารายการ Recommended Test Cases"><span>หน้า {impact.page} / {impact.totalPages} · ทั้งหมด {impact.totalItems} รายการ</span><label>ต่อหน้า<select value={pageSize} onChange={e=>{setPageSize(Number(e.target.value));setTimeout(()=>analyze(1,false),0)}}><option value="25">25</option><option value="50">50</option><option value="100">100</option><option value="200">200</option></select></label><button className="btn" disabled={loading||impact.page<=1} onClick={()=>analyze(impact.page-1,false)}>ก่อนหน้า</button><button className="btn" disabled={loading||impact.page>=impact.totalPages} onClick={()=>analyze(impact.page+1,false)}>ถัดไป</button></nav>}
    </section>
    <section className="card regression-schedule"><div className="regression-section-head"><div><span className="regression-title-icon">◷</span><div><h2>Scheduled Regression</h2><p>เตรียม Regression อัตโนมัติและแจ้งเตือนเมื่อมี Active Build ใหม่</p></div></div><Badge tone={notifications.length?"yellow":"green"}>{notifications.length} Notifications</Badge></div>{notifications.length>0&&<div className="regression-notifications">{notifications.map(x=><div key={`${x.regressionScheduleId}-${x.buildId}`}><span>!</span><p><b>{x.message}</b><small>{x.scheduleName} · {new Date(x.createdAt).toLocaleString("th-TH")}</small></p><button className="btn" disabled={!canEdit||saving} onClick={()=>acknowledgeNotification(x)}>รับทราบ</button></div>)}</div>}<div className="regression-schedule-form"><input aria-label="ชื่อ Scheduled Regression" value={scheduleName} onChange={e=>setScheduleName(e.target.value)}/><select aria-label="Profile สำหรับ Scheduled Regression" value={selectedProfileId} onChange={e=>{setSelectedProfileId(e.target.value);applyProfile(e.target.value)}}><option value="">ไม่ใช้ Profile</option>{profiles.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select><button className="btn primary" disabled={!selectedRelease||!scheduleName.trim()||saving} onClick={saveSchedule}>เปิด Schedule</button></div>{schedules.length>0&&<ul className="regression-schedule-list">{schedules.map(x=><li key={x.regressionScheduleId}><span><b>{x.name}</b><small>{releases.find(r=>r.releaseId===x.releaseId)?.releaseCode??"-"} · เปิดใช้งานอยู่</small></span><button className="btn danger" disabled={!canEdit||saving} onClick={()=>removeSchedule(x.regressionScheduleId)}>ปิด Schedule</button></li>)}</ul>}</section>
    <section className="regression-dashboard-grid"><article className="card regression-trend"><div className="regression-section-head"><div><span className="regression-title-icon">↗</span><div><h2>Regression Trend</h2><p>จำนวน Test Case ที่ระบบแนะนำจากการวิเคราะห์ 6 ครั้งล่าสุด</p></div></div></div><div className="regression-trend-bars">{history.slice(0,6).reverse().map(x=>{const max=Math.max(1,...history.slice(0,6).map(h=>h.recommendedCases));return <div key={x.regressionAnalysisId}><span style={{height:`${Math.max(8,x.recommendedCases*100/max)}%`}} title={`${x.recommendedCases} cases`}></span><small>{x.buildNumber}</small><b>{x.recommendedCases}</b></div>})}{history.length===0&&<p className="regression-helper">ยังไม่มีข้อมูลแนวโน้ม</p>}</div></article><article className="card regression-activity"><div className="regression-section-head"><div><span className="regression-title-icon">⌁</span><div><h2>Recent Activity</h2><p>กิจกรรม Regression ล่าสุดของ Release</p></div></div><Badge tone="blue">{activities.length}</Badge></div><div className="regression-activity-list">{activities.slice(0,6).map(x=><div key={x.regressionActivityId}><span></span><p><b>{x.action}</b><small>{x.details||"-"} · {x.actorName||"System"}</small></p><time>{new Date(x.createdAt).toLocaleString("th-TH",{dateStyle:"short",timeStyle:"short"})}</time></div>)}{activities.length===0&&<p className="regression-helper">ยังไม่มีกิจกรรม</p>}</div></article></section>
    <section className="regression-phase-grid"><article className="card regression-baseline"><div className="regression-section-head"><div><span className="regression-title-icon">Δ</span><div><h2>Baseline Comparison</h2><p>เปรียบเทียบผล Regression ของ Target Build กับ Build ก่อนหน้า</p></div></div></div><label>Baseline Build<select value={baselineBuild} onChange={e=>setBaselineBuild(e.target.value)}><option value="">เลือก Build สำหรับเปรียบเทียบ</option>{builds.filter(x=>x.buildId!==selectedBuild).map(x=><option key={x.buildId} value={x.buildId}>{x.buildNumber} · {x.applicationVersion||"-"}</option>)}</select></label>{baseline?<div className="regression-compare"><div><small>Executed</small><b>{baseline.target.executedCases}</b><span className={baseline.executedDelta>=0?"positive":"negative"}>{baseline.executedDelta>=0?"+":""}{baseline.executedDelta}</span></div><div><small>Passed</small><b>{baseline.target.passedCases}</b><span className={baseline.passedDelta>=0?"positive":"negative"}>{baseline.passedDelta>=0?"+":""}{baseline.passedDelta}</span></div><div><small>Failed</small><b>{baseline.target.failedCases+baseline.target.blockedCases}</b><span className={baseline.failedDelta<=0?"positive":"negative"}>{baseline.failedDelta>=0?"+":""}{baseline.failedDelta}</span></div><div><small>Pass Rate</small><b>{baseline.target.passRate}%</b><span className={baseline.passRateDelta>=0?"positive":"negative"}>{baseline.passRateDelta>=0?"+":""}{baseline.passRateDelta}%</span></div></div>:<p className="regression-helper">{builds.length<2?"Release นี้ยังไม่มี Build อื่นสำหรับเปรียบเทียบ":"เลือก Baseline Build เพื่อดูแนวโน้ม"}</p>}</article><article className="card regression-history"><div className="regression-section-head"><div><span className="regression-title-icon">↺</span><div><h2>Regression History</h2><p>ประวัติการวิเคราะห์ Impact ล่าสุด</p></div></div><Badge tone="blue">{history.length}</Badge></div>{history.length?<div className="regression-history-list">{history.slice(0,6).map(x=><div key={x.regressionAnalysisId}><span><b>Build {x.buildNumber}</b><small>{new Date(x.analyzedAt).toLocaleString("th-TH")} · {x.analyzedByName||"System"}</small></span><span><b>{x.recommendedCases}</b><small>Cases · {x.impactedModules} Modules · {x.minimumPriority}</small></span>{x.changeNotes&&<p>{x.changeNotes}</p>}</div>)}</div>:<p className="regression-helper">ยังไม่มีประวัติการวิเคราะห์สำหรับ Release นี้</p>}</article></section>
    {impact&&selectedCases.length>0&&<div className="regression-selection-bar"><div><b>{selectedCases.length}</b><span>Test Cases ที่เลือก</span></div><div className="regression-existing-cycle"><select aria-label="Regression Cycle ที่มีอยู่" value={existingCycle} onChange={e=>setExistingCycle(e.target.value)}><option value="">เพิ่มเข้า Regression Cycle ที่มีอยู่</option>{cycles.filter(x=>x.releaseId===selectedRelease&&x.buildId===selectedBuild).map(x=><option key={x.testCycleId} value={x.testCycleId}>{x.cycleCode} · {x.cycleName}</option>)}</select><button className="btn" disabled={!existingCycle||saving||!canEdit} onClick={addToCycle}>เพิ่มเข้า Cycle</button>{existingCycle&&<><button className="btn" onClick={()=>onOpenCycle("test-cycles",existingCycle)}>เปิด Cycle</button><button className="btn" onClick={()=>onOpenCycle("execution",existingCycle)}>เปิด Execution</button></>}</div><button className="btn primary" disabled={!canEdit} onClick={openSuite}>สร้าง Regression Suite / Cycle</button></div>}
    {suiteModal&&<div className="modal" role="dialog" aria-modal="true" aria-labelledby="regression-suite-title" onMouseDown={()=>!saving&&setSuiteModal(false)}><div className="modal-box regression-suite-modal" onMouseDown={e=>e.stopPropagation()}><div className="modal-head"><div><h2 id="regression-suite-title">สร้าง Regression Suite</h2><small>{selectedCases.length} Test Cases ที่เลือก</small></div><button disabled={saving} onClick={()=>setSuiteModal(false)}>×</button></div><div className="form-grid"><label className="full">Suite Name<input value={suiteName} onChange={e=>setSuiteName(e.target.value)}/></label><label>Risk Tier<select value={riskTier} onChange={e=>setRiskTier(e.target.value)}><option>Critical</option><option>High</option><option>Medium</option><option>Low</option></select></label><label className="full">Description<textarea rows={3} value={suiteDescription} onChange={e=>setSuiteDescription(e.target.value)}/></label></div><label className="regression-create-cycle"><input type="checkbox" checked={createCycle} onChange={e=>setCreateCycle(e.target.checked)}/><span><b>สร้าง Regression Cycle ต่อทันที</b><small>ระบบจะนำ Test Case ทั้งหมดใน Suite เข้า Cycle</small></span></label>{createCycle&&<div className="form-grid regression-cycle-fields"><label className="full">Cycle Name<input value={cycleName} onChange={e=>setCycleName(e.target.value)}/></label><label>Environment<select value={environmentId} onChange={e=>setEnvironmentId(e.target.value)}><option value="">เลือก Environment</option>{environments.map(x=><option key={x.testEnvironmentId} value={x.testEnvironmentId}>{x.environmentName}</option>)}</select></label><label>Start Date<input type="date" value={startDate} onChange={e=>setStartDate(e.target.value)}/></label><label>End Date<input type="date" value={endDate} onChange={e=>setEndDate(e.target.value)}/></label></div>}<div className="modal-actions"><button className="btn" disabled={saving} onClick={()=>setSuiteModal(false)}>ยกเลิก</button><button className="btn primary" disabled={saving||!suiteName.trim()||(createCycle&&!environmentId)} onClick={generateSuite}>{saving?"กำลังสร้าง...":createCycle?"สร้าง Suite และ Cycle":"สร้าง Suite"}</button></div></div></div>}
    {caseDetail&&<div className="modal" role="dialog" aria-modal="true" aria-labelledby="regression-case-detail-title" onMouseDown={()=>setCaseDetail(null)}><div className="modal-box testcase-detail" onMouseDown={e=>e.stopPropagation()}><div className="modal-head"><div><h2 id="regression-case-detail-title">{caseDetail.testCaseCode}</h2><small>{modules.find(x=>x.moduleId===caseDetail.moduleId)?.moduleName||"-"}</small></div><button aria-label="ปิดรายละเอียด Test Case" onClick={()=>setCaseDetail(null)}>×</button></div><div className="tc-detail-hero"><h3>{caseDetail.title}</h3><div className="tc-detail-badges"><Badge tone={caseDetail.priority==="P0"||caseDetail.priority==="P1"?"red":"blue"}>{caseDetail.priority}</Badge><Badge tone={caseDetail.status==="Ready"?"green":caseDetail.status==="Deprecated"?"yellow":"blue"}>{caseDetail.status}</Badge>{caseDetail.testType&&<Badge tone="yellow">{caseDetail.testType}</Badge>}</div></div><div className="tc-detail-meta"><div className="tc-detail-meta-item"><span>Revision</span><b>Rev. {caseDetail.revisionNo}</b></div><div className="tc-detail-meta-item"><span>Module</span><b>{modules.find(x=>x.moduleId===caseDetail.moduleId)?.moduleCode||"-"}</b></div><div className="tc-detail-meta-item"><span>Execution Type</span><b>{caseDetail.automationCandidate?"Automation Candidate":"Manual"}</b></div></div><section className="tc-detail-section"><h3>Objective</h3><p className="tc-detail-body">{caseDetail.objective||"ไม่ระบุวัตถุประสงค์"}</p></section>{caseDetail.preconditions&&<section className="tc-detail-section"><h3>Preconditions</h3><p className="tc-detail-body">{caseDetail.preconditions}</p></section>}<section className="tc-detail-section"><h3>Test Steps ({caseDetail.steps?.length??0})</h3><div className="tc-detail-steps">{(caseDetail.steps??[]).map(x=><div key={x.stepNo} className="tc-detail-step"><div className="tc-detail-step-no">{x.stepNo}</div><div className="tc-detail-step-body"><div className="tc-detail-step-action"><strong>Action</strong><p>{x.action}</p></div>{x.testData&&<div className="tc-detail-step-data"><strong>Test Data</strong><p>{x.testData}</p></div>}<div className="tc-detail-step-expect"><strong>Expected Result</strong><p>{x.expectedResult}</p></div></div></div>)}</div></section><div className="modal-actions"><button className="btn primary" onClick={()=>setCaseDetail(null)}>ปิด</button></div></div></div>}
  </div>
}

type RtmLinkedCase = { testCaseId: string; testCaseCode: string; title: string; priority: string; testType?: string; status: string; revisionNo: number; coverageType?: string };
type RtmItem = { requirementId: string; moduleId: string; moduleName: string; requirementCode: string; title: string; priority: string; testCaseCount: number; coverageStatus: string; status: string; testCases: RtmLinkedCase[] };
function RtmPage({ refresh, projectId, releaseId, search, canEdit }: { refresh: number; projectId?: string; releaseId?: string; search: string; canEdit: boolean }) {
  const [items, setItems] = useState<RtmItem[]>([]), [releases, setReleases] = useState<ReleaseItem[]>([]), [modules, setModules] = useState<ModuleItem[]>([]), [cases, setCases] = useState<TestCaseItem[]>([]);
  const [selectedRelease, setSelectedRelease] = useState(releaseId ?? ""), [moduleFilter, setModuleFilter] = useState(""), [coverageFilter, setCoverageFilter] = useState(""), [statusFilter, setStatusFilter] = useState("");
  const [detail, setDetail] = useState<RtmItem | null>(null), [caseDetail, setCaseDetail] = useState<RtmLinkedCase | null>(null), [linking, setLinking] = useState<RtmItem | null>(null), [linkModuleFilter,setLinkModuleFilter]=useState(""), [selectedCase, setSelectedCase] = useState(""), [coverageType, setCoverageType] = useState("Direct");
  const [busy, setBusy] = useState(false), [reload, setReload] = useState(0), [error, setError] = useState(""), [loading, setLoading] = useState(true);
  const headers = useMemo(() => ({ Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }), []);
  useEffect(() => setSelectedRelease(releaseId ?? ""), [releaseId]);
  useEffect(() => {
    const readJson = (url: string) => fetch(url, { headers }).then(r => r.ok ? r.json() : Promise.reject(new Error(`${r.status} ${url}`)));
    Promise.all([
      readJson(`${apiUrl}/releases`),
      projectId ? readJson(`${apiUrl}/projects/${projectId}/modules`) : Promise.resolve([]),
      readJson(`${apiUrl}/test-cases${projectId ? `?projectId=${projectId}` : ""}`),
    ]).then(([releaseRows, moduleRows, caseData]) => {
      setReleases(releaseRows);
      setModules((moduleRows as ModuleItem[]).filter(x => x.isActive));
      const tcRows = Array.isArray(caseData) ? caseData : (caseData as { items?: { rows: unknown[] } })?.items?.rows ?? (caseData as { rows?: unknown[] })?.rows ?? [];
      setCases(tcRows as TestCaseItem[]);
    }).catch(() => setError("โหลดข้อมูลตัวกรอง RTM ไม่สำเร็จ"));
  }, [headers, projectId, refresh]);
  useEffect(() => { if (!selectedRelease) { setItems([]); setLoading(false); return; } setLoading(true); setError(""); fetch(`${apiUrl}/releases/${selectedRelease}/rtm`, { headers }).then(r => r.ok ? r.json() : Promise.reject()).then((data: unknown) => { const rows = Array.isArray(data) ? data : (data as { items?: { rows: unknown[] } }).items?.rows ?? []; setItems(rows as RtmItem[]); }).catch(() => setError("โหลด RTM ไม่สำเร็จ")).finally(() => setLoading(false)); }, [headers, selectedRelease, refresh, reload]);
  const filtered = items.filter(x => (!moduleFilter || x.moduleId === moduleFilter) && (!coverageFilter || x.coverageStatus === coverageFilter) && (!statusFilter || x.status === statusFilter) && (!search || `${x.requirementCode} ${x.title} ${x.moduleName} ${x.testCases.map(t => t.testCaseCode).join(" ")}`.toLowerCase().includes(search.toLowerCase())));
  const linkableCases=cases.filter(testCase=>(!linkModuleFilter||testCase.moduleId===linkModuleFilter)&&(!linking||!linking.testCases.some(linked=>linked.testCaseId===testCase.testCaseId)));
  const counts = { covered: items.filter(x => x.coverageStatus === "Covered").length, partial: items.filter(x => x.coverageStatus === "Partial").length, none: items.filter(x => x.coverageStatus === "Not Covered").length };
  const saveLink = async (remove?: RtmLinkedCase) => { if (!linking || (!remove && !selectedCase)) return; setBusy(true); const id = remove?.testCaseId ?? selectedCase; const r = await fetch(`${apiUrl}/requirements/${linking.requirementId}/test-cases/${id}${remove ? "" : `?coverageType=${coverageType}`}`, { method: remove ? "DELETE" : "POST", headers }); setBusy(false); if (!r.ok) { setError("บันทึกการเชื่อมโยง Test Case ไม่สำเร็จ"); return; } if (!remove) { setLinking(null); setSelectedCase(""); } setReload(x => x + 1); };
  const exportCsv = () => { const rows = [["Requirement ID","Title","Module","Priority","Status","Coverage","Test Case ID","Test Case Title","Test Case Status","Link Type"], ...filtered.flatMap(x => (x.testCases.length ? x.testCases : [null]).map(t => [x.requirementCode,x.title,x.moduleName,x.priority,x.status,x.coverageStatus,t?.testCaseCode ?? "",t?.title ?? "",t?.status ?? "",t?.coverageType ?? ""]))]; const csv = "\ufeff" + rows.map(row => row.map(v => `"${String(v).replaceAll('"','""')}"`).join(",")).join("\r\n"); const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" })); const a = document.createElement("a"); a.href=url; a.download="rtm.csv"; a.click(); URL.revokeObjectURL(url); };
  if (loading) return <article className="card empty"><p>กำลังคำนวณ RTM...</p></article>;
  return <>
    <div className="kpi-grid"><article className="card kpi"><span>Requirements</span><strong>{items.length}</strong><small>In Scope</small></article><article className="card kpi"><span>Covered</span><strong>{counts.covered}</strong><small className="green">มี Test Case Ready</small></article><article className="card kpi"><span>Partial</span><strong>{counts.partial}</strong><small className="blue">เชื่อมแล้ว แต่ยังไม่ Ready</small></article><article className="card kpi"><span>Not Covered</span><strong>{counts.none}</strong><small className="red">ยังไม่มี Test Case</small></article></div>
    <article className="card"><div className="table-tools rtm-tools"><div><select value={selectedRelease} onChange={e => setSelectedRelease(e.target.value)}><option value="">เลือก Release</option>{releases.filter(x => !projectId || x.projectId === projectId).map(x => <option key={x.releaseId} value={x.releaseId}>{x.releaseCode} · {x.version}</option>)}</select><select value={moduleFilter} onChange={e => setModuleFilter(e.target.value)} aria-label="กรองตาม Module"><option value="">ทุก Module</option>{renderModuleSelectOptions(modules.filter(x => x.isActive && (!projectId || x.projectId === projectId)))}</select><select value={coverageFilter} onChange={e => setCoverageFilter(e.target.value)}><option value="">ทุก Coverage</option><option>Covered</option><option>Partial</option><option>Not Covered</option></select><select value={statusFilter} onChange={e => setStatusFilter(e.target.value)}><option value="">ทุก Status</option>{[...new Set(items.map(x => x.status))].map(x => <option key={x}>{x}</option>)}</select></div><button className="btn" onClick={exportCsv}>Export CSV</button></div>{error && <div className="inline-error">{error}</div>}
      <div className="table-wrap"><table className="rtm-table"><thead><tr><th>Requirement</th><th>Title</th><th>Priority</th><th>Test Cases</th><th>Coverage</th><th>Status</th><th>จัดการ</th></tr></thead><tbody>{filtered.map(x => <tr key={x.requirementId}><td data-label="Requirement"><button className="link-button" onClick={() => setDetail(x)}>{x.requirementCode}</button><small className="rtm-module">{x.moduleName}</small></td><td data-label="Title">{x.title}</td><td data-label="Priority">{x.priority}</td><td data-label="Test Cases">{x.testCaseCount}</td><td data-label="Coverage"><Badge tone={x.coverageStatus === "Covered" ? "green" : x.coverageStatus === "Partial" ? "yellow" : "red"}>{x.coverageStatus}</Badge></td><td data-label="Status">{x.status}</td><td data-label="จัดการ"><div className="row-actions"><button className="btn" onClick={() => setDetail(x)}>รายละเอียด</button>{canEdit && <button className="btn primary" onClick={() => {setLinking(x);setLinkModuleFilter(x.moduleId);setSelectedCase("");setCoverageType("Direct")}}>จัดการ Link</button>}</div></td></tr>)}</tbody></table></div>
    </article>
    {detail && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="rtm-detail-title" onMouseDown={() => setDetail(null)}><div className="modal-box rtm-detail" onMouseDown={e => e.stopPropagation()}><div className="modal-head"><div><h2 id="rtm-detail-title">รายละเอียด RTM</h2><small>{detail.requirementCode} · {detail.moduleName}</small></div><button aria-label="ปิดหน้าต่างรายละเอียด RTM" onClick={() => setDetail(null)}>×</button></div><div className="rtm-detail-hero"><div className="rtm-detail-hero-copy"><span className="rtm-detail-eyebrow">Requirement</span><b className="rtm-detail-code">{detail.requirementCode}</b><h3>{detail.title}</h3><div className="rtm-detail-badges"><Badge tone={detail.priority === "P0" || detail.priority === "P1" ? "red" : "blue"}>{detail.priority}</Badge><Badge tone={detail.status === "Approved" || detail.status === "Implemented" ? "green" : "yellow"}>{detail.status}</Badge></div></div><div className={`rtm-coverage-summary ${detail.coverageStatus.toLowerCase().replaceAll(" ", "-")}`}><span>Coverage</span><b>{detail.coverageStatus}</b><small>{detail.testCaseCount} Test Case{detail.testCaseCount === 1 ? "" : "s"}</small></div></div><div className="rtm-detail-meta"><div><span className="rtm-meta-icon" aria-hidden="true">M</span><span>Module<b>{detail.moduleName || "ไม่ระบุ"}</b></span></div><div><span className="rtm-meta-icon" aria-hidden="true">#</span><span>Linked Test Cases<b>{detail.testCaseCount}</b></span></div><div><span className="rtm-meta-icon" aria-hidden="true">✓</span><span>Traceability<b>{detail.coverageStatus}</b></span></div></div><section className="rtm-detail-section"><div className="rtm-section-heading"><div><span className="rtm-section-icon" aria-hidden="true">⇄</span><span><h3>Test Cases ที่เชื่อมโยง</h3><small>ตรวจสอบความครอบคลุมและชนิดการเชื่อมโยง</small></span></div><span className="rtm-linked-count">{detail.testCases.length} รายการ</span></div><div className="rtm-linked-list rtm-detail-linked-list">{detail.testCases.length ? detail.testCases.map((t, index) => <button key={t.testCaseId} onClick={() => setCaseDetail(t)}><span className="rtm-case-index">{String(index + 1).padStart(2, "0")}</span><span className="rtm-case-copy"><b>{t.testCaseCode}</b><span>{t.title}</span><small>{t.testType || "ไม่ระบุประเภท"} · Rev. {t.revisionNo}</small></span><span className="rtm-case-status"><Badge tone={t.status === "Ready" ? "green" : t.status === "Deprecated" ? "red" : "yellow"}>{t.status}</Badge>{t.coverageType && <small>{t.coverageType}</small>}<i aria-hidden="true">›</i></span></button>) : <div className="rtm-detail-empty"><span aria-hidden="true">⇄</span><b>ยังไม่มี Test Case ที่เชื่อมโยง</b><p>Requirement นี้ยังไม่ถูกครอบคลุม กรุณาเพิ่ม Test Case Link เพื่อให้ตรวจสอบ Traceability ได้</p></div>}</div></section><div className="modal-actions"><button className="btn" onClick={() => setDetail(null)}>ปิด</button>{canEdit && <button className="btn primary" onClick={() => {setLinking(detail);setLinkModuleFilter(detail.moduleId);setSelectedCase("");setCoverageType("Direct");setDetail(null)}}>จัดการ Link</button>}</div></div></div>}
    {caseDetail && <div className="modal nested-modal" onMouseDown={() => setCaseDetail(null)}><div className="modal-box" onMouseDown={e => e.stopPropagation()}><div className="modal-head"><h2>{caseDetail.testCaseCode}</h2><button onClick={() => setCaseDetail(null)}>×</button></div><h3>{caseDetail.title}</h3><div className="detail-grid"><span>Priority<b>{caseDetail.priority}</b></span><span>Type<b>{caseDetail.testType || "-"}</b></span><span>Status<b>{caseDetail.status}</b></span><span>Revision<b>Rev. {caseDetail.revisionNo}</b></span><span>Link Type<b>{caseDetail.coverageType || "Direct"}</b></span></div></div></div>}
    {linking && <div className="modal" onMouseDown={() => setLinking(null)}><div className="modal-box" onMouseDown={e => e.stopPropagation()}><div className="modal-head"><div><h2>จัดการ Test Case Link</h2><small>{linking.requirementCode}</small></div><button onClick={() => setLinking(null)}>×</button></div><div className="rtm-linked-list editable">{linking.testCases.map(t => <div key={t.testCaseId}><button onClick={() => setCaseDetail(t)}><b>{t.testCaseCode}</b><span>{t.title}</span></button><button className="btn danger" disabled={busy} onClick={() => saveLink(t)}>ยกเลิก Link</button></div>)}</div><div className="form-grid rtm-link-form"><label className="full">Module<select className="rtm-link-module-filter" value={linkModuleFilter} onChange={e=>{setLinkModuleFilter(e.target.value);setSelectedCase("")}}><option value="">ทุก Module</option>{renderModuleSelectOptions(modules.filter(x=>x.isActive&&(!projectId||x.projectId===projectId)))}</select></label><label>Test Case <small>{linkableCases.length} รายการ</small><select value={selectedCase} onChange={e => setSelectedCase(e.target.value)}><option value="">{linkableCases.length?"เลือก Test Case":"ไม่พบ Test Case ใน Module นี้"}</option>{linkableCases.map(t => <option key={t.testCaseId} value={t.testCaseId}>{t.testCaseCode} · {t.title}</option>)}</select></label><label>Coverage Type<select value={coverageType} onChange={e => setCoverageType(e.target.value)}><option>Direct</option><option>Indirect</option></select></label></div><div className="modal-actions"><button className="btn" onClick={() => setLinking(null)}>ปิด</button><button className="btn primary" disabled={busy || !selectedCase} onClick={() => saveLink()}>{busy ? "กำลังบันทึก..." : "เพิ่ม Link"}</button></div></div></div>}
  </>;
}
type CycleEnvironment = {
  testEnvironmentId: string;
  projectId: string;
  environmentName: string;
  baseUrl?: string;
  isActive: boolean;
};
type CycleBuild = {
  buildId: string;
  releaseId: string;
  buildNumber: string;
  isActive: boolean;
};
type CycleRelease = {
  releaseId: string;
  projectId: string;
  releaseCode: string;
  status: string;
};
type TestCycleItem = {
  testCycleId: string;
  projectId: string;
  releaseId: string;
  releaseCode: string;
  buildId: string;
  buildNumber: string;
  environmentId: string;
  environmentName: string;
  testSuiteId?: string;
  suiteName?: string;
  cycleCode: string;
  cycleName: string;
  cycleType?: string;
  startDate?: string;
  endDate?: string;
  status: string;
  notes?: string;
  caseCount: number;
  executedCount: number;
  progressPercent: number;
};
function TestCyclesPage({ search, canEdit, canExport, contextProjectId, contextReleaseId, contextBuildId }: { search: string; canEdit: boolean; canExport: boolean; contextProjectId?: string; contextReleaseId?: string; contextBuildId?: string }) {
  const masterOptions = useMasterOptions(), cycleTypes = masterOptions("TestCycleType");
  const [items, setItems] = useState<TestCycleItem[]>([]),
    [projects, setProjects] = useState<ProjectItem[]>([]),
    [releases, setReleases] = useState<CycleRelease[]>([]),
    [builds, setBuilds] = useState<CycleBuild[]>([]),
    [environments, setEnvironments] = useState<CycleEnvironment[]>([]),
    [suites, setSuites] = useState<TestSuiteItem[]>([]),
    [reload, setReload] = useState(0),
    [form, setForm] = useState(false),
    [editing, setEditing] = useState<TestCycleItem | null>(null),
    [detail, setDetail] = useState<TestCycleItem | null>(null),
    [saving, setSaving] = useState(false),
    [loading, setLoading] = useState(true),
    [exporting, setExporting] = useState(false),
    [error, setError] = useState(""),
    [notice, setNotice] = useState(""),
    [totalCount, setTotalCount] = useState(0),
    [page, setPage] = useState(1),
    [pageSize, setPageSize] = useState(20);
  const [projectId, setProjectId] = useState(""),
    [releaseId, setReleaseId] = useState(""),
    [buildId, setBuildId] = useState(""),
    [environmentId, setEnvironmentId] = useState(""),
    [suiteId, setSuiteId] = useState(""),
    [code, setCode] = useState(""),
    [name, setName] = useState(""),
    [cycleType, setCycleType] = useState(""),
    [startDate, setStartDate] = useState(""),
    [endDate, setEndDate] = useState(""),
    [notes, setNotes] = useState(""),
    [environmentName, setEnvironmentName] = useState("");
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
  useEffect(() => {
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    const readList = async <T,>(url: string): Promise<T[]> => {
      const response = await fetch(url, { headers: h });
      if (!response.ok) throw new Error(`โหลดข้อมูลไม่สำเร็จ (${response.status})`);
      const data: unknown = await response.json();
      return Array.isArray(data) ? (data as T[]) : (data as any)?.items?.rows ?? (data as any)?.rows ?? [];
    };
    Promise.all([
      readList<ProjectItem>(`${apiUrl}/projects`),
      readList<CycleRelease>(`${apiUrl}/releases`),
      readList<CycleEnvironment>(`${apiUrl}/test-environments`),
      readList<TestSuiteItem>(`${apiUrl}/test-suites`),
    ]).then(async ([p, r, e, s]) => {
      const activeProjects = (p as ProjectItem[]).filter((x) => x.isActive);
      const activeReleases = (r as CycleRelease[]).filter(
        (x) => x.status !== "Released" && x.status !== "Cancelled",
      );
      const buildGroups = await Promise.all(
        activeReleases.map((release) =>
          fetch(`${apiUrl}/releases/${release.releaseId}/builds`, {
            headers: h,
          }).then(async (x) => {
            if (!x.ok) throw new Error(`โหลด Build ไม่สำเร็จ (${x.status})`);
            const data: unknown = await x.json();
            return Array.isArray(data) ? (data as CycleBuild[]) : [];
          }),
        ),
      );
      setProjects(activeProjects);
      setReleases(activeReleases);
      setEnvironments(e);
      setSuites(s);
      setBuilds((buildGroups.flat() as CycleBuild[]).filter((x) => x.isActive));
      setProjectId((current) =>
        activeProjects.some((x) => x.projectId === current)
          ? current
          : activeProjects[0]?.projectId || "",
      );
    }).catch(() => {
      setProjects([]);
      setReleases([]);
      setBuilds([]);
      setEnvironments([]);
      setSuites([]);
    });
  }, [reload]);
  useEffect(() => {
    const query = new URLSearchParams({ page: String(page), size: String(pageSize) });
    if (contextProjectId) query.set("projectId", contextProjectId);
    if (contextReleaseId) query.set("releaseId", contextReleaseId);
    if (contextBuildId) query.set("buildId", contextBuildId);
    if (search.trim()) query.set("search", search.trim());
    setLoading(true);
    setError("");
    fetch(`${apiUrl}/test-cycles?${query}`, { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` } })
      .then(async response => {
        if (!response.ok) throw new Error(`โหลด Test Cycle ไม่สำเร็จ (${response.status})`);
        const data = await response.json();
        const container = data?.items ?? data;
        const rows = Array.isArray(container?.rows) ? container.rows : Array.isArray(container) ? container : [];
        setItems(rows);
        setTotalCount(Number(container?.total ?? rows.length));
      })
      .catch(reason => { setItems([]); setTotalCount(0); setError(reason instanceof Error ? reason.message : "โหลด Test Cycle ไม่สำเร็จ"); })
      .finally(() => setLoading(false));
  }, [contextProjectId, contextReleaseId, contextBuildId, search, page, pageSize, reload]);
  useEffect(() => { setPage(1); }, [contextProjectId, contextReleaseId, contextBuildId, search]);
  useEffect(()=>{const target=localStorage.getItem("qa.targetCycleId");if(!target)return;fetch(`${apiUrl}/test-cycles/${target}`,{headers:{Authorization:`Bearer ${localStorage.getItem("qa.accessToken")}`}}).then(r=>r.ok?r.json():null).then((cycle:TestCycleItem|null)=>{if(cycle)setDetail(cycle);localStorage.removeItem("qa.targetCycleId")}).catch(()=>localStorage.removeItem("qa.targetCycleId"))},[]);
  useEffect(() => {
    const lastPage = Math.max(1, Math.ceil(totalCount / pageSize));
    if (page > lastPage) setPage(lastPage);
  }, [page, pageSize, totalCount]);
  const projectReleases = useMemo(
      () => releases.filter((x) => x.projectId === projectId),
      [releases, projectId],
    ),
    releaseBuilds = useMemo(
      () => builds.filter((x) => x.releaseId === releaseId && x.isActive),
      [builds, releaseId],
    ),
    projectEnvironments = useMemo(
      () => environments.filter((x) => x.projectId === projectId && x.isActive),
      [environments, projectId],
    ),
    projectSuites = useMemo(
      () => suites.filter((x) => x.projectId === projectId && x.isActive),
      [suites, projectId],
    );
  useEffect(() => {
    if (!projectReleases.some((x) => x.releaseId === releaseId))
      setReleaseId(projectReleases[0]?.releaseId ?? "");
  }, [projectReleases, releaseId]);
  useEffect(() => {
    if (!releaseBuilds.some((x) => x.buildId === buildId))
      setBuildId(releaseBuilds[0]?.buildId ?? "");
  }, [releaseBuilds, buildId]);
  useEffect(() => {
    if (!projectEnvironments.some((x) => x.testEnvironmentId === environmentId))
      setEnvironmentId(projectEnvironments[0]?.testEnvironmentId ?? "");
  }, [projectEnvironments, environmentId]);
  useEffect(() => {
    if (!form || editing || !projectId) return;
    const project = projects.find((x) => x.projectId === projectId);
    setCode(
      nextBusinessCode(
        `${project?.projectCode ?? "PRJ"}-CYC`,
        items.map((x) => x.cycleCode),
      ),
    );
  }, [form, editing, projectId, projects, items]);
  const openForm = (cycle?: TestCycleItem) => {
    setEditing(cycle ?? null);
    setProjectId(cycle?.projectId ?? contextProjectId ?? projects[0]?.projectId ?? "");
    setReleaseId(cycle?.releaseId ?? contextReleaseId ?? "");
    setBuildId(cycle?.buildId ?? contextBuildId ?? "");
    setEnvironmentId(cycle?.environmentId ?? "");
    setSuiteId(cycle?.testSuiteId ?? "");
    const targetProjectId = cycle?.projectId ?? contextProjectId ?? projects[0]?.projectId ?? "";
    const project = projects.find((x) => x.projectId === targetProjectId);
    setCode(
      cycle?.cycleCode ??
        nextBusinessCode(
          `${project?.projectCode ?? "PRJ"}-CYC`,
          items.map((x) => x.cycleCode),
        ),
    );
    setName(cycle?.cycleName ?? "");
    setCycleType(cycle?.cycleType ?? cycleTypes[0]?.value ?? "");
    setStartDate(cycle?.startDate?.slice(0, 10) ?? "");
    setEndDate(cycle?.endDate?.slice(0, 10) ?? "");
    setNotes(cycle?.notes ?? "");
    setForm(true);
  };
  const openDetail = (cycle: TestCycleItem) => setDetail(cycle);
  const save = async () => {
    setSaving(true);
    try {
      const response = await fetch(
        `${apiUrl}/test-cycles${editing ? `/${editing.testCycleId}` : ""}`,
        {
          method: editing ? "PUT" : "POST",
          headers,
          body: JSON.stringify({
            projectId,
            releaseId,
            buildId,
            environmentId,
            testSuiteId: suiteId || null,
            cycleCode: editing ? code : "",
            cycleName: name,
            cycleType,
            startDate: startDate || null,
            endDate: endDate || null,
            ownerUserId: null,
            notes: notes || null,
            populateFromSuite: true,
            requiredOnly: false,
          }),
        },
      );
      if (!response.ok) {
        const p = await response.json();
        throw new Error(p.detail ?? "บันทึกไม่สำเร็จ");
      }
      setForm(false);
      setNotice(editing ? "แก้ไข Test Cycle แล้ว" : "สร้าง Test Cycle แล้ว");
      setReload((x) => x + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : "บันทึก Test Cycle ไม่สำเร็จ");
    } finally {
      setSaving(false);
    }
  };
  const createEnvironment = async () => {
    if (!projectId || !environmentName.trim()) return;
    setSaving(true);
    try {
      const response = await fetch(`${apiUrl}/test-environments`, {
        method: "POST",
        headers,
        body: JSON.stringify({
          projectId,
          environmentName,
          baseUrl: null,
          isActive: true,
        }),
      });
      if (!response.ok) throw new Error();
      const result = await response.json();
      setEnvironmentName("");
      setEnvironmentId(result.testEnvironmentId);
      setReload((x) => x + 1);
    } catch {
      setError("สร้าง Environment ไม่สำเร็จ");
    } finally {
      setSaving(false);
    }
  };
  const changeStatus = async (cycle: TestCycleItem, status: string) => {
    const response = await fetch(`${apiUrl}/test-cycles/${cycle.testCycleId}/status`, {
      method: "POST",
      headers,
      body: JSON.stringify({ status }),
    });
    if (!response.ok) { setError(`เปลี่ยนสถานะ ${cycle.cycleCode} ไม่สำเร็จ`); return; }
    setNotice(`เปลี่ยนสถานะ ${cycle.cycleCode} แล้ว`);
    setReload((x) => x + 1);
  };
  const remove = async (cycle: TestCycleItem) => {
    if (!window.confirm(`ยืนยันลบ ${cycle.cycleCode}?`)) return;
    const response = await fetch(`${apiUrl}/test-cycles/${cycle.testCycleId}`, {
      method: "DELETE",
      headers,
    });
    if (!response.ok) {
      setError("ลบ Test Cycle ไม่สำเร็จ");
      return;
    }
    setNotice(`ลบ ${cycle.cycleCode} แล้ว`);
    setReload((x) => x + 1);
  };
  const exportCsv = async () => {
    setExporting(true); setError("");
    try {
      const exported: TestCycleItem[] = [];
      const exportSize = 100;
      let exportPage = 1;
      let total = 0;
      do {
        const query = new URLSearchParams({ page: String(exportPage), size: String(exportSize) });
        if (contextProjectId) query.set("projectId", contextProjectId);
        if (contextReleaseId) query.set("releaseId", contextReleaseId);
        if (contextBuildId) query.set("buildId", contextBuildId);
        if (search.trim()) query.set("search", search.trim());
        const response = await fetch(`${apiUrl}/test-cycles?${query}`, { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` } });
        if (!response.ok) throw new Error(`ส่งออก Test Cycle ไม่สำเร็จ (${response.status})`);
        const data = await response.json();
        const container = data?.items ?? data;
        const batch = Array.isArray(container?.rows) ? container.rows as TestCycleItem[] : [];
        total = Number(container?.total ?? batch.length);
        exported.push(...batch);
        exportPage += 1;
      } while (exported.length < total);
      const csvRows = [["Cycle Code", "Name", "Release", "Build", "Environment", "Type", "Executed", "Cases", "Progress", "Status"], ...exported.map(item => [item.cycleCode, item.cycleName, item.releaseCode, item.buildNumber, item.environmentName, item.cycleType ?? "", item.executedCount, item.caseCount, `${item.progressPercent}%`, item.status])];
      const csv = "\ufeff" + csvRows.map(row => row.map(value => `"${String(value).replaceAll('"', '""')}"`).join(",")).join("\r\n");
      const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
      const link = document.createElement("a"); link.href = url; link.download = "test-cycles.csv"; link.click(); URL.revokeObjectURL(url);
      setNotice(`ส่งออก ${exported.length} Test Cycles แล้ว`);
    } catch (reason) { setError(reason instanceof Error ? reason.message : "ส่งออก Test Cycle ไม่สำเร็จ"); }
    finally { setExporting(false); }
  };
  const rows = items.filter((x) =>
    (!contextProjectId || x.projectId === contextProjectId) &&
    (!contextReleaseId || x.releaseId === contextReleaseId) &&
    (!contextBuildId || x.buildId === contextBuildId) &&
    `${x.cycleCode} ${x.cycleName} ${x.releaseCode} ${x.buildNumber}`
      .toLowerCase()
      .includes(search.toLowerCase()),
  );
  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize));
  return (
    <>
      <article className="card">
        {error && <div className="inline-alert error" role="alert"><span>{error}</span><button onClick={() => { setError(""); setReload(value => value + 1); }}>ลองใหม่</button></div>}
        {notice && <div className="inline-alert success" role="status"><span>{notice}</span><button aria-label="ปิดข้อความ" onClick={() => setNotice("")}>×</button></div>}
        <div className="table-tools">
          <span>{totalCount.toLocaleString()} Test Cycles</span>
          <div>
            {canExport && <button className="btn" disabled={exporting || loading || totalCount === 0} onClick={exportCsv}>{exporting ? "กำลัง Export..." : "Export CSV"}</button>}
            {canEdit && (
            <button className="btn primary" onClick={() => openForm()}>
              + สร้าง Test Cycle
            </button>
            )}
          </div>
        </div>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Cycle Code</th>
                <th>Name</th>
                <th>Release / Build</th>
                <th>Environment</th>
                <th>Type</th>
                <th>Progress</th>
                <th>Status</th>
                {canEdit && <th>จัดการ</th>}
              </tr>
            </thead>
            <tbody>
              {loading && <tr><td className="empty-cell" colSpan={canEdit ? 8 : 7}><div className="empty-state"><span aria-hidden="true">…</span><b>กำลังโหลด Test Cycle...</b></div></td></tr>}
              {!loading && !error && rows.length === 0 && <tr><td className="empty-cell" colSpan={canEdit ? 8 : 7}><div className="empty-state"><span aria-hidden="true">◎</span><b>ไม่พบ Test Cycle</b><small>ลองเปลี่ยน Project, Release, Build หรือคำค้นหา</small></div></td></tr>}
              {rows.map((x) => (
                <tr key={x.testCycleId}>
                  <td>
                    <button className="link-button" onClick={() => openDetail(x)}>{x.cycleCode}</button>
                  </td>
                  <td>{x.cycleName}</td>
                  <td>
                    {x.releaseCode}
                    <small className="cell-sub">{x.buildNumber}</small>
                  </td>
                  <td>{x.environmentName}</td>
                  <td>{x.cycleType ?? "-"}</td>
                  <td>
                    <div className="progress-cell">
                      <span>
                        <i style={{ width: `${x.progressPercent}%` }} />
                      </span>
                      <small>
                        {x.executedCount}/{x.caseCount} · {x.progressPercent}%
                      </small>
                    </div>
                  </td>
                  <td>
                    <Badge
                      tone={
                        x.status === "Closed" || x.status === "Completed"
                          ? "green"
                          : x.status === "Cancelled"
                            ? "red"
                            : "yellow"
                      }
                    >
                      {x.status}
                    </Badge>
                  </td>
                  {canEdit && <td>
                    <div className="row-actions">
                      <button
                        className="table-action"
                        onClick={() => openForm(x)}
                      >
                        แก้ไข
                      </button>
                      {x.status === "Draft" && (
                        <button
                          className="table-action"
                          onClick={() => changeStatus(x, "InProgress")}
                        >
                          เริ่ม
                        </button>
                      )}
                      {x.status === "InProgress" && (
                        <button
                          className="table-action"
                          onClick={() => changeStatus(x, "Closed")}
                        >
                          ปิด Cycle
                        </button>
                      )}
                      <button
                        className="table-action danger-action"
                        onClick={() => remove(x)}
                      >
                        ลบ
                      </button>
                    </div>
                  </td>}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="pagination">
          <label>แสดง<select value={pageSize} onChange={event => { setPageSize(Number(event.target.value)); setPage(1); }}><option>10</option><option>20</option><option>50</option></select> รายการ</label>
          <span>หน้า {Math.min(page, pageCount)} / {pageCount} ({totalCount.toLocaleString()} รายการ)</span>
          <button className="btn" disabled={loading || page <= 1} onClick={() => setPage(value => value - 1)}>ก่อนหน้า</button>
          <button className="btn" disabled={loading || page >= pageCount} onClick={() => setPage(value => value + 1)}>ถัดไป</button>
        </div>
      </article>
      {detail && (
        <div className="modal" role="presentation" onMouseDown={() => setDetail(null)}>
          <div className="modal-box cycle-modal cycle-detail-modal" role="dialog" aria-modal="true" aria-labelledby="cycle-detail-title" onMouseDown={event => event.stopPropagation()}>
            <div className="modal-head">
              <div><span className="cycle-detail-eyebrow">TEST CYCLE</span><h2 id="cycle-detail-title">{detail.cycleCode}</h2><small>{projects.find(project => project.projectId === detail.projectId)?.projectName ?? "-"}</small></div>
              <button aria-label="ปิดรายละเอียด Test Cycle" onClick={() => setDetail(null)}>×</button>
            </div>
            <section className="cycle-detail-hero">
              <div><h3>{detail.cycleName}</h3><p>{detail.releaseCode} <span aria-hidden="true">•</span> Build {detail.buildNumber}</p></div>
              <div className="cycle-detail-badges"><Badge tone={detail.status === "Completed" || detail.status === "Closed" ? "green" : detail.status === "Cancelled" ? "red" : "yellow"}>{detail.status}</Badge>{detail.cycleType && <Badge tone="blue">{detail.cycleType}</Badge>}</div>
            </section>
            <section className="cycle-detail-progress" aria-label={`ดำเนินการแล้ว ${detail.progressPercent}%`}>
              <div className="cycle-detail-progress-head"><div><span>Execution Progress</span><strong>{detail.progressPercent}%</strong></div><small>{detail.executedCount.toLocaleString()} จาก {detail.caseCount.toLocaleString()} Test Cases</small></div>
              <div className="cycle-detail-progress-track"><span style={{ width: `${Math.min(100, Math.max(0, detail.progressPercent))}%` }} /></div>
              <div className="cycle-detail-progress-stats"><span><b>{detail.executedCount.toLocaleString()}</b>ดำเนินการแล้ว</span><span><b>{Math.max(0, detail.caseCount - detail.executedCount).toLocaleString()}</b>คงเหลือ</span><span><b>{detail.caseCount.toLocaleString()}</b>ทั้งหมด</span></div>
            </section>
            <section className="cycle-detail-section">
              <h3>ข้อมูลการทดสอบ</h3>
              <dl className="cycle-detail-grid">
                <div><dt><span aria-hidden="true">◫</span> Release</dt><dd>{detail.releaseCode || "-"}</dd></div>
                <div><dt><span aria-hidden="true">#</span> Build</dt><dd>{detail.buildNumber || "-"}</dd></div>
                <div><dt><span aria-hidden="true">◎</span> Environment</dt><dd>{detail.environmentName || "-"}</dd></div>
                <div className="wide"><dt><span aria-hidden="true">▤</span> Test Suite</dt><dd>{detail.suiteName || "ไม่ระบุ Suite"}</dd></div>
              </dl>
            </section>
            <section className="cycle-detail-section">
              <h3>กำหนดการ</h3>
              <div className="cycle-detail-timeline">
                <div><span aria-hidden="true">S</span><small>Start Date</small><b>{detail.startDate ? new Date(detail.startDate).toLocaleDateString("th-TH", { day: "numeric", month: "short", year: "numeric" }) : "ไม่ระบุ"}</b></div>
                <i aria-hidden="true" />
                <div><span aria-hidden="true">E</span><small>End Date</small><b>{detail.endDate ? new Date(detail.endDate).toLocaleDateString("th-TH", { day: "numeric", month: "short", year: "numeric" }) : "ไม่ระบุ"}</b></div>
              </div>
            </section>
            <section className="cycle-detail-notes"><div aria-hidden="true">i</div><span><b>Notes</b><p>{detail.notes || "ไม่มี Notes สำหรับ Test Cycle นี้"}</p></span></section>
            <div className="modal-actions">
              <button className="btn" onClick={() => setDetail(null)}>ปิด</button>
              {canEdit && <button className="btn primary" onClick={() => { const cycle = detail; setDetail(null); openForm(cycle); }}>แก้ไข</button>}
            </div>
          </div>
        </div>
      )}
      {form && (
        <div className="modal" onMouseDown={() => setForm(false)}>
          <div
            className="modal-box cycle-modal"
            onMouseDown={(e) => e.stopPropagation()}
          >
            <div className="modal-head">
              <h2>{editing ? "แก้ไข" : "สร้าง"} Test Cycle</h2>
              <button onClick={() => setForm(false)}>×</button>
            </div>
            <div className="form-grid">
              <label>
                Project
                <select
                  disabled
                  value={projectId}
                  onChange={(e) => setProjectId(e.target.value)}
                >
                  {projects.map((x) => (
                    <option key={x.projectId} value={x.projectId}>
                      {x.projectName}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Release
                <select
                  disabled={!!editing}
                  value={releaseId}
                  onChange={(e) => setReleaseId(e.target.value)}
                >
                  <option value="">เลือก Release</option>
                  {projectReleases.map((x) => (
                    <option key={x.releaseId} value={x.releaseId}>
                      {x.releaseCode}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Build
                <select
                  disabled={!!editing}
                  value={buildId}
                  onChange={(e) => setBuildId(e.target.value)}
                >
                  <option value="">เลือก Build</option>
                  {releaseBuilds.map((x) => (
                    <option key={x.buildId} value={x.buildId}>
                      {x.buildNumber}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Environment
                <select
                  disabled={!!editing}
                  value={environmentId}
                  onChange={(e) => setEnvironmentId(e.target.value)}
                >
                  <option value="">เลือก Environment</option>
                  {projectEnvironments.map((x) => (
                    <option
                      key={x.testEnvironmentId}
                      value={x.testEnvironmentId}
                    >
                      {x.environmentName}
                    </option>
                  ))}
                </select>
              </label>
              {!editing && !projectEnvironments.length && (
                <div className="full inline-create">
                  <input
                    value={environmentName}
                    onChange={(e) => setEnvironmentName(e.target.value)}
                    placeholder="ชื่อ Environment เช่น QA Server"
                  />
                  <button
                    className="btn"
                    onClick={createEnvironment}
                    disabled={saving || !environmentName.trim()}
                  >
                    + สร้าง Environment
                  </button>
                </div>
              )}
              <label>
                Test Suite
                <select
                  disabled={!!editing}
                  value={suiteId}
                  onChange={(e) => setSuiteId(e.target.value)}
                >
                  <option value="">ไม่ระบุ Suite</option>
                  {projectSuites.map((x) => (
                    <option key={x.testSuiteId} value={x.testSuiteId}>
                      {x.suiteName}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Cycle Type
                <select
                  value={cycleType}
                  onChange={(e) => setCycleType(e.target.value)}
                >
                  {masterOptionElements(cycleTypes, cycleType)}
                </select>
              </label>
              <label>
                Cycle Code
                <input
                  disabled
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                />
              </label>
              <label>
                Cycle Name
                <input value={name} onChange={(e) => setName(e.target.value)} />
              </label>
              <label>
                Start Date
                <input
                  type="date"
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                />
              </label>
              <label>
                End Date
                <input
                  type="date"
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                />
              </label>
              <label className="full">
                Notes
                <textarea
                  rows={3}
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                />
              </label>
            </div>
            <div className="modal-actions">
              <button className="btn" onClick={() => setForm(false)}>
                ยกเลิก
              </button>
              <button
                className="btn primary"
                disabled={
                  saving ||
                  !projectId ||
                  !releaseId ||
                  !buildId ||
                  !environmentId ||
                  !code.trim() ||
                  !name.trim()
                }
                onClick={save}
              >
                {saving ? "กำลังบันทึก..." : "บันทึก Test Cycle"}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
type ExecutionCase = {
  testCycleCaseId: string;
  testCaseId: string;
  testCaseCode: string;
  title: string;
  preconditions?: string;
  priority: string;
  currentStatus: string;
  executionOrder: number;
  steps: {
    stepNo: number;
    action: string;
    testData?: string;
    expectedResult: string;
  }[];
  history: {
    testExecutionId: string;
    executionNo: number;
    status: string;
    actualResult?: string;
    comment?: string;
    testerName: string;
    completedAt?: string;
  }[];
};
type ExecutionWorkspace = {
  testCycleId: string;
  cycleCode: string;
  cycleName: string;
  status: string;
  buildNumber: string;
  environmentName: string;
  cases: ExecutionCase[];
};
function ExecutionWorkspacePage({ contextProjectId, contextReleaseId, contextBuildId }: { contextProjectId?: string; contextReleaseId?: string; contextBuildId?: string }) {
  const [cycles, setCycles] = useState<TestCycleItem[]>([]),
    [cycleId, setCycleId] = useState(()=>localStorage.getItem("qa.targetCycleId")??""),
    [workspace, setWorkspace] = useState<ExecutionWorkspace | null>(null),
    [selectedId, setSelectedId] = useState(""),
    [caseDetail, setCaseDetail] = useState<ExecutionCase | null>(null),
    [stepStatuses, setStepStatuses] = useState<Record<number, string>>({}),
    [stepActuals, setStepActuals] = useState<Record<number, string>>({}),
    [actual, setActual] = useState(""),
    [comment, setComment] = useState(""),
    [caseSearch, setCaseSearch] = useState(""),
    [statusFilter, setStatusFilter] = useState("All"),
    [saving, setSaving] = useState(false),
    [reload, setReload] = useState(0);
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
  useEffect(() => {
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetch(`${apiUrl}/test-cycles`, { headers: h })
      .then(async (r) => {
        if (!r.ok) throw new Error(`โหลด Test Cycle ไม่สำเร็จ (${r.status})`);
        const data: unknown = await r.json();
        return Array.isArray(data) ? (data as TestCycleItem[]) : (data as any)?.items?.rows ?? [];
      })
      .then((data: TestCycleItem[]) => {
        setCycles(data);
        setCycleId((current) => data.some(x=>x.testCycleId===current)?current:(data[0]?.testCycleId||""));
        localStorage.removeItem("qa.targetCycleId");
      })
      .catch(() => {
        setCycles([]);
        setCycleId("");
      });
  }, [reload]);
  useEffect(() => {
    if (!cycleId) {
      setWorkspace(null);
      return;
    }
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetch(`${apiUrl}/test-cycles/${cycleId}/execution`, { headers: h })
      .then((r) => (r.ok ? r.json() : null))
      .then((data: ExecutionWorkspace | null) => {
        setWorkspace(data);
        setSelectedId((current) =>
          data?.cases.some((x) => x.testCycleCaseId === current)
            ? current
            : (data?.cases[0]?.testCycleCaseId ?? ""),
        );
      });
  }, [cycleId, reload]);
  useEffect(() => {
    if (!selectedId || !cycleId) { setCaseDetail(null); return; }
    const h = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
    fetch(`${apiUrl}/test-cycles/${cycleId}/cases/${selectedId}`, { headers: h })
      .then(r => r.ok ? r.json() : null)
      .then((data) => setCaseDetail(data))
      .catch(() => setCaseDetail(null));
  }, [selectedId, cycleId, reload]);
  const selected = useMemo(
    () => {
      const base = workspace?.cases.find((x) => x.testCycleCaseId === selectedId);
      if (!base) return undefined;
      return caseDetail && caseDetail.testCycleCaseId === selectedId ? { ...base, ...caseDetail } : { ...base, steps: [], history: [] };
    },
    [workspace, selectedId, caseDetail],
  );
  const executionStats = useMemo(() => {
    const cases = workspace?.cases ?? [];
    return {
      total: cases.length,
      passed: cases.filter((x) => x.currentStatus === "Pass").length,
      failed: cases.filter((x) => x.currentStatus === "Fail").length,
      blocked: cases.filter((x) => x.currentStatus === "Blocked").length,
      pending: cases.filter((x) => x.currentStatus === "NotRun").length,
    };
  }, [workspace]);
  const filteredCases = useMemo(() => {
    const query = caseSearch.trim().toLocaleLowerCase("th-TH");
    return (workspace?.cases ?? []).filter((item) =>
      (statusFilter === "All" || item.currentStatus === statusFilter) &&
      (!query || `${item.testCaseCode} ${item.title}`.toLocaleLowerCase("th-TH").includes(query)),
    );
  }, [workspace, caseSearch, statusFilter]);
  useEffect(() => {
    if (selected) {
      setStepStatuses(
        Object.fromEntries(selected.steps.map((x) => [x.stepNo, "NotRun"])),
      );
      setStepActuals({});
      setActual("");
      setComment("");
    }
  }, [selected]);
  const finalize = async (status: string) => {
    if (
      !selected ||
      !window.confirm(
        `ยืนยันบันทึกผล ${status} สำหรับ ${selected.testCaseCode}?\nผลที่บันทึกแล้วจะไม่สามารถแก้ไขทับได้`,
      )
    )
      return;
    setSaving(true);
    try {
      const response = await fetch(
        `${apiUrl}/test-cycle-cases/${selected.testCycleCaseId}/executions`,
        {
          method: "POST",
          headers,
          body: JSON.stringify({
            status,
            actualResult: actual || null,
            comment: comment || null,
            stepResults: selected.steps.map((x) => ({
              stepNo: x.stepNo,
              status: stepStatuses[x.stepNo] ?? "NotRun",
              actualResult: stepActuals[x.stepNo] || null,
              comment: null,
            })),
          }),
        },
      );
      if (!response.ok) {
        const problem = await response.json();
        throw new Error(problem.detail ?? "บันทึกผลไม่สำเร็จ");
      }
      setReload((x) => x + 1);
    } catch (e) {
      window.alert(e instanceof Error ? e.message : "บันทึกผลไม่สำเร็จ");
    } finally {
      setSaving(false);
    }
  };
  const removeExecution = async (execution: ExecutionCase["history"][number]) => {
    if (!window.confirm(`ยืนยันลบผลการทดสอบ Run #${execution.executionNo}?\nข้อมูลจะถูกซ่อน แต่ยังเก็บไว้สำหรับ Audit`)) return;
    const response = await fetch(`${apiUrl}/executions/${execution.testExecutionId}`, {
      method: "DELETE",
      headers,
    });
    if (!response.ok) {
      window.alert("ลบผลการทดสอบไม่สำเร็จ");
      return;
    }
    setReload((x) => x + 1);
  };
  const filteredCycles = useMemo(() => cycles.filter((x) =>
    (!contextProjectId || x.projectId === contextProjectId) &&
    (!contextReleaseId || x.releaseId === contextReleaseId) &&
    (!contextBuildId || x.buildId === contextBuildId)
  ), [cycles, contextProjectId, contextReleaseId, contextBuildId]);
  useEffect(() => {
    if (filteredCycles.length && !filteredCycles.some((x) => x.testCycleId === cycleId)) {
      setCycleId(filteredCycles[0].testCycleId);
    }
  }, [filteredCycles, cycleId]);
  return (
    <div className="execution-page">
      <div className="execution-toolbar card">
        <label>
          Test Cycle
          <select value={cycleId} onChange={(e) => setCycleId(e.target.value)}>
            <option value="">เลือก Test Cycle</option>
            {filteredCycles.map((x) => (
              <option key={x.testCycleId} value={x.testCycleId}>
                {x.cycleCode} · {x.cycleName}
              </option>
            ))}
          </select>
        </label>
        {workspace && (
          <div className="execution-context">
            <span>
              <small>Build</small>
              <b>{workspace.buildNumber}</b>
            </span>
            <span>
              <small>Environment</small>
              <b>{workspace.environmentName}</b>
            </span>
            <Badge tone={workspace.status === "Closed" ? "green" : "yellow"}>
              {workspace.status}
            </Badge>
          </div>
        )}
      </div>
      {workspace && (
        <div className="execution-overview">
          <div><small>Test Cases</small><strong>{executionStats.total}</strong></div>
          <div className="metric-pass"><small>Passed</small><strong>{executionStats.passed}</strong></div>
          <div className="metric-fail"><small>Failed</small><strong>{executionStats.failed}</strong></div>
          <div className="metric-blocked"><small>Blocked</small><strong>{executionStats.blocked}</strong></div>
          <div className="metric-pending"><small>Not Run</small><strong>{executionStats.pending}</strong></div>
          <div className="execution-progress-summary">
            <span><i style={{width:`${executionStats.total ? ((executionStats.total-executionStats.pending)/executionStats.total)*100 : 0}%`}} /></span>
            <small>{executionStats.total ? Math.round(((executionStats.total-executionStats.pending)/executionStats.total)*100) : 0}% executed</small>
          </div>
        </div>
      )}
      {!workspace ? (
        <article className="card empty">
          <h3>เลือก Test Cycle เพื่อเริ่มทดสอบ</h3>
          <p>
            สร้าง Cycle และ Populate Test Case จาก Test Suite ก่อนเข้า Execution
            Workspace
          </p>
        </article>
      ) : !workspace.cases.length ? (
        <article className="card empty">
          <h3>Cycle นี้ยังไม่มี Test Case</h3>
          <p>เลือก Test Suite ตอนสร้าง Cycle เพื่อเพิ่ม Test Case อัตโนมัติ</p>
        </article>
      ) : (
        <div className="execution-layout">
          <aside className="card case-queue">
            <div className="card-title">
              <div>
                <h3>Test Cases</h3>
                <p>{workspace.cases.length} รายการ · เลือกเพื่อบันทึกผล</p>
              </div>
            </div>
            <div className="case-queue-tools">
              <input aria-label="ค้นหา Test Case" value={caseSearch} onChange={(e) => setCaseSearch(e.target.value)} placeholder="ค้นหารหัสหรือชื่อ Test Case" />
              <select aria-label="กรองสถานะ" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
                {["All", "NotRun", "Pass", "Fail", "Blocked", "Skipped"].map((status) => <option key={status} value={status}>{status === "All" ? "ทุกสถานะ" : status}</option>)}
              </select>
            </div>
            <div className="case-queue-list">
            {filteredCases.map((x) => (
              <button
                className={selectedId === x.testCycleCaseId ? "active" : ""}
                key={x.testCycleCaseId}
                onClick={() => setSelectedId(x.testCycleCaseId)}
              >
                <Badge tone={x.priority === "P0" ? "red" : "blue"}>
                  {x.priority}
                </Badge>
                <span>
                  <b>{x.testCaseCode}</b>
                  <small>{x.title}</small>
                </span>
                <i className={`status-dot ${x.currentStatus.toLowerCase()}`}>
                  {x.currentStatus}
                </i>
              </button>
            ))}
            {!filteredCases.length && <p className="queue-empty">ไม่พบ Test Case ที่ตรงกับตัวกรอง</p>}
            </div>
          </aside>
          {selected && (
            <main className="card execution-main">
              <div className="execution-case-head">
                <div>
                  <span>{selected.testCaseCode}</span>
                  <h2>{selected.title}</h2>
                </div>
                <Badge
                  tone={
                    selected.currentStatus === "Pass"
                      ? "green"
                      : selected.currentStatus === "Fail"
                        ? "red"
                        : "yellow"
                  }
                >
                  {selected.currentStatus}
                </Badge>
              </div>
              {selected.preconditions && (
                <div className="precondition">
                  <b>Preconditions</b>
                  <p>{selected.preconditions}</p>
                </div>
              )}
              <div className="step-table">
                <div className="step-bulk-actions">
                  <span>Test Steps <b>{selected.steps.length}</b></span>
                  <div>{["Pass", "Fail", "NotRun"].map((status) => <button type="button" key={status} onClick={() => setStepStatuses(Object.fromEntries(selected.steps.map((step) => [step.stepNo, status])))}>ทั้งหมด: {status}</button>)}</div>
                </div>
                <div className="step-row step-head">
                  <span>#</span>
                  <span>Action / Test Data</span>
                  <span>Expected Result</span>
                  <span>Result</span>
                </div>
                {selected.steps.map((x) => (
                  <div className="step-row" key={x.stepNo}>
                    <span>{x.stepNo}</span>
                    <span>
                      <b>{x.action}</b>
                      {x.testData && <small>{x.testData}</small>}
                    </span>
                    <span>{x.expectedResult}</span>
                    <span>
                      <select
                        value={stepStatuses[x.stepNo] ?? "NotRun"}
                        onChange={(e) =>
                          setStepStatuses((s) => ({
                            ...s,
                            [x.stepNo]: e.target.value,
                          }))
                        }
                      >
                        <option>NotRun</option>
                        <option>Pass</option>
                        <option>Fail</option>
                        <option>Blocked</option>
                        <option>Skipped</option>
                      </select>
                      <input
                        value={stepActuals[x.stepNo] ?? ""}
                        onChange={(e) =>
                          setStepActuals((s) => ({
                            ...s,
                            [x.stepNo]: e.target.value,
                          }))
                        }
                        placeholder="ผลที่ได้จริง"
                      />
                    </span>
                  </div>
                ))}
              </div>
              <div className="execution-notes">
                <label>
                  Actual Result
                  <textarea
                    rows={3}
                    value={actual}
                    onChange={(e) => setActual(e.target.value)}
                    placeholder="สรุปผลที่เกิดขึ้นจริง"
                  />
                </label>
                <label>
                  Comment
                  <textarea
                    rows={3}
                    value={comment}
                    onChange={(e) => setComment(e.target.value)}
                    placeholder="หมายเหตุเพิ่มเติม"
                  />
                </label>
              </div>
              <div className="execution-actions">
                <button
                  className="result-btn pass"
                  disabled={saving}
                  onClick={() => finalize("Pass")}
                >
                  ✓ Pass
                </button>
                <button
                  className="result-btn fail"
                  disabled={saving}
                  onClick={() => finalize("Fail")}
                >
                  × Fail
                </button>
                <button
                  className="result-btn blocked"
                  disabled={saving}
                  onClick={() => finalize("Blocked")}
                >
                  ! Blocked
                </button>
                <button
                  className="result-btn skip"
                  disabled={saving}
                  onClick={() => finalize("Skipped")}
                >
                  → Skip
                </button>
              </div>
            </main>
          )}
          <aside className="card execution-history">
            <div className="history-title">
              <div><h3>Execution History</h3><p>ประวัติของ {selected?.testCaseCode ?? "-"}</p></div>
              <span>{selected?.history.length ?? 0} Runs</span>
            </div>
            {selected?.history.length ? (
              selected.history.map((x) => (
                <div className="history-item" key={x.testExecutionId}>
                  <div className="history-item-head">
                    <Badge
                      tone={
                        x.status === "Pass"
                          ? "green"
                          : x.status === "Fail"
                            ? "red"
                            : "yellow"
                      }
                    >
                      {x.status}
                    </Badge>
                    <span className="history-run">Run #{x.executionNo}</span>
                    <button className="history-delete" onClick={() => removeExecution(x)} title="ลบผลการทดสอบ">ลบ</button>
                  </div>
                  <p>{x.actualResult || "-"}</p>
                  <small>
                    {x.testerName} ·{" "}
                    {x.completedAt
                      ? new Date(x.completedAt).toLocaleString("th-TH")
                      : "-"}
                  </small>
                </div>
              ))
            ) : (
              <p className="muted-text">ยังไม่มีประวัติการทดสอบ</p>
            )}
          </aside>
        </div>
      )}
    </div>
  );
}
type TestSuiteItem = {
  testSuiteId: string;
  projectId: string;
  suiteCode: string;
  suiteName: string;
  suiteType?: string;
  description?: string;
  riskTier?: string;
  isActive: boolean;
  cycleCount: number;
  cases: {
    testCaseId: string;
    testCaseCode: string;
    title: string;
    priority: string;
    sortOrder: number;
    isRequired: boolean;
  }[];
};
type GeneratedTestSuiteDraft={suiteName:string;suiteType:string;description:string;riskTier:string;testCases:{testCaseId:string;isRequired:boolean;reason:string}[];selectionSummary:string};
function TestSuitesPage({
  search,
  canEdit,
  contextProjectId,
}: {
  search: string;
  canEdit: boolean;
  contextProjectId?: string;
}) {
  const masterOptions = useMasterOptions(), suiteTypes = masterOptions("TestSuiteType"), riskTiers = masterOptions("TestSuiteRiskTier");
  const [items, setItems] = useState<TestSuiteItem[]>([]),
    [projects, setProjects] = useState<ProjectItem[]>([]),
    [modules, setModules] = useState<ModuleItem[]>([]),
    [testCases, setTestCases] = useState<TestCaseItem[]>([]),
    [reload, setReload] = useState(0),
    [form, setForm] = useState(false),
    [editing, setEditing] = useState<TestSuiteItem | null>(null),
    [managing, setManaging] = useState<TestSuiteItem | null>(null),
    [detail, setDetail] = useState<TestSuiteItem | null>(null),
    [checked, setChecked] = useState<string[]>([]),
    [saving, setSaving] = useState(false),
    [error, setError] = useState(""),
    [projectFilter, setProjectFilter] = useState(contextProjectId ?? ""),
    [typeFilter, setTypeFilter] = useState(""),
    [riskFilter, setRiskFilter] = useState(""),
    [activeFilter, setActiveFilter] = useState("active"),
    [caseSearch, setCaseSearch] = useState(""),
    [caseModuleFilter, setCaseModuleFilter] = useState(""),
    [casePriorityFilter, setCasePriorityFilter] = useState(""),
    [caseTypeFilter, setCaseTypeFilter] = useState(""),
    [caseStatusFilter, setCaseStatusFilter] = useState(""),
    [addRequired, setAddRequired] = useState(true),
    [suiteAiModal,setSuiteAiModal]=useState(false),[suiteAiGenerating,setSuiteAiGenerating]=useState(false),[suiteAiError,setSuiteAiError]=useState(""),
    [suiteAiProjectId,setSuiteAiProjectId]=useState(""),[suiteAiModuleId,setSuiteAiModuleId]=useState(""),[suiteAiModules,setSuiteAiModules]=useState<ModuleItem[]>([]),
    [suiteAiDrafts,setSuiteAiDrafts]=useState<GeneratedTestSuiteDraft[]>([]),[suiteAiExpanded,setSuiteAiExpanded]=useState<number|undefined>(undefined);
  const [code, setCode] = useState(""),
    [name, setName] = useState(""),
    [type, setType] = useState(""),
    [risk, setRisk] = useState(""),
    [description, setDescription] = useState(""),
    [projectId, setProjectId] = useState(""),
    [active, setActive] = useState(true);
  const headers = useMemo(() => ({
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  }), []);
  useEffect(() => { if (contextProjectId) setProjectFilter(contextProjectId); }, [contextProjectId]);
  useEffect(() => {
    const requestHeaders = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    Promise.all([
      fetch(`${apiUrl}/test-suites`, { headers: requestHeaders }).then((r) =>
        r.json(),
      ),
      fetch(`${apiUrl}/projects`, { headers: requestHeaders }).then((r) =>
        r.json(),
      ),
      fetch(`${apiUrl}/test-cases`, { headers: requestHeaders }).then((r) =>
        r.json(),
      ),
    ]).then(([s, p, t]) => {
      setItems(Array.isArray(s) ? s : (s as any)?.rows ?? []);
      const activeProjects = (p as ProjectItem[]).filter((x) => x.isActive);
      setProjects(activeProjects);
      setTestCases(Array.isArray(t) ? t : (t as any)?.rows ?? []);
      setProjectId((current) => current || activeProjects[0]?.projectId || "");
    });
  }, [reload]);
  useEffect(() => {
    const target = managing?.projectId ?? projectFilter ?? contextProjectId;
    if (!target) { setModules([]); return; }
    fetch(`${apiUrl}/projects/${target}/modules`, { headers }).then(r => r.ok ? r.json() : []).then((rows: ModuleItem[]) => setModules(rows.filter(x => x.isActive)));
  }, [headers, managing?.projectId, projectFilter, contextProjectId]);
  useEffect(()=>{
    if(!suiteAiProjectId){setSuiteAiModules([]);setSuiteAiModuleId("");return;}
    fetch(`${apiUrl}/projects/${suiteAiProjectId}/modules`,{headers}).then(async response=>response.ok?response.json():Promise.reject(new Error("โหลด Module ไม่สำเร็จ"))).then((rows:ModuleItem[])=>{const active=rows.filter(x=>x.isActive);setSuiteAiModules(active);setSuiteAiModuleId(current=>active.some(x=>x.moduleId===current)?current:(active[0]?.moduleId??""));}).catch(error=>setSuiteAiError(error instanceof Error?error.message:"โหลด Module ไม่สำเร็จ"));
  },[suiteAiProjectId,headers]);
  useEffect(() => {
    if (!form || editing || !projectId) return;
    const project = projects.find((x) => x.projectId === projectId);
    setCode(
      nextBusinessCode(
        `${project?.projectCode ?? "PRJ"}-TS`,
        items.map((x) => x.suiteCode),
      ),
    );
  }, [form, editing, projectId, projects, items]);
  const openForm = (suite?: TestSuiteItem) => {
    setEditing(suite ?? null);
    const targetProjectId = suite?.projectId ?? projects[0]?.projectId ?? "";
    const project = projects.find((x) => x.projectId === targetProjectId);
    setCode(
      suite?.suiteCode ??
        nextBusinessCode(
          `${project?.projectCode ?? "PRJ"}-TS`,
          items.map((x) => x.suiteCode),
        ),
    );
    setName(suite?.suiteName ?? "");
    setType(suite?.suiteType ?? suiteTypes[0]?.value ?? "");
    setRisk(suite?.riskTier ?? riskTiers[0]?.value ?? "");
    setDescription(suite?.description ?? "");
    setProjectId(suite?.projectId ?? contextProjectId ?? projects[0]?.projectId ?? "");
    setActive(suite?.isActive ?? true);
    setForm(true);
  };
  const openSuiteAi=()=>{const targetProject=contextProjectId||projectFilter||projects[0]?.projectId||"";setSuiteAiProjectId(targetProject);setSuiteAiModuleId("");setSuiteAiError("");setSuiteAiDrafts([]);setSuiteAiExpanded(undefined);setSuiteAiModal(true);};
  const generateSuiteWithAi=async()=>{if(!suiteAiProjectId||!suiteAiModuleId)return;setSuiteAiGenerating(true);setSuiteAiError("");try{const response=await fetch(`${apiUrl}/test-suites/generate-ai`,{method:"POST",headers,body:JSON.stringify({projectId:suiteAiProjectId,moduleId:suiteAiModuleId,suiteTypes:suiteTypes.map(x=>x.value),riskTiers:riskTiers.map(x=>x.value)})});if(!response.ok){const problem=await response.json().catch(()=>null);throw new Error(problem?.detail??"AI Generate Test Suite ไม่สำเร็จ");}const drafts:GeneratedTestSuiteDraft[]=await response.json();if(!Array.isArray(drafts)||!drafts.length)throw new Error("AI ไม่ได้สร้าง Test Suite กลับมา");setSuiteAiDrafts(drafts);setSuiteAiExpanded(0);}catch(error){if(error instanceof SyntaxError)setSuiteAiError("AI ส่งข้อมูลกลับมาในรูปแบบที่ไม่ถูกต้อง กรุณาลองใหม่");else setSuiteAiError(error instanceof Error?error.message:"AI Generate Test Suite ไม่สำเร็จ");}finally{setSuiteAiGenerating(false);}};
  const removeSuiteAiDraft=(index:number)=>setSuiteAiDrafts(drafts=>{const next=drafts.filter((_,i)=>i!==index);if(next.length===0){setSuiteAiModal(false);}return next;});
  const saveAllSuiteDrafts=async()=>{if(!suiteAiDrafts.length)return;setSuiteAiGenerating(true);setSuiteAiError("");try{let created=0;for(const draft of suiteAiDrafts){const body={code:"",name:draft.suiteName,projectId:suiteAiProjectId,moduleId:suiteAiModuleId,suiteType:draft.suiteType,riskTier:draft.riskTier,description:draft.description,isActive:true};const res=await fetch(`${apiUrl}/test-suites`,{method:"POST",headers,body:JSON.stringify(body)});if(!res.ok){const problem=await res.json().catch(()=>null);throw new Error(`สร้าง Suite "${draft.suiteName}" ไม่สำเร็จ: ${problem?.detail??""}`);}const saved:TestSuiteItem=await res.json();const required=draft.testCases.filter(x=>x.isRequired).map(x=>x.testCaseId),optional=draft.testCases.filter(x=>!x.isRequired).map(x=>x.testCaseId);for(const [ids,isRequired] of [[required,true],[optional,false]] as const){if(!ids.length)continue;const ar=await fetch(`${apiUrl}/test-suites/${saved.testSuiteId}/cases`,{method:"POST",headers,body:JSON.stringify({testCaseIds:ids,isRequired})});if(!ar.ok)throw new Error(`สร้าง "${draft.suiteName}" แล้ว แต่กำหนด Test Case ไม่สำเร็จ`);}created++;}setSuiteAiDrafts([]);setSuiteAiModal(false);setReload(x=>x+1);}catch(error){setSuiteAiError(error instanceof Error?error.message:"บันทึก Test Suite ไม่สำเร็จ");}finally{setSuiteAiGenerating(false);}};
  const save = async () => {
    setSaving(true);
    try {
      const response = await fetch(
        `${apiUrl}/test-suites${editing ? `/${editing.testSuiteId}` : ""}`,
        {
          method: editing ? "PUT" : "POST",
          headers,
          body: JSON.stringify({
            projectId,
            suiteCode: editing ? code : "",
            suiteName: name,
            suiteType: type,
            description: description || null,
            riskTier: risk,
            isActive: active,
          }),
        },
      );
      if (!response.ok) {
        const problem = await response.json();
        throw new Error(problem.detail ?? "บันทึกไม่สำเร็จ");
      }
      await response.json();
      setForm(false);
      setReload((x) => x + 1);
    } catch (e) {
      window.alert(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ");
    } finally {
      setSaving(false);
    }
  };
  const addCases = async () => {
    if (!managing || !checked.length) return;
    setSaving(true);
    try {
      const response = await fetch(`${apiUrl}/test-suites/${managing.testSuiteId}/cases`, {
        method: "POST",
        headers,
        body: JSON.stringify({ testCaseIds: checked, isRequired: addRequired }),
      });
      if (!response.ok) throw new Error(await response.text() || "เพิ่ม Test Case ไม่สำเร็จ");
      setChecked([]);
      setManaging(null);
      setReload((x) => x + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : "เพิ่ม Test Case ไม่สำเร็จ");
    } finally {
      setSaving(false);
    }
  };
  const removeCase = async (suiteId: string, caseId: string) => {
    const response = await fetch(`${apiUrl}/test-suites/${suiteId}/cases/${caseId}`, {
      method: "DELETE",
      headers,
    });
    if (!response.ok) { setError("นำ Test Case ออกจาก Suite ไม่สำเร็จ"); return; }
    setManaging(null);
    setReload((x) => x + 1);
  };
  const updateCase = async (suite: TestSuiteItem, caseId: string, sortOrder: number, isRequired: boolean) => {
    setSaving(true); setError("");
    const response = await fetch(`${apiUrl}/test-suites/${suite.testSuiteId}/cases/${caseId}`, { method: "PUT", headers, body: JSON.stringify({ sortOrder, isRequired }) });
    if (!response.ok) { setError("ปรับ Test Case ไม่สำเร็จ"); setSaving(false); return; }
    const fresh = await fetch(`${apiUrl}/test-suites/${suite.testSuiteId}`, { headers }).then(r => r.json());
    setManaging(fresh); setItems(current => current.map(x => x.testSuiteId === fresh.testSuiteId ? fresh : x)); setSaving(false);
  };
  const removeSuite = async (suite: TestSuiteItem) => {
    if (
      !window.confirm(
        `ยืนยันลบ ${suite.suiteCode}? ข้อมูลประวัติเดิมจะยังคงอยู่`,
      )
    )
      return;
    const response = await fetch(`${apiUrl}/test-suites/${suite.testSuiteId}`, {
      method: "DELETE",
      headers,
    });
    if (!response.ok) {
      window.alert("ลบ Test Suite ไม่สำเร็จ");
      return;
    }
    setReload((x) => x + 1);
  };
  const fetchFullSuite = async (item: TestSuiteItem): Promise<TestSuiteItem> => {
    try {
      const h = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
      const full = await fetch(`${apiUrl}/test-suites/${item.testSuiteId}`, { headers: h }).then(r => r.ok ? r.json() : null);
      return full ? { ...item, ...full } : { ...item, cases: [] };
    } catch { return { ...item, cases: [] }; }
  };
  const openSuiteDetail = async (item: TestSuiteItem) => { const full = await fetchFullSuite(item); setDetail(full); };
  const openSuiteManaging = async (item: TestSuiteItem) => { const full = await fetchFullSuite(item); setManaging(full); setChecked([]); setCaseSearch(""); setCaseModuleFilter(""); setCasePriorityFilter(""); setCaseTypeFilter(""); setCaseStatusFilter(""); setAddRequired(true); setError(""); };
  const rows = items.filter(
    (x) =>
      (!projectFilter || x.projectId === projectFilter) &&
      (!typeFilter || x.suiteType === typeFilter) &&
      (!riskFilter || x.riskTier === riskFilter) &&
      (activeFilter === "all" || (activeFilter === "active" ? x.isActive : !x.isActive)) &&
      `${x.suiteCode} ${x.suiteName} ${x.suiteType ?? ""}`
        .toLowerCase()
        .includes(search.toLowerCase()),
  );
  const available = testCases.filter(
    (x) =>
      managing &&
      !managing.cases.some((c) => c.testCaseId === x.testCaseId) &&
      x.projectId === managing.projectId,
  ).filter(x => (!caseSearch || `${x.testCaseCode} ${x.title}`.toLowerCase().includes(caseSearch.toLowerCase())) && (!caseModuleFilter || x.moduleId === caseModuleFilter) && (!casePriorityFilter || x.priority === casePriorityFilter) && (!caseTypeFilter || x.testType === caseTypeFilter) && (!caseStatusFilter || x.status === caseStatusFilter));
  return (
    <>
      <article className="card">
        <div className="table-tools suite-toolbar">
          <span>{rows.length} Test Suites</span>
          <div className="suite-filters">
            <select value={projectFilter} onChange={e => setProjectFilter(e.target.value)}><option value="">ทุก Project</option>{projects.map(x => <option key={x.projectId} value={x.projectId}>{x.projectCode} · {x.projectName}</option>)}</select>
            <select value={typeFilter} onChange={e => setTypeFilter(e.target.value)}><option value="">ทุก Type</option>{suiteTypes.map(x => <option key={x.value} value={x.value}>{x.displayName}</option>)}</select>
            <select value={riskFilter} onChange={e => setRiskFilter(e.target.value)}><option value="">ทุก Risk Tier</option>{riskTiers.map(x => <option key={x.value} value={x.value}>{x.displayName}</option>)}</select>
            <select value={activeFilter} onChange={e => setActiveFilter(e.target.value)}><option value="active">ใช้งาน</option><option value="inactive">ปิดใช้งาน</option><option value="all">ทุกสถานะ</option></select>
          </div>
          {canEdit && (
            <div className="suite-create-actions"><button className="btn ai-button" onClick={openSuiteAi}><span aria-hidden="true">✦</span> AI Generate</button><button className="btn primary" onClick={() => openForm()}>+ สร้าง Test Suite</button></div>
          )}
        </div>
        <div className="table-wrap">
          <table className="suite-table">
            <thead>
              <tr>
                <th>Suite Code</th>
                <th>Suite Name</th>
                <th>Type</th>
                <th>Risk Tier</th>
                <th>Case Count</th>
                <th>Test Cycles</th>
                <th>Active</th>
                {canEdit && <th>จัดการ</th>}
              </tr>
            </thead>
            <tbody>
              {rows.map((x) => (
                <tr key={x.testSuiteId}>
                  <td data-label="Suite Code">
                    <button className="link-button" onClick={() => openSuiteDetail(x)}>{x.suiteCode}</button>
                  </td>
                  <td data-label="Suite Name">{x.suiteName}</td>
                  <td data-label="Type">{x.suiteType ?? "-"}</td>
                  <td data-label="Risk Tier">
                    <Badge tone={x.riskTier === "P0" ? "red" : "yellow"}>
                      {x.riskTier ?? "-"}
                    </Badge>
                  </td>
                  <td data-label="Case Count">{(x as any).cases?.length ?? (x as any).caseCount ?? 0}</td>
                  <td data-label="Test Cycles">{x.cycleCount}</td>
                  <td data-label="Status">
                    <Badge tone={x.isActive ? "green" : "red"}>
                      {x.isActive ? "ใช้งาน" : "ปิดใช้งาน"}
                    </Badge>
                  </td>
                  {canEdit && (
                    <td data-label="จัดการ">
                      <div className="row-actions">
                        <button
                          className="table-action"
                          onClick={() => openSuiteDetail(x)}
                        >
                          รายละเอียด
                        </button>
                        <button
                          className="table-action"
                          onClick={() => openForm(x)}
                        >
                          แก้ไข
                        </button>
                        <button
                          className="table-action"
                          onClick={() => openSuiteManaging(x)}
                        >
                          จัด Test Case
                        </button>
                        <button
                          className="table-action danger-action"
                          onClick={() => removeSuite(x)}
                        >
                          ลบ
                        </button>
                      </div>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>
      {suiteAiModal&&<div className="modal" onMouseDown={()=>!suiteAiGenerating&&setSuiteAiModal(false)}><div className="modal-box requirement-ai-modal suite-ai-modal" role="dialog" aria-modal="true" aria-labelledby="suite-ai-title" onMouseDown={event=>event.stopPropagation()} style={{position:"relative"}}>{suiteAiGenerating&&<div className="ai-loading-overlay"><div className="ai-spinner"/>{suiteAiDrafts.length?<p>กำลังบันทึก Test Suite...</p>:<p>AI กำลังวิเคราะห์ Test Suite...</p>}<small>{suiteAiDrafts.length?"กรุณารอสักครู่ อย่าปิดหน้าต่างนี้":"รอสักครู่ ระบบกำลังประมวลผล Requirement และ Test Case"}</small></div>}<div className="modal-head"><div><h2 id="suite-ai-title">AI Generate Test Suite</h2><small>{suiteAiDrafts.length?`พบ ${suiteAiDrafts.length} Suite ที่ AI สร้าง — ตรวจสอบและบันทึก`:"วิเคราะห์ Requirement และ Test Case จาก Module ที่เลือก"}</small></div><button disabled={suiteAiGenerating} aria-label="ปิดหน้าต่าง AI Generate" onClick={()=>setSuiteAiModal(false)}>×</button></div>{suiteAiDrafts.length===0?(<section className="requirement-ai-panel"><div className="requirement-ai-head"><div><span className="ai-spark">AI</span><p><strong>ผู้ช่วยจัดกลุ่ม Test Case</strong><small>AI จะสร้าง Test Suite หลายชุดจาก Module ที่เลือก</small></p></div><span className="ai-review-badge">ตรวจสอบก่อนบันทึก</span></div>{suiteAiError&&<div className="inline-alert error"><span>{suiteAiError}</span></div>}{(!suiteTypes.length||!riskTiers.length)&&<div className="inline-alert error"><span>กรุณาเพิ่ม Test Suite Type และ Risk Tier ในการตั้งค่ากลางก่อนใช้งาน AI</span></div>}<div className="form-grid"><label>Project<select value={suiteAiProjectId} disabled={suiteAiGenerating} onChange={event=>{setSuiteAiProjectId(event.target.value);setSuiteAiModuleId("");setSuiteAiError("")}}><option value="">เลือก Project</option>{projects.map(project=><option key={project.projectId} value={project.projectId}>{project.projectCode} · {project.projectName}</option>)}</select></label><label>Module<select className="testcase-module-filter" value={suiteAiModuleId} disabled={suiteAiGenerating||!suiteAiProjectId} onChange={event=>setSuiteAiModuleId(event.target.value)}><option value="">เลือก Module</option>{renderModuleSelectOptions(suiteAiModules)}</select></label></div><div className="ai-draft-note"><span aria-hidden="true">i</span><p><strong>ใช้ข้อมูลที่มีอยู่ในระบบ</strong><small>ระบบส่งเฉพาะ Requirement และ Test Case ของ Module ที่เลือกให้ AI วิเคราะห์ ผลลัพธ์ยังไม่ถูกบันทึกจนกว่าจะตรวจ Draft และกดบันทึก</small></p></div>{suiteAiModuleId&&<div className="requirement-ai-actions"><small>{testCases.filter(testCase=>testCase.moduleId===suiteAiModuleId&&testCase.status!=="Deprecated").length} Test Cases พร้อมวิเคราะห์</small><div className="row-actions"><button className="btn" disabled={suiteAiGenerating} onClick={()=>setSuiteAiModal(false)}>ยกเลิก</button><button className="btn primary" disabled={suiteAiGenerating||!suiteAiProjectId||!suiteAiModuleId||!suiteTypes.length||!riskTiers.length} onClick={generateSuiteWithAi}>{suiteAiGenerating?"AI กำลังวิเคราะห์...":"✦ สร้าง Test Suite"}</button></div></div>}</section>):(<section className="requirement-ai-panel suite-ai-review"><div className="suite-ai-review-head"><div><h3>Suites ที่ AI สร้าง ({suiteAiDrafts.length})</h3><p>{suiteAiDrafts.reduce((sum,d)=>sum+d.testCases.length,0)} Test Cases ถูกจัดกลุ่มเป็น {suiteAiDrafts.length} Suite</p></div></div>{suiteAiError&&<div className="inline-alert error" style={{marginBottom:8}}><span>{suiteAiError}</span></div>}<div className="suite-ai-draft-list">{suiteAiDrafts.map((draft,index)=>{const isExpanded=suiteAiExpanded===index;return<div key={index} className={`suite-ai-draft-card${isExpanded?" expanded":""}`}><div className="suite-ai-draft-head" onClick={()=>setSuiteAiExpanded(isExpanded?undefined:index)}><div className="suite-ai-draft-title"><b>{draft.suiteName}</b><div className="suite-ai-draft-tags"><Badge tone="blue">{draft.suiteType}</Badge><Badge tone="yellow">{draft.riskTier}</Badge><span className="suite-ai-case-count">{draft.testCases.length} Cases</span></div></div><span className="suite-ai-expand-icon">{isExpanded?"▾":"▸"}</span></div>{isExpanded&&<div className="suite-ai-draft-body"><p className="suite-ai-draft-desc">{draft.description}</p><p className="suite-ai-draft-summary"><strong>สรุป:</strong> {draft.selectionSummary}</p><div className="suite-ai-case-list">{draft.testCases.map((tc,ci)=>{const testCase=testCases.find(x=>x.testCaseId===tc.testCaseId);return<div key={tc.testCaseId}><b>{ci+1}</b><span><strong>{testCase?.testCaseCode??tc.testCaseId}</strong><small>{testCase?.title??"ไม่พบรายละเอียด"}</small><small>{tc.reason}</small></span><Badge tone={tc.isRequired?"blue":"yellow"}>{tc.isRequired?"Required":"Optional"}</Badge></div>})}</div><button className="table-action danger-action" style={{marginTop:8}} onClick={()=>removeSuiteAiDraft(index)}>นำ Suite นี้ออก</button></div>}</div>})}</div><div className="requirement-ai-actions"><small>{suiteAiDrafts.length} Suite พร้อมบันทึก</small><div className="row-actions"><button className="btn" disabled={suiteAiGenerating} onClick={()=>setSuiteAiDrafts([])}>สร้างใหม่</button><button className="btn primary" disabled={suiteAiGenerating||!suiteAiDrafts.length} onClick={saveAllSuiteDrafts}>{suiteAiGenerating?"กำลังบันทึก...":`✦ บันทึกทั้งหมด (${suiteAiDrafts.length} Suite)`}</button></div></div></section>)}</div></div>}
      {form && (
        <div className="modal" onMouseDown={() => setForm(false)}>
          <div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
            <div className="modal-head">
              <h2>{editing ? "แก้ไข" : "สร้าง"} Test Suite</h2>
              <button onClick={() => setForm(false)}>×</button>
            </div>
            <div className="form-grid">
              <label>
                Project
                <select
                  value={projectId}
                  disabled={Boolean(editing)}
                  onChange={(e) => setProjectId(e.target.value)}
                >
                  {projects.map((x) => (
                    <option key={x.projectId} value={x.projectId}>
                      {x.projectName}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Suite Code
                <input
                  disabled
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                />
              </label>
              <label className="full">
                Suite Name
                <input value={name} onChange={(e) => setName(e.target.value)} />
              </label>
              <label>
                Type
                <select value={type} onChange={(e) => setType(e.target.value)}>
                  {masterOptionElements(suiteTypes, type)}
                </select>
              </label>
              <label>
                Risk Tier
                <select value={risk} onChange={(e) => setRisk(e.target.value)}>
                  {masterOptionElements(riskTiers, risk)}
                </select>
              </label>
              <label className="full">
                รายละเอียด
                <textarea
                  rows={3}
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                />
              </label>
              {editing && (
                <label className="full check-line">
                  <input
                    type="checkbox"
                    checked={active}
                    onChange={(e) => setActive(e.target.checked)}
                  />{" "}
                  เปิดใช้งาน
                </label>
              )}
            </div>
            <div className="modal-actions">
              <button className="btn" onClick={() => setForm(false)}>
                ยกเลิก
              </button>
              <button
                className="btn primary"
                disabled={saving || !projectId || !code.trim() || !name.trim()}
                onClick={save}
              >
                {saving ? "กำลังบันทึก..." : "บันทึก"}
              </button>
            </div>
          </div>
        </div>
      )}
      {managing && (
        <div className="modal" onMouseDown={() => setManaging(null)}>
          <div
            className="modal-box suite-editor"
            onMouseDown={(e) => e.stopPropagation()}
          >
            <div className="modal-head">
              <div>
                <h2>จัด Test Case</h2>
                <small>
                  {managing.suiteCode} · {managing.suiteName}
                </small>
              </div>
              <button onClick={() => setManaging(null)}>×</button>
            </div>
            {error && <div className="inline-error">{error}</div>}
            <div className="suite-case-toolbar">
              <input value={caseSearch} onChange={e => setCaseSearch(e.target.value)} placeholder="ค้นหา Test Case..." />
              <select value={caseModuleFilter} onChange={e => setCaseModuleFilter(e.target.value)}><option value="">ทุก Module</option>{renderModuleSelectOptions(modules)}</select>
              <select value={casePriorityFilter} onChange={e => setCasePriorityFilter(e.target.value)}><option value="">ทุก Priority</option>{[...new Set(testCases.map(x => x.priority))].map(x => <option key={x}>{x}</option>)}</select>
              <select value={caseTypeFilter} onChange={e => setCaseTypeFilter(e.target.value)}><option value="">ทุก Type</option>{[...new Set(testCases.map(x => x.testType).filter(Boolean))].map(x => <option key={x} value={x}>{x}</option>)}</select>
              <select value={caseStatusFilter} onChange={e => setCaseStatusFilter(e.target.value)}><option value="">ทุก Status</option>{[...new Set(testCases.map(x => x.status))].map(x => <option key={x}>{x}</option>)}</select>
            </div>
            <div className="suite-columns">
              <section>
                <h3>Test Case ในชุด ({managing.cases.length})</h3>
                {managing.cases.length ? (
                  managing.cases.map((x, index) => (
                    <div className="suite-case" key={x.testCaseId}>
                      <span>
                        <b>{x.testCaseCode}</b>
                        <small>{x.title}</small>
                        <small>ลำดับ {x.sortOrder} · {x.isRequired ? "Required" : "Optional"}</small>
                      </span>
                      <div className="suite-case-actions"><button disabled={saving || index === 0} title="เลื่อนขึ้น" onClick={() => updateCase(managing, x.testCaseId, managing.cases[index - 1]?.sortOrder ?? x.sortOrder, x.isRequired)}>↑</button><button disabled={saving || index === managing.cases.length - 1} title="เลื่อนลง" onClick={() => updateCase(managing, x.testCaseId, managing.cases[index + 1]?.sortOrder ?? x.sortOrder, x.isRequired)}>↓</button><button className="requirement-toggle" disabled={saving} onClick={() => updateCase(managing, x.testCaseId, x.sortOrder, !x.isRequired)}>{x.isRequired ? "Required" : "Optional"}</button></div>
                      <button
                        onClick={() =>
                          removeCase(managing.testSuiteId, x.testCaseId)
                        }
                      >
                        นำออก
                      </button>
                    </div>
                  ))
                ) : (
                  <p className="muted-text">ยังไม่มี Test Case</p>
                )}
              </section>
              <section>
                <div className="suite-available-head"><h3>Test Case ที่เพิ่มได้ ({available.length})</h3><div><button className="table-action" onClick={() => setChecked(available.map(x => x.testCaseId))}>เลือกทั้งหมด</button><button className="table-action" onClick={() => setChecked([])}>ล้าง</button></div></div>
                {available.map((x) => (
                  <label className="suite-case selectable" key={x.testCaseId}>
                    <input
                      type="checkbox"
                      checked={checked.includes(x.testCaseId)}
                      onChange={(e) =>
                        setChecked((c) =>
                          e.target.checked
                            ? [...c, x.testCaseId]
                            : c.filter((id) => id !== x.testCaseId),
                        )
                      }
                    />
                    <span>
                      <b>{x.testCaseCode}</b>
                      <small>{x.title}</small>
                    </span>
                  </label>
                ))}
              </section>
            </div>
            <div className="modal-actions">
              <label className="suite-required-choice"><input type="checkbox" checked={addRequired} onChange={e => setAddRequired(e.target.checked)} /> เพิ่มเป็น Required</label>
              <button className="btn" onClick={() => setManaging(null)}>
                ปิด
              </button>
              <button
                className="btn primary"
                onClick={addCases}
                disabled={saving || !checked.length}
              >
                เพิ่ม {checked.length} รายการ
              </button>
            </div>
          </div>
        </div>
      )}
      {detail && <div className="modal" onMouseDown={() => setDetail(null)}><div className="modal-box suite-detail" onMouseDown={e => e.stopPropagation()}><div className="modal-head"><div><h2>{detail.suiteCode}</h2><small>{projects.find(x => x.projectId === detail.projectId)?.projectName ?? "-"}</small></div><button onClick={() => setDetail(null)}>×</button></div><h3>{detail.suiteName}</h3><p className="muted-text">{detail.description || "ไม่มีรายละเอียด"}</p><div className="detail-grid"><span>Type<b>{detail.suiteType || "-"}</b></span><span>Risk Tier<b>{detail.riskTier || "-"}</b></span><span>Status<b>{detail.isActive ? "ใช้งาน" : "ปิดใช้งาน"}</b></span><span>Test Cycles<b>{detail.cycleCount}</b></span></div><h3>Test Cases ({detail.cases.length})</h3><div className="suite-detail-cases">{detail.cases.map(x => <div key={x.testCaseId}><span><b>{x.sortOrder}. {x.testCaseCode}</b><small>{x.title}</small></span><Badge tone={x.isRequired ? "blue" : "yellow"}>{x.isRequired ? "Required" : "Optional"}</Badge></div>)}</div><div className="modal-actions"><button className="btn primary" onClick={() => setDetail(null)}>ปิด</button></div></div></div>}
    </>
  );
}
type AdminUser = {
  userId: string;
  username: string;
  displayName: string;
  email?: string;
  isActive: boolean;
  lastLoginAt?: string;
  roles: string[];
  assignedProjectIds: string[];
};
type AdminRole = {
  roleId: string;
  roleCode: string;
  roleName: string;
  permissions: string[];
};
type AdminPermission = {
  permissionId: string;
  permissionCode: string;
  moduleArea?: string;
};
const masterSettingGroups = [
  ["ReleaseType", "Release", "Release Type"],
  ["TestCasePriority", "Test Case", "Priority"],
  ["TestCaseType", "Test Case", "Type"],
  ["TestSuiteType", "Test Suite", "Type"],
  ["TestSuiteRiskTier", "Test Suite", "Risk Tier"],
  ["TestCycleType", "Test Cycle", "Cycle Type"],
] as const;
const masterSettingSections = [
  { name: "Release", description: "ค่าที่ใช้ในหน้าจัดการ Release", groups: masterSettingGroups.filter((x) => x[1] === "Release") },
  { name: "Test Case", description: "ค่าที่ใช้ในการออกแบบ Test Case", groups: masterSettingGroups.filter((x) => x[1] === "Test Case") },
  { name: "Test Suite", description: "ค่าที่ใช้จัดกลุ่ม Test Case", groups: masterSettingGroups.filter((x) => x[1] === "Test Suite") },
  { name: "Test Cycle", description: "ค่าที่ใช้ในการวางแผนรอบทดสอบ", groups: masterSettingGroups.filter((x) => x[1] === "Test Cycle") },
];
type EnvironmentSetting = { testEnvironmentId: string; projectId: string; environmentName: string; baseUrl?: string; isActive: boolean };
type AiConfiguration = { provider: "OpenAI" | "Google" | "Anthropic" | "OpenRouter" | "Local"; model: string; baseUrl?: string; isEnabled: boolean; hasApiKey: boolean; apiKeyHint?: string; updatedAt?: string };
const aiProviderModels: Record<AiConfiguration["provider"], string[]> = { OpenAI: ["gpt-5-mini", "gpt-5.4"], Google: ["gemini-3.5-flash", "gemini-3.1-pro"], Anthropic: ["claude-sonnet-5", "claude-haiku-4-5-20251001"], OpenRouter: ["openai/gpt-4o", "anthropic/claude-sonnet-4", "google/gemini-2.5-flash", "meta-llama/llama-4-maverick", "nvidia/nemotron-3.5-lightning:free"], Local: ["qwen3", "llama3.3", "mistral-small"] };
type AiModelOption = { id: string; displayName: string };
function MasterSettingsPage() {
  const [items, setItems] = useState<MasterOption[]>([]), [environments, setEnvironments] = useState<EnvironmentSetting[]>([]), [projects, setProjects] = useState<ProjectItem[]>([]), [reload, setReload] = useState(0);
  const [category, setCategory] = useState("ReleaseType"), [formCategory, setFormCategory] = useState<string | null>(null), [value, setValue] = useState(""), [displayName, setDisplayName] = useState(""), [sortOrder, setSortOrder] = useState(10), [editing, setEditing] = useState<MasterOption | null>(null);
  const [environment, setEnvironment] = useState<EnvironmentSetting | null>(null), [environmentFormOpen, setEnvironmentFormOpen] = useState(false), [environmentProjectId, setEnvironmentProjectId] = useState(""), [environmentName, setEnvironmentName] = useState(""), [baseUrl, setBaseUrl] = useState("");
  const [aiConfiguration, setAiConfiguration] = useState<AiConfiguration>({ provider: "OpenAI", model: "gpt-5-mini", isEnabled: true, hasApiKey: false }), [aiApiKey, setAiApiKey] = useState(""), [savingAi, setSavingAi] = useState(false);
  const [aiModels, setAiModels] = useState<AiModelOption[]>([]), [loadingAiModels, setLoadingAiModels] = useState(false), [aiModelsError, setAiModelsError] = useState("");
  const [expandedSections, setExpandedSections] = useState<Set<string>>(() => new Set());
  const headers = useMemo(() => ({ "Content-Type": "application/json", Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }), []);
  useEffect(() => {
    const load = async () => {
      const read = async (url: string) => { const response = await fetch(url, { headers }); if (!response.ok) throw new Error(`โหลดข้อมูลไม่สำเร็จ (${response.status})`); return response.json(); };
      try { const [masterData, environmentData, projectData, aiData] = await Promise.all([read(`${apiUrl}/master-settings?includeInactive=true`), read(`${apiUrl}/master-settings/environments`), read(`${apiUrl}/projects`), read(`${apiUrl}/master-settings/ai`)]); setItems(masterData); setEnvironments(environmentData); setProjects(projectData); setAiConfiguration(aiData); setEnvironmentProjectId((x) => x || projectData[0]?.projectId || ""); }
      catch (error) { window.alert(error instanceof Error ? `${error.message} กรุณาตรวจสอบว่า API ใช้งานเวอร์ชันล่าสุด` : "โหลดการตั้งค่ากลางไม่สำเร็จ"); }
    };
    load();
  }, [reload, headers]);
  const resetOption = () => { setEditing(null); setFormCategory(null); setValue(""); setDisplayName(""); setSortOrder(10); };
  const saveOption = async () => {
    const response = await fetch(`${apiUrl}/master-settings${editing ? `/${editing.masterOptionId}` : ""}`, { method: editing ? "PUT" : "POST", headers, body: JSON.stringify({ category: editing?.category ?? category, value, displayName, sortOrder, isActive: editing?.isActive ?? true }) });
    if (!response.ok) { const p = await response.json(); window.alert(p.detail ?? "บันทึกข้อมูลไม่สำเร็จ"); return; } resetOption(); setReload((x) => x + 1);
  };
  const toggleOption = async (item: MasterOption) => { await fetch(`${apiUrl}/master-settings/${item.masterOptionId}`, { method: "PUT", headers, body: JSON.stringify({ ...item, isActive: !item.isActive }) }); setReload((x) => x + 1); };
  const deleteOption = async (item: MasterOption) => { if (!window.confirm(`ยืนยันลบ ${item.displayName}?`)) return; const response = await fetch(`${apiUrl}/master-settings/${item.masterOptionId}`, { method: "DELETE", headers }); if (!response.ok) { const p = await response.json(); window.alert(p.detail ?? "ลบข้อมูลไม่สำเร็จ"); return; } if (editing?.masterOptionId === item.masterOptionId) resetOption(); setReload((x) => x + 1); };
  const editEnvironment = (item?: EnvironmentSetting) => { setEnvironment(item ?? null); setEnvironmentFormOpen(true); setEnvironmentProjectId(item?.projectId ?? projects[0]?.projectId ?? ""); setEnvironmentName(item?.environmentName ?? ""); setBaseUrl(item?.baseUrl ?? ""); };
  const resetEnvironment = () => { setEnvironment(null); setEnvironmentFormOpen(false); setEnvironmentName(""); setBaseUrl(""); };
  const saveEnvironment = async () => { const response = await fetch(`${apiUrl}/master-settings/environments${environment ? `/${environment.testEnvironmentId}` : ""}`, { method: environment ? "PUT" : "POST", headers, body: JSON.stringify({ projectId: environmentProjectId, environmentName, baseUrl: baseUrl || null, isActive: environment?.isActive ?? true }) }); if (!response.ok) { const p = await response.json(); window.alert(p.detail ?? "บันทึก Environment ไม่สำเร็จ"); return; } resetEnvironment(); setReload((x) => x + 1); };
  const toggleEnvironment = async (item: EnvironmentSetting) => { await fetch(`${apiUrl}/master-settings/environments/${item.testEnvironmentId}`, { method: "PUT", headers, body: JSON.stringify({ ...item, isActive: !item.isActive }) }); setReload((x) => x + 1); };
  const deleteEnvironment = async (item: EnvironmentSetting) => { if (!window.confirm(`ยืนยันลบ Environment ${item.environmentName}?`)) return; const response = await fetch(`${apiUrl}/master-settings/environments/${item.testEnvironmentId}`, { method: "DELETE", headers }); if (!response.ok) { const p = await response.json(); window.alert(p.detail ?? "ลบ Environment ไม่สำเร็จ"); return; } if (environment?.testEnvironmentId === item.testEnvironmentId) resetEnvironment(); setReload((x) => x + 1); };
  const openOptionForm = (targetCategory: string, item?: MasterOption) => { setCategory(targetCategory); setFormCategory(targetCategory); setEditing(item ?? null); setValue(item?.value ?? ""); setDisplayName(item?.displayName ?? ""); setSortOrder(item?.sortOrder ?? 10); };
  const saveAiConfiguration = async () => { setSavingAi(true); try { const response = await fetch(`${apiUrl}/master-settings/ai`, { method: "PUT", headers, body: JSON.stringify({ provider: aiConfiguration.provider, model: aiConfiguration.model, baseUrl: aiConfiguration.baseUrl || null, apiKey: aiApiKey || null, isEnabled: aiConfiguration.isEnabled, clearApiKey: false }) }); if (!response.ok) { const problem = await response.json(); throw new Error(problem.detail ?? "บันทึกการตั้งค่า AI ไม่สำเร็จ"); } setAiConfiguration(await response.json()); setAiApiKey(""); window.alert("บันทึกการตั้งค่า AI เรียบร้อยแล้ว"); } catch (error) { window.alert(error instanceof Error ? error.message : "บันทึกการตั้งค่า AI ไม่สำเร็จ"); } finally { setSavingAi(false); } };
  const loadAiModels = async () => { setLoadingAiModels(true); setAiModelsError(""); try { const response = await fetch(`${apiUrl}/master-settings/ai/models`, { method: "POST", headers, body: JSON.stringify({ provider: aiConfiguration.provider, baseUrl: aiConfiguration.baseUrl || null, apiKey: aiApiKey || null }) }); if (!response.ok) { const problem = await response.json(); throw new Error(problem.detail ?? "โหลดรายการ Model ไม่สำเร็จ"); } const models = await response.json() as AiModelOption[]; setAiModels(models); if (!models.length) setAiModelsError("Provider ไม่ส่งรายการ Model กลับมา"); } catch (error) { setAiModelsError(error instanceof Error ? error.message : "โหลดรายการ Model ไม่สำเร็จ"); } finally { setLoadingAiModels(false); } };
  const toggleSection = (name: string) => setExpandedSections((current) => { const next = new Set(current); if (next.has(name)) next.delete(name); else next.add(name); return next; });
  const optionForm = (targetCategory: string) => formCategory === targetCategory && <div className="master-inline-editor"><label>รหัสค่า<input autoFocus value={value} onChange={(e) => setValue(e.target.value)} placeholder="เช่น Major" /></label><label>ชื่อที่แสดง<input value={displayName} onChange={(e) => setDisplayName(e.target.value)} /></label><label className="master-order-field">ลำดับ<input type="number" value={sortOrder} onChange={(e) => setSortOrder(Number(e.target.value))} /></label><div className="master-setting-actions"><button className="btn" onClick={resetOption}>ยกเลิก</button><button className="btn primary" disabled={!value.trim() || !displayName.trim()} onClick={saveOption}>{editing ? "บันทึกการแก้ไข" : "เพิ่มข้อมูล"}</button></div></div>;
  return <div className="master-settings-page">
    <section className="card master-setting-card master-ai-configuration">
      <div className="master-section-head"><div><span className="master-section-icon">AI</span><div><h3>AI Configuration</h3><p>ค่ากลางสำหรับ AI Generate ของ Requirement, Test Case และ Test Suite</p></div></div><Badge tone={aiConfiguration.isEnabled && aiConfiguration.hasApiKey ? "green" : "yellow"}>{aiConfiguration.isEnabled && aiConfiguration.hasApiKey ? "พร้อมใช้งาน" : "ยังไม่พร้อมใช้งาน"}</Badge></div>
      <div className="master-ai-body">
        <div className="master-ai-note"><b>การจัดเก็บที่ปลอดภัย</b><span>API key ถูกเข้ารหัสและเก็บเฉพาะฝั่ง Server เมื่อเปลี่ยน Provider ต้องกรอกคีย์ใหม่ ส่วน AI Local สามารถเว้นคีย์ได้</span></div>
        <div className="master-ai-form">
          <label>Provider<select value={aiConfiguration.provider} onChange={(e) => { const provider = e.target.value as AiConfiguration["provider"]; setAiApiKey(""); setAiModels([]); setAiModelsError(""); setAiConfiguration((current) => ({ ...current, provider, model: aiProviderModels[provider][0], baseUrl: provider === "Local" ? "http://localhost:11434/v1" : provider === "OpenRouter" ? "https://openrouter.ai/api/v1" : undefined, hasApiKey: false, apiKeyHint: undefined })); }}><option value="OpenAI">OpenAI</option><option value="Google">Google Gemini</option><option value="Anthropic">Anthropic Claude</option><option value="OpenRouter">OpenRouter</option><option value="Local">AI Local</option></select></label>
          <label>Model<span className="master-model-label"><small>{aiModels.length ? `${aiModels.length} Models` : "เลือกหรือพิมพ์ Model ID"}</small><button type="button" onClick={loadAiModels} disabled={loadingAiModels}>{loadingAiModels ? "กำลังโหลด..." : "โหลดทั้งหมด"}</button></span><input list="ai-model-options" value={aiConfiguration.model} onChange={(e) => setAiConfiguration((current) => ({ ...current, model: e.target.value }))} placeholder="ระบุ Model ID" /><datalist id="ai-model-options">{(aiModels.length ? aiModels : aiProviderModels[aiConfiguration.provider].map((id) => ({ id, displayName: "" }))).map((model) => <option key={model.id} value={model.id}>{model.displayName}</option>)}</datalist>{aiModelsError && <small className="master-model-error">{aiModelsError}</small>}</label>
          {(aiConfiguration.provider === "Local" || aiConfiguration.provider === "OpenRouter") && <label>Base URL<input value={aiConfiguration.baseUrl ?? ""} onChange={(e) => setAiConfiguration((current) => ({ ...current, baseUrl: e.target.value }))} placeholder={aiConfiguration.provider === "Local" ? "http://localhost:11434/v1" : "https://openrouter.ai/api/v1"} /></label>}
          <label>API key {aiConfiguration.provider === "Local" && <small>(ไม่บังคับ)</small>}<input type="password" autoComplete="new-password" value={aiApiKey} onChange={(e) => setAiApiKey(e.target.value)} placeholder={aiConfiguration.hasApiKey ? `ตั้งค่าแล้ว ${aiConfiguration.apiKeyHint ?? ""} — เว้นว่างเพื่อใช้ค่าเดิม` : aiConfiguration.provider === "Local" ? "เว้นว่างได้ หาก Server ไม่ใช้คีย์" : `กรอก API key สำหรับ ${aiConfiguration.provider}`} /></label>
          <label className="master-ai-toggle"><input type="checkbox" checked={aiConfiguration.isEnabled} onChange={(e) => setAiConfiguration((current) => ({ ...current, isEnabled: e.target.checked }))} /><span>เปิดใช้งาน AI ร่วมกันทุกระบบ</span></label>
          <button className="btn primary" disabled={savingAi || !aiConfiguration.model.trim() || ((aiConfiguration.provider === "Local" || aiConfiguration.provider === "OpenRouter") && !aiConfiguration.baseUrl?.trim()) || (aiConfiguration.provider !== "Local" && !aiConfiguration.hasApiKey && !aiApiKey.trim())} onClick={saveAiConfiguration}>{savingAi ? "กำลังบันทึก..." : "บันทึกการตั้งค่า"}</button>
        </div>
      </div>
    </section>
    {masterSettingSections.map((section) => <section className={`card master-setting-card master-section-${section.name.toLowerCase().replace(" ", "-")} ${expandedSections.has(section.name) ? "is-expanded" : ""}`} key={section.name}>
      <button type="button" className="master-section-head master-section-toggle" aria-expanded={expandedSections.has(section.name)} onClick={() => toggleSection(section.name)}><span className="master-section-summary"><span className="master-section-icon">{section.name === "Release" ? "R" : section.name === "Test Case" ? "TC" : section.name === "Test Suite" ? "TS" : "CY"}</span><span><strong>{section.name}</strong><small>{section.description}</small></span></span><span className="master-section-meta"><span className="count-pill">{items.filter((x) => section.groups.some((g) => g[0] === x.category) && x.isActive).length + (section.name === "Test Cycle" ? environments.filter((x) => x.isActive).length : 0)} Active</span><span className="master-chevron" aria-hidden="true">⌄</span></span></button>
      {expandedSections.has(section.name) && <><div className="master-section-groups">{section.groups.map((group) => <div className="master-subgroup" key={group[0]}><div className="master-subgroup-head"><h4>{group[2]}</h4><div><span>{items.filter((x) => x.category === group[0] && x.isActive).length}</span><button className="master-add-button" onClick={() => openOptionForm(group[0])}>+ เพิ่ม</button></div></div>{optionForm(group[0])}<div className="master-setting-list">{items.filter((x) => x.category === group[0]).map((item) => <div key={item.masterOptionId} className={!item.isActive ? "inactive" : ""}><span><b>{item.displayName}</b><small>{item.value} · ลำดับ {item.sortOrder}</small></span><button className="table-action" onClick={() => openOptionForm(group[0], item)}>แก้ไข</button><button className="table-action danger-action" onClick={() => deleteOption(item)}>ลบ</button><button className="table-action" onClick={() => toggleOption(item)}>{item.isActive ? "ปิดใช้" : "เปิดใช้"}</button></div>)}</div></div>)}</div>
      {section.name === "Test Cycle" && <div className="master-subgroup master-environment-group"><div className="master-subgroup-head"><h4>Environment</h4><div><span>{environments.filter((x) => x.isActive).length}</span><button className="master-add-button" onClick={() => editEnvironment()}>+ เพิ่ม</button></div></div>{environmentFormOpen && <div className="master-setting-form environment-form"><label>Project<select disabled={!!environment} value={environmentProjectId} onChange={(e) => setEnvironmentProjectId(e.target.value)}>{projects.map((x) => <option key={x.projectId} value={x.projectId}>{x.projectName}</option>)}</select></label><label>Environment<input autoFocus value={environmentName} onChange={(e) => setEnvironmentName(e.target.value)} /></label><label>Base URL<input value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} /></label><div className="master-setting-actions"><button className="btn" onClick={resetEnvironment}>ยกเลิก</button><button className="btn primary" disabled={!environmentProjectId || !environmentName.trim()} onClick={saveEnvironment}>{environment ? "บันทึกการแก้ไข" : "เพิ่มข้อมูล"}</button></div></div>}<div className="master-setting-list">{environments.map((item) => <div key={item.testEnvironmentId} className={!item.isActive ? "inactive" : ""}><span><b>{item.environmentName}</b><small>{projects.find((x) => x.projectId === item.projectId)?.projectName ?? "-"} · {item.baseUrl || "ไม่ระบุ URL"}</small></span><button className="table-action" onClick={() => editEnvironment(item)}>แก้ไข</button><button className="table-action danger-action" onClick={() => deleteEnvironment(item)}>ลบ</button><button className="table-action" onClick={() => toggleEnvironment(item)}>{item.isActive ? "ปิดใช้" : "เปิดใช้"}</button></div>)}</div></div>}</>}
    </section>)}
  </div>;
}

type SystemMonitorData = {
  checkedAt: string;
  machineName: string;
  environment: string;
  api: { status: string; processId: number; uptime: string; memoryBytes: number; processorCount: number };
  database: { status: string; responseMilliseconds: number; error?: string };
  services: { key: string; displayName: string; description?: string; status: string; isRunning: boolean; error?: string }[];
};
function SystemMonitorPage() {
  const [data, setData] = useState<SystemMonitorData | null>(null),
    [error, setError] = useState(""),
    [loading, setLoading] = useState(true),
    [busy, setBusy] = useState("");
  const headers = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
  const load = useCallback(async (silent = false) => {
    if (!silent) setLoading(true);
    setError("");
    try {
      const response = await fetch(`${apiUrl}/system-monitor`, { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` } });
      if (!response.ok) throw new Error(response.status === 403 ? "หน้านี้สำหรับ System Admin เท่านั้น" : "โหลดสถานะระบบไม่สำเร็จ");
      setData(await response.json());
    } catch (e) { setError(e instanceof Error ? e.message : "โหลดสถานะระบบไม่สำเร็จ"); }
    finally { if (!silent) setLoading(false); }
  }, []);
  useEffect(() => { load(); const timer = window.setInterval(() => load(true), 15000); return () => window.clearInterval(timer); }, [load]);
  const control = async (service: SystemMonitorData["services"][number], action: "start" | "restart") => {
    const verb = action === "restart" ? "Restart" : "Start";
    if (!window.confirm(`ยืนยัน ${verb} ${service.displayName}?\nการเชื่อมต่ออาจหยุดชั่วคราว`)) return;
    setBusy(service.key);
    try {
      const response = await fetch(`${apiUrl}/system-monitor/services/${encodeURIComponent(service.key)}/${action}`, { method: "POST", headers });
      if (!response.ok) { const problem = await response.json(); throw new Error(problem.detail ?? `${verb} Service ไม่สำเร็จ`); }
      await load(true);
    } catch (e) { window.alert(e instanceof Error ? e.message : `${verb} Service ไม่สำเร็จ`); }
    finally { setBusy(""); }
  };
  if (loading) return <article className="card empty"><p>กำลังตรวจสอบสถานะระบบ...</p></article>;
  if (error || !data) return <article className="card empty"><div className="login-error">{error}</div><button className="btn" onClick={() => load()}>ลองใหม่</button></article>;
  const statusTone = (status: string) => status === "Online" || status === "Running" ? "green" : status === "Starting" || status === "Stopping" ? "yellow" : "red";
  return <div className="system-monitor-page">
    <div className="monitor-summary">
      <article className="card monitor-card"><div className="monitor-card-head"><span className="monitor-icon">API</span><Badge tone={statusTone(data.api.status)}>{data.api.status}</Badge></div><h3>QA Management API</h3><p>Process #{data.api.processId} · Uptime {data.api.uptime}</p><div className="monitor-metrics"><span><b>{(data.api.memoryBytes / 1048576).toFixed(1)} MB</b><small>Memory</small></span><span><b>{data.api.processorCount}</b><small>CPU Cores</small></span></div></article>
      <article className="card monitor-card"><div className="monitor-card-head"><span className="monitor-icon">DB</span><Badge tone={statusTone(data.database.status)}>{data.database.status}</Badge></div><h3>QA Database</h3><p>{data.database.error || "เชื่อมต่อฐานข้อมูลสำเร็จ"}</p><div className="monitor-metrics"><span><b>{data.database.responseMilliseconds.toFixed(0)} ms</b><small>Response</small></span><span><b>{data.machineName}</b><small>Machine</small></span></div></article>
    </div>
    <article className="card monitor-services"><div className="monitor-section-head"><div><h3>Managed Services</h3><p>แสดงเฉพาะ Service ที่อนุญาตไว้ใน Server configuration</p></div><button className="btn" onClick={() => load()} disabled={loading || !!busy}>↻ Refresh</button></div>
      <div className="monitor-service-list">{data.services.length === 0 ? <div className="empty"><p>ยังไม่มี Service ในรายการที่อนุญาต</p></div> : data.services.map((service) => <div className="monitor-service" key={service.key}><span className={`service-light ${service.isRunning ? "online" : "offline"}`} /><div><b>{service.displayName}</b><small>{service.description || service.key}</small>{service.error && <em>{service.error}</em>}</div><Badge tone={statusTone(service.status)}>{service.status}</Badge><div className="row-actions"><button className="btn" disabled={!!busy || service.isRunning} onClick={() => control(service, "start")}>{busy === service.key ? "กำลังทำงาน..." : "Start"}</button><button className="btn primary" disabled={!!busy || !service.isRunning} onClick={() => control(service, "restart")}>{busy === service.key ? "กำลังทำงาน..." : "Restart"}</button></div></div>)}</div>
    </article>
    <footer className="monitor-footer">ตรวจล่าสุด {new Date(data.checkedAt).toLocaleString("th-TH")} · {data.environment}</footer>
  </div>;
}

function AdministrationPage({ refresh, allProjects }: { refresh: number; allProjects: ProjectItem[] }) {
  const [users, setUsers] = useState<AdminUser[]>([]),
    [roles, setRoles] = useState<AdminRole[]>([]),
    [permissions, setPermissions] = useState<AdminPermission[]>([]),
    [roleId, setRoleId] = useState(""),
    [selected, setSelected] = useState<string[]>([]),
    [saving, setSaving] = useState(false),
    [version, setVersion] = useState(0),
    [userSearch, setUserSearch] = useState("");
  const [editing, setEditing] = useState<AdminUser | null>(null),
    [displayName, setDisplayName] = useState(""),
    [email, setEmail] = useState(""),
    [active, setActive] = useState(true),
    [userRoleIds, setUserRoleIds] = useState<string[]>([]),
    [userProjectIds, setUserProjectIds] = useState<string[]>([]),
    [passwordUser, setPasswordUser] = useState<AdminUser | null>(null),
    [newPassword, setNewPassword] = useState("");
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
  useEffect(() => {
    const requestHeaders = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    Promise.all([
      fetch(`${apiUrl}/admin/users`, { headers: requestHeaders }).then((r) =>
        r.json(),
      ),
      fetch(`${apiUrl}/admin/roles`, { headers: requestHeaders }).then((r) =>
        r.json(),
      ),
      fetch(`${apiUrl}/admin/permissions`, { headers: requestHeaders }).then(
        (r) => r.json(),
      ),
    ]).then(([u, r, p]) => {
      setUsers(Array.isArray(u) ? u : u?.items?.rows ?? []);
      setRoles(r);
      setPermissions(p);
      if (r.length) {
        setRoleId(r[0].roleId);
        setSelected(
          p
            .filter((x: AdminPermission) =>
              r[0].permissions.includes(x.permissionCode),
            )
            .map((x: AdminPermission) => x.permissionId),
        );
      }
    });
  }, [refresh, version]);
  const filteredUsers = users.filter(
    (u) =>
      u.displayName.toLowerCase().includes(userSearch.toLowerCase()) ||
      u.username.toLowerCase().includes(userSearch.toLowerCase()) ||
      (u.email ?? "").toLowerCase().includes(userSearch.toLowerCase()),
  );
  const activeCount = users.filter((u) => u.isActive).length;
  const changeRole = (id: string) => {
    setRoleId(id);
    const role = roles.find((x) => x.roleId === id);
    setSelected(
      permissions
        .filter((x) => role?.permissions.includes(x.permissionCode))
        .map((x) => x.permissionId),
    );
  };
  const togglePermission = (id: string, checked: boolean) =>
    setSelected((current) =>
      checked ? [...current, id] : current.filter((value) => value !== id),
    );
  const savePermissions = async () => {
    setSaving(true);
    try {
      const response = await fetch(
        `${apiUrl}/admin/roles/${roleId}/permissions`,
        {
          method: "POST",
          headers,
          body: JSON.stringify({ permissionIds: selected }),
        },
      );
      if (!response.ok) throw new Error();
      window.alert("บันทึกสิทธิ์เรียบร้อยแล้ว");
    } catch {
      window.alert("ไม่สามารถบันทึกสิทธิ์ได้ กรุณาลองใหม่");
    } finally {
      setSaving(false);
    }
  };
  const openEdit = (user: AdminUser) => {
    setEditing(user);
    setDisplayName(user.displayName);
    setEmail(user.email ?? "");
    setActive(user.isActive);
    setUserRoleIds(
      roles
        .filter((role) => user.roles.includes(role.roleCode))
        .map((role) => role.roleId),
    );
    setUserProjectIds(user.assignedProjectIds ?? []);
  };
  const saveUser = async () => {
    if (!editing) return;
    setSaving(true);
    try {
      const update = await fetch(`${apiUrl}/admin/users/${editing.userId}`, {
        method: "PUT",
        headers,
        body: JSON.stringify({
          displayName,
          email: email || null,
          isActive: active,
        }),
      });
      if (!update.ok) throw new Error();
      const assign = await fetch(
        `${apiUrl}/admin/users/${editing.userId}/roles`,
        {
          method: "POST",
          headers,
          body: JSON.stringify({ roleIds: userRoleIds }),
        },
      );
      if (!assign.ok) throw new Error();
      const assignProjects = await fetch(
        `${apiUrl}/admin/users/${editing.userId}/projects`,
        {
          method: "POST",
          headers,
          body: JSON.stringify({ projectIds: userProjectIds }),
        },
      );
      if (!assignProjects.ok) throw new Error();
      setEditing(null);
      setVersion((x) => x + 1);
    } catch {
      window.alert("ไม่สามารถบันทึกข้อมูลผู้ใช้ได้");
    } finally {
      setSaving(false);
    }
  };
  const toggleActive = async (user: AdminUser) => {
    setSaving(true);
    try {
      const response = await fetch(`${apiUrl}/admin/users/${user.userId}`, {
        method: "PUT",
        headers,
        body: JSON.stringify({
          displayName: user.displayName,
          email: user.email ?? null,
          isActive: !user.isActive,
        }),
      });
      if (!response.ok) throw new Error();
      setVersion((x) => x + 1);
    } catch {
      window.alert("ไม่สามารถเปลี่ยนสถานะผู้ใช้ได้");
    } finally {
      setSaving(false);
    }
  };
  const resetPassword = async () => {
    if (!passwordUser || newPassword.length < 8) return;
    setSaving(true);
    try {
      const response = await fetch(
        `${apiUrl}/admin/users/${passwordUser.userId}/reset-password`,
        { method: "POST", headers, body: JSON.stringify({ newPassword }) },
      );
      if (!response.ok) throw new Error();
      setPasswordUser(null);
      setNewPassword("");
      window.alert("รีเซ็ตรหัสผ่านเรียบร้อยแล้ว");
    } catch {
      window.alert("ไม่สามารถรีเซ็ตรหัสผ่านได้");
    } finally {
      setSaving(false);
    }
  };
  const grouped = permissions.reduce<Record<string, AdminPermission[]>>(
    (result, item) => {
      const key = item.moduleArea || "OTHER";
      (result[key] ??= []).push(item);
      return result;
    },
    {},
  );
  return (
    <div className="admin-page">
      <header className="admin-page-header">
        <div>
          <h2>จัดการผู้ใช้และสิทธิ์</h2>
          <p>เพิ่ม แก้ไข และกำหนดบทบาท สิทธิ์ และ Project ให้ผู้ใช้ในระบบ</p>
        </div>
      </header>

      <div className="admin-stats-row">
        <div className="admin-stat-card">
          <span className="admin-stat-icon blue">&#x1F465;</span>
          <div>
            <b>{users.length}</b>
            <small>ผู้ใช้ทั้งหมด</small>
          </div>
        </div>
        <div className="admin-stat-card">
          <span className="admin-stat-icon green">&#x2705;</span>
          <div>
            <b>{activeCount}</b>
            <small>ใช้งานอยู่</small>
          </div>
        </div>
        <div className="admin-stat-card">
          <span className="admin-stat-icon orange">&#x1F6E1;</span>
          <div>
            <b>{users.length - activeCount}</b>
            <small>ปิดใช้งาน</small>
          </div>
        </div>
        <div className="admin-stat-card">
          <span className="admin-stat-icon purple">&#x1F3F7;</span>
          <div>
            <b>{roles.length}</b>
            <small>บทบาท</small>
          </div>
        </div>
      </div>

      <article className="card admin-users-card">
        <div className="card-title">
          <div>
            <h3>รายชื่อผู้ใช้งาน</h3>
            <p>เลือกผู้ใช้เพื่อแก้ไขข้อมูล บทบาท และ Project ที่เข้าถึงได้</p>
          </div>
          <div className="admin-users-toolbar">
            <div className="admin-search">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="11" cy="11" r="8"/><path d="M21 21l-4.35-4.35"/></svg>
              <input
                placeholder="ค้นหาผู้ใช้..."
                value={userSearch}
                onChange={(e) => setUserSearch(e.target.value)}
              />
            </div>
          </div>
        </div>

        <div className="admin-users-table-wrap">
          <table className="admin-users-table">
            <thead>
              <tr>
                <th>ผู้ใช้งาน</th>
                <th>บทบาท</th>
                <th>Project</th>
                <th>สถานะ</th>
                <th>เข้าสู่ระบบล่าสุด</th>
                <th className="th-action">จัดการ</th>
              </tr>
            </thead>
            <tbody>
              {filteredUsers.map((x) => (
                <tr key={x.userId}>
                  <td data-label="ผู้ใช้งาน" className="td-user">
                    <div className="user-cell">
                      <span className={`user-avatar ${x.isActive ? "" : "inactive"}`}>
                        {x.displayName.slice(0, 1).toUpperCase()}
                      </span>
                      <div className="user-info">
                        <b>{x.displayName}</b>
                        <small>{x.username}{x.email ? ` · ${x.email}` : ""}</small>
                      </div>
                    </div>
                  </td>
                  <td data-label="บทบาท">
                    <div className="role-tags">
                      {x.roles.length
                        ? x.roles.map((role) => <span key={role}>{role}</span>)
                        : <span className="tag-empty">-</span>}
                    </div>
                  </td>
                  <td data-label="Project">
                    <div className="role-tags project-tags">
                      {x.assignedProjectIds?.length
                        ? x.assignedProjectIds.map((pid) => {
                            const proj = allProjects.find((p) => p.projectId === pid);
                            return <span key={pid} className="project-tag">{proj?.projectCode ?? pid.slice(0, 8)}</span>;
                          })
                        : <span className="tag-empty">-</span>}
                    </div>
                  </td>
                  <td data-label="สถานะ">
                    <Badge tone={x.isActive ? "green" : "red"}>
                      {x.isActive ? "ใช้งาน" : "ปิดใช้งาน"}
                    </Badge>
                  </td>
                  <td data-label="เข้าสู่ระบบล่าสุด" className="td-meta">
                    {x.lastLoginAt
                      ? new Date(x.lastLoginAt).toLocaleString("th-TH")
                      : <span className="tag-empty">-</span>}
                  </td>
                  <td data-label="จัดการ" className="td-actions">
                    <button className="table-action" onClick={() => openEdit(x)}>
                      แก้ไข
                    </button>
                    <button
                      className={`table-action ${x.isActive ? "table-action-warn" : "table-action-green"}`}
                      onClick={() => toggleActive(x)}
                      disabled={saving}
                    >
                      {x.isActive ? "ปิด" : "เปิด"}
                    </button>
                    <button
                      className="table-action table-action-key"
                      onClick={() => { setPasswordUser(x); setNewPassword(""); }}
                    >
                      รหัสผ่าน
                    </button>
                  </td>
                </tr>
              ))}
              {!filteredUsers.length && (
                <tr>
                  <td colSpan={6} className="empty-cell">
                    <div className="empty-state">
                      <span>&#x1F464;</span>
                      <b>ไม่พบผู้ใช้</b>
                      <small>{userSearch ? "ลองค้นหาด้วยคำอื่น" : "ยังไม่มีผู้ใช้ในระบบ"}</small>
                    </div>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </article>

      {editing && (
        <div className="modal" onMouseDown={() => setEditing(null)}>
          <div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
            <div className="modal-head">
              <h2>แก้ไขผู้ใช้ — {editing.username}</h2>
              <button onClick={() => setEditing(null)}>&times;</button>
            </div>
            <div className="form-grid form-grid-2col">
              <label>
                ชื่อที่แสดง <span className="required">*</span>
                <input
                  value={displayName}
                  onChange={(e) => setDisplayName(e.target.value)}
                />
              </label>
              <label>
                อีเมล
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                />
              </label>
            </div>
            <div className="modal-section">
              <h3 className="modal-section-title">บทบาทของผู้ใช้</h3>
              <div className="role-checks">
                {roles.map((role) => (
                  <label
                    key={role.roleId}
                    className={userRoleIds.includes(role.roleId) ? "selected" : ""}
                  >
                    <input
                      type="checkbox"
                      checked={userRoleIds.includes(role.roleId)}
                      onChange={(e) =>
                        setUserRoleIds((current) =>
                          e.target.checked
                            ? [...current, role.roleId]
                            : current.filter((id) => id !== role.roleId),
                        )
                      }
                    />
                    <span>
                      <b>{role.roleName}</b>
                      <small>{role.roleCode}</small>
                    </span>
                  </label>
                ))}
              </div>
            </div>
            <div className="modal-section">
              <h3 className="modal-section-title">Project ที่เข้าถึงได้</h3>
              <p className="fieldset-hint">ไม่เลือก Project ใด = ไม่เห็น Project ในระบบ</p>
              <div className="role-checks">
                {allProjects.map((project) => (
                  <label
                    key={project.projectId}
                    className={userProjectIds.includes(project.projectId) ? "selected" : ""}
                  >
                    <input
                      type="checkbox"
                      checked={userProjectIds.includes(project.projectId)}
                      onChange={(e) =>
                        setUserProjectIds((current) =>
                          e.target.checked
                            ? [...current, project.projectId]
                            : current.filter((id) => id !== project.projectId),
                        )
                      }
                    />
                    <span>
                      <b>{project.projectName}</b>
                      <small>{project.projectCode}</small>
                    </span>
                  </label>
                ))}
                {!allProjects.length && <p className="muted-text">ไม่มี Project ในระบบ</p>}
              </div>
            </div>
            <label className="active-switch">
              <input
                type="checkbox"
                checked={active}
                onChange={(e) => setActive(e.target.checked)}
              />
              อนุญาตให้เข้าสู่ระบบ
            </label>
            <div className="modal-actions">
              <button className="btn" onClick={() => setEditing(null)}>ยกเลิก</button>
              <button
                className="btn primary"
                onClick={saveUser}
                disabled={saving || !displayName.trim()}
              >
                {saving ? "กำลังบันทึก..." : "บันทึกข้อมูลผู้ใช้"}
              </button>
            </div>
          </div>
        </div>
      )}

      {passwordUser && (
        <div className="modal" onMouseDown={() => setPasswordUser(null)}>
          <div className="modal-box" style={{ maxWidth: 480 }} onMouseDown={(e) => e.stopPropagation()}>
            <div className="modal-head">
              <h2>รีเซ็ตรหัสผ่าน — {passwordUser.username}</h2>
              <button onClick={() => setPasswordUser(null)}>&times;</button>
            </div>
            <div className="password-hint-box">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/></svg>
              <span>รหัสผ่านต้องมีอย่างน้อย 8 ตัวอักษร</span>
            </div>
            <label>
              รหัสผ่านใหม่
              <input
                type="password"
                autoFocus
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                placeholder="รหัสผ่านใหม่"
              />
            </label>
            <div className="modal-actions">
              <button className="btn" onClick={() => setPasswordUser(null)}>ยกเลิก</button>
              <button
                className="btn primary"
                onClick={resetPassword}
                disabled={saving || newPassword.length < 8}
              >
                {saving ? "กำลังบันทึก..." : "ยืนยันรีเซ็ตรหัสผ่าน"}
              </button>
            </div>
          </div>
        </div>
      )}

      <article className="card permission-card">
        <div className="card-title">
          <div>
            <h3>สิทธิ์ตามบทบาท</h3>
            <p>กำหนดเมนูและการดำเนินการที่แต่ละบทบาทเข้าถึงได้</p>
          </div>
          <span className="count-pill blue-pill">
            {selected.length}/{permissions.length}
          </span>
        </div>
        <label className="role-selector">
          <span>บทบาท</span>
          <select value={roleId} onChange={(e) => changeRole(e.target.value)}>
            {roles.map((x) => (
              <option value={x.roleId} key={x.roleId}>
                {x.roleName} ({x.roleCode})
              </option>
            ))}
          </select>
        </label>
        <div className="permission-toolbar">
          <span>สิทธิ์ที่อนุญาต</span>
          <div>
            <button
              type="button"
              onClick={() => setSelected(permissions.map((x) => x.permissionId))}
            >
              เลือกทั้งหมด
            </button>
            <button type="button" onClick={() => setSelected([])}>
              ล้างทั้งหมด
            </button>
          </div>
        </div>
        <div className="permission-groups">
          {Object.entries(grouped).map(([area, items]) => (
            <section key={area}>
              <h4>{area.replaceAll("_", " ")}</h4>
              <div className="permission-grid">
                {items.map((x) => (
                  <label
                    className={selected.includes(x.permissionId) ? "selected" : ""}
                    key={x.permissionId}
                  >
                    <input
                      type="checkbox"
                      checked={selected.includes(x.permissionId)}
                      onChange={(e) => togglePermission(x.permissionId, e.target.checked)}
                    />
                    <span>
                      <b>{x.permissionCode.split(".").at(-1)}</b>
                      <small>{x.permissionCode}</small>
                    </span>
                  </label>
                ))}
              </div>
            </section>
          ))}
        </div>
        <div className="permission-actions">
          <small>เลือกแล้ว {selected.length} สิทธิ์</small>
          <button className="btn primary" onClick={savePermissions} disabled={!roleId || saving}>
            {saving ? "กำลังบันทึก..." : "บันทึกการเปลี่ยนแปลง"}
          </button>
        </div>
      </article>
    </div>
  );
}

function Login({ onLogin }: { onLogin: (user: SessionUser) => void }) {
  const [username, setUsername] = useState(""),
    [password, setPassword] = useState(""),
    [error, setError] = useState(""),
    [loading, setLoading] = useState(false);
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError("");
    setLoading(true);
    try {
      const response = await fetch(`${apiUrl}/auth/login`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ username, password }),
      });
      if (!response.ok) throw new Error("ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง");
      const data = await response.json();
      localStorage.setItem("qa.accessToken", data.accessToken);
      localStorage.setItem("qa.user", JSON.stringify(data.user));
      onLogin(data.user);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "ไม่สามารถเชื่อมต่อระบบได้");
    } finally {
      setLoading(false);
    }
  };
  return (
    <div className="login-page">
      <div className="login-visual">
        <div>
          <div className="login-logo"><span className="login-logo-text">QA</span></div>
          <h1>ProMaxx2 QA Hub</h1>
          <p>
            บริหาร Requirement, Test Execution, Defect และ Release Readiness
            ในที่เดียว
          </p>
        </div>
        <small>Quality Assurance Management System</small>
      </div>
      <form className="login-card" onSubmit={submit}>
        <div className="mobile-brand">
          <div className="login-logo"><span className="login-logo-text">QA</span></div>
          <b>ProMaxx2 QA Hub</b>
        </div>
        <span className="eyebrow">WELCOME BACK</span>
        <h2>เข้าสู่ระบบ</h2>
        <p>กรอกบัญชีผู้ใช้งานเพื่อเข้าสู่ QA Workspace</p>
        {error && <div className="login-error">{error}</div>}
        <label>
          ชื่อผู้ใช้
          <input
            autoFocus
            autoComplete="username"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            required
            placeholder="Username"
          />
        </label>
        <label>
          รหัสผ่าน
          <input
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            placeholder="Password"
          />
        </label>
        <label className="remember">
          <input type="checkbox" /> จดจำการเข้าสู่ระบบ
        </label>
        <button className="btn primary login-button" disabled={loading}>
          {loading ? "กำลังเข้าสู่ระบบ..." : "เข้าสู่ระบบ"}
        </button>
        <small>
          หากไม่สามารถเข้าสู่ระบบได้ กรุณาติดต่อ System Administrator
        </small>
      </form>
    </div>
  );
}

function App() {
  const shareParams = new URLSearchParams(window.location.search);
  const shareCode = shareParams.get("s") ?? "";
  const shareToken = shareParams.get("dashboardShare") ?? "";
  const [page, setPage] = useState<Page>(restoredActivePage),
    [menu, setMenu] = useState(false),
    [search, setSearch] = useState(""),
    [modal, setModal] = useState(false);
  const [user, setUser] = useState<SessionUser | null>(() => {
    try {
      const value = localStorage.getItem("qa.user");
      return value ? JSON.parse(value) : null;
    } catch {
      return null;
    }
  });
  // on mount, verify token not expired — if expired, clear and redirect to login
  useEffect(() => {
    try {
      if (isTokenExpiredLocal()) {
        localStorage.removeItem("qa.accessToken");
        localStorage.removeItem("qa.user");
        setUser(null);
        if (window.location.pathname !== "/") window.location.href = "/";
      }
    } catch {}
  }, []);
  useEffect(() => {
    if (shareCode || shareToken) return;
    localStorage.setItem("qa.activePage", page);
    const expectedHash = `#/${page}`;
    if (window.location.hash !== expectedHash) window.history.replaceState(null, "", expectedHash);
  }, [page, shareCode, shareToken]);
  useEffect(() => {
    const restoreFromHistory = () => {
      const hashPage = window.location.hash.match(/^#\/([^/?#]+)/)?.[1];
      if (hashPage && pageIds.has(hashPage as Page)) setPage(hashPage as Page);
    };
    window.addEventListener("hashchange", restoreFromHistory);
    return () => window.removeEventListener("hashchange", restoreFromHistory);
  }, []);
  const [contextProjects, setContextProjects] = useState<ProjectItem[]>([]),
    [contextReleases, setContextReleases] = useState<ReleaseItem[]>([]),
    [contextBuilds, setContextBuilds] = useState<BuildItem[]>([]),
    [contextProjectId, setContextProjectId] = useState(
      () => localStorage.getItem("qa.context.project") ?? "",
    ),
    [contextReleaseId, setContextReleaseId] = useState(
      () => localStorage.getItem("qa.context.release") ?? "",
    ),
    [contextBuildId, setContextBuildId] = useState(
      () => localStorage.getItem("qa.context.build") ?? "",
    ),
    [blockerCount, setBlockerCount] = useState(0);
  const [code, setCode] = useState(""),
    [name, setName] = useState(""),
    [details, setDetails] = useState(""),
    [createProjectId, setCreateProjectId] = useState(""),
    [createModuleId, setCreateModuleId] = useState(""),
    [createReleaseId, setCreateReleaseId] = useState(""),
    [createModules, setCreateModules] = useState<ModuleItem[]>([]),
    [createReleases, setCreateReleases] = useState<ReleaseItem[]>([]),
    [createRequirementPriority, setCreateRequirementPriority] = useState("P2"),
    [createRequirementRisk, setCreateRequirementRisk] = useState("Medium"),
    [createRequirementSource, setCreateRequirementSource] = useState(""),
    [createRequirementCriteria, setCreateRequirementCriteria] = useState(""),
    [createRequirementOwnerId, setCreateRequirementOwnerId] = useState(""),
    [createRequirementInScope, setCreateRequirementInScope] = useState(true),
    [createRequirementUsers, setCreateRequirementUsers] = useState<AdminUser[]>([]),
    [requirementAiPrompt,setRequirementAiPrompt]=useState(""),
    [requirementAiGenerating,setRequirementAiGenerating]=useState(false),
    [requirementAiError,setRequirementAiError]=useState(""),
    [requirementAiModal,setRequirementAiModal]=useState(false),
    [requirementAiFiles,setRequirementAiFiles]=useState<File[]>([]),
    [refresh, setRefresh] = useState(0),
    [saving, setSaving] = useState(false);
  const generateRequirementWithAi=async()=>{
    if(!requirementAiPrompt.trim())return;
    setRequirementAiGenerating(true);setRequirementAiError("");
    try{
      const project=contextProjects.find(x=>x.projectId===createProjectId);
      const module=createModules.find(x=>x.moduleId===createModuleId);
      const release=createReleases.find(x=>x.releaseId===createReleaseId);
      const body=new FormData();body.append("prompt",requirementAiPrompt);body.append("projectName",project?.projectName??"");body.append("moduleName",module?.moduleName??"");body.append("releaseName",release?`${release.releaseCode} Version ${release.version}`:"");requirementAiFiles.forEach(file=>body.append("files",file));
      const response=await fetch(`${apiUrl}/requirements/generate-ai`,{method:"POST",headers:{Authorization:`Bearer ${localStorage.getItem("qa.accessToken")}`},body});
      if(!response.ok){const problem=await response.json().catch(()=>null);throw new Error(problem?.detail??"AI Generate Requirement ไม่สำเร็จ");}
      const draft=await response.json();setName(draft.title);setDetails(draft.description);setCreateRequirementCriteria(draft.acceptanceCriteria);setCreateRequirementPriority(draft.priority);setCreateRequirementRisk(draft.riskLevel);setCreateRequirementSource(draft.source);setRequirementAiModal(false);setModal(true);
    }catch(e){setRequirementAiError(e instanceof Error?e.message:"AI Generate Requirement ไม่สำเร็จ");}
    finally{setRequirementAiGenerating(false);}
  };
  useEffect(() => {
    if (!user) return;
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetch(`${apiUrl}/projects`, { headers: h })
      .then((response) => (response.ok ? response.json() : []))
      .then((data: ProjectItem[]) => {
        const active = data.filter((x) => x.isActive);
        setContextProjects(active);
        setContextProjectId((current) =>
          active.some((x) => x.projectId === current)
            ? current
            : (active[0]?.projectId ?? ""),
        );
      });
  }, [user, page, refresh]);
  useEffect(() => {
    if (!contextProjectId) {
      setContextReleases([]);
      setContextReleaseId("");
      return;
    }
    localStorage.setItem("qa.context.project", contextProjectId);
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetch(`${apiUrl}/projects/${contextProjectId}/releases`, { headers: h })
      .then((response) => (response.ok ? response.json() : []))
      .then((data: ReleaseItem[]) => {
        const active = data.filter((x) => x.status !== "Cancelled");
        setContextReleases(active);
        setContextReleaseId((current) =>
          active.some((x) => x.releaseId === current)
            ? current
            : (active[0]?.releaseId ?? ""),
        );
      });
  }, [contextProjectId, page, refresh]);
  useEffect(() => {
    if (!contextReleaseId) {
      setContextBuilds([]);
      setContextBuildId("");
      return;
    }
    localStorage.setItem("qa.context.release", contextReleaseId);
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetch(`${apiUrl}/releases/${contextReleaseId}/builds`, { headers: h })
      .then((response) => (response.ok ? response.json() : []))
      .then((data: BuildItem[]) => {
        const active = data.filter((x) => x.isActive);
        setContextBuilds(active);
        setContextBuildId((current) =>
          active.some((x) => x.buildId === current)
            ? current
            : (active[0]?.buildId ?? ""),
        );
      });
  }, [contextReleaseId, page, refresh]);
  useEffect(() => {
    if (!contextBuildId) {
      setBlockerCount(0);
      return;
    }
    setBlockerCount(0);
    localStorage.setItem("qa.context.build", contextBuildId);
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetch(`${apiUrl}/builds/${contextBuildId}/blocked-count`, { headers: h })
      .then((response) => (response.ok ? response.json() : { count: 0 }))
      .then((data: { count: number }) => setBlockerCount(data.count));
  }, [contextBuildId, page, refresh]);
  useEffect(() => {
    if (!modal || page !== "requirements") return;
    const targetProjectId = createProjectId || contextProjectId || contextProjects[0]?.projectId || "";
    if (!targetProjectId) return;
    if (targetProjectId !== createProjectId) {
      setCreateProjectId(targetProjectId);
      return;
    }
    const h = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
    Promise.all([
      fetch(`${apiUrl}/projects/${targetProjectId}/modules`, { headers: h }).then((r) => r.ok ? r.json() : []),
      fetch(`${apiUrl}/projects/${targetProjectId}/releases`, { headers: h }).then((r) => r.ok ? r.json() : []),
      fetch(`${apiUrl}/admin/users`, { headers: h }).then(async (r) => { if (!r.ok) return []; const d = await r.json(); return Array.isArray(d) ? d : d?.items?.rows ?? []; }),
    ]).then(([moduleData, releaseData, userData]: [ModuleItem[], ReleaseItem[], unknown[]]) => {
      const activeModules = (moduleData as ModuleItem[]).filter((x) => x.isActive);
      const activeReleases = (releaseData as ReleaseItem[]).filter((x) => x.status !== "Cancelled");
      setCreateModules(activeModules);
      setCreateReleases(activeReleases);
      setCreateRequirementUsers((userData as any[]).filter((x) => x.isActive));
      setCreateModuleId((current) => activeModules.some((x) => x.moduleId === current) ? current : (activeModules[0]?.moduleId ?? ""));
      setCreateReleaseId((current) => activeReleases.some((x) => x.releaseId === current) ? current : (contextReleaseId && activeReleases.some((x) => x.releaseId === contextReleaseId) ? contextReleaseId : (activeReleases[0]?.releaseId ?? "")));
    });
  }, [modal, page, createProjectId, contextProjectId, contextReleaseId, contextProjects]);
  useEffect(() => {
    if (!requirementAiModal) return;
    const targetProjectId = createProjectId || contextProjectId || contextProjects[0]?.projectId || "";
    if (!targetProjectId) return;
    if (targetProjectId !== createProjectId) { setCreateProjectId(targetProjectId); return; }
    const h = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
    Promise.all([
      fetch(`${apiUrl}/projects/${targetProjectId}/modules`, { headers: h }).then((r) => r.ok ? r.json() : []),
      fetch(`${apiUrl}/projects/${targetProjectId}/releases`, { headers: h }).then((r) => r.ok ? r.json() : []),
    ]).then(([moduleData, releaseData]: [ModuleItem[], ReleaseItem[]]) => {
      const activeModules = moduleData.filter((x) => x.isActive);
      const activeReleases = releaseData.filter((x) => x.status !== "Cancelled");
      setCreateModules(activeModules);
      setCreateReleases(activeReleases);
      setCreateModuleId((current) => activeModules.some((x) => x.moduleId === current) ? current : (activeModules[0]?.moduleId ?? ""));
      setCreateReleaseId((current) => activeReleases.some((x) => x.releaseId === current) ? current : (activeReleases[0]?.releaseId ?? ""));
    });
  }, [requirementAiModal, createProjectId, contextProjectId, contextProjects]);
  const description = useMemo(
    () =>
      page === "dashboard"
        ? "สถานะคุณภาพและความพร้อม Release แบบรวมศูนย์"
        : page === "settings"
          ? "จัดการค่ากลางและบริการ AI ที่ทุกระบบใช้งานร่วมกัน"
        : `จัดการข้อมูล ${pageNames[page]} ของ Release ปัจจุบัน`,
    [page],
  );
  const go = (id: Page) => {
    setPage(id);
    setMenu(false);
  };
  const openRegressionCycle=(target:"test-cycles"|"execution",cycleId:string)=>{localStorage.setItem("qa.targetCycleId",cycleId);go(target)};
  const logout = () => {
    localStorage.removeItem("qa.accessToken");
    localStorage.removeItem("qa.user");
    setUser(null);
  };
  const shareDashboard = async () => {
    try {
      const response = await fetch(`${apiUrl}/dashboard/share`, { method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }, body: JSON.stringify({ projectId: contextProjectId || null, releaseId: contextReleaseId || null, buildId: contextBuildId || null, validHours: 24 * 30 }) });
      if (!response.ok) throw new Error("ไม่สามารถสร้างลิงก์แชร์ได้");
      const result: { code: string; expiresAt: string } = await response.json();
      const url = `${window.location.origin}${window.location.pathname}?s=${encodeURIComponent(result.code)}`;
      const copied = await copyText(url);
      if (copied) window.alert(`คัดลอกลิงก์ Dashboard แบบอ่านอย่างเดียวแล้ว\nลิงก์หมดอายุ ${new Date(result.expiresAt).toLocaleString("th-TH")}`);
      else window.prompt("เบราว์เซอร์ไม่อนุญาตให้คัดลอกอัตโนมัติ กรุณาคัดลอกลิงก์นี้", url);
    } catch (e) { window.alert(e instanceof Error ? e.message : "ไม่สามารถสร้างลิงก์แชร์ได้"); }
  };
  const save = async () => {
    if (
      !["projects", "releases", "requirements", "test-cases", "users"].includes(
        page,
      )
    ) {
      setModal(false);
      return;
    }
    setSaving(true);
    try {
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
      let url = `${apiUrl}/projects`,
        body: object = {
          projectCode: "",
          projectName: name,
          description: details || null,
          ownerUserId: null,
        };
      if (page === "users") {
        url = `${apiUrl}/admin/users`;
        body = {
          username: code,
          displayName: name,
          email: null,
          password: details,
          roleIds: null,
        };
      } else if (page !== "projects") {
        const projects: ProjectItem[] = await fetch(`${apiUrl}/projects`, {
          headers,
        }).then((r) => r.json());
        if (!projects.length) throw new Error("กรุณาสร้าง Project ก่อน");
        const targetProject = page === "requirements"
          ? projects.find((x) => x.projectId === createProjectId)
          : projects[0];
        if (!targetProject) throw new Error("กรุณาเลือก Project");
        if (page === "releases") {
          url = `${apiUrl}/projects/${targetProject.projectId}/releases`;
          body = {
            releaseCode: "",
            version: name,
            releaseType: "Major",
            plannedReleaseDate: null,
            scope: details || null,
            releaseOwnerUserId: user?.userId ?? null,
          };
        } else {
          const modules = await fetch(
            `${apiUrl}/projects/${targetProject.projectId}/modules`,
            { headers },
          ).then((r) => r.json());
          const activeModules = (modules as ModuleItem[]).filter((x) => x.isActive);
          if (!activeModules.length) throw new Error("Project ที่เลือกยังไม่มี Module ที่ Active");
          if (page === "requirements") {
            const selectedModule = activeModules.find((x) => x.moduleId === createModuleId);
            if (!selectedModule) throw new Error("กรุณาเลือก Module");
            url = `${apiUrl}/requirements`;
            body = {
              projectId: targetProject.projectId,
              releaseId: createReleaseId || null,
              moduleId: selectedModule.moduleId,
              requirementCode: "",
              title: name,
              description: details || null,
              acceptanceCriteria: createRequirementCriteria || null,
              priority: createRequirementPriority,
              riskLevel: createRequirementRisk || null,
              source: createRequirementSource || null,
              ownerUserId: createRequirementOwnerId || null,
              isInScope: createRequirementInScope,
            };
          } else {
            url = `${apiUrl}/test-cases`;
            body = {
              projectId: targetProject.projectId,
              moduleId: activeModules[0].moduleId,
              testCaseCode: "",
              title: name,
              objective: details || null,
              preconditions: null,
              priority: "P1",
              testType: "Functional",
              automationCandidate: false,
              ownerUserId: user?.userId ?? null,
              steps: [
                {
                  stepNo: 1,
                  action: "ดำเนินการตามกรณีทดสอบ",
                  testData: null,
                  expectedResult: "ผลลัพธ์ถูกต้องตามข้อกำหนด",
                },
              ],
            };
          }
        }
      }
      const response = await fetch(url, {
        method: "POST",
        headers,
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        const problem = await response.json();
        throw new Error(problem.detail ?? "บันทึกข้อมูลไม่สำเร็จ");
      }
      setModal(false);
      setCode("");
      setName("");
      setDetails("");
      if (page === "requirements") {
        setCreateRequirementPriority("P2");
        setCreateRequirementRisk("Medium");
        setCreateRequirementSource("");
        setCreateRequirementCriteria("");
        setCreateRequirementOwnerId("");
        setCreateRequirementInScope(true);
      }
      setRefresh((x) => x + 1);
    } catch (e) {
      window.alert(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ");
    } finally {
      setSaving(false);
    }
  };
  if (shareCode || shareToken) return <div className="shared-dashboard"><header><div className="logo">QA</div><div><b>ProMaxx2 QA Hub</b><small>Executive Read-only Report</small></div><Badge tone="blue">READ ONLY</Badge></header><main><Dashboard shareCode={shareCode} shareToken={shareToken} /></main><footer>ข้อมูลสำหรับการบริหารจัดการ • ไม่สามารถแก้ไขข้อมูลจากหน้านี้</footer></div>;
  if (!user) return <Login onLogin={(u) => { localStorage.removeItem("qa.activePage"); setPage("dashboard"); setUser(u); }} />;
  const can = (permission: string) =>
    user.roles.includes("SYS_ADMIN") || user.permissions.includes(permission);
  const canCreate =
    editPermission[page] !== undefined && can(editPermission[page]!);
  return (
    <div className="app">
      <aside className={menu ? "sidebar open" : "sidebar"}>
        <div className="brand">
          <div className="logo">QA</div>
          <div>
            <b>ProMaxx2 QA Hub</b>
            <small>Quality Assurance Management</small>
          </div>
        </div>
        {nav.map((g) => {
          const items = g.items.filter((i) => can(viewPermission[i.id]));
          return items.length ? (
            <div className="nav-group" key={g.label}>
              <p>{g.label}</p>
              {items.map((i) => (
                <button
                  key={i.id}
                  className={page === i.id ? "active" : ""}
                  onClick={() => go(i.id)}
                >
                  <i>{i.icon}</i>
                  {i.label}
                </button>
              ))}
            </div>
          ) : null;
        })}
      </aside>
      <main>
        <header className="topbar qa-topbar">
          <button className="menu-btn topbar-menu" aria-label={menu?"ปิดเมนู":"เปิดเมนู"} onClick={() => setMenu((v) => !v)}>
            <span aria-hidden="true">☰</span>
          </button>
          {!["projects","users","settings","system-monitor"].includes(page) && <div className="context">
            <label className="context-field"><span>Project</span><select
              value={contextProjectId}
              onChange={(e) => setContextProjectId(e.target.value)}
              aria-label="Project ปัจจุบัน"
            >
              {!contextProjects.length && (
                <option value="">ไม่มี Project</option>
              )}
              {contextProjects.map((x) => (
                <option key={x.projectId} value={x.projectId}>
                  {x.projectName}
                </option>
              ))}
            </select></label>
            <label className="context-field"><span>Release</span><select
              value={contextReleaseId}
              onChange={(e) => setContextReleaseId(e.target.value)}
              aria-label="Release ปัจจุบัน"
              disabled={!contextReleases.length}
            >
              {!contextReleases.length && (
                <option value="">ไม่มี Release</option>
              )}
              {contextReleases.map((x) => (
                <option key={x.releaseId} value={x.releaseId}>
                  Release {x.releaseCode}
                </option>
              ))}
            </select></label>
            <label className="context-field"><span>Build</span><select
              value={contextBuildId}
              onChange={(e) => setContextBuildId(e.target.value)}
              aria-label="Build ปัจจุบัน"
              disabled={!contextBuilds.length}
            >
              {!contextBuilds.length && <option value="">ไม่มี Build</option>}
              {contextBuilds.map((x) => (
                <option key={x.buildId} value={x.buildId}>
                  Build {x.buildNumber}
                  {x.isReleaseCandidate ? " RC" : ""}
                </option>
              ))}
            </select></label>
          </div>}
          <div className="profile">
            <div className={`topbar-health ${blockerCount?"has-blockers":""}`}><span aria-hidden="true"></span><b>{blockerCount}</b><small>Blockers</small></div>
            <div className="avatar">
              {user.displayName.slice(0, 2).toUpperCase()}
            </div>
            <div className="profile-copy">
              <b>{user.displayName}</b>
              <small>{user.roles.includes("SYS_ADMIN")?"System Administrator":"QA Workspace"}</small>
            </div>
            <button className="logout" aria-label="ออกจากระบบ" title="ออกจากระบบ" onClick={logout}><span aria-hidden="true">↪</span><span>ออกจากระบบ</span></button>
          </div>
        </header>
        <div className="content">
          <div className="page-head">
            <div>
              <h1>{pageNames[page]}</h1>
              <p>{description}</p>
            </div>
            <div className="actions">
              <label className="search">
                ⌕
                <input
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="ค้นหา..."
                />
              </label>
              {can("REPORT.EXPORT") && page !== "test-cycles" && <button className="btn">Export</button>}
              {page === "dashboard" && <button className="btn share-btn" onClick={shareDashboard}>↗ แชร์ Dashboard</button>}
              {page === "requirements"&&can("REQUIREMENT.EDIT")&&<button className="btn ai-button" onClick={()=>{setCreateProjectId(contextProjectId);setCreateModuleId("");setCreateReleaseId(contextReleaseId);setRequirementAiPrompt("");setRequirementAiFiles([]);setRequirementAiError("");setRequirementAiModal(true)}}><span aria-hidden="true">✦</span> AI Generate</button>}
              {canCreate && (
                <button className="btn primary" onClick={() => {
                  if (page === "requirements") {
                    setCreateProjectId(contextProjectId);
                    setCreateModuleId("");
                    setCreateReleaseId(contextReleaseId);
                  }
                  setModal(true);
                }}>
                  + สร้างรายการ
                </button>
              )}
            </div>
          </div>
          {page === "dashboard" ? (
            <Dashboard projectId={contextProjectId} releaseId={contextReleaseId} buildId={contextBuildId} projectName={contextProjects.find(x => x.projectId === contextProjectId)?.projectName} />
          ) : page === "projects" ? (
            <ProjectsPage search={search} refresh={refresh} />
          ) : page === "releases" ? (
            <ReleasesPage search={search} refresh={refresh} contextProjectId={contextProjectId} />
          ) : page === "requirements" ? (
            <RequirementsPage search={search} refresh={refresh} canEdit={can("REQUIREMENT.EDIT")} contextProjectId={contextProjectId} />
          ) : page === "test-cases" ? (
            <TestCasesPage search={search} canEdit={can("TESTCASE.EDIT")} contextProjectId={contextProjectId} />
          ) : page === "rtm" ? (
            <RtmPage refresh={refresh} projectId={contextProjectId} releaseId={contextReleaseId} search={search} canEdit={can("TESTCASE.EDIT")} />
          ) : page === "regression" ? (
            <RegressionPage projectId={contextProjectId} releaseId={contextReleaseId} buildId={contextBuildId} search={search} canEdit={can("REGRESSION.MANAGE")} onOpenCycle={openRegressionCycle} />
          ) : page === "users" ? (
            <AdministrationPage refresh={refresh} allProjects={contextProjects} />
          ) : page === "settings" ? (
            <MasterSettingsPage />
          ) : page === "system-monitor" ? (
            <SystemMonitorPage />
          ) : page === "defects" ? (
            <DefectsPage projectId={contextProjectId} releaseId={contextReleaseId} buildId={contextBuildId} search={search} canEdit={can("DEFECT.EDIT")} />
          ) : (
            <DataPage page={page} search={search} projectId={contextProjectId} releaseId={contextReleaseId} buildId={contextBuildId} canAssignExecution={can("EXECUTION.ASSIGN")} canExport={can("REPORT.EXPORT")} />
          )}
        </div>
      </main>
      {requirementAiModal&&<div className="modal" role="dialog" aria-modal="true" aria-labelledby="requirement-ai-title" onMouseDown={()=>!requirementAiGenerating&&setRequirementAiModal(false)}>
        <div className="modal-box requirement-ai-modal" onMouseDown={e=>e.stopPropagation()} style={{position:"relative"}}>{requirementAiGenerating&&<div className="ai-loading-overlay"><div className="ai-spinner"/><p>AI กำลังวิเคราะห์ Requirement...</p><small>รอสักครู่ ระบบกำลังประมวลผลข้อมูลและไฟล์แนบ</small></div>}
          <div className="modal-head"><div><h2 id="requirement-ai-title">AI Generate Requirement</h2><small>สร้าง Draft จากคำอธิบายและไฟล์อ้างอิง</small></div><button aria-label="ปิดหน้าต่าง AI Generate" disabled={requirementAiGenerating} onClick={()=>setRequirementAiModal(false)}>×</button></div>
          <div className="requirement-ai-panel">
            <div className="requirement-ai-head"><div><span className="ai-spark" aria-hidden="true">AI</span><div><h3>ข้อมูลอ้างอิง</h3><p>เลือกบริบทให้ AI สร้าง Requirement ได้ตรงกับระบบ</p></div></div><span className="ai-review-badge">ต้องตรวจสอบก่อนใช้</span></div>
            <div className="form-grid">
              <label>Project<select value={createProjectId} onChange={e=>{setCreateProjectId(e.target.value);setCreateModuleId("");setCreateReleaseId("")}}>{contextProjects.map(x=><option key={x.projectId} value={x.projectId}>{x.projectName}</option>)}</select></label>
              <label>Module<select value={createModuleId} onChange={e=>setCreateModuleId(e.target.value)}><option value="">เลือก Module</option>{renderModuleSelectOptions(createModules.filter(x=>x.isActive))}</select></label>
              <label className="full">Release<select value={createReleaseId} onChange={e=>setCreateReleaseId(e.target.value)}><option value="">ไม่ระบุ Release</option>{createReleases.sort((a,b)=>a.releaseCode.localeCompare(b.releaseCode)).map(x=><option key={x.releaseId} value={x.releaseId}>{x.releaseCode} · Version {x.version}</option>)}</select></label>
              <label className="full">อธิบายความต้องการ<textarea rows={5} value={requirementAiPrompt} onChange={e=>setRequirementAiPrompt(e.target.value)} placeholder="เช่น ผู้ใช้ต้องเห็นยอดขายวันนี้ จำนวนบิล และสินค้าใกล้หมดบน Dashboard หลัง Login" maxLength={4000} autoFocus/><small>{requirementAiPrompt.length.toLocaleString()} / 4,000 ตัวอักษร</small></label>
              <div className="ai-attachments full">
                <div><b>ไฟล์สำหรับให้ AI วิเคราะห์เพิ่มเติม</b><small>PDF, Word, Excel, CSV, TXT, Markdown หรือรูปภาพ · สูงสุด 5 ไฟล์ รวม 20 MB</small></div>
                <label className="ai-file-picker">＋ เลือกไฟล์<input type="file" multiple accept=".pdf,.txt,.md,.csv,.docx,.xlsx,.png,.jpg,.jpeg,.webp" disabled={requirementAiGenerating||requirementAiFiles.length>=5} onChange={e=>{const selected=Array.from(e.target.files??[]);const next=[...requirementAiFiles,...selected].slice(0,5);if(next.reduce((sum,file)=>sum+file.size,0)>20_000_000)setRequirementAiError("ขนาดไฟล์รวมต้องไม่เกิน 20 MB");else{setRequirementAiFiles(next);setRequirementAiError("")}e.target.value=""}}/></label>
                {requirementAiFiles.length>0&&<div className="ai-file-list">{requirementAiFiles.map((file,index)=><div key={`${file.name}-${file.lastModified}`}><span aria-hidden="true">▤</span><p><b>{file.name}</b><small>{(file.size/1024/1024).toFixed(2)} MB</small></p><button type="button" aria-label={`ลบไฟล์ ${file.name}`} disabled={requirementAiGenerating} onClick={()=>setRequirementAiFiles(files=>files.filter((_,i)=>i!==index))}>×</button></div>)}</div>}
              </div>
            </div>
            {requirementAiError&&<div className="login-error" role="alert">{requirementAiError}</div>}
            <div className="ai-draft-note"><span aria-hidden="true">i</span><p><b>AI จะไม่บันทึกข้อมูลหรือไฟล์แนบอัตโนมัติ</b><small>ไฟล์ใช้วิเคราะห์ในคำขอนี้เท่านั้น จากนั้นระบบจะเปิดฟอร์มพร้อม Draft ให้ตรวจสอบ</small></p></div>
          </div>
          <div className="modal-actions"><button className="btn" disabled={requirementAiGenerating} onClick={()=>setRequirementAiModal(false)}>ยกเลิก</button><button className="btn ai-button" disabled={requirementAiGenerating||!requirementAiPrompt.trim()||!createProjectId||!createModuleId} onClick={generateRequirementWithAi}>{requirementAiGenerating?"กำลังวิเคราะห์...":"✦ สร้าง Draft ด้วย AI"}</button></div>
        </div>
      </div>}
      {modal && (
        <div className="modal" onMouseDown={() => setModal(false)}>
          <div className={`modal-box ${page === "requirements" ? "requirement-editor" : ""}`} onMouseDown={(e) => e.stopPropagation()}>
            <div className="modal-head">
              <h2>สร้าง {pageNames[page]}</h2>
              <button onClick={() => setModal(false)}>×</button>
            </div>
            <div className="form-grid">
              {page === "requirements" && (
                <>
                  <label>
                    Project
                    <select value={createProjectId} onChange={(e) => { setCreateProjectId(e.target.value); setCreateModuleId(""); setCreateReleaseId(""); }}>
                      {contextProjects.map((x) => <option key={x.projectId} value={x.projectId}>{x.projectName}</option>)}
                    </select>
                  </label>
                  <label>
                    Module
                    <select value={createModuleId} onChange={(e) => setCreateModuleId(e.target.value)}>
                      <option value="">เลือก Module</option>
                      {renderModuleSelectOptions(createModules.filter((x) => x.isActive))}
                    </select>
                  </label>
                  <label className="full">
                    Release
                    <select value={createReleaseId} onChange={(e) => setCreateReleaseId(e.target.value)}>
                      <option value="">ไม่ระบุ Release</option>
                      {createReleases.map((x) => <option key={x.releaseId} value={x.releaseId}>{x.releaseCode} · Version {x.version}</option>)}
                    </select>
                  </label>
                </>
              )}
              <label>
                {page === "users" ? "Username" : "รหัส"}
                <input
                  disabled={page === "requirements"}
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  placeholder={
                    page === "users"
                      ? "Username"
                      : "ระบบสร้างรหัสอัตโนมัติเมื่อบันทึก"
                  }
                  required
                />
              </label>
              <label>
                {page === "users" ? "ชื่อที่แสดง" : "ชื่อรายการ"}
                <input
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="ระบุชื่อ"
                  required
                />
              </label>
              <label className="full">
                {page === "users" ? "รหัสผ่านเริ่มต้น" : "รายละเอียด"}
                {page === "users" ? (
                  <input
                    type="password"
                    value={details}
                    onChange={(e) => setDetails(e.target.value)}
                    minLength={8}
                  />
                ) : (
                  <textarea
                    value={details}
                    onChange={(e) => setDetails(e.target.value)}
                    rows={4}
                  />
                )}
              </label>
              {page === "requirements" && <>
                <label>Priority<select value={createRequirementPriority} onChange={(e) => setCreateRequirementPriority(e.target.value)}>{["P0","P1","P2","P3"].map((x) => <option key={x}>{x}</option>)}</select></label>
                <label>Risk<select value={createRequirementRisk} onChange={(e) => setCreateRequirementRisk(e.target.value)}>{["Critical","High","Medium","Low"].map((x) => <option key={x}>{x}</option>)}</select></label>
                <label>Source<input value={createRequirementSource} onChange={(e) => setCreateRequirementSource(e.target.value)} placeholder="เช่น BRD, User Story, ลูกค้า" /></label>
                <label>Owner<select value={createRequirementOwnerId} onChange={(e) => setCreateRequirementOwnerId(e.target.value)}><option value="">ไม่ระบุผู้รับผิดชอบ</option>{createRequirementUsers.map((x) => <option key={x.userId} value={x.userId}>{x.displayName}</option>)}</select></label>
                <label className="check-line"><input type="checkbox" checked={createRequirementInScope} onChange={(e) => setCreateRequirementInScope(e.target.checked)} /> In Scope</label>
                <label className="full">Acceptance Criteria<textarea rows={3} value={createRequirementCriteria} onChange={(e) => setCreateRequirementCriteria(e.target.value)} placeholder="ระบุเงื่อนไขที่ใช้ยืนยันว่า Requirement เสร็จสมบูรณ์" /></label>
              </>}
            </div>
            <div className="modal-actions">
              <button className="btn" onClick={() => setModal(false)}>
                ยกเลิก
              </button>
              <button
                className="btn primary"
                disabled={
                  saving ||
                  (page !== "requirements" && !code.trim()) ||
                  (page === "requirements" && (!createProjectId || !createModuleId)) ||
                  !name.trim() ||
                  (page === "users" && details.length < 8)
                }
                onClick={save}
              >
                {saving ? "กำลังบันทึก..." : "บันทึก"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default App;
