# แผนระบบสรุปข้อมูล Defect สำหรับ AI Local (Defect Knowledge)

> สถานะ: **แผน — ยังไม่เริ่มพัฒนา**
> จัดทำ: 25 กันยายน 2026
> ขอบเขต: Defect ที่พบจากการทดสอบ (Manual Execution, Auto-create เมื่อ Fail, Automation Failure) และ **เคสที่ CRM (BlueSea Helpdesk) ส่งมาหา QA** ทุก Project

## 1. เป้าหมาย

เมื่อพบ Defect ระบบต้องเก็บและสรุปข้อมูลเป็น **"บันทึกความรู้ของ Defect" (Defect Knowledge Record)** ที่มีโครงสร้างคงที่ สะอาด และตรวจสอบได้ เพื่อให้ในอนาคตนำไปใช้กับ AI ที่รันภายในองค์กร (เช่น Ollama / LM Studio) ได้ทันที โดยไม่ต้องย้อนไปแกะข้อมูลเก่า

สิ่งที่อยากให้ AI Local ทำได้ในอนาคต:

1. เตือน Defect ซ้ำตอนสร้าง Defect ใหม่ (แสดง Defect ที่คล้ายกัน 5 อันดับ)
2. แนะนำ Severity / Module / Root Cause จาก Defect ในอดีต
3. ตอบคำถามเกี่ยวกับ Defect แบบอ้างอิงรหัส Defect จริง (RAG) เช่น "Module Stock มีปัญหาแบบไหนบ่อยใน 3 Release ล่าสุด"
4. เขียนสรุปความเสี่ยงของ Release ให้หน้า Test Summary
5. ชี้ช่องว่างของ Test Case (Defect ที่หลุดไปโดยไม่มี Test Case ครอบคลุม)
6. เรียนรู้จากเคสที่ลูกค้าแจ้งผ่าน CRM — ปัญหาที่ลูกค้าเจอแต่ QA ไม่เจอ (Escaped Defect) และเรื่องที่ไม่ใช่ Bug แต่ถูกส่งมาบ่อย (ใช้งานผิด / ตั้งค่าผิด)

หลักการ: **AI เป็นผู้แนะนำเท่านั้น ไม่เปลี่ยนข้อมูลเอง** และข้อมูลไม่ออกนอกองค์กร

## 2. สภาพปัจจุบัน (สิ่งที่มี / สิ่งที่ขาด)

| ด้าน | มีแล้ว | ขาด |
|---|---|---|
| ข้อมูล Defect | Title, Severity, Status, Description, StepsToReproduce, Expected/Actual, Module, Release, Build, ผู้รับผิดชอบ (`Domain/Defects/Defect.cs`) | Root Cause, Resolution, Fixed-in Build, Duplicate-of, ResolvedAt/ClosedAt, Category, Tags |
| ความเชื่อมโยงกับการทดสอบ | ลิงก์ Test Case (`DefectTestCaseLink`), Automation Execution → Defect | **ไม่มี FK** ไป Test Cycle, Test Execution, Step ที่ Fail, Environment — ข้อมูลเหล่านี้อยู่แค่ในข้อความ Description ที่ `DefectAutoCreateService` / `ExecutionDefectEditor` สร้าง |
| ประวัติ | `DefectActivities` (สร้าง, เปลี่ยนสถานะ, คอมเมนต์, แนบไฟล์, CRM) | เก็บเป็นข้อความอิสระ ไม่มีค่าเดิม/ค่าใหม่ → คำนวณเวลาแก้ไข, จำนวนครั้งที่ Reopen ไม่ได้แม่นยำ |
| AI | `SharedAiConfigurationService` รองรับ Provider **Local** (OpenAI-compatible `{BaseUrl}/chat/completions`, ไม่ต้องใช้ API key) และมีตัวอย่าง `AutomationDefectService.AnalyzeAsync` | ไม่มี embeddings, ไม่มี vector search, ไม่มี full-text index |
| Automation | ErrorCode, ErrorMessage, ClassifiedFailureType, Evidence (screenshot) | ยังไม่ได้รวมเข้ากับสรุป Defect |
| CRM: ticket ที่ QA ส่งไป | ส่ง Defect ไปเป็น ticket; `CrmSyncWorker` ดึงสถานะ, Assignto และคำตอบ (`HelpDeskAnswerMain`) กลับมาเป็น `DefectActivity` (`CrmStatusChanged`, `CrmComment`, `CrmReturnedToOwner`) | เก็บเป็นข้อความในประวัติเท่านั้น ยังไม่แยกเป็นข้อมูลโครงสร้าง |
| CRM: ticket ที่ส่งมาหา QA | — | **ไม่มีเลย** — ticket ที่ลูกค้า/Support แจ้งแล้ว CRM ส่งมาหา QA ไม่เข้าระบบ; `CrmApiClient` รู้จักแค่ endpoint ดึงทีละใบ (`HelpDesksJob?JobNo=`) ยังไม่มี endpoint ดึง **รายการ** ticket ที่ assign ให้ QA |

