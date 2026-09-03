# QA Hub UI System Remediation Specification

เอกสารนี้เป็นแผนพัฒนาปรับปรุง UI/UX และความสอดคล้องของระบบ QA Hub ทุกหน้า โดยเรียงลำดับจากเร่งด่วนมากไปน้อย แบ่งเป็น Phase เพื่อให้ทีมพัฒนานำไปแตกงานและตรวจรับได้

วันที่จัดทำ: 2026-09-03  
ขอบเขต: React Frontend, API data contract ที่เกี่ยวข้องกับการแสดงผล, responsive, accessibility, permission-aware UI และเอกสารกำกับการพัฒนา

## หลักการสำคัญ

- ต้องไม่ทำลาย workflow/API เดิมโดยไม่จำเป็น
- ทุกหน้าต้องมี loading, empty, error และ unauthorized state ที่สื่อความหมาย
- Project/Release/Build context ต้องเป็นแหล่งอ้างอิงเดียวกันทั้งระบบ
- ค่าที่ใช้ใน dropdown ต้องมาจาก Master Settings/API ไม่ hard-code
- การคำนวณ KPI, Gate, Coverage, Pass Rate และ Risk ต้องตรงกันระหว่าง Dashboard, Test Summary และ Release Sign-off
- การแก้ UI ต้องตรวจ Desktop และ Mobile และไม่ทำให้เกิด horizontal scroll ระดับหน้าโดยไม่จำเป็น
- Action ที่เปลี่ยนหรือลบข้อมูลต้องมี permission, loading/disabled state และ confirmation ตามความเสี่ยง

## สถานะปัจจุบันที่ต้องคำนึงถึง

- หน้า Test Summary มีการเพิ่ม Executive Snapshot, Test Case status breakdown และ Risk Signals แล้วใน `App.tsx`/`TestSummary.css`
- มีการเพิ่มข้อมูล pattern ดังกล่าวใน `UI_DESIGN_SYSTEM.md`
- Frontend build ผ่านและ lint ผ่านโดยมี warning เดิมเรื่อง React Hook dependency ที่ `App.tsx:4229`
- พบความเสี่ยง data contract: backend ส่ง `generatedAt` ระดับ root ของ response แต่ frontend อ่านจาก `summary.generatedAt`
- เมนู Audit Log ยังมีเส้นทางที่ fallback ไป `EmptyPage`
- Screen Specification ระบุ section ของ Test Summary มากกว่าที่ UI แสดงอยู่ในปัจจุบัน

---

# Phase 1 — Critical Governance & Data Integrity

ระดับ: เร่งด่วนมาก  
เป้าหมาย: ป้องกันข้อมูลผู้บริหารคลาดเคลื่อน และป้องกันการตัดสินใจ Release จากข้อมูลไม่ตรงกัน

## P1.1 แก้ Test Summary generated timestamp

ขอบเขต:

- ตรวจ response `GET /releases/{releaseId}/test-summary`
- ปรับ frontend ให้อ่าน `generatedAt` จากระดับ root ของ response
- กำหนด type response แยกจาก `TestSummaryData` ให้ชัดเจน
- แสดง fallback ที่อ่านได้เมื่อไม่มี timestamp

Acceptance Criteria:

- วันที่/เวลาที่แสดงบน Test Summary ตรงกับ `GeneratedAt` จาก API
- Export CSV/Excel ใช้ timestamp ชุดเดียวกัน หากมีการเพิ่ม field ใน export
- ไม่เกิด `Invalid Date` หรือแสดง `-` เมื่อ API ส่งวันที่ที่ถูกต้อง
- มี test หรือ manual verification กรณี response ไม่มี `generatedAt`

## P1.2 ทำให้ Dashboard, Test Summary และ Sign-off ใช้ Quality Rules เดียวกัน

ขอบเขต:

- Requirement Coverage
- Execution Progress
- Pass Rate
- Open P0/P1
- Critical/High Defects
- Regression Pass Rate
- Approved Risks
- Overall Decision: GO / CONDITIONAL GO / NO-GO

Acceptance Criteria:

- ค่าเดียวกันใน 3 หน้าหลักไม่ขัดแย้งกันเมื่อใช้ Project/Release/Build เดียวกัน
- นิยาม Hard Block และ Warning ระบุใน UI
- GO ไม่สามารถผ่านเมื่อมี Hard Block ตาม policy
- CONDITIONAL GO ต้องตรวจ Approved Risk ตาม rule
- กรณีไม่มีข้อมูลแสดง `NO DATA` พร้อมคำอธิบาย ไม่แสดงเป็น 0 ที่ทำให้เข้าใจผิด

