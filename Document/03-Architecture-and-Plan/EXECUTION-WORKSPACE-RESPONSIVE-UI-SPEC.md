# Execution Workspace Responsive UI Development Specification

เอกสารนี้กำหนดแนวทางพัฒนาปรับปรุงหน้า Execution Workspace สำหรับหน้าจอขนาดเล็ก โดยเน้นแก้ปัญหา layout เพี้ยน, ข้อความถูกบีบ, ปุ่มล้น และการใช้งาน Test Step ที่ไม่สะดวก โดยต้องไม่กระทบ logic การบันทึกผล Execution เดิม

วันที่จัดทำ: 2026-09-03  
หน้า: Execution Workspace  
Frontend: `src/ProMaxx2.QA.Web/src/App.tsx`, `src/ProMaxx2.QA.Web/src/styles.css`

## 1. เป้าหมาย

- ให้หน้าใช้งานได้จริงตั้งแต่ Desktop ถึง Mobile
- ไม่มี horizontal scroll ระดับหน้าโดยไม่จำเป็น
- ไม่ให้ Test Step, Actual Result, Result Button หรือ Action Bar ถูกบีบ/ทับ/ตัด
- รักษาโครงสร้างการทำงานเดิมของ Save Progress, Complete Test, Skip และ Create Defect
- ทำให้ Case Queue, Main Panel และ Execution History ใช้งานได้ตามลำดับความสำคัญบนจอแคบ
- ทำให้ controls รองรับ keyboard และ screen reader ตาม UI Design System

## 2. ขอบเขตที่อนุญาตให้แก้

### แก้ได้

- CSS layout, grid, flex, breakpoint และ container query
- JSX wrapper/label/data attribute ที่จำเป็นต่อ responsive และ accessibility
- การจัดกลุ่ม visual ของ Toolbar, Case Queue, Step Card และ History
- Loading/empty/error presentation ที่ไม่เปลี่ยน API
- Shared style เฉพาะ Execution Workspace

### ห้ามเปลี่ยนโดยไม่มี requirement เพิ่มเติม

- API endpoint และ request payload
- ชื่อหรือความหมายของ status: `Pass`, `Fail`, `Blocked`, `NotRun`, `Skipped`
- สูตร `calculateOverallResult`
- validation ของ Complete Test
- การ restore ค่า `stepStatuses`, `stepActuals`, `actual`, `comment`
- flow ของ Save Progress, Skip, Complete, Create Defect และ Delete History
- permission behavior ของ action เดิม

## 3. หลักฐานและสาเหตุปัจจุบัน

### 3.1 Layout มี minimum width สูงเกินไป

ปัจจุบัน layout หลักใช้:

```css
.execution-layout {
  grid-template-columns: 320px minmax(520px, 1fr) 290px;
}
```

เมื่อรวม Queue, Main, History และ gap ต้องใช้พื้นที่มากกว่า 1,100px ก่อนหัก Sidebar ของ application shell จึงทำให้ content area แคบเกินไปเมื่อ viewport ลดลง

### 3.2 Breakpoint อิง viewport มากกว่าพื้นที่ content จริง

การ collapse สำคัญอยู่ที่ 1,100px และ 760px แต่ viewport ยังรวมพื้นที่ Sidebar อยู่ ทำให้ content จริงเข้าสู่ layout แบบ mobile ช้ากว่าที่ควร

### 3.3 Step table ใช้ fixed columns

ปัจจุบัน Step row กำหนด Result และ Actual Result เป็น column ความกว้างคงที่ ทำให้ Action/Expected Result ถูกบีบเมื่อ Main Panel แคบ

### 3.4 Mobile ยังใช้โครงสร้างตารางเดิม

บนจอเล็กมีการเปลี่ยนเป็น 2 columns แต่ยังใช้ cell เดิมโดยไม่มี label ต่อ field จึงอ่านความสัมพันธ์ระหว่างหัวข้อกับข้อมูลได้ยาก และมีโอกาสเกิดการซ้อน/ตัดข้อความ