ข้อสรุป: **ปัญหาหลักไม่ใช่ AI แต่เป็นคุณภาพและโครงสร้างของข้อมูล** — ต้องแก้ส่วนนี้ก่อน ข้อมูลที่เก็บตั้งแต่วันนี้จึงจะใช้กับ AI ได้ในวันหน้า

## 3. ภาพรวมสถาปัตยกรรม

```
 พบ Defect (Manual / Auto-create / Automation)      CRM ส่งเคสมาหา QA (ลูกค้า / Support แจ้ง)
        │  (Phase 1: เก็บ FK + ฟิลด์ปิดงานให้ครบ)          │  (Phase 1B: CrmInboundWorker → CRM Inbox → QA คัดกรอง)
        │                                              ├─ เป็น Bug → สร้าง/ผูก Defect (DetectedBy = Customer)
        │ ◄────────────────────────────────────────────┘  └─ ไม่ใช่ Bug → ปิดพร้อมเหตุผล (ยังเป็นความรู้)
        ▼
 Defect + Activities + Test Case/Step + Execution + Automation Evidence + CRM Ticket / คำตอบ
        │  DefectKnowledgeBuilder (rule-based, ไม่ใช้ AI)   ← Phase 2
        │  + Masking ข้อมูลอ่อนไหว
        ▼
 DefectKnowledgeRecords (ประเภท Defect / CrmTicket — JSON โครงสร้างคงที่ + ข้อความมาตรฐาน + hash + version)
        │                         │
        │ Phase 3                 │ Phase 4 (อนาคต)
        ▼                         ▼
 หน้า "สรุปความรู้" / Dashboard     AI Local (Ollama ผ่าน Provider "Local")
 Export JSONL สำหรับ AI            ├─ AI summary + แนะนำ Root Cause (ต้องมีคนรีวิว)
                                  ├─ Embeddings → ค้นหา Defect คล้ายกัน / เตือนซ้ำ
                                  └─ ถาม-ตอบแบบ RAG อ้างอิงรหัส Defect
```

## 4. Phase 1 — เก็บข้อมูลให้ครบตั้งแต่ต้นทาง (ต้องทำก่อน)

### 4.1 ฟิลด์ใหม่ใน `Defect` (migration ใหม่ ค่าเดิมเป็น null ได้)

