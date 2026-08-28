# ProMaxx2 QA Hub — Weighted Auto Assignment Development Specification

**Document Type:** Development Specification  
**Module:** QA Workload / My Work / Auto Assignment  
**Product:** ProMaxx2 QA Hub  
**Official Name:** ProMaxx2 Quality Assurance Management System  
**Version:** 1.0  
**Status:** Proposed  
**Target Team:** QA 4 คน  
**Related Module:** Test Cycle, Test Case, My Work, QA Workload, Automation, Defect, Retest, Regression  

---

# 1. Objective

พัฒนาระบบ Auto Assign สำหรับกระจาย Test Case ให้ QA แต่ละคนโดยอัตโนมัติ โดยไม่ใช้เพียงจำนวน Test Case แต่ให้พิจารณา:

- ความยากของ Test Case
- Priority
- Estimated Time
- Skill ของ QA ตาม Module
- Current Workload
- Capacity
- Availability
- Criticality
- Minimum Required Skill
- Existing Assignment
- Due Date

ระบบต้องสามารถเสนอ Assignment ที่สมดุลและอธิบายเหตุผลได้

---

# 2. Design Principle

Auto Assign ต้องทำงานแบบ:

```text
Auto Assign
    ↓
Calculate
    ↓
Preview
    ↓
QA Lead Review
    ↓
Confirm
```

ห้าม Auto Assign แล้วบันทึกทันทีโดยไม่มี Preview

เหตุผล:

- QA Lead อาจมีข้อมูลเพิ่มเติมที่ระบบไม่รู้
- QA บางคนอาจติด Support / Workshop / งานด่วน
- บาง Case ต้องการคนที่มีประสบการณ์เฉพาะ
- Critical Case อาจต้อง Reviewer เพิ่ม

---

# 3. Core Concept

ระบบต้องใช้แนวคิด:

```text
Weighted Assignment
```

แทน:

```text
Equal Case Count
```

ตัวอย่าง:

```text
QA01 = 10 Cases
QA02 = 10 Cases
```

ไม่ได้หมายความว่างานเท่ากัน

ต้องดู:

```text
QA01 Weight = 35
QA02 Weight = 72
```

---

# 4. Test Case Weight

## 4.1 Complexity Weight

กำหนดค่าเริ่มต้น:

| Complexity | Weight |
|---|---:|
| Easy | 1 |
| Medium | 2 |
| Hard | 3 |
| Critical | 5 |

---

## 4.2 Priority Weight

| Priority | Weight |
|---|---:|
| P0 | 5 |
| P1 | 3 |
| P2 | 2 |
| P3 | 1 |

---

## 4.3 Estimated Time Factor

ใช้ EstimatedMinutes เป็นฐาน

ตัวอย่าง:

```text
30 นาที  = 0.5
60 นาที  = 1.0
90 นาที  = 1.5
120 นาที = 2.0
```

Formula:

```text
EstimatedTimeFactor = EstimatedMinutes / 60
```

ควรกำหนด Minimum:

```text
Minimum Time Factor = 0.5
```

เพื่อไม่ให้ Case สั้นมากมี Weight = 0

---

# 5. Case Weight Formula

Phase 1:

```text
CaseWeight =
ComplexityWeight
× PriorityWeight
× EstimatedTimeFactor
```

ตัวอย่าง:

```text
TC-STOCK-001

Complexity = Hard      = 3
Priority   = P0        = 5
Estimated  = 120 min   = 2.0

CaseWeight = 3 × 5 × 2.0
           = 30
```

อีกตัวอย่าง:

```text
TC-REPORT-001

Complexity = Easy     = 1
Priority   = P2       = 2
Estimated  = 30 min   = 0.5

CaseWeight = 1 × 2 × 0.5
           = 1
```

---

# 6. Criticality

เพิ่ม Field:

```text
Criticality
```

ค่า:

```text
Low
Medium
High
Critical
```

กรณี Criticality = Critical:

- ต้องตรวจ Minimum Skill
- อาจต้อง Reviewer
- ไม่ Auto Assign ให้ QA ที่ Skill ต่ำกว่า Requirement
- ถ้าไม่มี QA ที่ผ่านเกณฑ์ ให้ระบบแจ้ง Manual Assignment Required

