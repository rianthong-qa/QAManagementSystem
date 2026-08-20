# ProMaxx2 QA Hub — UI Design System

> Dashboard share rule (13 สิงหาคม 2026): ลิงก์แชร์ใหม่ต้องใช้ short code แบบสุ่ม 8 ตัวใน `?s=` และเก็บ scope/วันหมดอายุในฐานข้อมูลเพื่อให้ใช้ต่อได้หลัง restart; endpoint token แบบเดิมต้องยังเปิดอ่านได้เพื่อ backward compatibility

> Dashboard Module Health rule (13 สิงหาคม 2026): ต้องแสดง Module เป็น Tree และเรียง Root/Child ด้วย `ParentModuleId`, `SortOrder`, `ModuleCode` ชุดเดียวกับหน้า Modules ห้ามเรียงด้วยชื่อ Module แยกต่างหาก

> Test Suite rule (13 สิงหาคม 2026): หน้า Test Suite ต้องใช้ Project Context, กรอง Project/Type/Risk/Active ได้, เปิดดูรายละเอียดแบบ read-only, จัด Test Case พร้อมค้นหา/ตัวกรอง/Select All, ระบุ Required หรือ Optional, ปรับลำดับ, ตรวจ API error, แสดงจำนวน Test Cycle ที่อ้างอิง และเปลี่ยนตารางเป็น card บน Mobile

> RTM Module dropdown (13 สิงหาคม 2026): ต้องเรียงแบบ Tree ตาม `ParentModuleId` และ `SortOrder` เหมือนหน้า Modules โดยแสดง Module Code, indentation และสัญลักษณ์กิ่งเพื่อแยก Root/Child ชัดเจน

> RTM rule (13 สิงหาคม 2026): หน้า RTM ต้องอ้างอิง Project/Release ที่ผู้ใช้เลือก, แสดง KPI Covered/Partial/Not Covered, กรอง Module/Requirement Status/Coverage ได้, เปิดดู Requirement และ Test Case แบบ read-only modal, จัดการ Direct/Indirect Link ตามสิทธิ์, Export CSV และเปลี่ยนตารางเป็น card บน Mobile

> สถานะ: **UI Single Source of Truth**
> อัปเดตล่าสุด: 13 สิงหาคม 2026
> ขอบเขต: Web frontend ทั้งหมดใน `src/ProMaxx2.QA.Web`

เอกสารนี้เป็นหลักสำหรับการออกแบบ สร้าง และแก้ไข UI ทุกหน้า หากโค้ดเดิมไม่สอดคล้องกับเอกสารนี้ ให้ปรับโค้ดเข้าหาเอกสาร เว้นแต่ requirement ใหม่ระบุเป็นอย่างอื่นอย่างชัดเจน ทุกครั้งที่มีการเปลี่ยนแปลง UI ต้องอัปเดตหัวข้อที่เกี่ยวข้องและ Change Log ในไฟล์นี้ในงานเดียวกัน

## 1. เป้าหมายการออกแบบ

- หน้าจอกระชับ อ่านง่าย และใช้งานได้โดยไม่เกิด horizontal scroll โดยไม่จำเป็น
- Desktop ใช้พื้นที่แนวนอนอย่างเหมาะสม ส่วน Mobile เน้น touch target และลำดับข้อมูลแนวตั้ง
- ฟอร์มเพิ่ม/แก้ไขทุกเมนูต้องมีรูปแบบและพฤติกรรมเดียวกัน
- สี สถานะ ปุ่ม และ feedback ต้องสื่อความหมายเหมือนกันทั่วระบบ
- UI ต้องรักษาฟังก์ชัน validation, permission, audit และ business workflow เดิม

## 2. Technology และไฟล์หลัก

- Framework: React + TypeScript + Vite
- Component หลัก: `src/ProMaxx2.QA.Web/src/App.tsx`
- Global design system: `src/ProMaxx2.QA.Web/src/styles.css`
- Test Case และ Test Step UI: `src/ProMaxx2.QA.Web/src/TestManagement.css`
- Stylesheet เฉพาะหน้า: `App.css`, `Dashboard.css`, `DashboardExecutive.css`, `DragDrop.css`, `ReleaseBuild.css`
- ห้ามเพิ่ม style ใหม่ใน `Login.css`; ไฟล์นี้เป็น legacy และไม่ได้เป็น stylesheet หลักของแอป

ลำดับตรวจสอบหลังแก้ UI:

```powershell
cd src/ProMaxx2.QA.Web
npm.cmd run build
npm.cmd run lint
git diff --check
```

## 3. Design Tokens

| Token | ค่า | การใช้งาน |
|---|---:|---|
| `--bg` | `#f5f7fb` | พื้นหลังแอป |
| `--card` | `#ffffff` | Card และ Modal |
| `--text` | `#1f2937` | ข้อความหลัก |
| `--muted` | `#667085` | คำอธิบายและ metadata |
| `--line` | `#e5e7eb` | Border และ divider |
| `--primary` | `#2457d6` | Primary action และ focus |
| `--green` | `#169c63` | Pass/Success |
| `--yellow` | `#d79a00` | Warning/Pending/Blocked |
| `--red` | `#d64545` | Fail/Danger/Delete |

หลักการทั่วไป:

