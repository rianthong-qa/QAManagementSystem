import { useEffect, useMemo, useState } from "react";
import "./App.css";
import "./Login.css";
import "./DragDrop.css";
import "./ReleaseBuild.css";
import "./TestManagement.css";
import "./Dashboard.css";
import "./DashboardExecutive.css";

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
  | "audit";
type SessionUser = {
  userId: string;
  username: string;
  displayName: string;
  roles: string[];
  permissions: string[];
};
type DashboardSummary = {
  totalRequirements: number; coveredRequirements: number; requirementCoverage: number;
  totalCases: number; executedCases: number; executionProgress: number; passedCases: number; passRate: number;
  openP0: number; openP1: number; overallScore?: number; recommendedDecision: string; generatedAt: string;
  modules: { moduleId: string; parentModuleId?: string; moduleName: string; requirements: number; coveredRequirements: number; testCases: number; executed: number; passed: number; failed: number; blocked: number; coveragePercent: number; executionPercent: number; passRate: number; health: string }[];
  users: { userId: string; displayName: string; executions: number; passed: number; failed: number; blocked: number; passRate: number; lastExecutedAt?: string }[];
  statusDistribution: { status: string; count: number; color: string }[];
};
const apiUrl = import.meta.env.VITE_API_URL ?? "http://localhost:5038/api/v1";

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
      { id: "audit", icon: "⌕", label: "Audit Log" },
    ],
  },
];

const pageNames: Record<Page, string> = Object.fromEntries(
  nav.flatMap((g) => g.items.map((i) => [i.id, i.label])),
) as Record<Page, string>;
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
  regression: "TESTCASE.VIEW",
  summary: "REPORT.EXPORT",
  risks: "RISK.APPROVE",
  signoff: "RELEASE.SIGNOFF",
  users: "ADMIN.USER",
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