## P1.3 แก้ Context และ stale data ทุกหน้า

ขอบเขต:

- Project → Release → Build dependency
- Test Summary, Risk Acceptance, Sign-off, Regression, Execution, Defect และ Automation
- reset ค่า child selector เมื่อ parent เปลี่ยน
- reload data เมื่อ context เปลี่ยน

Acceptance Criteria:

- ไม่สามารถเลือก Release ที่ไม่อยู่ใน Project ปัจจุบัน
- ไม่สามารถเลือก Build ที่ไม่อยู่ใน Release ปัจจุบัน
- ไม่แสดงข้อมูลจาก context เดิมค้างหลังเปลี่ยน selection
- ทุกหน้ามี loading state ระหว่าง context transition
- URL/hash/local storage ไม่ทำให้ context เก่ากลับมาโดยไม่ตรวจสอบ

## P1.4 ตรวจ Governance permissions และ destructive actions

ขอบเขต:

- Risk submit/approve/reject/close
- Release Sign-off
- Release status
- Defect transition/close/delete
- User/Role changes
- Automation run/delete

Acceptance Criteria:

- ปุ่มที่ไม่มีสิทธิ์ไม่แสดงหรือ disabled ตาม policy ที่กำหนด
- ทุก action แสดง saving/loading state
- ทุก destructive action มี confirmation
- Error จาก permission แสดงข้อความที่เข้าใจได้
- หลัง action สำเร็จข้อมูล refresh และไม่แสดง stale state

## P1.5 เพิ่ม Audit Log ขั้นพื้นฐาน

ขอบเขต:

- หน้า Audit Log ต้องไม่ fallback เป็น Placeholder
- แสดง timestamp, user, action, entity, summary
- รองรับ filter, pagination และ detail read-only

Acceptance Criteria:

- เปิดเมนู Audit แล้วเห็นข้อมูลจาก API จริงหรือ empty state ที่ระบุชัดเจน
- ค้นหา/filter/pagination ทำงานจริง
- รายการ governance สำคัญมี audit entry
- ไม่สามารถแก้ไขหรือลบ audit record จาก UI

---

# Phase 2 — Executive Reporting Completeness

ระดับ: เร่งด่วนสูง  
เป้าหมาย: ทำให้ Test Summary พร้อมใช้เป็นเอกสารสำหรับผู้บริหารและ Sign-off

## P2.1 เติม Test Summary ให้ตรง Screen Specification

ต้องมี section ต่อไปนี้:

- Executive Summary
- Scope
- Out-of-Scope
- Environment
- Metrics
- Requirement Coverage
- Defects
- Regression
- Installation/Update
- Performance
- Known Issues
- Remaining Risks
- QA Recommendation

Acceptance Criteria:

- ทุก section มีข้อมูลจริง, empty state หรือข้อความ `ยังไม่ได้ระบุ`
- แยก calculated data กับ narrative ที่ผู้ใช้แก้ไขได้อย่างชัดเจน
- ข้อมูลที่แก้ไขได้ถูกเก็บต่อ Release และไม่หายเมื่อ reload
- section ที่เกี่ยวกับ decision มี label/คำอธิบายสำหรับผู้บริหาร

## P2.2 เพิ่ม Evidence Breakdown

ขอบเขต:

- Test Case status: Passed, Failed, Blocked, Skipped, Not Executed
- Requirement: Total, Covered, Partial, Not Covered, Out of Scope
- Defect: Total, Open, Critical, High, Resolved/Closed
- Regression: planned, executed, passed, failed, blocked

Acceptance Criteria:

- จำนวนรวมของแต่ละ breakdown รวมกันตรวจสอบได้
- อัตราส่วนไม่เกิน 100% และไม่ติดลบ
- กรณี total เป็น 0 ไม่เกิด NaN/Infinity
- แสดง source/time ของข้อมูลล่าสุด

## P2.3 เพิ่ม Release decision panel

ต้องแสดง:

- Recommended Decision
- Decision rationale
- Hard blockers
- Warnings
- Approved risks
- Next action
- Link ไป Risk Acceptance และ Sign-off

Acceptance Criteria:

- ผู้บริหารเข้าใจเหตุผลของ GO/CONDITIONAL GO/NO-GO ได้โดยไม่ต้องเปิดหลายหน้า
- P0/Critical แสดงเด่นและมีคำแนะนำถัดไป
- ปุ่ม Sign-off รักษา Project/Release context เดิม

