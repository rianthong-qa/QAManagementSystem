# ProMaxx2 QA Hub — QA Workload & My Work Development Plan

**Document Type:** Development Plan  
**Module:** QA Workload / My Work / Test Assignment  
**Product:** ProMaxx2 QA Hub  
**Official Name:** ProMaxx2 Quality Assurance Management System  
**Version:** 1.0  
**Status:** Proposed  
**Target Team Size:** QA 4 คน

---

## 1. เป้าหมาย

เพิ่มความสามารถให้ QA Hub รองรับการกระจายงานและติดตามภาระงานของ QA รายบุคคล โดยใช้ Test Cycle เป็นจุดเริ่มต้นของ Assignment และให้ผู้ทดสอบเห็นเฉพาะงานของตัวเองหลัง Login

เป้าหมายหลัก:

- Assign Test Case ให้ QA แต่ละคน
- แสดง QA Workload ของทีม 4 คน
- มีหน้า My Work สำหรับ QA Tester
- รองรับ Bulk Assignment และ Reassign
- เก็บ Assignment History และ Audit
- รองรับ Auto Assignment ใน Phase ถัดไป
- เชื่อมกับ Test Execution, Defect, Retest, Regression และ Automation
- ไม่แก้ไข Historical Execution เมื่อมีการ Reassign

---

## 2. Architecture

```text
QA Lead / Admin
      │
      ├── QA Workload
      ├── Assignment
      ├── Team Progress
      └── Rebalance Work
             │
             ▼
        Test Cycle Case
             │
             ▼
      AssignedTesterUserId
             │
       ┌─────┼─────┬─────┐
       ▼     ▼     ▼     ▼
     QA01  QA02  QA03  QA04
       │
       ▼
     My Work
       │
       ▼
 Test Execution
       │
       ├── Pass
       ├── Fail → Defect
       ├── Blocked
       └── Retest / Regression
```

---

## 3. เมนูที่เพิ่ม/ปรับ

```text
Dashboard

My Work
├── งานของฉัน
├── งานวันนี้
├── งานค้าง
├── Fail / Retest
└── Automation Review

Test Execution
├── Test Cycle
├── Execution Workspace
├── Defect
├── Regression
└── Automation

Report & Summary
├── Test Summary
├── QA Workload
└── Release Sign-off
```

### Role Visibility

| Role | การมองเห็น |
|---|---|
| QA Lead | เห็นงาน QA ทุกคน + Assign/Reassign |
| QA Tester | เห็นเฉพาะ My Work ของตัวเอง |
| System Admin | เห็นและจัดการทั้งหมด |
| Viewer | Read-only ตาม Permission |

---

## 4. หน้า QA Workload

### 4.1 เป้าหมาย

ให้ QA Lead เห็นสถานะทีมในหน้าเดียว และตอบได้ทันทีว่าใครงานเยอะ ใครเหลืองานเท่าไร และใครมีงานเสี่ยงค้าง

ตัวอย่างข้อมูล:

```text
QA01
Assigned     35
Done         23
Remaining    12
Load         88%

QA02
Assigned     32
Done         24
Remaining     8
Load         80%

QA03
Assigned     30
Done         25
Remaining     5
Load         75%

QA04
Assigned     28
Done         18
Remaining    10
Load         70%
```

### 4.2 QA Card

แสดง:

- QA Code / Display Name
- Assigned
- Not Run
- In Progress
- Pass
- Fail
- Blocked
- Done
- Remaining
- Workload %
- Estimated Hours
- Actual Hours
- Current Active Case
- Last Activity

### 4.3 Filter

- Project
- Release
- Build
- Test Cycle
- Module
- Test Type
- Priority
- Date

---

## 5. Workload Calculation

### Phase 1 — Count Based

```text
Workload % = Assigned Case / Maximum Assigned Case ของทีม × 100
```

### Phase 2 — Weighted Workload

| Priority | Weight |
|---|---:|
| P0 | 5 |
| P1 | 3 |
| P2 | 2 |
| P3 | 1 |

สูตรแนะนำ:

```text
Case Weight = Priority Weight × Complexity × Estimated Duration Factor

Tester Workload = SUM(Case Weight ของงานที่ยังไม่ Completed)
```

