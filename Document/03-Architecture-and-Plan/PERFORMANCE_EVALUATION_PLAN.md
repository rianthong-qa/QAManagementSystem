# ProMaxx2 QA Hub — ผลงานและการประเมินทีม QA (Performance & Evaluation)

**Document Type:** Development Plan + Status\
**Module:** ผลงานและการประเมิน (`#/performance`)\
**Status:** เฟส 1–3 Implemented และ deploy แล้ว (2026-10-08)\
**อัปเดตล่าสุด:** 9 ตุลาคม 2026

---

## 1. ปัญหาและเป้าหมาย

หัวหน้าทีม QA ไม่รู้ว่าพนักงานแต่ละคนทำอะไรบ้าง และทีมมีพนักงานที่อยู่ในช่วงทดลองงานซึ่งต้องประเมินตามกำหนด ระบบมีข้อมูลกิจกรรมรายคนอยู่แล้วหลายตาราง แต่ยังไม่มีหน้าไหนรวมข้อมูลเหล่านี้ตามคนและตามช่วงเวลา และยังไม่มีข้อมูลพนักงาน (วันเริ่มงาน สถานะทดลองงาน หัวหน้า)

เป้าหมายมี 4 ข้อ

1. รวมผลงานจากระบบให้อัตโนมัติ และให้บันทึกงานนอกระบบเพิ่มได้ (Work Log / ผลงานเด่น)
2. ติดตามช่วงทดลองงาน 119 วัน โดยประเมินที่วัน 30, 60, 90 และรอบสรุป
3. ประเมินพนักงานประจำรายไตรมาส
4. หัวหน้าเห็นทุกคน พนักงานเห็นเฉพาะของตัวเอง และประเมินตนเองได้

**หลักการ:** ตัวเลขจากระบบใช้เป็นหลักฐานประกอบการประเมินเท่านั้น ไม่นำมาคิดเป็นคะแนนอัตโนมัติ เพราะงานแต่ละชิ้นยากไม่เท่ากัน และ `TestExecution` ยังไม่มีเวลาที่ใช้จริง (constructor ตั้ง `StartedAt` กับ `CompletedAt` เป็นเวลาเดียวกัน) หัวหน้าเป็นผู้ให้คะแนนตามเกณฑ์

## 2. สิทธิ์

| Permission | ความหมาย | Role เริ่มต้น |
|---|---|---|
| `PERFORMANCE.VIEW` | เห็นเมนูและข้อมูลของตัวเอง, เพิ่มบันทึกงานของตัวเอง | QA_LEAD, QA_TESTER |
| `PERFORMANCE.EDIT` | ดูทุกคน, แก้ข้อมูลพนักงาน, เขียนข้อสังเกต (Concern), แก้/ลบบันทึกของทุกคน, ประเมินในฐานะหัวหน้า, สร้างรอบไตรมาส, แก้แบบประเมิน, เปิดผลประเมินใหม่ | QA_LEAD |
| `PERFORMANCE.CREATE` / `PERFORMANCE.DELETE` | seed ไว้แต่ยังไม่มี endpoint ใช้ (สำรองไว้แยกสิทธิ์ภายหลัง) | QA_LEAD |

- SYS_ADMIN ผ่านทุก policy
- Policy `PerformanceView` (VIEW หรือ EDIT) ครอบทั้ง controller ส่วนสิทธิ์รายข้อมูลตรวจใน `PerformanceService`: ผู้ไม่มี EDIT ขอได้เฉพาะ `userId` ของตัวเอง (ตอบ 403) และ Concern ของตัวเองตอบ 404 เพื่อไม่บอกว่ามีอยู่
- controller ไม่ใช้ `[RequireProjectAccess]` เพราะข้อมูลไม่ผูก Project เดียว แต่ metric ทุกตัวกรองเฉพาะ Project ที่ผู้ดูเป็นสมาชิก (`ProjectAccessService`)
- permission seed ทั้งใน `DatabaseInitializer` และใน SQL ของ migration `AddPerformanceProfilesAndWorkLogs` (เพราะ `Database:ApplyMigrations=false`)

### ผู้ประเมิน (2026-10-09)

- ให้คะแนน / บันทึกร่าง / ส่งผล / เปิดผลใหม่ ได้เฉพาะ **หัวหน้าที่ระบุในข้อมูลพนักงาน** (`EmployeeProfile.SupervisorUserId`) — ผู้มี `PERFORMANCE.EDIT` คนอื่นรวม SYS_ADMIN ดูได้แต่ประเมินแทนไม่ได้ (`EvaluationService.RequireSupervisorAsync`, ตอบ 403)
- พนักงานที่ยังไม่มีหัวหน้าประเมินไม่ได้จนกว่าจะตั้งหัวหน้า; หน้าต่างประเมินแสดงเหตุผล (`EvaluationDetailDto.managerRestriction`)
- หัวหน้าต้องเป็นผู้ใช้ Active ที่ได้ `PERFORMANCE.EDIT` จาก Role (`UserOptionDto.canEvaluate`) — ตรวจทั้งในช่องเลือกและตอนบันทึกข้อมูลพนักงาน
- การสร้างรอบไตรมาส / แก้แบบประเมิน / ข้อมูลพนักงาน ยังเป็นสิทธิ์ `PERFORMANCE.EDIT` เหมือนเดิม