---

# 7. Required Skill Level

เพิ่ม Field ใน Test Case:

```text
RequiredSkillLevel
```

ค่า 1–5

```text
1 = Beginner
2 = Basic
3 = Intermediate
4 = Advanced
5 = Expert
```

ตัวอย่าง:

```text
TC-STOCK-COST-001

Module             = Inventory
Criticality        = Critical
RequiredSkillLevel = 4
```

ระบบต้อง Filter QA ที่:

```text
TesterSkillLevel < RequiredSkillLevel
```

ออกจาก Candidate List

---

# 8. Tester Skill Matrix

สร้าง Skill Matrix ตาม Module

ตัวอย่าง:

| QA | Sales | Inventory | Member | Accounting |
|---|---:|---:|---:|---:|
| QA01 | 5 | 4 | 3 | 2 |
| QA02 | 3 | 5 | 4 | 2 |
| QA03 | 4 | 3 | 5 | 3 |
| QA04 | 3 | 4 | 3 | 5 |

---

# 9. Tester Current Load

คำนวณ Current Load จาก Assignment ที่ยังไม่ Completed

```text
CurrentWeight =
SUM(AssignmentWeight)
WHERE AssignmentStatus NOT IN (Completed, Cancelled)
```

แนะนำคำนวณแยกตาม:

- Test Cycle
- Date Range
- Release
- Build
- Project

---

# 10. Tester Capacity

เพิ่มแนวคิด Capacity

ตัวอย่าง:

```text
DailyCapacityMinutes = 480
AvailableMinutes     = 360
AssignedMinutes      = 240
RemainingMinutes     = 120
```

Formula:

```text
RemainingCapacity =
AvailableMinutes - AssignedMinutes
```

ระบบไม่ควร Assign Case ที่ EstimatedMinutes สูงกว่า RemainingCapacity หากมี Candidate อื่น

---

# 11. Availability

สถานะ QA:

```text
Available
Busy
Leave
Unavailable
```

Rule:

```text
Leave       → Exclude
Unavailable → Exclude
Busy        → ลดคะแนน
Available   → Normal
```

---

# 12. Assignment Candidate Filter

ก่อนคำนวณคะแนน ต้อง Filter Candidate

```text
Candidate QA
    ↓
IsActive = true
    ↓
Has QA Role
    ↓
Availability != Leave/Unavailable
    ↓
Skill >= RequiredSkillLevel
    ↓
Has Remaining Capacity
```

หากไม่มี Candidate:

```text
AssignmentStatus = ManualReviewRequired
```

---

# 13. Assignment Score

Phase 2 แนะนำ Formula:

```text
AssignmentScore =
WorkloadBalanceScore × 40%
+ ModuleSkillScore   × 35%
+ AvailabilityScore  × 15%
+ ExperienceScore    × 10%
```

---

# 14. Workload Balance Score

ตัวอย่าง Scale 0–100

```text
CurrentLoadPercent = CurrentWeight / CapacityWeight × 100
```

จากนั้น:

```text
WorkloadBalanceScore = 100 - CurrentLoadPercent
```

Clamp:

```text
0 ถึง 100
```

ตัวอย่าง:

```text
QA01 Load = 80%
WorkloadBalanceScore = 20

QA02 Load = 45%
WorkloadBalanceScore = 55
```

---

# 15. Module Skill Score

Convert Skill 1–5 เป็น Score:

```text
Skill 1 = 20
Skill 2 = 40
Skill 3 = 60
Skill 4 = 80
Skill 5 = 100
```

---

# 16. Availability Score

ตัวอย่าง:

```text
Available = 100
Busy      = 50
Leave     = Exclude
Unavailable = Exclude
```

---

# 17. Experience Score

สามารถคำนวณจาก:

- จำนวน Case Module นี้ที่เคย Execute
- Pass/Fail History
- Defect Discovery
- Reviewer Experience

Phase แรกใช้ Manual Score 1–5

Phase หลังค่อยคำนวณจาก History

---

# 18. Assignment Score Example

```text
TC-INV-025

Module       = Inventory
Complexity   = Hard
Priority     = P0
Weight       = 30
RequiredSkill = 4
```

Candidate:

```text
QA01
Inventory Skill = 4
Load = 80%
Score = 76

QA02
Inventory Skill = 5
Load = 65%
Score = 91

QA03
Inventory Skill = 3
→ Excluded

QA04
Inventory Skill = 4
Load = 90%
Score = 70
```

Result:

```text
Recommended Tester = QA02
```

Reason:

```text
- Skill ผ่าน Requirement และสูงสุด
- Current Load ต่ำกว่า QA01/QA04
- Capacity เพียงพอ
```

---

# 19. Assignment Algorithm — Phase 1

เริ่มด้วย Algorithm ที่เข้าใจง่าย

```text
1. เรียง Test Case ตาม Priority
2. ภายใน Priority เรียง CaseWeight มาก → น้อย
3. Filter QA ตาม Required Skill
4. Filter QA ตาม Availability
5. เลือก QA ที่ CurrentWeightedLoad ต่ำสุด
6. หาก Load ใกล้กัน เลือก QA ที่ Module Skill สูงกว่า
7. หากยังเท่ากัน เลือก QA ที่ Remaining Capacity สูงกว่า
8. Assign แบบ Preview
9. Recalculate Load
10. ทำซ้ำจนหมด
```

---

# 20. Suggested Sorting

```text
Priority ASC
Criticality DESC
CaseWeight DESC
EstimatedMinutes DESC
```

P0 ต้องถูก Assign ก่อน

---

# 21. Pseudo Code

```text
cases = GetUnassignedCases()
testers = GetAvailableTesters()

sort cases by:
    Priority
    Criticality desc
    CaseWeight desc

for each case:

    candidates = testers
        where Skill(case.Module) >= case.RequiredSkillLevel
        and Availability allows assignment
        and RemainingCapacity > 0

    if candidates empty:
        mark case ManualReviewRequired
        continue

    candidate = candidates
        order by CurrentWeightedLoad asc
        then ModuleSkill desc
        then RemainingCapacity desc
        first

    preview assignment(case, candidate)

    candidate.CurrentWeightedLoad += case.CaseWeight
    candidate.AssignedMinutes += case.EstimatedMinutes
```

---

# 22. Pair Testing / Reviewer Assignment

สำหรับ Critical Case สามารถเพิ่ม:

```text
ReviewerRequired = true
```

Flow:

```text
Primary Tester
+
Reviewer
```

Reviewer Rule:

```text
Reviewer Skill >= Tester Skill
หรือ
Reviewer Skill >= RequiredSkillLevel
```

ตัวอย่าง:

```text
TC-STOCK-COST-001

Tester   = QA02
Reviewer = QA01
```

---

# 23. Manual Override

QA Lead ต้องสามารถเปลี่ยน Suggested Tester

ระบบต้องเก็บ:

```text
SuggestedTesterUserId
FinalTesterUserId
OverrideReason
ChangedBy
ChangedAt
```

ถ้า Final Tester ไม่ตรง Suggestion ให้บังคับ Reason เมื่อ:

- Skill ต่ำกว่า Required
- Load สูงกว่า Threshold
- Critical Case

---

# 24. Auto Assignment Preview

หน้าจอ Preview ต้องแสดง:

| Test Case | Complexity | Weight | Skill Required | Recommended QA | Current Load | After Load |
|---|---|---:|---:|---|---:|---:|
| TC-STOCK-001 | Hard | 30 | 4 | QA02 | 45% | 68% |
| TC-SALE-010 | Medium | 9 | 3 | QA01 | 52% | 61% |
| TC-MEMBER-021 | Easy | 2 | 2 | QA03 | 40% | 42% |

---

# 25. Workload Summary Preview

Before:

```text
QA01 Weight 42
QA02 Weight 38
QA03 Weight 35
QA04 Weight 45
```

After:

```text
QA01 Weight 51
QA02 Weight 68
QA03 Weight 37
QA04 Weight 85
```

---

# 26. Workload Threshold

Default:

```text
Normal    < 70%
Warning   70–84%
Overload  >= 85%
```

หาก Auto Assign ทำให้ QA >= 85%:

```text
Warning:
QA04 Workload สูงเกินเกณฑ์
```

ระบบยัง Preview ได้ แต่ต้องให้ QA Lead Review

---

# 27. Auto Rebalance

Phase 3 เพิ่ม:

