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

### GET `/test-cases/picker?projectId={projectId}`

Test Case ทั้ง Project แบบย่อในคำขอเดียว (ไม่แบ่งหน้า ไม่มี Step/StepCount/COUNT) สำหรับตัวเลือกในฟอร์ม Test Suite และ AI Generate — คืน `testCaseId`, `projectId`, `moduleId`, `testCaseCode`, `title`, `priority`, `testType`, `status` เรียงตาม `testCaseCode`; Project ที่ผู้เรียกไม่มีสิทธิ์คืนรายการว่าง (policy `TestCaseView`, `RequireProjectAccess`) — ใช้แทนการไล่ดึง `GET /test-cases` ทีละ 100 (Project 6,500 เคสเคยต้องยิง 66 ครั้ง)

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

แบ่งหน้า `page`/`size` — `size` 20–500 (เดิมสูงสุด 100; ขยาย 2026-10-20 ให้หน้า Test Suite/Test Cycle ดึงทุก Suite ในคำขอเดียว) คืน `{ total, rows }`

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

แบ่งหน้า `page`/`size` — `size` 10–500 (เดิมสูงสุด 150; ขยาย 2026-10-20) รองรับ `status`, `projectId`, `releaseId`, `buildId`, `moduleId`, `cycleType`, `search`, `createdBy` — Execution Workspace ขอ `status=InProgress&size=500` เพื่อให้ได้ Cycle ที่กำลังทดสอบครบทุกรายการ

`summary.statusCounts` (เพิ่ม 2026-10-20) = จำนวน Cycle ต่อสถานะ (`{ "InProgress": 3, "Closed": 1, ... }`) ตาม filter อื่นทั้งหมด **ยกเว้น** `status` — หน้า Test Cycle ใช้แสดงชิปสถานะจากคำขอเดียว (เดิมยิงแยกทีละสถานะ 5 คำขอ ซึ่งถูกยกเลิกพร้อมกันบ่อยเมื่อเปลี่ยน filter)
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

`cases[].assignedTesterUserId` (เพิ่ม 2026-10-20) = ผู้ทดสอบที่ถูก assign เคสนี้ (null = ยังไม่ assign) — คิวใช้กรอง "เฉพาะเคสที่ assign ให้ฉัน"

### GET `/test-cycles/{cycleId}/cases/{cycleCaseId}`

รายละเอียดเคสใน Execution Workspace — `history[].stepResults` (เพิ่ม 2026-10-20) = ผลราย Step ของ Run นั้น `[{ stepNo, status, actualResult }]` เรียงตาม stepNo

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
### GET `/defects/{defectId}/activities`

คืน activity ใหม่ → เก่า: `activityId`, `defectId`, `activityType`, `description`, `performedByUserId`, `performedAt`, `attachments` — `attachments` มีค่าเฉพาะ activity `AttachmentAdded` (รายการ `{attachmentId, fileName, size}` ของรูปที่อัปโหลดครั้งนั้น ซึ่ง API จับคู่จากผู้อัปโหลดเดียวกันและเวลาที่ใกล้สุดภายใน 5 วินาที เพราะ activity ไม่ได้เก็บ id ของรูป; รูปที่ถูกลบแล้วไม่แสดง) ส่วน activity อื่นเป็น `null`; ดึงรูปด้วย `GET /defects/{defectId}/attachments/{attachmentId}`

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
- `GET /api/v1/crm/tickets/{jobNo}` returns a normalized read-only ticket detail plus CRM answer history. CRM `HelpDesksJob` carries only the Service/Product IDs (`sysserviceType`/`sysProductId`), so the API fills `serviceType`/`product` names from the CRM `SysSrviceType`/`Products` lookups (best-effort: a lookup failure leaves the name empty instead of failing the detail).
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
- Previously held tickets: a ticket whose status is not `Close`/`Finish` also stays in scope (List, Detail, and the endpoints that
  re-check scope through Detail) when the current user previously held it — a `CrmTicketFlow` audit snapshot recorded them as
  `Assignto` (e.g. Support → QA → back to Support), or a CRM answer on the ticket was `Posted` by them. Once the ticket is
  `Close`/`Finish`, only the current `Assignto`/`OwnerSubjectId`/`sysDevelop` match applies.
- `GET /crm/tickets` accepts `scope=mine` (default: the user is the current `Assignto`, any status) or `scope=previous`
  (not `Close`/`Finish`, `Assignto` is someone else, and the user is the `OwnerSubjectId`, the `sysDevelop`, or a previous
  holder); any other value returns `CRM_INVALID_QUERY`. Closed tickets where the user is only Owner/Developer are not listed
  (Detail/Job No. lookup still finds them). `rows`, `total`, and
  `summary` cover the selected scope only; `scopeCounts: { mine, previous }` counts both scopes after the date/status/search
  filters and before pagination.
- `GET /crm/tickets` also returns `previousQa: { "<JOBNO>": "<staff>" }` for the returned rows: the latest Flow Tracking snapshot
  whose `Assignto` was neither the Support owner nor the job's Developer (taken from the newest snapshot that has `sysDevelop`,
  since `Assignto` often moves to Dev before `sysDevelop` is set). Keys are upper-case Job No.; jobs without such history are
  omitted. The UI shows it as the previous holder ("ก่อนหน้า …") when it differs from the current `Assignto`.
- Flow Tracking audit summaries use CRM field names: `เจ้าของเรื่อง <owner> → Assign To <assignto> → Development <sysDevelop> · สถานะ <status>`.
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

## Addendum: Performance & Evaluation API (เฟส 1)

- Base path `/api/v1/performance`, policy `PerformanceView` (SYS_ADMIN, `PERFORMANCE.VIEW` หรือ `PERFORMANCE.EDIT`). ไม่ใช้ `[RequireProjectAccess]`; metric กรองตาม Project ที่ผู้เรียกเป็นสมาชิก
- ผู้ไม่มี `PERFORMANCE.EDIT` ขอได้เฉพาะ `userId` ของตัวเอง (อื่น ๆ ตอบ `403`), ข้อสังเกต (`Kind = Concern`) ของตัวเองตอบ `404`
- `from`/`to` เป็นวันที่ตามเวลาไทย `yyyy-MM-dd` (ค่าเริ่มต้น: ต้นเดือนถึงวันนี้, ช่วงสูงสุด 366 วัน — เกินหรือกลับด้านตอบ `400`)