## 3. เฟส 1 — เห็นผลงานทันที (Implemented 2026-10-06)

### Data model

| ตาราง | ฟิลด์หลัก |
|---|---|
| `EmployeeProfiles` (1:1 กับ `Users`) | EmployeeCode, Position, Level (Junior/Mid/Senior/Lead), SupervisorUserId, HireDate, EmploymentStatus (`Probation`/`Extended`/`Permanent`/`Resigned`), ProbationEndDate (ค่าเริ่มต้น HireDate + 118 วัน = 119 วันนับวันเริ่มงาน), Notes |
| `WorkLogEntries` | UserId, WorkDate, Category, Kind (`WorkLog`/`Highlight`/`Concern`), Title, Description, EffortHours, ProjectId, ReferenceUrl (รับเฉพาะ http/https) |

index ใหม่สำหรับ query รายคน: `TestExecutions(TesterUserId, CompletedAt)`, `Defects(CreatedBy, CreatedAt)`

### Metric ที่รวมจากระบบ (`PerformanceQueries`, group ฝั่ง SQL)

| หมวด | Metric | แหล่งข้อมูล |
|---|---|---|
| การทดสอบ | Execution, Pass/Fail/Blocked/Skipped, Retest (`ExecutionNo > 1`), Test Case ที่รันไม่ซ้ำ | `TestExecutions.TesterUserId` + `CompletedAt` |
| งานที่ได้รับมอบหมาย | งานค้าง (NotRun/InProgress/Blocked), เลยกำหนด (`DueDate` ผ่านแล้ว) | `TestCycleCases.AssignedTesterUserId` + `TestCycleCaseAssignments.DueDate` — เป็นสถานะปัจจุบัน ไม่ขึ้นกับช่วงวันที่ |
| ออกแบบเทส | Test Case ที่สร้าง/แก้, Requirement ที่สร้าง/แก้ (RevisionNo > 1), Test Suite ที่สร้าง | `CreatedBy`, `TestCaseRevisions`, `RequirementRevisions` |
| Defect | ที่แจ้ง, Critical/High, ถูก Reject, คอมเมนต์ | `Defects.CreatedBy`, `DefectActivities` (`Comment`) |
| Automation | Case/Version ที่สร้าง, Version ที่อนุมัติ, สั่งรัน | `AutomationCases`, `AutomationVersions`, `AutomationExecutions.RequestedBy` (string → Guid) |
| อื่น ๆ | กิจกรรม Regression, บันทึกงาน/ผลงานเด่น/ชั่วโมง (ไม่นับ Concern) | `RegressionActivities`, `WorkLogEntries` |

ช่วงวันที่ที่ผู้ใช้เลือกเป็นวันตามเวลาไทย (UTC+7) แปลงเป็น UTC ก่อน query และจำกัดช่วงไม่เกิน 366 วัน

### งานจาก CRM (เพิ่ม 2026-10-07)

`CrmPerformanceSource` (Api) ผ่าน interface `ICrmPerformanceSource` — `PerformanceService.CollectMetricsAsync` รวมเข้ากับ metric อื่น ใช้ทั้งหน้าผลงานและ snapshot ตอนส่งผลประเมิน

| Metric | แหล่งข้อมูล |
|---|---|
| Ticket ที่ดูแล (ขั้น QA) | `AuditLogs` EntityType `CrmTicketFlow` ในช่วงวันที่ ที่ snapshot ก่อนหรือหลังมี Assignto = รหัส CRM ของผู้ใช้ (จาก `CrmConfigurations.Username`) และไม่ใช่ Support/Developer ของ Ticket — ตรรกะเดียวกับ `GetLastQaAsync` |
| ส่งต่อ Dev / ส่งกลับ Support | `FlowChanged` ที่ก่อนหน้าอยู่ขั้น QA ของผู้ใช้ แล้ว Assignto ย้ายไปเป็น Developer (หรือสถานะ Develop) / เจ้าของเรื่อง |
| ปิดงาน | Ticket ที่เปลี่ยนเป็น Finish/Close ขณะอยู่กับผู้ใช้ |
| อัปเดต Ticket ผ่าน QA Hub | `AuditLogs` EntityType `CrmTicketUpdate` (ใหม่ — `CrmController` บันทึกทุกครั้งที่อัปเดตสำเร็จ โดย UserId = ผู้ทำจริง; นับได้ตั้งแต่ 2026-10-07) และแสดงในไทม์ไลน์ |
| Defect ที่สร้างจาก CRM | `DefectActivities` `CreatedFromCrm` (กรองตาม Project ที่ผู้ดูเป็นสมาชิก) |

