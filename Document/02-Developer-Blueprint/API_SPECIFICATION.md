# ProMaxx2 QA Management — API Specification

> รูปแบบแนะนำ: REST API  
> Backend: ASP.NET Core .NET 10  
> Base URL: `/api/v1`

---

## 1. API Principles

- JSON Request/Response
- HTTPS เท่านั้น
- Authentication แบบ Bearer Token หรือองค์กรใช้ SSO ได้
- ใช้ UTC สำหรับ DateTime
- Pagination ทุก Endpoint ที่เป็น List
- รองรับ Filter / Sort / Search
- Validation Error ใช้มาตรฐานเดียวกัน
- ห้ามคืน PasswordHash/Secret
- ทุก Write สำคัญต้อง Audit

---

## 2. Standard Response

### Success

```json
{
  "success": true,
  "data": {},
  "message": null,
  "traceId": "00-..."
}
```

### Error

```json
{
  "success": false,
  "data": null,
  "message": "Validation failed",
  "errors": {
    "title": ["Title is required"]
  },
  "traceId": "00-..."
}
```

---

## 3. Pagination

Request:

```http
GET /api/v1/test-cases?page=1&pageSize=50&search=sale&sort=priority
```

Response:

```json
{
  "success": true,
  "data": {
    "items": [],
    "page": 1,
    "pageSize": 50,
    "totalItems": 1200,
    "totalPages": 24
  }
}
```

---

## 4. Authentication

### POST `/auth/login`

Request:

```json
{
  "username": "qa01",
  "password": "********",
  "rememberMe": false
}
```

Response:

```json
{
  "accessToken": "...",
  "expiresIn": 86400,
  "user": {
    "userId": "...",
    "displayName": "QA 01",
    "roles": ["QA_TESTER"]
  }
}
```

> `rememberMe` (optional, default `false`) — เมื่อ `true` ระบบจะออก JWT แบบอายุยาว (ค่าเริ่มต้น 30 วัน ตั้งได้ที่ `Jwt:RememberMeDays`) เพื่อให้ผู้ใช้ยังคงเข้าสู่ระบบได้แม้ปิดเบราว์เซอร์ เมื่อ `false` (ค่าเริ่มต้น) token มีอายุตาม `Jwt:ExpiresMinutes` (ค่าเริ่มต้น 24 ชั่วโมง / 1440 นาที)

### GET `/auth/me`
คืน User + Roles + Permissions

---

## 5. Projects

### GET `/projects`
### POST `/projects`
### GET `/projects/{projectId}`
### PUT `/projects/{projectId}`
### DELETE `/projects/{projectId}`

ตัวอย่าง Create:

```json
{
  "projectCode": "PMX2",
  "projectName": "ProMaxx2",
  "description": "QA Management for ProMaxx2",
  "ownerUserId": "..."
}
```

---

## 6. Modules

### GET `/projects/{projectId}/modules`
### POST `/projects/{projectId}/modules`
### PUT `/modules/{moduleId}`
### DELETE `/modules/{moduleId}`

Request:

```json
{
  "moduleCode": "SALES",
  "moduleName": "Sales",
  "parentModuleId": null,
  "ownerUserId": "..."
}
```

---

## 7. Releases

### GET `/projects/{projectId}/releases`
### POST `/projects/{projectId}/releases`
### GET `/releases/{releaseId}`
### PUT `/releases/{releaseId}`
### POST `/releases/{releaseId}/status`

Request:

```json
{
  "releaseCode": "2026.08",
  "version": "10.0.0",
  "releaseType": "Major",
  "plannedReleaseDate": "2026-08-31",
  "scope": "Sales, Stock, Report, Update",
  "releaseOwnerUserId": "..."
}
```

---

## 8. Builds

### GET `/releases/{releaseId}/builds`
### POST `/releases/{releaseId}/builds`
### GET `/builds/{buildId}`
### PUT `/builds/{buildId}`
### POST `/builds/{buildId}/mark-release-candidate`

`PUT /builds/{buildId}` รับ `releaseId` เพิ่มเป็น optional เพื่อย้าย Build ไป Release อื่นใน Project เดียวกัน (ไม่รับ Released/Cancelled) และคืน `409` หาก Build Number ซ้ำหรือมี Test Cycle, Defect, Sign-off, Regression, Dashboard share หรือ Automation record อ้างอิง Build อยู่

Request:

```json
{
  "buildNumber": "10.0.228",
  "applicationVersion": "10.0.228",
  "packageVersion": "10.0.228",
  "commitReference": "abc123",
  "buildDate": "2026-08-11T09:00:00Z",
  "changeNotes": "Fix Sales and Report",
  "knownIssues": ""
}
```

---

## 9. Requirements

### GET `/requirements`
Filter:
- projectId
- releaseId
- moduleId
- status
- priority
- inScope
- search

