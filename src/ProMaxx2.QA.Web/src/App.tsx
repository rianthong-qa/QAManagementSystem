import { lazy, useState, useEffect, useMemo, Suspense } from "react";
import { isApiRequest, apiUrl } from "./api";
import type { ProjectItem, ReleaseItem, BuildItem } from "./shared/types";
import { notify, confirmDialog, promptDialog } from "./components/dialogStore";
import { formatThaiDateTime } from "./dateTime";
import { Badge } from "./components/Badge";
import { ModalShell } from "./components/ModalShell";
import { type AdminUser, type ModuleItem, type Page, type SessionUser, copyText, nav, okJsonOrEmpty, renderModuleSelectOptions } from "./shared/appShared";
import "./App.css";
import "./styles.css";
import "./ExecutionWorkspace.css";
import "./DragDrop.css";
import "./ReleaseBuild.css";
import "./TestManagement.css";
import "./PermissionMatrix.css";
import "./RoleManagement.css";
import "./MyWork.css";
import "./Dashboard.css";
import "./DashboardExecutive.css";
import "./Rtm.css";
import "./Regression.css";
import "./Automation.css";
// ทุกหน้าโหลดแยก chunk เมื่อเปิดหน้าเท่านั้น (React.lazy) — App.tsx เหลือเฉพาะ shell, context selector, routing และ Login
// หน้าใหม่ให้สร้างใน src/pages/ แล้วเพิ่ม lazy import ที่นี่; ของที่หลายหน้าใช้ร่วมอยู่ใน src/shared/
const Dashboard = lazy(() => import("./pages/Dashboard").then((m) => ({ default: m.Dashboard })));
const DefectsPage = lazy(() => import("./pages/DefectsPage").then((m) => ({ default: m.DefectsPage })));
const ProjectsPage = lazy(() => import("./pages/ProjectsPage").then((m) => ({ default: m.ProjectsPage })));
const ReleasesPage = lazy(() => import("./pages/ReleasesPage").then((m) => ({ default: m.ReleasesPage })));
const RequirementsPage = lazy(() => import("./pages/RequirementsPage").then((m) => ({ default: m.RequirementsPage })));
const TestCasesPage = lazy(() => import("./pages/TestCasesPage").then((m) => ({ default: m.TestCasesPage })));
const RegressionPage = lazy(() => import("./pages/RegressionPage").then((m) => ({ default: m.RegressionPage })));
const RtmPage = lazy(() => import("./pages/RtmPage").then((m) => ({ default: m.RtmPage })));
const TestCyclesPage = lazy(() => import("./pages/TestCyclesPage").then((m) => ({ default: m.TestCyclesPage })));
const ExecutionWorkspacePage = lazy(() => import("./pages/ExecutionWorkspacePage").then((m) => ({ default: m.ExecutionWorkspacePage })));
const TestSuitesPage = lazy(() => import("./pages/TestSuitesPage").then((m) => ({ default: m.TestSuitesPage })));
const MyWorkPage = lazy(() => import("./pages/MyWorkPage").then((m) => ({ default: m.MyWorkPage })));
const MasterSettingsPage = lazy(() => import("./pages/MasterSettingsPage").then((m) => ({ default: m.MasterSettingsPage })));
const SystemMonitorPage = lazy(() => import("./pages/SystemMonitorPage").then((m) => ({ default: m.SystemMonitorPage })));
const AdministrationPage = lazy(() => import("./pages/AdministrationPage").then((m) => ({ default: m.AdministrationPage })));
const AutomationPage = lazy(() => import("./AutomationPage").then((m) => ({ default: m.AutomationPage })));
const AuditLogPage = lazy(() => import("./AuditLogPage").then((m) => ({ default: m.AuditLogPage })));
const TestSummaryPage = lazy(() => import("./pages/TestSummaryPage").then((m) => ({ default: m.TestSummaryPage })));
const RiskAcceptancePage = lazy(() => import("./pages/RiskAcceptancePage").then((m) => ({ default: m.RiskAcceptancePage })));
const ReleaseSignoffPage = lazy(() => import("./pages/ReleaseSignoffPage").then((m) => ({ default: m.ReleaseSignoffPage })));
const pageLoading = <article className="card empty" role="status" aria-live="polite"><div className="spinner" /><h3>กำลังโหลดหน้า...</h3></article>;
// apiUrl มาจาก ./api (แหล่งเดียวทั้งแอป)
const contextReleaseStorageKey = (projectId: string) => `qa.context.release.${projectId}`;
const contextBuildStorageKey = (releaseId: string) => `qa.context.build.${releaseId}`;
const contextRequestTimeoutMs = 10000;
const fetchContextData = (input: RequestInfo | URL, init: RequestInit = {}) => {
  const controller = new AbortController();
  const timeoutId = window.setTimeout(() => controller.abort(), contextRequestTimeoutMs);
  return fetch(input, { ...init, signal: controller.signal }).finally(() => window.clearTimeout(timeoutId));
};