ข้อจำกัด: snapshot ของ `CrmTicketFlow` ถูกบันทึกเฉพาะเมื่อมีคนเปิดดู/อัปเดต Ticket ผ่านหน้า CRM ของ QA Hub — Ticket ที่ทำใน CRM โดยตรงและไม่เคยถูกเปิดใน QA Hub จะไม่ถูกนับ และผู้ใช้ที่ยังไม่ผูกบัญชี CRM จะแสดง "ยังไม่ได้ผูก" แทนตัวเลข 0

### API (`/api/v1/performance`)

| Endpoint | การทำงาน |
|---|---|
| `GET team?from&to` | ภาพรวมทีม + metric รายคน (ต้องมี EDIT) |
| `GET users` | รายชื่อผู้ใช้ Active สำหรับเลือกพนักงาน/หัวหน้า (ต้องมี EDIT) |
| `GET users/{userId}/summary?from&to` | โปรไฟล์ + metric + แนวโน้มรายสัปดาห์ |
| `GET users/{userId}/timeline?from&to&page&size` | กิจกรรมในระบบ (Execution, Defect activity, Test Case, Requirement, Regression) |
| `PUT users/{userId}/profile` | บันทึกข้อมูลพนักงาน (ต้องมี EDIT) — เขียน `AuditLogs` (EntityType `EmployeeProfile`) |
| `GET users/{userId}/work-logs?from&to` | บันทึกงาน (Concern เห็นเฉพาะผู้มี EDIT) |
| `POST work-logs`, `PUT/DELETE work-logs/{id}` | เพิ่ม/แก้/ลบบันทึกงาน — เจ้าของแก้ของที่ตัวเองสร้าง หัวหน้าแก้ได้ทุกรายการ |

### UI (`src/pages/PerformancePage.tsx` + `Performance.css`)

- เมนู "ผลงานและการประเมิน" ในกลุ่ม ภาพรวม
- ผู้มี EDIT เห็นแท็บ "ภาพรวมทีม" (KPI, การ์ดติดตามทดลองงาน, ตารางผลงานรายคน) และ "รายบุคคล"
- ผู้มีแค่ VIEW เห็นเฉพาะหน้าของตัวเอง
- หน้ารายบุคคลมี 6 ส่วน: โปรไฟล์ + แถบทดลองงาน (หมุด 30/60/90/สรุป), กลุ่ม metric, กราฟแนวโน้มรายสัปดาห์ (สลับเป็นตารางได้), บันทึกงาน, ไทม์ไลน์กิจกรรม และ modal แก้ข้อมูลพนักงาน

## 4. เฟส 2 — การประเมิน (Implemented 2026-10-07)

สิ่งที่ทำจริงต่างจากแผนเดิมเล็กน้อย: สถานะเหลือ 4 ขั้น `Open → SelfSubmitted → Completed → Acknowledged` (หัวหน้าส่งผลได้แม้พนักงานยังไม่ประเมินตนเอง) และเกณฑ์ถูก copy ลง `EvaluationScores` ทุกครั้งที่สร้างรอบ ทำให้แก้แบบประเมินได้โดยไม่กระทบรอบเดิม; แบบประเมินตั้งต้น 2 ชุดถูกสร้างอัตโนมัติจากโค้ด (`EvaluationTemplate.DefaultCriteria`) เมื่อยังไม่มี แทนการ seed ใน migration

### แผนเดิม (อ้างอิง)

- `EvaluationTemplate` + `EvaluationCriterion` (กลุ่ม: ปริมาณงาน/คุณภาพงาน/ความรู้-ทักษะ/พฤติกรรม/การสื่อสาร, น้ำหนักรวม 100%) และ seed แบบประเมินตั้งต้น 2 ชุด (Probation, Quarterly)
- `Evaluation` (Round `P30`/`P60`/`P90`/`PFinal`/`Q1`–`Q4`, Status `Scheduled → SelfAssessment → ManagerReview → Completed → Acknowledged`) + `EvaluationScore` (SelfScore/ManagerScore 1–5)
- สร้างรอบทดลองงานอัตโนมัติเมื่อตั้งโปรไฟล์เป็น Probation: P30/P60/P90 และ PFinal ครบก่อนสิ้นสุด 14 วัน
- สร้างรอบไตรมาสให้พนักงาน Permanent ทุกคนในปุ่มเดียว (idempotent)
- snapshot metric ลง `MetricsSnapshotJson` ตอนหัวหน้าส่งผล
- คำนวณคะแนนรวมแบบถ่วงน้ำหนัก แล้วแปลงเป็นเกรด A–E
- Completed แก้ไม่ได้จนกว่าจะ Reopen(reason) และทุกการเปลี่ยนสถานะบันทึกลง AuditLog
- กฎผลรอบสรุป
  - `Pass` → โปรไฟล์เปลี่ยนเป็น Permanent
  - `Extend` → ต้องระบุวันสิ้นสุดใหม่ และระบบสร้าง PFinal รอบใหม่
  - `NotPass` → บันทึกผลอย่างเดียว ไม่ปิด user อัตโนมัติ
