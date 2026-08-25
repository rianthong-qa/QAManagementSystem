import { Fragment, useEffect, useMemo, useState } from "react";
import { formatThaiDateTime } from "./dateTime";
import {
  automationCaseTone as caseStatusTone,
  automationExecutionTone as executionStatusTone,
  automationJobTone as jobStatusTone,
  automationVersionTone as versionStatusTone,
  automationCoverage,
  parseDslSteps,
  buildObjectKey,
} from "./automationUtils";

const apiUrl = import.meta.env.VITE_API_URL ?? "/api/v1";
const token = () => localStorage.getItem("qa.accessToken");

type AutomationCaseItem = {
  automationCaseId: string; testCaseId: string; testCaseCode: string; testCaseTitle: string; automationCode: string;
  automationType: string; status: string; currentVersionNo: number; versionCount: number; ownerUserId?: string; ownerName?: string; isAiGenerated: boolean; createdAt: string;
};
type AutomationVersionItem = {
  automationVersionId: string; automationCaseId: string; versionNo: number; testCaseRevisionNo: number; dslVersion: string; dslJson: string;
  generatedByAi: boolean; aiProvider?: string; aiModel?: string; aiConfidence?: number; validationStatus: string; validationErrors?: string;
  approvedBy?: string; approvedAt?: string; changeReason?: string; createdAt: string;
};
type AutomationActionItem = {
  automationActionId: string; actionCode: string; actionName: string; category: string; description?: string; parameterSchemaJson: string;
  handlerKey: string; minimumAgentVersion?: string; isActive: boolean;
};
type AutomationObjectItem = {
  automationObjectId: string; projectId: string; moduleId?: string; moduleCode?: string; moduleName?: string; applicationCode: string;
  screenCode: string; objectCode: string; objectName: string; controlType: string; automationId?: string; selectorJson: string; objectVersion: number; isActive: boolean;
};
type AutomationAgentItem = {
  agentId: string; agentCode: string; machineName: string; agentVersion: string; operatingSystem: string; architecture: string; status: string;
  lastHeartbeatAt: string; currentExecutionId?: string; registeredAt: string; isEnabled: boolean; connectivity: string; capabilities: string[];
};
type AutomationJobItem = {
  jobId: string; automationExecutionId: string; priority: number; requestedAgentId?: string; assignedAgentId?: string; assignedAgentCode?: string;
  status: string; queuedAt: string; assignedAt?: string; startedAt?: string; completedAt?: string; retryCount: number; lastError?: string;
};
type AutomationStepResultItem = {
  automationStepResultId: string; stepNo: number; actionCode: string; status: string; startedAt: string; completedAt: string; durationMs: number;
  actualResult?: string; errorCode?: string; errorMessage?: string; evidencePath?: string;
};
type AutomationExecutionItem = {
  automationExecutionId: string; automationCaseId: string; automationCode: string; testCaseCode?: string; testCaseTitle?: string; automationVersionId: string; versionNo: number; testExecutionId?: string; defectId?: string; targetApp?: string;
  agentId?: string; agentCode?: string; buildId: string; buildNumber: string; environmentId: string; environmentName: string; jobId?: string; status: string;
  startedAt?: string; completedAt?: string; durationMs?: number; failureType?: string; errorCode?: string; errorMessage?: string; stepResults: AutomationStepResultItem[];
  evidence?: AutomationEvidenceItem[];
};
type AutomationEvidenceItem = { automationEvidenceId: string; stepNo?: number; evidenceType: string; filePath: string; capturedBy?: string; capturedAt: string };
type TestCandidate = { testCaseId: string; testCaseCode: string; title: string; priority: string; status: string; moduleId: string; automationCandidate?: boolean; testType?: string };
type TestCaseDetailItem = {
  testCaseId: string; projectId: string; moduleId: string; testCaseCode: string; title: string;
  objective?: string; preconditions?: string; priority: string; testType?: string; automationCandidate: boolean; status: string;
  revisionNo: number; ownerUserId?: string;
  steps: { stepNo: number; action: string; testData?: string; expectedResult: string }[];
};
type BuildOption = { buildId: string; buildNumber: string; applicationVersion?: string; status: string };
type EnvironmentOption = { testEnvironmentId: string; environmentName: string; isActive: boolean };
type AutomationDashboardItem = {
  totalTestCases: number; automationCandidates: number; automationCases: number; ready: number; maintenanceRequired: number;
  needsReview: number; inProgress: number; running: number; passToday: number; failToday: number; averageDurationMs?: number;
  agentsOnline: number; agentsTotal: number; readyCoverage: number; candidateCoverage: number;
};

function Badge({ children, tone = "blue" }: { children: React.ReactNode; tone?: string }) {
  return <span className={`badge ${tone}`}>{children}</span>;
}

const targetTone: Record<string, string> = { Pos: "blue", App: "purple", WindowsUI: "gray" };
const failureTone: Record<string, string> = { ApplicationFailure: "red", AssertionFailure: "yellow", TestDataFailure: "yellow", AutomationFailure: "blue", EnvironmentFailure: "yellow", AgentFailure: "blue", Unknown: "gray" };
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