| Method | Path | สิทธิ์ | คำอธิบาย |
|---|---|---|---|
| GET | `/performance/team?from&to` | EDIT | `{ from, to, members: [{ profile, metrics }] }` |
| GET | `/performance/users` | EDIT | ผู้ใช้ Active `{ userId, displayName }` |
| GET | `/performance/users/{userId}/summary?from&to` | ตัวเอง/EDIT | `{ from, to, profile, metrics, weekly: [{ weekStart, executions, defects, testCases }] }` |
| GET | `/performance/users/{userId}/timeline?from&to&page&size` | ตัวเอง/EDIT | `{ items: [{ timestamp, source, action, summary, reference }], total, page, size }` (size ≤ 100) |
| PUT | `/performance/users/{userId}/profile` | EDIT | `{ employeeCode, position, level, supervisorUserId, hireDate, employmentStatus, probationEndDate, notes }` — Probation/Extended ต้องมี `hireDate`; เขียน AuditLog `EmployeeProfile` |
| GET | `/performance/users/{userId}/work-logs?from&to` | ตัวเอง/EDIT | รายการบันทึกงาน (`canEdit` บอกว่าผู้เรียกแก้ได้หรือไม่) |
| POST | `/performance/work-logs` | ตัวเอง/EDIT | `{ userId, workDate, category, kind, title, description, effortHours, projectId, referenceUrl }` — `kind=Concern` ต้องมี EDIT; `referenceUrl` ต้องเป็น http/https; `projectId` ต้องเป็น Project ที่ผู้เรียกเป็นสมาชิก |
| PUT/DELETE | `/performance/work-logs/{id}` | ผู้สร้าง/EDIT | แก้/ลบบันทึกงาน |

### Performance — Evaluation API (เฟส 2)

| Method | Path | สิทธิ์ | คำอธิบาย |
|---|---|---|---|
| GET | `/performance/evaluations?userId&openOnly` | ตัวเอง/EDIT | รายการ `{ evaluationId, userId, userName, type, round, year, periodStart, periodEnd, dueDate, status, overallScore, grade, recommendation, evaluatorName, isOverdue, ... }` (ไม่มี EDIT = ของตัวเองเท่านั้น; ไม่ระบุ `userId` = มุมมองทีม ไม่รวมพนักงานที่ลาออก/บัญชีถูกปิด — ระบุ `userId` ยังได้ประวัติของคนนั้นครบ) |
| GET | `/performance/evaluations/{id}` | ตัวเอง/EDIT | รายละเอียด + `scores` + `metrics` (snapshot เมื่อปิดแล้ว) + `canSelfEdit/canManagerEdit/canAcknowledge/canReopen`; ของคนอื่นที่ไม่มีสิทธิ์ตอบ `404` |
| POST | `/performance/evaluations/quarterly` | EDIT | `{ year, quarter }` → `{ created, skipped }` |
| PUT | `/performance/evaluations/{id}/self` | เจ้าของ | `{ scores: [{ evaluationScoreId, score, comment }], comment, submit }` — `submit=true` ต้องให้คะแนนครบ; ส่งแล้วแก้ไม่ได้ (`409`) |
| PUT | `/performance/evaluations/{id}/manager` | EDIT (ไม่ใช่เจ้าของ) | `{ scores, strengths, improvements, nextGoals, recommendation, extendToDate, complete }` — `recommendation` (`Pass/Extend/NotPass`) เฉพาะรอบ `PFinal`; `complete=true` ต้องให้คะแนนครบ (`400`) |
| POST | `/performance/evaluations/{id}/acknowledge` | เจ้าของ | `{ comment }` — เฉพาะสถานะ Completed |
| POST | `/performance/evaluations/{id}/reopen` | EDIT | `{ reason }` (บังคับ) |
| GET | `/performance/templates` | EDIT | แบบประเมิน Probation/Quarterly (สร้างชุดตั้งต้นให้ถ้ายังไม่มี) |
| PUT | `/performance/templates/{id}` | EDIT | `{ name, criteria: [{ group, name, description, weight }], externalWeight? }` — 1–15 เกณฑ์, น้ำหนักรวม 100; `externalWeight` 0–50 (% ของคะแนนแผนกอื่นในผลรวม, ค่าเริ่มต้น 30, ไม่ส่ง = คงเดิม) copy ไปที่รอบที่สร้างหลังจากนี้ |
| POST | `/performance/evaluations/{id}/external-reviews` | EDIT + หัวหน้าตรง | `{ reviewerName, reviewerDepartment?, note?, expiresInDays: 7/14/30/60 }` → `EvaluationExternalReviewDto` (มี `code` สำหรับลิงก์ `/?er=code`); รอบที่ปิดแล้ว `409`; สูงสุด 10 ลิงก์ที่ยังไม่ยกเลิกต่อรอบ |
| POST | `/performance/evaluations/{id}/external-reviews/{reviewId}/revoke` | EDIT + หัวหน้าตรง | ยกเลิกลิงก์ที่ยังไม่ส่ง (ส่งแล้ว `409`) |

`PUT /performance/users/{userId}/profile` สร้าง/เลื่อนรอบประเมินทดลองงานให้อัตโนมัติหลังบันทึก

### Performance — สรุปผลงานรายคน + ลิงก์แชร์ HR (2026-10-09)

