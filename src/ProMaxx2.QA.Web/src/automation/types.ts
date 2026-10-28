export type AutomationCaseItem = {
  automationCaseId: string; testCaseId: string; testCaseCode: string; testCaseTitle: string; automationCode: string;
  automationType: string; status: string; currentVersionNo: number; versionCount: number; ownerUserId?: string; ownerName?: string; isAiGenerated: boolean; createdAt: string;
  maintenanceReason?: string; maintenanceOwnerUserId?: string; maintenanceOpenedAt?: string;
  isQuarantined?: boolean; quarantineReason?: string; quarantineOwnerUserId?: string; quarantineExpiresAt?: string;
};

export type AutomationVersionItem = {
  automationVersionId: string; automationCaseId: string; versionNo: number; testCaseRevisionNo: number; dslVersion: string; dslJson: string;
  generatedByAi: boolean; aiProvider?: string; aiModel?: string; aiConfidence?: number; validationStatus: string; validationErrors?: string;
  approvedBy?: string; approvedAt?: string; changeReason?: string; createdAt: string;
};

export type AutomationActionItem = {
  automationActionId: string; actionCode: string; actionName: string; category: string; description?: string; parameterSchemaJson: string;
  handlerKey: string; minimumAgentVersion?: string; isActive: boolean; retrySafety: string;
};

export type AutomationObjectItem = {
  automationObjectId: string; projectId: string; moduleId?: string; moduleCode?: string; moduleName?: string; applicationCode: string;
  screenCode: string; objectCode: string; objectName: string; controlType: string; automationId?: string; selectorJson: string; objectVersion: number; isActive: boolean;
};

export type AutomationObjectImportDraft = {
  clientId: string; moduleId?: string; applicationCode: string; screenCode: string; objectCode: string; objectName: string; controlType: string; automationId?: string; selectorJson: string;
  status: "Ready" | "DuplicateKey" | "DuplicateAutomationId" | "Invalid"; message: string;
};

export type AutomationObjectImportResult = { imported: number; skipped: number; rows: { businessKey: string; automationId?: string; status: string; message: string }[] };

export type AutomationObjectVerificationItem = {
  automationObjectVerificationId: string; automationObjectId: string; objectCode: string; screenCode: string; expectedAutomationId?: string; expectedControlType: string;
  actualAutomationId?: string; actualControlType?: string; status: string; assignedAgentId?: string; assignedAgentCode?: string; requestedAt: string; completedAt?: string; message?: string;
};

export type AutomationAgentItem = {
  agentId: string; agentCode: string; machineName: string; agentVersion: string; operatingSystem: string; architecture: string; status: string;
  lastHeartbeatAt: string; currentExecutionId?: string; registeredAt: string; isEnabled: boolean; connectivity: string; capabilities: string[];
};

export type AutomationJobItem = {
  jobId: string; automationExecutionId: string; priority: number; requestedAgentId?: string; assignedAgentId?: string; assignedAgentCode?: string;
  status: string; queuedAt: string; assignedAt?: string; startedAt?: string; completedAt?: string; retryCount: number; lastError?: string;
};

export type FlakyCandidateItem = { automationCaseId: string; automationCode: string; recentRuns: number; transitions: number; lastExecutedAt: string };

export type AutomationSuiteCaseItem = { automationCaseId: string; automationCode: string; testCaseCode: string; testCaseTitle: string; automationType: string; status: string; sortOrder: number; isRequired: boolean };

export type AutomationSuiteListItem = { automationSuiteId: string; projectId: string; suiteCode: string; suiteName: string; description?: string; isActive: boolean; createdAt: string; closedAt?: string; revisionNo: number; caseCount: number; readyCaseCount: number };

export type AutomationSuiteDetailItem = { automationSuiteId: string; projectId: string; suiteCode: string; suiteName: string; description?: string; isActive: boolean; createdBy?: string; createdAt: string; updatedAt?: string; closedAt?: string; revisionNo: number; cases: AutomationSuiteCaseItem[] };