- การมองเห็น: พนักงานไม่เห็นคะแนน/ความเห็นของหัวหน้าจนกว่าจะ Completed
- หมายเหตุด้านกฎหมาย: การขยายทดลองงานเกิน 119 วันมีผลต่อสิทธิ์ค่าชดเชย ต้องให้ HR ยืนยันก่อน ระบบจะเตือนแต่ไม่บังคับ

### สิ่งที่ implement ในเฟส 2

| ส่วน | รายละเอียด |
|---|---|
| ตาราง | `EvaluationTemplates`, `EvaluationTemplateCriteria`, `Evaluations`, `EvaluationScores` (migration รวมกับเฟส 1: `AddPerformanceEvaluation`) |
| รอบทดลองงาน | บันทึกโปรไฟล์เป็น Probation → สร้าง P30/P60/P90 (ครบวันที่ 30/60/90) + PFinal (ครบก่อนสิ้นสุด 14 วัน); แก้วันเริ่มงาน → เลื่อนรอบที่ยังไม่ปิด; ทดลองงานสั้นกว่าปกติ → ข้ามรอบที่ชนรอบสรุป |
| รอบไตรมาส | `POST evaluations/quarterly` สร้างให้พนักงาน Permanent ทุกคน (ข้ามคนที่มีรอบนั้นแล้ว) ช่วง = ไตรมาส, กำหนดส่ง = จบไตรมาส + 14 วัน |
| คะแนน | 1–5 ต่อเกณฑ์, คะแนนรวม = ถ่วงน้ำหนัก (ทศนิยม 2 ตำแหน่ง), เกรด A ≥ 4.5, B ≥ 3.5, C ≥ 2.5, D ≥ 1.5, E |
| การมองเห็น | พนักงานไม่เห็นคะแนน/ความเห็น/จุดแข็ง/ผลทดลองงานของหัวหน้าจนกว่าจะ Completed; หัวหน้าเห็นคะแนนประเมินตนเองตลอด |
| กฎ | หัวหน้าประเมินผลตัวเองไม่ได้ (QA_LEAD ต้องให้ SYS_ADMIN หรือ Lead คนอื่นประเมิน); Completed แก้ไม่ได้จนกว่าจะ Reopen (ต้องมีเหตุผล); Reopen ไม่ย้อนผลต่อโปรไฟล์ |
| ผลรอบสรุป | Pass → Permanent · Extend → สถานะ Extended + วันสิ้นสุดใหม่ (ต้องหลังวันเดิม) + PFinal รอบใหม่ · NotPass → บันทึก AuditLog อย่างเดียว |
| หลักฐาน | ตอนส่งผล snapshot metric ของช่วงประเมินลง `MetricsSnapshotJson` |
| Audit | `Evaluation` (SelfSubmitted/Completed/Acknowledged/Reopened), `EmployeeProfile` (ProbationPassed/Extended/NotPassed), `EvaluationTemplate` (Updated) |
| UI | แท็บ "การประเมิน" (หัวหน้า: ทุกคน + สร้างรอบไตรมาส + แก้แบบประเมิน / พนักงาน: ของตัวเอง), ประวัติการประเมินในหน้ารายบุคคล, modal ประเมิน 1040px พร้อมแผง "หลักฐานจากระบบ" |

### Deploy

- 2026-10-07: apply migration `20261006190223_AddPerformanceEvaluation` บน `localhost\MSSQLSERVER2022` / `ProMaxx2QA` ด้วย idempotent script (`dotnet ef migrations script AddDefectCommentImages AddPerformanceEvaluation --idempotent`) ผ่าน `sqlcmd` — ยืนยันตาราง 6 ตาราง และ permission `PERFORMANCE.*` ของ SYS_ADMIN/QA_LEAD/QA_TESTER แล้ว; restart API (profile production) แล้ว `/api/v1/performance/team` ตอบ 401 ทั้ง local และ `api-promaxx2.qahub.store`
- ผู้ใช้ที่ login อยู่ก่อน deploy ต้อง login ใหม่จึงจะได้ permission ใหม่ใน token
- ยังไม่ได้ทดสอบ UI บนเบราว์เซอร์จริง (Desktop/Mobile)

## 5. เฟส 3 — เสริม (Implemented 2026-10-07)

