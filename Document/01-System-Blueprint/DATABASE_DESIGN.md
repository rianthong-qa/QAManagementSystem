# ProMaxx2 QA Management System — DATABASE DESIGN

ฐานข้อมูลแนะนำ: SQL Server

## 1. หลักการ
- PK ภายในใช้ `uniqueidentifier` หรือ `bigint`
- Business ID แยกและ Unique เช่น PMX2-SALE-FUNC-001
- ตารางหลักมี CreatedAt/By, UpdatedAt/By
- ใช้ Soft Delete
- Execution/Approval History ห้าม Hard Delete
- เวลาเก็บ UTC

## 2. ER Overview
```text
Project
 ├─ Module
 ├─ Release ─ Build
 ├─ Requirement ─ RequirementTestCase ─ TestCase
 ├─ TestScenario ─ TestCase ─ TestStep
 ├─ TestData ─ TestCaseTestData
 ├─ TestSuite ─ TestSuiteCase ─ TestCase
 └─ TestCycle
     ├─ Environment
     └─ TestCycleCase ─ TestExecution ─ Evidence
                                  └─ Defect

Defect ─ Requirement / TestCase / Execution / BuildFound / FixBuild
Release ─ TestSummary / RiskAcceptance / ReleaseSignoff
```

## 3. ตารางหลัก

### Security
- Users(UserId, Username, DisplayName, Email, PasswordHash, IsActive, LastLoginAt)
- Roles(RoleId, RoleCode, RoleName, Description)
- UserRoles(UserId, RoleId)
- Permissions(PermissionId, PermissionCode, PermissionName, ModuleArea)
- RolePermissions(RoleId, PermissionId)

### Project
- Projects(ProjectId, ProjectCode, ProjectName, Description, Status, OwnerUserId, IsActive)
- Modules(ModuleId, ProjectId, ParentModuleId, ModuleCode, ModuleName, OwnerUserId, IsActive)

### Release
- Releases(ReleaseId, ProjectId, ReleaseCode, Version, ReleaseType, Scope, PlannedReleaseDate, ActualReleaseDate, Status, ReleaseOwnerUserId)
- Builds(BuildId, ReleaseId, BuildNumber, ApplicationVersion, PackageVersion, CommitReference, BuildDate, ChangeNotes, KnownIssues, IsReleaseCandidate, Status)

### Requirement / RTM
- Requirements(RequirementId, ProjectId, ReleaseId, ModuleId, RequirementCode, Title, Description, AcceptanceCriteria, Priority, RiskLevel, Source, OwnerUserId, Status, RevisionNo, IsInScope, IsDeleted)
- RequirementRevisions(RequirementRevisionId, RequirementId, RevisionNo, Title, Description, AcceptanceCriteria, ChangedBy, ChangedAt, ChangeReason)
- RequirementTestCases(RequirementId, TestCaseId, CoverageType)

### Test Design
- TestScenarios(TestScenarioId, ProjectId, ModuleId, ScenarioCode, Title, Objective, TestType, Priority, RiskLevel, Status, OwnerUserId)
- TestCases(TestCaseId, ProjectId, ModuleId, TestScenarioId, TestCaseCode, Title, Objective, Preconditions, Priority, TestType, AutomationCandidate, Status, RevisionNo, OwnerUserId, IsDeleted)
- TestCaseRevisions(TestCaseRevisionId, TestCaseId, RevisionNo, Title, Objective, Preconditions, ChangedBy, ChangedAt, ChangeReason)
- TestSteps(TestStepId, TestCaseId, RevisionNo, StepNo, Action, TestDataText, ExpectedResult)

### Test Data / Environment
- TestData(TestDataId, ProjectId, DataCode, DataType, Description, DataValue, ExpectedInitialState, ResetInstruction, IsSensitive, OwnerUserId, IsActive)
- TestCaseTestData(TestCaseId, TestDataId)
- TestEnvironments(EnvironmentId, ProjectId, EnvironmentName, OperatingSystem, AppVersion, DatabaseType, DatabaseVersion, DatasetName, DpiScale, Resolution, NetworkProfile, ServiceVersions, DeviceInfo, Notes, IsActive)

### Suite / Cycle / Execution
- TestSuites(TestSuiteId, ProjectId, SuiteCode, SuiteName, SuiteType, Description, RiskTier, IsActive)
- TestSuiteCases(TestSuiteId, TestCaseId, SortOrder, IsRequired)
- TestCycles(TestCycleId, ProjectId, ReleaseId, BuildId, EnvironmentId, TestSuiteId, CycleCode, CycleName, CycleType, StartDate, EndDate, OwnerUserId, Status, Notes)
- TestCycleCases(TestCycleCaseId, TestCycleId, TestCaseId, TestCaseRevisionNo, AssignedTesterUserId, Priority, ExecutionOrder, CurrentStatus)
- TestExecutions(TestExecutionId, TestCycleCaseId, ExecutionNo, BuildId, EnvironmentId, TesterUserId, StartedAt, CompletedAt, Status, ActualResult, Comment)
- TestStepResults(TestStepResultId, TestExecutionId, TestStepId, StepNo, Status, ActualResult, Comment)

