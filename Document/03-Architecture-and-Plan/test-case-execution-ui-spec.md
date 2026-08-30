# SPEC: ปรับปรุงหน้า Test Case Execution

## 1. Objective

ปรับปรุงหน้า Test Case Execution ให้รองรับการบันทึกผลการทดสอบแบบราย Step และให้ระบบคำนวณผลรวมของ Test Case อัตโนมัติ

### ปัญหาปัจจุบัน
- แต่ละ Test Step สามารถมีผลต่างกัน เช่น Pass / Fail
- ด้านล่างยังมีปุ่ม Pass / Fail / Blocked / Skip สำหรับทั้ง Test Case
- ผู้ใช้งานเกิดความสับสนว่าต้องเลือกผลรวมเองหรือไม่
- มีความเสี่ยงที่ผลรวม Test Case ไม่ตรงกับผลของแต่ละ Step

### แนวทางใหม่
- QA เป็นผู้กำหนดผลเฉพาะระดับ Step
- ระบบคำนวณ Overall Result ของ Test Case อัตโนมัติ
- ไม่ให้ผู้ใช้งานเลือก Pass / Fail ของ Test Case เอง

---

## 2. Layout ใหม่

แบ่งหน้าออกเป็น 5 Sections

1. Test Case Header
2. Overall Result Summary
3. Test Steps
4. Test Case Summary
5. Action Bar

---

## 3. Test Case Header

แสดงข้อมูลดังนี้

- Test Case ID
- Test Case Name
- Tester
- Test Date / Test Time
- Environment
- Build Version
- Test Cycle

### ตัวอย่าง

```text
Test Case Execution

TC-LOGIN-001
ตรวจสอบการ Login เข้าสู่ระบบ

Tester: QA01
Environment: UAT
Build: 10.4.28.0845
Test Cycle: Regression Cycle #12
```

---

## 4. Overall Result Summary

เพิ่มส่วนสรุปไว้ด้านบนของ Test Steps

### แสดง Overall Result

Overall Result ต้องเป็นค่าที่ระบบคำนวณเท่านั้น

ห้ามให้ User กดเปลี่ยนสถานะโดยตรง

สถานะที่รองรับ:

- Pass
- Fail
- Blocked
- Not Run
- In Progress
- Skipped

### Summary Card

แสดงจำนวน Step ตามสถานะ

```text
Overall Result
❌ Fail

Passed      2
Failed      2
Blocked     0
Not Run     0
```

ข้อความอธิบาย:

```text
มี Step ที่ Fail จำนวน 2 รายการ
```

---

## 5. Overall Result Calculation Logic

ใช้ Rule ตามลำดับ Priority ดังนี้

```text
Fail
>
Blocked
>
In Progress / Not Run
>
Pass
```

### Rule 1: Fail

ถ้ามี Step ใด Step หนึ่งเป็น Fail

```text
Overall Result = Fail
```

ตัวอย่าง:

```text
Step 1 = Pass
Step 2 = Fail
Step 3 = Pass

Result = Fail
```

### Rule 2: Blocked

กรณีไม่มี Fail แต่มี Step เป็น Blocked

```text
Overall Result = Blocked
```

ตัวอย่าง:

```text
Pass
Pass
Blocked

Result = Blocked
```

### Rule 3: In Progress

กรณีมี Step ที่ยังไม่ได้เลือกผล

ตัวอย่าง:

```text
Pass
Pass
Not Run

Overall Result = In Progress
```

หมายเหตุ:

Not Run ของ Step ไม่ควรหมายถึง Test Case Not Run โดยทันที  
หากบาง Step ถูกทดสอบแล้วให้ถือว่า Test Case กำลังดำเนินการ

### Rule 4: Pass

ทุก Step ต้องเป็น Pass เท่านั้น

```text
Overall Result = Pass
```

### Rule 5: Skipped Test Case

Skipped ใช้ในระดับ Test Case เท่านั้น

ตัวอย่างเหตุผล:

- Requirement ถูกยกเลิก
- Test Case ไม่เกี่ยวข้องกับ Build นี้
- Feature ถูก Remove