function Dashboard({ projectId, releaseId, buildId, shareToken }: { projectId?: string; releaseId?: string; buildId?: string; shareToken?: string }) {
  const [data, setData] = useState<DashboardSummary | null>(null), [loading, setLoading] = useState(true), [error, setError] = useState("");
  const [moduleSearch, setModuleSearch] = useState(""), [collapsedModules, setCollapsedModules] = useState<Set<string>>(new Set()), [userFilter, setUserFilter] = useState("");
  useEffect(() => {
    setLoading(true); setError("");
    const params = new URLSearchParams({ ...(projectId && { projectId }), ...(releaseId && { releaseId }), ...(buildId && { buildId }) });
    const url = shareToken ? `${apiUrl}/dashboard/shared?token=${encodeURIComponent(shareToken)}` : `${apiUrl}/dashboard/summary?${params}`;
    fetch(url, shareToken ? {} : { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` } })
      .then(async r => { if (!r.ok) throw new Error(r.status === 401 ? "ลิงก์แชร์ไม่ถูกต้องหรือหมดอายุ" : "ไม่สามารถโหลดข้อมูล Dashboard ได้"); return r.json(); })
      .then(setData).catch(e => setError(e.message)).finally(() => setLoading(false));
  }, [projectId, releaseId, buildId, shareToken]);
  if (loading) return <div className="executive-loading">กำลังประมวลผลข้อมูลคุณภาพ...</div>;
  if (error || !data) return <div className="executive-error">{error || "ไม่พบข้อมูล"}</div>;
  const normalizedModuleSearch = moduleSearch.trim().toLocaleLowerCase("th-TH");
  const includedModuleIds = new Set(data.modules.filter(x => !normalizedModuleSearch || x.moduleName.toLocaleLowerCase("th-TH").includes(normalizedModuleSearch)).map(x => x.moduleId));
  if (normalizedModuleSearch) for (const item of data.modules) if (includedModuleIds.has(item.moduleId)) { let parentId = item.parentModuleId; while (parentId) { includedModuleIds.add(parentId); parentId = data.modules.find(x => x.moduleId === parentId)?.parentModuleId; } }
  const moduleRows: { item: DashboardSummary["modules"][number]; depth: number; childCount: number }[] = [];
  const appendModules = (parentId: string | undefined, depth: number) => data.modules.filter(x => (x.parentModuleId || undefined) === parentId && includedModuleIds.has(x.moduleId)).sort((a,b) => a.moduleName.localeCompare(b.moduleName, "th")).forEach(item => { const children = data.modules.filter(x => x.parentModuleId === item.moduleId && includedModuleIds.has(x.moduleId)); moduleRows.push({ item, depth, childCount: children.length }); if (normalizedModuleSearch || !collapsedModules.has(item.moduleId)) appendModules(item.moduleId, depth + 1); });
  appendModules(undefined, 0);
  data.modules.filter(x => x.parentModuleId && !data.modules.some(parent => parent.moduleId === x.parentModuleId) && includedModuleIds.has(x.moduleId)).forEach(item => { if (!moduleRows.some(row => row.item.moduleId === item.moduleId)) moduleRows.push({ item, depth: 0, childCount: 0 }); });
  const users = data.users.filter(x => !userFilter || x.userId === userFilter);
  const totalStatus = Math.max(1, data.statusDistribution.reduce((n, x) => n + x.count, 0)); let angle = 0;
  const donut = data.statusDistribution.map(x => { const start = angle; angle += x.count / totalStatus * 360; return `${x.color} ${start}deg ${angle}deg`; }).join(",");
  const decisionReason = data.recommendedDecision === "NO DATA" ? "ยังไม่มี Requirement หรือ Test Cycle สำหรับประเมิน"
    : data.openP0 > 0 ? `พบ P0 ค้าง ${data.openP0} รายการ`
    : data.openP1 > 0 ? `พบ P1 ค้าง ${data.openP1} รายการ`
    : data.requirementCoverage < 90 ? `Requirement Coverage ${data.requirementCoverage}% ต่ำกว่าเกณฑ์ 90%`
    : data.passRate < 90 ? `Pass Rate ${data.passRate}% ต่ำกว่าเกณฑ์ 90%`
    : "ผ่านเกณฑ์ P0/P1, Coverage และ Pass Rate";
  return <div className="executive-dashboard">
    <section className="executive-hero"><div className="executive-title"><span className="eyebrow">QUALITY EXECUTIVE OVERVIEW</span><h2>Release Readiness Dashboard</h2><p>ข้อมูลจากระบบ ณ {new Date(data.generatedAt).toLocaleString("th-TH")}</p></div><div className="overall-score"><small>PROJECT OVERALL SCORE</small><strong>{data.overallScore == null ? "N/A" : `${data.overallScore}%`}</strong><span>Coverage 30% · Execution 30% · Pass 40%</span></div><div className={`decision decision-${data.recommendedDecision.toLowerCase().replace(" ", "-")}`}><small>คำแนะนำ</small><strong>{data.recommendedDecision}</strong><span>{decisionReason}</span></div></section>
    <div className="kpi-grid">{[
      ["Requirement Coverage", `${data.requirementCoverage}%`, `${data.coveredRequirements.toLocaleString()} / ${data.totalRequirements.toLocaleString()} Covered`, "green"],
      ["Execution Progress", `${data.executionProgress}%`, `${data.executedCases.toLocaleString()} / ${data.totalCases.toLocaleString()} Cases`, "blue"],
      ["Pass Rate", `${data.passRate}%`, `${data.passedCases.toLocaleString()} Passed`, "green"],
      ["Release Blockers", data.openP0 + data.openP1, `P0 ${data.openP0} • P1 ${data.openP1}`, data.openP0 + data.openP1 ? "red" : "green"],
    ].map(x => <article className="card kpi" key={x[0]}><span>{x[0]}</span><strong>{x[1]}</strong><small className={String(x[3])}>{x[2]}</small></article>)}</div>
    <div className="executive-chart-grid">
      <article className="card status-card"><div><h3>Execution Results</h3><p>สัดส่วนผลการทดสอบทั้งหมด</p><div className="chart-legend">{data.statusDistribution.map(x => <span key={x.status}><i style={{background:x.color}} />{x.status}<b>{x.count}</b></span>)}</div></div><div className="donut" style={{background:`conic-gradient(${donut || "#e2e8f0 0 360deg"})`}}><div><strong>{data.executedCases}</strong><small>Executed</small></div></div></article>
      <article className="card"><div className="card-title"><div><h3>Performance by User</h3><p>ผลการดำเนินงานของผู้ทดสอบ</p></div><select value={userFilter} onChange={e=>setUserFilter(e.target.value)}><option value="">ผู้ใช้ทั้งหมด</option>{data.users.map(x=><option key={x.userId} value={x.userId}>{x.displayName}</option>)}</select></div><div className="user-bars">{users.length ? users.slice(0,8).map(x=><div className="user-bar" key={x.userId}><span>{x.displayName}</span><div><i style={{width:`${x.passRate}%`}} /></div><strong>{x.passRate}%</strong><small>{x.executions} runs</small></div>) : <p className="muted-row">ยังไม่มีข้อมูลการทดสอบโดยผู้ใช้</p>}</div></article>
    </div>
    <article className="card module-health-card">
      <div className="card-title"><div><h3>Module Health <Badge tone="blue">{data.modules.length}</Badge></h3><p>Coverage, execution และคุณภาพตามโครงสร้าง Module</p></div><label className="module-search"><span>⌕</span><input value={moduleSearch} onChange={e=>setModuleSearch(e.target.value)} placeholder="ค้นหา Module..." /></label></div>
      <div className="table-wrap module-table-scroll"><table><thead><tr><th>Module</th><th>Requirements</th><th>Test Cases</th><th>Coverage</th><th>Execution</th><th>Pass Rate</th><th>Fail</th><th>Blocked</th><th>Status</th></tr></thead><tbody>
        {moduleRows.map(({item:r,depth,childCount}) => <tr key={r.moduleId} className={depth ? "module-child-row" : "module-parent-row"}><td><div className="module-tree-name" style={{paddingLeft:`${depth * 24}px`}}>{childCount > 0 ? <button title={collapsedModules.has(r.moduleId) ? "ขยาย" : "ย่อ"} onClick={()=>setCollapsedModules(current=>{const next=new Set(current); if(next.has(r.moduleId)) next.delete(r.moduleId); else next.add(r.moduleId); return next;})}>{collapsedModules.has(r.moduleId) ? "▸" : "▾"}</button> : <span className="tree-branch">└</span>}<b>{r.moduleName}</b>{childCount > 0 && <small>{childCount} รายการ</small>}</div></td><td>{r.coveredRequirements}/{r.requirements}</td><td>{r.testCases}</td><td><span className="metric-bar"><i style={{width:`${r.coveragePercent}%`}} /></span>{r.coveragePercent}%</td><td>{r.executionPercent}%</td><td><b className="metric-pass">{r.passRate}%</b></td><td><b className="metric-fail">{r.failed}</b></td><td><b className="metric-blocked">{r.blocked}</b></td><td><Badge tone={r.health === "Healthy" ? "green" : r.health === "Watch" ? "yellow" : "red"}>{r.health}</Badge></td></tr>)}
        {!moduleRows.length && <tr><td colSpan={9} className="muted-row">ไม่พบ Module ที่ค้นหา</td></tr>}
      </tbody></table></div>
    </article>
  </div>;
}

function DataPage({ page, search, canAssignExecution = false }: { page: Page; search: string; canAssignExecution?: boolean }) {
  if (page === "execution") return <ExecutionWorkspacePage />;
  if (page === "test-cycles") return <TestCyclesPage search={search} canEdit={canAssignExecution} />;
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
    return <TestSuitesPage search={search} canEdit={canEdit} />;
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
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
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
function ReleasesPage({ search }: { search: string; refresh?: number }) {
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
    [editRelease, setEditRelease] = useState<ReleaseItem | null>(null),
    [editBuild, setEditBuild] = useState<BuildItem | null>(null),
    [projectId, setProjectId] = useState(""),
    [code, setCode] = useState(""),
    [name, setName] = useState(""),
    [type, setType] = useState("Major"),
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
      `${x.releaseCode} ${x.version} ${x.releaseType ?? ""} ${x.status}`
        .toLowerCase()
        .includes(term),
    ),
    filteredBuilds = builds.filter((x) =>
      `${x.buildNumber} ${x.applicationVersion ?? ""} ${x.commitReference ?? ""}`
        .toLowerCase()
        .includes(term),
    );
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
    setType(item?.releaseType ?? "Major");
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
      window.alert(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ");
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
            <button
              key={x.releaseId}
              className={selectedId === x.releaseId ? "active" : ""}
              onClick={() => setSelectedId(x.releaseId)}
            >
              <span>{x.releaseCode}</span>
              <b>Version {x.version}</b>
              <small>
                {x.releaseType || "-"} ·{" "}
                {x.plannedReleaseDate
                  ? new Date(x.plannedReleaseDate).toLocaleDateString("th-TH")
                  : "ไม่ระบุวัน"}
              </small>
              <Badge
                tone={
                  x.status === "Ready" || x.status === "Released"
                    ? "green"
                    : "yellow"
                }
              >
                {x.status}
              </Badge>
            </button>
          ))}
        </div>
      </article>
      <article className="card build-panel">
        <div className="card-title">
          <div>
            <h3>Builds {selected && <span>· {selected.releaseCode}</span>}</h3>
            <p>{filteredBuilds.length} Build ใน Release ที่เลือก</p>
          </div>
          {canEdit && selected && (
            <div className="row-actions">
              <button className="btn" onClick={() => openRelease(selected)}>
                แก้ไข Release
              </button>
              <button className="btn primary" onClick={() => openBuild()}>
                + Build
              </button>
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
                      <b>{x.buildNumber}</b>
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
                      <option>Major</option>
                      <option>Minor</option>
                      <option>Hotfix</option>
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
};
function RequirementsPage({
  search,
  refresh,
  canEdit,
}: {
  search: string;
  refresh: number;
  canEdit: boolean;
}) {
  const [items, setItems] = useState<RequirementItem[]>([]),
    [error, setError] = useState(""),
    [loading, setLoading] = useState(true),
    [reload, setReload] = useState(0),
    [editing, setEditing] = useState<RequirementItem | null>(null),
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
      .then(setItems)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [refresh, reload]);
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
        body: JSON.stringify({ releaseId: releaseId || null, moduleId, title, description: description || null, acceptanceCriteria: criteria || null, priority, riskLevel: risk || null, source: source || null, ownerUserId: editing.ownerUserId ?? null, isInScope: inScope }),
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
  const remove = async (item: RequirementItem) => {
    if (!window.confirm(`ยืนยันลบ ${item.requirementCode}?\nข้อมูลจะถูกซ่อนและยังเก็บประวัติไว้`)) return;
    const response = await fetch(`${apiUrl}/requirements/${item.requirementId}`, { method: "DELETE", headers });
    if (!response.ok) { window.alert("ลบ Requirement ไม่สำเร็จ"); return; }
    setReload((x) => x + 1);
  };
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
  const filtered = items.filter((x) =>
    `${x.requirementCode} ${x.title} ${x.priority} ${x.status}`
      .toLowerCase()
      .includes(search.toLowerCase()),
  );
  return (
    <article className="card">
      <div className="table-tools">
        <div>
          <select>
            <option>ทุกสถานะ</option>
          </select>
          <select>
            <option>ทุก Priority</option>
            <option>P0</option>
            <option>P1</option>
          </select>
        </div>
        <span>{filtered.length} Requirements</span>
      </div>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Requirement ID</th>
              <th>Title</th>
              <th>Priority</th>
              <th>Risk</th>
              <th>Revision</th>
              <th>In Scope</th>
              <th>Status</th>
              {canEdit && <th>จัดการ</th>}
            </tr>
          </thead>
          <tbody>
            {filtered.map((x) => (
              <tr key={x.requirementId}>
                <td>
                  <b>{x.requirementCode}</b>
                </td>
                <td>{x.title}</td>
                <td>
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
                <td>{x.riskLevel ?? "-"}</td>
                <td>Rev. {x.revisionNo}</td>
                <td>{x.isInScope ? "Yes" : "No"}</td>
                <td>
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
                {canEdit && <td><div className="row-actions"><button className="table-action" onClick={() => openEdit(x)}>แก้ไข</button><button className="table-action danger-action" onClick={() => remove(x)}>ลบ</button></div></td>}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {editing && (
        <div className="modal" onMouseDown={() => setEditing(null)}>
          <div className="modal-box requirement-editor" onMouseDown={(e) => e.stopPropagation()}>
            <div className="modal-head"><h2>แก้ไข Requirement</h2><button onClick={() => setEditing(null)}>×</button></div>
            <div className="form-grid">
              <label>Requirement Code<input value={editing.requirementCode} disabled /></label>
              <label>Module<select value={moduleId} onChange={(e) => setModuleId(e.target.value)}>{modules.map((x) => <option key={x.moduleId} value={x.moduleId}>{x.moduleName}</option>)}</select></label>
              <label>Release<select value={releaseId} onChange={(e) => setReleaseId(e.target.value)}><option value="">ไม่ระบุ Release</option>{releases.map((x) => <option key={x.releaseId} value={x.releaseId}>{x.releaseCode} · Version {x.version}</option>)}</select></label>
              <label>Title<input value={title} onChange={(e) => setTitle(e.target.value)} /></label>
              <label>Priority<select value={priority} onChange={(e) => setPriority(e.target.value)}>{["P0","P1","P2","P3"].map((x) => <option key={x}>{x}</option>)}</select></label>
              <label>Risk<select value={risk} onChange={(e) => setRisk(e.target.value)}>{["Critical","High","Medium","Low"].map((x) => <option key={x}>{x}</option>)}</select></label>
              <label>Status<select value={status} onChange={(e) => setStatus(e.target.value)}>{["Draft","Review","Approved","Implemented","Cancelled"].map((x) => <option key={x}>{x}</option>)}</select></label>
              <label>Source<input value={source} onChange={(e) => setSource(e.target.value)} /></label>
              <label className="check-line"><input type="checkbox" checked={inScope} onChange={(e) => setInScope(e.target.checked)} /> In Scope</label>
              <label className="full">Description<textarea rows={3} value={description} onChange={(e) => setDescription(e.target.value)} /></label>
              <label className="full">Acceptance Criteria<textarea rows={3} value={criteria} onChange={(e) => setCriteria(e.target.value)} /></label>
            </div>
            <div className="modal-actions"><button className="btn" onClick={() => setEditing(null)}>ยกเลิก</button><button className="btn primary" disabled={saving || !title.trim() || !moduleId} onClick={saveEdit}>{saving ? "กำลังบันทึก..." : "บันทึก"}</button></div>
          </div>
        </div>
      )}
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
function TestCasesPage({
  search,
  canEdit,
}: {
  search: string;
  canEdit: boolean;
}) {
  const [items, setItems] = useState<TestCaseItem[]>([]),
    [projects, setProjects] = useState<ProjectItem[]>([]),
    [modules, setModules] = useState<ModuleItem[]>([]),
    [loading, setLoading] = useState(true),
    [reload, setReload] = useState(0),
    [form, setForm] = useState(false),
    [editing, setEditing] = useState<TestCaseItem | null>(null),
    [saving, setSaving] = useState(false),
    [statusFilter, setStatusFilter] = useState("");
  const [projectId, setProjectId] = useState(""),
    [moduleId, setModuleId] = useState(""),
    [code, setCode] = useState(""),
    [title, setTitle] = useState(""),
    [objective, setObjective] = useState(""),
    [preconditions, setPreconditions] = useState(""),
    [priority, setPriority] = useState("P1"),
    [testType, setTestType] = useState("Functional"),
    [automation, setAutomation] = useState(false),
    [status, setStatus] = useState("Draft"),
    [changeReason, setChangeReason] = useState(""),
    [steps, setSteps] = useState([
      { stepNo: 1, action: "", testData: "", expectedResult: "" },
    ]);
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
  useEffect(() => {
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    Promise.all([
      fetch(`${apiUrl}/test-cases`, { headers: h }).then((r) => r.json()),
      fetch(`${apiUrl}/projects`, { headers: h }).then((r) => r.json()),
    ])
      .then(([cases, projectData]) => {
        setItems(cases);
        const activeProjects = (projectData as ProjectItem[]).filter(
          (x) => x.isActive,
        );
        setProjects(activeProjects);
        setProjectId(
          (current) => current || activeProjects[0]?.projectId || "",
        );
      })
      .finally(() => setLoading(false));
  }, [reload]);
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
    setPriority(item?.priority ?? "P1");
    setTestType(item?.testType ?? "Functional");
    setAutomation(item?.automationCandidate ?? false);
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
            ownerUserId: editing.ownerUserId ?? null,
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
            ownerUserId: null,
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
      setForm(false);
      setReload((x) => x + 1);
    } catch (e) {
      window.alert(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ");
    } finally {
      setSaving(false);
    }
  };
  const remove = async (item: TestCaseItem) => {
    if (
      !window.confirm(
        `ยืนยันลบ ${item.testCaseCode}? ข้อมูลประวัติเดิมจะยังคงอยู่`,
      )
    )
      return;
    const response = await fetch(`${apiUrl}/test-cases/${item.testCaseId}`, {
      method: "DELETE",
      headers,
    });
    if (!response.ok) {
      window.alert("ลบ Test Case ไม่สำเร็จ");
      return;
    }
    setReload((x) => x + 1);
  };
  if (loading)
    return (
      <article className="card empty">
        <p>กำลังโหลด Test Case...</p>
      </article>
    );
  const rows = items.filter(
    (x) =>
      `${x.testCaseCode} ${x.title} ${x.testType ?? ""} ${x.status}`
        .toLowerCase()
        .includes(search.toLowerCase()) &&
      (!statusFilter || x.status === statusFilter),
  );
  return (
    <>
      <article className="card">
        <div className="table-tools">
          <div>
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
          </div>
          <div className="row-actions">
            <span>{rows.length} Test Cases</span>
            {canEdit && (
              <button className="btn primary" onClick={() => openForm()}>
                + Test Case
              </button>
            )}
          </div>
        </div>
        <div className="table-wrap">
          <table>
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
              {rows.map((x) => (
                <tr key={x.testCaseId}>
                  <td>
                    <b>{x.testCaseCode}</b>
                  </td>
                  <td>{x.title}</td>
                  <td>
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
                  <td>{x.testType ?? "-"}</td>
                  <td>Rev. {x.revisionNo}</td>
                  <td>{x.steps.length}</td>
                  <td>
                    <Badge tone={x.status === "Ready" ? "green" : "yellow"}>
                      {x.status}
                    </Badge>
                  </td>
                  {canEdit && (
                    <td>
                      <div className="row-actions">
                        <button
                          className="table-action"
                          onClick={() => openForm(x)}
                        >
                          แก้ไข
                        </button>
                        <button
                          className="table-action danger-action"
                          onClick={() => remove(x)}
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
                Module
                <select
                  value={moduleId}
                  onChange={(e) => setModuleId(e.target.value)}
                >
                  {modules.map((x) => (
                    <option key={x.moduleId} value={x.moduleId}>
                      {x.moduleCode} · {x.moduleName}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Test Case Code
                <input
                  disabled
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                />
              </label>
              <label>
                Title
                <input
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                />
              </label>
              <label>
                Priority
                <select
                  value={priority}
                  onChange={(e) => setPriority(e.target.value)}
                >
                  <option>P0</option>
                  <option>P1</option>
                  <option>P2</option>
                  <option>P3</option>
                </select>
              </label>
              <label>
                Type
                <select
                  value={testType}
                  onChange={(e) => setTestType(e.target.value)}
                >
                  <option>Functional</option>
                  <option>Regression</option>
                  <option>Integration</option>
                  <option>Performance</option>
                  <option>Security</option>
                  <option>UAT</option>
                </select>
              </label>
              {editing && (
                <label>
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
              <label className="check-line">
                <input
                  type="checkbox"
                  checked={automation}
                  onChange={(e) => setAutomation(e.target.checked)}
                />{" "}
                Automation Candidate
              </label>
              <label className="full">
                Objective
                <textarea
                  rows={2}
                  value={objective}
                  onChange={(e) => setObjective(e.target.value)}
                />
              </label>
              <label className="full">
                Preconditions
                <textarea
                  rows={2}
                  value={preconditions}
                  onChange={(e) => setPreconditions(e.target.value)}
                />
              </label>
              {editing && (
                <label className="full">
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
    </>
  );
}
type RtmItem = {
  requirementId: string;
  requirementCode: string;
  title: string;
  priority: string;
  testCaseCount: number;
  coverageStatus: string;
  status: string;
};
function RtmPage({ refresh }: { refresh: number }) {
  const [items, setItems] = useState<RtmItem[]>([]),
    [summary, setSummary] = useState<{
      totalRequirements: number;
      covered: number;
      coveragePercent: number;
    } | null>(null),
    [loading, setLoading] = useState(true);
  useEffect(() => {
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetch(`${apiUrl}/releases`, { headers: h })
      .then((r) => r.json())
      .then(async (releases) => {
        if (!releases.length) return;
        const [data, sum] = await Promise.all([
          fetch(`${apiUrl}/releases/${releases[0].releaseId}/rtm`, {
            headers: h,
          }).then((r) => r.json()),
          fetch(
            `${apiUrl}/releases/${releases[0].releaseId}/coverage-summary`,
            { headers: h },
          ).then((r) => r.json()),
        ]);
        setItems(data);
        setSummary(sum);
      })
      .finally(() => setLoading(false));
  }, [refresh]);
  if (loading)
    return (
      <article className="card empty">
        <p>กำลังคำนวณ RTM...</p>
      </article>
    );
  return (
    <>
      <div className="kpi-grid">
        <article className="card kpi">
          <span>Requirements</span>
          <strong>{summary?.totalRequirements ?? 0}</strong>
          <small>In Scope</small>
        </article>
        <article className="card kpi">
          <span>Covered</span>
          <strong>{summary?.covered ?? 0}</strong>
          <small className="green">Linked Test Cases</small>
        </article>
        <article className="card kpi">
          <span>Coverage</span>
          <strong>{summary?.coveragePercent ?? 0}%</strong>
          <small>Current Release</small>
        </article>
      </div>
      <article className="card">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Requirement</th>
                <th>Title</th>
                <th>Priority</th>
                <th>Test Cases</th>
                <th>Coverage</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {items.map((x) => (
                <tr key={x.requirementId}>
                  <td>
                    <b>{x.requirementCode}</b>
                  </td>
                  <td>{x.title}</td>
                  <td>{x.priority}</td>
                  <td>{x.testCaseCount}</td>
                  <td>
                    <Badge tone={x.testCaseCount ? "green" : "red"}>
                      {x.coverageStatus}
                    </Badge>
                  </td>
                  <td>{x.status}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>
    </>
  );
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
function TestCyclesPage({ search, canEdit }: { search: string; canEdit: boolean }) {
  const [items, setItems] = useState<TestCycleItem[]>([]),
    [projects, setProjects] = useState<ProjectItem[]>([]),
    [releases, setReleases] = useState<CycleRelease[]>([]),
    [builds, setBuilds] = useState<CycleBuild[]>([]),
    [environments, setEnvironments] = useState<CycleEnvironment[]>([]),
    [suites, setSuites] = useState<TestSuiteItem[]>([]),
    [reload, setReload] = useState(0),
    [form, setForm] = useState(false),
    [editing, setEditing] = useState<TestCycleItem | null>(null),
    [saving, setSaving] = useState(false);
  const [projectId, setProjectId] = useState(""),
    [releaseId, setReleaseId] = useState(""),
    [buildId, setBuildId] = useState(""),
    [environmentId, setEnvironmentId] = useState(""),
    [suiteId, setSuiteId] = useState(""),
    [code, setCode] = useState(""),
    [name, setName] = useState(""),
    [cycleType, setCycleType] = useState("Regression"),
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
      return Array.isArray(data) ? (data as T[]) : [];
    };
    Promise.all([
      readList<TestCycleItem>(`${apiUrl}/test-cycles`),
      readList<ProjectItem>(`${apiUrl}/projects`),
      readList<CycleRelease>(`${apiUrl}/releases`),
      readList<CycleEnvironment>(`${apiUrl}/test-environments`),
      readList<TestSuiteItem>(`${apiUrl}/test-suites`),
    ]).then(async ([c, p, r, e, s]) => {
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
      setItems(c);
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
      setItems([]);
      setProjects([]);
      setReleases([]);
      setBuilds([]);
      setEnvironments([]);
      setSuites([]);
    });
  }, [reload]);
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
    setProjectId(cycle?.projectId ?? projects[0]?.projectId ?? "");
    setReleaseId(cycle?.releaseId ?? "");
    setBuildId(cycle?.buildId ?? "");
    setEnvironmentId(cycle?.environmentId ?? "");
    setSuiteId(cycle?.testSuiteId ?? "");
    const targetProjectId = cycle?.projectId ?? projects[0]?.projectId ?? "";
    const project = projects.find((x) => x.projectId === targetProjectId);
    setCode(
      cycle?.cycleCode ??
        nextBusinessCode(
          `${project?.projectCode ?? "PRJ"}-CYC`,
          items.map((x) => x.cycleCode),
        ),
    );
    setName(cycle?.cycleName ?? "");
    setCycleType(cycle?.cycleType ?? "Regression");
    setStartDate(cycle?.startDate?.slice(0, 10) ?? "");
    setEndDate(cycle?.endDate?.slice(0, 10) ?? "");
    setNotes(cycle?.notes ?? "");
    setForm(true);
  };
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
      setReload((x) => x + 1);
    } catch (e) {
      window.alert(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ");
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
    } finally {
      setSaving(false);
    }
  };
  const changeStatus = async (cycle: TestCycleItem, status: string) => {
    await fetch(`${apiUrl}/test-cycles/${cycle.testCycleId}/status`, {
      method: "POST",
      headers,
      body: JSON.stringify({ status }),
    });
    setReload((x) => x + 1);
  };
  const remove = async (cycle: TestCycleItem) => {
    if (!window.confirm(`ยืนยันลบ ${cycle.cycleCode}?`)) return;
    const response = await fetch(`${apiUrl}/test-cycles/${cycle.testCycleId}`, {
      method: "DELETE",
      headers,
    });
    if (!response.ok) {
      window.alert("ลบ Test Cycle ไม่สำเร็จ");
      return;
    }
    setReload((x) => x + 1);
  };
  const rows = items.filter((x) =>
    `${x.cycleCode} ${x.cycleName} ${x.releaseCode} ${x.buildNumber}`
      .toLowerCase()
      .includes(search.toLowerCase()),
  );
  return (
    <>
      <article className="card">
        <div className="table-tools">
          <span>{rows.length} Test Cycles</span>
          {canEdit && (
            <button className="btn primary" onClick={() => openForm()}>
              + สร้าง Test Cycle
            </button>
          )}
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
              {rows.map((x) => (
                <tr key={x.testCycleId}>
                  <td>
                    <b>{x.cycleCode}</b>
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
      </article>
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
                  <option>Smoke</option>
                  <option>Regression</option>
                  <option>UAT</option>
                  <option>Exploratory</option>
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
function ExecutionWorkspacePage() {
  const [cycles, setCycles] = useState<TestCycleItem[]>([]),
    [cycleId, setCycleId] = useState(""),
    [workspace, setWorkspace] = useState<ExecutionWorkspace | null>(null),
    [selectedId, setSelectedId] = useState(""),
    [stepStatuses, setStepStatuses] = useState<Record<number, string>>({}),
    [stepActuals, setStepActuals] = useState<Record<number, string>>({}),
    [actual, setActual] = useState(""),
    [comment, setComment] = useState(""),
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
        return Array.isArray(data) ? (data as TestCycleItem[]) : [];
      })
      .then((data: TestCycleItem[]) => {
        setCycles(data);
        setCycleId((current) => current || data[0]?.testCycleId || "");
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
  const selected = useMemo(
    () => workspace?.cases.find((x) => x.testCycleCaseId === selectedId),
    [workspace, selectedId],
  );
  const executionStats = useMemo(() => {
    const cases = workspace?.cases ?? [];
    return {
      total: cases.length,
      passed: cases.filter((x) => x.currentStatus === "Pass").length,
      failed: cases.filter((x) => x.currentStatus === "Fail").length,
      pending: cases.filter((x) => x.currentStatus === "NotRun").length,
    };
  }, [workspace]);
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
  return (
    <div className="execution-page">
      <div className="execution-toolbar card">
        <label>
          Test Cycle
          <select value={cycleId} onChange={(e) => setCycleId(e.target.value)}>
            <option value="">เลือก Test Cycle</option>
            {cycles.map((x) => (
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
            {workspace.cases.map((x) => (
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
  cases: {
    testCaseId: string;
    testCaseCode: string;
    title: string;
    priority: string;
    sortOrder: number;
    isRequired: boolean;
  }[];
};
function TestSuitesPage({
  search,
  canEdit,
}: {
  search: string;
  canEdit: boolean;
}) {
  const [items, setItems] = useState<TestSuiteItem[]>([]),
    [projects, setProjects] = useState<ProjectItem[]>([]),
    [testCases, setTestCases] = useState<TestCaseItem[]>([]),
    [reload, setReload] = useState(0),
    [form, setForm] = useState(false),
    [editing, setEditing] = useState<TestSuiteItem | null>(null),
    [managing, setManaging] = useState<TestSuiteItem | null>(null),
    [checked, setChecked] = useState<string[]>([]),
    [saving, setSaving] = useState(false);
  const [code, setCode] = useState(""),
    [name, setName] = useState(""),
    [type, setType] = useState("Regression"),
    [risk, setRisk] = useState("P1"),
    [description, setDescription] = useState(""),
    [projectId, setProjectId] = useState(""),
    [active, setActive] = useState(true);
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
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
      setItems(s);
      const activeProjects = (p as ProjectItem[]).filter((x) => x.isActive);
      setProjects(activeProjects);
      setTestCases(t);
      setProjectId((current) => current || activeProjects[0]?.projectId || "");
    });
  }, [reload]);
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
    setType(suite?.suiteType ?? "Regression");
    setRisk(suite?.riskTier ?? "P1");
    setDescription(suite?.description ?? "");
    setProjectId(suite?.projectId ?? projects[0]?.projectId ?? "");
    setActive(suite?.isActive ?? true);
    setForm(true);
  };
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
      await fetch(`${apiUrl}/test-suites/${managing.testSuiteId}/cases`, {
        method: "POST",
        headers,
        body: JSON.stringify({ testCaseIds: checked, isRequired: true }),
      });
      setChecked([]);
      setManaging(null);
      setReload((x) => x + 1);
    } finally {
      setSaving(false);
    }
  };
  const removeCase = async (suiteId: string, caseId: string) => {
    await fetch(`${apiUrl}/test-suites/${suiteId}/cases/${caseId}`, {
      method: "DELETE",
      headers,
    });
    setManaging(null);
    setReload((x) => x + 1);
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
  const rows = items.filter(
    (x) =>
      x.isActive &&
      `${x.suiteCode} ${x.suiteName} ${x.suiteType ?? ""}`
        .toLowerCase()
        .includes(search.toLowerCase()),
  );
  const available = testCases.filter(
    (x) =>
      managing &&
      !managing.cases.some((c) => c.testCaseId === x.testCaseId) &&
      x.projectId === managing.projectId,
  );
  return (
    <>
      <article className="card">
        <div className="table-tools">
          <span>{rows.length} Test Suites</span>
          {canEdit && (
            <button className="btn primary" onClick={() => openForm()}>
              + สร้าง Test Suite
            </button>
          )}
        </div>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Suite Code</th>
                <th>Suite Name</th>
                <th>Type</th>
                <th>Risk Tier</th>
                <th>Case Count</th>
                <th>Active</th>
                {canEdit && <th>จัดการ</th>}
              </tr>
            </thead>
            <tbody>
              {rows.map((x) => (
                <tr key={x.testSuiteId}>
                  <td>
                    <b>{x.suiteCode}</b>
                  </td>
                  <td>{x.suiteName}</td>
                  <td>{x.suiteType ?? "-"}</td>
                  <td>
                    <Badge tone={x.riskTier === "P0" ? "red" : "yellow"}>
                      {x.riskTier ?? "-"}
                    </Badge>
                  </td>
                  <td>{x.cases.length}</td>
                  <td>
                    <Badge tone={x.isActive ? "green" : "red"}>
                      {x.isActive ? "ใช้งาน" : "ปิดใช้งาน"}
                    </Badge>
                  </td>
                  {canEdit && (
                    <td>
                      <div className="row-actions">
                        <button
                          className="table-action"
                          onClick={() => openForm(x)}
                        >
                          แก้ไข
                        </button>
                        <button
                          className="table-action"
                          onClick={() => {
                            setManaging(x);
                            setChecked([]);
                          }}
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
                  <option>Smoke</option>
                  <option>Regression</option>
                  <option>Critical Regression</option>
                  <option>UAT</option>
                </select>
              </label>
              <label>
                Risk Tier
                <select value={risk} onChange={(e) => setRisk(e.target.value)}>
                  <option>P0</option>
                  <option>P1</option>
                  <option>P2</option>
                  <option>P3</option>
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
            <div className="suite-columns">
              <section>
                <h3>Test Case ในชุด ({managing.cases.length})</h3>
                {managing.cases.length ? (
                  managing.cases.map((x) => (
                    <div className="suite-case" key={x.testCaseId}>
                      <span>
                        <b>{x.testCaseCode}</b>
                        <small>{x.title}</small>
                      </span>
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
                <h3>Test Case ที่เพิ่มได้ ({available.length})</h3>
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
function AdministrationPage({ refresh }: { refresh: number }) {
  const [users, setUsers] = useState<AdminUser[]>([]),
    [roles, setRoles] = useState<AdminRole[]>([]),
    [permissions, setPermissions] = useState<AdminPermission[]>([]),
    [roleId, setRoleId] = useState(""),
    [selected, setSelected] = useState<string[]>([]),
    [saving, setSaving] = useState(false),
    [version, setVersion] = useState(0);
  const [editing, setEditing] = useState<AdminUser | null>(null),
    [displayName, setDisplayName] = useState(""),
    [email, setEmail] = useState(""),
    [active, setActive] = useState(true),
    [userRoleIds, setUserRoleIds] = useState<string[]>([]),
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
      setUsers(u);
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
    <div className="admin-layout">
      <article className="card user-admin-panel">
        <div className="card-title">
          <div>
            <h3>ดำเนินการกับผู้ใช้</h3>
            <p>เลือกบัญชีเพื่อแก้ไขบทบาท สถานะ หรือรีเซ็ตรหัสผ่าน</p>
          </div>
        </div>
        <div className="user-action-bar">
          <select
            aria-label="เลือกผู้ใช้"
            value={editing?.userId ?? ""}
            onChange={(e) => {
              const target = users.find((x) => x.userId === e.target.value);
              if (target) openEdit(target);
              else setEditing(null);
            }}
          >
            <option value="">เลือกบัญชีผู้ใช้...</option>
            {users.map((x) => (
              <option key={x.userId} value={x.userId}>
                {x.displayName} ({x.username})
              </option>
            ))}
          </select>
          {editing && (
            <>
              <button
                className="btn"
                onClick={() => toggleActive(editing)}
                disabled={saving}
              >
                {editing.isActive ? "ปิดใช้งาน" : "เปิดใช้งาน"}
              </button>
              <button
                className="btn"
                onClick={() => {
                  setPasswordUser(editing);
                  setNewPassword("");
                }}
              >
                รีเซ็ตรหัสผ่าน
              </button>
            </>
          )}
        </div>
        {editing && (
          <div className="user-edit-form">
            <label>
              ชื่อที่แสดง
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
            <fieldset>
              <legend>บทบาทของผู้ใช้</legend>
              <div className="role-checks">
                {roles.map((role) => (
                  <label
                    key={role.roleId}
                    className={
                      userRoleIds.includes(role.roleId) ? "selected" : ""
                    }
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
            </fieldset>
            <div className="inline-actions">
              <label className="active-switch">
                <input
                  type="checkbox"
                  checked={active}
                  onChange={(e) => setActive(e.target.checked)}
                />{" "}
                อนุญาตให้เข้าสู่ระบบ
              </label>
              <button
                className="btn primary"
                onClick={saveUser}
                disabled={saving || !displayName.trim()}
              >
                {saving ? "กำลังบันทึก..." : "บันทึกข้อมูลผู้ใช้"}
              </button>
            </div>
          </div>
        )}
        {passwordUser && (
          <div className="password-panel">
            <div>
              <b>ตั้งรหัสผ่านใหม่สำหรับ {passwordUser.username}</b>
              <small>รหัสผ่านต้องมีอย่างน้อย 8 ตัวอักษร</small>
            </div>
            <input
              type="password"
              autoFocus
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              placeholder="รหัสผ่านใหม่"
            />
            <button
              className="btn primary"
              onClick={resetPassword}
              disabled={saving || newPassword.length < 8}
            >
              ยืนยัน
            </button>
            <button className="btn" onClick={() => setPasswordUser(null)}>
              ยกเลิก
            </button>
          </div>
        )}
      </article>
      <article className="card admin-users">
        <div className="card-title">
          <div>
            <h3>จัดการผู้ใช้งาน</h3>
            <p>บัญชีผู้ใช้และบทบาทที่ได้รับมอบหมาย</p>
          </div>
          <span className="count-pill">{users.length} ผู้ใช้</span>
        </div>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>ผู้ใช้งาน</th>
                <th>ชื่อที่แสดง</th>
                <th>บทบาท</th>
                <th>สถานะ</th>
                <th>เข้าสู่ระบบล่าสุด</th>
                <th>จัดการ</th>
              </tr>
            </thead>
            <tbody>
              {users.map((x) => (
                <tr key={x.userId}>
                  <td>
                    <div className="user-cell">
                      <span className="user-avatar">
                        {x.displayName.slice(0, 1).toUpperCase()}
                      </span>
                      <b>{x.username}</b>
                    </div>
                  </td>
                  <td>{x.displayName}</td>
                  <td>
                    <div className="role-tags">
                      {x.roles.length
                        ? x.roles.map((role) => <span key={role}>{role}</span>)
                        : "-"}
                    </div>
                  </td>
                  <td>
                    <Badge tone={x.isActive ? "green" : "red"}>
                      {x.isActive ? "ใช้งาน" : "ปิดใช้งาน"}
                    </Badge>
                  </td>
                  <td>
                    {x.lastLoginAt
                      ? new Date(x.lastLoginAt).toLocaleString("th-TH")
                      : "-"}
                  </td>
                  <td>
                    <button
                      className="table-action"
                      onClick={() => openEdit(x)}
                    >
                      แก้ไข
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>
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
              onClick={() =>
                setSelected(permissions.map((x) => x.permissionId))
              }
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
                    className={
                      selected.includes(x.permissionId) ? "selected" : ""
                    }
                    key={x.permissionId}
                  >
                    <input
                      type="checkbox"
                      checked={selected.includes(x.permissionId)}
                      onChange={(e) =>
                        togglePermission(x.permissionId, e.target.checked)
                      }
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
          <button
            className="btn primary"
            onClick={savePermissions}
            disabled={!roleId || saving}
          >
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
          <div className="login-logo">QA</div>
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
          <div className="login-logo">QA</div>
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
  const shareToken = new URLSearchParams(window.location.search).get("dashboardShare") ?? "";
  const [page, setPage] = useState<Page>("dashboard"),
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
    [refresh, setRefresh] = useState(0),
    [saving, setSaving] = useState(false);
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
    ]).then(([moduleData, releaseData]: [ModuleItem[], ReleaseItem[]]) => {
      const activeModules = moduleData.filter((x) => x.isActive);
      const activeReleases = releaseData.filter((x) => x.status !== "Cancelled");
      setCreateModules(activeModules);
      setCreateReleases(activeReleases);
      setCreateModuleId((current) => activeModules.some((x) => x.moduleId === current) ? current : (activeModules[0]?.moduleId ?? ""));
      setCreateReleaseId((current) => activeReleases.some((x) => x.releaseId === current) ? current : (contextReleaseId && activeReleases.some((x) => x.releaseId === contextReleaseId) ? contextReleaseId : (activeReleases[0]?.releaseId ?? "")));
    });
  }, [modal, page, createProjectId, contextProjectId, contextReleaseId, contextProjects]);
  const description = useMemo(
    () =>
      page === "dashboard"
        ? "สถานะคุณภาพและความพร้อม Release แบบรวมศูนย์"
        : `จัดการข้อมูล ${pageNames[page]} ของ Release ปัจจุบัน`,
    [page],
  );
  const go = (id: Page) => {
    setPage(id);
    setMenu(false);
    window.history.replaceState(null, "", `#/${id}`);
  };
  const logout = () => {
    localStorage.removeItem("qa.accessToken");
    localStorage.removeItem("qa.user");
    setUser(null);
  };
  const shareDashboard = async () => {
    try {
      const response = await fetch(`${apiUrl}/dashboard/share`, { method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }, body: JSON.stringify({ projectId: contextProjectId || null, releaseId: contextReleaseId || null, buildId: contextBuildId || null, validHours: 24 }) });
      if (!response.ok) throw new Error("ไม่สามารถสร้างลิงก์แชร์ได้");
      const result: { token: string; expiresAt: string } = await response.json();
      const url = `${window.location.origin}${window.location.pathname}?dashboardShare=${encodeURIComponent(result.token)}`;
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
              acceptanceCriteria: null,
              priority: "P1",
              riskLevel: "High",
              source: "Manual",
              ownerUserId: user?.userId ?? null,
              isInScope: true,
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
      setRefresh((x) => x + 1);
    } catch (e) {
      window.alert(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ");
    } finally {
      setSaving(false);
    }
  };
  if (shareToken) return <div className="shared-dashboard"><header><div className="logo">QA</div><div><b>ProMaxx2 QA Hub</b><small>Executive Read-only Report</small></div><Badge tone="blue">READ ONLY</Badge></header><main><Dashboard shareToken={shareToken} /></main><footer>ข้อมูลสำหรับการบริหารจัดการ • ไม่สามารถแก้ไขข้อมูลจากหน้านี้</footer></div>;
  if (!user) return <Login onLogin={setUser} />;
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
        <header className="topbar">
          <button className="menu-btn" onClick={() => setMenu((v) => !v)}>
            ☰
          </button>
          <div className="context">
            <select
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
            </select>
            <select
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
            </select>
            <select
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
            </select>
          </div>
          <div className="profile">
            <Badge tone={blockerCount ? "yellow" : "green"}>
              {blockerCount} Blockers
            </Badge>
            <span className="bell">●</span>
            <div className="avatar">
              {user.displayName.slice(0, 2).toUpperCase()}
            </div>
            <div>
              <b>{user.displayName}</b>
              <button className="logout" onClick={logout}>
                ออกจากระบบ
              </button>
            </div>
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
              {can("REPORT.EXPORT") && <button className="btn">Export</button>}
              {page === "dashboard" && <button className="btn share-btn" onClick={shareDashboard}>↗ แชร์ Dashboard</button>}
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
            <Dashboard projectId={contextProjectId} releaseId={contextReleaseId} buildId={contextBuildId} />
          ) : page === "projects" ? (
            <ProjectsPage search={search} refresh={refresh} />
          ) : page === "releases" ? (
            <ReleasesPage search={search} refresh={refresh} />
          ) : page === "requirements" ? (
            <RequirementsPage search={search} refresh={refresh} canEdit={can("REQUIREMENT.EDIT")} />
          ) : page === "test-cases" ? (
            <TestCasesPage search={search} canEdit={can("TESTCASE.EDIT")} />
          ) : page === "rtm" ? (
            <RtmPage refresh={refresh} />
          ) : page === "users" ? (
            <AdministrationPage refresh={refresh} />
          ) : (
            <DataPage page={page} search={search} canAssignExecution={can("EXECUTION.ASSIGN")} />
          )}
        </div>
      </main>
      {modal && (
        <div className="modal" onMouseDown={() => setModal(false)}>
          <div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
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
                      {createModules.map((x) => <option key={x.moduleId} value={x.moduleId}>{x.moduleName}</option>)}
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
