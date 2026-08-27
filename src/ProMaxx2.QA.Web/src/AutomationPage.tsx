import { Fragment, useEffect, useMemo, useState } from "react";
import { formatThaiDateTime } from "./dateTime";
import {
  automationCaseTone as caseStatusTone,
  automationExecutionTone as executionStatusTone,
  automationJobTone as jobStatusTone,
  automationVersionTone as versionStatusTone,
  automationVerificationTone as verificationStatusTone,
  automationCoverage,
  parseDslSteps,
  buildObjectKey,
} from "./automationUtils";

const apiUrl = import.meta.env.VITE_API_URL ?? "/api/v1";
const token = () => localStorage.getItem("qa.accessToken");

type AutomationCaseItem = {
  automationCaseId: string; testCaseId: string; testCaseCode: string; testCaseTitle: string; automationCode: string;
  automationType: string; status: string; currentVersionNo: number; versionCount: number; ownerUserId?: string; ownerName?: string; isAiGenerated: boolean; createdAt: string;
  maintenanceReason?: string; maintenanceOwnerUserId?: string; maintenanceOpenedAt?: string;
  isQuarantined?: boolean; quarantineReason?: string; quarantineOwnerUserId?: string; quarantineExpiresAt?: string;
};
type AutomationVersionItem = {
  automationVersionId: string; automationCaseId: string; versionNo: number; testCaseRevisionNo: number; dslVersion: string; dslJson: string;
  generatedByAi: boolean; aiProvider?: string; aiModel?: string; aiConfidence?: number; validationStatus: string; validationErrors?: string;
  approvedBy?: string; approvedAt?: string; changeReason?: string; createdAt: string;
};
type AutomationActionItem = {
  automationActionId: string; actionCode: string; actionName: string; category: string; description?: string; parameterSchemaJson: string;
  handlerKey: string; minimumAgentVersion?: string; isActive: boolean; retrySafety: string;
};
type AutomationObjectItem = {
  automationObjectId: string; projectId: string; moduleId?: string; moduleCode?: string; moduleName?: string; applicationCode: string;
  screenCode: string; objectCode: string; objectName: string; controlType: string; automationId?: string; selectorJson: string; objectVersion: number; isActive: boolean;
};
type AutomationObjectImportDraft = {
  clientId: string; moduleId?: string; applicationCode: string; screenCode: string; objectCode: string; objectName: string; controlType: string; automationId?: string; selectorJson: string;
  status: "Ready" | "DuplicateKey" | "DuplicateAutomationId" | "Invalid"; message: string;
};
type AutomationObjectImportResult = { imported: number; skipped: number; rows: { businessKey: string; automationId?: string; status: string; message: string }[] };
type AutomationObjectVerificationItem = {
  automationObjectVerificationId: string; automationObjectId: string; objectCode: string; screenCode: string; expectedAutomationId?: string; expectedControlType: string;
  actualAutomationId?: string; actualControlType?: string; status: string; assignedAgentId?: string; assignedAgentCode?: string; requestedAt: string; completedAt?: string; message?: string;
};
type AutomationAgentItem = {
  agentId: string; agentCode: string; machineName: string; agentVersion: string; operatingSystem: string; architecture: string; status: string;
  lastHeartbeatAt: string; currentExecutionId?: string; registeredAt: string; isEnabled: boolean; connectivity: string; capabilities: string[];
};
type AutomationJobItem = {
  jobId: string; automationExecutionId: string; priority: number; requestedAgentId?: string; assignedAgentId?: string; assignedAgentCode?: string;
  status: string; queuedAt: string; assignedAt?: string; startedAt?: string; completedAt?: string; retryCount: number; lastError?: string;
};
type FlakyCandidateItem = { automationCaseId: string; automationCode: string; recentRuns: number; transitions: number; lastExecutedAt: string };
type AutomationSuiteCaseItem = { automationCaseId: string; automationCode: string; testCaseCode: string; testCaseTitle: string; automationType: string; status: string; sortOrder: number; isRequired: boolean };
type AutomationSuiteListItem = { automationSuiteId: string; projectId: string; suiteCode: string; suiteName: string; description?: string; isActive: boolean; createdAt: string; closedAt?: string; revisionNo: number; caseCount: number; readyCaseCount: number };
type AutomationSuiteDetailItem = { automationSuiteId: string; projectId: string; suiteCode: string; suiteName: string; description?: string; isActive: boolean; createdBy?: string; createdAt: string; updatedAt?: string; closedAt?: string; revisionNo: number; cases: AutomationSuiteCaseItem[] };
type AutomationSuiteRevisionItem = { automationSuiteRevisionId: string; revisionNo: number; changeType: string; detail?: string; changeReason?: string; changedBy?: string; changedByName?: string; changedAt: string };
type AutomationScheduleListItem = {
  automationScheduleId: string; projectId: string; automationSuiteId: string; suiteCode: string; suiteName: string; name: string; description?: string;
  frequency: string; daysOfWeekMask: number; runAtTime: string; onceOnDate?: string; timeZoneId: string; buildNumber: string; environmentName: string; isActive: boolean; nextRunAtUtc: string; lastRunAtUtc?: string; createdAt: string;
};
type AutomationScheduleDetailItem = {
  automationScheduleId: string; projectId: string; automationSuiteId: string; suiteCode: string; suiteName: string; name: string; description?: string;
  frequency: string; daysOfWeekMask: number; runAtTime: string; onceOnDate?: string; timeZoneId: string; buildId: string; buildNumber: string; environmentId: string; environmentName: string;
  agentId?: string; agentCode?: string; priority: number; isActive: boolean; nextRunAtUtc: string; lastRunAtUtc?: string; createdBy?: string; createdAt: string; updatedAt?: string;
};
type AutomationScheduleRunItem = { automationScheduleRunId: string; automationScheduleId: string; firedAtUtc: string; status: string; executionsCreated: number; skippedCount: number; errorMessage?: string };
type AutomationScheduleNotificationItem = {
  automationScheduleNotificationId: string; projectId: string; automationScheduleId: string; scheduleName: string;
  automationExecutionId: string; automationCode: string; eventType: string; message: string; createdAtUtc: string; isRead: boolean; readAtUtc?: string;
};
type AutomationBuildTriggerPolicyItem = {
  automationBuildTriggerPolicyId: string; projectId: string; automationSuiteId: string; suiteCode: string; suiteName: string; pack: string;
  environmentId: string; environmentName: string; agentId?: string; agentCode?: string; priority: number; isActive: boolean; createdAt: string; updatedAt?: string;
};
type AutomationBuildTriggerRunItem = { automationBuildTriggerRunId: string; automationBuildTriggerPolicyId: string; buildId: string; buildNumber: string; firedAtUtc: string; status: string; executionsCreated: number; skippedCount: number; errorMessage?: string };
type AutomationWebhookTokenItem = { automationWebhookTokenId: string; projectId: string; name: string; tokenPrefix: string; isActive: boolean; lastUsedAtUtc?: string; createdBy?: string; createdAt: string; revokedAt?: string };
type AutomationWebhookDeliveryItem = { automationWebhookDeliveryId: string; projectId: string; automationWebhookTokenId: string; tokenName: string; requestId: string; receivedAtUtc: string; buildId?: string; buildNumber?: string; status: string; errorMessage?: string };
type AutomationDbSnapshotItem = {
  automationDbSnapshotId: string; projectId: string; environmentId: string; environmentName: string; buildId: string; buildNumber: string;
  status: string; dbKind?: string; agentId?: string; agentCode?: string; snapshotPath?: string; checksum?: string; sizeBytes?: number; errorMessage?: string;
  requestedBy?: string; requestedAt: string; startedAt?: string; completedAt?: string;
};
type AutomationDbRestoreItem = {
  automationDbRestoreId: string; projectId: string; automationDbSnapshotId: string; environmentId: string; environmentName: string; buildId: string; buildNumber: string;
  status: string; agentId?: string; agentCode?: string; checksumVerified: boolean; availabilityVerified: boolean; errorMessage?: string;
  requestedBy?: string; requestedAt: string; startedAt?: string; completedAt?: string;
};
type AutomationDataSeedScriptListItem = { automationDataSeedScriptId: string; projectId: string; name: string; description?: string; scriptType: string; dbKind: string; isActive: boolean; approvalStatus: string; createdAt: string };
type AutomationDataSeedScriptDetailItem = {
  automationDataSeedScriptId: string; projectId: string; name: string; description?: string; scriptType: string; dbKind: string; sqlScript: string; isActive: boolean;
  approvalStatus: string; reviewedBy?: string; reviewedAt?: string; rejectionReason?: string; createdBy?: string; createdAt: string; updatedAt?: string;
};
type AutomationDataSeedRunItem = {
  automationDataSeedRunId: string; projectId: string; automationDataSeedScriptId: string; scriptName: string; scriptType: string; environmentId: string; environmentName: string; buildId: string; buildNumber: string;
  status: string; agentId?: string; agentCode?: string; rowsAffected?: number; errorMessage?: string; requestedBy?: string; requestedAt: string; startedAt?: string; completedAt?: string;
};
type RetryPolicyItem = { maxAttempts: number; backoffSeconds: number; enabled: boolean; updatedAt?: string };
type CountByKeyItem = { key: string; count: number };
type FailureBreakdownItem = { totalFailed: number; byFailureType: CountByKeyItem[]; byBuild: CountByKeyItem[]; byAgent: CountByKeyItem[]; byAutomationCase: CountByKeyItem[] };
type AutomationStepResultItem = {
  automationStepResultId: string; stepNo: number; actionCode: string; status: string; startedAt: string; completedAt: string; durationMs: number;
  actualResult?: string; errorCode?: string; errorMessage?: string; evidencePath?: string;
};
type AutomationExecutionItem = {
  automationExecutionId: string; automationCaseId: string; automationCode: string; testCaseCode?: string; testCaseTitle?: string; automationVersionId: string; versionNo: number; testExecutionId?: string; defectId?: string; targetApp?: string;
  agentId?: string; agentCode?: string; buildId: string; buildNumber: string; environmentId: string; environmentName: string; jobId?: string; status: string;
  startedAt?: string; completedAt?: string; durationMs?: number; failureType?: string; errorCode?: string; errorMessage?: string; stepResults: AutomationStepResultItem[];
  evidence?: AutomationEvidenceItem[];
  classifiedFailureType?: string; classifiedRecommendation?: string; retryOfExecutionId?: string; retryCount?: number;
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

const parseAutomationObjectImport = (text: string): Omit<AutomationObjectImportDraft, "clientId" | "status" | "message">[] => {
  const clean = text.trim();
  if (!clean) return [];
  if (clean.startsWith("[") || clean.startsWith("{")) {
    const raw = JSON.parse(clean);
    const rows = Array.isArray(raw) ? raw : Array.isArray(raw.objects) ? raw.objects : [raw];
    return rows.map((x: Record<string, unknown>) => ({
      moduleId: typeof x.moduleId === "string" ? x.moduleId : undefined,
      applicationCode: String(x.applicationCode ?? x.app ?? "Promaxx2"),
      screenCode: String(x.screenCode ?? x.screen ?? "Default"),
      objectCode: String(x.objectCode ?? x.name ?? x.automationId ?? ""),
      objectName: String(x.objectName ?? x.name ?? x.objectCode ?? x.automationId ?? ""),
      controlType: String(x.controlType ?? x.type ?? "Button"),
      automationId: x.automationId == null ? undefined : String(x.automationId),
      selectorJson: typeof x.selectorJson === "string" ? x.selectorJson : JSON.stringify(x.selector ?? { automationId: x.automationId ?? "" }),
    }));
  }
  const lines = clean.split(/\r?\n/).map((x) => x.trim()).filter(Boolean);
  const header = lines[0].split(",").map((x) => x.trim().toLowerCase());
  const hasHeader = header.some((x) => ["applicationcode", "screencode", "objectcode", "objectname", "controltype", "automationid"].includes(x));
  const dataLines = hasHeader ? lines.slice(1) : lines;
  return dataLines.map((line) => {
    const cols = line.split(",").map((x) => x.trim());
    const get = (name: string, index: number) => hasHeader ? cols[header.indexOf(name.toLowerCase())] : cols[index];
    const applicationCode = get("applicationCode", 0) || "Promaxx2";
    const screenCode = get("screenCode", 1) || "Default";
    const objectCode = get("objectCode", 2) || get("automationId", 5) || "";
    const objectName = get("objectName", 3) || objectCode;
    const controlType = get("controlType", 4) || "Button";
    const automationId = get("automationId", 5) || undefined;
    return { applicationCode, screenCode, objectCode, objectName, controlType, automationId, selectorJson: JSON.stringify({ automationId: automationId ?? "" }) };
  });
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

  const [flakyCandidates, setFlakyCandidates] = useState<FlakyCandidateItem[]>([]);
  const [retryPolicy, setRetryPolicy] = useState<RetryPolicyItem | null>(null);
  const [maintenanceBusy, setMaintenanceBusy] = useState(false);
  const [maintenanceOwnerInput, setMaintenanceOwnerInput] = useState("");
  const [maintenanceNote, setMaintenanceNote] = useState("");
  const [quarantineModalFor, setQuarantineModalFor] = useState<FlakyCandidateItem | null>(null);

  const pid = projectId ?? "";

  useEffect(() => {
    if (!pid) { setCases([]); setObjects([]); setExecutions([]); setDash(null); return; }
    const h = { Authorization: `Bearer ${token()}` };
    setError("");
    // Cases/jobs/executions here deliberately stay a flat "up to 200" load (AUT-P2-001 kept this shared, cross-
    // cutting fetch as-is) — it feeds dashboard KPIs, CSV export, and the batch-run/suite case pickers, none of
    // which need true pagination. The three endpoints now always return a PagedResult ({total, rows}) — see
    // AutomationCasesTab/ExecutionTab below for the components that fetch real server-paginated pages of their own.
    Promise.all([
      fetch(`${apiUrl}/automation/cases?projectId=${pid}&page=1&size=200`, { headers: h }).then((r) => (r.ok ? r.json() : { rows: [] })),
      fetch(`${apiUrl}/automation/objects?projectId=${pid}`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/jobs?projectId=${pid}${buildId ? `&buildId=${buildId}` : ""}&page=1&size=200`, { headers: h }).then((r) => (r.ok ? r.json() : { rows: [] })),
      fetch(`${apiUrl}/automation/executions?projectId=${pid}${buildId ? `&buildId=${buildId}` : ""}&page=1&size=200`, { headers: h }).then((r) => (r.ok ? r.json() : { rows: [] })),
      fetch(`${apiUrl}/automation/agents`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/actions`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/dashboard?projectId=${pid}`, { headers: h }).then((r) => (r.ok ? r.json() : null)),
      fetch(`${apiUrl}/automation/cases/flaky-candidates?projectId=${pid}`, { headers: h }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/automation/settings/retry-policy`, { headers: h }).then((r) => (r.ok ? r.json() : null)),
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
        setRetryPolicy(rp && typeof rp === "object" ? rp : null);
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
  const casePageCount = Math.max(1, Math.ceil(casesPaged.total / casePageSize));
  useEffect(() => setCasePage(1), [headSearch, caseStatusFilter, caseTargetFilter, caseSortBy]);
  useEffect(() => {
    if (!pid) { setCasesPaged({ total: 0, rows: [] }); return; }
    const qs = new URLSearchParams({ projectId: pid, page: String(casePage), size: String(casePageSize), sortBy: caseSortBy });
    if (headSearch.trim()) qs.set("search", headSearch.trim());
    if (caseStatusFilter !== "all") qs.set("status", caseStatusFilter);
    if (caseTargetFilter !== "all") qs.set("automationTarget", caseTargetFilter);
    fetch(`${apiUrl}/automation/cases?${qs}`, { headers })
      .then((r) => (r.ok ? r.json() : { total: 0, rows: [] }))
      .then((d) => setCasesPaged(d && typeof d === "object" && Array.isArray(d.rows) ? d : { total: 0, rows: [] }))
      .catch(() => setCasesPaged({ total: 0, rows: [] }));
  }, [pid, casePage, casePageSize, headSearch, caseStatusFilter, caseTargetFilter, caseSortBy, headers, reload]);

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
    { id: "suites", label: "Automation Suite", icon: "▶" },
    { id: "schedules", label: "Schedule", icon: "◷" },
    { id: "buildTriggers", label: "Build Trigger", icon: "⚡" },
    { id: "webhooks", label: "Webhook", icon: "🔗" },
    { id: "dataSnapshots", label: "DB Snapshot", icon: "💾" },
    { id: "dataSeeds", label: "Seed & Cleanup", icon: "🌱" },
    { id: "dataProfiles", label: "Environment Data Profile", icon: "🗂" },
    { id: "execution", label: "Execution", icon: "▶" },
    { id: "failures", label: "Failure Dashboard", icon: "!" },
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
        {flakyCandidates.length > 0 && <section className="automation-failure-analysis" aria-label="Flaky Candidates">
          <div className="automation-section-head"><h3>Flaky Candidates (AUT-P0-010)</h3><span className="muted-text">Pass/Fail สลับกันบ่อยใน execution ล่าสุด</span></div>
          <div className="automation-result-list">{flakyCandidates.map((f) => <div key={f.automationCaseId} className="automation-failure-row">
            <b>{f.automationCode}</b><span>{f.transitions} transitions / {f.recentRuns} runs</span><span>ล่าสุด {formatThaiDateTime(f.lastExecutedAt)}</span>
            {canManage && <button type="button" className="table-action" onClick={() => setQuarantineModalFor(f)}>Quarantine</button>}
          </div>)}</div>
        </section>}
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
            <select aria-label="เรียงตาม" value={caseSortBy} onChange={(e) => setCaseSortBy(e.target.value)}>
              <option value="created">ล่าสุดก่อน</option>
              <option value="code">Code (A→Z)</option>
              <option value="status">สถานะ</option>
            </select>
            {(caseStatusFilter !== "all" || caseTargetFilter !== "all" || headSearch.trim()) && <button type="button" className="table-action" onClick={() => { setCaseStatusFilter("all"); setCaseTargetFilter("all"); setHeadSearch(""); }}>ล้างตัวกรอง</button>}
          </div>
          {(headSearch.trim() || caseStatusFilter !== "all" || caseTargetFilter !== "all") && <div className="automation-search-hint">แสดง {casesPaged.total.toLocaleString()} รายการที่ตรงเงื่อนไข{headSearch.trim() ? ` · ค้นหา "${headSearch}"` : ""}{caseStatusFilter !== "all" ? ` · สถานะ ${caseStatusFilter}` : ""}{caseTargetFilter !== "all" ? ` · Target ${caseTargetFilter}` : ""} — <button type="button" className="table-action" onClick={() => { setHeadSearch(""); setCaseStatusFilter("all"); setCaseTargetFilter("all"); }}>ล้างทั้งหมด</button></div>}
          {casesPaged.rows.length ? <div className="table-wrap"><table><thead><tr><th>Code</th><th>Test Case</th><th>Target App</th><th>Status</th><th>Version</th><th>Owner</th><th></th></tr></thead><tbody>{casesPaged.rows.map((c) => <tr key={c.automationCaseId}><td><b>{c.automationCode}</b></td><td><span>{c.testCaseCode}</span><small>{c.testCaseTitle}</small></td><td><Badge tone={targetTone[c.automationType] ?? "blue"}>{c.automationType}</Badge></td><td><Badge tone={caseStatusTone[c.status] ?? "blue"}>{c.status}</Badge>{c.isQuarantined && <Badge tone="orange">Quarantined</Badge>}</td><td>Rev {c.currentVersionNo}</td><td>{c.ownerName ?? "-"}</td><td><button className="table-action" onClick={() => openCase(c)}>รายละเอียด</button></td></tr>)}</tbody></table></div>
            : <div className="empty"><p>ไม่พบ Automation Case ที่ตรงเงื่อนไข</p><small>ลองเปลี่ยนคำค้นหาหรือตัวกรองด้านบน</small></div>}
          {casesPaged.total > casePageSize && <Pager page={casePage} count={casePageCount} total={casesPaged.total} pageSize={casePageSize} onPrev={() => setCasePage((p) => Math.max(1, p - 1))} onNext={() => setCasePage((p) => Math.min(casePageCount, p + 1))} />}
        </> : <div className="empty"><p>ยังไม่มี Automation Case</p><small>สร้างจาก Test Case ที่เป็น Automation Candidate — จากนั้นเขียน DSL / Generate AI → Validate → อนุมัติ → พร้อมรัน</small>{canEdit && <button className="btn primary" onClick={openCreate}>+ สร้าง Automation Case</button>}</div>}
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
        {manageTab === "agents" && <AgentsSection agents={agents} agentsOnline={agentsOnline} canManage={canManage} onToggle={toggleAgent} onDelete={deleteAgent} />}
        {manageTab === "retry" && <RetryPolicyTab policy={retryPolicy} canManage={canManage} busy={maintenanceBusy} onSave={updateRetryPolicy} />}
      </section>}

      {tab === "execution" && <ExecutionTab projectId={pid} buildId={buildId} releaseId={releaseId} agents={agents} headers={headers} jobs={jobs} executions={executions} setExecDetail={setExecDetail} execFilter={execFilter} setExecFilter={setExecFilter} canRun={canRun} onCancel={cancelExecution} onRerun={rerunExecution} reload={reload} />}

      {tab === "failures" && <FailureDashboardTab projectId={pid} releaseId={releaseId} agents={agents} headers={headers} setExecDetail={setExecDetail} />}
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

      {selectedCase.status === "MaintenanceRequired" && <section className="automation-failure-analysis" aria-label="Maintenance Repair">
        <div className="automation-section-head"><h3>Maintenance Repair (AUT-P0-007)</h3></div>
        {selectedCase.maintenanceReason && <div className="inline-alert error"><span>สาเหตุ: {selectedCase.maintenanceReason}</span></div>}
        <p className="muted-text">เปิดตั้งแต่ {formatThaiDateTime(selectedCase.maintenanceOpenedAt)}{selectedCase.maintenanceOwnerUserId ? ` · ผู้รับผิดชอบ: ${selectedCase.maintenanceOwnerUserId}` : " · ยังไม่ได้มอบหมายผู้รับผิดชอบ"}</p>
        {canEdit && <>
          <div className="form-grid">
            <label>User Id ผู้รับผิดชอบ<input value={maintenanceOwnerInput} onChange={(e) => setMaintenanceOwnerInput(e.target.value)} placeholder="ระบุ User Id" /></label>
          </div>
          <div className="automation-failure-actions">
            <button type="button" className="btn" disabled={maintenanceBusy || !maintenanceOwnerInput.trim()} onClick={assignMaintenanceOwner}>รับผิดชอบซ่อม</button>
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
        {canManage && <div className="automation-failure-actions"><button type="button" className="btn" disabled={maintenanceBusy} onClick={() => unquarantineCase(selectedCase.automationCaseId)}>Unquarantine</button></div>}
      </section>}

      <VersionEditor selectedCase={selectedCase} versions={versions} canEdit={canEdit} canValidate={canValidate} canApprove={canApprove} canRun={canRun} canGenerateAi={canGenerateAi} createBusy={createBusy} versionError={versionError} onCreate={createVersion} onValidate={validateVersion} onApprove={approveVersion} onRun={openRun} onGenerateAi={generateAi} />
      <div className="modal-actions"><button className="btn primary" onClick={() => setSelectedCase(null)}>ปิด</button></div>
    </div></div>}

    {runModal && selectedCase && <RunModal item={selectedCase} versions={versions} builds={builds} environments={environments} agents={agents} busy={createBusy} onClose={() => setRunModal(false)} onRun={runCase} />}
    {batchModal && <BatchRunModal cases={cases} releaseId={releaseId} canRun={canRun} busy={createBusy} onClose={() => setBatchModal(false)} onRunBatch={runBatch} onError={setError} />}
    {quarantineModalFor && <QuarantineModal candidate={quarantineModalFor} busy={maintenanceBusy} onClose={() => setQuarantineModalFor(null)} onConfirm={quarantineCase} />}
    {execDetail && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-exec-detail-title" onMouseDown={() => setExecDetail(null)}><div className="modal-box automation-exec-detail" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2 id="automation-exec-detail-title">{execDetail.automationCode} · Execution</h2><small>Build {execDetail.buildNumber} · {execDetail.environmentName}{execDetail.agentCode ? ` · ${execDetail.agentCode}` : ""}</small></div><button aria-label="ปิด" onClick={() => setExecDetail(null)}>×</button></div>
      <div className="automation-run-detail-summary">
        <Badge tone={executionStatusTone[execDetail.status] ?? "blue"}>{execDetail.status}</Badge>
        <span>เริ่ม {formatThaiDateTime(execDetail.startedAt)}</span>
        <span>จบ {formatThaiDateTime(execDetail.completedAt)}</span>
        {execDetail.durationMs != null && <span>{(execDetail.durationMs / 1000).toFixed(2)} วิ</span>}
        {execDetail.errorCode && <Badge tone="red">{execDetail.errorCode}</Badge>}
        {execDetail.retryOfExecutionId && <Badge tone="orange">Auto-Retry #{execDetail.retryCount}</Badge>}
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
        {execDetail.classifiedFailureType && !classification && <div className="automation-failure-row"><Badge tone={failureTone[execDetail.classifiedFailureType] ?? "blue"}>{execDetail.classifiedFailureType}</Badge><span>จำแนกอัตโนมัติตอน Complete</span><span>แนะนำ: {execDetail.classifiedRecommendation}</span></div>}
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
  const emptyForm = { actionCode: "", actionName: "", category: "Generic", description: "", parameterSchemaJson: "{}", handlerKey: "", minimumAgentVersion: "1.0.0", isActive: true, retrySafety: "Unsafe" };
  const [form, setForm] = useState(emptyForm);
  const [editing, setEditing] = useState<AutomationActionItem | null>(null);
  const [busy, setBusy] = useState(false);
  const cats = ["ทั้งหมด", ...Array.from(new Set(actions.map((a) => a.category)))];
  const filtered = category === "ทั้งหมด" ? actions : actions.filter((a) => a.category === category);

  const openCreate = () => { setEditing(null); setForm(emptyForm); setActionModal(true); };
  const openEdit = (item: AutomationActionItem) => {
    setEditing(item);
    setForm({ actionCode: item.actionCode, actionName: item.actionName, category: item.category, description: item.description ?? "", parameterSchemaJson: item.parameterSchemaJson || "{}", handlerKey: item.handlerKey, minimumAgentVersion: item.minimumAgentVersion ?? "", isActive: item.isActive, retrySafety: item.retrySafety || "Unsafe" });
    setActionModal(true);
  };

  const save = async () => {
    setBusy(true);
    try {
      JSON.parse(form.parameterSchemaJson || "{}");
      const body = editing
        ? { actionName: form.actionName, category: form.category, description: form.description, parameterSchemaJson: form.parameterSchemaJson, handlerKey: form.handlerKey, minimumAgentVersion: form.minimumAgentVersion, isActive: form.isActive, retrySafety: form.retrySafety }
        : { actionCode: form.actionCode, actionName: form.actionName, category: form.category, description: form.description, parameterSchemaJson: form.parameterSchemaJson, minimumAgentVersion: form.minimumAgentVersion };
      const r = await fetch(editing ? `${apiUrl}/automation/actions/${editing.automationActionId}` : `${apiUrl}/automation/actions`, { method: editing ? "PUT" : "POST", headers, body: JSON.stringify(body) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? `${editing ? "แก้ไข" : "สร้าง"} Action ไม่สำเร็จ`);
      }
      setActionModal(false);
      onReload();
    } catch (e) {
      onError(e instanceof SyntaxError ? "Parameter Schema ต้องเป็น JSON ที่ถูกต้อง" : e instanceof Error ? e.message : "บันทึก Action ไม่สำเร็จ");
    } finally {
      setBusy(false);
    }
  };

  const toggle = async (item: AutomationActionItem) => {
    if (!window.confirm(`${item.isActive ? "ปิด" : "เปิด"} Action ${item.actionCode}?`)) return;
    setBusy(true);
    try {
      const r = await fetch(`${apiUrl}/automation/actions/${item.automationActionId}`, { method: "PUT", headers, body: JSON.stringify({ actionName: item.actionName, category: item.category, description: item.description, parameterSchemaJson: item.parameterSchemaJson, handlerKey: item.handlerKey, minimumAgentVersion: item.minimumAgentVersion, isActive: !item.isActive, retrySafety: item.retrySafety }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เปลี่ยนสถานะ Action ไม่สำเร็จ"); }
      onReload();
    } catch (e) { onError(e instanceof Error ? e.message : "เปลี่ยนสถานะ Action ไม่สำเร็จ"); }
    finally { setBusy(false); }
  };

  return <section className="automation-actions" aria-label="Action Library">
    <header className="automation-section-head"><div><h2>Action Library</h2><p>ชุดคำสั่งที่ Agent รองรับ · <code>ActionCode</code> ต้องตรงกับ DSL</p></div>{canManage && <button className="btn primary" onClick={openCreate}>+ เพิ่ม Action</button>}</header>
    <div className="automation-cand-filters" role="group" aria-label="กรอง Action ตาม Category">{cats.map((c) => <button key={c} type="button" className={"chip" + (category === c ? " active" : "")} onClick={() => setCategory(c)}>{c}</button>)}</div>
    {filtered.length ? <div className="table-wrap"><table><thead><tr><th>Action Code</th><th>Name</th><th>Category</th><th>Handler</th><th>Min Agent</th><th>Retry Safety</th><th>Active</th>{canManage && <th>Actions</th>}</tr></thead><tbody>{filtered.map((a) => <tr key={a.automationActionId}><td><b>{a.actionCode}</b></td><td>{a.actionName}</td><td><Badge tone="blue">{a.category}</Badge></td><td><code>{a.handlerKey}</code></td><td>{a.minimumAgentVersion ?? "-"}</td><td><Badge tone={a.retrySafety === "Safe" ? "green" : a.retrySafety === "Conditional" ? "yellow" : "red"}>{a.retrySafety}</Badge></td><td><Badge tone={a.isActive ? "green" : "gray"}>{a.isActive ? "Active" : "Inactive"}</Badge></td>{canManage && <td><div className="automation-row-actions"><button type="button" className="table-action" disabled={busy} onClick={() => openEdit(a)}>แก้ไข</button><button type="button" className={`table-action${a.isActive ? " danger" : ""}`} disabled={busy} onClick={() => toggle(a)}>{a.isActive ? "ปิด" : "เปิด"}</button></div></td>}</tr>)}</tbody></table></div> : <div className="empty"><p>ไม่พบ Action</p></div>}

    {actionModal && <div className="modal" role="dialog" aria-modal="true" onMouseDown={() => !busy && setActionModal(false)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2>{editing ? `แก้ไข ${editing.actionCode}` : "เพิ่ม Action"}</h2><small>Action จะถูกใช้ตรวจสอบ (Validate) ว่า DSL ถูกต้อง</small></div><button aria-label="ปิด" disabled={busy} onClick={() => setActionModal(false)}>×</button></div>
      <div className="form-grid">
        <label>Action Code<input value={form.actionCode} disabled={Boolean(editing)} onChange={(e) => setForm({ ...form, actionCode: e.target.value.toUpperCase() })} placeholder="เช่น SET_QTY" /></label>
        <label>Name<input value={form.actionName} onChange={(e) => setForm({ ...form, actionName: e.target.value })} /></label>
        <label>Category<select value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })}><option>Authentication</option><option>Navigation</option><option>Document</option><option>Item</option><option>Generic UI</option><option>Validation</option></select></label>
        <label>Minimum Agent Version<input value={form.minimumAgentVersion} onChange={(e) => setForm({ ...form, minimumAgentVersion: e.target.value })} /></label>
        <label>Handler Key<input value={form.handlerKey || form.actionCode} disabled={!editing} onChange={(e) => setForm({ ...form, handlerKey: e.target.value.toUpperCase() })} /></label>
        {editing && <label>Retry Safety<select value={form.retrySafety} onChange={(e) => setForm({ ...form, retrySafety: e.target.value })}><option value="Safe">Safe — retry ได้เสมอ</option><option value="Conditional">Conditional — retry ถ้ายังไม่สำเร็จ</option><option value="Unsafe">Unsafe — ห้าม retry (เช่น บันทึกเอกสาร)</option></select></label>}
        {editing && <label className="checkbox-field"><input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} /> Active</label>}
        <label className="full">Description<textarea value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} /></label>
        <label className="full">Parameter Schema JSON<textarea rows={6} spellCheck={false} value={form.parameterSchemaJson} onChange={(e) => setForm({ ...form, parameterSchemaJson: e.target.value })} /></label>
      </div>
      <div className="modal-actions"><button className="btn" disabled={busy} onClick={() => setActionModal(false)}>ยกเลิก</button><button className="btn primary" disabled={busy || !form.actionCode.trim() || !form.actionName.trim() || (Boolean(editing) && !form.handlerKey.trim())} onClick={save}>{busy ? "กำลังบันทึก..." : "บันทึก"}</button></div>
    </div></div>}
  </section>;
}

function ObjectRepositoryTab({ projectId, objects, canManage, headers, onReload, onError, objectModal, setObjectModal }: {
  projectId: string; objects: AutomationObjectItem[]; canManage: boolean; headers: Record<string, string>; onReload: () => void; onError: (e: string) => void;
  objectModal: boolean; setObjectModal: (v: boolean) => void;
}) {
  const [screen, setScreen] = useState("");
  const emptyForm = { moduleId: "", applicationCode: "Promaxx2", screenCode: "Sales", objectCode: "", objectName: "", controlType: "Button", automationId: "", selectorJson: "{}", isActive: true };
  const [form, setForm] = useState(emptyForm);
  const [editing, setEditing] = useState<AutomationObjectItem | null>(null);
  const [busy, setBusy] = useState(false);
  const [importModal, setImportModal] = useState(false);
  const [importText, setImportText] = useState("");
  const [importRows, setImportRows] = useState<AutomationObjectImportDraft[]>([]);
  const [selectedImport, setSelectedImport] = useState<Set<string>>(new Set());
  const [verifySelected, setVerifySelected] = useState<Set<string>>(new Set());
  const [verifications, setVerifications] = useState<AutomationObjectVerificationItem[]>([]);
  const [verifyModal, setVerifyModal] = useState(false);
  const [verifyReload, setVerifyReload] = useState(0);
  const screens = ["", ...Array.from(new Set(objects.map((o) => o.screenCode)))];
  const filtered = screen ? objects.filter((o) => o.screenCode === screen) : objects;
  const readyImportRows = importRows.filter((r) => r.status === "Ready");
  const latestVerificationByObject = useMemo(() => {
    const map = new Map<string, AutomationObjectVerificationItem>();
    for (const v of verifications) {
      const existing = map.get(v.automationObjectId);
      if (!existing || new Date(v.requestedAt) > new Date(existing.requestedAt)) map.set(v.automationObjectId, v);
    }
    return map;
  }, [verifications]);

  useEffect(() => {
    if (!projectId) return;
    fetch(`${apiUrl}/automation/objects/verifications?projectId=${projectId}`, { headers: { Authorization: `Bearer ${token()}` } })
      .then((r) => (r.ok ? r.json() : []))
      .then((v) => setVerifications(Array.isArray(v) ? v : []))
      .catch(() => setVerifications([]));
  }, [projectId, verifyReload]);

  const toggleVerifySelect = (id: string) => setVerifySelected((prev) => { const next = new Set(prev); if (next.has(id)) next.delete(id); else next.add(id); return next; });

  const requestVerification = async () => {
    if (!verifySelected.size) return;
    setBusy(true);
    try {
      const r = await fetch(`${apiUrl}/automation/objects/verify?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ objectIds: [...verifySelected], agentId: null }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "ขอตรวจสอบ Object ไม่สำเร็จ"); }
      setVerifySelected(new Set());
      setVerifyReload((x) => x + 1);
      setVerifyModal(true);
      onError(`ส่งคำขอตรวจสอบ ${verifySelected.size} Object แล้ว — รัน "runner verify --exe <path>" บนเครื่อง Agent เพื่อสแกนและรายงานผล`);
    } catch (e) { onError(e instanceof Error ? e.message : "ขอตรวจสอบ Object ไม่สำเร็จ"); }
    finally { setBusy(false); }
  };

  const openCreate = () => { setEditing(null); setForm(emptyForm); setObjectModal(true); };
  const openEdit = (item: AutomationObjectItem) => {
    setEditing(item);
    setForm({ moduleId: item.moduleId ?? "", applicationCode: item.applicationCode, screenCode: item.screenCode, objectCode: item.objectCode, objectName: item.objectName, controlType: item.controlType, automationId: item.automationId ?? "", selectorJson: item.selectorJson || "{}", isActive: item.isActive });
    setObjectModal(true);
  };

  const save = async () => {
    setBusy(true);
    try {
      JSON.parse(form.selectorJson || "{}");
      const body = { moduleId: form.moduleId || null, applicationCode: form.applicationCode, screenCode: form.screenCode, objectCode: form.objectCode, objectName: form.objectName, controlType: form.controlType, automationId: form.automationId || null, selectorJson: form.selectorJson };
      const r = await fetch(editing ? `${apiUrl}/automation/objects/${editing.automationObjectId}?projectId=${projectId}` : `${apiUrl}/automation/objects`, { method: editing ? "PUT" : "POST", headers, body: JSON.stringify(editing ? body : { projectId, ...body }) });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error(p?.detail ?? `${editing ? "แก้ไข" : "สร้าง"} Object ไม่สำเร็จ`);
      }
      setObjectModal(false);
      onReload();
    } catch (e) {
      onError(e instanceof SyntaxError ? "Selector JSON ต้องเป็น JSON ที่ถูกต้อง" : e instanceof Error ? e.message : "บันทึก Object ไม่สำเร็จ");
    } finally {
      setBusy(false);
    }
  };

  const toggle = async (item: AutomationObjectItem) => {
    if (!window.confirm(`${item.isActive ? "ปิด" : "เปิด"} Object ${buildObjectKey(item.screenCode, item.objectCode)}?`)) return;
    setBusy(true);
    try {
      const action = item.isActive ? "deactivate" : "activate";
      const r = await fetch(`${apiUrl}/automation/objects/${item.automationObjectId}/${action}?projectId=${projectId}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เปลี่ยนสถานะ Object ไม่สำเร็จ"); }
      onReload();
    } catch (e) { onError(e instanceof Error ? e.message : "เปลี่ยนสถานะ Object ไม่สำเร็จ"); }
    finally { setBusy(false); }
  };

  const previewImport = (text = importText) => {
    try {
      const existingKeys = new Set(objects.map((o) => `${o.applicationCode}.${o.screenCode}.${o.objectCode}`.toUpperCase()));
      const existingAutomationIds = new Set(objects.filter((o) => o.automationId).map((o) => `${o.applicationCode}.${o.automationId}`.toUpperCase()));
      const batchKeys = new Set<string>();
      const batchAutomationIds = new Set<string>();
      const rows = parseAutomationObjectImport(text).map((r, index) => {
        const applicationCode = r.applicationCode.trim() || "Promaxx2";
        const screenCode = r.screenCode.trim() || "Default";
        const objectCode = r.objectCode.trim().toUpperCase();
        const automationId = r.automationId?.trim();
        const key = `${applicationCode}.${screenCode}.${objectCode}`.toUpperCase();
        const automationKey = automationId ? `${applicationCode}.${automationId}`.toUpperCase() : "";
        let status: AutomationObjectImportDraft["status"] = "Ready";
        let message = "Ready to import.";
        try { JSON.parse(r.selectorJson || "{}"); } catch { status = "Invalid"; message = "Selector JSON is invalid."; }
        if (!objectCode || !r.objectName.trim() || !r.controlType.trim()) { status = "Invalid"; message = "Required fields are missing."; }
        else if (existingKeys.has(key) || batchKeys.has(key)) { status = "DuplicateKey"; message = "Business key already exists."; }
        else if (automationKey && (existingAutomationIds.has(automationKey) || batchAutomationIds.has(automationKey))) { status = "DuplicateAutomationId"; message = "AutomationId already exists."; }
        batchKeys.add(key);
        if (automationKey) batchAutomationIds.add(automationKey);
        return { ...r, applicationCode, screenCode, objectCode, automationId, clientId: `${index}-${key}`, status, message };
      });
      setImportRows(rows);
      setSelectedImport(new Set(rows.filter((r) => r.status === "Ready").map((r) => r.clientId)));
    } catch (e) {
      onError(e instanceof Error ? e.message : "Cannot parse import data.");
    }
  };

  const importSelected = async () => {
    const items = importRows.filter((r) => selectedImport.has(r.clientId) && r.status === "Ready");
    if (!items.length) return;
    setBusy(true);
    try {
      const r = await fetch(`${apiUrl}/automation/objects/import`, { method: "POST", headers, body: JSON.stringify({ projectId, items }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "Import Object failed"); }
      const result = await r.json() as AutomationObjectImportResult;
      setImportModal(false);
      setImportText("");
      setImportRows([]);
      setSelectedImport(new Set());
      onError(`Import complete: ${result.imported} imported, ${result.skipped} skipped.`);
      onReload();
    } catch (e) { onError(e instanceof Error ? e.message : "Import Object failed"); }
    finally { setBusy(false); }
  };

  return <section className="automation-objects" aria-label="Object Repository">
    <header className="automation-section-head"><div><h2>Object Repository</h2><p>Mapping ชื่อ Business (<code>Screen.Object</code>) ไปยัง Windows Control (<code>AutomationId</code>)</p></div>{canManage && <div className="automation-row-actions"><button className="btn" disabled={busy || !verifySelected.size} onClick={requestVerification}>⌕ ตรวจสอบที่เลือก ({verifySelected.size})</button><button className="btn" onClick={() => setVerifyModal(true)}>ผลตรวจสอบ</button><button className="btn" onClick={() => setImportModal(true)}>Import Scanner</button><button className="btn primary" onClick={openCreate}>+ เพิ่ม Object</button></div>}</header>
    <div className="automation-cand-filters" role="group" aria-label="กรอง Object ตาม Screen">{screens.map((s) => <button key={s || "all"} type="button" className={"chip" + (screen === s ? " active" : "")} onClick={() => setScreen(s)}>{s || "ทุก Screen"}</button>)}</div>
    {filtered.length ? <div className="table-wrap"><table><thead><tr>{canManage && <th aria-label="เลือก"></th>}<th>Business Key</th><th>Name</th><th>Screen</th><th>ControlType</th><th>AutomationId</th><th>Verification</th><th>Version</th><th>Active</th>{canManage && <th>Actions</th>}</tr></thead><tbody>{filtered.map((o) => { const lastVerify = latestVerificationByObject.get(o.automationObjectId); return <tr key={o.automationObjectId}>
      {canManage && <td><input type="checkbox" aria-label={`เลือกตรวจสอบ ${o.objectCode}`} checked={verifySelected.has(o.automationObjectId)} onChange={() => toggleVerifySelect(o.automationObjectId)} /></td>}
      <td><b>{buildObjectKey(o.screenCode, o.objectCode)}</b></td><td>{o.objectName}</td><td><Badge tone="blue">{o.screenCode}</Badge></td><td>{o.controlType}</td><td><code>{o.automationId ?? "-"}</code></td>
      <td>{lastVerify ? <Badge tone={verificationStatusTone[lastVerify.status] ?? "gray"}>{lastVerify.status}</Badge> : <span className="muted-text">ยังไม่ตรวจ</span>}</td>
      <td>v{o.objectVersion}</td><td><Badge tone={o.isActive ? "green" : "gray"}>{o.isActive ? "Active" : "Inactive"}</Badge></td>
      {canManage && <td><div className="automation-row-actions"><button type="button" className="table-action" disabled={busy} onClick={() => openEdit(o)}>แก้ไข</button><button type="button" className={`table-action${o.isActive ? " danger" : ""}`} disabled={busy} onClick={() => toggle(o)}>{o.isActive ? "ปิด" : "เปิด"}</button></div></td>}
    </tr>; })}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Object</p><small>Agent จะใช้ <code>AutomationId</code> นี้หาคอนโทรลบน Windows UI</small></div>}

    {verifyModal && <div className="modal" role="dialog" aria-modal="true" onMouseDown={() => setVerifyModal(false)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2>ผลตรวจสอบ Object (AUT-P0-006)</h2><small>รัน <code>runner verify --exe &lt;path&gt;</code> บนเครื่อง Agent เพื่อสแกนและรายงานผล Found/NotFound/Duplicate/ControlTypeMismatch</small></div><button aria-label="ปิด" onClick={() => setVerifyModal(false)}>×</button></div>
      {verifications.length ? <div className="table-wrap"><table><thead><tr><th>Business Key</th><th>Expected AutomationId</th><th>Actual</th><th>Status</th><th>Agent</th><th>เวลา</th></tr></thead><tbody>{verifications.map((v) => <tr key={v.automationObjectVerificationId}><td><b>{buildObjectKey(v.screenCode, v.objectCode)}</b></td><td><code>{v.expectedAutomationId ?? "-"}</code></td><td>{v.actualAutomationId ? <code>{v.actualAutomationId}</code> : "-"}{v.actualControlType ? ` (${v.actualControlType})` : ""}</td><td><Badge tone={verificationStatusTone[v.status] ?? "gray"}>{v.status}</Badge>{v.message && <small>{v.message}</small>}</td><td>{v.assignedAgentCode ?? "-"}</td><td>{formatThaiDateTime(v.completedAt ?? v.requestedAt)}</td></tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มีการขอตรวจสอบ</p></div>}
      <div className="modal-actions"><button className="btn primary" onClick={() => setVerifyModal(false)}>ปิด</button></div>
    </div></div>}

    {objectModal && <div className="modal" role="dialog" aria-modal="true" onMouseDown={() => !busy && setObjectModal(false)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2>{editing ? `แก้ไข ${buildObjectKey(editing.screenCode, editing.objectCode)}` : "เพิ่ม Object"}</h2><small>Business Key = <code>ScreenCode.ObjectCode</code> — DSL อ้างอิงด้วยค่านี้</small></div><button aria-label="ปิด" disabled={busy} onClick={() => setObjectModal(false)}>×</button></div>
      <div className="form-grid">
        <label>Application Code<input value={form.applicationCode} onChange={(e) => setForm({ ...form, applicationCode: e.target.value })} /></label>
        <label>Screen Code<input value={form.screenCode} onChange={(e) => setForm({ ...form, screenCode: e.target.value })} placeholder="เช่น Sales" /></label>
        <label>Object Code<input value={form.objectCode} onChange={(e) => setForm({ ...form, objectCode: e.target.value.toUpperCase() })} placeholder="เช่น SAVE" /></label>
        <label>Object Name<input value={form.objectName} onChange={(e) => setForm({ ...form, objectName: e.target.value })} /></label>
        <label>Control Type<select value={form.controlType} onChange={(e) => setForm({ ...form, controlType: e.target.value })}><option>Button</option><option>TextBox</option><option>ComboBox</option><option>CheckBox</option><option>Menu</option><option>Window</option></select></label>
        <label>AutomationId<input value={form.automationId} onChange={(e) => setForm({ ...form, automationId: e.target.value })} placeholder="เช่น btnSave" /></label>
        <label className="full">Selector JSON<textarea rows={5} spellCheck={false} value={form.selectorJson} onChange={(e) => setForm({ ...form, selectorJson: e.target.value })} /></label>
      </div>
      <div className="modal-actions"><button className="btn" disabled={busy} onClick={() => setObjectModal(false)}>ยกเลิก</button><button className="btn primary" disabled={busy || !form.screenCode.trim() || !form.objectCode.trim() || !form.objectName.trim() || !form.automationId.trim()} onClick={save}>{busy ? "กำลังบันทึก..." : "บันทึก"}</button></div>
    </div></div>}
    {importModal && <div className="modal" role="dialog" aria-modal="true" onMouseDown={() => !busy && setImportModal(false)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2>Import Objects from Scanner</h2><small>Paste JSON or CSV, preview duplicates, then import selected rows.</small></div><button aria-label="Close" disabled={busy} onClick={() => setImportModal(false)}>×</button></div>
      <div className="form-grid">
        <label className="full">Scanner Output<textarea rows={8} spellCheck={false} value={importText} onChange={(e) => setImportText(e.target.value)} placeholder={'applicationCode,screenCode,objectCode,objectName,controlType,automationId\nPromaxx2,Sales,SAVE,Save Button,Button,btnSave'} /></label>
      </div>
      <div className="automation-row-actions" style={{ marginBottom: 10 }}>
        <button className="btn" disabled={busy || !importText.trim()} onClick={() => previewImport()}>Preview Diff</button>
        <label className="btn import-button">Load File<input type="file" accept=".json,.csv,.txt" disabled={busy} onChange={(e) => { const f = e.target.files?.[0]; if (!f) return; void f.text().then((text) => { setImportText(text); previewImport(text); }); e.target.value = ""; }} /></label>
        {importRows.length > 0 && <button className="table-action" type="button" onClick={() => setSelectedImport(new Set(readyImportRows.map((r) => r.clientId)))}>Select Ready</button>}
        {importRows.length > 0 && <button className="table-action" type="button" onClick={() => setSelectedImport(new Set())}>Clear</button>}
      </div>
      {importRows.length > 0 && <div className="table-wrap"><table><thead><tr><th></th><th>Business Key</th><th>Name</th><th>Control</th><th>AutomationId</th><th>Status</th></tr></thead><tbody>{importRows.map((r) => <tr key={r.clientId}><td><input type="checkbox" aria-label={`Select ${r.objectCode}`} checked={selectedImport.has(r.clientId)} disabled={busy || r.status !== "Ready"} onChange={() => setSelectedImport((prev) => { const next = new Set(prev); if (next.has(r.clientId)) next.delete(r.clientId); else next.add(r.clientId); return next; })} /></td><td><b>{buildObjectKey(r.screenCode, r.objectCode)}</b><small>{r.applicationCode}</small></td><td>{r.objectName}</td><td>{r.controlType}</td><td><code>{r.automationId ?? "-"}</code></td><td><Badge tone={r.status === "Ready" ? "green" : r.status === "Invalid" ? "red" : "yellow"}>{r.status}</Badge><small>{r.message}</small></td></tr>)}</tbody></table></div>}
      <div className="modal-actions"><button className="btn" disabled={busy} onClick={() => setImportModal(false)}>Cancel</button><button className="btn primary" disabled={busy || selectedImport.size === 0} onClick={importSelected}>{busy ? "Importing..." : `Import ${selectedImport.size} rows`}</button></div>
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

function QuarantineModal({ candidate, busy, onClose, onConfirm }: {
  candidate: FlakyCandidateItem; busy: boolean; onClose: () => void; onConfirm: (caseId: string, reason: string, ownerUserId: string, expiresAt: string) => void;
}) {
  const [reason, setReason] = useState(`Flaky: ${candidate.transitions} transitions ใน ${candidate.recentRuns} execution ล่าสุด`);
  const [ownerUserId, setOwnerUserId] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-quarantine-title" onMouseDown={() => !busy && onClose()}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
    <div className="modal-head"><div><h2 id="automation-quarantine-title">Quarantine {candidate.automationCode}</h2><small>แยกออกจาก Product Fail ชั่วคราวจนกว่าจะแก้ไข Flaky</small></div><button aria-label="ปิด" disabled={busy} onClick={onClose}>×</button></div>
    <div className="form-grid">
      <label className="full">เหตุผล<textarea rows={3} value={reason} onChange={(e) => setReason(e.target.value)} /></label>
      <label>User Id ผู้รับผิดชอบ (ไม่บังคับ)<input value={ownerUserId} onChange={(e) => setOwnerUserId(e.target.value)} /></label>
      <label>หมดอายุ (ไม่บังคับ)<input type="date" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} /></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}>ยกเลิก</button><button className="btn primary" disabled={busy || !reason.trim()} onClick={() => onConfirm(candidate.automationCaseId, reason.trim(), ownerUserId, expiresAt ? new Date(expiresAt).toISOString() : "")}>{busy ? "กำลังบันทึก..." : "Quarantine"}</button></div>
  </div></div>;
}

function RetryPolicyTab({ policy, canManage, busy, onSave }: {
  policy: RetryPolicyItem | null; canManage: boolean; busy: boolean; onSave: (policy: RetryPolicyItem) => void;
}) {
  const [maxAttempts, setMaxAttempts] = useState(policy?.maxAttempts ?? 2);
  const [backoffSeconds, setBackoffSeconds] = useState(policy?.backoffSeconds ?? 30);
  const [enabled, setEnabled] = useState(policy?.enabled ?? true);
  useEffect(() => { if (policy) { setMaxAttempts(policy.maxAttempts); setBackoffSeconds(policy.backoffSeconds); setEnabled(policy.enabled); } }, [policy]);

  return <section className="automation-actions" aria-label="Retry Policy">
    <header className="automation-section-head"><div><h2>Retry Policy (AUT-P0-009)</h2><p>กำหนดจำนวนครั้ง/ระยะเวลา backoff สำหรับ auto-retry เมื่อ Execution ล้มเหลวจาก Environment/Agent — ไม่ retry เมื่อมี Step ที่ไม่ปลอดภัย (Unsafe) สำเร็จไปแล้ว</p></div></header>
    <div className="form-grid">
      <label>Max Attempts<input type="number" min={0} max={10} value={maxAttempts} disabled={!canManage} onChange={(e) => setMaxAttempts(Number(e.target.value))} /></label>
      <label>Backoff (วินาที)<input type="number" min={0} max={3600} value={backoffSeconds} disabled={!canManage} onChange={(e) => setBackoffSeconds(Number(e.target.value))} /></label>
      <label className="checkbox-field"><input type="checkbox" checked={enabled} disabled={!canManage} onChange={(e) => setEnabled(e.target.checked)} /> เปิดใช้งาน Auto-Retry</label>
    </div>
    {policy?.updatedAt && <p className="muted-text">แก้ไขล่าสุด {formatThaiDateTime(policy.updatedAt)}</p>}
    {canManage && <div className="acw-action-bar"><button type="button" className="btn primary" disabled={busy} onClick={() => onSave({ maxAttempts, backoffSeconds, enabled })}>{busy ? "กำลังบันทึก..." : "บันทึก"}</button></div>}
  </section>;
}

function AgentsSection({ agents, agentsOnline, canManage, onToggle, onDelete }: {
  agents: AutomationAgentItem[]; agentsOnline: number; canManage: boolean; onToggle: (a: AutomationAgentItem, enable: boolean) => void; onDelete: (a: AutomationAgentItem) => void;
}) {
  return <section className="automation-agents" aria-label="Automation Agents">
    <header className="automation-section-head"><div><h2>Central Windows Agents</h2><p>Agent ลงทะเบียนอัตโนมัติและส่ง heartbeat ทุก 15 วินาที · Offline เมื่อเงียบเกิน 60 วินาที</p></div><span className="automation-agent-count">{agentsOnline} Online</span></header>
    {agents.length ? <div className="automation-agent-grid">{agents.map((a) => <article key={a.agentId}><div className="automation-agent-top"><div><Badge tone={a.connectivity === "Online" ? "green" : a.connectivity === "Disabled" ? "gray" : "yellow"}>{a.connectivity}</Badge><Badge tone={a.status === "Busy" ? "blue" : "green"}>{a.status}</Badge></div><div className="automation-agent-actions">{canManage && <button type="button" className={`table-action icon-btn${a.isEnabled ? "" : " danger"}`} title={a.isEnabled ? "ปิดใช้งาน" : "เปิดใช้งาน"} aria-label={a.isEnabled ? "ปิดใช้งาน" : "เปิดใช้งาน"} onClick={() => onToggle(a, !a.isEnabled)}>{a.isEnabled ? "⏻" : "⏼"}</button>}{canManage && <button type="button" className="table-action danger icon-btn" title="ลบ Agent" aria-label="ลบ Agent" onClick={() => onDelete(a)}>🗑</button>}</div></div><b>{a.agentCode}</b><span>{a.machineName} · v{a.agentVersion}</span><small>{a.operatingSystem} · {a.architecture}</small><small>รองรับ {a.capabilities.join(" + ") || "-"}</small><time dateTime={a.lastHeartbeatAt}>ล่าสุด {formatThaiDateTime(a.lastHeartbeatAt)}</time></article>)}</div> : <div className="empty"><p>ยังไม่มี Agent ลงทะเบียน</p><small>ติดตั้งบนเครื่อง Windows: ตั้งค่า env แล้วรัน <code>agent\\run-agent.ps1</code> — Agent จะ register + ส่ง heartbeat อัตโนมัติ</small></div>}
  </section>;
}

function ExecutionTab({ projectId, buildId, releaseId, agents: agentOptions, headers, jobs, executions, setExecDetail, execFilter, setExecFilter, canRun, onCancel, onRerun, reload }: {
  projectId: string; buildId?: string; releaseId?: string; agents: AutomationAgentItem[]; headers: Record<string, string>; jobs: AutomationJobItem[]; executions: AutomationExecutionItem[]; setExecDetail: (v: AutomationExecutionItem | null) => void;
  execFilter: string; setExecFilter: (v: string) => void; canRun: boolean; onCancel: (x: AutomationExecutionItem) => void; onRerun: (x: AutomationExecutionItem) => void; reload: number;
}) {
  // AUT-P2-001: Job Queue and Run History are server-paginated for real — their own fetch, own page/filter/sort
  // state, hitting the same paged endpoints as the shared "up to 200" load above. The `jobs`/`executions` props
  // (that shared, capped state) are kept ONLY for the KPI strip below, which is a cross-cutting summary, not a list
  // to page through — per the confirmed scope, KPIs/export/pickers stay on the flat shared load.
  const [execSearch, setExecSearch] = useState("");
  const [execPage, setExecPage] = useState(1);
  const [jobPage, setJobPage] = useState(1);
  const pageSize = 15;
  const [jobsPaged, setJobsPaged] = useState<{ total: number; rows: AutomationJobItem[] }>({ total: 0, rows: [] });
  const [execPaged, setExecPaged] = useState<{ total: number; rows: AutomationExecutionItem[] }>({ total: 0, rows: [] });
  // AUT-P2-002: advanced Run History filters — date range, Build, Environment, Agent, Target, Failure Type.
  // `execBuildFilter` overrides the page-level `buildId` context when set (empty means "all builds").
  const [execFrom, setExecFrom] = useState("");
  const [execTo, setExecTo] = useState("");
  const [execBuildFilter, setExecBuildFilter] = useState("");
  const [execEnvironmentFilter, setExecEnvironmentFilter] = useState("");
  const [execAgentFilter, setExecAgentFilter] = useState("");
  const [execTargetFilter, setExecTargetFilter] = useState("");
  const [execFailureTypeFilter, setExecFailureTypeFilter] = useState("");
  const [execBuilds, setExecBuilds] = useState<BuildOption[]>([]);
  const [execEnvironments, setExecEnvironments] = useState<EnvironmentOption[]>([]);
  const hasAdvancedFilters = execFrom || execTo || execBuildFilter || execEnvironmentFilter || execAgentFilter || execTargetFilter || execFailureTypeFilter;
  const clearAdvancedFilters = () => { setExecFrom(""); setExecTo(""); setExecBuildFilter(""); setExecEnvironmentFilter(""); setExecAgentFilter(""); setExecTargetFilter(""); setExecFailureTypeFilter(""); };

  const queuedJobs = jobs.filter((j) => j.status === "Queued");
  const jobPageCount = Math.max(1, Math.ceil(jobsPaged.total / pageSize));
  const execPageCount = Math.max(1, Math.ceil(execPaged.total / pageSize));
  const kpiRunning = executions.filter((e) => e.status === "Running").length;
  const kpiPassed = executions.filter((e) => e.status === "Passed").length;
  const kpiFailed = executions.filter((e) => e.status === "Failed").length;
  useEffect(() => setExecPage(1), [execSearch, execFilter, execFrom, execTo, execBuildFilter, execEnvironmentFilter, execAgentFilter, execTargetFilter, execFailureTypeFilter]);
  useEffect(() => setJobPage(1), [buildId]);

  useEffect(() => {
    let mounted = true;
    Promise.all([
      releaseId ? fetch(`${apiUrl}/releases/${releaseId}/builds`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : [])) : Promise.resolve([]),
      fetch(`${apiUrl}/master-settings/environments`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : [])),
    ]).then(([b, e]) => {
      if (!mounted) return;
      setExecBuilds(Array.isArray(b) ? b : []);
      setExecEnvironments(Array.isArray(e) ? (e as EnvironmentOption[]).filter((x) => x.isActive) : []);
    }).catch(() => { /* selects just render empty — the run history table still works without them */ });
    return () => { mounted = false; };
  }, [releaseId]);

  useEffect(() => {
    if (!projectId) { setJobsPaged({ total: 0, rows: [] }); return; }
    const qs = new URLSearchParams({ projectId, page: String(jobPage), size: String(pageSize) });
    if (buildId) qs.set("buildId", buildId);
    fetch(`${apiUrl}/automation/jobs?${qs}`, { headers })
      .then((r) => (r.ok ? r.json() : { total: 0, rows: [] }))
      .then((d) => setJobsPaged(d && typeof d === "object" && Array.isArray(d.rows) ? d : { total: 0, rows: [] }))
      .catch(() => setJobsPaged({ total: 0, rows: [] }));
  }, [projectId, buildId, jobPage, headers, reload]);

  useEffect(() => {
    if (!projectId) { setExecPaged({ total: 0, rows: [] }); return; }
    const qs = new URLSearchParams({ projectId, page: String(execPage), size: String(pageSize) });
    if (execBuildFilter || buildId) qs.set("buildId", execBuildFilter || buildId!);
    if (execEnvironmentFilter) qs.set("environmentId", execEnvironmentFilter);
    if (execAgentFilter) qs.set("agentId", execAgentFilter);
    if (execTargetFilter) qs.set("targetApp", execTargetFilter);
    if (execFilter !== "all") qs.set("status", execFilter);
    if (execFailureTypeFilter) qs.set("failureType", execFailureTypeFilter);
    if (execFrom) qs.set("from", new Date(execFrom).toISOString());
    if (execTo) qs.set("to", new Date(execTo).toISOString());
    if (execSearch.trim()) qs.set("search", execSearch.trim());
    fetch(`${apiUrl}/automation/executions?${qs}`, { headers })
      .then((r) => (r.ok ? r.json() : { total: 0, rows: [] }))
      .then((d) => setExecPaged(d && typeof d === "object" && Array.isArray(d.rows) ? d : { total: 0, rows: [] }))
      .catch(() => setExecPaged({ total: 0, rows: [] }));
  }, [projectId, buildId, execPage, execFilter, execSearch, execFrom, execTo, execBuildFilter, execEnvironmentFilter, execAgentFilter, execTargetFilter, execFailureTypeFilter, headers, reload]);

  return <section className="automation-execution" aria-label="Automation Execution">
    <header className="automation-section-head"><div><h2>Execution Queue & Run History</h2><p>ติดตามงานที่ Agent รับไปรัน และผลลัพธ์ทั้งหมด — รองรับข้อมูลจำนวนมากด้วยค้นหา/กรอง/แบ่งหน้าฝั่ง Server</p></div></header>
    <div className="automation-kpis">
      <div><small>Queued</small><strong>{queuedJobs.length}</strong><span>รอ Agent รับ</span></div>
      <div><small>Running</small><strong>{kpiRunning}</strong><span>กำลังรัน</span></div>
      <div><small>Passed</small><strong>{kpiPassed}</strong><span>ผ่านทั้งหมด</span></div>
      <div className={kpiFailed ? "needs-review" : ""}><small>Failed</small><strong>{kpiFailed}</strong><span>ไม่ผ่าน</span></div>
      <div><small>Total</small><strong>{executions.length}</strong><span>ผลรัน</span></div>
    </div>
    <div className="automation-exec-grid">
      <article className="card">
        <div className="automation-section-head"><h3>Job Queue ({jobsPaged.total.toLocaleString()})</h3><span className="muted-text">{queuedJobs.length} queued</span></div>
        {jobsPaged.rows.length ? <>
          <div className="automation-exec-list">{jobsPaged.rows.map((j) => <div key={j.jobId} className="automation-queue-list"><article><div className="automation-queue-main"><Badge tone={jobStatusTone[j.status] ?? "blue"}>{j.status}</Badge><b>P{j.priority}</b><span>{j.assignedAgentCode ?? "รอ Agent"}</span>{j.retryCount > 0 && <Badge tone="orange">Retry {j.retryCount}</Badge>}</div><div><time dateTime={j.queuedAt}>{formatThaiDateTime(j.queuedAt)}</time>{j.lastError && <small className="queue-error">{j.lastError}</small>}</div></article></div>)}</div>
          <Pager page={jobPage} count={jobPageCount} total={jobsPaged.total} pageSize={pageSize} onPrev={() => setJobPage((p) => Math.max(1, p - 1))} onNext={() => setJobPage((p) => Math.min(jobPageCount, p + 1))} />
        </> : <div className="empty"><p>ไม่มีงานในคิว</p></div>}
      </article>
      <article className="card">
        <div className="automation-section-head"><h3>Run History ({execPaged.total.toLocaleString()})</h3></div>
        <div className="automation-run-toolbar">
          <input aria-label="ค้นหาด้วยรหัสหรือ Agent" placeholder="ค้นหา Code / Agent..." value={execSearch} onChange={(e) => setExecSearch(e.target.value)} />
          <select aria-label="กรองสถานะ" value={execFilter} onChange={(e) => setExecFilter(e.target.value)}>
            <option value="all">ทุกสถานะ</option>
            {["Passed", "Failed", "Running", "Queued", "Blocked", "Cancelled", "Timeout", "AgentLost"].map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>
        {/* AUT-P2-002: advanced filters — date/Build/Environment/Agent/Target/Failure Type, all server-side. */}
        <div className="automation-run-toolbar">
          <label>จาก<input type="date" value={execFrom} onChange={(e) => setExecFrom(e.target.value)} aria-label="วันที่เริ่ม" /></label>
          <label>ถึง<input type="date" value={execTo} onChange={(e) => setExecTo(e.target.value)} aria-label="วันที่สิ้นสุด" /></label>
          <select aria-label="กรอง Build" value={execBuildFilter} onChange={(e) => setExecBuildFilter(e.target.value)}><option value="">ทุก Build</option>{execBuilds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}</option>)}</select>
          <select aria-label="กรอง Environment" value={execEnvironmentFilter} onChange={(e) => setExecEnvironmentFilter(e.target.value)}><option value="">ทุก Environment</option>{execEnvironments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select>
          <select aria-label="กรอง Agent" value={execAgentFilter} onChange={(e) => setExecAgentFilter(e.target.value)}><option value="">ทุก Agent</option>{agentOptions.map((a) => <option key={a.agentId} value={a.agentId}>{a.agentCode}</option>)}</select>
          <select aria-label="กรอง Target" value={execTargetFilter} onChange={(e) => setExecTargetFilter(e.target.value)}><option value="">ทุก Target</option><option value="Pos">Pos</option><option value="App">App</option><option value="WindowsUI">WindowsUI</option></select>
          <select aria-label="กรอง Failure Type" value={execFailureTypeFilter} onChange={(e) => setExecFailureTypeFilter(e.target.value)}><option value="">ทุก Failure Type</option>{failureTypeOptions.map((f) => <option key={f} value={f}>{f}</option>)}</select>
          {hasAdvancedFilters && <button type="button" className="table-action" onClick={clearAdvancedFilters}>ล้างตัวกรองขั้นสูง</button>}
        </div>
        {execPaged.rows.length ? <div className="table-wrap"><table className="automation-exec-table"><thead><tr><th>Code</th><th>Target</th><th>Agent</th><th>Status</th><th>Duration</th><th>เวลา</th><th></th></tr></thead><tbody>{execPaged.rows.map((x) => <tr key={x.automationExecutionId} onClick={() => setExecDetail(x)} className="automation-exec-tr"><td><b>{x.automationCode}</b><small>Rev {x.versionNo} · {x.buildNumber}</small></td><td><Badge tone={x.targetApp === "Pos" ? "blue" : x.targetApp === "App" ? "purple" : "gray"}>{x.targetApp ?? "WindowsUI"}</Badge></td><td>{x.agentCode ?? "-"}</td><td><Badge tone={executionStatusTone[x.status] ?? "blue"}>{x.status}</Badge></td><td>{x.durationMs != null ? `${(x.durationMs / 1000).toFixed(1)}s` : "-"}</td><td>{formatThaiDateTime(x.completedAt ?? x.startedAt)}</td><td onClick={(e) => e.stopPropagation()}><div className="automation-row-actions"><button type="button" className="automation-more" title="ดูรายละเอียด" aria-label={`ดูรายละเอียด ${x.automationCode}`} onClick={() => setExecDetail(x)}>⋮</button>{canRun && x.status !== "Running" && x.status !== "Queued" && <button type="button" className="automation-more is-run" title="รันซ้ำ" aria-label={`รันซ้ำ ${x.automationCode}`} onClick={() => onRerun(x)}>▶</button>}{canRun && (x.status === "Running" || x.status === "Queued") && <button type="button" className="automation-more is-danger" title="ยกเลิก" aria-label={`ยกเลิก ${x.automationCode}`} onClick={() => onCancel(x)}>✕</button>}</div></td></tr>)}</tbody></table></div> : <div className="empty"><p>{execSearch || execFilter !== "all" ? "ไม่พบผลการรันที่ตรงเงื่อนไข" : "ยังไม่มีประวัติการรัน"}</p></div>}
        {execPaged.total > pageSize && <Pager page={execPage} count={execPageCount} total={execPaged.total} pageSize={pageSize} onPrev={() => setExecPage((p) => Math.max(1, p - 1))} onNext={() => setExecPage((p) => Math.min(execPageCount, p + 1))} />}
      </article>
    </div>
  </section>;
}
const failureTypeOptions = ["EnvironmentFailure", "AssertionFailure", "AutomationFailure", "AgentFailure", "Unknown"];

function FailureDashboardTab({ projectId, releaseId, agents, headers, setExecDetail }: {
  projectId: string; releaseId?: string; agents: AutomationAgentItem[]; headers: Record<string, string>; setExecDetail: (v: AutomationExecutionItem | null) => void;
}) {
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [buildId, setBuildId] = useState("");
  const [agentId, setAgentId] = useState("");
  const [failureType, setFailureType] = useState("");
  const [builds, setBuilds] = useState<BuildOption[]>([]);
  const [breakdown, setBreakdown] = useState<FailureBreakdownItem | null>(null);
  const [rows, setRows] = useState<AutomationExecutionItem[]>([]);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!releaseId) return;
    fetch(`${apiUrl}/releases/${releaseId}/builds`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : [])).then((b) => setBuilds(Array.isArray(b) ? b : [])).catch(() => setBuilds([]));
  }, [releaseId]);

  useEffect(() => {
    if (!projectId) return;
    setBusy(true);
    const qs = new URLSearchParams({ projectId });
    if (from) qs.set("from", new Date(from).toISOString());
    if (to) qs.set("to", new Date(to).toISOString());
    if (buildId) qs.set("buildId", buildId);
    if (agentId) qs.set("agentId", agentId);
    if (failureType) qs.set("failureType", failureType);
    Promise.all([
      fetch(`${apiUrl}/automation/failures/dashboard?${qs}`, { headers }).then((r) => (r.ok ? r.json() : null)),
      fetch(`${apiUrl}/automation/failures/executions?${qs}`, { headers }).then((r) => (r.ok ? r.json() : [])),
    ]).then(([b, e]) => { setBreakdown(b); setRows(Array.isArray(e) ? e : []); }).finally(() => setBusy(false));
  }, [projectId, from, to, buildId, agentId, failureType, headers]);

  const clearFilters = () => { setFrom(""); setTo(""); setBuildId(""); setAgentId(""); setFailureType(""); };
  const hasFilters = from || to || buildId || agentId || failureType;

  return <section className="automation-execution" aria-label="Failure Dashboard">
    <header className="automation-section-head"><div><h2>Failure Dashboard</h2><p>วิเคราะห์ Execution ที่ Fail ตาม Failure Type / Build / Agent / วันที่ พร้อม drill down</p></div></header>
    <div className="automation-run-toolbar">
      <label>จาก<input type="date" value={from} onChange={(e) => setFrom(e.target.value)} aria-label="วันที่เริ่ม" /></label>
      <label>ถึง<input type="date" value={to} onChange={(e) => setTo(e.target.value)} aria-label="วันที่สิ้นสุด" /></label>
      <select aria-label="กรอง Build" value={buildId} onChange={(e) => setBuildId(e.target.value)}><option value="">ทุก Build</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}</option>)}</select>
      <select aria-label="กรอง Agent" value={agentId} onChange={(e) => setAgentId(e.target.value)}><option value="">ทุก Agent</option>{agents.map((a) => <option key={a.agentId} value={a.agentId}>{a.agentCode}</option>)}</select>
      <select aria-label="กรอง Failure Type" value={failureType} onChange={(e) => setFailureType(e.target.value)}><option value="">ทุก Failure Type</option>{failureTypeOptions.map((f) => <option key={f} value={f}>{f}</option>)}</select>
      {hasFilters && <button type="button" className="table-action" onClick={clearFilters}>ล้างตัวกรอง</button>}
    </div>
    {breakdown && <div className="automation-kpis">
      <div className="needs-review"><small>Total Failed</small><strong>{breakdown.totalFailed}</strong><span>ตามตัวกรอง</span></div>
      {breakdown.byFailureType.slice(0, 4).map((x) => <div key={x.key}><small>{x.key}</small><strong>{x.count}</strong><span>Failure Type</span></div>)}
    </div>}
    <div className="automation-exec-grid">
      <article className="card">
        <div className="automation-section-head"><h3>By Build</h3></div>
        {breakdown?.byBuild.length ? <div className="automation-result-list">{breakdown.byBuild.map((x) => <div key={x.key} className="automation-failure-row"><b>{x.key}</b><span>{x.count} fail</span></div>)}</div> : <div className="empty"><p>ไม่มีข้อมูล</p></div>}
      </article>
      <article className="card">
        <div className="automation-section-head"><h3>By Agent</h3></div>
        {breakdown?.byAgent.length ? <div className="automation-result-list">{breakdown.byAgent.map((x) => <div key={x.key} className="automation-failure-row"><b>{x.key}</b><span>{x.count} fail</span></div>)}</div> : <div className="empty"><p>ไม่มีข้อมูล</p></div>}
      </article>
      <article className="card">
        <div className="automation-section-head"><h3>Top Automation Case</h3></div>
        {breakdown?.byAutomationCase.length ? <div className="automation-result-list">{breakdown.byAutomationCase.map((x) => <div key={x.key} className="automation-failure-row"><b>{x.key}</b><span>{x.count} fail</span></div>)}</div> : <div className="empty"><p>ไม่มีข้อมูล</p></div>}
      </article>
    </div>
    <article className="card">
      <div className="automation-section-head"><h3>Failed Executions ({rows.length})</h3>{busy && <span className="muted-text">กำลังโหลด...</span>}</div>
      {rows.length ? <div className="table-wrap"><table className="automation-exec-table"><thead><tr><th>Code</th><th>Classified</th><th>Build</th><th>Agent</th><th>เวลา</th><th></th></tr></thead><tbody>{rows.map((x) => <tr key={x.automationExecutionId} className="automation-exec-tr" onClick={() => setExecDetail(x)}>
        <td><b>{x.automationCode}</b><small>Rev {x.versionNo}</small></td>
        <td>{x.classifiedFailureType ? <Badge tone={failureTone[x.classifiedFailureType] ?? "blue"}>{x.classifiedFailureType}</Badge> : <span className="muted-text">ยังไม่จำแนก</span>}</td>
        <td>{x.buildNumber}</td>
        <td>{x.agentCode ?? "-"}</td>
        <td>{formatThaiDateTime(x.completedAt ?? x.startedAt)}</td>
        <td onClick={(e) => e.stopPropagation()}><button type="button" className="table-action" onClick={() => setExecDetail(x)}>ดูรายละเอียด</button></td>
      </tr>)}</tbody></table></div> : <div className="empty"><p>ไม่พบ Execution ที่ Fail ตามเงื่อนไข</p></div>}
    </article>
  </section>;
}

function AutomationSuiteTab({ projectId, releaseId, headers, canEdit, canRun, cases }: {
  projectId: string; releaseId?: string; headers: Record<string, string>; canEdit: boolean; canRun: boolean; cases: AutomationCaseItem[];
}) {
  const [suites, setSuites] = useState<AutomationSuiteListItem[]>([]);
  const [search, setSearch] = useState("");
  const [activeFilter, setActiveFilter] = useState<"all" | "active" | "closed">("active");
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [createModal, setCreateModal] = useState(false);
  const [editSuite, setEditSuite] = useState<AutomationSuiteListItem | null>(null);
  const [detail, setDetail] = useState<AutomationSuiteDetailItem | null>(null);
  const [addCasesModal, setAddCasesModal] = useState(false);
  const [history, setHistory] = useState<AutomationSuiteRevisionItem[] | null>(null);
  const [runSuiteFor, setRunSuiteFor] = useState<{ automationSuiteId: string; suiteCode: string; caseCount: number; readyCaseCount: number } | null>(null);
  const [runResult, setRunResult] = useState<{ suiteCode: string; created: number; skipped: string[] } | null>(null);

  useEffect(() => {
    if (!projectId) return;
    const qs = new URLSearchParams({ projectId });
    if (search.trim()) qs.set("search", search.trim());
    if (activeFilter !== "all") qs.set("isActive", activeFilter === "active" ? "true" : "false");
    fetch(`${apiUrl}/automation/suites?${qs}`, { headers }).then((r) => (r.ok ? r.json() : [])).then((s) => setSuites(Array.isArray(s) ? s : [])).catch(() => setError("โหลด Automation Suite ไม่สำเร็จ"));
  }, [projectId, search, activeFilter, headers, reload]);

  const refreshDetail = async (id: string) => {
    const r = await fetch(`${apiUrl}/automation/suites/${id}?projectId=${projectId}`, { headers });
    if (r.ok) setDetail(await r.json());
  };

  const openDetail = async (row: AutomationSuiteListItem) => { await refreshDetail(row.automationSuiteId); };

  const openHistory = async (id: string) => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${id}/history?projectId=${projectId}`, { headers });
      if (!r.ok) throw new Error("โหลดประวัติไม่สำเร็จ");
      setHistory(await r.json());
    } catch (e) { setError(e instanceof Error ? e.message : "โหลดประวัติไม่สำเร็จ"); }
  };

  const createSuite = async (suiteCode: string, suiteName: string, description: string) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ suiteCode: suiteCode.trim() || null, suiteName: suiteName.trim(), description: description.trim() || null }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Suite ไม่สำเร็จ"); }
      setCreateModal(false); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "สร้าง Suite ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const updateSuite = async (id: string, suiteName: string, description: string, changeReason: string) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${id}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify({ suiteName: suiteName.trim(), description: description.trim() || null, changeReason: changeReason.trim() || null }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "แก้ไข Suite ไม่สำเร็จ"); }
      setEditSuite(null); setReload((v) => v + 1);
      if (detail?.automationSuiteId === id) await refreshDetail(id);
    } catch (e) { setError(e instanceof Error ? e.message : "แก้ไข Suite ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const toggleSuite = async (row: AutomationSuiteListItem) => {
    if (!window.confirm(`${row.isActive ? "ปิด" : "เปิด"} Suite "${row.suiteCode}"?`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${row.automationSuiteId}/${row.isActive ? "close" : "reopen"}?projectId=${projectId}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เปลี่ยนสถานะ Suite ไม่สำเร็จ"); }
      setReload((v) => v + 1);
      if (detail?.automationSuiteId === row.automationSuiteId) await refreshDetail(row.automationSuiteId);
    } catch (e) { setError(e instanceof Error ? e.message : "เปลี่ยนสถานะ Suite ไม่สำเร็จ"); }
  };

  const addCases = async (caseIds: string[], isRequired: boolean, changeReason: string) => {
    if (!detail) return;
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${detail.automationSuiteId}/cases?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ automationCaseIds: caseIds, isRequired, changeReason: changeReason.trim() || null }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เพิ่ม Case ไม่สำเร็จ"); }
      setDetail(await r.json()); setAddCasesModal(false); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "เพิ่ม Case ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const removeCase = async (caseId: string) => {
    if (!detail || !window.confirm("ลบ Case นี้ออกจาก Suite?")) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${detail.automationSuiteId}/cases/${caseId}?projectId=${projectId}`, { method: "DELETE", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "ลบ Case ไม่สำเร็จ"); }
      setDetail(await r.json()); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "ลบ Case ไม่สำเร็จ"); }
  };

  const toggleRequired = async (row: AutomationSuiteCaseItem) => {
    if (!detail) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${detail.automationSuiteId}/cases/${row.automationCaseId}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify({ sortOrder: row.sortOrder, isRequired: !row.isRequired }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "แก้ไข Case ไม่สำเร็จ"); }
      setDetail(await r.json());
    } catch (e) { setError(e instanceof Error ? e.message : "แก้ไข Case ไม่สำเร็จ"); }
  };

  const runSuite = async (suiteId: string, suiteCode: string, buildId: string, environmentId: string, priority: number) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/suites/${suiteId}/run?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ buildId, environmentId, agentId: null, priority }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "รัน Suite ไม่สำเร็จ"); }
      const result: { created: unknown[]; skippedCodes: string[] } = await r.json();
      setRunSuiteFor(null);
      setRunResult({ suiteCode, created: result.created.length, skipped: result.skippedCodes });
    } catch (e) { setError(e instanceof Error ? e.message : "รัน Suite ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const moveCase = async (row: AutomationSuiteCaseItem, direction: -1 | 1) => {
    if (!detail) return;
    const sorted = [...detail.cases].sort((a, b) => a.sortOrder - b.sortOrder);
    const idx = sorted.findIndex((c) => c.automationCaseId === row.automationCaseId);
    const swapWith = sorted[idx + direction];
    if (!swapWith) return;
    setError("");
    try {
      await Promise.all([
        fetch(`${apiUrl}/automation/suites/${detail.automationSuiteId}/cases/${row.automationCaseId}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify({ sortOrder: swapWith.sortOrder, isRequired: row.isRequired }) }),
        fetch(`${apiUrl}/automation/suites/${detail.automationSuiteId}/cases/${swapWith.automationCaseId}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify({ sortOrder: row.sortOrder, isRequired: swapWith.isRequired }) }),
      ]);
      await refreshDetail(detail.automationSuiteId);
    } catch { setError("เรียงลำดับไม่สำเร็จ"); }
  };

  return <section className="automation-cases" aria-label="Automation Suite">
    <header className="automation-section-head"><div><h2>Automation Suite (AUT-P1-001/002)</h2><p>รวม Automation Case เป็นชุดถาวรสำหรับรัน Regression/Smoke ซ้ำได้ — Required/Optional ต่อ Case</p></div>{canEdit && <button className="btn primary" type="button" onClick={() => setCreateModal(true)}>＋ สร้าง Suite</button>}</header>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    <div className="automation-case-toolbar">
      <input aria-label="ค้นหา Suite" placeholder="ค้นหา Suite..." value={search} onChange={(e) => setSearch(e.target.value)} />
      <select aria-label="กรองสถานะ Suite" value={activeFilter} onChange={(e) => setActiveFilter(e.target.value as "all" | "active" | "closed")}>
        <option value="active">เปิดใช้งาน</option>
        <option value="closed">ปิดแล้ว</option>
        <option value="all">ทั้งหมด</option>
      </select>
    </div>
    {suites.length ? <div className="table-wrap"><table><thead><tr><th>Code</th><th>ชื่อ</th><th>Case</th><th>สถานะ</th><th>สร้างเมื่อ</th><th></th></tr></thead><tbody>{suites.map((s) => <tr key={s.automationSuiteId}>
      <td><b>{s.suiteCode}</b></td>
      <td><span>{s.suiteName}</span>{s.description && <small>{s.description}</small>}</td>
      <td>{s.readyCaseCount}/{s.caseCount} Ready</td>
      <td><Badge tone={s.isActive ? "green" : "gray"}>{s.isActive ? "เปิดใช้งาน" : "ปิดแล้ว"}</Badge></td>
      <td>{formatThaiDateTime(s.createdAt)}</td>
      <td>{canRun && s.isActive && <button type="button" className="table-action" onClick={() => setRunSuiteFor(s)}>▶ รัน</button>}<button type="button" className="table-action" onClick={() => openDetail(s)}>รายละเอียด</button><button type="button" className="table-action" onClick={() => openHistory(s.automationSuiteId)}>ประวัติ</button>{canEdit && s.isActive && <button type="button" className="table-action" onClick={() => setEditSuite(s)}>แก้ไข</button>}{canEdit && <button type="button" className={`table-action${s.isActive ? " danger" : ""}`} onClick={() => toggleSuite(s)}>{s.isActive ? "ปิด" : "เปิด"}</button>}</td>
    </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Automation Suite</p><small>สร้าง Suite เพื่อรวม Automation Case ที่ต้องรันซ้ำเป็นชุด (Smoke/Regression)</small></div>}

    {detail && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-suite-detail-title" onMouseDown={() => setDetail(null)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2 id="automation-suite-detail-title">{detail.suiteCode} · {detail.suiteName}</h2><small>{detail.cases.length} case · Rev {detail.revisionNo} · <Badge tone={detail.isActive ? "green" : "gray"}>{detail.isActive ? "เปิดใช้งาน" : "ปิดแล้ว"}</Badge></small></div><button aria-label="ปิด" onClick={() => setDetail(null)}>×</button></div>
      {!detail.isActive && <div className="inline-alert"><span>Suite นี้ปิดแล้ว — ต้องเปิดใช้งานก่อนจึงจะแก้ไข Case ได้</span></div>}
      {detail.isActive && (canEdit || canRun) && <div className="acw-action-bar">
        {canRun && <button type="button" className="btn primary" onClick={() => setRunSuiteFor({ automationSuiteId: detail.automationSuiteId, suiteCode: detail.suiteCode, caseCount: detail.cases.length, readyCaseCount: detail.cases.filter((c) => c.status === "Ready").length })}>▶ รัน Suite</button>}
        {canEdit && <button type="button" className="btn" onClick={() => setAddCasesModal(true)}>＋ เพิ่ม Case</button>}
        <button type="button" className="btn" onClick={() => openHistory(detail.automationSuiteId)}>🕐 ประวัติ</button>
      </div>}
      {detail.cases.length ? <div className="table-wrap"><table><thead><tr><th>ลำดับ</th><th>Code</th><th>Test Case</th><th>Target</th><th>สถานะ</th><th>Required</th><th></th></tr></thead><tbody>{[...detail.cases].sort((a, b) => a.sortOrder - b.sortOrder).map((c, i, arr) => <tr key={c.automationCaseId}>
        <td>{canEdit && detail.isActive ? <span className="automation-sort-controls"><button type="button" className="table-action icon-btn" aria-label="เลื่อนขึ้น" disabled={i === 0} onClick={() => moveCase(c, -1)}>↑</button><button type="button" className="table-action icon-btn" aria-label="เลื่อนลง" disabled={i === arr.length - 1} onClick={() => moveCase(c, 1)}>↓</button></span> : c.sortOrder}</td>
        <td><b>{c.automationCode}</b></td>
        <td><span>{c.testCaseCode}</span><small>{c.testCaseTitle}</small></td>
        <td><Badge tone={targetTone[c.automationType] ?? "blue"}>{c.automationType}</Badge></td>
        <td><Badge tone={caseStatusTone[c.status] ?? "blue"}>{c.status}</Badge></td>
        <td>{canEdit && detail.isActive ? <button type="button" className="table-action" onClick={() => toggleRequired(c)}>{c.isRequired ? "Required" : "Optional"}</button> : <Badge tone={c.isRequired ? "blue" : "gray"}>{c.isRequired ? "Required" : "Optional"}</Badge>}</td>
        <td>{canEdit && detail.isActive && <button type="button" className="table-action danger" onClick={() => removeCase(c.automationCaseId)}>ลบ</button>}</td>
      </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Case ใน Suite นี้</p></div>}
      <div className="modal-actions"><button className="btn" onClick={() => setDetail(null)}>ปิดหน้าต่าง</button></div>
    </div></div>}

    {addCasesModal && detail && <AddSuiteCasesModal cases={cases} existingCaseIds={detail.cases.map((c) => c.automationCaseId)} busy={busy} onClose={() => setAddCasesModal(false)} onAdd={addCases} />}
    {createModal && <SuiteFormModal title="สร้าง Automation Suite" busy={busy} onClose={() => setCreateModal(false)} onSave={createSuite} />}
    {editSuite && <SuiteFormModal title={`แก้ไข ${editSuite.suiteCode}`} initialName={editSuite.suiteName} initialDescription={editSuite.description ?? ""} busy={busy} onClose={() => setEditSuite(null)} onSave={(_, name, desc, reason) => updateSuite(editSuite.automationSuiteId, name, desc, reason ?? "")} />}
    {history && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-suite-history-title" onMouseDown={() => setHistory(null)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2 id="automation-suite-history-title">ประวัติการแก้ไข (AUT-P1-003)</h2><small>{history.length} รายการ — ล่าสุดก่อน</small></div><button aria-label="ปิด" onClick={() => setHistory(null)}>×</button></div>
      {history.length ? <div className="automation-result-list">{history.map((h) => <div key={h.automationSuiteRevisionId} className="automation-failure-row">
        <b>Rev {h.revisionNo} · {h.changeType}</b>
        <span>{h.detail}</span>
        {h.changeReason && <span>เหตุผล: {h.changeReason}</span>}
        <span>{h.changedByName ?? (h.changedBy ? h.changedBy : "ระบบ")} · {formatThaiDateTime(h.changedAt)}</span>
      </div>)}</div> : <div className="empty"><p>ยังไม่มีประวัติ</p></div>}
      <div className="modal-actions"><button className="btn" onClick={() => setHistory(null)}>ปิดหน้าต่าง</button></div>
    </div></div>}

    {runSuiteFor && <RunSuiteModal suite={runSuiteFor} releaseId={releaseId} canRun={canRun} busy={busy} onClose={() => setRunSuiteFor(null)} onRun={(buildId, envId, priority) => runSuite(runSuiteFor.automationSuiteId, runSuiteFor.suiteCode, buildId, envId, priority)} onError={setError} />}

    {runResult && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-suite-run-result-title" onMouseDown={() => setRunResult(null)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2 id="automation-suite-run-result-title">สั่งรัน {runResult.suiteCode} แล้ว</h2></div><button aria-label="ปิด" onClick={() => setRunResult(null)}>×</button></div>
      <p>สร้าง Execution {runResult.created} รายการ</p>
      {runResult.skipped.length > 0 && <p>ข้าม {runResult.skipped.length} รายการ (ไม่ Ready หรือ Quarantined): {runResult.skipped.join(", ")}</p>}
      <div className="modal-actions"><button className="btn primary" onClick={() => setRunResult(null)}>ตกลง</button></div>
    </div></div>}
  </section>;
}

const DAY_LABELS: { value: number; label: string }[] = [
  { value: 0, label: "อา" }, { value: 1, label: "จ" }, { value: 2, label: "อ" }, { value: 3, label: "พ" },
  { value: 4, label: "พฤ" }, { value: 5, label: "ศ" }, { value: 6, label: "ส" },
];
const FALLBACK_TIMEZONES = ["UTC", "Asia/Bangkok", "Asia/Singapore", "Asia/Tokyo", "Asia/Ho_Chi_Minh", "Asia/Jakarta", "Asia/Kolkata", "Europe/London", "Europe/Paris", "America/New_York", "America/Los_Angeles"];
const timezoneOptions = (): string[] => {
  try {
    const supported = (Intl as unknown as { supportedValuesOf?: (key: string) => string[] }).supportedValuesOf?.("timeZone");
    if (supported && supported.length) return supported;
  } catch { /* older browser without Intl.supportedValuesOf — fall back to a curated list */ }
  return FALLBACK_TIMEZONES;
};
const describeSchedule = (s: { frequency: string; daysOfWeekMask: number; runAtTime: string; onceOnDate?: string }) => {
  const time = s.runAtTime.slice(0, 5);
  if (s.frequency === "Once") return `ครั้งเดียว ${s.onceOnDate ?? "-"} ${time}`;
  if (s.frequency === "Weekly") {
    const days = DAY_LABELS.filter((d) => (s.daysOfWeekMask & (1 << d.value)) !== 0).map((d) => d.label).join(",");
    return `ทุกสัปดาห์ (${days || "-"}) ${time}`;
  }
  return `ทุกวัน ${time}`;
};

function AutomationScheduleTab({ projectId, releaseId, headers, canEdit, agents, setExecDetail }: {
  projectId: string; releaseId?: string; headers: Record<string, string>; canEdit: boolean; agents: AutomationAgentItem[]; setExecDetail: (v: AutomationExecutionItem | null) => void;
}) {
  const [schedules, setSchedules] = useState<AutomationScheduleListItem[]>([]);
  const [activeFilter, setActiveFilter] = useState<"all" | "active" | "inactive">("active");
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [createModal, setCreateModal] = useState(false);
  const [editSchedule, setEditSchedule] = useState<AutomationScheduleDetailItem | null>(null);
  const [runHistory, setRunHistory] = useState<{ name: string; runs: AutomationScheduleRunItem[] } | null>(null);
  const [unreadCount, setUnreadCount] = useState(0);
  const [notifications, setNotifications] = useState<AutomationScheduleNotificationItem[] | null>(null);

  useEffect(() => {
    if (!projectId) return;
    const qs = new URLSearchParams({ projectId });
    if (activeFilter !== "all") qs.set("isActive", activeFilter === "active" ? "true" : "false");
    fetch(`${apiUrl}/automation/schedules?${qs}`, { headers }).then((r) => (r.ok ? r.json() : [])).then((s) => setSchedules(Array.isArray(s) ? s : [])).catch(() => setError("โหลด Schedule ไม่สำเร็จ"));
  }, [projectId, activeFilter, headers, reload]);

  useEffect(() => {
    if (!projectId) return;
    fetch(`${apiUrl}/automation/schedules/notifications/unread-count?projectId=${projectId}`, { headers }).then((r) => (r.ok ? r.json() : 0)).then((n) => setUnreadCount(typeof n === "number" ? n : 0)).catch(() => { /* badge just stays at its last known value */ });
  }, [projectId, headers, reload]);

  const openNotifications = async () => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/schedules/notifications?projectId=${projectId}&take=100`, { headers });
      if (!r.ok) throw new Error("โหลดการแจ้งเตือนไม่สำเร็จ");
      setNotifications(await r.json());
    } catch (e) { setError(e instanceof Error ? e.message : "โหลดการแจ้งเตือนไม่สำเร็จ"); }
  };

  const markNotificationRead = async (n: AutomationScheduleNotificationItem) => {
    if (n.isRead) return;
    try {
      await fetch(`${apiUrl}/automation/schedules/notifications/${n.automationScheduleNotificationId}/read?projectId=${projectId}`, { method: "POST", headers });
      setNotifications((prev) => prev?.map((x) => x.automationScheduleNotificationId === n.automationScheduleNotificationId ? { ...x, isRead: true } : x) ?? null);
      setUnreadCount((c) => Math.max(0, c - 1));
    } catch { /* best-effort — the notification just stays unread until the next open */ }
  };

  const markAllNotificationsRead = async () => {
    try {
      await fetch(`${apiUrl}/automation/schedules/notifications/mark-all-read?projectId=${projectId}`, { method: "POST", headers });
      setNotifications((prev) => prev?.map((x) => ({ ...x, isRead: true })) ?? null);
      setUnreadCount(0);
    } catch { /* best-effort */ }
  };

  const openNotificationExecution = async (n: AutomationScheduleNotificationItem) => {
    await markNotificationRead(n);
    try {
      const r = await fetch(`${apiUrl}/automation/executions/${n.automationExecutionId}?projectId=${projectId}`, { headers });
      if (!r.ok) throw new Error("โหลด Execution ไม่สำเร็จ");
      setExecDetail(await r.json());
      setNotifications(null);
    } catch (e) { setError(e instanceof Error ? e.message : "โหลด Execution ไม่สำเร็จ"); }
  };

  const openEdit = async (id: string) => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/schedules/${id}?projectId=${projectId}`, { headers });
      if (!r.ok) throw new Error("โหลด Schedule ไม่สำเร็จ");
      setEditSchedule(await r.json());
    } catch (e) { setError(e instanceof Error ? e.message : "โหลด Schedule ไม่สำเร็จ"); }
  };

  const createSchedule = async (body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/schedules?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Schedule ไม่สำเร็จ"); }
      setCreateModal(false); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "สร้าง Schedule ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const updateSchedule = async (id: string, body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/schedules/${id}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "แก้ไข Schedule ไม่สำเร็จ"); }
      setEditSchedule(null); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "แก้ไข Schedule ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const toggleActive = async (row: AutomationScheduleListItem) => {
    if (!window.confirm(`${row.isActive ? "ปิด" : "เปิด"}ใช้งาน Schedule "${row.name}"?`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/schedules/${row.automationScheduleId}/${row.isActive ? "deactivate" : "activate"}?projectId=${projectId}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เปลี่ยนสถานะ Schedule ไม่สำเร็จ"); }
      setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "เปลี่ยนสถานะ Schedule ไม่สำเร็จ"); }
  };

  const openRunHistory = async (row: AutomationScheduleListItem) => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/schedules/${row.automationScheduleId}/runs?projectId=${projectId}`, { headers });
      if (!r.ok) throw new Error("โหลดประวัติการรันไม่สำเร็จ");
      setRunHistory({ name: row.name, runs: await r.json() });
    } catch (e) { setError(e instanceof Error ? e.message : "โหลดประวัติการรันไม่สำเร็จ"); }
  };

  return <section className="automation-cases" aria-label="Automation Schedule">
    <header className="automation-section-head"><div><h2>Automation Schedule (AUT-P1-005)</h2><p>ตั้งเวลารัน Automation Suite ซ้ำอัตโนมัติ — Once/Daily/Weekly พร้อม timezone และคำนวณรอบถัดไป</p></div><div className="automation-section-head-actions"><button className="btn automation-notif-bell" type="button" onClick={openNotifications} aria-label="การแจ้งเตือน Schedule">🔔 การแจ้งเตือน{unreadCount > 0 && <Badge tone="red">{unreadCount}</Badge>}</button>{canEdit && <button className="btn primary" type="button" onClick={() => setCreateModal(true)}>＋ สร้าง Schedule</button>}</div></header>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    <div className="automation-case-toolbar">
      <select aria-label="กรองสถานะ Schedule" value={activeFilter} onChange={(e) => setActiveFilter(e.target.value as "all" | "active" | "inactive")}>
        <option value="active">เปิดใช้งาน</option>
        <option value="inactive">ปิดแล้ว</option>
        <option value="all">ทั้งหมด</option>
      </select>
    </div>
    {schedules.length ? <div className="table-wrap"><table><thead><tr><th>ชื่อ</th><th>Suite</th><th>ตารางเวลา</th><th>Timezone</th><th>รันครั้งถัดไป</th><th>รันล่าสุด</th><th>สถานะ</th><th></th></tr></thead><tbody>{schedules.map((s) => <tr key={s.automationScheduleId}>
      <td><b>{s.name}</b>{s.description && <small>{s.description}</small>}</td>
      <td>{s.suiteCode}</td>
      <td>{describeSchedule(s)}</td>
      <td>{s.timeZoneId}</td>
      <td>{s.isActive ? formatThaiDateTime(s.nextRunAtUtc) : "-"}</td>
      <td>{s.lastRunAtUtc ? formatThaiDateTime(s.lastRunAtUtc) : "ยังไม่เคยรัน"}</td>
      <td><Badge tone={s.isActive ? "green" : "gray"}>{s.isActive ? "เปิดใช้งาน" : "ปิดแล้ว"}</Badge></td>
      <td>{canEdit && <button type="button" className="table-action" onClick={() => openEdit(s.automationScheduleId)}>แก้ไข</button>}<button type="button" className="table-action" onClick={() => openRunHistory(s)}>ประวัติการรัน</button>{canEdit && <button type="button" className={`table-action${s.isActive ? " danger" : ""}`} onClick={() => toggleActive(s)}>{s.isActive ? "ปิด" : "เปิด"}</button>}</td>
    </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Automation Schedule</p><small>ตั้งเวลารัน Automation Suite ที่มีอยู่แล้วให้ทำงานซ้ำอัตโนมัติตามรอบที่กำหนด</small></div>}

    {createModal && <ScheduleFormModal projectId={projectId} releaseId={releaseId} headers={headers} agents={agents} busy={busy} onClose={() => setCreateModal(false)} onSave={createSchedule} />}
    {editSchedule && <ScheduleFormModal projectId={projectId} releaseId={releaseId} headers={headers} agents={agents} busy={busy} schedule={editSchedule} onClose={() => setEditSchedule(null)} onSave={(body) => updateSchedule(editSchedule.automationScheduleId, body)} />}

    {runHistory && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-schedule-run-history-title" onMouseDown={() => setRunHistory(null)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2 id="automation-schedule-run-history-title">ประวัติการรัน — {runHistory.name}</h2><small>{runHistory.runs.length} รายการ — ล่าสุดก่อน (AUT-P1-006)</small></div><button aria-label="ปิด" onClick={() => setRunHistory(null)}>×</button></div>
      {runHistory.runs.length ? <div className="automation-result-list">{runHistory.runs.map((r) => <div key={r.automationScheduleRunId} className="automation-failure-row">
        <b><Badge tone={r.status === "Succeeded" ? "green" : r.status === "NoReadyCases" ? "yellow" : "red"}>{r.status}</Badge> {formatThaiDateTime(r.firedAtUtc)}</b>
        <span>สร้าง Execution {r.executionsCreated} รายการ{r.skippedCount > 0 && ` · ข้าม ${r.skippedCount} รายการ`}</span>
        {r.errorMessage && <span>{r.errorMessage}</span>}
      </div>)}</div> : <div className="empty"><p>ยังไม่เคยถูกรันจาก Schedule นี้</p></div>}
      <div className="modal-actions"><button className="btn" onClick={() => setRunHistory(null)}>ปิดหน้าต่าง</button></div>
    </div></div>}

    {notifications && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-schedule-notif-title" onMouseDown={() => setNotifications(null)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2 id="automation-schedule-notif-title">การแจ้งเตือน Schedule (AUT-P1-009)</h2><small>{notifications.length} รายการ — Started/Completed/Failed/No Agent ล่าสุดก่อน</small></div><button aria-label="ปิด" onClick={() => setNotifications(null)}>×</button></div>
      {notifications.length > 0 && <div className="modal-actions" style={{ justifyContent: "flex-start" }}><button className="btn" type="button" onClick={markAllNotificationsRead}>ทำเครื่องหมายว่าอ่านแล้วทั้งหมด</button></div>}
      {notifications.length ? <div className="automation-result-list">{notifications.map((n) => <div key={n.automationScheduleNotificationId} className={`automation-failure-row automation-notif-item${n.isRead ? "" : " is-unread"}`}>
        <b><Badge tone={n.eventType === "Completed" ? "green" : n.eventType === "Failed" ? "red" : n.eventType === "NoAgent" ? "orange" : "blue"}>{n.eventType}</Badge> {n.scheduleName} · {n.automationCode}{!n.isRead && <Badge tone="gray">ใหม่</Badge>}</b>
        <span>{n.message}</span>
        <span className="muted-text">{formatThaiDateTime(n.createdAtUtc)}</span>
        <div className="modal-actions" style={{ justifyContent: "flex-start", padding: 0 }}>
          <button className="table-action" type="button" onClick={() => openNotificationExecution(n)}>ดู Execution</button>
          {!n.isRead && <button className="table-action" type="button" onClick={() => markNotificationRead(n)}>ทำเครื่องหมายว่าอ่านแล้ว</button>}
        </div>
      </div>)}</div> : <div className="empty"><p>ยังไม่มีการแจ้งเตือน</p><small>จะมีเมื่อ Schedule เริ่มรัน/รันเสร็จ/ล้มเหลว หรือรันแล้วไม่มี Agent ว่าง</small></div>}
      <div className="modal-actions"><button className="btn" onClick={() => setNotifications(null)}>ปิดหน้าต่าง</button></div>
    </div></div>}
  </section>;
}

function ScheduleFormModal({ projectId, releaseId, headers, agents, schedule, busy, onClose, onSave }: {
  projectId: string; releaseId?: string; headers: Record<string, string>; agents: AutomationAgentItem[]; schedule?: AutomationScheduleDetailItem; busy: boolean; onClose: () => void; onSave: (body: Record<string, unknown>) => void;
}) {
  const isEdit = !!schedule;
  const [suites, setSuites] = useState<{ automationSuiteId: string; suiteCode: string; suiteName: string }[]>([]);
  const [builds, setBuilds] = useState<BuildOption[]>([]);
  const [environments, setEnvironments] = useState<EnvironmentOption[]>([]);
  const [automationSuiteId, setAutomationSuiteId] = useState(schedule?.automationSuiteId ?? "");
  const [name, setName] = useState(schedule?.name ?? "");
  const [description, setDescription] = useState(schedule?.description ?? "");
  const [frequency, setFrequency] = useState(schedule?.frequency ?? "Daily");
  const [daysOfWeekMask, setDaysOfWeekMask] = useState(schedule?.daysOfWeekMask ?? 0);
  const [runAtTime, setRunAtTime] = useState((schedule?.runAtTime ?? "09:00").slice(0, 5));
  const [onceOnDate, setOnceOnDate] = useState(schedule?.onceOnDate ?? "");
  const [timeZoneId, setTimeZoneId] = useState(schedule?.timeZoneId ?? "Asia/Bangkok");
  const [buildId, setBuildId] = useState(schedule?.buildId ?? "");
  const [environmentId, setEnvironmentId] = useState(schedule?.environmentId ?? "");
  const [agentId, setAgentId] = useState(schedule?.agentId ?? "");
  const [priority, setPriority] = useState(schedule?.priority ?? 5);

  useEffect(() => {
    let mounted = true;
    Promise.all([
      fetch(`${apiUrl}/automation/suites?projectId=${projectId}&isActive=true`, { headers }).then((r) => (r.ok ? r.json() : [])),
      releaseId ? fetch(`${apiUrl}/releases/${releaseId}/builds`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : [])) : Promise.resolve([]),
      fetch(`${apiUrl}/master-settings/environments`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : [])),
    ]).then(([su, b, e]) => {
      if (!mounted) return;
      setSuites(Array.isArray(su) ? su : []);
      setBuilds(Array.isArray(b) ? b : []);
      setEnvironments(Array.isArray(e) ? (e as EnvironmentOption[]).filter((x) => x.isActive) : []);
    }).catch(() => { /* selects just render empty — the inline error banner elsewhere already covers fetch failures for this tab */ });
    return () => { mounted = false; };
  }, [projectId, releaseId, headers]);

  const toggleDay = (value: number) => setDaysOfWeekMask((prev) => (prev & (1 << value)) !== 0 ? prev & ~(1 << value) : prev | (1 << value));

  const canSave = name.trim() && automationSuiteId && buildId && environmentId
    && (frequency !== "Weekly" || daysOfWeekMask > 0)
    && (frequency !== "Once" || onceOnDate);

  const save = () => onSave({
    automationSuiteId, name: name.trim(), description: description.trim() || null, frequency,
    daysOfWeekMask: frequency === "Weekly" ? daysOfWeekMask : 0,
    runAtTime: runAtTime.length === 5 ? `${runAtTime}:00` : runAtTime,
    onceOnDate: frequency === "Once" ? onceOnDate : null,
    timeZoneId, buildId, environmentId, agentId: agentId || null, priority,
  });

  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-schedule-form-title" onMouseDown={() => !busy && onClose()}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
    <div className="modal-head"><div><h2 id="automation-schedule-form-title">{isEdit ? `แก้ไข ${schedule!.name}` : "สร้าง Automation Schedule"}</h2></div><button aria-label="ปิด" disabled={busy} onClick={onClose}>×</button></div>
    <div className="form-grid">
      <label className="full">Automation Suite{isEdit ? <input value={`${schedule!.suiteCode} · ${schedule!.suiteName}`} disabled /> : <select value={automationSuiteId} onChange={(e) => setAutomationSuiteId(e.target.value)}><option value="">เลือก Suite</option>{suites.map((s) => <option key={s.automationSuiteId} value={s.automationSuiteId}>{s.suiteCode} · {s.suiteName}</option>)}</select>}</label>
      <label className="full">ชื่อ Schedule<input value={name} onChange={(e) => setName(e.target.value)} placeholder="เช่น Nightly Smoke" /></label>
      <label className="full">คำอธิบาย (ไม่บังคับ)<textarea rows={2} value={description} onChange={(e) => setDescription(e.target.value)} /></label>
      <label>ความถี่<select value={frequency} onChange={(e) => setFrequency(e.target.value)}><option value="Daily">ทุกวัน</option><option value="Weekly">ทุกสัปดาห์</option><option value="Once">ครั้งเดียว</option></select></label>
      <label>เวลา (ตาม Timezone ที่เลือก)<input type="time" value={runAtTime} onChange={(e) => setRunAtTime(e.target.value)} /></label>
      {frequency === "Weekly" && <div className="full form-grid-label-like"><span>วันในสัปดาห์</span><div className="automation-days-row">{DAY_LABELS.map((d) => <label key={d.value}><input type="checkbox" checked={(daysOfWeekMask & (1 << d.value)) !== 0} onChange={() => toggleDay(d.value)} />{d.label}</label>)}</div></div>}
      {frequency === "Once" && <label>วันที่<input type="date" value={onceOnDate} onChange={(e) => setOnceOnDate(e.target.value)} /></label>}
      <label>Timezone<select value={timeZoneId} onChange={(e) => setTimeZoneId(e.target.value)}>{timezoneOptions().map((tz) => <option key={tz} value={tz}>{tz}</option>)}</select></label>
      <label>Build<select value={buildId} onChange={(e) => setBuildId(e.target.value)}><option value="">เลือก Build</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}</option>)}</select></label>
      <label>Environment<select value={environmentId} onChange={(e) => setEnvironmentId(e.target.value)}><option value="">เลือก Env</option>{environments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>
      <label>Agent (ไม่บังคับ)<select value={agentId} onChange={(e) => setAgentId(e.target.value)}><option value="">Agent ใดก็ได้</option>{agents.map((a) => <option key={a.agentId} value={a.agentId}>{a.agentCode}</option>)}</select></label>
      <label>Priority<select value={priority} onChange={(e) => setPriority(Number(e.target.value))}>{[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((p) => <option key={p} value={p}>{p}</option>)}</select></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}>ยกเลิก</button><button className="btn primary" disabled={busy || !canSave} onClick={save}>{busy ? "กำลังบันทึก..." : "บันทึก"}</button></div>
  </div></div>;
}

const packTone = (pack: string) => (pack === "Smoke" ? "blue" : "purple");

function AutomationBuildTriggerTab({ projectId, headers, canEdit, agents }: {
  projectId: string; headers: Record<string, string>; canEdit: boolean; agents: AutomationAgentItem[];
}) {
  const [policies, setPolicies] = useState<AutomationBuildTriggerPolicyItem[]>([]);
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [createModal, setCreateModal] = useState(false);
  const [editPolicy, setEditPolicy] = useState<AutomationBuildTriggerPolicyItem | null>(null);
  const [runHistory, setRunHistory] = useState<{ label: string; runs: AutomationBuildTriggerRunItem[] } | null>(null);

  useEffect(() => {
    if (!projectId) return;
    fetch(`${apiUrl}/automation/build-triggers?projectId=${projectId}`, { headers }).then((r) => (r.ok ? r.json() : [])).then((p) => setPolicies(Array.isArray(p) ? p : [])).catch(() => setError("โหลด Build Trigger ไม่สำเร็จ"));
  }, [projectId, headers, reload]);

  const createPolicy = async (body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/build-triggers?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Build Trigger ไม่สำเร็จ"); }
      setCreateModal(false); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "สร้าง Build Trigger ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const updatePolicy = async (id: string, body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/build-triggers/${id}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "แก้ไข Build Trigger ไม่สำเร็จ"); }
      setEditPolicy(null); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "แก้ไข Build Trigger ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const toggleActive = async (row: AutomationBuildTriggerPolicyItem) => {
    if (!window.confirm(`${row.isActive ? "ปิด" : "เปิด"}ใช้งาน Build Trigger "${row.pack} · ${row.suiteCode}"?`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/build-triggers/${row.automationBuildTriggerPolicyId}/${row.isActive ? "deactivate" : "activate"}?projectId=${projectId}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เปลี่ยนสถานะไม่สำเร็จ"); }
      setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "เปลี่ยนสถานะไม่สำเร็จ"); }
  };

  const openRunHistory = async (row: AutomationBuildTriggerPolicyItem) => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/build-triggers/${row.automationBuildTriggerPolicyId}/runs?projectId=${projectId}`, { headers });
      if (!r.ok) throw new Error("โหลดประวัติการรันไม่สำเร็จ");
      setRunHistory({ label: `${row.pack} · ${row.suiteCode}`, runs: await r.json() });
    } catch (e) { setError(e instanceof Error ? e.message : "โหลดประวัติการรันไม่สำเร็จ"); }
  };

  return <section className="automation-cases" aria-label="Automation Build Trigger">
    <header className="automation-section-head"><div><h2>Build Trigger (AUT-P1-007)</h2><p>Build ใหม่รัน Suite อัตโนมัติตาม policy — Smoke รันทุก Build ใหม่, Regression รันเมื่อ Build ถูกตั้งเป็น Release Candidate</p></div>{canEdit && <button className="btn primary" type="button" onClick={() => setCreateModal(true)}>＋ สร้าง Policy</button>}</header>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    {policies.length ? <div className="table-wrap"><table><thead><tr><th>Pack</th><th>Suite</th><th>Environment</th><th>Agent</th><th>Priority</th><th>สถานะ</th><th></th></tr></thead><tbody>{policies.map((p) => <tr key={p.automationBuildTriggerPolicyId}>
      <td><Badge tone={packTone(p.pack)}>{p.pack}</Badge></td>
      <td>{p.suiteCode}</td>
      <td>{p.environmentName}</td>
      <td>{p.agentCode ?? "Agent ใดก็ได้"}</td>
      <td>{p.priority}</td>
      <td><Badge tone={p.isActive ? "green" : "gray"}>{p.isActive ? "เปิดใช้งาน" : "ปิดแล้ว"}</Badge></td>
      <td>{canEdit && <button type="button" className="table-action" onClick={() => setEditPolicy(p)}>แก้ไข</button>}<button type="button" className="table-action" onClick={() => openRunHistory(p)}>ประวัติการรัน</button>{canEdit && <button type="button" className={`table-action${p.isActive ? " danger" : ""}`} onClick={() => toggleActive(p)}>{p.isActive ? "ปิด" : "เปิด"}</button>}</td>
    </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Build Trigger Policy</p><small>ตั้ง policy ให้ Build ใหม่รัน Smoke/Regression Suite อัตโนมัติโดยไม่ต้องสั่งรันเอง</small></div>}

    {createModal && <BuildTriggerFormModal projectId={projectId} headers={headers} agents={agents} busy={busy} onClose={() => setCreateModal(false)} onSave={createPolicy} />}
    {editPolicy && <BuildTriggerFormModal projectId={projectId} headers={headers} agents={agents} busy={busy} policy={editPolicy} onClose={() => setEditPolicy(null)} onSave={(body) => updatePolicy(editPolicy.automationBuildTriggerPolicyId, body)} />}

    {runHistory && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-build-trigger-run-history-title" onMouseDown={() => setRunHistory(null)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2 id="automation-build-trigger-run-history-title">ประวัติการรัน — {runHistory.label}</h2><small>{runHistory.runs.length} รายการ — ล่าสุดก่อน</small></div><button aria-label="ปิด" onClick={() => setRunHistory(null)}>×</button></div>
      {runHistory.runs.length ? <div className="automation-result-list">{runHistory.runs.map((r) => <div key={r.automationBuildTriggerRunId} className="automation-failure-row">
        <b><Badge tone={r.status === "Succeeded" ? "green" : r.status === "NoReadyCases" ? "yellow" : "red"}>{r.status}</Badge> Build {r.buildNumber} · {formatThaiDateTime(r.firedAtUtc)}</b>
        <span>สร้าง Execution {r.executionsCreated} รายการ{r.skippedCount > 0 && ` · ข้าม ${r.skippedCount} รายการ`}</span>
        {r.errorMessage && <span>{r.errorMessage}</span>}
      </div>)}</div> : <div className="empty"><p>ยังไม่เคยถูกรันจาก Policy นี้</p></div>}
      <div className="modal-actions"><button className="btn" onClick={() => setRunHistory(null)}>ปิดหน้าต่าง</button></div>
    </div></div>}
  </section>;
}

function BuildTriggerFormModal({ projectId, headers, agents, policy, busy, onClose, onSave }: {
  projectId: string; headers: Record<string, string>; agents: AutomationAgentItem[]; policy?: AutomationBuildTriggerPolicyItem; busy: boolean; onClose: () => void; onSave: (body: Record<string, unknown>) => void;
}) {
  const isEdit = !!policy;
  const [suites, setSuites] = useState<{ automationSuiteId: string; suiteCode: string; suiteName: string }[]>([]);
  const [environments, setEnvironments] = useState<EnvironmentOption[]>([]);
  const [automationSuiteId, setAutomationSuiteId] = useState(policy?.automationSuiteId ?? "");
  const [pack, setPack] = useState(policy?.pack ?? "Smoke");
  const [environmentId, setEnvironmentId] = useState(policy?.environmentId ?? "");
  const [agentId, setAgentId] = useState(policy?.agentId ?? "");
  const [priority, setPriority] = useState(policy?.priority ?? 5);

  useEffect(() => {
    let mounted = true;
    Promise.all([
      fetch(`${apiUrl}/automation/suites?projectId=${projectId}&isActive=true`, { headers }).then((r) => (r.ok ? r.json() : [])),
      fetch(`${apiUrl}/master-settings/environments`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : [])),
    ]).then(([su, e]) => {
      if (!mounted) return;
      setSuites(Array.isArray(su) ? su : []);
      setEnvironments(Array.isArray(e) ? (e as EnvironmentOption[]).filter((x) => x.isActive) : []);
    }).catch(() => { /* selects just render empty — the inline error banner elsewhere already covers fetch failures for this tab */ });
    return () => { mounted = false; };
  }, [projectId, headers]);

  const canSave = automationSuiteId && environmentId;
  const save = () => onSave({ automationSuiteId, pack, environmentId, agentId: agentId || null, priority });

  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-build-trigger-form-title" onMouseDown={() => !busy && onClose()}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
    <div className="modal-head"><div><h2 id="automation-build-trigger-form-title">{isEdit ? "แก้ไข Build Trigger Policy" : "สร้าง Build Trigger Policy"}</h2></div><button aria-label="ปิด" disabled={busy} onClick={onClose}>×</button></div>
    <div className="form-grid">
      <label className="full">Automation Suite<select value={automationSuiteId} onChange={(e) => setAutomationSuiteId(e.target.value)}><option value="">เลือก Suite</option>{suites.map((s) => <option key={s.automationSuiteId} value={s.automationSuiteId}>{s.suiteCode} · {s.suiteName}</option>)}</select></label>
      <label>Pack<select value={pack} onChange={(e) => setPack(e.target.value)}><option value="Smoke">Smoke (รันทุก Build ใหม่)</option><option value="Regression">Regression (รันเมื่อตั้งเป็น Release Candidate)</option></select></label>
      <label>Environment<select value={environmentId} onChange={(e) => setEnvironmentId(e.target.value)}><option value="">เลือก Env</option>{environments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>
      <label>Agent (ไม่บังคับ)<select value={agentId} onChange={(e) => setAgentId(e.target.value)}><option value="">Agent ใดก็ได้</option>{agents.map((a) => <option key={a.agentId} value={a.agentId}>{a.agentCode}</option>)}</select></label>
      <label>Priority<select value={priority} onChange={(e) => setPriority(Number(e.target.value))}>{[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((p) => <option key={p} value={p}>{p}</option>)}</select></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}>ยกเลิก</button><button className="btn primary" disabled={busy || !canSave} onClick={save}>{busy ? "กำลังบันทึก..." : "บันทึก"}</button></div>
  </div></div>;
}

function AutomationWebhookTab({ projectId, headers, canEdit }: { projectId: string; headers: Record<string, string>; canEdit: boolean }) {
  const [tokens, setTokens] = useState<AutomationWebhookTokenItem[]>([]);
  const [deliveries, setDeliveries] = useState<AutomationWebhookDeliveryItem[]>([]);
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [createModal, setCreateModal] = useState(false);
  const [newToken, setNewToken] = useState<{ name: string; plainTextToken: string } | null>(null);

  useEffect(() => {
    if (!projectId) return;
    fetch(`${apiUrl}/automation/webhook-tokens?projectId=${projectId}`, { headers }).then((r) => (r.ok ? r.json() : [])).then((t) => setTokens(Array.isArray(t) ? t : [])).catch(() => setError("โหลด Webhook Token ไม่สำเร็จ"));
    fetch(`${apiUrl}/automation/webhook-tokens/deliveries?projectId=${projectId}`, { headers }).then((r) => (r.ok ? r.json() : [])).then((d) => setDeliveries(Array.isArray(d) ? d : [])).catch(() => setError("โหลดประวัติ Webhook ไม่สำเร็จ"));
  }, [projectId, headers, reload]);

  const createToken = async (name: string) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/webhook-tokens?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ name }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Token ไม่สำเร็จ"); }
      const result: { token: AutomationWebhookTokenItem; plainTextToken: string } = await r.json();
      setCreateModal(false); setReload((v) => v + 1);
      setNewToken({ name: result.token.name, plainTextToken: result.plainTextToken });
    } catch (e) { setError(e instanceof Error ? e.message : "สร้าง Token ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const revokeToken = async (row: AutomationWebhookTokenItem) => {
    if (!window.confirm(`เพิกถอน Token "${row.name}"? ระบบ CI/CD ที่ใช้ Token นี้จะเรียก webhook ไม่ได้อีก`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/webhook-tokens/${row.automationWebhookTokenId}/revoke?projectId=${projectId}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เพิกถอน Token ไม่สำเร็จ"); }
      setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "เพิกถอน Token ไม่สำเร็จ"); }
  };

  return <section className="automation-cases" aria-label="Automation Webhook">
    <header className="automation-section-head"><div><h2>CI/CD Webhook (AUT-P1-008)</h2><p>ให้ CI/CD ยิง Build เข้ามาสร้างอัตโนมัติผ่าน webhook ที่ authenticate ด้วย Token — trigger Smoke/Regression ต่อเนื่องจาก Build Trigger ได้ทันที</p></div>{canEdit && <button className="btn primary" type="button" onClick={() => setCreateModal(true)}>＋ สร้าง Token</button>}</header>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    <h3>Webhook Token</h3>
    {tokens.length ? <div className="table-wrap"><table><thead><tr><th>ชื่อ</th><th>Token</th><th>สร้างเมื่อ</th><th>ใช้ล่าสุด</th><th>สถานะ</th><th></th></tr></thead><tbody>{tokens.map((t) => <tr key={t.automationWebhookTokenId}>
      <td><b>{t.name}</b></td>
      <td><code>{t.tokenPrefix}…</code></td>
      <td>{formatThaiDateTime(t.createdAt)}</td>
      <td>{t.lastUsedAtUtc ? formatThaiDateTime(t.lastUsedAtUtc) : "ยังไม่เคยใช้"}</td>
      <td><Badge tone={t.isActive ? "green" : "gray"}>{t.isActive ? "ใช้งานได้" : "เพิกถอนแล้ว"}</Badge></td>
      <td>{canEdit && t.isActive && <button type="button" className="table-action danger" onClick={() => revokeToken(t)}>เพิกถอน</button>}</td>
    </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Webhook Token</p><small>สร้าง Token ให้ระบบ CI/CD ใช้ authenticate ตอนยิง webhook เข้ามาสร้าง Build</small></div>}

    <h3>ประวัติการเรียก Webhook</h3>
    {deliveries.length ? <div className="table-wrap"><table><thead><tr><th>Token</th><th>Request ID</th><th>Build</th><th>สถานะ</th><th>เวลา</th></tr></thead><tbody>{deliveries.map((d) => <tr key={d.automationWebhookDeliveryId}>
      <td>{d.tokenName}</td>
      <td><code>{d.requestId}</code></td>
      <td>{d.buildNumber ?? "-"}</td>
      <td><Badge tone={d.status === "Created" ? "green" : d.status === "Duplicate" ? "yellow" : "red"}>{d.status}</Badge>{d.errorMessage && <small>{d.errorMessage}</small>}</td>
      <td>{formatThaiDateTime(d.receivedAtUtc)}</td>
    </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่เคยมี webhook เรียกเข้ามา</p></div>}

    {createModal && <WebhookTokenFormModal busy={busy} onClose={() => setCreateModal(false)} onSave={createToken} />}
    {newToken && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-webhook-new-token-title" onMouseDown={() => setNewToken(null)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2 id="automation-webhook-new-token-title">สร้าง Token "{newToken.name}" สำเร็จ</h2></div><button aria-label="ปิด" onClick={() => setNewToken(null)}>×</button></div>
      <div className="inline-alert">⚠ คัดลอก Token นี้เก็บไว้ตอนนี้ — ระบบจะไม่แสดง Token เต็มให้ดูอีกครั้ง</div>
      <p><code>{newToken.plainTextToken}</code></p>
      <p>ใส่ header <code>X-Webhook-Token</code> เวลายิงมาที่ <code>POST /api/v1/webhooks/automation/builds</code> พร้อม <code>releaseId</code>/<code>buildNumber</code>/<code>requestId</code> (idempotency key ป้องกัน trigger ซ้ำ)</p>
      <div className="modal-actions"><button className="btn primary" onClick={() => setNewToken(null)}>คัดลอกแล้ว ปิดหน้าต่าง</button></div>
    </div></div>}
  </section>;
}

function WebhookTokenFormModal({ busy, onClose, onSave }: { busy: boolean; onClose: () => void; onSave: (name: string) => void }) {
  const [name, setName] = useState("");
  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-webhook-token-form-title" onMouseDown={() => !busy && onClose()}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
    <div className="modal-head"><div><h2 id="automation-webhook-token-form-title">สร้าง Webhook Token</h2></div><button aria-label="ปิด" disabled={busy} onClick={onClose}>×</button></div>
    <div className="form-grid">
      <label className="full">ชื่อ (สำหรับระบุ เช่นชื่อระบบ CI/CD)<input value={name} onChange={(e) => setName(e.target.value)} placeholder="เช่น Jenkins Nightly" /></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}>ยกเลิก</button><button className="btn primary" disabled={busy || !name.trim()} onClick={() => onSave(name.trim())}>{busy ? "กำลังสร้าง..." : "สร้าง"}</button></div>
  </div></div>;
}

const snapshotStatusTone: Record<string, string> = { Requested: "gray", Running: "blue", Succeeded: "green", Failed: "red" };

function formatBytes(bytes?: number): string {
  if (bytes === undefined || bytes === null) return "-";
  if (bytes < 1024) return `${bytes} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let value = bytes / 1024;
  let i = 0;
  while (value >= 1024 && i < units.length - 1) { value /= 1024; i++; }
  return `${value.toFixed(1)} ${units[i]}`;
}

function AutomationDataSnapshotTab({ projectId, releaseId, headers, canRun }: {
  projectId: string; releaseId?: string; headers: Record<string, string>; canRun: boolean;
}) {
  const [snapshots, setSnapshots] = useState<AutomationDbSnapshotItem[]>([]);
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [requestModal, setRequestModal] = useState(false);
  const [detail, setDetail] = useState<AutomationDbSnapshotItem | null>(null);
  const [restoreHistory, setRestoreHistory] = useState<AutomationDbRestoreItem[]>([]);

  useEffect(() => {
    if (!projectId) return;
    fetch(`${apiUrl}/automation/data/snapshots?projectId=${projectId}`, { headers }).then((r) => (r.ok ? r.json() : [])).then((s) => setSnapshots(Array.isArray(s) ? s : [])).catch(() => setError("โหลด Snapshot ไม่สำเร็จ"));
  }, [projectId, headers, reload]);

  const requestSnapshot = async (environmentId: string, buildId: string) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/snapshots?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ environmentId, buildId }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "ขอ Snapshot ไม่สำเร็จ"); }
      setRequestModal(false); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "ขอ Snapshot ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const openDetail = async (s: AutomationDbSnapshotItem) => {
    setDetail(s); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/restores?projectId=${projectId}&automationDbSnapshotId=${s.automationDbSnapshotId}`, { headers });
      setRestoreHistory(r.ok ? await r.json() : []);
    } catch { setRestoreHistory([]); }
  };

  const requestRestore = async (s: AutomationDbSnapshotItem) => {
    if (!window.confirm(`ยืนยัน restore ฐานข้อมูลจริงของ "${s.environmentName}" กลับไปที่ snapshot นี้ (build ${s.buildNumber})?\n\n⚠ ข้อมูลปัจจุบันใน DB ของ Environment นี้จะถูกทับทั้งหมด — ย้อนกลับไม่ได้`)) return;
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/restores?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ automationDbSnapshotId: s.automationDbSnapshotId }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "ขอ Restore ไม่สำเร็จ"); }
      if (detail?.automationDbSnapshotId === s.automationDbSnapshotId) await openDetail(s);
    } catch (e) { setError(e instanceof Error ? e.message : "ขอ Restore ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  return <section className="automation-cases" aria-label="Automation DB Snapshot">
    <header className="automation-section-head"><div><h2>Database Snapshot &amp; Restore (AUT-DATA-001/002)</h2><p>ขอ backup ฐานข้อมูลจริงของ Environment ก่อนรัน และ restore กลับได้ภายหลัง — Windows Agent เป็นผู้ backup/restore จริง (gbak สำหรับ Firebird / BACKUP-RESTORE DATABASE สำหรับ SQL Server) ผ่านคำสั่ง <code>runner snapshot</code>/<code>runner restore</code> บนเครื่อง Agent</p></div>{canRun && <button className="btn primary" type="button" onClick={() => setRequestModal(true)}>＋ ขอ Snapshot</button>}</header>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    {snapshots.length ? <div className="table-wrap"><table><thead><tr><th>Environment</th><th>Build</th><th>สถานะ</th><th>DB</th><th>Agent</th><th>ขนาด</th><th>ขอเมื่อ</th><th></th></tr></thead><tbody>{snapshots.map((s) => <tr key={s.automationDbSnapshotId}>
      <td>{s.environmentName}</td>
      <td>{s.buildNumber}</td>
      <td><Badge tone={snapshotStatusTone[s.status] ?? "blue"}>{s.status}</Badge></td>
      <td>{s.dbKind ?? "-"}</td>
      <td>{s.agentCode ?? "-"}</td>
      <td>{formatBytes(s.sizeBytes)}</td>
      <td>{formatThaiDateTime(s.requestedAt)}</td>
      <td><button type="button" className="table-action" onClick={() => openDetail(s)}>รายละเอียด</button>{canRun && s.status === "Succeeded" && <button type="button" className="table-action danger" onClick={() => requestRestore(s)}>↺ Restore</button>}</td>
    </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Snapshot</p><small>ขอ Snapshot ก่อนรันชุด Automation เพื่อให้เริ่มจาก data state ที่รู้จักได้แน่นอน และ restore ได้ภายหลัง (AUT-DATA-002)</small></div>}

    {requestModal && <SnapshotRequestModal projectId={projectId} releaseId={releaseId} headers={headers} busy={busy} onClose={() => setRequestModal(false)} onSave={requestSnapshot} />}

    {detail && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-snapshot-detail-title" onMouseDown={() => setDetail(null)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2 id="automation-snapshot-detail-title">Snapshot — {detail.environmentName} / {detail.buildNumber}</h2><small><Badge tone={snapshotStatusTone[detail.status] ?? "blue"}>{detail.status}</Badge></small></div><button aria-label="ปิด" onClick={() => setDetail(null)}>×</button></div>
      <div className="automation-result-list">
        <div className="automation-failure-row"><b>ขอเมื่อ</b><span>{formatThaiDateTime(detail.requestedAt)}{detail.requestedBy ? ` · โดย ${detail.requestedBy}` : ""}</span></div>
        {detail.startedAt && <div className="automation-failure-row"><b>Agent เริ่ม backup</b><span>{formatThaiDateTime(detail.startedAt)}{detail.agentCode ? ` · ${detail.agentCode}` : ""}</span></div>}
        {detail.completedAt && <div className="automation-failure-row"><b>เสร็จสิ้น</b><span>{formatThaiDateTime(detail.completedAt)}</span></div>}
        {detail.status === "Succeeded" && <>
          <div className="automation-failure-row"><b>DB</b><span>{detail.dbKind} · {formatBytes(detail.sizeBytes)}</span></div>
          <div className="automation-failure-row"><b>ไฟล์ (บนเครื่อง Agent)</b><span><code>{detail.snapshotPath}</code></span></div>
          <div className="automation-failure-row"><b>Checksum (SHA-256)</b><span><code>{detail.checksum}</code></span></div>
        </>}
        {detail.status === "Failed" && <div className="automation-failure-row"><b>Error</b><span>{detail.errorMessage}</span></div>}
      </div>

      <h3>ประวัติการ Restore</h3>
      {restoreHistory.length ? <div className="automation-result-list">{restoreHistory.map((r) => <div key={r.automationDbRestoreId} className="automation-failure-row">
        <b><Badge tone={snapshotStatusTone[r.status] ?? "blue"}>{r.status}</Badge> {formatThaiDateTime(r.requestedAt)}{r.agentCode ? ` · ${r.agentCode}` : ""}</b>
        <span>Checksum: <Badge tone={r.checksumVerified ? "green" : "gray"}>{r.checksumVerified ? "ตรวจแล้ว" : "-"}</Badge> · ความพร้อมใช้งาน: <Badge tone={r.availabilityVerified ? "green" : "gray"}>{r.availabilityVerified ? "ตรวจแล้ว" : "-"}</Badge></span>
        {r.errorMessage && <span>{r.errorMessage}</span>}
      </div>)}</div> : <div className="empty"><p>ยังไม่เคย Restore จาก Snapshot นี้</p></div>}
      {canRun && detail.status === "Succeeded" && <div className="modal-actions" style={{ justifyContent: "flex-start" }}><button className="btn danger" disabled={busy} type="button" onClick={() => requestRestore(detail)}>↺ ขอ Restore จาก Snapshot นี้</button></div>}

      <div className="modal-actions"><button className="btn" onClick={() => setDetail(null)}>ปิดหน้าต่าง</button></div>
    </div></div>}
  </section>;
}

function SnapshotRequestModal({ projectId, releaseId, headers, busy, onClose, onSave }: {
  projectId: string; releaseId?: string; headers: Record<string, string>; busy: boolean; onClose: () => void; onSave: (environmentId: string, buildId: string) => void;
}) {
  const [builds, setBuilds] = useState<BuildOption[]>([]);
  const [environments, setEnvironments] = useState<EnvironmentOption[]>([]);
  const [environmentId, setEnvironmentId] = useState("");
  const [buildId, setBuildId] = useState("");

  useEffect(() => {
    let mounted = true;
    Promise.all([
      releaseId ? fetch(`${apiUrl}/releases/${releaseId}/builds`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : [])) : Promise.resolve([]),
      fetch(`${apiUrl}/master-settings/environments`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : [])),
    ]).then(([b, e]) => {
      if (!mounted) return;
      setBuilds(Array.isArray(b) ? b : []);
      setEnvironments(Array.isArray(e) ? (e as EnvironmentOption[]).filter((x) => x.isActive) : []);
    }).catch(() => { /* selects just render empty — the inline error banner elsewhere already covers fetch failures for this tab */ });
    return () => { mounted = false; };
  }, [projectId, releaseId, headers]);

  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-snapshot-request-title" onMouseDown={() => !busy && onClose()}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
    <div className="modal-head"><div><h2 id="automation-snapshot-request-title">ขอ Database Snapshot</h2><small>Windows Agent จะ backup ฐานข้อมูลจริงและรายงานผลกลับมาที่นี่</small></div><button aria-label="ปิด" disabled={busy} onClick={onClose}>×</button></div>
    <div className="form-grid">
      <label>Environment<select value={environmentId} onChange={(e) => setEnvironmentId(e.target.value)}><option value="">เลือก Environment</option>{environments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>
      <label>Build<select value={buildId} onChange={(e) => setBuildId(e.target.value)}><option value="">เลือก Build</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}</option>)}</select></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}>ยกเลิก</button><button className="btn primary" disabled={busy || !environmentId || !buildId} onClick={() => onSave(environmentId, buildId)}>{busy ? "กำลังส่งคำขอ..." : "ขอ Snapshot"}</button></div>
  </div></div>;
}