| ฟิลด์ | ชนิด | ใช้ทำอะไร |
|---|---|---|
| `TestCycleId`, `TestExecutionId`, `TestStepResultId` / `FailedStepNo`, `EnvironmentId` | FK / int, nullable | เชื่อม Defect กับการทดสอบที่พบจริง แทนการเก็บเป็นข้อความ |
| `DetectedBy` | enum: Manual, AutoOnFail, Automation, Exploratory, Customer | แยกแหล่งที่พบ (ใช้วิเคราะห์ Defect ที่หลุดถึงลูกค้า) |
| `RootCauseCategory` | Master Option | หมวดสาเหตุ เช่น Code Logic, Requirement ไม่ชัด, Data/Migration, Config/Environment, UI/UX, Integration/CRM, Performance, Regression, Test Case/Test Data ผิด |
| `RootCauseNote` | nvarchar(2000) | คำอธิบายสาเหตุจากผู้แก้ |
| `ResolutionType` | enum: Fixed, Duplicate, CannotReproduce, ByDesign, WontFix, Deferred | ผลการปิดงาน |
| `ResolutionNote` | nvarchar(2000) | แก้อย่างไร |
| `FixedInBuildId` | FK Build | Build ที่แก้แล้ว (ใช้คำนวณ Defect ต่อ Build/Release) |
| `DuplicateOfDefectId` | FK Defect | **ป้ายกำกับสำคัญที่สุด** สำหรับสอน/ประเมินระบบเตือน Defect ซ้ำ |
| `ResolvedAt`, `ClosedAt`, `ReopenCount` | datetime / int | เวลาแก้ไขและคุณภาพการแก้ |
| `Tags` | ตาราง `DefectTags` | ป้ายอิสระ เช่น `ภาษาไทย`, `PDF`, `ปิดวัน` |

### 4.2 กติกาใน UI / API

- เปลี่ยนสถานะเป็น **Resolved / Closed / Rejected** ต้องเลือก `ResolutionType` และ (ถ้า Fixed) `RootCauseCategory` — ใช้ modal ตาม `UI_DESIGN_SYSTEM.md` §9
- เลือก Duplicate ต้องระบุ Defect ต้นฉบับ
- `DefectAutoCreateService`, `ExecutionDefectEditor` และ `AutomationDefectService` เติม FK ใน 4.1 ให้อัตโนมัติ (ข้อความ Description เดิมยังคงไว้)

### 4.3 ประวัติแบบมีโครงสร้าง

เพิ่ม `FieldName`, `FromValue`, `ToValue` ใน `DefectActivity` สำหรับ Status / Severity / Assignee / Module เพื่อคำนวณ Time-to-Resolve, จำนวนครั้งที่ Reopen และการส่งต่อระหว่างคน

**Acceptance:** Defect ใหม่ทุกช่องทางมี FK ไป Execution/Cycle (เมื่อสร้างจากการทดสอบ), ปิด Defect ไม่ได้ถ้าไม่ระบุ Resolution, Activity เปลี่ยนสถานะมีค่าเดิม/ใหม่, unit test ครอบคลุมทั้ง 3 ช่องทางสร้าง

## 4B. Phase 1B — รับเคสจาก CRM ที่ส่งมาหา QA (CRM Inbox)

### 4B.1 Phase 0 ของส่วนนี้: หา endpoint รายการ ticket (blocker)

- endpoint ที่รู้จักตอนนี้ (`CRM_INTEGRATION_PLAN.md` §4) ดึงได้ทีละ ticket เท่านั้น — ต้องหา API ที่หน้า Support list ของ BlueSea (`/bluesea/BookLicence/MA/Support`) ใช้โหลดตาราง (น่าจะเป็น datatable `draw/start/length` แบบเดียวกับ `HelpDeskAnswerMain`) หรือขอจากทีม CRM
- ต้องกรองได้อย่างน้อย: `Assignto` (รหัสพนักงาน QA), สถานะ, ช่วงวันที่, ประเภทงาน (`SysSrviceType`)
- ต้องตกลงว่า "ส่งมาหา QA" หมายถึงอะไร: assign ให้พนักงาน QA รายคน, กลุ่ม/ทีม QA หรือ Followup เฉพาะ
- **ถ้ายังไม่มี endpoint รายการ:** ใช้ทางสำรองชั่วคราว — ให้ QA กรอกเลข JobNo ในหน้า CRM Inbox เพื่อดึงเข้ามาทีละใบ (ใช้ `HelpDesksJob` ที่มีอยู่)

