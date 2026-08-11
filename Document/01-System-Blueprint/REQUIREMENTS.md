# ProMaxx2 QA Management System — REQUIREMENTS

เวอร์ชัน 1.0 | Development Blueprint

## 1. เป้าหมาย
สร้างระบบกลางสำหรับบริหารงาน QA ของ ProMaxx2 โดย Trace ได้ครบ:
`Requirement → Test Scenario → Test Case → Test Cycle → Test Result → Defect → Retest → Regression → Test Summary → Release Sign-off`

เป้าหมาย:
- ลดการใช้ Excel/เอกสารแยกหลายชุด
- ตรวจ Requirement Coverage ได้
- ระบุผลทดสอบตาม Release/Build ได้
- Trace Defect กลับ Requirement/Test Case ได้
- ควบคุม Smoke และ Regression
- แสดง Release Readiness
- เก็บ Evidence และ Audit Trail

## 2. Module ที่ระบบต้องมี
1. Dashboard
2. Project / Product / Module
3. Release & Build Management
4. Requirement & RTM
5. Test Scenario / Test Case
6. Test Data
7. Test Environment
8. Test Suite
9. Smoke Test
10. Test Cycle / Execution
11. Regression
12. Defect Management
13. Evidence / Attachment
14. Daily / Weekly QA Status
15. Test Summary
16. Risk Acceptance
17. Release Sign-off
18. User / Role / Permission
19. Notification
20. Audit Log
21. Import / Export
22. Integration API

## 3. Role
| Role | หน้าที่ |
|---|---|
| System Admin | User/Role/Permission/Setting |
| QA Lead | Scope, Assignment, Review, Release Recommendation |
| QA Tester | Test Case, Execution, Evidence, Defect, Retest |
| Developer | Defect, Root Cause, Fix Build, Technical Note |
| Product Owner | Requirement, Acceptance Criteria |
| Release Owner | Build/Release และ Final Decision |
| Viewer/Support | Read-only ตามสิทธิ์ |

## 4. Functional Requirements

### FR-001 Dashboard
แสดง Release, Build, Requirement Coverage, Execution %, Pass Rate, Open Defect P0-P3, Regression Progress, Module Health และ GO/CONDITIONAL GO/NO-GO พร้อม Filter ตาม Release/Build/Module/Tester/Date

### FR-002 Project / Module
สร้าง Project และ Module แบบ Parent/Child, Module Owner, Active/Inactive

### FR-003 Release
เก็บ Release Code, Version, Type, Scope, Planned Date, Owner และสถานะ Draft/Testing/Ready/Released/Cancelled

### FR-004 Build
ทุก Build ผูก Release และเก็บ Build Number, App Version, Package Version, Build Date, Change Notes, Known Issues, Release Candidate Flag

### FR-005 Requirement
เก็บ Requirement ID, Title, Description, Acceptance Criteria, Module, Priority, Risk, Source, Owner, Release, Status, Revision และ In-Scope Flag

### FR-006 RTM
Trace Requirement → Test Case → Execution → Defect และคำนวณ Covered/Not Covered/Not Tested/Passed/Failed/Blocked

### FR-007 Test Scenario
เก็บ Scenario ID, Module, Title, Objective, Type, Priority, Risk, Requirement Link

### FR-008 Test Case
เก็บ ID, Scenario, Module, Title, Objective, Preconditions, Priority, Test Type, Owner, Status, Revision และ Automation Candidate

### FR-009 Test Steps
Test Case มีหลาย Step: Step No., Action, Test Data, Expected Result และ Reorder ได้

### FR-010 Test Data
เก็บ Data ID, Type, Description, Value/Reference, Initial State, Reset Instruction, Sensitive Flag และ Owner

### FR-011 Test Environment
เก็บ OS, App Version, DB Type/Version, Dataset, DPI, Resolution, Network, Service/API Version, Device

### FR-012 Test Suite
สร้าง Smoke, Critical Regression, Module Regression และ Full Regression; Test Case เดียวอยู่หลาย Suite ได้

### FR-013 Test Cycle
ผูก Release + Build + Environment + Suite พร้อม Owner, Tester, Date และสถานะ Draft → Ready → In Progress → Completed → Closed

### FR-014 Test Execution
บันทึก Tester, Execution No., Start/Complete, Pass/Fail/Blocked/Skipped, Actual Result, Comment, Build, Environment และเก็บ History ห้ามเขียนทับ

### FR-015 Evidence
แนบ Screenshot, Video, Log, SQL Result, PDF, Excel/CSV และ Text กับ Execution/Defect