### POST `/requirements`
### GET `/requirements/{requirementId}`
### PUT `/requirements/{requirementId}`
### POST `/requirements/{requirementId}/revision`
### POST `/requirements/{requirementId}/status`
### DELETE `/requirements/{requirementId}`

Create:

```json
{
  "projectId": "...",
  "releaseId": "...",
  "moduleId": "...",
  "requirementCode": "PMX2-REQ-SALE-001",
  "title": "ผู้ไม่มีสิทธิ์ต้องไม่สามารถแก้ราคาขาย",
  "description": "...",
  "acceptanceCriteria": "...",
  "priority": "P0",
  "riskLevel": "High",
  "ownerUserId": "...",
  "isInScope": true
}
```

---

## 10. RTM

### GET `/releases/{releaseId}/rtm`
คืน:
- Requirement
- linkedTestCases
- coverageStatus
- latestExecutionStatus
- defects

### POST `/requirements/{requirementId}/test-cases/{testCaseId}`
Link

### DELETE `/requirements/{requirementId}/test-cases/{testCaseId}`
Unlink

### GET `/releases/{releaseId}/coverage-summary`

Response:

```json
{
  "totalRequirements": 200,
  "covered": 188,
  "notCovered": 12,
  "coveragePercent": 94.0,
  "passed": 170,
  "failed": 8,
  "blocked": 10
}
```

---

## 11. Test Scenarios

### GET `/test-scenarios`
### POST `/test-scenarios`
### GET `/test-scenarios/{id}`
### PUT `/test-scenarios/{id}`
### DELETE `/test-scenarios/{id}`

---

## 12. Test Cases

### GET `/test-cases`
Filter:
- projectId
- moduleId
- scenarioId
- priority
- status
- testType
- ownerUserId
- tag
- search

### POST `/test-cases`
### GET `/test-cases/{testCaseId}`
### PUT `/test-cases/{testCaseId}`
### POST `/test-cases/{testCaseId}/revision`
### POST `/test-cases/{testCaseId}/status`
### DELETE `/test-cases/{testCaseId}`

Create:

```json
{
  "projectId": "...",
  "moduleId": "...",
  "testScenarioId": "...",
  "testCaseCode": "PMX2-SALE-FUNC-001",
  "title": "บันทึกเอกสารขายปกติ",
  "objective": "ยืนยันการบันทึกยอดและ Stock",
  "preconditions": "สินค้า A มี Stock 10",
  "priority": "P0",
  "testType": "Functional",
  "automationCandidate": false,
  "ownerUserId": "...",
  "steps": [
    {
      "stepNo": 1,
      "action": "เปิดเมนูขาย",
      "testData": null,
      "expectedResult": "หน้าขายเปิดสำเร็จ"
    }
  ]
}
```

---

## 13. Test Data

### GET `/test-data`
### POST `/test-data`
### PUT `/test-data/{id}`
### DELETE `/test-data/{id}`
### POST `/test-cases/{testCaseId}/test-data/{testDataId}`

---

## 14. Test Environments

### GET `/test-environments`
### POST `/test-environments`
### PUT `/test-environments/{id}`
### DELETE `/test-environments/{id}`

---

## 15. Test Suites

### GET `/test-suites`
### POST `/test-suites`
### GET `/test-suites/{id}`
### PUT `/test-suites/{id}`
### POST `/test-suites/{suiteId}/cases`
### DELETE `/test-suites/{suiteId}/cases/{testCaseId}`

Add Cases:

```json
{
  "testCaseIds": ["...", "..."],
  "isRequired": true
}
```

---

## 16. Test Cycles

### GET `/test-cycles`
### POST `/test-cycles`
### GET `/test-cycles/{cycleId}`
### PUT `/test-cycles/{cycleId}`
### POST `/test-cycles/{cycleId}/status`
### POST `/test-cycles/{cycleId}/populate-from-suite`
### POST `/test-cycles/{cycleId}/assign`

Create:

```json
{
  "projectId": "...",
  "releaseId": "...",
  "buildId": "...",
  "environmentId": "...",
  "testSuiteId": "...",
  "cycleCode": "RC2-REG-001",
  "cycleName": "RC2 Critical Regression",
  "cycleType": "Regression",
  "ownerUserId": "..."
}
```

Assign:

```json
{
  "assignments": [
    {
      "testCycleCaseId": "...",
      "testerUserId": "..."
    }
  ]
}
```

---

## 17. Test Execution

### GET `/test-cycles/{cycleId}/execution`
### GET `/test-cycle-cases/{cycleCaseId}`
### POST `/test-cycle-cases/{cycleCaseId}/executions`
### GET `/test-cycle-cases/{cycleCaseId}/executions`
### GET `/test-executions/{executionId}`

Create Execution:

```json
{
  "status": "Fail",
  "actualResult": "บันทึกสำเร็จแต่ Stock ถูกตัด 2 ครั้ง",
  "comment": "พบเมื่อ Double Click Save",
  "stepResults": [
    {
      "stepNo": 1,
      "status": "Pass",
      "actualResult": "เปิดหน้าได้"
    },
    {
      "stepNo": 5,
      "status": "Fail",
      "actualResult": "เอกสารถูกสร้าง 2 รายการ"
    }
  ]
}
```

Business Rule:
- Server เป็นผู้สร้าง ExecutionNo
- ห้าม Update Result เดิม
- Retest = POST Execution ใหม่

---

## 18. Evidence / Attachments

### POST `/attachments`
`multipart/form-data`

Fields:
- entityType
- entityId
- projectId
- file

### GET `/attachments?entityType=TestExecution&entityId=...`
### GET `/attachments/{attachmentId}/download`
### DELETE `/attachments/{attachmentId}`

Validation:
- Extension whitelist
- MIME validation
- File size limit
- Virus scan ถ้ามี infrastructure รองรับ

---

## 19. Defects

### GET `/defects`
Filter:
- projectId
- moduleId
- releaseId
- buildFoundId
- fixBuildId
- severity
- priority
- status
- assignee
- reporter
- search

### POST `/defects`
### GET `/defects/{defectId}`
### PUT `/defects/{defectId}`
### POST `/defects/{defectId}/transition`
### POST `/defects/{defectId}/resolution`
### POST `/defects/{defectId}/links/test-cases`
### POST `/defects/{defectId}/links/requirements`
### POST `/defects/{defectId}/links/executions`

Create:

```json
{
  "projectId": "...",
  "moduleId": "...",
  "title": "กด Save ซ้ำทำให้ตัด Stock ซ้ำ",
  "description": "...",
  "severity": "P0",
  "priority": "P0",
  "buildFoundId": "...",
  "environmentId": "...",
  "precondition": "...",
  "stepsToReproduce": "...",
  "expectedResult": "สร้างรายการเดียว",
  "actualResult": "สร้าง 2 รายการ",
  "frequency": "Always",
  "businessImpact": "Stock และยอดขายผิด"
}
```

Transition:

```json
{
  "toStatus": "ReadyForRetest",
  "comment": "แก้แล้วใน Build 10.0.229"
}
```

Resolution:

```json
{
  "rootCause": "Double submit",
  "resolution": "เพิ่ม request lock",
  "fixBuildId": "...",
  "changedComponents": ["Sales", "Core Transaction"],
  "regressionImpact": "Sales Save, Stock, Report"
}
```

---

## 20. Retest

### POST `/defects/{defectId}/retest`

Request:

```json
{
  "buildId": "...",
  "environmentId": "...",
  "testerUserId": "...",
  "result": "Pass",
  "comment": "ไม่พบปัญหาซ้ำ",
  "evidenceAttachmentIds": ["..."]
}
```

Server:
- สร้าง TestExecution ใหม่ถ้ามี linked case
- Update Defect → Closed เมื่อ Pass
- Update → Reopen เมื่อ Fail
- Audit

---

## 21. Regression

### POST `/releases/{releaseId}/regression-impact`

รองรับ `page` (เริ่มที่ 1), `pageSize` (10–200), น้ำหนัก `directImpactWeight`, `historicalDefectWeight`, `criticalPriorityWeight`, `sharedDependencyWeight` และ `recordAnalysis`; response ส่ง `page`, `pageSize`, `totalItems`, `totalPages` พร้อม `riskScore` ราย Test Case โดยเรียงความเสี่ยงสูงก่อน
### GET `/releases/{releaseId}/regression-history?size=20`

แสดงเฉพาะประวัติที่อ้างอิง Build ซึ่งยัง Active (`Build.IsActive = true`)

### Regression Phase 4

- `GET /projects/{projectId}/regression-profiles` อ่าน Profile ของเจ้าของและ Profile แบบ Shared
- `POST /regression-profiles` บันทึก Profile พร้อม `visibility` และ `settingsJson`
- `PUT /regression-profiles/{id}` แก้ไขชื่อ/Visibility/SettingsJson ของ Profile โดยเจ้าของหรือ SYS_ADMIN เท่านั้น
- `DELETE /regression-profiles/{id}` ปิดใช้งาน Profile โดยเจ้าของหรือ SYS_ADMIN
- `GET /projects/{projectId}/regression-schedules` และ `POST /regression-schedules` จัดการ Scheduled Regression
- `DELETE /regression-schedules/{id}` ปิดใช้งาน Schedule โดยเจ้าของหรือ SYS_ADMIN
- `GET /projects/{projectId}/regression-notifications` แจ้ง Active Build ใหม่ที่ตรงกับ Schedule
- `POST /regression-schedules/{scheduleId}/acknowledge/{buildId}` ยืนยันการรับแจ้งเตือน
- `regression-impact` รองรับ `includeAllCaseIds=true` เพื่อเลือก Test Case ครบทุกหน้าจาก Server
### GET `/releases/{releaseId}/regression-activities?size=50`
### GET `/releases/{releaseId}/regression-baseline?baselineBuildId={id}&targetBuildId={id}`
### POST `/regression-suites/generate`
### POST `/test-cycles/{cycleId}/add-impact-cases`