| ส่วน | รายละเอียด |
|---|---|
| อีเมลเตือน | `PerformanceReminderService` + `PerformanceReminderWorker` (Api) ทุก 1 ชม. หลัง 08:00 น. — การประเมิน Open/SelfSubmitted ที่เหลือ ≤ 7 วัน (`DueSoon`) และเลยกำหนด (`Overdue`) อย่างละครั้ง; ผู้รับ = หัวหน้าในโปรไฟล์ หรือถ้าไม่มี/ไม่มีอีเมล = ทุกคนที่มี `PERFORMANCE.EDIT` (ไม่รวมตัวผู้ถูกประเมิน); 1 อีเมลต่อผู้รับ (`EmailTemplates.EvaluationReminder`, ปุ่มเปิด `{PublicWebBaseUrl}/#/performance`); กันซ้ำด้วยตาราง `EvaluationNotifications` ที่มี DueDate ใน key (เลื่อนกำหนดส่ง = เตือนใหม่); SMTP ยังไม่ตั้งค่า = ข้ามโดยไม่บันทึก; ปิดด้วย `Performance:RemindersEnabled=false`; กฎการเลือกผู้รับอยู่ใน `Plan()` ที่ไม่มี IO เพื่อทดสอบได้ |
| ไฟล์หลักฐาน | ตาราง `WorkLogAttachments` + `PerformanceEvidenceStorage` (`App_Data/PerformanceEvidence/{workLogEntryId}/`) — PNG/JPG/WebP/PDF ตรวจ signature, ≤ 5 ไฟล์/บันทึก, ไฟล์ละ ≤ 5 MB, รวม ≤ 20 MB; สิทธิ์ดู = สิทธิ์เห็นบันทึกงาน (Concern ตอบ 404), สิทธิ์แนบ/ลบ = สิทธิ์แก้บันทึก; ลบบันทึกแล้วไฟล์ถูกลบตาม |
| Export PDF | `pages/EvaluationPdf.ts` ฝั่ง client (jspdf + html2canvas แบบเดียวกับ `DefectModulePdf`) — ใช้ข้อมูลชุดเดียวกับที่หน้าจอได้จาก API จึงเคารพกฎการมองเห็น (พนักงานไม่เห็นคะแนนหัวหน้าก่อนส่งผล); รอบที่ยังไม่ปิดมีป้าย "ร่าง"; มีช่องลงนามผู้ถูกประเมิน/ผู้ประเมิน/HR; `EvaluationDetailDto.employee` เพิ่มรหัส/ตำแหน่ง/ระดับ/วันเริ่มงาน/หัวหน้า |
| เทียบผลข้ามรอบ | `GET users/{userId}/evaluation-comparison` — รอบที่ปิดแล้วล่าสุด 8 รอบ (เก่า → ใหม่) คะแนนรวม + คะแนนหัวหน้าเฉลี่ยถ่วงน้ำหนักรายกลุ่มเกณฑ์; หน้ารายบุคคลแสดงเป็นตารางพร้อมลูกศรเทียบรอบก่อนหน้า |
| Migration | `20261007141454_AddPerformanceEvidenceAndReminders` — สร้าง 2 ตาราง ไม่แตะตารางเดิม |

### ต่อยอดเฟส 3 (2026-10-08)

| ส่วน | รายละเอียด |
|---|---|
| เตือนพนักงานให้ประเมินตนเอง | `PerformanceReminderService.PlanSelf` — รอบที่ยัง `Open` (ยังไม่ส่งแบบประเมินตนเอง) ของพนักงานที่มีอีเมล เหลือ ≤ 7 วัน = `SelfDueSoon`, เลยกำหนด = `SelfOverdue` อย่างละครั้งต่อกำหนดส่ง; อีเมลแยกจากอีเมลสรุปถึงหัวหน้า (`EmailTemplates.EvaluationSelfReminder`, ปุ่ม "ประเมินตนเอง →"); กฎเดิมของ `Plan()` ที่ไม่ส่งอีเมลสรุปของหัวหน้าให้ผู้ถูกประเมินยังคงอยู่ |
| เทียบผลทั้งทีมรายไตรมาส | `GET evaluations/team-comparison?year=` (EDIT) — แถว = สมาชิกทีม (รวมคนลาออกที่มีผลในปีนั้น; ไม่แสดงคนที่ยังทดลองงานและไม่มีรอบไตรมาส), คอลัมน์ Q1–Q4: รอบที่ปิดแล้วแสดงเกรด + คะแนน + ลูกศรเทียบไตรมาสก่อน (Q1 เทียบ Q4 ปีก่อน), รอบที่ยังไม่ปิดแสดงสถานะ/เลยกำหนด; ค่าเฉลี่ยรายคนและรายไตรมาสคิดจากรอบที่ปิดแล้วเท่านั้น; อยู่บนสุดของแท็บ "การประเมิน" ของหัวหน้า คลิกช่องเพื่อเปิดรอบนั้น |
| ดูตัวอย่างไฟล์หลักฐาน | รูปเปิดใน `ImageLightbox` (เลื่อนดูรูปอื่นในบันทึกเดียวกัน), PDF เปิดใน modal 1040px พร้อม "เปิดในแท็บใหม่"/"ดาวน์โหลด"; ไฟล์โหลดผ่าน `fetchAuthedObjectUrl` (มี token) และ revoke object URL เมื่อปิด; ปุ่มดาวน์โหลดแยกข้างไฟล์ |