const scriptTypeTone: Record<string, string> = { Seed: "blue", Cleanup: "orange", MasterData: "purple" };
const approvalStatusTone: Record<string, string> = { Pending: "gray", Approved: "green", Rejected: "red" };

function AutomationDataSeedTab({ projectId, releaseId, headers, canEdit, canRun }: {
  projectId: string; releaseId?: string; headers: Record<string, string>; canEdit: boolean; canRun: boolean;
}) {
  const [scripts, setScripts] = useState<AutomationDataSeedScriptListItem[]>([]);
  const [typeFilter, setTypeFilter] = useState<"all" | "Seed" | "Cleanup" | "MasterData">("all");
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [createModal, setCreateModal] = useState(false);
  const [editScript, setEditScript] = useState<AutomationDataSeedScriptDetailItem | null>(null);
  const [runModal, setRunModal] = useState<AutomationDataSeedScriptListItem | null>(null);
  const [runHistory, setRunHistory] = useState<{ name: string; runs: AutomationDataSeedRunItem[] } | null>(null);

  useEffect(() => {
    if (!projectId) return;
    const qs = new URLSearchParams({ projectId });
    if (typeFilter !== "all") qs.set("scriptType", typeFilter);
    fetch(`${apiUrl}/automation/data/seed-scripts?${qs}`, { headers }).then((r) => (r.ok ? r.json() : [])).then((s) => setScripts(Array.isArray(s) ? s : [])).catch(() => setError("โหลด Script ไม่สำเร็จ"));
  }, [projectId, typeFilter, headers, reload]);

  const openEdit = async (id: string) => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-scripts/${id}?projectId=${projectId}`, { headers });
      if (!r.ok) throw new Error("โหลด Seed Script ไม่สำเร็จ");
      setEditScript(await r.json());
    } catch (e) { setError(e instanceof Error ? e.message : "โหลด Seed Script ไม่สำเร็จ"); }
  };

  const createScript = async (body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-scripts?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Seed Script ไม่สำเร็จ"); }
      setCreateModal(false); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "สร้าง Seed Script ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const updateScript = async (id: string, body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-scripts/${id}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "แก้ไข Seed Script ไม่สำเร็จ"); }
      setEditScript(null); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "แก้ไข Seed Script ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const toggleActive = async (row: AutomationDataSeedScriptListItem) => {
    if (!window.confirm(`${row.isActive ? "ปิด" : "เปิด"}ใช้งาน Seed Script "${row.name}"?`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-scripts/${row.automationDataSeedScriptId}/${row.isActive ? "deactivate" : "activate"}?projectId=${projectId}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "เปลี่ยนสถานะไม่สำเร็จ"); }
      setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "เปลี่ยนสถานะไม่สำเร็จ"); }
  };

  const requestRun = async (environmentId: string, buildId: string) => {
    if (!runModal) return;
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-runs?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ automationDataSeedScriptId: runModal.automationDataSeedScriptId, environmentId, buildId }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สั่งรัน Seed Script ไม่สำเร็จ"); }
      setRunModal(null);
    } catch (e) { setError(e instanceof Error ? e.message : "สั่งรัน Seed Script ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const openRunHistory = async (row: AutomationDataSeedScriptListItem) => {
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-runs?projectId=${projectId}&automationDataSeedScriptId=${row.automationDataSeedScriptId}`, { headers });
      if (!r.ok) throw new Error("โหลดประวัติการรันไม่สำเร็จ");
      setRunHistory({ name: row.name, runs: await r.json() });
    } catch (e) { setError(e instanceof Error ? e.message : "โหลดประวัติการรันไม่สำเร็จ"); }
  };

  const approveScript = async (row: AutomationDataSeedScriptListItem) => {
    if (!window.confirm(`อนุมัติ Master Data Script "${row.name}"? หลังอนุมัติจึงจะสั่งรันได้`)) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-scripts/${row.automationDataSeedScriptId}/approve?projectId=${projectId}`, { method: "POST", headers });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "อนุมัติไม่สำเร็จ"); }
      setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "อนุมัติไม่สำเร็จ"); }
  };

  const rejectScript = async (row: AutomationDataSeedScriptListItem) => {
    const reason = window.prompt(`เหตุผลที่ไม่อนุมัติ "${row.name}" (ไม่บังคับ):`);
    if (reason === null) return;
    setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/seed-scripts/${row.automationDataSeedScriptId}/reject?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify({ reason: reason.trim() || null }) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "ไม่อนุมัติไม่สำเร็จ"); }
      setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "ไม่อนุมัติไม่สำเร็จ"); }
  };

  return <section className="automation-cases" aria-label="Automation Seed Cleanup Data">
    <header className="automation-section-head"><div><h2>Seed, Cleanup &amp; Master Data (AUT-DATA-003/004/005)</h2><p>เก็บ SQL script สำหรับ seed ข้อมูลพื้นฐาน (เช่นสินค้า/ราคา/โปรโมชั่น) ก่อนรัน, cleanup ข้อมูลที่ทิ้งไว้หลังรัน, และเตรียม Master Data (สินค้า/ราคา/โปรโมชั่น) ก่อน POS scenario แบบ repeatable/idempotent — Windows Agent เป็นผู้รัน SQL จริงผ่านคำสั่ง <code>runner seed</code> โดยไม่มี credential ของ DB เก็บอยู่ในนี้เลย; ถ้า Agent ที่รับงานหายไประหว่างรัน ระบบจะดึงงานกลับมาให้ Agent อื่นรับต่อได้อัตโนมัติหลัง 30 นาที (AUT-DATA-004) — Script ประเภท "Master Data" ต้องผ่านการอนุมัติก่อนจึงจะสั่งรันได้ (AUT-DATA-005)</p></div>{canEdit && <button className="btn primary" type="button" onClick={() => setCreateModal(true)}>＋ สร้าง Script</button>}</header>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    <div className="automation-case-toolbar">
      <select aria-label="กรองประเภท Script" value={typeFilter} onChange={(e) => setTypeFilter(e.target.value as "all" | "Seed" | "Cleanup" | "MasterData")}>
        <option value="all">ทุกประเภท</option>
        <option value="Seed">Seed</option>
        <option value="Cleanup">Cleanup</option>
        <option value="MasterData">Master Data</option>
      </select>
    </div>
    {scripts.length ? <div className="table-wrap"><table><thead><tr><th>ชื่อ</th><th>ประเภท</th><th>DB</th><th>สถานะ</th><th>การอนุมัติ</th><th>สร้างเมื่อ</th><th></th></tr></thead><tbody>{scripts.map((s) => {
      const isMasterData = s.scriptType === "MasterData";
      const canRunNow = s.isActive && (!isMasterData || s.approvalStatus === "Approved");
      return <tr key={s.automationDataSeedScriptId}>
      <td><b>{s.name}</b>{s.description && <small>{s.description}</small>}</td>
      <td><Badge tone={scriptTypeTone[s.scriptType] ?? "blue"}>{s.scriptType}</Badge></td>
      <td>{s.dbKind}</td>
      <td><Badge tone={s.isActive ? "green" : "gray"}>{s.isActive ? "เปิดใช้งาน" : "ปิดแล้ว"}</Badge></td>
      <td>{isMasterData ? <Badge tone={approvalStatusTone[s.approvalStatus] ?? "gray"}>{s.approvalStatus}</Badge> : <span className="muted-text">-</span>}</td>
      <td>{formatThaiDateTime(s.createdAt)}</td>
      <td>
        {canEdit && <button type="button" className="table-action" onClick={() => openEdit(s.automationDataSeedScriptId)}>แก้ไข</button>}
        {canEdit && isMasterData && s.approvalStatus !== "Approved" && <button type="button" className="table-action" onClick={() => approveScript(s)}>✓ อนุมัติ</button>}
        {canEdit && isMasterData && s.approvalStatus !== "Rejected" && <button type="button" className="table-action danger" onClick={() => rejectScript(s)}>✗ ไม่อนุมัติ</button>}
        {canRun && canRunNow && <button type="button" className="table-action" onClick={() => setRunModal(s)}>▶ รัน</button>}
        <button type="button" className="table-action" onClick={() => openRunHistory(s)}>ประวัติการรัน</button>
        {canEdit && <button type="button" className={`table-action${s.isActive ? " danger" : ""}`} onClick={() => toggleActive(s)}>{s.isActive ? "ปิด" : "เปิด"}</button>}
      </td>
    </tr>;
    })}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Script</p><small>สร้าง SQL script ที่รันได้ซ้ำโดยไม่พัง (เช่นใช้ MERGE/UPSERT หรือเช็คก่อน insert/delete) — Seed สั่งก่อนชุด Automation ที่ต้องการ master data, Cleanup สั่งหลังรันเพื่อล้างข้อมูลที่ทิ้งไว้, Master Data เตรียมสินค้า/ราคา/โปรโมชั่นก่อน POS scenario (ต้องอนุมัติก่อนรัน)</small></div>}

    {createModal && <SeedScriptFormModal busy={busy} onClose={() => setCreateModal(false)} onSave={createScript} />}
    {editScript && <SeedScriptFormModal script={editScript} busy={busy} onClose={() => setEditScript(null)} onSave={(body) => updateScript(editScript.automationDataSeedScriptId, body)} />}
    {runModal && <SnapshotRequestModal projectId={projectId} releaseId={releaseId} headers={headers} busy={busy} onClose={() => setRunModal(null)} onSave={requestRun} />}

    {runHistory && <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-seed-run-history-title" onMouseDown={() => setRunHistory(null)}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
      <div className="modal-head"><div><h2 id="automation-seed-run-history-title">ประวัติการรัน — {runHistory.name}</h2><small>{runHistory.runs.length} รายการ — ล่าสุดก่อน</small></div><button aria-label="ปิด" onClick={() => setRunHistory(null)}>×</button></div>
      {runHistory.runs.length ? <div className="automation-result-list">{runHistory.runs.map((r) => <div key={r.automationDataSeedRunId} className="automation-failure-row">
        <b><Badge tone={snapshotStatusTone[r.status] ?? "blue"}>{r.status}</Badge> <Badge tone={scriptTypeTone[r.scriptType] ?? "blue"}>{r.scriptType}</Badge> {r.environmentName} / {r.buildNumber} · {formatThaiDateTime(r.requestedAt)}</b>
        <span>{r.status === "Succeeded" ? `Rows affected: ${r.rowsAffected ?? 0}` : (r.agentCode ? `Agent: ${r.agentCode}` : "")}</span>
        {r.errorMessage && <span>{r.errorMessage}</span>}
      </div>)}</div> : <div className="empty"><p>ยังไม่เคยถูกรัน</p></div>}
      <div className="modal-actions"><button className="btn" onClick={() => setRunHistory(null)}>ปิดหน้าต่าง</button></div>
    </div></div>}
  </section>;
}

function SeedScriptFormModal({ script, busy, onClose, onSave }: {
  script?: AutomationDataSeedScriptDetailItem; busy: boolean; onClose: () => void; onSave: (body: Record<string, unknown>) => void;
}) {
  const isEdit = !!script;
  const [name, setName] = useState(script?.name ?? "");
  const [description, setDescription] = useState(script?.description ?? "");
  const [scriptType, setScriptType] = useState(script?.scriptType ?? "Seed");
  const [dbKind, setDbKind] = useState(script?.dbKind ?? "Firebird");
  const [sqlScript, setSqlScript] = useState(script?.sqlScript ?? "");

  const canSave = name.trim() && sqlScript.trim();
  const save = () => onSave({ name: name.trim(), description: description.trim() || null, scriptType, dbKind, sqlScript });

  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-seed-form-title" onMouseDown={() => !busy && onClose()}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
    <div className="modal-head"><div><h2 id="automation-seed-form-title">{isEdit ? `แก้ไข ${script!.name}` : "สร้าง Script"}</h2></div><button aria-label="ปิด" disabled={busy} onClick={onClose}>×</button></div>
    <div className="form-grid">
      <label className="full">ชื่อ<input value={name} onChange={(e) => setName(e.target.value)} placeholder="เช่น Baseline Products" /></label>
      <label className="full">คำอธิบาย (ไม่บังคับ)<textarea rows={2} value={description} onChange={(e) => setDescription(e.target.value)} /></label>
      <label>ประเภท<select value={scriptType} onChange={(e) => setScriptType(e.target.value)}><option value="Seed">Seed (ใส่ข้อมูลก่อนรัน)</option><option value="Cleanup">Cleanup (ล้างข้อมูลหลังรัน)</option><option value="MasterData">Master Data (สินค้า/ราคา/โปรโมชั่นก่อน POS scenario — ต้องอนุมัติก่อนรัน)</option></select></label>
      {isEdit && script!.scriptType === "MasterData" && <p className="muted-text">แก้ไข SQL แล้วจะต้องขออนุมัติใหม่อีกครั้งก่อนสั่งรันได้ (สถานะอนุมัติปัจจุบันจะถูกรีเซ็ตเป็น Pending)</p>}
      <label>ฐานข้อมูล<select value={dbKind} onChange={(e) => setDbKind(e.target.value)}><option value="Firebird">Firebird</option><option value="SqlServer">SQL Server</option></select></label>
      <label className="full">SQL Script (ต้อง repeatable/idempotent เอง เช่นเช็คก่อน insert — ห้ามใส่ connection string/credential)<textarea rows={10} className="mono" value={sqlScript} onChange={(e) => setSqlScript(e.target.value)} placeholder={"INSERT INTO Products (Code, Name)\nSELECT 'P001', 'Test Product'\nFROM RDB$DATABASE\nWHERE NOT EXISTS (SELECT 1 FROM Products WHERE Code='P001');"} /></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}>ยกเลิก</button><button className="btn primary" disabled={busy || !canSave} onClick={save}>{busy ? "กำลังบันทึก..." : "บันทึก"}</button></div>
  </div></div>;
}

type AutomationEnvironmentDataProfileItem = { automationEnvironmentDataProfileId: string; projectId: string; environmentId: string; environmentName: string; dbKind: string; notes?: string; createdAt: string; updatedAt?: string };

function AutomationEnvironmentDataProfileTab({ projectId, headers, canEdit }: {
  projectId: string; headers: Record<string, string>; canEdit: boolean;
}) {
  const [profiles, setProfiles] = useState<AutomationEnvironmentDataProfileItem[]>([]);
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [createModal, setCreateModal] = useState(false);
  const [editProfile, setEditProfile] = useState<AutomationEnvironmentDataProfileItem | null>(null);

  useEffect(() => {
    if (!projectId) return;
    fetch(`${apiUrl}/automation/data/environment-data-profiles?projectId=${projectId}`, { headers }).then((r) => (r.ok ? r.json() : [])).then((p) => setProfiles(Array.isArray(p) ? p : [])).catch(() => setError("โหลด Environment Data Profile ไม่สำเร็จ"));
  }, [projectId, headers, reload]);

  const createProfile = async (body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/environment-data-profiles?projectId=${projectId}`, { method: "POST", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "สร้าง Profile ไม่สำเร็จ"); }
      setCreateModal(false); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "สร้าง Profile ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  const updateProfile = async (id: string, body: Record<string, unknown>) => {
    setBusy(true); setError("");
    try {
      const r = await fetch(`${apiUrl}/automation/data/environment-data-profiles/${id}?projectId=${projectId}`, { method: "PUT", headers, body: JSON.stringify(body) });
      if (!r.ok) { const p = await r.json().catch(() => null); throw new Error(p?.detail ?? "แก้ไข Profile ไม่สำเร็จ"); }
      setEditProfile(null); setReload((v) => v + 1);
    } catch (e) { setError(e instanceof Error ? e.message : "แก้ไข Profile ไม่สำเร็จ"); } finally { setBusy(false); }
  };

  return <section className="automation-cases" aria-label="Automation Environment Data Profile">
    <header className="automation-section-head"><div><h2>Environment Data Profile (AUT-DATA-006)</h2><p>เก็บ metadata ที่ไม่ใช่ secret ต่อ Environment (ตอนนี้มีแค่ประเภทฐานข้อมูล) เพื่อให้ Hub เช็คความไม่ตรงกันของ DbKind ระหว่าง Environment กับ Seed script/DB Snapshot ได้ตั้งแต่ตอนสั่งงาน แทนที่จะรอให้ Agent claim งานไปแล้วค่อย fail — <b>ไม่มี field เก็บ connection string/credential ในนี้เลย</b> credential ของ DB จริงยังอยู่ที่เครื่อง Windows Agent เท่านั้นเหมือนเดิมทุกประการ (Environment ที่ยังไม่สร้าง Profile จะไม่ถูกเช็คอะไรเลย เป็น opt-in)</p></div>{canEdit && <button className="btn primary" type="button" onClick={() => setCreateModal(true)}>＋ สร้าง Profile</button>}</header>
    {error && <div className="inline-alert error"><span>{error}</span></div>}
    {profiles.length ? <div className="table-wrap"><table><thead><tr><th>Environment</th><th>DbKind</th><th>หมายเหตุ</th><th>แก้ไขล่าสุด</th><th></th></tr></thead><tbody>{profiles.map((p) => <tr key={p.automationEnvironmentDataProfileId}>
      <td><b>{p.environmentName}</b></td>
      <td><Badge tone={p.dbKind === "Firebird" ? "blue" : "purple"}>{p.dbKind}</Badge></td>
      <td>{p.notes ?? <span className="muted-text">-</span>}</td>
      <td>{formatThaiDateTime(p.updatedAt ?? p.createdAt)}</td>
      <td>{canEdit && <button type="button" className="table-action" onClick={() => setEditProfile(p)}>แก้ไข</button>}</td>
    </tr>)}</tbody></table></div> : <div className="empty"><p>ยังไม่มี Environment Data Profile</p><small>สร้าง Profile ต่อ Environment เพื่อระบุว่าเป็น Firebird หรือ SQL Server — Hub จะใช้เทียบกับ Seed script/Snapshot ก่อนสั่งงานให้อัตโนมัติ</small></div>}

    {createModal && <EnvironmentDataProfileFormModal busy={busy} onClose={() => setCreateModal(false)} onSave={createProfile} />}
    {editProfile && <EnvironmentDataProfileFormModal profile={editProfile} busy={busy} onClose={() => setEditProfile(null)} onSave={(body) => updateProfile(editProfile.automationEnvironmentDataProfileId, body)} />}
  </section>;
}

