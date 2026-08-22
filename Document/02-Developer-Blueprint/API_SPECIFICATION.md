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
  "password": "********"
}
```

Response:

```json
{
  "accessToken": "...",
  "expiresIn": 3600,
  "user": {
    "userId": "...",
    "displayName": "QA 01",
    "roles": ["QA_TESTER"]
  }
}
```

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