- Border radius ของ input/button: 8–10px
- Border radius ของ card/modal: 14–16px
- Focus ring: primary โปร่งใส 10–20% ขนาด 3px
- Card shadow ต้องเบา ไม่บดบังเส้นแบ่งข้อมูล
- Font หลัก: `Tahoma, "Noto Sans Thai", Arial, sans-serif`

## 4. Application Shell

- Desktop ใช้ Sidebar ทางซ้ายและ Topbar แบบ sticky
- Mobile ที่ความกว้างไม่เกิน 800px ซ่อน Sidebar ไว้นอกจอและเปิดด้วย menu button
- Content padding: Desktop ประมาณ 24–28px, Mobile ประมาณ 16–18px
- Page header แสดงชื่อหน้าและ action; Mobile เปลี่ยนเป็นแนวตั้ง
- ตารางที่มีหลายคอลัมน์อนุญาตให้เลื่อนเฉพาะ container ของตาราง ห้ามทำให้ทั้งหน้าเลื่อนแนวนอน

## 5. Screen Inventory

| กลุ่ม | หน้า | รูปแบบหลัก |
|---|---|---|
| Overview | Dashboard | KPI, charts, module/user summary |
| Overview | Project / Module | Card/list และ hierarchical module |
| Overview | Release / Build | Release และ build management |
| Test Design | Requirement | Table, create/edit form, status |
| Test Design | RTM | Traceability matrix |
| Test Design | Test Case | Table, compact form, step editor |
| Test Design | Test Suite | Table, compact form, case assignment |
| Execution | Test Cycle | Table, cycle form, suite population |
| Execution | Execution Workspace | Queue, execution detail, history |
| Execution | Defect | Table และ create/edit defect form |
| Execution | Regression | Regression overview |
| Governance | Test Summary | Reporting summary |
| Governance | Risk Acceptance | Risk review/approval |
| Governance | Release Sign-off | Gate และ sign-off |
| Administration | User / Role | User, role และ permission grid |
| Administration | การตั้งค่ากลาง | Master data และ Environment ที่จัดเก็บในฐานข้อมูล |
| Administration | System Monitor | API/Database health, allowlisted Windows Service status และ privileged Start/Restart actions |
| Administration | Audit Log | Audit table/filter |

## 6. Card, Table และ Status

### Card

- พื้นหลังขาว, border `--line`, radius 14px
- Padding ปกติ 16–20px
- Card title ต้องมีชื่อ, คำอธิบายสั้น และ action ทางขวาเมื่อมี

### Table

- Header ใช้พื้นหลังเทาอ่อนและข้อความ muted
- Cell ต้องไม่หนาแน่นเกินไป; padding ประมาณ 10–12px
- Action ของ row อยู่คอลัมน์ขวาสุด
- ข้อมูลที่ยาวใช้ ellipsis หรือ wrap ตามความเหมาะสม

### Badge และสถานะ

- Pass/Ready/Success: เขียว
- Fail/Critical/Danger: แดง
- Pending/Blocked/Draft: เหลือง
- Informational/Code/Count: น้ำเงิน
- Badge ต้องไม่ wrap และต้องมีข้อความ ไม่สื่อด้วยสีอย่างเดียว

## 7. Form Controls

### Textbox และ Textarea

- ข้อความที่กรอก, placeholder, disabled และ read-only ใช้ `font-weight: 400`
- Label ใช้น้ำหนัก 600–700 ได้ แต่ค่าที่กรอกต้องไม่เป็นตัวหนา
- Input สูงประมาณ 38–42px; textarea เริ่มต้นประมาณ 62px และ resize แนวตั้งได้
- Disabled ใช้พื้นหลังเทาอ่อนและยังต้องอ่านค่าได้
- Field ทุกชนิดต้องมี focus state ชัดเจน

### Select

- ใช้ขนาดและ border เดียวกับ textbox
- รองรับชื่อไทยยาวโดยไม่ดัน container ล้น
- Hierarchical option ใช้ indentation และสัญลักษณ์ `└`

### Checkbox และ Radio

- ขนาดมาตรฐาน `18 × 18px` และห้ามขยายเต็มความกว้าง
- Checkbox เมื่อเลือก: พื้นหลัง primary พร้อมเครื่องหมาย `✓` สีขาวเต็มรูป
- Radio เมื่อเลือก: จุด primary อยู่กึ่งกลาง
- Label และ control อยู่แนวเดียวกัน โดยมี gap ประมาณ 8px
- ต้องมี hover, focus และ disabled state ที่เห็นชัด
- Role/Permission card เปลี่ยน border/background เพิ่มเติมเมื่อ selected

### Validation

- Required mark และข้อความผิดพลาดใช้ `--red`
- ปุ่มบันทึก disabled เมื่อข้อมูลบังคับไม่ครบ
- Error message ต้องอยู่ใกล้ field และไม่พึ่ง alert เพียงอย่างเดียวเมื่อทำได้

## 8. Button Standard

- Primary: พื้นหลัง `--primary`, ข้อความขาว
- Secondary: พื้นหลังขาวหรือฟ้าอ่อน, border `--line`
- Danger: แดงหรือแดงอ่อนตามระดับความรุนแรง
- Table action ใช้ปุ่มขนาดเล็ก แต่ touch target บน Mobile ต้องกดง่าย
- Disabled ลด opacity และเปลี่ยน cursor
- ปุ่มยกเลิกอยู่ซ้ายของปุ่มบันทึกเสมอ