Generate:

```json
{
  "releaseId": "...",
  "buildId": "...",
  "changedModules": ["SALES", "CORE"],
  "includeSharedDependencies": true,
  "minimumPriority": "P1"
}
```

`regression-impact` รับ Build, Module ที่เปลี่ยนแปลง, minimum priority และ change flags
เพื่อคืน Metrics พร้อม Recommended Test Cases แยกเป็น Direct Impact, Shared Dependency,
Critical P0/P1 และ Historical Defect Cases

ทุกครั้งที่วิเคราะห์สำเร็จ ระบบบันทึก Regression History พร้อม Build, จำนวน Module/Case,
Minimum Priority, Change Notes, ผู้วิเคราะห์ และเวลา ส่วน `regression-baseline` เปรียบเทียบ
Executed, Passed, Failed/Blocked, Not Run และ Pass Rate จาก Regression Cycle ของสอง Build

Regression API ใช้สิทธิ์ `REGRESSION.VIEW` สำหรับอ่าน History/Baseline/Activity และ
`REGRESSION.MANAGE` สำหรับวิเคราะห์ Impact, สร้าง Suite และเพิ่ม Case เข้า Cycle;
การวิเคราะห์, สร้าง Suite และเพิ่ม Case เข้า Cycle จะบันทึก Activity audit พร้อมผู้ดำเนินการและเวลา

`regression-suites/generate` สร้าง Test Suite ชนิด Regression จาก Test Case ที่เลือก
และ `add-impact-cases` เพิ่มรายการที่เลือกเข้า Regression Cycle เดิมโดยไม่สร้างรายการซ้ำ

---

## 22. Dashboard

### GET `/dashboard/release-readiness?releaseId=...&buildId=...`

Response:

```json
{
  "release": "2026.08",
  "build": "10.0.228",
  "requirementCoverage": 94.0,
  "executionPercent": 82.0,
  "passRate": 91.7,
  "openDefects": {
    "p0": 0,
    "p1": 2,
    "p2": 12,
    "p3": 18
  },
  "smokePassPercent": 100.0,
  "criticalRegressionPassPercent": 88.0,
  "decision": "CONDITIONAL_GO"
}
```

### GET `/dashboard/module-health`
### GET `/dashboard/defect-trend`
### GET `/dashboard/tester-workload`

---

## 23. Daily / Weekly Status

### GET `/qa-status/daily`
### POST `/qa-status/daily`
### GET `/qa-status/weekly`
### POST `/qa-status/weekly`
### POST `/qa-status/weekly/generate`

---

## 24. Test Summary

### POST `/test-summaries/generate`

Request:

```json
{
  "releaseId": "...",
  "buildId": "..."
}
```

### GET `/test-summaries/{id}`
### GET `/releases/{releaseId}/test-summary/latest`

---

## 25. Risk Acceptance

### GET `/risk-acceptances`
### POST `/risk-acceptances`
### GET `/risk-acceptances/{id}`
### PUT `/risk-acceptances/{id}`
### POST `/risk-acceptances/{id}/submit`
### POST `/risk-acceptances/{id}/approve`
### POST `/risk-acceptances/{id}/reject`

Approve:

```json
{
  "comment": "ยอมรับความเสี่ยงสำหรับ Release นี้"
}
```

---

## 26. Release Sign-off

### GET `/releases/{releaseId}/signoffs`
### POST `/releases/{releaseId}/signoffs`

Request:

```json
{
  "buildId": "...",
  "signoffType": "QA",
  "decision": "CONDITIONAL_GO",
  "comment": "มี P2 จำนวน 2 รายการและมี workaround"
}
```

### GET `/releases/{releaseId}/release-gate`

Response:

```json
{
  "smoke": {"passed": true},
  "openP0": 0,
  "p1Blockers": 0,
  "requirementCoverage": 96.5,
  "criticalRegression": 98.0,
  "updateTestPassed": true,
  "approvedRisks": 2,
  "recommendedDecision": "GO"
}
```

---

## 27. Users / Roles / Permissions

### GET `/users`
### POST `/users`
### PUT `/users/{id}`
### POST `/users/{id}/roles`

### GET `/roles`
### POST `/roles`
### PUT `/roles/{id}`
### POST `/roles/{id}/permissions`

---

## 28. Notifications

