import { useState, useRef, useEffect, useMemo } from "react";
import type { ProjectItem, UserLookup } from "../shared/types";
import { apiUrl, isAbortError, getJson } from "../api";
import { useDebounced } from "../components/useDebounced";
import { notify, confirmDialog } from "../components/dialogStore";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
import { formatThaiDateTime } from "../dateTime";
import { type ModuleItem, type TestCycleItem, type TestSuiteItem, currentUserId, masterOptionElements, nextBusinessCode, okJsonOrEmpty, renderModuleSelectOptions, useMasterOptions } from "../shared/appShared";

const cycleStatusOptions = ["Draft", "InProgress", "Completed", "Closed", "Cancelled"];
// ไอคอนประกอบ Badge สถานะ/ประเภทใน Test Cycle detail modal — สถานะเป็น enum ตายตัว (cycleStatusOptions
// ด้านบน) เลย map ตรงๆ ได้ครบทุกค่า ส่วนประเภท (cycleType) มาจาก Master Setting ("TestCycleType") ที่แอดมิน
// เพิ่มค่าใหม่ได้เอง เลยต้องมี fallback ไอคอน generic ไว้เผื่อค่าที่ไม่ได้ map ไว้ล่วงหน้า
const cycleStatusIcons: Record<string, string> = { Draft: "draft", InProgress: "play_circle", Completed: "check_circle", Closed: "lock", Cancelled: "cancel" };
const cycleCaseStatusLabels: Record<string, string> = { Pass: "ผ่าน", Fail: "ไม่ผ่าน", Blocked: "ติดปัญหา", Skipped: "ข้าม", InProgress: "กำลังทำ", NotRun: "ยังไม่เริ่ม" };
const cycleCaseStatusTones: Record<string, string> = { Pass: "green", Fail: "red", Blocked: "yellow", Skipped: "gray", InProgress: "blue", NotRun: "gray" };
// ความหมายสถานะ Test Cycle (TestCycle.cs) — โชว์ใน Test Cycle detail modal เหมือนรูปแบบเดียวกับ
// requirementStatusInformation/testCaseStatusInfo ด้านล่าง
const cycleStatusInfo = [
  { value: "Draft", label: "ฉบับร่าง", meaning: "สร้าง Cycle และเลือก Test Case แล้ว แต่ยังไม่เริ่ม Assign หรือ Execute", impact: "แก้ไขรายการ Test Case ในรอบนี้ได้อิสระ" },
  { value: "InProgress", label: "กำลังทดสอบ", meaning: "อยู่ระหว่างทดสอบจริง ผู้ทดสอบบันทึกผลผ่าน Execution Workspace ได้", impact: "บันทึก Save Progress/Skip/Complete Test และสร้าง Defect ต่อ Step ได้ตามปกติ" },
  { value: "Completed", label: "ทดสอบครบแล้ว", meaning: "Execute ครบทุก Test Case ในรอบนี้แล้ว ไม่ได้แปลว่าผลทั้งหมดคือ Pass", impact: "หากยังมี Fail ที่รอ Fix/Retest ต้องเปิด Cycle ใหม่หรือกลับไป InProgress เพื่อ Retest" },
  { value: "Closed", label: "ปิดรอบแล้ว", meaning: "ปิดรอบทดสอบอย่างเป็นทางการ ถือเป็นผลสรุปสุดท้ายของรอบนี้", impact: "บันทึกหรือแก้ไขผล Execution เพิ่มไม่ได้อีก (read-only)" },
  { value: "Cancelled", label: "ยกเลิก", meaning: "ยกเลิกรอบทดสอบนี้ ไม่นำผลไปใช้อ้างอิง", impact: "บันทึกหรือแก้ไขผล Execution เพิ่มไม่ได้อีกเช่นเดียวกับ Closed" },
] as const;
const cycleTypeIcons: Record<string, string> = { Smoke: "local_fire_department", Regression: "sync", Sanity: "science", UAT: "person", Functional: "extension", Performance: "bolt" };
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
  applicationVersion?: string;
  isActive: boolean;
};
type CycleRelease = {
  releaseId: string;
  projectId: string;
  releaseCode: string;
  version?: string;
  status: string;
};
type TestCycleCaseSummary = {
  testCycleCaseId: string;
  testCaseId: string;
  testCaseCode: string;
  title: string;
  priority: string;
  currentStatus: string;
  executionOrder: number;
  moduleCode?: string | null;
  moduleName?: string | null;
  expectedResult?: string | null;
  actualResult?: string | null;
};
type GeneratedTestCycleDraft = { cycleName: string; cycleType: string; startDate?: string; endDate?: string; notes?: string; selectionSummary: string };
export function TestCyclesPage({ search, canEdit, canExport, contextProjectId, contextReleaseId, contextBuildId }: { search: string; canEdit: boolean; canExport: boolean; contextProjectId?: string; contextReleaseId?: string; contextBuildId?: string }) {
  const masterOptions = useMasterOptions(), cycleTypes = masterOptions("TestCycleType");
  const [items, setItems] = useState<TestCycleItem[]>([]),
    [projects, setProjects] = useState<ProjectItem[]>([]),
    [releases, setReleases] = useState<CycleRelease[]>([]),
    [builds, setBuilds] = useState<CycleBuild[]>([]),
    [environments, setEnvironments] = useState<CycleEnvironment[]>([]),
    [suites, setSuites] = useState<TestSuiteItem[]>([]),
    [users, setUsers] = useState<UserLookup[]>([]),
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
    [caseSummary, setCaseSummary] = useState({ totalCases: 0, executedCases: 0 }),
    [page, setPage] = useState(1),
    [pageSize, setPageSize] = useState(30),
    [listModuleFilter, setListModuleFilter] = useState(""),
    [listCycleTypeFilter, setListCycleTypeFilter] = useState(""),
    [listCreatedByFilter, setListCreatedByFilter] = useState(currentUserId),
    [listStatusFilter, setListStatusFilter] = useState(""),
    [statusCounts, setStatusCounts] = useState<Record<string, number>>({}),
    [listModules, setListModules] = useState<ModuleItem[]>([]),
    [cycleSelected, setCycleSelected] = useState<Set<string>>(new Set()),
    [cycleBulkStatus, setCycleBulkStatus] = useState(""),
    [cycleBulkSaving, setCycleBulkSaving] = useState(false),
    [cloneSource, setCloneSource] = useState<TestCycleItem | null>(null),
    [cloneReleaseId, setCloneReleaseId] = useState(""),
    [cloneBuildId, setCloneBuildId] = useState(""),
    [cloneEnvironmentId, setCloneEnvironmentId] = useState(""),
    [cloneMode, setCloneMode] = useState("SourceSnapshot"),
    [cloneCode, setCloneCode] = useState(""),
    [cloneName, setCloneName] = useState(""),
    [cloneCycleType, setCloneCycleType] = useState(""),
    [cloneStartDate, setCloneStartDate] = useState(""),
    [cloneEndDate, setCloneEndDate] = useState(""),
    [cloneOwnerUserId, setCloneOwnerUserId] = useState(""),
    [cloneNotes, setCloneNotes] = useState(""),
    [cloneSaving, setCloneSaving] = useState(false),
    [cloneError, setCloneError] = useState("");
  const [detailCases, setDetailCases] = useState<TestCycleCaseSummary[]>([]),
    [detailCasesLoading, setDetailCasesLoading] = useState(false),
    [detailCasesError, setDetailCasesError] = useState("");
  const [projectId, setProjectId] = useState(""),
    [releaseId, setReleaseId] = useState(""),
    [buildId, setBuildId] = useState(""),
    [environmentId, setEnvironmentId] = useState(""),
    [suiteId, setSuiteId] = useState(""),
    [suiteSearch, setSuiteSearch] = useState(""),
    [code, setCode] = useState(""),
    [name, setName] = useState(""),
    // true = Cycle Name ยังเป็นค่าที่ระบบตั้งให้อัตโนมัติอยู่ (ยังไม่ถูกแก้ไขเอง) — ให้ auto-generate
    // ใหม่ทุกครั้งที่ Release/Build/Environment/Cycle Type/Module เปลี่ยน จนกว่าผู้ใช้จะพิมพ์แก้เอง
    [nameAutoFilled, setNameAutoFilled] = useState(true),
    [formModuleId, setFormModuleId] = useState(""),
    [formModules, setFormModules] = useState<ModuleItem[]>([]),
    [cycleType, setCycleType] = useState(""),
    [startDate, setStartDate] = useState(""),
    [endDate, setEndDate] = useState(""),
    [notes, setNotes] = useState(""),
    [environmentName, setEnvironmentName] = useState("");
  const [cycleAiModal, setCycleAiModal] = useState(false),
    [cycleAiProjectId, setCycleAiProjectId] = useState(""),
    [cycleAiReleaseId, setCycleAiReleaseId] = useState(""),
    [cycleAiBuildId, setCycleAiBuildId] = useState(""),
    [cycleAiEnvironmentId, setCycleAiEnvironmentId] = useState(""),
    [cycleAiSuiteId, setCycleAiSuiteId] = useState(""),
    [cycleAiSuiteSearch, setCycleAiSuiteSearch] = useState(""),
    [cycleAiGenerating, setCycleAiGenerating] = useState(false),
    [cycleAiError, setCycleAiError] = useState(""),
    [cycleAiDrafts, setCycleAiDrafts] = useState<GeneratedTestCycleDraft[]>([]);
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
  const detailCasesRequest = useRef(0);
  const loadDetailCases = (cycleId: string) => {
    const requestId = ++detailCasesRequest.current;
    setDetailCases([]);
    setDetailCasesError("");
    setDetailCasesLoading(true);
    fetch(`${apiUrl}/test-cycles/${cycleId}/execution`, { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` } })
      .then(async response => {
        if (!response.ok) throw new Error(`โหลด Test Case ของ Cycle ไม่สำเร็จ (${response.status})`);
        return response.json();
      })
      .then(data => {
        if (requestId !== detailCasesRequest.current) return;
        const cases = Array.isArray(data?.cases) ? data.cases : Array.isArray(data) ? data : [];
        setDetailCases(cases as TestCycleCaseSummary[]);
      })
      .catch(reason => {
        if (requestId !== detailCasesRequest.current) return;
        setDetailCasesError(reason instanceof Error ? reason.message : "โหลด Test Case ของ Cycle ไม่สำเร็จ");
      })
      .finally(() => { if (requestId === detailCasesRequest.current) setDetailCasesLoading(false); });
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
      readList<TestSuiteItem>(`${apiUrl}/test-suites?size=100`),
      readList<UserLookup>(`${apiUrl}/lookups/users`),
    ]).then(async ([p, r, e, s, u]) => {
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
      setUsers(u);
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
      setUsers([]);
      // dropdown ในฟอร์มจะว่างทั้งหมด — ต้องบอกผู้ใช้ ไม่ใช่ปล่อยให้ดูเหมือนไม่มีข้อมูล
      setError("โหลดข้อมูลตั้งต้น (Project / Release / Build / Environment / Suite / ผู้ใช้) ไม่สำเร็จ — กด ⟳ เพื่อลองใหม่");
    });
  }, [reload]);
  const debouncedSearch = useDebounced(search.trim());
  useEffect(() => {
    const ctrl = new AbortController();
    const query = new URLSearchParams({ page: String(page), size: String(pageSize) });
    if (contextProjectId) query.set("projectId", contextProjectId);
    if (contextReleaseId) query.set("releaseId", contextReleaseId);
    if (contextBuildId) query.set("buildId", contextBuildId);
    if (listModuleFilter) query.set("moduleId", listModuleFilter);
    if (listCycleTypeFilter) query.set("cycleType", listCycleTypeFilter);
    if (listCreatedByFilter) query.set("createdBy", listCreatedByFilter);
    if (listStatusFilter) query.set("status", listStatusFilter);
    if (debouncedSearch) query.set("search", debouncedSearch);
    setLoading(true);
    setError("");
    // AbortController: ผลของคำขอเก่า (ตัวกรอง/หน้าก่อนหน้า) ต้องไม่มาทับผลของคำขอล่าสุด
    fetch(`${apiUrl}/test-cycles?${query}`, { headers: { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` }, signal: ctrl.signal })
      .then(async response => {
        if (!response.ok) throw new Error(`โหลด Test Cycle ไม่สำเร็จ (${response.status})`);
        const data = await response.json();
        const container = data?.items ?? data;
        const rows = Array.isArray(container?.rows) ? container.rows : Array.isArray(container) ? container : [];
        setItems(rows);
        setTotalCount(Number(container?.total ?? rows.length));
        // summary รวมทุก Test Cycle ที่ตรงเงื่อนไข filter ปัจจุบัน (คำนวณฝั่ง server ไม่ใช่แค่หน้าที่กำลังแสดง)
        setCaseSummary({ totalCases: Number(data?.summary?.totalCases ?? 0), executedCases: Number(data?.summary?.executedCases ?? 0) });
      })
      .catch(reason => { if (isAbortError(reason)) return; setItems([]); setTotalCount(0); setCaseSummary({ totalCases: 0, executedCases: 0 }); setError(reason instanceof Error ? reason.message : "โหลด Test Cycle ไม่สำเร็จ"); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [contextProjectId, contextReleaseId, contextBuildId, listModuleFilter, listCycleTypeFilter, listCreatedByFilter, listStatusFilter, debouncedSearch, page, pageSize, reload]);
  useEffect(() => { setPage(1); }, [contextProjectId, contextReleaseId, contextBuildId, listModuleFilter, listCycleTypeFilter, listCreatedByFilter, listStatusFilter, debouncedSearch]);
  useEffect(() => { setCycleSelected(new Set()); }, [items]);
  // Lightweight count-only queries (size=1, just read `total`) per status — respects every other active
  // filter except status itself, so the chips always reflect "how many would show if you picked this status".
  useEffect(() => {
    const ctrl = new AbortController();
    const baseParams = () => {
      const q = new URLSearchParams({ page: "1", size: "1" });
      if (contextProjectId) q.set("projectId", contextProjectId);
      if (contextReleaseId) q.set("releaseId", contextReleaseId);
      if (contextBuildId) q.set("buildId", contextBuildId);
      if (listModuleFilter) q.set("moduleId", listModuleFilter);
      if (listCycleTypeFilter) q.set("cycleType", listCycleTypeFilter);
      if (listCreatedByFilter) q.set("createdBy", listCreatedByFilter);
      if (debouncedSearch) q.set("search", debouncedSearch);
      return q;
    };
    Promise.all(cycleStatusOptions.map(status => {
      const q = baseParams(); q.set("status", status);
      // นับไม่สำเร็จ = ไม่แสดงตัวเลขของสถานะนั้น (เดิมแสดง 0 ซึ่งดูเหมือนไม่มีข้อมูลจริง)
      return getJson<{ items?: { total?: number }; total?: number }>(`${apiUrl}/test-cycles?${q}`, ctrl.signal).then(data => {
        const container = data?.items ?? data;
        return [status, Number(container?.total ?? 0)] as const;
      }).catch(() => null);
    })).then(pairs => { if (!ctrl.signal.aborted) setStatusCounts(Object.fromEntries(pairs.filter((x): x is readonly [string, number] => x !== null))); });
    return () => ctrl.abort();
  }, [contextProjectId, contextReleaseId, contextBuildId, listModuleFilter, listCycleTypeFilter, listCreatedByFilter, debouncedSearch, reload]);
  useEffect(() => {
    const h = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
    const projectIds = contextProjectId ? [contextProjectId] : [...new Set(items.map((x) => x.projectId))];
    if (!projectIds.length) { setListModules([]); return; }
    Promise.all(projectIds.map((id) => fetch(`${apiUrl}/projects/${id}/modules`, { headers: h }).then((r) => r.ok ? r.json() : [])))
      .then((groups: ModuleItem[][]) => {
        const seen = new Map<string, ModuleItem>();
        groups.flat().filter((m) => m.isActive).forEach((m) => { if (!seen.has(m.moduleId)) seen.set(m.moduleId, m); });
        setListModules([...seen.values()].sort((a, b) => a.moduleCode.localeCompare(b.moduleCode)));
      });
  }, [contextProjectId, items]);
  // เปิดจากหน้าอื่นแล้วโหลดรายละเอียด Test Case ของ Cycle ต่อทันที
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(()=>{const target=localStorage.getItem("qa.targetCycleId");if(!target)return;fetch(`${apiUrl}/test-cycles/${target}`,{headers:{Authorization:`Bearer ${localStorage.getItem("qa.accessToken")}`}}).then(r=>r.ok?r.json():null).then((cycle:TestCycleItem|null)=>{if(cycle){setDetail(cycle);loadDetailCases(cycle.testCycleId);}localStorage.removeItem("qa.targetCycleId")}).catch(()=>localStorage.removeItem("qa.targetCycleId"))},[]);
  // ปุ่ม "สร้าง Test Cycle" แบบด่วนจากหน้า Test Suite ฝาก Project/Suite ไว้ผ่าน localStorage แล้วพามาที่นี่ —
  // รอจน projects โหลดเสร็จก่อน (openForm ต้องใช้ project code มา gen เลข Cycle Code) แล้วค่อยเปิดฟอร์มสร้าง
  // openForm ประกาศด้านล่าง จึงเรียกผ่าน ref — effect นี้ผูกกับการโหลด projects เสร็จเท่านั้น
  const openFormRef = useRef<((cycle?: TestCycleItem, prefill?: { projectId?: string; testSuiteId?: string }) => void) | null>(null);
  useEffect(() => {
    if (!projects.length) return;
    const raw = localStorage.getItem("qa.createCycleFromSuite");
    if (!raw) return;
    localStorage.removeItem("qa.createCycleFromSuite");
    try {
      const prefill: { projectId?: string; testSuiteId?: string } = JSON.parse(raw);
      if (prefill.projectId) openFormRef.current?.(undefined, prefill);
    } catch { /* ignore malformed prefill */ }
  }, [projects]);
  useEffect(() => {
    const lastPage = Math.max(1, Math.ceil(totalCount / pageSize));
    if (page > lastPage) setPage(lastPage);
  }, [page, pageSize, totalCount]);
  // Module list scoped to the create form's selected project — used only to auto-prefix the Cycle Name,
  // Test Cycles aren't themselves linked to a single Module so this isn't persisted anywhere.
  useEffect(() => {
    if (!form || editing || !projectId) { setFormModules([]); return; }
    const h = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
    fetch(`${apiUrl}/projects/${projectId}/modules`, { headers: h }).then(r => okJsonOrEmpty<ModuleItem[]>(r, "รายการ Module")).then((rows) => setFormModules(rows.filter(x => x.isActive))).catch(() => notify("โหลดรายการ Module ไม่สำเร็จ", "error"));
  }, [form, editing, projectId]);
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
    // เมื่อเลือก Module ไว้ ให้กรอง Test Suite เหลือเฉพาะ Suite ที่มี Test Case อยู่ใน Module นั้นจริง
    // (ไม่ระบุ Module = ไม่กรอง แสดง Suite ทั้งหมดของโปรเจกต์เหมือนเดิม)
    projectSuites = useMemo(
      () => suites.filter((x) => x.projectId === projectId && x.isActive && (!formModuleId || (x.modules ?? []).some((m) => m.moduleId === formModuleId))),
      [suites, projectId, formModuleId],
    );
  const suiteOptions = useMemo(() => {
    const query = suiteSearch.trim().toLowerCase();
    if (!query) return projectSuites;
    // Always keep the currently selected suite visible even if it doesn't match the search text,
    // so the dropdown never silently loses the active selection while filtering a long list.
    return projectSuites.filter((x) => x.testSuiteId === suiteId || `${x.suiteCode} ${x.suiteName}`.toLowerCase().includes(query));
  }, [projectSuites, suiteSearch, suiteId]);
  const cycleAiReleases = useMemo(() => releases.filter((x) => x.projectId === cycleAiProjectId), [releases, cycleAiProjectId]),
    cycleAiBuilds = useMemo(() => builds.filter((x) => x.releaseId === cycleAiReleaseId && x.isActive), [builds, cycleAiReleaseId]),
    cycleAiEnvironments = useMemo(() => environments.filter((x) => x.projectId === cycleAiProjectId && x.isActive), [environments, cycleAiProjectId]),
    cycleAiSuites = useMemo(() => suites.filter((x) => x.projectId === cycleAiProjectId && x.isActive), [suites, cycleAiProjectId]);
  const cycleAiSuiteOptions = useMemo(() => {
    const query = cycleAiSuiteSearch.trim().toLowerCase();
    if (!query) return cycleAiSuites;
    // Always keep the currently selected suite visible even if it doesn't match the search text,
    // so the dropdown never silently loses the active selection while filtering a long list.
    return cycleAiSuites.filter((x) => x.testSuiteId === cycleAiSuiteId || `${x.suiteCode} ${x.suiteName}`.toLowerCase().includes(query));
  }, [cycleAiSuites, cycleAiSuiteSearch, cycleAiSuiteId]);
  const cloneReleases = useMemo(() => cloneSource ? releases.filter((x) => x.projectId === cloneSource.projectId) : [], [releases, cloneSource]);
  const cloneBuilds = useMemo(() => builds.filter((x) => x.releaseId === cloneReleaseId && x.isActive), [builds, cloneReleaseId]);
  const cloneEnvironments = useMemo(() => cloneSource ? environments.filter((x) => x.projectId === cloneSource.projectId && x.isActive) : [], [environments, cloneSource]);
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
  // Suite เป็นฟิลด์ไม่บังคับ — ถ้าเปลี่ยน Module แล้ว Suite ที่เลือกไว้ไม่อยู่ในรายการที่กรองใหม่
  // (ไม่ตรง Module) ให้เคลียร์ค่าทิ้งเฉยๆ (ไม่ auto-เลือกตัวอื่นแทน เพราะ Suite ไม่ใช่ฟิลด์บังคับ)
  useEffect(() => {
    if (suiteId && !projectSuites.some((x) => x.testSuiteId === suiteId)) setSuiteId("");
  }, [projectSuites, suiteId]);
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
  // Cycle Name อัตโนมัติ: ตราบใดที่ผู้ใช้ยังไม่ได้พิมพ์แก้ไขเอง (nameAutoFilled) ให้ประกอบชื่อจาก
  // Module + Suite Code (ถ้าเลือก) + Cycle Type + Release + Build ให้อัตโนมัติทุกครั้งที่ค่าพวกนี้เปลี่ยน
  // — ใส่ Suite Code ต่อจาก Module เพราะ 1 Module อาจมีหลาย Suite (เช่น 4 Suite ในโมดูลเดียวกัน)
  // ถ้าใช้แค่ Module อย่างเดียวชื่อ Cycle ที่สร้างจากแต่ละ Suite จะซ้ำกันหมด ต้องมี Suite Code มาแยกให้ไม่ซ้ำ
  // — พอผู้ใช้แก้ไขในช่อง Cycle Name เอง จะหยุด auto-generate ทันที (เคารพชื่อที่ผู้ใช้ตั้งเอง)
  useEffect(() => {
    if (!form || editing || !nameAutoFilled) return;
    const module = formModules.find((m) => m.moduleId === formModuleId);
    const suite = projectSuites.find((x) => x.testSuiteId === suiteId);
    const typeLabel = cycleTypes.find((x) => x.value === cycleType)?.displayName ?? cycleType;
    const release = projectReleases.find((x) => x.releaseId === releaseId);
    const build = releaseBuilds.find((x) => x.buildId === buildId);
    const parts = [module?.moduleName, suite?.suiteCode, [typeLabel, release?.releaseCode, build?.buildNumber].filter(Boolean).join(" ")].filter(Boolean);
    setName(parts.length ? parts.join("-") : "");
  }, [form, editing, nameAutoFilled, formModuleId, formModules, suiteId, projectSuites, cycleType, cycleTypes, releaseId, buildId, projectReleases, releaseBuilds]);
  const openForm = (cycle?: TestCycleItem, prefill?: { projectId?: string; testSuiteId?: string }) => {
    setEditing(cycle ?? null);
    setProjectId(cycle?.projectId ?? prefill?.projectId ?? contextProjectId ?? projects[0]?.projectId ?? "");
    setReleaseId(cycle?.releaseId ?? contextReleaseId ?? "");
    setBuildId(cycle?.buildId ?? contextBuildId ?? "");
    setEnvironmentId(cycle?.environmentId ?? "");
    setSuiteId(cycle?.testSuiteId ?? prefill?.testSuiteId ?? "");
    setSuiteSearch("");
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
    setNameAutoFilled(!cycle); // สร้างใหม่ = ให้ auto-generate ชื่อ, แก้ไขของเดิม = คงชื่อเดิมไว้ไม่แตะ
    setFormModuleId("");
    setCycleType(cycle?.cycleType ?? cycleTypes[0]?.value ?? "");
    setStartDate(cycle?.startDate?.slice(0, 10) ?? "");
    setEndDate(cycle?.endDate?.slice(0, 10) ?? "");
    setNotes(cycle?.notes ?? "");
    setForm(true);
  };
  openFormRef.current = openForm;
  const openDetail = (cycle: TestCycleItem) => { setDetail(cycle); loadDetailCases(cycle.testCycleId); };
  const openClone = (cycle: TestCycleItem) => {
    const targetReleases = releases.filter((x) => x.projectId === cycle.projectId);
    const targetRelease = targetReleases.find((x) => x.releaseId === cycle.releaseId) ?? targetReleases[0];
    const targetBuild = builds.find((x) => x.releaseId === targetRelease?.releaseId && x.isActive);
    const targetEnvironment = environments.find((x) => x.testEnvironmentId === cycle.environmentId && x.isActive && x.projectId === cycle.projectId) ?? environments.find((x) => x.projectId === cycle.projectId && x.isActive);
    setDetail(null);
    setCloneSource(cycle);
    setCloneReleaseId(targetRelease?.releaseId ?? "");
    setCloneBuildId(targetBuild?.buildId ?? "");
    setCloneEnvironmentId(targetEnvironment?.testEnvironmentId ?? "");
    setCloneMode("SourceSnapshot");
    setCloneCode("");
    setCloneName(`${cycle.cycleName} - New Target`);
    setCloneCycleType(cycle.cycleType ?? cycleTypes[0]?.value ?? "");
    setCloneStartDate(cycle.startDate?.slice(0, 10) ?? "");
    setCloneEndDate(cycle.endDate?.slice(0, 10) ?? "");
    setCloneOwnerUserId(cycle.ownerUserId ?? "");
    setCloneNotes(cycle.notes ?? "");
    setCloneError("");
  };
  useEffect(() => {
    if (cloneSource && !cloneReleases.some((x) => x.releaseId === cloneReleaseId)) setCloneReleaseId(cloneReleases[0]?.releaseId ?? "");
  }, [cloneSource, cloneReleases, cloneReleaseId]);
  useEffect(() => {
    if (cloneSource && !cloneBuilds.some((x) => x.buildId === cloneBuildId)) setCloneBuildId(cloneBuilds[0]?.buildId ?? "");
  }, [cloneSource, cloneBuilds, cloneBuildId]);
  const clone = async () => {
    if (!cloneSource || !cloneReleaseId || !cloneBuildId || !cloneEnvironmentId || !cloneName.trim()) return;
    setCloneSaving(true);
    setCloneError("");
    try {
      const response = await fetch(`${apiUrl}/test-cycles/${cloneSource.testCycleId}/clone`, {
        method: "POST",
        headers,
        body: JSON.stringify({
          targetReleaseId: cloneReleaseId,
          targetBuildId: cloneBuildId,
          targetEnvironmentId: cloneEnvironmentId,
          cycleCode: cloneCode.trim() || null,
          cycleName: cloneName.trim(),
          cycleType: cloneCycleType || null,
          startDate: cloneStartDate || null,
          endDate: cloneEndDate || null,
          ownerUserId: cloneOwnerUserId || null,
          notes: cloneNotes.trim() || null,
          cloneMode,
        }),
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new Error(problem?.detail ?? "Clone Test Cycle ไม่สำเร็จ");
      }
      const created: TestCycleItem = await response.json();
      setCloneSource(null);
      setNotice(`สร้าง ${created.cycleCode} จาก ${cloneSource.cycleCode} แล้ว`);
      setReload((x) => x + 1);
      openDetail(created);
    } catch (reason) {
      setCloneError(reason instanceof Error ? reason.message : "Clone Test Cycle ไม่สำเร็จ");
    } finally {
      setCloneSaving(false);
    }
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
        const p = await response.json().catch(() => null);
        throw new Error(p?.detail ?? "บันทึกไม่สำเร็จ");
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
  const openCycleAi = () => {
    const targetProject = contextProjectId || projectId || projects[0]?.projectId || "";
    setCycleAiProjectId(targetProject);
    setCycleAiReleaseId("");
    setCycleAiBuildId("");
    setCycleAiEnvironmentId("");
    setCycleAiSuiteId("");
    setCycleAiSuiteSearch("");
    setCycleAiError("");
    setCycleAiDrafts([]);
    setCycleAiModal(true);
  };
  const generateCycleWithAi = async () => {
    if (!cycleAiProjectId || !cycleAiReleaseId || !cycleAiBuildId || !cycleAiEnvironmentId) return;
    setCycleAiGenerating(true);
    setCycleAiError("");
    try {
      const response = await fetch(`${apiUrl}/test-cycles/generate-ai`, {
        method: "POST",
        headers,
        body: JSON.stringify({
          projectId: cycleAiProjectId,
          releaseId: cycleAiReleaseId,
          buildId: cycleAiBuildId,
          environmentId: cycleAiEnvironmentId,
          testSuiteId: cycleAiSuiteId || null,
        }),
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new Error(problem?.detail ?? "AI Generate Test Cycle ไม่สำเร็จ");
      }
      const drafts: GeneratedTestCycleDraft[] = await response.json();
      if (!Array.isArray(drafts) || !drafts.length) throw new Error("AI ไม่ได้สร้าง Test Cycle กลับมา");
      setCycleAiDrafts(drafts);
    } catch (error) {
      setCycleAiError(error instanceof Error ? error.message : "AI Generate Test Cycle ไม่สำเร็จ");
    } finally {
      setCycleAiGenerating(false);
    }
  };
  const removeCycleAiDraft = (index: number) =>
    setCycleAiDrafts((drafts) => {
      const next = drafts.filter((_, i) => i !== index);
      if (next.length === 0) setCycleAiModal(false);
      return next;
    });
  const saveAllCycleDrafts = async () => {
    if (!cycleAiDrafts.length) return;
    setCycleAiGenerating(true);
    setCycleAiError("");
    try {
      const project = projects.find((x) => x.projectId === cycleAiProjectId);
      const existingCodes = items.map((x) => x.cycleCode);
      for (const draft of cycleAiDrafts) {
        const draftCode = nextBusinessCode(`${project?.projectCode ?? "PRJ"}-CYC`, existingCodes);
        existingCodes.push(draftCode);
        const res = await fetch(`${apiUrl}/test-cycles`, {
          method: "POST",
          headers,
          body: JSON.stringify({
            projectId: cycleAiProjectId,
            releaseId: cycleAiReleaseId,
            buildId: cycleAiBuildId,
            environmentId: cycleAiEnvironmentId,
            testSuiteId: cycleAiSuiteId || null,
            cycleCode: draftCode,
            cycleName: draft.cycleName,
            cycleType: draft.cycleType,
            startDate: draft.startDate || null,
            endDate: draft.endDate || null,
            ownerUserId: null,
            notes: draft.notes || null,
            populateFromSuite: true,
            requiredOnly: false,
          }),
        });
        if (!res.ok) {
          const problem = await res.json().catch(() => null);
          throw new Error(`สร้าง "${draft.cycleName}" ไม่สำเร็จ: ${problem?.detail ?? ""}`);
        }
      }
      const count = cycleAiDrafts.length;
      setCycleAiDrafts([]);
      setCycleAiModal(false);
      setNotice(`สร้าง Test Cycle จาก AI แล้ว ${count} รายการ`);
      setReload((x) => x + 1);
    } catch (error) {
      setCycleAiError(error instanceof Error ? error.message : "บันทึก Test Cycle ไม่สำเร็จ");
    } finally {
      setCycleAiGenerating(false);
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
    if (["Closed", "Cancelled", "Completed"].includes(status) && !await confirmDialog({ title: "เปลี่ยนสถานะ Test Cycle", message: `เปลี่ยน ${cycle.cycleCode} เป็น ${status} ใช่หรือไม่?\nCycle ที่ปิด/ยกเลิกแล้วจะบันทึกผลเพิ่มไม่ได้`, confirmLabel: `เปลี่ยนเป็น ${status}` })) return;
    const response = await fetch(`${apiUrl}/test-cycles/${cycle.testCycleId}/status`, {
      method: "POST",
      headers,
      body: JSON.stringify({ status }),
    });
    if (!response.ok) { setError(`เปลี่ยนสถานะ ${cycle.cycleCode} ไม่สำเร็จ`); return; }
    setNotice(`เปลี่ยนสถานะ ${cycle.cycleCode} แล้ว`);
    setReload((x) => x + 1);
  };
  const toggleCycleSelect = (id: string) => setCycleSelected((prev) => { const next = new Set(prev); if (next.has(id)) next.delete(id); else next.add(id); return next; });
  const toggleCycleSelectPage = () => setCycleSelected((prev) => { const next = new Set(prev); const all = rows.length > 0 && rows.every((x) => prev.has(x.testCycleId)); if (all) rows.forEach((x) => next.delete(x.testCycleId)); else rows.forEach((x) => next.add(x.testCycleId)); return next; });
  const applyCycleBulkStatus = async () => {
    if (!cycleBulkStatus || !cycleSelected.size) return;
    if (!await confirmDialog({ title: "เปลี่ยนสถานะหลายรายการ", message: `เปลี่ยนสถานะ Test Cycle ${cycleSelected.size} รายการเป็น ${cycleBulkStatus} ใช่หรือไม่?`, confirmLabel: `เปลี่ยนเป็น ${cycleBulkStatus}` })) return;
    setCycleBulkSaving(true);
    setError("");
    const targets = rows.filter((x) => cycleSelected.has(x.testCycleId));
    const failed: string[] = [];
    try {
      for (const cycle of targets) {
        const response = await fetch(`${apiUrl}/test-cycles/${cycle.testCycleId}/status`, { method: "POST", headers, body: JSON.stringify({ status: cycleBulkStatus }) });
        if (!response.ok) failed.push(cycle.cycleCode);
      }
      setCycleSelected(new Set());
      setCycleBulkStatus("");
      if (failed.length) setError(`เปลี่ยนสถานะไม่สำเร็จ ${failed.length} รายการ: ${failed.join(", ")}`);
      else setNotice(`เปลี่ยนสถานะ ${targets.length} Test Cycle เป็น ${cycleBulkStatus} แล้ว`);
      setReload((x) => x + 1);
    } finally {
      setCycleBulkSaving(false);
    }
  };
  const remove = async (cycle: TestCycleItem) => {
    if (!await confirmDialog(`ยืนยันลบ ${cycle.cycleCode}?`)) return;
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
        if (listModuleFilter) query.set("moduleId", listModuleFilter);
        if (listCycleTypeFilter) query.set("cycleType", listCycleTypeFilter);
        if (listStatusFilter) query.set("status", listStatusFilter);
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
      const csvRows = [["Cycle Code", "Name", "Module", "Release", "Build", "Environment", "Type", "Executed", "Cases", "Progress", "Status"], ...exported.map(item => [item.cycleCode, item.cycleName, item.modules?.map(m => m.moduleName).join("; ") ?? "", item.releaseCode, item.buildNumber, item.environmentName, item.cycleType ?? "", item.executedCount, item.caseCount, `${item.progressPercent}%`, item.status])];
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
  const detailCaseStatusCounts = detailCases.reduce<Record<string, number>>((counts, item) => { counts[item.currentStatus] = (counts[item.currentStatus] ?? 0) + 1; return counts; }, {});
  return (
    <>
      <article className="card">
        {error && <div className="inline-alert error" role="alert"><span>{error}</span><button onClick={() => { setError(""); setReload(value => value + 1); }}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> ลองใหม่</button></div>}
        {notice && <div className="inline-alert success" role="status"><span>{notice}</span><button aria-label="ปิดข้อความ" onClick={() => setNotice("")}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>}
        <div className="filter-toolbar">
          <div className="filter-toolbar-top">
            <div className="result-count-row">
              <div className="result-count"><strong>{totalCount.toLocaleString()}</strong><span>Test Cycles</span></div>
              <div className="result-count"><strong>{caseSummary.totalCases.toLocaleString()}</strong><span>Test Case ทั้งหมด</span></div>
            </div>
            <div>
              {canExport && <button className="btn" disabled={exporting || loading || totalCount === 0} onClick={exportCsv}>{exporting ? <><span className="spinner inline" aria-hidden="true" /> กำลัง Export...</> : <><span className="material-symbols-outlined" aria-hidden="true">download</span> Export CSV</>}</button>}
              {canEdit && (
              <>
              <button className="btn ai-button" onClick={openCycleAi}>
                <span className="material-symbols-outlined" aria-hidden="true">auto_awesome</span> AI Generate
              </button>
              <button className="btn primary" onClick={() => openForm()}>
                <span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Test Cycle
              </button>
              </>
              )}
            </div>
          </div>
          <div className="filter-toolbar-row cycle-toolbar-row">
            <div className="cycle-status-chips" role="group" aria-label="กรองตามสถานะ">
              <button type="button" className={"status-chip" + (listStatusFilter === "" ? " active" : "")} onClick={() => setListStatusFilter("")}>
                ทั้งหมด <b>{cycleStatusOptions.reduce((s, x) => s + (statusCounts[x] ?? 0), 0).toLocaleString()}</b>
              </button>
              {cycleStatusOptions.map(status => (
                <button key={status} type="button" className={"status-chip" + (listStatusFilter === status ? " active" : "")} onClick={() => setListStatusFilter(current => current === status ? "" : status)}>
                  <i className={`status-chip-dot status-chip-dot-${status.toLowerCase()}`} aria-hidden="true" />
                  {status} <b>{(statusCounts[status] ?? 0).toLocaleString()}</b>
                </button>
              ))}
            </div>
            <div className="cycle-filters-right">
              <select className="testcase-module-filter" aria-label="กรอง Module" value={listModuleFilter} onChange={e => setListModuleFilter(e.target.value)} disabled={!listModules.length}>
                <option value="">ทุก Module</option>
                {renderModuleSelectOptions(listModules)}
              </select>
              <select aria-label="กรอง Type" value={listCycleTypeFilter} onChange={e => setListCycleTypeFilter(e.target.value)}>
                <option value="">ทุก Type</option>
                {cycleTypes.map(x => <option key={x.value} value={x.value}>{x.displayName}</option>)}
              </select>
              <select aria-label="กรองผู้สร้าง" value={listCreatedByFilter} onChange={e => setListCreatedByFilter(e.target.value)}>
                <option value="">ผู้สร้างทั้งหมด</option>
                {users.map(u => <option key={u.userId} value={u.userId}>{u.displayName}</option>)}
              </select>
            </div>
          </div>
        </div>
        {canEdit && cycleSelected.size > 0 && (
          <div className="testcase-bulk-bar" role="region" aria-label="กำหนดสถานะแบบกลุ่ม">
            <span className="bulk-count">{cycleSelected.size} เลือกแล้ว</span>
            <label className="bulk-status">กำหนดสถานะ
              <select value={cycleBulkStatus} onChange={(e) => setCycleBulkStatus(e.target.value)}>
                <option value="">เลือกสถานะ...</option>
                <option>Draft</option>
                <option>InProgress</option>
                <option>Completed</option>
                <option>Closed</option>
                <option>Cancelled</option>
              </select>
            </label>
            <button type="button" className="btn primary" disabled={cycleBulkSaving || !cycleBulkStatus} onClick={applyCycleBulkStatus}>{cycleBulkSaving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> กำหนดสถานะ</>}</button>
            <button type="button" className="bulk-clear" disabled={cycleBulkSaving} onClick={() => setCycleSelected(new Set())}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิกเลือก</button>
          </div>
        )}
        <div className="table-wrap">
          <table className="cycle-list-table table-cards">
            <thead>
              <tr>
                {canEdit && <th className="cycle-select-col"><input type="checkbox" aria-label="เลือกทั้งหน้านี้" checked={rows.length > 0 && rows.every((x) => cycleSelected.has(x.testCycleId))} onChange={toggleCycleSelectPage} /></th>}
                <th>Cycle Code</th>
                <th>Name</th>
                <th>Release / Build / Environment</th>
                <th>Progress</th>
                <th>Status</th>
                {canEdit && <th className="actions-col">จัดการ</th>}
              </tr>
            </thead>
            <tbody>
              {loading && <tr><td className="empty-cell" colSpan={canEdit ? 7 : 5}><div className="empty-state"><div className="spinner" /><b>กำลังโหลด Test Cycle...</b></div></td></tr>}
              {!loading && !error && rows.length === 0 && <tr><td className="empty-cell" colSpan={canEdit ? 7 : 5}><div className="empty-state"><span className="material-symbols-outlined" aria-hidden="true">event_busy</span><b>ไม่พบ Test Cycle</b><small>ลองเปลี่ยน Project, Release, Build หรือคำค้นหา</small></div></td></tr>}
              {rows.map((x) => {
                return (
                <tr key={x.testCycleId} className={cycleSelected.has(x.testCycleId) ? "is-selected" : ""}>
                  {canEdit && <td className="cycle-select-col"><input type="checkbox" aria-label={`เลือก ${x.cycleCode}`} checked={cycleSelected.has(x.testCycleId)} onChange={() => toggleCycleSelect(x.testCycleId)} /></td>}
                  <td>
                    <button className="link-button" onClick={() => openDetail(x)}>{x.cycleCode}</button>
                    {x.cycleType && <small className="cell-sub">{x.cycleType}</small>}
                  </td>
                  <td>{x.cycleName}</td>
                  <td>
                    {x.releaseCode}
                    <small className="cell-sub">Build {x.buildNumber} · {x.environmentName}</small>
                  </td>
                  <td>
                    <div className={`progress-cell ${x.progressPercent >= 100 ? "is-complete" : x.progressPercent >= 50 ? "is-progress" : x.progressPercent > 0 ? "is-low" : "is-empty"}`}>
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
                  {canEdit && <td className="actions-col">
                    <div className="row-actions">
                      <button className="table-action icon-only" title="Clone เป็น Test Cycle ใหม่" aria-label={`Clone ${x.cycleCode} เป็น Test Cycle ใหม่`} onClick={() => openClone(x)}>
                        <span className="material-symbols-outlined" aria-hidden="true">content_copy</span>
                      </button>
                      <button
                        className="table-action icon-only"
                        title="แก้ไข"
                        aria-label={`แก้ไข ${x.cycleCode}`}
                        onClick={() => openForm(x)}
                      >
                        <span className="material-symbols-outlined" aria-hidden="true">edit</span>
                      </button>
                      {x.status === "Draft" && (
                        <button
                          className="table-action icon-only"
                          title="เริ่ม"
                          aria-label={`เริ่ม ${x.cycleCode}`}
                          onClick={() => changeStatus(x, "InProgress")}
                        >
                          <span className="material-symbols-outlined" aria-hidden="true">play_arrow</span>
                        </button>
                      )}
                      {x.status === "InProgress" && (
                        <button
                          className="table-action icon-only"
                          title="ปิด Cycle"
                          aria-label={`ปิด Cycle ${x.cycleCode}`}
                          onClick={() => changeStatus(x, "Closed")}
                        >
                          <span className="material-symbols-outlined" aria-hidden="true">stop_circle</span>
                        </button>
                      )}
                      <button
                        className="table-action danger-action icon-only"
                        title="ลบ"
                        aria-label={`ลบ ${x.cycleCode}`}
                        onClick={() => remove(x)}
                      >
                        <span className="material-symbols-outlined" aria-hidden="true">delete</span>
                      </button>
                    </div>
                  </td>}
                </tr>
              );})}
            </tbody>
          </table>
        </div>
        <div className="pagination suite-pagination">
          <label>แสดง<select value={pageSize} onChange={event => { setPageSize(Number(event.target.value)); setPage(1); }}><option value="30">30</option><option value="50">50</option><option value="100">100</option><option value="150">150</option></select> รายการ</label>
          <span>หน้า {Math.min(page, pageCount)} / {pageCount} · ทั้งหมด {totalCount.toLocaleString()} รายการ</span>
          <button className="btn" disabled={loading || page <= 1} onClick={() => setPage(value => value - 1)}><span className="material-symbols-outlined" aria-hidden="true">chevron_left</span> ก่อนหน้า</button>
          <button className="btn" disabled={loading || page >= pageCount} onClick={() => setPage(value => value + 1)}>ถัดไป <span className="material-symbols-outlined" aria-hidden="true">chevron_right</span></button>
        </div>
      </article>
      {detail && (
        <ModalShell labelledBy="cycle-detail-title" className="cycle-modal cycle-detail-modal" onDismiss={() => setDetail(null)}>
            <div className="modal-head cycle-detail-crumb-head">
              <div className="cycle-detail-crumb"><span>Test Cycle</span><i className="material-symbols-outlined" aria-hidden="true">chevron_right</i><span>รายละเอียด</span></div>
              <button aria-label="ปิดรายละเอียด Test Cycle" onClick={() => setDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
            </div>
            <div className="cycle-detail-title-row">
              <span className="suite-detail-hero-icon cycle-detail-title-icon material-symbols-outlined" aria-hidden="true">assignment</span>
              <div className="cycle-detail-title-text">
                <h2 id="cycle-detail-title">{detail.cycleCode}</h2>
                <div className="cycle-detail-title-meta">
                  <span>{projects.find(project => project.projectId === detail.projectId)?.projectName ?? "-"}</span>
                  <span className="cycle-detail-id-pill">Test Cycle ID</span>
                </div>
              </div>
              <div className="cycle-detail-badges cycle-detail-title-badges">
                <Badge tone={detail.status === "Completed" || detail.status === "Closed" ? "green" : detail.status === "Cancelled" ? "red" : "yellow"}><span className="material-symbols-outlined cycle-detail-badge-icon" aria-hidden="true">{cycleStatusIcons[detail.status] ?? "play_circle"}</span> {detail.status}</Badge>
                {detail.cycleType && <Badge tone="blue"><span className="material-symbols-outlined cycle-detail-badge-icon" aria-hidden="true">{cycleTypeIcons[detail.cycleType] ?? "label"}</span> {detail.cycleType}</Badge>}
              </div>
            </div>
            <details className="requirement-status-information full">
              <summary>
                <span className="information-icon" aria-hidden="true">i</span>
                <span><b>{detail.status} · {cycleStatusInfo.find((x) => x.value === detail.status)?.label}</b><small>{cycleStatusInfo.find((x) => x.value === detail.status)?.meaning}</small></span>
                <em>ดูความหมายทั้งหมด</em>
              </summary>
              <div className="requirement-status-list">
                {cycleStatusInfo.map((item) => <article key={item.value} className={detail.status === item.value ? "active" : ""}>
                  <div><b>{item.value}</b><span>{item.label}</span></div>
                  <p>{item.meaning}</p>
                  <small><strong>ผลต่อการใช้งาน:</strong> {item.impact}</small>
                </article>)}
              </div>
            </details>
            <section className="cycle-detail-hero">
              <div className="cycle-detail-hero-text">
                <span className="cycle-detail-hero-icon material-symbols-outlined" aria-hidden="true">desktop_windows</span>
                <div><h3>{detail.cycleName}</h3><p>{detail.releaseCode} <span aria-hidden="true">•</span> Build {detail.buildNumber}</p></div>
              </div>
            </section>
            <section className="cycle-detail-section" aria-label={`ดำเนินการแล้ว ${Math.min(100, Math.max(0, detail.progressPercent))}%`}>
              <div className="cycle-detail-section-head"><h3><span className="material-symbols-outlined cycle-detail-section-icon" aria-hidden="true">monitoring</span>ความคืบหน้าการทดสอบ</h3><span className="cycle-detail-progress-count">{detail.executedCount.toLocaleString()} จาก {detail.caseCount.toLocaleString()} Test Cases</span></div>
              <div className="cycle-detail-progress">
                {/* วงแหวน % ใช้ SVG stroke-dasharray/dashoffset ล้วนๆ (ไม่ใช้ CSS conic-gradient) เพราะ
                    geometry ของ SVG circle ตายตัวแน่นอน ไม่ขึ้นกับการคำนวณ flex/grid ของ parent เหมือน
                    div+conic-gradient ที่เคยพังกลายเป็นแท่งสี่เหลี่ยมยาวเต็มความกว้างในบางเบราว์เซอร์/เครื่อง */}
                <div className="cycle-detail-progress-ring">
                  <svg viewBox="0 0 128 128" width="128" height="128" aria-hidden="true">
                    <circle cx="64" cy="64" r="54" fill="none" stroke="#e9eef8" strokeWidth="12" />
                    <circle cx="64" cy="64" r="54" fill="none" stroke="#2457d6" strokeWidth="12" strokeLinecap="round"
                      strokeDasharray={2 * Math.PI * 54}
                      strokeDashoffset={2 * Math.PI * 54 * (1 - Math.min(100, Math.max(0, detail.progressPercent)) / 100)}
                      transform="rotate(-90 64 64)" />
                  </svg>
                  <div className="cycle-detail-progress-ring-inner">
                    <strong>{Math.min(100, Math.max(0, detail.progressPercent))}%</strong>
                    <small>{detail.progressPercent >= 100 ? "เสร็จสมบูรณ์" : detail.progressPercent <= 0 ? "ยังไม่เริ่ม" : "กำลังดำเนินการ"}</small>
                  </div>
                </div>
                <div className="cycle-detail-progress-side">
                  <div className="cycle-detail-progress-track"><span style={{ width: `${Math.min(100, Math.max(0, detail.progressPercent))}%` }} /></div>
                  <div className="cycle-detail-progress-stats">
                    <div className="cycle-detail-progress-stat"><span className="cycle-detail-progress-stat-icon blue material-symbols-outlined" aria-hidden="true">task_alt</span><div><b>{detail.executedCount.toLocaleString()}</b><small>ดำเนินการแล้ว</small></div></div>
                    <div className="cycle-detail-progress-stat"><span className="cycle-detail-progress-stat-icon orange material-symbols-outlined" aria-hidden="true">pending_actions</span><div><b>{Math.max(0, detail.caseCount - detail.executedCount).toLocaleString()}</b><small>ค้างอยู่</small></div></div>
                    <div className="cycle-detail-progress-stat"><span className="cycle-detail-progress-stat-icon green material-symbols-outlined" aria-hidden="true">flag</span><div><b>{detail.caseCount.toLocaleString()}</b><small>ทั้งหมด</small></div></div>
                  </div>
                </div>
              </div>
            </section>
            <section className="cycle-detail-section cycle-detail-cases" aria-labelledby="cycle-detail-cases-title">
              <div className="cycle-detail-section-head"><h3 id="cycle-detail-cases-title"><span className="material-symbols-outlined cycle-detail-section-icon" aria-hidden="true">checklist</span>Test Cases ใน Cycle</h3><span className="cycle-detail-progress-count">{detailCases.length || detail.caseCount} รายการ</span></div>
              {detailCasesLoading ? <div className="cycle-detail-cases-state" role="status"><span className="spinner inline" aria-hidden="true" /> กำลังโหลด Test Case...</div> : detailCasesError ? <div className="cycle-detail-cases-state is-error" role="alert"><span className="material-symbols-outlined" aria-hidden="true">error</span><span>{detailCasesError}</span><button type="button" className="btn" onClick={() => loadDetailCases(detail.testCycleId)}>ลองใหม่</button></div> : !detailCases.length ? <div className="cycle-detail-cases-state"><span className="material-symbols-outlined" aria-hidden="true">inventory_2</span>ยังไม่มี Test Case ใน Cycle นี้</div> : <>
                <div className="cycle-detail-case-summary" aria-label="สรุปสถานะ Test Case">{Object.entries(detailCaseStatusCounts).map(([status, count]) => <span key={status} className={`cycle-detail-case-summary-item is-${status.toLowerCase()}`}><b>{count.toLocaleString()}</b>{cycleCaseStatusLabels[status] ?? status}</span>)}</div>
                <div className="cycle-detail-case-list">{detailCases.map((testCase, index) => <article key={testCase.testCycleCaseId} className="cycle-detail-case-row">
                  <span className="cycle-detail-case-order">{String(index + 1).padStart(2, "0")}</span>
                  <div className="cycle-detail-case-main"><div><b>{testCase.testCaseCode}</b><Badge tone={testCase.priority === "P0" || testCase.priority === "P1" ? "red" : "blue"}>{testCase.priority}</Badge></div><strong>{testCase.title}</strong><small>{[testCase.moduleCode, testCase.moduleName].filter(Boolean).join(" · ") || "ไม่ระบุ Module"}</small><small className="cycle-detail-case-expected" title={testCase.expectedResult || "ไม่ระบุ Expected Result"}><b>Expected Result (Step แรก):</b> {testCase.expectedResult || "ไม่ระบุ"}</small><small className="cycle-detail-case-actual" title={testCase.actualResult || "ยังไม่มี Actual Result"}><b>Actual Result (ผลล่าสุด):</b> {testCase.actualResult || "ยังไม่มีผลการรัน"}</small></div>
                  <Badge tone={cycleCaseStatusTones[testCase.currentStatus] ?? "blue"}>{cycleCaseStatusLabels[testCase.currentStatus] ?? testCase.currentStatus}</Badge>
                </article>)}</div>
              </>}
            </section>
            <section className="cycle-detail-section">
              <h3><span className="material-symbols-outlined cycle-detail-section-icon" aria-hidden="true">dataset</span>ข้อมูลการทดสอบ</h3>
              <dl className="cycle-detail-grid">
                <div><dt><span className="purple material-symbols-outlined" aria-hidden="true">inventory_2</span> Release</dt><dd>{detail.releaseCode || "-"}</dd></div>
                <div><dt><span className="blue material-symbols-outlined" aria-hidden="true">deployed_code</span> Build</dt><dd>{detail.buildNumber || "-"}</dd></div>
                <div><dt><span className="blue material-symbols-outlined" aria-hidden="true">desktop_windows</span> Environment</dt><dd>{detail.environmentName || "-"}</dd></div>
                <div><dt><span className="orange material-symbols-outlined" aria-hidden="true">person</span> สร้างโดย</dt><dd>{detail.createdByName || "-"}</dd></div>
                <div><dt><span className="purple material-symbols-outlined" aria-hidden="true">calendar_today</span> สร้างเมื่อ</dt><dd>{formatThaiDateTime(detail.createdAt, { day: "numeric", month: "short", year: "numeric" })}</dd></div>
                <div><dt><span className="blue material-symbols-outlined" aria-hidden="true">folder_copy</span> Test Suite</dt><dd>{detail.suiteName || "ไม่ระบุ Suite"}</dd></div>
                <div className="wide"><dt><span className="purple material-symbols-outlined" aria-hidden="true">account_tree</span> Module</dt><dd>{detail.modules?.length ? detail.modules.map(m => m.moduleName).join(", ") : "-"}</dd></div>
              </dl>
            </section>
            <section className="cycle-detail-section">
              <h3><span className="material-symbols-outlined cycle-detail-section-icon" aria-hidden="true">date_range</span>กำหนดการ</h3>
              <div className="cycle-detail-timeline">
                <div><span className="material-symbols-outlined" aria-hidden="true">event_available</span><small>Start Date</small><b>{detail.startDate ? formatThaiDateTime(detail.startDate, { day: "numeric", month: "short", year: "numeric" }) : "ไม่ระบุ"}</b></div>
                <i aria-hidden="true" />
                <div><span className="material-symbols-outlined" aria-hidden="true">event</span><small>End Date</small><b>{detail.endDate ? formatThaiDateTime(detail.endDate, { day: "numeric", month: "short", year: "numeric" }) : "ไม่ระบุ"}</b></div>
              </div>
            </section>
            <section className="cycle-detail-notes"><div className="material-symbols-outlined" aria-hidden="true">info</div><span><b>Notes</b><p>{detail.notes || "ไม่มี Notes สำหรับ Test Cycle นี้"}</p></span></section>
            {detail.copiedFromTestCycleId && <section className="cycle-detail-lineage"><span className="material-symbols-outlined" aria-hidden="true">account_tree</span><span><b>สร้างจาก Cycle เดิม</b><small>{detail.copiedFromCycleCode || detail.copiedFromTestCycleId}</small></span></section>}
            <div className="modal-actions">
              <button className="btn" onClick={() => setDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิด</button>
              {canEdit && <button className="btn" onClick={() => openClone(detail)}><span className="material-symbols-outlined" aria-hidden="true">content_copy</span> Clone เป็น Cycle ใหม่</button>}
              {canEdit && <button className="btn primary" onClick={() => { const cycle = detail; setDetail(null); openForm(cycle); }}><span className="material-symbols-outlined" aria-hidden="true">edit</span> แก้ไข</button>}
            </div>
          </ModalShell>
      )}
      {cycleAiModal && (
        <ModalShell labelledBy="cycle-ai-title" dirty={cycleAiDrafts.length > 0} className="requirement-ai-modal suite-ai-modal cycle-ai-modal" boxStyle={{ position: "relative" }} onDismiss={() => { if (!cycleAiGenerating) setCycleAiModal(false); }}>
            {cycleAiGenerating && (
              <div className="ai-loading-overlay">
                <div className="ai-spinner" />
                {cycleAiDrafts.length ? <p>กำลังบันทึก Test Cycle...</p> : <p>AI กำลังวิเคราะห์ Test Cycle...</p>}
                <small>{cycleAiDrafts.length ? "กรุณารอสักครู่ อย่าปิดหน้าต่างนี้" : "รอสักครู่ ระบบกำลังประมวลผล Release/Build/Test Suite"}</small>
              </div>
            )}
            <div className="modal-head">
              <div>
                <h2 id="cycle-ai-title">AI Generate Test Cycle</h2>
                <small>{cycleAiDrafts.length ? `พบ ${cycleAiDrafts.length} Test Cycle ที่ AI สร้าง — ตรวจสอบและบันทึก` : "วางแผนรอบทดสอบจาก Release/Build/Environment/Test Suite ที่เลือก"}</small>
              </div>
              <button disabled={cycleAiGenerating} aria-label="ปิดหน้าต่าง AI Generate" onClick={() => setCycleAiModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
            </div>
            {cycleAiDrafts.length === 0 ? (
              <section className="requirement-ai-panel">
                <div className="requirement-ai-head">
                  <div>
                    <span className="ai-spark">AI</span>
                    <p><strong>ผู้ช่วยวางแผนรอบทดสอบ</strong><small>AI จะเสนอ Test Cycle 1-3 รอบจากขอบเขตที่เลือก</small></p>
                  </div>
                  <span className="ai-review-badge">ตรวจสอบก่อนบันทึก</span>
                </div>
                {cycleAiError && <div className="inline-alert error"><span>{cycleAiError}</span></div>}
                {!cycleTypes.length && <div className="inline-alert error"><span>กรุณาเพิ่ม Test Cycle Type ในการตั้งค่ากลางก่อนใช้งาน AI</span></div>}
                <div className="form-grid">
                  <label>
                    Project
                    <select value={cycleAiProjectId} disabled={cycleAiGenerating} onChange={(e) => { setCycleAiProjectId(e.target.value); setCycleAiReleaseId(""); setCycleAiBuildId(""); setCycleAiEnvironmentId(""); setCycleAiSuiteId(""); setCycleAiSuiteSearch(""); setCycleAiError(""); }}>
                      <option value="">เลือก Project</option>
                      {projects.map((x) => <option key={x.projectId} value={x.projectId}>{x.projectCode} · {x.projectName}</option>)}
                    </select>
                  </label>
                  <label>
                    Release
                    <select value={cycleAiReleaseId} disabled={cycleAiGenerating || !cycleAiProjectId} onChange={(e) => { setCycleAiReleaseId(e.target.value); setCycleAiBuildId(""); }}>
                      <option value="">เลือก Release</option>
                      {cycleAiReleases.map((x) => <option key={x.releaseId} value={x.releaseId}>{x.releaseCode}{x.version ? ` · ${x.version}` : ""}</option>)}
                    </select>
                  </label>
                  <label>
                    Build
                    <select value={cycleAiBuildId} disabled={cycleAiGenerating || !cycleAiReleaseId} onChange={(e) => setCycleAiBuildId(e.target.value)}>
                      <option value="">เลือก Build</option>
                      {cycleAiBuilds.map((x) => <option key={x.buildId} value={x.buildId}>{x.buildNumber}{x.applicationVersion ? ` · App ${x.applicationVersion}` : ""}</option>)}
                    </select>
                  </label>
                  <label>
                    Environment
                    <select value={cycleAiEnvironmentId} disabled={cycleAiGenerating || !cycleAiProjectId} onChange={(e) => setCycleAiEnvironmentId(e.target.value)}>
                      <option value="">เลือก Environment</option>
                      {cycleAiEnvironments.map((x) => <option key={x.testEnvironmentId} value={x.testEnvironmentId}>{x.environmentName}</option>)}
                    </select>
                  </label>
                  <label className="cycle-ai-suite-field">
                    <span>Test Suite (ไม่บังคับ){cycleAiSuites.length > 8 && <small className="cycle-ai-suite-count"> · {cycleAiSuiteOptions.length}/{cycleAiSuites.length}</small>}</span>
                    {cycleAiSuites.length > 8 && (
                      <input
                        type="text"
                        placeholder="ค้นหารหัสหรือชื่อ Test Suite..."
                        value={cycleAiSuiteSearch}
                        disabled={cycleAiGenerating || !cycleAiProjectId}
                        onChange={(e) => setCycleAiSuiteSearch(e.target.value)}
                      />
                    )}
                    <select value={cycleAiSuiteId} disabled={cycleAiGenerating || !cycleAiProjectId} onChange={(e) => setCycleAiSuiteId(e.target.value)}>
                      <option value="">ไม่ระบุ Test Suite</option>
                      {cycleAiSuiteOptions.map((x) => <option key={x.testSuiteId} value={x.testSuiteId}>{x.suiteCode} · {x.suiteName}</option>)}
                    </select>
                    {cycleAiSuiteSearch.trim() && cycleAiSuiteOptions.length === 0 && <small>ไม่พบ Test Suite ที่ตรงกับคำค้นหา</small>}
                  </label>
                </div>
                <div className="ai-draft-note">
                  <span className="material-symbols-outlined" aria-hidden="true">info</span>
                  <p><strong>ใช้ข้อมูลที่มีอยู่ในระบบ</strong><small>ระบบส่ง Release/Build/Environment/Test Suite ที่เลือกให้ AI วิเคราะห์ ผลลัพธ์ยังไม่ถูกบันทึกจนกว่าจะตรวจ Draft และกดบันทึก</small></p>
                </div>
                <div className="requirement-ai-actions">
                  <small>{cycleAiSuiteId ? `อ้างอิง Test Suite ที่เลือก` : "ไม่ได้อ้างอิง Test Suite"}</small>
                  <div className="row-actions">
                    <button className="btn" disabled={cycleAiGenerating} onClick={() => setCycleAiModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button>
                    <button className="btn primary" disabled={cycleAiGenerating || !cycleAiProjectId || !cycleAiReleaseId || !cycleAiBuildId || !cycleAiEnvironmentId || !cycleTypes.length} onClick={generateCycleWithAi}>{cycleAiGenerating ? <><span className="spinner inline" aria-hidden="true" /> AI กำลังวิเคราะห์...</> : "✦ สร้าง Test Cycle"}</button>
                  </div>
                </div>
              </section>
            ) : (
              <section className="requirement-ai-panel suite-ai-review">
                <div className="suite-ai-review-head">
                  <div>
                    <h3>Test Cycle ที่ AI สร้าง ({cycleAiDrafts.length})</h3>
                  </div>
                </div>
                {cycleAiError && <div className="inline-alert error" style={{ marginBottom: 8 }}><span>{cycleAiError}</span></div>}
                <div className="suite-ai-draft-list">
                  {cycleAiDrafts.map((draft, index) => (
                    <div key={index} className="suite-ai-draft-card expanded">
                      <div className="suite-ai-draft-head">
                        <div className="suite-ai-draft-title">
                          <b>{draft.cycleName}</b>
                          <div className="suite-ai-draft-tags">
                            <Badge tone="blue">{draft.cycleType}</Badge>
                            {draft.startDate && <span className="suite-ai-case-count">{draft.startDate} → {draft.endDate ?? "-"}</span>}
                          </div>
                        </div>
                      </div>
                      <div className="suite-ai-draft-body">
                        {draft.notes && <p className="suite-ai-draft-desc">{draft.notes}</p>}
                        <p className="suite-ai-draft-summary"><strong>สรุป:</strong> {draft.selectionSummary}</p>
                        <button className="table-action danger-action" style={{ marginTop: 8 }} onClick={() => removeCycleAiDraft(index)}><span className="material-symbols-outlined" aria-hidden="true">close</span> นำ Test Cycle นี้ออก</button>
                      </div>
                    </div>
                  ))}
                </div>
                <div className="requirement-ai-actions">
                  <small>{cycleAiDrafts.length} Test Cycle พร้อมบันทึก</small>
                  <div className="row-actions">
                    <button className="btn" disabled={cycleAiGenerating} onClick={() => setCycleAiDrafts([])}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> สร้างใหม่</button>
                    <button className="btn primary" disabled={cycleAiGenerating || !cycleAiDrafts.length} onClick={saveAllCycleDrafts}>{cycleAiGenerating ? "กำลังบันทึก..." : `✦ บันทึกทั้งหมด (${cycleAiDrafts.length} Cycle)`}</button>
                  </div>
                </div>
              </section>
            )}
          </ModalShell>
      )}
      {form && (
        <ModalShell label={editing ? "แก้ไข Test Cycle" : "สร้าง Test Cycle"} className="cycle-modal" onDismiss={() => setForm(false)}>
            <div className="modal-head">
              <div>
                <h2>{editing ? "แก้ไข" : "สร้าง"} Test Cycle</h2>
                <small>กำหนดขอบเขต Release/Build/Environment และรายละเอียดของรอบทดสอบนี้</small>
              </div>
              <button aria-label="ปิดหน้าต่าง" onClick={() => setForm(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
            </div>
            <p className="fieldset-hint"><span className="required">*</span> ข้อมูลที่จำเป็นต้องกรอก</p>
            <div className="cycle-form-columns">
            <div className="modal-section">
            <h3 className="modal-section-title">ขอบเขตการทดสอบ</h3>
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
              <div className="form-row">
                <label>
                  Release <span className="required">*</span>
                  <select
                    disabled={!!editing}
                    value={releaseId}
                    onChange={(e) => setReleaseId(e.target.value)}
                  >
                    <option value="">เลือก Release</option>
                    {projectReleases.map((x) => (
                      <option key={x.releaseId} value={x.releaseId}>
                        {x.releaseCode}{x.version ? ` · ${x.version}` : ""}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Build <span className="required">*</span>
                  <select
                    disabled={!!editing}
                    value={buildId}
                    onChange={(e) => setBuildId(e.target.value)}
                  >
                    <option value="">เลือก Build</option>
                    {releaseBuilds.map((x) => (
                      <option key={x.buildId} value={x.buildId}>
                        {x.buildNumber}{x.applicationVersion ? ` · App ${x.applicationVersion}` : ""}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
              <label>
                Environment <span className="required">*</span>
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
              {!editing && (
                <label>
                  Module <span className="required">*</span>
                  <select
                    className="testcase-module-filter"
                    value={formModuleId}
                    disabled={!formModules.length}
                    onChange={(e) => setFormModuleId(e.target.value)}
                  >
                    <option value="">เลือก Module</option>
                    {renderModuleSelectOptions(formModules)}
                  </select>
                  {!formModules.length && <small>โปรเจกต์นี้ยังไม่มี Module — ไปสร้าง Module ก่อนที่เมนู "Project / Module"</small>}
                </label>
              )}
              <label className="full cycle-ai-suite-field">
                <span>Test Suite (ไม่บังคับ){formModuleId && <small className="cycle-ai-suite-count"> · กรองตาม Module ที่เลือก</small>}{projectSuites.length > 8 && <small className="cycle-ai-suite-count"> · {suiteOptions.length}/{projectSuites.length}</small>}</span>
                {!editing && projectSuites.length > 8 && (
                  <input
                    type="text"
                    placeholder="ค้นหารหัสหรือชื่อ Test Suite..."
                    value={suiteSearch}
                    onChange={(e) => setSuiteSearch(e.target.value)}
                  />
                )}
                <select
                  disabled={!!editing}
                  value={suiteId}
                  onChange={(e) => setSuiteId(e.target.value)}
                >
                  <option value="">ไม่ระบุ Suite</option>
                  {suiteOptions.map((x) => (
                    <option key={x.testSuiteId} value={x.testSuiteId}>
                      {x.suiteCode} · {x.suiteName}
                    </option>
                  ))}
                </select>
                {suiteSearch.trim() && suiteOptions.length === 0 && <small>ไม่พบ Test Suite ที่ตรงกับคำค้นหา</small>}
                {!suiteSearch.trim() && formModuleId && projectSuites.length === 0 && <small>ไม่มี Test Suite ที่มี Test Case อยู่ใน Module นี้</small>}
              </label>
            </div>
            </div>
            <div className="modal-section">
            <h3 className="modal-section-title">รายละเอียด Cycle</h3>
            <div className="form-grid">
              <div className="form-row">
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
              </div>
              <label>
                Cycle Name <span className="required">*</span>
                <input value={name} onChange={(e) => { setName(e.target.value); setNameAutoFilled(false); }} placeholder="เช่น รอบทดสอบ Sprint 12" />
                {!editing && nameAutoFilled && <small>ตั้งชื่อให้อัตโนมัติจาก Module/Suite/Cycle Type/Release/Build — แก้ไขได้ตามต้องการ</small>}
              </label>
              <div className="form-row">
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
              </div>
              <label className="full">
                Notes
                <textarea
                  rows={2}
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  placeholder="รายละเอียดเพิ่มเติมของรอบทดสอบนี้ (ไม่บังคับ)"
                />
              </label>
            </div>
            </div>
            </div>
            {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
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
                  (!editing && !formModuleId) ||
                  !code.trim() ||
                  !name.trim()
                }
                onClick={save}
              >
                {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">check</span> บันทึก Test Cycle</>}
              </button>
            </div>
          </ModalShell>
      )}
      {cloneSource && <ModalShell labelledBy="cycle-clone-title" className="cycle-modal cycle-clone-modal" onDismiss={() => { if (!cloneSaving) setCloneSource(null); }}>
        <div className="modal-head"><div><h2 id="cycle-clone-title">Clone เป็น Test Cycle ใหม่</h2><small>เลือก Release, Build และ Environment เป้าหมาย โดย Cycle เดิมจะไม่ถูกแก้ไข</small></div><button disabled={cloneSaving} aria-label="ปิดหน้าต่าง Clone Test Cycle" onClick={() => setCloneSource(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
        {cloneError && <div className="inline-alert error" role="alert"><span>{cloneError}</span></div>}
        <div className="clone-source-summary"><div><span>Source Snapshot</span><strong>{cloneSource.cycleCode}</strong><small>{cloneSource.cycleName}</small></div><div><span>เดิม</span><b>{cloneSource.releaseCode} · Build {cloneSource.buildNumber}</b><small>{cloneSource.environmentName} · {cloneSource.caseCount} Test Cases · {cloneSource.status}</small></div></div>
        <div className="cycle-form-columns"><section className="modal-section"><h3 className="modal-section-title">Target Release Scope</h3><div className="form-grid"><label>Release <span className="required">*</span><select value={cloneReleaseId} disabled={cloneSaving} onChange={(event) => { setCloneReleaseId(event.target.value); setCloneBuildId(""); }}><option value="">เลือก Release</option>{cloneReleases.map((item) => <option key={item.releaseId} value={item.releaseId}>{item.releaseCode}{item.version ? ` · ${item.version}` : ""}</option>)}</select></label><label>Build <span className="required">*</span><select value={cloneBuildId} disabled={cloneSaving || !cloneReleaseId} onChange={(event) => setCloneBuildId(event.target.value)}><option value="">เลือก Build</option>{cloneBuilds.map((item) => <option key={item.buildId} value={item.buildId}>{item.buildNumber}{item.applicationVersion ? ` · App ${item.applicationVersion}` : ""}</option>)}</select></label><label>Environment <span className="required">*</span><select value={cloneEnvironmentId} disabled={cloneSaving} onChange={(event) => setCloneEnvironmentId(event.target.value)}><option value="">เลือก Environment</option>{cloneEnvironments.map((item) => <option key={item.testEnvironmentId} value={item.testEnvironmentId}>{item.environmentName}</option>)}</select></label><div className="clone-mode-field"><span>วิธีคัดลอก Test Cases</span><label className="clone-mode-option"><input type="radio" name="clone-mode" value="SourceSnapshot" checked={cloneMode === "SourceSnapshot"} disabled={cloneSaving} onChange={(event) => setCloneMode(event.target.value)} /><span><b>Source Snapshot</b><small>คัดลอกชุด Case และ Revision เดิม ({cloneSource.caseCount} รายการ)</small></span></label><label className="clone-mode-option"><input type="radio" name="clone-mode" value="SuiteLatest" checked={cloneMode === "SuiteLatest"} disabled={cloneSaving || !cloneSource.testSuiteId} onChange={(event) => setCloneMode(event.target.value)} /><span><b>Suite Latest</b><small>{cloneSource.testSuiteId ? "ใช้สมาชิกและ Revision ล่าสุดจาก Suite เดิม" : "ใช้ได้เมื่อ Source มี Test Suite"}</small></span></label></div></div></section><section className="modal-section"><h3 className="modal-section-title">รายละเอียด Cycle ใหม่</h3><div className="form-grid"><label>Cycle Code<small>เว้นว่างเพื่อให้ระบบสร้างรหัสอัตโนมัติ</small><input value={cloneCode} disabled={cloneSaving} onChange={(event) => setCloneCode(event.target.value)} placeholder="เช่น PRJ-CYC-003" /></label><label>Cycle Name <span className="required">*</span><input value={cloneName} disabled={cloneSaving} onChange={(event) => setCloneName(event.target.value)} /></label><div className="form-row"><label>Cycle Type<select value={cloneCycleType} disabled={cloneSaving} onChange={(event) => setCloneCycleType(event.target.value)}>{masterOptionElements(cycleTypes, cloneCycleType)}</select></label><label>Owner<select value={cloneOwnerUserId} disabled={cloneSaving} onChange={(event) => setCloneOwnerUserId(event.target.value)}><option value="">ไม่ระบุ</option>{users.map((user) => <option key={user.userId} value={user.userId}>{user.displayName}</option>)}</select></label></div><div className="form-row"><label>Start Date<input type="date" value={cloneStartDate} disabled={cloneSaving} onChange={(event) => setCloneStartDate(event.target.value)} /></label><label>End Date<input type="date" value={cloneEndDate} disabled={cloneSaving} onChange={(event) => setCloneEndDate(event.target.value)} /></label></div><label className="full">Notes<textarea rows={3} value={cloneNotes} disabled={cloneSaving} onChange={(event) => setCloneNotes(event.target.value)} /></label></div></section></div>
        <div className="clone-target-note"><span className="material-symbols-outlined" aria-hidden="true">info</span><span>ผลลัพธ์จะเป็น Cycle Draft, Case NotRun และไม่คัดลอก Execution, Evidence หรือ Assignment</span></div><div className="modal-actions"><button className="btn" disabled={cloneSaving} onClick={() => setCloneSource(null)}>ยกเลิก</button><button className="btn primary" disabled={cloneSaving || !cloneReleaseId || !cloneBuildId || !cloneEnvironmentId || !cloneName.trim()} onClick={clone}>{cloneSaving ? "กำลัง Clone..." : "สร้าง Cycle ใหม่"}</button></div>
      </ModalShell>}
    </>
  );
}