---

# Phase 1 — Prevent Layout Breakage

ระดับ: เร่งด่วนมาก  
เป้าหมาย: หยุดอาการล้นจอและการบีบเนื้อหาหลักก่อน

## 1.1 ปรับ Execution layout ให้ยืดหยุ่น

แก้ให้ grid item ยอมย่อได้จริง:

- ใช้ `minmax(0, 1fr)` แทน `minmax(520px, 1fr)` ในช่วงที่พื้นที่น้อย
- กำหนด `min-width: 0` ให้ `.execution-layout`, `.execution-main`, `.step-table`, `.step-row` และ child ที่เป็น grid/flex item
- หลีกเลี่ยง fixed width ที่รวมแล้วเกิน content area
- ตรวจ padding ของ `.content`, `.execution-main` และ card wrapper

โครงสร้างเป้าหมาย:

```text
Wide:    Queue | Main | History
Medium:  Queue | Main
         History
Narrow:  Queue (collapsible) / Main / History
Mobile:  Queue / Main / History (single column)
```

Acceptance Criteria:

- ไม่มี page-level horizontal scroll ที่ 1024px, 768px, 390px และ 360px
- Main Panel ไม่ถูกดันออกนอก viewport
- Case Queue และ History ไม่ทำให้ Main Panel กว้างเกินพื้นที่
- text และ input สามารถ wrap หรือย่อได้โดยไม่ทับกัน

## 1.2 ปรับ breakpoint ให้สัมพันธ์กับพื้นที่จริง

ขั้นต่ำต้องปรับ breakpoint ให้ collapse ก่อนพื้นที่ไม่พอ:

- Wide desktop: 3 columns
- Medium desktop/tablet landscape: Queue + Main, History ลงด้านล่าง
- Tablet: Main เป็นหลัก และ Queue/History เป็น stacked หรือ collapsible
- Mobile: single-column card layout

แนะนำให้พิจารณา CSS Container Query จาก content area เพื่อไม่ผูกกับ viewport ที่ยังรวม Sidebar อยู่

หากยังใช้ media query ให้กำหนด breakpoint โดยทดสอบร่วมกับ Sidebar ที่เปิดอยู่จริง

## 1.3 ป้องกัน overflow ที่เกิดจาก text และ URL

ใช้กับข้อมูลที่อาจยาว:

- Test Case title
- Action
- Test Data
- Expected Result
- Actual Result
- Comment
- Environment
- Tester name

ต้องรองรับ:

```css
overflow-wrap: anywhere;
word-break: break-word;
min-width: 0;
```

ห้ามใช้ `overflow-x: hidden` ที่ root เพื่อซ่อนปัญหาโดยไม่แก้ layout ต้นเหตุ

---

# Phase 2 — Mobile Step Interaction

ระดับ: เร่งด่วนสูง  
เป้าหมาย: ทำให้การอ่านและกรอกผล Test Step บนมือถือใช้งานได้จริง

## 2.1 Desktop/tablet Step layout

Desktop สามารถคงรูปแบบ grid/table ได้ แต่ต้องปรับเป็น flexible columns:

```text
Step No       40px
Action        minmax(140px, 1fr)
Expected      minmax(140px, 1fr)
Step Result   160–180px
Actual        minmax(180px, 1.2fr)
```

ข้อกำหนด:

- ห้ามให้ Step Result fixed width ดัน Actual Result ออกนอก card
- `step-result-control` ต้องย่อได้
- Actual input ต้องมี `min-width: 0`
- Create Defect ต้องไม่บังคับให้ input ล้น

## 2.2 Mobile Step card

เมื่อ content width ไม่พอ ให้แต่ละ Step แสดงเป็น card แนวตั้ง:

```text
Step 1
Action / Test Data
Expected Result
Step Result
Actual Result / Comment
Create Defect
```

ข้อกำหนด JSX/CSS:

- แต่ละ field ต้องมี visible label หรือ semantic label
- ไม่พึ่ง header table ที่อยู่นอก card
- ไม่ใช้ `nth-child` อย่างเดียวเป็น logic หลักของ mobile layout
- ใช้ class เฉพาะ เช่น `.step-field`, `.step-field-label`, `.step-field-value`
- Step number ต้องเห็นเด่นและอ่านโดย screen reader ได้

Acceptance Criteria:

- ผู้ใช้รู้ได้ทันทีว่าแต่ละค่าคือ Action, Expected, Result หรือ Actual
- ไม่มี cell ซ้อนกัน
- Actual input กว้างเต็มพื้นที่ที่เหลือ
- ปุ่ม Create Defect อยู่ในตำแหน่งที่สัมพันธ์กับ Step ที่ Fail
- ทุก Step card มีระยะห่างและขอบเขตชัดเจน

## 2.3 Step Result controls

บน Desktop:

- แสดง 4 ปุ่ม Pass, Fail, Blocked, Not Run
- active state ต้องเห็นชัด

บน Mobile:

- ปุ่มสูงอย่างน้อย 40–44px
- มีพื้นที่กดเพียงพอ
- ถ้าใช้ icon อย่างเดียวต้องมี `aria-label` และ tooltip/title
- สีต้องไม่เป็นสัญญาณเพียงอย่างเดียว ควรมี icon/label/state outline
- ไม่ให้ปุ่ม 4 ปุ่มถูกบีบจนอ่านหรือกดไม่ได้

## 2.4 Actual Result และ Create Defect

- Fail/Blocked ต้องแสดง required state ชัดเจน
- error ต้องสัมพันธ์กับ input
- input ต้องไม่ถูกบีบโดยปุ่ม Create Defect
- เมื่อสร้าง Defect สำเร็จ ต้องแสดง Defect code ใน Step เดิม
- ระหว่างสร้าง Defect ต้อง disable เฉพาะ action ที่เกี่ยวข้อง

---

# Phase 3 — Queue, History และ Toolbar

ระดับ: สูง  
เป้าหมาย: ทำให้การเลือก Case และดูประวัติยังใช้งานได้เมื่อพื้นที่จำกัด

## 3.1 Execution Toolbar

Desktop:

- Group Cycle, Module, Status และ My Cycles ให้อ่านเป็นกลุ่ม
- Context แสดง Build/Environment/Cycle อย่างชัดเจน

Mobile:

- ทุก select กว้างเต็มและสูงอย่างน้อย 42px
- label อยู่เหนือ control
- ไม่ให้ context ชนกับ filter
- แสดง current Cycle/Build/Environment ใน compact summary

Acceptance Criteria:

- เปลี่ยน Cycle/Module/Status ได้โดยไม่ล้น
- ไม่มี select ถูกตัดด้านขวา
- เมื่อไม่มี Cycle มี empty state ที่บอกวิธีแก้

## 3.2 Case Queue

Desktop:

- คงเป็น panel ด้านซ้าย
- รายการ scroll แยกจาก page

Mobile:

- ใช้ collapsible panel หรือ selector ที่เปิด/ปิดได้
- แสดงจำนวนรายการที่ตรง filter
- Search และ status filter อยู่ด้านบน
- selected case ต้องมี visual state และ accessible state
- รายการต้องแสดง Code, Title และ Status โดย wrap ได้

ห้ามล้าง selected case หรือค่าที่กำลังกรอกเพียงเพราะ queue ถูก collapse

## 3.3 Execution History

Desktop:

- อยู่ด้านขวาหรือด้านล่างตาม breakpoint
- scroll ภายใน panel ได้

Mobile:

- แสดงแต่ละ Run เป็น card หรือ accordion
- Header แสดง Status, Run number และเวลา
- Actual Result/Comment รองรับ wrap และ expand
- Delete action แยกตำแหน่งจาก Run number
- ต้องมี confirmation และ loading state

## 3.4 Case Header

- Test Case code, title, module และ status ต้อง wrap ได้
- บน Mobile status badge ให้อยู่บรรทัดแยกหากพื้นที่ไม่พอ
- ห้าม title ดัน badge ออกนอก card
- Precondition ต้อง wrap และอ่านง่าย