function EnvironmentDataProfileFormModal({ profile, busy, onClose, onSave }: {
  profile?: AutomationEnvironmentDataProfileItem; busy: boolean; onClose: () => void; onSave: (body: Record<string, unknown>) => void;
}) {
  const isEdit = !!profile;
  const [environments, setEnvironments] = useState<EnvironmentOption[]>([]);
  const [environmentId, setEnvironmentId] = useState(profile?.environmentId ?? "");
  const [dbKind, setDbKind] = useState(profile?.dbKind ?? "Firebird");
  const [notes, setNotes] = useState(profile?.notes ?? "");

  useEffect(() => {
    if (isEdit) return;
    let mounted = true;
    fetch(`${apiUrl}/master-settings/environments`, { headers: { Authorization: `Bearer ${token()}` } }).then((r) => (r.ok ? r.json() : [])).then((e) => {
      if (mounted) setEnvironments(Array.isArray(e) ? (e as EnvironmentOption[]).filter((x) => x.isActive) : []);
    }).catch(() => { /* select just renders empty — inline error banner elsewhere covers fetch failures */ });
    return () => { mounted = false; };
  }, [isEdit]);

  const canSave = isEdit ? true : !!environmentId;
  const save = () => onSave(isEdit ? { dbKind, notes: notes.trim() || null } : { environmentId, dbKind, notes: notes.trim() || null });

  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-data-profile-form-title" onMouseDown={() => !busy && onClose()}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
    <div className="modal-head"><div><h2 id="automation-data-profile-form-title">{isEdit ? `แก้ไข ${profile!.environmentName}` : "สร้าง Environment Data Profile"}</h2></div><button aria-label="ปิด" disabled={busy} onClick={onClose}>×</button></div>
    <div className="form-grid">
      {isEdit ? <label>Environment<input value={profile!.environmentName} disabled /></label> :
        <label>Environment<select value={environmentId} onChange={(e) => setEnvironmentId(e.target.value)}><option value="">เลือก Environment</option>{environments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>}
      <label>DbKind<select value={dbKind} onChange={(e) => setDbKind(e.target.value)}><option value="Firebird">Firebird</option><option value="SqlServer">SQL Server</option></select></label>
      <label className="full">หมายเหตุ (ไม่บังคับ — ห้ามใส่ connection string/credential)<textarea rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="เช่น เครื่อง UAT ทีม Sales" /></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}>ยกเลิก</button><button className="btn primary" disabled={busy || !canSave} onClick={save}>{busy ? "กำลังบันทึก..." : "บันทึก"}</button></div>
  </div></div>;
}

