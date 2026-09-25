# ProMaxx2 QA Hub — UI Design System

> Dashboard share rule (13 สิงหาคม 2026): ลิงก์แชร์ใหม่ต้องใช้ short code แบบสุ่ม 8 ตัวใน `?s=` และเก็บ scope/วันหมดอายุในฐานข้อมูลเพื่อให้ใช้ต่อได้หลัง restart; endpoint token แบบเดิมต้องยังเปิดอ่านได้เพื่อ backward compatibility

> Dashboard Module Health rule (13 สิงหาคม 2026): ต้องแสดง Module เป็น Tree และเรียง Root/Child ด้วย `ParentModuleId`, `SortOrder`, `ModuleCode` ชุดเดียวกับหน้า Modules ห้ามเรียงด้วยชื่อ Module แยกต่างหาก

> Test Suite rule (13 สิงหาคม 2026): หน้า Test Suite ต้องใช้ Project Context, กรอง Project/Type/Risk/Active ได้, เปิดดูรายละเอียดแบบ read-only, จัด Test Case พร้อมค้นหา/ตัวกรอง/Select All, ระบุ Required หรือ Optional, ปรับลำดับ, ตรวจ API error, แสดงจำนวน Test Cycle ที่อ้างอิง และเปลี่ยนตารางเป็น card บน Mobile

> RTM Module dropdown (13 สิงหาคม 2026): ต้องเรียงแบบ Tree ตาม `ParentModuleId` และ `SortOrder` เหมือนหน้า Modules โดยแสดง Module Code, indentation และสัญลักษณ์กิ่งเพื่อแยก Root/Child ชัดเจน

> Requirement filter & module order rule (21 สิงหาคม 2026): หน้า Requirement ต้องกรองด้วยค่าที่มีในข้อมูลจริง (Status/Priority แสดงเฉพาะค่าที่พบพร้อมจำนวน, Release filter แสดงเมื่อมี Project context), dropdown Module เรียง Tree ด้วย `ParentModuleId`/`SortOrder`/`ModuleCode` เหมือนหน้า Modules และแถวตารางต้องเรียงตามลำดับ Module tree พร้อมคอลัมน์ Module (Code · Name)

> Release selector — Active only rule (23 สิงหาคม 2026): **ทุกหน้า** ที่มี dropdown/selector เลือก Release ต้องแสดงเฉพาะ Release ที่ **Active** (`Release.status !== "Cancelled"`) เท่านั้น เมื่อเปลี่ยน Project/Release context ต้องเคลียร์ Release/Build ที่ถูกปิดใช้งานออก และเลือก Active Release แรกถ้ามี — ให้ใช้เงื่อนไขเดียวกันกับหน้า Regression และ Test Summary เป็นมาตรฐานกลาง ทั่วทั้งแอป

> Automation page style rule (25 สิงหาคม 2026): หน้า **Automation** ใช้ UX/UI ตาม `Document/06.UI/automation-ui-style4.html` — Page Header มีช่องค้นหา + Export + ปุ่มสร้าง; แท็บ ภาพรวม เรียงเป็น KPI 6 การ์ด (กดแล้วไปแท็บที่เกี่ยวข้อง) → Workflow Flow 5 ขั้นตอน (สร้าง Case → Generate DSL/AI → Validate → Run Agent → Evidence/Result) → สองคอลัมน์ (สิ่งที่ต้องดำเนินการ + Agent Status พร้อม Health ring) → ตารางผลการรันล่าสุด; ช่องค้นหาหัวหน้าหน้ากรองรายการ Automation Cases

> RTM rule (13 สิงหาคม 2026): หน้า RTM ต้องอ้างอิง Project/Release ที่ผู้ใช้เลือก, แสดง KPI Covered/Partial/Not Covered, กรอง Module/Requirement Status/Coverage ได้, เปิดดู Requirement และ Test Case แบบ read-only modal, จัดการ Direct/Indirect Link ตามสิทธิ์, Export CSV และเปลี่ยนตารางเป็น card บน Mobile

> สถานะ: **UI Single Source of Truth**
> อัปเดตล่าสุด: 25 กันยายน 2026
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
- App shell: `src/ProMaxx2.QA.Web/src/App.tsx`; แต่ละหน้าอยู่ใน `src/pages/<ชื่อหน้า>.tsx` (โหลดแบบ `React.lazy` — ระหว่างโหลดแสดง `pageLoading`), ของที่ใช้ร่วมอยู่ใน `src/shared/` และ component กลางใน `src/components/` (`ModalShell`, `Badge`, `DialogHost`)
- Global design system: `src/ProMaxx2.QA.Web/src/styles.css`
- Test Case และ Test Step UI: `src/ProMaxx2.QA.Web/src/TestManagement.css`
- Stylesheet เฉพาะหน้า: `App.css`, `Dashboard.css`, `DashboardExecutive.css`, `DragDrop.css`, `ReleaseBuild.css`, `Automation.css`, `Regression.css`, `Rtm.css`, `MyWork.css` ฯลฯ — stylesheet ของหน้าที่อยู่ใน `src/pages/` ให้ import ในไฟล์หน้านั้น (โหลดพร้อม chunk ของหน้า) และ selector ต้อง scope ด้วย class ของหน้าเพื่อไม่ให้ขึ้นกับลำดับการโหลด

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
| `--warning-text` | `#9a6d00` | ข้อความ/ไอคอนสถานะ Warning/Blocked บนพื้นเหลืองอ่อน (contrast ผ่านกว่า `--yellow`) |
| `--info` | `#2563eb` | ข้อความสถานะ In Progress/Informational |

หลักการทั่วไป:

- Border radius ของ input/button: 8–10px
- Border radius ของ card/modal: 14–16px
- Focus ring: primary โปร่งใส 10–20% ขนาด 3px
- Card shadow ต้องเบา ไม่บดบังเส้นแบ่งข้อมูล
- Font หลัก: `"Kanit", Tahoma, "Noto Sans Thai", Arial, sans-serif` (Kanit โหลดจาก Google Fonts ใน `index.html`); PDF ที่สร้างฝั่ง client ใช้ `Tahoma, "Noto Sans Thai", Arial`
- **ขนาดตัวอักษรขั้นต่ำ 11px** สำหรับข้อความทุกชนิด (รวม label/metadata/หัวข้อ card บน Mobile) — ยกเว้นเฉพาะ glyph ตกแต่งใน `::before`/`::after` ที่ไม่ใช่ข้อความ
- **ห้ามเขียน hex ที่ซ้ำกับค่า token** — ใช้ `var(--primary)`, `var(--muted)` ฯลฯ แทน (สีที่ยังไม่มี token ใช้ hex ได้ แต่ถ้าใช้ซ้ำหลายหน้าให้เพิ่ม token ในตารางนี้ก่อน); สีใน `url(data:...)`, canvas และ PDF ใช้ hex ได้เพราะ `var()` ใช้ไม่ได้ในบริบทนั้น

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
| Execution | Automation | Dashboard, Automation Cases (DSL/Version/Validate/Approve/Run), Action Library, Object Repository, Agents, Execution Queue/History/Evidence |
| Governance | Test Summary | Reporting summary |
| Governance | Risk Acceptance | Risk review/approval |
| Governance | Release Sign-off | Gate และ sign-off |
| Administration | User / Role | User, role และ permission grid |
| Administration | การตั้งค่ากลาง | Master data และ Environment ที่จัดเก็บในฐานข้อมูล |
| Administration | System Monitor | API/Database health, allowlisted Windows Service status และ privileged Start/Restart actions |
| Administration | Audit Log | Activity overview, search/entity filter, desktop table, mobile cards และ read-only detail |

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
- **Mobile (≤760px):** ตารางหลายคอลัมน์ต้องเปลี่ยนเป็น card — ใส่ class `table-cards` ที่ `<table>` แล้วแต่ละแถวจะเป็น card ที่มีหัวข้อคอลัมน์ทางซ้าย (34%) และค่าทางขวา; `data-label` ของทุก cell ถูกเติมอัตโนมัติจาก `<th>` (`src/components/tableCardLabels.ts`) — ใส่ `data-label` เองได้ถ้าต้องการข้อความต่างจากหัวตาราง, cell ที่กินทั้งแถวไม่มีหัวข้อ; โหมด card ยกเลิกความกว้าง/nowrap รายคอลัมน์ทั้งหมดเพื่อไม่ให้เกิด horizontal scroll

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
- **ทุก modal ต้องใช้ `ModalShell` (`src/components/ModalShell.tsx`)** แทนการเขียน `.modal`/`.modal-box` เอง — ให้ `role="dialog"`, `aria-modal`, `aria-labelledby`/`aria-label`, focus ช่องแรกตอนเปิดและคืน focus ตอนปิด, Tab วนใน modal และ Escape ปิดเฉพาะ modal บนสุด
- ข้อมูลที่ยังไม่บันทึก: `ModalShell` ถือว่า dirty เมื่อผู้ใช้พิมพ์/เลือกค่าใน modal (event `input`/`change`) หรือผู้เรียกส่ง `dirty` (เช่น AI draft ที่สร้างขึ้นเอง) — Escape ถามยืนยันก่อนปิด และคลิกพื้นหลังจะปิดได้เฉพาะตอนยังไม่ dirty; modal แสดง secret ครั้งเดียวใช้ `backdropDismiss={false}`
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
| 900px | App shell เป็น Tablet/Mobile navigation เพื่อรองรับ iPad แนวตั้ง |
| 760px | Modal เต็มหน้าจอและ form stack |
| 420px | Test Case compact fields เหลือหนึ่งคอลัมน์ |

ห้ามออกแบบเฉพาะ viewport เดียว อย่างน้อยต้องตรวจที่ประมาณ 1440px, 1024px, 768px และ 390px

## 13. Accessibility และ UX Rules

- Input ที่ไม่มี label มองเห็นได้ต้องมี `aria-label`
- ใช้ semantic `button`, `label`, `input`, `select`, `textarea`
- Keyboard focus ต้องมองเห็นได้
- ห้ามใช้สีเพียงอย่างเดียวในการบอกสถานะ
- Confirm ก่อน operation ที่สร้าง historical record หรือลบข้อมูล
- ห้ามใช้ `window.alert` / `window.confirm` / `window.prompt` — ใช้ `confirmDialog` / `promptDialog` / `notify` จาก `src/components/dialogStore.ts` (แสดงผลโดย `<DialogHost />` ที่ mount ใน `main.tsx`): กล่องยืนยันระบุผลที่จะเกิดขึ้นและใช้ `tone: "danger"` กับการลบ/ทิ้งข้อมูล; แจ้งผลสำเร็จ/ล้มเหลวที่ไม่ผูกกับฟอร์มใช้ toast (`notify(message, "success" | "error" | "info")`) ส่วน error ของฟอร์มยังแสดงใกล้ field/ใน modal
- Loading และ disabled state ต้องป้องกันการ submit ซ้ำ
- หน้าที่มี Workflow หลายขั้นตอนต้องจัดลำดับ section ตามขั้นตอนการทำงานจริง (งานหลักก่อนข้อมูลประกอบ) และใช้ Step Guide Strip แสดงสถานะ done/active พร้อม scroll-to-section — reference implementation `.regression-steps` ใน Regression.css
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

### 2026-09-25 — แยก App.tsx เป็นหน้าละไฟล์

- ย้ายทุกหน้าที่เหลือ 15 หน้า (Dashboard, Defect, Project, Release, Requirement, RTM, Test Case, Test Suite, Test Cycle, Execution Workspace, Regression, My Work, User/Role, การตั้งค่ากลาง, System Monitor) ไป `src/pages/` แบบ `React.lazy`; App.tsx เหลือ ~1,070 บรรทัด (shell, context selector, routing, Login)
- ของที่หลายหน้าใช้ร่วมย้ายไป `src/shared/appShared.tsx` โดยใช้ TypeScript AST หาการอ้างอิงจริง — โค้ดของแต่ละหน้าไม่ได้แก้ logic มีแค่ย้ายไฟล์และเพิ่ม import/export
- ระหว่างโหลด chunk ของหน้าแสดง "กำลังโหลดหน้า..." (`Suspense` ครอบทั้งส่วน routing และลิงก์ Dashboard ที่แชร์)
- ไฟล์ JS หลักจาก 741 kB เหลือ 246 kB (gzip 172 → 75 kB) และ warning ขนาด chunk ของ Vite หายไป; ตรวจด้วย headless Edge ว่า import ได้ครบทั้ง 18 หน้าและหน้า Login render ได้

### 2026-09-25 — UI รอบ 6: token, ขนาดตัวอักษร, ลบโค้ดที่ไม่ใช้ และเริ่มแยก App.tsx

- **Design token:** แทน hex ที่มีค่าตรงกับ token ด้วย `var(--…)` ใน CSS 410 จุด (14 ไฟล์; ไม่แตะ `:root`, `url(...)`, canvas/PDF) — หน้าตาเหมือนเดิม แต่เปลี่ยน token แล้วมีผลทั้งแอป
- **ขนาดตัวอักษร:** ข้อความที่เล็กกว่า 11px (8/9/10px ราว 160 จุด รวมหัวข้อคอลัมน์ของ card บน Mobile) ปรับเป็น 11px ตามกฎใหม่ §3; เว้น glyph ตกแต่งใน pseudo-element และจุดแจ้งเตือน `.bell`
- **ลบโค้ดที่ไม่ใช้:** `WeightedAssignmentPreview.tsx`, `MyWorkDetailModal.tsx`, `Workload*.css` 5 ไฟล์ (ไม่มีใคร import), `LegacyMyWorkPage`, ตาราง mock ของ `DataPage` + `EmptyPage` + ข้อมูลตัวอย่าง (ทุกหน้ามีหน้าจริงแล้ว — Execution/Test Cycle/Test Suite route ตรงจาก App), CSS `.defect-table` บน Mobile และ CSS ของ My Work overview เดิม
- **เริ่มแยก App.tsx** (10,281 → ~9,150 บรรทัด): ย้าย Test Summary, Risk Acceptance และ Release Sign-off ไป `src/pages/` แบบ `React.lazy` พร้อม stylesheet ของหน้า; ย้าย `Badge`, `defectAgeDays` และ type ที่ใช้ร่วม (`ProjectItem`, `ReleaseItem`, `BuildItem`, `DefectItem`, `UserLookup`) ไป `components/`/`shared/` — chunk หลักเล็กลงราว 100 kB แต่ยังเกิน 500 kB (ต้องแยกหน้าที่เหลือต่อ)

### 2026-09-25 — UI รอบ 5: ตารางแบบ card บน Mobile

- เพิ่ม pattern กลาง `table-cards` (styles.css) + `installTableCardLabels()` ใน `main.tsx` ที่เติม `data-label` จาก `<th>` ให้อัตโนมัติ (รวมแถวที่ render ภายหลัง) — ตารางไม่ต้องเขียน data-label ทีละ cell
- ใช้กับ 25 ตารางที่เดิมบน Mobile ต้องเลื่อนแนวนอน: Defect (เดิม min-width 1180px และ CSS mobile ชี้ class ผิด `.defect-table`), Test Cycle, Build ใน Release, Regression automation preview, Risk Acceptance, ประวัติ Sign-off และทุกตารางในหน้า Automation (Cases, ผลรันล่าสุด, Wizard, Execution/Failure, Action/Object/Verification/Import, Agent heartbeat, Schedule, Build Trigger, Webhook token/delivery, Snapshot, Seed, Data Profile, Suite และ Suite cases)
- ตรวจด้วย headless Edge ที่กรอบกว้าง 390px: `scrollWidth` = `clientWidth` (ไม่มี horizontal scroll), ข้อความยาวตัดบรรทัดใน card, ปุ่มหลายปุ่มอยู่แถวเดียวกัน

### 2026-09-25 — UI รอบ 4: แยก error ออกจาก "ไม่มีข้อมูล" และยกเลิกคำขอเก่า