export type AutomationSuiteRevisionItem = { automationSuiteRevisionId: string; revisionNo: number; changeType: string; detail?: string; changeReason?: string; changedBy?: string; changedByName?: string; changedAt: string };

export type AutomationScheduleListItem = {
  automationScheduleId: string; projectId: string; automationSuiteId: string; suiteCode: string; suiteName: string; name: string; description?: string;
  frequency: string; daysOfWeekMask: number; runAtTime: string; onceOnDate?: string; timeZoneId: string; buildNumber: string; environmentName: string; isActive: boolean; nextRunAtUtc: string; lastRunAtUtc?: string; createdAt: string;
};

export type AutomationScheduleDetailItem = {
  automationScheduleId: string; projectId: string; automationSuiteId: string; suiteCode: string; suiteName: string; name: string; description?: string;
  frequency: string; daysOfWeekMask: number; runAtTime: string; onceOnDate?: string; timeZoneId: string; buildId: string; buildNumber: string; environmentId: string; environmentName: string;
  agentId?: string; agentCode?: string; priority: number; isActive: boolean; nextRunAtUtc: string; lastRunAtUtc?: string; createdBy?: string; createdAt: string; updatedAt?: string;
};

export type AutomationScheduleRunItem = { automationScheduleRunId: string; automationScheduleId: string; firedAtUtc: string; status: string; executionsCreated: number; skippedCount: number; errorMessage?: string };

export type AutomationScheduleNotificationItem = {
  automationScheduleNotificationId: string; projectId: string; automationScheduleId: string; scheduleName: string;
  automationExecutionId: string; automationCode: string; eventType: string; message: string; createdAtUtc: string; isRead: boolean; readAtUtc?: string;
};

export type AutomationBuildTriggerPolicyItem = {
  automationBuildTriggerPolicyId: string; projectId: string; automationSuiteId: string; suiteCode: string; suiteName: string; pack: string;
  environmentId: string; environmentName: string; agentId?: string; agentCode?: string; priority: number; isActive: boolean; createdAt: string; updatedAt?: string;
};

export type AutomationBuildTriggerRunItem = { automationBuildTriggerRunId: string; automationBuildTriggerPolicyId: string; buildId: string; buildNumber: string; firedAtUtc: string; status: string; executionsCreated: number; skippedCount: number; errorMessage?: string };

export type AutomationWebhookTokenItem = { automationWebhookTokenId: string; projectId: string; name: string; tokenPrefix: string; isActive: boolean; lastUsedAtUtc?: string; createdBy?: string; createdAt: string; revokedAt?: string };

export type AutomationWebhookDeliveryItem = { automationWebhookDeliveryId: string; projectId: string; automationWebhookTokenId: string; tokenName: string; requestId: string; receivedAtUtc: string; buildId?: string; buildNumber?: string; status: string; errorMessage?: string };

export type AutomationDbSnapshotItem = {
  automationDbSnapshotId: string; projectId: string; environmentId: string; environmentName: string; buildId: string; buildNumber: string;
  status: string; dbKind?: string; agentId?: string; agentCode?: string; snapshotPath?: string; checksum?: string; sizeBytes?: number; errorMessage?: string;
  requestedBy?: string; requestedAt: string; startedAt?: string; completedAt?: string;
};

export type AutomationDbRestoreItem = {
  automationDbRestoreId: string; projectId: string; automationDbSnapshotId: string; environmentId: string; environmentName: string; buildId: string; buildNumber: string;
  status: string; agentId?: string; agentCode?: string; checksumVerified: boolean; availabilityVerified: boolean; errorMessage?: string;
  requestedBy?: string; requestedAt: string; startedAt?: string; completedAt?: string;
};

export type AutomationDataSeedScriptListItem = { automationDataSeedScriptId: string; projectId: string; name: string; description?: string; scriptType: string; dbKind: string; isActive: boolean; approvalStatus: string; createdAt: string };