Complexity:

```text
Low    = 1.0
Medium = 1.5
High   = 2.0
```

---

## 6. หน้า My Work

### 6.1 หลักการ

เมื่อ QA Login ระบบต้องใช้ User จาก Authentication Context และแสดงเฉพาะ Test Cycle Case ที่ Assign ให้ User นั้น

```text
Current User
    ↓
AssignedTesterUserId
    ↓
My Work
```

**ห้ามให้ QA Tester ต้องเลือกชื่อ Tester เองเป็นค่า Default**

### 6.2 Summary

```text
งานทั้งหมด       35
ยังไม่เริ่ม       12
กำลังดำเนินการ    2
ผ่าน              18
ไม่ผ่าน            3
Blocked            0
```

### 6.3 Status Filter

- All
- Not Run
- In Progress
- Pass
- Fail
- Blocked
- Retest
- Today
- Overdue

### 6.4 ตาราง My Work

| Field | รายละเอียด |
|---|---|
| Test Case ID | รหัส Test Case |
| Test Case | ชื่อ Test |
| Module | Module |
| Priority | P0-P3 |
| Test Type | Functional / Smoke / Regression |
| Test Cycle | รอบทดสอบ |
| Build | Build ปัจจุบัน |
| Status | Not Run / In Progress / Pass / Fail |
| Due Date | กำหนดเสร็จ |
| Estimated Time | เวลาประเมิน |
| Last Update | อัปเดตล่าสุด |

---

## 7. Assignment Status

Execution Status เดิม:

```text
Not Run
In Progress
Pass
Fail
Blocked
Skipped
```

เพิ่ม Assignment Status:

```text
Unassigned
Assigned
Accepted
In Progress
Completed
Reassigned
```

---

## 8. Quick Actions

หน้า My Work ควรมี:

```text
[Run Test Case]
[บันทึกผลหลาย Case]
[สร้าง Defect]
[ดู Test Cycle]
[ดู Requirement]
[ดู Test Summary]
```

Rules:

- Not Run → แสดง `Run Test Case`
- Fail → แสดง `Create Defect`
- Ready for Retest → แสดง `Retest`
- Automation Ready → แสดง `Run Automation`
- Blocked → ต้องบันทึก Blocker/Reason

---

## 9. Manual Assignment

QA Lead สามารถ Assign งานได้จาก Test Cycle

```text
Test Cycle
    ↓
Select Test Cases
    ↓
Assign Tester
    ↓
QA01 / QA02 / QA03 / QA04
    ↓
Save
```

รองรับ:

- Assign ราย Case
- Multi-select
- Assign ตาม Module
- Assign ตาม Priority
- Assign ตาม Suite
- Assign ตาม Test Type

---

## 10. Bulk Assignment

ตัวอย่าง UX:

```text
เลือก 20 Test Cases

[Assign To]
QA01
QA02
QA03
QA04

[Apply]
```

ก่อน Save แสดง Summary:

```text
QA01 +5 Cases → Total 35
QA02 +5 Cases → Total 32
QA03 +5 Cases → Total 30
QA04 +5 Cases → Total 28
```

---

## 11. Reassign / Rebalance

QA Lead สามารถโยกงานระหว่าง QA

```text
QA01 35 Cases
   ↓ Reassign 5 Cases
QA04 28 → 33 Cases
```

ต้องเก็บ:

```text
FromTester
ToTester
Reason
ChangedBy
ChangedAt
```

Business Rule:

- ห้ามแก้ Historical Execution เดิม
- Assignment ใหม่มีผลเฉพาะงานที่ยังไม่ Completed หรือ Execution ใหม่
- Reassign P0/P1 ต้อง Audit

---

## 12. Auto Assignment — Phase 1

Algorithm แบบ Load Balance:

```text
1. เรียง Test Case ตาม Priority
2. P0 → P1 → P2 → P3
3. หา QA ที่ Workload ต่ำสุด
4. Assign Case
5. Recalculate Workload
6. ทำซ้ำจนหมด
```

Pseudo Logic:

```text
foreach testcase order by priority:
    tester = tester_with_lowest_workload()
    assign(testcase, tester)
    recalculate_workload(tester)
```