- เพิ่ม `getJson(url, signal)` / `isAbortError` ใน `api.ts` (ไม่ OK = throw `ApiError`) และ `useDebounced` กลาง (`src/components/useDebounced.ts`, หน้า Automation re-export)
- **Requirement:** โหลด RTM ไม่สำเร็จแจ้งว่า Coverage อาจต่ำกว่าความจริง (เดิมทุกแถว "ยังไม่มี Test Case"), ประวัติ Revision ที่โหลดไม่สำเร็จแสดง error (เดิม "ยังไม่มีประวัติ"), เปิดแก้ไขไม่สำเร็จแจ้ง toast (เดิมกดแล้วเงียบ), ตัวกรอง Project/Module/Release แจ้งเมื่อโหลดไม่สำเร็จ
- **Test Cycle:** ช่องค้นหา debounce 300ms (เดิมยิง 6 คำขอต่อตัวอักษร), รายการและจำนวนตามสถานะยกเลิกคำขอเก่าด้วย AbortController, สถานะที่นับไม่สำเร็จไม่แสดง 0 ปลอม, โหลดข้อมูลตั้งต้นของฟอร์มไม่สำเร็จแสดง error
- **Test Suite:** โหลดรายการไม่สำเร็จแสดง error; โหลดรายละเอียดไม่สำเร็จจะไม่เปิด detail/editor (เดิมแสดง 0 case และ editor อาจทำ case หายตอนบันทึก)
- **Defect:** ประวัติกิจกรรม/Test Case ที่เชื่อมที่โหลดไม่สำเร็จแสดง error ใน detail; ตัวกรอง Module/ผู้ใช้แจ้งเมื่อโหลดไม่สำเร็จ
- **Project / Release:** Module และ Build ตรวจผลก่อนใช้ (เดิม `data.filter` พังเมื่อ API error) และยกเลิกคำขอของรายการที่เลือกก่อนหน้า
- **Test Summary / Risk / Dashboard / Execution Workspace / Regression:** โหลดข้อมูลหลักไม่สำเร็จแสดง error แทนหน้าว่าง; Dashboard/Workspace ยกเลิกคำขอเก่าเมื่อเปลี่ยนบริบท; Regression ไม่ให้ผลของ Release ก่อนหน้ามาทับ
- dropdown ตั้งต้น (Master Settings, Project/Release/Build บน Topbar, Module ในฟอร์ม Test Cycle) ยังคงว่างเมื่อโหลดไม่สำเร็จแต่แจ้ง toast (`okJsonOrEmpty`)

### 2026-09-24 — UI รอบ 3: กล่องยืนยันและแจ้งผลแบบกลาง

- เพิ่ม `confirmDialog` / `promptDialog` / `notify` (`src/components/dialogStore.ts`) + `DialogHost` (ใช้ `ModalShell` จึงได้ Escape/focus/Tab ครบ) และแทน `window.confirm` 39 จุด, `window.alert` 49 จุด (เป็น toast แยกสี success/error), `window.prompt` 2 จุดทั้งแอป — toast อยู่มุมขวาล่าง (Mobile เต็มความกว้าง), error ค้าง 9 วินาที, success 5 วินาที, มีปุ่มปิดและ `role="alert"`/`status`
- กล่องยืนยันปิด modal ที่มีข้อมูลค้าง (ModalShell) เปลี่ยนจาก `window.confirm` เป็นกล่องของระบบ ("ปิดหน้าต่าง" / "กลับไปแก้ไข")
- เพิ่มการยืนยันที่ขาด: ปิด Defect (Quick Close) และเปลี่ยนสถานะ Defect หลายรายการ, Mark RC, เปลี่ยน Test Cycle เป็น Closed/Cancelled/Completed และเปลี่ยนสถานะหลาย Cycle, นำ Test Case ออกจาก Suite, ปิดใช้งานผู้ใช้, Regenerate ข้อความสรุปที่แก้ไว้ใน Test Summary, ปุ่มลัด P/F/B ใน Execution Workspace (ให้ตรงกับปุ่ม "ตั้งทุก Step"), อนุมัติ Automation Version และยกเลิก Quarantine
- Quick status / เปลี่ยนสถานะหลายรายการของ Defect และ Mark RC แจ้ง error เมื่อไม่สำเร็จ (เดิมเงียบ)

### 2026-09-24 — UI รอบ 2: Modal มาตรฐานทั้งแอป

- ย้าย `ModalShell` จากหน้า Automation ไปเป็น component กลาง `src/components/ModalShell.tsx` และเปลี่ยน modal ใน `App.tsx` ทั้ง 38 จุด + Audit Log เป็น `ModalShell` — ได้ Escape (เฉพาะบนสุด), focus เข้า/คืน, Tab trap, role/aria ครบทุก modal
- เลิกใช้ flag `form` (เดิมหน้า Automation ถามยืนยันทุกครั้งที่กด Escape แม้ยังไม่ได้กรอก): ตรวจ dirty จากการกรอกจริง — Escape ถามเฉพาะเมื่อมีข้อมูลที่กรอก, คลิกพื้นหลังปิดได้เฉพาะตอนยังไม่กรอก (เดิมฟอร์มส่วนใหญ่ใน App.tsx ปิดทันทีและข้อมูลหาย)
- AI draft modal ของ Test Case/Test Suite/Test Cycle ส่ง `dirty` เมื่อมี draft — คลิกพื้นหลังไม่ทิ้ง draft ที่ AI สร้างแล้ว
- Execution Workspace: เปลี่ยนเคส (คลิกรายการหรือปุ่ม N) ขณะมีผล step/Actual/Comment ที่ยังไม่บันทึก ต้องยืนยันก่อน (เดิมผลที่กรอกถูกล้างเงียบ ๆ)

### 2026-09-24 — UI รอบ 1: บั๊กที่ข้อมูลผิด/ใช้งานไม่ได้ (จากการตรวจ UI ทุกหน้า)

- **Release Sign-off:** เหลือ modal เดียว (เดิม render ซ้อน 2 ชั้น), ป้ายตัวกรองเป็น Release/Build ตามจริง, Smoke ที่ยังไม่มีข้อมูลแสดง "Not Run" (ไม่ใช่ "Fail"), NO GO/CONDITIONAL GO ต้องกรอก Comment, error แสดงใน modal และโหลด Gate/ประวัติไม่สำเร็จแสดง error แทนหน้าว่าง; ฟอร์มไม่ปิดเมื่อคลิกพื้นหลัง
- **Release/Build:** ป้ายสถานะ Build ใช้ `buildStatusTone` — Ready/Passed เขียว, Testing น้ำเงิน, Blocked เหลือง, Failed แดง, ไม่รู้จัก = เทา (เพิ่ม `.badge.gray`); เดิมหน้ารายการเขียวทุกสถานะ
- **Topbar:** นำปุ่ม Export กลางที่ไม่มีการทำงานออก — Export อยู่ในหน้าที่รองรับ (Defect, Test Cycle, RTM, Automation, Test Summary)
- **User / Role:** แสดง "สิทธิ์เพิ่มเติม" (สิทธิ์ที่ตาราง Create/Delete/Edit/View ไม่ครอบคลุม เช่น RISK.APPROVE, RELEASE.SIGNOFF, REPORT.EXPORT, AUTOMATION.EXECUTE) ซึ่งเดิมถูกซ่อนด้วย `display:none` จึงให้สิทธิ์จากหน้าจอไม่ได้; ตอนค้นหา ปุ่มเป็น "เลือก/ล้างที่แสดงอยู่" และไม่แตะสิทธิ์อื่น; โหลดผู้ใช้/สิทธิ์ไม่สำเร็จแสดง error + ลองใหม่ (เดิมหน้าพัง)
- **Test Case:** ถ้าโหลดรายละเอียดไม่สำเร็จจะไม่เปิดฟอร์มแก้ไข (เดิมเปิดด้วยข้อมูลจากรายการที่ไม่มี steps แล้วบันทึกทับ steps จริง); ฟอร์มไม่ปิดเมื่อคลิกพื้นหลัง, มี role/aria-label และ error แสดงใน modal
- **Test Cycle / Defect / Risk:** error ตอนบันทึกแสดงใน modal (เดิมแสดงที่หน้าหลักใต้ modal จึงมองไม่เห็น) และฟอร์มไม่ปิดเมื่อคลิกพื้นหลัง; Risk: Release ใน form มีตัวเลือก "เลือก Release" (เดิมแสดง Release แรกแต่ค่าจริงว่าง ทำให้ Save กดไม่ได้โดยไม่มีเหตุผล) และ Comment อนุมัติ/ปฏิเสธถูกล้างทุกครั้งที่เปิด
- **RTM:** ยกเลิก Link ต้องกดยืนยันในแถว (เดิมลบทันที), error แสดงใน modal และ busy ไม่ค้างเมื่อเครือข่ายล้ม
- กฎ (ใช้ต่อทุกหน้า): error ของการบันทึกใน modal ต้องแสดง **ภายใน modal**; ฟอร์มกรอกข้อมูลห้ามปิดด้วยคลิกพื้นหลัง; โหลดข้อมูลไม่สำเร็จต้องแสดง error ไม่ใช่รายการว่าง

### 2026-09-24 — Automation: `ModalShell` มาตรฐานและการโหลด dropdown

- ทุก modal ของหน้า Automation ใช้ `ModalShell` (`src/automation/ui.tsx`) แทนการเขียน `.modal`/`.modal-box` เอง: เปิดแล้ว focus ช่องกรอกแรก (หรือปุ่มแรก), Tab วนอยู่ใน modal, ปิดแล้วคืน focus ให้ปุ่มที่เปิด, Escape ปิดเฉพาะ modal บนสุด, มี `aria-labelledby` (หรือ `aria-label` เมื่อหัวข้อไม่มี id)
- modal ที่เป็นฟอร์ม (`form`) คลิกพื้นหลังไม่ปิด และ Escape ต้องถามยืนยันก่อนทิ้งข้อมูล; modal อ่านอย่างเดียวปิดด้วยคลิกพื้นหลังได้; modal แสดง secret ใช้ `backdropDismiss={false}`; ระหว่าง busy ห้ามปิด (ตรวจใน `onDismiss`)
- dropdown Build/Environment โหลดผ่าน `useBuildsAndEnvironments` ตัวเดียว และแสดง error **ภายใน modal นั้น** (เดิมบาง modal ส่ง error ไปที่หน้าหลักซึ่งถูก modal บังอยู่ หรือกลืน error แล้วแสดงเป็นรายการว่าง)
- modal ใหม่ของหน้า Automation ต้องใช้ `ModalShell` เสมอ

### 2026-09-24 — Automation: modal ข้อมูลสำคัญ และ error state

- Secret ที่แสดงครั้งเดียว (เช่น Webhook Token) ต้องอยู่ใน modal ที่ **ไม่ปิดด้วยคลิกพื้นหลัง**, มีช่อง read-only + ปุ่ม "คัดลอก" (ถ้า clipboard ใช้ไม่ได้ให้เลือกข้อความและบอกให้กด Ctrl+C), และถามยืนยันก่อนปิดถ้ายังไม่ได้คัดลอก (ทั้งปุ่มปิดและ Escape); Mobile ช่องและปุ่มเรียงแนวตั้ง (`.automation-token-copy`)
- คำสั่งที่รัน SQL บน DB จริง (Seed/Cleanup/MasterData, Restore) ต้องมี modal ที่ระบุชื่อ Script/ประเภท/DB, คำเตือนสีเหลือง และ `window.confirm` ที่ระบุ Environment/Build ก่อนส่ง แล้วแจ้งผลเมื่อเข้าคิวสำเร็จ
- modal Execution Detail: ผลจำแนก/AI/Defect เป็นของ execution ที่เปิดอยู่เท่านั้น; ปุ่มสร้าง Defect แสดงตามสิทธิ์ `DEFECT.EDIT`, ถามยืนยัน และหายทันทีหลังสร้าง
- รายการที่โหลดจาก API ต้องแยก "โหลดไม่สำเร็จ" (inline error `role="alert"` + ลองใหม่เมื่อทำได้) ออกจาก "ไม่มีข้อมูล" (`.empty`) — ห้ามแปลง 4xx/5xx เป็นรายการว่าง; ช่องค้นหาที่ยิง API ต้อง debounce ~300ms และยกเลิกคำขอเก่าด้วย `AbortController`

### 2026-09-23 — System review: workspace stability, defect modal a11y, tokens

- Execution Workspace: บันทึกผล (Save/Complete/Skip) หรือลบประวัติแล้วต้องคง Test Case ที่เลือกและตำแหน่ง scroll ไว้ — spinner เต็มหน้าแสดงเฉพาะตอนเปลี่ยน Test Cycle; ป้าย `Defect: <code>` ของ Step ต้องไม่หายหลัง reload (ล้างเฉพาะตอนเปลี่ยน Test Case)
- Modal แก้ Defect ใน Workspace: focus ช่องชื่อ Defect ตอนเปิด, ปิดด้วย Escape ได้, ถ้ามีข้อมูลที่ยังไม่บันทึก (รวมรูปที่เลือก) ต้องยืนยันก่อนปิดทั้งจากปุ่ม ✕/ยกเลิก/คลิกพื้นหลัง/Escape; ถ้าสร้าง Defect สำเร็จแต่แนบรูปหรือเชื่อม Test Case ไม่สำเร็จ Defect ต้องขึ้นใน Linked Defects ทันที; ปุ่ม "เพิ่มรูป" ต้องแสดง focus ring (`:focus-within`) เพราะ input ถูกซ่อน; ปุ่ม Edit ของ Linked Defect ต้องมี `aria-label` ระบุรหัส Defect
- หน้า Defect: โหลดสรุป (`/defects/stats`) ไม่สำเร็จต้องแสดง inline error + ปุ่มลองใหม่ ห้ามแสดงเป็น "ยังไม่มี Defect"; ปุ่มปิดข้อความ (icon-only) ทุกจุดต้องมี `aria-label="ปิดข้อความ"`
- Dashboard share: ต้องเลือก Project ที่ Topbar ก่อนสร้างลิงก์ (ลิงก์แบบ "ทุก Project" ถูกยกเลิก) และแสดงข้อความ detail จาก server เมื่อสร้างไม่สำเร็จ
- เพิ่ม token `--warning-text` และ `--info` (ดู §3) และใช้แทน hex ใน Test Summary release impact/unrun, Test Cycle case summary และ Execution Workspace metrics/step result; ข้อความประกอบใหม่ต้องมีขนาดอย่างน้อย 11px
- หน้า Automation และ Audit Log โหลดแบบ lazy (แยก chunk) — ระหว่างโหลดแสดง card `.empty` + `.spinner` พร้อม `role="status"`
- PDF Defect ตามโมดูลใช้ฟอนต์ตาม §3 (`Tahoma, "Noto Sans Thai", Arial`)

### 2026-09-16 — Audit Log visual refresh

- หน้า Audit Log ใช้ Page Header ของแอปเป็นหัวเรื่องชุดเดียว ไม่ซ้ำหัวเรื่องในการ์ด; แสดงจำนวนผลลัพธ์ในหัวรายการ พร้อมตัวค้นหา ตัวกรองประเภทข้อมูล สถานะ loading/error/empty และปุ่ม retry ที่ชัดเจน
- Desktop ใช้ตารางเรียงใหม่ล่าสุดก่อน แยกเวลา ผู้ดำเนินการ Action ข้อมูลที่เกี่ยวข้อง และสรุป; Mobile แปลงแต่ละแถวเป็น card ไม่ให้เกิด horizontal scroll ระดับหน้า
- รายละเอียดเปิดใน read-only modal มาตรฐาน โดยแสดงเฉพาะข้อมูลที่ API ส่งจริง (เวลา ผู้ดำเนินการ Action ประเภท/รหัสข้อมูล และสรุป) ไม่แสดง Before/After diff ที่ API ยังไม่มี

### 2026-09-10 — Test Summary readiness gap unit

- การ์ด Pass Rate ในส่วน “Release readiness gaps” แสดงส่วนต่างต่ำกว่าเกณฑ์ด้วยหน่วย `%` แทนคำว่า “จุด” เพื่อให้ตรงกับรูปแบบเปอร์เซ็นต์ที่ผู้ใช้ต้องการ

### 2026-09-09 — Regression workspace visual refresh

- หน้า Regression ใช้ workspace hero เพื่อสรุป Release, Target Build, Progress และสถานะรอบปัจจุบันจากข้อมูลเดิม พร้อมทางลัดไปยังการตั้งค่า Impact Analysis โดยไม่เพิ่ม API request
- KPI 5 ด้านใช้ icon, accent color และคำอธิบายร่วมกับสี; workflow 3 ขั้นปรับเป็น step navigation ที่แยกสถานะ Done/Active/Next ชัดเจนและเข้าถึงด้วย keyboard
- Impact Analysis, Recommended Cases, Schedule, Trend, Activity, Baseline และ History ใช้ลำดับชั้น card เดียวกัน; form control สูงอย่างน้อย 42px, focus ring ตาม primary token และรายการ Module/Test Case แสดง selected state ชัดเจน
- Responsive hero/KPI/workflow ลดจากหลายคอลัมน์เป็น 1–2 คอลัมน์ตามพื้นที่จริง และข้อความ Release, Build, Module รวมถึง Test Case ต้อง wrap โดยไม่สร้าง horizontal scroll ระดับหน้า