### Deploy เฟส 3

- 2026-10-08: apply migration `20261007141454_AddPerformanceEvidenceAndReminders` (สร้าง `WorkLogAttachments`, `EvaluationNotifications`) บน `ProMaxx2QA` ด้วย idempotent script ผ่าน `sqlcmd` แล้ว restart API — อีเมลเตือนเปิดใช้งาน (`Performance:RemindersEnabled` ค่าเริ่มต้น true)

### สำเนา CRM Ticket ใน QA Hub (2026-10-08)

ตัวเลขงาน CRM เดิมนับจาก snapshot ที่บันทึกเฉพาะเมื่อมีคนเปิด Ticket ใน QA Hub จึงไม่ครบ — เปลี่ยนเป็นดึง CRM ทั้งทีมมาเก็บเอง

| ส่วน | รายละเอียด |
|---|---|
| ตัวดึง | `CrmMirrorSyncService` + `CrmMirrorWorker` — บัญชีกลาง `CrmMirror:AccountUsername` (ค่าเริ่มต้น `6101`, ใช้ `CrmConfigurations` ของผู้ใช้ที่มี Username นี้และเปิดใช้งาน); HelpDeskExport ไม่กรองผู้รับผิดชอบ จึงได้ Ticket ทั้งทีมในช่วงวันที่ติดต่อ; ทุก 15 นาทีย้อนหลัง 90 วัน + รอบใหญ่ 366 วันเมื่อรอบใหญ่ล่าสุดเกิน 23 ชม.; ทีละรอบ (กันซ้อน); ปิดด้วย `CrmMirror:Enabled=false` |
| ตาราง | `CrmTickets` (สถานะล่าสุด: เลข, ชื่อเรื่อง, สถานะ, Service/Product, รหัสผู้รับผิดชอบ/เจ้าของเรื่อง/Dev, สาขา, วันที่ติดต่อ/ครบกำหนด/ตอบล่าสุด) · `CrmTicketEvents` (Snapshot/Changed/Answer — เวลา Changed = เวลาที่ sync เห็น ละเอียดเท่ารอบการดึง; Answer = เลข/ผู้ตอบ/วันที่จาก CRM ไม่เก็บเนื้อหา) · `CrmMirrorRuns` (ผลแต่ละรอบ + DiagnosticsJson = field ที่ CRM ส่งมา) — **ไม่เก็บ**ข้อมูลลูกค้า (ชื่อ/เบอร์/อีเมล) และเนื้อหาข้อความ |
| ประวัติเดิม | รอบแรกนำเข้า snapshot Flow Tracking เดิม (`AuditLogs` CrmTicketFlow) เป็นเหตุการณ์ Source = Legacy ครั้งเดียว |
| ตัวชี้วัด | `CrmPerformanceSource.ComputeFlow` สร้างช่วง "อยู่ขั้น QA ของผู้ใช้" จากเหตุการณ์: ดูแล, ส่งต่อ Dev / ส่งกลับ Support, ปิดงาน, **ส่งกลับแก้ (EditErr)**, **ตอบใน Ticket**, **เวลาเฉลี่ยที่อยู่กับ QA** (เฉพาะช่วงที่เริ่มจากการเปลี่ยนจริง) — ถ้ายังไม่มีเหตุการณ์ในตารางเลยใช้วิธีเดิม |
| ดูแลระบบ | `GET /api/v1/crm/mirror/status`, `POST /api/v1/crm/mirror/sync?full=` (SYS_ADMIN) + แผง "ข้อมูล CRM ใน QA Hub" ท้ายหน้า CRM |
| Migration | `AddCrmTicketMirror` — สร้าง 3 ตาราง ไม่แตะตารางเดิม |

Deploy 2026-10-08: apply migration `AddCrmTicketMirror` ด้วย `sqlcmd -I` (filtered index ต้องเปิด QUOTED_IDENTIFIER) → รอบแรก (Full) ได้ 2,997 Ticket ของผู้รับผิดชอบ 68 คน (ติดต่อ 7 ต.ค. 2568 – 8 ต.ค. 2569) ใน ~8 วินาที + นำเข้าประวัติเดิม 176 เหตุการณ์; DiagnosticsJson พบว่า HelpDeskExport ส่งประวัติการตอบมาเฉพาะบางใบ (5 จาก 859) แต่ทุกใบมี `ansDate` (ตอบล่าสุด) — จึงเพิ่ม `SyncRecentAnswersAsync`: ดึงข้อความตอบรายใบ (`GetHelpDeskAnswersAsync`, เก็บเฉพาะเลข/ผู้ตอบ/วันที่) ของ Ticket ที่ตอบล่าสุดภายใน 30 วันแต่ยังไม่มีข้อความตอบนั้นในฐานข้อมูล ≤ 60 ใบต่อรอบ ไม่ซ้ำใบเดิมภายใน 6 ชม.; ข้อความตอบที่เก่ากว่า 30 วันจะไม่ถูกเติมย้อนหลัง (ตัวชี้วัด "ตอบใน Ticket" จึงเชื่อถือได้ตั้งแต่ ~8 ก.ย. 2569)

