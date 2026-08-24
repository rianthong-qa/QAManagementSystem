import { useEffect, useMemo, useState } from "react";
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
  automationExecutionId: string; automationCaseId: string; automationCode: string; automationVersionId: string; versionNo: number; testExecutionId?: string; defectId?: string; targetApp?: string;
  agentId?: string; agentCode?: string; buildId: string; buildNumber: string; environmentId: string; environmentName: string; jobId?: string; status: string;
  startedAt?: string; completedAt?: string; durationMs?: number; failureType?: string; errorCode?: string; errorMessage?: string; stepResults: AutomationStepResultItem[];
  evidence?: AutomationEvidenceItem[];
};
type AutomationEvidenceItem = { automationEvidenceId: string; stepNo?: number; evidenceType: string; filePath: string; capturedBy?: string; capturedAt: string };
type TestCandidate = { testCaseId: string; testCaseCode: string; title: string; priority: string; status: string; moduleId: string };
type BuildOption = { buildId: string; buildNumber: string; applicationVersion?: string; status: string };
type EnvironmentOption = { testEnvironmentId: string; environmentName: string; isActive: boolean };

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

export function AutomationPage({
  projectId, releaseId, buildId, canEdit, canValidate, canApprove, canRun, canManage, canViewEvidence, canGenerateAi,
}: {
  projectId?: string; releaseId?: string; buildId?: string; canView: boolean; canEdit: boolean; canValidate: boolean; canApprove: boolean; canRun: boolean; canManage: boolean; canViewEvidence: boolean; canGenerateAi: boolean;
}) {
  const [tab, setTab] = useState("dashboard");
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
    if (!pid) { setCases([]); setObjects([]); setExecutions([]); return; }
    const h = { Authorization: `Bearer ${token()}` };
    setError("");
    Promise.all([
      fetch(`${apiUrl}/automation/cases?projectId=${pid}&take=200`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/objects?projectId=${pid}`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/jobs?projectId=${pid}${buildId ? `&buildId=${buildId}` : ""}&take=200`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/executions?projectId=${pid}${buildId ? `&buildId=${buildId}` : ""}&take=200`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/agents`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/actions`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
    ])
      .then(([c, o, j, e, a, ac]) => {
        setCases(Array.isArray(c) ? c : []);
        setObjects(Array.isArray(o) ? o : []);
        setJobs(Array.isArray(j) ? j : []);
        setExecutions(Array.isArray(e) ? e : []);
        setAgents(Array.isArray(a) ? a : []);
        setActions(Array.isArray(ac) ? ac : []);
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
    setCreatePick(null);
    setCreatePickSteps([]);
    setCreatedCaseId("");
    setNewVersionError("");
    setNewDsl(sampleDsl);
    setCreateSearch("");
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

  const pickCandidate = async (c: TestCandidate) => {
    setCreatePick(c);
    setCreatePickSteps([]);
    setCreatedCaseId("");
    setNewVersionError("");
    setNewDsl(sampleDsl);
    try {
      const r = await fetch(`${apiUrl}/test-cases/${c.testCaseId}`, { headers: { Authorization: `Bearer ${token()}` } });
      const d = r.ok ? await r.json() : null;
      setCreatePickSteps(Array.isArray(d?.steps) ? d.steps : []);
    } catch {
      setCreatePickSteps([]);
    }
  };

  const createCase = async (testCaseId: string) => {
    setCreateBusy(true);
    setError("");
    setNewVersionError("");
    try {
      const r = await fetch(`${apiUrl}/automation/cases?projectId=${pid}`, { method: "POST", headers, body: JSON.stringify({ testCaseId, automationType: "WindowsUI", ownerUserId: null }) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "สร้าง Automation Case ไม่สำเร็จ");
      }
      const created = await r.json();
      setCreatedCaseId(created.automationCaseId);
      setNotice("สร้าง Automation Case แล้ว — เขียนหรือ Generate DSL ต่อได้เลยในหน้านี้");
    } catch (e) {
      setError(e instanceof Error ? e.message : "สร้าง Automation Case ไม่สำเร็จ");
    } finally {
      setCreateBusy(false);
    }
  };

  const generateAiForNewCase = async () => {
    if (!createdCaseId) return;
    setCreateBusy(true);
    setNewVersionError("");
    try {
      const r = await fetch(`${apiUrl}/automation/cases/${createdCaseId}/generate?projectId=${pid}`, { method: "POST", headers });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "Generate AI ไม่สำเร็จ");
      }
      const v = await r.json();
      setNewDsl(v.dslJson);
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
    try {
      const r = await fetch(`${apiUrl}/automation/cases/${createdCaseId}/versions?projectId=${pid}`, { method: "POST", headers, body: JSON.stringify({ dslJson: newDsl, changeReason: "สร้างครั้งแรกจากหน้าสร้าง" }) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? "สร้าง Version ไม่สำเร็จ");
      }
      const v = await r.json();
      const vr = await fetch(`${apiUrl}/automation/versions/${v.automationVersionId}/validate?projectId=${pid}`, { method: "POST", headers });
      const vd = await vr.json();
      if (vd.validationStatus !== "Valid") throw new Error(vd.validationErrors || "Validate ไม่ผ่าน");
      setCreateModal(false);
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

  const workflowSteps = [
    { t: "สร้าง Automation Case", d: totalCandidates ? `มี ${totalCandidates} case` : "ยังไม่มี — สร้างจาก Test Case ที่เป็น Candidate", done: totalCandidates > 0, tab: "cases" },
    { t: "เขียน DSL / Generate AI", d: inProgress ? `มี ${inProgress} case กำลังเขียน DSL` : "DSL ครบแล้ว", done: inProgress === 0 && totalCandidates > 0, tab: "cases" },
    { t: "Validate + อนุมัติ → Ready", d: ready ? `Ready ${ready} case` : "ยังไม่มี case พร้อมรัน", done: ready > 0, tab: "cases" },
    { t: "รันผ่าน Agent", d: agentsOnline ? `${agentsOnline} agent online` : "ยังไม่มี agent online", done: agentsOnline > 0, tab: "suites" },
    { t: "ตรวจผล / Evidence / Defect", d: executions.length ? `รันแล้ว ${executions.length} ครั้ง` : "ยังไม่มีผลรัน", done: executions.length > 0, tab: "execution" },
  ] as { t: string; d: string; done: boolean; tab: string }[];
  const activeWorkflowStep = workflowSteps.findIndex((s) => !s.done);

  const nextActions: { text: string; btn: string; tab: string }[] = [];
  if (totalCandidates === 0) nextActions.push({ text: "ยังไม่มี Automation Case — เริ่มจากสร้าง Case จาก Test Case ที่เป็น Automation Candidate", btn: "สร้าง Automation Case", tab: "cases" });
  if (needsReview > 0) nextActions.push({ text: `มี ${needsReview} case ต้องตรวจสอบ DSL (AI ต้องการ Human Review) — เปิดรายละเอียดแล้ว Validate/อนุมัติ`, btn: "ไปตรวจ DSL", tab: "cases" });
  if (ready > 0 && agentsOnline === 0) nextActions.push({ text: "มี case พร้อมรัน แต่ยังไม่มี Agent Online — เริ่ม agent\\run-agent.ps1 บนเครื่องทดสอบ", btn: "ดู Agents", tab: "agents" });
  if (ready > 0 && agentsOnline > 0) nextActions.push({ text: `พร้อมรัน ${ready} case — เลือก Build/Environment แล้วรันเดี่ยวหรือรันเป็น Regression Suite`, btn: "ไป Regression Suites", tab: "suites" });
  if (failToday > 0) nextActions.push({ text: `มี Fail วันนี้ ${failToday} ครั้ง — ตรวจผล/Evidence และจำแนก Fail ก่อนสร้าง Defect`, btn: "ไป Execution", tab: "execution" });
  if (executions.length > 0 && failToday === 0) nextActions.push({ text: "ผลล่าสุดผ่านทั้งหมด — ดูประวัติและ Evidence ในหน้า Execution", btn: "ไป Execution", tab: "execution" });

  const tabs = [
    { id: "dashboard", label: "ภาพรวม", icon: "◉" },
    { id: "cases", label: "Automation Cases", icon: "▤" },
    { id: "execution", label: "Execution", icon: "▶" },
    { id: "manage", label: "การจัดการ", icon: "⚙" },
  ];

  return <article className="automation-page">
    {!pid ? <div className="empty"><p>เลือก Project เพื่อดู Automation Workspace</p></div> : <>
      <div className="automation-head">
        <div className="automation-head-title"><h1>Automation</h1><p>สร้าง Automation Case → เขียน/Generate DSL → Validate/อนุมัติ → รันผ่าน Agent → ตรวจผล</p></div>
        <div className="automation-head-status">
          <span className={agentsOnline ? "is-ready" : "is-warning"}><i />{agentsOnline} Agent Online</span>
          <span><i />{running} กำลังรัน</span>
          <span><i />{jobs.filter((j) => j.status === "Queued").length} ในคิว</span>
          {canEdit && <button className="btn primary" onClick={openCreate}>+ สร้าง Automation Case</button>}
        </div>
      </div>
      <nav className="automation-tabs" aria-label="Automation Module"><div className="automation-tabs-inner">{tabs.map((t) => <button key={t.id} type="button" className={tab === t.id ? "active" : ""} aria-current={tab === t.id ? "page" : undefined} onClick={() => setTab(t.id)}><span aria-hidden="true">{t.icon}</span>{t.label}</button>)}</div></nav>
      {error && <div className="inline-alert error"><span>{error}</span></div>}
      {notice && <div className="inline-alert success"><span>{notice}</span></div>}
      {tab === "dashboard" && <nav className="automation-steps" aria-label="ขั้นตอนการทำงาน Automation">{workflowSteps.map((s, i) => <button key={i} type="button" className={s.done ? "done" : i === activeWorkflowStep ? "active" : ""} aria-current={!s.done && i === activeWorkflowStep ? "step" : undefined} onClick={() => setTab(s.tab)}><span className="automation-step-no" aria-hidden="true">{s.done ? "✓" : String(i + 1)}</span><span className="automation-step-text"><b>{s.t}</b><small>{s.d}</small></span></button>)}</nav>}

      {tab === "dashboard" && <section className="automation-dashboard" aria-label="Automation Dashboard">
        <div className="automation-kpis">
          <div><small>Automation Cases</small><strong>{totalCandidates}</strong><span>ทั้งหมด</span></div>
          <div><small>Ready</small><strong>{ready}</strong><span>{coverage}% coverage</span></div>
          <div className={maintenance ? "needs-review" : ""}><small>Maintenance</small><strong>{maintenance}</strong><span>ต้องซ่อม DSL/Object</span></div>
          <div><small>Running</small><strong>{running}</strong><span>งานระหว่างรัน</span></div>
          <div><small>Pass</small><strong>{passToday}</strong><span>ผ่านแล้ว</span></div>
          <div><small>Fail</small><strong>{failToday}</strong><span>ไม่ผ่าน</span></div>
          <div><small>Agents Online</small><strong>{agentsOnline}/{agents.length}</strong><span>พร้อมรับงาน</span></div>
        </div>
        {nextActions.length > 0 && <section className="automation-next-steps" aria-label="ขั้นตอนถัดไป">
          <div className="automation-section-head"><h3>ขั้นตอนถัดไป</h3></div>
          {nextActions.map((a, i) => <div key={i} className="automation-next-step"><span className="automation-step-no" aria-hidden="true">{i + 1}</span><p>{a.text}</p><button className="btn" onClick={() => setTab(a.tab)}>{a.btn}</button></div>)}
        </section>}
        <div className="automation-dashboard-grid">
          <article className="card">
            <div className="automation-section-head"><h3>ผลการรันล่าสุด</h3><button className="btn" onClick={() => setTab("execution")}>ดูทั้งหมด</button></div>
            {executions.length ? <div className="automation-run-links">{executions.slice(0, 8).map((x) => <div key={x.automationExecutionId} className="automation-run-link"><span className="automation-run-link-main"><b>{x.automationCode}</b><Badge tone={executionStatusTone[x.status] ?? "blue"}>{x.status}</Badge><time>{formatThaiDateTime(x.completedAt ?? x.startedAt)}</time></span><button type="button" className="table-action" onClick={() => setExecDetail(x)}>ดูรายละเอียด</button></div>)}</div> : <div className="empty"><p>ยังไม่มีประวัติการรัน</p><small>สร้าง Automation Case แล้วรันผ่าน Agent — ผลจะแสดงที่นี่</small><button className="btn" onClick={() => setTab("cases")}>ไปสร้าง Automation Case</button></div>}
          </article>
        </div>
      </section>}

      {tab === "cases" && <section className="automation-cases" aria-label="Automation Cases">
        <header className="automation-section-head"><div><h2>Automation Cases</h2><p>หนึ่ง Test Case → หนึ่ง Automation Case พร้อม Version (DSL) หลายเวอร์ชัน</p></div><div className="automation-cases-actions">{canRun && <button className="btn" onClick={() => setBatchModal(true)}>▶ รันเป็นกลุ่ม</button>}{canEdit && <button className="btn primary" onClick={openCreate}>+ สร้าง Automation Case</button>}</div></header>
        {cases.length ? <div className="table-wrap"><table><thead><tr><th>Code</th><th>Test Case</th><th>Target App</th><th>Status</th><th>Version</th><th>Owner</th><th></th></tr></thead><tbody>{cases.map((c) => <tr key={c.automationCaseId}><td><b>{c.automationCode}</b></td><td><span>{c.testCaseCode}</span><small>{c.testCaseTitle}</small></td><td><Badge tone={targetTone[c.automationType] ?? "blue"}>{c.automationType}</Badge></td><td><Badge tone={caseStatusTone[c.status] ?? "blue"}>{c.status}</Badge></td><td>Rev {c.currentVersionNo}</td><td>{c.ownerName ?? "-"}</td><td><button className="table-action" onClick={() => openCase(c)}>รายละเอียด</button></td></tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Automation Case</p><small>สร้างจาก Test Case ที่เป็น Automation Candidate — จากนั้นเขียน DSL / Generate AI → Validate → อนุมัติ → พร้อมรัน</small>{canEdit && <button className="btn primary" onClick={openCreate}>+ สร้าง Automation Case</button>}</div>}
      <div className="automation-status-legend" role="note" aria-label="ความหมายสถานะ"><span><i className="legend-dot legend-draft" />Draft — ยังไม่มี DSL</span><span><i className="legend-dot legend-review" />NeedsReview — AI สร้างแล้ว รอตรวจ</span><span><i className="legend-dot legend-ready" />Ready — พร้อมรัน</span><span><i className="legend-dot legend-maint" />MaintenanceRequired — ต้องซ่อม DSL/Object</span></div>
      </section>}

      {tab === "manage" && <section className="automation-manage" aria-label="Automation จัดการ">
        <nav className="automation-subtabs" aria-label="จัดการ"><button type="button" className={manageTab === "actions" ? "active" : ""} onClick={() => setManageTab("actions")}>Action Library</button><button type="button" className={manageTab === "objects" ? "active" : ""} onClick={() => setManageTab("objects")}>Object Repository</button><button type="button" className={manageTab === "agents" ? "active" : ""} onClick={() => setManageTab("agents")}>Agents</button></nav>
        {manageTab === "actions" && <ActionLibraryTab actions={actions} canManage={canManage} headers={headers} onReload={() => setReload((x) => x + 1)} onError={setError} actionModal={actionModal} setActionModal={setActionModal} />}
        {manageTab === "objects" && <ObjectRepositoryTab projectId={pid} objects={objects} canManage={canManage} headers={headers} onReload={() => setReload((x) => x + 1)} onError={setError} objectModal={objectModal} setObjectModal={setObjectModal} />}
        {manageTab === "agents" && <AgentsSection agents={agents} agentsOnline={agentsOnline} canManage={canManage} onToggle={toggleAgent} onDelete={deleteAgent} />}
      </section>}

      {tab === "execution" && <ExecutionTab jobs={jobs} executions={executions} setExecDetail={setExecDetail} />}
    </>}

    {createModal && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-create-title" onMouseDown={() => !createBusy && setCreateModal(false)}><div className="modal-box automation-create-modal" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2 id="automation-create-title">สร้าง Automation Case</h2><small>เลือก Test Case ทางซ้าย → สร้าง Case + เขียน DSL ทางขวา จบในหน้าเดียว</small></div><button aria-label="ปิด" disabled={createBusy} onClick={() => setCreateModal(false)}>×</button></div>
      <div className="automation-create-body">
        <div className="automation-create-left">
          <div className="automation-create-step-title"><span className="automation-step-no" aria-hidden="true">1</span><b>เลือก Test Case</b></div>
          <div className="automation-create-filters">
            {createModules.length > 0 && <select aria-label="กรอง Module" value={createModuleFilter} onChange={(e) => setCreateModuleFilter(e.target.value)}><option value="">ทุก Module</option>{moduleTreeOptions(createModules)}</select>}
            <input aria-label="ค้นหา Test Case" placeholder="ค้นหา Code / ชื่อ..." value={createSearch} onChange={(e) => setCreateSearch(e.target.value)} />
          </div>
          {(() => { const q = createSearch.trim().toLowerCase(); const list = (createModuleFilter ? candidates.filter((c) => c.moduleId === createModuleFilter) : candidates).filter((c) => !q || c.testCaseCode.toLowerCase().includes(q) || c.title.toLowerCase().includes(q)); return list.length ? <div className="automation-candidate-pick">{list.map((c) => { const has = existingTestCaseIds.has(c.testCaseId); return <button key={c.testCaseId} type="button" className={"automation-candidate-row" + (has ? " is-taken" : "") + (createPick?.testCaseId === c.testCaseId ? " is-selected" : "")} disabled={createBusy || has} title={has ? "Test Case นี้มี Automation Case แล้ว" : undefined} onClick={() => pickCandidate(c)}><b>{c.testCaseCode}</b><span>{c.title}</span>{has ? <Badge tone="gray">มี Case แล้ว</Badge> : <Badge tone={c.priority === "P0" ? "red" : "blue"}>{c.priority}</Badge>}</button>; })}</div> : <div className="empty"><p>ไม่พบ Test Case ที่ตรงเงื่อนไข</p><small>เปิดหน้า Test Case และทำเครื่องหมาย Automation Candidate ก่อน</small></div>; })()}
        </div>
        <div className="automation-create-right">
          {!createPick ? <div className="empty"><p>ยังไม่ได้เลือก Test Case</p><small>คลิก Test Case ทางซ้ายเพื่อเริ่ม</small></div> : <>
            <div className="automation-create-step-title"><span className="automation-step-no" aria-hidden="true">2</span><b>สร้าง Case + เขียน DSL</b></div>
            {!createdCaseId && <div className="automation-create-pick"><div className="automation-create-pick-info"><b>{createPick.testCaseCode}</b><span>{createPick.title}</span></div><button className="btn primary" disabled={createBusy} onClick={() => createCase(createPick.testCaseId)}>{createBusy ? "กำลังสร้าง..." : "สร้าง Automation Case"}</button></div>}
            {createPickSteps.length > 0 && <details className="automation-create-steps"><summary>ดูขั้นตอนของ Test Case ({createPickSteps.length})</summary><ol>{createPickSteps.map((s) => <li key={s.stepNo}><b>{s.stepNo}. {s.action}</b>{s.testData ? <span>ข้อมูล: {s.testData}</span> : null}<span>คาดหวัง: {s.expectedResult}</span></li>)}</ol></details>}
            {createdCaseId && <>
              {newVersionError && <div className="inline-alert error"><span>{newVersionError}</span></div>}
              <div className="automation-version-create-actions">
                {canGenerateAi && <button type="button" className="btn primary" disabled={createBusy} onClick={generateAiForNewCase}>{createBusy ? "AI กำลังสร้าง..." : "✦ Generate AI"}</button>}
                <button type="button" className="btn" disabled={createBusy} onClick={() => setNewDsl(sampleDsl)}>โหลดตัวอย่าง</button>
                <button type="button" className="btn" disabled={createBusy || !newDsl.trim()} onClick={createNewVersionAndValidate}>{createBusy ? "กำลังบันทึก..." : "สร้าง Version + Validate"}</button>
              </div>
              <textarea rows={10} value={newDsl} onChange={(e) => setNewDsl(e.target.value)} spellCheck={false} aria-label="DSL JSON" />
              <p className="muted-text">Validate ผ่าน = ครบขั้นตอน — ไปที่ Automation Cases เพื่อ ตรวจ/อนุมัติ/สั่งรัน ต่อ</p>
            </>}
          </>}
        </div>
      </div>
      <div className="modal-actions"><button className="btn" disabled={createBusy} onClick={() => setCreateModal(false)}>ปิด</button></div>
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

function ExecutionTab({ jobs, executions, setExecDetail }: {
  jobs: AutomationJobItem[]; executions: AutomationExecutionItem[]; setExecDetail: (v: AutomationExecutionItem | null) => void;
}) {
  const [execSearch, setExecSearch] = useState("");
  const [execFilter, setExecFilter] = useState("all");
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
        {pagedExec.length ? <div className="table-wrap"><table className="automation-exec-table"><thead><tr><th>Code</th><th>Target</th><th>Agent</th><th>Status</th><th>Duration</th><th>เวลา</th><th></th></tr></thead><tbody>{pagedExec.map((x) => <tr key={x.automationExecutionId} onClick={() => setExecDetail(x)} className="automation-exec-tr"><td><b>{x.automationCode}</b><small>Rev {x.versionNo} · {x.buildNumber}</small></td><td><Badge tone={x.targetApp === "Pos" ? "blue" : x.targetApp === "App" ? "purple" : "gray"}>{x.targetApp ?? "WindowsUI"}</Badge></td><td>{x.agentCode ?? "-"}</td><td><Badge tone={executionStatusTone[x.status] ?? "blue"}>{x.status}</Badge></td><td>{x.durationMs != null ? `${(x.durationMs / 1000).toFixed(1)}s` : "-"}</td><td>{formatThaiDateTime(x.completedAt ?? x.startedAt)}</td><td><button className="table-action" onClick={(e) => { e.stopPropagation(); setExecDetail(x); }}>ดู</button></td></tr>)}</tbody></table></div> : <div className="empty"><p>{execSearch || execFilter !== "all" ? "ไม่พบผลการรันที่ตรงเงื่อนไข" : "ยังไม่มีประวัติการรัน"}</p></div>}
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