## 9. Create / Edit Modal Standard

มาตรฐานนี้ใช้กับฟอร์มเพิ่ม/แก้ไข **ทุกเมนู**

### Desktop

- Modal ทั่วไปกว้างสูงสุด 900px
- Test Case และ Test Suite ที่มี editor ซับซ้อนกว้างสูงสุด 1040px
- ความสูงสูงสุด 92vh และเลื่อนเฉพาะแนวตั้ง
- ห้ามมี horizontal scrollbar
- Pattern: `<div className="modal">` (backdrop, blur 6px, dark overlay) → `<div className="modal-box">` (white box, centered, 16px radius, strong shadow)
- Header ใช้ `.modal-head` (border-bottom 2px, h2 20px/800, ปุ่ม close มี border)
- Action footer ใช้ `.modal-actions` (border-top 2px, ปุ่ม primary มี shadow)
- Form ใช้ grid 2 คอลัมน์เป็นค่าเริ่มต้น
- Test Case ใช้ grid 4 คอลัมน์; field สำคัญ span 2 คอลัมน์
- Full-width field ใช้สำหรับ description, change reason หรือข้อมูลยาว

### Mobile (≤ 760px)

- Modal เปิดเต็มหน้าจอ: `100% × 100dvh`
- ไม่มี border radius
- Form ทั่วไปเรียงหนึ่งคอลัมน์
- Header และ footer sticky
- Footer รองรับ `env(safe-area-inset-bottom)`
- ปุ่มยกเลิกและบันทึกแบ่งพื้นที่เท่ากัน
- Inline form และ multi-column selector เปลี่ยนเป็นแนวตั้ง

### Modal เฉพาะทาง

- Requirement editor: ใช้มาตรฐาน modal ทั่วไป
- Test Case editor: compact 4-column desktop และ full-screen mobile
- Test Suite editor: 1040px, assignment columns 2 ฝั่ง; Mobile เหลือ 1 ฝั่ง
- Test Cycle editor: 900px; inline create ต้อง stack บน Mobile
- Defect และ modal อื่น: ใช้มาตรฐาน 900px/2 columns

## 10. Test Case Editor

- Desktop: Project/Module, Code/Title จัดคู่; Priority/Type/Status/Automation จัดแถวกระชับ
- Objective และ Preconditions แสดงคู่กัน
- Test Steps แสดงแบบตาราง: ลำดับ, Action, Test Data, Expected Result, Action
- Input ใน step ต้อง `min-width: 0` เพื่อป้องกัน overflow
- Header และปุ่มบันทึก sticky
- Mobile: Step แต่ละรายการเปลี่ยนเป็น card; input เรียงแนวตั้งและปุ่มลบอยู่ท้าย card
- ห้ามตัด field, action หรือ expected result ออกจาก mobile flow

## 11. Execution Workspace

- Toolbar แสดง Test Cycle, Build, Environment และสถานะ
- Overview แสดง Total, Passed, Failed, Blocked, Not Run และ progress
- Desktop ใช้ 3 ส่วน: Test Case Queue, Execution Detail, History
- Queue รองรับค้นหาด้วย code/title และกรองสถานะ
- Selected case ต้องเห็นชัดด้วยพื้นหลังและเส้น primary
- Test Steps รองรับตั้งผลทั้งหมดเป็น Pass, Fail หรือ NotRun
- Result action มี Pass, Fail, Blocked และ Skipped พร้อมสีตามความหมาย
- Responsive: History ย้ายลงแถวใหม่ก่อน และทุกส่วนเรียงหนึ่งคอลัมน์บน Mobile

## 12. Responsive Breakpoints

| Breakpoint | การใช้งาน |
|---:|---|
| 1450px | ลดความกว้าง execution columns |
| 1100px | Execution history ลงแถวใหม่ |
| 800px | App shell เป็น Mobile navigation |
| 760px | Modal เต็มหน้าจอและ form stack |
| 420px | Test Case compact fields เหลือหนึ่งคอลัมน์ |

ห้ามออกแบบเฉพาะ viewport เดียว อย่างน้อยต้องตรวจที่ประมาณ 1440px, 1024px, 768px และ 390px

## 13. Accessibility และ UX Rules

- Input ที่ไม่มี label มองเห็นได้ต้องมี `aria-label`
- ใช้ semantic `button`, `label`, `input`, `select`, `textarea`
- Keyboard focus ต้องมองเห็นได้
- ห้ามใช้สีเพียงอย่างเดียวในการบอกสถานะ
- Confirm ก่อน operation ที่สร้าง historical record หรือลบข้อมูล
- Loading และ disabled state ต้องป้องกันการ submit ซ้ำ
- ข้อความไทยต้องบันทึกเป็น UTF-8 และห้ามมี mojibake

## 14. UI Change Workflow

ทุกงาน UI ต้องทำตามลำดับนี้:

1. อ่านไฟล์นี้ก่อนแก้ไข
2. ตรวจ component และ stylesheet ที่มีอยู่ หลีกเลี่ยง style ซ้ำ
3. ใช้ token และ component pattern ในเอกสารนี้
4. ตรวจ Desktop และ Mobile
5. รัน build, lint และ `git diff --check`
6. อัปเดตเอกสารนี้หากเกิด pattern, component หรือข้อกำหนดใหม่
7. เพิ่มรายการใน Change Log ด้านล่าง