### 4B.2 ตาราง `CrmInboundTickets`

| ฟิลด์ | คำอธิบาย |
|---|---|
| `CrmInboundTicketId`, `JobNo` (unique) | เลข ticket ใน CRM |
| `ProjectId`, `ReleaseId?` | map จาก CRM Product/Version ด้วย `CrmProjectMapping` ที่มีอยู่แล้ว (ไม่ map ได้ = รอ QA เลือกเอง) |
| `Subject`, `Description` | อาการที่ลูกค้าแจ้ง (เก็บต้นฉบับไว้ในตารางนี้ — แสดงเฉพาะผู้มีสิทธิ์ Project; ส่งไปบันทึกความรู้หลัง mask เท่านั้น) |
| `ServiceType`, `CrmStatus`, `Assignto`, `OwnerSubjectId`, `Source`, `CustomerType`, `BranchId` | ข้อมูลจาก CRM |
| `ReportedAt`, `ReceivedByQaAt`, `LastPolledAt` | เวลาที่ลูกค้าแจ้ง / เวลาที่ส่งมาถึง QA / ดึงล่าสุด |
| `TriageStatus` | `New`, `Investigating`, `Reproduced`, `NotReproducible`, `NotBug`, `Duplicate`, `Closed` |
| `TriageReason` | Master Option เมื่อไม่ใช่ Bug เช่น ใช้งานผิด, ตั้งค่าผิด, ข้อมูลลูกค้า, Feature Request, ปัญหาเครื่อง/เครือข่าย |
| `TriageNote`, `TriagedByUserId`, `TriagedAt` | บันทึกการคัดกรอง |
| `LinkedDefectId` | Defect ที่สร้างหรือผูก (Duplicate = ผูกกับ Defect เดิม) |

คำตอบและประวัติใน CRM เก็บตาราง `CrmInboundAnswers` (AnswerNo, Description, Posted, AnsDate, AnswerType, มีไฟล์แนบหรือไม่) ดึงจาก `HelpDeskAnswerMain` และ `HelpDeskAnswerChangeHistory`

### 4B.3 การดึงข้อมูล

- `CrmInboundWorker` (BackgroundService รูปแบบเดียวกับ `CrmSyncWorker`) ทุก 5–10 นาที ดึง ticket ใหม่/ที่เปลี่ยน ของพนักงาน QA ที่ตั้งค่า CRM credential ไว้แล้ว (per-user ตาม `CRM_INTEGRATION_PLAN.md` §5.4) — ไม่ใช้ Service Account กลาง
- upsert ตาม `JobNo`, ดึงคำตอบใหม่ด้วย `AnswerNo` ล่าสุดแบบเดียวกับ `CrmLastSeenAnswerNo`
- ปิด/เปิดและตั้งรอบได้จาก Service Manager; ถ้า CRM ล้มเหลวให้บันทึก error และไม่หยุด worker (แบบ AUT-REL-003)

### 4B.4 หน้า CRM Inbox (ในเมนู Defect)

- รายการ ticket ที่รอคัดกรอง กรองตามสถานะ/Project/ผู้รับ, แสดงอายุของเคส
- การกระทำ:
  - **สร้าง Defect จาก ticket** — เติม Title/Description/Project/Release จาก ticket, `DetectedBy = Customer`, ผูก `CrmTicketId` เป็น JobNo เดิม (ไม่สร้าง ticket ใหม่ใน CRM — ใช้ sync 2 ทางที่มีอยู่ต่อได้ทันที)
  - **ผูกกับ Defect ที่มีอยู่** (Duplicate)
  - **ไม่ใช่ Bug / ทำซ้ำไม่ได้** — ต้องเลือกเหตุผล