### 2026-09-09 — Risk Acceptance detail typography

- Modal รายละเอียด Risk Acceptance ใช้น้ำหนักตัวอักษรปกติสำหรับเนื้อหา Issue, Workaround, Target Fix, QA Recommendation และ Review Comment พร้อมเพิ่ม line-height เพื่อให้อ่านข้อความยาวได้ง่าย โดยคงหัวข้อ ค่า Impact/Probability/Owner และสถานะไว้ที่น้ำหนักกึ่งหนาเพื่อรักษาลำดับชั้นข้อมูล
- ชื่อ Risk ใน hero ใช้ขนาด 17px น้ำหนัก 600 และข้อความยาวต้อง wrap ภายใน modal โดยไม่สร้าง horizontal scroll บน Desktop หรือ Mobile

### 2026-09-09 — Dashboard attention modules scalability

- การ์ด “โมดูลที่ต้องติดตามเป็นพิเศษ” แสดง Top 8 โมดูลเป็นค่าเริ่มต้น พร้อมจำนวนโมดูลที่มีปัญหา ยอด Open Defect และอันดับรายโมดูลจาก Dashboard Summary ชุดเดียวกัน โดยห้ามยิง Defect list API ซ้ำ เพื่อให้ข้อมูลปรากฏพร้อม Dashboard และลดความสูงเมื่อข้อมูลเพิ่มขึ้น
- รายการใช้ลำดับ ชื่อโมดูลที่ wrap ได้ จำนวน Defect และ progress bar แยกสองบรรทัด พร้อมปุ่มขยายดูทั้งหมด/ย่อกลับที่เข้าถึงด้วย keyboard และมี `aria-expanded`
- Desktop จัดความสูงเริ่มต้นให้สมดุลกับกราฟผลการทดสอบ ส่วน Mobile ให้ summary wrap และชื่อยาวตัดบรรทัดโดยไม่สร้าง horizontal scroll ระดับหน้า
- การ์ด “ภาพรวมผลการทดสอบ” และ “โมดูลที่ต้องติดตามเป็นพิเศษ” ต้องยืดเต็มความสูงของแถวเดียวกันบน Desktop; ฝั่งภาพรวมใช้พื้นที่เพิ่มสำหรับสถานะเทียบเกณฑ์ คำอธิบายฐานการนับแบบย่อ และการ์ดประเด็นดำเนินการ (Not Run, Fail/Blocked และ Coverage Gap) จาก Dashboard Summary เดิม โดยพื้นที่ส่วนเกินต้องกระจายรอบกราฟ ห้ามเว้นช่องว่างก้อนใหญ่ระหว่าง KPI กับประเด็นดำเนินการ
- เมื่อกดดูโมดูลทั้งหมดบน Desktop รายการต้องเลื่อนภายในพื้นที่การ์ดเดิมและห้ามทำให้การ์ด “ภาพรวมผลการทดสอบ” ยืดตาม; Mobile ให้รายการขยายตามเนื้อหาปกติเพื่อหลีกเลี่ยง nested scroll

### 2026-09-09 — Requirement list visual refresh

- หน้า Requirement แสดง KPI 4 ด้านจากข้อมูลจริง (ทั้งหมด, In Scope, Approved และ Test Coverage) เป็นปุ่มกรองที่เข้าถึงด้วย keyboard และต้องแสดงสถานะที่เลือกด้วย `aria-pressed`
- แสดง Workflow status chips พร้อมจำนวน, filter controls และ removable active-filter chips ภายใน card รายการเดียวกัน โดยการล้างตัวกรองต้องคืนค่าทุกเงื่อนไขของหน้า
- ตาราง Desktop รวม Code/Title/Module และ Priority/Risk เป็นกลุ่มข้อมูลเดียวกัน ลดจำนวนคอลัมน์ พร้อมแสดง Scope/Coverage ด้วยข้อความร่วมกับสี; Requirement ใน Scope ที่ยังไม่มี Test Case ต้องเห็นจุดเตือนชัดเจน
- Responsive KPI ใช้ 4/2/1 คอลัมน์ตามพื้นที่ และตาราง Mobile ใช้ labeled card rows โดยไม่สร้าง horizontal scroll ระดับหน้า
- App shell ช่วง Tablet ≤1200px ต้องใช้ content track เต็มความกว้างและแสดง sidebar เป็น off-canvas; selector ของสถานะเมนูต้องไม่คืนค่า desktop grid จนบีบเนื้อหาเหลือเท่าความกว้าง sidebar

### 2026-09-09 — Test Summary executive message readability

- “ข้อความสรุปสำหรับผู้บริหาร” ต้องไม่แสดงเป็นย่อหน้ายาวต่อเนื่อง ให้แยกเป็นหัวข้อสถานะ, Fact cards 4 ด้าน (Execution, Test Result, Coverage, Risk) และแถบข้อสรุปเพื่อสแกนข้อมูลได้เร็ว
- Fact cards ใช้ตัวเลขจาก Test Summary แหล่งเดียวกัน จัดวางสูงสุด 4 คอลัมน์และลดเป็น 2/1 คอลัมน์ตามพื้นที่ภายใน card จริง ไม่ยึดเฉพาะ viewport breakpoint และต้องแสดงสถานะด้วยข้อความร่วมกับสี

### 2026-09-08 — Test Summary narrative persistence

- การบันทึก Known Issues, Remaining Risks และ QA Recommendation ต้องเริ่มหลังโหลด Narrative ของ Release ปัจจุบันเสร็จแล้วเท่านั้น เพื่อไม่ให้ค่าเริ่มต้นว่างเขียนทับ `qa.testSummaryNarrative.{releaseId}` ระหว่าง refresh หรือเปลี่ยน Release
- ข้อความที่ผู้ใช้แก้ไขต้องคงอยู่หลัง refresh; ปุ่ม Generate / Regenerate ยังคงแทนค่าด้วยข้อความที่ derive จาก Summary ล่าสุดตามพฤติกรรมเดิม

### 2026-09-08 — Test Summary executive readiness gaps

- Executive view ต้องมีข้อความสรุปและตัวเลข Readiness Gap ที่คำนวณจาก Test Summary แหล่งเดียวกัน เพื่อไม่ให้ข้อความขัดกับ KPI ที่แสดงบนหน้า
- Readiness Gap ใช้เกณฑ์ Coverage/Pass Rate ≥ 90%, Execution 100% และ P0 = 0 พร้อมจัดวาง 4/2/1 คอลัมน์บน Desktop/Tablet/Mobile

### 2026-09-08 — Test Summary decision support

- หน้า Test Summary ต้องสรุป Quality Gate 6 ด้านด้วยสถานะ Pass/Fail/No Data โดยห้ามตีความข้อมูล Regression ว่าผ่านเมื่อ API ยังไม่มีหลักฐาน
- Presentation Forecast ใช้วันที่นำเสนอที่ผู้ใช้ปรับได้และจำค่าแยกตาม Release พร้อมคำนวณจำนวน Test Case ขั้นต่ำต่อวันจาก Snapshot ล่าสุด; ต้องระบุว่าไม่รวมเวลาแก้ไขและ Retest
- Top Blockers แสดง Open Defect สูงสุด 5 รายการ เรียง Severity และอายุ พร้อม Owner จากข้อมูลจริง; ฟิลด์ที่ระบบไม่มี เช่น ETA ต้องแสดง “ยังไม่ระบุ” และห้ามสร้างข้อมูลสมมติ
- Quality Gate/Forecast จัดวาง 2/1 คอลัมน์ และ Top Blocker เปลี่ยนเป็น stacked row บน Mobile โดยไม่สร้าง horizontal scroll ระดับหน้า

### 2026-09-08 — Test Summary trends and presenter mode

- Test Summary เก็บ Daily Snapshot สูงสุด 30 วันแยกตาม Release ใน browser storage และเปรียบเทียบ Pass Rate, Execution, Open P0 และ Open Defects กับ Snapshot วันก่อนหน้า; วันแรกต้องแสดงสถานะรอข้อมูลแทนค่าการเปลี่ยนแปลงสมมติ
- Data Confidence คำนวณจากความพร้อมของ Scope, Environment, Out-of-Scope, Regression, Installation/Update และ Performance พร้อมแสดงหมวดที่ขาดอย่างชัดเจน
- Presenter Mode เป็น full-viewport read-only view สำหรับการประชุม แสดงเฉพาะ KPI, Executive View, Quality Gate, Forecast, Top Blockers, Trend และ Data Confidence; ปิดได้ด้วยปุ่มหรือ Escape และต้องรองรับ safe area/Mobile โดยไม่เกิด horizontal scroll

### 2026-09-08 — Test Summary interactive drill-down

- Readiness และ Quality Gate cards ต้องเป็น keyboard-accessible buttons พร้อม `aria-pressed`; เมื่อเลือกให้แสดงหลักฐานปัจจุบันและ Next Action ของ Gate เดียวกัน
- Top Blockers ใช้ accordion button พร้อม `aria-expanded` เพื่อเปิดรายละเอียดและ Expected/Actual โดยค่าไม่ครบต้องแสดง “ยังไม่ระบุ”
- Data Confidence chips ต้องกดเพื่อนำผู้ใช้ไปยังหมวด Release Report ที่เกี่ยวข้องและไฮไลต์หมวดนั้น; หากอยู่ใน Presenter Mode ให้กลับสู่หน้าปกติก่อนเลื่อนไปยังรายละเอียด

### 2026-09-08 — Service Manager resource chart

- ช่องว่างด้านขวาของ Automation Schedule Worker ใช้แสดงการ์ด `Service Resources` โดยกราฟเส้นสีน้ำเงินแทน CPU และสีเขียวแทน RAM ของ API + Web
- เก็บข้อมูลสูงสุด 120 จุดทุก 1 วินาที, แสดงค่าปัจจุบันพร้อมหน่วย และวาดด้วย double buffering เพื่อให้หน้าจอลื่นโดยไม่สะสม control หรือ drawing resource

### 2026-09-08 — Service Manager Activity Log layout

- หน้าต่าง Service Manager ต้องมีความสูงขั้นต่ำ 640px และพื้นที่ Service Cards ต้องไม่เบียด Activity Log จนช่องข้อความมองไม่เห็น โดยสงวนพื้นที่แนวตั้งให้ Log ใช้งานได้ในขนาดหน้าต่างเริ่มต้น

### 2026-09-08 — Test Case sortable columns

- หัวตารางหน้า Test Case ใช้ขนาดตัวอักษร 12px และคอลัมน์ `Test Case ID` กับ `สร้างเมื่อ` เป็นปุ่มเรียงข้อมูลที่เข้าถึงด้วย keyboard ได้ พร้อมไอคอนแสดงสถานะและ `aria-sort`
- การเรียงต้องทำฝั่ง Server ก่อนแบ่งหน้า รองรับน้อยไปมาก/มากไปน้อย และบันทึกคอลัมน์กับทิศทางล่าสุดไว้ใน `localStorage` ด้วย key `qa.testCases.listSort`
- Frontend ต้องเรียงข้อมูลในหน้าปัจจุบันซ้ำตามค่าเดียวกัน โดย Test Case ID ใช้ natural numeric comparison เพื่อให้การคลิกตอบสนองถูกต้องแม้ API instance ที่กำลังรันยังเป็นเวอร์ชันก่อนรองรับ sort

### 2026-09-08 — Execution Workspace Test Case queue

- ส่วน Test Cases ใน Execution Workspace ต้องจัดหัวข้อ เครื่องมือ และข้อมูลภายในรายการชิดซ้ายอย่างสม่ำเสมอ
- รายการ Test Case ต้องเรียง `Test Case ID` จากน้อยไปมากด้วย natural numeric order เพื่อให้รหัสที่ลงท้ายด้วย `2` อยู่ก่อน `10`; หากรหัสซ้ำให้ใช้ Cycle Case ID เป็นลำดับสำรองเพื่อให้ผลลัพธ์คงที่

### 2026-09-07 — Test Case mobile text containment

- การ์ดรายการ Test Case บน Mobile ต้องกว้างไม่เกินพื้นที่ของ table container และใช้ fixed table layout หลังแปลงตารางเป็น card
- Test Case ID, Title และค่าทุกคอลัมน์ต้องตัดบรรทัดด้วย `overflow-wrap:anywhere` โดยห้ามดันการ์ดหรือทำให้เกิด horizontal scroll ระดับหน้า

### 2026-09-07 — Global back-to-top control

- ทุกหน้าภายใน App shell แสดงปุ่ม `กลับไปด้านบน` แบบลอยเมื่อ scroll container หลักเลื่อนเกิน 480px และกดแล้วเลื่อนกลับด้านบนแบบ smooth
- ทุกขนาดหน้าจอใช้ปุ่มวงกลมขนาด 44px แสดงเฉพาะ Material Symbol `arrow_upward` โดยมี `aria-label` และ `title` อธิบายการทำงาน
- ปุ่มต้องรองรับ keyboard focus, safe-area และ `prefers-reduced-motion` รวมทั้งใช้ z-index ต่ำกว่า modal เพื่อไม่ทับหน้าต่างโต้ตอบ

### 2026-09-07 — Test Suite list visual refresh

- ปรับหน้า Test Suite ให้ summary/actions, status chips, filters และ table อยู่ใน card เดียวที่แบ่งพื้นที่ชัดเจน
- ลด visual weight ของ row actions โดย Desktop เรียง action ในแถวเดียวเพื่อลดความสูงของ data row; Mobile ใช้ touch target 34px และเรียงตามพื้นที่
- สถานะ `ยังไม่มี Cycle` ในตาราง Test Suite ใช้สีแดงทั้งข้อความและ Material Symbol เพื่อสื่อว่าต้องดำเนินการต่อ
- Test Suite pagination เริ่มต้น 30 รายการต่อหน้า และเลือก 30/50/100/150 ได้; เมื่อเปลี่ยน filter หรือจำนวนต่อหน้าต้องกลับหน้าแรก และแสดงจำนวนผลลัพธ์หลังกรอง
- Filter รองรับ 5/3/2/1 ช่องตามพื้นที่ และ Mobile แสดง Suite เป็น labeled card rows โดยไม่เกิด horizontal scroll ระดับหน้า

### 2026-09-07 — Test Case list visual refresh

- ปรับหน้า Test Case ให้ summary/actions, filter และ table อยู่ใน card เดียวที่แบ่งพื้นที่ชัดเจน พร้อมลด visual weight ของ row actions
- Template และ Import ใช้ Material Symbols พร้อม label ที่อ่านง่าย; filter ใช้ responsive grid 6/3/2/1 ช่องตามพื้นที่
- Desktop ใช้ compact data table และ Mobile เปลี่ยนเป็น card rows ภายใน container โดยไม่สร้าง horizontal scroll ระดับหน้า
- Pagination ใช้รูปแบบเดียวกับ Test Suite: ค่าเริ่มต้น 30 รายการ ตัวเลือก 30/50/100/150 แสดงจำนวนทั้งหมด และใช้ Material Symbols สำหรับก่อนหน้า/ถัดไป

### 2026-09-07 — RTM dashboard visual refresh

- ปรับ RTM summary เป็น icon-based coverage cards แยก Requirements, Covered, Partial และ Not Covered ด้วยสีเชิงความหมาย
- จัด filter toolbar และตารางเป็น card เดียวกัน ลด visual weight ของ action buttons และเพิ่ม hover/readability สำหรับข้อมูล Requirement
- Responsive ใช้ KPI 4/2 คอลัมน์ตามพื้นที่, filter 4/2/1 คอลัมน์ และ labeled table rows บน Mobile โดยไม่สร้าง horizontal scroll ระดับหน้า

### 2026-09-07 — My Work dashboard visual refresh

- ปรับหน้า My Work ให้ใช้ profile strip, icon-based metric cards, compact filter chips และ assignment card ที่แบ่ง header/filter/content ชัดเจน
- Empty state ต้องอยู่กึ่งกลางพื้นที่ข้อมูลและใช้ Material Symbol ภายในกรอบสีอ่อน แทนการแสดงชิดมุมใต้หัวตาราง
- Metric grid รองรับ 5/3/2/1 คอลัมน์ตามพื้นที่ และตาราง Mobile ใช้ labeled rows เดิมโดยไม่เกิด horizontal scroll ระดับหน้า

### 2026-09-07 — My Work assigned-case data source