## 15. Change Log

### 2026-08-20

- Execution Toolbar responsive: `.execution-toolbar` ใช้ `flex-wrap: wrap` และ `gap: 14px 20px` เพื่อให้ controls ลงตัวบนทุกขนาดหน้าจอ; `.execution-toolbar select` ลด `min-width` จาก 340px เหลือ 180px; เพิ่ม `.execution-lock-note` สำหรับแสดงข้อความล็อค execution พร้อมไอคอน ⛔
- เพิ่ม `.status-dot.skipped` สำหรับแสดงสถานะ Skipped ด้วยสี `#98a2b3`
- เพิ่ม `.case-module` class สำหรับแสดงชื่อ module ขนาดเล็กใน execution queue
- Dashboard Module Tree Hierarchy: เพิ่ม class สำหรับแสดง module เป็น tree hierarchy ในหน้า Dashboard ได้แก่ `.module-overview-head`, `.module-overview-total`, `.module-card.tree-parent`, `.module-card.child`, `.tree-toggle`, `.module-child-dot`, `.module-metrics`, `.module-bar-row`, `.module-bar-legend`, `.dashboard-two-col`; tree toggle หมุน 90 องศาเมื่อเปิด; child card มี left border dashed เพื่อแสดงระดับ; responsive `.dashboard-two-col` เป็น 1fr บน ≤900px
- DashboardExecutive.css เพิ่ม `.module-card.tree-parent`, `.module-card.child`, `.module-child-dot`, `.module-metrics`, `.module-bar-row`, `.module-bar-legend` สำหรับ module tree และ `.severity-grid`, `.severity-pill` สำหรับ severity distribution display
- Defect Management Page: เพิ่ม CSS classes สำหรับหน้าจัดการ Defect ทั้งหมด ได้แก่ `.defect-page`, `.defect-toolbar`, `.defect-summary`, `.defect-table`, `.defect-detail`, `.defect-activities`, `.defect-form`, `.severity-badge`, `.status-badge`; ใช้ modal pattern 标准 900px/2 columns; responsive: summary ลดเป็น 2 columns บน ≤900px และ 1 column บน ≤600px; ตารางเปลี่ยนเป็น card layout บน Mobile ด้วย data-label

### 2026-08-13