### GET `/notifications`
### POST `/notifications/{id}/read`
### POST `/notifications/read-all`

---

## 29. Audit

### GET `/audit-logs`
Filter:
- entityType
- entityId
- userId
- action
- dateFrom
- dateTo

---

## 30. Import / Export

### POST `/imports/requirements`
### POST `/imports/test-cases`
### POST `/imports/test-data`

### GET `/exports/rtm`
### GET `/exports/test-cases`
### GET `/exports/executions`
### GET `/exports/defects`
### GET `/exports/test-summary`

แนะนำให้ Large Export ใช้ Async Job + Notification

---

## 31. HTTP Status

| Status | ใช้เมื่อ |
|---|---|
| 200 | GET/PUT สำเร็จ |
| 201 | POST สร้างสำเร็จ |
| 204 | Delete/Action สำเร็จไม่มี Body |
| 400 | Validation |
| 401 | ไม่ Login |
| 403 | ไม่มี Permission |
| 404 | ไม่พบ Resource |
| 409 | Conflict / Invalid Transition / Duplicate |
| 422 | Business Rule ไม่ผ่าน |
| 500 | Unexpected Error |

---

## 32. Authorization ตัวอย่าง

| API Area | Permission |
|---|---|
| Requirement Edit | REQUIREMENT.EDIT |
| Test Case Edit | TESTCASE.EDIT |
| Run Test | EXECUTION.RUN |
| Assign QA | EXECUTION.ASSIGN |
| Create Defect | DEFECT.CREATE |
| Resolve Defect | DEFECT.RESOLVE |
| Approve Risk | RISK.APPROVE |
| Release Sign-off | RELEASE.SIGNOFF |
| Export | REPORT.EXPORT |

---

## 33. API Versioning

เริ่มต้น:
`/api/v1/...`

หาก Contract เปลี่ยนแบบ Breaking:
`/api/v2/...`

---

## 34. Logging / Trace

ทุก Request ควรมี:
- TraceId
- UserId
- Endpoint
- StatusCode
- Duration
- Entity ID เมื่อเกี่ยวข้อง

ห้าม Log:
- Password
- Access Token
- Secret
- Sensitive Test Data แบบ Plain Text

## Automation Trigger & Queue (2026-08-22)

ทุก endpoint ใช้ JWT, permission `EXECUTION.RUN` และ Project access:

- `GET /api/v1/automation/queue?projectId=&buildId=&take=` — ประวัติ Queue; ไม่คืน lease token
- `POST /api/v1/automation/queue?projectId=` — สร้างงานด้วย `projectId`, `releaseId`, `buildId`, optional `testCycleId`, `targetApp: pos|app`, optional `notes`
- `POST /api/v1/automation/queue/claim?projectId=` — Runner claim งานเก่าสุดด้วย `runnerName` และ `targetApps`; คืน `204` เมื่อไม่มีงาน หรือคืนงานพร้อม lease token
- `POST /api/v1/automation/queue/{jobId}/status?projectId=` — Runner ส่ง `leaseToken`, `status`, optional `errorMessage`/`automationRunId`
- `DELETE /api/v1/automation/queue/{jobId}?projectId=` — ยกเลิกได้เฉพาะสถานะ Queued/Claimed

### Automation Runner Agents

- `GET /api/v1/automation/agents?projectId=` — คืน agent พร้อม `connectivity`, state, capabilities และ heartbeat ล่าสุด
- `POST /api/v1/automation/agents/heartbeat?projectId=` — ลงทะเบียน/อัปเดต agent และต่ออายุ lease เมื่อส่ง `currentJobId` + `leaseToken`
- Payload มี `runnerName`, `machineName`, `version`, `capabilities: [pos|app]`, `state: Idle|Busy` และข้อมูล lease แบบ optional
- Queue DTO เพิ่ม `leaseExpiresAt`/`attemptCount`; lease token ยังคงคืนเฉพาะ claim response

### Automation Scheduling & Retry

- `GET/POST /api/v1/automation/schedules?projectId=` — ดู/สร้าง recurring schedule
- `DELETE /api/v1/automation/schedules/{scheduleId}?projectId=` — ปิด schedule แบบ soft disable
- `GET /api/v1/automation/notifications?projectId=` — alerts จาก retry queued และ terminal failed
- Schedule payload: project/release/build, name, targetApp, pack, frequency `Daily|Weekdays`, `runAtUtc`, `maxAttempts` 1–5
- Queue status update เพิ่ม `errorType`; retry ได้เฉพาะ Infrastructure, Timeout และ ApplicationStart
## Addendum: Test Cycle Clone API

### POST `/test-cycles/{sourceCycleId}/clone`

สร้าง Target Test Cycle ใหม่จาก Source Cycle โดยไม่แก้ Source Cycle และไม่คัดลอก Execution History