| Method | Path | สิทธิ์ | Request / Response |
|---|---|---|---|
| GET | `/performance/users/{userId}/report?from&to&evaluations=true&workLogs=true` | ตัวเอง/EDIT | `EmployeeReportDto` `{ from, to, generatedAt, profile, metrics, weekly, includesWorkLogs, workLogs, includesEvaluations, evaluations, comparison }` — `profile` ไม่มี UserId/อีเมล/Notes; `workLogs` ไม่รวม Concern และไม่มีไฟล์แนบ/ลิงก์อ้างอิง; `evaluations` ทุกรอบ แต่คะแนน/เกรด/ผลทดลองงานเฉพาะรอบ Completed/Acknowledged |
| GET | `/performance/users/{userId}/share-links` | EDIT | ลิงก์ล่าสุด 50 รายการ `{ shareLinkId, url, from, to, includeEvaluations, includeWorkLogs, note, expiresAt, createdAt, createdByName, revokedAt, revokedByName, viewCount, lastViewedAt, status (Active/Expired/Revoked) }` |
| POST | `/performance/users/{userId}/share-links` | EDIT | `{ from, to, expiresInDays (7/14/30/60/90), includeEvaluations, includeWorkLogs, note (≤200) }` → ลิงก์ `{PublicWebBaseUrl}/?p=<code 10 ตัว>`; เขียน AuditLog `PerformanceShareLink` (Created) |
| POST | `/performance/share-links/{id}/revoke` | EDIT | ยกเลิกลิงก์ (ไม่ลบแถว) — AuditLog `Revoked` |
| GET | `/shared/evaluation-reviews/{code}` | **Anonymous** (rate limit `share`) | แบบฟอร์มของหัวหน้าแผนกอื่น `ExternalReviewFormDto` { reviewerName, reviewerDepartment, note, requestedByName, employeeName, position, type, round, year, periodStart, periodEnd, templateName, expiresAt, status, submittedAt, criteria[{ evaluationScoreId, group, name, description, weight }] } — **ไม่มีคะแนนตนเอง/หัวหน้าตรง**; ส่งแล้วคืน `status: Submitted` + criteria ว่าง; `404` = ไม่มี code, `410` = ยกเลิก/หมดอายุ/การประเมินปิดแล้ว; `Cache-Control: no-store` |
| POST | `/shared/evaluation-reviews/{code}` | **Anonymous** (rate limit `share`) | `{ scores: [{ evaluationScoreId, score: 1–5, comment? }], comment? }` ต้องครบทุกเกณฑ์ (`400`), ส่งได้ครั้งเดียว (`409`), บันทึก AuditLog `Submitted` (userId ว่าง) |
| GET | `/shared/performance/{code}` | **Anonymous** (rate limit `share`) | `{ report: EmployeeReportDto, expiresAt, sharedAt, sharedByName, note }` + `Cache-Control: no-store`; `404` = ไม่มี code, `410` = หมดอายุ/ถูกยกเลิก/ผู้สร้างไม่ Active หรือไม่มี PERFORMANCE.EDIT แล้ว; ทุกครั้งที่เปิดสำเร็จเพิ่ม `viewCount` |

ข้อมูลในลิงก์คำนวณสดทุกครั้งตามสิทธิ์ของผู้สร้าง (metric กรองตาม Project ที่ผู้สร้างเป็นสมาชิก ณ ตอนเปิด)

### Performance — สรุปประจำสัปดาห์ส่งผู้บริหาร (2026-10-09)

| Method | Path | สิทธิ์ | Request / Response |
|---|---|---|---|
| GET | `/performance/weekly?weekStart=` | EDIT | `WeeklyReportDto` `{ weekStart, weekEnd, isCurrentWeek, generatedAt, current, previous, typical, typicalFrom, typicalTo, assignedPending, assignedOverdue, members[], probation[], evaluationsDue[], anomalies[], crmDataSince, narrative }` — สัปดาห์ = จันทร์–อาทิตย์เวลาไทย (`weekStart` ไม่ระบุ = สัปดาห์นี้, วันอื่นปัดเป็นวันจันทร์, อนาคต/เกิน 2 ปี = 400); `current`/`previous` = ผลรวมทีม, `typical` = ค่ากลาง (median) รายช่องของ 4 สัปดาห์ก่อนหน้า; `members[]` = ผู้ใช้ Active ที่ไม่ลาออก `{ userId, displayName, position, employmentStatus, probationDay, probationTotalDays, current, previous, workItems[] }` (`workItems` = บันทึกงาน/ผลงานเด่นของสัปดาห์ **ไม่รวม Concern**); `evaluationsDue[]` = รอบที่ยังไม่ปิด เลยกำหนดหรือครบกำหนดภายใน 14 วันหลังสัปดาห์; `anomalies[]` `{ date, kind (Execution/Defect/TestCase), count, typicalDaily, inReportWeek }` = วันที่ ≥ 100 รายการและ ≥ 4 เท่าของค่ากลางวันอื่น; `crmDataSince` = วันแรกที่ดึง CRM ทั้งทีมสำเร็จ; `narrative` = null เมื่อยังไม่เขียน |
| PUT | `/performance/weekly/{weekStart}/note` | EDIT | `{ health (Green/Yellow/Red/null), headline (≤500), keyRisk (≤2000), ask (≤2000), nextWeekPlan (≤2000), memberNotes: { userId: ข้อความ ≤300 } }` → `{ health, headline, keyRisk, ask, nextWeekPlan, memberNotes, updatedAt, updatedByName }`; `weekStart` ต้องเป็นวันจันทร์; เขียน AuditLog `WeeklyReport` (Created/Updated) |

ไม่มีคะแนนหรืออันดับรายคน — metric ชุดเดียวกับ `/performance/team` (กรองตาม Project ของผู้ดู)

### Performance — งานจาก CRM

`metrics` ของ `/performance/team`, `/performance/users/{userId}/summary` และ `/performance/evaluations/{id}` มีฟิลด์ `hasCrmAccount`, `crmTicketsHandled`, `crmHandedToDev`, `crmReturnedToSupport`, `crmClosed`, `crmUpdates`, `crmDefectsCreated`; ไทม์ไลน์มี `source = "CRM"` จากการอัปเดต Ticket ผ่าน QA Hub. `PATCH /crm/tickets/{jobNo}` ที่สำเร็จบันทึก `AuditLogs` EntityType `CrmTicketUpdate` (UserId = ผู้อัปเดต) ทุกครั้ง ไม่ว่า Ticket จะผูก Defect หรือไม่

### Performance — เฟส 3: ไฟล์หลักฐาน, เทียบผล, อีเมลเตือน (2026-10-07)

