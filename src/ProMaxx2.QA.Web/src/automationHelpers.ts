export interface PlanCaseItem {
  testCaseCode: string;
  title: string;
  priority: string;
  status: string;
  automationTarget?: string;
  stepCount: number;
}
export interface PlanModule {
  moduleCode: string;
  moduleName: string;
}
export interface PlanRow {
  caseItem: PlanCaseItem;
  module?: PlanModule;
}

export function csvEscape(value: string): string {
  return `"${String(value).replaceAll('"', '""')}"`;
}

export function buildAutomationPlanCsv(rows: PlanRow[]): string {
  const header = ["Test Case Code", "Title", "Module", "Priority", "Status", "Readiness", "Target", "Steps"];
  const lines = [
    header,
    ...rows.map(({ caseItem: c, module: m }) => [
      c.testCaseCode,
      c.title,
      m ? `${m.moduleCode} · ${m.moduleName}` : "",
      c.priority,
      c.status,
      c.status === "Ready" ? "Ready" : "Not Ready",
      c.automationTarget || "",
      String(c.stepCount),
    ]),
  ];
  return "﻿" + lines.map((row) => row.map(csvEscape).join(",")).join("\r\n");
}

export function formatThaiDateTime(value?: string | number | null, options?: Intl.DateTimeFormatOptions): string {
  if (value === null || value === undefined || value === "") return "-";
  let date = value as string | number;
  if (typeof value === "string" && !/(z|Z|[+-]\d{2}:?\d{2})$/.test(value)) date = `${value}Z`;
  const d = new Date(date);
  if (Number.isNaN(d.getTime())) return String(value);
  return d.toLocaleString("th-TH", { ...options, timeZone: "Asia/Bangkok" });
}

export type ExecutionStatus = "Pass" | "Fail" | "Blocked" | "Skipped";

export function mapAutomationStatusToExecution(status: string): ExecutionStatus {
  switch (status) {
    case "Passed":
      return "Pass";
    case "Failed":
      return "Fail";
    case "Blocked":
      return "Blocked";
    default:
      return "Skipped";
  }
}

export interface ExportPlanDeps {
  rows: PlanRow[];
  projectId?: string;
  onError: (message: string) => void;
  createObjectUrl?: (blob: Blob) => string;
  revokeObjectUrl?: (url: string) => void;
  createAnchor?: () => { href: string; download: string; click: () => void };
}

export function exportPlanAction(deps: ExportPlanDeps): void {
  const createObjectUrl = deps.createObjectUrl ?? ((blob: Blob) => URL.createObjectURL(blob));
  const revokeObjectUrl = deps.revokeObjectUrl ?? ((url: string) => URL.revokeObjectURL(url));
  const createAnchor =
    deps.createAnchor ??
    (() => document.createElement("a") as unknown as { href: string; download: string; click: () => void });
  try {
    const csv = buildAutomationPlanCsv(deps.rows);
    const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
    const url = createObjectUrl(blob);
    const anchor = createAnchor();
    anchor.href = url;
    anchor.download = `automation-plan-${deps.projectId || "all"}.csv`;
    anchor.click();
    revokeObjectUrl(url);
  } catch {
    deps.onError("Export Plan ไม่สำเร็จ");
  }
}

export interface WriteBackRunResult {
  testCaseId?: string | null;
  testExecutionId?: string | null;
  status: string;
  errorMessage?: string | null;
}
export interface WriteBackRun {
  runnerName?: string | null;
  targetApp: string;
  results: WriteBackRunResult[];
}
export interface WriteBackDeps {
  run: WriteBackRun;
  cycleId: string;
  apiUrl: string;
  token: string | null;
  canRun: boolean;
  onError: (message: string) => void;
  onDone: (count: number) => void;
  fetchImpl?: typeof fetch;
}

export async function writeBackAction(deps: WriteBackDeps): Promise<void> {
  if (!deps.cycleId || !deps.run || !deps.canRun) return;
  const fetchImpl = deps.fetchImpl ?? fetch;
  try {
    const ws = await fetchImpl(`${deps.apiUrl}/test-cycles/${deps.cycleId}/execution`, {
      headers: { Authorization: `Bearer ${deps.token}` },
    }).then((r) => (r.ok ? r.json() : null));
    if (!ws) throw new Error("โหลด Test Cycle ไม่สำเร็จ");
    const cases: { testCaseId?: string; testCycleCaseId?: string }[] = ws.cases || [];
    const map = new Map<string, string>();
    for (const c of cases) if (c.testCaseId && c.testCycleCaseId) map.set(c.testCaseId, c.testCycleCaseId);
    let count = 0;
    for (const item of deps.run.results) {
      if (item.testExecutionId || !item.testCaseId) continue;
      const cid = map.get(item.testCaseId);
      if (!cid) continue;
      const status = mapAutomationStatusToExecution(item.status);
      const r = await fetchImpl(`${deps.apiUrl}/test-cycle-cases/${cid}/executions`, {
        method: "POST",
        headers: { Authorization: `Bearer ${deps.token}`, "Content-Type": "application/json" },
        body: JSON.stringify({
          status,
          actualResult: item.errorMessage || null,
          comment: `Automation write-back: ${deps.run.runnerName || deps.run.targetApp}`,
          stepResults: [],
        }),
      });
      if (!r.ok) {
        const p = await r.json().catch(() => null);
        throw new Error((p as { detail?: string } | null)?.detail ?? "เขียนกลับไม่สำเร็จ");
      }
      count++;
    }
    deps.onDone(count);
  } catch (e) {
    deps.onError(e instanceof Error ? e.message : "เขียนกลับไม่สำเร็จ");
  }
}
