# Automation Development To-Do

> สถานะ: **Automation Work Tracker — Single Source of Truth**
>
> อัปเดตล่าสุด: 26 สิงหาคม 2026
>
> ขอบเขต: Automation UI, API, Domain, Database, Windows Agent, DSL, Queue, Evidence, Test Data, Regression และ CI/CD

ไฟล์นี้ใช้ติดตามงานพัฒนา Automation ที่ยังเหลืออยู่ ทุกครั้งก่อนเริ่มงาน Automation ต้องอ่านไฟล์นี้ทั้งไฟล์ และเมื่อทำงานเสร็จต้องอัปเดตสถานะกับ Progress Log ภายในงานเดียวกัน

## 1. วิธีใช้สถานะ

| สถานะ | ความหมาย |
|---|---|
| `TODO` | ยังไม่ได้เริ่ม |
| `IN PROGRESS` | เริ่มทำแล้วแต่ Acceptance Criteria ยังไม่ครบ |
| `BLOCKED` | ทำต่อไม่ได้และมี dependency ที่ระบุชัดเจน |
| `DONE` | ทำครบและมีหลักฐานการตรวจสอบแล้ว |

กฎการอัปเดต:

- ระบุผู้รับผิดชอบในช่อง Owner เมื่อเริ่มงาน
- รายการ `DONE` ต้องใส่หลักฐาน เช่น test, endpoint, screenshot, build หรือไฟล์ที่เปลี่ยน
- หาก requirement เปลี่ยน ให้แก้ Acceptance Criteria ก่อนเริ่ม implementation
- งาน UI ต้องอ่าน `UI_DESIGN_SYSTEM.md` และตรวจ Desktop/Mobile ตาม `AGENTS.md`
- หลังแก้ frontend ต้องรัน `npm.cmd run build`, `npm.cmd run lint` และ `git diff --check`
- หลังแก้ backend ต้อง build และ restart API ก่อนตรวจ endpoint จริง

## 2. Baseline ปัจจุบัน

ข้อมูลจากฐานข้อมูล ณ วันที่ 26 สิงหาคม 2026:

| Metric | จำนวน |
|---|---:|
| Automation Cases | 11 |
| Ready | 5 |
| Maintenance Required | 5 |
| Draft | 1 |
| Automation Versions | 13 |
| Action Library | 39 |
| Object Repository | 5 |
| Agents | 1 |
| Executions | 35 |
| Passed | 12 |
| Failed | 23 |
| Evidence | 28 |

ข้อสังเกตตั้งต้น:

- Execution ล้มเหลว 23 จาก 35 ครั้ง หรือประมาณ 66%
- Case 5 จาก 11 รายการอยู่สถานะ `MaintenanceRequired`
- Object Repository มีเพียง 5 รายการ เทียบกับ Action Library 39 รายการ
- ควรทำ Runtime/Selector/Failure ให้เสถียรก่อนเพิ่ม Schedule และ Trigger อัตโนมัติ

## 3. P0 — Runtime และความเสถียร

| ID | งาน | สถานะ | Owner | Acceptance Criteria / หลักฐาน |
|---|---|---|---|---|
| AUT-P0-001 | เพิ่ม Edit Action Library | TODO | - | แก้ไข Name, Category, Description, Schema, Handler และ Minimum Agent Version ได้ พร้อม validation และ audit ที่เหมาะสม |
| AUT-P0-002 | เพิ่ม Activate/Deactivate Action | TODO | - | เปิด/ปิด Action ได้, DSL ใหม่ไม่ใช้ Action ที่ปิด และ UI แสดงสถานะชัดเจน |
| AUT-P0-003 | เพิ่ม Edit Object Repository | TODO | - | แก้ Application, Screen, Object, Control Type, AutomationId และ Selector JSON ได้ |
| AUT-P0-004 | เพิ่ม Activate/Deactivate Object | TODO | - | เปิด/ปิด Object ได้และ Validator ปฏิเสธ Object ที่ปิด |
| AUT-P0-005 | Import Object จาก UI Inspector/AutomationId Scanner | TODO | - | Preview diff ก่อน import, เลือกรายการได้, ป้องกัน Business Key/AutomationId ซ้ำ |
| AUT-P0-006 | Object Verification Tool | TODO | - | สั่ง Agent ตรวจ Object บน AUT จริงและคืน Found/Not Found/Duplicate/Control Type mismatch พร้อมเวลาและ Agent |
| AUT-P0-007 | ซ่อม Case ที่เป็น MaintenanceRequired | TODO | - | Case ทั้ง 5 รายการมีสาเหตุ, owner, ผล revalidate และสถานะสุดท้าย |
| AUT-P0-008 | วิเคราะห์ Execution Failed เดิม | TODO | - | Execution Failed ทั้ง 23 รายการถูกจัดกลุ่ม Product/Automation/Environment/Agent/TestData พร้อม action ถัดไป |
| AUT-P0-009 | Retry Policy | TODO | - | กำหนด retryable failure, max retry, backoff, idempotency และแสดง RetryCount/LastError ใน UI |
| AUT-P0-010 | Flaky Test Quarantine | TODO | - | ระบุ flaky case, quarantine reason/owner/expiry และไม่ปะปนกับ Product Fail |
| AUT-P0-011 | Failure Dashboard | TODO | - | กรอง Failure Type/Build/Agent/วันที่ได้ และ drill down ไป Execution Detail |

