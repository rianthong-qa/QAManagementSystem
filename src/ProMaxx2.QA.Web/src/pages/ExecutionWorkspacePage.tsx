import { useState, useMemo, useEffect, useRef } from "react";
import { type WorkspaceDefect, type WorkspaceDefectContext, ExecutionDefectEditor } from "../ExecutionDefectEditor";
import { apiUrl, getJson, isAbortError } from "../api";
import { confirmDialog, notify } from "../components/dialogStore";
import { type StepStatus, calculateOverallResult } from "../overallResult";
import { Badge } from "../components/Badge";
import { ModalShell } from "../components/ModalShell";
import { formatThaiDateTime } from "../dateTime";
import { type ModuleItem, type SessionUser, type TestCycleItem, currentUserId, defectStatusTones, renderModuleSelectOptions } from "../shared/appShared";

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
export function ExecutionWorkspacePage({ contextProjectId, contextReleaseId, contextBuildId }: { contextProjectId?: string; contextReleaseId?: string; contextBuildId?: string }) {
  const [cycles, setCycles] = useState<TestCycleItem[]>([]),
    [cyclesLoaded, setCyclesLoaded] = useState(false),
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
    [cycleModuleFilter, setCycleModuleFilter] = useState(""),
    [cycleModules, setCycleModules] = useState<ModuleItem[]>([]),
    // ปิดเป็นค่าเริ่มต้นเสมอ (ไม่กรอง) — Test Cycle ไม่มีช่องให้กำหนด "ผู้ดำเนินการ" (ownerUserId) ตอนสร้าง/แก้ไข
    // เลยเป็น null เสมอทุก Cycle ในระบบ ถ้า default เปิดไว้จะกรองจนไม่เหลือ Cycle ให้เลือกเลยสำหรับทุกคน
    [myCyclesOnly, setMyCyclesOnly] = useState(false),
    [workspaceLoading, setWorkspaceLoading] = useState(false),
    [saving, setSaving] = useState(false),
    [reload, setReload] = useState(0),
    // สถานะสำหรับ Skip Test Case modal (§18) และปุ่ม Create Defect ต่อ Step (§19)
    [skipModalOpen, setSkipModalOpen] = useState(false),
    [skipReason, setSkipReason] = useState(""),
    [skipComment, setSkipComment] = useState(""),
    [linkedDefects, setLinkedDefects] = useState<WorkspaceDefect[]>([]),
    [linkedDefectsError, setLinkedDefectsError] = useState(""),
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
  useEffect(() => {
    const h = {
      Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}`,
    };
    const query = cycleModuleFilter ? `?moduleId=${cycleModuleFilter}&size=100` : "?size=100";
    setCyclesLoaded(false);
    setCycleLoadError("");
    fetch(`${apiUrl}/test-cycles${query}`, { headers: h })
      .then(async (r) => {
        if (!r.ok) throw new Error(`โหลด Test Cycle ไม่สำเร็จ (${r.status})`);
        const data: unknown = await r.json();
        return Array.isArray(data) ? (data as TestCycleItem[]) : (data as any)?.items?.rows ?? [];
      })
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
  }, [reload, cycleModuleFilter, myCyclesOnly]);
  useEffect(() => {
    const h = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
    // Keep the filter options stable while the cycle list is being reloaded
    // for the selected module; otherwise the selected result would empty the
    // source used to build the Module dropdown and disable it.
    if (cycleModuleFilter) return;
    const projectIds = contextProjectId ? [contextProjectId] : [...new Set(cycles.map((x) => x.projectId))];
    if (!projectIds.length) { setCycleModules([]); return; }
    Promise.all(projectIds.map((id) => fetch(`${apiUrl}/projects/${id}/modules`, { headers: h }).then((r) => r.ok ? r.json() : [])))
      .then((groups: ModuleItem[][]) => {
        const seen = new Map<string, ModuleItem>();
        groups.flat().filter((m) => m.isActive).forEach((m) => { if (!seen.has(m.moduleId)) seen.set(m.moduleId, m); });
        setCycleModules([...seen.values()].sort((a, b) => a.moduleCode.localeCompare(b.moduleCode)));
      });
  }, [contextProjectId, cycles, cycleModuleFilter]);
  // Cycle ที่ workspace ปัจจุบันเป็นของ — ใช้แยก "เปลี่ยน Cycle" (ล้าง state + spinner เต็มหน้า) ออกจาก
  // "reload หลังบันทึกผล" (refetch เงียบๆ คง Test Case ที่เลือกและตำแหน่ง scroll ไว้)
  const workspaceCycleRef = useRef("");
  useEffect(() => {
    const cycleChanged = workspaceCycleRef.current !== cycleId;
    workspaceCycleRef.current = cycleId;
    if (cycleChanged) {
      setWorkspace(null);
      setSelectedId("");
      setCaseDetail(null);
      setLinkedDefects([]);
      setStepDefectCodes({});
      setStepStatuses({});
      setStepActuals({});
      setActual("");
      setComment("");
      setDefectEditor(null);
    }
    setWorkspaceLoadError("");
    if (!cycleId) {
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
  useEffect(() => {
    if (!selectedId || !cycleId) { setCaseDetail(null); setCaseDetailLoadError(""); return; }
    const h = { Authorization: `Bearer ${localStorage.getItem("qa.accessToken")}` };
    const controller = new AbortController();
    setCaseDetailLoadError("");
    fetch(`${apiUrl}/test-cycles/${cycleId}/cases/${selectedId}`, { headers: h, signal: controller.signal })
      .then(r => {
        if (!r.ok) throw new Error(`โหลดรายละเอียด Test Case ไม่สำเร็จ (${r.status})`);
        return r.json();
      })
      .then((data) => { if (!controller.signal.aborted) setCaseDetail(data); })
      .catch((e) => {
        if (controller.signal.aborted) return;
        setCaseDetail(null);
        setCaseDetailLoadError(e instanceof Error ? e.message : "โหลดรายละเอียด Test Case ไม่สำเร็จ");
      });
    return () => controller.abort();
  }, [selectedId, cycleId, reload]);
  useEffect(() => {
    const testCaseId = caseDetail?.testCaseId;
    if (!testCaseId) { setLinkedDefects([]); return; }
    const controller = new AbortController();
    setLinkedDefectsError("");
    // โหลดไม่สำเร็จต้องบอก — เดิมคืน [] แล้วดูเหมือนเคสนี้ไม่มี Defect ที่เชื่อมไว้
    getJson<WorkspaceDefect[]>(`${apiUrl}/defects/by-test-case/${testCaseId}`, controller.signal)
      .then((rows) => { if (!controller.signal.aborted) setLinkedDefects(rows); })
      .catch((e) => { if (!controller.signal.aborted && !isAbortError(e)) { setLinkedDefects([]); setLinkedDefectsError("โหลด Defect ที่เชื่อมกับ Test Case นี้ไม่สำเร็จ"); } });
    return () => controller.abort();
  }, [caseDetail?.testCaseId, reload]);
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
      inProgress: cases.filter((x) => x.currentStatus === "InProgress").length,
      pending: cases.filter((x) => x.currentStatus === "NotRun").length,
    };
  }, [workspace]);
  const filteredCases = useMemo(() => {
    const query = caseSearch.trim().toLocaleLowerCase("th-TH");
    return (workspace?.cases ?? [])
      .filter((item) =>
        (statusFilter === "All" || item.currentStatus === statusFilter) &&
        (!query || `${item.testCaseCode} ${item.title}`.toLocaleLowerCase("th-TH").includes(query)),
      )
      .sort((a, b) =>
        a.testCaseCode.localeCompare(b.testCaseCode, undefined, { numeric: true, sensitivity: "base" }) ||
        a.testCycleCaseId.localeCompare(b.testCycleCaseId),
      );
  }, [workspace, caseSearch, statusFilter]);
  useEffect(() => {
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
  useEffect(() => { setStepDefectCodes({}); }, [selectedId]);
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
    if (!selected) return;
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
      const result: { createdDefectCode?: string; existingDefectCode?: string; defectAutoCreateError?: string } = await response.json();
      if (result.createdDefectCode) notify(`ระบบสร้าง Defect ${result.createdDefectCode} ให้อัตโนมัติ เนื่องจากผลเป็น Fail`, "success");
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
      if (!selected || skipModalOpen || defectEditor || target?.matches("input, textarea, select, [contenteditable=\"true\"]")) return;
      const key = event.key.toLowerCase();
      if (["p", "f", "b"].includes(key)) {
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
  }, [selected, selectedId, filteredCases, skipModalOpen, defectEditor, hasUnsavedResults]);
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
  const filteredCycles = useMemo(() => cycles.filter((x) =>
    (!contextProjectId || x.projectId === contextProjectId) &&
    (!contextReleaseId || x.releaseId === contextReleaseId) &&
    (!contextBuildId || x.buildId === contextBuildId)
  ), [cycles, contextProjectId, contextReleaseId, contextBuildId]);
  useEffect(() => {
    if (!cyclesLoaded) return;
    if (!filteredCycles.length) {
      setCycleId("");
    } else if (!filteredCycles.some((x) => x.testCycleId === cycleId)) {
      setCycleId(filteredCycles[0].testCycleId);
    }
  }, [filteredCycles, cycleId, cyclesLoaded]);
  return (
    <div className="execution-page">
      <div className="execution-toolbar card">
        <label className="check-line">
          <input type="checkbox" checked={myCyclesOnly} onChange={(e) => { setCycleId(""); setMyCyclesOnly(e.target.checked); }} />
          เฉพาะฉัน
        </label>
        <label>
          Module
          <select className="testcase-module-filter" aria-label="กรอง Test Cycle ตาม Module" value={cycleModuleFilter} onChange={(e) => { setCycleId(""); setCycleModuleFilter(e.target.value); }} disabled={!cycleModules.length}>
            <option value="">ทุก Module</option>
            {renderModuleSelectOptions(cycleModules)}
          </select>
        </label>
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
          <div className="metric-inprogress"><small>In Progress</small><strong>{executionStats.inProgress}</strong></div>
          <div className="metric-pending"><small>Not Run</small><strong>{executionStats.pending}</strong></div>
          <div className="execution-progress-summary">
            <span><i style={{width:`${executionStats.total ? ((executionStats.total-executionStats.pending)/executionStats.total)*100 : 0}%`}} /></span>
            <small><b>{executionStats.total - executionStats.pending}/{executionStats.total}</b> Test Cases executed · {executionStats.total ? Math.round(((executionStats.total-executionStats.pending)/executionStats.total)*100) : 0}%</small>
          </div>
        </div>
      )}
      {cycleLoadError ? (
        <article className="card empty">
          <h3>โหลดรายการ Test Cycle ไม่สำเร็จ</h3>
          <div className="login-error">{cycleLoadError}</div>
        </article>
      ) : workspaceLoading ? (
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
            <p>{cycleId ? "Cycle นี้ยังไม่มีข้อมูลสำหรับการ Execute หรือข้อมูลถูกเปลี่ยนแปลงแล้ว" : "สร้าง Cycle และ Populate Test Case จาก Test Suite ก่อนเข้า Execution Workspace"}</p>
          </article>
        )
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
                {["All", "NotRun", "InProgress", "Pass", "Fail", "Blocked", "Skipped"].map((status) => <option key={status} value={status}>{status === "All" ? "ทุกสถานะ" : status}</option>)}
              </select>
            </div>
            <div className="case-queue-list">
            {filteredCases.map((x) => (
              <button
                className={selectedId === x.testCycleCaseId ? "active" : ""}
                aria-current={selectedId === x.testCycleCaseId ? "true" : undefined}
                key={x.testCycleCaseId}
                onClick={() => selectCase(x.testCycleCaseId)}
              >
                <span className="case-row-top">
                  <b>{x.testCaseCode}</b>
                  <Badge tone={executionStatusTone(x.currentStatus)}>
                    {x.currentStatus}
                  </Badge>
                </span>
                <small>{x.title}</small>
              </button>
            ))}
            {!filteredCases.length && <p className="queue-empty">ไม่พบ Test Case ที่ตรงกับตัวกรอง</p>}
            </div>
          </aside>
          {selected && (
            <main className="card execution-main">
              {caseDetailLoadError && <div className="login-error" role="alert">{caseDetailLoadError}</div>}
              <div className="execution-case-head">
                <div>
                  <span>{selected.testCaseCode}</span>
                  <h2>{selected.title}</h2>
                </div>
                <Badge tone={executionStatusTone(selected.currentStatus)}>
                  {selected.currentStatus}
                </Badge>
              </div>
              {/* ตัดแถว Tester/Environment/Build/Test Cycle ออกทั้งหมด — ข้อมูลซ้ำกับที่แสดงอยู่แล้ว
                  ในหน้านี้ (Environment/Build อยู่ใน toolbar บนสุด, Test Cycle อยู่ใน dropdown เลือก
                  Cycle, Tester คือผู้ใช้ที่ login อยู่ซึ่งเห็นอยู่แล้วที่ profile บน topbar ของทั้งแอป) */}
              {/* Overall Result Summary การ์ดแยกก็ตัดออกด้วย — ซ้ำกับ Badge สถานะที่ execution-case-head
                  ด้านบนอยู่แล้ว (ยังคง bind กับ selected.currentStatus ตัวเดิม ไม่ใช่ liveOverall เพราะ
                  ต้องโชว์สถานะที่ persist ไว้จริงรวมถึง "Skipped" ซึ่ง liveOverall ไม่มีค่านี้) */}
              {selected.preconditions && (
                <div className="precondition">
                  <b>Preconditions</b>
                  <p>{selected.preconditions}</p>
                </div>
              )}
              {linkedDefectsError && <div className="inline-alert error" role="alert"><span>{linkedDefectsError}</span></div>}
              {linkedDefects.length > 0 && (
                <div className="execution-linked-defects">
                  <b>Linked Defects</b>
                  {linkedDefects.map((d) => (
                    <span className="execution-linked-defect" key={d.defectId}>
                      <Badge tone={defectStatusTones[d.status] ?? "gray"}>{d.defectCode}</Badge>
                      <small>{d.title}</small>
                      {canEditDefect && (
                        <button type="button" className="link-btn" aria-label={`แก้ไข ${d.defectCode}`} onClick={() => editLinkedDefect(d)}>
                          Edit
                        </button>
                      )}
                    </span>
                  ))}
                </div>
              )}
              <div className="step-table">
                <div className="step-bulk-actions">
                  <span>Test Steps <b>{selected.steps.length}</b></span>
                  <div>{(["Pass", "Fail", "Blocked", "NotRun"] as const).map((status) => {
                    const label = status === "NotRun" ? "Not Run" : status;
                    return (
                      <button
                        type="button"
                        key={status}
                        onClick={async () => {
                          if (!await confirmDialog(`ต้องการเปลี่ยนผล Test Step ทั้งหมดเป็น ${label} หรือไม่?`)) return;
                          setStepStatuses(Object.fromEntries(selected.steps.map((step) => [step.stepNo, status])));
                        }}
                      >
                        <span className="material-symbols-outlined bulk-result-icon" aria-hidden="true">{status === "Pass" ? "check_circle" : status === "Fail" ? "cancel" : status === "Blocked" ? "block" : "radio_button_unchecked"}</span> Set All {label}
                      </button>
                    );
                  })}</div>
                </div>
                <div className="step-row step-head">
                  <span>#</span>
                  <span>Action / Test Data</span>
                  <span>Expected Result</span>
                  <span>Step Result</span>
                  <span>Actual Result / Comment</span>
                </div>
                {selected.steps.map((x) => {
                  const status = (stepStatuses[x.stepNo] ?? "NotRun") as StepStatus;
                  const requiresActual = status === "Fail" || status === "Blocked";
                  const missingActual = requiresActual && !stepActuals[x.stepNo]?.trim();
                  const actualInputId = `step-actual-${x.stepNo}`;
                  const actualErrorId = `step-actual-error-${x.stepNo}`;
                  return (
                    <div className="step-row" key={x.stepNo}>
                      <span className="step-field step-field-no">{x.stepNo}</span>
                      {/* .step-field-label แทน CSS ::before ที่อิง nth-child (spec §2.2/§5.2) — label
                          เป็น element จริงในทุก breakpoint แต่ซ่อนด้วย CSS บน desktop เพราะ .step-head
                          ทำหน้าที่เป็นหัวคอลัมน์อยู่แล้ว ไม่ต้องพึ่งตำแหน่ง DOM มาสร้าง label */}
                      <span className="step-field">
                        <span className="step-field-label" aria-hidden="true">Action / Test Data</span>
                        <span className="step-field-value">
                          <b>{x.action}</b>
                          {x.testData && <small>{x.testData}</small>}
                        </span>
                      </span>
                      <span className="step-field">
                        <span className="step-field-label" aria-hidden="true">Expected Result</span>
                        <span className="step-field-value">{x.expectedResult}</span>
                      </span>
                      <span className="step-field">
                        <span className="step-field-label" aria-hidden="true">Step Result</span>
                        <span className="step-result-control">
                          {(["Pass", "Fail", "Blocked", "NotRun"] as const).map((opt) => (
                          <button
                            type="button"
                            key={opt}
                            className={`step-result-btn ${opt.toLowerCase()}${status === opt ? " active" : ""}`}
                            title={opt === "NotRun" ? "Not Run" : opt}
                            aria-label={`ตั้งผล Step ${x.stepNo} เป็น ${opt === "NotRun" ? "Not Run" : opt}`}
                              onClick={() => setStepStatuses((s) => ({ ...s, [x.stepNo]: opt }))}
                            >
                              <span className="material-symbols-outlined step-result-icon" aria-hidden="true">{opt === "Pass" ? "check_circle" : opt === "Fail" ? "cancel" : opt === "Blocked" ? "block" : "radio_button_unchecked"}</span>
                            </button>
                          ))}
                        </span>
                      </span>
                      <span className="step-field">
                        <span className="step-field-label" aria-hidden="true">Actual Result / Comment</span>
                        <span className="step-actual-cell">
                          <label className="sr-only" htmlFor={actualInputId}>
                            {`Actual Result Step ${x.stepNo}${requiresActual ? " (บังคับกรอก)" : ""}`}
                          </label>
                          <input
                            id={actualInputId}
                            className={missingActual ? "input-required" : ""}
                            aria-required={requiresActual || undefined}
                            aria-invalid={missingActual || undefined}
                            aria-describedby={missingActual ? actualErrorId : undefined}
                            value={stepActuals[x.stepNo] ?? ""}
                            onChange={(e) =>
                              setStepActuals((s) => ({
                                ...s,
                                [x.stepNo]: e.target.value,
                              }))
                            }
                            placeholder={requiresActual ? "ผลที่ได้จริง (บังคับกรอก) *" : "ผลที่ได้จริง / Comment"}
                          />
                          {missingActual && <small id={actualErrorId} className="step-actual-error">กรุณาระบุผลที่เกิดขึ้นจริง</small>}
                          {status === "Fail" && canEditDefect && (
                            stepDefectCodes[x.stepNo]
                              ? <small className="step-defect-linked">Defect: {stepDefectCodes[x.stepNo]}</small>
                              : (
                                <button
                                  type="button"
                                  className="step-create-defect"
                                  title="Create Defect"
                                  onClick={() => openDefectEditorForStep(x)}
                                >
                                  + Defect
                                </button>
                              )
                          )}
                        </span>
                      </span>
                    </div>
                  );
                })}
              </div>
              <div className="execution-notes">
                <label>
                  Actual Result (สรุปผลที่เกิดขึ้นจริง)
                  <textarea
                    rows={3}
                    value={actual}
                    onChange={(e) => setActual(e.target.value)}
                    placeholder="สรุปผลที่เกิดขึ้นจริง"
                  />
                </label>
                <label>
                  Comment / หมายเหตุเพิ่มเติม
                  <textarea
                    rows={3}
                    value={comment}
                    onChange={(e) => setComment(e.target.value)}
                    placeholder="หมายเหตุเพิ่มเติม"
                  />
                </label>
              </div>
              {/* Action Bar (§14) — เดิมมี Pass/Fail/Blocked ให้ผู้ใช้กำหนด Overall Result เอง เอาออก
                  หมดตาม spec เหลือ 3 ปุ่มนี้เท่านั้น */}
              <div className="execution-actions">
                <button className="btn" disabled={saving} onClick={saveProgress}>
                  {saving ? <><span className="spinner inline" aria-hidden="true" /> กำลังบันทึก...</> : "Save Progress"}
                </button>
                <button className="result-btn skip" disabled={saving} onClick={openSkipModal}>
                  Skip Test Case
                </button>
                <button className="result-btn pass" disabled={saving} onClick={completeTest}>
                  Complete Test
                </button>
                <small className="execution-shortcut-hint">Shortcuts: P = Pass · F = Fail · B = Blocked · N = Next Case</small>
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
          <aside className="card execution-history">
            <div className="history-title">
              <div><h3>Execution History</h3><p>ประวัติของ {selected?.testCaseCode ?? "-"}</p></div>
              <span>{selected?.history.length ?? 0} Runs</span>
            </div>
            {selected?.history.length ? (
              selected.history.map((x) => (
                <div className="history-item" key={x.testExecutionId}>
                  <div className="history-item-head">
                    <Badge tone={executionStatusTone(x.status)}>
                      {x.status}
                    </Badge>
                    <span className="history-run">Run #{x.executionNo}</span>
                  </div>
                  <p>{x.actualResult || "-"}</p>
                  {x.comment && <small className="history-comment">Comment: {x.comment}</small>}
                  <small>
                    {x.testerName} ·{" "}
                    {x.completedAt
                      ? formatThaiDateTime(x.completedAt)
                      : "-"}
                  </small>
                  {/* แยกตำแหน่งปุ่มลบออกจาก Run number (spec §3.3) ลดโอกาสกดพลาดบนมือถือ */}
                  <div className="history-item-foot">
                    <button
                      className="history-delete"
                      disabled={deletingHistoryId === x.testExecutionId || workspace?.status === "Closed" || workspace?.status === "Cancelled"}
                      onClick={() => removeExecution(x)}
                      title={workspace?.status === "Closed" || workspace?.status === "Cancelled" ? "Cycle ปิดแล้ว ไม่สามารถลบผลได้" : "ลบผลการทดสอบ"}
                    >
                      {deletingHistoryId === x.testExecutionId
                        ? <><span className="spinner inline" aria-hidden="true" /> กำลังลบ...</>
                        : <><span className="material-symbols-outlined" aria-hidden="true">close</span> ลบ</>}
                    </button>
                  </div>
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
