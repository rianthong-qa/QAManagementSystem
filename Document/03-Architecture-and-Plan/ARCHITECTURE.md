# ProMaxx2 QA Management System — ARCHITECTURE

> Target: ASP.NET Core .NET 10 + SQL Server + Web Frontend  
> Architecture: Modular Monolith + Clean Architecture principles

## 1. Architecture Overview

```text
Web Frontend
    ↓ HTTPS / JSON
ASP.NET Core .NET 10 REST API
    ↓
Application Layer — Use Cases / Commands / Queries / DTO / Validation
    ↓
Domain Layer — Entities / Business Rules / Value Objects
    ↓
Infrastructure — EF Core / SQL Server / File Storage / Logging / Notification
```

## 2. เหตุผลที่ใช้ Modular Monolith
- Deploy และ Debug ง่ายกว่า Microservices
- Transaction ข้าม Requirement/Test/Defect ทำได้ตรงไปตรงมา
- Infrastructure ไม่ซับซ้อนเกินความจำเป็น
- เหมาะกับระบบภายในและทีมขนาดเล็กถึงกลาง
- แยก Logical Module ชัดเจนเพื่อรองรับการแยก Service ภายหลัง

## 3. Logical Modules
- Identity — User, Role, Permission
- Project — Project, Product Module
- Release — Release, Build, Release Candidate
- Requirements — Requirement, Revision, RTM
- TestManagement — Scenario, Test Case, Step, Data, Environment, Suite
- TestExecution — Cycle, Execution, Step Result, Evidence
- Defects — Defect, Resolution, Retest, History
- Regression — Impact Analysis, Regression Suite/Cycle
- Reporting — Dashboard, Daily/Weekly, Test Summary
- Governance — Risk Acceptance, Release Gate, Sign-off, Audit
- Notification — Assignment, Defect, Retest, Approval

## 4. Dependency Rule
`API/UI → Application → Domain`  
Infrastructure implement interfaces ที่ Application/Domain กำหนด

Domain ห้ามอ้าง SQL Server, HTTP, Controller หรือ UI โดยตรง

## 5. Backend
- ASP.NET Core Web API .NET 10
- Entity Framework Core
- SQL Server
- OpenAPI/Swagger
- Dependency Injection
- Structured Logging
- ProblemDetails
- Validation Pipeline
- API Version `/api/v1`

## 6. Frontend
ใช้ React/TypeScript หรือ Blazor ตามมาตรฐานทีม โดยรับผิดชอบ Routing, Data Table, Filter, Form, Dashboard และ Execution Workspace

Business Rule สำคัญต้อง Validate ที่ Backend เสมอ

## 7. Authentication / Authorization
`Login/SSO → Authentication → User/Roles/Permissions → API Authorization Policy`

ตัวอย่าง Permission:
- REQUIREMENT.EDIT
- TESTCASE.EDIT
- EXECUTION.RUN
- EXECUTION.ASSIGN
- DEFECT.RESOLVE
- RISK.APPROVE
- RELEASE.SIGNOFF

## 8. Data Architecture
SQL Server เป็น System of Record:
- Guid/UUID สำหรับ Entity หลัก
- Business Code แยกจาก PK
- Soft Delete
- Revision History
- Immutable Execution History
- Immutable Sign-off History
- UTC DateTime
- FK + Index

## 9. Evidence Storage
ไม่เก็บ Screenshot/Video ขนาดใหญ่ใน SQL Server

`Attachment Metadata → SQL Server`  
`Actual File → File/Object Storage`

เก็บ Filename, MIME, Size, Hash, Storage Path, Entity Type/ID, Uploader, Timestamp

## 10. Audit
Write สำคัญต้องทำใน Transaction เดียวกับ Audit เมื่อเหมาะสม:
`Authorize → Load → Validate Rule → Update Data + Audit → Commit`

Audit อย่างน้อย Requirement Change, Test Case Revision, Execution, Defect Status/Severity, Risk Approval, Sign-off และ Permission

## 11. Execution History
ห้ามเขียนทับผลเดิม:

```text
TestCycleCase
 ├─ Execution #1 Fail
 ├─ Execution #2 Fail
 └─ Execution #3 Pass
```

CurrentStatus เป็นสถานะล่าสุด ส่วน TestExecutions เป็น Historical Source

## 12. Release Gate
คำนวณจาก Requirement Coverage, Smoke, Critical Regression, Open P0, P1 Blocker, Update/Migration และ Approved Risk

`Quality Metrics → Release Gate Engine → GO / CONDITIONAL GO / NO-GO → Sign-off`

UI ห้ามกำหนด Readiness โดยตรง

## 13. Transaction Boundary
ควรใช้ Transaction สำหรับ:
- Execution + Step Results
- Create Defect จาก Failed Execution
- Defect Transition + History
- Retest + Defect Status
- Risk Approval + Audit
- Sign-off + Audit