```text
[Rebalance]
```

ระบบตรวจ Assignment ที่ยังไม่ Started

แล้วเสนอการย้าย Case

ข้อห้าม:

- ห้าม Rebalance Case ที่ In Progress
- ห้าม Reassign Execution History
- Critical Case ต้องตรวจ Skill ใหม่
- ต้องเก็บ Assignment History

---

# 28. Database Changes

## 28.1 TestCases

เพิ่ม:

```text
Complexity
EstimatedMinutes
RequiredSkillLevel
Criticality
ReviewerRequired
DefaultAssignmentWeight
```

---

## 28.2 TestCycleCases

เพิ่ม:

```text
AssignmentStatus
AssignmentWeight
AssignedTesterUserId
SuggestedTesterUserId
AssignedAt
AssignedBy
DueDate
EstimatedMinutesSnapshot
RequiredSkillLevelSnapshot
ComplexitySnapshot
CriticalitySnapshot
```

ต้อง Snapshot ค่าใน Cycle เพื่อไม่ให้ Test Case Master ที่แก้ภายหลังกระทบ Historical Cycle

---

# 29. TesterSkills

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

Unique:

```text
UserId + ProjectId + ModuleId
```

---

# 30. TesterAvailability

```text
TesterAvailability
- AvailabilityId
- UserId
- WorkDate
- AvailableMinutes
- AssignedMinutes
- Status
- Note
- UpdatedAt
```

---

# 31. TestAssignmentHistory

```text
TestAssignmentHistory
- AssignmentHistoryId
- TestCycleCaseId
- SuggestedTesterUserId
- FromTesterUserId
- ToTesterUserId
- AssignmentType
- CaseWeight
- AssignmentScore
- AlgorithmVersion
- OverrideReason
- ChangedBy
- ChangedAt
```

AssignmentType:

```text
Manual
AutoPreview
AutoConfirmed
Reassign
Rebalance
System
```

---

# 32. Assignment Calculation Log

แนะนำเพิ่มเพื่อ Debug / Audit:

```text
AssignmentCalculationLogs
- CalculationLogId
- TestCycleId
- TestCycleCaseId
- CandidateUserId
- WorkloadScore
- SkillScore
- AvailabilityScore
- ExperienceScore
- FinalScore
- ExcludedReason
- AlgorithmVersion
- CalculatedAt
```

มีประโยชน์มากเมื่อถาม:

```text
ทำไมระบบ Assign Case นี้ให้ QA02?
```

---

# 33. Algorithm Versioning

ทุก Auto Assignment ต้องเก็บ:

```text
AlgorithmVersion
```

ตัวอย่าง:

```text
WAA-1.0
```

เมื่อ Formula เปลี่ยน:

```text
WAA-1.1
```

ห้ามคำนวณย้อนหลังแล้วเขียนทับ History

---

# 34. API

## Preview

```http
POST /api/test-cycles/{cycleId}/auto-assign/preview
```

Request:

```json
{
  "testCycleCaseIds": [],
  "includeAvailability": true,
  "includeSkill": true,
  "algorithmVersion": "WAA-1.0"
}
```

Response:

```json
{
  "cycleId": "...",
  "algorithmVersion": "WAA-1.0",
  "assignments": [
    {
      "testCycleCaseId": "...",
      "caseWeight": 30,
      "recommendedTesterUserId": "...",
      "score": 91,
      "currentLoadPercent": 45,
      "afterLoadPercent": 68,
      "reason": [
        "Skill requirement passed",
        "Lowest eligible workload",
        "Capacity available"
      ]
    }
  ],
  "warnings": []
}
```

---

# 35. Confirm API

```http
POST /api/test-cycles/{cycleId}/auto-assign/confirm
```

Request:

```json
{
  "previewId": "...",
  "assignments": [
    {
      "testCycleCaseId": "...",
      "testerUserId": "...",
      "overrideReason": null
    }
  ]
}
```

---

# 36. Preview Expiration

Preview ต้องมีอายุ

Default:

```text
10 นาที
```

เพราะ Current Load อาจเปลี่ยน

หากหมดอายุ:

```text
PreviewExpired
```

ให้คำนวณใหม่ก่อน Confirm

---

# 37. Concurrency