| Method | Path | สิทธิ์ | Request / Response |
|---|---|---|---|
| POST | `/performance/work-logs/{id}/attachments` | ผู้สร้าง/EDIT | `multipart/form-data` field `files` (≤ 5 ไฟล์ต่อบันทึก, ไฟล์ละ ≤ 5 MB, รวม ≤ 20 MB, PNG/JPG/WebP/PDF ตรวจ signature ของไฟล์) → `WorkLogDto` ล่าสุด; ไม่ผ่านกฎ `400` |
| GET | `/performance/work-logs/{id}/attachments/{attachmentId}` | เห็นบันทึกได้ | ไฟล์ (`Content-Disposition: inline`) — Concern ของตัวเอง/ของคนอื่นที่ไม่มีสิทธิ์ตอบ `404` |
| DELETE | `/performance/work-logs/{id}/attachments/{attachmentId}` | ผู้สร้าง/EDIT | `204` — ลบแถวก่อนแล้วค่อยลบไฟล์บนดิสก์ |
| GET | `/performance/users/{userId}/evaluation-comparison` | ตัวเอง/EDIT | `{ groups: string[], rounds: [{ evaluationId, type, round, year, periodStart, periodEnd, overallScore, grade, groups: [{ group, score }] }] }` — เฉพาะรอบ Completed/Acknowledged ล่าสุด 8 รอบ เรียงเก่า → ใหม่, `score` = คะแนนหัวหน้าเฉลี่ยถ่วงน้ำหนักในกลุ่ม (ทศนิยม 2) |

- `WorkLogDto` เพิ่ม `attachments: [{ attachmentId, fileName, size, contentType }]`; ลบบันทึกงานแล้วไฟล์ถูกลบตาม (DB cascade + ลบโฟลเดอร์ `App_Data/PerformanceEvidence/{workLogEntryId}`)
- `EvaluationDetailDto` เพิ่ม `employee: { employeeCode, position, level, hireDate, supervisorName }` สำหรับ Export PDF
- **อีเมลเตือน (`PerformanceReminderWorker`, ทุก 1 ชม., เริ่มหลัง API สตาร์ท 2 นาที, ส่งหลัง 08:00 น. เวลาไทย):** การประเมินสถานะ Open/SelfSubmitted ที่เหลือ ≤ 7 วัน (`DueSoon`) หรือเลยกำหนด (`Overdue`) ของพนักงานที่ Active และไม่ได้ลาออก → ส่งหาหัวหน้าในโปรไฟล์ (ต้อง Active และมีอีเมล) ไม่เช่นนั้นส่งหาผู้ใช้ที่ได้ `PERFORMANCE.EDIT` จาก Role ทุกคน (ไม่ส่งหาตัวผู้ถูกประเมิน); รวมเป็นอีเมลเดียวต่อผู้รับ; กันซ้ำด้วย `EvaluationNotifications` (EvaluationId, Kind, DueDate, Recipient) — เลื่อนกำหนดส่งแล้วเตือนใหม่; SMTP ยังไม่ตั้งค่า/ปิดอยู่ = ข้ามโดยไม่บันทึก (ส่งเมื่อเปิดใช้งาน); ปิดทั้งระบบได้ด้วย config `Performance:RemindersEnabled=false`

### CRM → Defect status sync (2026-10-07)

- เมื่อ CRM Ticket ที่ผูกกับ Defect เปลี่ยนเป็น `Finish` → Defect เป็น `Resolved`, `Close` → `Closed` ทั้งตอน `PATCH /crm/tickets/{jobNo}` และตอน `CrmSyncWorker` เห็นสถานะใหม่ (ปิดใน CRM โดยตรง) — logic อยู่ที่ `CrmDefectStatusSync`
- เปลี่ยนไปข้างหน้าเท่านั้น: ไม่ทับ Defect ที่ `Closed`/`Rejected` แล้ว และไม่ย้อน `Closed` → `Resolved`; บันทึก DefectActivity `StatusChanged` ("Status changed from X to Y (ตาม CRM Ticket #… สถานะ …)") — actor = ผู้อัปเดตใน QA Hub หรือ null เมื่อมาจาก worker
- `CrmTicketMutationDto` เพิ่ม `defectCode` และ `defectStatusChanged` (null = ไม่เปลี่ยน) ให้หน้า CRM แจ้งผลใน toast

### CRM — BlueID ปฏิเสธบัญชี (2026-10-07)

- ถ้าหลัง submit ฟอร์ม BlueID ตอบหน้า JSON error ของ OIDC (เช่น `{"error":"invalid_grant","error_description":"C0003 : NotCloudProductLicence"}`) `CrmTokenService` แจ้งสาเหตุเป็นภาษาไทยทันที (`BlueIdRejectionMessage`) แทนข้อความ "จับ Bearer token ไม่ได้ — หน้าเว็บ CRM อาจเปลี่ยน" และไม่ reload รอ token ต่อ
- `NotCloudProductLicence` = บัญชีไม่มี License ใช้งาน BlueSea (รหัสผ่านถูก) → ตรวจ Merchant ID หรือให้ผู้ดูแล BlueID เปิดสิทธิ์ — แก้ที่บัญชี ไม่ใช่โค้ด
### CRM — แก้ชื่อเรื่อง Ticket (2026-10-07)

- `PATCH /crm/tickets/{jobNo}` รับ form field `subject` (ไม่เกิน 200 ตัวอักษร, ว่าง = คงเดิม) — ส่งไปเป็น `Subject` ของการ Update CRM และเมื่อไม่มีข้อความตอบ ระบบเขียนบันทึก "ชื่อเรื่อง → …" ในประวัติการติดต่อให้; ใช้ `expectedStatus`/`expectedAssignee` กันแก้ทับเหมือนการอัปเดตอื่น; response มี `subject` ใหม่
### CRM — แท็บ "ปิดงานแล้ว" (2026-10-07)

- `GET /crm/tickets?scope=closed` = Ticket ที่ Assign ให้ผู้ใช้และสถานะ Finish/Close; `scope=mine` ไม่รวมงานที่ปิดแล้วอีกต่อไป; `scopeCounts` เพิ่ม `closed`
- Ticket ที่เคยถือแต่ถูกปิดหลังส่งต่อไปแล้วยังไม่แสดงในแท็บใด (กฎเดิม 2026-10-06)
### Performance — ต่อยอดเฟส 3 (2026-10-08)