function SuiteFormModal({ title, initialCode = "", initialName = "", initialDescription = "", busy, onClose, onSave }: {
  title: string; initialCode?: string; initialName?: string; initialDescription?: string; busy: boolean; onClose: () => void; onSave: (code: string, name: string, description: string, changeReason?: string) => void;
}) {
  const [suiteCode, setSuiteCode] = useState(initialCode);
  const [suiteName, setSuiteName] = useState(initialName);
  const [description, setDescription] = useState(initialDescription);
  const [changeReason, setChangeReason] = useState("");
  const isEdit = initialName !== "";
  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-suite-form-title" onMouseDown={() => !busy && onClose()}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
    <div className="modal-head"><div><h2 id="automation-suite-form-title">{title}</h2></div><button aria-label="ปิด" disabled={busy} onClick={onClose}>×</button></div>
    <div className="form-grid">
      {!isEdit && <label>รหัส Suite (ไม่บังคับ — เว้นว่างให้ระบบสร้างให้)<input value={suiteCode} onChange={(e) => setSuiteCode(e.target.value)} placeholder="เช่น AUT-AS-SMOKE" /></label>}
      <label className="full">ชื่อ Suite<input value={suiteName} onChange={(e) => setSuiteName(e.target.value)} /></label>
      <label className="full">คำอธิบาย (ไม่บังคับ)<textarea rows={3} value={description} onChange={(e) => setDescription(e.target.value)} /></label>
      {isEdit && <label className="full">เหตุผลที่แก้ไข (ไม่บังคับ — บันทึกลงประวัติ)<input value={changeReason} onChange={(e) => setChangeReason(e.target.value)} placeholder="เช่น ปรับให้ตรงชื่อ Release ใหม่" /></label>}
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}>ยกเลิก</button><button className="btn primary" disabled={busy || !suiteName.trim()} onClick={() => onSave(suiteCode, suiteName, description, changeReason)}>{busy ? "กำลังบันทึก..." : "บันทึก"}</button></div>
  </div></div>;
}