## 14. Background Jobs
รองรับภายหลัง: Large Export, Weekly Report, Notification, Cleanup และ Metrics Snapshot

## 15. Observability
Log TraceId, UserId, Endpoint, Duration, StatusCode และ Entity ID  
ห้าม Log Password, Token, Secret หรือ Sensitive Test Data

## 16. Security
HTTPS, RBAC, Server Validation, ORM/Parameterized Query, File MIME/Size Validation, Secret Management, Auth Rate Limit และ Audit privileged operations

## 17. Deployment
แยก Environment: DEV / QA-UAT / PROD  
Config และ Secret แยก Environment

## 18. Backup
SQL Backup + Evidence Backup + Restore Drill + Retention Policy

## 19. Integration Points
เตรียม Interface สำหรับ CI/CD, Source Control, Automated Test Runner, CRM/Support และ Notification

### 19.1 Automated Test Runner

- QA Hub เป็นผู้ส่งออก Test Plan schema 1.1 ซึ่งระบุ `projectId`, `testCaseId`, `TestCaseCode` และ `targetApp`
- Runner แยกการรัน `pos` (`PromaxxsPos.exe`) และ `app` (`Promaxxs.App.exe`) คนละ run
- หลังรัน Runner login ด้วยบัญชีจาก environment variables และส่ง Passed/Failed/Skipped, duration, error และ evidence ผ่าน `POST /api/v1/automation/runs?projectId=...`
- QA Hub เก็บข้อมูลใน `AutomationRuns`/`AutomationRunCases`; เมื่อระบุ `testCycleId` ระบบสร้าง immutable `TestExecution` ต่อผลแต่ละ case และเก็บ `AutomationRunCase.TestExecutionId` สำหรับ trace กลับ โดยมี FK แบบ Restrict ป้องกันการลบประวัติข้ามกัน
- Runner รองรับ `--cycle <guid>` ทั้งตอน `export` และ `run`; QA Hub ตรวจว่า Cycle อยู่ใน Project/Release/Build เดียวกัน, case อยู่ใน Cycle และ Cycle ยังไม่ Completed/Closed/Cancelled ก่อนบันทึกทั้งหมดใน transaction เดียว
- `--no-publish` ใช้เฉพาะการรัน offline หรือวิเคราะห์ปัญหา โดยยังสร้าง `run-results.json` ในเครื่อง

### 19.2 AutomationId Scanner

- Runner `scan` ใช้ UIA3 อ่าน control tree จาก AUT ที่กำลังทำงาน ไม่อ่านหรือ decompile assembly
- Scanner เปิดโปรแกรมและ login จาก environment variables แล้วทำ navigation เฉพาะ `AutomationId` ที่ประกาศใน manifest เพื่อหลีกเลี่ยงการสุ่มคลิก action ที่แก้ไขข้อมูล
- แต่ละ screen สร้าง snapshot ของ AutomationId/Name/Class/ControlType พร้อมรายงาน actionable control ที่ไม่มี ID และ ID ซ้ำ
- เมื่อส่ง `--baseline` ระบบ diff ID ที่เพิ่ม หาย หรือเปลี่ยนชนิด control; ID ที่หายทำให้ command จบด้วย exit code 2 เพื่อใช้เป็น quality gate ใน CI ได้
- ผลลัพธ์มี JSON สำหรับ machine processing และ Markdown runtime registry สำหรับ review/แนบ release evidence

### 19.3 Automation Evidence

- Runner publish metadata ก่อน แล้วใช้ `AutomationRunId`/`AutomationRunCaseId` ที่ QA Hub ตอบกลับเพื่ออัปโหลด screenshot ของ case ที่ fail
- Evidence จำกัด 10 MB และ allowlist เฉพาะ PNG/JPG/WEBP/TXT/LOG/JSON; ชื่อไฟล์ฝั่ง client ไม่ถูกใช้เป็น storage key
- API เก็บไฟล์ใต้ `App_Data/AutomationEvidence/{runId}/{caseId}.{ext}` ซึ่งอยู่นอก web root และบันทึกเฉพาะ relative path ในฐานข้อมูล
- การอ่านไฟล์ต้องผ่าน JWT, `EXECUTION.RUN` และ Project access เหมือน Run History; API ตรวจ canonical path ก่อนเปิดไฟล์เพื่อป้องกัน path traversal
- หน้า Automation ดาวน์โหลด evidence ผ่าน authenticated fetch และเปิดด้วย object URL ชั่วคราว ไม่เผย physical path ของ server

### 19.4 AutomationId Build Quality Gate