- หน้า My Work ต้องใช้ `GET /api/v1/my-work` ซึ่งอ้างอิงผู้ใช้จาก Authentication Context และแสดง Test Cycle Case ที่ `AssignedTesterUserId` ตรงกับผู้ใช้ปัจจุบัน ห้ามใช้รายการที่ผู้ใช้สร้างเองแทนงานที่ได้รับมอบหมาย
- แสดง summary และ filter จาก execution status พร้อม Module, Priority, Test Type, Test Cycle, Build, Due Date, Estimated Time และ action เข้า Execution Workspace
- ตาราง Desktop ต้องเลื่อนภายใน container และ Mobile แปลงเป็น labeled rows โดยไม่สร้าง horizontal scroll ระดับหน้า

### 2026-09-07 — Test Cycle detail visual hierarchy refresh

- ปรับ modal รายละเอียด Test Cycle ให้มีความกว้างที่อ่านง่ายขึ้น พร้อม sticky breadcrumb header ภายใน modal
- ปรับ progress summary เป็น responsive grid และแยก KPI เป็นการ์ดย่อยเพื่อสแกนข้อมูลได้เร็วขึ้น
- เพิ่ม Material Symbols ให้หัวข้อข้อมูลหลัก และรองรับการจัดวางแบบเต็มจอบน Mobile โดยไม่เกิด horizontal scroll
- Pagination หน้า Test Cycle ใช้รูปแบบเดียวกับ Test Suite: ค่าเริ่มต้น 30 รายการ ตัวเลือก 30/50/100/150 แสดงจำนวนทั้งหมด และรองรับ API สูงสุด 150 รายการต่อหน้า

### 2026-09-07 — Material Symbols across all pages

- ตรวจ React components ทุกหน้าและแทน icon แบบ emoji/text glyph ที่ยังเหลือสำหรับ action มาตรฐาน เช่น close, confirm, edit, refresh, download, AI, run, warning, navigation, power และ empty state ด้วย `Material Symbols Outlined`; ส่วนที่ใช้ Material Symbols อยู่แล้วคงเดิม
- เพิ่มกฎกลางสำหรับขนาด line-height น้ำหนัก และ alignment ของ Material Symbols ภายใน button/table action/modal header เพื่อให้ไอคอนสม่ำเสมอทุกหน้า โดยข้อความ ลูกศรที่เป็นส่วนของข้อมูล และเครื่องหมายสถานะที่ไม่ใช่ icon ยังคงเดิม

### 2026-09-07 — Cross-page responsive data integrity

- ตรวจ responsive inventory ทุกหน้าจาก application routes และ stylesheet ทั้งหมด แล้วเพิ่ม safety layer กลางให้ page/card/grid children ใช้ `min-width: 0`, ข้อความและ identifier ยาว wrap ได้, media จำกัดตาม container และ wide table/permission matrix เลื่อนแนวนอนภายใน container เท่านั้นโดยไม่ดันทั้งหน้า
- Tablet/Mobile ≤900px เปลี่ยน application shell และ topbar พร้อมกันเพื่อให้ iPad แนวตั้งแสดง content เต็มพื้นที่ และทำให้ filter/tool/action groups wrap โดยไม่บีบหรือซ่อนข้อมูล พร้อมคงปุ่ม primary/secondary ทุก action; modal ทุกชนิดใช้ `100dvh` เต็มจอตามมาตรฐาน ≤760px
- แก้ mobile sidebar ที่ถูก desktop selector `.app.menu-closed .sidebar` บังคับให้แสดงค้างทับเนื้อหา: สถานะปิดต้อง translate ออกนอกจอเสมอ; เมื่อเปิดใน collapsed mode แถบกว้าง 64px และปุ่มกว้าง 46px เท่าพื้นที่ไอคอน โดยแบ่งพื้นที่ให้ `<main>` และไม่ใช้ backdrop บังข้อมูล ส่วนเมนูแบบเต็มยังใช้ backdrop ตามเดิม
- Popup/Modal ทุกหน้าต้องยึดกับ viewport และเปิดตรงกลางจอเสมอ: `.page-transition` ห้ามคง `transform` หรือ `will-change: transform` เพราะจะสร้าง containing block ให้ `position: fixed` จน Popup เคลื่อนตาม scroll; page transition ใช้ opacity animation เท่านั้น
- Small phone ≤560px ลด page/card padding, ให้หัวข้อ wrap และ action/pagination มี touch target ที่เหมาะสม โดยใช้ breakpoint และ component pattern เดิมของระบบ

### 2026-09-07 — Test Cycle detail Material Symbols

- หน้า Test Cycle/รายละเอียดใช้ `Material Symbols Outlined` จาก Google Fonts สำหรับไอคอนทั้งหมดแทน emoji และ text glyph ได้แก่ breadcrumb/close, cycle/status/type, environment, progress statistics, release/build/ผู้สร้าง/วันที่/suite/module, timeline, notes และปุ่มปิด/แก้ไข
- หน้า Test Cycle ส่วนรายการและ action ที่นำไปยังรายละเอียดใช้ Material Symbols ชุดเดียวกันด้วย ได้แก่ retry/close notice, export, AI generate, create, bulk status, empty state, edit/start/close/delete และ pagination เพื่อไม่ให้มี glyph หรือ emoji เก่าปะปนใน flow หน้า Test Cycle
- ไอคอนใช้ชื่อ ligature ที่สื่อความหมายพร้อม `aria-hidden="true"`; สี ขนาด วงกลมพื้นหลัง และ responsive timeline arrow กำหนดผ่าน class เฉพาะ `.cycle-detail-*` เพื่อไม่กระทบหน้าอื่น

### 2026-09-07 — Test Suite detail Material Symbols

- หน้า Test Suite/แสดงรายละเอียดใช้ `Material Symbols Outlined` แทน emoji, ตัวอักษรย่อ และ glyph ทั้งหมดใน header/close, hero, KPI, ข้อมูล Module/ผู้สร้าง/วันที่, กำหนดการและผู้ดำเนินการของ Test Cycle, ลิงก์เปิดรายละเอียด, expand list และ footer actions
- กำหนดขนาด น้ำหนัก และ alignment ผ่าน selector ภายใต้ `.suite-detail` เพื่อรักษารูปทรง icon container และไม่เปลี่ยน icon ของหน้าอื่น

### 2026-08-28 (Weighted Auto Assignment)

- หน้า Test Cycle เพิ่ม action `Auto Assign Preview` ใน unified modal เดิม เพื่อเริ่ม workflow Preview อายุ 10 นาที; ใช้ปุ่มมาตรฐานและแสดงผลผ่าน notice/error เดิมของหน้า ไม่สร้าง page-level horizontal scroll หรือ modal pattern ใหม่
- เพิ่ม Preview result modal แยก component แสดง Before/After Load, Score, Reason และ warnings; Confirm ใช้ primary button มาตรฐานและแสดง modal ซ้อนแบบ keyboard-accessible ตาม pattern เดิม
- Test Cycle detail เพิ่ม Assignment History section แบบโหลดตามคำขอ ใช้ `.table-wrap` เพื่อจำกัด horizontal scroll ไว้เฉพาะตาราง

### 2026-08-26 (Automation Action/Object Management)

- หน้า Automation แท็บการจัดการเพิ่ม row actions **แก้ไข/เปิด/ปิด** สำหรับ Action Library และ Object Repository ตามสิทธิ์ `AUTOMATION.MANAGE`; modal เดิมรองรับทั้ง create/edit โดย Action Code คงที่เมื่อแก้ไข, แก้ Parameter Schema JSON/Handler Key/Minimum Agent Version ได้ และ Object แก้ Business Key/AutomationId/Selector JSON พร้อมแสดง Object Version; การเปิด–ปิดต้อง confirm, ระหว่างบันทึก action ถูก disabled, invalid JSON แสดง inline page error; ตารางยังเลื่อนภายใน `.table-wrap` และ modal ใช้ Unified modal responsive เดิม

### 2026-08-26 (Automation Object Import)

- หน้า Automation แท็บ Object Repository เพิ่ม `Import Scanner` ตามสิทธิ์ `AUTOMATION.MANAGE`; ใช้ Unified modal pattern เดิม รองรับ paste/upload JSON หรือ CSV จาก UI Inspector/AutomationId Scanner, ต้องกด Preview Diff ก่อน import, แสดงสถานะต่อแถว Ready/DuplicateKey/DuplicateAutomationId/Invalid, เลือกเฉพาะแถว Ready ได้ และตาราง preview ต้องอยู่ใน `.table-wrap` เพื่อไม่สร้าง page-level horizontal scroll

### 2026-08-25 (Dashboard share: แสดงชื่อ Project)

- **หน้า Dashboard (รวมโหมดแชร์) แสดงชื่อ Project แทน "Release Readiness Dashboard"**: root cause — hero title (`exec-hero-title`) ใช้ `projectName || "Release Readiness Dashboard"` โดยโหมดปกติส่ง `projectName` จาก context แต่**โหมดแชร์ไม่ส่ง prop นี้** เลย fallback เป็นข้อความคงที่; **แก้ที่ backend** — เพิ่ม field `ProjectName` ใน `DashboardSummary` (`DashboardService.cs` record) และ populate ใน `DashboardRepository.GetAsync` (query `ProjectName` จาก `ProjectId` ที่ resolve แล้ว — ใช้ได้ทั้ง mode ปกติและ share เพราะ share เรียก endpoint เดียวกัน), frontend เพิ่ม `projectName?` ใน type `DashboardSummary` + เปลี่ยน hero title เป็น `projectName || data.projectName || "Release Readiness Dashboard"`; ทดสอบยืนยัน summary คืน `projectName='ProMaxx2'`; restart API ตาม §4

### 2026-08-25 (Bugfix: เพิ่ม Project ใหม่ไม่ขึ้น)

- **แก้บั๊กหน้า Project/Module เพิ่ม Project ใหม่แล้วไม่ปรากฏ**: root cause — เมื่อสร้าง Project ใหม่ backend (`ProjectService.CreateAsync`) ไม่สร้าง `ProjectUser` ให้ผู้สร้าง ทำให้ `RequireProjectAccess` filter (ผ่าน `ProjectAccessService.GetAllowedProjectIdsAsync` + `GET /projects` → `ListForUserAsync`) กรองโปรเจกต์ใหม่ที่ผู้ใช้สร้างออกจากรายการทันที → เหมือน "เพิ่ม Project ไม่ได้" (POST สำเร็จ 201 แต่ไม่เห็นในหน้า); **แก้ที่ `ProjectContracts.cs` `CreateAsync`** ให้เรียก `AddProjectUserAsync(userId, projectId)` หลังสร้าง Project (เมื่อมี `userId`) — ผู้สร้างเห็นโปรเจกต์ใหม่ทันที; ทดสอบยืนยัน: ก่อนแก้ GET /projects คืน 1 โปรเจกต์, หลังสร้าง+แก้ คืน 2 (PMX2 + โปรเจกต์ใหม่); restart API ตาม §4

### 2026-08-25 (Create Automation Case Wizard)

- **Modal สร้าง Automation Case เปลี่ยนเป็น Wizard 4 ขั้นตอนตาม `Document/06.UI/automation-case-ui.html`**: Stepper `.acw-stepper` (เลือก Test Case → ตรวจสอบรายละเอียด → สร้าง Automation Case → เสร็จสิ้น) พร้อมสถานะ done/active; **ขั้น 1 เลือก Test Case** — filter 3 ช่อง `.acw-filters` (Module tree เดิม + ค้นหา + **Priority ใหม่**), ตาราง `.acw-table` เลือกแบบ radio (Code/ชื่อ/Priority badge/สถานะ "มี Case แล้ว"/Automation candidate ✓) + หน้าเลข `.acw-pages` 8 แถว/หน้า, แผงขวาแสดงรายละเอียดจริงจาก `GET /test-cases/{id}` (Objective/Preconditions/Expected จาก step สุดท้าย/Module/Test Type/สถานะ) + hint box; **ขั้น 2 ตรวจสอบรายละเอียด** — การ์ดทบทวน (meta + Objective/Preconditions) + Test Steps list, **Readiness checklist คำนวณจากข้อมูลจริง** (`status==="Ready"`/มี Objective/มี Module/มี Steps), ตั้งค่า **Automation Type** (WindowsUI/Pos/App — ส่งจริงไป `POST /automation/cases` แทน hardcode เดิม) + หมายเหตุสำหรับ AI (ใช้เป็น changeReason ของ Version แรก); **ขั้น 3 สร้าง Automation Case** — การ์ดข้อมูล Case (Code/Name/Linked TC/Version/Status badge/Agent Target) + summary box 4 ค่า real (`parseDslSteps` Actions, นับ `EXPECT_*` Assertions, AI Confidence จาก generate, Validation Error count) + chips หลัง Validate, **DSL editor พื้นหลัง dark** `.acw-dsl` (textarea จริง แก้ไขได้) + Generate AI/โหลดตัวอย่าง + ปุ่ม "บันทึก + Validate" (POST version + validate — ถ้าผ่านไปขั้น 4, ไม่ผ่านโชว์ error คงอยู่ขั้น 3); **ขั้น 4 เสร็จสิ้น** — success screen (icon ✓, Result grid, ปุ่ม ดู Automation Case = เปิด case detail modal ที่แท็บ Cases / ไปหน้า Automation / สร้าง Case เพิ่ม = resetWizard); footer nav ยกเลิก/‹ ย้อนกลับ/ถัดไป ›/สร้าง Automation ›/บันทึก + Validate ›; เติม `automationCandidate`/`testType` ใน type `TestCandidate`; responsive ≤1000px เรียง 1 คอลัมน์, ≤700px ซ่อน stepper

### 2026-08-25 (P1 UX gaps)

- **หน้า Automation ทำ P1 ครบ 5 ข้อตามผลวิเคราะห์**: ① **กด KPI แล้ว filter ตามสถานะ** — metrics เปลี่ยนจาก `tab` เป็น `go()` closure: Cases/Ready/Maintenance → แท็บ Cases พร้อม set `caseStatusFilter`, Running/Failed → แท็บ Execution พร้อม set `execFilter` (ยก state ขึ้น parent), Agents Online → แท็บ การจัดการ sub-tab Agents; ② **Cancel Execution** — ปุ่ม ✕ (confirm) เรียก `POST /automation/executions/{id}/cancel` ในตาราง Run History + การ์ดผลรันล่าสุด (เฉพาะ Queued/Running) + modal Execution Detail (`canRun` เท่านั้น) อัปเดตสถานะใน modal จาก response ทันที; ③ **Re-run** — ปุ่ม ▶ (confirm แสดง Rev/Build/Env) เรียก `POST /automation/cases/{id}/run` ด้วย version/build/environment ชุดเดิมของ execution นั้น (agent = auto assign, P5); ④ **Refresh + Auto-polling** — ปุ่ม ↻ รีเฟรชใน page header + poll อัตโนมัติทุก 15 วิ เมื่อมีงาน Active (`kRunning > 0` หรือ job Queued/Assigned/Running) และข้ามเมื่อ `document.hidden`; ⑤ **แท็บ Cases filter + pagination** — toolbar `.automation-case-toolbar` (กรอง Status 7 ค่า + Target App + ปุ่มล้างตัวกรอง), hint `.automation-search-hint` รวมผลค้นหา+filter, ตาราง render ทีละ 15 แถว (`.Pager` เดิม), footer "ดูผลการรันทั้งหมด" แสดงเมื่อ >5 แถว; CSS เพิ่ม `.automation-row-actions`/`.automation-more.is-run|is-danger`/`.automation-detail-action`/`.automation-hide-mobile`

### 2026-08-25

