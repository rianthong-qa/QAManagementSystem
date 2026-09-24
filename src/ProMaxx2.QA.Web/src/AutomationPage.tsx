import { Fragment, useEffect, useMemo, useState } from "react";
import { confirmDialog } from "./components/dialogStore";
import { formatThaiDateTime } from "./dateTime";
import { apiUrl } from "./api";
import {
  automationCaseTone as caseStatusTone,
  automationExecutionTone as executionStatusTone,
  automationCoverage,
  parseDslSteps,
} from "./automationUtils";
import type { AutomationCaseItem, AutomationVersionItem, AutomationActionItem, AutomationObjectItem, AutomationAgentItem, AutomationJobItem, FlakyCandidateItem, RetryPolicyItem, AutomationStepResultItem, AutomationExecutionItem, AutomationEvidenceItem, TestCandidate, TestCaseDetailItem, AutomationDashboardItem } from "./automation/types";
import { token, fetchJson, isAbort, useDebounced, AI_GENERATE_TIMEOUT_MS, targetTone, failureTone, sampleDsl } from "./automation/shared";
import { ModalShell, Badge, Pager } from "./automation/ui";
import { VersionEditor, RunModal, BatchRunModal, QuarantineModal } from "./automation/caseModals";
import { ActionLibraryTab, ObjectRepositoryTab, RetryPolicyTab, AgentsSection } from "./automation/manageTabs";
import { ExecutionTab, FailureDashboardTab } from "./automation/executionTabs";
import { AutomationSuiteTab } from "./automation/suiteTab";
import { AutomationScheduleTab, AutomationBuildTriggerTab, AutomationWebhookTab } from "./automation/scheduleTabs";
import { AutomationDataSnapshotTab, AutomationDataSeedTab, AutomationEnvironmentDataProfileTab } from "./automation/dataTabs";

const aiGenerateErrorMessage = (e: unknown) =>
  e instanceof DOMException && e.name === "TimeoutError"
    ? `AI ใช้เวลานานเกินไป (เกิน ${AI_GENERATE_TIMEOUT_MS / 1000} วินาที) ไม่ได้รับคำตอบจาก AI Provider — กรุณาลองใหม่ หรือตรวจสอบการตั้งค่า AI ที่ Setting Center`
    : e instanceof Error ? e.message : "Generate AI ไม่สำเร็จ";

const evidenceTone: Record<string, string> = { Screenshot: "blue", SqlResult: "yellow", AutomationLog: "green", AppLog: "yellow", Video: "blue" };

function moduleTreeOptions(modules: { moduleId: string; moduleCode: string; moduleName: string; parentModuleId?: string; sortOrder?: number }[]): React.ReactElement[] {
  const comparator = (a: { sortOrder?: number; moduleCode: string }, b: { sortOrder?: number; moduleCode: string }) => (a.sortOrder ?? 999) - (b.sortOrder ?? 999) || a.moduleCode.localeCompare(b.moduleCode);
  const rows: { module: (typeof modules)[number]; depth: number }[] = [];
  const visited = new Set<string>();
  const append = (parentId: string | undefined, depth: number) => {
    modules.filter((m) => (m.parentModuleId || undefined) === parentId && !visited.has(m.moduleId)).sort(comparator).forEach((m) => {
      visited.add(m.moduleId);
      rows.push({ module: m, depth });
      append(m.moduleId, depth + 1);
    });
  };
  append(undefined, 0);
  modules.forEach((m) => { if (!visited.has(m.moduleId)) { visited.add(m.moduleId); rows.push({ module: m, depth: 0 }); } });
  return rows.map(({ module: m, depth }) => (
    <option key={m.moduleId} value={m.moduleId} className={depth === 0 ? "module-root-option" : "module-child-option"}>
      {depth ? `${"　".repeat(depth)}└ ` : "▾ "}{m.moduleCode ? `${m.moduleCode} · ` : ""}{m.moduleName}
    </option>
  ));
}

const splitTaskText = (text: string) => {
  const idx = text.indexOf("—");
  return idx > -1 ? [text.slice(0, idx).trim(), text.slice(idx + 1).trim()] : [text, ""];
};