- ticket ที่ถูกผูกกับ Defect แล้ว ให้ `CrmSyncWorker` เดิมดูแลต่อ (สถานะ/คำตอบ)

### 4B.5 แยกข้อมูล CRM ของ ticket ที่ QA ส่งไปให้เป็นโครงสร้าง

`CrmSyncService` บันทึกคำตอบและการเปลี่ยนสถานะลงตาราง `CrmInboundAnswers` / activity แบบมีค่าเดิม-ใหม่ (ตาม 4.3) ด้วย เพื่อให้ ticket ทั้ง 2 ทิศทางใช้โครงสร้างเดียวกัน

**Acceptance:** ticket ที่ assign ให้ QA ปรากฏใน CRM Inbox ภายในรอบ worker, คัดกรองแล้วมีผล (Defect / เหตุผล) ครบทุกใบ, สร้าง Defect จาก ticket แล้ว sync สถานะ CRM ต่อได้โดยไม่เกิด ticket ซ้ำ, ข้อมูลลูกค้าไม่ออกนอกตาราง `CrmInboundTickets` โดยไม่ mask, ผู้ใช้เห็นเฉพาะ Project ที่มีสิทธิ์

## 5. Phase 2 — Defect Knowledge Record (สรุปแบบ rule-based)

### 5.1 ตาราง `DefectKnowledgeRecords`

| คอลัมน์ | คำอธิบาย |
|---|---|
| `KnowledgeRecordId` (PK) | |
| `RecordType` | `Defect` หรือ `CrmTicket` (ticket จาก CRM ที่คัดกรองแล้วว่าไม่ใช่ Bug — ถ้าเป็น Bug จะรวมอยู่ใน record ของ Defect นั้น) |
| `DefectId?` / `CrmInboundTicketId?` (unique ตามประเภท) | 1 Defect หรือ 1 ticket ต่อ 1 record |
| `ProjectId` | ใช้กรองสิทธิ์ตาม Project (กติกาเดียวกับ `ProjectAccessFilter`) |
| `SchemaVersion` | เวอร์ชันโครงสร้าง JSON (เริ่ม `1`) |
| `SummaryJson` | ข้อมูลโครงสร้างตาม 5.2 (ผ่านการ mask แล้ว) |
| `CanonicalText` | ข้อความภาษาไทยรูปแบบคงที่ สำหรับทำ embedding/อ่าน |
| `ContentHash` | SHA-256 ของข้อมูลต้นทาง — ไม่เปลี่ยนก็ไม่สร้างใหม่ |
| `State` | `Draft` (ยังไม่ปิด), `Final` (ปิดแล้ว), `Stale` (ข้อมูลต้นทางเปลี่ยน รอสร้างใหม่) |
| `ReviewedByUserId`, `ReviewedAt` | QA Lead รับรองว่าใช้เป็นข้อมูลอ้างอิงได้ |
| `ExcludeFromAi` | ไม่ให้ส่งไป AI (เช่น มีข้อมูลลูกค้า) |
| `GeneratedAt`, `AiSummaryJson`, `AiModel` | ส่วนของ AI (ว่างจนถึง Phase 4) |

### 5.2 โครงสร้าง `SummaryJson` (v1)