- กำหนด UI Design System เป็น Single Source of Truth
- ปรับ Execution Workspace: overview, filter/search queue, bulk step status และ responsive layout
- ปรับ Test Case create/edit เป็น compact 4-column desktop และ full-screen mobile
- แก้ Modal Desktop ให้แสดงครบและไม่มี horizontal scrollbar
- ทำ create/edit modal ทุกเมนูให้ใช้ header/footer sticky และ responsive pattern เดียวกัน
- กำหนด checkbox/radio ขนาด 18px และแก้ checked icon ให้แสดงเครื่องหมายถูกชัดเจน
- กำหนดข้อความใน textbox/textarea เป็น `font-weight: 400` ทั่วระบบ
- เพิ่มหน้าการตั้งค่ากลางสำหรับ Release Type, Test Case Priority/Type, Test Suite Type/Risk Tier, Test Cycle Type และ Environment
- Dropdown ที่เกี่ยวข้องต้องอ่านค่าจาก Master Settings API และห้ามประกาศ option แบบ hard-code ใน component
- Master data ใช้การเปิด/ปิดใช้งานแทน hard delete เพื่อรักษาความหมายของข้อมูลประวัติ
- หน้าการตั้งค่ากลางจัดข้อมูลตามเมนูหลัก 4 กลุ่ม โดยค่าที่มีเจ้าของเมนูเดียวกันต้องอยู่ในการ์ดเดียวกัน เช่น Test Case รวม Priority และ Type และ Test Cycle รวม Cycle Type กับ Environment
- การตั้งค่าแต่ละประเภทรองรับเพิ่ม แก้ไข และลบจากส่วนของตัวเองด้วย inline editor; การลบ Environment ที่มีการอ้างอิงต้องถูกป้องกันและแจ้งให้ปิดใช้งานแทน
- หน้าการตั้งค่ากลางต้องแสดงข้อผิดพลาดเมื่อโหลด API ไม่สำเร็จ ห้ามแทนข้อผิดพลาดด้วยรายการว่างหรือ `0 Active` โดยไม่มีคำอธิบาย
- Mobile app shell ต้องเปลี่ยน `.app` เป็นคอลัมน์เดียวจริง ไม่สงวนความกว้าง Sidebar ที่ถูกซ่อน; Topbar แสดง menu, Project context, avatar และปุ่ม Logout แบบกะทัดรัด โดยซ่อนชื่อผู้ใช้เพื่อไม่ให้ล้นหน้าจอ
- หน้า Requirement ต้องใช้ฟิลด์ชุดเดียวกันในฟอร์มเพิ่มและแก้ไข ได้แก่ Project/Module/Release, Title, Description, Priority, Risk, Source, Owner, In Scope และ Acceptance Criteria; ตัวกรอง Status/Priority/Scope ต้องกรองข้อมูลได้จริง และต้องเปิดดู Revision History ได้จากแต่ละรายการ
- ตาราง Requirement บนหน้าจอไม่เกิน 760px ต้องเปลี่ยนแต่ละแถวเป็น card แนวตั้งพร้อม label ของข้อมูล ห้ามบังคับให้ผู้ใช้เลื่อนทั้งหน้าในแนวนอน
- หน้า System Monitor แสดง API, Database และ Managed Services เป็น card/status list; Start/Restart ต้องมี confirmation, loading/disabled state, แสดงผลผิดพลาด และต้องไม่รับชื่อ Service อิสระจากผู้ใช้ โดยแสดงเฉพาะ allowlist จาก Server configuration
- เมื่อ Windows ปฏิเสธการควบคุม Service หน้า System Monitor ต้องแจ้งวิธีให้สิทธิ์เฉพาะ Service อย่างชัดเจน ห้ามแสดงข้อความดิบ `[SC] OpenService FAILED 5` เพียงอย่างเดียว
- ช่อง Status ของ Requirement ต้องมี Information แบบกะทัดรัดที่อธิบาย Draft, Review, Approved, Implemented และ Cancelled พร้อมผลต่อการใช้งาน โดยต้องย้ำว่า RTM/Coverage พิจารณา `In Scope` แยกจาก Status; รายละเอียดทั้งหมดเปิด/ปิดได้และสถานะปัจจุบันต้องเด่นชัด
- หน้า Requirement ต้องกรองตาม Module ได้จริง โดย option ระบุ Project และ Module เพื่อป้องกันชื่อซ้ำ; Requirement ID ต้องเป็น interactive link ที่เปิด read-only detail modal แยกจาก edit modal และแสดงบริบท, metadata, Description, Acceptance Criteria และคำอธิบาย Status ครบถ้วน
- Dropdown Module ของหน้า Requirement ต้องจัดกลุ่มด้วย Project และเรียง Module แบบ Tree ตาม `ParentModuleId`/`SortOrder`; Root และ Child ต้องมีสัญลักษณ์กิ่งกับ indentation ที่แยกระดับได้ชัดเจนเหมือนหน้า Modules
- หน้า Test Case ต้องกรอง Project/Module แบบ Tree/Priority/Type/Status/Automation ได้, เปิด Test Case ID เป็น read-only detail modal ที่แสดง Owner, Requirement linkage, Steps และ Revision History, รองรับ Clone และ Import CSV/XLSX พร้อมปุ่มดาวน์โหลด `.xlsx` Template ของ Project ที่เลือกซึ่งมีแถวตัวอย่าง คำแนะนำ และรายการ Module Code ที่เปิดใช้งาน, ใช้ confirmation modal แทน browser confirm, มี pagination/error/retry feedback และเปลี่ยนตารางเป็น card ที่ไม่เลื่อนทั้งหน้าบน Mobile
- หน้า Requirement แยกปุ่ม `AI Generate` ออกจาก `สร้าง Requirement` ปกติ; AI เปิด modal เฉพาะสำหรับเลือก Project/Module/Release รับคำอธิบายภาษาธรรมชาติ และแนบไฟล์อ้างอิงได้ไม่เกิน 5 ไฟล์รวม 20 MB โดยแสดงชื่อ/ขนาดและลบก่อนส่งได้ ไฟล์ต้องใช้วิเคราะห์ในคำขอเท่านั้นและไม่บันทึกลงฐานข้อมูลหรือดิสก์ จากนั้นจึงส่ง Draft ไปเติมฟอร์มสร้างปกติใน Title, Description, Acceptance Criteria, Priority, Risk และ Source ผลลัพธ์ AI ต้องให้ผู้ใช้ตรวจแก้และกดบันทึกเอง, API key ต้องอยู่ฝั่ง Server เท่านั้น และ UI ต้องมี loading/error/configuration feedback ที่ชัดเจน
- หน้า Test Case แยกปุ่ม `AI Generate` ออกจาก `+ Test Case` ปกติ และใช้ modal รูปแบบเดียวกับ Requirement สำหรับเลือก Project/Module รับคำอธิบายและไฟล์อ้างอิงสูงสุด 5 ไฟล์รวม 20 MB; ผลลัพธ์ต้องเป็น Draft ที่เติม Title, Objective, Preconditions, Priority, Type, Automation Candidate และ Test Steps ลงฟอร์มเพิ่มปกติ โดยผู้ใช้ต้องตรวจแก้และกดบันทึกเอง ไฟล์ใช้ในหน่วยความจำเฉพาะคำขอและห้ามบันทึกลงระบบ
- Toolbar หน้า Test Case ต้องแยกส่วนสรุปจำนวน/ปุ่มทำงานออกจากกรอบตัวกรอง, ปุ่ม AI/Template/Import/Add ต้องสูงเท่ากันและไม่ตัดคำบน Desktop; ตัวกรองใช้ responsive grid และบน Mobile ปุ่มกับตัวกรองต้องลดเป็น 2 คอลัมน์และ 1 คอลัมน์ตามความกว้างโดยไม่ทำให้หน้าเกิด horizontal scroll
- App navigation ต้องเก็บเมนูที่ Active ไว้ใน URL hash รูปแบบ `#/page-id` และ `localStorage` เพื่อให้ Browser Refresh/Reload กลับมาหน้าเดิม รวมถึงรองรับการเปลี่ยน hash จาก browser history; ลิงก์ Dashboard แบบ Share และหน้า Login ต้องไม่ถูกรบกวนจากการคืนค่าเมนูนี้
- Topbar ต้องจัด Project/Release/Build เป็น context fields ที่มี label ชัดเจน, แยกสถานะ Blocker และข้อมูลผู้ใช้ออกจาก context ด้วยเส้นแบ่ง, ใช้สถานะสีเขียว/ส้มแทนจุดแจ้งเตือนที่ไม่มีความหมาย และบน Mobile แสดงเฉพาะ Project, Avatar กับปุ่ม Logout แบบ icon เพื่อรักษาพื้นที่โดยไม่เกิด horizontal scroll
- Topbar Project context ต้องส่งให้ทุกหน้าที่มี data scope ระดับ Project (Requirement, Test Case, Test Suite, Test Cycle, Execution, Defect); เมื่อผู้ใช้เปลี่ยน Project ใน Topbar หน้าเหล่านี้ต้อง sync filter ของตัวเองตาม (เช่น ล้าง Module filter, เปลี่ยน Project filter, กรองรายการ) โดยไม่บังคับให้ผู้ใช้ต้องเลือกซ้ำในแต่ละหน้า
- หน้า Release / Build ต้องรับ `contextProjectId` จาก Topbar และกรอง Release list ตาม Project ที่เลือก; เมื่อเปลี่ยน Project ต้องเลือก Release แรกของ Project นั้นโดยอัตโนมัติหาก Release ที่เลือกอยู่คนละ Project
- หน้า Requirement ต้องกรองข้อมูลตาม `contextProjectId` จาก Topbar ในส่วน `filtered` computation เพื่อให้แสดงเฉพาะ Requirement ของ Project ที่เลือก
- App shell ต้องมีความสูง `100dvh` และให้พื้นที่ `<main>` เป็น scroll container แนวตั้งเพียงส่วนเดียว โดย Topbar ใช้ `position: sticky; top: 0` ภายในพื้นที่นี้ เพื่อให้ Topbar ค้างตลอดการเลื่อนทั้ง Desktop/Mobile โดยไม่บังเนื้อหาและไม่ทำให้ Body เลื่อนซ้อนกัน
- ตัวกรอง Module หน้า Test Case ต้องโหลด Module ตาม Project ที่เลือกในตัวกรองโดยแยกจาก state ของฟอร์มเพิ่ม/แก้ไข; เมื่อเลือกทุก Project ต้องรวมข้อมูลทุก Project และจัดกลุ่มด้วย Project Code/Name จากนั้นเรียง Tree ด้วย `ParentModuleId`, `SortOrder`, `ModuleCode` ชุดเดียวกับหน้า Modules พร้อม indentation แสดงระดับ Root/Child
- Modal `จัดการ Test Case Link` ในหน้า RTM ต้องมีตัวกรอง Module แบบ Tree ก่อนช่องเลือก Test Case, เริ่มต้นด้วย Module ของ Requirement, แสดงจำนวน Test Case ที่ Link ได้ และต้องล้าง Test Case ที่เลือกเมื่อเปลี่ยน Module เพื่อป้องกันการเชื่อมโยงรายการนอกตัวกรอง; Root Module แสดงตัวหนาและ Child ใช้ indentation ตามหน้า Modules
- หน้า Test Suite แยกปุ่ม `AI Generate` จาก `สร้าง Test Suite`; AI modal ต้องเลือก Project/Module แบบ Tree แล้วให้ Server โหลด Requirement ที่ In Scope และ Test Case ที่ไม่ Deprecated ของ Module เพื่อสร้าง Draft ชื่อภาษาไทย, Type/Risk จากค่าการตั้งค่ากลาง และรายการ Test Case Required/Optional พร้อมเหตุผล ผู้ใช้ต้องตรวจแก้/นำ Case ออกได้ก่อนบันทึก และเมื่อบันทึกจึงสร้าง Suite พร้อมกำหนด Test Case อัตโนมัติ โดย API key อยู่ฝั่ง Server และคำขอ Responses API ต้องตั้ง `store: false`
- หน้าการตั้งค่ากลางต้องมีการ์ด `AI Configuration` เต็มความกว้างสำหรับ Provider, Model, API key และสถานะเปิดใช้งานร่วมกันของ Requirement/Test Case/Test Suite; API key ต้องเก็บแบบเข้ารหัสฝั่ง Server ห้ามส่งค่าจริงกลับ Browser ช่องคีย์ว่างขณะบันทึกต้องคงค่าเดิม และฟอร์มต้องลดจากหลายคอลัมน์เป็นคอลัมน์เดียวบน Mobile โดยไม่เกิด horizontal scroll
- เมนูการตั้งค่ากลางใช้ชื่อ `Setting Center`; กลุ่ม Release, Test Case, Test Suite และ Test Cycle ต้องเริ่มต้นแบบย่อและกดหัวการ์ดเพื่อพับ/ขยายได้ โดยแสดงจำนวน Active ในสถานะย่อ การ์ดที่ขยายใช้เต็มความกว้างเพื่อลดความแน่นของข้อมูล และ Mobile ซ่อนข้อมูลสรุปที่ไม่จำเป็นโดยยังคงชื่อหมวดกับสถานะการพับ/ขยายที่ชัดเจน
- ช่อง Provider, Model และ API key ใน `AI Configuration` ต้องใช้ input pattern เดียวกับฟอร์มมาตรฐาน แม้ element จะไม่ได้ระบุ `type="text"`; ช่อง read-only ใช้พื้นหลังเทาอ่อน และ focus ต้องแสดง primary ring ชัดเจน
- `AI Configuration` รองรับ OpenAI, Google Gemini, Anthropic Claude และ AI Local แบบ OpenAI-compatible; Provider ใช้ select, Model เลือกค่าที่แนะนำหรือพิมพ์ Model ID เองได้, เมื่อเปลี่ยน Provider ต้องกรอก API key ใหม่ และ AI Local ต้องแสดง Base URL พร้อมอนุญาตให้เว้น API key ได้ ฟอร์มต้องปรับคอลัมน์ตามพื้นที่และเรียงเป็นคอลัมน์เดียวบน Mobile
- ช่อง Model ใน `AI Configuration` ต้องโหลดรายการ Model ทั้งหมดที่บัญชีเข้าถึงได้จาก Models API ของ Provider ที่เลือก แสดงจำนวนรายการและ loading/error feedback โดยไม่ล้าง Model ID ที่กรอกอยู่; หากยังไม่มี API key หรือติดต่อ Provider ไม่ได้ ผู้ใช้ยังต้องพิมพ์ Model ID เองได้

