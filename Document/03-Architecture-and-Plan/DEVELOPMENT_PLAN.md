# ProMaxx2 QA Management System — DEVELOPMENT_PLAN

## 1. Strategy
พัฒนาแบบ Vertical Slice: `Database → Domain → Application → API → UI → Test`

## 2. Phase 0 — Foundation
Solution/Repository, .NET 10, Frontend, SQL Server, Migration, Logging, Error Handling, Authentication, User/Role/Permission, Swagger และ Environment Config

**Exit:** Login/Permission/Health Check/Migration/TraceId ทำงาน

## 3. Phase 1 — Project / Release / Build
Project, Module, Release, Build และ Candidate Build

**Exit:** QA สร้าง Release และ Build สำหรับทดสอบได้

## 4. Phase 2 — Requirement / RTM
Requirement CRUD, Revision, In/Out Scope, RTM และ Coverage

**Exit:** Link Requirement-Test Case, หา Not Covered และดู Revision ได้

## 5. Phase 3 — Test Design
Scenario, Test Case, Step, Test Data, Environment, Suite และ Revision

**Exit:** QA สร้าง Case และจัด Smoke/Regression Suite ได้

## 6. Phase 4 — Test Cycle / Execution
Cycle, Populate Suite, Assignment, Execution Workspace, Step Result, Evidence และ History

**Critical:** Build/Environment required, Retest ไม่เขียนทับ, Closed Cycle read-only

**Exit:** QA Execute Release โดยไม่ใช้ Excel เป็นตัวหลัก

## 7. Phase 5 — Defect / Retest
Defect, Trace Links, Workflow, Developer Resolution, Fix Build, Retest, Reopen, Evidence, History

**Exit:** Trace `Requirement → Test → Fail → Defect → Fix Build → Retest`

## 8. Phase 6 — Regression
Impact Analysis, Regression Suite, Suggested Cases, Regression Cycle/Dashboard

## 9. Phase 7 — Dashboard / Reporting
Coverage, Progress, Pass Rate, Defect Trend, Module Health, Workload, Daily/Weekly

## 10. Phase 8 — Governance
Test Summary, Release Gate, Risk Acceptance, QA Recommendation, Approvals, Final Sign-off, Audit

**Exit:** ระบบแนะนำ GO/CONDITIONAL GO/NO-GO จากข้อมูลจริง

## 11. Phase 9 — Productivity
Import Requirement/Test Case, Export RTM/Defect/Summary, Notification Center

## 12. Phase 10 — Integration
CI/CD Build Intake, Automated Test Result, Source Control, CRM/Support, Email/Teams/Slack

## 13. Milestones

| Milestone | Scope |
|---|---|
| M0 | Foundation |
| M1 | Project/Release/Build |
| M2 | Requirement/RTM/Test Design |
| M3 | Cycle/Execution |
| M4 | Defect/Fix/Retest |
| M5 | Regression |
| M6 | Dashboard/Reports |
| M7 | Risk/Gate/Sign-off |
| M8 | Import/Export/Notification |

## 14. Priority
**P0:** Auth, User/Role, Project/Module, Release/Build, Requirement, RTM, Test Case, Suite, Cycle, Execution, Defect, Basic Dashboard

**P1:** Test Data, Environment, Evidence, Retest, Regression, Summary, Risk, Sign-off, Audit

**P2:** Daily/Weekly, Notification, Import/Export, Advanced Dashboard

**P3:** CI/CD, Automation, External Integration

## 15. Technical Checklist ทุก Feature
Migration, FK/Index, DTO/API, Validation, Authorization, Audit, UI Loading/Error/Empty State, Unit Test, Integration Test, QA Test และ Documentation

## 16. QA Strategy
Developer: Unit + Integration + API Contract  
QA: Functional + Negative + Permission + Workflow + DB Integrity + Regression

Critical Areas: Execution History, Defect Workflow, Revision, Release Gate, Permission, Sign-off/Audit

## 17. Pilot
เริ่มกับ ProMaxx2 1 Release:
1. Import Requirement/Test Case
2. Create Release/Build
3. Smoke
4. Critical Execution
5. Defect จริง
6. Retest
7. Regression
8. Test Summary
9. Sign-off

## 18. Rollout
Stage 1 QA Lead + QA กลุ่มเล็ก → Stage 2 QA ทั้งทีม + Developer → Stage 3 Product/Release Owner → Stage 4 Integration/Automation

## 19. Risks
- Scope ใหญ่ → ทำ P0 ก่อน
- Test Case เดิมมาก → Import
- ไม่ย้ายจาก Excel → Pilot Release จริง
- Evidence โต → File Storage
- Dashboard ช้า → Index/Aggregation
- Permission ผิด → Backend Policy + Test
- History ถูกแก้ → Immutable Execution/Approval

## 20. Definition of Ready
Requirement, Acceptance Criteria, Screen/API, Business Rule, Permission และ Data Dependency ชัด

## 21. Definition of Done
Code Review, Build, Migration, Unit/Integration Test, QA Functional, Permission, Audit, Documentation และไม่มี Critical Known Issue

## 22. First Iteration
เริ่ม Solution+DB → Auth/User/Role → Project/Module → Release/Build → Requirement → Test Case → RTM แล้วจึงเข้าสู่ Execution ซึ่งเป็น Core ที่ซับซ้อนที่สุด