```json
{
  "schemaVersion": 1,
  "defect": { "code": "DEF-1021", "title": "...", "severity": "High", "status": "Closed", "detectedBy": "AutoOnFail",
              "createdAt": "...", "resolvedAt": "...", "closedAt": "...", "reopenCount": 0, "daysToResolve": 3 },
  "context": { "project": "PMX2", "modulePath": ["Sales / POS", "ส่วนลด"], "release": "REL-2026.09", "build": "10.0.228",
               "fixedInBuild": "10.0.230", "environment": "UAT", "cycle": "TC-UAT-05 (Regression)" },
  "reproduction": { "preconditions": "...", "steps": [{ "no": 1, "action": "...", "data": "...", "expected": "...", "actual": "...", "status": "Fail" }],
                    "failedStepNo": 3, "expected": "...", "actual": "..." },
  "testCoverage": { "testCases": ["TC-SALE-201"], "requirements": ["REQ-SALE-014"] },
  "automation": { "errorCode": "AUT-...", "failureType": "ProductDefect", "evidence": ["screenshot:step3"] },
  "customerReport": { "crmJobNo": "HD-...", "direction": "CrmToQa", "reportedAt": "...", "receivedByQaAt": "...",
                      "customerType": "[CUSTOMER_TYPE]", "symptom": "อาการที่ลูกค้าแจ้ง (mask แล้ว)", "escaped": true,
                      "foundInRelease": "REL-2026.09", "triage": { "status": "Reproduced", "reason": null, "hoursToTriage": 5 },
                      "crmAnswers": [{ "at": "...", "by": "[STAFF]", "text": "คำตอบใน CRM (mask แล้ว)" }],
                      "hoursReportToFix": 52 },
  "resolution": { "type": "Fixed", "rootCauseCategory": "Code Logic", "rootCauseNote": "...", "resolutionNote": "...", "duplicateOf": null },
  "timeline": [{ "at": "...", "field": "Status", "from": "Open", "to": "In Progress" }],
  "discussion": ["คอมเมนต์ที่ mask แล้ว (สูงสุด N รายการ)"],
  "attachments": [{ "type": "image/png", "name": "step3.png" }],
  "tags": ["ภาษาไทย"]
}
```

- `customerReport` มีเมื่อ Defect มาจาก CRM Inbox หรือถูกส่งไป CRM (`direction` = `CrmToQa` / `QaToCrm`); `escaped = true` เมื่อลูกค้าพบหลัง Release ออกไปแล้ว
- record ประเภท `CrmTicket` ใช้ส่วน `context`, `customerReport` และ `resolution` (โดย `resolution.type` = เหตุผลคัดกรอง เช่น ใช้งานผิด / ตั้งค่าผิด) ไม่มีส่วน reproduction

`CanonicalText` สร้างจากเทมเพลตตายตัว เช่น `ปัญหา: … | โมดูล: … | ขั้นตอนที่ผิด: … | ผลที่คาดหวัง: … | ผลจริง: … | สาเหตุ: … | วิธีแก้: …` เพื่อให้ embedding เทียบกันได้ยุติธรรม

### 5.3 การสร้างและอัปเดต

- `DefectKnowledgeBuilder` (Application layer, ไม่เรียก AI) รวบรวมข้อมูลจากตารางใน Phase 1
- ทุกครั้งที่ Defect / Activity / Link เปลี่ยน → ตั้ง `State = Stale` (ถูกกว่าสร้างทันที)
- `DefectKnowledgeWorker` (BackgroundService รูปแบบเดียวกับ `CrmSyncWorker`) สร้าง record ที่ Stale ทุก 5 นาที ทีละ batch; ปิด/เปิดได้จาก Service Manager
- Endpoint backfill สำหรับ Defect เก่า (ข้อมูลเก่าจะไม่มี Root Cause — ติด flag `incomplete`)

### 5.4 Masking ข้อมูลอ่อนไหว (ทำก่อนบันทึกทุกครั้ง)

อีเมล, เบอร์โทร/แฟกซ์, เลขบัตรประชาชน 13 หลัก, เลขบัตรเครดิต, token/password/connection string, IP ภายใน, ชื่อลูกค้าตามรายการที่กำหนด → แทนด้วย `[EMAIL]`, `[PHONE]` ฯลฯ มี unit test ชุดตัวอย่างภาษาไทย

ข้อมูลจาก CRM ต้อง mask เพิ่ม: ชื่อลูกค้า/ร้านค้า, สาขา (`BranchId` → `[BRANCH]`), MerchantID, ชื่อพนักงาน Support ในคำตอบ (`[STAFF]`), ที่อยู่ — ต้นฉบับอยู่เฉพาะใน `CrmInboundTickets` / `CrmInboundAnswers` เท่านั้น