function RunSuiteModal({ suite, releaseId, canRun, busy, onClose, onRun, onError }: {
  suite: { automationSuiteId: string; suiteCode: string; caseCount: number; readyCaseCount: number }; releaseId?: string; canRun: boolean; busy: boolean; onClose: () => void;
  onRun: (buildId: string, environmentId: string, priority: number) => void; onError: (e: string) => void;
}) {
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

  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-suite-run-title" onMouseDown={() => !busy && onClose()}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
    <div className="modal-head"><div><h2 id="automation-suite-run-title">รัน {suite.suiteCode}</h2><small>{suite.readyCaseCount}/{suite.caseCount} case Ready — ไม่ต้องเลือก Case ใหม่ ใช้ชุดเดิมของ Suite</small></div><button aria-label="ปิด" disabled={busy} onClick={onClose}>×</button></div>
    <div className="form-grid">
      <label>Build<select value={buildId} onChange={(e) => setBuildId(e.target.value)}><option value="">เลือก Build</option>{builds.map((b) => <option key={b.buildId} value={b.buildId}>{b.buildNumber}</option>)}</select></label>
      <label>Environment<select value={envId} onChange={(e) => setEnvId(e.target.value)}><option value="">เลือก Env</option>{environments.map((e) => <option key={e.testEnvironmentId} value={e.testEnvironmentId}>{e.environmentName}</option>)}</select></label>
      <label>Priority<select value={priority} onChange={(e) => setPriority(Number(e.target.value))}>{[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((p) => <option key={p} value={p}>{p}</option>)}</select></label>
    </div>
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}>ยกเลิก</button><button className="btn primary" disabled={!canRun || busy || !buildId || !envId || suite.readyCaseCount === 0} onClick={() => onRun(buildId, envId, priority)}>{busy ? "กำลังส่ง..." : `▶ รัน ${suite.readyCaseCount} case`}</button></div>
  </div></div>;
}