---

# Phase 4 — Preserve Execution Behavior and Accessibility

ระดับ: สูง  
เป้าหมาย: ปรับ UI โดยไม่ทำให้ข้อมูล execution หรือ workflow สูญหาย

## 4.1 Functional regression rules

ต้องคง behavior เดิมของ:

- `saveProgress`
- `completeTest`
- `openSkipModal`
- `confirmSkip`
- `createDefectForStep`
- `removeExecution`
- `calculateOverallResult`

ต้องตรวจว่า resize, breakpoint transition, collapse/expand และ re-render ไม่ล้างค่า:

- `stepStatuses`
- `stepActuals`
- `actual`
- `comment`

## 4.2 Loading/error/empty states

ต้องมี state ที่แยกกันสำหรับ:

- ไม่มี InProgress Test Cycle
- โหลด Cycle ไม่สำเร็จ
- โหลด Workspace ไม่สำเร็จ
- โหลด Test Case detail ไม่สำเร็จ
- ไม่มี Test Case ใน Cycle
- ไม่มี Step ใน Test Case
- กำลังบันทึกผล
- สร้าง Defect ไม่สำเร็จ
- ลบ History ไม่สำเร็จ

ห้ามใช้ `0` หรือหน้าว่างแทน API error โดยไม่มีคำอธิบาย

## 4.3 Accessibility

- ทุก result button มี accessible name
- Input ของแต่ละ Step มี label ที่สัมพันธ์กัน
- Focus ring เห็นชัด
- Keyboard ใช้ tab ผ่าน Step ตามลำดับ
- Modal Skip มี `role="dialog"`, `aria-modal="true"`, title และ focus management
- Error message ผูกกับ field ที่ผิด
- Status active ไม่สื่อด้วยสีอย่างเดียว
- ปุ่ม Delete/Create Defect มีชื่อที่บอก action และรายการเป้าหมาย

## 4.4 Keyboard shortcut

ตรวจและ implement ตาม Screen Specification หากยังไม่มี:

- `P` = Pass
- `F` = Fail
- `B` = Blocked
- `N` = Next Case

ข้อกำหนด:

- ไม่ทำงานขณะ focus อยู่ใน input, textarea หรือ select
- ไม่ทำงานขณะ modal เปิด
- มีคำอธิบาย/shortcut hint ที่ค้นพบได้
- ไม่บันทึกผลทันทีโดยไม่มี confirmation หาก action เป็น finalize

---

# Phase 5 — Code Structure and Shared Styling

ระดับ: ปานกลาง  
เป้าหมาย: ลดความเสี่ยงที่การแก้ responsive ครั้งต่อไปจะกระทบหน้าอื่น

## 5.1 แยก style เฉพาะหน้า

พิจารณาย้าย CSS ของ Execution Workspace ออกจาก `styles.css` ไปยังไฟล์เฉพาะ เช่น `ExecutionWorkspace.css` โดย:

- ไม่เปลี่ยน class ที่หน้าอื่นใช้ร่วมกันโดยไม่ตรวจผลกระทบ
- คง shared token จาก UI Design System
- ใช้ prefix `.execution-` หรือ `.step-` อย่างสม่ำเสมอ

## 5.2 ลด selector ที่พึ่ง DOM position

ควรหลีกเลี่ยง selector เช่น:

```css
.step-row > span:nth-child(3)
.step-row > span:nth-child(4)
```

ให้ใช้ semantic class/data attribute เพื่อป้องกัน JSX เพิ่ม field แล้ว layout พัง

## 5.3 Shared responsive patterns

สร้าง pattern กลางสำหรับ:

- Responsive panel
- Mobile card conversion
- Sticky action bar
- Collapsible queue/history
- Inline error
- Confirmation dialog

---

# Phase 6 — Validation and Release Acceptance

ระดับ: หลังพัฒนาทุก Phase  
เป้าหมาย: ยืนยันว่า UI ใช้งานได้และไม่กระทบ execution data