**Acceptance:** Defect ที่ปิดแล้วมี record `Final` ภายใน 10 นาที, hash เดิมไม่สร้างซ้ำ, masking ผ่าน test, ผู้ใช้เห็นเฉพาะ record ของ Project ที่มีสิทธิ์

## 6. Phase 3 — หน้าสรุปและ Export

- **หน้า Defect → แท็บ "สรุปความรู้"**: แสดง record, ปุ่มสร้างใหม่, ปุ่ม "รับรองแล้ว" (QA Lead), สวิตช์ "ไม่ส่งให้ AI", แจ้งเตือนฟิลด์ที่ขาด (เช่น ไม่มี Root Cause)
- **Dashboard / Test Summary**: สัดส่วน Root Cause ต่อ Module/Release, เวลาเฉลี่ยในการแก้, อัตรา Reopen, Defect ที่ไม่มี Test Case ครอบคลุม (Test gap) — คำนวณจาก record ไม่ต้องใช้ AI
- **สถิติจาก CRM**:
  - **Escaped Defect rate** ต่อ Release = Defect ที่ลูกค้าพบ ÷ Defect ทั้งหมดของ Release นั้น, แยกตาม Module
  - Module ที่ลูกค้าแจ้งบ่อยที่สุด และในนั้นมีกี่เคสที่ไม่มี Test Case ครอบคลุม → ส่งเป็นรายการแนะนำเพิ่ม Test Case
  - เรื่องที่ไม่ใช่ Bug แยกตามเหตุผล (ใช้งานผิด / ตั้งค่าผิด ฯลฯ) → ข้อมูลให้ทีมเอกสาร/Support
  - เวลาตั้งแต่ลูกค้าแจ้ง → QA คัดกรอง → แก้เสร็จ
- **Export** `GET /api/v1/defect-knowledge/export?projectId=&releaseId=&from=&to=&state=Final&reviewedOnly=true`
  - ส่งออก JSONL (1 บรรทัด = 1 record) + `manifest.json` (schemaVersion, จำนวน, ช่วงวันที่, hash) สำหรับนำเข้า AI Local แบบ offline
  - สิทธิ์ใหม่ `DEFECT.KNOWLEDGE.EXPORT`, เคารพ `AllowedProjectIds`, ไม่รวม `ExcludeFromAi`, บันทึก Audit Log

**Acceptance:** Export ได้ไฟล์ที่ validate ผ่าน JSON Schema v1, ข้อมูลข้าม Project ไม่หลุด, UI ผ่าน Desktop/Mobile ตาม `UI_DESIGN_SYSTEM.md`

## 7. Phase 4 — เชื่อม AI Local (อนาคต)

| ความสามารถ | วิธี | หมายเหตุ |
|---|---|---|
| AI summary + แนะนำ Root Cause | ใช้ `SharedAiConfigurationService` Provider **Local** (Ollama `/v1`) + json_schema แบบเดียวกับ `AutomationDefectService.AnalyzeAsync` | เก็บใน `AiSummaryJson` พร้อม confidence, แสดงเป็น "คำแนะนำ" ต้องกดยืนยัน |
| Embeddings | เพิ่มการเรียก `{BaseUrl}/embeddings` ใน service กลาง; โมเดลแนะนำ `bge-m3` (รองรับไทย) | เก็บตาราง `DefectKnowledgeEmbeddings` (DefectId, Model, Dim, Vector) |
| ค้นหา Defect คล้ายกัน / เตือนซ้ำ | cosine similarity ระหว่าง `CanonicalText` ของ Defect ใหม่กับ record เดิมใน Project เดียวกัน | Defect หลักพันคำนวณในแอปได้; ถ้าโตมากใช้ SQL Server 2025 `VECTOR` หรือ Qdrant ภายใน |
| ถาม-ตอบ (RAG) | ค้น top-k record → ส่งให้ LLM Local ตอบพร้อมอ้างอิงรหัส Defect | จำกัดตามสิทธิ์ Project ของผู้ถาม |
| Release risk narrative | สรุปจาก record ของ Release ให้หน้า Test Summary | ต่อยอด narrative เดิม |