### 2026-08-18

- Dashboard Premium Design V2: Hero section ปรับเป็น dark gradient premium (`#071a33 → #123b77 → #2563eb → #7c3aed`) พร้อม decorative circles, eyebrow badge แบบ glass effect, hero-badges แสดงสถิติสำคัญ (Requirement/Test Case/Execution/Pass Rate/Critical/P0), hero-side แสดง Total Test Cases ขนาดใหญ่ทางขวา, overall-score card ย้ายไปอยู่ระหว่าง hero-side กับ decision badge, decision badge ปรับเป็น semi-transparent บน dark background; KPI grid ปรับเป็น 5 คอลัมน์ fixed พร้อม glass-morphism cards (backdrop-filter blur, subtle shadow), corner gradient accent, uppercase label; Cards ใช้ glass-morphism effect (rgba background, backdrop-filter); Chart grid ปรับ user-bar/donut ให้ premium; module-health-card ปรับ metric-bar เป็น gradient; Module tree expand/collapse button ใช้สี `#315b96` บน `#eaf2ff`; Responsive: hero-side ซ่อนบน ≤900px, hero stacking, KPI force 1fr บน ≤760px, donut/legend/user-bar ปรับขนาดตาม breakpoint
- Dashboard.css ย้าย hero/eyebrow/decision/KPI-override ทั้งหมดไป DashboardExecutive.css เพื่อลด specificity conflict; Dashboard.css เหลือแค่ chart-grid และ module-health mobile card layout
- Dashboard Section Additions (Design V2 alignment): เพิ่ม 3 sections ใหม่ตาม `Dashboard_UIV2.html` — (1) **Module Overview** — card grid แสดง top-level modules พร้อม index number, ชื่อ, test cases, coverage%; (2) **Capacity Assumption + QA Performance** — 2-column grid แสดงสมมติฐาน 8h/2h support/6h effective/+35% retest พร้อม QA performance cards ที่มี progress bar; (3) **Module Workload Distribution** — donut chart แสดงสัดส่วน test case ตาม module พร้อม bar chart ราย module; CSS เพิ่ม class ใหม่: `.module-sequence`, `.module-card`, `.module-index`, `.grid2`, `.assump-grid`, `.assump`, `.callout`, `.qa-list`, `.qa-card`, `.qa-icon`, `.qa-progress`, `.module-share-grid`, `.donut-chart-lg`, `.donut-hole-lg`, `.donut-caption`, `.module-pct-list`, `.module-pct-row`, `.pct-name`, `.pct-bar`, `.pct-value`; Responsive: `.grid2` → 1fr บน ≤900px, `.module-share-grid` → 1fr บน ≤1100px, `.assump-grid` → 2 columns บน ≤900px/≤420px, `.module-sequence` → 1fr บน ≤760px
- Dashboard Restructure (Design alignment): ลบ Capacity Assumption, Performance by User, Module Health ออกทั้งหมด; เปลี่ยน Module Health (table ซ้อน nested) เป็น **Module Effort Summary** (flat table) ที่แสดง #, Module, Test Cases, Coverage, Execution, Pass Rate, Fail, Blocked, Health; เพิ่ม **Risks & Blockers** section ที่ derive จาก defects data (Critical/P0/P1/High, low coverage modules, low requirement coverage) พร้อม green "No Critical Risks" card เมื่อไม่มี; ลบ `collapsedModules` state, `moduleRows`, `appendModules` ที่ใช้กับ Module Health เดิม; Layout ใหม่: Hero → KPI → Module Sequence → QA Performance → Module Distribution → Module Effort Summary → Execution Results → Risks; CSS เพิ่ม `.effort-table`, `.order-no`, `.count-pill`, `.m-title`, `.risks-grid`, `.risk-card`, `.risk-icon`, `.risk-body`; Dashboard.css เหลือแค่ `.executive-dashboard{display:grid;gap:18px}`
- Dashboard Merge & Reorder: รวบ Execution Results (donut + legend) เข้าไปใน Module Workload Distribution เป็น card เดียว (`.exec-results-row` แสดง execution donut ขนาดเล็ก + legend pills ด้านล่าง workload bar chart); ลบ standalone Execution Results card; ย้าย section ที่รวมแล้วมาไว้ก่อน Module Overview; Layout ใหม่: Hero → KPI → Module Workload & Execution Results → Module Overview → QA Performance → Module Effort Summary → Risks