## 6.1 Required commands

```powershell
cd src/ProMaxx2.QA.Web
npm.cmd run build
npm.cmd run lint
cd ../..
git diff --check
```

## 6.2 Viewport test matrix

ต้องตรวจอย่างน้อย:

- 1440px Desktop
- 1280px Desktop
- 1024px Tablet landscape
- 900px Compact desktop
- 768px Tablet
- 560px Mobile landscape/small tablet
- 390px Mobile
- 360px Mobile

ตรวจทั้งกรณี Sidebar เปิดและ state ที่ content area แคบจริง

## 6.3 Functional test matrix

### Load and navigation

- เลือก InProgress Cycle
- เลือก Test Case จาก Queue
- ค้นหา Test Case
- filter status
- เปลี่ยน Module
- เปลี่ยน Cycle
- เปิด/ปิด Queue บน Mobile
- เปิด/ปิด History บน Mobile

### Step result

- Set All Pass
- Set All Fail
- Set All Blocked
- Set All Not Run
- เปลี่ยนผลทีละ Step
- กรอก Actual Result
- Fail/Blocked โดยไม่กรอก Actual Result
- สร้าง Defect จาก Step ที่ Fail

### Execution submission

- Save Progress ขณะยังมี Not Run
- Complete Test เมื่อครบทุก Step
- Complete Test เมื่อยังมี Not Run
- Complete Test เมื่อ Fail/Blocked ไม่มี Actual Result
- Skip โดยไม่เลือก Reason
- Skip พร้อม Reason และ Comment
- ตรวจ Overall Result ที่คำนวณจาก Step

### History

- แสดง Run เดิม
- เปิดอ่านข้อความยาว
- ลบ Run
- ยกเลิก confirmation
- API ลบไม่สำเร็จ

## 6.4 Visual acceptance criteria

- ไม่มีข้อความทับกัน
- ไม่มี input/button ล้น card
- ไม่มี column ถูกตัดโดยไม่สามารถอ่านข้อมูลได้
- ไม่มี horizontal scroll ระดับหน้า
- Step card มี label ครบ
- action bar ไม่บัง content
- sticky element ไม่ทับ keyboard/input บน mobile
- สีและสถานะยังตรงกับ Design System

## 6.5 Functional safety acceptance criteria

- ค่า form ไม่หายเมื่อ resize
- ค่า form ไม่หายเมื่อ collapse/expand panel
- ค่า saved ล่าสุดถูก restore เมื่อเปลี่ยน Case
- API payload ยังมี step result ครบทุก Step
- Defect link และ Defect code ยังทำงานเหมือนเดิม
- History delete ยังทำงานตาม permission/confirmation
- ไม่เกิด regression กับ Dashboard, Test Summary, Defect หรือ Sign-off

## 6.6 Definition of Done

- Phase ที่ทำเสร็จผ่าน Acceptance Criteria ของตัวเองครบ
- ผ่าน viewport test matrix
- ผ่าน functional test matrix
- `npm.cmd run build` ผ่าน
- `npm.cmd run lint` ผ่าน หรือ warning ใหม่มีการบันทึกเหตุผล
- `git diff --check` ผ่าน
- อัปเดต `UI_DESIGN_SYSTEM.md` หากเกิด responsive pattern ใหม่
- อัปเดต `SCREEN_SPECIFICATION.md` หาก behavior หรือ layout เปลี่ยนจาก specification เดิม
- มีหลักฐานการทดสอบ Desktop/Mobile สำหรับส่งมอบ

## ลำดับพัฒนาที่แนะนำ

1. Phase 1: ป้องกัน layout overflow และแก้ breakpoint
2. Phase 2: เปลี่ยน Step layout บน Mobile
3. Phase 3: ปรับ Queue, History, Toolbar และ Case Header
4. Phase 4: ตรวจ functional safety และ accessibility
5. Phase 5: จัดโครงสร้าง CSS/component ให้ดูแลต่อได้
6. Phase 6: ทดสอบและยืนยัน release