**การประเมินผล:** ใช้ record ที่ `Reviewed` + `DuplicateOfDefectId` เป็นชุดทดสอบ วัด precision@5 ของการเตือน Defect ซ้ำ และความแม่นของ Root Cause ที่แนะนำ ก่อนเปิดให้ทุกคนใช้

## 8. Governance

- ข้อมูลอยู่ในองค์กรเท่านั้น (AI Local) — ห้ามตั้ง Provider ภายนอกให้ฟีเจอร์นี้โดยไม่ผ่านการอนุมัติ
- ทุกการ Export และการเรียก AI บันทึก Audit Log
- `SchemaVersion` เปลี่ยนเมื่อโครงสร้าง JSON เปลี่ยน และมี migration สร้าง record ใหม่
- กำหนดระยะเวลาเก็บข้อมูลและขั้นตอนลบเมื่อ Defect ถูกลบ (soft delete → ลบ record + embedding)

## 9. ลำดับและขนาดงานโดยประมาณ

| Phase | งาน | ขนาด |
|---|---|---|
| 1 | Migration ฟิลด์ใหม่, กติกาปิด Defect, เติม FK 3 ช่องทาง, Activity แบบมีโครงสร้าง, tests | M (≈1–1.5 สัปดาห์) |
| 1B | หา endpoint รายการ ticket, ตาราง CRM Inbound, `CrmInboundWorker`, หน้า CRM Inbox + คัดกรอง, tests | M (≈1–1.5 สัปดาห์ หลังได้ endpoint; ทางสำรองกรอก JobNo ≈3 วัน) |
| 2 | ตาราง record, Builder, Masking, Worker, Backfill, tests | M (≈1 สัปดาห์) |
| 3 | แท็บสรุปความรู้, Dashboard metrics, Export JSONL + สิทธิ์ | S–M (≈1 สัปดาห์) |
| 4 | AI summary, embeddings, similar/duplicate, RAG | L (หลังเลือกเครื่อง/โมเดล) |

แนะนำเริ่ม **Phase 1 ทันที** แม้ AI Local ยังไม่พร้อม เพราะข้อมูลที่ไม่ได้เก็บวันนี้ย้อนกลับมาเก็บทีหลังไม่ได้

## 10. เรื่องที่ต้องตัดสินใจ

1. รายการ **Root Cause Category** ที่ทีมจะใช้ (ร่างไว้ใน 4.1) และบังคับกรอกตอนปิดหรือไม่
2. เครื่องที่จะรัน AI Local (มี GPU หรือไม่) และโมเดล — ส่งผลต่อ Phase 4 เท่านั้น
3. เวอร์ชัน SQL Server ที่ใช้จริง (2025 รองรับ `VECTOR` ในตัว) หรือจะใช้ vector store แยก
4. รายการข้อมูลที่ถือว่าอ่อนไหว (ชื่อลูกค้า/สาขา/ข้อมูลร้านค้า) สำหรับ masking
5. ใครมีสิทธิ์รับรอง record และสิทธิ์ Export
6. **CRM:** นิยาม "ticket ที่ส่งมาหา QA" (assign ให้พนักงาน QA รายคน / กลุ่ม QA / Followup เฉพาะ) และประเภทงานที่นับ
7. **CRM:** ขอ endpoint รายการ ticket จากทีม CRM หรือให้ reverse-engineer จากหน้า Support list — ระหว่างรอจะใช้ทางสำรองกรอก JobNo หรือไม่
8. **CRM:** รายการเหตุผลกรณี "ไม่ใช่ Bug" สำหรับการคัดกรอง