// Global fetch wrapper: redirect to login on 401 Unauthorized
if (typeof window !== "undefined") {
  const __origFetch = window.fetch.bind(window);
  window.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    const resp = await __origFetch(input, init);
    if (resp.status === 401) {
      // determine request url (string)
      let reqUrl = "";
      try {
        if (typeof input === "string") reqUrl = input;
        else if (input instanceof Request) reqUrl = input.url;
        else reqUrl = String(input);
      } catch {}
      // 401 ที่แปลว่า session หมดอายุต้องมาจาก QA Hub API เท่านั้น — ไม่ล้าง token เพราะ 401 จากบริการอื่น,
      // จากการ login เอง หรือจากลิงก์แชร์ Dashboard ที่หมดอายุ (endpoint anonymous)
      if (!isApiRequest(reqUrl) || reqUrl.includes("/auth/login") || reqUrl.includes("/dashboard/shared")) return resp;
      try { localStorage.removeItem("qa.accessToken"); localStorage.removeItem("qa.user"); } catch {}
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
  "my-work": "QA.MYWORK.VIEW",
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
  automation: "AUTOMATION.VIEW",
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

function Login({ onLogin }: { onLogin: (user: SessionUser) => void }) {
  const [username, setUsername] = useState(""),
    [password, setPassword] = useState(""),
    [rememberMe, setRememberMe] = useState(false),
    [showPassword, setShowPassword] = useState(false),
    [error, setError] = useState(""),
    [loading, setLoading] = useState(false),
    // Shown after the credentials check succeeds, for a beat before the
    // dashboard actually mounts — without this the app used to jump
    // straight from the login form to the dashboard with no transition.
    [entering, setEntering] = useState(false);
  const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError("");
    setLoading(true);
    try {
      // Race the real request against a minimum delay so the "กำลังเข้าสู่ระบบ..."
      // state is always visible for a moment, even when the API responds
      // instantly (e.g. local dev) — a flash of loading state feels broken.
      const [response] = await Promise.all([
        fetch(`${apiUrl}/auth/login`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ username, password, rememberMe: rememberMe }),
        }),
        wait(900),
      ]);
      if (!response.ok) throw new Error("ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง");
      const data = await response.json();
      localStorage.setItem("qa.accessToken", data.accessToken);
      localStorage.setItem("qa.user", JSON.stringify(data.user));
      setLoading(false);
      setEntering(true);
      await wait(1800);
      onLogin(data.user);
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : "ไม่สามารถเชื่อมต่อระบบได้");
      setLoading(false);
    }
  };
  if (entering) {
    return (
      <div className="login-page">
        <div className="app-loading-screen">
          <div className="login-logo"><span className="login-logo-text">QA</span></div>
          <div className="loading-bar"><span aria-hidden="true" /></div>
          <p>กำลังเตรียม QA Workspace...</p>
        </div>
      </div>
    );
  }
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
          <div className="login-password-wrap">
            <input
              type={showPassword ? "text" : "password"}
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              placeholder="Password"
              style={{ paddingRight: 62 }}
            />
            <button
              type="button"
              className="login-password-toggle"
              aria-label={showPassword ? "ซ่อนรหัสผ่าน" : "แสดงรหัสผ่าน"}
              aria-pressed={showPassword}
              onClick={() => setShowPassword((v) => !v)}
            >
              {showPassword ? "ซ่อน" : "แสดง"}
            </button>
          </div>
        </label>
        <label className="remember">
          <input type="checkbox" checked={rememberMe} onChange={(e) => setRememberMe(e.target.checked)} /> จดจำการเข้าสู่ระบบ
        </label>
        <button className="btn primary login-button" disabled={loading}>
          {loading ? <><span className="spinner inline" aria-hidden="true" /> กำลังเข้าสู่ระบบ...</> : "เข้าสู่ระบบ"}
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
    [sidebarCollapsed, setSidebarCollapsed] = useState(() => localStorage.getItem("qa.sidebar.collapsed") === "true"),
    [search, setSearch] = useState(""),
    [modal, setModal] = useState(false),
    [showBackToTop, setShowBackToTop] = useState(false);
  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === "Escape") setMenu(false); };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, []);
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
    [blockerCount, setBlockerCount] = useState(0),
    [contextLoading, setContextLoading] = useState(false);
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
  useEffect(() => {
    if (contextProjectId) localStorage.setItem("qa.context.project", contextProjectId);
  }, [contextProjectId]);
  useEffect(() => {
    if (!contextProjectId || !contextReleaseId) return;
    localStorage.setItem("qa.context.release", contextReleaseId);
    localStorage.setItem(contextReleaseStorageKey(contextProjectId), contextReleaseId);
  }, [contextProjectId, contextReleaseId]);
  useEffect(() => {
    if (!contextReleaseId || !contextBuildId) return;
    localStorage.setItem("qa.context.build", contextBuildId);
    localStorage.setItem(contextBuildStorageKey(contextReleaseId), contextBuildId);
  }, [contextReleaseId, contextBuildId]);
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
    setContextLoading(true);
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetchContextData(`${apiUrl}/projects`, { headers: h })
      .then((response) => okJsonOrEmpty<ProjectItem[]>(response, "รายการ Project"))
      .then((data: ProjectItem[]) => {
        const active = data.filter((x) => x.isActive);
        setContextProjects(active);
        setContextProjectId((current) =>
          active.some((x) => x.projectId === current)
            ? current
            : (active[0]?.projectId ?? ""),
        );
      })
      .catch(() => {
        setContextProjects([]);
        setContextProjectId("");
      })
      .finally(() => setContextLoading(false));
  }, [user, refresh]);
  useEffect(() => {
    if (!contextProjectId) {
      setContextLoading(false);
      setContextReleases([]);
      setContextReleaseId("");
      return;
    }
    setContextLoading(true);
    setContextReleases([]);
    setContextBuilds([]);
    setContextReleaseId("");
    setContextBuildId("");
    localStorage.setItem("qa.context.project", contextProjectId);
    const savedReleaseId =
      localStorage.getItem(contextReleaseStorageKey(contextProjectId)) ??
      localStorage.getItem("qa.context.release");
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetchContextData(`${apiUrl}/projects/${contextProjectId}/releases`, { headers: h })
      .then((response) => okJsonOrEmpty<ReleaseItem[]>(response, "รายการ Release"))
      .then((data: ReleaseItem[]) => {
        const active = data.filter((x) => x.status !== "Cancelled");
        setContextReleases(active);
        setContextReleaseId((current) => {
          const preferred = current || savedReleaseId || "";
          return active.some((x) => x.releaseId === preferred)
            ? preferred
            : (active[0]?.releaseId ?? "");
        });
      })
      .catch(() => {
        setContextReleases([]);
        setContextReleaseId("");
      })
      .finally(() => setContextLoading(false));
  }, [contextProjectId, refresh]);
  useEffect(() => {
    if (!contextReleaseId) {
      setContextLoading(false);
      setContextBuilds([]);
      setContextBuildId("");
      return;
    }
    setContextLoading(true);
    setContextBuilds([]);
    setContextBuildId("");
    localStorage.setItem("qa.context.release", contextReleaseId);
    if (contextProjectId) {
      localStorage.setItem(contextReleaseStorageKey(contextProjectId), contextReleaseId);
    }
    const savedBuildId =
      localStorage.getItem(contextBuildStorageKey(contextReleaseId)) ??
      localStorage.getItem("qa.context.build");
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetchContextData(`${apiUrl}/releases/${contextReleaseId}/builds`, { headers: h })
      .then((response) => okJsonOrEmpty<BuildItem[]>(response, "รายการ Build"))
      .then((data: BuildItem[]) => {
        const active = data.filter((x) => x.isActive);
        setContextBuilds(active);
        setContextBuildId((current) => {
          const preferred = current || savedBuildId || "";
          return active.some((x) => x.buildId === preferred)
            ? preferred
            : (active[0]?.buildId ?? "");
        });
      })
      .catch(() => {
        setContextBuilds([]);
        setContextBuildId("");
      })
      .finally(() => setContextLoading(false));
  }, [contextProjectId, contextReleaseId, refresh]);
  useEffect(() => {
    if (!contextBuildId) {
      setBlockerCount(0);
      return;
    }
    setBlockerCount(0);
    localStorage.setItem("qa.context.build", contextBuildId);
    if (contextReleaseId) {
      localStorage.setItem(contextBuildStorageKey(contextReleaseId), contextBuildId);
    }
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    fetch(`${apiUrl}/builds/${contextBuildId}/blocked-count`, { headers: h })
      .then((response) => (response.ok ? response.json() : { count: 0 }))
      .then((data: { count: number }) => setBlockerCount(data.count));
  }, [contextReleaseId, contextBuildId, refresh]);
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
        ? "สถานะคุณภาพและความพร้อมใช้งาน"
        : page === "automation"
          ? "สร้างและจัดการ Automation Case, DSL, Action Library, Agent และติดตามผลการรัน"
        : page === "settings"
          ? "จัดการค่ากลางและบริการ AI ที่ทุกระบบใช้งานร่วมกัน"
        : page === "audit"
          ? "ตรวจสอบประวัติการเปลี่ยนแปลงและกิจกรรมในระบบ"
        : page === "test-suites"
          ? "Test Suite จัดเก็บระดับ Project · เลือก Release/Build ตอนนำไปสร้าง Test Cycle"
        : `จัดการข้อมูล ${pageNames[page]} ของ Release ปัจจุบัน`,
    [page],
  );
  const go = (id: Page) => {
    setPage(id);
    setMenu(false);
  };
  const openRegressionCycle=(target:"test-cycles"|"execution",cycleId:string)=>{localStorage.setItem("qa.targetCycleId",cycleId);go(target)};
  // ปุ่มสร้าง Test Cycle แบบด่วนจากหน้า Test Suite — ฝาก Project/Suite ที่จะ prefill ไว้ใน localStorage
  // แล้วพาไปหน้า Test Cycle ซึ่งจะเปิดฟอร์มสร้างพร้อมข้อมูลนี้ทันที (ดู useEffect ใน TestCyclesPage)
  const createCycleFromSuite=(projectId:string,testSuiteId:string)=>{localStorage.setItem("qa.createCycleFromSuite",JSON.stringify({projectId,testSuiteId}));go("test-cycles")};
  // ปุ่ม "ดูรายละเอียด" บน Test Case ที่เชื่อมโยงกับ Defect — ฝาก id ไว้แล้วพาไปหน้า Test Case ซึ่งจะเปิด
  // detail ของ Test Case นั้นให้ทันที (ดู useEffect ใน TestCasesPage)
  const openTestCase=(testCaseId:string)=>{localStorage.setItem("qa.targetTestCaseId",testCaseId);go("test-cases")};
  const logout = () => {
    localStorage.removeItem("qa.accessToken");
    localStorage.removeItem("qa.user");
    setUser(null);
  };
  // CRM แยกงานตามคนที่ login จริง — แต่ละ user จัดการบัญชี CRM ของตัวเองที่นี่ (self-service, ไม่ใช่ Service
  // Account กลางที่ Admin ตั้งให้ทุกคนแล้ว) ปุ่มเปิด modal นี้อยู่ข้างๆ ปุ่ม logout ใน topbar
  const [myCrmOpen, setMyCrmOpen] = useState(false);
  const [myCrmConfig, setMyCrmConfig] = useState<{ merchantId: string; username: string; hasPassword: boolean; passwordHint?: string | null; isEnabled: boolean }>({ merchantId: "", username: "", hasPassword: false, isEnabled: true });
  const [myCrmPassword, setMyCrmPassword] = useState("");
  const [savingMyCrm, setSavingMyCrm] = useState(false);
  const openMyCrmModal = async () => {
    setMyCrmOpen(true);
    setMyCrmPassword("");
    try {
      const response = await fetch(`${apiUrl}/auth/me/crm`, { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` } });
      if (response.ok) setMyCrmConfig(await response.json());
    } catch {}
  };
  const saveMyCrmConfig = async () => {
    setSavingMyCrm(true);
    try {
      const response = await fetch(`${apiUrl}/auth/me/crm`, { method: "PUT", headers: { "Content-Type": "application/json", Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }, body: JSON.stringify({ merchantId: myCrmConfig.merchantId, username: myCrmConfig.username, password: myCrmPassword || null, isEnabled: myCrmConfig.isEnabled, clearPassword: false }) });
      if (!response.ok) { const problem = await response.json(); throw new Error(problem.detail ?? "บันทึกบัญชี CRM ไม่สำเร็จ"); }
      setMyCrmConfig(await response.json());
      setMyCrmPassword("");
      notify("บันทึกบัญชี CRM ของคุณเรียบร้อยแล้ว", "success");
    } catch (error) { notify(error instanceof Error ? error.message : "บันทึกบัญชี CRM ไม่สำเร็จ", "error"); }
    finally { setSavingMyCrm(false); }
  };
  const shareDashboard = async () => {
    try {
      // ลิงก์แชร์เปิดให้คนนอกอ่านได้ จึงต้องผูกกับ Project เดียวเสมอ (server ปฏิเสธลิงก์แบบทุก Project)
      if (!contextProjectId) { notify("กรุณาเลือก Project ที่ Topbar ก่อนสร้างลิงก์แชร์ Dashboard", "error"); return; }
      const selectedProject = contextProjects.find(x => x.projectId === contextProjectId);
      const shareReleaseId = contextReleaseId || contextReleases[0]?.releaseId || "";
      const selectedRelease = contextReleases.find(x => x.releaseId === shareReleaseId);
      const shareBuildId = contextBuildId || contextBuilds[0]?.buildId || "";
      const selectedBuild = contextBuilds.find(x => x.buildId === shareBuildId);
      const scopeMessage = [`Project: ${selectedProject?.projectName ?? "ทุก Project"}`, `Release: ${selectedRelease ? `${selectedRelease.releaseCode} · ${selectedRelease.version}` : "ทุก Release"}`, `Build: ${selectedBuild?.buildNumber ?? "ทุก Build"}`].join("\n");
      if (!await confirmDialog(`กำลังจะสร้างลิงก์แชร์ Dashboard ด้วยข้อมูลนี้:\n\n${scopeMessage}\n\nลิงก์มีอายุ 90 วัน ต้องการดำเนินการต่อหรือไม่?`)) return;
      const response = await fetch(`${apiUrl}/dashboard/share`, { method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }, body: JSON.stringify({ projectId: contextProjectId || null, releaseId: shareReleaseId || null, buildId: shareBuildId || null, validHours: 24 * 90 }) });
      if (!response.ok) { const problem = await response.json().catch(() => null); throw new Error(problem?.detail ?? "ไม่สามารถสร้างลิงก์แชร์ได้"); }
      const result: { code: string; expiresAt: string } = await response.json();
      const url = `${window.location.origin}${window.location.pathname}?s=${encodeURIComponent(result.code)}`;
      const copied = await copyText(url);
      if (copied) notify(`คัดลอกลิงก์ Dashboard แบบอ่านอย่างเดียวแล้ว\nลิงก์หมดอายุ ${formatThaiDateTime(result.expiresAt)}`, "success");
      else await promptDialog({ title: "คัดลอกลิงก์ Dashboard", message: "เบราว์เซอร์ไม่อนุญาตให้คัดลอกอัตโนมัติ — เลือกข้อความแล้วกด Ctrl+C", initialValue: url, confirmLabel: "ปิด" });
    } catch (e) { notify(e instanceof Error ? e.message : "ไม่สามารถสร้างลิงก์แชร์ได้", "error"); }
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
      notify(e instanceof Error ? e.message : "บันทึกไม่สำเร็จ", "error");
    } finally {
      setSaving(false);
    }
  };
  if (shareCode || shareToken) return <div className="shared-dashboard"><header><div className="logo">QA</div><div><b>ProMaxx2 QA Hub</b><small>Executive Read-only Report</small></div><Badge tone="blue">READ ONLY</Badge></header><main><Suspense fallback={pageLoading}><Dashboard shareCode={shareCode} shareToken={shareToken} /></Suspense></main><footer>ข้อมูลสำหรับการบริหารจัดการ • ไม่สามารถแก้ไขข้อมูลจากหน้านี้</footer></div>;
  if (!user) return <Login onLogin={(u) => { localStorage.removeItem("qa.activePage"); setPage("dashboard"); setUser(u); }} />;
  const can = (permission: string) =>
    user.roles.includes("SYS_ADMIN") || user.permissions.includes(permission);
  const canCreate =
    editPermission[page] !== undefined && can(editPermission[page]!);
  return (
    <div className={`${menu ? "app menu-open" : "app menu-closed"}${sidebarCollapsed ? " sidebar-is-collapsed" : ""}`}>
      <aside className={`${menu ? "sidebar open" : "sidebar"}${sidebarCollapsed ? " collapsed" : ""}`}>
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
                  title={sidebarCollapsed ? i.label : undefined}
                  data-tooltip={sidebarCollapsed ? i.label : undefined}
                  aria-label={i.label}
                  onClick={() => go(i.id)}
                >
                  <i className="material-symbols-outlined" aria-hidden="true">{i.icon}</i>
                  {i.label}
                </button>
              ))}
            </div>
          ) : null;
        })}
      </aside>
      {menu && !sidebarCollapsed && <button className="sidebar-backdrop" type="button" aria-label="ปิดเมนู" onClick={() => setMenu(false)} />}
      <main onScroll={(event) => setShowBackToTop(event.currentTarget.scrollTop > 480)}>
        <header className="topbar qa-topbar">
          <button className="menu-btn topbar-menu" aria-label={sidebarCollapsed ? "ขยายเมนู" : "ย่อเมนู"} title={sidebarCollapsed ? "ขยายเมนู" : "ย่อเมนู"} onClick={() => { if (window.matchMedia("(max-width: 900px)").matches) setMenu((v) => !v); else { const next = !sidebarCollapsed; setSidebarCollapsed(next); localStorage.setItem("qa.sidebar.collapsed", String(next)); } }}>
            <span aria-hidden="true">☰</span>
          </button>
          {!["projects","users","settings","system-monitor"].includes(page) && <div className="context">
            {contextLoading && <span className="context-loading" role="status">กำลังเปลี่ยน Context...</span>}
            <label className="context-field"><span>Project</span><select
              value={contextProjectId}
              onChange={(e) => { setContextLoading(true); setContextProjectId(e.target.value); }}
              aria-label="Project ปัจจุบัน"
              disabled={contextLoading}
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
              onChange={(e) => { setContextLoading(true); setContextReleaseId(e.target.value); }}
              aria-label="Release ปัจจุบัน"
              disabled={contextLoading || !contextReleases.length}
            >
              {!contextReleases.length && (
                <option value="">ไม่มี Release</option>
              )}
              {contextReleases.map((x) => (
                <option key={x.releaseId} value={x.releaseId}>
                  Release {x.releaseCode}{x.version ? ` · ${x.version}` : ""}
                </option>
              ))}
            </select></label>
            <label className="context-field"><span>Build</span><select
              value={contextBuildId}
              onChange={(e) => setContextBuildId(e.target.value)}
              aria-label="Build ปัจจุบัน"
              disabled={contextLoading || !contextBuilds.length}
            >
              {!contextBuilds.length && <option value="">ไม่มี Build</option>}
              {contextBuilds.map((x) => (
                <option key={x.buildId} value={x.buildId}>
                  Build {x.buildNumber}{x.applicationVersion ? ` · App ${x.applicationVersion}` : ""}
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
            <button className="table-action icon-only" aria-label="บัญชี CRM ของฉัน" title="บัญชี CRM ของฉัน" onClick={openMyCrmModal}><span aria-hidden="true">⇪</span></button>
            <button className="logout" aria-label="ออกจากระบบ" title="ออกจากระบบ" onClick={logout}><span aria-hidden="true">↪</span><span>ออกจากระบบ</span></button>
          </div>
        </header>
        {myCrmOpen && (
          <ModalShell labelledBy="my-crm-title" onDismiss={() => { if (!savingMyCrm) setMyCrmOpen(false); }}>
              <div className="modal-head">
                <h2 id="my-crm-title">บัญชี CRM ของฉัน</h2>
                <button aria-label="ปิด" disabled={savingMyCrm} onClick={() => setMyCrmOpen(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
              </div>
              <p>Login ของคุณเข้า CRM (BlueSea Helpdesk) — ใช้ข้อมูลเดียวกับที่ Login เข้า BlueID (Employee) ของคุณเอง ใช้ตอนกด "ส่งไป CRM"/"เปลี่ยนผู้รับผิดชอบ CRM"/คอมเมนต์ที่ sync ไป CRM</p>
              <div className="master-ai-form">
                <label>Merchant ID<input value={myCrmConfig.merchantId} onChange={(e) => setMyCrmConfig((current) => ({ ...current, merchantId: e.target.value }))} placeholder="เช่น 10000001 (เลข 8 หลัก ตามที่ BlueID กำหนด)" /></label>
                <label>Username (รหัสพนักงาน)<input value={myCrmConfig.username} onChange={(e) => setMyCrmConfig((current) => ({ ...current, username: e.target.value }))} placeholder="เช่น 6101" /></label>
                <label>Password<input type="password" autoComplete="new-password" value={myCrmPassword} onChange={(e) => setMyCrmPassword(e.target.value)} placeholder={myCrmConfig.hasPassword ? "ตั้งค่าแล้ว — เว้นว่างเพื่อใช้ค่าเดิม" : "กรอก Password สำหรับ Login เข้า CRM"} /></label>
                <label className="master-ai-toggle"><input type="checkbox" checked={myCrmConfig.isEnabled} onChange={(e) => setMyCrmConfig((current) => ({ ...current, isEnabled: e.target.checked }))} /><span>เปิดใช้งานการเชื่อมต่อ CRM ของฉัน</span></label>
              </div>
              <div className="modal-actions">
                <small className="master-ai-hint">Password ถูกเข้ารหัสและเก็บเฉพาะฝั่ง Server</small>
                <button className="btn primary" disabled={savingMyCrm || !myCrmConfig.merchantId.trim() || !myCrmConfig.username.trim() || (!myCrmConfig.hasPassword && !myCrmPassword.trim())} onClick={saveMyCrmConfig}>
                  {savingMyCrm ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึกการตั้งค่า</>}
                </button>
              </div>
            </ModalShell>
        )}
        <div className="content">
          <div className="page-head">
            <div>
              <h1>{pageNames[page]}</h1>
              <p>{description}</p>
            </div>
            <div className="actions">
              {page !== "audit" && <label className="search">
                ⌕
                <input
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="ค้นหา..."
                />
              </label>}
              {/* ปุ่ม Export กลางถูกนำออก (ไม่เคยมี onClick) — แต่ละหน้ามีปุ่ม Export ของตัวเอง เช่น Defect, Test Cycle, RTM, Automation */}
              {page === "dashboard" && <button className="btn share-btn" onClick={shareDashboard}>↗ แชร์ Dashboard</button>}
              {page === "requirements"&&can("REQUIREMENT.EDIT")&&<button className="btn ai-button" onClick={()=>{setCreateProjectId(contextProjectId);setCreateModuleId("");setCreateReleaseId(contextReleaseId);setRequirementAiPrompt("");setRequirementAiFiles([]);setRequirementAiError("");setRequirementAiModal(true)}}><span className="material-symbols-outlined" aria-hidden="true">auto_awesome</span> AI Generate</button>}
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
          <div className="page-transition" key={page}>
          <Suspense fallback={pageLoading}>
          {page === "dashboard" ? (
            <Dashboard projectId={contextProjectId} releaseId={contextReleaseId} buildId={contextBuildId} projectName={contextProjects.find(x => x.projectId === contextProjectId)?.projectName} releaseLabel={contextReleases.find(x => x.releaseId === contextReleaseId)?.releaseCode ? `${contextReleases.find(x => x.releaseId === contextReleaseId)?.releaseCode} · ${contextReleases.find(x => x.releaseId === contextReleaseId)?.version}` : undefined} buildLabel={contextBuilds.find(x => x.buildId === contextBuildId)?.buildNumber} />
          ) : page === "my-work" ? (
            <MyWorkPage user={user} onOpenExecution={(cycleId) => { localStorage.setItem("qa.targetCycleId", cycleId); go("execution"); }} onNavigate={go} />
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
            <RegressionPage projectId={contextProjectId} releaseId={contextReleaseId} buildId={contextBuildId} search={search} canEdit={can("REGRESSION.MANAGE")} canRunAutomation={can("AUTOMATION.EXECUTE") || can("EXECUTION.RUN")} onOpenCycle={openRegressionCycle} />
          ) : page === "automation" ? (
            <Suspense fallback={pageLoading}><AutomationPage projectId={contextProjectId} releaseId={contextReleaseId} buildId={contextBuildId} canView={can("AUTOMATION.VIEW")} canEdit={can("AUTOMATION.EDIT")} canValidate={can("AUTOMATION.VALIDATE")} canApprove={can("AUTOMATION.APPROVE")} canRun={can("AUTOMATION.EXECUTE") || can("EXECUTION.RUN")} canManage={can("AUTOMATION.MANAGE")} canViewEvidence={can("AUTOMATION.VIEWEVIDENCE")} canGenerateAi={can("AUTOMATION.GENERATEAI")} canCreateDefect={can("DEFECT.EDIT")} /></Suspense>
          ) : page === "users" ? (
            <AdministrationPage refresh={refresh} allProjects={contextProjects} />
          ) : page === "settings" ? (
            <MasterSettingsPage />
          ) : page === "system-monitor" ? (
            <SystemMonitorPage />
          ) : page === "defects" ? (
            <DefectsPage projectId={contextProjectId} releaseId={contextReleaseId} buildId={contextBuildId} projectName={contextProjects.find(x => x.projectId === contextProjectId)?.projectName} releaseLabel={contextReleases.find(x => x.releaseId === contextReleaseId)?.releaseCode} buildLabel={contextBuilds.find(x => x.buildId === contextBuildId)?.buildNumber} search={search} onClearSearch={() => setSearch("")} canEdit={can("DEFECT.EDIT")} canExport={can("REPORT.EXPORT")} onOpenTestCase={openTestCase} />
          ) : page === "summary" ? (
            <Suspense fallback={pageLoading}><TestSummaryPage projects={contextProjects} projectId={contextProjectId} releaseId={contextReleaseId} buildId={contextBuildId} canExport={can("REPORT.EXPORT")} onOpenRisks={() => setPage("risks")} onOpenSignoff={() => setPage("signoff")} /></Suspense>
          ) : page === "risks" ? (
            <Suspense fallback={pageLoading}><RiskAcceptancePage projectId={contextProjectId} releaseId={contextReleaseId} canEdit={can("PROJECT.EDIT")} canApprove={can("RISK.APPROVE")} /></Suspense>
          ) : page === "signoff" ? (
            <Suspense fallback={pageLoading}><ReleaseSignoffPage projectId={contextProjectId} releaseId={contextReleaseId} canSignoff={can("RELEASE.SIGNOFF")} /></Suspense>
          ) : page === "audit" ? (
            <Suspense fallback={pageLoading}><AuditLogPage /></Suspense>
          ) : page === "execution" ? (
            <ExecutionWorkspacePage contextProjectId={contextProjectId} contextReleaseId={contextReleaseId} contextBuildId={contextBuildId} />
          ) : page === "test-cycles" ? (
            <TestCyclesPage search={search} canEdit={can("EXECUTION.ASSIGN")} canExport={can("REPORT.EXPORT")} contextProjectId={contextProjectId} contextReleaseId={contextReleaseId} contextBuildId={contextBuildId} />
          ) : (
            <TestSuitesPage search={search} canEdit={can("TESTCASE.EDIT")} contextProjectId={contextProjectId} onOpenCycle={openRegressionCycle} onCreateCycle={createCycleFromSuite} />
          )}
          </Suspense>
          </div>
        </div>
        {showBackToTop && (
          <button
            type="button"
            className="back-to-top"
            aria-label="กลับไปด้านบน"
            title="กลับไปด้านบน"
            onClick={(event) => event.currentTarget.closest("main")?.scrollTo({ top: 0, behavior: "smooth" })}
          >
            <span className="material-symbols-outlined" aria-hidden="true">arrow_upward</span>
          </button>
        )}
      </main>
      {requirementAiModal&&<ModalShell labelledBy="requirement-ai-title" className="requirement-ai-modal" boxStyle={{position:"relative"}} onDismiss={() => { if (!requirementAiGenerating) setRequirementAiModal(false); }}>{requirementAiGenerating&&<div className="ai-loading-overlay"><div className="ai-spinner"/><p>AI กำลังวิเคราะห์ Requirement...</p><small>รอสักครู่ ระบบกำลังประมวลผลข้อมูลและไฟล์แนบ</small></div>}
          <div className="modal-head"><div><h2 id="requirement-ai-title">AI Generate Requirement</h2><small>สร้าง Draft จากคำอธิบายและไฟล์อ้างอิง</small></div><button aria-label="ปิดหน้าต่าง AI Generate" disabled={requirementAiGenerating} onClick={()=>setRequirementAiModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
          <div className="requirement-ai-panel">
            <div className="requirement-ai-head"><div><span className="ai-spark" aria-hidden="true">AI</span><div><h3>ข้อมูลอ้างอิง</h3><p>เลือกบริบทให้ AI สร้าง Requirement ได้ตรงกับระบบ</p></div></div><span className="ai-review-badge">ต้องตรวจสอบก่อนใช้</span></div>
            <div className="form-grid">
              <label>Project<select value={createProjectId} onChange={e=>{setCreateProjectId(e.target.value);setCreateModuleId("");setCreateReleaseId("")}}>{contextProjects.map(x=><option key={x.projectId} value={x.projectId}>{x.projectName}</option>)}</select></label>
              <label>Module<select value={createModuleId} onChange={e=>setCreateModuleId(e.target.value)}><option value="">เลือก Module</option>{renderModuleSelectOptions(createModules.filter(x=>x.isActive))}</select></label>
              <label className="full">Release<select value={createReleaseId} onChange={e=>setCreateReleaseId(e.target.value)}><option value="">ไม่ระบุ Release</option>{createReleases.sort((a,b)=>a.releaseCode.localeCompare(b.releaseCode)).map(x=><option key={x.releaseId} value={x.releaseId}>{x.releaseCode} · Version {x.version}</option>)}</select></label>
              <label className="full">อธิบายความต้องการ<textarea rows={5} value={requirementAiPrompt} onChange={e=>setRequirementAiPrompt(e.target.value)} placeholder="เช่น ผู้ใช้ต้องเห็นยอดขายวันนี้ จำนวนบิล และสินค้าใกล้หมดบน Dashboard หลัง Login" maxLength={4000} autoFocus/><small>{requirementAiPrompt.length.toLocaleString()} / 4,000 ตัวอักษร</small></label>
              <div className="ai-attachments full">
                <div><b>ไฟล์สำหรับให้ AI วิเคราะห์เพิ่มเติม</b><small>PDF, Word, Excel, CSV, TXT, Markdown หรือรูปภาพ · สูงสุด 5 ไฟล์ รวม 20 MB</small></div>
                <label className="ai-file-picker"><span className="material-symbols-outlined" aria-hidden="true">add</span> เลือกไฟล์<input type="file" multiple accept=".pdf,.txt,.md,.csv,.docx,.xlsx,.png,.jpg,.jpeg,.webp" disabled={requirementAiGenerating||requirementAiFiles.length>=5} onChange={e=>{const selected=Array.from(e.target.files??[]);const next=[...requirementAiFiles,...selected].slice(0,5);if(next.reduce((sum,file)=>sum+file.size,0)>20_000_000)setRequirementAiError("ขนาดไฟล์รวมต้องไม่เกิน 20 MB");else{setRequirementAiFiles(next);setRequirementAiError("")}e.target.value=""}}/></label>
                {requirementAiFiles.length>0&&<div className="ai-file-list">{requirementAiFiles.map((file,index)=><div key={`${file.name}-${file.lastModified}`}><span className="material-symbols-outlined" aria-hidden="true">description</span><p><b>{file.name}</b><small>{(file.size/1024/1024).toFixed(2)} MB</small></p><button type="button" aria-label={`ลบไฟล์ ${file.name}`} disabled={requirementAiGenerating} onClick={()=>setRequirementAiFiles(files=>files.filter((_,i)=>i!==index))}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>)}</div>}
              </div>
            </div>
            {requirementAiError&&<div className="login-error" role="alert">{requirementAiError}</div>}
            <div className="ai-draft-note"><span className="material-symbols-outlined" aria-hidden="true">info</span><p><b>AI จะไม่บันทึกข้อมูลหรือไฟล์แนบอัตโนมัติ</b><small>ไฟล์ใช้วิเคราะห์ในคำขอนี้เท่านั้น จากนั้นระบบจะเปิดฟอร์มพร้อม Draft ให้ตรวจสอบ</small></p></div>
          </div>
          <div className="modal-actions"><button className="btn" disabled={requirementAiGenerating} onClick={()=>setRequirementAiModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button><button className="btn ai-button" disabled={requirementAiGenerating||!requirementAiPrompt.trim()||!createProjectId||!createModuleId} onClick={generateRequirementWithAi}>{requirementAiGenerating?"กำลังวิเคราะห์...":"✦ สร้าง Draft ด้วย AI"}</button></div>
        </ModalShell>}
      {modal && (
        <ModalShell boxClassName={`modal-box ${page === "requirements" ? "requirement-editor" : ""}`} onDismiss={() => setModal(false)}>
            <div className="modal-head">
              <h2>สร้าง {pageNames[page]}</h2>
              <button onClick={() => setModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
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
                {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก</>}
              </button>
            </div>
          </ModalShell>
      )}
    </div>
  );
}

export default App;