export type AutomationDataSeedScriptDetailItem = {
  automationDataSeedScriptId: string; projectId: string; name: string; description?: string; scriptType: string; dbKind: string; sqlScript: string; isActive: boolean;
  approvalStatus: string; reviewedBy?: string; reviewedAt?: string; rejectionReason?: string; createdBy?: string; createdAt: string; updatedAt?: string;
};

export type AutomationDataSeedRunItem = {
  automationDataSeedRunId: string; projectId: string; automationDataSeedScriptId: string; scriptName: string; scriptType: string; environmentId: string; environmentName: string; buildId: string; buildNumber: string;
  status: string; agentId?: string; agentCode?: string; rowsAffected?: number; errorMessage?: string; requestedBy?: string; requestedAt: string; startedAt?: string; completedAt?: string;
};

export type RetryPolicyItem = { maxAttempts: number; backoffSeconds: number; enabled: boolean; updatedAt?: string };

export type CountByKeyItem = { key: string; count: number };

export type FailureBreakdownItem = { totalFailed: number; byFailureType: CountByKeyItem[]; byBuild: CountByKeyItem[]; byAgent: CountByKeyItem[]; byAutomationCase: CountByKeyItem[] };

export type AutomationStepResultItem = {
  automationStepResultId: string; stepNo: number; actionCode: string; status: string; startedAt: string; completedAt: string; durationMs: number;
  actualResult?: string; errorCode?: string; errorMessage?: string; evidencePath?: string;
};

export type AutomationExecutionItem = {
  automationExecutionId: string; automationCaseId: string; automationCode: string; testCaseCode?: string; testCaseTitle?: string; automationVersionId: string; versionNo: number; testExecutionId?: string; defectId?: string; targetApp?: string;
  agentId?: string; agentCode?: string; buildId: string; buildNumber: string; environmentId: string; environmentName: string; jobId?: string; status: string;
  startedAt?: string; completedAt?: string; durationMs?: number; failureType?: string; errorCode?: string; errorMessage?: string; stepResults: AutomationStepResultItem[];
  evidence?: AutomationEvidenceItem[];
  classifiedFailureType?: string; classifiedRecommendation?: string; retryOfExecutionId?: string; retryCount?: number;
};

export type AutomationEvidenceItem = { automationEvidenceId: string; stepNo?: number; evidenceType: string; filePath: string; capturedBy?: string; capturedAt: string };

export type TestCandidate = { testCaseId: string; testCaseCode: string; title: string; priority: string; status: string; moduleId: string; automationCandidate?: boolean; testType?: string };

export type TestCaseDetailItem = {
  testCaseId: string; projectId: string; moduleId: string; testCaseCode: string; title: string;
  objective?: string; preconditions?: string; priority: string; testType?: string; automationCandidate: boolean; status: string;
  revisionNo: number; ownerUserId?: string;
  steps: { stepNo: number; action: string; testData?: string; expectedResult: string }[];
};

export type BuildOption = { buildId: string; buildNumber: string; applicationVersion?: string; status: string };

export type EnvironmentOption = { testEnvironmentId: string; environmentName: string; isActive: boolean };

export type AutomationDashboardItem = {
  totalTestCases: number; automationCandidates: number; automationCases: number; ready: number; maintenanceRequired: number;
  needsReview: number; inProgress: number; running: number; passToday: number; failToday: number; averageDurationMs?: number;
  agentsOnline: number; agentsTotal: number; readyCoverage: number; candidateCoverage: number;
};

export type AutomationAgentWorkload = {
  agentId: string; agentCode: string; windowFrom: string; windowTo: string; utilizationPercent: number; avgQueueTimeMs?: number; avgRuntimeMs?: number;
  totalExecutions: number; failedExecutions: number; failureRatePercent: number; recentHeartbeats: { status: string; currentExecutionId?: string; occurredAt: string }[];
};

export type ExecutionTrendBucket = { bucketKey: string; bucketLabel: string; passed: number; failed: number; flaky: number; total: number };

export type AutomationEnvironmentDataProfileItem = { automationEnvironmentDataProfileId: string; projectId: string; environmentId: string; environmentName: string; dbKind: string; notes?: string; createdAt: string; updatedAt?: string };
