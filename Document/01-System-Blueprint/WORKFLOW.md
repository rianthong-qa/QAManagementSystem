# ProMaxx2 QA Management System — WORKFLOW

## 1. End-to-End
```text
สร้าง Release → เพิ่ม Build → รับ Requirement
→ QA Impact Analysis → RTM → Scenario/Test Case
→ Test Data/Environment → Test Suite → Test Cycle
→ Smoke
   ├─ Fail → Defect → Reject/Hold Build → Build ใหม่
   └─ Pass
→ Functional/Integration
→ Defect → Developer Fix → Fix Build → Retest
→ Regression
→ Test Summary
→ Release Gate
   ├─ Blocker → NO-GO
   ├─ Acceptable Risk → Risk Acceptance → CONDITIONAL GO
   └─ ผ่านทั้งหมด → GO
→ Sign-off → Released
```

## 2. Requirement / RTM
1. สร้าง Requirement
2. ระบุ Module/Release/Priority/Acceptance Criteria
3. QA Review
4. Approved
5. สร้าง Scenario/Test Case
6. Link RTM
7. Execute
8. Coverage อัปเดตอัตโนมัติ

Coverage:
- ไม่มี Case = Not Covered
- มี Case ยังไม่ Run = Covered/Not Tested
- Required Case ผ่านทั้งหมด = Passed
- มี Fail = Failed
- มี Blocked = Blocked/Partial

## 3. Test Case Lifecycle
`Draft → Review → Ready → Deprecated`

เมื่อแก้ Ready Case:
- เพิ่ม Revision
- เก็บ Revision เดิม
- Historical Execution อ้าง Revision เดิม

## 4. Build Intake
Release Owner/Developer ระบุ Build, Version, Package, Change List, Changed Components, Known Issues
QA ตรวจ Package/DB Migration/Module Version แล้วสร้าง Smoke Cycle

## 5. Smoke
1. เลือก Build + Environment
2. โหลด Smoke Suite
3. Assign QA
4. Execute P0 ก่อน
5. P0 Fail → Defect + Hold Build
6. Pass → เปิด Full Test

## 6. Execution
`Not Run → Pass / Fail / Blocked / Skipped`

- Fail → Create/Link Defect
- Blocked → ต้องระบุ Blocker
- Skipped → ต้องมีเหตุผล
- Retest สร้าง ExecutionNo ใหม่
- เก็บ Evidence ตาม Execution

## 7. Defect
`New → Triaged → Assigned → In Progress → Fixed → Ready for Retest`
- Retest Pass → Closed
- Retest Fail → Reopen

P0/P1 แจ้ง QA Lead และ Developer Owner และขึ้น Release Dashboard

## 8. Developer Fix
Developer กรอก Root Cause, Resolution, Changed Components, Fix Build, Regression Impact, Technical Note แล้วส่ง Ready for Retest

## 9. Retest
QA เลือก Fix Build → ดู Original Case/Result → Execute ใหม่ → Pass=Closed / Fail=Reopen → เพิ่ม Regression ตาม Impact

## 10. Regression
Trigger สำคัญ:
- Shared Library
- DB Schema
- Calculation
- Permission
- API Contract
- Velopack/Installer
- P0/P1 Fix
- Major Refactor

Flow: Impact Analysis → Select Suite → Add Impact Cases → Regression Cycle → Execute → Update Readiness

## 11. Daily / Weekly
Daily: ระบบคำนวณ Planned/Executed/Pass/Fail/Blocked/Defect; QA เพิ่ม Blocker/Risk/Next Plan

Weekly: Trend + Coverage + Pass Rate + Defect + Regression; QA Lead ระบุ Green/Yellow/Red และ Recommendation

## 12. Test Summary
เลือก Release + Candidate Build → รวม Metrics → ตรวจ Scope/Out-of-Scope → Defects → Regression → Update/Performance → Known Issues → Risks → QA Recommendation

## 13. Release Gate

### GO
- Smoke ผ่าน
- P0 = 0
- P1 Blocker = 0
- Critical Regression ผ่าน Threshold
- Coverage ผ่าน Threshold
- Update/Migration Critical ผ่าน

### CONDITIONAL GO
- ไม่มี P0
- Issue ไม่กระทบ Core/Data Integrity
- มี Workaround
- มี Approved Risk Acceptance

### NO-GO
- P0 ค้าง
- Core Flow Fail
- Data Loss/Corruption Risk
- Financial/Stock Critical ผิด
- Permission Critical
- Update/Migration ไม่ปลอดภัย
- P1 Blocker ไม่มี Approval

## 14. Risk Acceptance
`Draft → Pending Approval → Approved / Rejected → Closed/Expired`

ต้องมี Impact, Probability, Workaround, Owner, Target Fix, Review Date, Approver

## 15. Sign-off
QA Recommendation → Development Acknowledgement → Product Approval → Release Owner Final Decision

ทุก Approval เก็บ User, Decision, Comment, Timestamp, Build, Release และ Audit

## 16. Notification
| Trigger | ผู้รับ |
|---|---|
| Assign Cycle | QA Tester |
| P0/P1 | QA Lead + Developer |
| Ready for Retest | QA |
| New Build | QA Lead |
| Blocked | QA Lead |
| Risk Approval | Approver |
| Sign-off | Approver |
| NO-GO | Stakeholders |

## 17. Audit
Audit Requirement Change, Test Case Revision, Execution, Defect Status/Severity, Risk Approval, Sign-off และ Permission

## 18. หน้าจอ
Login, Dashboard, Project/Module, Release, Build, Requirement, RTM, Scenario, Test Case, Test Data, Environment, Suite, Cycle, Execution Workspace, Defect, Regression, Daily, Weekly, Summary, Risk, Sign-off, User/Role, Audit, Settings

## 19. Development Order
Phase A: Project → Release → Build → User/Role

Phase B: Requirement → Scenario → Test Case → RTM

Phase C: Suite → Cycle → Execution

Phase D: Defect → Retest → Evidence

Phase E: Regression → Dashboard → Daily/Weekly

Phase F: Summary → Risk → Sign-off → Audit

จากนั้นเพิ่ม Import/Export, Notification และ Integration API