Confirm ต้องตรวจ Optimistic Concurrency

เช่น:

```text
AssignmentVersion
RowVersion
```

หากมีคน Assign Case ไปก่อนแล้ว:

```text
409 Conflict
```

ระบบต้องไม่เขียนทับ Assignment ล่าสุด

---

# 38. Permission

เพิ่ม:

```text
QA.Assignment.AutoPreview
QA.Assignment.AutoConfirm
QA.Assignment.Override
QA.Assignment.Rebalance
QA.SkillMatrix.View
QA.SkillMatrix.Edit
QA.Availability.View
QA.Availability.Edit
```

Role:

```text
QA Lead
- Preview
- Confirm
- Override
- Rebalance

QA Tester
- View own assignment
- Update own availability (ถ้าอนุญาต)

Admin
- All
```

---

# 39. UX/UI — Test Cycle Assignment

เพิ่ม Tab:

```text
Test Cycle
├── Test Cases
├── Assignment
├── Execution
└── Summary
```

หน้า Assignment:

```text
[Manual Assign]
[Auto Assign]
```

---

# 40. UX/UI — Auto Assign Wizard

## Step 1 — Scope

```text
เลือก Test Cases
เลือก Module
เลือก Priority
เลือก QA Pool
```

## Step 2 — Rule

```text
Use Skill Matrix
Use Capacity
Use Availability
Workload Threshold
```

## Step 3 — Calculate

```text
กำลังคำนวณ Case Weight
กำลังตรวจ Skill
กำลัง Balance Workload
```

## Step 4 — Preview

แสดง Recommended Assignment

## Step 5 — Confirm

บันทึก Assignment

---

# 41. UX/UI — QA Lead Preview

แต่ละ Case แสดง:

```text
TC-STOCK-001

Hard / P0
Weight 30

Recommended
QA02

Skill        5/5
Current Load 45%
After Load   68%
Capacity     120 min
Score        91/100

[Use QA02]
[Change]
```

---

# 42. Explainability

ระบบต้องอธิบายได้ว่าเลือก QA เพราะอะไร

ตัวอย่าง:

```text
Recommended QA02

เหตุผล:
✓ Inventory Skill 5/5
✓ Required Skill 4
✓ Current Load ต่ำกว่า Candidate อื่น
✓ Remaining Capacity เพียงพอ
```

ไม่ควรแสดงแค่:

```text
AI Recommended QA02
```

---

# 43. My Work Integration

หลัง Confirm:

```text
AssignedTesterUserId
```

ถูกบันทึก

Case ต้องปรากฏใน:

```text
My Work
```

ของ QA นั้นทันที

---

# 44. Notification

หลัง Confirm:

```text
Test Case Assigned
```

แจ้ง QA Tester

กรณี P0 / Critical:

แจ้ง:

```text
QA Tester
QA Lead
```

---

# 45. Automation Integration

Automation Case สามารถใช้ Assignment Engine สำหรับ:

```text
Automation Failure Review
Automation Maintenance Review
Object Repository Maintenance
```

เช่น:

```text
Automation Fail
    ↓
Review Task
    ↓
Auto Assign Reviewer
```

โดยใช้ Skill:

```text
Automation
Module
Object Repository
```

---

# 46. Defect / Retest Integration

Ready for Retest:

ระบบสามารถใช้ Auto Assignment แบบ:

```text
Prefer Original Tester
```

Rule:

```text
Original Tester Available
+ Skill ผ่าน
+ Load ไม่เกิน Threshold
→ Assign คนเดิม
```

หากไม่ผ่าน:

```text
Run Auto Assignment
```

---

# 47. Regression Integration

Regression Cycle อาจมี Test Case จำนวนมาก

Auto Assign เหมาะสำหรับ:

```text
Module Regression
Full Regression
Release Regression
```

Flow:

```text
Regression Suite
    ↓
Test Cycle
    ↓
Auto Assign
    ↓
QA01–QA04
```

---

# 48. Business Rules