- `GET /performance/evaluations/team-comparison?year=` (PERFORMANCE.EDIT; ไม่ระบุปี = ปีปัจจุบันตามเวลาไทย) → `{ year, rows: [{ userId, displayName, position, employmentStatus, quarters: [{ round, evaluationId, status, overallScore, grade, isOverdue }] (Q1–Q4), averageScore, previousYearQ4Score }], quarterAverages: [Q1..Q4] }` — คะแนน/เกรดมีเฉพาะรอบที่ปิดแล้ว; ไม่รวมพนักงานที่ลาออก (`Resigned`) หรือบัญชีถูกปิด แม้มีผลในปีนั้น
- อีเมลเตือนพนักงานให้ประเมินตนเอง: บันทึกกันซ้ำใน `EvaluationNotifications` ด้วย Kind `SelfDueSoon` / `SelfOverdue` (RecipientUserId = ผู้ถูกประเมิน)
### CRM — สำเนา Ticket ใน QA Hub (2026-10-08)

- `GET /api/v1/crm/mirror/status` (SYS_ADMIN) → `{ enabled, accountUsername, tickets, events, lastSucceededAt, runs: [{ kind: Incremental|Full, rangeFrom, rangeTo, status: Running|Succeeded|Failed|Skipped, startedAt, completedAt, ticketCount, newTickets, changedTickets, newAnswers, error }] }` (10 รอบล่าสุด)
- `POST /api/v1/crm/mirror/sync?full=false|true` (SYS_ADMIN) — ดึงทันที (90 วัน / 366 วัน); กำลังดึงอยู่ตอบ `409`
- `metrics` ของหน้าผลงานเพิ่ม `crmEditErr`, `crmAnswers`, `crmAvgQaHours`
### Performance — ผู้ประเมินคือหัวหน้าที่ระบุ (2026-10-09)

- **ผู้ประเมินจากแผนกอื่น:** `PUT /performance/evaluations/{id}/manager` ที่ `complete: true` คำนวณ `overallScore = managerScore × (100 − externalWeight)% + ค่าเฉลี่ย weightedScore ของผู้ประเมินแผนกอื่นที่ส่งแล้ว × externalWeight%` (ไม่มีใครส่ง หรือ externalWeight = 0 → เท่ากับ managerScore) แล้วปิดลิงก์ที่ยังไม่ส่ง; `GET /performance/evaluations/{id}` (ผู้เห็นคะแนนหัวหน้าตรง) มี `externalWeight`, `managerScore`, `externalScore` (ระหว่างร่าง = ค่าเฉลี่ยตอนนี้), `externalReviews[]`, `canManageExternal` และ `scores[].externalAverage/externalCount`
- `PUT /performance/evaluations/{id}/manager` และ `POST /performance/evaluations/{id}/reopen` ต้องเป็นหัวหน้าที่ระบุในข้อมูลพนักงานของผู้ถูกประเมิน (นอกจาก PERFORMANCE.EDIT) — ไม่ใช่ / ยังไม่ได้ตั้งหัวหน้า ตอบ `403` พร้อมเหตุผล; `GET /performance/evaluations/{id}` มี `managerRestriction` (null = ประเมินได้)
- `GET /performance/users` เพิ่ม `canEvaluate`; `PUT /performance/users/{userId}/profile` ปฏิเสธ (`400`) หัวหน้าที่ไม่มี PERFORMANCE.EDIT

### Knowledge — คลังความรู้ (2026-10-21)

ทุก endpoint ต้องมี `KNOWLEDGE.VIEW` (หรือ SYS_ADMIN) — สิทธิ์ข้อมูลตามสมาชิก Project (`ProjectUsers`) เท่านั้น; ความรู้กลาง (ไม่มี Project) ทุกคนเห็น; ข้อมูล CRM ต้องผ่าน policy `CrmView`

- `PUT /api/v1/master-settings/ai/knowledge` (`ADMIN.USER`) — `{ embeddingProvider: None|OpenAI|Local, embeddingModel, embeddingBaseUrl, embeddingApiKey, clearEmbeddingApiKey, allowCrmDataToExternalAi }` → `AiConfigurationView` (เพิ่ม `embeddingProvider`, `embeddingModel`, `embeddingBaseUrl`, `hasEmbeddingApiKey`, `embeddingApiKeyHint`, `allowCrmDataToExternalAi`); ต้องบันทึก AI หลักก่อน (`400`)
- `GET /api/v1/knowledge/search?q=&projectId=&sources=Defect,Article&take=20` → `[{ knowledgeDocumentId, sourceType, sourceKey, title, projectId, projectName, sourceUrl, snippet, rank, matchedChunks, indexedAt }]` — `q` อย่างน้อย 2 ตัวอักษร (ตัดที่ 200) ไม่งั้น `400`; `projectId` ที่ไม่ได้เป็นสมาชิก `403`; `take` สูงสุด 30
- `GET /api/v1/knowledge/articles?projectId=&status=&search=` → รายการบทความ (ไม่มี `bodyMarkdown`, มี `excerpt`, `indexState`, `indexedAt`) — ฉบับร่างเห็นเฉพาะ `KNOWLEDGE.EDIT`; `projectId` = บทความของ Project นั้น + ความรู้กลาง
- `GET /api/v1/knowledge/articles/{id}` → บทความเต็ม (`404` เมื่อไม่มีสิทธิ์ Project / เป็นฉบับร่างและไม่มีสิทธิ์แก้ไข)
- `POST /api/v1/knowledge/articles`, `PUT /api/v1/knowledge/articles/{id}` (`KNOWLEDGE.EDIT`) — `{ title (≤300), bodyMarkdown, projectId?, tags? (≤500), status: Draft|Published }`; Project ต้อง active และเป็นสมาชิก (`400`/`403`); บันทึก Audit Log `KnowledgeArticle` และปลุก indexer
- `DELETE /api/v1/knowledge/articles/{id}` (`KNOWLEDGE.EDIT`) — soft delete + Audit Log
- `GET /api/v1/knowledge/status` (SYS_ADMIN) → `{ indexerEnabled, sources: [{ sourceType, indexed, pending, failed, excluded, chunks, lastIndexedAt }], failures: [{ knowledgeDocumentId, sourceType, title, error, attemptedAt }] }`
- `POST /api/v1/knowledge/reindex` (SYS_ADMIN) → `{ documents }` — สร้าง chunk ใหม่ทุกเอกสาร + Audit Log `KnowledgeIndex`

### Knowledge — ผู้ช่วย AI (2026-10-21)

ต้องมี `KNOWLEDGE.VIEW`; บทสนทนาเป็นของผู้ใช้แต่ละคน (ของคนอื่น = `404`)