```json
{
  "targetReleaseId": "...",
  "targetBuildId": "...",
  "targetEnvironmentId": "...",
  "cycleCode": "",
  "cycleName": "Regression on new build",
  "cycleType": "Regression",
  "startDate": "2026-09-16T00:00:00Z",
  "endDate": "2026-09-20T00:00:00Z",
  "ownerUserId": "...",
  "notes": "",
  "cloneMode": "SourceSnapshot"
}
```

`cloneMode` รองรับ:

- `SourceSnapshot`: คัดลอก Test Case membership, order และ `TestCaseRevisionNo` จาก Source Cycle
- `SuiteLatest`: ใช้ Test Suite ของ Source Cycle และดึง membership/Revision ล่าสุด; ถ้า Source ไม่มี Suite ให้ตอบ validation error

กติกา response และ transaction:

- Response เป็น `TestCycleDto` ของ Target Cycle ที่สร้างใหม่
- `TestCycleDto` ของ Target เพิ่ม `copiedFromTestCycleId` และ `copiedFromCycleCode` สำหรับแสดง lineage
- Target ต้องอยู่ใน Project เดียวกับ Source, Build ต้องอยู่ใต้ Target Release และ Environment ต้อง Active
- Release ที่ `Released` หรือ `Cancelled` ห้ามใช้เป็น Target
- Target Cycle เริ่ม `Draft`; Target Cases เริ่ม `NotRun` และไม่มี Assignment/Execution/Step Result/Evidence
- บันทึก `CopiedFromTestCycleId` และ Audit log ที่มี Source/Target กับจำนวน Case
- ผู้เรียกต้องมี JWT, `EXECUTION.RUN` และ Project access; ตรวจ permission ที่ Backend เสมอ

## Addendum: CRM Read-only API (Phase 1)

All CRM endpoints require JWT authentication and either the `CRM.VIEW` permission or the `SYS_ADMIN` role. The backend resolves the
current QA Hub user from the JWT, loads that user's CRM configuration, and enforces
the current user's CRM username in the ticket's `Assignto`, `OwnerSubjectId`, or `sysDevelop` field. `userId`,
`crmUsername`, and scope fields are never accepted
from the frontend request.
Authorization is also checked against the current database profile for the JWT subject, so a newly granted or revoked CRM permission takes effect without relying on a stale permission claim.

- `GET /api/v1/crm/connection` returns per-user CRM configuration state without exposing a password or token.
- `GET /api/v1/crm/tickets?page=1&pageSize=25&search=&status=&from=&to=` returns normalized ticket rows,
  total count, page metadata, and summary counts. Default date range is the latest 30 days; maximum
  `pageSize` is 100.
- `GET /api/v1/crm/tickets/{jobNo}` returns a normalized read-only ticket detail plus CRM answer history.
  The backend re-checks the returned `Assignto`, `OwnerSubjectId`, and `sysDevelop` against the current user's CRM username and returns
  `CRM_TICKET_NOT_FOUND` when the ticket is outside the user's scope.
- CRM date fields (`contactDate`, `dueDate`, `lastReplyAt`, answer `answerDate`) are returned as ISO 8601 UTC
  (`yyyy-MM-ddTHH:mm:ss.fffffffZ`). CRM values without an offset — `dd/MM/yyyy[ HH:mm[:ss]]` (B.E. or A.D.),
  `yyyy-MM-dd[THH:mm:ss]`, or digits `yyyyMMdd[HHmm[ss]]` — are treated as Bangkok time (+07:00), including
  date-only values (midnight Bangkok). `from`/`to` are Bangkok calendar dates and are compared against the
  ticket's Bangkok-local contact date.
- CRM resource limits (server stability): every CRM/BlueID HTTP call times out after 30 s (`CRM_TIMEOUT`, 408) and reads
  at most 20 MB (`CRM_RESULT_TOO_LARGE`); connection failures map to `CRM_UNAVAILABLE` (503). At most 2 headless-browser
  BlueID logins run at once across all users (others wait up to 90 s, then `CRM_UNAVAILABLE`). A non-network login failure
  (wrong credentials / changed login page) pauses that user's logins for 5 minutes (`CRM_UNAUTHORIZED` with the retry time)
  until the cooldown ends or the user saves their CRM account again.
- `GET /crm/tickets`: `from`–`to` may span at most 366 days (`CRM_INVALID_QUERY`). The raw HelpDeskExport result is cached per
  user + status + date range for 60 s so paging/search do not re-download it; `refresh=true` bypasses the cache (the UI sends it
  for the Refresh button and after an update). `lastFetchedAt` is the time the cached data was fetched from CRM.