- **หน้า Automation map ข้อมูลจริงให้สอดคล้องกับ UI**: KPI/Flow/Health/งานต้องทำ ใช้ค่า aggregate จริงจาก **`GET /automation/dashboard?projectId=`** (`AutomationDashboardDto` — `AutomationCases/Ready/MaintenanceRequired/**NeedsReview/InProgress**/Running/**PassToday/FailToday**/AgentsOnline/AgentsTotal/**ReadyCoverage/CandidateCoverage`) แทนการคำนวณ client-side จาก list ที่จำกัด 200 รายการ; KPI **Ready** ใช้ note `{ReadyCoverage}% coverage` (คิดจาก Test Case ทั้งหมด), **Failed** ใช้ `FailToday` (ผล Fail เฉพาะวันนี้), Agents Online ใช้ `AgentsOnline/AgentsTotal`, Flow step "Generate DSL/AI" ใช้ `InProgress` (Draft+NeedsReview+Validated+Approved) และ "Evidence/Result" ใช้ `ผ่าน PassToday / Fail FailToday`, งาน "ต้องตรวจสอบ DSL" ใช้ `NeedsReview`, Health ring ใช้ `AgentsOnline/AgentsTotal`; backend **ขยาย `AutomationDashboardDto` เพิ่ม `NeedsReview` + `InProgress`** เพื่อให้ทุกตัวเลขที่แสดงเป็น aggregate จริงจาก DB; ถ้า dashboard endpoint ไม่ตอบ (ไม่มี Project หรือเก่า) จะ fallback เป็นค่า client-side เดิม; **backend เพิ่ม `TestCaseCode/TestCaseTitle` ใน `AutomationExecutionDto`** (ทุก endpoint executions/detail) ให้ตารางผลการรันล่าสุดคอลัมน์ Linked Test Case แสดง Code + ชื่อ Test Case จริงโดยตรง (ยังมี fallback ผ่าน lookup จาก Automation Cases); restart API ตาม §4

- **หน้า Automation ปรับ UX/UI ใหม่ทั้งหมดตาม `Document/06.UI/automation-ui-style4.html`**: แทนที่ `.automation-head` ด้วย **Page Header** `.automation-page-head` (หัวข้อ + คำอธิบาย + ช่องค้นหา `.automation-search` + ปุ่ม **↥ Export** (Export CSV ตาม `exportCases`) + ปุ่ม **＋ สร้าง Automation Case**); แท็บ ภาพรวม (Dashboard) เรียงตาม style4 — ① **KPI 6 การ์ด** `.automation-metrics`/`.automation-metric` (Automation Cases/Ready/Maintenance/Running/Failed/Agents Online พร้อม icon วงกลมสี `.automation-metric-ico m-*` + note + ลูกศร `›`, **กด KPI แล้วไปแท็บที่เกี่ยวข้อง**) ② **Workflow Flow** `.automation-flow-card`/`.automation-flow` (5 ขั้นตอน แนวนอนมีลูกศร `→` — สร้าง Case → Generate DSL/AI → Validate → Run Agent → Evidence/Result, ขั้นที่ทำแล้ว `✓` badge `.automation-flow-badge`, ขั้นถัดไป `.active`, กดแล้วเปิดแท็บ; ≤1280px เรียงแนวตั้ง) ③ **สองคอลัมน์** `.automation-two-col` (ซ้าย **สิ่งที่ต้องดำเนินการ** `.automation-task-*` เรียงตาม `nextActions` แยก title/desc ด้วย `—` + icon สีตามความเร่งด่วน; ขวา **Agent Status** `.automation-agent-card` แสดง meta (PC Name/OS/Version/Heartbeat/Running Jobs) + วง Health `.automation-ring` แบบ conic-gradient % จาก agents online) ④ **ผลการรันล่าสุด** `.automation-result-panel` + `.automation-recent-table` (Automation Case + Linked Test Case + Result + Agent + Execution Time + Duration HH:MM:SS + ปุ่ม `⋮` เปิดรายละเอียด, footer "ดูผลการรันทั้งหมด ›"); ช่องค้นหาหัวหน้าหน้ากรองแท็บ **Automation Cases** (`.automation-search-hint` แสดงผล + ล้างการค้นหา); แก้ nextActions/workflow tab targets ให้เป็นแท็บจริง (เดิม "agents"/"suites" ค้างจากโครงสร้างเก่า); เพิ่ม badge tone `purple`/`gray`/`cyan`; responsive: KPI 6→3→2→1, two-col 2→1, agent card/health stack; ใช้ token ระบบ (`--primary/--green/--yellow/--red/--muted/--line`) ไม่เกิด horizontal scroll ระดับหน้า

### 2026-08-24

- **หน้า Test Case เพิ่มปุ่มลบแบบกลุ่ม**: แถบ bulk selection (`.testcase-bulk-bar`) เพิ่มปุ่ม **ลบที่เลือก** (`.btn danger`) — คลิกแล้วเปิด modal ยืนยัน `.confirm-box` แสดงจำนวนที่เลือก ("ยืนยันการลบ Test Case ที่เลือก") ก่อน `DELETE /test-cases/{id}` ทีละรายการตาม pattern bulk เดิม (`applyTcBulkStatus`/`applyTcBulkAutomation`); ระหว่างลบปุ่มทั้งหมดในแถบ disabled (`tcSaving==="bulk-delete"`) หลังเสร็จล้างการเลือก + reload + แจ้งผลเป็น notice (จำนวนที่ลบสำเร็จ) / error (รายการที่ลบไม่สำเร็จ สูงสุด 5 รหัสแรก); ปุ่มยกเลิกเลือกและปุ่มอื่นในแถบ disabled ระหว่าง saving
- **แถบ bulk ของหน้า Test Case ตรึงติดหน้าจอ** (`position:sticky`): `.testcase-bulk-bar` จอดอยู่ใต้ topbar ขณะเลื่อนตารางยาว (`top:74px` ตรงความสูง `.qa-topbar` desktop; `top:60px` เมื่อ topbar หุบเป็นแถวเดียว ≤800px; `top:134px` เมื่อ context field เรียงซ้อน ≤420px), `z-index:5` (ต่ำกว่า topbar `z-index:15`), พื้นหลังโปร่งแสง + `backdrop-filter:blur(8px)` + เงาเบาเพื่อแยกชั้นจากแถวตาราง; ไม่เกิด horizontal scroll

### 2026-08-23

- **Modal สร้าง Automation Case จบในหน้าเดียว**: flow 3 ขั้นตอนใน modal เดียว (`.automation-create-step` + `.automation-create-step-title` พร้อมเลขขั้น) — ① เลือก Test Case (มี **Filter ตาม Module** `.automation-create-filter` dropdown + คลิกเลือกแถว highlight `.is-selected`) ② สร้าง Automation Case ③ เขียน/Generate DSL (✦ Generate AI + textarea + โหลดตัวอย่าง) → **สร้าง Version + Validate** ครบแล้วปิด modal; ไม่ต้องไปต่อที่แท็บอื่น

- **หน้า Automation ปรับ UI ใหม่ให้สะอาดขึ้น**: รวม 7 แท็บเป็น **4 แท็บ** (ภาพรวม / Automation Cases / Execution / การจัดการ) — Action Library + Object Repository + Agents ย้ายไปอยู่ใต้แท็บ การจัดการ (sub-tabs `.automation-subtabs`); ตัด hero ขนาดใหญ่เป็น header กะทัดรัด `.automation-head` (title + สถานะ agent/รัน/คิว + ปุ่มสร้าง); **Step Guide Strip ย้ายให้แสดงเฉพาะแท็บ ภาพรวม** (ลดความรกในแท็บทำงาน); Dashboard ตัดการ์ด Action Library summary ออก (เหลือ KPI + ขั้นตอนถัดไป + ผลรันล่าสุดแบบเต็มแถว); Regression Suites เปลี่ยนเป็นปุ่ม **▶ รันเป็นกลุ่ม** ในแท็บ Cases → modal `.automation-batch-*` (เลือก Ready case + Build/Env/Priority)

- **หน้า Automation Execution ปรับ UI รองรับข้อมูลจำนวนมาก**: เพิ่ม KPI แถวบน (Queued/Running/Passed/Failed/Total), Job Queue และ Run History มี **แบ่งหน้า** (`Pager` — `หน้า X/Y · N รายการ · 15/หน้า`) ครั้งละ 15 รายการ (fetch 200), Run History มี **ช่องค้นหา** (Code/Agent) + **กรองสถานะ** (dropdown Passed/Failed/Running/...), เปลี่ยนเป็นตาราง `.automation-exec-table` (Code+Rev, Target App badge, Agent, Status, Duration, เวลา, ปุ่มดู) + คลิกแถวเปิดรายละเอียดได้; ป้องกันการเรนเดอร์รายการเยอะในครั้งเดียว

- **Automation Agents เพิ่มปุ่มลบ**: การ์ด Agent (`.automation-agent-actions`) เพิ่มปุ่ม **ลบ** (confirm) → `DELETE /automation/agents/{id}` (soft delete `IsDeleted` — ไม่กระทบ Execution ที่อ้างอิง FK); Agent ที่ถูกลบแล้วถ้ายังรันอยู่จะ **register กลับมาใหม่เองอัตโนมัติ** (Reactivate) ในรายการ Agents

- **Automation Module เพิ่ม Target App routing**: Automation Case มี `AutomationType` = **Pos / App / WindowsUI** (สร้างจาก Test Case ใช้ `AutomationTarget` เดิม pos/app); แท็บ Cases เปลี่ยนคอลัมน์เป็น **Target App** (badge สี); Case Detail เพิ่ม **select เปลี่ยน Target App** (`POST /automation/cases/{id}/target`) — งานที่รันจะถูก Agent ที่รองรับ target นั้นรับไป (Agent ประกาศ target จากชื่อ exe `PromaxxsPos.exe`→Pos, `Promaxxs.App.exe`→App) — ตอบโจทย์ว่า Test Case จะรู้ว่าส่งไปทดสอบแอปไหน

- **หน้า Automation ปรับ UX ให้เข้าใจง่ายขึ้น**: เพิ่ม **Step Guide Strip** (`.automation-steps` ตาม pattern `.regression-steps` ใน §13) 5 ขั้นตอน — ① สร้าง Automation Case ② เขียน DSL / Generate AI ③ Validate + อนุมัติ → Ready ④ รันผ่าน Agent ⑤ ตรวจผล/Evidence/Defect — แต่ละขั้นแสดง `✓` เมื่อทำแล้ว / highlight ขั้นที่กำลังทำ (`.active` + `aria-current="step"`) คลิกแล้วเปิดแท็บที่เกี่ยวข้อง; Dashboard เพิ่ม **"ขั้นตอนถัดไป"** (`.automation-next-steps`) แนะนำ action ที่ควรทำตามสถานะจริง (ยังไม่มี case → สร้าง / มี NeedsReview → ตรวจ DSL / มี Ready แต่ไม่มี agent online → เริ่ม agent / มี Fail → ตรวจผล); Empty state ทุกแท็บมีปุ่ม action นำทาง; แท็บ Cases เพิ่ม **status legend** (`.automation-status-legend` + `.legend-dot`) อธิบาย Draft/NeedsReview/Ready/MaintenanceRequired; Case Detail เพิ่ม `.automation-case-hint` แนะนำขั้นตอนถัดไปตามสถานะ; responsive: step strip 5→3→1 คอลัมน์

- **Automation Module เพิ่ม G10 Regression Suites**: แท็บ **Regression Suites** (`.automation-suites`) แสดง Coverage KPIs (จาก `GET /automation/dashboard` — Total/Candidates/Ready/Maintenance/Running/Pass-Fail วันนี้/Avg Duration/Agents Online + **Ready Coverage** และ **Candidate Coverage** แบบ progress bar `.automation-coverage-track`) + เลือกหลาย Automation Case (checkbox + select-all) → ปุ่ม **▶ รัน N case** (เรียก `POST /automation/batch-run`) เลือก Build/Environment/Priority — งานกระจายไปหลาย Agent รัน parallel ได้ผลรวมในหน้า Execution

- **Automation Module เพิ่ม G9 Defect/Failure Classification**: ใน modal Execution Detail (`.automation-failure-analysis`) เมื่อ Execution เป็น Failed จะแสดงชุด **Failure Analysis** — ปุ่ม "จำแนก Fail" (rule-based classifier → FailureType + Product Defect Candidate + คำแนะนำ), ปุ่ม "วิเคราะห์ด้วย AI" (AI Failure Analyzer → classification + confidence + summary + recommendation, ต้องมี `AUTOMATION.GENERATEAI`), ปุ่ม "สร้าง Defect" (QA confirm → `POST .../executions/{id}/defect` ใช้ `DEFECT.EDIT`; ถ้า AI วิเคราะห์แล้วจะส่ง classification นั้นด้วย) + แสดง badge/ผล/คำแนะนำ และสถานะ Defect ที่ link แล้ว

- **Automation Module เพิ่ม G8 AI Generator**: ปุ่ม **✦ Generate AI** ใน Automation Case Detail → Version Editor (`.automation-version-create-actions`) เรียก `POST /automation/cases/{id}/generate` (policy `AUTOMATION.GENERATEAI`) ให้ AI อ่าน Test Case + Available Actions + Object Repository แล้วสร้าง Automation Version `GeneratedByAi=true` พร้อม `AiProvider/AiModel/AiConfidence`; Case เปลี่ยนเป็น `NeedsReview` (Human Review ตามแผน §6.3/§G8) แล้ว Validate → Approve → Ready; frontend reload versions + notice หลัง generate

- **หน้า Automation สร้างใหม่ทั้งหมดตาม `AUTOMATION_MODULE_DEVELOPMENT_PLAN.md` (MVP scope ข้อ 41)**: ระบบเก่า (Windows Runner + pos/app target + AutomationId Quality Gate) ถูกลบออกทั้ง backend/frontend และสร้าง Automation Module ใหม่เป็น 6 แท็บ (`AutomationPage` ใน `src/AutomationPage.tsx` + `Automation.css`): ① Dashboard (KPI: Cases/Ready/Maintenance/Running/Pass/Fail/Agents Online + Coverage + ผลรันล่าสุด + สรุป Action Library) ② Automation Cases (รายการ + modal รายละเอียดพร้อม **Version Editor** — สร้าง Version/DSL JSON, Validate, อนุมัติ, สั่งรัน; step strip `.automation-tabs` sticky, `.automation-version-*`, `.automation-dsl-preview`, `.automation-validation-errors`) ③ Action Library (CRUD + filter category) ④ Object Repository (CRUD + filter screen) ⑤ Agents (สถานะ Online/Offline/Disabled + enable/disable) ⑥ Execution (Job Queue + Run History + modal รายละเอียด step results + เปิด Evidence); สร้าง Automation Case จาก Test Case ที่เป็น Automation Candidate (modal `.automation-candidate-pick`); modal สั่งรัน (`RunModal`) เลือก Version/Build/Environment/Agent/Priority; ใช้ token/`.card`/`.badge`/`.modal`/`.form-grid`/`.chip` เดิมทั้งหมด responsive ≤760px (KPI 2 คอลัมน์, tabs scroll, hero stack)
- สร้าง **Central Windows Agent** โปรเจกต์ใหม่ `agent/` (ProMaxx2.Automation.Core/Hub/Runner): Runner console loop heartbeat→claim→execute DSL→report step→complete; UI driver ผ่าน `IUiAutomationDriver` + FlaUI (`cf.ByAutomationId`); ActionExecutor รองรับ LOGIN/OPEN_MENU/CLICK/SET_TEXT/EXPECT_*; screenshot-on-fail + upload evidence; env config ผ่าน `set-agent-env.ps1`
- Backend Automation Module ใหม่: 9 ตาราง (AutomationCases/Versions/Actions/Objects/Agents/AgentCapabilities/Executions/StepResults/Jobs) + DSL v1 Typed JSON + `AutomationValidator` (Schema/Action/Object/TestData) + API `/api/v1/automation/*` (Cases/Versions/Actions/Objects/Agents/Jobs/Executions/Agent endpoints register/heartbeat/claim/result/complete/evidence) + permission `AUTOMATION.*` (seed ผ่าน migration) + `ExecutionType=Manual|Automation` ใน TestExecution; ลบระบบเดิม (AutomationRuns/QualityGate/Queue/RunnerAgents/Schedules + Release gate)