---

## 13. Auto Assignment — Phase 2 (Skill Based)

เพิ่ม Skill Matrix

| QA | Sales | Inventory | Member | Accounting |
|---|---:|---:|---:|---:|
| QA01 | 5 | 4 | 3 | 2 |
| QA02 | 3 | 5 | 4 | 2 |
| QA03 | 4 | 3 | 5 | 3 |
| QA04 | 3 | 4 | 3 | 5 |

Assignment Score:

```text
Assignment Score =
Skill Score
+ Availability Score
+ Workload Balance Score
+ Module Experience Score
+ Due Date Priority Score
```

---

## 14. Database Changes

ใช้ Core เดิม:

```text
TestCycleCases.AssignedTesterUserId
TestExecutions.TesterUserId
```

เพิ่มใน `TestCycleCases`:

```text
AssignmentStatus
AssignedAt
AssignedBy
AcceptedAt
StartedAt
DueDate
EstimatedMinutes
Complexity
AssignmentWeight
```

### 14.1 TesterSkills

```text
TesterSkills
- TesterSkillId
- UserId
- ProjectId
- ModuleId
- SkillLevel
- ExperienceLevel
- IsPrimary
- UpdatedAt
- UpdatedBy
```

SkillLevel:

```text
1 Beginner
2 Basic
3 Intermediate
4 Advanced
5 Expert
```

### 14.2 TestAssignmentHistory

```text
TestAssignmentHistory
- AssignmentHistoryId
- TestCycleCaseId
- FromTesterUserId
- ToTesterUserId
- AssignmentType
- Reason
- ChangedBy
- ChangedAt
```

AssignmentType:

```text
Manual
Auto
Reassign
System
```

### 14.3 TesterAvailability (Optional)

```text
TesterAvailability
- AvailabilityId
- UserId
- WorkDate
- AvailableMinutes
- AssignedMinutes
- Status
- Note
```

Status:

```text
Available
Busy
Leave
Unavailable
```

---

## 15. SQL View — vw_TesterWorkload

แนะนำ Implement:

```text
vw_TesterWorkload
```

Fields:

```text
UserId
DisplayName
AssignedCount
NotRunCount
InProgressCount
PassCount
FailCount
BlockedCount
CompletedCount
RemainingCount
EstimatedMinutes
AssignmentWeight
WorkloadPercent
```

---

## 16. API

### QA Workload

```http
GET /api/qa-workload
```

Filters:

```text
ProjectId
ReleaseId
BuildId
TestCycleId
ModuleId
```

### My Work

```http
GET /api/my-work
```

ใช้ UserId จาก Authentication Token

**ไม่ควรรับ TesterUserId จาก Client สำหรับ My Work Default**

### Assign

```http
POST /api/test-cycles/{cycleId}/assign
```

```json
{
  "testCycleCaseIds": [],
  "testerUserId": "",
  "reason": ""
}
```

### Auto Assign

```http
POST /api/test-cycles/{cycleId}/auto-assign
```

### Reassign

```http
POST /api/test-cycle-cases/{id}/reassign
```

---

## 17. Permission

เพิ่ม Permission:

```text
QA.Workload.View
QA.Assignment.View
QA.Assignment.Create
QA.Assignment.Reassign
QA.Assignment.AutoAssign
QA.MyWork.View
QA.MyWork.Execute
```

| Role | Permission |
|---|---|
| QA Lead | Assignment ทั้งหมด |
| QA Tester | My Work + Execute |
| Admin | ทั้งหมด |
| Viewer | View Only |

---

## 18. Security Rules

- QA Tester ห้ามเปลี่ยน `AssignedTesterUserId` เอง
- Reassign ต้องผ่าน API + Permission
- ทุก Reassign ต้อง Audit
- Historical Execution ห้ามถูกแก้ตาม Assignment ใหม่
- API My Work ใช้ User จาก Authentication Context
- QA Lead/Admin เท่านั้นที่ Filter ดู Tester อื่นได้

---

## 19. Test Cycle Integration

เพิ่ม Step ตอนสร้าง Test Cycle:

```text
1. Release / Build
2. Environment
3. Test Suite
4. Select Cases
5. Assign QA
6. Review
7. Ready
```