## 4. P0 — Automated Tests

| ID | งาน | สถานะ | Owner | Acceptance Criteria / หลักฐาน |
|---|---|---|---|---|
| AUT-TEST-001 | DSL และ Validator tests | TODO | - | ครอบคลุม schema, action, object, test data, environment และ invalid transition |
| AUT-TEST-002 | Automation Case workflow tests | TODO | - | ครอบคลุม Draft → NeedsReview → Validated/Approved → Ready → MaintenanceRequired |
| AUT-TEST-003 | Atomic Job Claim tests | TODO | - | ยืนยันว่า Agent สองตัวรับ Job เดียวกันไม่ได้และ lease token ถูกตรวจทุกครั้ง |
| AUT-TEST-004 | Cancel/Timeout/Lease Recovery tests | TODO | - | ครอบคลุม Queued, Claimed, Running, expired lease และ late result |
| AUT-TEST-005 | Retry tests | TODO | - | ครอบคลุม max retry, backoff, non-retryable failure และไม่สร้าง execution ซ้ำ |
| AUT-TEST-006 | Batch Run/Multi-Agent tests | TODO | - | กระจายงานตาม capability/target และรวมผลครบ |
| AUT-TEST-007 | Evidence security tests | TODO | - | ตรวจ type/size/project access/path traversal และ permission |
| AUT-TEST-008 | Automation permission tests | TODO | - | ครอบคลุม View/Edit/Validate/Approve/Execute/Manage/Evidence/GenerateAI |
| AUT-TEST-009 | Agent ActionExecutor tests | TODO | - | ครอบคลุม UI actions, assertions, timeout, cancellation และ error code |
| AUT-TEST-010 | Database Validator tests | TODO | - | ครอบคลุม Firebird/SQL Server, parameter binding, timeout และ query failure |

## 5. P1 — Automation Suite และ Scheduling

| ID | งาน | สถานะ | Owner | Acceptance Criteria / หลักฐาน |
|---|---|---|---|---|
| AUT-P1-001 | Persistent Automation Suite | TODO | - | สร้าง/แก้ไข/ปิด Suite และเก็บในฐานข้อมูลได้ |
| AUT-P1-002 | จัดการ Case ใน Suite | TODO | - | เพิ่ม/ลบ/เรียง Case, Required/Optional และตรวจ Target/Status ได้ |
| AUT-P1-003 | Suite Version/History | TODO | - | เก็บ revision, change reason, ผู้แก้และเวลาครบ |
| AUT-P1-004 | Run Suite ซ้ำ | TODO | - | เลือก Suite เดิมแล้วรันกับ Build/Environment ใหม่ได้โดยไม่เลือก Case ใหม่ |
| AUT-P1-005 | Persistent Automation Schedule | TODO | - | สร้าง/แก้ไข/เปิด/ปิด Schedule พร้อม timezone และ next run |
| AUT-P1-006 | Schedule Execution Worker | TODO | - | สร้าง Job ครั้งเดียวตามเวลา, recovery หลัง restart และ audit ผล |
| AUT-P1-007 | Build Trigger | TODO | - | Build ใหม่ trigger Smoke/Regression ตาม policy ที่กำหนดได้ |
| AUT-P1-008 | CI/CD หรือ Webhook Integration | TODO | - | รับ trigger แบบ authenticated, ป้องกัน replay และ trace กลับ Build ได้ |
| AUT-P1-009 | Schedule Notifications | TODO | - | แจ้ง Started/Completed/Failed/No Agent พร้อมลิงก์ Execution |

## 6. P1 — Test Data Management

| ID | งาน | สถานะ | Owner | Acceptance Criteria / หลักฐาน |
|---|---|---|---|---|
| AUT-DATA-001 | Database Snapshot | TODO | - | snapshot ก่อนรันพร้อม metadata ของ Environment/Build |
| AUT-DATA-002 | Database Restore | TODO | - | restore หลังรันหรือเมื่อ fail และยืนยัน checksum/ความพร้อมใช้งาน |
| AUT-DATA-003 | Seed Test Data | TODO | - | seed แบบ repeatable/idempotent และไม่เก็บ credential ใน DSL |
| AUT-DATA-004 | Cleanup Test Data | TODO | - | cleanup สำเร็จแม้ execution ถูก cancel หรือ Agent หาย |
| AUT-DATA-005 | Master Data Setup Flow | TODO | - | เตรียมสินค้า/ราคา/โปรโมชั่นก่อน POS scenario ผ่าน UI หรือ approved DB seed |
| AUT-DATA-006 | Environment Data Profile | TODO | - | แยก config/data source ตาม Environment โดย secret อยู่ใน secure store |

## 7. P1 — Regression Closed Loop