ข้อจำกัด: เวลาการเปลี่ยนสถานะละเอียดเท่ารอบการดึง (15 นาที); Ticket ที่ติดต่อเกิน 1 ปีและยังไม่ปิดจะไม่ถูกดึง; ถ้ารหัสผ่าน CRM ของบัญชีกลางเปลี่ยน การดึงจะล้มจนกว่าจะบันทึกรหัสผ่านใหม่ใน QA Hub (เห็นในแผงสถานะ)

### สรุปผลงานรายคน + ลิงก์แชร์ให้ HR (2026-10-09)

| ส่วน | รายละเอียด |
|---|---|
| รายงาน | `PerformanceReportService` (Application) ประกอบจาก service เดิม — โปรไฟล์ (ไม่มี UserId/อีเมล/Notes), metric + แนวโน้มรายสัปดาห์ของช่วงที่เลือก, บันทึกงาน/ผลงานเด่น (**ไม่รวม Concern เสมอ**, ไม่มีไฟล์แนบ/ลิงก์อ้างอิง), การประเมินทุกรอบ (คะแนน/เกรด/ผลทดลองงานเฉพาะรอบที่ปิดแล้ว) + คะแนนหัวหน้ารายกลุ่มเกณฑ์ ≤ 8 รอบ — `GET users/{userId}/report` |
| ลิงก์แชร์ | ตาราง `PerformanceShareLinks` + `PerformanceShareLinkService` (Api): หัวหน้า (EDIT) สร้างลิงก์ `/?p=<code สุ่ม 10 ตัว>` ต่อพนักงาน 1 คน + ช่วงวันที่, อายุ 7–90 วัน, เลือกได้ว่ารวมการประเมิน/บันทึกงาน, ยกเลิกได้, นับจำนวนครั้งที่เปิด, AuditLog `PerformanceShareLink` (Created/Revoked) |
| เปิดลิงก์ | `GET /api/v1/shared/performance/{code}` anonymous + rate limit `share` + `Cache-Control: no-store`; คำนวณสดด้วยสิทธิ์ของผู้สร้าง ณ ตอนเปิด (Project ที่เป็นสมาชิก) — ผู้สร้างถูกปิดบัญชีหรือหมดสิทธิ์ PERFORMANCE.EDIT ลิงก์ตอบ `410` |
| UI | แท็บ "สรุปผลงาน" (หัวหน้า) + ปุ่มลัดในหน้ารายบุคคล, หน้าต่างแชร์, การ์ดลิงก์ที่แชร์แล้ว, หน้าแชร์ HR, พิมพ์/บันทึก PDF ผ่าน `window.print()` |
| Migration | `20261009064922_AddPerformanceShareLinks` — สร้าง 1 ตาราง ไม่แตะตารางเดิม |
| ทดสอบ | `PerformanceShareLinkTests` 4 กรณี (สิทธิ์/อายุลิงก์/Audit, ไม่รวม Concern + นับการเปิด, ซ่อนคะแนนรอบที่ยังไม่ปิด + ส่วนที่ไม่แชร์, หมดอายุ/ยกเลิก/ผู้สร้างหมดสิทธิ์ = 410) |

Deploy 2026-10-09: apply migration `AddPerformanceShareLinks` บน `ProMaxx2QA` ด้วย idempotent script (`dotnet ef migrations script SeedCrmPermissions AddPerformanceShareLinks --idempotent`) ผ่าน `sqlcmd -I` — ยืนยันตาราง 15 คอลัมน์, index 5 ตัว, FK 3 ตัว และแถวใน `__EFMigrationsHistory` แล้ว; **ยังไม่ได้ restart API** (endpoint ใหม่จะใช้ได้หลัง restart)

ข้อจำกัด: ใครได้ลิงก์ก็เปิดได้จนหมดอายุ (ไม่มีการยืนยันตัวตน HR) จึงจำกัดอายุและให้ยกเลิกได้; ลิงก์ไม่ตรึงข้อมูล ณ วันที่แชร์ — ตัวเลขอาจเปลี่ยนถ้าข้อมูลย้อนหลังถูกแก้

### สรุปประจำสัปดาห์ส่งผู้บริหาร (2026-10-09)