- `GET /api/v1/knowledge/chat/sessions` → `[{ chatSessionId, title, projectId, createdAt, updatedAt }]` (ล่าสุดก่อน, สูงสุด 100)
- `POST /api/v1/knowledge/chat/sessions` `{ projectId? }` → session ใหม่ ("บทสนทนาใหม่" — เปลี่ยนเป็นคำถามแรกเมื่อตอบเสร็จ)
- `GET /api/v1/knowledge/chat/sessions/{id}` → `{ session, messages: [{ chatMessageId, role: user|assistant, content, citations: [{ number, knowledgeDocumentId, sourceType, sourceKey, title, projectName, sourceUrl, cited }], model, feedback, createdAt }] }`
- `DELETE /api/v1/knowledge/chat/sessions/{id}` — soft delete
- `POST /api/v1/knowledge/chat/sessions/{id}/messages?stream=true` `{ question (1–2000), projectId?, sources?: ["Defect","Article"] }` — rate limit 20/นาที/ผู้ใช้ (`429`)
  - ก่อนเริ่มตอบ: `400` คำถามว่าง/ยาวเกิน, `403` ไม่มีสิทธิ์ Project, `404` ไม่พบบทสนทนา, `503` AI ยังไม่ตั้งค่า/ปิดใช้งาน
  - `stream=true` → `text/event-stream`: `event: sources` `{ citations, crmExcluded }` → `event: delta` `{ text }` (หลายครั้ง) → `event: done` `{ userMessageId, assistantMessageId, citations (มี cited), title, model }` หรือ `event: error` `{ message }`; บรรทัด `: ping` ทุก 15 วินาที
  - `stream=false` → `{ answer, sources, done }` หรือ `502` เมื่อ Provider ตอบไม่สำเร็จ
  - ไม่พบความรู้ที่เกี่ยวข้อง = ตอบข้อความ "ไม่พบข้อมูลในคลังความรู้…" โดยไม่เรียก LLM (`model = null`)
- `POST /api/v1/knowledge/chat/messages/{id}/feedback` `{ feedback: 1 | -1 | null, note? }` — เฉพาะคำตอบของผู้ช่วยในบทสนทนาของตัวเอง

### Knowledge — เชื่อมต่อ Claude ผ่าน MCP (2026-10-21)

- `GET /api/v1/knowledge/mcp/info` (`KNOWLEDGE.VIEW`) → `{ enabled, canCreateToken, defaultLifetimeDays: 90, maxLifetimeDays: 365, maxActiveTokens: 10 }`
- `GET /api/v1/knowledge/mcp/tokens` (`KNOWLEDGE.MCP`) → Token ของตัวเอง `[{ personalAccessTokenId, name, tokenPrefix, scope, createdAt, expiresAt, lastUsedAt, lastUsedIp, revokedAt, isActive }]` (ไม่มี token จริง)
- `POST /api/v1/knowledge/mcp/tokens` (`KNOWLEDGE.MCP`) `{ name (≤100), lifetimeDays (1–365) }` → `{ token, item }` — **token แสดงครั้งเดียว**; สวิตช์ปิด = `403`, ใช้งานอยู่ครบ 10 ใบ/ชื่อหรืออายุไม่ถูกต้อง = `400`; Audit Log `PersonalAccessToken`
- `DELETE /api/v1/knowledge/mcp/tokens/{id}` (เจ้าของ) · `GET /api/v1/knowledge/mcp/tokens/all` + `DELETE /api/v1/knowledge/mcp/tokens/all/{id}` (SYS_ADMIN)
- **MCP endpoint `POST /mcp`** (Streamable HTTP, stateless; ไม่ใช่ `/api/v1`) — header `Authorization: Bearer qak_…` เท่านั้น (JWT ใช้ไม่ได้), ต้องมี `KNOWLEDGE.VIEW` + `KNOWLEDGE.MCP` (หรือ SYS_ADMIN); ไม่มี/ผิด/หมดอายุ/ยกเลิก = `401` (`WWW-Authenticate: Bearer realm="ProMaxx2 QA Hub"` ไม่มี OAuth metadata), ไม่มีสิทธิ์ = `403`, สวิตช์ปิด = `403`; rate limit 60/นาที/token
  - tools (อ่านอย่างเดียวทั้งหมด): `list_projects()`, `search_knowledge(query, projectCode?, source? = defect|article, limit ≤ 10)`, `get_defect(defect = รหัสหรือ GUID)`, `get_article(articleId)`, `find_similar_defects(description, projectCode?, limit ≤ 10)` — ผลเป็นข้อความภาษาไทย (mask แล้ว, ≤ ~8,000 ตัวอักษร, มีลิงก์เว็บ); ไม่พบ/ไม่มีสิทธิ์ = tool result `isError`; ไม่คืนข้อมูล CRM; Audit Log `KnowledgeMcp`

### OAuth 2.1 สำหรับ MCP (2026-10-22)

ไม่อยู่ใต้ `/api/v1` (ยกเว้นการตัดสินใจในหน้าเว็บ) — base = `Mcp:PublicBaseUrl` (`https://api-promaxx2.qahub.store`); rate limit `oauth` 120/นาที/IP