| ID | งาน | สถานะ | Owner | Acceptance Criteria / หลักฐาน |
|---|---|---|---|---|
| AUT-REG-001 | Regression Impact → Run List | TODO | - | นำ recommended case IDs มาสร้าง Automation run list โดยตรวจ Ready/Target |
| AUT-REG-002 | Scheduled Regression → Automation | TODO | - | notification/build event เริ่ม workflow ได้อัตโนมัติและไม่สร้างงานซ้ำ |
| AUT-REG-003 | Multi-Agent Result Merge | TODO | - | รวมผลจากหลาย Agent ใน Test Cycle เดียวและแสดง partial/complete |
| AUT-REG-004 | TestExecution Write-back Verification | TODO | - | step result/evidence/source=Automation ครบและ dashboard อัปเดตถูกต้อง |
| AUT-REG-005 | Defect Closed Loop | TODO | - | Product Fail สร้าง draft defect หลัง QA confirm พร้อม evidence และ classification |
| AUT-REG-006 | End-to-End Regression Test | TODO | - | Build → Impact → Run → Write-back → Defect ผ่านจริงอย่างน้อย 1 release |

## 8. P2 — Monitoring, UX และ Scalability

| ID | งาน | สถานะ | Owner | Acceptance Criteria / หลักฐาน |
|---|---|---|---|---|
| AUT-P2-001 | Server-side pagination | TODO | - | Cases/Jobs/Executions ใช้ page/size/filter/sort ฝั่ง server |
| AUT-P2-002 | Advanced execution filters | TODO | - | กรองวันที่, Build, Environment, Agent, Target และ Failure Type |
| AUT-P2-003 | Pass/Fail/Flaky trend | TODO | - | กราฟแนวโน้มตามวัน/Build/Release และ drill down ได้ |
| AUT-P2-004 | Agent workload/history | TODO | - | แสดง utilization, queue time, runtime, failure และ heartbeat history |
| AUT-P2-005 | Bulk Revalidate/Repair | TODO | - | เลือก MaintenanceRequired หลาย Case แล้ว revalidate พร้อมผลรายรายการ |
| AUT-P2-006 | Export execution report | TODO | - | Export CSV/Excel ตาม filter พร้อม summary และ trace IDs |
| AUT-P2-007 | Stuck Job/Offline Agent alert | TODO | - | แจ้งเมื่อ heartbeat หาย, lease หมด, queue เกิน SLA และกดไปแก้ได้ |
| AUT-P2-008 | Accessibility/Responsive QA | TODO | - | ตรวจ 1440/1024/768/390px, keyboard, focus, modal และไม่มี page-level horizontal scroll |

## 9. Documentation และมาตรฐาน

| ID | งาน | สถานะ | Owner | Acceptance Criteria / หลักฐาน |
|---|---|---|---|---|
| AUT-DOC-001 | รวม Automation Roadmap | TODO | - | เอกสาร Phase 0–5 และ G0–G10 ไม่ขัดกันและอ้าง tracker นี้ |
| AUT-DOC-002 | อนุมัติ Selector Contract | BLOCKED | QA + ProMaxx2 Dev | ทีม Dev อนุมัติ contract และมี owner สำหรับแก้ missing/duplicate ID |
| AUT-DOC-003 | ขยาย P0 Smoke Set | TODO | - | มี 10–15 Case ครบ Pos/App และ stability run 5 รอบผ่าน ≥95% |
| AUT-DOC-004 | กำหนด Reliability KPI | TODO | - | flaky rate <5%, maintenance effort <20%, queue/runtime SLA และ pass-rate target |
| AUT-DOC-005 | Operational Runbook | TODO | - | ครอบคลุมติดตั้ง Agent, rotate secret, recovery, retry, data restore และ incident |

## 10. ลำดับดำเนินงานที่แนะนำ

1. `AUT-P0-003` ถึง `AUT-P0-006` — Object Repository และ Verification
2. `AUT-P0-007` ถึง `AUT-P0-011` — Maintenance/Failure/Retry/Quarantine
3. `AUT-TEST-001` ถึง `AUT-TEST-010` — Automated Tests
4. `AUT-P1-001` ถึง `AUT-P1-004` — Persistent Automation Suite
5. `AUT-P1-005` ถึง `AUT-P1-009` — Schedule และ Build Trigger
6. `AUT-DATA-001` ถึง `AUT-DATA-006` — Test Data Management
7. `AUT-REG-001` ถึง `AUT-REG-006` — Regression Closed Loop
8. `AUT-P2-001` ถึง `AUT-P2-008` — Monitoring, UX และ Scalability

## 11. Progress Log

### 2026-08-26 — สร้าง Tracker

- สร้าง Automation Development To-Do เป็นแหล่งติดตามงานคงค้างกลาง
- บันทึก baseline จากฐานข้อมูล: 11 Cases, 35 Executions, 23 Failed, 5 MaintenanceRequired, 5 Objects
- จัดลำดับงานเป็น P0 Runtime/Tests, P1 Suite/Schedule/Data/Regression และ P2 Monitoring/UX
- เพิ่มกฎใน `AGENTS.md` ให้ทุกงาน Automation ต้องอ่านและอัปเดตไฟล์นี้