## P2.4 ทำ Export ให้ครบ

ขอบเขต:

- CSV
- Excel
- PDF/Print-ready report ตาม Screen Specification

Acceptance Criteria:

- Export มีข้อมูลครบทุก section ที่แสดงบนหน้า
- ชื่อไฟล์มี Release code/version และ timestamp ที่ปลอดภัย
- ภาษาไทยไม่เสีย encoding
- PDF ไม่มี card ถูกตัดหรือ table ล้นขอบกระดาษ
- Export เคารพ `REPORT.EXPORT`

---

# Phase 3 — Core Test Management Consistency

ระดับ: สูง  
เป้าหมาย: ทำให้ข้อมูลตั้งแต่ Requirement ถึง Execution มี traceability และ interaction ที่สม่ำเสมอ

## P3.1 Project/Module

- ทำ tree selector กลาง
- เรียงด้วย `ParentModuleId`, `SortOrder`, `ModuleCode`
- แสดง code + name + indentation
- ป้องกัน deactivate Module ที่มีข้อมูลอ้างอิงโดยไม่แจ้งผลกระทบ
- ตรวจ mobile long name และ child indentation

Acceptance Criteria: ทุกหน้าที่มี Module selector แสดง tree และ ordering เดียวกัน

## P3.2 Release/Build

- ใช้ Active-only selector rule กลาง
- แสดง status ใน option/detail
- ตรวจ Build parent relationship
- ป้องกัน action บน Closed/Cancelled Release
- แสดง release scope, owner, date และ readiness

Acceptance Criteria: ไม่มีหน้าใดเลือก Release/Build ที่ผิด context ได้

## P3.3 Requirement/RTM

- แยก Status กับ In Scope
- เพิ่ม coverage reason
- แสดง Requirement ที่ไม่มี Test Case
- แสดง Test Case ที่ยังไม่ Execute
- เพิ่ม revision comparison/history
- รองรับ drill-down Requirement → Test Case → Defect
- ปรับ RTM mobile เป็น expandable card

Acceptance Criteria: Coverage ใน RTM ตรงกับ Test Summary และอธิบายได้ว่าทำไมแต่ละรายการจึง Covered/Partial/Not Covered

## P3.4 Test Case/Test Suite

- ตรวจ pagination/search/filter server-side
- ตรวจ Steps ไม่หายจาก list edit
- แสดง Required/Optional และจำนวนรวมใน Suite
- ป้องกัน Test Case ซ้ำ
- เพิ่ม keyboard alternative สำหรับ drag/drop
- แสดง revision, latest execution และ linked entities
- ปรับตารางเป็น card บน mobile

Acceptance Criteria: การแก้ไข/จัด Suite/เลือกหลายรายการไม่ทำให้ข้อมูลสูญหายหรือใช้งานต่อไม่ได้

## P3.5 Test Cycle/Execution

- ตรวจ Release/Build/Environment validation
- แยก Active กับ Historical execution
- ป้องกันแก้ execution ที่ปิดแล้ว
- แสดงผู้บันทึก, timestamp, comment และ evidence
- เพิ่ม error/retry state ตอนบันทึกผล
- ตรวจ status transition และ assignment

Acceptance Criteria: ผล Execution ทุกตัว trace กลับไป Test Case, Cycle, Release, Environment และผู้บันทึกได้

## P3.6 Defect/Regression

- รวม filter pattern
- แสดง P0/P1/Critical/High ชัดเจน
- แสดง linked Requirement/Test Case/Execution
- อธิบาย regression risk score
- ตรวจ pagination และ selection ครบทุกหน้า
- แสดง baseline comparison และ audit activity

Acceptance Criteria: Defect และ Regression evidence เชื่อมกลับไปยัง Release decision ได้

---

# Phase 4 — Operational Automation Quality

ระดับ: สูง แต่แยกเป็น workstream  
เป้าหมาย: ทำให้ Automation ใช้งานปฏิบัติการได้ปลอดภัย ตรวจสอบย้อนกลับได้ และไม่สร้าง false confidence ให้ผู้บริหาร

## P4.1 Runner/Queue/Execution

- แสดง Runner connectivity, status, heartbeat และ workload
- แสดงเหตุผล Queue ค้าง/ไม่มี Agent
- ป้องกัน run เมื่อ version/DSL/environment ไม่พร้อม
- แสดง Build, Environment, Agent และ Version ที่ใช้จริง
- รองรับ cancel/retry พร้อม disabled state