หาก User เลือก Skip Test Case

```text
Overall Result = Skipped
```

และไม่ต้องใช้ผล Step ในการคำนวณ

---

## 6. Test Steps Table

ปรับ Column เป็น

| Column | Description |
|---|---|
| # | Step Number |
| Action / Test Data | ขั้นตอนและข้อมูลทดสอบ |
| Expected Result | ผลที่คาดหวัง |
| Step Result | ผลการทดสอบ Step |
| Actual Result / Comment | ผลที่เกิดขึ้นจริง |

---

## 7. Step Result

แต่ละ Step ให้เลือกสถานะได้

- Pass
- Fail
- Blocked
- Not Run

ใช้ Dropdown หรือ Segmented Control

ตัวอย่าง:

```text
[ ✓ Pass ▼ ]
[ ✕ Fail ▼ ]
[ ⊘ Blocked ▼ ]
[ ○ Not Run ▼ ]
```

### สีแนะนำ

- Pass = Green
- Fail = Red
- Blocked = Amber / Yellow
- Not Run = Gray

---

## 8. Actual Result

แต่ละ Step ต้องมีช่อง Actual Result

### ตัวอย่าง

```text
Step 2

Expected Result:
เลือกรหัสสาขาถูกต้อง

Result:
Fail

Actual Result:
ไม่พบสาขา BR-A ในรายการ
```

---

## 9. Validation Rule

### Pass

Actual Result ไม่บังคับกรอก

### Fail

Actual Result บังคับกรอก

หาก User เลือก Fail แต่ Actual Result ว่าง ให้แสดง Validation:

```text
กรุณาระบุผลที่เกิดขึ้นจริง
```

### Blocked

Actual Result / Reason บังคับกรอก

ตัวอย่าง:

```text
ไม่สามารถทดสอบได้ เนื่องจาก API Server ไม่ทำงาน
```

### Not Run

ไม่บังคับกรอก

---

## 10. Bulk Step Action

ด้านบนของตารางเพิ่ม

```text
Set All Steps
```

ปุ่ม:

```text
[ ✓ Set All Pass ]
[ ✕ Set All Fail ]
[ ○ Set All Not Run ]
[ ⊘ Set All Blocked ]
```

เมื่อกดต้องมี Confirmation

ตัวอย่าง:

```text
ต้องการเปลี่ยนผล Test Step ทั้งหมดเป็น Pass หรือไม่?

Cancel
Confirm
```

---

## 11. เปลี่ยนข้อความปุ่มเดิม

ห้ามใช้ข้อความ:

```text
ทั้งหมด: Pass
ทั้งหมด: Fail
ทั้งหมด: NotRun
```

ให้เปลี่ยนเป็น:

```text
Set All Pass
Set All Fail
Set All Not Run
```

เพื่อป้องกันความเข้าใจผิดว่าเป็น Summary

---

## 12. Test Case Summary

ใต้ Test Steps เพิ่ม

### Actual Result Summary

Textarea

Label:

```text
Actual Result (สรุปผลที่เกิดขึ้นจริง)
```

ตัวอย่าง:

```text
BR-A ยังไม่สามารถ Override ค่า Allow Negative Stock ได้
ระบบยังใช้ค่าจากสำนักงานใหญ่
```

### Comment

Textarea

Label:

```text
Comment / หมายเหตุเพิ่มเติม
```

ใช้สำหรับ:

- ข้อมูลเพิ่มเติม
- Environment
- Reference
- ข้อสังเกตของ QA

---

## 13. Auto Generate Actual Result Summary

Optional Feature

เพิ่มปุ่ม:

```text
[ Generate Summary ]
```

ระบบสามารถนำ Failed / Blocked Step มาสรุปเป็น Actual Result ให้อัตโนมัติ

ตัวอย่าง:

```text
Step 2 Fail:
ไม่พบสาขา BR-A

Step 4 Fail:
BR-A ยังใช้ค่า Override เดิม
```

สร้าง Summary:

