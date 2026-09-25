// ของที่หลายหน้าใช้ร่วมกัน (type, helper, ค่าคงที่) — แยกออกจาก App.tsx เพื่อให้แต่ละหน้าอยู่ไฟล์ของตัวเอง
import { type ReactElement, useState, useEffect } from "react";
import { notify } from "../components/dialogStore";
import { apiUrl } from "../api";
import { toUtcDate } from "../dateTime";

export type Page =
  | "dashboard"
  | "my-work"
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
  | "automation"
  | "summary"
  | "risks"
  | "signoff"
  | "users"
  | "settings"
  | "system-monitor"
  | "audit";
export type SessionUser = {
  userId: string;
  username: string;
  displayName: string;
  roles: string[];
  permissions: string[];
  assignedProjectIds: string[];
};

export function moduleTreeComparator(a: ModuleItem, b: ModuleItem): number {
  return (a.sortOrder ?? 999) - (b.sortOrder ?? 999) || (a.moduleCode ?? "").localeCompare(b.moduleCode ?? "");
}
export type ModuleTreeEntry = { module: ModuleItem; depth: number };
export function buildModuleTree(modules: ModuleItem[]): ModuleTreeEntry[] {
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
export function renderModuleSelectOptions(modules: ModuleItem[]): ReactElement[] {
  return buildModuleTree(modules).map(({ module, depth }) => (
    <option key={module.moduleId} value={module.moduleId} className={depth === 0 ? "module-root-option" : "module-child-option"}>
      {depth ? `${"　".repeat(depth)}└ ` : "▾ "}{module.moduleCode ? `${module.moduleCode} · ` : ""}{module.moduleName}
    </option>
  ));
}
// ค่าเริ่มต้นของ Filter "ผู้สร้าง" ในหน้า Test Case/Suite/Cycle — ตั้งเป็น User ที่ login อยู่
export function currentUserId(): string {
  try { return (JSON.parse(localStorage.getItem("qa.user") ?? "{}") as SessionUser).userId ?? ""; }
  catch { return ""; }
}

/** UI รอบ 4: dropdown ที่โหลดไม่สำเร็จยังคงเป็นรายการว่างเหมือนเดิม แต่ต้องแจ้งผู้ใช้ (เดิมเงียบ ดูเหมือนไม่มีข้อมูลจริง) */
export async function okJsonOrEmpty<T = unknown[]>(response: Response, what: string, empty: T = [] as unknown as T): Promise<T> {
  if (response.ok) return response.json() as Promise<T>;
  notify(`โหลด${what}ไม่สำเร็จ (${response.status}) — ตัวเลือกอาจไม่ครบ`, "error");
  return empty;
}
export type MasterOption = { masterOptionId: string; category: string; value: string; displayName: string; sortOrder: number; isActive: boolean };
export function useMasterOptions() {
  const [options, setOptions] = useState<MasterOption[]>([]);
  useEffect(() => {
    fetch(`${apiUrl}/master-settings`, { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` } })
      .then((r) => okJsonOrEmpty<MasterOption[]>(r, "ค่าตั้งต้น (Master Settings)"))
      .then((data: MasterOption[]) => setOptions(Array.isArray(data) ? data : []));
  }, []);
  return (category: string) => options.filter((x) => x.category === category && x.isActive);
}
export function masterOptionElements(options: MasterOption[], current: string) {
  return <>{current && !options.some((x) => x.value === current) && <option value={current}>{current} (ปิดใช้งาน)</option>}{options.map((x) => <option key={x.masterOptionId} value={x.value}>{x.displayName}</option>)}</>;
}

export async function copyText(text: string) {
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

export function nextBusinessCode(prefix: string, existingCodes: string[]) {
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

export const nav: {
  label: string;
  items: { id: Page; icon: string; label: string }[];
}[] = [
  {
    label: "ภาพรวม",
    items: [
      { id: "dashboard", icon: "dashboard", label: "Dashboard" },
      { id: "my-work", icon: "assignment_ind", label: "My Work" },
      { id: "projects", icon: "account_tree", label: "Project / Module" },
      { id: "releases", icon: "inventory_2", label: "Release / Build" },
    ],
  },
  {
    label: "REQUIREMENT & TEST DESIGN",
    items: [
      { id: "requirements", icon: "description", label: "Requirement" },
      { id: "rtm", icon: "account_tree", label: "RTM" },
      { id: "test-cases", icon: "fact_check", label: "Test Case" },
      { id: "test-suites", icon: "library_books", label: "Test Suite" },
    ],
  },
  {
    label: "TEST EXECUTION",
    items: [
      { id: "test-cycles", icon: "cycle", label: "Test Cycle" },
      { id: "execution", icon: "play_circle", label: "Execution Workspace" },
      { id: "defects", icon: "bug_report", label: "Defect" },
      { id: "regression", icon: "replay", label: "Regression" },
      { id: "automation", icon: "smart_toy", label: "Automation" },
    ],
  },
  {
    label: "RELEASE GOVERNANCE",
    items: [
      { id: "summary", icon: "summarize", label: "Test Summary" },
      { id: "risks", icon: "warning", label: "Risk Acceptance" },
      { id: "signoff", icon: "verified", label: "Release Sign-off" },
    ],
  },
  {
    label: "ADMINISTRATION",
    items: [
      { id: "users", icon: "manage_accounts", label: "User / Role" },
      { id: "settings", icon: "settings", label: "Setting Center" },
      { id: "system-monitor", icon: "monitor_heart", label: "System Monitor" },
      { id: "audit", icon: "manage_search", label: "Audit Log" },
    ],
  },
];
export const defectStatusTones: Record<string, string> = { Open: "yellow", "In Progress": "blue", Resolved: "green", Closed: "green", Rejected: "gray" };
// รูปแบบ DD/MM/YYYY HH:MM:SS แบบปี พ.ศ. (เช่น 28/08/2569 14:35:02) — เดิมใช้ d.getDate()/d.getHours()
// ฯลฯ ตรงๆ ซึ่งอ่านค่าตาม timezone ของเครื่อง (ถูกก็ต่อเมื่อเครื่องตั้งเป็นไทยพอดี) เปลี่ยนมาใช้ toUtcDate
// (เติม Z ให้ก่อนถ้า backend ส่งมาไม่มี) + Intl.DateTimeFormat บังคับ timeZone: Asia/Bangkok แทน เพื่อให้
// ได้ผลลัพธ์ถูกต้องเสมอไม่ว่าเครื่อง client จะตั้ง timezone เป็นอะไร (ใช้ locale "en-GB" ดึงเป็นเลขฐาน
// สากลก่อน แล้วค่อย +543 เอง เพราะ locale "th-TH" ของ Intl จะคืนปี พ.ศ. มาให้อยู่แล้วซึ่งจะกลายเป็นบวกซ้ำ)
export function fmtDateTimeBE(iso?: string | null): string {
  const d = toUtcDate(iso);
  if (!d) return "-";
  const parts = new Intl.DateTimeFormat("en-GB", {
    timeZone: "Asia/Bangkok", hour12: false, hourCycle: "h23",
    day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit", second: "2-digit",
  }).formatToParts(d);
  const get = (type: string) => parts.find(p => p.type === type)?.value ?? "00";
  return `${get("day")}/${get("month")}/${Number(get("year")) + 543} ${get("hour")}:${get("minute")}:${get("second")}`;
}

export type ModuleItem = {
  moduleId: string;
  projectId: string;
  parentModuleId?: string | null;
  moduleCode: string;
  moduleName: string;
  description?: string;
  isActive: boolean;
  sortOrder: number;
};
export type TestCaseItem = {
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
  createdAt?: string;
};

export type RtmLinkedCase = { testCaseId: string; testCaseCode: string; title: string; priority: string; testType?: string; status: string; revisionNo: number; coverageType?: string };
export type RtmItem = { requirementId: string; moduleId: string; moduleName: string; requirementCode: string; title: string; priority: string; testCaseCount: number; coverageStatus: string; status: string; testCases: RtmLinkedCase[] };
export type TestCycleItem = {
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
  modules?: { moduleId: string; moduleCode: string; moduleName: string }[];
  ownerUserId?: string;
  createdBy?: string;
  createdByName?: string;
  createdAt?: string;
  copiedFromTestCycleId?: string;
  copiedFromCycleCode?: string;
};
export type TestSuiteItem = {
  testSuiteId: string;
  projectId: string;
  suiteCode: string;
  suiteName: string;
  suiteType?: string;
  description?: string;
  riskTier?: string;
  isActive: boolean;
  cycleCount: number;
  createdBy?: string;
  createdByName?: string;
  createdAt?: string;
  modules?: { moduleId: string; moduleCode: string; moduleName: string }[];
  cases: {
    testCaseId: string;
    testCaseCode: string;
    title: string;
    priority: string;
    sortOrder: number;
    isRequired: boolean;
  }[];
  linkedCycles?: {
    testCycleId: string;
    cycleCode: string;
    cycleName: string;
    status: string;
    isDeleted: boolean;
    releaseCode?: string;
    releaseVersion?: string;
    buildNumber?: string;
    startDate?: string;
    endDate?: string;
    ownerName?: string;
    caseCount: number;
    executedCount: number;
    progressPercent: number;
  }[];
};
export type AdminUser = {
  userId: string;
  username: string;
  displayName: string;
  email?: string;
  isActive: boolean;
  lastLoginAt?: string;
  roles: string[];
  assignedProjectIds: string[];
};