- `GET /.well-known/oauth-protected-resource[/mcp]` → `{ resource: "<base>/mcp", authorization_servers: ["<base>"], scopes_supported: ["knowledge:read"], bearer_methods_supported: ["header"] }`
- `GET /.well-known/oauth-authorization-server` → `{ issuer, authorization_endpoint: "<base>/oauth/authorize", token_endpoint: "<base>/oauth/token", registration_endpoint: "<base>/oauth/register", response_types_supported: ["code"], grant_types_supported: ["authorization_code","refresh_token"], code_challenge_methods_supported: ["S256"], token_endpoint_auth_methods_supported: ["none"], scopes_supported, authorization_response_iss_parameter_supported: true }`
- `POST /oauth/register` (JSON, RFC 7591) `{ client_name?, redirect_uris[1-10], grant_types?, response_types?, token_endpoint_auth_method? }` → `201 { client_id: "qac_…", client_id_issued_at, client_name, redirect_uris, grant_types, response_types, token_endpoint_auth_method: "none", scope }`; redirect ไม่อยู่ใน allowlist = `400 { error: "invalid_redirect_uri" }`
- `GET /oauth/authorize?response_type=code&client_id&redirect_uri&code_challenge&code_challenge_method=S256&state&scope&resource` → `302` ไป `<web>/?oauth=req_…`; ข้อผิดพลาดที่ client เชื่อถือได้ → `302 redirect_uri?error=…&state&iss`; client/redirect_uri ไม่ถูกต้อง → `400` หน้า HTML (ไม่ redirect)
- `GET /api/v1/oauth/requests/{id}` (JWT, `KNOWLEDGE.VIEW`) → `{ requestId, clientName, redirectHost, isLoopback, scope, expiresAt, mcpEnabled, canApprove }` / `404` หมดอายุ-ใช้แล้ว
- `POST /api/v1/oauth/requests/{id}/approve` | `/deny` (JWT) → `{ redirectUrl }` (approve ต้องเปิดสวิตช์ + มี `KNOWLEDGE.MCP`, ไม่งั้น `403`)
- `POST /oauth/token` (`application/x-www-form-urlencoded`, `Cache-Control: no-store`)
  - `grant_type=authorization_code&client_id&code&redirect_uri&code_verifier[&resource]`
  - `grant_type=refresh_token&client_id&refresh_token`
  - ใช้ได้ครั้งเดียวจริง (2026-10-28): แลก code/refresh พร้อมกันด้วยค่าเดียวกัน สำเร็จได้คำขอเดียว ที่เหลือ `invalid_grant`; code ที่ verifier หรือ `resource` ไม่ตรงถูกเผาทิ้งทันที; **refresh token เก่า (ถูก rotate แล้ว) ถูกนำมาใช้อีก = ยกเลิกการเชื่อมต่อนั้นทั้งหมด** (OAuth 2.1 reuse detection) ผู้ใช้ต้องเชื่อมต่อใหม่จาก Claude
  - → `{ access_token: "qao_…", token_type: "Bearer", expires_in: 3600, refresh_token: "qrt_…", scope: "knowledge:read" }`; ข้อผิดพลาด `400 { error, error_description }` (`invalid_grant`, `invalid_request`, `invalid_target`, `unsupported_grant_type`) หรือ `401 invalid_client`
- `/mcp` ไม่มี/ผิด token → `401` + `WWW-Authenticate: Bearer realm="ProMaxx2 QA Hub", resource_metadata="<base>/.well-known/oauth-protected-resource/mcp", scope="knowledge:read"`; รับทั้ง Personal Access Token (`qak_`) และ OAuth access token (`qao_`, ตรวจ audience)

### Knowledge — เอกสาร (Phase 3, 2026-10-22)

- `GET /api/v1/knowledge/files?projectId=&search=` (`KNOWLEDGE.VIEW`) → `[{ knowledgeFileId, fileName, extension, sizeBytes, projectId, projectName, description, origin: Upload|Import, importPath, extractedChars, extractError, uploadedAt, uploadedByName, updatedAt, indexState, indexedAt }]` (เฉพาะความรู้กลาง + Project ที่เป็นสมาชิก)
- `POST /api/v1/knowledge/files` (`KNOWLEDGE.EDIT`, multipart: `files` 1–10, `projectId?`, `description?`; ไฟล์ละ ≤ 20 MB รวม ≤ 50 MB) → `[{ fileName, success, message, item? }]` — ผลรายไฟล์: ไม่รองรับ/เนื้อหาไม่ตรงชนิด/ซ้ำ = `success: false` ไม่บันทึก; สกัดข้อความไม่ได้ = บันทึกไฟล์แต่ `success: false` + เหตุผล
- `GET /api/v1/knowledge/files/{id}/download` → ไฟล์ต้นฉบับ (`404` เมื่อไม่มีสิทธิ์ Project)
- `DELETE /api/v1/knowledge/files/{id}` (`KNOWLEDGE.EDIT`) — soft delete + ลบไฟล์บนดิสก์
- `POST /api/v1/knowledge/files/import-system` (SYS_ADMIN) → `{ added, updated, unchanged, failed, messages[] }` — นำเข้า `.md`/`.txt` จาก `Knowledge:SystemDocumentFolders` (ค่าเริ่มต้น `Document/ProMaxx2_MD`)
- MCP: `search_knowledge` รับ `source: "document"`; เครื่องมือใหม่ `get_document(documentId, part = 1)` → ข้อความเอกสาร (mask) ทีละ ~7,600 ตัวอักษร + บอก `part` ถัดไป

### Knowledge — CRM Ticket (Phase 4, 2026-10-22)

- `GET /api/v1/knowledge/crm/{jobNo}` (`KNOWLEDGE.VIEW` + `CRM.VIEW`) → `{ jobNo, subject, status, jobType, serviceType, product, contactDate, description, answers: [{ answerNo, answerType, answeredAt, text }], fetchedAt, indexState, indexedAt }` — ข้อความ **mask แล้ว** จากคลังความรู้ (ไม่เรียก CRM สด); `404` เมื่อยังไม่ได้ดึง
- `GET /api/v1/knowledge/status` เพิ่ม `crm: { enabled, allowExternalAi, candidates, fetched, pending, failed, lastFetchedAt }`
- ค้นหา/แชท: `sources=CrmTicket` (ผลลัพธ์ CRM คืนเฉพาะผู้มี `CRM.VIEW`; แชทที่ใช้ Provider ภายนอกตัด CRM ออกเมื่อไม่ได้อนุญาต `allowCrmDataToExternalAi`)
- MCP: `search_knowledge(source: "crm")` และเครื่องมือใหม่ `get_crm_ticket(jobNo)` — ใช้ได้เฉพาะผู้มี `CRM.VIEW` **และ** ผู้ดูแลเปิด "อนุญาตส่งข้อมูล CRM ให้ AI ภายนอก" (Claude เป็น AI ภายนอกเสมอ) ไม่งั้นได้ข้อความแจ้งเหตุผล
- Config: `Knowledge:CrmFetchEnabled` (ค่าเริ่มต้น `true`), `Knowledge:CrmFetchPerRun` (ค่าเริ่มต้น 50 ใบ/รอบ, 1–500) — ใช้บัญชีกลาง `CrmMirror:AccountUsername`

### Knowledge — ค้นตามความหมาย (Phase 5, 2026-10-22)