```text
พบปัญหา 2 รายการ ได้แก่ ไม่พบสาขา BR-A ในรายการ และ BR-A ยังไม่ใช้ค่า Override ตามที่กำหนด
```

---

## 14. Bottom Action Bar

ลบปุ่มเดิม:

```text
Pass
Fail
Blocked
```

ออกทั้งหมด

ให้เหลือ:

```text
[ Save Progress ]

[ Skip Test Case ]

[ Complete Test ]
```

---

## 15. Save Progress

ใช้สำหรับบันทึกผลระหว่างทดสอบ

สามารถ Save ได้แม้บาง Step ยังเป็น Not Run

หลัง Save:

```text
Status = In Progress
```

---

## 16. Complete Test

เมื่อกด Complete Test ระบบตรวจสอบก่อน

### Condition

ทุก Step ต้องมี Result

หากพบ Not Run ให้แสดง Dialog:

```text
ยังมี Test Step ที่ยังไม่ได้ทดสอบจำนวน 2 Step
```

ตัวเลือก:

```text
Cancel
Complete Anyway
```

Default:

```text
Cancel
```

---

## 17. Complete Test Result

เมื่อ Complete ระบบคำนวณ Overall Result

ตัวอย่าง:

```text
Step Results:

Pass
Fail
Pass
Fail

Overall:

Fail
```

User ไม่สามารถ Override เป็น Pass ได้

---

## 18. Skip Test Case

เมื่อเลือก Skip Test Case ต้องแสดง Modal

### Title

```text
Skip Test Case
```

### Field

```text
Reason *
```

### Reason Options

- Requirement Changed
- Not Applicable
- Feature Removed
- Environment Limitation
- Duplicate Test Case
- Other

### Comment

Textarea

### ปุ่ม

```text
Cancel
Confirm Skip
```

---

## 19. Defect Integration

เมื่อ Step = Fail

แสดง Action:

```text
[ Create Defect ]
```

เมื่อกดให้สร้าง Defect จาก Step ปัจจุบัน

### Auto Fill

- Test Case ID
- Test Case Name
- Step Number
- Action
- Expected Result
- Actual Result
- Build
- Environment
- Tester

หลังสร้างแล้วแสดง:

```text
Defect: DF-2026-00123
```

สามารถ Click เพื่อเปิด Defect Detail

---

## 20. Status Badge

ใช้ Badge มาตรฐาน

```text
✓ Pass
✕ Fail
⊘ Blocked
○ Not Run
→ Skipped
◷ In Progress
```

---

## 21. UI Style

Design Direction:

- Flat UI
- Minimal
- Clean
- Enterprise QA Tool
- ใช้ whitespace ให้มากขึ้น
- Border บาง
- Radius 8-10px
- ไม่ใช้ Shadow หนัก
- Button ขนาดกลาง
- Table อ่านง่าย
- ใช้ Theme เดิมของ QA Hub

---

## 22. Responsive

Desktop First

รองรับความกว้าง:

- 1920
- 1440
- 1280

เมื่อหน้าจอเล็ก:

- Actual Result Column สามารถลด Width ได้
- แต่ไม่ควรซ่อน Action
- ไม่ควรซ่อน Expected Result
- ไม่ควรซ่อน Step Result

---

## 23. Example Scenario

Test Case มี 4 Step

### Step 1

```text
Action:
บันทึกค่ากลาง

Expected:
ค่ากลางบันทึกสำเร็จ

Result:
Pass
```

### Step 2

```text
Action:
เปิดค่าระดับสาขา BR-A

Expected:
เลือกรหัสสาขาถูกต้อง

Result:
Fail

Actual:
ไม่พบสาขา BR-A
```

### Step 3

```text
Action:
กำหนด Override

Expected:
Override สำเร็จ

Result:
Pass
```

### Step 4

```text
Action:
Login ทดสอบทั้งสองสาขา

Expected:
แต่ละสาขาใช้ค่าตามลำดับที่กำหนด

Result:
Fail

Actual:
BR-A ยังใช้ค่าจากส่วนกลาง
```