## P4.2 DSL/Object/Evidence

- แสดง validation result และข้อผิดพลาดที่แก้ได้
- แสดง Object verification status
- แสดง evidence loading/error/empty state
- ตรวจไม่ให้ URL, machine name หรือ capability ยาวทำให้ layout แตก

## P4.3 Retry/Failure/Maintenance

- บังคับ Retry Safety: Safe, Conditional, Unsafe
- แสดง failure classification
- แยก maintenance required กับ execution failed
- แสดง owner, resolution note และ target fix
- ตรวจการสร้าง Defect จาก automation failure

## P4.4 Schedule/Data/Webhook

- ตรวจ timezone และ schedule state
- แสดง notification read/unread
- ป้องกัน secret/credential ใน UI และ log
- ตรวจ Snapshot, Seed, Cleanup และ Environment Data Profile
- เพิ่ม confirmation สำหรับ hard-delete

Acceptance Criteria:

- Automation execution ทุกตัวมีสถานะและหลักฐานตรวจสอบได้
- การ retry ไม่ทำ action ที่ Unsafe โดยอัตโนมัติ
- ไม่มี credential ปรากฏในหน้าจอ, export หรือ error message

---

# Phase 5 — Cross-cutting UX, Accessibility & Maintainability

ระดับ: ปานกลางถึงสูง  
เป้าหมาย: ทำให้ทุกหน้ามีประสบการณ์และมาตรฐานเดียวกัน

## P5.1 Shared UI patterns

สร้าง/ใช้ component กลางสำหรับ:

- Project/Release/Build selector
- Module tree selector
- Status badge
- Loading state
- Empty state
- Error state
- Pagination
- Confirmation modal
- Toast/inline alert
- Date/time formatter
- Export action

## P5.2 Accessibility

- label ต้องสัมพันธ์กับ input
- icon button ต้องมี accessible name
- focus visible ทุก interactive control
- keyboard navigation ครบ
- modal รองรับ Escape และ focus management
- สีไม่เป็นตัวบ่งชี้เพียงอย่างเดียว
- error message เชื่อมกับ field
- table/card อ่านตามลำดับที่ถูกต้อง

## P5.3 Responsive

ตรวจที่:

- Desktop wide
- Tablet
- 900px
- 760px
- 560px
- 420px

Acceptance Criteria:

- ไม่มี page-level horizontal scroll ที่ไม่จำเป็น
- ตารางยาวมีวิธีอ่านบน mobile
- modal ไม่ล้น viewport
- action buttons ไม่ถูกตัดหรือกดซ้อนกัน

## P5.4 Error and feedback consistency

- ลดการใช้ `window.alert`
- ใช้ inline alert/toast/modal ตามประเภท action
- แสดง server error ที่เข้าใจได้
- มี retry สำหรับ read failure
- มี save success/failure state
- ไม่ใช้ `0` แทน error หรือไม่มีข้อมูลโดยไม่มีคำอธิบาย

## P5.5 Code quality

- แยก page component จาก data fetching logic
- ลด duplicated fetch/context logic
- ใช้ typed API response
- แก้ lint warning ที่เกี่ยวข้องกับส่วนที่แก้ไข
- เพิ่ม unit/component tests ใน critical governance flow

---

# Phase 6 — Validation, QA และ Release Readiness

ระดับ: หลังการพัฒนาทุก Phase  
เป้าหมาย: ยืนยันว่า UI ที่ปรับไม่กระทบการใช้งานเดิม

## Required checks

```powershell
cd src/ProMaxx2.QA.Web
npm.cmd run build
npm.cmd run lint
cd ../..
git diff --check
```

## Manual test matrix

### Governance flow

- เลือก Project → Release → Build
- เปิด Test Summary
- Generate/Regenerate
- แก้ Narrative
- Export CSV/Excel/PDF
- เปิด Risk Acceptance
- Submit/Approve Risk
- เปิด Release Sign-off
- ตรวจ GO/CONDITIONAL GO/NO-GO

### Traceability flow

- Requirement → RTM → Test Case → Test Suite → Test Cycle → Execution → Defect → Regression → Summary → Sign-off

### Permission flow

- ผู้ไม่มีสิทธิ์ดู
- ผู้มีสิทธิ์แก้ไข
- ผู้มีสิทธิ์ approve/sign-off
- ผู้มีสิทธิ์ export
- Admin และ non-admin

### Responsive flow

- Desktop 1440px
- Tablet 768px
- Mobile 390px
- ตรวจทุกหน้าหลักและ modal ที่เกี่ยวข้อง