function AddSuiteCasesModal({ cases, existingCaseIds, busy, onClose, onAdd }: {
  cases: AutomationCaseItem[]; existingCaseIds: string[]; busy: boolean; onClose: () => void; onAdd: (caseIds: string[], isRequired: boolean, changeReason: string) => void;
}) {
  const available = cases.filter((c) => !existingCaseIds.includes(c.automationCaseId));
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [isRequired, setIsRequired] = useState(true);
  const [changeReason, setChangeReason] = useState("");
  const toggle = (id: string) => setSelected((prev) => { const next = new Set(prev); if (next.has(id)) next.delete(id); else next.add(id); return next; });

  return <div className="modal" role="dialog" aria-modal="true" aria-labelledby="automation-suite-add-cases-title" onMouseDown={() => !busy && onClose()}><div className="modal-box" onMouseDown={(e) => e.stopPropagation()}>
    <div className="modal-head"><div><h2 id="automation-suite-add-cases-title">เพิ่ม Automation Case เข้า Suite</h2><small>เลือก Case ที่ยังไม่อยู่ใน Suite นี้</small></div><button aria-label="ปิด" disabled={busy} onClick={onClose}>×</button></div>
    <label className="checkbox-field"><input type="checkbox" checked={isRequired} onChange={(e) => setIsRequired(e.target.checked)} /> ตั้งเป็น Required (ต้องผ่านทุกตัว)</label>
    <label>เหตุผล (ไม่บังคับ — บันทึกลงประวัติ)<input value={changeReason} onChange={(e) => setChangeReason(e.target.value)} placeholder="เช่น เพิ่ม case สำหรับ regression รอบนี้" /></label>
    {available.length ? <div className="automation-batch-list">
      {available.map((c) => <label key={c.automationCaseId} className="automation-batch-row"><input type="checkbox" aria-label={`เลือก ${c.automationCode}`} checked={selected.has(c.automationCaseId)} onChange={() => toggle(c.automationCaseId)} /><span><b>{c.automationCode}</b><small>{c.testCaseCode} · {c.testCaseTitle}</small></span><Badge tone={caseStatusTone[c.status] ?? "blue"}>{c.status}</Badge></label>)}
    </div> : <div className="empty"><p>ทุก Automation Case ถูกเพิ่มเข้า Suite นี้หมดแล้ว</p></div>}
    <div className="modal-actions"><button className="btn" disabled={busy} onClick={onClose}>ยกเลิก</button><button className="btn primary" disabled={busy || !selected.size} onClick={() => onAdd([...selected], isRequired, changeReason)}>{busy ? "กำลังเพิ่ม..." : `เพิ่ม ${selected.size} case`}</button></div>
  </div></div>;
}

function Pager({ page, count, total, pageSize, onPrev, onNext }: { page: number; count: number; total: number; pageSize: number; onPrev: () => void; onNext: () => void }) {
  return <div className="automation-pager" role="navigation" aria-label="แบ่งหน้า">
    <button type="button" className="pager-btn" disabled={page <= 1} onClick={onPrev} aria-label="หน้าก่อนหน้า">‹ ก่อนหน้า</button>
    <span className="pager-info">หน้า {page} / {count} · {total.toLocaleString()} รายการ · {pageSize}/หน้า</span>
    <button type="button" className="pager-btn" disabled={page >= count} onClick={onNext} aria-label="หน้าถัดไป">ถัดไป ›</button>
  </div>;
}