const formatDuration = (ms?: number) => {
  if (ms == null) return "-";
  const s = Math.max(0, Math.round(ms / 1000));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}:${String(sec).padStart(2, "0")}`;
};

const taskClass = (text: string, tab: string) => {
  if (/Fail/i.test(text)) return "red";
  if (/Maintenance/i.test(text)) return "orange";
  if (tab === "suites" || tab === "manage" || tab === "agents") return "green";
  return "blue";
};

const taskIcon = (tab: string) => (tab === "execution" ? "!" : tab === "suites" ? "▶" : tab === "manage" || tab === "agents" ? "◉" : "▤");

export function AutomationPage({
  projectId, releaseId, buildId, canEdit, canValidate, canApprove, canRun, canManage, canViewEvidence, canGenerateAi, canCreateDefect,
}: {
  projectId?: string; releaseId?: string; buildId?: string; canView: boolean; canEdit: boolean; canValidate: boolean; canApprove: boolean; canRun: boolean; canManage: boolean; canViewEvidence: boolean; canGenerateAi: boolean; canCreateDefect: boolean;
}) {
  const [tab, setTab] = useState("dashboard");
  const [headSearch, setHeadSearch] = useState("");
  const [caseStatusFilter, setCaseStatusFilter] = useState("all");
  const [caseTargetFilter, setCaseTargetFilter] = useState("all");
  const [casePage, setCasePage] = useState(1);
  const [execFilter, setExecFilter] = useState("all");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [reload, setReload] = useState(0);
  const headers = useMemo(() => ({ "Content-Type": "application/json", Authorization: `Bearer ${token()}` }), []);

  const [cases, setCases] = useState<AutomationCaseItem[]>([]);
  const [actions, setActions] = useState<AutomationActionItem[]>([]);
  const [objects, setObjects] = useState<AutomationObjectItem[]>([]);
  const [agents, setAgents] = useState<AutomationAgentItem[]>([]);
  const [jobs, setJobs] = useState<AutomationJobItem[]>([]);
  const [executions, setExecutions] = useState<AutomationExecutionItem[]>([]);
  const [dash, setDash] = useState<AutomationDashboardItem | null>(null);

  const [selectedCase, setSelectedCase] = useState<AutomationCaseItem | null>(null);
  const [versions, setVersions] = useState<AutomationVersionItem[]>([]);
  const [versionError, setVersionError] = useState("");

  const [createModal, setCreateModal] = useState(false);
  const [candidates, setCandidates] = useState<TestCandidate[]>([]);
  const [createBusy, setCreateBusy] = useState(false);
  const [createLoadError, setCreateLoadError] = useState("");
  const [createModules, setCreateModules] = useState<{ moduleId: string; moduleCode: string; moduleName: string; parentModuleId?: string; sortOrder?: number }[]>([]);
  const [createModuleFilter, setCreateModuleFilter] = useState("");
  const [createPick, setCreatePick] = useState<TestCandidate | null>(null);
  const [createdCaseId, setCreatedCaseId] = useState("");
  const [newDsl, setNewDsl] = useState(sampleDsl);
  const [newVersionError, setNewVersionError] = useState("");
  const [createSearch, setCreateSearch] = useState("");
  const [createPickSteps, setCreatePickSteps] = useState<{ stepNo: number; action: string; testData?: string; expectedResult: string }[]>([]);
  const [wizardStep, setWizardStep] = useState(1);
  const [wizardPriority, setWizardPriority] = useState("");
  const [wizardPage, setWizardPage] = useState(1);
  const [wizardType, setWizardType] = useState("WindowsUI");
  const [wizardNote, setWizardNote] = useState("");
  const [createDetail, setCreateDetail] = useState<TestCaseDetailItem | null>(null);
  const [aiConf, setAiConf] = useState<number | null>(null);
  const [valErrors, setValErrors] = useState("");
  const [validatedOk, setValidatedOk] = useState(false);
  const [createdCode, setCreatedCode] = useState("");
  const [createdStatus, setCreatedStatus] = useState("Draft");

  const [actionModal, setActionModal] = useState(false);
  const [objectModal, setObjectModal] = useState(false);

  const [runModal, setRunModal] = useState(false);
  const [manageTab, setManageTab] = useState("actions");
  const [batchModal, setBatchModal] = useState(false);

  const [execDetail, setExecDetail] = useState<AutomationExecutionItem | null>(null);
  const [evidenceBusy, setEvidenceBusy] = useState("");
  const [classifyBusy, setClassifyBusy] = useState("");
  const [classification, setClassification] = useState<{ failureType: string; isProductDefectCandidate: boolean; recommendation: string; detail?: string } | null>(null);
  const [aiAnalysis, setAiAnalysis] = useState<{ classification: string; confidence: number; summary: string; recommendation: string } | null>(null);
  const [defectResult, setDefectResult] = useState<string>("");

  const [flakyCandidates, setFlakyCandidates] = useState<FlakyCandidateItem[]>([]);
  const [retryPolicy, setRetryPolicy] = useState<RetryPolicyItem | null>(null);
  const [maintenanceBusy, setMaintenanceBusy] = useState(false);
  const [maintenanceOwnerInput, setMaintenanceOwnerInput] = useState("");
  const [maintenanceNote, setMaintenanceNote] = useState("");
  const [quarantineModalFor, setQuarantineModalFor] = useState<FlakyCandidateItem | null>(null);

  const pid = projectId ?? "";

  // AUT-UI-001: ผลจำแนก/วิเคราะห์ AI/Defect เป็นของ execution ใดตัวหนึ่ง — เปลี่ยน execution ต้องล้าง ไม่ให้ผลของตัวก่อนค้างใน modal
  const execDetailId = execDetail?.automationExecutionId;
  useEffect(() => { setClassification(null); setAiAnalysis(null); setDefectResult(""); setClassifyBusy(""); }, [execDetailId]);

  useEffect(() => {
    if (!pid) { setCases([]); setObjects([]); setExecutions([]); setDash(null); return; }
    const h = { Authorization: `Bearer ${token()}` };
    const ctrl = new AbortController();
    const signal = ctrl.signal;
    // endpoint เสริม (flaky/retry-policy/agents/actions) อาจตอบ 403 ตามสิทธิ์ — ใช้ค่าว่างได้; ส่วน cases/jobs/executions/dashboard ต้องแจ้ง error
    const optional = (url: string, fallback: unknown) => fetch(url, { headers: h, signal }).then((r) => (r.ok ? r.json() : fallback));
    setError("");
    // Cases/jobs/executions here deliberately stay a flat "up to 200" load (AUT-P2-001 kept this shared, cross-
    // cutting fetch as-is) — it feeds dashboard KPIs, CSV export, and the batch-run/suite case pickers, none of
    // which need true pagination. The three endpoints now always return a PagedResult ({total, rows}) — see
    // AutomationCasesTab/ExecutionTab below for the components that fetch real server-paginated pages of their own.
    Promise.all([
      fetchJson(`${apiUrl}/automation/cases?projectId=${pid}&page=1&size=200`, h, signal),
      optional(`${apiUrl}/automation/objects?projectId=${pid}`, []),
      fetchJson(`${apiUrl}/automation/jobs?projectId=${pid}${buildId ? `&buildId=${buildId}` : ""}&page=1&size=200`, h, signal),
      fetchJson(`${apiUrl}/automation/executions?projectId=${pid}${buildId ? `&buildId=${buildId}` : ""}&page=1&size=200`, h, signal),
      optional(`${apiUrl}/automation/agents`, []),
      optional(`${apiUrl}/automation/actions`, []),
      fetchJson(`${apiUrl}/automation/dashboard?projectId=${pid}`, h, signal),
      optional(`${apiUrl}/automation/cases/flaky-candidates?projectId=${pid}`, []),
      optional(`${apiUrl}/automation/settings/retry-policy`, null),
    ])
      .then(([c, o, j, e, a, ac, d, fk, rp]) => {
        setCases(Array.isArray(c?.rows) ? c.rows : []);
        setObjects(Array.isArray(o) ? o : []);
        setJobs(Array.isArray(j?.rows) ? j.rows : []);
        setExecutions(Array.isArray(e?.rows) ? e.rows : []);
        setAgents(Array.isArray(a) ? a : []);
        setActions(Array.isArray(ac) ? ac : []);
        setDash(d && typeof d === "object" && d.automationCases != null ? d : null);
        setFlakyCandidates(Array.isArray(fk) ? fk : []);
        setRetryPolicy(rp && typeof rp === "object" ? (rp as RetryPolicyItem) : null);
      })
      .catch((err) => { if (!isAbort(err)) setError(`โหลดข้อมูล Automation ไม่สำเร็จ${err instanceof Error ? ` (${err.message})` : ""} — ข้อมูลที่แสดงอาจไม่ครบ`); });
    return () => ctrl.abort();
  }, [pid, buildId, reload]);

  const openCase = async (item: AutomationCaseItem) => {
    setSelectedCase(item);
    setVersionError("");
    try {
      const r = await fetch(`${apiUrl}/automation/cases/${item.automationCaseId}/versions?projectId=${pid}`, { headers: { Authorization: `Bearer ${token()}` } });
      setVersions(r.ok ? (await r.json()) : []);
    } catch {
      setVersions([]);
    }
  };

  const openCreate = async () => {
    setCreateModal(true);
    resetWizard();
    setError("");
    setCreateLoadError("");
    const h = { Authorization: `Bearer ${token()}` };
    try {
      const [tc, md] = await Promise.all([
        fetchJson(`${apiUrl}/test-cases?projectId=${pid}&automation=true&page=1&size=200`, h),
        fetchJson(`${apiUrl}/projects/${pid}/modules`, h),
      ]);
      setCandidates(Array.isArray(tc?.rows) ? tc.rows : []);
      setCreateModules(Array.isArray(md) ? md.filter((m: { isActive?: boolean }) => m.isActive !== false) : []);
    } catch {
      setCandidates([]);
      setCreateModules([]);
      setCreateLoadError("โหลดรายการ Test Case/Module ไม่สำเร็จ — ปิดหน้าต่างแล้วลองใหม่");
    }
  };

  const resetWizard = () => {
    setCreatePick(null);
    setCreateDetail(null);
    setCreatePickSteps([]);
    setCreatedCaseId("");
    setCreatedCode("");
    setCreatedStatus("Draft");
    setNewVersionError("");
    setNewDsl(sampleDsl);
    setCreateSearch("");
    setCreateModuleFilter("");
    setWizardPriority("");
    setWizardPage(1);
    setWizardType("WindowsUI");
    setWizardNote("");
    setAiConf(null);
    setValErrors("");
    setValidatedOk(false);
    setWizardStep(1);
  };

  const pickCandidate = async (c: TestCandidate) => {
    setCreatePick(c);
    setCreateDetail(null);
    setCreatePickSteps([]);
    setCreatedCaseId("");
    setCreatedCode("");
    setNewVersionError("");
    setValErrors("");
    setValidatedOk(false);
    setAiConf(null);
    setNewDsl(sampleDsl);
    try {
      const r = await fetch(`${apiUrl}/test-cases/${c.testCaseId}`, { headers: { Authorization: `Bearer ${token()}` } });
      const d = r.ok ? await r.json() : null;
      setCreateDetail(d && typeof d === "object" ? d : null);
      setCreatePickSteps(Array.isArray(d?.steps) ? d.steps : []);
    } catch {
      setCreatePickSteps([]);
    }
  };

  const createCase = async (testCaseId: string, automationType = "WindowsUI"): Promise<{ id: string; code: string; status: string } | null> => {
    setCreateBusy(true);
    setError("");
    setNewVersionError("");
    try {
      const r = await fetch(`${apiUrl}/automation/cases?projectId=${pid}`, { method: "POST", headers, body: JSON.stringify({ testCaseId, automationType, ownerUserId: null }) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "สร้าง Automation Case ไม่สำเร็จ");
      }
      const created = await r.json();
      setCreatedCaseId(created.automationCaseId);
      setCreatedCode(created.automationCode ?? "");
      setCreatedStatus(created.status ?? "Draft");
      setNotice("สร้าง Automation Case แล้ว — เขียนหรือ Generate DSL ต่อได้เลย");
      return { id: created.automationCaseId, code: created.automationCode ?? "", status: created.status ?? "Draft" };
    } catch (e) {
      setError(e instanceof Error ? e.message : "สร้าง Automation Case ไม่สำเร็จ");
      return null;
    } finally {
      setCreateBusy(false);
    }
  };

  const generateAiForNewCase = async () => {
    if (!createdCaseId) return;
    setCreateBusy(true);
    setNewVersionError("");
    setValErrors("");
    setValidatedOk(false);
    try {
      const r = await fetch(`${apiUrl}/automation/cases/${createdCaseId}/generate?projectId=${pid}`, { method: "POST", headers, signal: AbortSignal.timeout(AI_GENERATE_TIMEOUT_MS) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "Generate AI ไม่สำเร็จ");
      }
      const v = await r.json();
      setNewDsl(v.dslJson);
      setAiConf(v.aiConfidence != null ? v.aiConfidence : null);
      setCreatedStatus("NeedsReview");
      setNotice(`AI สร้าง DSL แล้ว (confidence ${v.aiConfidence != null ? `${Math.round(v.aiConfidence * 100)}%` : "-"}) — ตรวจแล้วกด Validate`);
    } catch (e) {
      setNewVersionError(aiGenerateErrorMessage(e));
    } finally {
      setCreateBusy(false);
    }
  };

  const createNewVersionAndValidate = async () => {
    if (!createdCaseId) return;
    setCreateBusy(true);
    setNewVersionError("");
    setValErrors("");
    setValidatedOk(false);
    try {
      const r = await fetch(`${apiUrl}/automation/cases/${createdCaseId}/versions?projectId=${pid}`, { method: "POST", headers, body: JSON.stringify({ dslJson: newDsl, changeReason: wizardNote.trim() || "สร้างครั้งแรกจาก Wizard" }) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "สร้าง Version ไม่สำเร็จ");
      }
      const v = await r.json();
      const vr = await fetch(`${apiUrl}/automation/versions/${v.automationVersionId}/validate?projectId=${pid}`, { method: "POST", headers });
      const vd = await vr.json();
      if (vd.validationStatus !== "Valid") {
        const msg = vd.validationErrors || "Validate ไม่ผ่าน";
        setValErrors(msg.includes("Object Repository") ? `${msg} Fix: add the missing Object to Object Repository or change the object parameter to an existing BusinessKey, then Validate again.` : msg);
        throw new Error(msg);
      }
      setValidatedOk(true);
      setCreatedStatus("Validated");
      setWizardStep(4);
      setReload((x) => x + 1);
      setNotice("สร้าง Automation Case + Version และ Validate ผ่านแล้ว — ไปที่ Automation Cases เพื่อตรวจ/อนุมัติ/สั่งรัน");
    } catch (e) {
      setNewVersionError(e instanceof Error ? e.message : "Validate ไม่สำเร็จ");
    } finally {
      setCreateBusy(false);
    }
  };

  const createVersion = async (dslJson: string, changeReason: string) => {
    if (!selectedCase) return;
    setCreateBusy(true);
    setVersionError("");
    try {
      const r = await fetch(`${apiUrl}/automation/cases/${selectedCase.automationCaseId}/versions?projectId=${pid}`, { method: "POST", headers, body: JSON.stringify({ dslJson, changeReason: changeReason || null }) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "สร้าง Version ไม่สำเร็จ");
      }
      await openCase(selectedCase);
      setReload((x) => x + 1);
      setNotice(`สร้าง Version แล้ว (ตอนนี้ Rev ${selectedCase.currentVersionNo + 1}) — กด Validate เพื่อตรวจสอบ`);
    } catch (e) {
      setVersionError(e instanceof Error ? e.message : "สร้าง Version ไม่สำเร็จ");
    } finally {
      setCreateBusy(false);
    }
  };

  const validateVersion = async (v: AutomationVersionItem) => {
    setVersionError("");
    try {
      const r = await fetch(`${apiUrl}/automation/versions/${v.automationVersionId}/validate?projectId=${pid}`, { method: "POST", headers });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "Validate ไม่สำเร็จ");
      }
      if (selectedCase) await openCase(selectedCase);
      setNotice(v.validationStatus === "Valid" ? "Version ผ่านการ Validate แล้ว — อนุมัติได้" : "Version Validate แล้ว");
    } catch (e) {
      setVersionError(e instanceof Error ? e.message : "Validate ไม่สำเร็จ");
    }
  };

  const approveVersion = async (v: AutomationVersionItem) => {
    if (!await confirmDialog({ title: "อนุมัติ Automation Version", message: `อนุมัติ Rev ${v.versionNo} ใช่หรือไม่?\nCase จะเป็น Ready และ Agent รับไปรันได้ทันที`, confirmLabel: "อนุมัติ" })) return;
    setVersionError("");
    try {
      const r = await fetch(`${apiUrl}/automation/versions/${v.automationVersionId}/approve?projectId=${pid}`, { method: "POST", headers });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "อนุมัติไม่สำเร็จ");
      }
      if (selectedCase) await openCase(selectedCase);
      setReload((x) => x + 1);
      setNotice("อนุมัติ Version แล้ว — Automation Case พร้อมรัน");
    } catch (e) {
      setVersionError(e instanceof Error ? e.message : "อนุมัติไม่สำเร็จ");
    }
  };

  const generateAi = async () => {
    if (!selectedCase) return;
    setCreateBusy(true);
    setVersionError("");
    try {
      const r = await fetch(`${apiUrl}/automation/cases/${selectedCase.automationCaseId}/generate?projectId=${pid}`, { method: "POST", headers, signal: AbortSignal.timeout(AI_GENERATE_TIMEOUT_MS) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "Generate AI ไม่สำเร็จ");
      }
      await openCase(selectedCase);
      setReload((x) => x + 1);
      setNotice("AI สร้าง DSL แล้ว — Version ใหม่เป็น NeedsReview รอตรวจ/Validate/อนุมัติ");
    } catch (e) {
      setVersionError(aiGenerateErrorMessage(e));
    } finally {
      setCreateBusy(false);
    }
  };

  const changeTarget = async (target: string) => {
    if (!selectedCase || !canEdit) return;
    setCreateBusy(true);
    setVersionError("");
    try {
      const r = await fetch(`${apiUrl}/automation/cases/${selectedCase.automationCaseId}/target?projectId=${pid}`, { method: "POST", headers, body: JSON.stringify({ targetApp: target }) });
      if (!r.ok) throw new Error("เปลี่ยน Target App ไม่สำเร็จ");
      const updated = await r.json();
      setSelectedCase({ ...selectedCase, automationType: updated.automationType });
      setCases((prev) => prev.map((c) => c.automationCaseId === selectedCase.automationCaseId ? { ...c, automationType: updated.automationType } : c));
      setNotice(`Target App = ${updated.automationType} — งานนี้จะถูก Agent ที่รองรับ ${updated.automationType} รับไปรัน`);
    } catch (e) {
      setVersionError(e instanceof Error ? e.message : "เปลี่ยน Target App ไม่สำเร็จ");
    } finally {
      setCreateBusy(false);
    }
  };

  const openRun = () => {
    setRunModal(true);
    setError("");
  };


  const runCase = async (item: AutomationCaseItem, versionId: string, selBuildId: string, envId: string, agentId: string, priority: number) => {
    if (!agents.some((agent) => agent.isEnabled && agent.connectivity !== "Offline" && ["Online", "Idle", "Available"].includes(agent.status))) {
      setError("ยังไม่มี Automation Agent ที่พร้อมทำงาน งานยังไม่ถูกส่งเข้าคิว");
      return;
    }
    setCreateBusy(true);
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/cases/${item.automationCaseId}/run?projectId=${pid}`, { method: "POST", headers, body: JSON.stringify({ versionId, buildId: selBuildId, environmentId: envId, agentId: agentId || null, priority }) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "สั่งรันไม่สำเร็จ");
      }
      setRunModal(false);
      setTab("execution");
      setReload((x) => x + 1);
      setNotice("ส่งงานเข้าคิวแล้ว — Agent จะมารับงานและรายงานผล");
    } catch (e) {
      setError(e instanceof Error ? e.message : "สั่งรันไม่สำเร็จ");
    } finally {
      setCreateBusy(false);
    }
  };

  const runBatch = async (caseIds: string[], selBuildId: string, envId: string, priority: number) => {
    if (!caseIds.length || !selBuildId || !envId) return;
    if (!agents.some((agent) => agent.isEnabled && agent.connectivity !== "Offline" && ["Online", "Idle", "Available"].includes(agent.status))) {
      setError("ยังไม่มี Automation Agent ที่พร้อมทำงาน งาน Batch ยังไม่ถูกส่งเข้าคิว");
      return;
    }
    setCreateBusy(true);
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/batch-run?projectId=${pid}`, { method: "POST", headers, body: JSON.stringify({ caseIds, buildId: selBuildId, environmentId: envId, agentId: null, priority }) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "สั่งรัน Batch ไม่สำเร็จ");
      }
      const d = await r.json();
      setTab("execution");
      setReload((x) => x + 1);
      setNotice(`รัน Batch สำเร็จ: ${d.created.length} งานเข้าคิว${d.skippedCodes?.length ? ` · ข้าม ${d.skippedCodes.join(", ")}` : ""} — Agents จะรับงานพร้อมกัน`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "สั่งรัน Batch ไม่สำเร็จ");
    } finally {
      setCreateBusy(false);
    }
  };

  const cancelExecution = async (x: AutomationExecutionItem) => {
    if (!await confirmDialog(`ยืนยันยกเลิก Execution "${x.automationCode}" ?`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/executions/${x.automationExecutionId}/cancel?projectId=${pid}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "ยกเลิกไม่สำเร็จ"); }
      const updated = await r.json();
      if (execDetail?.automationExecutionId === x.automationExecutionId) setExecDetail(updated);
      setReload((v) => v + 1);
      setNotice(`ยกเลิก ${x.automationCode} แล้ว`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "ยกเลิกไม่สำเร็จ");
    }
  };

  const rerunExecution = async (x: AutomationExecutionItem) => {
    if (!agents.some((agent) => agent.isEnabled && agent.connectivity !== "Offline" && ["Online", "Idle", "Available"].includes(agent.status))) {
      setError("ยังไม่มี Automation Agent ที่พร้อมทำงาน จึงยัง Retry ไม่ได้");
      return;
    }
    if (!await confirmDialog(`สั่งรัน "${x.automationCode}" ซ้ำ?\nRev ${x.versionNo} · Build ${x.buildNumber} · ${x.environmentName}`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/cases/${x.automationCaseId}/run?projectId=${pid}`, { method: "POST", headers, body: JSON.stringify({ versionId: x.automationVersionId, buildId: x.buildId, environmentId: x.environmentId, agentId: null, priority: 5 }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สั่งรันซ้ำไม่สำเร็จ"); }
      setReload((v) => v + 1);
      setNotice(`ส่งรันซ้ำ ${x.automationCode} เข้าคิวแล้ว — Agent จะรับงานตามลำดับ`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "สั่งรันซ้ำไม่สำเร็จ");
    }
  };

  const toggleAgent = async (a: AutomationAgentItem, enable: boolean) => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/agents/${a.agentId}/${enable ? "enable" : "disable"}`, { method: "POST", headers });
      if (!r.ok) throw new Error("เปลี่ยนสถานะ Agent ไม่สำเร็จ");
      setReload((x) => x + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : "เปลี่ยนสถานะ Agent ไม่สำเร็จ");
    }
  };

  const deleteAgent = async (a: AutomationAgentItem) => {
    if (!await confirmDialog(`ต้องการลบ Agent "${a.agentCode}" ออกหรือไม่?\n(ถ้า Agent ยังรันอยู่จะลงทะเบียนใหม่เองอัตโนมัติ)`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/agents/${a.agentId}`, { method: "DELETE", headers });
      if (!r.ok) throw new Error("ลบ Agent ไม่สำเร็จ");
      setReload((x) => x + 1);
      setNotice(`ลบ Agent ${a.agentCode} แล้ว`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "ลบ Agent ไม่สำเร็จ");
    }
  };

  const openEvidence = async (step: AutomationStepResultItem) => {
    if (!step.evidencePath) return;
    setEvidenceBusy(step.automationStepResultId);
    try {
      const r = await fetch(`${apiUrl}/automation/executions/${execDetail?.automationExecutionId}/evidence/${step.automationStepResultId}?projectId=${pid}`, { headers: { Authorization: `Bearer ${token()}` } });
      if (!r.ok) throw new Error("เปิด Evidence ไม่สำเร็จ");
      const blob = await r.blob();
      const url = URL.createObjectURL(blob);
      window.open(url, "_blank", "noopener,noreferrer");
      setTimeout(() => URL.revokeObjectURL(url), 60000);
    } catch (e) {
      setError(e instanceof Error ? e.message : "เปิด Evidence ไม่สำเร็จ");
    } finally {
      setEvidenceBusy("");
    }
  };

  const openEvidenceFile = async (evidence: AutomationEvidenceItem) => {
    setEvidenceBusy(evidence.automationEvidenceId);
    try {
      const r = await fetch(`${apiUrl}/automation/executions/${execDetail?.automationExecutionId}/evidence/${evidence.automationEvidenceId}?projectId=${pid}`, { headers: { Authorization: `Bearer ${token()}` } });
      if (!r.ok) throw new Error("เปิด Evidence ไม่สำเร็จ");
      const blob = await r.blob();
      const url = URL.createObjectURL(blob);
      window.open(url, "_blank", "noopener,noreferrer");
      setTimeout(() => URL.revokeObjectURL(url), 60000);
    } catch (e) {
      setError(e instanceof Error ? e.message : "เปิด Evidence ไม่สำเร็จ");
    } finally {
      setEvidenceBusy("");
    }
  };

  const runClassify = async () => {
    if (!execDetail) return;
    setClassifyBusy("classify"); setClassification(null);
    try {
      const r = await fetch(`${apiUrl}/automation/executions/${execDetail.automationExecutionId}/classify?projectId=${pid}`, { method: "POST", headers: { Authorization: `Bearer ${token()}` } });
      if (!r.ok) throw new Error("จำแนก Fail ไม่สำเร็จ");
      setClassification(await r.json());
    } catch (e) { setError(e instanceof Error ? e.message : "จำแนก Fail ไม่สำเร็จ"); } finally { setClassifyBusy(""); }
  };

  const runAnalyze = async () => {
    if (!execDetail) return;
    setClassifyBusy("analyze"); setAiAnalysis(null);
    try {
      const r = await fetch(`${apiUrl}/automation/executions/${execDetail.automationExecutionId}/analyze?projectId=${pid}`, { method: "POST", headers: { Authorization: `Bearer ${token()}` } });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "AI วิเคราะห์ไม่สำเร็จ"); }
      setAiAnalysis(await r.json());
    } catch (e) { setError(e instanceof Error ? e.message : "AI วิเคราะห์ไม่สำเร็จ"); } finally { setClassifyBusy(""); }
  };

  const runCreateDefect = async () => {
    if (!execDetail || execDetail.defectId || defectResult) return;
    if (!await confirmDialog(`สร้าง Defect จาก Execution "${execDetail.automationCode}" (Build ${execDetail.buildNumber}) ?`)) return;
    const executionId = execDetail.automationExecutionId;
    setClassifyBusy("defect");
    try {
      const r = await fetch(`${apiUrl}/automation/executions/${execDetail.automationExecutionId}/defect?projectId=${pid}`, { method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${token()}` }, body: JSON.stringify({ classification: aiAnalysis?.classification ?? null, severity: "High", title: null, description: null }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Defect ไม่สำเร็จ"); }
      const d = await r.json();
      setDefectResult(d.defectCode);
      // เชื่อม defect กับ execution ใน state ทันที — ปุ่ม "สร้าง Defect" จะหายไปทั้งใน modal และเมื่อเปิดซ้ำจากรายการ
      const link = (x: AutomationExecutionItem) => (x.automationExecutionId === executionId ? { ...x, defectId: d.defectId ?? x.defectId ?? executionId } : x);
      setExecDetail((prev) => (prev ? link(prev) : prev));
      setExecutions((prev) => prev.map(link));
      setError("");
    } catch (e) { setError(e instanceof Error ? e.message : "สร้าง Defect ไม่สำเร็จ"); } finally { setClassifyBusy(""); }
  };

  const assignMaintenanceOwner = async () => {
    if (!selectedCase || !maintenanceOwnerInput.trim()) return;
    setMaintenanceBusy(true);
    try {
      const r = await fetch(`${apiUrl}/automation/cases/${selectedCase.automationCaseId}/maintenance/owner?projectId=${pid}`, { method: "POST", headers, body: JSON.stringify({ ownerUserId: maintenanceOwnerInput.trim() }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "มอบหมายไม่สำเร็จ"); }
      const updated = await r.json();
      setSelectedCase(updated);
      setReload((v) => v + 1);
      setNotice("มอบหมายผู้รับผิดชอบซ่อมแล้ว");
    } catch (e) { setError(e instanceof Error ? e.message : "มอบหมายไม่สำเร็จ"); } finally { setMaintenanceBusy(false); }
  };

  const resolveMaintenance = async () => {
    if (!selectedCase) return;
    setMaintenanceBusy(true);
    try {
      const r = await fetch(`${apiUrl}/automation/cases/${selectedCase.automationCaseId}/maintenance/resolve?projectId=${pid}`, { method: "POST", headers, body: JSON.stringify({ resolutionNote: maintenanceNote.trim() || null }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "แก้ไขไม่สำเร็จ"); }
      const updated = await r.json();
      setSelectedCase(updated);
      setMaintenanceNote("");
      setMaintenanceOwnerInput("");
      await openCase(updated);
      setReload((v) => v + 1);
      setNotice("บันทึกการซ่อมแล้ว — Case กลับไปสถานะ NeedsReview รอ Validate/อนุมัติใหม่");
    } catch (e) { setError(e instanceof Error ? e.message : "แก้ไขไม่สำเร็จ"); } finally { setMaintenanceBusy(false); }
  };

  const quarantineCase = async (caseId: string, reason: string, ownerUserId: string, expiresAt: string) => {
    setMaintenanceBusy(true);
    try {
      const r = await fetch(`${apiUrl}/automation/cases/${caseId}/quarantine?projectId=${pid}`, { method: "POST", headers, body: JSON.stringify({ reason, ownerUserId: ownerUserId.trim() || null, expiresAt: expiresAt || null }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "Quarantine ไม่สำเร็จ"); }
      setQuarantineModalFor(null);
      setReload((v) => v + 1);
      setNotice("Quarantine Case แล้ว — จะไม่นับเป็น Product Fail จนกว่าจะ Unquarantine");
    } catch (e) { setError(e instanceof Error ? e.message : "Quarantine ไม่สำเร็จ"); } finally { setMaintenanceBusy(false); }
  };

  const unquarantineCase = async (caseId: string) => {
    if (!await confirmDialog({ title: "ยกเลิก Quarantine", message: "นำ Case นี้กลับเข้าการรันปกติใช่หรือไม่? Schedule / Batch จะรัน Case นี้อีกครั้ง", confirmLabel: "ยกเลิก Quarantine" })) return;
    setMaintenanceBusy(true);
    try {
      const r = await fetch(`${apiUrl}/automation/cases/${caseId}/unquarantine?projectId=${pid}`, { method: "POST", headers });
      if (!r.ok) throw new Error("Unquarantine ไม่สำเร็จ");
      if (selectedCase?.automationCaseId === caseId) setSelectedCase(await r.json());
      setReload((v) => v + 1);
      setNotice("นำ Case ออกจาก Quarantine แล้ว");
    } catch (e) { setError(e instanceof Error ? e.message : "Unquarantine ไม่สำเร็จ"); } finally { setMaintenanceBusy(false); }
  };

  const updateRetryPolicy = async (policy: RetryPolicyItem) => {
    setMaintenanceBusy(true);
    try {
      const r = await fetch(`${apiUrl}/automation/settings/retry-policy`, { method: "PUT", headers, body: JSON.stringify({ maxAttempts: policy.maxAttempts, backoffSeconds: policy.backoffSeconds, enabled: policy.enabled }) });
      if (!r.ok) throw new Error("บันทึก Retry Policy ไม่สำเร็จ");
      setRetryPolicy(await r.json());
      setNotice("บันทึก Retry Policy แล้ว");
    } catch (e) { setError(e instanceof Error ? e.message : "บันทึก Retry Policy ไม่สำเร็จ"); } finally { setMaintenanceBusy(false); }
  };

  const totalCandidates = cases.length;
  const existingTestCaseIds = new Set(cases.map((c) => c.testCaseId));
  const ready = cases.filter((x) => x.status === "Ready").length;
  const maintenance = cases.filter((x) => x.status === "MaintenanceRequired").length;
  const needsReview = cases.filter((x) => x.status === "NeedsReview").length;
  const inProgress = cases.filter((x) => x.status === "Draft" || x.status === "NeedsReview" || x.status === "Validated" || x.status === "Approved").length;
  const running = executions.filter((x) => x.status === "Running").length;
  const passToday = executions.filter((x) => x.status === "Passed").length;
  const failToday = executions.filter((x) => x.status === "Failed").length;
  const agentsOnline = agents.filter((x) => x.connectivity === "Online").length;
  const coverage = automationCoverage(cases);
  const kTotal = dash?.automationCases ?? totalCandidates;
  const kReady = dash?.ready ?? ready;
  const kMaintenance = dash?.maintenanceRequired ?? maintenance;
  const kNeedsReview = dash?.needsReview ?? needsReview;
  const kInProgress = dash?.inProgress ?? inProgress;
  const kRunning = dash?.running ?? running;
  const kPassToday = dash?.passToday ?? passToday;
  const kFailToday = dash?.failToday ?? failToday;
  const kAgentsOnline = dash?.agentsOnline ?? agentsOnline;
  const kAgentsTotal = dash?.agentsTotal ?? agents.length;
  const kReadyCoverage = dash?.readyCoverage ?? coverage;

  const goCases = (status: string) => { setCaseStatusFilter(status); setCaseTargetFilter("all"); setCasePage(1); setTab("cases"); };
  const goExecutionWithStatus = (status: string) => { setExecFilter(status); setTab("execution"); };

  const metrics = [
    { label: "Automation Cases", value: kTotal, note: "ทั้งหมด", tone: "blue", icon: "◇", go: () => goCases("all") },
    { label: "Ready", value: kReady, note: `${kReadyCoverage}% coverage`, tone: "green", icon: "✓", go: () => goCases("Ready") },
    { label: "Maintenance", value: kMaintenance, note: "ต้องแก้ DSL / Object", tone: "orange", icon: "⌕", go: () => goCases("MaintenanceRequired") },
    { label: "Running", value: kRunning, note: "กำลังทำงาน", tone: "purple", icon: "▶", go: () => goExecutionWithStatus("Running") },
    { label: "Failed", value: kFailToday, note: "ต้องตรวจสอบ", tone: "red", icon: "×", go: () => goExecutionWithStatus("Failed") },
    { label: "Agents Online", value: `${kAgentsOnline} / ${kAgentsTotal}`, note: "พร้อมใช้งาน", tone: "cyan", icon: "♙", go: () => { setManageTab("agents"); setTab("manage"); } },
  ] as { label: string; value: number | string; note: string; tone: string; icon: string; go: () => void }[];

  // AUT-P2-001: the Cases table is server-paginated for real (separate fetch from the shared "up to 200" load
  // above) — filters/sort become query params instead of a client-side .filter(), and only the current page's rows
  // ever reach the browser.
  const casePageSize = 15;
  const [casesPaged, setCasesPaged] = useState<{ total: number; rows: AutomationCaseItem[] }>({ total: 0, rows: [] });
  const [caseSortBy, setCaseSortBy] = useState("created");
  const [casesError, setCasesError] = useState("");
  const debouncedHeadSearch = useDebounced(headSearch.trim());
  const casePageCount = Math.max(1, Math.ceil(casesPaged.total / casePageSize));
  useEffect(() => setCasePage(1), [headSearch, caseStatusFilter, caseTargetFilter, caseSortBy]);
  // เลือกได้เฉพาะแถวในหน้าปัจจุบัน — ล้างการเลือกทุกครั้งที่เปลี่ยนหน้า/ตัวกรอง กันเลือก Case จากหน้าอื่นค้างไว้
  // แล้วโดนลบไปด้วยโดยไม่รู้ตัว
  const [selectedCaseIds, setSelectedCaseIds] = useState<Set<string>>(new Set());
  const [bulkDeleteBusy, setBulkDeleteBusy] = useState(false);
  useEffect(() => setSelectedCaseIds(new Set()), [pid, casePage, headSearch, caseStatusFilter, caseTargetFilter, caseSortBy]);
  const toggleCaseSelected = (id: string) => setSelectedCaseIds((prev) => { const next = new Set(prev); if (next.has(id)) next.delete(id); else next.add(id); return next; });
  const allCasesOnPageSelected = casesPaged.rows.length > 0 && casesPaged.rows.every((c) => selectedCaseIds.has(c.automationCaseId));
  const toggleSelectAllCasesOnPage = () => setSelectedCaseIds((prev) => {
    const next = new Set(prev);
    if (allCasesOnPageSelected) casesPaged.rows.forEach((c) => next.delete(c.automationCaseId));
    else casesPaged.rows.forEach((c) => next.add(c.automationCaseId));
    return next;
  });
  const hardDeleteSelectedCases = async () => {
    if (!selectedCaseIds.size || !pid) return;
    const targets = casesPaged.rows.filter((c) => selectedCaseIds.has(c.automationCaseId));
    const preview = targets.slice(0, 5).map((c) => c.automationCode).join(", ") + (targets.length > 5 ? ` และอีก ${targets.length - 5} รายการ` : "");
    if (!await confirmDialog(`ยืนยันลบ Automation Case ถาวร ${selectedCaseIds.size} รายการ (${preview})?\n\nการลบนี้ไม่สามารถกู้คืนได้ รวม Version/DSL และประวัติการรัน (Execution) ทั้งหมดของ Case ที่เลือกไปด้วย`)) return;
    setBulkDeleteBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/cases/hard-delete?projectId=${pid}`, { method: "POST", headers, body: JSON.stringify({ automationCaseIds: [...selectedCaseIds] }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "ลบ Automation Case ไม่สำเร็จ"); }
      setSelectedCaseIds(new Set());
      setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "ลบ Automation Case ไม่สำเร็จ"); }
    finally { setBulkDeleteBusy(false); }
  };
  useEffect(() => {
    if (!pid) { setCasesPaged({ total: 0, rows: [] }); return; }
    const ctrl = new AbortController();
    const qs = new URLSearchParams({ projectId: pid, page: String(casePage), size: String(casePageSize), sortBy: caseSortBy });
    if (debouncedHeadSearch) qs.set("search", debouncedHeadSearch);
    if (caseStatusFilter !== "all") qs.set("status", caseStatusFilter);
    if (caseTargetFilter !== "all") qs.set("automationTarget", caseTargetFilter);
    fetchJson(`${apiUrl}/automation/cases?${qs}`, headers, ctrl.signal)
      .then((d) => { setCasesError(""); setCasesPaged(d && typeof d === "object" && Array.isArray(d.rows) ? d : { total: 0, rows: [] }); })
      .catch((e) => { if (!isAbort(e)) setCasesError("โหลดรายการ Automation Case ไม่สำเร็จ"); });
    return () => ctrl.abort();
  }, [pid, casePage, casePageSize, debouncedHeadSearch, caseStatusFilter, caseTargetFilter, caseSortBy, headers, reload]);

  const hasActiveWork = kRunning > 0 || jobs.some((j) => j.status === "Queued" || j.status === "Assigned" || j.status === "Running");
  useEffect(() => {
    if (!hasActiveWork) return;
    const t = setInterval(() => { if (!document.hidden) setReload((v) => v + 1); }, 15000);
    return () => clearInterval(t);
  }, [hasActiveWork]);

  const modName = createModules.find((m) => m.moduleId === createPick?.moduleId)?.moduleName;
  const hasAutomation = createPick ? existingTestCaseIds.has(createPick.testCaseId) : false;
  const preconditionsList = createDetail?.preconditions ? createDetail.preconditions.split(/\r?\n|\|/).map((s) => s.trim()).filter(Boolean) : [];
  const lastStepExpected = createDetail?.steps?.length ? createDetail.steps[createDetail.steps.length - 1].expectedResult : "";
  const wizardList = candidates.filter((c) => {
    const q = createSearch.trim().toLowerCase();
    return (!q || c.testCaseCode.toLowerCase().includes(q) || c.title.toLowerCase().includes(q)) && (!createModuleFilter || c.moduleId === createModuleFilter) && (!wizardPriority || c.priority === wizardPriority);
  });
  const wizardPageSize = 8;
  const wizardPageCount = Math.max(1, Math.ceil(wizardList.length / wizardPageSize));
  const wizardPaged = wizardList.slice((wizardPage - 1) * wizardPageSize, wizardPage * wizardPageSize);
  useEffect(() => setWizardPage(1), [createSearch, createModuleFilter, wizardPriority]);
  const readyChecks = [
    { ok: createDetail?.status === "Ready", text: "Test Case อยู่ในสถานะ Ready" },
    { ok: !!createDetail?.objective?.trim(), text: "มี Objective" },
    { ok: !!createDetail?.moduleId, text: "ระบุ Module แล้ว" },
    { ok: (createDetail?.steps?.length ?? 0) > 0, text: `มี Test Steps (${createDetail?.steps?.length ?? 0} ขั้นตอน) สำหรับ AI Interpreter` },
  ];
  const dslSteps = parseDslSteps(newDsl);
  const dslActions = dslSteps.length;
  const dslAssertions = dslSteps.filter((s) => s.action.startsWith("EXPECT_")).length;
  const valErrCount = validatedOk ? 0 : (valErrors ? valErrors.split("\n").filter((l) => l.trim()).length : "—");

  const openCreatedCase = () => {
    setCreateModal(false);
    setTab("cases");
    if (createdCaseId && createPick) {
      openCase({ automationCaseId: createdCaseId, testCaseId: createPick.testCaseId, testCaseCode: createPick.testCaseCode, testCaseTitle: createPick.title, automationCode: createdCode, automationType: wizardType, status: createdStatus, currentVersionNo: 1, versionCount: 1, ownerName: undefined, isAiGenerated: aiConf != null, createdAt: new Date().toISOString() });
    }
  };
  const caseByExecId = useMemo(() => { const m = new Map<string, AutomationCaseItem>(); cases.forEach((c) => m.set(c.automationCaseId, c)); return m; }, [cases]);
  const primaryAgent = agents.find((a) => a.connectivity === "Online") ?? agents[0];
  const healthPct = kAgentsTotal ? Math.round((kAgentsOnline / kAgentsTotal) * 100) : 0;

  const exportCases = () => {
    const rows: string[][] = [["Automation Code", "Test Case Code", "Test Case Title", "Target App", "Status", "Version", "Owner"]];
    cases.forEach((c) => rows.push([c.automationCode, c.testCaseCode, c.testCaseTitle, c.automationType, c.status, `Rev ${c.currentVersionNo}`, c.ownerName ?? "-"]));
    const csv = "\ufeff" + rows.map((row) => row.map((v) => `"${String(v).replaceAll('"', '""')}"`).join(",")).join("\r\n");
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = "automation-cases.csv";
    a.click();
    URL.revokeObjectURL(url);
  };

  const workflowSteps = [
    { t: "สร้าง Automation Case", d: kTotal ? `มี ${kTotal} case` : "ยังไม่มี — สร้างจาก Test Case", done: kTotal > 0, tab: "cases", icon: "▧" },
    { t: "Generate DSL / AI", d: kInProgress ? `มี ${kInProgress} case กำลังเขียน DSL` : "DSL ครบแล้ว", done: kInProgress === 0 && kTotal > 0, tab: "cases", icon: "✦" },
    { t: "Validate", d: kReady ? `Ready ${kReady} case` : "ยังไม่มี case พร้อมรัน", done: kReady > 0, tab: "cases", icon: "⬟" },
    { t: "Run Agent", d: kAgentsOnline ? `${kAgentsOnline} agent online` : "ยังไม่มี agent online", done: kAgentsOnline > 0, tab: "cases", icon: "▣" },
    { t: "Evidence / Result", d: executions.length || kPassToday || kFailToday ? `ผ่าน ${kPassToday} / Fail ${kFailToday}` : "ยังไม่มีผลรัน", done: executions.length > 0, tab: "execution", icon: "⌁" },
  ] as { t: string; d: string; done: boolean; tab: string; icon: string }[];
  const activeWorkflowStep = workflowSteps.findIndex((s) => !s.done);

  const nextActions: { text: string; btn: string; tab: string }[] = [];
  if (kTotal === 0) nextActions.push({ text: "ยังไม่มี Automation Case — เริ่มจากสร้าง Case จาก Test Case ที่เป็น Automation Candidate", btn: "สร้าง Automation Case", tab: "cases" });
  if (kNeedsReview > 0) nextActions.push({ text: `มี ${kNeedsReview} case ต้องตรวจสอบ DSL (AI ต้องการ Human Review) — เปิดรายละเอียดแล้ว Validate/อนุมัติ`, btn: "ไปตรวจ DSL", tab: "cases" });
  if (kReady > 0 && kAgentsOnline === 0) nextActions.push({ text: "มี case พร้อมรัน แต่ยังไม่มี Agent Online — เริ่ม agent\\run-agent.ps1 บนเครื่องทดสอบ", btn: "ดู Agents", tab: "manage" });
  if (kReady > 0 && kAgentsOnline > 0) nextActions.push({ text: `พร้อมรัน ${kReady} case — เลือก Build/Environment แล้วรันเดี่ยวหรือรันเป็น Regression Suite`, btn: "ไป Regression Suites", tab: "cases" });
  if (kFailToday > 0) nextActions.push({ text: `มี Fail วันนี้ ${kFailToday} ครั้ง — ตรวจผล/Evidence และจำแนก Fail ก่อนสร้าง Defect`, btn: "ไป Execution", tab: "execution" });
  if (executions.length > 0 && kFailToday === 0) nextActions.push({ text: "ผลล่าสุดผ่านทั้งหมด — ดูประวัติและ Evidence ในหน้า Execution", btn: "ไป Execution", tab: "execution" });

  // Grouped instead of one flat row of 12 tabs — that read as cluttered
  // (a wall of pills, several needing a horizontal scroll to even see).
  // Each group is one pill in the primary bar; a group with more than one
  // screen reveals a second, smaller row (reusing the same visual family
  // "การจัดการ" already used for its own Action Library/Objects/Agents/Retry
  // sub-nav) so nothing that used to be reachable in one click is lost —
  // most things are now just one click to reveal, one more to open.
  const tabGroups = [
    { id: "dashboard", label: "ภาพรวม", icon: "◉", tabs: [
      { id: "dashboard", label: "ภาพรวม" },
    ] },
    { id: "testing", label: "Automation Testing", icon: "▤", tabs: [
      { id: "cases", label: "Automation Cases" },
      { id: "suites", label: "Automation Suite" },
      { id: "execution", label: "Execution" },
      { id: "failures", label: "Failure Dashboard" },
    ] },
    { id: "scheduling", label: "Scheduling & CI", icon: "◷", tabs: [
      { id: "schedules", label: "Schedule" },
      { id: "buildTriggers", label: "Build Trigger" },
      { id: "webhooks", label: "Webhook" },
    ] },
    { id: "data", label: "Test Data", icon: "💾", tabs: [
      { id: "dataSnapshots", label: "DB Snapshot" },
      { id: "dataSeeds", label: "Seed & Cleanup" },
      { id: "dataProfiles", label: "Environment Data Profile" },
    ] },
    { id: "manage", label: "การจัดการ", icon: "⚙", tabs: [
      { id: "manage", label: "การจัดการ" },
    ] },
  ];
  const activeGroup = tabGroups.find((g) => g.tabs.some((t) => t.id === tab)) ?? tabGroups[0];

  return <article className="automation-page">
    {!pid ? <div className="empty"><p>เลือก Project เพื่อดู Automation Workspace</p></div> : <>
      <section className="automation-page-head">
        <div className="automation-page-actions">
          <div className="automation-search"><input type="text" aria-label="ค้นหา Automation Case" placeholder="ค้นหา Automation Case..." value={headSearch} onChange={(e) => setHeadSearch(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter") setTab("cases"); }} /></div>
          <button className="btn" type="button" title="รีเฟรชข้อมูล" aria-label="รีเฟรชข้อมูล" onClick={() => setReload((v) => v + 1)}><span className="material-symbols-outlined" aria-hidden="true">refresh</span> <span className="automation-hide-mobile">รีเฟรช</span></button>
          <button className="btn" type="button" disabled={!cases.length} onClick={exportCases}>↥ Export</button>
          {canEdit && <button className="btn primary" type="button" onClick={openCreate}><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Automation Case</button>}
        </div>
      </section>
      <nav className="automation-tabs" aria-label="Automation Module"><div className="automation-tabs-inner">{tabGroups.map((g) => <button key={g.id} type="button" className={activeGroup.id === g.id ? "active" : ""} aria-current={activeGroup.id === g.id ? "page" : undefined} onClick={() => setTab(g.tabs[0].id)}><span aria-hidden="true">{g.icon}</span>{g.label}</button>)}</div></nav>
      {activeGroup.tabs.length > 1 && <nav className="automation-subtabs" aria-label={activeGroup.label}>{activeGroup.tabs.map((t) => <button key={t.id} type="button" className={tab === t.id ? "active" : ""} aria-current={tab === t.id ? "page" : undefined} onClick={() => setTab(t.id)}>{t.label}</button>)}</nav>}
      {error && <div className="inline-alert error" role="alert"><span>{error}</span></div>}
      {notice && <div className="inline-alert success"><span>{notice}</span></div>}

      {tab === "dashboard" && <section className="automation-dashboard" aria-label="Automation Dashboard">
        <div className="automation-metrics" aria-label="Automation KPI">
          {metrics.map((m) => <button key={m.label} type="button" className="automation-metric" aria-label={`${m.label}: ${m.value}`} onClick={() => m.go()}>
            <span className={`automation-metric-ico m-${m.tone}`} aria-hidden="true">{m.icon}</span>
            <span className="automation-metric-body"><span className="automation-metric-label">{m.label}</span><strong>{m.value}</strong><span className="automation-metric-note">{m.note}</span></span>
            <span className="automation-metric-go" aria-hidden="true">›</span>
          </button>)}
        </div>

        <section className="automation-flow-card" aria-label="ขั้นตอนการทำงาน Automation">
          <div className="automation-flow">
            {workflowSteps.map((s, i) => <Fragment key={s.t}>
              <button type="button" className={"automation-flow-step" + (s.done ? " done" : i === activeWorkflowStep ? " active" : "")} aria-current={!s.done && i === activeWorkflowStep ? "step" : undefined} onClick={() => setTab(s.tab)}>
                <span className="automation-flow-icon" aria-hidden="true">{s.icon}{s.done && <span className="automation-flow-badge">✓</span>}</span>
                <span className="automation-flow-text"><strong>{s.t}</strong><small>{s.d}</small></span>
              </button>
              {i < workflowSteps.length - 1 && <span className="automation-flow-arrow" aria-hidden="true">→</span>}
            </Fragment>)}
          </div>
        </section>

        <div className="automation-two-col">
          <section className="automation-panel">
            <div className="automation-panel-head"><h2>สิ่งที่ต้องดำเนินการ</h2></div>
            {nextActions.length ? <div className="automation-task-list">{nextActions.map((a, i) => { const [title, desc] = splitTaskText(a.text); return <button key={i} type="button" className="automation-task" onClick={() => setTab(a.tab)}><span className={`automation-task-icon ${taskClass(a.text, a.tab)}`} aria-hidden="true">{taskIcon(a.tab)}</span><span className="automation-task-body"><strong>{title}</strong>{desc && <p>{desc}</p>}</span><span className="automation-task-go" aria-hidden="true">›</span></button>; })}</div> : <div className="empty"><p>ไม่มีรายการที่ต้องดำเนินการ</p><small>ทุกอย่างพร้อม — สร้างและรัน Automation Case ได้เลย</small></div>}
          </section>
          <section className="automation-panel">
            <div className="automation-panel-head"><h2>Agent Status</h2>{agents.length > 0 && <button className="automation-panel-link" onClick={() => setTab("manage")}><span aria-hidden="true">»</span> ดูทั้งหมด</button>}</div>
            {primaryAgent ? <div className="automation-agent-card">
              <div>
                <div className="automation-agent-status"><span className="automation-online-dot" />{primaryAgent.agentCode}<span className="automation-primary-tag">Primary</span></div>
                <div className="automation-agent-meta">
                  <div className="k">PC Name</div><div>{primaryAgent.machineName}</div>
                  <div className="k">OS</div><div>{primaryAgent.operatingSystem}</div>
                  <div className="k">Agent Version</div><div>{primaryAgent.agentVersion}</div>
                  <div className="k">Last Heartbeat</div><div>{formatThaiDateTime(primaryAgent.lastHeartbeatAt)}</div>
                  <div className="k">Running Jobs</div><div>{primaryAgent.currentExecutionId ? 1 : 0}</div>
                </div>
              </div>
              <div className="automation-health">
                <div><div className="automation-ring" style={{ "--health": `${healthPct}%` } as React.CSSProperties}><strong>{healthPct}%</strong></div><span>Health</span></div>
              </div>
            </div> : <div className="empty"><p>ยังไม่มี Agent ลงทะเบียน</p><small>รัน <code>agent\\run-agent.ps1</code> บนเครื่อง Windows เพื่อเริ่ม Agent</small></div>}
          </section>
        </div>

        <section className="automation-panel automation-result-panel">
          <div className="automation-panel-head"><h2>ผลการรันล่าสุด</h2><button className="automation-panel-link" onClick={() => setTab("execution")}><span aria-hidden="true">»</span> ดูทั้งหมด</button></div>
          {executions.length ? <>
            <div className="automation-table-wrap">
              <table className="automation-recent-table">
                <thead><tr><th>Automation Case</th><th>Linked Test Case</th><th>Result</th><th>Agent</th><th>Execution Time</th><th>Duration</th><th></th></tr></thead>
                <tbody>{executions.slice(0, 5).map((x) => { const c = caseByExecId.get(x.automationCaseId); const tcCode = x.testCaseCode ?? c?.testCaseCode; const tcTitle = x.testCaseTitle ?? c?.testCaseTitle; return <tr key={x.automationExecutionId}>
                  <td><span className="automation-case-code">{x.automationCode}</span>{tcTitle && <span className="automation-subline">{tcTitle}</span>}</td>
                  <td>{tcCode ? <><strong>{tcCode}</strong>{tcTitle && <span className="automation-subline">{tcTitle}</span>}</> : <span className="automation-subline">-</span>}</td>
                  <td><Badge tone={executionStatusTone[x.status] ?? "blue"}>{x.status}</Badge></td>
                  <td>{x.agentCode ?? "-"}</td>
                  <td>{formatThaiDateTime(x.completedAt ?? x.startedAt)}</td>
                  <td>{formatDuration(x.durationMs)}</td>
                  <td><div className="automation-row-actions">
                    <button type="button" className="automation-more" title="ดูรายละเอียด / Evidence / Defect" aria-label={`ดูรายละเอียด ${x.automationCode}`} onClick={() => setExecDetail(x)}>⋮</button>
                    {canRun && x.status !== "Running" && x.status !== "Queued" && <button type="button" className="automation-more is-run" title="รันซ้ำ" aria-label={`รันซ้ำ ${x.automationCode}`} onClick={() => rerunExecution(x)}>▶</button>}
                    {canRun && (x.status === "Running" || x.status === "Queued") && <button type="button" className="automation-more is-danger" title="ยกเลิก" aria-label={`ยกเลิก ${x.automationCode}`} onClick={() => cancelExecution(x)}>✕</button>}
                  </div></td>
                </tr>; })}</tbody>
              </table>
            </div>
            {executions.length > 5 && <div className="automation-table-footer"><button type="button" onClick={() => setTab("execution")}>ดูผลการรันทั้งหมด ›</button></div>}
          </> : <div className="empty"><p>ยังไม่มีประวัติการรัน</p><small>สร้าง Automation Case แล้วรันผ่าน Agent — ผลจะแสดงที่นี่</small>{canEdit && <button className="btn primary" onClick={openCreate}><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Automation Case</button>}</div>}
        </section>
      </section>}

      {tab === "cases" && <section className="automation-cases" aria-label="Automation Cases">
        <header className="automation-section-head"><div><h2>Automation Cases</h2><p>หนึ่ง Test Case → หนึ่ง Automation Case พร้อม Version (DSL) หลายเวอร์ชัน</p></div><div className="automation-cases-actions">{canRun && <button className="btn" onClick={() => setBatchModal(true)}><span className="material-symbols-outlined" aria-hidden="true">play_arrow</span> รันเป็นกลุ่ม</button>}{canEdit && <button className="btn primary" onClick={openCreate}><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Automation Case</button>}</div></header>
        {flakyCandidates.length > 0 && <section className="automation-failure-analysis" aria-label="Flaky Candidates">
          <div className="automation-section-head"><h3>Flaky Candidates (AUT-P0-010)</h3><span className="muted-text">Pass/Fail สลับกันบ่อยใน execution ล่าสุด</span></div>
          <div className="automation-result-list">{flakyCandidates.map((f) => <div key={f.automationCaseId} className="automation-failure-row">
            <b>{f.automationCode}</b><span>{f.transitions} transitions / {f.recentRuns} runs</span><span>ล่าสุด {formatThaiDateTime(f.lastExecutedAt)}</span>
            {canManage && <button type="button" className="table-action icon-only" title="Quarantine" aria-label={`Quarantine ${f.automationCode}`} onClick={() => setQuarantineModalFor(f)}><span className="material-symbols-outlined" aria-hidden="true">warning</span></button>}
          </div>)}</div>
        </section>}
        {cases.length ? <>
          <div className="filter-toolbar">
            <div className="filter-toolbar-top">
              <div className="result-count"><strong>{casesPaged.total.toLocaleString()}</strong><span>Automation Cases{(headSearch.trim() || caseStatusFilter !== "all" || caseTargetFilter !== "all") ? " ที่ตรงเงื่อนไข" : ""}</span></div>
              {(caseStatusFilter !== "all" || caseTargetFilter !== "all" || headSearch.trim()) && <button type="button" className="table-action" onClick={() => { setCaseStatusFilter("all"); setCaseTargetFilter("all"); setHeadSearch(""); }}><span className="material-symbols-outlined" aria-hidden="true">close</span> ล้างตัวกรอง</button>}
            </div>
            <div className="filter-toolbar-row automation-case-toolbar">
              <select aria-label="กรองสถานะ" value={caseStatusFilter} onChange={(e) => setCaseStatusFilter(e.target.value)}>
                <option value="all">ทุกสถานะ</option>
                {["Draft", "NeedsReview", "Validated", "Approved", "Ready", "Running", "MaintenanceRequired"].map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
              <select aria-label="กรอง Target App" value={caseTargetFilter} onChange={(e) => setCaseTargetFilter(e.target.value)}>
                <option value="all">ทุก Target App</option>
                <option value="Pos">Pos · PromaxxsPos.exe</option>
                <option value="App">App · Promaxxs.App.exe</option>
                <option value="WindowsUI">WindowsUI · generic</option>
              </select>
              <select aria-label="เรียงตาม" value={caseSortBy} onChange={(e) => setCaseSortBy(e.target.value)}>
                <option value="created">ล่าสุดก่อน</option>
                <option value="code">Code (A→Z)</option>
                <option value="status">สถานะ</option>
              </select>
            </div>
          </div>
          {canManage && selectedCaseIds.size > 0 && (
            <div className="testcase-bulk-bar" role="region" aria-label="จัดการ Automation Case ที่เลือก">
              <span className="bulk-count">{selectedCaseIds.size} เลือกแล้ว</span>
              <button type="button" className="btn danger" disabled={bulkDeleteBusy} onClick={hardDeleteSelectedCases}>{bulkDeleteBusy ? <><span className="spinner inline" aria-hidden="true" /> กำลังลบ...</> : <><span className="material-symbols-outlined" aria-hidden="true">close</span> ลบถาวร</>}</button>
              <button type="button" className="bulk-clear" disabled={bulkDeleteBusy} onClick={() => setSelectedCaseIds(new Set())}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิกเลือก</button>
            </div>
          )}
          {casesError && <div className="inline-alert error" role="alert"><span>{casesError}</span><button type="button" className="btn" onClick={() => setReload((v) => v + 1)}>ลองใหม่</button></div>}
          {casesPaged.rows.length ? <div className="table-wrap"><table><thead><tr>{canManage && <th className="case-select-col"><input type="checkbox" aria-label="เลือกทั้งหน้านี้" checked={allCasesOnPageSelected} onChange={toggleSelectAllCasesOnPage} /></th>}<th>Code</th><th>Test Case</th><th>Target App</th><th>Status</th><th>Version</th><th>Owner</th><th className="actions-col">จัดการ</th></tr></thead><tbody>{casesPaged.rows.map((c) => <tr key={c.automationCaseId} className={selectedCaseIds.has(c.automationCaseId) ? "is-selected" : ""}>{canManage && <td className="case-select-col" data-label="เลือก"><input type="checkbox" aria-label={`เลือก ${c.automationCode}`} checked={selectedCaseIds.has(c.automationCaseId)} onChange={() => toggleCaseSelected(c.automationCaseId)} /></td>}<td><b>{c.automationCode}</b></td><td><span>{c.testCaseCode}</span><small>{c.testCaseTitle}</small></td><td><Badge tone={targetTone[c.automationType] ?? "blue"}>{c.automationType}</Badge></td><td><Badge tone={caseStatusTone[c.status] ?? "blue"}>{c.status}</Badge>{c.isQuarantined && <Badge tone="orange">Quarantined</Badge>}</td><td>Rev {c.currentVersionNo}</td><td>{c.ownerName ?? "-"}</td><td className="actions-col"><button className="table-action icon-only" title="รายละเอียด" aria-label={`รายละเอียด ${c.automationCode}`} onClick={() => openCase(c)}><span className="material-symbols-outlined" aria-hidden="true">info</span></button></td></tr>)}</tbody></table></div>
            : <div className="empty"><p>ไม่พบ Automation Case ที่ตรงเงื่อนไข</p><small>ลองเปลี่ยนคำค้นหาหรือตัวกรองด้านบน</small></div>}
          {casesPaged.total > casePageSize && <Pager page={casePage} count={casePageCount} total={casesPaged.total} pageSize={casePageSize} onPrev={() => setCasePage((p) => Math.max(1, p - 1))} onNext={() => setCasePage((p) => Math.min(casePageCount, p + 1))} />}
        </> : <div className="empty"><p>ยังไม่มี Automation Case</p><small>สร้างจาก Test Case ที่เป็น Automation Candidate — จากนั้นเขียน DSL / Generate AI → Validate → อนุมัติ → พร้อมรัน</small>{canEdit && <button className="btn primary" onClick={openCreate}><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Automation Case</button>}</div>}
      <div className="automation-status-legend" role="note" aria-label="ความหมายสถานะ"><span><i className="legend-dot legend-draft" />Draft — ยังไม่มี DSL</span><span><i className="legend-dot legend-review" />NeedsReview — AI สร้างแล้ว รอตรวจ</span><span><i className="legend-dot legend-ready" />Ready — พร้อมรัน</span><span><i className="legend-dot legend-maint" />MaintenanceRequired — ต้องซ่อม DSL/Object</span></div>
      </section>}

      {tab === "suites" && <AutomationSuiteTab projectId={pid} releaseId={releaseId} headers={headers} canEdit={canEdit} canRun={canRun} cases={cases} />}
      {tab === "schedules" && <AutomationScheduleTab projectId={pid} releaseId={releaseId} headers={headers} canEdit={canEdit} agents={agents} setExecDetail={setExecDetail} />}
      {tab === "buildTriggers" && <AutomationBuildTriggerTab projectId={pid} headers={headers} canEdit={canEdit} agents={agents} />}
      {tab === "webhooks" && <AutomationWebhookTab projectId={pid} headers={headers} canEdit={canEdit} />}
      {tab === "dataSnapshots" && <AutomationDataSnapshotTab projectId={pid} releaseId={releaseId} headers={headers} canRun={canRun} />}
      {tab === "dataSeeds" && <AutomationDataSeedTab projectId={pid} releaseId={releaseId} headers={headers} canEdit={canEdit} canRun={canRun} />}
      {tab === "dataProfiles" && <AutomationEnvironmentDataProfileTab projectId={pid} headers={headers} canEdit={canEdit} />}

      {tab === "manage" && <section className="automation-manage" aria-label="Automation จัดการ">
        <nav className="automation-subtabs" aria-label="จัดการ"><button type="button" className={manageTab === "actions" ? "active" : ""} onClick={() => setManageTab("actions")}>Action Library</button><button type="button" className={manageTab === "objects" ? "active" : ""} onClick={() => setManageTab("objects")}>Object Repository</button><button type="button" className={manageTab === "agents" ? "active" : ""} onClick={() => setManageTab("agents")}>Agents</button><button type="button" className={manageTab === "retry" ? "active" : ""} onClick={() => setManageTab("retry")}>Retry Policy</button></nav>
        {manageTab === "actions" && <ActionLibraryTab actions={actions} canManage={canManage} headers={headers} onReload={() => setReload((x) => x + 1)} onError={setError} actionModal={actionModal} setActionModal={setActionModal} />}
        {manageTab === "objects" && <ObjectRepositoryTab projectId={pid} objects={objects} canManage={canManage} headers={headers} onReload={() => setReload((x) => x + 1)} onError={setError} objectModal={objectModal} setObjectModal={setObjectModal} />}
        {manageTab === "agents" && <AgentsSection agents={agents} agentsOnline={agentsOnline} canManage={canManage} headers={headers} onToggle={toggleAgent} onDelete={deleteAgent} />}
        {manageTab === "retry" && <RetryPolicyTab policy={retryPolicy} canManage={canManage} busy={maintenanceBusy} onSave={updateRetryPolicy} />}
      </section>}

      {tab === "execution" && <ExecutionTab projectId={pid} buildId={buildId} releaseId={releaseId} agents={agents} headers={headers} jobs={jobs} executions={executions} setExecDetail={setExecDetail} execFilter={execFilter} setExecFilter={setExecFilter} canRun={canRun} onCancel={cancelExecution} onRerun={rerunExecution} reload={reload} />}

      {tab === "failures" && <FailureDashboardTab projectId={pid} releaseId={releaseId} agents={agents} headers={headers} setExecDetail={setExecDetail} />}
    </>}

    {createModal && <ModalShell labelledBy="automation-create-title" className="automation-create-modal" onDismiss={() => { if (!createBusy && wizardStep !== 4) setCreateModal(false); }} form>
      <div className="modal-head"><div className="acw-head"><span className="acw-head-icon" aria-hidden="true">⚙</span><div><h2 id="automation-create-title">สร้าง Automation Case</h2><small>Wizard สำหรับเลือก Test Case ตรวจสอบรายละเอียด สร้าง DSL และบันทึก Automation Case</small></div></div><button aria-label="ปิด" disabled={createBusy} onClick={() => setCreateModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      {createLoadError && <div className="inline-alert error" role="alert"><span>{createLoadError}</span></div>}

      <div className="acw-stepper" aria-label="ขั้นตอนการสร้าง Automation Case">
        {["เลือก Test Case", "ตรวจสอบรายละเอียด", "สร้าง Automation Case", "เสร็จสิ้น"].map((label, i) => {
          const n = i + 1;
          return <div key={n} className={"acw-step" + (n === wizardStep ? " active" : n < wizardStep ? " done" : "")}><span className="acw-num" aria-hidden="true">{n < wizardStep ? "✓" : n}</span>{label}</div>;
        })}
      </div>

      <div className="acw-body">
        {wizardStep === 1 && <div className="acw-grid">
          <section className="acw-left">
            <div className="acw-section-head"><div><h2>เลือก Test Case</h2><p>ค้นหาและเลือก Test Case ที่ต้องการสร้าง Automation Case</p></div><button type="button" className="btn" title="รีเฟรชรายการ" aria-label="รีเฟรชรายการ" disabled={createBusy} onClick={openCreate}>↻</button></div>
            <div className="acw-filters">
              {createModules.length > 0 && <select aria-label="กรอง Module" value={createModuleFilter} onChange={(e) => setCreateModuleFilter(e.target.value)}><option value="">ทุก Module</option>{moduleTreeOptions(createModules)}</select>}
              <input type="text" aria-label="ค้นหา Test Case" placeholder="ค้นหา Code / ชื่อ..." value={createSearch} onChange={(e) => setCreateSearch(e.target.value)} />
              <select aria-label="กรอง Priority" value={wizardPriority} onChange={(e) => setWizardPriority(e.target.value)}><option value="">ทุก Priority</option><option value="P0">P0</option><option value="P1">P1</option><option value="P2">P2</option><option value="P3">P3</option></select>
            </div>
            {wizardList.length ? <>
              <div className="acw-table-wrap">
                <table className="acw-table">
                  <colgroup><col style={{ width: 42 }} /><col style={{ width: 185 }} /><col /><col style={{ width: 85 }} /><col style={{ width: 115 }} /><col style={{ width: 90 }} /></colgroup>
                  <thead><tr><th aria-label="เลือก"></th><th>Test Case</th><th>ชื่อ Test Case</th><th>Priority</th><th>สถานะ</th><th>Automation</th></tr></thead>
                  <tbody>{wizardPaged.map((c) => { const taken = existingTestCaseIds.has(c.testCaseId); const sel = createPick?.testCaseId === c.testCaseId; return <tr key={c.testCaseId} className={(sel ? " is-selected" : "") + (taken ? " is-taken" : "")} onClick={() => { if (!taken && !createBusy) pickCandidate(c); }} aria-disabled={taken} title={taken ? "Test Case นี้มี Automation Case แล้ว" : undefined}>
                    <td><span className="acw-radio" aria-hidden="true" /></td>
                    <td><span className="acw-code">{c.testCaseCode}</span></td>
                    <td><span className="acw-ellipsis">{c.title}</span></td>
                    <td><span className={`badge acw-badge ${c.priority === "P0" ? "p0" : "p1"}`}>{c.priority}</span></td>
                    <td>{taken ? <span className="badge has">มี Case แล้ว</span> : <span className="badge none">ยังไม่มี Case</span>}</td>
                    <td>{c.automationCandidate ? <span className="acw-cand">✓ พร้อม</span> : <span className="acw-muted">—</span>}</td>
                  </tr>; })}
                  </tbody>
                </table>
              </div>
              <div className="acw-table-footer">
                <span>แสดง {wizardList.length ? (wizardPage - 1) * wizardPageSize + 1 : 0}–{Math.min(wizardPage * wizardPageSize, wizardList.length)} จาก {wizardList.length} รายการ</span>
                <div className="acw-pages">
                  <button type="button" className="acw-page-btn" disabled={wizardPage <= 1} onClick={() => setWizardPage((p) => Math.max(1, p - 1))} aria-label="หน้าก่อนหน้า">‹</button>
                  {wizardPageCount > 1 && Array.from({ length: wizardPageCount }, (_, i) => i + 1).map((n) => <button key={n} type="button" className={"acw-page-btn" + (n === wizardPage ? " on" : "")} onClick={() => setWizardPage(n)}>{n}</button>)}
                  <button type="button" className="acw-page-btn" disabled={wizardPage >= wizardPageCount} onClick={() => setWizardPage((p) => Math.min(wizardPageCount, p + 1))} aria-label="หน้าถัดไป">›</button>
                </div>
              </div>
            </> : <div className="acw-empty"><div><strong>ไม่พบ Test Case ที่ตรงเงื่อนไข</strong>เปิดหน้า Test Case และทำเครื่องหมาย Automation Candidate ก่อน</div></div>}
          </section>
          <aside className="acw-right">
            <div className="acw-section-head"><div><h2>รายละเอียด Test Case</h2><p>ตรวจสอบข้อมูลก่อนสร้าง Automation Case</p></div></div>
            {!createPick || !createDetail ? <div className="acw-card"><div className="acw-empty"><div><strong>ยังไม่ได้เลือก Test Case</strong>คลิก Test Case ทางซ้ายเพื่อเริ่ม</div></div></div>
              : <div className="acw-card">
                  <div className="acw-detail-top"><div><span className="acw-detail-code">{createPick.testCaseCode}</span><div className="acw-detail-title">{createPick.title}</div></div><span className={`badge acw-badge ${createPick.priority === "P0" ? "p0" : "p1"}`}>{createPick.priority}</span></div>
                  <div className="acw-meta">
                    <div className="key">Module</div><div>{modName ?? "—"}</div>
                    <div className="key">Test Type</div><div>{createDetail.testType ?? "—"}</div>
                    <div className="key">สถานะ</div><div>{hasAutomation ? "มี Automation Case แล้ว" : "ยังไม่มี Automation Case"}</div>
                    <div className="key">Automation Candidate</div><div>{createDetail.automationCandidate ? "พร้อมใช้งาน Automation" : "แนะนำให้ทำ Automation"}</div>
                  </div>
                  <div className="acw-divider" />
                  <div className="acw-text-block">
                    <h3>Objective</h3><p>{createDetail.objective || "—"}</p>
                    <h3>Preconditions</h3>{preconditionsList.length ? <ul>{preconditionsList.map((p, i) => <li key={i}>{p}</li>)}</ul> : <p>—</p>}
                    <h3>Expected Result (ย่อ)</h3><p>{lastStepExpected || "—"}</p>
                  </div>
                </div>}
            <div className="acw-hint"><h3>เมื่อสร้าง Automation Case</h3><ul><li>ระบบจะสร้าง Automation Case และ DSL เบื้องต้นให้</li><li>สามารถแก้ไข DSL ก่อน Run ได้</li><li>ต้องตรวจสอบและอนุมัติก่อนใช้งานจริง</li></ul></div>
          </aside>
        </div>}

        {wizardStep === 2 && <div className="acw-review">
          <div className="acw-review-col">
            <div className="acw-card">
              <div className="acw-section-head"><div><h2>ตรวจสอบรายละเอียด Test Case</h2><p>ข้อมูลจาก Test Management ที่ AI จะนำไปใช้ Generate Automation</p></div><span className="badge ai">AI Input</span></div>
              <div className="acw-meta">
                <div className="key">Test Case</div><div><strong>{createPick?.testCaseCode}</strong></div>
                <div className="key">ชื่อ</div><div>{createPick?.title}</div>
                <div className="key">Module</div><div>{modName ?? "—"}</div>
                <div className="key">Priority</div><div>{createPick?.priority}</div>
                <div className="key">Test Type</div><div>{createDetail?.testType ?? "—"}</div>
                <div className="key">สถานะ</div><div>{createDetail?.status ?? "—"}</div>
              </div>
              <div className="acw-divider" />
              <div className="acw-text-block">
                <h3>Objective</h3><p>{createDetail?.objective || "—"}</p>
                <h3>Preconditions</h3>{preconditionsList.length ? <ul>{preconditionsList.map((p, i) => <li key={i}>{p}</li>)}</ul> : <p>—</p>}
              </div>
            </div>
            <div className="acw-card">
              <div className="acw-section-head"><div><h2>Test Steps</h2><p>ขั้นตอนที่ระบบจะส่งให้ AI เพื่อแปลงเป็น Automation DSL</p></div><span className="badge ready">{createPickSteps.length} Steps</span></div>
              {createPickSteps.length ? <div className="acw-step-list">{createPickSteps.map((s) => <div key={s.stepNo} className="acw-step-item"><span className="acw-step-no">{s.stepNo}</span><div><strong>{s.action}</strong>{s.testData ? <div className="acw-desc">ข้อมูล: {s.testData}</div> : null}<div className="acw-desc">Expected: {s.expectedResult}</div></div></div>)}</div> : <div className="acw-empty"><div><strong>ไม่มี Test Steps</strong>เปิดหน้า Test Case เพื่อเพิ่มขั้นตอนก่อนสร้าง Automation</div></div>}
            </div>
          </div>
          <div className="acw-review-col">
            <div className="acw-card">
              <div className="acw-section-head"><div><h2>Automation Readiness</h2><p>ตรวจสอบความพร้อมก่อน Generate</p></div></div>
              <div className="acw-check-list">
                {readyChecks.map((ch, i) => <div key={i} className="acw-check-row"><span className={ch.ok ? "ok" : "warn"} aria-hidden="true">{ch.ok ? "✓" : "!"}</span>{ch.text}</div>)}
                <div className="acw-check-row"><span className="warn" aria-hidden="true">!</span>Object Repository จะตรวจสอบหลัง Generate DSL</div>
              </div>
            </div>
            <div className="acw-card">
              <div className="acw-section-head"><div><h2>ตั้งค่าการสร้าง Automation</h2><p>กำหนดค่าที่ใช้สำหรับ Automation Case ใหม่</p></div></div>
              <div className="acw-form-grid">
                <label className="field full">Automation Type
                  <select value={wizardType} onChange={(e) => setWizardType(e.target.value)}><option value="WindowsUI">WindowsUI · generic</option><option value="Pos">Pos · PromaxxsPos.exe</option><option value="App">App · Promaxxs.App.exe</option></select>
                  <span className="help">Target Application ที่ Agent จะใช้รัน</span>
                </label>
                <label className="field full">หมายเหตุสำหรับ AI
                  <textarea rows={3} value={wizardNote} onChange={(e) => setWizardNote(e.target.value)} placeholder="เช่น ให้ตรวจข้อความแจ้งเตือนและตรวจสอบข้อมูลในฐานข้อมูลหลังบันทึก" />
                  <span className="help">ใช้เป็น changeReason ของ Version แรก</span>
                </label>
              </div>
            </div>
          </div>
        </div>}

        {wizardStep === 3 && <div className="acw-builder">
          {newVersionError && <div className="inline-alert error" role="alert"><span>{newVersionError}</span></div>}
          <div className="acw-card">
            <div className="acw-section-head"><div><h2>Automation Case</h2><p>ตรวจสอบข้อมูล Automation ก่อนอนุมัติ</p></div><span className="badge ai">{aiConf != null ? "AI Generated" : "Manual DSL"}</span></div>
            <div className="acw-form-grid">
              <label className="field full">Automation Code<input type="text" value={createdCode} readOnly /></label>
              <label className="field full">Automation Name<input type="text" value={`Automation - ${createPick?.title ?? ""}`} readOnly /></label>
              <label className="field">Linked Test Case<input type="text" value={createPick?.testCaseCode ?? ""} readOnly /></label>
              <label className="field">Version<input type="text" value="1.0" readOnly /></label>
              <label className="field">Status<span className="acw-status"><Badge tone={caseStatusTone[createdStatus] ?? "blue"}>{createdStatus}</Badge></span></label>
              <label className="field">Agent Target<span className="acw-status">{wizardType} · Windows Agent</span></label>
            </div>
            <div className="acw-divider" />
            <div className="acw-summary-box">
              <div className="acw-summary-item"><div className="n">{dslActions}</div><div className="l">Actions</div></div>
              <div className="acw-summary-item"><div className="n">{dslAssertions}</div><div className="l">Assertions</div></div>
              <div className="acw-summary-item"><div className="n">{aiConf != null ? `${Math.round(aiConf * 100)}%` : "—"}</div><div className="l">AI Confidence</div></div>
              <div className="acw-summary-item"><div className="n">{valErrCount}</div><div className="l">Validation Error</div></div>
            </div>
            <div className="acw-action-bar">
              {validatedOk ? <><span className="chip">✓ Action Library</span><span className="chip">✓ Parameter Schema</span><span className="chip">✓ Object Mapping</span><span className="chip">✓ Test Data</span></> : <span className="chip">! ยังไม่ผ่าน Validation</span>}
            </div>
          </div>
          <div className="acw-card">
            <div className="acw-section-head"><div><h2>Automation DSL</h2><p>ภาษากลางที่ Agent จะนำไป Execute กับ ProMaxx2 Windows</p></div>{canGenerateAi && <button type="button" className="btn" title={`เรียก AI Provider จริง อาจใช้เวลาถึง ${AI_GENERATE_TIMEOUT_MS / 1000} วินาที`} disabled={createBusy} onClick={generateAiForNewCase}>{createBusy ? <><span className="spinner inline" aria-hidden="true" /> AI กำลังสร้าง... (อาจถึง {AI_GENERATE_TIMEOUT_MS / 1000}s)</> : "↻ Generate AI"}</button>}</div>
            <textarea className="acw-dsl" rows={14} value={newDsl} onChange={(e) => setNewDsl(e.target.value)} spellCheck={false} aria-label="DSL JSON" />
            <div className="acw-action-bar">
              <button type="button" className="btn" disabled={createBusy} onClick={() => { setNewDsl(sampleDsl); setValErrors(""); setValidatedOk(false); }}><span className="material-symbols-outlined" aria-hidden="true">description</span> โหลดตัวอย่าง</button>
            </div>
            <div className="acw-note">{valErrors ? <span className="warn"><span className="material-symbols-outlined" aria-hidden="true">close</span> Validate Error: {valErrors}</span> : <span className="ok">✓ สถานะ: {validatedOk ? "Validate ผ่าน — พร้อมบันทึก" : "DSL พร้อมตรวจสอบ — กด 'บันทึก + Validate'"}</span>}</div>
          </div>
        </div>}

        {wizardStep === 4 && <div className="acw-success-screen">
          <div className="acw-success-card">
            <div className="acw-success-icon" aria-hidden="true">✓</div>
            <h2>สร้าง Automation Case สำเร็จ</h2>
            <p>Automation Case ถูกสร้างและผูกกับ Test Case เรียบร้อยแล้ว พร้อมนำไป Validate และ Run ผ่าน Windows Agent</p>
            <div className="acw-result-grid">
              <div className="acw-result-item"><div className="k">Automation Case</div><div className="v">{createdCode}</div></div>
              <div className="acw-result-item"><div className="k">Linked Test Case</div><div className="v">{createPick?.testCaseCode}</div></div>
              <div className="acw-result-item"><div className="k">Status</div><div className="v"><Badge tone={caseStatusTone[createdStatus] ?? "blue"}>{createdStatus}</Badge></div></div>
              <div className="acw-result-item"><div className="k">Execution Target</div><div className="v">{wizardType} · Windows Agent</div></div>
            </div>
            <div className="acw-action-bar acw-center">
              <button type="button" className="btn" onClick={openCreatedCase}><span className="material-symbols-outlined" aria-hidden="true">info</span> ดู Automation Case</button>
              <button type="button" className="btn" onClick={() => setCreateModal(false)}>ไปหน้า Automation <span className="material-symbols-outlined" aria-hidden="true">arrow_forward</span></button>
              <button type="button" className="btn acw-btn-success" onClick={resetWizard}><span className="material-symbols-outlined" aria-hidden="true">add</span> สร้าง Case เพิ่ม</button>
            </div>
          </div>
        </div>}
      </div>

      {wizardStep < 4 && <div className="modal-actions">
        <button className="btn" disabled={createBusy} onClick={() => setCreateModal(false)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button>
        {wizardStep > 1 && <button className="btn" disabled={createBusy} onClick={() => setWizardStep((s) => s - 1)}>‹ ย้อนกลับ</button>}
        {wizardStep === 1 && <button className="btn primary" disabled={createBusy || !createPick} onClick={() => setWizardStep(2)}>ถัดไป ›</button>}
        {wizardStep === 2 && <button className="btn primary" disabled={createBusy || !createPick} onClick={async () => { const r = await createCase(createPick?.testCaseId ?? "", wizardType); if (r) setWizardStep(3); }}>{createBusy ? "กำลังสร้าง..." : "สร้าง Automation ›"}</button>}
        {wizardStep === 3 && <button className="btn primary" disabled={createBusy || !newDsl.trim()} onClick={createNewVersionAndValidate}>{createBusy ? "กำลังบันทึก..." : "บันทึก + Validate ›"}</button>}
      </div>}
    </ModalShell>}

    {selectedCase && <ModalShell labelledBy="automation-case-detail-title" className="automation-case-detail" onDismiss={() => setSelectedCase(null)}>
      <div className="modal-head"><div><h2 id="automation-case-detail-title">{selectedCase.automationCode}</h2><small>{selectedCase.testCaseCode} · {selectedCase.testCaseTitle}</small></div><button aria-label="ปิด" onClick={() => setSelectedCase(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      <div className="automation-case-detail-hero"><Badge tone={caseStatusTone[selectedCase.status] ?? "blue"}>{selectedCase.status}</Badge><span>Target App: </span>{canEdit ? <select aria-label="Target App" value={selectedCase.automationType} disabled={createBusy} onChange={(e) => changeTarget(e.target.value)}><option value="Pos">Pos · PromaxxsPos.exe</option><option value="App">App · Promaxxs.App.exe</option><option value="WindowsUI">WindowsUI · generic</option></select> : <Badge tone={targetTone[selectedCase.automationType] ?? "blue"}>{selectedCase.automationType}</Badge>}<span>Rev {selectedCase.currentVersionNo}</span><span>AI Generated: {selectedCase.isAiGenerated ? "ใช่" : "ไม่"}</span></div>
      <p className="automation-case-hint">{selectedCase.status === "Draft" ? "ขั้นตอนถัดไป: เขียน DSL (หรือกด ✦ Generate AI) แล้ว Validate" : selectedCase.status === "NeedsReview" ? "ขั้นตอนถัดไป: ตรวจ DSL ที่ AI สร้าง → กด Validate → อนุมัติ" : selectedCase.status === "Validated" || selectedCase.status === "Approved" ? "ขั้นตอนถัดไป: กดอนุมัติ (ถ้ายัง) → Case จะเป็น Ready และสั่งรันได้" : selectedCase.status === "Ready" ? "พร้อมรัน — กด ▶ สั่งรัน หรือรันเป็นกลุ่มใน Regression Suites" : selectedCase.status === "MaintenanceRequired" ? "ต้องซ่อม: แก้ Object Repository / DSL → Validate ใหม่ → อนุมัติ" : "สร้าง Version แล้ว Validate/อนุมัติเพื่อให้พร้อมรัน"}</p>

      {selectedCase.status === "MaintenanceRequired" && <section className="automation-failure-analysis" aria-label="Maintenance Repair">
        <div className="automation-section-head"><h3>Maintenance Repair (AUT-P0-007)</h3></div>
        {selectedCase.maintenanceReason && <div className="inline-alert error" role="alert"><span>สาเหตุ: {selectedCase.maintenanceReason}</span></div>}
        <p className="muted-text">เปิดตั้งแต่ {formatThaiDateTime(selectedCase.maintenanceOpenedAt)}{selectedCase.maintenanceOwnerUserId ? ` · ผู้รับผิดชอบ: ${selectedCase.maintenanceOwnerUserId}` : " · ยังไม่ได้มอบหมายผู้รับผิดชอบ"}</p>
        {canEdit && <>
          <div className="form-grid">
            <label>User Id ผู้รับผิดชอบ<input type="text" value={maintenanceOwnerInput} onChange={(e) => setMaintenanceOwnerInput(e.target.value)} placeholder="ระบุ User Id" /></label>
          </div>
          <div className="automation-failure-actions">
            <button type="button" className="btn" disabled={maintenanceBusy || !maintenanceOwnerInput.trim()} onClick={assignMaintenanceOwner}><span className="material-symbols-outlined" aria-hidden="true">check</span> รับผิดชอบซ่อม</button>
          </div>
          <label className="full">บันทึกการแก้ไข<textarea rows={3} value={maintenanceNote} onChange={(e) => setMaintenanceNote(e.target.value)} placeholder="สาเหตุที่แท้จริงและสิ่งที่แก้ไขแล้ว เช่น อัปเดต Object Repository AutomationId ใหม่" /></label>
          <div className="automation-failure-actions">
            <button type="button" className="btn primary" disabled={maintenanceBusy} onClick={resolveMaintenance}>{maintenanceBusy ? "กำลังบันทึก..." : "แก้ไขแล้ว → กลับไป Review"}</button>
          </div>
        </>}
      </section>}

      {selectedCase.isQuarantined && <section className="automation-failure-analysis" aria-label="Quarantine">
        <div className="automation-section-head"><h3>Flaky Quarantine</h3><Badge tone="orange">Quarantined</Badge></div>
        <p className="muted-text">เหตุผล: {selectedCase.quarantineReason}{selectedCase.quarantineExpiresAt ? ` · หมดอายุ ${formatThaiDateTime(selectedCase.quarantineExpiresAt)}` : ""}</p>
        {canManage && <div className="automation-failure-actions"><button type="button" className="btn" disabled={maintenanceBusy} onClick={() => unquarantineCase(selectedCase.automationCaseId)}><span className="material-symbols-outlined" aria-hidden="true">check</span> Unquarantine</button></div>}
      </section>}

      <VersionEditor selectedCase={selectedCase} versions={versions} canEdit={canEdit} canValidate={canValidate} canApprove={canApprove} canRun={canRun} canGenerateAi={canGenerateAi} createBusy={createBusy} versionError={versionError} onCreate={createVersion} onValidate={validateVersion} onApprove={approveVersion} onRun={openRun} onGenerateAi={generateAi} />
      <div className="modal-actions"><button className="btn primary" onClick={() => setSelectedCase(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิด</button></div>
    </ModalShell>}

    {runModal && selectedCase && <RunModal item={selectedCase} versions={versions} releaseId={releaseId} agents={agents} busy={createBusy} onClose={() => setRunModal(false)} onRun={runCase} />}
    {batchModal && <BatchRunModal cases={cases} releaseId={releaseId} canRun={canRun} busy={createBusy} onClose={() => setBatchModal(false)} onRunBatch={runBatch} onError={setError} />}
    {quarantineModalFor && <QuarantineModal candidate={quarantineModalFor} busy={maintenanceBusy} onClose={() => setQuarantineModalFor(null)} onConfirm={quarantineCase} />}
    {execDetail && <ModalShell labelledBy="automation-exec-detail-title" className="automation-exec-detail" onDismiss={() => setExecDetail(null)}>
      <div className="modal-head"><div><h2 id="automation-exec-detail-title">{execDetail.automationCode} · Execution</h2><small>Build {execDetail.buildNumber} · {execDetail.environmentName}{execDetail.agentCode ? ` · ${execDetail.agentCode}` : ""}</small></div><button aria-label="ปิด" onClick={() => setExecDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span></button></div>
      <div className="automation-run-detail-summary">
        <Badge tone={executionStatusTone[execDetail.status] ?? "blue"}>{execDetail.status}</Badge>
        <span>เริ่ม {formatThaiDateTime(execDetail.startedAt)}</span>
        <span>จบ {formatThaiDateTime(execDetail.completedAt)}</span>
        {execDetail.durationMs != null && <span>{(execDetail.durationMs / 1000).toFixed(2)} วิ</span>}
        {execDetail.errorCode && <Badge tone="red">{execDetail.errorCode}</Badge>}
        {execDetail.retryOfExecutionId && <Badge tone="orange">Auto-Retry #{execDetail.retryCount}</Badge>}
        {canRun && (execDetail.status === "Running" || execDetail.status === "Queued") && <button type="button" className="btn danger automation-detail-action" onClick={() => cancelExecution(execDetail)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ยกเลิก</button>}
        {canRun && execDetail.status !== "Running" && execDetail.status !== "Queued" && <button type="button" className="btn automation-detail-action" onClick={() => rerunExecution(execDetail)}><span className="material-symbols-outlined" aria-hidden="true">play_arrow</span> รันซ้ำ</button>}
      </div>
      {execDetail.errorMessage && <div className="inline-alert error" role="alert"><span>{execDetail.errorMessage}</span></div>}
      {execDetail.testExecutionId && <p className="muted-text">สร้าง TestExecution (ExecutionType = Automation) แล้ว</p>}
      {execDetail.status === "Failed" && <section className="automation-failure-analysis">
        <div className="automation-section-head"><h3>Failure Analysis (G9)</h3><div className="automation-failure-actions">
          <button className="btn" disabled={classifyBusy !== ""} onClick={runClassify}>{classifyBusy === "classify" ? "กำลังจำแนก..." : "จำแนก Fail"}</button>
          {canGenerateAi && <button className="btn" disabled={classifyBusy !== ""} onClick={runAnalyze}>{classifyBusy === "analyze" ? "AI กำลังวิเคราะห์..." : "วิเคราะห์ด้วย AI"}</button>}
          {canCreateDefect && !execDetail.defectId && !defectResult && <button className="btn danger" disabled={classifyBusy !== ""} onClick={runCreateDefect}>{classifyBusy === "defect" ? "กำลังสร้าง..." : "สร้าง Defect"}</button>}
        </div></div>
        {execDetail.classifiedFailureType && !classification && <div className="automation-failure-row"><Badge tone={failureTone[execDetail.classifiedFailureType] ?? "blue"}>{execDetail.classifiedFailureType}</Badge><span>จำแนกอัตโนมัติตอน Complete</span><span>แนะนำ: {execDetail.classifiedRecommendation}</span></div>}
        {classification && <div className="automation-failure-row"><Badge tone={failureTone[classification.failureType] ?? "blue"}>{classification.failureType}</Badge><span>Product Defect Candidate: {classification.isProductDefectCandidate ? "ใช่" : "ไม่ใช่"}</span><span>แนะนำ: {classification.recommendation}</span>{classification.detail && <small>{classification.detail}</small>}</div>}
        {aiAnalysis && <div className="automation-failure-row"><Badge tone={failureTone[aiAnalysis.classification] ?? "blue"}>{aiAnalysis.classification}</Badge><span>AI Confidence {(aiAnalysis.confidence * 100).toFixed(0)}%</span><span>แนะนำ: {aiAnalysis.recommendation}</span><small>{aiAnalysis.summary}</small></div>}
        {defectResult && <div className="inline-alert success"><span>สร้าง Defect แล้ว: <b>{defectResult}</b> — เปิดหน้า Defect เพื่อดูรายละเอียด</span></div>}
        {execDetail.defectId && !defectResult && <p className="muted-text">สร้าง Defect แล้ว (Execution เชื่อมกับ Defect แล้ว)</p>}
      </section>}
      <div className="automation-result-list">{execDetail.stepResults.length ? execDetail.stepResults.map((s) => <article key={s.automationStepResultId} className="automation-result-card">
        <div><b>Step {s.stepNo} · {s.actionCode}</b><Badge tone={s.status === "Pass" ? "green" : s.status === "Fail" ? "red" : "yellow"}>{s.status}</Badge></div>
        <span>{(s.durationMs / 1000).toFixed(2)} วิ</span>
        {s.actualResult && <p>{s.actualResult}</p>}
        {s.errorMessage && <p className="queue-error">{s.errorMessage}</p>}
        <footer>{s.evidencePath && canViewEvidence && <button className="table-action" disabled={evidenceBusy === s.automationStepResultId} onClick={() => openEvidence(s)}>{evidenceBusy === s.automationStepResultId ? "กำลังเปิด..." : "เปิด Evidence"}</button>}</footer>
      </article>) : <div className="empty"><p>ยังไม่มี Step Result</p></div>}</div>
      {execDetail.evidence?.length ? <section className="automation-evidence-list">
        <h3>Evidence ({execDetail.evidence.length})</h3>
        {execDetail.evidence.map((ev) => <article key={ev.automationEvidenceId} className="automation-result-card">
          <div><b>{ev.evidenceType}{ev.stepNo ? ` · Step ${ev.stepNo}` : ""}</b><Badge tone={evidenceTone[ev.evidenceType] ?? "blue"}>{ev.evidenceType}</Badge></div>
          <span>{ev.filePath.split("/").pop()}</span>
          <footer>{canViewEvidence && <button className="table-action" disabled={evidenceBusy === ev.automationEvidenceId} onClick={() => openEvidenceFile(ev)}>{evidenceBusy === ev.automationEvidenceId ? "กำลังเปิด..." : "เปิดไฟล์"}</button>}</footer>
        </article>)}
      </section> : <section className="automation-evidence-list"><h3>Evidence</h3><p className="muted-text">ยังไม่มี Evidence สำหรับ Execution นี้</p></section>}
      <div className="modal-actions"><button className="btn primary" onClick={() => setExecDetail(null)}><span className="material-symbols-outlined" aria-hidden="true">close</span> ปิด</button></div>
    </ModalShell>}
  </article>;
}