- หน้า Automation ปรับ UX ให้ใช้งานง่ายขึ้น (workflow-first): เพิ่ม **Step Guide Strip** (`.automation-steps`, pattern เดียวกับ `.regression-steps`) แสดง 3 ขั้นตอน — ① กำหนดเป้าหมาย Test Case ② สั่งรันหรือตั้งตาราง ③ ติดตามผลการรัน — โดยขั้นที่เสร็จแล้วแสดง `✓` (`.done`), ขั้นที่กำลังทำอยู่ highlight (`.active`) พร้อม `aria-current="step"`; คลิกขั้น 1–2 `scrollIntoView` ไปยัง section Candidates/Queue, ขั้น 3 เปิด Run History view; ค่า done/active คำนวณจากสถานะจริง (`review===0`, มีคิว/Schedule, มีประวัติรัน); ใช้ `.automation-step-no` (วงกลมเลข/✓) + `.automation-step-text` (หัวข้อ + คำอธิบาย) และ stack เป็นคอลัมน์เดียวบน ≤760px; แทนที่ `.automation-section-nav` แบบ anchor ราบ และตัด `.automation-guide` ที่ซ้ำซ้อนทิ้ง (flow แสดงผ่าน step strip แล้ว)
- ย่อ section ที่เป็น "monitoring" ให้ยุบได้: Ready Runner (`automation-agents`), Automation Scheduling (`automation-schedules`) และ AutomationId Quality Gate (`automation-gate`) เปลี่ยนจาก `<section>` แบบขยายเสมอ เป็น `<details className="automation-monitor">` เริ่มต้นปิด (แสดงเฉพาะหัวการ์ด/สถานะสรุป) เพื่อลดความยาวหน้าและไม่ให้แย่งความสนใจจากงานหลัก (Automation Candidates + Trigger & Queue ที่ยังขยายเต็ม); header เดิมย้ายมาเป็น `<summary>` (ซ่อน marker เนทีฟ + chevron ▸ หมุน 90° เมื่อ open), คง `#automation-*-title` id ไว้; `aria-labelledby` ของ section เปลี่ยนเป็น `aria-label` บน `<details>`; เปิดปิดด้วย native `<details>` ไม่ต้องใช้ JS
- หน้า Test Summary (เดิมเป็น `EmptyPage`) พัฒนาเป็นหน้า report ระดับ Release จริง: เพิ่ม component `TestSummaryPage` + stylesheet ใหม่ `TestSummary.css`; ให้เลือก Release (ดึงจาก `GET /releases?projectId=`) และโหลด auto-summary จาก `GET /dashboard/summary?projectId&releaseId&buildId` (reuse `DashboardSummary`) ประกอบกับ `GET /releases/{id}` (Scope) และ `GET /master-settings/environments`; แสดง Executive Summary (Badge decision GO/CONDITIONAL GO/NO-GO + Pass Rate, Requirement Coverage, Execution Progress, Defect Quality), Metrics (coverage/executed/pass/open P0-P1/defects/overall), Defect Severity distribution, progress bars + legend ตามสถานะ, Environment chips; ส่วน narrative Known Issues / Remaining Risks / QA Recommendation ปรับแก้ไขได้ (textarea) และ Generate/Regenerate จะเติมค่าแนะนำอัตโนมัติจากข้อมูล (`derive()`); ปุ่ม Export CSV (REPORT.EXPORT) สร้างรายการ Metric; responsive 2 คอลัมน์บน Desktop → 1 คอลัมน์ ≤900px, exec grid 4→2→1; ใช้ token/`.card`/`.badge` เดิม ไม่เกิด horizontal scroll ระดับหน้า
- ต่อยอดหน้า Test Summary (backend + ส่งต่อ Sign-off): เพิ่ม endpoint ใหม่ `GET /releases/{releaseId}/test-summary` (backend `TestSummaryController` + `TestSummaryService` สร้าง `TestSummaryDto(ReleaseDto, DashboardSummary, GeneratedAt)` คำนวณจากข้อมูลเดิม — ไม่เพิ่มตาราง DB เนื่องจาก `Database:ApplyMigrations` เป็น false) ให้หน้าเรียก API เดียวแทน 3 calls เดิม (dashboard/summary + release detail); เพิ่มปุ่ม **Export Excel (.xls)** ข้าง Export CSV (สร้าง HTML table ผ่าน `exportExcel` เหมือนรูปแบบ export อื่นในระบบ); เพิ่มปุ่ม **ไปหน้า Sign-off** (เรียก `onOpenSignoff` → `setPage("signoff")` ไปยังหน้า Release Sign-off); narrative Known Issues/Remaining Risks/QA Recommendation ถูกเก็บใน `localStorage` (key `qa.testSummaryNarrative.{releaseId}`) เพื่อไม่หายเมื่อ refresh และ `Generate/Regenerate` จะรีเซ็ตเป็นค่าแนะนำจากข้อมูล
- หน้า Test Summary: dropdown Release กรองเฉพาะ Release ที่ Active (`status !== "Cancelled"`) และ logic เลือก Release จาก context จะเลือกเฉพาะ release ที่ active; ตั้งกฎกลาง "**Release selector — Active only rule**" ในหัวข้อกฎของเอกสารนี้ ให้ทุกหน้าที่มี Release selector ใช้เงื่อนไขเดียวกันกับ Regression/Test Summary
- หน้า Risk Acceptance (เดิมเป็น `EmptyPage`) สร้างเป็น feature เต็ม: component `RiskAcceptancePage` + `RiskAcceptance.css` + backend เต็ม (`RiskAcceptance` entity/`RiskAcceptanceService`/`RiskAcceptanceRepository`/`RiskAcceptanceController` + migration `AddRiskAcceptances`); สร้างตาราง `RiskAcceptances` ผ่าน `dotnet ef database update`; ใช้ `GET/POST/PUT /risk-acceptances` + `POST .../submit|approve|reject|close` + `DELETE`; แสดงรายการ (Risk ID, Title, Release, Impact, Probability, Risk Level, Owner, Status, Review Date) กรอง Release (active only) / Status, ค้นหา; modal สร้าง/แก้ไข (Release, Linked Defect, Title, Issue, Impact, Probability, Workaround, Target Fix, QA Recommendation, Owner); รายละเอียด (Approval UI ตาม spec: Issue, Business Impact, Workaround, Target Fix, QA Recommendation, Linked Defect) พร้อม workflow action ตามสถานะ (Draft→Submit→Approve/Reject→Close, แก้ไข/ลบเฉพาะ Draft/Rejected); badge `risk-level` (High/Medium/Low), แสดงผู้ประเมิน/comment; สิทธิ์: view/create = `PROJECT.EDIT`, approve/reject/close = `RISK.APPROVE` (policy `RiskApprove`); confirm ก่อนลบ/อนุมัติ/ปฏิเสธ/ปิด; responsive ≥760px modal เต็มจอ + grid 1 คอลัมน์
- หน้า Release Sign-off (เดิมเป็น `EmptyPage`) สร้างเป็น feature เต็ม: component `ReleaseSignoffPage` + `ReleaseSignoff.css` + backend เต็ม (`ReleaseSignoff` entity/`ReleaseSignoffService`/`ReleaseSignoffRepository`/`ReleaseSignoffController` + migration `AddReleaseSignoffs`); สร้างตาราง `ReleaseSignoffs` ผ่าน `dotnet ef database update`; เพิ่ม endpoint `GET /releases/{releaseId}/release-gate` (คำนวณ Release Gate ตาม `ReleaseGate.Evaluate`: Smoke = AutomationId Quality Gate ผ่านทั้ง pos+app, P0/P1 blocker จาก test cycle, Coverage, Regression Pass Rate, Approved Risks, แสดง decision GO/CONDITIONAL_GO/NO_GO), `GET /releases/{releaseId}/signoffs`, `POST /releases/{releaseId}/signoffs`; หน้าแสดง Release Gate Panel (Smoke / Coverage / Regression / P0 / P1 Blocker + badge decision), ผู้ใช้เลือก Release (active only) + Build, สร้าง sign-off (Decision GO/CONDITIONAL_GO/NO_GO + Comment + confirm), แสดง Sign-off History (Build, Type, Decision, By, Comment, Date); สิทธิ์: view = `PROJECT.VIEW`, สร้าง sign-off = `RELEASE.SIGNOFF` (policy `ReleaseSignoff`); responsive gate-grid 4→2→1 คอลัมน์
- ใช้กฎ **Release selector — Active only rule** กับทุกหน้าให้ครบ: แก้ `RtmPage` ให้กรอง Release ที่ Active (`status !== "Cancelled"`) เมื่อโหลดรายชื่อ (เดิมแสดงทุก Release); หน้า Regression/test-cycles/dashboard/Test Summary/Risk/Sign-off กรองแล้ว; seed role permission เพิ่ม `RISK.APPROVE` ให้ QA_LEAD/PRODUCT_OWNER/RELEASE_OWNER และ `RELEASE.SIGNOFF` ให้ QA_LEAD (RELEASE_OWNER มีแล้ว)
- หน้า User / Role (Administration) ปรับให้สอดคล้องเมนูระบบและใช้งานง่ายขึ้น: เพิ่มปุ่ม **+ เพิ่มผู้ใช้** (สร้างผู้ใช้ใหม่ผ่าน `POST /admin/users` + กำหนด Project ต่อ — ฟอร์มมี Username/Roles/Projects/กำหนดรหัสผ่าน) ใน toolbar รายชื่อผู้ใช้; แผง **สิทธิ์ตามบทบาท** จัดกลุ่มใหม่ตามเมนูระบบ (ภาพรวม Overview / Test Design / Test Execution / Release Governance / Administration) พร้อมไอคอนกลุ่ม `.perm-group-icon` + ตัวนับต่อกลุ่ม และช่อง **ค้นหาสิทธิ์** `.permission-filter` (กรอง permission), ปุ่ม "เลือกทั้งหมด" เลือกเฉพาะที่กรองเห็น; badge `role/status` เดิม, responsive toolbar wrap
- หน้า Setting Center: ปรับ UI AI Configuration — ฟอร์มจัดเป็น grid 2 คอลัมน์, เพิ่ม provider badge (`.master-ai-provider-badge`), note/action ขยายเต็มแถว; เพิ่ม Provider **opencode** (backend `Providers` + `GetRuntimeAsync` กำหนดให้ต้องระบุ Base URL และไม่บังคับ API key + `SendStructuredAsync`/`ListModelsAsync` ใช้เส้นทาง OpenAI-compatible; frontend เพิ่มใน type/`aiProviderModels`/option/Base URL/save condition) — พร้อมรายการ Model เริ่มต้นของ opencode
- แก้บั๊ก **Automation Schedule ไม่ทำงานตรงเวลา**: เพิ่ม background worker `AutomationScheduleWorker` (hosted service) ประมวลผล schedule ที่ถึงกำหนด (สร้าง queue job ให้ตรงเวลา) และกู้คืน lease ที่หมดอายุของงาน Claimed/Running ที่ runner หายทุก ๆ 15 วินาที — เดิม schedule จะถุกประมวลผลก็ต่อเมื่อมีคนเปิดหน้า Automation หรือ runner heartbeat เท่านั้น (เพิ่ม `IAutomationRunRepository.RunScheduledWorkAsync`); แก้ **เวลารันของ Schedule เป็นเวลาท้องถิ่น** (เดิม `runAtUtc` ส่งเวลาท้องถิ่นตรง ๆ ไปเป็น UTC ทำให้รันผิดไป 7 ชม. — เพิ่ม `scheduleToUtc` แปลง Local→UTC ฝั่ง frontend + เปลี่ยน label เป็น "เวลารัน (Local)")
- หน้า Automation Trigger & Queue เพิ่ม **สั่งรันทันทีแบบ 1 คลิก** (`.automation-quick-run`): เลือกโปรแกรมเป้าหมาย (pos/app) + ปุ่ม **▶ รันทันที** สร้างงานเข้าคิวทันที (ไม่เปิด modal, `testCycleId=null`) พร้อม `quickRun()`; ปุ่ม "ตั้งค่าเพิ่มเติม" เปิด modal เดิมสำหรับเลือก Test Cycle/หมายเหตุ; แจ้งผลผ่าน `.inline-alert.success` และถ้าไม่มี Runner Online แสดงคำเตือน `.automation-run-hint` ("ยังไม่มี Windows Runner Online — งานจะอยู่ในคิวจนกว่า Runner --worker จะมารับงาน")
- หน้า Setting Center เพิ่มการ์ด **Windows Runner (Worker)** (`.master-runner-configuration`): ฟอร์มกรอก QA Hub Base URL, Username, Password, path `PromaxxsPos.exe`/`Promaxxs.App.exe`/FDB แล้วกด **⤓ ดาวน์โหลด set-runner-env.ps1** ได้สคริปต์ PowerShell (escape ค่าครบ) ไปรันบนเครื่อง Runner เพื่อตั้ง User environment variables — ไม่เก็บ secret ฝั่ง Server
- คง KPI summary และ hero ไว้; ตรวจ Desktop + Mobile (≤760px step 1 คอลัมน์, monitor stack) ไม่เกิด horizontal scroll ระดับหน้า

### 2026-08-22