### 2026-08-17

- Topbar Project context ต้อง sync กับ filter ในทุกหน้าที่เกี่ยวข้อง: เมื่อผู้ใช้เปลี่ยน Project ใน Topbar หน้า Requirement จะล้าง Module filter, หน้า Test Case จะเปลี่ยน Project filter ตาม, หน้า Test Cycle จะกรองรายการตาม Project/Release/Build ที่เลือก, หน้า Execution Workspace จะกรอง Test Cycle selector ตาม Project/Release/Build ที่เลือก; filter ภายในหน้ายังเปลี่ยนอิสระได้แต่ต้อง reset เมื่อ Context เปลี่ยน
- หน้า Release / Build ต้องรับ `contextProjectId` จาก Topbar และกรอง Release list ตาม Project ที่เลือก; เมื่อเปลี่ยน Project ต้องเลือก Release แรกของ Project นั้นโดยอัตโนมัติหาก Release ที่เลือกอยู่คนละ Project
- หน้า Requirement ต้องกรองข้อมูลตาม `contextProjectId` จาก Topbar ในส่วน `filtered` computation เพื่อให้แสดงเฉพาะ Requirement ของ Project ที่เลือก
- Topbar Project/Release/Build filter ต้องซ่อนในหน้าที่ไม่เกี่ยวข้องกับ data scope ระดับ Project ได้แก่ Project / Module, User / Role, Setting Center, System Monitor เพื่อไม่ให้ topbar รกและสื่อความหมายผิด
- Responsive layout สำหรับมือถือและแท็บเล็ต: Dashboard Executive (hero/status-card/user-bar/donut ต้อง stacking เป็นคอลัมน์เดียวบนหน้าจอ ≤760px), Executive Dashboard (module table เปลี่ยนเป็น mobile card layout ด้วย data-label, module-search/module-tree-name ลด min-width), RTM (เพิ่ม breakpoint ≤420px สำหรับ label column ที่เล็กลง); ทุกหน้าต้องไม่มี horizontal scroll ที่เกิดจาก layout หลัก
- หน้า Login ต้อง responsive 3 ระดับ: Desktop (2 คอลัมน์ visual + card), Tablet ≤1100px (visual panel ลด padding/scale ตัวอักษร, card ลด padding), Mobile ≤800px (ซ่อน visual, card เต็มหน้า, แสดง mobile-brand), Small Phone ≤420px (ลด padding/card radius进一步); ต้องใช้ `100dvh` แทน `100vh` และรองรับ `env(safe-area-inset-*)` สำหรับ notched phones
- Dashboard responsive แก้ไขปัญหา cascade/specificity: KPI grid ต้อง force `1fr` บน mobile ด้วย `.executive-dashboard .kpi-grid` + `!important` เพื่อชนะ `auto-fit minmax(190px)` จาก `DashboardExecutive.css`; `.card-title` ต้อง scoped `.executive-dashboard .card-title` เพื่อไม่ให้ override จาก `Dashboard.css` ตาย; `.decision`/`.overall-score`/`.module-search`/`.module-tree-name` ต้องลด `min-width: 0` ที่ breakpoint ≤900px เพื่อป้องกัน overflow บน tablet; `.module-health-card` table ต้องเปลี่ยนเป็น mobile card layout ด้วย `data-label` เหมือน module table อื่น; `.donut` ใช้ `flex: 0 0` แทน fixed width เพื่อ scale ได้ลื่นขึ้น; `.shared-dashboard > header` ต้อง `flex-wrap: wrap` บน mobile
- หน้า User / Role (Administration) ปรับ UI ใหม่ทั้งหมด: ใช้ single-column layout พร้อม stats row แสดงจำนวนผู้ใช้/สถานะ/บทบาท; ตารางผู้ใช้แสดง avatar gradient, role tags, project tags, status badge และ inline actions (แก้ไข/ปิด-เปิด/รหัสผ่าน); แก้ไขผู้ใช้เปิดเป็น modal ตามมาตรฐาน Create/Edit Modal; รีเซ็ตรหัสผ่านเปิดเป็น modal ขนาด 480px พร้อม password hint; เพิ่ม search bar ค้นหาผู้ใช้; ตารางเปลี่ยนเป็น card layout บน Mobile ≤520px ด้วย data-label
- Unified modal pattern ทั้งระบบ: ใช้ `.modal` เป็น backdrop (position fixed, blur 6px, พื้นหลัง `rgba(15,23,42,0.55)`) และ `.modal-box` เป็น content box สีขาว (border-radius 16px, shadow 0 24px 80px, max-width 900px) ตรงกลางหน้าจอ; `.modal-head` มี border-bottom 2px + ปุ่ม close มี border; `.modal-actions` มี border-top 2px + ปุ่ม primary มี gradient shadow; ทุกหน้าใช้ pattern เดียวกัน不再มี `.modal-backdrop`/`.modal-header`/`.modal-body`/`.modal-footer` แยก; Mobile ≤760px modal เต็มหน้าจอ 100dvh + safe-area support