### Defect
- Defects(DefectId, ProjectId, DefectCode, ModuleId, Title, Description, Severity, Priority, Status, BuildFoundId, EnvironmentId, ReporterUserId, AssigneeUserId, Precondition, StepsToReproduce, ExpectedResult, ActualResult, Frequency, BusinessImpact, Workaround, RootCause, Resolution, FixBuildId, RegressionImpact, CreatedAt, ClosedAt)
- DefectTestExecutions(DefectId, TestExecutionId)
- DefectTestCases(DefectId, TestCaseId)
- DefectRequirements(DefectId, RequirementId)
- DefectHistory(DefectHistoryId, DefectId, FromStatus, ToStatus, Comment, ChangedBy, ChangedAt)

### Evidence / Reports / Governance
- Attachments(AttachmentId, ProjectId, EntityType, EntityId, FileName, StoredFileName, ContentType, FileSize, FileHash, StoragePath, UploadedBy, UploadedAt)
- DailyQAStatus(DailyStatusId, ProjectId, ReleaseId, ReportDate, ReporterUserId, ScopeToday, PlannedCount, ExecutedCount, PassedCount, FailedCount, BlockedCount, Blockers, Risks, CompletedWork, NextPlan)
- WeeklyQAStatus(WeeklyStatusId, ProjectId, ReleaseId, WeekStart, WeekEnd, OverallStatus, CoveragePercent, PassRate, DefectSummary, RegressionStatus, TopRisks, Blockers, NextMilestones, Recommendation)
- TestSummaries(TestSummaryId, ReleaseId, BuildId, Scope, OutOfScope, Planned, Executed, Passed, Failed, Blocked, RequirementCoverage, PassRate, DefectSummary, RegressionResult, UpdateResult, PerformanceResult, KnownIssues, RemainingRisks, QARecommendation, GeneratedBy, GeneratedAt)
- RiskAcceptances(RiskAcceptanceId, ReleaseId, RiskCode, DefectId, Title, Description, Impact, Probability, RiskLevel, Workaround, OwnerUserId, TargetFixDate, ReviewDate, Status, ApprovedBy, ApprovedAt)
- ReleaseSignoffs(ReleaseSignoffId, ReleaseId, BuildId, SignoffType, Decision, Comment, ApproverUserId, ApprovedAt)
- Notifications(NotificationId, UserId, Type, Title, Message, EntityType, EntityId, IsRead, CreatedAt, ReadAt)
- AuditLogs(AuditLogId, UserId, Action, EntityType, EntityId, ChangeSummary, BeforeJson, AfterJson, ClientIp, CreatedAt)

## 4. Status
Requirement: Draft, Review, Approved, InTesting, Passed, Failed, Deferred, Cancelled

TestCase: Draft, Review, Ready, Deprecated

Execution: NotRun, Pass, Fail, Blocked, Skipped

Defect: New, Triaged, Assigned, InProgress, Fixed, ReadyForRetest, Reopen, Closed, Deferred, Rejected

Release: Draft, Testing, Ready, Released, Cancelled

Risk: Draft, PendingApproval, Approved, Rejected, Expired, Closed

## 5. Index สำคัญ
- Requirements(ReleaseId, ModuleId, Status)
- TestCases(ProjectId, ModuleId, Priority, Status)
- TestCycleCases(TestCycleId, CurrentStatus, AssignedTesterUserId)
- TestExecutions(TestCycleCaseId, ExecutionNo)
- Defects(ProjectId, Status, Severity, AssigneeUserId)
- AuditLogs(EntityType, EntityId, CreatedAt)
- Attachments(EntityType, EntityId)

## 6. Integrity Rules
- Build ต้องอยู่ Release เดียวกับ Cycle
- Execution ต้องระบุ Build/Environment
- Requirement/Test Case ที่เคยใช้ห้าม Hard Delete
- Closed Cycle ห้ามแก้ Historical Execution
- Sign-off เก็บ Immutable History
- Approved Risk ต้อง Audit เมื่อแก้ไข
- Dashboard Readiness คำนวณจาก Source Data

## 7. SQL Views แนะนำ
- vw_RequirementCoverage
- vw_TestExecutionSummary
- vw_DefectSummary
- vw_ModuleQuality
- vw_ReleaseReadiness
- vw_TesterWorkload