- หน้า Automation ปรับโครงสร้างเป็น Workflow-first: เรียง section ตามลำดับการทำงานจริง คือ Automation Candidates (กำหนดเป้าหมาย) → Windows Runner Agents → Automation Trigger & Queue → Automation Scheduling → AutomationId Quality Gate → Run History โดยย้าย Candidates ขึ้นก่อน Runner/Queue และลบ anchor แยกให้ใช้ `id="automation-candidates"` บน section เดียวกับ nav; ปรับ `.automation-section-nav` ให้เรียงลิงก์ตามลำดับเดียวกัน
- หน้า Automation Candidates เพิ่ม header สรุป (Badge `พร้อม X/Y` และ `รอตรวจสอบ`) และ toolbar กรอง/เรียง: chip filter (ทั้งหมด/พร้อม/ยังไม่พร้อม/POS/App/ยังไม่ระบุ) + select เรียงตาม (รหัส/Priority/สถานะ) ฝั่งขวา; ใช้ class `.automation-cand-summary` `.automation-cand-toolbar` `.automation-cand-filters .chip` `.automation-cand-sort`; กรอง/เรียงคำนวณ client-side จาก `cases` และ `search`
- หน้า Automation Candidates เพิ่มการแบ่งหน้าวาดตารางทีละ 50 รายการ (`candPageSize=50`) เพื่อป้องกันการเรนเดอร์แถวจำนวนมากครั้งเดียว; เพิ่ม `.automation-pager` (ปุ่ม ก่อนหน้า/ถัดไป + ข้อความ `หน้า X / Y · N รายการ`) ใต้ตารางเมื่อรายการเกิน 50; เปลี่ยนตัวกรอง/เรียง/ค้นหา/เปลี่ยน Project-Build จะรีเซ็ตกลับหน้า 1 ผ่าน effect; KPI summary คงคำนวณจากข้อมูลทั้งหมดเพื่อความถูกต้อง
- หน้า Automation ปรับ AUTOMATION CONTROL CENTER (hero) ให้สะอาดขึ้น: เปลี่ยนจากการ์ดสีครามไล่ระดับพร้อมเงาแรง มาเป็นการ์ดสีขาวขอบ `--line` เงาเบา, ลดขนาดหัวข้อเหลือ `clamp(18px,2vw,23px)` และใช้สี `--text`/`--muted` แทนขาว/ฟ้าสว่าง, จุดสถานะใช้สีเขียว/ส้ม/เทาโทนสมดุลและลด halo, ปุ่มรองใช้เส้นขอบมาตรฐาน; ตัด `.automation-hero:after` decorative blob ออก
- หน้า Automation Candidates เพิ่มตัวกรอง Module (`.automation-cand-module` ใช้ `renderModuleSelectOptions` เรียง Tree ตาม `ParentModuleId`/`SortOrder`/`ModuleCode` เหมือนหน้าอื่น) กรองร่วมกับ search/status/target client-side; เพิ่มการเลือกหลายแถวพร้อมกำหนด Target ทีละหลายรายการ: ช่อง checkbox ต่อแถว + หัวตารางเลือกทั้งหน้า, แถบ `.automation-bulk-bar` โผล่เมื่อมีรายการถูกเลือก (แสดงจำนวนที่เลือก + เลือก Target pos/app/ต้องตรวจสอบ + ปุ่ม "กำหนด Target" เรียก `PATCH /test-cases/{id}/automation-target` ทีละรายการผ่าน `applyBulkTarget` + ปุ่ม "ยกเลิกเลือก"); เฉพาะผู้มี `TESTCASE.EDIT` (`canEdit`) เท่านั้นที่เลือก/กำหนดได้, ระหว่างบันทึก bulk ใช้ `savingId="bulk"` ล็อก select ทุกแถว; แถวที่เลือกมีพื้นหลัง `.is-selected`; การเปลี่ยนตัวกรอง/เรียง/ค้นหา/Module/Project-Build รีเซ็ตการเลือก; Mobile ปรับ `.col-select` ไม่เยื้องขอบและแสดง label "เลือก" ในการ์ด
- หน้า Automation Candidates ปรับการแสดงผลแถวให้อ่านง่ายและสวยงาม: รหัสแสดงเป็น pill `.cand-code` (พื้นหลังฟ้าอ่อน ตัวหนา สี primary), ชื่อเรื่อง `.cand-title` สี `--text` (แก้ specificity ไม่ให้ถูก span ทั่วไปทำให้ซีด), Module ห่อเป็น chip `.cand-module-chip` (pill ขอบ `--line` ตัวอักษร muted ตัดคำด้วย ellipsis), จำนวน Steps แสดง `.cand-steps` เป็น `N ขั้นตอน` ตัวเลขเด่น; เพิ่มระยะห่างแถวแรก (`gap:6px`) ให้ชัดเจน; คง hover/selected states เดิม
- หน้า Automation Candidates แก้ checkbox ไม่ตรงกับแถวข้อมูลด้วยการยุบคอลัมน์ checkbox แยกออก แล้วย้าย checkbox ไปไว้ในเซลล์ "Test Case" แถวเดียวกับรหัส (`<div className="cand-main-top">` flex จัด checkbox คู่กับ `.cand-code` ชิดบรรทัดแรก), หัวตารางใช้ `.cand-head-top` จัด select-all ติดคำว่า "Test Case"; ไม่มีคอลัมน์แยกจึงไม่เกิด offset แนวตั้ง/แนวนอนอีก; ลบกฎ `.col-select` เดิมออก (.automation-table .cand-head-top / .cand-main-top / .cand-row-check)
- หน้า Automation Candidates เพิ่มปุ่ม "⤓ Export Plan" ใน header สรุป (`.automation-cand-summary`) เพื่อส่งออกรายการ Candidate ที่ผ่านตัวกรอง/ค้นหา/Module ปัจจุบันเป็นไฟล์ CSV (UTF-8 BOM) คอลัมน์ Test Case Code, Title, Module, Priority, Status, Readiness, Target, Steps ผ่านฟังก์ชัน `exportPlan` (client-side, ไม่ต้องเรียก API เพิ่ม) — เติมขั้นตอน "Export Plan" ใน workflow guide ให้ทำงานได้จริง
- หน้า Automation Run History เพิ่มแถบ Pass Rate ต่อแถวแต่ละรัน (`.automation-run-rate` / `.automation-run-rate-track`) คำนวณ `passedCount/totalCount`; ปรับ `.automation-run-card` เป็น grid 5 คอลัมน์บน Desktop และจัดวาง rate/time/action เป็นแถวเดียวกันบน Mobile ไม่เกิด horizontal scroll
- หน้า Automation เพิ่ม Trigger & Queue ตาม Build context: ผู้มี `EXECUTION.RUN` เปิด modal เลือก `PromaxxsPos.exe` หรือ `Promaxxs.App.exe`, เลือก Test Cycle ที่ยังเปิดอยู่ของ Build เดียวกันแบบ optional และใส่หมายเหตุได้; Queue แสดง status/target/runner/cycle/requested time/error และยกเลิกได้เฉพาะ Queued/Claimed; Desktop ใช้ summary row และ Mobile stack เป็น card หนึ่งคอลัมน์โดยไม่มี horizontal scroll
- หน้า Automation ต้องแสดง AutomationId Quality Gate ของ Build context แยก `PromaxxsPos.exe` และ `Promaxxs.App.exe` พร้อม baseline/current build, finding counts, runner และเวลา; สถานะรวมเป็น Passed เฉพาะเมื่อผลล่าสุดครบและผ่านทั้งสอง target, ข้อมูลไม่ครบเป็น Pending และ Failed/Pending ต้องสื่อว่าบล็อก Release; Desktop ใช้การ์ดสองคอลัมน์และ Mobile ลดเป็นหนึ่งคอลัมน์โดยไม่เกิด horizontal scroll
- หน้า Automation Run History เพิ่มปุ่มดูรายละเอียดและ modal แสดงผลราย case ได้แก่ Status, Duration, Error, Evidence และการเชื่อม Test Execution; Evidence เปิดผ่าน authenticated action ไม่ใช้ public URL; Desktop แสดง summary row และ Mobile เรียงข้อมูล/action เป็นคอลัมน์เดียวโดยไม่เกิด horizontal scroll
- หน้า Automation Run Detail เพิ่ม Write-back (ขั้นตอนสุดท้ายใน workflow guide): หากรันไม่ได้ผูก Test Cycle จะแสดงช่องเลือก Test Cycle (ที่ยังเปิดอยู่) + ปุ่ม "Write-back" ที่เขียนผลรันกลับเป็น Test Execution ใน Cycle นั้น (ใช้ `GET /test-cycles/{id}/execution` ดึงแมปรหัสเคส แล้ว `POST /test-cycle-cases/{id}/executions` ทีละรายการ แปลงสถานะ Passed→Pass/Failed→Fail/Blocked→Blocked/อื่น→Skipped ข้ามรายการที่มี testExecutionId แล้ว) — ฝั่ง frontend เท่านั้น ไม่ต้องรัน backend ใหม่; หากรันผูก Cycle แล้วแสดงข้อความ "เขียนกลับเรียบร้อย" ให้ดูผ่านลิงก์ "เปิด Execution"; ปุ่มแสดงเมื่อมีสิทธิ์ `EXECUTION.RUN` (`canRun`)
- เพิ่ม Automated Test จริง (vitest): แยก logic บริสุทธิ์ที่ทดสอบได้ออกจาก `App.tsx` ไปไว้ใน `src/automationHelpers.ts` (`csvEscape`, `buildAutomationPlanCsv`, `mapAutomationStatusToExecution`) แล้วให้ `exportPlan` เรียก `buildAutomationPlanCsv(rows)` และ `writeBack` เรียก `mapAutomationStatusToExecution(item.status)`; เขียน `src/automationHelpers.test.ts` ครอบคลุมการ escape CSV (รวม Thai/คำมีเครื่องหมายอัญประกาศ), โครงสร้าง/ลำดับคอลัมน์/การแมป Readiness+Target ของ Export Plan, และการแปลงสถานะ automation→execution; รันผ่าน `npm run test` (11 tests ผ่าน) — ไม่กระทบ UI เดิม
- เพิ่ม Integration Test (`src/automationActions.test.ts`, 6 tests) สำหรับพฤติกรรมจริงของ `exportPlan` และ `writeBack` โดยแยก behavior ออกเป็น `exportPlanAction` / `writeBackAction` (รับ dependency แบบ inject ได้) แล้วให้ `App.tsx` เรียกผ่าน wrapper เดิม; ทดสอบด้วยการ mock DOM (`createObjectUrl`/`revokeObjectUrl`/`createAnchor`) และ `fetch` — ตรวจว่า Export Plan สร้าง CSV download ถูก filename/revoke URL ถูกต้อง และWrite-back POST หนึ่งรายการต่อ result ที่ยังไม่ผูก execution (แมปสถานะ, ส่ง errorMessage/comment), ข้ามรายการที่ผูกแล้ว/ไม่มี testCaseId, แจ้ง onError เมื่อ workspace ผิดพลาด หรือ backend คืน detail, และไม่เรียก fetch เมื่อไม่มีสิทธิ์/ยังไม่เลือก Cycle; รวมทั้งหมด `npm run test` ผ่าน 17 tests ไม่กระทบ UI
- หน้า Automation เพิ่ม Run History ต่อจาก Candidate queue แสดงสถานะ, โปรแกรมเป้าหมาย, Runner, จำนวน Passed/Failed/Skipped/Total และเวลารัน โดย Desktop ใช้แถวสรุปและ Mobile เรียงเป็น card คอลัมน์เดียว ไม่ทำให้หน้าเกิด horizontal scroll

- เพิ่มหน้า Automation แยกในกลุ่ม Test Execution: ใช้ Test Case ที่มี `AutomationCandidate=true` เป็น source of truth, แสดง KPI Candidate/Ready/POS/Master Data/ต้องตรวจสอบ Route, workflow guide และ responsive candidate table/card; ผู้มี `TESTCASE.EDIT` กำหนด target ที่บันทึกจริงต่อ Test Case ได้ (`pos`/`app`) ส่วนผู้ใช้อื่นเป็น read-only; routing rule คือ POS/งานขาย → `PromaxxsPos.exe` และ Master Data → `Promaxxs.App.exe`; หน้าไม่เก็บ credential หรือสั่งรัน desktop AUT จาก Web server

### 2026-08-21

- Regression page workflow-first layout: เรียง section ใหม่ตามขั้นตอนการทำงานจริง — Summary KPIs → Step Guide Strip → Alerts (ย้ายขึ้นบนสุดให้เห็น feedback ทันที) → Impact Analysis (ขั้นตอน 1) → Recommended Test Cases (ขั้นตอน 2) → Scheduled Regression / Trend+Activity / Baseline+History (ส่วนประกอบย้ายลงล่าง); เพิ่ม `.regression-steps` Step Guide Strip 3 ขั้นตอน (semantic button + `aria-current="step"`, สถานะ done ✓ / active คำนวณจาก selectedRelease+Build, impact, selectedCases, คลิก scrollIntoView ไปยัง section) responsive เป็นคอลัมน์เดียว ≤900px; section Analysis/Results มี `id` + step chip `.regression-step-chip`; ปุ่ม "วิเคราะห์ Impact" wrap ด้วย `.regression-analyze-action` พร้อม hint "เลือก Release และ Target Build ก่อน" เมื่อ disabled; กฎ Workflow-first layout เพิ่มในหัวข้อ 13 Accessibility และ UX Rules
- Module dropdown มาตรฐานเดียวทุกหน้า: สร้าง helper กลาง `buildModuleTree`/`renderModuleSelectOptions` (App.tsx) เป็น Single Source สำหรับ option แบบ Tree ทุกจุด — เรียงด้วย `ParentModuleId` → `SortOrder` → `ModuleCode` เหมือนหน้า Modules เสมอ, Root ใช้ class `module-root-option` (ตัวหนา สี `--text`) Child ใช้ `module-child-option` (สี `#475467`) พร้อม indentation `　` + สัญลักษณ์ `└` และ label `ModuleCode · Name`; CSS global `select option.module-root-option/.module-child-option` อยู่ใน styles.css ครอบคลุมทุก select; แก้การเรียงที่ยังใช้ชื่อ Module (`moduleName.localeCompare`) ใน Requirement form/filter, Test Case AI modal/form, Requirement create modal; เปลี่ยน dropdown ที่เคยเป็น flat list ไม่มี Tree ให้ใช้ helper เดียวกัน ได้แก่ Defect filter + Defect form modal + Test Suite "จัด Test Case" modal, และลบ logic tree ซ้ำซ้อนของ RTM filter/Link modal, Test Case filter optgroup และ Suite AI modal ให้เรียก helper เดียวกันทั้งหมด
- Requirement page filter & module ordering: ตัวกรอง Status/Priority เปลี่ยนจาก hard-code เป็นค่าที่พบจริงในข้อมูลของ Project context (คงลำดับ workflow `Draft→Review→Approved→Implemented→Cancelled` และ `P0–P3` ก่อน แล้วต่อท้ายด้วยค่านอกชุด) พร้อมแสดงจำนวนใน option เช่น `Draft (12)`; เพิ่ม Release filter (`releaseCode · version`, เฉพาะ Release ที่ไม่ Cancelled, แสดงเมื่อมี Project context และมี Release, รีเซ็ตเมื่อเปลี่ยน context); dropdown Module แก้ comparator เป็น `SortOrder` → `ModuleCode` ชุดเดียวกับหน้า Modules (เดิมใช้ชื่อ Module ซึ่งขัดกฎ tree) แสดง `ModuleCode · Name` และ optgroup ระบุ Project Code; แถวตารางเรียงตามลำดับ DFS ของ Module tree (`ParentModuleId`/`SortOrder`/`ModuleCode`) โดย Requirement ที่ไม่มี Module ไปอยู่ท้ายสุด fallback เรียงด้วย Requirement Code; เพิ่มคอลัมน์ Module (`.requirement-module` สี muted 11px) หลัง Title และ search ครอบคลุมชื่อ/รหัส Module; Mobile ยังใช้ card layout เดิมผ่าน `data-label`
- Dashboard Executive Timeline บนลิงก์แชร์: Executive Timeline ต้องแสดงใน Dashboard โหมด Share (`?s=` short code และ `?dashboardShare=` token) เหมือนหน้าภายใน; เพิ่ม backend endpoint แบบ anonymous `GET /api/v1/dashboard/shared/{code}/timeline` และ `GET /api/v1/dashboard/shared/timeline?token=` คืน `DashboardTimeline` (Releases: Draft/Testing/Ready + Cycles: Draft/InProgress พร้อม ProgressPercent) scope ตาม ProjectId/ReleaseId/BuildId ที่บันทึกไว้ใน share; ฝั่ง frontend `ExecutiveTimeline` รับ `shareCode`/`shareToken` และเปลี่ยนไป fetch shared timeline endpoint (ไม่แนบ Authorization) เมื่ออยู่โหมด share; ถ้าไม่มีข้อมูล timeline ยังคงซ่อน section เดิม
- Dashboard Module Overview totals + UI: หัวการ์ดต้องแสดงจำนวน Test Case รวมทั้งหมด (`module-overview-total` เป็นกล่องสรุปพื้นหลังฟ้าอ่อน `#f8faff` border `#dbe7ff` แสดงตัวเลขใหญ่สี primary, ป้าย "Test Cases ทั้งหมด" และจำนวน Modules/Root) และทุก Module ที่มี Submodules ต้องแสดงจำนวน Test Case แบบ rollup sum รวมทุกระดับลูก (คำนวณ client-side จาก `ParentModuleId` recursion); แถว Parent แสดง pill `.module-cases-pill` จำนวนรวมพร้อม tooltip แยก direct/submodules, บรรทัดรองอธิบาย "X ในโมดูลนี้ + Y จาก N Submodules", health badge `.health-badge` (healthy/watch/risk/nodata) และ module code chip; status bar คิดเปอร์เซ็นต์จาก Executed (Pass/Fail/Blocked) และแสดง "ยังไม่มีผล Execution" เมื่อยังไม่ execute; tree expand button หมุน 90° ด้วย class `.open` พร้อม `aria-expanded`/`aria-label`; responsive ≤760px head stack เป็นคอลัมน์, total เป็นแถวแนวนอน, pill ลดขนาด โดยไม่เกิด horizontal scroll
- Regression Phase 4 completion: Notification ของ Scheduled Regression ต้องมีปุ่ม `รับทราบ` เรียก acknowledge endpoint ต่อรายการและหายจากลิสต์ทันทีเมื่อสำเร็จ; การ์ด Scheduled Regression ต้องแสดงรายการ Schedule ที่เปิดอยู่ (ชื่อ + Release Code) พร้อมปุ่มปิดที่ยืนยันด้วย `window.confirm` ตาม pattern การลบของระบบ; Profile bar ต้องแยกปุ่ม `บันทึกใหม่` / `อัปเดต Profile` / `ลบ Profile` โดยอัปเดตได้เฉพาะ Profile ของตัวเอง (`isOwner`) และเมื่อเลือก Profile ของตัวเองต้อง prefill ชื่อกับ visibility, Shared profile ต้องต่อท้ายชื่อด้วย `(Shared)`; profile bar เป็น grid 6 คอลัมน์บน Desktop และ stack เป็นคอลัมน์เดียวบน Mobile พร้อม schedule list/notification action ที่ไม่ทำให้หน้าเกิด horizontal scroll
- Regression Phase 4: Profile/Template ต้องบันทึกในฐานข้อมูลและเลือก visibility แบบ Owner/Private หรือ Shared with Team; Recommended Test Cases ต้องมี action เลือกทั้งหมดทุกหน้าจาก Server และ Export ทุกหน้าพร้อม Risk Score; Scheduled Regression แสดง notification เมื่อมี Active Build ใหม่ โดย section และ action ทั้งหมดต้อง stack เป็นคอลัมน์เดียวบน Mobile
- Regression Release selector ต้องแสดงเฉพาะ Release ที่ Active (`status != Cancelled`) และต้องล้าง context ของ Release/Build ที่ถูกปิดใช้งาน พร้อมเลือก Active Release แรกเมื่อมีข้อมูล
- Regression Phase 3: Recommended Test Cases ใช้ server-side pagination ขนาด 25/50/100/200 รายการและคงรายการที่เลือกข้ามหน้า, แสดง Risk Score พร้อมปรับน้ำหนัก Direct Impact/Historical Defect/Critical Priority/Shared Dependency, บันทึกและเรียกใช้ Regression Profile จากเครื่องผู้ใช้, และเพิ่ม Dashboard สำหรับแนวโน้มการวิเคราะห์กับ Recent Activity; dashboard/profile/risk/pagination ต้อง responsive เป็นคอลัมน์เดียวบน Mobile และห้ามสร้าง page-level horizontal scroll
- Regression Phase 2: รายการ Recommendation จำนวนมากใช้ CSS content virtualization (`content-visibility` + intrinsic size), action bar เปิด Test Cycle/Execution พร้อมส่ง Cycle context อัตโนมัติและต้อง wrap บนพื้นที่แคบ; สิทธิ์แยกเป็น `REGRESSION.VIEW`/`REGRESSION.MANAGE` และ action สำคัญต้องบันทึก Regression Activity audit
- Regression Phase 1: เพิ่ม Regression History แบบถาวร, Baseline Comparison ระหว่าง Build, Export CSV/Excel จากรายการที่กรอง และตัวกรอง Last Result/เคยพบ Defect; layout ใช้การ์ด 2 คอลัมน์บน Desktop และ 1 คอลัมน์บน Mobile โดย action และ filter ต้อง wrap/stack โดยไม่ทำให้หน้าเกิด horizontal scroll
- Regression recommendations: แยก checkbox สำหรับเลือก Test Case ออกจากลิงก์ Test Case Code สำหรับเปิดรายละเอียดแบบ read-only modal โดยใช้รูปแบบ Test Case detail เดิม รองรับ keyboard focus และ mobile full-screen modal

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