- When the BlueID login server (or BlueSea) cannot be reached from the API host (browser `net::ERR_*`, e.g.
  `ERR_CONNECTION_TIMED_OUT`), CRM endpoints return `503` with code `CRM_UNAVAILABLE` and a `detail` that names the
  network error or the time (Bangkok) of the next login attempt. New BlueID logins are paused for 1 minute for all
  users after such a failure (`CrmTokenService.UnavailableCooldown`); cached CRM tokens keep working. `CrmSyncWorker`
  logs one warning and stops the current poll tick instead of retrying every linked Defect.
- Detail Answer history follows the CRM DataTables `start`/`length` contract up to 1,000 records and de-duplicates by `answerNo`; if CRM ignores pagination, the adapter stops when a page contains no new answers.
- The CRM adapter currently uses `POST /Support/HelpDeskExport` without an upstream assignee filter so a QA-to-Development handoff remains visible, filters grouping rows without `JobNo`,
  applies user scope across `Assignto`, `OwnerSubjectId`, and `sysDevelop`, then applies local pagination, and caps the returned ticket set at 5,000 rows.
- Ticket mapping accepts the casing/field aliases observed across CRM List and Detail responses for
  Service Type, Product, Owner, Member, and Last Reply without changing the normalized QA Hub DTO.
- Error responses expose a stable `code` extension: `CRM_NOT_CONFIGURED`, `CRM_UNAUTHORIZED`,
  `CRM_TIMEOUT`, `CRM_RESULT_TOO_LARGE`, `CRM_RATE_LIMITED`, `CRM_BAD_RESPONSE`, `CRM_UNAVAILABLE`,
  `CRM_TICKET_NOT_FOUND`, and `CRM_INVALID_QUERY`.
- `POST /api/v1/crm/connection/test` performs a user-initiated, read-only CRM probe using the current user's encrypted configuration; it returns `{ isReachable, checkedAt }` and never returns a CRM password or token.
- Connection Probe maps configuration, credential, timeout, rate-limit, upstream availability, and malformed-response failures to the same stable CRM error codes used by the ticket endpoints.

### Addendum: CRM Board and QA Hub Defect linking (Phase 3)

- `GET /api/v1/crm/tickets/{jobNo}/defect` returns the QA Hub Defect linked to the CRM Ticket, if one exists. The backend re-checks the CRM Ticket scope for the current user before returning the link.
- `POST /api/v1/crm/tickets/{jobNo}/link-defect` links an in-scope CRM Ticket to an existing accessible Defect. It requires `CRM.VIEW` plus `DEFECT.EDIT`, validates Project Access, rejects duplicate Ticket links and conflicting existing links, captures the latest CRM status/assignee snapshot, and writes a `CrmTicketLinked` activity entry.
- `DELETE /api/v1/crm/tickets/{jobNo}/defect` requires `CRM.VIEW` plus `DEFECT.EDIT` and Project Access. It removes the QA Hub link from the accessible Defect, clears the CRM link/snapshot fields, records a `CrmTicketUnlinked` activity, and leaves the Defect itself intact. If no link exists it returns `404 CRM_DEFECT_LINK_NOT_FOUND`.
- Link requests accept only `{ "defectId": "..." }`; the frontend cannot provide or override the CRM ownership scope.
### CRM Phase 4 — Controlled CRM Update

- `GET /api/v1/crm/staff` requires `CRM.VIEW`; returns `[{ staffCode, name }]` from the BlueID staff directory (no email) for
  showing "6101 เหรียญทอง" instead of bare staff codes. Cached server-side for 1 hour and shared by all users
  (`CrmStaffDirectoryCache`); the first request after expiry reloads it with the caller's CRM token.