1. Case ที่ In Progress ห้าม Auto Reassign
2. Completed Case ห้าม Auto Reassign
3. Skill ต่ำกว่า Required ห้าม Assign Critical Case
4. Leave / Unavailable ห้าม Assign
5. Auto Assign ต้อง Preview ก่อน Confirm
6. Confirm ต้องตรวจ Preview Version
7. Manual Override ต้อง Audit
8. Historical Execution ห้ามเปลี่ยน Tester ตาม Assignment ใหม่
9. Test Case Weight ใน Cycle ต้อง Snapshot
10. Algorithm Version ต้องเก็บทุกครั้ง
11. Critical Case ReviewerRequired ต้อง Assign Reviewer
12. P0 ต้องถูกจัดลำดับก่อน P1/P2/P3

---

# 49. Error Codes

แนะนำ:

```text
AUTOASSIGN_NO_ELIGIBLE_TESTER
AUTOASSIGN_SKILL_REQUIREMENT_NOT_MET
AUTOASSIGN_CAPACITY_EXCEEDED
AUTOASSIGN_PREVIEW_EXPIRED
AUTOASSIGN_ASSIGNMENT_CONFLICT
AUTOASSIGN_INVALID_CASE_WEIGHT
AUTOASSIGN_TESTER_UNAVAILABLE
AUTOASSIGN_CRITICAL_REVIEWER_REQUIRED
```

---

# 50. Audit

เก็บ Audit:

```text
Auto Assign Requested
Preview Generated
Assignment Suggested
Assignment Overridden
Assignment Confirmed
Assignment Rebalanced
```

Audit ต้องมี:

```text
User
Timestamp
TestCycle
Case
Suggested Tester
Final Tester
Weight
Score
Reason
Algorithm Version
```

---

# 51. Dashboard Integration

QA Workload Dashboard แสดง:

```text
QA01
Cases       35
Weight      72
Est. Hours  18.5
Load        82%

Easy        15
Medium      12
Hard         6
Critical     2
```

---

# 52. Team Balance KPI

เพิ่ม:

```text
Workload Variance
```

ตัวอย่าง:

```text
Team Balance = 92%
```

เป้าหมาย:

```text
>= 85% = Good
70–84% = Warning
< 70% = Imbalanced
```

---

# 53. Acceptance Criteria

Auto Assign ถือว่าผ่านเมื่อ:

- ระบบคำนวณ Case Weight ได้ถูกต้อง
- P0 ถูก Assign ก่อน Priority ต่ำกว่า
- Critical Case ไม่ Assign ให้ Skill ต่ำกว่า Requirement
- Leave / Unavailable ถูก Exclude
- Current Load ถูก Recalculate ทุกครั้งหลัง Suggest Case
- QA ที่ Load ต่ำไม่ได้ถูกเลือกเสมอ หาก Skill ไม่ผ่าน
- Preview แสดง Before/After Load
- QA Lead เปลี่ยน Suggested Tester ได้
- Override มี Audit
- Confirm บันทึกเข้า TestCycleCases
- My Work แสดง Assignment ใหม่
- Historical Execution ไม่ถูกเปลี่ยน
- Preview หมดอายุแล้ว Confirm ไม่ได้
- Concurrent Assignment ไม่เขียนทับกัน
- Algorithm Version ถูกเก็บ
- Calculation Reason ตรวจสอบย้อนหลังได้

---

# 54. Suggested Test Scenarios

## Scenario 1 — Equal Skill / Different Load

```text
QA01 Skill 4 Load 80
QA02 Skill 4 Load 40
```

Expected:

```text
QA02
```

---

## Scenario 2 — Lower Load but Skill Not Enough

```text
Required Skill = 4

QA01 Skill 4 Load 70
QA02 Skill 2 Load 20
```

Expected:

```text
QA01
```

---

## Scenario 3 — Critical No Eligible Tester

```text
Required Skill = 5

All QA Skill <= 4
```

Expected:

```text
ManualReviewRequired
```

---

## Scenario 4 — QA On Leave

```text
QA02 Skill 5
Load 30
Status Leave
```

Expected:

```text
QA02 excluded
```

---

## Scenario 5 — Overload Warning

After Assignment:

```text
QA04 Load = 88%
```

Expected:

```text
Preview Warning
```

---

## Scenario 6 — Preview Expired

```text
Preview > 10 min
```

Expected:

```text
Confirm rejected
```

---

## Scenario 7 — Assignment Conflict

QA Lead A และ QA Lead B เปิด Preview เดียวกัน