### 2026-08-22

- Runner status ใช้ card grid `auto-fit/minmax(220px,1fr)`; Connectivity และ workload ใช้ Badge แยกเพื่อไม่รวมความหมาย Online กับ Busy
- Mobile ≤760px ให้ section header stack และ agent grid เป็นหนึ่งคอลัมน์; machine/capability ใช้ `overflow-wrap:anywhere`
- Automation schedule form ใช้ Unified modal pattern และ `.form-grid` แทน inline form; alert ใช้ Badge สีเหลืองสำหรับ retry และสีแดงสำหรับ terminal failure

### Automation Control Center UI — 2026-08-22

- หน้า Automation ใช้ operational hero สีน้ำเงินเข้มเพื่อรวม page purpose, Runner/Queue/Schedule health และ primary action “สั่งรันทันที”; secondary action ต้องใช้ contrast ที่ผ่านบนพื้น gradient
- ใช้ sticky section navigation สำหรับ Runner, Schedule, Queue, Quality Gate, Test Cases และ Run History โดยต้อง scroll แนวนอนภายใน nav เท่านั้นบน mobile
- KPI card ใช้ accent bar แทนการลงสีทั้ง card; สีเขียวสื่อ Ready, น้ำเงินสื่อ target และเหลืองสื่อรายการที่ต้องตรวจสอบ
- Operational sections ใช้ header gradient บาง, radius 17px และ shadow ระดับต่ำร่วมกัน; nested agent/gate cards ใช้พื้น `#f5f7fb` เพื่อแยกลำดับชั้น
- Breakpoints: ≤1100px hero stack; ≤760px ทุก section/header/list และ modal form stack เป็นหนึ่งคอลัมน์; ≤420px KPI เป็นหนึ่งคอลัมน์
- Form control ภายใน Automation (`input`, `select`, `textarea`) ใช้ความสูงมาตรฐาน 42px, radius 9px, border `#d0d5dd`, น้ำหนักข้อความ 400 และ primary focus ring แบบเดียวกับฟอร์มหลักของระบบ
- การสร้าง Schedule ต้องเปิดผ่านปุ่ม `+ เพิ่ม Schedule` ใน section header และแสดงใน Unified modal เพื่อลดความหนาแน่นของหน้า; Alerts ยุบไว้ก่อนด้วย `details/summary` และยังเข้าถึงได้ด้วย keyboard

### Test Case page — bulk status & Automation Candidate (2026-08-22)

- หน้า Test Case เพิ่มคอลัมน์เลือก (checkbox) คอลัมน์แรก `.tc-select-col` (ความกว้าง 42px, `accent-color:var(--primary)`) สำหรับเลือกหลายแถว; หัวตารางมี select-all ของหน้าปัจจุบัน (`toggleTcSelectPage`) และแต่ละแถวมี checkbox (`toggleTcSelect`) พร้อม highlight แถวที่เลือก (`.is-selected` พื้น `#eef4ff`)
- เพิ่ม bulk action bar `.testcase-bulk-bar` (พื้นฟ้าอ่อน ขอบ `#cddcff`) แสดงเมื่อมีรายการถูกเลือก: กำหนดสถานะ (Draft/Review/Ready/Deprecated) และกำหนด Automation Candidate (เป็น Candidate/Manual) แล้วกดปุ่มกำหนดเพื่ออัปเดตทีละหลายรายการ; ปุ่ม "ยกเลิกเลือก" เคลียร์การเลือก (ทำงานเมื่อ `canEdit` เท่านั้น)
- Backend เพิ่ม endpoint `POST /api/v1/test-cases/{id}/automation` (body `{automationCandidate:bool}`) และ domain method `TestCase.SetAutomationCandidate` เพื่อตั้งค่า Automation Candidate โดยไม่ต้องส่ง steps ใหม่ (ป้องกันการลบ Steps); สถานะใช้ endpoint `POST /test-cases/{id}/status` ที่มีอยู่แล้ว ทั้งคู่วนลูปเรียก API ทีละรายการแล้ว reload หน้าปัจจุบัน
- Bulk กำหนด Automation Candidate ใช้ endpoint ที่มีอยู่แล้ว (`GET /test-cases/{id}` ดึง full รวม Steps แล้ว `PUT /test-cases/{id}` ส่ง `automationCandidate` ใหม่) วนลูปทีละรายการแล้ว reload หน้าปัจจุบัน — จึงทำงานได้โดยไม่ต้องรัน backend ใหม่ (endpoint `POST /test-cases/{id}/automation` ที่เพิ่มไว้ยังคงใช้ได้หากรัน backend ใหม่)
- แก้ปุ่ม "แก้ไข" ไม่เปิดฟอร์ม: `openForm` เดิมใช้ `item?.steps.length` ซึ่งโยน TypeError เมื่อรายการจากตารางไม่มี field `steps` (list endpoint ไม่คืน steps) จึงคราส handler ก่อนเปิด modal; เปลี่ยนเป็น `item?.steps?.length` และให้ `openForm` ดึงรายละเอียดเต็ม (`GET /test-cases/{id}` ที่มี steps) มาก่อน populate ฟอร์มแก้ไขเพื่อให้บันทึกได้โดยไม่สูญเสีย Steps
### Test Summary Executive View — 2026-09-03

- Test Summary เพิ่ม Executive Snapshot แบบ read-only สำหรับผู้บริหาร โดยรวม recommendation, release context, timestamp, test status distribution และ risk signals จากข้อมูล summary เดิม
- ใช้ section heading/eyebrow และ accent risk cards เพื่อแยกข้อมูลสำหรับการตัดสินใจออกจาก narrative ที่ผู้ใช้แก้ไขได้ โดยไม่เปลี่ยน API หรือ workflow เดิม
- Executive content ต้อง responsive: desktop แบ่งสองคอลัมน์ และ mobile stack เป็นคอลัมน์เดียว พร้อม `overflow-wrap:anywhere` สำหรับ URL และ scope ที่ยาว
### 2026-09-03 — Execution Workspace responsive styling

- Execution Workspace-only responsive primitives are scoped in `ExecutionWorkspace.css`, including `min-width: 0`, `overflow-wrap: anywhere`, flexible action wrapping, and mobile touch targets.

### 2026-09-03 — Automation responsive action layout

- Automation section headers and action groups stack/wrap at mobile widths so controls remain reachable without page-level horizontal scrolling.
### 2026-09-16 — Test Cycle Clone to New Target (Implemented)

- Test Cycle Clone ใช้แนวคิด `Clone as New Cycle`: Source Cycle เป็น read-only reference และ Target ใช้ Cycle/Case ID ใหม่เสมอ เพื่อไม่เขียนทับ Execution History
- Clone modal แบ่งข้อมูลเป็น Source Summary, Target Scope และ Clone Options; Target Release → Build เป็น dependent selector และ Environment ต้องกรองตาม Project/Active
- แสดง preview จำนวน Test Case และ warning ที่อ่านได้ชัดเจนว่า Execution, Evidence, Assignment และผลเดิมไม่ถูกคัดลอก
- ปุ่ม Clone ใช้ permission/disabled state และต้องมี inline loading/error/success feedback ตามมาตรฐาน modal เดิม
- Desktop ใช้ modal สูงสุด 900px แบบ 2 คอลัมน์; Mobile ≤760px ใช้ full-screen modal, form 1 คอลัมน์, header/footer sticky และ action group wrap ได้ โดยห้ามเกิด page-level horizontal scroll
- Detail ของ Target แสดง lineage badge/text `Cloned from <Source Cycle>` และใช้สี/Badge ตาม status เดิมของระบบ

### 2026-09-18 — Defect module ranking

- หน้า Defect แสดงอันดับจำนวน Defect รายโมดูลจาก `GET /defects/stats` ตาม Project/Release/Build context เดียวกับ KPI โดยรวมทุกสถานะและไม่ผูกกับตัวกรองของตารางที่แบ่งหน้า
- เรียงจำนวนมากไปน้อย (จำนวนเท่ากันเรียง Module Code) พร้อมลำดับ จำนวน และแถบเทียบกับอันดับแรก; รวมกลุ่มไม่ระบุโมดูลและโมดูลที่ถูกลบเพื่อให้ยอดรวมตรงกับ Total
- รายการใช้ `min-width: 0` และ `overflow-wrap: anywhere`; บน Mobile หัวการ์ด wrap และไม่ทำให้เกิด page-level horizontal scroll
- คลิกอันดับโมดูลได้ทั้งเมาส์และ keyboard เพื่อกรองตาราง Defect ตามโมดูลนั้นและเลื่อนไปยังรายการ; ล้าง search และตัวกรองอื่นก่อนแสดงผล รองรับกลุ่มไม่ระบุโมดูลด้วย

### 2026-09-18 — Defect module A4 PDF export

- หน้า Defect มีปุ่มส่งออก PDF A4 ในหัวการ์ดอันดับโมดูลสำหรับผู้มีสิทธิ์ REPORT.EXPORT; ปุ่มปิดระหว่างโหลดสรุปหรือสร้างไฟล์ และแสดงสถานะกำลังสร้าง
- รายงานใช้ Project/Release/Build context และข้อมูล summary ทั้งหมด ไม่อิงรายการตารางที่แบ่งหน้า; แสดงยอดสำคัญ โมดูลอันดับหนึ่ง และอันดับครบทุกโมดูล
- รายงานแบ่งหน้าตามความสูงจริงของแถว มีหัวหน้าต่อและเลขหน้าทุกหน้า; ใช้สีและตัวอักษรสอดคล้องกับ design tokens และรองรับชื่อโมดูลภาษาไทยที่ยาว

### 2026-09-20 — Execution Workspace inline Defect management

- ปุ่ม `+ Defect` ของ Step ที่ Fail เปิด Unified modal ภายใน Execution Workspace พร้อมข้อมูล Test Case, Step, Cycle, Build, Environment และ Tester ที่เติมให้อัตโนมัติ ผู้ใช้แก้ Title, Severity, Status, Description, Steps to Reproduce, Expected และ Actual Result ก่อนบันทึกได้
- Modal รองรับรูป PNG/JPG/WebP สูงสุด 5 รูป รูปละไม่เกิน 5 MB รวมไม่เกิน 20 MB พร้อม thumbnail, ลบรูปก่อนบันทึก และจัดการรูปของ Defect เดิม
- แสดง Linked Defects ของ Test Case เหนือรายการ Step และเปิดแก้ไขรายละเอียด/รูปได้โดยไม่ออกจาก Workspace เฉพาะผู้มีสิทธิ์ `DEFECT.EDIT`
- Desktop ใช้ modal สูงสุด 900px; Mobile ≤760px ใช้ full-screen modal, form หนึ่งคอลัมน์ และ image grid สองคอลัมน์โดยไม่เกิด page-level horizontal scroll

### 2026-09-21 — Defect workspace visual hierarchy

- หน้า Defect ใช้ลำดับการอ่าน 4 ช่วง: context ของข้อมูล, KPI สุขภาพรวม, ranking โมดูล และ Defect queue เพื่อให้เริ่มจากภาพรวมแล้วลงไปที่งานที่ต้องทำได้เร็วขึ้น
- Ranking โมดูลแสดงอันดับต้น ๆ ก่อนและมีปุ่มขยายรายการทั้งหมด; queue แยกหัวข้อกับ filter bar พร้อมปุ่มล้างตัวกรองเมื่อมี filter active
- เพิ่ม responsive rules สำหรับ intro/context, filter controls และ queue header ที่ breakpoint 760px; ตารางยังเลื่อนได้ภายใน table wrapper โดยไม่ทำให้เกิด page-level horizontal scroll

### 2026-09-21 — Test Summary module readiness

- หน้า Test Summary แสดงโมดูลที่ยังไม่เริ่มทดสอบและโมดูลที่ทดสอบแล้วแต่ยังไม่ครบ โดยใช้ข้อมูล Module Health จาก Summary เดียวกับ Execution Progress
- รายการแสดง Module Code, ชื่อโมดูล, จำนวน Test Case และสัดส่วนที่ Execute แล้ว พร้อมสถานะว่างเมื่อไม่มีโมดูลค้าง
- Layout แบ่งเป็น summary highlight และรายการสองกลุ่ม; บนจอเล็กจัดเป็นคอลัมน์เดียวและตัดข้อความยาวภายใน card

### 2026-09-21 — Defect priority filter

- หน้า Defect เพิ่มตัวกรอง Priority แยกจาก Severity และรองรับตัวเลือก P0–P3 เพื่อให้ค้นหารายการตามความสำคัญได้ตรงความหมาย
- ตัวกรอง Priority ใช้ความสำคัญของ Test Case ที่เชื่อมโยงกับ Defect และยังคง responsive โดยไม่ทำให้เกิด horizontal scroll ระดับหน้า

### 2026-09-21 — Test Summary release impact

- เพิ่มส่วน Release Impact ใน Executive View เพื่อสรุประดับผลกระทบต่อการส่งมอบ โมดูลที่มีสัญญาณความเสี่ยง กำหนด Release และ Build ที่ใช้ประเมิน
- แสดง Change Notes, Known Issues และข้อเสนอการจัดการจากข้อมูล Release, Build และ Test Summary ล่าสุด โดยข้อความยาวต้อง wrap ได้และไม่ทำให้เกิด horizontal scroll ระดับหน้า
- รองรับ responsive layout: ข้อมูลผลกระทบและโมดูลเรียงเป็นคอลัมน์เดียวบนหน้าจอแคบ และใช้สีตามระดับ High/Medium/Low/Unknown ให้สอดคล้องกับสถานะคุณภาพเดิม

### 2026-09-21 — Test Cycle detail Test Case list

- หน้า Test Cycle Detail แสดงรายการ Test Case ใน Cycle พร้อมลำดับ, Code, Title, Priority, Module และสถานะล่าสุด
- แสดง Expected Result ของ Step แรกในแต่ละ Test Case เป็นข้อความประกอบแบบตัดไม่เกิน 3 บรรทัด พร้อม title สำหรับดูข้อความเต็ม
- แสดง Actual Result จากผล Execute ล่าสุดของ Test Case คู่กับ Expected Result และใช้สีฟ้าเพื่อแยกผลที่เกิดขึ้นจริงจากผลที่คาดหวัง
- รายการโหลดจาก Execution Workspace endpoint เดิม และจำกัดความสูงด้วย internal scroll เพื่อไม่ให้ Modal ยาวจนใช้งานยากเมื่อมีหลายเคส
- เพิ่มสถานะโหลด/ผิดพลาด/ไม่มีข้อมูล และ responsive layout สำหรับหน้าจอแคบโดยไม่ทำให้เกิด page-level horizontal scroll

### 2026-09-21 — Test Suite context scope

- Test Suite ใช้ขอบเขตระดับ Project จึงให้ตัวกรอง Project ด้านบนมีผลกับรายการโดยตรง
- Release และ Build ไม่กรองรายการ Test Suite เพราะ Suite เป็นชุดทดสอบที่นำกลับมาใช้ซ้ำได้; สองบริบทนี้จะถูกใช้เมื่อเลือก Suite ไปสร้าง Test Cycle
- เพิ่มข้อความอธิบายขอบเขตบนหน้า Test Suite เพื่อไม่ให้ผู้ใช้เข้าใจว่า Release/Build กำลังกรอง Suite อยู่
- ในรายละเอียด Test Suite ให้แสดง Release/Build ที่ถูกใช้งานผ่าน Test Cycle พร้อมสรุปแบบไม่ซ้ำ และแสดงข้อมูลเดียวกันในรายงานส่งออก