### Summary ต้องแสดง

```text
Overall Result

Fail

Passed
2

Failed
2

Blocked
0

Not Run
0
```

---

## 24. Acceptance Criteria

### AC01
ผู้ใช้สามารถกำหนด Result แยกแต่ละ Step ได้

### AC02
รองรับ Pass / Fail / Blocked / Not Run

### AC03
เมื่อมี Step Fail อย่างน้อย 1 Step  
Overall Result ต้องเป็น Fail

### AC04
ผู้ใช้ไม่สามารถเปลี่ยน Overall Result เป็น Pass เองได้

### AC05
หาก Result = Fail ต้องบังคับกรอก Actual Result

### AC06
หาก Result = Blocked ต้องบังคับกรอก Reason

### AC07
สามารถ Set All Pass / Fail / Not Run / Blocked ได้

### AC08
สามารถ Save Progress ได้แม้ยังทดสอบไม่ครบ

### AC09
ระบบแสดงจำนวน Passed / Failed / Blocked / Not Run แบบ Real-Time

### AC10
สามารถ Complete Test ได้

### AC11
หลัง Complete Test ระบบคำนวณ Overall Result อัตโนมัติ

### AC12
สามารถ Skip Test Case พร้อมระบุเหตุผลได้

### AC13
Step ที่ Fail สามารถ Create Defect ได้

### AC14
Overall Result และ Counter ต้อง Update ทันทีเมื่อเปลี่ยน Step Result

---

## 25. Important Business Rule

ห้ามมี Logic แบบนี้:

```text
User กำหนด Step Result = Fail
แต่สามารถกด Test Case = Pass ได้
```

ผล Test Case ต้องอ้างอิงจาก Step Result เสมอ

ยกเว้นกรณีเดียว:

```text
Test Case = Skipped
```

---

## 26. Recommended Component Structure

```text
TestCaseExecutionPage

├── TestCaseHeader
├── TestExecutionSummary
│   ├── OverallResultCard
│   └── StepResultCounter
│
├── TestStepSection
│   ├── BulkStepActions
│   └── TestStepTable
│       └── TestStepRow
│           ├── StepResultSelector
│           ├── ActualResultInput
│           └── CreateDefectAction
│
├── TestSummarySection
│   ├── ActualResultSummary
│   └── Comment
│
└── TestExecutionActionBar
    ├── SaveProgressButton
    ├── SkipTestCaseButton
    └── CompleteTestButton
```

---

## 27. คำสั่งเพิ่มเติมสำหรับ AI Coding Agent

ให้ปรับเฉพาะหน้า Test Case Execution ก่อน  
ห้ามเปลี่ยน Business Logic ของ Module อื่นโดยไม่จำเป็น

ควรแยกฟังก์ชันคำนวณ Result กลางออกมา เช่น:

```ts
calculateOverallResult(steps)
```

เพื่อให้สามารถนำไปใช้ซ้ำใน:

- Test Cycle
- Dashboard
- Report
- Test Summary

### Unit Test ที่ต้องมี

อย่างน้อยต้องครอบคลุม:

1. All Pass
2. Pass + Fail
3. Pass + Blocked
4. Pass + Not Run
5. All Not Run
6. Skipped

### ตัวอย่าง Expected Logic

```ts
calculateOverallResult([
  { result: "PASS" },
  { result: "FAIL" },
  { result: "PASS" }
])

// Expected: FAIL
```

---

# Definition of Done

งานถือว่าเสร็จเมื่อ:

- UI ใหม่แสดงผลตาม Spec
- Step Result แยกจาก Overall Result อย่างชัดเจน
- Overall Result คำนวณอัตโนมัติ
- ไม่มีปุ่มให้ User Override Pass / Fail ของ Test Case
- Validation Fail / Blocked ทำงาน
- Counter Update Real-Time
- Save Progress ใช้งานได้
- Complete Test ใช้งานได้
- Skip Test Case ใช้งานได้
- Existing Test Data ไม่เสียหาย
- Unit Test ของ Result Calculation ผ่านทั้งหมด