- Approved baseline เก็บแบบ versioned ที่ `Automation/Promaxx2.Automation/baselines/{build}/{pos|app}.json`; การเปลี่ยน baseline ต้องเป็นการตัดสินใจของ QA/Dev ไม่เขียนทับอัตโนมัติระหว่าง scan
- Policy กลาง `quality-gate-policy.json` ใช้แนวคิด no-new-regression: technical debt ที่มีใน baseline ไม่ทำให้ build เดิม fail แต่ new missing ID, new duplicate ID, removed ID และ changed control type/class ถูกบล็อกตาม threshold
- รายการ breaking change ที่ตั้งใจทำต้องระบุใน `allowedRemoved`/`allowedChanged` อย่างเจาะจงและมี release note รองรับ ห้ามเพิ่ม threshold กว้างเพื่อข้าม finding
- คำสั่ง `gate` สร้าง JSON สำหรับระบบและ JUnit XML สำหรับ CI; exit code 0=ผ่าน, 3=ไม่ผ่าน
- `run-quality-gate.ps1` รัน scan และ gate ทั้ง POS/App โดยรับ `-Build` และ `-BaselineBuild`; runner host ต้องเป็น Windows interactive session เพราะ UIA ไม่ทำงานใน headless service session

### 19.5 QA Hub Build/Release Gate Integration

- Runner ส่งผลจาก JSON Gate เข้า `POST /api/v1/automation/quality-gates` โดยระบุ `ProjectId`, `ReleaseId`, `BuildId`, target, baseline/current build, finding counts, message, runner และเวลาเสร็จสิ้น
- QA Hub เก็บประวัติใน `AutomationQualityGateRuns`; สถานะระดับ Build ใช้ผลล่าสุดแยก `pos` และ `app` โดยต้องมีครบและผ่านทั้งคู่จึงเป็น Passed ส่วนข้อมูลไม่ครบเป็น Pending
- `GET /api/v1/automation/quality-gates/builds/{buildId}` คืนสถานะรวมและ `isReleaseBlocked`; หน้า Automation แสดงรายละเอียด Gate ตาม Build context แบบ responsive
- Backend บังคับ Gate จริงเมื่อเปลี่ยน Build เป็น `Passed` หรือทำเครื่องหมาย Release Candidate ไม่พึ่งการซ่อนปุ่มใน UI

### 19.6 Automation Trigger and Windows Runner Queue

- QA Hub เก็บคำสั่งรันใน `AutomationQueueJobs` โดยผูก Project/Release/Build, optional Test Cycle, target `pos|app`, ผู้สั่งและหมายเหตุ
- สถานะเป็น state machine `Queued → Claimed → Running → Completed|Failed`; `Queued|Claimed → Cancelled` สำหรับการยกเลิกก่อนเริ่มรัน
- Runner claim งานเก่าสุดของ Project/target ภายใน serializable transaction และได้รับ lease token เฉพาะครั้งนั้น; token ไม่ถูกส่งใน list UI และต้องตรงกันทุกครั้งที่ Runner เปลี่ยนสถานะ
- คำสั่ง `worker --project <guid>` login QA Hub, poll queue, export เฉพาะ `AutomationCandidate=true`, `Ready` และ `AutomationTarget` ตรงกับงาน จากนั้นสร้าง plan, รัน AUT, publish Automation Run/evidence และปิดสถานะ Queue
- หน้า Automation ใช้ Build context แสดง Queue และ modal สั่งรัน; action ต้องมี `EXECUTION.RUN`, Desktop แสดง summary row และ Mobile stack เป็น card โดยไม่มี page-level horizontal scroll

## 20. Architecture Decisions
1. Modular Monolith ก่อน Microservices
2. SQL Server เป็น Source of Truth
3. REST API เป็น UI/Backend Contract
4. Evidence แยก File Storage
5. Execution และ Approval History เป็น Immutable
6. Release Readiness คำนวณจากข้อมูลจริง
7. Permission ตรวจ Backend เสมอ

## Windows Runner Agent Management (2026-08-22)

- `AutomationRunnerAgents` เป็น registry แยก Project/RunnerName เก็บ machine, version, capabilities, state, current job และ heartbeat ล่าสุด
- Agent heartbeat upsert registry และต่ออายุ queue lease; Online ใช้กรอบเวลา 60 วินาที
- Queue lease TTL 2 นาที งาน stale recover ก่อน list/claim/heartbeat และ retry สูงสุด 3 claims
- Runner host ใช้ Scheduled Task แบบ AtLogOn/Interactive เพื่อให้ UI Automation เห็น desktop session

## Automation Scheduling & Retry Policy (2026-08-22)

- `AutomationSchedules` เก็บ recurring definition และ `NextRunAt`; due schedule ถูก materialize เป็น `AutomationQueueJob` เมื่อมี Runner heartbeat/claim/list
- Queue job เก็บ Pack, MaxAttempts, SourceScheduleId และ ErrorType เพื่อ audit การ retry
- Retryable: Infrastructure/Timeout/ApplicationStart; terminal: Assertion/Configuration หรือ attempts ครบ
- Notifications เป็น projection จาก terminal failed และ retry-queued jobs จึงไม่ต้องทำ notification table ซ้ำ