หน้า Assign QA มี 2 Mode:

```text
Manual Assignment
Auto Assignment
```

---

## 20. Dashboard Integration

Dashboard เพิ่ม Team Workload:

```text
QA01 ████████ 88%
QA02 ███████  80%
QA03 ██████   75%
QA04 ██████   70%
```

และ Summary:

```text
Unassigned Cases  12
Overdue Cases      4
Blocked Cases      2
P0 Remaining       3
```

---

## 21. Automation Integration

Automation Execution ไม่จำเป็นต้อง Assign QA ทุก Run แต่ต้องมี Owner และ Reviewer

เพิ่ม Concept:

```text
AutomationOwnerUserId
FailureReviewerUserId
```

Flow:

```text
Automation Run
    ↓
FAIL
    ↓
Failure Reviewer
    ↓
My Work
    ↓
Review Evidence
    ↓
Create Defect / False Failure / Re-run
```

---

## 22. Defect Integration

เมื่อสร้าง Defect จาก My Work ระบบ Auto Link:

```text
TestCaseId
TestExecutionId
TestCycleId
BuildId
EnvironmentId
ReporterUserId
```

เพื่อลดการกรอกซ้ำ

---

## 23. Retest Integration

เมื่อ Defect เป็น:

```text
Ready for Retest
```

สร้าง Task เข้า My Work ของ QA ที่รับผิดชอบ

```text
Type: Retest
Source: DEF-00125
Test Case: TC-SALE-010
Build: 10.4.28.0850
Priority: P0
```

---

## 24. Notification

| Trigger | ผู้รับ |
|---|---|
| Test Case Assigned | QA Tester |
| Reassigned | QA เดิม + QA ใหม่ |
| P0 Assigned | QA Tester + QA Lead |
| Due Soon | QA Tester |
| Overdue | QA Tester + QA Lead |
| Ready for Retest | Assigned QA |
| Workload Over Threshold | QA Lead |
| Automation Failure Assigned | Failure Reviewer |

---

## 25. UX/UI Rules

### QA Lead

Priority ของข้อมูล:

```text
Team → Workload → Remaining → Risk → Action
```

### QA Tester

Priority ของข้อมูล:

```text
My Work → Priority → Status → Due Date → Action
```

### Color

```text
Primary       Blue
Pass          Green
Fail          Red
Blocked       Orange
In Progress   Blue
Not Run       Gray
```

ต้องมี Text Label เสมอ ห้ามใช้สีอย่างเดียว

---

## 26. Empty / Loading / Error State

### Empty

```text
วันนี้ยังไม่มีงานที่ได้รับมอบหมาย

เมื่อ QA Lead Assign Test Case
งานจะแสดงที่หน้านี้อัตโนมัติ
```

### Loading

ใช้ Skeleton และห้ามแสดงค่า `0` ชั่วคราวก่อน API โหลดสำเร็จ

### Error

```text
ไม่สามารถโหลดข้อมูล Workload ได้

[ลองใหม่]
```

ห้าม fallback เป็น 0 เพราะอาจทำให้เข้าใจผิด

---

## 27. Functional Requirements ใหม่

```text
FR-051 QA Workload
FR-052 My Work
FR-053 Manual Test Assignment
FR-054 Bulk Assignment
FR-055 Auto Assignment
FR-056 Workload Balance
FR-057 Tester Skill Matrix
FR-058 Assignment History
FR-059 Reassignment
FR-060 Tester Availability
FR-061 My Work Notification
FR-062 Retest Assignment
FR-063 Automation Failure Assignment
```

---

## 28. Development Phases

### Phase W1 — Foundation (P0)

- Assigned Tester
- My Work API
- My Work UI
- QA Workload View
- Manual Assignment
- Permission
- Audit

### Phase W2 — Team Management (P1)

- Bulk Assignment
- Reassignment
- Assignment History
- Due Date
- Estimated Time
- Overdue Detection

### Phase W3 — Smart Assignment (P2)

- Auto Assignment
- Workload Weight
- Skill Matrix
- Tester Availability

### Phase W4 — Integration (P1/P2)

- Defect
- Retest
- Regression
- Automation Failure Review
- Notification

### Phase W5 — Analytics (P3)

