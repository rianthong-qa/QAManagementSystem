import { useState, useMemo, useEffect, useLayoutEffect, useRef } from "react";
import { CyclePicker } from "../components/CyclePicker";
import { type WorkspaceDefect, type WorkspaceDefectContext, ExecutionDefectEditor } from "../ExecutionDefectEditor";
import { apiUrl, getJson, isAbortError } from "../api";
import { confirmDialog, notify } from "../components/dialogStore";
import { type StepStatus, calculateOverallResult } from "../overallResult";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
import { formatThaiDateTime } from "../dateTime";
import { type ModuleItem, type SessionUser, type TestCycleItem, currentUserId, defectStatusTones, priorityTone, renderModuleSelectOptions } from "../shared/appShared";

// เหตุผลของ Skip Test Case (test-case-execution-ui-spec.md §18) — ไม่มีคอลัมน์ DB แยกเก็บ Reason
// เลยเข้ารหัสรวมไว้ใน Comment field เดิมตอนส่ง (ดู confirmSkip ใน ExecutionWorkspacePage)
const skipReasonOptions = [
  { value: "RequirementChanged", label: "Requirement Changed" },
  { value: "NotApplicable", label: "Not Applicable" },
  { value: "FeatureRemoved", label: "Feature Removed" },
  { value: "EnvironmentLimitation", label: "Environment Limitation" },
  { value: "DuplicateTestCase", label: "Duplicate Test Case" },
  { value: "Other", label: "Other" },
];
// สีของ Badge สถานะผลการทดสอบ (Test Execution) — ใช้ชุดสีเดียวกับ Dashboard's statusDistribution
// (Pass=เขียว, Fail=แดง, Blocked=เหลือง, Skipped=ม่วง, NotRun=เทา) ให้สื่อความหมายตรงกันทั้งระบบ
function executionStatusTone(status: string): string {
  switch (status) {
    case "Pass": return "green";
    case "Fail": return "red";
    case "Blocked": return "yellow";
    case "Skipped": return "purple";
    case "InProgress": return "blue";
    default: return "gray"; // NotRun
  }
}
const isOpenDefectStatus = (status: string) => !["Resolved", "Closed", "Rejected"].includes(status);
type ExecutionCase = {
  testCycleCaseId: string;
  testCaseId: string;
  testCaseCode: string;
  title: string;
  preconditions?: string;
  priority: string;
  moduleId?: string;
  currentStatus: string;
  executionOrder: number;
  assignedTesterUserId?: string | null;
  steps: {
    stepNo: number;
    action: string;
    testData?: string;
    expectedResult: string;
    lastStatus?: string;
    lastActualResult?: string;
  }[];
  history: {
    testExecutionId: string;
    executionNo: number;
    status: string;
    actualResult?: string;
    comment?: string;
    testerName: string;
    completedAt?: string;
    stepResults?: { stepNo: number; status: string; actualResult?: string | null }[];
  }[];
};
// โหลด Cycle ที่ InProgress ทีละหน้า (server จำกัด size สูงสุด 500) จนครบ total — ใช้ทั้งตอนโหลดรายการและตอนรีเฟรช %
const cyclePageSize = 500;
async function loadInProgressCycles(moduleId: string): Promise<TestCycleItem[]> {
  const h = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
  const query = `?status=InProgress&size=${cyclePageSize}${moduleId ? `&moduleId=${moduleId}` : ""}`;
  const loadPage = async (page: number, acc: TestCycleItem[]): Promise<TestCycleItem[]> => {
    const r = await fetch(`${apiUrl}/test-cycles${query}&page=${page}`, { headers: h });
    if (!r.ok) throw new Error(`โหลด Test Cycle ไม่สำเร็จ (${r.status})`);
    const data: unknown = await r.json();
    if (Array.isArray(data)) return data as TestCycleItem[];
    const items = (data as { items?: { total?: number; rows?: TestCycleItem[] } })?.items;
    const rows = [...acc, ...(items?.rows ?? [])];
    return (items?.rows?.length ?? 0) === cyclePageSize && rows.length < (items?.total ?? 0) ? loadPage(page + 1, rows) : rows;
  };
  return loadPage(1, []);
}
// อัปเดตเฉพาะตัวเลขความคืบหน้าของ Cycle ที่มีอยู่แล้ว (ไม่เพิ่ม/ลบรายการ ไม่เปลี่ยน Cycle ที่เลือก) — คืน array เดิมถ้าไม่มีอะไรเปลี่ยน
// เพื่อไม่ให้ effect ที่ขึ้นกับ cycles ทำงานซ้ำโดยไม่จำเป็น; สูตรเดียวกับ server: (ไม่ใช่ NotRun) / ทั้งหมด ปัด 2 ตำแหน่ง
type CycleProgress = { executedCount: number; caseCount: number };
const progressPercentOf = ({ executedCount, caseCount }: CycleProgress) => caseCount ? Math.round(executedCount * 10000 / caseCount) / 100 : 0;
function mergeCycleProgress(current: TestCycleItem[], updates: Map<string, CycleProgress>): TestCycleItem[] {
  let changed = false;
  const next = current.map((c) => {
    const u = updates.get(c.testCycleId);
    if (!u || (u.executedCount === c.executedCount && u.caseCount === c.caseCount)) return c;
    changed = true;
    return { ...c, executedCount: u.executedCount, caseCount: u.caseCount, progressPercent: progressPercentOf(u) };
  });
  return changed ? next : current;
}
type ExecutionWorkspace = {
  testCycleId: string;
  cycleCode: string;
  cycleName: string;
  status: string;
  buildNumber: string;
  environmentName: string;
  cases: ExecutionCase[];
};
export function ExecutionWorkspacePage({ contextProjectId, contextReleaseId, contextBuildId }: { contextProjectId?: string; contextReleaseId?: string; contextBuildId?: string }) {
  const [cycles, setCycles] = useState<TestCycleItem[]>([]),
    [cyclesLoaded, setCyclesLoaded] = useState(false),
    // Cycle ที่หน้าอื่นส่งมา (qa.targetCycleId) มาก่อน ไม่งั้นใช้ Cycle ล่าสุดที่เปิด — เริ่มโหลด workspace ได้ทันทีพร้อมรายการ Cycle
    // ไม่ต้องรอรายการเสร็จก่อน (ทุกคำขอผ่าน Cloudflare ~0.2–1 วินาที); ถ้า Cycle นั้นไม่อยู่ในรายการแล้ว effect ด้านล่างจะเปลี่ยนให้เอง
    [cycleId, setCycleId] = useState(()=>{ try { return localStorage.getItem("qa.targetCycleId") ?? localStorage.getItem("qa.execution.lastCycleId") ?? ""; } catch { return ""; } }),
    [workspace, setWorkspace] = useState<ExecutionWorkspace | null>(null),
    [selectedId, setSelectedId] = useState(""),
    [caseDetail, setCaseDetail] = useState<ExecutionCase | null>(null),
    [stepStatuses, setStepStatuses] = useState<Record<number, string>>({}),
    [stepActuals, setStepActuals] = useState<Record<number, string>>({}),
    [actual, setActual] = useState(""),
    [comment, setComment] = useState(""),
    [caseSearch, setCaseSearch] = useState(""),
    [statusFilter, setStatusFilter] = useState("All"),
    [cycleModuleFilter, setCycleModuleFilter] = useState(""),
    [cycleModules, setCycleModules] = useState<ModuleItem[]>([]),
    // ปิดเป็นค่าเริ่มต้นเสมอ (ไม่กรอง) — Test Cycle ไม่มีช่องให้กำหนด "ผู้ดำเนินการ" (ownerUserId) ตอนสร้าง/แก้ไข
    // เลยเป็น null เสมอทุก Cycle ในระบบ ถ้า default เปิดไว้จะกรองจนไม่เหลือ Cycle ให้เลือกเลยสำหรับทุกคน
    [myCyclesOnly, setMyCyclesOnly] = useState(false),
    // คิว: แสดงเฉพาะเคสที่ assign ให้ฉัน (AssignedTesterUserId) — ปุ่มแสดงเมื่อ Cycle นี้มีการ assign อย่างน้อย 1 เคส
    [queueMineOnly, setQueueMineOnly] = useState(false),
    [workspaceLoading, setWorkspaceLoading] = useState(false),
    [saving, setSaving] = useState(false),
    [reload, setReload] = useState(0),
    // สถานะสำหรับ Skip Test Case modal (§18) และปุ่ม Create Defect ต่อ Step (§19)
    [skipModalOpen, setSkipModalOpen] = useState(false),
    [skipReason, setSkipReason] = useState(""),
    [skipComment, setSkipComment] = useState(""),
    [linkedDefects, setLinkedDefects] = useState<WorkspaceDefect[]>([]),
    [linkedDefectsError, setLinkedDefectsError] = useState(""),
    // testCaseId ที่ linkedDefects ชุดปัจจุบันเป็นของ — ใช้รอให้ Defect โหลดเสร็จพร้อมรายละเอียดเคสแล้วค่อยสลับหน้าจอทีเดียว
    [linkedDefectsFor, setLinkedDefectsFor] = useState<string | undefined>(undefined),
    [defectEditor, setDefectEditor] = useState<{ context: WorkspaceDefectContext; existing?: WorkspaceDefect } | null>(null),
    // Defect code ที่เพิ่งสร้างจาก step นี้ในรอบทำงานปัจจุบัน — เหมือน defectCodes เดิม ยังต้องเก็บแยก
    // เป็น local state เพราะ Defect ไม่มีคอลัมน์ stepNo ให้ map กลับจาก linkedDefects ที่ persist จริงได้
    [stepDefectCodes, setStepDefectCodes] = useState<Record<number, string>>({}),
    [deletingHistoryId, setDeletingHistoryId] = useState<string | null>(null),
    // สถานะ error แยกต่อจุดโหลดข้อมูล (spec §4.2) — เพื่อแยกให้ชัดว่า "ยังไม่มี Cycle ให้เลือก" กับ
    // "โหลดข้อมูลไม่สำเร็จจริงๆ" เป็นคนละกรณี ไม่ให้ error เงียบๆ กลายเป็น empty state ธรรมดา
    [cycleLoadError, setCycleLoadError] = useState(""),
    [workspaceLoadError, setWorkspaceLoadError] = useState(""),
    [caseDetailLoadError, setCaseDetailLoadError] = useState("");
  const currentUser = useMemo(() => { try { return JSON.parse(localStorage.getItem("qa.user") ?? "{}") as SessionUser; } catch { return null; } }, []);
  const canEditDefect = Boolean(currentUser?.roles?.includes("SYS_ADMIN") || currentUser?.permissions?.includes("DEFECT.EDIT"));
  const headers = {
    "Content-Type": "application/json",
    Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
  };
  const lastProgressRefresh = useRef(0), progressRefreshing = useRef(false);
  // Project ของ Cycle เป็น string คงที่ — effect โหลด Module ไม่ต้องทำงานซ้ำเมื่อแค่ % ของ Cycle เปลี่ยน
  const cycleProjectKey = useMemo(() => [...new Set(cycles.map((x) => x.projectId))].sort().join(","), [cycles]);
  useEffect(() => {
    // ขอเฉพาะ InProgress จาก server — เดิมขอ 100 Cycle ล่าสุดทุกสถานะแล้วกรองในหน้าเว็บ ทำให้ Cycle ที่ InProgress แต่เก่ากว่า
    // 100 รายการล่าสุดไม่โผล่ใน dropdown เลย (ตรวจ 2026-10-20: PMX2 มี InProgress 191 รายการ หายไป 91)
    // server จำกัด size สูงสุด 500 — ขอทีละหน้าจนครบ total ไม่งั้น Cycle เกิน 500 รายการจะหายเงียบ ๆ อีก
    setCyclesLoaded(false);
    setCycleLoadError("");
    lastProgressRefresh.current = Date.now();
    loadInProgressCycles(cycleModuleFilter)
      .then((data: TestCycleItem[]) => {
        // Only offer cycles that are actively being executed — Draft hasn't started yet, and
        // Completed/Closed/Cancelled have no more work to do, so none of them belong in this dropdown.
        // "เฉพาะฉัน" further narrows to cycles created by the logged-in user — toggleable,
        // since a lead/admin may still need to see everyone's cycles. (Not ownerUserId: Test Cycle has
        // no "ผู้ดำเนินการ" field in the create/edit form, so that column is always null for every cycle.)
        const myId = currentUserId();
        const openCycles = data.filter((x) => x.status === "InProgress" && (!myCyclesOnly || x.createdBy === myId));
        setCycles(openCycles);
        setCyclesLoaded(true);
        setCycleId((current) => openCycles.some(x=>x.testCycleId===current)?current:(openCycles[0]?.testCycleId||""));
        localStorage.removeItem("qa.targetCycleId");
      })
      .catch((e) => {
        setCycles([]);
        setCyclesLoaded(true);
        setCycleId("");
        setCycleLoadError(e instanceof Error ? e.message : "โหลด Test Cycle ไม่สำเร็จ");
      });
    // % ของ Cycle อัปเดตแยกด้วย mergeCycleProgress (หลังโหลด workspace และตอนเปิด dropdown)
    // ไม่ผูกกับ reload — บันทึก/ลบผลไม่ได้เปลี่ยนรายการ Cycle แต่ถ้าโหลดใหม่ cycles จะเป็น array ใหม่ ทำให้โหลด Module
    // ของทุก Project ซ้ำและ effect เลือก Cycle ทำงานอีกรอบ ดูเหมือนหน้ารีโหลดเองทุกครั้งที่กดบันทึก
  }, [cycleModuleFilter, myCyclesOnly]);
  useEffect(() => {
    const h = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
    // Keep the filter options stable while the cycle list is being reloaded
    // for the selected module; otherwise the selected result would empty the
    // source used to build the Module dropdown and disable it.
    if (cycleModuleFilter) return;
    const projectIds = contextProjectId ? [contextProjectId] : cycleProjectKey.split(",").filter(Boolean);
    if (!projectIds.length) { setCycleModules([]); return; }
    Promise.all(projectIds.map((id) => fetch(`${apiUrl}/projects/${id}/modules`, { headers: h }).then((r) => r.ok ? r.json() : [])))
      .then((groups: ModuleItem[][]) => {
        const seen = new Map<string, ModuleItem>();
        groups.flat().filter((m) => m.isActive).forEach((m) => { if (!seen.has(m.moduleId)) seen.set(m.moduleId, m); });
        setCycleModules([...seen.values()].sort((a, b) => a.moduleCode.localeCompare(b.moduleCode)));
      });
  }, [contextProjectId, cycleProjectKey, cycleModuleFilter]);
  // Cycle ที่ workspace ปัจจุบันเป็นของ — ใช้แยก "เปลี่ยน Cycle" (แสดงสถานะกำลังโหลด) ออกจาก
  // "reload หลังบันทึกผล" (refetch เงียบๆ คง Test Case ที่เลือกและตำแหน่ง scroll ไว้)
  // เปลี่ยน Cycle แล้วไม่ล้างหน้าจอ: คงข้อมูล Cycle เดิมไว้ (หรี่ลง) จนข้อมูลใหม่มาครบแล้วสลับทีเดียว — เดิมล้างเป็น spinner
  // แล้วค่อยเติมคิว/รายละเอียดทีละส่วน ทำให้ความสูงหน้ายุบแล้วขยาย หน้าจอเด้งไปมา (state รายเคสถูกตั้งใหม่ตอนเคสใหม่พร้อม)
  const workspaceCycleRef = useRef("");
  useEffect(() => {
    const cycleChanged = workspaceCycleRef.current !== cycleId;
    workspaceCycleRef.current = cycleId;
    if (cycleChanged) setDefectEditor(null);
    setWorkspaceLoadError("");
    if (!cycleId) {
      // ไม่มี Cycle ให้เลือกจริง ๆ (รายการว่าง/โหลดไม่สำเร็จ) — ถึงตอนนี้จึงล้างหน้าจอ
      setWorkspace(null);
      setWorkspaceLoading(false);
      return;
    }
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    const controller = new AbortController();
    if (cycleChanged) setWorkspaceLoading(true);
    fetch(`${apiUrl}/test-cycles/${cycleId}/execution`, { headers: h, signal: controller.signal })
      .then((r) => {
        if (!r.ok) throw new Error(`โหลด Execution Workspace ไม่สำเร็จ (${r.status})`);
        return r.json();
      })
      .then((data: ExecutionWorkspace | null) => {
        if (controller.signal.aborted) return;
        setWorkspace(data);
        // % ใน dropdown ของ Cycle นี้ให้ตรงกับข้อมูลที่เพิ่งโหลด (รวมหลังบันทึกผล ซึ่งโหลด workspace ใหม่ผ่าน reload)
        if (data) setCycles((current) => mergeCycleProgress(current, new Map([[cycleId, { caseCount: data.cases.length, executedCount: data.cases.filter((x) => x.currentStatus !== "NotRun").length }]])));
        setSelectedId((current) =>
          data?.cases.some((x) => x.testCycleCaseId === current)
            ? current
            : (data?.cases[0]?.testCycleCaseId ?? ""),
        );
      })
      .catch((e) => {
        if (controller.signal.aborted) return;
        setWorkspace(null);
        setWorkspaceLoadError(e instanceof Error ? e.message : "โหลด Execution Workspace ไม่สำเร็จ");
      })
      .finally(() => { if (!controller.signal.aborted) setWorkspaceLoading(false); });
    return () => controller.abort();
  }, [cycleId, reload]);
  // แคชรายละเอียดเคส (รวมเคสถัดไปที่โหลดไว้ล่วงหน้า) — ล้างเมื่อเปลี่ยน Cycle หรือหลังบันทึก/ลบผล (reload)
  // generation กันผล prefetch ที่ค้างอยู่จากก่อนล้างแคชมาใส่ทับข้อมูลใหม่
  const detailCache = useRef(new Map<string, ExecutionCase>());
  const detailCacheGeneration = useRef(0);
  useEffect(() => { detailCache.current.clear(); detailCacheGeneration.current += 1; }, [cycleId, reload]);
  useEffect(() => {
    if (!selectedId || !cycleId) { setCaseDetail(null); setCaseDetailLoadError(""); return; }
    const cached = detailCache.current.get(selectedId);
    if (cached) { setCaseDetail(cached); setCaseDetailLoadError(""); return; }
    const h = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
    const controller = new AbortController();
    const generation = detailCacheGeneration.current;
    setCaseDetailLoadError("");
    fetch(`${apiUrl}/test-cycles/${cycleId}/cases/${selectedId}`, { headers: h, signal: controller.signal })
      .then(r => {
        if (!r.ok) throw new Error(`โหลดรายละเอียด Test Case ไม่สำเร็จ (${r.status})`);
        return r.json();
      })
      .then((data: ExecutionCase) => {
        if (generation === detailCacheGeneration.current) detailCache.current.set(selectedId, data);
        if (!controller.signal.aborted) setCaseDetail(data);
      })
      .catch((e) => {
        if (controller.signal.aborted) return;
        setCaseDetail(null);
        setCaseDetailLoadError(e instanceof Error ? e.message : "โหลดรายละเอียด Test Case ไม่สำเร็จ");
      });
    return () => controller.abort();
  }, [selectedId, cycleId, reload]);
  useEffect(() => { if (cycleId) { try { localStorage.setItem("qa.execution.lastCycleId", cycleId); } catch { /* ไม่มี storage ก็แค่ไม่จำ */ } } }, [cycleId]);
  // testCaseId มีอยู่ในรายการเคสของ workspace แล้ว — โหลด Defect ที่เชื่อมโยงพร้อมกับรายละเอียดเคสได้เลย (เดิมรอรายละเอียดเคสโหลดเสร็จก่อน)
  const selectedTestCaseId = workspace?.cases.find((x) => x.testCycleCaseId === selectedId)?.testCaseId;
  useEffect(() => {
    const testCaseId = selectedTestCaseId;
    if (!testCaseId) { setLinkedDefects([]); setLinkedDefectsFor(undefined); return; }
    const controller = new AbortController();
    setLinkedDefectsError("");
    // โหลดไม่สำเร็จต้องบอก — เดิมคืน [] แล้วดูเหมือนเคสนี้ไม่มี Defect ที่เชื่อมไว้
    getJson<WorkspaceDefect[]>(`${apiUrl}/defects/by-test-case/${testCaseId}`, controller.signal)
      .then((rows) => { if (!controller.signal.aborted) { setLinkedDefects(rows); setLinkedDefectsFor(testCaseId); } })
      .catch((e) => { if (!controller.signal.aborted && !isAbortError(e)) { setLinkedDefects([]); setLinkedDefectsFor(testCaseId); setLinkedDefectsError("โหลด Defect ที่เชื่อมกับ Test Case นี้ไม่สำเร็จ"); } });
    return () => controller.abort();
  }, [selectedTestCaseId, reload]);
  // เคสที่แสดงอยู่: รอรายละเอียด + Linked Defects ของเคสใหม่มาครบก่อนแล้วสลับทีเดียว — ระหว่างรอคงเคสเดิมไว้ (หรี่ลง)
  // เดิมแสดงเคสใหม่แบบไม่มี Step ก่อน แล้ว Step/Defect ค่อยตามมา ความสูงพื้นที่ทดสอบยุบแล้วขยาย หน้าเด้ง
  // Linked Defects สลับพร้อมเคสในชุดเดียวกัน — ไม่งั้นช่วงที่ Defect ของเคสใหม่มาก่อน เคสเดิมบนจอจะแสดง Defect ของเคสใหม่
  // โหลดรายละเอียดเคสใหม่ไม่สำเร็จ = เลิกแสดงเคสเดิม (แสดงข้อความ error แทน) ไม่ให้ค้างเคสเก่าที่ยังกดแก้ผลได้แต่บันทึกไม่ได้
  type ShownCase = { item?: ExecutionCase; defects: WorkspaceDefect[]; defectsError: string };
  const shownCaseRef = useRef<ShownCase>({ defects: [], defectsError: "" });
  const shown = useMemo(
    () => {
      const base = workspace?.cases.find((x) => x.testCycleCaseId === selectedId);
      if (base && caseDetail && caseDetail.testCycleCaseId === selectedId && linkedDefectsFor === base.testCaseId) {
        shownCaseRef.current = { item: { ...base, ...caseDetail }, defects: linkedDefects, defectsError: linkedDefectsError };
      } else if (!workspace?.cases.length || caseDetailLoadError) {
        shownCaseRef.current = { defects: [], defectsError: "" };
      }
      return shownCaseRef.current;
    },
    [workspace, selectedId, caseDetail, linkedDefectsFor, linkedDefects, linkedDefectsError, caseDetailLoadError],
  );
  const selected = shown.item;
  const selectedDefects = shown.defects;
  const selectedDefectsError = shown.defectsError;
  const executionStats = useMemo(() => {
    const cases = workspace?.cases ?? [];
    return {
      total: cases.length,
      passed: cases.filter((x) => x.currentStatus === "Pass").length,
      failed: cases.filter((x) => x.currentStatus === "Fail").length,
      blocked: cases.filter((x) => x.currentStatus === "Blocked").length,
      skipped: cases.filter((x) => x.currentStatus === "Skipped").length,
      inProgress: cases.filter((x) => x.currentStatus === "InProgress").length,
      pending: cases.filter((x) => x.currentStatus === "NotRun").length,
    };
  }, [workspace]);
  const myUserId = currentUserId();
  const filteredCases = useMemo(() => {
    const query = caseSearch.trim().toLocaleLowerCase("th-TH");
    return (workspace?.cases ?? [])
      .filter((item) =>
        (statusFilter === "All" || item.currentStatus === statusFilter) &&
        (!queueMineOnly || item.assignedTesterUserId === myUserId) &&
        (!query || `${item.testCaseCode} ${item.title}`.toLocaleLowerCase("th-TH").includes(query)),
      )
      .sort((a, b) =>
        a.testCaseCode.localeCompare(b.testCaseCode, undefined, { numeric: true, sensitivity: "base" }) ||
        a.testCycleCaseId.localeCompare(b.testCycleCaseId),
      );
  }, [workspace, caseSearch, statusFilter, queueMineOnly, myUserId]);
  const hasAssignedCases = useMemo(() => (workspace?.cases ?? []).some((x) => x.assignedTesterUserId), [workspace]);
  useEffect(() => { if (!hasAssignedCases) setQueueMineOnly(false); }, [hasAssignedCases]);
  // โหลดรายละเอียดเคสถัดไปไว้ล่วงหน้าเมื่อเคสปัจจุบันแสดงแล้ว — กด "ถัดไป" แล้วขึ้นทันทีไม่ต้องรอผ่าน Cloudflare
  // ไม่ abort คำขอนี้ (ผลยังใช้ได้แม้ผู้ใช้เปลี่ยนเคส) — ลดการยกเลิกคำสั่ง SQL กลางทาง
  useEffect(() => {
    if (!cycleId || !caseDetail || caseDetail.testCycleCaseId !== selectedId) return;
    const index = filteredCases.findIndex((x) => x.testCycleCaseId === selectedId);
    const next = index >= 0 ? filteredCases[index + 1] : undefined;
    if (!next || detailCache.current.has(next.testCycleCaseId)) return;
    const generation = detailCacheGeneration.current;
    getJson<ExecutionCase>(`${apiUrl}/test-cycles/${cycleId}/cases/${next.testCycleCaseId}`)
      .then((data) => { if (generation === detailCacheGeneration.current) detailCache.current.set(next.testCycleCaseId, data); })
      .catch(() => { /* prefetch ไม่สำเร็จ = ตอนเลือกเคสจะโหลดตามปกติ */ });
  }, [cycleId, caseDetail, selectedId, filteredCases]);
  useLayoutEffect(() => {
    if (selected) {
      // Restore the last saved values for this case instead of always resetting to blank — otherwise
      // switching cases (or any background refetch) silently discards previously recorded results.
      setStepStatuses(
        Object.fromEntries(selected.steps.map((x) => [x.stepNo, x.lastStatus ?? "NotRun"])),
      );
      setStepActuals(
        Object.fromEntries(selected.steps.filter((x) => x.lastActualResult).map((x) => [x.stepNo, x.lastActualResult as string])),
      );
      const latest = selected.history[0];
      setActual(latest?.actualResult ?? "");
      setComment(latest?.comment ?? "");
      setSkipModalOpen(false);
      setSkipReason("");
      setSkipComment("");
    }
  }, [selected]);
  // กำลังสลับไปเคส/Cycle อื่น — หน้าจอยังแสดงของเดิม จึงต้องปิดการบันทึก/ปุ่มลัดไว้จนของใหม่พร้อม
  const filteredCycles = useMemo(() => cycles.filter((x) =>
    (!contextProjectId || x.projectId === contextProjectId) &&
    (!contextReleaseId || x.releaseId === contextReleaseId) &&
    (!contextBuildId || x.buildId === contextBuildId)
  ), [cycles, contextProjectId, contextReleaseId, contextBuildId]);
  // เปิด dropdown = ดึง % ล่าสุดของทุก Cycle แบบเงียบ ๆ (รวมผลที่คนอื่นบันทึก) — เว้นอย่างน้อย 15 วินาทีต่อรอบและไม่ซ้อนกัน
  const refreshCycleProgress = () => {
    if (progressRefreshing.current || Date.now() - lastProgressRefresh.current < 15_000) return;
    progressRefreshing.current = true;
    lastProgressRefresh.current = Date.now();
    loadInProgressCycles(cycleModuleFilter)
      .then((rows) => setCycles((current) => mergeCycleProgress(current, new Map(rows.map((x) => [x.testCycleId, { executedCount: x.executedCount, caseCount: x.caseCount }])))))
      .catch(() => { /* ไม่สำเร็จ = คงตัวเลขเดิม เปิดครั้งหน้าค่อยลองใหม่ */ })
      .finally(() => { progressRefreshing.current = false; });
  };
  const workspaceSwitching = workspaceLoading && !!workspace;
  // Cycle ที่เปิดทันทีจาก qa.execution.lastCycleId/qa.targetCycleId ก่อนรายการ Cycle มา — ยังไม่รู้ว่าอยู่ใน context ปัจจุบันและยัง InProgress
  // จึงให้ดูได้แต่ยังบันทึก/ใช้ปุ่มลัดไม่ได้ จนรายการยืนยัน (ถ้าไม่อยู่ในรายการ effect ด้านล่างจะเปลี่ยน Cycle ให้)
  const cycleUnconfirmed = !!cycleId && !filteredCycles.some((x) => x.testCycleId === cycleId);
  const caseSwitching = workspaceSwitching || cycleUnconfirmed || (!!selected && selected.testCycleCaseId !== selectedId);
  // UI รอบ 2: เปลี่ยนเคสแล้ว effect ด้านบนคืนค่าที่บันทึกล่าสุด — ผลที่กรอกแต่ยังไม่บันทึกจะหายเงียบ ๆ จึงถามก่อน
  const hasUnsavedResults = !!selected && (
    selected.steps.some((step) => (stepStatuses[step.stepNo] ?? "NotRun") !== (step.lastStatus ?? "NotRun") || (stepActuals[step.stepNo] ?? "") !== (step.lastActualResult ?? ""))
    || actual !== (selected.history[0]?.actualResult ?? "") || comment !== (selected.history[0]?.comment ?? ""));
  const UNSAVED_SWITCH_CONFIRM = "ผลการทดสอบของเคสนี้ยังไม่ได้บันทึก — ถ้าเปลี่ยนไปเคสอื่น ผลที่กรอกไว้จะหาย ต้องการเปลี่ยนหรือไม่?";
  const selectCase = async (id: string) => {
    if (id === selectedId) return;
    if (hasUnsavedResults && !await confirmDialog(UNSAVED_SWITCH_CONFIRM)) return;
    setSelectedId(id);
  };
  // ล้าง Defect code รายสเต็ปเฉพาะตอนเปลี่ยน Test Case — ถ้าล้างทุกครั้งที่ selected เปลี่ยน (รวม reload
  // หลังบันทึก) ปุ่ม "+ Defect" จะกลับมาบนสเต็ปที่เพิ่งสร้าง Defect ไปแล้ว ชวนให้สร้างซ้ำ
  useEffect(() => { setStepDefectCodes({}); }, [selected?.testCycleCaseId]);
  // แท็บของพื้นที่ทดสอบ (บันทึกผล | ประวัติ) — เปลี่ยนเคสแล้วกลับมาที่บันทึกผลเสมอ; ค่าที่กรอกอยู่ใน state ของหน้า ไม่หายเมื่อสลับแท็บ
  const [mainTab, setMainTab] = useState<"steps" | "history">("steps");
  // Step ที่เลือกด้วยคีย์บอร์ด (↑/↓ เลื่อน, 1–4 ตั้งผล) — null = ยังไม่ได้เลือก กด ↓ ครั้งแรกไป Step แรก
  const [activeStep, setActiveStep] = useState<number | null>(null);
  // ผูกกับเคสที่ "แสดงอยู่" ไม่ใช่เคสที่เพิ่งคลิก — ไม่งั้นแท็บ/Step ที่เลือกของเคสเดิมรีเซ็ตก่อนเคสใหม่โหลดเสร็จ
  useEffect(() => { setMainTab("steps"); setActiveStep(null); }, [selected?.testCycleCaseId]);
  // เลื่อนจอตาม Step เฉพาะตอนใช้คีย์บอร์ด — คลิกแถวเองไม่ต้องเลื่อน (เดิมเลื่อนทุกครั้ง หน้าจอขยับตอนคลิก)
  const scrollToActiveStep = useRef(false);
  useEffect(() => {
    if (activeStep == null || !scrollToActiveStep.current) return;
    scrollToActiveStep.current = false;
    document.getElementById(`step-row-${activeStep}`)?.scrollIntoView({ block: "nearest" });
  }, [activeStep]);
  // เคสใหม่พร้อมแล้ว: ถ้าหัวพื้นที่ทดสอบเลื่อนพ้นจอไปแล้ว (เลือกเคสจากท้ายคิว/กดถัดไปจาก Action Bar) ค่อย ๆ เลื่อนกลับไปหัวเคส
  const mainRef = useRef<HTMLElement | null>(null);
  const shownCaseId = selected?.testCycleCaseId;
  const lastShownCaseId = useRef<string | undefined>(undefined);
  useEffect(() => {
    const previous = lastShownCaseId.current;
    lastShownCaseId.current = shownCaseId;
    if (!previous || !shownCaseId || previous === shownCaseId) return;
    const main = mainRef.current;
    if (main && main.getBoundingClientRect().top < 0) main.scrollIntoView({ block: "start", behavior: "smooth" });
  }, [shownCaseId]);
  // เปลี่ยน Cycle แล้วคิวเริ่มที่บนสุด
  const queueListRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => { queueListRef.current?.scrollTo({ top: 0 }); }, [workspace?.testCycleId]);
  // Mobile: คิว Test Case พับได้ (EXECUTION-WORKSPACE-RESPONSIVE-UI-SPEC §3.2) — Desktop แสดงตลอด
  const [queueOpen, setQueueOpen] = useState(false);
  // Overall Result แบบ live พรีวิวจากสถานะ Step ปัจจุบันที่กำลังแก้ (test-case-execution-ui-spec.md §4-5)
  // — คำนวณด้วยฟังก์ชันเดียวกับที่ backend ใช้จริงตอนบันทึก (ดู overallResult.ts)
  const liveStepStatuses = useMemo(
    () => (selected?.steps ?? []).map((x) => (stepStatuses[x.stepNo] ?? "NotRun") as StepStatus),
    [selected, stepStatuses],
  );
  const liveOverall = useMemo(() => calculateOverallResult(liveStepStatuses), [liveStepStatuses]);
  const stepCounts = useMemo(() => ({
    passed: liveStepStatuses.filter((s) => s === "Pass").length,
    failed: liveStepStatuses.filter((s) => s === "Fail").length,
    blocked: liveStepStatuses.filter((s) => s === "Blocked").length,
    notRun: liveStepStatuses.filter((s) => s === "NotRun").length,
  }), [liveStepStatuses]);
  // เดิมมีปุ่ม Pass/Fail/Blocked ให้ผู้ใช้กดกำหนด Overall Result ของ Test Case เอง (finalize(status))
  // — เอาออกตาม spec §14/§25: Overall Result ต้องมาจากการคำนวณผล Step เท่านั้น (ยกเว้น Skipped)
  // เหลือ submitExecution กลางที่ทุกปุ่มใหม่ (Save Progress/Skip/Complete) เรียกใช้ร่วมกัน
  const submitExecution = async (status: string, opts?: { confirmMessage?: string; commentOverride?: string }) => {
    if (!selected || caseSwitching) return;
    if (workspace?.status === "Closed" || workspace?.status === "Cancelled") {
      notify(`Cycle นี้อยู่สถานะ ${workspace.status} แล้ว ไม่สามารถบันทึกผล Execution เพิ่มได้`, "error");
      return;
    }
    if (opts?.confirmMessage && !await confirmDialog(opts.confirmMessage)) return;
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
            comment: opts?.commentOverride ?? (comment || null),
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
      // ExecutionHistoryDto.CreatedDefectCode/ExistingDefectCode/DefectAutoCreateError มีให้ "ครั้งเดียว"
      // ตรงนี้เท่านั้น — GET .../cases/{id} (ที่ setReload ทำให้ refetch ใหม่) ไม่ได้ persist/ผูกกลับมาให้
      // query ซ้ำได้ทีหลัง เลยต้องแจ้งผู้ใช้ตรงนี้ทันที ไม่งั้นข้อมูลนี้จะหายไปเงียบๆ — ต้องแยก 3 กรณีให้
      // ชัดว่า auto-create สำเร็จ / ข้ามเพราะมี Defect เปิดอยู่แล้ว / ล้มเหลวจริง ไม่ให้ผู้ใช้เข้าใจว่า
      // ระบบไม่ทำงานทั้งที่ตั้งใจข้ามให้ (เช่น สร้าง Defect รายสเต็ปไว้ก่อนหน้าแล้ว)
      const result: { createdDefectCode?: string; existingDefectCode?: string; existingDefectStatus?: string; defectAutoCreateError?: string; reopenedDefectCode?: string; regressionOfDefectCode?: string } = await response.json();
      // Fail ซ้ำบน Defect ที่ Dev แก้แล้ว (Resolved) = Reopen ตัวเดิม; บน Defect ที่ปิดแล้ว = สร้างใหม่อ้างตัวเดิม (DEFECT_BUILD_LIFECYCLE.md)
      if (result.reopenedDefectCode) notify(`Reopen ${result.reopenedDefectCode} แล้ว — Defect ที่ Dev แก้ไว้ยัง Fail บน Build นี้`, "success");
      else if (result.createdDefectCode && result.regressionOfDefectCode) notify(`ระบบสร้าง Defect ${result.createdDefectCode} ให้อัตโนมัติ — ปัญหาเดิมของ ${result.regressionOfDefectCode} ที่ปิดไปแล้วกลับมาอีก`, "success");
      else if (result.createdDefectCode) notify(`ระบบสร้าง Defect ${result.createdDefectCode} ให้อัตโนมัติ เนื่องจากผลเป็น Fail`, "success");
      else if (result.existingDefectCode && result.existingDefectStatus === "Resolved") notify(`ไม่ได้ Reopen ${result.existingDefectCode} — Dev แก้ไว้ใน Build ที่ใหม่กว่า Build ของ Cycle นี้ ให้ตรวจซ้ำบน Fix Build`, "error");
      else if (result.existingDefectCode) notify(`ไม่ได้สร้าง Defect ใหม่ให้อัตโนมัติ เนื่องจากมี Defect ${result.existingDefectCode} ที่ยังเปิดอยู่ผูกกับ Test Case นี้อยู่แล้ว`, "error");
      else if (result.defectAutoCreateError) notify(`คำเตือน: ${result.defectAutoCreateError}`, "error");
      setReload((x) => x + 1);
      if (status === "Skipped") { setSkipModalOpen(false); setSkipReason(""); setSkipComment(""); }
    } catch (e) {
      notify(e instanceof Error ? e.message : "บันทึกผลไม่สำเร็จ", "error");
    } finally {
      setSaving(false);
    }
  };
  // §15 Save Progress — บันทึกได้แม้ยังทดสอบไม่ครบทุก Step ไม่ต้องยืนยันซ้ำ (เป็นการบันทึกระหว่างทางบ่อยๆ)
  const saveProgress = () => submitExecution(liveOverall);
  // §16 Complete Test — ตรวจ validation ก่อนเสมอ: Fail/Blocked ต้องมี Actual Result, และถ้ายังมี Step
  // NotRun อยู่ต้องถามยืนยันก่อน (Default = Cancel ตาม native window.confirm)
  const completeTest = () => {
    if (!selected) return;
    const missingActual = selected.steps.filter((x) => {
      const st = stepStatuses[x.stepNo] ?? "NotRun";
      return (st === "Fail" || st === "Blocked") && !stepActuals[x.stepNo]?.trim();
    });
    if (missingActual.length) {
      notify(`กรุณาระบุผลที่เกิดขึ้นจริงสำหรับ Step ที่ยังไม่ได้กรอก: #${missingActual.map((x) => x.stepNo).join(", #")}`, "error");
      return;
    }
    const confirmMessage = stepCounts.notRun > 0
      ? `ยังมี Test Step ที่ยังไม่ได้ทดสอบจำนวน ${stepCounts.notRun} Step\n\nกด Cancel เพื่อกลับไปทดสอบต่อ หรือกด OK เพื่อบันทึกทั้งที่ยังไม่ครบ (Complete Anyway)`
      : `ยืนยันบันทึกผล ${liveOverall} สำหรับ ${selected.testCaseCode}?\nผลที่บันทึกแล้วจะไม่สามารถแก้ไขทับได้`;
    submitExecution(liveOverall, { confirmMessage });
  };
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      if (!selected || caseSwitching || skipModalOpen || defectEditor || target?.matches("input, textarea, select, [contenteditable=\"true\"]")) return;
      if (event.ctrlKey || event.metaKey || event.altKey) return;
      const key = event.key.toLowerCase();
      const stepNos = selected.steps.map((step) => step.stepNo);
      // ลูกศรขึ้น/ลงเลื่อน Step เฉพาะเมื่อเริ่มเลือก Step แล้ว และโฟกัสอยู่ในพื้นที่ทดสอบ (หรือไม่ได้อยู่ที่ control ใด) —
      // โฟกัสอยู่ที่ปุ่ม/ลิงก์นอกพื้นที่ทดสอบ (คิว, แท็บ, toolbar) ปล่อยให้ลูกศรทำงาน/เลื่อนหน้าตามปกติ
      const inSteps = target === document.body || !!(target && mainRef.current?.contains(target));
      if (mainTab === "steps" && (event.key === "ArrowDown" || event.key === "ArrowUp") && stepNos.length && activeStep != null && inSteps) {
        event.preventDefault();
        const index = activeStep == null ? -1 : stepNos.indexOf(activeStep);
        const nextIndex = event.key === "ArrowDown" ? Math.min(stepNos.length - 1, index + 1) : Math.max(0, index - 1);
        scrollToActiveStep.current = true;
        setActiveStep(stepNos[nextIndex]);
      } else if (mainTab === "steps" && ["1", "2", "3", "4"].includes(key) && activeStep != null) {
        event.preventDefault();
        const status = (["Pass", "Fail", "Blocked", "NotRun"] as const)[Number(key) - 1];
        setStepStatuses((s) => ({ ...s, [activeStep]: status }));
        // Pass/Not Run เลื่อนไป Step ถัดไปเลย; Fail/Blocked อยู่ที่เดิมเพื่อกรอก Actual Result ต่อ (Enter = ไปช่องกรอก)
        if (status === "Pass" || status === "NotRun") {
          const index = stepNos.indexOf(activeStep);
          if (index >= 0 && index < stepNos.length - 1) { scrollToActiveStep.current = true; setActiveStep(stepNos[index + 1]); }
        }
      } else if (mainTab === "steps" && event.key === "Enter" && activeStep != null && !target?.closest("button, a")) {
        event.preventDefault();
        document.getElementById(`step-actual-${activeStep}`)?.focus();
      } else if (["p", "f", "b"].includes(key)) {
        const status = key === "p" ? "Pass" : key === "f" ? "Fail" : "Blocked";
        event.preventDefault();
        // เหมือนปุ่ม "ตั้งทุก Step" — ถามก่อนเปลี่ยนผลทุก Step (เดิมปุ่มลัดเปลี่ยนทันทีโดยไม่ถาม)
        void (async () => { if (!await confirmDialog(`ต้องการเปลี่ยนผล Test Step ทั้งหมดเป็น ${status} หรือไม่?`)) return; setStepStatuses(Object.fromEntries(selected.steps.map((step) => [step.stepNo, status]))); })();
      } else if (key === "n") {
        const index = filteredCases.findIndex((item) => item.testCycleCaseId === selectedId);
        const next = index >= 0 ? filteredCases[index + 1] : undefined;
        if (next) { event.preventDefault(); void (async () => { if (hasUnsavedResults && !await confirmDialog(UNSAVED_SWITCH_CONFIRM)) return; setSelectedId(next.testCycleCaseId); })(); }
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [selected, caseSwitching, selectedId, filteredCases, skipModalOpen, defectEditor, hasUnsavedResults, mainTab, activeStep]);
  // §18 Skip Test Case — เปิด modal เลือก Reason + Comment ก่อนเสมอ ไม่มีปุ่มลัด
  const openSkipModal = () => { setSkipReason(""); setSkipComment(""); setSkipModalOpen(true); };
  const confirmSkip = () => {
    if (!skipReason) { notify("กรุณาเลือก Reason ก่อนยืนยัน Skip", "error"); return; }
    const label = skipReasonOptions.find((r) => r.value === skipReason)?.label ?? skipReason;
    submitExecution("Skipped", { commentOverride: `[${label}] ${skipComment}`.trim() });
  };
  // §19 Create Defect ต่อ Step ที่ Fail — เดิมยิง POST /defects ตรงๆ ด้วย description คงที่ ตอนนี้เปลี่ยน
  // มาเปิด ExecutionDefectEditor แทน เพื่อให้กรอก Severity/รายละเอียด/แนบรูปเองก่อนบันทึกได้ (ตัว editor
  // เป็นคนยิง POST + link ให้เองเมื่อกด บันทึก) — ไม่ชนกับ Defect ที่ auto-create ตอน Complete เป็น Fail
  // เพราะฝั่งนั้นเช็คก่อนแล้วว่ามี Defect เปิดอยู่ของ Test Case นี้หรือยัง ถ้ามีจะไม่สร้างซ้ำ
  const buildDefectContext = (step?: { stepNo: number; action: string; expectedResult: string }): WorkspaceDefectContext | null => {
    const cycle = cycles.find((item) => item.testCycleId === cycleId);
    if (!selected || !cycle?.projectId) return null;
    return {
      projectId: cycle.projectId,
      releaseId: cycle.releaseId || contextReleaseId,
      buildId: cycle.buildId || contextBuildId,
      moduleId: selected.moduleId,
      testCaseId: selected.testCaseId,
      testCaseCode: selected.testCaseCode,
      testCaseTitle: selected.title,
      cycleCode: workspace?.cycleCode ?? "",
      buildNumber: workspace?.buildNumber ?? "",
      environmentName: workspace?.environmentName ?? "",
      testerName: currentUser?.displayName ?? "",
      step: step ? { ...step, actualResult: stepActuals[step.stepNo] || "" } : undefined,
    };
  };
  const openDefectEditorForStep = (step: { stepNo: number; action: string; expectedResult: string }) => {
    const context = buildDefectContext(step);
    if (!context) { notify("ไม่พบ Project ของ Test Cycle นี้ ไม่สามารถสร้าง Defect ได้", "error"); return; }
    setDefectEditor({ context });
  };
  const editLinkedDefect = (defect: WorkspaceDefect) => {
    const context = buildDefectContext();
    if (!context) return;
    setDefectEditor({ context, existing: defect });
  };
  // บันทึก Defect ลงรายการ Linked Defects + ผูก code กับ Step — ใช้ทั้งตอนบันทึกครบ และตอนสร้างสำเร็จแต่
  // link/อัปโหลดรูปล้มเหลว (modal ยังเปิดอยู่) เพื่อไม่ให้ Defect ที่สร้างไปแล้วหายจากหน้าจอ
  const mergeLinkedDefect = (defect: WorkspaceDefect) => {
    const stepNo = defectEditor?.context.step?.stepNo;
    if (stepNo) setStepDefectCodes((d) => ({ ...d, [stepNo]: defect.defectCode }));
    setLinkedDefects((current) =>
      current.some((x) => x.defectId === defect.defectId)
        ? current.map((x) => (x.defectId === defect.defectId ? defect : x))
        : [...current, defect],
    );
  };
  const handleDefectSaved = (defect: WorkspaceDefect) => {
    mergeLinkedDefect(defect);
    setDefectEditor(null);
  };
  const removeExecution = async (execution: ExecutionCase["history"][number]) => {
    if (workspace?.status === "Closed" || workspace?.status === "Cancelled") {
      notify(`Cycle นี้อยู่สถานะ ${workspace.status} แล้ว ไม่สามารถลบผล Execution ได้`, "error");
      return;
    }
    if (!await confirmDialog(`ยืนยันลบผลการทดสอบ Run #${execution.executionNo}?\nข้อมูลจะถูกซ่อน แต่ยังเก็บไว้สำหรับ Audit`)) return;
    setDeletingHistoryId(execution.testExecutionId);
    try {
      const response = await fetch(`${apiUrl}/executions/${execution.testExecutionId}`, {
        method: "DELETE",
        headers,
      });
      if (!response.ok) {
        notify("ลบผลการทดสอบไม่สำเร็จ", "error");
        return;
      }
      setReload((x) => x + 1);
    } finally {
      setDeletingHistoryId(null);
    }
  };
  useEffect(() => {
    if (!cyclesLoaded) return;
    if (!filteredCycles.length) {
      setCycleId("");
    } else if (!filteredCycles.some((x) => x.testCycleId === cycleId)) {
      setCycleId(filteredCycles[0].testCycleId);
    }
  }, [filteredCycles, cycleId, cyclesLoaded]);
  // Generate Summary (spec §13) — สร้าง Actual Result สรุปจากผล Step ปัจจุบัน: เน้น Step ที่ Fail/Blocked พร้อมผลที่ได้จริง
  const generateSummary = async () => {
    if (!selected?.steps.length) return;
    const statusOf = (stepNo: number) => stepStatuses[stepNo] ?? "NotRun";
    const problems = selected.steps.filter((s) => statusOf(s.stepNo) === "Fail" || statusOf(s.stepNo) === "Blocked");
    const notRun = selected.steps.filter((s) => statusOf(s.stepNo) === "NotRun");
    const overallText = liveOverall === "InProgress" ? "In Progress" : liveOverall === "NotRun" ? "Not Run" : liveOverall;
    const lines = [`ผลรวม: ${overallText} (Pass ${stepCounts.passed} / Fail ${stepCounts.failed} / Blocked ${stepCounts.blocked} / Not Run ${stepCounts.notRun})`];
    if (problems.length) {
      problems.forEach((s) => lines.push(`- Step ${s.stepNo} ${statusOf(s.stepNo)}: ${stepActuals[s.stepNo]?.trim() || "ไม่ได้ระบุผลที่ได้จริง"} (คาดหวัง: ${s.expectedResult})`));
    } else if (!notRun.length) {
      lines.push(`ทุก Step (${selected.steps.length}) ได้ผลตามที่คาดหวัง`);
    }
    if (notRun.length) lines.push(`ยังไม่ได้ทดสอบ: Step ${notRun.map((s) => s.stepNo).join(", ")}`);
    const text = lines.join("\n");
    if (actual.trim() && actual.trim() !== text && !await confirmDialog("แทนที่ Actual Result ที่กรอกไว้ด้วยสรุปที่สร้างจากผล Step หรือไม่?")) return;
    setActual(text);
  };
  // Step ที่ Fail แต่ยังไม่มี Defect — ไม่มี Defect ที่ยังเปิดผูกกับเคสนี้ และยังไม่ได้สร้างรายสเต็ปในรอบนี้
  const failedStepsWithoutDefect = !selected || selectedDefects.some((d) => isOpenDefectStatus(d.status)) ? []
    : selected.steps.filter((s) => (stepStatuses[s.stepNo] ?? "NotRun") === "Fail" && !stepDefectCodes[s.stepNo]);
  // ===== Presentation helpers (UI ปรับใหม่ 2026-10-20 — ไม่เปลี่ยน logic/flow การบันทึกตาม EXECUTION-WORKSPACE-RESPONSIVE-UI-SPEC §2) =====
  const resultIcon = (status: string) => status === "Pass" ? "check_circle" : status === "Fail" ? "cancel" : status === "Blocked" ? "block" : "radio_button_unchecked";
  const resultLabel = (status: string) => status === "NotRun" ? "Not Run" : status;
  const caseIndex = filteredCases.findIndex((x) => x.testCycleCaseId === selectedId);
  const prevCase = caseIndex > 0 ? filteredCases[caseIndex - 1] : undefined;
  const nextCase = caseIndex >= 0 ? filteredCases[caseIndex + 1] : undefined;
  const executedCount = executionStats.total - executionStats.pending;
  const executedPercent = executionStats.total ? Math.round((executedCount / executionStats.total) * 100) : 0;
  // ข้อความอธิบาย Overall Result (test-case-execution-ui-spec.md §4)
  const overallMessage = liveOverall === "Fail" ? `มี Step ที่ Fail จำนวน ${stepCounts.failed} รายการ`
    : liveOverall === "Blocked" ? `มี Step ที่ Blocked จำนวน ${stepCounts.blocked} รายการ`
    : liveOverall === "InProgress" ? `ทดสอบแล้ว ${liveStepStatuses.length - stepCounts.notRun} จาก ${liveStepStatuses.length} Step`
    : liveOverall === "Pass" ? "ทุก Step ผ่าน" : "ยังไม่ได้บันทึกผล Step";
  const statusChips: { status: string; label: string; count: number }[] = [
    { status: "Pass", label: "Passed", count: executionStats.passed },
    { status: "Fail", label: "Failed", count: executionStats.failed },
    { status: "Blocked", label: "Blocked", count: executionStats.blocked },
    // Skipped นับเป็น "ทดสอบแล้ว" (executedCount = ทั้งหมด − Not Run) — ต้องมีในแถบ/ชิปด้วย ไม่งั้นแถบเต็มไม่ถึง % ที่แสดง
    { status: "Skipped", label: "Skipped", count: executionStats.skipped },
    { status: "InProgress", label: "In Progress", count: executionStats.inProgress },
    { status: "NotRun", label: "Not Run", count: executionStats.pending },
  ];
  return (
    <div className="execution-page">
      <div className="execution-toolbar card">
        <div className="execution-toolbar-row">
          {/* ไม่ห่อด้วย <label> — ในแผงมีช่องค้นหา การคลิกใน label จะไปกดปุ่มเปิด/ปิดซ้ำ */}
          <div className="execution-cycle-field">
            <span className="execution-field-label" aria-hidden="true">Test Cycle{workspaceSwitching && <span className="execution-switching"><span className="spinner inline" /> กำลังโหลด</span>}</span>
            <CyclePicker cycles={filteredCycles} value={cycleId} onChange={setCycleId} onOpen={refreshCycleProgress} disabled={!cyclesLoaded && !filteredCycles.length} />
          </div>
          <label className="execution-module-field">
            <span className="execution-field-label">Module</span>
            <select className="testcase-module-filter" value={cycleModuleFilter} onChange={(e) => setCycleModuleFilter(e.target.value)} disabled={!cycleModules.length}>
              <option value="">ทุก Module</option>
              {renderModuleSelectOptions(cycleModules)}
            </select>
          </label>
          <label className={`execution-mine-toggle${myCyclesOnly ? " is-on" : ""}`} title="แสดงเฉพาะ Test Cycle ที่ฉันสร้าง">
            <input type="checkbox" checked={myCyclesOnly} onChange={(e) => setMyCyclesOnly(e.target.checked)} />
            <span className="material-symbols-outlined" aria-hidden="true">{myCyclesOnly ? "person_check" : "person"}</span>
            เฉพาะฉัน
          </label>
          {/* แสดงตลอด (ค่า "-" ระหว่างโหลด/ยังไม่เลือก) — ถ้าซ่อนไว้ ช่อง Test Cycle จะยืด/หดทุกครั้งที่ workspace โหลด */}
          <dl className="execution-context" aria-label="ข้อมูล Test Cycle">
            <div><dt><span className="material-symbols-outlined" aria-hidden="true">deployed_code</span>Build</dt><dd title={workspace?.buildNumber || undefined}>{workspace?.buildNumber || "-"}</dd></div>
            <div><dt><span className="material-symbols-outlined" aria-hidden="true">dns</span>Environment</dt><dd title={workspace?.environmentName || undefined}>{workspace?.environmentName || "-"}</dd></div>
            <div><dt><span className="material-symbols-outlined" aria-hidden="true">flag</span>สถานะ</dt><dd>{workspace ? <Badge tone={workspace.status === "Closed" ? "green" : "yellow"}>{workspace.status}</Badge> : "-"}</dd></div>
          </dl>
        </div>
        {workspace && workspace.cases.length > 0 && (
          <div className={`execution-progress-row${workspaceSwitching ? " is-loading" : ""}`}>
            <div className="execution-progress-summary">
              <strong>{executedPercent}%</strong>
              <span><b>{executedCount}/{executionStats.total}</b> Test Case ทดสอบแล้ว</span>
            </div>
            {/* แถบแบ่งสีตามสถานะ — เห็นสัดส่วน Pass/Fail/Blocked/In Progress/Not Run ในแถบเดียว */}
            <div className="execution-progress-bar" role="img" aria-label={statusChips.map((c) => `${c.label} ${c.count}`).join(", ")}>
              {statusChips.filter((c) => c.count > 0).map((c) => (
                <i key={c.status} className={`is-${c.status.toLowerCase()}`} style={{ width: `${(c.count / executionStats.total) * 100}%` }} title={`${c.label} ${c.count}`} />
              ))}
            </div>
            <div className="execution-status-chips" role="group" aria-label="กรองคิว Test Case ตามสถานะ">
              {statusChips.map((chip) => (
                <button type="button" key={chip.status} className={`execution-status-chip is-${chip.status.toLowerCase()}${statusFilter === chip.status ? " active" : ""}`}
                  aria-pressed={statusFilter === chip.status} title={`แสดงเฉพาะ ${chip.label} ในคิว (กดซ้ำเพื่อแสดงทั้งหมด)`}
                  onClick={() => setStatusFilter((current) => current === chip.status ? "All" : chip.status)}>
                  <i aria-hidden="true" />{chip.label} <b>{chip.count}</b>
                </button>
              ))}
            </div>
          </div>
        )}
      </div>
      {cycleLoadError ? (
        <article className="card empty">
          <h3>โหลดรายการ Test Cycle ไม่สำเร็จ</h3>
          <div className="login-error">{cycleLoadError}</div>
        </article>
      ) : workspaceLoading && !workspace ? (
        <article className="card empty" role="status" aria-live="polite">
          <div className="spinner" />
          <h3>กำลังโหลด Execution Workspace...</h3>
        </article>
      ) : !workspace ? (
        workspaceLoadError ? (
          <article className="card empty">
            <h3>โหลด Execution Workspace ไม่สำเร็จ</h3>
            <div className="login-error">{workspaceLoadError}</div>
          </article>
        ) : (
          <article className="card empty">
            <h3>{cycleId ? "ไม่พบข้อมูล Execution Workspace" : "เลือก Test Cycle เพื่อเริ่มทดสอบ"}</h3>
            <p>{cycleId ? "Cycle นี้ยังไม่มีข้อมูลสำหรับการ Execute หรือข้อมูลถูกเปลี่ยนแปลงแล้ว" : cyclesLoaded && !filteredCycles.length ? "ยังไม่มี Test Cycle ที่สถานะ InProgress ตามตัวกรองนี้ — ลองเปลี่ยน Module/เฉพาะฉัน หรือเริ่ม Cycle จากหน้า Test Cycle" : "สร้าง Cycle และ Populate Test Case จาก Test Suite ก่อนเข้า Execution Workspace"}</p>
          </article>
        )
      ) : !workspace.cases.length ? (
        <article className="card empty">
          <h3>Cycle นี้ยังไม่มี Test Case</h3>
          <p>เลือก Test Suite ตอนสร้าง Cycle เพื่อเพิ่ม Test Case อัตโนมัติ</p>
        </article>
      ) : (
        <div className={`execution-layout${workspaceSwitching ? " is-loading" : ""}`} aria-busy={workspaceSwitching || undefined}>
          <aside className={`card case-queue${queueOpen ? " is-open" : ""}`} aria-label="คิว Test Case">
            <button type="button" className="case-queue-toggle" aria-expanded={queueOpen} onClick={() => setQueueOpen((v) => !v)}>
              <span><b>Test Cases</b> {filteredCases.length === workspace.cases.length ? workspace.cases.length : `${filteredCases.length} / ${workspace.cases.length}`}</span>
              <small>{selected ? `กำลังทดสอบ ${selected.testCaseCode}` : "เลือกเคส"}</small>
              <span className="material-symbols-outlined" aria-hidden="true">{queueOpen ? "expand_less" : "expand_more"}</span>
            </button>
            <div className="case-queue-body">
              <div className="card-title">
                <div>
                  <h3>Test Cases</h3>
                  <p>{filteredCases.length === workspace.cases.length ? `${workspace.cases.length} รายการ` : `แสดง ${filteredCases.length} จาก ${workspace.cases.length} รายการ`}</p>
                </div>
              </div>
              <div className="case-queue-tools">
                <input aria-label="ค้นหา Test Case" value={caseSearch} onChange={(e) => setCaseSearch(e.target.value)} placeholder="ค้นหารหัสหรือชื่อ Test Case" />
                <select aria-label="กรองสถานะ" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
                  {["All", "NotRun", "InProgress", "Pass", "Fail", "Blocked", "Skipped"].map((status) => <option key={status} value={status}>{status === "All" ? "ทุกสถานะ" : status}</option>)}
                </select>
                {hasAssignedCases && (
                  <label className={`case-queue-mine${queueMineOnly ? " is-on" : ""}`}>
                    <input type="checkbox" checked={queueMineOnly} onChange={(e) => setQueueMineOnly(e.target.checked)} />
                    <span className="material-symbols-outlined" aria-hidden="true">assignment_ind</span>
                    เฉพาะเคสที่ assign ให้ฉัน
                  </label>
                )}
              </div>
              <div className="case-queue-list" ref={queueListRef}>
                {filteredCases.map((x) => (
                  <button
                    className={`case-queue-item is-${x.currentStatus.toLowerCase()}${selectedId === x.testCycleCaseId ? " active" : ""}`}
                    aria-current={selectedId === x.testCycleCaseId ? "true" : undefined}
                    key={x.testCycleCaseId}
                    onClick={() => { void selectCase(x.testCycleCaseId); setQueueOpen(false); }}
                  >
                    <span className="case-row-top">
                      <b>{x.testCaseCode}</b>
                      <span className="case-row-badges">
                        {x.priority && <Badge tone={priorityTone(x.priority)}>{x.priority}</Badge>}
                        <Badge tone={executionStatusTone(x.currentStatus)}>{x.currentStatus}</Badge>
                      </span>
                    </span>
                    <small>{x.title}</small>
                  </button>
                ))}
                {!filteredCases.length && <p className="queue-empty">ไม่พบ Test Case ที่ตรงกับตัวกรอง</p>}
              </div>
            </div>
          </aside>
          {!selected ? (
            // ยังไม่เคยแสดงเคสใดใน Cycle นี้ — กรอบสูงเท่าพื้นที่ทดสอบ ไม่ให้หน้ายุบแล้วขยาย
            <main className="card execution-main execution-main-placeholder" aria-busy={!caseDetailLoadError || undefined}>
              {caseDetailLoadError ? <div className="login-error" role="alert">{caseDetailLoadError}</div> : <><div className="spinner" /><span>กำลังโหลด Test Case...</span></>}
            </main>
          ) : (
            <main ref={mainRef} className={`card execution-main${caseSwitching && !workspaceSwitching && !caseDetailLoadError ? " is-loading" : ""}`} aria-busy={(caseSwitching && !caseDetailLoadError) || undefined}>
              {caseDetailLoadError && <div className="login-error" role="alert">{caseDetailLoadError}</div>}
              <div className="execution-case-head">
                <div className="execution-case-title">
                  <span>{selected.testCaseCode}{caseIndex >= 0 && <em> · เคส {caseIndex + 1} / {filteredCases.length}</em>}</span>
                  <h2>{selected.title}</h2>
                </div>
                <div className="execution-case-nav">
                  <Badge tone={executionStatusTone(selected.currentStatus)}>{selected.currentStatus}</Badge>
                  <button type="button" className="btn" disabled={!prevCase} aria-label="Test Case ก่อนหน้า" title="Test Case ก่อนหน้า" onClick={() => prevCase && void selectCase(prevCase.testCycleCaseId)}><span className="material-symbols-outlined" aria-hidden="true">chevron_left</span></button>
                  <button type="button" className="btn" disabled={!nextCase} aria-label="Test Case ถัดไป" title="Test Case ถัดไป (N)" onClick={() => nextCase && void selectCase(nextCase.testCycleCaseId)}>ถัดไป <span className="material-symbols-outlined" aria-hidden="true">chevron_right</span></button>
                </div>
              </div>
              <div className="execution-tabs" role="tablist" aria-label="บันทึกผลและประวัติ">
                <button type="button" role="tab" id="execution-tab-steps" aria-controls="execution-panel-steps" aria-selected={mainTab === "steps"} tabIndex={mainTab === "steps" ? 0 : -1}
                  onClick={() => setMainTab("steps")} onKeyDown={(e) => { if (e.key === "ArrowRight" || e.key === "ArrowLeft") { setMainTab("history"); document.getElementById("execution-tab-history")?.focus(); } }}>
                  <span className="material-symbols-outlined" aria-hidden="true">fact_check</span> บันทึกผล
                </button>
                <button type="button" role="tab" id="execution-tab-history" aria-controls="execution-panel-history" aria-selected={mainTab === "history"} tabIndex={mainTab === "history" ? 0 : -1}
                  onClick={() => setMainTab("history")} onKeyDown={(e) => { if (e.key === "ArrowRight" || e.key === "ArrowLeft") { setMainTab("steps"); document.getElementById("execution-tab-steps")?.focus(); } }}>
                  <span className="material-symbols-outlined" aria-hidden="true">history</span> ประวัติการทดสอบ <span className="execution-tab-count">{selected.history.length}</span>
                </button>
              </div>
              {mainTab === "steps" ? (
                <div role="tabpanel" id="execution-panel-steps" aria-labelledby="execution-tab-steps" className="execution-steps-panel">
                  {(selected.preconditions || selectedDefects.length > 0 || selectedDefectsError) && (
                    <div className="execution-context-notes">
                      {selected.preconditions && (
                        <div className="precondition">
                          <b>Preconditions</b>
                          <p>{selected.preconditions}</p>
                        </div>
                      )}
                      {selectedDefectsError && <div className="inline-alert error" role="alert"><span>{selectedDefectsError}</span></div>}
                      {selectedDefects.length > 0 && (
                        <div className="execution-linked-defects">
                          <b>Linked Defects</b>
                          {selectedDefects.map((d) => (
                            <span className="execution-linked-defect" key={d.defectId}>
                              <Badge tone={defectStatusTones[d.status] ?? "gray"}>{d.defectCode}</Badge>
                              <small>{d.title}</small>
                              {canEditDefect && <button type="button" className="link-btn" aria-label={`แก้ไข ${d.defectCode}`} onClick={() => editLinkedDefect(d)}>Edit</button>}
                            </span>
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                  {/* Overall Result Summary (test-case-execution-ui-spec.md §4) — คำนวณจากผล Step ที่กำลังแก้ ผู้ใช้กำหนดเองไม่ได้ */}
                  <div className={`execution-overall is-${liveOverall.toLowerCase()}`} role="status" aria-live="polite">
                    <div className="execution-overall-result">
                      <small>Overall Result</small>
                      <b><span className="material-symbols-outlined" aria-hidden="true">{liveOverall === "InProgress" ? "pending" : resultIcon(liveOverall)}</span> {liveOverall === "InProgress" ? "In Progress" : resultLabel(liveOverall)}</b>
                      <span>{overallMessage}</span>
                    </div>
                    <dl className="execution-overall-counts">
                      <div className="is-pass"><dt>Passed</dt><dd>{stepCounts.passed}</dd></div>
                      <div className="is-fail"><dt>Failed</dt><dd>{stepCounts.failed}</dd></div>
                      <div className="is-blocked"><dt>Blocked</dt><dd>{stepCounts.blocked}</dd></div>
                      <div className="is-notrun"><dt>Not Run</dt><dd>{stepCounts.notRun}</dd></div>
                    </dl>
                  </div>
                  <div className="step-table">
                    <div className="step-bulk-actions">
                      <span>Test Steps <b>{selected.steps.length}</b></span>
                      <div>{(["Pass", "Fail", "Blocked", "NotRun"] as const).map((status) => (
                        <button
                          type="button"
                          key={status}
                          className={`is-${status.toLowerCase()}`}
                          onClick={async () => {
                            if (!await confirmDialog(`ต้องการเปลี่ยนผล Test Step ทั้งหมดเป็น ${resultLabel(status)} หรือไม่?`)) return;
                            setStepStatuses(Object.fromEntries(selected.steps.map((step) => [step.stepNo, status])));
                          }}
                        >
                          <span className="material-symbols-outlined bulk-result-icon" aria-hidden="true">{resultIcon(status)}</span> Set All {resultLabel(status)}
                        </button>
                      ))}</div>
                    </div>
                    <div className="step-row step-head" aria-hidden="true">
                      <span>#</span>
                      <span>Action / Test Data</span>
                      <span>Expected Result</span>
                      <span>Step Result</span>
                      <span>Actual Result / Comment</span>
                    </div>
                    {!selected.steps.length && <p className="queue-empty">Test Case นี้ไม่มี Test Step</p>}
                    {selected.steps.map((x) => {
                      const status = (stepStatuses[x.stepNo] ?? "NotRun") as StepStatus;
                      const requiresActual = status === "Fail" || status === "Blocked";
                      const missingActual = requiresActual && !stepActuals[x.stepNo]?.trim();
                      const actualInputId = `step-actual-${x.stepNo}`;
                      const actualErrorId = `step-actual-error-${x.stepNo}`;
                      return (
                        <div id={`step-row-${x.stepNo}`} className={`step-row is-${status.toLowerCase()}${activeStep === x.stepNo ? " is-current" : ""}`} key={x.stepNo} onClick={() => setActiveStep(x.stepNo)}>
                          <span className="step-field step-field-no"><span className="sr-only">Step </span>{x.stepNo}</span>
                          <span className="step-field">
                            <span className="step-field-label" aria-hidden="true">Action / Test Data</span>
                            <span className="step-field-value">
                              <b>{x.action}</b>
                              {x.testData && <small className="step-test-data">{x.testData}</small>}
                            </span>
                          </span>
                          <span className="step-field">
                            <span className="step-field-label" aria-hidden="true">Expected Result</span>
                            <span className="step-field-value">{x.expectedResult}</span>
                          </span>
                          <span className="step-field">
                            <span className="step-field-label" aria-hidden="true">Step Result</span>
                            <span className="step-result-control" role="group" aria-label={`ผล Step ${x.stepNo}`}>
                              {(["Pass", "Fail", "Blocked", "NotRun"] as const).map((opt) => (
                                <button
                                  type="button"
                                  key={opt}
                                  className={`step-result-btn ${opt.toLowerCase()}${status === opt ? " active" : ""}`}
                                  title={resultLabel(opt)}
                                  aria-pressed={status === opt}
                                  aria-label={`ตั้งผล Step ${x.stepNo} เป็น ${resultLabel(opt)}`}
                                  onClick={() => setStepStatuses((s) => ({ ...s, [x.stepNo]: opt }))}
                                >
                                  <span className="material-symbols-outlined step-result-icon" aria-hidden="true">{resultIcon(opt)}</span>
                                  <em>{opt === "NotRun" ? "—" : opt}</em>
                                </button>
                              ))}
                            </span>
                          </span>
                          <span className="step-field">
                            <span className="step-field-label" aria-hidden="true">Actual Result / Comment</span>
                            <span className="step-actual-cell">
                              <label className="sr-only" htmlFor={actualInputId}>{`Actual Result Step ${x.stepNo}${requiresActual ? " (บังคับกรอก)" : ""}`}</label>
                              <input
                                id={actualInputId}
                                className={missingActual ? "input-required" : ""}
                                aria-required={requiresActual || undefined}
                                aria-invalid={missingActual || undefined}
                                aria-describedby={missingActual ? actualErrorId : undefined}
                                value={stepActuals[x.stepNo] ?? ""}
                                onChange={(e) => setStepActuals((s) => ({ ...s, [x.stepNo]: e.target.value }))}
                                placeholder={requiresActual ? "ผลที่ได้จริง (บังคับกรอก) *" : "ผลที่ได้จริง / Comment"}
                              />
                              {missingActual && <small id={actualErrorId} className="sr-only">กรุณาระบุผลที่เกิดขึ้นจริง</small>}
                              {status === "Fail" && canEditDefect && (
                                stepDefectCodes[x.stepNo]
                                  ? <small className="step-defect-linked">Defect: {stepDefectCodes[x.stepNo]}</small>
                                  : <button type="button" className="step-create-defect" title="สร้าง Defect จาก Step นี้" aria-label={`สร้าง Defect จาก Step ${x.stepNo}`} onClick={() => openDefectEditorForStep(x)}>+ Defect</button>
                              )}
                            </span>
                          </span>
                        </div>
                      );
                    })}
                  </div>
                  <div className="execution-notes">
                    <div className="execution-notes-field">
                      <div className="execution-notes-head">
                        <label htmlFor="execution-actual-summary">Actual Result (สรุปผลที่เกิดขึ้นจริง)</label>
                        <button type="button" className="execution-generate-summary" disabled={!selected.steps.length} onClick={() => void generateSummary()} title="สร้างสรุปจากผล Step ที่ Fail/Blocked และ Step ที่ยังไม่ได้ทดสอบ">
                          <span className="material-symbols-outlined" aria-hidden="true">auto_awesome</span> Generate Summary
                        </button>
                      </div>
                      <textarea id="execution-actual-summary" rows={3} value={actual} onChange={(e) => setActual(e.target.value)} placeholder="สรุปผลที่เกิดขึ้นจริง หรือกด Generate Summary" />
                    </div>
                    <label>
                      Comment / หมายเหตุเพิ่มเติม
                      <textarea rows={2} value={comment} onChange={(e) => setComment(e.target.value)} placeholder="หมายเหตุเพิ่มเติม" />
                    </label>
                  </div>
                </div>
              ) : (
                <div role="tabpanel" id="execution-panel-history" aria-labelledby="execution-tab-history" className="execution-history">
                  {selected.history.length ? selected.history.map((x) => (
                    <div className="history-item" key={x.testExecutionId}>
                      <div className="history-item-head">
                        <Badge tone={executionStatusTone(x.status)}>{x.status}</Badge>
                        <span className="history-run">Run #{x.executionNo}</span>
                        <small>{x.testerName} · {x.completedAt ? formatThaiDateTime(x.completedAt) : "-"}</small>
                      </div>
                      {x.stepResults && x.stepResults.length > 0 && (
                        <ol className="history-steps" aria-label={`ผลราย Step ของ Run #${x.executionNo}`}>
                          {x.stepResults.map((r) => (
                            <li key={r.stepNo} className={`is-${r.status.toLowerCase()}`} title={`Step ${r.stepNo}: ${resultLabel(r.status)}${r.actualResult ? ` — ${r.actualResult}` : ""}`}>
                              <span className="material-symbols-outlined" aria-hidden="true">{resultIcon(r.status)}</span>
                              {r.stepNo}
                              <span className="sr-only"> {resultLabel(r.status)}</span>
                            </li>
                          ))}
                        </ol>
                      )}
                      <p>{x.actualResult || "-"}</p>
                      {x.stepResults?.filter((r) => (r.status === "Fail" || r.status === "Blocked") && r.actualResult).map((r) => (
                        <p className={`history-step-note is-${r.status.toLowerCase()}`} key={r.stepNo}><b>Step {r.stepNo} · {r.status}</b> {r.actualResult}</p>
                      ))}
                      {x.comment && <small className="history-comment">Comment: {x.comment}</small>}
                      {/* แยกตำแหน่งปุ่มลบออกจาก Run number (spec §3.3) ลดโอกาสกดพลาดบนมือถือ */}
                      <div className="history-item-foot">
                        <button
                          className="history-delete"
                          disabled={deletingHistoryId === x.testExecutionId || workspace?.status === "Closed" || workspace?.status === "Cancelled"}
                          onClick={() => removeExecution(x)}
                          aria-label={`ลบผลการทดสอบ Run #${x.executionNo}`}
                          title={workspace?.status === "Closed" || workspace?.status === "Cancelled" ? "Cycle ปิดแล้ว ไม่สามารถลบผลได้" : "ลบผลการทดสอบ"}
                        >
                          {deletingHistoryId === x.testExecutionId
                            ? <><span className="spinner inline" aria-hidden="true" /> กำลังลบ...</>
                            : <><span className="material-symbols-outlined" aria-hidden="true">close</span> ลบ</>}
                        </button>
                      </div>
                    </div>
                  )) : <p className="muted-text">ยังไม่มีประวัติการทดสอบของ {selected.testCaseCode}</p>}
                </div>
              )}
              {/* Action Bar (§14) — 3 ปุ่มเท่านั้น ติดล่างจอ พร้อม Overall Result ย่อให้เห็นตลอดระหว่างเลื่อน Step */}
              <div className="execution-actions">
                {failedStepsWithoutDefect.length > 0 && (
                  <div className="execution-defect-warning" role="status">
                    <span className="material-symbols-outlined" aria-hidden="true">warning</span>
                    <span><b>Step {failedStepsWithoutDefect.map((s) => s.stepNo).join(", ")} Fail ยังไม่มี Defect</b> — เมื่อบันทึกผล Fail ระบบจะสร้าง Defect ให้อัตโนมัติ 1 รายการ</span>
                    {canEditDefect && <button type="button" className="link-btn" onClick={() => openDefectEditorForStep(failedStepsWithoutDefect[0])}>สร้าง Defect เอง</button>}
                  </div>
                )}
                <span className={`execution-actions-overall is-${liveOverall.toLowerCase()}`}>
                  <span className="material-symbols-outlined" aria-hidden="true">{liveOverall === "InProgress" ? "pending" : resultIcon(liveOverall)}</span>
                  <b>{liveOverall === "InProgress" ? "In Progress" : resultLabel(liveOverall)}</b>
                  <small>P {stepCounts.passed} · F {stepCounts.failed} · B {stepCounts.blocked} · — {stepCounts.notRun}</small>
                </span>
                <small className="execution-shortcut-hint">ปุ่มลัด: ↑/↓ เลือก Step · 1–4 = Pass/Fail/Blocked/Not Run · Enter = กรอกผล · P/F/B = ทุก Step · N = เคสถัดไป</small>
                <button className="btn" disabled={saving} onClick={saveProgress}>
                  {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : <><span className="material-symbols-outlined" aria-hidden="true">save</span> Save Progress</>}
                </button>
                <button className="result-btn skip" disabled={saving} onClick={openSkipModal}><span className="material-symbols-outlined" aria-hidden="true">skip_next</span> Skip Test Case</button>
                <button className="result-btn pass" disabled={saving} onClick={completeTest}><span className="material-symbols-outlined" aria-hidden="true">task_alt</span> Complete Test</button>
              </div>
              {skipModalOpen && (
                <ModalShell labelledBy="skip-test-case-title" onDismiss={() => setSkipModalOpen(false)}>
                    <div className="modal-head">
                      <h2 id="skip-test-case-title">Skip Test Case</h2>
                      <button aria-label="ปิดหน้าต่าง" onClick={() => setSkipModalOpen(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button>
                    </div>
                    <div className="form-grid">
                      <label>
                        Reason <span className="required">*</span>
                        <select value={skipReason} onChange={(e) => setSkipReason(e.target.value)}>
                          <option value="">เลือก Reason</option>
                          {skipReasonOptions.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
                        </select>
                      </label>
                      <label className="full">
                        Comment
                        <textarea rows={3} value={skipComment} onChange={(e) => setSkipComment(e.target.value)} placeholder="รายละเอียดเพิ่มเติม (ไม่บังคับ)" />
                      </label>
                    </div>
                    <div className="modal-actions">
                      <button className="btn" onClick={() => setSkipModalOpen(false)}>Cancel</button>
                      <button className="btn primary" disabled={saving || !skipReason} onClick={confirmSkip}>Confirm Skip</button>
                    </div>
                  </ModalShell>
              )}
              {defectEditor && (
                <ExecutionDefectEditor
                  apiUrl={apiUrl}
                  context={defectEditor.context}
                  existing={defectEditor.existing}
                  onClose={() => setDefectEditor(null)}
                  onSaved={handleDefectSaved}
                  onPartialSave={mergeLinkedDefect}
                />
              )}
            </main>
          )}
        </div>
      )}
    </div>
  );
}
