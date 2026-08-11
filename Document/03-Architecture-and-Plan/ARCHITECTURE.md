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

## 20. Architecture Decisions
1. Modular Monolith ก่อน Microservices
2. SQL Server เป็น Source of Truth
3. REST API เป็น UI/Backend Contract
4. Evidence แยก File Storage
5. Execution และ Approval History เป็น Immutable
6. Release Readiness คำนวณจากข้อมูลจริง
7. Permission ตรวจ Backend เสมอ