- Tester Productivity
- Test Completion Trend
- Average Execution Time
- Workload Balance
- Fail Discovery
- Module Coverage

---

## 29. Priority

| Feature | Priority |
|---|---|
| My Work | P0 |
| Manual Assignment | P0 |
| QA Workload | P0 |
| Assigned Tester Filter | P0 |
| Permission/Audit | P0 |
| Bulk Assignment | P1 |
| Reassign | P1 |
| Assignment History | P1 |
| Retest Integration | P1 |
| Auto Assignment | P2 |
| Skill Matrix | P2 |
| Availability | P3 |
| Analytics | P3 |

---

## 30. Recommended Development Order

```text
Users / Roles
    ↓
Test Cycle Case Assignment
    ↓
My Work
    ↓
QA Workload
    ↓
Bulk Assign
    ↓
Reassign + History
    ↓
Defect / Retest Integration
    ↓
Notification
    ↓
Auto Assignment
    ↓
Skill Matrix
    ↓
Analytics
```

---

## 31. QA Test Scenarios สำหรับ Feature นี้

### Assignment

- Assign Case ให้ QA01 แล้ว QA01 เห็นใน My Work
- QA02 ต้องไม่เห็นงาน QA01
- QA Lead เห็นทั้ง 4 คน
- Bulk Assign 20 Case สำเร็จครบ
- Reassign QA01 → QA04 ถูกต้อง
- Reassign แล้ว History ครบ

### Permission

- QA Tester เปลี่ยน AssignedTester ไม่ได้
- Viewer Execute ไม่ได้
- QA Lead Reassign ได้

### Historical Data

- Case ที่ Execute แล้ว Reassign ต้องไม่เปลี่ยน TesterUserId ของ Execution เดิม
- Retest สร้าง Execution ใหม่

### Workload

- Assigned/Done/Remaining ถูกต้อง
- P0/P1 Weight ถูกต้อง
- Completed Case ไม่ถูกนับเป็น Remaining

### My Work

- Default เห็นเฉพาะ User Login
- Filter Status ทำงาน
- Overdue ถูกต้อง
- Empty/Loading/Error State ถูกต้อง

---

## 32. Definition of Done

Feature ถือว่าเสร็จเมื่อ:

- QA 4 คนรับ Assignment แยกกันได้
- Login แล้วเห็นงานของตัวเองทันที
- QA Lead เห็น Workload ทุกคน
- Manual และ Bulk Assignment ใช้งานได้
- Reassign เก็บ History
- Workload คำนวณถูกต้อง
- Execution เก็บ Tester จริง
- Historical Execution ไม่ถูกแก้
- Permission ถูกต้อง
- Audit Log ครบ
- Defect/Retest เชื่อมกลับ My Work ได้
- Dashboard แสดง Team Progress
- รองรับ Loading/Empty/Error State
- Critical Flow มี Unit / Integration / UI Test

---

## 33. Target Flow

```text
Release
   ↓
Build
   ↓
Test Cycle
   ↓
Test Suite
   ↓
Test Cases
   ↓
QA Assignment
   │
   ├── QA01
   ├── QA02
   ├── QA03
   └── QA04
        ↓
      My Work
        ↓
     Execution
        ↓
 Pass / Fail / Blocked
        │
        ├── Defect
        │      ↓
        │    Retest
        │      ↓
        └──── My Work
        ↓
 Regression
        ↓
 Test Summary
        ↓
 Release Readiness
```

---

## 34. สรุป

การเพิ่ม **QA Workload + My Work + Assignment** ควรต่อยอดจาก `TestCycleCases` และ `TestExecutions` เดิม ไม่สร้างระบบ Execution แยกใหม่

หลักสำคัญ:

```text
QA Lead   = บริหารงานทั้งทีม
QA Tester = เห็นและทำงานของตัวเอง
Test Cycle = จุดเริ่ม Assignment
Execution  = เก็บผู้ทดสอบจริง
History    = ห้ามกระทบ Historical Execution
```

แนวทางนี้รองรับทีม QA 4 คนปัจจุบัน และสามารถขยายจำนวน QA ในอนาคตโดยไม่ต้องเปลี่ยน Core Workflow