A Confirm ก่อน

Expected B:

```text
409 Conflict
```

---

# 55. Development Phases

## Phase AA1 — Core Weight

- Complexity
- Priority Weight
- Estimated Time
- Case Weight
- Weighted Workload

**Priority: P0**

---

## Phase AA2 — Skill Assignment

- Skill Matrix
- Required Skill
- Candidate Filter
- Suggested Tester

**Priority: P0**

---

## Phase AA3 — Preview / Confirm

- Auto Assign Preview
- Before / After Load
- Manual Override
- Confirm
- Audit

**Priority: P0**

---

## Phase AA4 — Capacity / Availability

- Daily Capacity
- Leave / Busy
- Remaining Minutes
- Overload Warning

**Priority: P1**

---

## Phase AA5 — Smart Scoring

- Assignment Score
- Experience
- Explainability
- Algorithm Version

**Priority: P1**

---

## Phase AA6 — Advanced

- Auto Rebalance
- Pair Tester / Reviewer
- Historical Performance
- Automation Review Assignment

**Priority: P2**

---

# 56. Recommended MVP

MVP ไม่ควรเริ่มด้วย AI Model

ใช้ Deterministic Rule Engine ก่อน

```text
Complexity
+ Priority
+ Estimated Time
+ Skill
+ Current Load
```

ข้อดี:

- ตรวจสอบง่าย
- Test ได้ง่าย
- Explain ได้
- Debug ได้
- Audit ได้

AI สามารถเพิ่มภายหลังเพื่อช่วย:

```text
Predict Complexity
Predict Estimated Time
Suggest Required Skill
```

แต่ Final Assignment Engine ควรยังใช้ Rule + Score ที่ตรวจสอบได้

---

# 57. Future AI Enhancement

ในอนาคต AI สามารถวิเคราะห์ Test Case เพื่อเสนอ:

```text
Complexity = Hard
Estimated = 90 min
Required Skill = 4
Criticality = High
```

Flow:

```text
Test Case
   ↓
AI Estimation
   ↓
QA Lead Review
   ↓
Approved Weight
   ↓
Auto Assignment Engine
```

AI ไม่ควรเปลี่ยน Weight ที่ Approved แล้วแบบอัตโนมัติใน Historical Cycle

---

# 58. Definition of Done

Feature Weighted Auto Assignment เสร็จเมื่อ:

- Case Weight ถูกคำนวณ
- Skill Matrix ใช้งานจริง
- QA 4 คนถูก Balance ตาม Weight
- Required Skill ถูกบังคับ
- Auto Assign Preview ใช้งานได้
- QA Lead Confirm ได้
- Manual Override ได้
- Before / After Load แสดงถูกต้อง
- My Work อัปเดตทันที
- Audit ครบ
- Assignment History ครบ
- Concurrent Update ปลอดภัย
- Critical Rule ทำงาน
- Unit Test / Integration Test ครบ Critical Path
- UX แสดง Reason ของ Recommendation
- Algorithm Version ถูกเก็บ

---

# 59. Target Flow

```text
Test Cycle
    ↓
Unassigned Test Cases
    ↓
Calculate Case Weight
    ↓
Load Skill Matrix
    ↓
Load QA Availability
    ↓
Calculate Current Workload
    ↓
Filter Eligible QA
    ↓
Calculate Assignment
    ↓
Preview
    ↓
QA Lead Review
    ├── Override
    └── Accept
         ↓
       Confirm
         ↓
   TestCycleCases
         ↓
      My Work
         ↓
     Execution
```

---

# 60. Summary

ระบบ Auto Assign ของ ProMaxx2 QA Hub ควรใช้หลัก:

```text
ความหนักของเคส
+
ความถนัดของ QA
+
Load ปัจจุบัน
+
เวลาที่เหลือ
```

ไม่ใช่:

```text
จำนวน Test Case เท่ากัน
```

แนวทางที่แนะนำคือ:

```text
Deterministic Weighted Assignment
→ Preview
→ Human Review
→ Confirm
```

เพื่อให้ระบบมีความแม่นยำ ตรวจสอบย้อนหลังได้ และเหมาะกับทีม QA ที่แต่ละ Module มีความยากและความเสี่ยงต่างกัน