const sampleDsl = JSON.stringify({
  dslVersion: "1.0",
  automationType: "WindowsUI",
  steps: [
    { stepNo: 1, action: "LOGIN", parameters: { userRef: "QA_STANDARD_USER" } },
    { stepNo: 2, action: "OPEN_MENU", parameters: { menu: "SALES" } },
    { stepNo: 3, action: "NEW_DOCUMENT", parameters: { documentType: "SALES" } },
    { stepNo: 4, action: "SELECT_ITEM", parameters: { itemCode: "A001" } },
    { stepNo: 5, action: "SET_QTY", parameters: { object: "Sales.Quantity", value: "20" } },
    { stepNo: 6, action: "SAVE_DOCUMENT", parameters: {} },
    { stepNo: 7, action: "EXPECT_MESSAGE", parameters: { messageKey: "STOCK_NOT_ENOUGH" } },
  ],
}, null, 2);

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
  projectId, releaseId, buildId, canEdit, canValidate, canApprove, canRun, canManage, canViewEvidence, canGenerateAi,
}: {
  projectId?: string; releaseId?: string; buildId?: string; canView: boolean; canEdit: boolean; canValidate: boolean; canApprove: boolean; canRun: boolean; canManage: boolean; canViewEvidence: boolean; canGenerateAi: boolean;
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
  const [builds, setBuilds] = useState<BuildOption[]>([]);
  const [environments, setEnvironments] = useState<EnvironmentOption[]>([]);
  const [manageTab, setManageTab] = useState("actions");
  const [batchModal, setBatchModal] = useState(false);

  const [execDetail, setExecDetail] = useState<AutomationExecutionItem | null>(null);
  const [evidenceBusy, setEvidenceBusy] = useState("");
  const [classifyBusy, setClassifyBusy] = useState("");
  const [classification, setClassification] = useState<{ failureType: string; isProductDefectCandidate: boolean; recommendation: string; detail?: string } | null>(null);
  const [aiAnalysis, setAiAnalysis] = useState<{ classification: string; confidence: number; summary: string; recommendation: string } | null>(null);
  const [defectResult, setDefectResult] = useState<string>("");

  const pid = projectId ?? "";

  useEffect(() => {
    if (!pid) { setCases([]); setObjects([]); setExecutions([]); setDash(null); return; }
    const h = { Authorization: `Bearer ${token()}` };
    setError("");
    Promise.all([
      fetch(`${apiUrl}/automation/cases?projectId=${pid}&take=200`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/objects?projectId=${pid}`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/jobs?projectId=${pid}${buildId ? `&buildId=${buildId}` : ""}&take=200`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/executions?projectId=${pid}${buildId ? `&buildId=${buildId}` : ""}&take=200`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/agents`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/actions`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/dashboard?projectId=${pid}`, { headers: h }).then((r) => (r.ok ? r.json() : null)),
    ])
      .then(([c, o, j, e, a, ac, d]) => {
        setCases(Array.isArray(c) ? c : []);
        setObjects(Array.isArray(o) ? o : []);
        setJobs(Array.isArray(j) ? j : []);
        setExecutions(Array.isArray(e) ? e : []);
        setAgents(Array.isArray(a) ? a : []);
        setActions(Array.isArray(ac) ? ac : []);
        setDash(d && typeof d === "object" && d.automationCases != null ? d : null);
      })
      .catch((err) => setError(err instanceof Error ? err.message : "โหลดข้อมูล Automation ไม่สำเร็จ"));
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
    try {
      const [tc, md] = await Promise.all([
        fetch(`${apiUrl}/test-cases?projectId=${pid}&automation=true&page=1&size=200`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : null)),
        fetch(`${apiUrl}/projects/${pid}/modules`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : [])),
      ]);
      setCandidates(Array.isArray(tc?.rows) ? tc.rows : []);
      setCreateModules(Array.isArray(md) ? md.filter((m: { isActive?: boolean }) => m.isActive !== false) : []);
    } catch {
      setCandidates([]);
      setCreateModules([]);
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
      const r = await fetch(`${apiUrl}/automation/cases/${createdCaseId}/generate?projectId=${pid}`, { method: "POST", headers });
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
      setNewVersionError(e instanceof Error ? e.message : "Generate AI ไม่สำเร็จ");
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
        setValErrors(msg);
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
      const r = await fetch(`${apiUrl}/automation/cases/${selectedCase.automationCaseId}/generate?projectId=${pid}`, { method: "POST", headers });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "Generate AI ไม่สำเร็จ");
      }
      await openCase(selectedCase);
      setReload((x) => x + 1);
      setNotice("AI สร้าง DSL แล้ว — Version ใหม่เป็น NeedsReview รอตรวจ/Validate/อนุมัติ");
    } catch (e) {
      setVersionError(e instanceof Error ? e.message : "Generate AI ไม่สำเร็จ");
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

  const openRun = async () => {
    setRunModal(true);
    setError("");
    try {
      const [b, e] = await Promise.all([
        releaseId ? fetch(`${apiUrl}/releases/${releaseId}/builds`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : [])) : Promise.resolve([]),
        fetch(`${apiUrl}/master-settings/environments`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : [])),
      ]);
      setBuilds(Array.isArray(b) ? b : []);
      setEnvironments(Array.isArray(e) ? e.filter((x: EnvironmentOption) => x.isActive) : []);
    } catch {
      setBuilds([]);
      setEnvironments([]);
    }
  };

  const runCase = async (item: AutomationCaseItem, versionId: string, selBuildId: string, envId: string, agentId: string, priority: number) => {
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
    if (!window.confirm(`ยืนยันยกเลิก Execution "${x.automationCode}" ?`)) return;
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
    if (!window.confirm(`สั่งรัน "${x.automationCode}" ซ้ำ?\nRev ${x.versionNo} · Build ${x.buildNumber} · ${x.environmentName}`)) return;
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
    if (!window.confirm(`ต้องการลบ Agent "${a.agentCode}" ออกหรือไม่?\n(ถ้า Agent ยังรันอยู่จะลงทะเบียนใหม่เองอัตโนมัติ)`)) return;
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
    setClassifyBusy("classify"); setClassification(null); setAiAnalysis(null); setDefectResult("");
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
    if (!execDetail) return;
    setClassifyBusy("defect");
    try {
      const r = await fetch(`${apiUrl}/automation/executions/${execDetail.automationExecutionId}/defect?projectId=${pid}`, { method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${token()}` }, body: JSON.stringify({ classification: aiAnalysis?.classification ?? null, severity: "High", title: null, description: null }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Defect ไม่สำเร็จ"); }
      const d = await r.json();
      setDefectResult(d.defectCode);
      setError("");
    } catch (e) { setError(e instanceof Error ? e.message : "สร้าง Defect ไม่สำเร็จ"); } finally { setClassifyBusy(""); }
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

  const headQuery = headSearch.trim().toLowerCase();
  const filteredCases = cases.filter((c) => (!headQuery || c.automationCode.toLowerCase().includes(headQuery) || c.testCaseCode.toLowerCase().includes(headQuery) || c.testCaseTitle.toLowerCase().includes(headQuery)) && (caseStatusFilter === "all" || c.status === caseStatusFilter) && (caseTargetFilter === "all" || c.automationType === caseTargetFilter));
  const casePageSize = 15;
  const casePageCount = Math.max(1, Math.ceil(filteredCases.length / casePageSize));
  const pagedCases = filteredCases.slice((casePage - 1) * casePageSize, casePage * casePageSize);
  useEffect(() => setCasePage(1), [headSearch, caseStatusFilter, caseTargetFilter]);

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

  const tabs = [
    { id: "dashboard", label: "ภาพรวม", icon: "◉" },
    { id: "cases", label: "Automation Cases", icon: "▤" },
    { id: "execution", label: "Execution", icon: "▶" },
    { id: "manage", label: "การจัดการ", icon: "⚙" },
  ];

  return <article className="automation-page">
    {!pid ? <div className="empty"><p>เลือก Project เพื่อดู Automation Workspace</p></div> : <>
      <section className="automation-page-head">
        <div className="automation-page-actions">
          <div className="automation-search"><input aria-label="ค้นหา Automation Case" placeholder="ค้นหา Automation Case..." value={headSearch} onChange={(e) => setHeadSearch(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter") setTab("cases"); }} /></div>
          <button className="btn" type="button" title="รีเฟรชข้อมูล" aria-label="รีเฟรชข้อมูล" onClick={() => setReload((v) => v + 1)}>↻ <span className="automation-hide-mobile">รีเฟรช</span></button>
          <button className="btn" type="button" disabled={!cases.length} onClick={exportCases}>↥ Export</button>
          {canEdit && <button className="btn primary" type="button" onClick={openCreate}>＋ สร้าง Automation Case</button>}
        </div>
      </section>
      <nav className="automation-tabs" aria-label="Automation Module"><div className="automation-tabs-inner">{tabs.map((t) => <button key={t.id} type="button" className={tab === t.id ? "active" : ""} aria-current={tab === t.id ? "page" : undefined} onClick={() => setTab(t.id)}><span aria-hidden="true">{t.icon}</span>{t.label}</button>)}</div></nav>
      {error && <div className="inline-alert error"><span>{error}</span></div>}
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
            <div className="automation-panel-head"><h2>Agent Status</h2>{agents.length > 0 && <button className="automation-panel-link" onClick={() => setTab("manage")}>ดูทั้งหมด</button>}</div>
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
          <div className="automation-panel-head"><h2>ผลการรันล่าสุด</h2><button className="automation-panel-link" onClick={() => setTab("execution")}>ดูทั้งหมด</button></div>
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
          </> : <div className="empty"><p>ยังไม่มีประวัติการรัน</p><small>สร้าง Automation Case แล้วรันผ่าน Agent — ผลจะแสดงที่นี่</small>{canEdit && <button className="btn primary" onClick={openCreate}>+ สร้าง Automation Case</button>}</div>}
        </section>
      </section>}

      {tab === "cases" && <section className="automation-cases" aria-label="Automation Cases">
        <header className="automation-section-head"><div><h2>Automation Cases</h2><p>หนึ่ง Test Case → หนึ่ง Automation Case พร้อม Version (DSL) หลายเวอร์ชัน</p></div><div className="automation-cases-actions">{canRun && <button className="btn" onClick={() => setBatchModal(true)}>▶ รันเป็นกลุ่ม</button>}{canEdit && <button className="btn primary" onClick={openCreate}>+ สร้าง Automation Case</button>}</div></header>
        {cases.length ? <>
          <div className="automation-case-toolbar">
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
            {(caseStatusFilter !== "all" || caseTargetFilter !== "all" || headSearch.trim()) && <button type="button" className="table-action" onClick={() => { setCaseStatusFilter("all"); setCaseTargetFilter("all"); setHeadSearch(""); }}>ล้างตัวกรอง</button>}
          </div>
          {(headSearch.trim() || caseStatusFilter !== "all" || caseTargetFilter !== "all") && <div className="automation-search-hint">แสดง {filteredCases.length} จาก {cases.length} รายการ{headSearch.trim() ? ` · ค้นหา "${headSearch}"` : ""}{caseStatusFilter !== "all" ? ` · สถานะ ${caseStatusFilter}` : ""}{caseTargetFilter !== "all" ? ` · Target ${caseTargetFilter}` : ""} — <button type="button" className="table-action" onClick={() => { setHeadSearch(""); setCaseStatusFilter("all"); setCaseTargetFilter("all"); }}>ล้างทั้งหมด</button></div>}
          {pagedCases.length ? <div className="table-wrap"><table><thead><tr><th>Code</th><th>Test Case</th><th>Target App</th><th>Status</th><th>Version</th><th>Owner</th><th></th></tr></thead><tbody>{pagedCases.map((c) => <tr key={c.automationCaseId}><td><b>{c.automationCode}</b></td><td><span>{c.testCaseCode}</span><small>{c.testCaseTitle}</small></td><td><Badge tone={targetTone[c.automationType] ?? "blue"}>{c.automationType}</Badge></td><td><Badge tone={caseStatusTone[c.status] ?? "blue"}>{c.status}</Badge></td><td>Rev {c.currentVersionNo}</td><td>{c.ownerName ?? "-"}</td><td><button className="table-action" onClick={() => openCase(c)}>รายละเอียด</button></td></tr>)}</tbody></table></div>
            : <div className="empty"><p>ไม่พบ Automation Case ที่ตรงเงื่อนไข</p><small>ลองเปลี่ยนคำค้นหาหรือตัวกรองด้านบน</small></div>}
          {filteredCases.length > casePageSize && <Pager page={casePage} count={casePageCount} total={filteredCases.length} pageSize={casePageSize} onPrev={() => setCasePage((p) => Math.max(1, p - 1))} onNext={() => setCasePage((p) => Math.min(casePageCount, p + 1))} />}
        </> : <div className="empty"><p>ยังไม่มี Automation Case</p><small>สร้างจาก Test Case ที่เป็น Automation Candidate — จากนั้นเขียน DSL / Generate AI → Validate → อนุมัติ → พร้อมรัน</small>{canEdit && <button className="btn primary" onClick={openCreate}>+ สร้าง Automation Case</button>}</div>}
      <div className="automation-status-legend" role="note" aria-label="ความหมายสถานะ"><span><i className="legend-dot legend-draft" />Draft — ยังไม่มี DSL</span><span><i className="legend-dot legend-review" />NeedsReview — AI สร้างแล้ว รอตรวจ</span><span><i className="legend-dot legend-ready" />Ready — พร้อมรัน</span><span><i className="legend-dot legend-maint" />MaintenanceRequired — ต้องซ่อม DSL/Object</span></div>
      </section>}

      {tab === "manage" && <section className="automation-manage" aria-label="Automation จัดการ">
        <nav className="automation-subtabs" aria-label="จัดการ"><button type="button" className={manageTab === "actions" ? "active" : ""} onClick={() => setManageTab("actions")}>Action Library</button><button type="button" className={manageTab === "objects" ? "active" : ""} onClick={() => setManageTab("objects")}>Object Repository</button><button type="button" className={manageTab === "agents" ? "active" : ""} onClick={() => setManageTab("agents")}>Agents</button></nav>
        {manageTab === "actions" && <ActionLibraryTab actions={actions} canManage={canManage} headers={headers} onReload={() => setReload((x) => x + 1)} onError={setError} actionModal={actionModal} setActionModal={setActionModal} />}
        {manageTab === "objects" && <ObjectRepositoryTab projectId={pid} objects={objects} canManage={canManage} headers={headers} onReload={() => setReload((x) => x + 1)} onError={setError} objectModal={objectModal} setObjectModal={setObjectModal} />}
        {manageTab === "agents" && <AgentsSection agents={agents} agentsOnline={agentsOnline} canManage={canManage} onToggle={toggleAgent} onDelete={deleteAgent} />}
      </section>}

      {tab === "execution" && <ExecutionTab jobs={jobs} executions={executions} setExecDetail={setExecDetail} execFilter={execFilter} setExecFilter={setExecFilter} canRun={canRun} onCancel={cancelExecution} onRerun={rerunExecution} />}
    </>}

    {createModal && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-create-title" onMouseDown={() => !createBusy && wizardStep !== 4 && setCreateModal(false)}><div className="modal-box automation-create-modal" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div className="acw-head"><span className="acw-head-icon" aria-hidden="true">⚙</span><div><h2 id="automation-create-title">สร้าง Automation Case</h2><small>Wizard สำหรับเลือก Test Case ตรวจสอบรายละเอียด สร้าง DSL และบันทึก Automation Case</small></div></div><button aria-label="ปิด" disabled={createBusy} onClick={() => setCreateModal(false)}>×</button></div>

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
              <input aria-label="ค้นหา Test Case" placeholder="ค้นหา Code / ชื่อ..." value={createSearch} onChange={(e) => setCreateSearch(e.target.value)} />
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
          {newVersionError && <div className="inline-alert error"><span>{newVersionError}</span></div>}
          <div className="acw-card">
            <div className="acw-section-head"><div><h2>Automation Case</h2><p>ตรวจสอบข้อมูล Automation ก่อนอนุมัติ</p></div><span className="badge ai">{aiConf != null ? "AI Generated" : "Manual DSL"}</span></div>
            <div className="acw-form-grid">
              <label className="field full">Automation Code<input value={createdCode} readOnly /></label>
              <label className="field full">Automation Name<input value={`Automation - ${createPick?.title ?? ""}`} readOnly /></label>
              <label className="field">Linked Test Case<input value={createPick?.testCaseCode ?? ""} readOnly /></label>
              <label className="field">Version<input value="1.0" readOnly /></label>
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
            <div className="acw-section-head"><div><h2>Automation DSL</h2><p>ภาษากลางที่ Agent จะนำไป Execute กับ ProMaxx2 Windows</p></div>{canGenerateAi && <button type="button" className="btn" disabled={createBusy} onClick={generateAiForNewCase}>{createBusy ? "AI กำลังสร้าง..." : "↻ Generate AI"}</button>}</div>
            <textarea className="acw-dsl" rows={14} value={newDsl} onChange={(e) => setNewDsl(e.target.value)} spellCheck={false} aria-label="DSL JSON" />
            <div className="acw-action-bar">
              <button type="button" className="btn" disabled={createBusy} onClick={() => { setNewDsl(sampleDsl); setValErrors(""); setValidatedOk(false); }}>โหลดตัวอย่าง</button>
            </div>
            <div className="acw-note">{valErrors ? <span className="warn">✕ Validate Error: {valErrors}</span> : <span className="ok">✓ สถานะ: {validatedOk ? "Validate ผ่าน — พร้อมบันทึก" : "DSL พร้อมตรวจสอบ — กด 'บันทึก + Validate'"}</span>}</div>
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
              <button type="button" className="btn" onClick={openCreatedCase}>ดู Automation Case</button>
              <button type="button" className="btn" onClick={() => setCreateModal(false)}>ไปหน้า Automation</button>
              <button type="button" className="btn acw-btn-success" onClick={resetWizard}>สร้าง Case เพิ่ม</button>
            </div>
          </div>
        </div>}
      </div>

      {wizardStep < 4 && <div className="modal-actions">
        <button className="btn" disabled={createBusy} onClick={() => setCreateModal(false)}>ยกเลิก</button>
        {wizardStep > 1 && <button className="btn" disabled={createBusy} onClick={() => setWizardStep((s) => s - 1)}>‹ ย้อนกลับ</button>}
        {wizardStep === 1 && <button className="btn primary" disabled={createBusy || !createPick} onClick={() => setWizardStep(2)}>ถัดไป ›</button>}
        {wizardStep === 2 && <button className="btn primary" disabled={createBusy || !createPick} onClick={async () => { const r = await createCase(createPick?.testCaseId ?? "", wizardType); if (r) setWizardStep(3); }}>{createBusy ? "กำลังสร้าง..." : "สร้าง Automation ›"}</button>}
        {wizardStep === 3 && <button className="btn primary" disabled={createBusy || !newDsl.trim()} onClick={createNewVersionAndValidate}>{createBusy ? "กำลังบันทึก..." : "บันทึก + Validate ›"}</button>}
      </div>}
    </div></div>}

    {selectedCase && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-case-detail-title" onMouseDown={() => setSelectedCase(null)}><div className="modal-box automation-case-detail" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2 id="automation-case-detail-title">{selectedCase.automationCode}</h2><small>{selectedCase.testCaseCode} · {selectedCase.testCaseTitle}</small></div><button aria-label="ปิด" onClick={() => setSelectedCase(null)}>×</button></div>
      <div className="automation-case-detail-hero"><Badge tone={caseStatusTone[selectedCase.status] ?? "blue"}>{selectedCase.status}</Badge><span>Target App: </span>{canEdit ? <select aria-label="Target App" value={selectedCase.automationType} disabled={createBusy} onChange={(e) => changeTarget(e.target.value)}><option value="Pos">Pos · PromaxxsPos.exe</option><option value="App">App · Promaxxs.App.exe</option><option value="WindowsUI">WindowsUI · generic</option></select> : <Badge tone={targetTone[selectedCase.automationType] ?? "blue"}>{selectedCase.automationType}</Badge>}<span>Rev {selectedCase.currentVersionNo}</span><span>AI Generated: {selectedCase.isAiGenerated ? "ใช่" : "ไม่"}</span></div>
      <p className="automation-case-hint">{selectedCase.status === "Draft" ? "ขั้นตอนถัดไป: เขียน DSL (หรือกด ✦ Generate AI) แล้ว Validate" : selectedCase.status === "NeedsReview" ? "ขั้นตอนถัดไป: ตรวจ DSL ที่ AI สร้าง → กด Validate → อนุมัติ" : selectedCase.status === "Validated" || selectedCase.status === "Approved" ? "ขั้นตอนถัดไป: กดอนุมัติ (ถ้ายัง) → Case จะเป็น Ready และสั่งรันได้" : selectedCase.status === "Ready" ? "พร้อมรัน — กด ▶ สั่งรัน หรือรันเป็นกลุ่มใน Regression Suites" : selectedCase.status === "MaintenanceRequired" ? "ต้องซ่อม: แก้ Object Repository / DSL → Validate ใหม่ → อนุมัติ" : "สร้าง Version แล้ว Validate/อนุมัติเพื่อให้พร้อมรัน"}</p>
      <VersionEditor selectedCase={selectedCase} versions={versions} canEdit={canEdit} canValidate={canValidate} canApprove={canApprove} canRun={canRun} canGenerateAi={canGenerateAi} createBusy={createBusy} versionError={versionError} onCreate={createVersion} onValidate={validateVersion} onApprove={approveVersion} onRun={openRun} onGenerateAi={generateAi} />
      <div className="modal-actions"><button className="btn primary" onClick={() => setSelectedCase(null)}>ปิด</button></div>
    </div></div>}

    {runModal && selectedCase && <RunModal item={selectedCase} versions={versions} builds={builds} environments={environments} agents={agents} busy={createBusy} onClose={() => setRunModal(false)} onRun={runCase} />}
    {batchModal && <BatchRunModal cases={cases} releaseId={releaseId} canRun={canRun} busy={createBusy} onClose={() => setBatchModal(false)} onRunBatch={runBatch} onError={setError} />}
    {execDetail && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-exec-detail-title" onMouseDown={() => setExecDetail(null)}><div className="modal-box automation-exec-detail" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2 id="automation-exec-detail-title">{execDetail.automationCode} · Execution</h2><small>Build {execDetail.buildNumber} · {execDetail.environmentName}{execDetail.agentCode ? ` · ${execDetail.agentCode}` : ""}</small></div><button aria-label="ปิด" onClick={() => setExecDetail(null)}>×</button></div>
      <div className="automation-run-detail-summary">
        <Badge tone={executionStatusTone[execDetail.status] ?? "blue"}>{execDetail.status}</Badge>
        <span>เริ่ม {formatThaiDateTime(execDetail.startedAt)}</span>
        <span>จบ {formatThaiDateTime(execDetail.completedAt)}</span>
        {execDetail.durationMs != null && <span>{(execDetail.durationMs / 1000).toFixed(2)} วิ</span>}
        {execDetail.errorCode && <Badge tone="red">{execDetail.errorCode}</Badge>}
        {canRun && (execDetail.status === "Running" || execDetail.status === "Queued") && <button type="button" className="btn danger automation-detail-action" onClick={() => cancelExecution(execDetail)}>✕ ยกเลิก</button>}
        {canRun && execDetail.status !== "Running" && execDetail.status !== "Queued" && <button type="button" className="btn automation-detail-action" onClick={() => rerunExecution(execDetail)}>▶ รันซ้ำ</button>}
      </div>
      {execDetail.errorMessage && <div className="inline-alert error"><span>{execDetail.errorMessage}</span></div>}
      {execDetail.testExecutionId && <p className="muted-text">สร้าง TestExecution (ExecutionType = Automation) แล้ว</p>}
      {execDetail.status === "Failed" && <section className="automation-failure-analysis">
        <div className="automation-section-head"><h3>Failure Analysis (G9)</h3><div className="automation-failure-actions">
          <button className="btn" disabled={classifyBusy !== ""} onClick={runClassify}>{classifyBusy === "classify" ? "กำลังจำแนก..." : "จำแนก Fail"}</button>
          {canGenerateAi && <button className="btn" disabled={classifyBusy !== ""} onClick={runAnalyze}>{classifyBusy === "analyze" ? "AI กำลังวิเคราะห์..." : "วิเคราะห์ด้วย AI"}</button>}
          {canEdit && !execDetail.defectId && <button className="btn danger" disabled={classifyBusy !== ""} onClick={runCreateDefect}>{classifyBusy === "defect" ? "กำลังสร้าง..." : "สร้าง Defect"}</button>}
        </div></div>
        {classification && <div className="automation-failure-row"><Badge tone={failureTone[classification.failureType] ?? "blue"}>{classification.failureType}</Badge><span>Product Defect Candidate: {classification.isProductDefectCandidate ? "ใช่" : "ไม่ใช่"}</span><span>แนะนำ: {classification.recommendation}</span>{classification.detail && <small>{classification.detail}</small>}</div>}
        {aiAnalysis && <div className="automation-failure-row"><Badge tone={failureTone[aiAnalysis.classification] ?? "blue"}>{aiAnalysis.classification}</Badge><span>AI Confidence {(aiAnalysis.confidence * 100).toFixed(0)}%</span><span>แนะนำ: {aiAnalysis.recommendation}</span><small>{aiAnalysis.summary}</small></div>}
        {defectResult && <div className="inline-alert success"><span>สร้าง Defect แล้ว: <b>{defectResult}</b> — เปิดหน้า Defect เพื่อดูรายละเอียด</span></div>}
        {execDetail.defectId && <p className="muted-text">สร้าง Defect แล้ว (Execution เชื่อมกับ Defect แล้ว)</p>}
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
      </section> : null}
      <div className="modal-actions"><button className="btn primary" onClick={() => setExecDetail(null)}>ปิด</button></div>
    </div></div>}
  </article>;
}

function VersionEditor({
  selectedCase, versions, canEdit, canValidate, canApprove, canRun, canGenerateAi, createBusy, versionError, onCreate, onValidate, onApprove, onRun, onGenerateAi,
}: {
  selectedCase: AutomationCaseItem; versions: AutomationVersionItem[]; canEdit: boolean; canValidate: boolean; canApprove: boolean; canRun: boolean; canGenerateAi: boolean;
  createBusy: boolean; versionError: string;
  onCreate: (dsl: string, reason: string) => void; onValidate: (v: AutomationVersionItem) => void; onApprove: (v: AutomationVersionItem) => void;
  onRun: () => void; onGenerateAi: () => void;
}) {
  const [dsl, setDsl] = useState(sampleDsl);
  const [reason, setReason] = useState("");

  return <div className="automation-version-editor">
    {versionError && <div className="inline-alert error"><span>{versionError}</span></div>}
    <section className="automation-version-list">
      <h3>Version History ({versions.length})</h3>
      {versions.length ? versions.map((v) => <article key={v.automationVersionId} className="automation-version-row">
        <div className="automation-version-meta"><b>Rev {v.versionNo}</b><Badge tone={versionStatusTone[v.validationStatus] ?? "gray"}>{v.validationStatus}</Badge>{v.approvedAt && <Badge tone="green">Approved</Badge>}<span>TestCase Rev {v.testCaseRevisionNo}</span><span>{parseDslSteps(v.dslJson).length} steps</span><time>{formatThaiDateTime(v.createdAt)}</time></div>
        <p className="automation-dsl-preview">{v.dslJson.length > 300 ? `${v.dslJson.slice(0, 300)}…` : v.dslJson}</p>
        {v.validationErrors && <p className="automation-validation-errors">{v.validationErrors}</p>}
        <div className="automation-version-actions">
          {canValidate && v.validationStatus !== "Valid" && <button className="btn" disabled={createBusy} onClick={() => onValidate(v)}>Validate</button>}
          {canApprove && v.validationStatus === "Valid" && !v.approvedAt && <button className="btn primary" disabled={createBusy} onClick={() => onApprove(v)}>อนุมัติ</button>}
          {canRun && selectedCase.status === "Ready" && <button className="btn primary" disabled={createBusy} onClick={onRun}>▶ สั่งรัน</button>}
        </div>
      </article>) : <div className="empty"><p>ยังไม่มี Version</p><small>สร้าง Version แรกด้วย DSL ด้านล่าง</small></div>}
    </section>
    {canEdit && <section className="automation-version-create">
      <h3>สร้าง Version ใหม่ (DSL v1)</h3>
      <label className="full">DSL JSON<textarea rows={14} value={dsl} onChange={(e) => setDsl(e.target.value)} spellCheck={false} aria-label="DSL JSON" /></label>
      <div className="automation-version-create-actions">
        {canGenerateAi && <button type="button" className="btn primary" disabled={createBusy} onClick={onGenerateAi}>{createBusy ? "AI กำลังสร้าง..." : "✦ Generate AI"}</button>}
        <button type="button" className="btn" onClick={() => setDsl(sampleDsl)}>โหลดตัวอย่าง</button>
        <label className="automation-reason-field">หมายเหตุการเปลี่ยนแปลง<input value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} placeholder="เช่น เพิ่ม step ตรวจ stock" /></label>
        <button type="button" className="btn" disabled={createBusy || !dsl.trim()} onClick={() => onCreate(dsl, reason)}>{createBusy ? "กำลังบันทึก..." : "สร้าง Version"}</button>
      </div>
      <p className="muted-text">DSL ต้องเป็น JSON ตามรูปแบบ: <code>{"{ dslVersion, automationType, steps: [{ stepNo, action, parameters }] }"}</code> — Action ต้องมีใน Action Library</p>
    </section>}
  </div>;
}

function RunModal({
  item, versions, builds, environments, agents, busy, onClose, onRun,
}: {
  item: AutomationCaseItem; versions: AutomationVersionItem[]; builds: BuildOption[]; environments: EnvironmentOption[]; agents: AutomationAgentItem[];
  busy: boolean; onClose: () => void; onRun: (c: AutomationCaseItem, versionId: string, buildId: string, envId: string, agentId: string, priority: number) => void;
}) {
  const [versionId, setVersionId] = useState("");
  const [buildId, setBuildId] = useState("");
  const [envId, setEnvId] = useState("");
  const [agentId, setAgentId] = useState("");
  const [priority, setPriority] = useState(5);
  const approved = versions.find((v) => v.approvedAt && v.validationStatus === "Valid");
  useEffect(() => { if (!versionId && approved) setVersionId(approved.automationVersionId); }, [versionId, approved]);

  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-run-title" onMouseDown={() => !busy && onClose()}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
    <div className="modal-head"><div><h2 id="automation-run-title">สั่งรัน {item.automationCode}</h2><small>สร้าง Automation Execution + Job เข้าคิว ให้ Agent รับไปรัน</small></div><button aria-label="ปิด" disabled={busy} onClick={onClose}>×</button></div>
    <div className="form-grid">
      <label className="full">Automation Version<select value={versionId} onChange={(e) => setVersionId(e.target.value)}><option value="">เลือก Version</option>{versions.map((v) => <option key={v.automationVersionId} value={v.automationVersionId}>Rev {v.versionNo} · {v.validationStatus}{v.approvedAt ? " · Approved" : ""}</option>)}</select></label>
      <label>Build<select value={buildId} onChange={(e) => setBuildId(e.target.value)}><option value="">เลือก Build</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}</option>)}</select></label>
      <label>Environment<select value={envId} onChange={(e) => setEnvId(e.target.value)}><option value="">เลือก Environment</option>{environments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>
      <label>Agent (ไม่บังคับ)<select value={agentId} onChange={(e) => setAgentId(e.target.value)}><option value="">ปล่อยให้คิวจัดสรร</option>{agents.map((a) => <option key={a.agentId} value={a.agentId}>{a.agentCode} · {a.connectivity}</option>)}</select></label>
      <label>Priority<select value={priority} onChange={(e) => setPriority(Number(e.target.value))}>{[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((p) => <option key={p} value={p}>{p}</option>)}</select></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}>ยกเลิก</button><button className="btn primary" disabled={busy || !versionId || !buildId || !envId} onClick={() => onRun(item, versionId, buildId, envId, agentId, priority)}>{busy ? "กำลังส่งงาน..." : "ส่งเข้าคิว"}</button></div>
  </div></div>;
}

function ActionLibraryTab({ actions, canManage, headers, onReload, onError, actionModal, setActionModal }: {
  actions: AutomationActionItem[]; canManage: boolean; headers: Record<string, string>; onReload: () => void; onError: (e: string) => void;
  actionModal: boolean; setActionModal: (v: boolean) => void;
}) {
  const [category, setCategory] = useState("ทั้งหมด");
  const [form, setForm] = useState({ actionCode: "", actionName: "", category: "Generic", description: "", minimumAgentVersion: "1.0.0" });
  const [busy, setBusy] = useState(false);
  const cats = ["ทั้งหมด", ...Array.from(new Set(actions.map((a) => a.category)))];
  const filtered = category === "ทั้งหมด" ? actions : actions.filter((a) => a.category === category);

  const save = async () => {
    setBusy(true);
    try {
      const r = await fetch(`${apiUrl}/automation/actions`, { method: "POST", headers, body: JSON.stringify({ ...form, parameterSchemaJson: "{}" }) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "สร้าง Action ไม่สำเร็จ");
      }
      setActionModal(false);
      onReload();
    } catch (e) {
      onError(e instanceof Error ? e.message : "สร้าง Action ไม่สำเร็จ");
    } finally {
      setBusy(false);
    }
  };

  return <section className="automation-actions" aria-label="Action Library">
    <header className="automation-section-head"><div><h2>Action Library</h2><p>ชุดคำสั่งที่ Agent รองรับ · <code>ActionCode</code> ต้องตรงกับ DSL</p></div>{canManage && <button className="btn primary" onClick={() => setActionModal(true)}>+ เพิ่ม Action</button>}</header>
    <div className="automation-cand-filters" role="group" aria-label="กรอง Action ตาม Category">{cats.map((c) => <button key={c} type="button" className={"chip" + (category === c ? " active" : "")} onClick={() => setCategory(c)}>{c}</button>)}</div>
    {filtered.length ? <div className="table-wrap"><table><thead><tr><th>Action Code</th><th>Name</th><th>Category</th><th>Handler</th><th>Min Agent</th><th>Active</th></tr></thead><tbody>{filtered.map((a) => <tr key={a.automationActionId}><td><b>{a.actionCode}</b></td><td>{a.actionName}</td><td><Badge tone="blue">{a.category}</Badge></td><td><code>{a.handlerKey}</code></td><td>{a.minimumAgentVersion ?? "-"}</td><td><Badge tone={a.isActive ? "green" : "gray"}>{a.isActive ? "Active" : "Inactive"}</Badge></td></tr>)}</tbody></table></div> : <div className="empty"><p>ไม่พบ Action</p></div>}

    {actionModal && <div className="modal" role="dialog" aria-modal="true" onMouseDown={() => !busy && setActionModal(false)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2>เพิ่ม Action</h2><small>Action จะถูกใช้ตรวจสอบ (Validate) ว่า DSL ถูกต้อง</small></div><button aria-label="ปิด" disabled={busy} onClick={() => setActionModal(false)}>×</button></div>
      <div className="form-grid">
        <label>Action Code<input value={form.actionCode} onChange={(e) => setForm({ ...form, actionCode: e.target.value.toUpperCase() })} placeholder="เช่น SET_QTY" /></label>
        <label>Name<input value={form.actionName} onChange={(e) => setForm({ ...form, actionName: e.target.value })} /></label>
        <label>Category<select value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })}><option>Authentication</option><option>Navigation</option><option>Document</option><option>Item</option><option>Generic UI</option><option>Validation</option></select></label>
        <label>Minimum Agent Version<input value={form.minimumAgentVersion} onChange={(e) => setForm({ ...form, minimumAgentVersion: e.target.value })} /></label>
        <label className="full">Description<textarea value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} /></label>
      </div>
      <div className="modal-actions"><button className="btn" disabled={busy} onClick={() => setActionModal(false)}>ยกเลิก</button><button className="btn primary" disabled={busy || !form.actionCode.trim() || !form.actionName.trim()} onClick={save}>{busy ? "กำลังบันทึก..." : "บันทึก"}</button></div>
    </div></div>}
  </section>;
}

function ObjectRepositoryTab({ projectId, objects, canManage, headers, onReload, onError, objectModal, setObjectModal }: {
  projectId: string; objects: AutomationObjectItem[]; canManage: boolean; headers: Record<string, string>; onReload: () => void; onError: (e: string) => void;
  objectModal: boolean; setObjectModal: (v: boolean) => void;
}) {
  const [screen, setScreen] = useState("");
  const [form, setForm] = useState({ applicationCode: "Promaxx2", screenCode: "Sales", objectCode: "", objectName: "", controlType: "Button", automationId: "", selectorJson: "{}" });
  const [busy, setBusy] = useState(false);
  const screens = ["", ...Array.from(new Set(objects.map((o) => o.screenCode)))];
  const filtered = screen ? objects.filter((o) => o.screenCode === screen) : objects;

  const save = async () => {
    setBusy(true);
    try {
      const r = await fetch(`${apiUrl}/automation/objects`, { method: "POST", headers, body: JSON.stringify({ projectId, moduleId: null, ...form }) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "สร้าง Object ไม่สำเร็จ");
      }
      setObjectModal(false);
      onReload();
    } catch (e) {
      onError(e instanceof Error ? e.message : "สร้าง Object ไม่สำเร็จ");
    } finally {
      setBusy(false);
    }
  };

  return <section className="automation-objects" aria-label="Object Repository">
    <header className="automation-section-head"><div><h2>Object Repository</h2><p>Mapping ชื่อ Business (<code>Screen.Object</code>) ไปยัง Windows Control (<code>AutomationId</code>)</p></div>{canManage && <button className="btn primary" onClick={() => setObjectModal(true)}>+ เพิ่ม Object</button>}</header>
    <div className="automation-cand-filters" role="group" aria-label="กรอง Object ตาม Screen">{screens.map((s) => <button key={s || "all"} type="button" className={"chip" + (screen === s ? " active" : "")} onClick={() => setScreen(s)}>{s || "ทุก Screen"}</button>)}</div>
    {filtered.length ? <div className="table-wrap"><table><thead><tr><th>Business Key</th><th>Name</th><th>Screen</th><th>ControlType</th><th>AutomationId</th><th>Active</th></tr></thead><tbody>{filtered.map((o) => <tr key={o.automationObjectId}><td><b>{buildObjectKey(o.screenCode, o.objectCode)}</b></td><td>{o.objectName}</td><td><Badge tone="blue">{o.screenCode}</Badge></td><td>{o.controlType}</td><td><code>{o.automationId ?? "-"}</code></td><td><Badge tone={o.isActive ? "green" : "gray"}>{o.isActive ? "Active" : "Inactive"}</Badge></td></tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Object</p><small>Agent จะใช้ <code>AutomationId</code> นี้หาคอนโทรลบน Windows UI</small></div>}

    {objectModal && <div className="modal" role="dialog" aria-modal="true" onMouseDown={() => !busy && setObjectModal(false)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2>เพิ่ม Object</h2><small>Business Key = <code>ScreenCode.ObjectCode</code> — DSL อ้างอิงด้วยค่านี้</small></div><button aria-label="ปิด" disabled={busy} onClick={() => setObjectModal(false)}>×</button></div>
      <div className="form-grid">
        <label>Application Code<input value={form.applicationCode} onChange={(e) => setForm({ ...form, applicationCode: e.target.value })} /></label>
        <label>Screen Code<input value={form.screenCode} onChange={(e) => setForm({ ...form, screenCode: e.target.value })} placeholder="เช่น Sales" /></label>
        <label>Object Code<input value={form.objectCode} onChange={(e) => setForm({ ...form, objectCode: e.target.value.toUpperCase() })} placeholder="เช่น SAVE" /></label>
        <label>Object Name<input value={form.objectName} onChange={(e) => setForm({ ...form, objectName: e.target.value })} /></label>
        <label>Control Type<select value={form.controlType} onChange={(e) => setForm({ ...form, controlType: e.target.value })}><option>Button</option><option>TextBox</option><option>ComboBox</option><option>CheckBox</option><option>Menu</option><option>Window</option></select></label>
        <label>AutomationId<input value={form.automationId} onChange={(e) => setForm({ ...form, automationId: e.target.value })} placeholder="เช่น btnSave" /></label>
      </div>
      <div className="modal-actions"><button className="btn" disabled={busy} onClick={() => setObjectModal(false)}>ยกเลิก</button><button className="btn primary" disabled={busy || !form.screenCode.trim() || !form.objectCode.trim() || !form.objectName.trim() || !form.automationId.trim()} onClick={save}>{busy ? "กำลังบันทึก..." : "บันทึก"}</button></div>
    </div></div>}
  </section>;
}

function BatchRunModal({ cases, releaseId, canRun, busy, onClose, onRunBatch, onError }: {
  cases: AutomationCaseItem[]; releaseId?: string; canRun: boolean; busy: boolean; onClose: () => void;
  onRunBatch: (ids: string[], buildId: string, envId: string, priority: number) => Promise<void>; onError: (e: string) => void;
}) {
  const readyCases = cases.filter((c) => c.status === "Ready");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [buildId, setBuildId] = useState("");
  const [envId, setEnvId] = useState("");
  const [priority, setPriority] = useState(5);
  const [builds, setBuilds] = useState<BuildOption[]>([]);
  const [environments, setEnvironments] = useState<EnvironmentOption[]>([]);

  useEffect(() => {
    let mounted = true;
    Promise.all([
      releaseId ? fetch(`${apiUrl}/releases/${releaseId}/builds`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : [])) : Promise.resolve([]),
      fetch(`${apiUrl}/master-settings/environments`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : [])),
    ]).then(([b, e]) => {
      if (!mounted) return;
      setBuilds(Array.isArray(b) ? b : []);
      setEnvironments(Array.isArray(e) ? (e as EnvironmentOption[]).filter((x) => x.isActive) : []);
    }).catch(() => onError("โหลด Build/Environment ไม่สำเร็จ"));
    return () => { mounted = false; };
  }, [releaseId, onError]);

  const toggle = (id: string) => setSelected((prev) => { const next = new Set(prev); if (next.has(id)) next.delete(id); else next.add(id); return next; });
  const toggleAll = () => setSelected((prev) => prev.size === readyCases.length ? new Set() : new Set(readyCases.map((c) => c.automationCaseId)));

  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-batch-title" onMouseDown={() => !busy && onClose()}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
    <div className="modal-head"><div><h2 id="automation-batch-title">รันเป็นกลุ่ม (Regression)</h2><small>เลือก Automation Case ที่พร้อมรัน — งานกระจายไปหลาย Agent พร้อมกัน</small></div><button aria-label="ปิด" disabled={busy} onClick={onClose}>×</button></div>
    <div className="form-grid">
      <label>Build<select value={buildId} onChange={(e) => setBuildId(e.target.value)}><option value="">เลือก Build</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}</option>)}</select></label>
      <label>Environment<select value={envId} onChange={(e) => setEnvId(e.target.value)}><option value="">เลือก Env</option>{environments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>
      <label>Priority<select value={priority} onChange={(e) => setPriority(Number(e.target.value))}>{[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((p) => <option key={p} value={p}>{p}</option>)}</select></label>
    </div>
    {readyCases.length ? <div className="automation-batch-list">
      <div className="automation-batch-head"><input type="checkbox" aria-label="เลือกทั้งหมด" checked={selected.size === readyCases.length && readyCases.length > 0} disabled={!canRun} onChange={toggleAll} /><b>เลือกทั้งหมด ({readyCases.length})</b><span>{selected.size} เลือก</span></div>
      {readyCases.map((c) => <label key={c.automationCaseId} className="automation-batch-row"><input type="checkbox" aria-label={`เลือก ${c.automationCode}`} checked={selected.has(c.automationCaseId)} disabled={!canRun} onChange={() => toggle(c.automationCaseId)} /><span><b>{c.automationCode}</b><small>{c.testCaseCode} · {c.testCaseTitle}</small></span><Badge tone={targetTone[c.automationType] ?? "blue"}>{c.automationType}</Badge></label>)}
    </div> : <div className="empty"><p>ยังไม่มี Automation Case ที่ Ready</p><small>สร้าง Case แล้ว Validate/อนุมัติให้เป็น Ready ก่อน</small></div>}
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}>ยกเลิก</button><button className="btn primary" disabled={!canRun || busy || !selected.size || !buildId || !envId} onClick={() => onRunBatch([...selected], buildId, envId, priority)}>{busy ? "กำลังส่ง..." : `▶ รัน ${selected.size} case`}</button></div>
  </div></div>;
}

function AgentsSection({ agents, agentsOnline, canManage, onToggle, onDelete }: {
  agents: AutomationAgentItem[]; agentsOnline: number; canManage: boolean; onToggle: (a: AutomationAgentItem, enable: boolean) => void; onDelete: (a: AutomationAgentItem) => void;
}) {
  return <section className="automation-agents" aria-label="Automation Agents">
    <header className="automation-section-head"><div><h2>Central Windows Agents</h2><p>Agent ลงทะเบียนอัตโนมัติและส่ง heartbeat ทุก 15 วินาที · Offline เมื่อเงียบเกิน 60 วินาที</p></div><span className="automation-agent-count">{agentsOnline} Online</span></header>
    {agents.length ? <div className="automation-agent-grid">{agents.map((a) => <article key={a.agentId}><div className="automation-agent-top"><div><Badge tone={a.connectivity === "Online" ? "green" : a.connectivity === "Disabled" ? "gray" : "yellow"}>{a.connectivity}</Badge><Badge tone={a.status === "Busy" ? "blue" : "green"}>{a.status}</Badge></div><div className="automation-agent-actions">{canManage && <button type="button" className={`table-action icon-btn${a.isEnabled ? "" : " danger"}`} title={a.isEnabled ? "ปิดใช้งาน" : "เปิดใช้งาน"} aria-label={a.isEnabled ? "ปิดใช้งาน" : "เปิดใช้งาน"} onClick={() => onToggle(a, !a.isEnabled)}>{a.isEnabled ? "⏻" : "⏼"}</button>}{canManage && <button type="button" className="table-action danger icon-btn" title="ลบ Agent" aria-label="ลบ Agent" onClick={() => onDelete(a)}>🗑</button>}</div></div><b>{a.agentCode}</b><span>{a.machineName} · v{a.agentVersion}</span><small>{a.operatingSystem} · {a.architecture}</small><small>รองรับ {a.capabilities.join(" + ") || "-"}</small><time dateTime={a.lastHeartbeatAt}>ล่าสุด {formatThaiDateTime(a.lastHeartbeatAt)}</time></article>)}</div> : <div className="empty"><p>ยังไม่มี Agent ลงทะเบียน</p><small>ติดตั้งบนเครื่อง Windows: ตั้งค่า env แล้วรัน <code>agent\\run-agent.ps1</code> — Agent จะ register + ส่ง heartbeat อัตโนมัติ</small></div>}
  </section>;
}

function ExecutionTab({ jobs, executions, setExecDetail, execFilter, setExecFilter, canRun, onCancel, onRerun }: {
  jobs: AutomationJobItem[]; executions: AutomationExecutionItem[]; setExecDetail: (v: AutomationExecutionItem | null) => void;
  execFilter: string; setExecFilter: (v: string) => void; canRun: boolean; onCancel: (x: AutomationExecutionItem) => void; onRerun: (x: AutomationExecutionItem) => void;
}) {
  const [execSearch, setExecSearch] = useState("");
  const [execPage, setExecPage] = useState(1);
  const [jobPage, setJobPage] = useState(1);
  const pageSize = 15;

  const queuedJobs = jobs.filter((j) => j.status === "Queued");
  const filteredExec = executions.filter((x) => (execFilter === "all" || x.status === execFilter) && (!execSearch.trim() || x.automationCode.toLowerCase().includes(execSearch.trim().toLowerCase()) || (x.agentCode ?? "").toLowerCase().includes(execSearch.trim().toLowerCase())));
  const execPageCount = Math.max(1, Math.ceil(filteredExec.length / pageSize));
  const pagedExec = filteredExec.slice((execPage - 1) * pageSize, execPage * pageSize);
  const jobPageCount = Math.max(1, Math.ceil(jobs.length / pageSize));
  const pagedJobs = jobs.slice((jobPage - 1) * pageSize, jobPage * pageSize);
  const kpiRunning = executions.filter((e) => e.status === "Running").length;
  const kpiPassed = executions.filter((e) => e.status === "Passed").length;
  const kpiFailed = executions.filter((e) => e.status === "Failed").length;
  useEffect(() => setExecPage(1), [execSearch, execFilter]);
  useEffect(() => setJobPage(1), [jobs.length]);

  return <section className="automation-execution" aria-label="Automation Execution">
    <header className="automation-section-head"><div><h2>Execution Queue & Run History</h2><p>ติดตามงานที่ Agent รับไปรัน และผลลัพธ์ทั้งหมด — รองรับข้อมูลจำนวนมากด้วยค้นหา/กรอง/แบ่งหน้า</p></div></header>
    <div className="automation-kpis">
      <div><small>Queued</small><strong>{queuedJobs.length}</strong><span>รอ Agent รับ</span></div>
      <div><small>Running</small><strong>{kpiRunning}</strong><span>กำลังรัน</span></div>
      <div><small>Passed</small><strong>{kpiPassed}</strong><span>ผ่านทั้งหมด</span></div>
      <div className={kpiFailed ? "needs-review" : ""}><small>Failed</small><strong>{kpiFailed}</strong><span>ไม่ผ่าน</span></div>
      <div><small>Total</small><strong>{executions.length}</strong><span>ผลรัน</span></div>
    </div>
    <div className="automation-exec-grid">
      <article className="card">
        <div className="automation-section-head"><h3>Job Queue ({jobs.length})</h3><span className="muted-text">{queuedJobs.length} queued</span></div>
        {jobs.length ? <>
          <div className="automation-exec-list">{pagedJobs.map((j) => <div key={j.jobId} className="automation-queue-list"><article><div className="automation-queue-main"><Badge tone={jobStatusTone[j.status] ?? "blue"}>{j.status}</Badge><b>P{j.priority}</b><span>{j.assignedAgentCode ?? "รอ Agent"}</span></div><div><time dateTime={j.queuedAt}>{formatThaiDateTime(j.queuedAt)}</time>{j.lastError && <small className="queue-error">{j.lastError}</small>}</div></article></div>)}</div>
          <Pager page={jobPage} count={jobPageCount} total={jobs.length} pageSize={pageSize} onPrev={() => setJobPage((p) => Math.max(1, p - 1))} onNext={() => setJobPage((p) => Math.min(jobPageCount, p + 1))} />
        </> : <div className="empty"><p>ไม่มีงานในคิว</p></div>}
      </article>
      <article className="card">
        <div className="automation-section-head"><h3>Run History ({filteredExec.length})</h3></div>
        <div className="automation-run-toolbar">
          <input aria-label="ค้นหาด้วยรหัสหรือ Agent" placeholder="ค้นหา Code / Agent..." value={execSearch} onChange={(e) => setExecSearch(e.target.value)} />
          <select aria-label="กรองสถานะ" value={execFilter} onChange={(e) => setExecFilter(e.target.value)}>
            <option value="all">ทุกสถานะ</option>
            {["Passed", "Failed", "Running", "Queued", "Blocked", "Cancelled", "Timeout", "AgentLost"].map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>
        {pagedExec.length ? <div className="table-wrap"><table className="automation-exec-table"><thead><tr><th>Code</th><th>Target</th><th>Agent</th><th>Status</th><th>Duration</th><th>เวลา</th><th></th></tr></thead><tbody>{pagedExec.map((x) => <tr key={x.automationExecutionId} onClick={() => setExecDetail(x)} className="automation-exec-tr"><td><b>{x.automationCode}</b><small>Rev {x.versionNo} · {x.buildNumber}</small></td><td><Badge tone={x.targetApp === "Pos" ? "blue" : x.targetApp === "App" ? "purple" : "gray"}>{x.targetApp ?? "WindowsUI"}</Badge></td><td>{x.agentCode ?? "-"}</td><td><Badge tone={executionStatusTone[x.status] ?? "blue"}>{x.status}</Badge></td><td>{x.durationMs != null ? `${(x.durationMs / 1000).toFixed(1)}s` : "-"}</td><td>{formatThaiDateTime(x.completedAt ?? x.startedAt)}</td><td onClick={(e) => e.stopPropagation()}><div className="automation-row-actions"><button type="button" className="automation-more" title="ดูรายละเอียด" aria-label={`ดูรายละเอียด ${x.automationCode}`} onClick={() => setExecDetail(x)}>⋮</button>{canRun && x.status !== "Running" && x.status !== "Queued" && <button type="button" className="automation-more is-run" title="รันซ้ำ" aria-label={`รันซ้ำ ${x.automationCode}`} onClick={() => onRerun(x)}>▶</button>}{canRun && (x.status === "Running" || x.status === "Queued") && <button type="button" className="automation-more is-danger" title="ยกเลิก" aria-label={`ยกเลิก ${x.automationCode}`} onClick={() => onCancel(x)}>✕</button>}</div></td></tr>)}</tbody></table></div> : <div className="empty"><p>{execSearch || execFilter !== "all" ? "ไม่พบผลการรันที่ตรงเงื่อนไข" : "ยังไม่มีประวัติการรัน"}</p></div>}
        {filteredExec.length > pageSize && <Pager page={execPage} count={execPageCount} total={filteredExec.length} pageSize={pageSize} onPrev={() => setExecPage((p) => Math.max(1, p - 1))} onNext={() => setExecPage((p) => Math.min(execPageCount, p + 1))} />}
      </article>
    </div>
  </section>;
}
function Pager({ page, count, total, pageSize, onPrev, onNext }: { page: number; count: number; total: number; pageSize: number; onPrev: () => void; onNext: () => void }) {
  return <div className="automation-pager" role="navigation" aria-label="แบ่งหน้า">
    <button type="button" className="pager-btn" disabled={page <= 1} onClick={onPrev} aria-label="หน้าก่อนหน้า">‹ ก่อนหน้า</button>
    <span className="pager-info">หน้า {page} / {count} · {total.toLocaleString()} รายการ · {pageSize}/หน้า</span>
    <button type="button" className="pager-btn" disabled={page >= count} onClick={onNext} aria-label="หน้าถัดไป">ถัดไป ›</button>
  </div>;
}