- `GET /api/v1/crm/assignees` requires `CRM.VIEW` and `CRM.EDIT`; returns the allow-listed CRM assignee directory without credentials.
- `GET /api/v1/crm/lookups` requires `CRM.VIEW` and `CRM.EDIT`; returns `{ serviceTypes: [{ id, name }], products: [{ id, name }] }` read live from CRM `/Support/SysSrviceType` and `/Support/Products` (array or `data`/`result`/`items`/`rows` wrapper, field names matched case-insensitively). An unreadable list returns `502 CRM_BAD_RESPONSE` naming the fields found (names only, no values).
- `POST /api/v1/crm/tickets` requires `CRM.VIEW` and `CRM.EDIT`. **`multipart/form-data`** (limit 27 MB): fields `subject*, description*, serviceTypeId*, productId*, member*, firstName*, lastName, tel*, nickName, lineId, email, customerType (1 None MA \| 2 MA \| 3 Demo \| 4 Dealer, default 1), status (CRM statuses, default Open), source (Call \| Email \| Facebook \| Walk In \| Remote \| Line, default Call), dueDate (yyyy-MM-dd, default today, not before today), refJobNo, assignToStaffCode, ownerStaffCode, developmentStaffCode` + files `files` (≤ 10, ≤ 5 MB each, ≤ 25 MB total, .jpg/.jpeg/.png/.xlsx/.xls/.doc/.docx/.pdf). Creates a CRM HelpDesk Ticket via `POST /Support` and returns `{ jobNo }`. Field mapping confirmed against BlueSea JobDetailsHD (same `/Support` endpoint): LineID → `Fax`, files → `Images1..N`, development empty → `SysDevelop=0` (ไม่ระบุพนักงาน). RecipientId/Posted are always the caller; assignTo/owner empty = the caller; a non-empty staff code must be the caller or in `/crm/assignees` (`CRM_INVALID_ASSIGNEE`). Version/OS `0`. Validation errors return `400 CRM_INVALID_CREATE` (required fields, lengths, email format, lookups, files). It does not create a QA Hub Defect.
- `PATCH /api/v1/crm/tickets/{jobNo}` requires `CRM.VIEW` and `CRM.EDIT`. Works on any Ticket in the current user's CRM scope
  (backend re-checks `Assignto`, `OwnerSubjectId`, or `sysDevelop` via the detail call); a linked Defect is **not** required. The request is `multipart/form-data` (limit 27 MB):
  fields `{ message?, status?, serviceTypeId?, productId?, assignToOwner?, assignToStaffCode?, expectedStatus?, expectedAssignee? }` plus repeated `files` fields.
  At least one of the update fields or one attachment is required. Attachments use the same rules as create (≤ 10 files, ≤ 5 MB each, ≤ 25 MB total,
  `.jpg/.jpeg/.png/.xlsx/.xls/.doc/.docx/.pdf`) and are forwarded to CRM as `Images1..N`; an attachment-only update creates an automatic history note.
  `message` (≤ 1000 chars) is sent as the CRM update `Description`, which
  CRM records as a **new row in the answer history**; without a message a short `[QA Hub] {actor} (dd/MM/yyyy HH:mm B.E.)`
  note describing the change is sent instead. `status` must be one of Open, Continue, Approve, Develop, Planning, Test,
  EditErr, Finish, Close. The QA Hub contract and CRM wire value both use `Close`; the backend also normalizes legacy CRM
  `Closed` responses back to `Close`. A Ticket already in `Close` is read-only; `PATCH /crm/tickets/{jobNo}` returns HTTP 409
  with code `CRM_TICKET_CLOSED`. `assignToOwner=true` sets `Assignto = OwnerSubjectId` (CRM's "to เจ้าของเรื่อง"; `SysDevelop`
  unchanged) and cannot be combined with `assignToStaffCode` (which must be in the allowed directory). Expected values
  mismatch → `409 CRM_CONFLICT`; nothing to change or missing owner → `400 CRM_INVALID_UPDATE`. If the Ticket is linked to
  an accessible Defect, its CRM snapshot is updated and a `CrmTicketUpdated` activity is written.
- When `assignToStaffCode` is supplied, the backend validates it against the allow-listed CRM/BlueID directory; UI selection is not treated as a security boundary.
- When `serviceTypeId` is supplied, the backend validates it against the live `/Support/SysSrviceType` lookup and sends it as `SysserViceType` in the CRM update payload.
- When `productId` is supplied, the backend validates it against the live `/Support/Products` lookup and sends it as `SysProductId` in the CRM update payload.
- When `status=Close`, the backend also sends the CRM form's required close-notification metadata (`Body`, `SubjectEmail`, `CC`, and `ToAdd` when the recipient email is available); other updates omit these notification fields.

### CRM Phase 5 — Create QA Hub Defect from CRM Ticket

- `POST /api/v1/crm/tickets/{jobNo}/create-defect` requires `CRM.VIEW`, `DEFECT.EDIT`, and Project Access. It creates a QA Hub Defect only; it never creates or updates a CRM Ticket.
- Request: `{ "projectId": "...", "title": "...", "severity": "Critical|High|Medium|Low", "description": "..." }`. Title and Description may be prefilled from CRM detail, but final values are confirmed by the user.
- The backend re-checks CRM ownership, validates the active Project, rejects an already-linked Ticket with `409 CRM_TICKET_ALREADY_LINKED`, generates a QA Hub Defect code, stores `CrmTicketId`, captures CRM Status/Assignee, and records `CreatedFromCrm` activity.

### CRM Flow Tracking

- `GET /api/v1/crm/tickets/{jobNo}/flow-history` requires `CRM.VIEW`. The API re-checks the current user's CRM ticket scope, then returns chronological Support/QA/Dev flow snapshots with the status and actor that observed each change.
- CRM List/Detail reads and successful Ticket updates persist a deduplicated flow snapshot in the existing `AuditLogs` table (`EntityType = CrmTicketFlow`, `EntityId = JobNo`). A new record is written only when Support, QA, Dev, or status changes. `sysDevelop` is normalized into the Dev step and CRM value `0` means no developer assigned yet.