## Definition of Done

- Acceptance Criteria ของ Phase ที่ทำเสร็จผ่านทั้งหมด
- ไม่มี regression ใน workflow เดิม
- API contract และ frontend type ตรงกัน
- ทุกหน้ามี state สำคัญครบ
- ตรวจ accessibility และ responsive แล้ว
- Build/lint/diff check ผ่าน
- อัปเดต Screen Specification/UI Design System/API documentation เมื่อ behavior เปลี่ยน
- มีหลักฐานการทดสอบหรือ screenshot/ผลตรวจสำหรับการส่งมอบ

## Recommended implementation order

1. P1.1 generatedAt contract
2. P1.2 shared quality rules
3. P1.3 context/stale data
4. P1.4 permission and destructive actions
5. P1.5 Audit Log
6. P2.1–P2.4 Test Summary completeness/export
7. P3 core test traceability
8. P4 Automation operational quality
9. P5 shared UX/accessibility/responsive
10. P6 final validation and regression testing

## Implementation Progress — 2026-09-03

- Completed P1.1–P1.3: generated timestamp/export consistency, shared 90% quality threshold, and Project/Release/Build context reset with transition loading state.
- Completed P1.4: permission guard and destructive-action confirmation improvements for Defect bulk actions and related workflows.
- Started P1.5: added the read-only `GET /api/v1/audit-logs` endpoint and wired the Audit Log menu to a real page with search, Entity filter, pagination, actor/action/entity/summary display, loading/error/empty states. The endpoint currently aggregates persisted Defect and Regression activity records; a dedicated cross-domain audit writer and Governance/User/Role event coverage remain as follow-up work before marking the full acceptance criteria complete.
- Completed P2.1–P2.4: Test Summary report sections, evidence breakdown, release decision panel, and CSV/Excel export completeness.
- Completed P3.3, P3.5, P3.6: RTM context validation, closed Execution protection, and Regression Build/Release validation.
- Completed P3.1: Project module tree keeps ParentModuleId/SortOrder/ModuleCode ordering and now blocks deactivation when Requirements, Test Cases, or Defects still reference the Module, with an actionable API error.
- Completed P3.2: Release selectors now exclude Closed/Cancelled records, Build actions are disabled for closed context, and Release detail continues to expose status, scope, planned date, and Build readiness information.
- In progress P3.3: RTM now applies a consistent Coverage reason model and supports filtering Requirements by linked Test Case presence; existing Requirement → Test Case drill-down and mobile expandable-card behavior remain in place.
- Completed P3.4: Test Case and Test Suite screens retain server-side pagination/search/filter, preserve step detail through the detail view, prevent duplicate Suite membership through the available-case list, show Required/Optional counts, and provide keyboard-friendly move controls with responsive mobile cards.
- Completed P3.5: Execution offers only InProgress cycles, carries Build/Environment context, blocks edits and deletes for Closed/Cancelled cycles, and shows execution tester, timestamp, actual result, and comment in history.
- Completed P3.6: Defect and Regression screens provide shared filtering/pagination patterns, clear Critical/High severity and P0/P1 risk badges, linked Test Case/Execution drill-down, risk scoring, baseline comparison, and recent audit activity; Defect rows now expose Release/Build context without opening detail.
- Completed P4.1–P4.4 UI follow-ups: Agent readiness guard, Evidence empty state, Retry safety, Schedule transition guard, and Automation responsive actions.
- Phase 4 review completed: Automation UI now covers Runner/Queue/Execution state, DSL/Object/Evidence inspection, Retry Safety and Maintenance flow, Schedule/Notification state, Webhook history, Snapshot/Restore, Seed/Cleanup, and Environment Data Profile while keeping credential values out of UI/log/export paths.
- Completed P5.1/P5.5 review: shared Badge, spinner/loading, empty/error, pagination, confirmation, inline feedback, date/time, export, context-selector and module-tree patterns are reused across pages; deferred Test Suite → Test Cycle initialization now includes its handler dependency to remove the remaining lint warning.
- Completed P5.2–P5.4 UI follow-ups: responsive Automation actions, accessible alert/loading semantics, and Test Summary retry feedback.
- Validation: frontend build, lint, diff check, and 14 frontend tests passed. One pre-existing Hook dependency warning for `openForm` remains documented.
 - Shared Dashboard parity: Shared links now render the Module Attention panel from the same scoped Dashboard summary, including open-defect counts by module, while remaining read-only and anonymous-link safe.
