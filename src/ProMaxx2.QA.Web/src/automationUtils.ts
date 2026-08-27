export interface DslStepLike {
  stepNo: number;
  action: string;
  parameters?: Record<string, string>;
}

export interface DslLike {
  dslVersion?: string;
  automationType?: string;
  steps?: DslStepLike[];
}

export function parseDslSteps(dslJson: string): DslStepLike[] {
  if (!dslJson?.trim()) return [];
  try {
    const parsed = JSON.parse(dslJson) as DslLike;
    if (!Array.isArray(parsed?.steps)) return [];
    return parsed.steps
      .filter((s) => s && typeof s.action === "string" && s.action.trim())
      .map((s) => ({ stepNo: Number(s.stepNo) || 0, action: s.action.trim().toUpperCase(), parameters: s.parameters ?? {} }))
      .filter((s) => s.stepNo > 0);
  } catch {
    return [];
  }
}

export function buildObjectKey(screenCode: string, objectCode: string): string {
  return `${(screenCode || "").trim()}.${(objectCode || "").trim()}`;
}

export const automationCaseTone: Record<string, string> = {
  Draft: "gray", NeedsReview: "yellow", Validated: "blue", Approved: "blue", Ready: "green", Running: "blue", MaintenanceRequired: "red",
};
export const automationExecutionTone: Record<string, string> = {
  Queued: "gray", Running: "blue", Passed: "green", Failed: "red", Blocked: "yellow", Cancelled: "gray", Timeout: "yellow", AgentLost: "red",
};
export const automationJobTone: Record<string, string> = {
  Queued: "gray", Assigned: "blue", Running: "blue", Passed: "green", Failed: "red", Blocked: "yellow", Cancelled: "gray", Timeout: "yellow", AgentLost: "red",
};
export const automationVersionTone: Record<string, string> = { Pending: "gray", Valid: "green", Invalid: "red" };
export const automationVerificationTone: Record<string, string> = { Pending: "gray", Assigned: "blue", Found: "green", NotFound: "red", Duplicate: "orange", ControlTypeMismatch: "yellow", Error: "gray" };

export function automationCoverage(cases: { status: string }[]): number {
  const total = cases.length;
  if (!total) return 0;
  const ready = cases.filter((c) => c.status === "Ready").length;
  return Math.round((ready * 100) / total);
}