### FR-016 Defect
เก็บ Defect ID, Module, Build Found, Environment, Severity, Priority, Status, Steps, Expected/Actual, Frequency, Business Impact, Workaround, Assignee/Reporter

Workflow: `New → Triaged → Assigned → In Progress → Fixed → Ready for Retest → Closed` และ Reopen ได้

### FR-017 Defect Traceability
เชื่อม Defect กับ Requirement, Test Case, Execution, Build Found, Fix Build และ Regression Case

### FR-018 Developer Resolution
Developer ระบุ Root Cause, Resolution, Fix Build, Changed Components, Regression Impact และ Technical Note

### FR-019 Retest
สร้าง Retest จาก Defect, เลือก Fix Build, อ้าง Test Case เดิม, เก็บผลใหม่ และ Reopen ได้

### FR-020 Regression
สร้าง Regression Suite จาก Module/Tag/Risk/Impact และเก็บผลแยก Build

### FR-021 Impact Analysis
บันทึก Changed Module, Shared Component, DB Change, API Change, Calculation Change และ Installer/Update Change

### FR-022 Daily QA Status
สรุป Planned/Executed/Pass/Fail/Blocked, Defect New/Retest, Blocker, Risk, Completed และ Next Plan

### FR-023 Weekly QA Status
สรุป Green/Yellow/Red, Progress, Coverage, Pass Rate, Defect Trend, Regression, Risks, Blockers และ Milestones

### FR-024 Test Summary
Generate Scope, Out-of-Scope, Execution Metrics, Coverage, Defects, Regression, Update, Performance, Known Issues, Risks และ QA Recommendation

### FR-025 Risk Acceptance
เก็บ Risk ID, Issue, Impact, Probability, Risk Level, Workaround, Owner, Target Fix, Review Date, Approver และ Approval Status

### FR-026 Release Sign-off
รองรับ QA, Development, Product และ Release Owner พร้อม Decision: GO / CONDITIONAL GO / NO-GO

### FR-027 Release Gate
Config Threshold ได้ เช่น Smoke P0=100%, Open P0=0, P1 Blocker=0 หรือมี Approved Risk, Critical Regression และ Requirement Coverage ผ่านเกณฑ์

### FR-028 User / Role / Permission
กำหนด View/Create/Edit/Delete/Execute/Assign/Approve/Export/Admin ระดับ Module/Feature

### FR-029 Audit Log
เก็บ User, Action, Entity, Entity ID, Change Summary/Before-After, Timestamp และ Client Info

### FR-030 Notification
แจ้ง Assignment, P0/P1, Ready for Retest, New Build, Deadline, Blocker, Pending Risk Approval และ Pending Sign-off

### FR-031 Search / Filter
ค้น ID, Title, Module, Release, Build, Status, Priority, Owner/Tester, Tag, Date

### FR-032 Import / Export
Import Requirement/Test Case/Test Data และ Export RTM/Test Result/Defect/Status/Summary

### FR-033 Attachment
เก็บ Unique ID, Filename, MIME, Size, Hash, Uploader, Entity Reference

### FR-034 Version History
Requirement/Test Case ต้องมี Revision History และ Execution เก่าต้องอ้าง Revision เดิม

## 5. Non-Functional Requirements
- Authentication + Role Authorization + HTTPS
- Server-side validation และ Parameterized Query/ORM
- หน้า List ใช้ Pagination
- Dashboard เป้าหมาย <3 วินาทีในข้อมูลปกติ
- Transaction สำหรับการบันทึกหลายตาราง
- Soft Delete สำหรับข้อมูลหลัก
- ภาษาไทยเป็นหลักและ Responsive
- แนะนำ ASP.NET Core .NET 10 REST API + SQL Server
- Centralized Logging และ Backup

## 6. Business Rules
1. Execution ต้องผูก Build
2. Fail ต้อง Link/Create Defect ได้
3. Retest ห้ามเขียนทับ Fail เดิม
4. P0 ห้าม Release แบบ GO
5. P1 Blocker ต้องปิดหรือมี Approved Risk Acceptance
6. Sign-off ต้องมี Audit
7. Test Case Revision ใหม่ห้ามเปลี่ยน Historical Execution
8. Requirement In Scope ต้องคำนวณ Coverage
9. Inactive Test Case ต้องยังอยู่ใน Historical Cycle
10. Release Readiness คำนวณจาก Source Data

## 7. MVP
Phase 1: Login/User/Role, Project/Module, Release/Build, Requirement/RTM, Test Case, Suite, Cycle/Execution, Defect, Dashboard

Phase 2: Test Data, Environment, Evidence, Regression, Summary, Risk, Sign-off, Audit

Phase 3: Notification, Import/Export, Integration API, CI/CD, Analytics