ทุกวันศุกร์หัวหน้า QA ต้องส่งสรุปงานทั้งทีมให้ผู้บริหาร — ข้อมูลจริงย้อนหลัง 4 สัปดาห์ (ณ 9 ต.ค. 2569) พบว่ายอดดิบใช้ตรง ๆ ไม่ได้: วันที่ 15 ก.ย. มี Execution 1,052 และ Defect ~960 รายการ (นำเข้าช่วงตี 3–8 โมง), บันทึกงานทั้งทีมมี 1 รายการ และ CRM ครบทั้งทีมตั้งแต่ 8 ต.ค.

| ส่วน | รายละเอียด |
|---|---|
| Service | `WeeklyReportService` (Api) — `CollectMetricsAsync` 5 ครั้ง (สัปดาห์ที่รายงาน + 4 สัปดาห์ก่อน) แล้วรวมเป็นยอดทีม; ค่า "ปกติ" ใช้ **median** รายช่องของ 4 สัปดาห์ก่อน เพื่อไม่ให้สัปดาห์ที่นำเข้าข้อมูลดึงค่าทั้งชุด; ตรวจวันผิดปกติ (≥ 100 รายการ และ ≥ 4 เท่าของค่ากลางวันอื่น) ของ Execution/Defect/Test Case แล้วแจ้งในรายงาน |
| ข้อความของหัวหน้า | ตาราง `WeeklyReportNotes` (สัปดาห์ละแถว): สถานะภาพรวม เขียว/เหลือง/แดง, สรุป, ความเสี่ยงหลัก, เรื่องที่ขอผู้บริหาร, แผนสัปดาห์หน้า, ความเห็นรายคน — AuditLog `WeeklyReport` |
| สิ่งที่ไม่ทำ | ไม่มีคะแนน/อันดับรายคน (ตาม §1); ไม่รวม Concern; คนลาออกไม่แสดง |
| UI | แท็บ "สรุปประจำสัปดาห์" (หัวหน้า) — เลือกสัปดาห์, เขียนสรุป, พิมพ์/PDF, คัดลอกเป็นข้อความสำหรับอีเมล/แชท |
| ทดสอบ | `WeeklyReportTests` 6 กรณี (สิทธิ์/การปัดสัปดาห์, เทียบสัปดาห์ + ไม่รวม Concern/คนลาออก, บันทึกข้อความ + validate + Audit, median, การตรวจวันผิดปกติ, query แปลงเป็น SQL) |
| Migration | `AddWeeklyReportNotes` — สร้าง 1 ตาราง ไม่แตะตารางเดิม |

Deploy 2026-10-09: apply migration `AddWeeklyReportNotes` บน `ProMaxx2QA` ด้วย idempotent script (`dotnet ef migrations script AddPerformanceShareLinks AddWeeklyReportNotes --idempotent`) ผ่าน `sqlcmd -I` — ยืนยันตาราง 12 คอลัมน์, index 4 ตัว, FK 2 ตัว และแถวใน `__EFMigrationsHistory`; restart API (profile production) แล้ว `GET /api/v1/performance/weekly` ตอบ 401 ทั้ง local และ `api-promaxx2.qahub.store` (route มีอยู่จริง) — ยังไม่ได้ทดสอบหน้าจอกับข้อมูลจริงขณะ login

ต่อยอด: ลิงก์แชร์อ่านอย่างเดียวให้ผู้บริหาร (แบบ `/?p=`) และอีเมลสรุปอัตโนมัติเช้าวันศุกร์

### ต่อยอดได้

- หน้าเทียบผลยังเป็นตาราง — ถ้าทีมใหญ่ขึ้นอาจเพิ่มกราฟแนวโน้มรายคน

## 6. ข้อจำกัดที่รู้แล้ว

- `TestExecution` ไม่มีเวลาที่ใช้จริง จึงยังวัด productivity เป็นชั่วโมงไม่ได้
- "งานค้าง/เลยกำหนด" เป็นสถานะ ณ ตอนนี้ ไม่ใช่ย้อนหลังตามช่วงวันที่
- `UpdatedBy` เก็บเฉพาะผู้แก้คนล่าสุด จึงนับการแก้จาก revision table เท่านั้น (Test Case, Requirement); Defect ที่ถูกแก้ดูได้จากไทม์ไลน์ (DefectActivity)
- `AuditLogs` ไม่มี index ที่ `UserId` และไทม์ไลน์ยังไม่ดึงจาก AuditLogs
- เปลี่ยนสถานะพนักงานจาก "ทดลองงาน" เป็น "ประจำ" เองโดยไม่ผ่านรอบสรุป รอบทดลองงานที่ยังเปิดอยู่จะค้าง และจะถูกเตือนทางอีเมลเมื่อใกล้/เลยกำหนด — ให้ส่งผลรอบนั้นหรือตั้งสถานะผ่านรอบสรุปแทน
- Export PDF ทำฝั่ง browser (html2canvas) — ข้อความยาวมากในย่อหน้าเดียวอาจล้นหน้าได้ (แบ่งหน้าตามย่อหน้าเท่านั้น)
