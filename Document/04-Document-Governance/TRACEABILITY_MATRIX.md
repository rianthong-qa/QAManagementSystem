# ProMaxx2 QA Management System — TRACEABILITY_MATRIX

> ใช้ตรวจสอบความสัมพันธ์ระหว่าง Requirement → Workflow → Database → API → Screen → Test → Release

## 1. Traceability Principle

ทุก Requirement ที่อยู่ใน Scope ควรสามารถ Trace ได้:

`Requirement → Business Workflow → Data Model → API → Screen → Test Case → Execution → Defect → Release`

สถานะ Coverage:
- `COVERED`
- `PARTIAL`
- `NOT COVERED`
- `N/A`

## 2. Master Traceability Matrix

| Requirement Area | Requirement | Workflow | Database | API | Screen | Test Coverage | Status |
|---|---|---|---|---|---|---|---|
| Identity | User/Role/Permission | Authentication/Authorization | Users, Roles, Permissions | `/auth`, `/users`, `/roles` | Login, User, Role | Unit + Integration + Permission | COVERED |
| Project | Project/Module Management | Project Setup | Projects, Modules | `/projects`, `/modules` | Project, Module | Functional | COVERED |
| Release | Release Management | Release Lifecycle | Releases | `/releases` | Release List/Detail | Functional | COVERED |
| Build | Build Management | Build Intake/RC | Builds | `/builds` | Build List/Detail | Functional | COVERED |
| Requirement | Requirement Management | Requirement Review | Requirements, RequirementRevisions | `/requirements` | Requirement List/Detail | Functional + Revision | COVERED |
| RTM | Requirement Traceability | Requirement → Test | RequirementTestCases | `/rtm`, coverage APIs | RTM | Traceability Test | COVERED |
| Test Design | Scenario/Test Case | Test Design | TestScenarios, TestCases, TestSteps | `/test-scenarios`, `/test-cases` | Scenario, Test Case | Functional | COVERED |
| Test Data | Test Data Management | Test Preparation | TestData | `/test-data` | Test Data | Functional + Security | COVERED |
| Environment | Test Environment | Test Preparation | TestEnvironments | `/test-environments` | Environment | Functional | COVERED |
| Suite | Smoke/Regression Suite | Test Planning | TestSuites, TestSuiteCases | `/test-suites` | Suite | Functional | COVERED |
| Cycle | Test Cycle | Test Execution | TestCycles, TestCycleCases | `/test-cycles` | Cycle | Workflow | COVERED |
| Execution | Test Execution History | Execute/Retest | TestExecutions, TestStepResults | `/executions` | Execution Workspace | Functional + Integrity | COVERED |
| Evidence | Test Evidence | Execution/Defect | Attachments | `/attachments` | Evidence Uploader | File/Security | COVERED |
| Defect | Defect Management | Fail → Defect → Fix | Defects, DefectHistory | `/defects` | Defect List/Detail | Workflow + Permission | COVERED |
| Retest | Defect Retest | Fix → Retest | TestExecutions + Defects | `/defects/{id}/retest` | Retest | Historical Integrity | COVERED |
| Regression | Regression Impact | Change → Regression | Suites/Cycles/Links | `/regression-*` | Regression Impact | Regression | COVERED |
| Dashboard | Release Visibility | Metrics Aggregation | Views/Aggregates | `/dashboard/*` | Dashboard | Calculation | COVERED |
| Summary | Test Summary | Test Closure | TestSummaries | `/test-summaries` | Test Summary | Report Validation | COVERED |
| Risk | Risk Acceptance | Risk Approval | RiskAcceptances | `/risk-acceptances` | Risk Acceptance | Approval/Audit | COVERED |
| Sign-off | Release Sign-off | Release Gate | ReleaseSignoffs | `/signoffs`, release gate | Sign-off | Governance | COVERED |
| Audit | Audit Trail | All Critical Writes | AuditLogs | `/audit-logs` | Audit Log | Security/Integrity | COVERED |
| Notification | Notifications | Assignment/Approval | Notifications | `/notifications` | Notification Center | Functional | COVERED |

## 3. Requirement-Level Template

| Req ID | Requirement | Workflow ID | DB Entity | API | Screen | Test Case | Priority | Coverage |
|---|---|---|---|---|---|---|---|---|
| REQ-XXX-001 | ... | WF-XXX-001 | Table/Column | METHOD /api/v1/... | SCR-XXX-001 | TC-XXX-001 | P0 | COVERED |

## 4. Change Impact Matrix

เมื่อ Requirement เปลี่ยน ให้ตรวจอย่างน้อย:

| Area | ต้องตรวจ |
|---|---|
| Requirement | Acceptance Criteria / Priority / Scope |
| Workflow | State / Actor / Transition |
| Database | Table / Column / FK / Index / Migration |
| API | Endpoint / DTO / Validation / Permission |
| Screen | Field / Action / Validation / Status |
| Security | Role / Permission / Sensitive Data |
| Audit | Before/After / Approval History |
| Test | Test Case / Regression / Negative |
| Report | Dashboard / Summary / Export |
| Deployment | Migration / Config / Compatibility |

## 5. Release Traceability

ก่อน Sign-off ต้องตอบได้:
1. Requirement ไหนอยู่ใน Release
2. Requirement ไหนไม่มี Test Case
3. Test Case ไหนยัง Not Run
4. Failed Case เชื่อม Defect ใด
5. Defect แก้ใน Build ใด
6. Retest ด้วย Build ใด
7. Regression ครอบคลุม Module ใด
8. Risk ใดถูก Accept
9. ใคร Sign-off และเมื่อใด

## 6. Traceability Gate

แนะนำ Release Gate:
- P0 Requirement Coverage = 100%
- P0 Test Execution = 100%
- Open P0 Defect = 0
- P1 Blocker = 0 หรือมี Approved Risk ตาม Policy
- Critical Regression ผ่านตาม Threshold
- Sign-off มี Audit History