- `POST /api/v1/master-settings/ai/knowledge/test-embedding` (`AdminUser`) → `{ provider, model, dimensions, elapsedMs }` — ทดสอบ Embedding ด้วยค่าที่บันทึกไว้; `502` + `detail` เมื่อ provider ใช้งานไม่ได้
- `PUT /api/v1/master-settings/ai/knowledge` — บันทึกแล้วปลุก `KnowledgeIndexWorker` ให้เริ่มสร้างเวกเตอร์ทันที
- `GET /api/v1/knowledge/status` เพิ่ม `embedding: { enabled, provider, model, isExternal, eligible, embedded, error, lastErrorAt, lastSuccessAt }`
- `GET /knowledge/search`, แชท และ MCP ใช้ผลรวม Full-Text + ความหมาย (RRF) อัตโนมัติเมื่อตั้งค่า Embedding — `rank` ในผลค้นหาเป็นคะแนนเพื่อเรียงลำดับเท่านั้น (ไม่ใช่ RANK ของ Full-Text)
- Config: `Knowledge:EmbeddingBatchSize` (32), `Knowledge:VectorMinScore` (0.35)

### Defect — ผลการปิด + Defect ที่อาจซ้ำ (คลังความรู้ Phase 6, 2026-10-22)

- `DefectDto` เพิ่ม `resolutionType`, `rootCauseCategory`, `rootCauseNote`, `resolutionNote`, `duplicateOfDefectId`, `duplicateOfDefectCode` (รายการ Defect ไม่เติม `duplicateOfDefectCode`)
- `PATCH /api/v1/defects/{id}/status` → `{ status, resolution? }`, `PUT /api/v1/defects/{id}` และ `POST /api/v1/defects` รับ `resolution?` = `{ resolutionType: Fixed|Duplicate|CannotReproduce|ByDesign|WontFix|Deferred, rootCauseCategory?, rootCauseNote?, resolutionNote?, duplicateOfDefectId? }` — `400` (ข้อความไทย) เมื่อ Fixed ไม่มีหมวด, หมวดไม่อยู่ใน Master Option `DefectRootCause` ที่เปิดใช้, Duplicate ไม่มี/ข้าม Project; API **ไม่บังคับ** ส่ง resolution (หน้า Defect บังคับเอง)
- `PUT /api/v1/defects/{id}/resolution` (`DefectEdit`) — แก้ผลการปิดโดยไม่เปลี่ยนสถานะ, บันทึก Activity `Resolution`
- `GET /api/v1/defects/similar?projectId=&text=&excludeId=` (`DefectView`) → `[{ defectId, defectCode, title, status, severity, resolutionType, snippet }]` ≤ 5 รายการ (ค้นคลังความรู้เฉพาะ Defect ใน Project นั้น)
- Master Option หมวดใหม่ `DefectRootCause`
- คลังความรู้: แหล่ง `TestCase` (`sources=TestCase`); MCP: `search_knowledge(source: "testcase")` + เครื่องมือ `get_test_case(testCase)` (รหัสหรือ id)

### Setting Center — ทดสอบการเชื่อมต่อ AI (2026-10-22)

- `POST /api/v1/master-settings/ai/test` (`AdminUser`) body `{ provider, model, baseUrl?, apiKey? }` → `{ provider, model, elapsedMs, reply }` — ทดสอบด้วย**ค่าในฟอร์ม (ยังไม่ต้องบันทึก)**: เว้น `apiKey` = ใช้คีย์ที่บันทึกไว้เมื่อเป็น Provider เดียวกัน; ส่งคำถามสั้น 1 ข้อผ่าน `IAiChatClient` ตัวเดียวกับผู้ช่วย AI (`reply` ตัดที่ 200 ตัวอักษร); `400` เมื่อขาด Model/คีย์/Base URL ไม่ถูกต้อง, `502` เมื่อ Provider ตอบ error, `504` เมื่อไม่ตอบใน 60 วินาที
- `PUT /api/v1/master-settings/ai`: Provider `opencode` บันทึกได้โดยไม่มี API key แล้ว (เดิมหน้าเว็บอนุญาตแต่ API ตอบ 400)

### 2026-10-22 — รอบดึงข้อมูล CRM ตั้งได้ (migration `AddCrmMirrorInterval`)

- `GET /api/v1/master-settings/crm-sync` (`AdminUser`) → `{ pollIntervalMinutes, mirrorIntervalMinutes, updatedAt }` — `mirrorIntervalMinutes` = รอบของ `CrmMirrorWorker` (ค่าเริ่มต้น 15)
- `PUT /api/v1/master-settings/crm-sync` (`AdminUser`) body `{ pollIntervalMinutes?, mirrorIntervalMinutes? }` — ส่งเฉพาะค่าที่ต้องการเปลี่ยน (`null`/ไม่ส่ง = คงค่าเดิม); `pollIntervalMinutes` 1–60, `mirrorIntervalMinutes` 5–1440 นอกช่วง = 400
- `GET /api/v1/knowledge/status` → `crm` เพิ่ม `mirrorIntervalMinutes`; `GET /api/v1/crm/mirror/status` เพิ่ม `intervalMinutes`

### 2026-10-22 — เรียงรายการ Test Cycle ฝั่ง server

- `GET /api/v1/test-cycles` เพิ่ม query `sortBy=<field>_<asc|desc>` — field: `code`, `name`, `progress` (ไม่ใช่ NotRun / ทั้งหมด), `cases`, `status` (ลำดับงาน Draft → InProgress → Completed → Closed → Cancelled), `startDate`, `createdAt`; ไม่ส่ง/ค่าไม่รู้จัก = `createdAt_desc` (ลำดับเดิม) และค่าเท่ากันเรียงต่อด้วย `CycleCode` desc — เรียงก่อนแบ่งหน้า; ผู้เรียกเดิม (Execution Workspace ฯลฯ) ไม่ต้องแก้

### 2026-10-22 — เรียงรายการ Defect ฝั่ง server

- `GET /api/v1/defects` เพิ่ม query `sortBy=<field>_<asc|desc>` — field: `code`, `title`, `severity` (Critical → High → Medium → Low), `status` (Open → In Progress → Resolved → Closed → Rejected), `createdAt`; ไม่ส่ง/ค่าไม่รู้จัก = `createdAt_desc` (ลำดับเดิม), ค่าเท่ากันเรียงต่อด้วย `CreatedAt` desc (`DefectListSort`)
