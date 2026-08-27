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
| AUT-P0-001 | เพิ่ม Edit Action Library | DONE | Codex | แก้ Name, Category, Description, Parameter Schema JSON, Handler และ Minimum Agent Version ผ่าน modal; server ตรวจ JSON; `AutomationManagementTests`; build/lint + unit tests 70 ผ่าน (2026-08-26) |
| AUT-P0-002 | เพิ่ม Activate/Deactivate Action | DONE | Codex | เปิด/ปิดจาก row action พร้อม confirm; backend update `IsActive`; Validator ใช้เฉพาะ Action active; build/lint + unit tests 70 ผ่าน (2026-08-26) |
| AUT-P0-003 | เพิ่ม Edit Object Repository | DONE | Codex | แก้ Application, Screen, Object Code/Name, Control Type, AutomationId, Selector JSON; ป้องกัน Business Key ซ้ำ; ObjectVersion เพิ่ม; build/lint + unit tests 70 ผ่าน (2026-08-26) |
| AUT-P0-004 | เพิ่ม Activate/Deactivate Object | DONE | Codex | เปิด/ปิดจาก row action พร้อม confirm; endpoint activate/deactivate; Validator ใช้เฉพาะ Object active; build/lint + unit tests 70 ผ่าน (2026-08-26) |
| AUT-P0-005 | Import Object จาก UI Inspector/AutomationId Scanner | DONE | Codex | เพิ่ม modal Import Scanner ใน Object Repository รองรับ JSON/CSV, Preview diff, เลือกเฉพาะแถว Ready, ป้องกัน Business Key/AutomationId ซ้ำฝั่ง UI และ API; frontend build + backend build ผ่าน (2026-08-26) |
| AUT-P0-006 | Object Verification Tool | DONE | Claude | ตาราง `AutomationObjectVerifications`; endpoint `POST objects/verify`/`GET objects/verifications` (Hub) + `POST verifications/claim`/`verifications/result` (Agent); Agent `ProMaxx2.Automation.Core/ObjectVerifier.cs` เทียบ AutomationId/ControlType จาก `UiInspector` scan คืน Found/NotFound/Duplicate/ControlTypeMismatch พร้อมเวลา+Agent; คำสั่งใหม่ `runner verify --exe <path>`; UI: เลือก Object หลายรายการ + ปุ่มตรวจสอบ + modal ผลตรวจสอบใน Object Repository; **หมายเหตุ: Agent build ผ่านแล้วแต่ยังไม่ได้ทดสอบกับ ProMaxx2.exe จริง ต้องให้ QA ทดสอบภาคสนามบนเครื่องที่มี AUT ติดตั้งจริง** (2026-08-26) |
| AUT-P0-007 | ซ่อม Case ที่เป็น MaintenanceRequired | DONE | Claude | `AutomationCase` เพิ่ม MaintenanceReason/Owner/OpenedAt; `RequireMaintenance` บันทึกสาเหตุอัตโนมัติจาก ErrorMessage ตอน Complete; endpoint `POST cases/{id}/maintenance/owner`/`maintenance/resolve` (resolve → กลับสถานะ NeedsReview บังคับ Validate/อนุมัติใหม่); UI panel ในหน้า Case Detail (2026-08-26) |
| AUT-P0-008 | วิเคราะห์ Execution Failed เดิม | DONE | Claude | Persist `ClassifiedFailureType`/`ClassifiedRecommendation` บน `AutomationExecution` อัตโนมัติทุกครั้งที่ Complete ด้วย `AutomationFailureClassifier` เดิม; endpoint `GET failures/dashboard`/`failures/executions` กรอง/จัดกลุ่มตาม Failure Type, Build, Agent, Automation Case (2026-08-26) |
| AUT-P0-009 | Retry Policy | DONE | Claude | ตาราง `AutomationRetryPolicySettings` (MaxAttempts/BackoffSeconds/Enabled, default 2/30s); `AutomationAction.RetrySafety` (Safe/Unsafe/Conditional); auto-retry ใน `CompleteExecutionAsync` เมื่อ Recommendation เป็น Retry/RetryOrCheckEnvironment และไม่มี Unsafe step สำเร็จ, backoff ผ่าน `AutomationJob.QueuedAt` ในอนาคต + filter ใน `ClaimNextJobAsync`; endpoint `GET`/`PUT settings/retry-policy`; UI แสดง Retry Count บน Execution/Job และตั้งค่า Retry Policy ในหน้าจัดการ (2026-08-26) |
| AUT-P0-010 | Flaky Test Quarantine | DONE | Claude | `AutomationCase` เพิ่ม IsQuarantined/QuarantineReason/Owner/ExpiresAt (แยกจาก Status หลัก); `GetFlakyCandidatesAsync` ตรวจ transition Pass/Fail ≥2 ครั้งใน 5 execution ล่าสุด; endpoint `GET cases/flaky-candidates`, `POST cases/{id}/quarantine`/`unquarantine`; UI panel "Flaky Candidates" ในหน้า Cases + badge Quarantined (2026-08-26) |
| AUT-P0-011 | Failure Dashboard | DONE | Claude | Tab ใหม่ "Failure Dashboard" กรอง Failure Type/Build/Agent/ช่วงวันที่ พร้อม breakdown by Build/Agent/Top Automation Case และตาราง drill-down เปิด Execution Detail เดิม (2026-08-26) |
| AUT-P0-012 | Runner ส่ง execution-level ErrorCode ที่แท้จริงแทนการ hardcode | TODO | - | พบระหว่าง AUT-TEST-009 (2026-08-27): `agent/ProMaxx2.Automation.Runner/Program.cs:285` hardcode ErrorCode="AUT-UI-003"/FailureType="AssertionFailure" ทุกครั้งที่ execution fail โดยไม่สนใจ ErrorCode จริงของ step ที่ fail (ทั้งที่ `ActionExecutor` รายงานโค้ดที่ถูกต้องระดับ step แล้วหลัง AUT-TEST-009 แก้ `ExtractErrorCode`) ผลคือ server-side `AutomationFailureClassifier`/Retry Policy (AUT-P0-008/009) แทบไม่เห็นโค้ด AUT-DB-*/AUT-APP-*/AUT-AGENT-* จาก execution จริงเลย ทำให้ recommendation "Retry"/"RetryOrCheckEnvironment" แทบไม่ทำงานในสนามจริง ต้องแก้ Runner ให้ forward ErrorCode/FailureType จาก step ที่ fail จริง (หรือจาก step แรกที่ fail ถ้ามีหลาย step) แทนการ hardcode — ควรให้ทีมตัดสินใจ mapping ที่ถูกต้องก่อนแก้ เพราะกระทบพฤติกรรม retry ในสนามจริง |
| AUT-P0-013 | DB Validator: ConnectTimeout + CancellationToken ไม่ถูกใช้จริง | TODO | - | พบระหว่าง AUT-TEST-010 (2026-08-27): (1) `SqlServerDbValidator`/`FirebirdDbValidator` ไม่ set `ConnectTimeout` บน connection string เลย ใช้ default provider (~15s) ทำให้ตอน DB ต่อไม่ติดจริงในสนาม (เช่น network ช้า/ตาย) ต้องรอนานกว่าจะ fail แทนที่จะ fail เร็วแล้วให้ retry/แจ้งเตือน; (2) `ValidateAsync(request, ct)` รับ `CancellationToken` มาแต่ไม่เคยใช้เลยทั้งสอง validator (`con.Open()`/`cmd.ExecuteReader()` เป็น sync call ไม่ใช่ async+ct overload) แปลว่า DB assertion step ที่ query ค้างนานจะถูก cancel ไม่ได้จริงแม้ execution จะถูก Cancel จาก UI ก็ตาม ควรแก้ให้ set ConnectTimeout สั้นลง (เช่น ผูกกับ `ActionTimeout`) และเปลี่ยนไปใช้ async+ct overload ทั้งสอง validator |

## 4. P0 — Automated Tests

| ID | งาน | สถานะ | Owner | Acceptance Criteria / หลักฐาน |
|---|---|---|---|---|
| AUT-TEST-001 | DSL และ Validator tests | DONE | Claude | `AutomationDslValidatorTests.cs` 17 tests ครอบคลุม DslVersion/AutomationType required, unsupported version, empty/duplicate/non-contiguous/non-positive step no, unknown Action/Object/TestData (ทั้งกรณี library ว่างและไม่ว่าง) (2026-08-27) |
| AUT-TEST-002 | Automation Case workflow tests | DONE | Claude | `AutomationCaseWorkflowTests.cs` + `AutomationTestFixtures.cs` (shared InMemory seed helper) ครอบคลุม Draft → version → Validate → Approve → Ready, MaintenanceRequired → AssignOwner → Resolve → NeedsReview, non-candidate TestCase reject, invalid DSL → version Invalid + case NeedsReview, resolve-maintenance guard เมื่อไม่ได้อยู่ MaintenanceRequired (2026-08-27) |
| AUT-TEST-003 | Atomic Job Claim tests | DONE | Claude | `AutomationJobClaimTests.cs` 7 tests: claim ที่ 2 ไม่ได้ job เดิมซ้ำ, claim สำเร็จ assign job+agent ถูกต้องและ execution เริ่ม Running, agent ปิดใช้งาน claim ไม่ได้, ไม่มี job คิว claim ได้ null, job ที่ QueuedAt อยู่อนาคต (backoff) claim ไม่ได้ยัง, priority ต่ำกว่าถูก claim ก่อน, domain guard `AutomationJob.Assign` throw เมื่อ job ไม่ใช่ Queued แล้ว; **หมายเหตุ:** โค้ดจริงไม่มี field "lease token" แยกต่างหาก — ความ atomic มาจาก domain guard นี้ + Serializable transaction ใน `ClaimNextJobAsync` (relational provider เท่านั้น); EF InMemory ไม่รองรับ transaction จึงเทสต์ครอบคลุมเฉพาะ sequential invariant ไม่ใช่ concurrent race จริง — ต้องทำ integration test บน SQL Server/SQLite ถ้าต้องการยืนยัน race condition จริง (2026-08-27) |
| AUT-TEST-004 | Cancel/Timeout/Lease Recovery tests | DONE | Claude | `AutomationCancelTimeoutTests.cs` 9 tests: cancel Queued/Running (execution+job→Cancelled, case กลับ Ready), cancel execution ที่ terminal แล้วต้อง throw 409, Timeout/AgentLost complete ถูก classify, **late/duplicate result ไม่ทับสถานะเดิม** (agent ส่งผลช้าหลัง cancel หรือส่งซ้ำ) — แก้ bug จริงที่พบระหว่างเขียนเทสต์: เพิ่ม guard ใน `AutomationExecution.Complete`/`AutomationJob.Complete` ให้ throw ถ้าสถานะ terminal อยู่แล้ว และ `CompleteExecutionAsync` เช็คก่อนแล้ว return สถานะปัจจุบันแบบ idempotent แทนที่จะ overwrite; **หมายเหตุ:** ยังไม่มี background lease-expiry watchdog (ไม่มี hosted service ตรวจ stale Running job จาก heartbeat หมดอายุแล้ว mark AgentLost อัตโนมัติ) — ส่วน "expired lease" ใน AC เดิมยังไม่ implement จริง แนะนำเปิดเป็นงานใหม่ (AUT-P1 หรือ P2) ถ้าต้องการ auto-recovery จริง (2026-08-27) |
| AUT-TEST-005 | Retry tests | DONE | Claude | `AutomationRetryTests.cs` 7 tests: retryable failure สร้าง retry execution เดียว (RetryOfExecutionId/RetryCount ถูก, backoff ทำให้ claim ทันทีไม่ได้), retry หยุดเมื่อ RetryCount ถึง MaxAttempts (ไล่ retry 2 รอบด้วย backoff=0 แล้วยืนยันไม่มีรอบที่ 3), non-retryable UI failure (AUT-UI-001) ไม่ retry และเข้า MaintenanceRequired, error code ที่ไม่รู้จักไม่ retry case กลับ Ready, retry policy Disabled ไม่สร้าง retry เลย, unsafe action ที่ Pass ไปแล้วบล็อก retry แม้ classification บอกว่า retryable, duplicate completion report ไม่สร้าง retry ซ้ำ (2026-08-27) |
| AUT-TEST-006 | Batch Run/Multi-Agent tests | DONE | Claude | `AutomationBatchRunTests.cs` 7 tests: batch สร้าง execution ต่อ 1 Ready case และ skip case ที่ยังไม่ approve, dedup case id ซ้ำ, request ว่างเปล่า throw, 2 agent ไล่ claim คิวเดียวกันไม่ชนกันและผลรวมครบ (4/4), target routing กรณี non-WindowsUI poller ถูก, non-retryable target mismatch, requested-agent ไม่ถูกบังคับจริง; **หมายเหตุ:** พบว่า target-based routing ทำงานทางเดียวเท่านั้น — poller ที่ใช้ targetApp="WindowsUI" (default) ไม่มี filter เลย เคลม job ของ AutomationType ไหนก็ได้ (ตรงข้ามกับที่คาดตอนแรก), ไม่มี capability-based routing จริง (`capabilities` param ถูกรับแต่ไม่ใช้กรองเลย), และ `AutomationJob.RequestedAgentId` (จาก `BatchRunRequest.AgentId`) ไม่ถูกบังคับตอน claim — agent ไหนก็แย่ง job ที่ "จอง" ให้ agent อื่นได้ ส่วน AC เดิม "กระจายงานตาม capability/target" จึงยังไม่ตรงกับพฤติกรรมจริงทั้งหมด แนะนำเปิดเป็นงานปรับปรุงแยกถ้าต้องการ routing จริง (2026-08-27) |
| AUT-TEST-007 | Evidence security tests | DONE | Claude | `AutomationEvidenceSecurityTests.cs` 8 tests: type/size reject (step และ execution evidence upload), path-traversal ผ่าน `evidenceType` ที่ฝัง `..` ถูกบล็อกไม่ให้หลุด evidence root, upload สำหรับ step/execution ที่ไม่มีจริงคืน NotFound และลบไฟล์ orphan ที่เขียนไปก่อนหน้า, upload สำเร็จเก็บไฟล์เฉพาะโฟลเดอร์ execution ตัวเอง, `GetEvidencePathAsync` ถูก scope ตาม projectId (project อื่นเห็นไม่ได้แม้รู้ executionId/stepResultId); ทดสอบโดย instantiate `AutomationAgentController` ตรงๆ (ไม่ต้องใช้ WebApplicationFactory) เพราะ method ไม่แตะ HttpContext; **หมายเหตุขอบเขต:** `[Authorize(Policy="AutomationEvidence")]` และ `[RequireProjectAccess]` filter บน `AutomationController` เป็น ASP.NET pipeline-level ต้องมี hosted server ถึงทดสอบได้จริง — ส่วนนี้ยกให้ AUT-TEST-008 (Permission tests) แทนที่จะ fake ผ่านไป (2026-08-27) |
| AUT-TEST-008 | Automation permission tests | DONE | Claude | `AutomationAuthorizationPolicyTests.cs` 25 tests ครอบคลุมทั้ง 8 policy (View/Edit/Validate/Approve/Execute/Manage/Evidence/GenerateAI): ผ่านเมื่อมี claim ที่ตรงเป๊ะ, fail เมื่อไม่มี claim เลย, fail เมื่อมี Automation permission อื่นที่ไม่ตรง (กัน cross-privilege escalation), AutomationExecute ผ่านได้ 3 ทาง (AUTOMATION.EXECUTE claim/EXECUTION.RUN claim/SYS_ADMIN role) และ fail เมื่อไม่มีทั้งสาม; รีแฟกเตอร์ `Program.cs` แยก policy registration ออกเป็น `AutomationAuthorizationPolicies.AddAutomationPolicies()` extension (`src/ProMaxx2.QA.Api/Services/`) ให้เทสต์เรียกใช้ policy set เดียวกันกับที่ API ใช้จริงผ่าน `AddAuthorizationCore` บน `ServiceCollection` เปล่าๆ (ไม่ต้องพึ่ง WebApplicationFactory/Mvc.Testing); **ขอบเขตที่ไม่ครอบคลุม (ตั้งใจ):** ไม่ได้ทดสอบว่า endpoint ไหนผูกกับ policy ไหนจริง (controller wiring) และ `[RequireProjectAccess]` filter — ทั้งสองต้องมี hosted pipeline จริง (2026-08-27) |
| AUT-TEST-009 | Agent ActionExecutor tests | DONE | Claude | สร้าง test project ใหม่ `agent/ProMaxx2.Automation.Core.Tests` (เพิ่มเข้า `ProMaxx2.Automation.slnx`) พร้อม `FakeUiAutomationDriver` (fake `IUiAutomationDriver`, ไม่พึ่ง FlaUI/Windows UI Automation จริง); `ActionExecutorTests.cs` 18 tests ครอบคลุม UI action (CLICK/LOGIN) สำเร็จ, unresolved object → AUT-UI-001, driver คืน false (จำลอง timeout แบบ "condition not met") → AUT-UI-003, driver throw `TimeoutException` → fallback AUT-UI-003, cancellation ก่อน dispatch → "Execution cancelled." + screenshot evidence, unsupported action → fallback, assertion actions (EXPECT_TEXT/EXPECT_VISIBLE/EXPECT_NOT_VISIBLE), DB assertion ไม่มี DB config → AUT-DB-001, evidence capture เฉพาะตอน fail; **แก้ bug จริงที่พบระหว่างเขียนเทสต์:** `ActionExecutor.ExecuteAsync` catch(Exception) เดิม hardcode `ErrorCode="AUT-UI-003"` เสมอ ทั้งที่ exception message ฝัง error code เฉพาะไว้แล้ว (เช่น "...not found in Object Repository (AUT-UI-001).", "...not configured (AUT-DB-001).") — เพิ่ม `ExtractErrorCode` ดึงโค้ดจาก pattern `(AUT-XXX-NNN)` ท้ายข้อความ ใช้แทน hardcode (fallback AUT-UI-003 เมื่อไม่มีโค้ดฝังอยู่); **หมายเหตุสำคัญ — พบ gap ใหญ่กว่าที่ยังไม่แก้:** `agent/ProMaxx2.Automation.Runner/Program.cs:285` เมื่อ execution fail จะ hardcode execution-level ErrorCode เป็น `"AUT-UI-003"`/FailureType `"AssertionFailure"` เสมอ **โดยไม่สนใจ ErrorCode จริงของ step ที่ fail** (แม้ ActionExecutor จะรายงานโค้ดที่ถูกต้องระดับ step แล้วก็ตาม) ผลคือ server-side `AutomationFailureClassifier` (ที่ทดสอบไว้ใน AUT-TEST-005) แทบไม่มีทางเห็นโค้ด AUT-DB-*/AUT-APP-*/AUT-AGENT-* จาก execution จริงเลย ทำให้ recommendation "Retry"/"RetryOrCheckEnvironment" แทบไม่ถูกใช้งานจริงในสนาม (เข้าถึงได้แค่ผ่าน fatal-exception path กับ DSL-empty case เท่านั้น) — ยังไม่แก้เพราะเป็นการเปลี่ยนพฤติกรรม retry ในสนามจริงที่ควรให้ทีมตัดสินใจก่อน แนะนำเปิดเป็นงานแยก (2026-08-27) |
| AUT-TEST-010 | Database Validator tests | DONE | Claude | เพิ่ม `DbAssertionComparer` (`agent/ProMaxx2.Automation.Core/DbAssertion.cs`) แยก Compare operator logic (`>=`/`<=`/`!=`/`>`/`<`/`=`/numeric/bool/string) และ parameter-name normalization ออกจาก `FirebirdDbValidator` (เดิมเป็น private method เข้าถึงไม่ได้จนกว่าจะต่อ DB จริงสำเร็จ) มาเป็น shared static class ที่ทั้ง Firebird และ SQL Server validator เรียกใช้ร่วมกัน — **แก้ inconsistency จริงที่พบ: `SqlServerDbValidator` เดิมไม่รองรับ operator เลย (ใช้ case-insensitive string equals ตรงๆ) ต่างจาก Firebird ที่รองรับครบ** ตอนนี้ทั้งสองใช้ logic เดียวกัน; `DatabaseValidatorTests.cs` 45 tests ครอบคลุม Compare ทุก operator + numeric/bool/string fallback, parameter name normalization, `DbValidatorFactory.Create` คืน type ถูกต้องตาม Firebird/SqlServer, และ Firebird validator ต่อ connection ที่ถูกปฏิเสธ (127.0.0.1 port ปิด) จริงคืน failure result ที่ถูกต้อง (เร็ว ไม่ต้องมี DB จริง); **ขอบเขตที่ไม่ครอบคลุม (ตั้งใจ):** ไม่มี live-connection test สำหรับ SQL Server เพราะ `Microsoft.Data.SqlClient` ใช้เวลา ~14 วินาทีกว่าจะ fail connection ที่ถูกปฏิเสธ (validator ไม่ได้ set `ConnectTimeout` เลย ใช้ default 15s) ทำให้ suite ช้าลงมากสำหรับ assertion เดียว — เปิดเป็นงานใหม่ `AUT-P0-013`; parameter binding/query execution จริงกับ query failure ระดับ query (ไม่ใช่ connection) ต้องมี DB จริงถึงทดสอบได้ ยังไม่ครอบคลุม; **หมายเหตุสำคัญอีกจุด:** `ValidateAsync(request, ct)` รับ `CancellationToken` มาแต่ไม่เคยใช้เลย (`con.Open()`/`cmd.ExecuteReader()` เป็น sync call ไม่ใช่ `OpenAsync(ct)`/`ExecuteReaderAsync(ct)`) แปลว่า DB assertion step ที่ query ค้างจะ cancel ไม่ได้จริง — รวมอยู่ใน `AUT-P0-013` เช่นกัน (2026-08-27) |

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

### 2026-08-27 — AUT-TEST-010: Database Validator tests (สุดท้ายใน P0 Automated Tests — ปิดครบ 001-010)

- ปิด `AUT-TEST-010` — ปิดครบทุก item ใน section 4 (P0 Automated Tests)
- เพิ่ม `DbAssertionComparer` (`agent/ProMaxx2.Automation.Core/DbAssertion.cs`): แยก Compare operator logic และ parameter-name normalization ออกจาก `FirebirdDbValidator` เดิม (private method ที่เข้าถึงไม่ได้จนกว่าจะต่อ DB สำเร็จ จึงเทสต์ไม่ได้เลยถ้าไม่แยกออกมา) เป็น shared static class
- **แก้ inconsistency จริงที่พบระหว่างแยกโค้ด:** `SqlServerDbValidator` เดิมไม่รองรับ operator (`>=`/`<=`/`!=`/`>`/`<`) เลย ใช้ case-insensitive string equals ตรงๆ ต่างจาก Firebird ที่รองรับครบมาตั้งแต่แรก — ตอนนี้ทั้งสอง validator ใช้ `DbAssertionComparer` ร่วมกันแล้ว behaviour ตรงกัน
- เพิ่ม `DatabaseValidatorTests.cs` 45 tests: Compare ทุก operator + numeric/bool/string fallback, parameter normalization, `DbValidatorFactory.Create` คืน type ถูกต้อง, Firebird validator ต่อ connection ที่ถูกปฏิเสธจริง (127.0.0.1 port ปิด — เร็ว ไม่ต้องมี DB จริง) คืน failure result ถูกต้อง
- **ตัดสินใจไม่ใส่ live-connection test สำหรับ SQL Server:** ลองแล้วพบว่า `Microsoft.Data.SqlClient` ใช้เวลา ~14 วินาทีกว่าจะ fail connection ที่ถูกปฏิเสธ (เทียบกับ Firebird ที่ fail เกือบทันที) เพราะ validator ไม่ได้ set `ConnectTimeout` เลย — ตัดออกจาก suite เพื่อไม่ให้ทั้งชุดช้าลงสำหรับ assertion เดียว แล้วเปิดเป็นงานใหม่ `AUT-P0-013` แทน (รวม CancellationToken ที่ไม่เคยถูกใช้จริงในทั้งสอง validator ด้วย)
- Build: `dotnet build ProMaxx2.Automation.slnx` (agent) 0 warning/0 error; `dotnet test` agent **45/45 ผ่าน ใน 2 วินาที** (หลังตัด live SQL Server test ออก); QA Hub solution sanity-check ยังผ่าน 166/166 เหมือนเดิม

### 2026-08-27 — AUT-TEST-009: Agent ActionExecutor tests + error-code bug fix

- ปิด `AUT-TEST-009`
- สร้าง test project ใหม่ `agent/ProMaxx2.Automation.Core.Tests` (net10.0-windows, xunit) เพิ่มเข้า `ProMaxx2.Automation.slnx`; `FakeUiAutomationDriver` (fake `IUiAutomationDriver` ไม่พึ่ง FlaUI/Windows UI Automation จริง — รันได้ทุกเครื่อง ไม่ต้องมี AUT ติดตั้ง)
- เพิ่ม `ActionExecutorTests.cs` 18 tests: UI action สำเร็จ (CLICK/LOGIN), object ไม่เจอ → AUT-UI-001, driver คืน false (timeout แบบ "condition not met") → AUT-UI-003, driver throw TimeoutException → fallback AUT-UI-003, cancellation ก่อน dispatch, unsupported action, assertion actions (EXPECT_TEXT trimmed compare, EXPECT_VISIBLE/EXPECT_NOT_VISIBLE), DB assertion ไม่มี config → AUT-DB-001, evidence capture เฉพาะตอน fail
- **แก้ bug จริงที่พบระหว่างเขียนเทสต์:** `ActionExecutor.ExecuteAsync` catch(Exception) เดิม hardcode ErrorCode="AUT-UI-003" เสมอ ทั้งที่ exception message ฝัง error code เฉพาะไว้แล้ว (เช่น object-not-found ฝัง "(AUT-UI-001)", DB not configured ฝัง "(AUT-DB-001)") — เพิ่ม `ExtractErrorCode` ดึงโค้ดจาก pattern `(AUT-XXX-NNN)` ท้ายข้อความมาใช้แทน โดย fallback เป็น AUT-UI-003 เมื่อไม่มีโค้ดฝังอยู่ (เช่น raw TimeoutException, unsupported action)
- **เปิดงานใหม่ `AUT-P0-012`:** ระหว่างแก้ bug ข้างต้น พบ gap ที่ใหญ่กว่าและยังไม่แก้ — `agent/ProMaxx2.Automation.Runner/Program.cs:285` hardcode execution-level ErrorCode/FailureType เสมอโดยไม่สนใจ step ที่ fail จริง ทำให้ retry/classification บนฝั่ง server แทบไม่ทำงานกับ execution จริงในสนาม (รายละเอียดในตาราง P0) — ไม่แก้ในรอบนี้เพราะกระทบพฤติกรรม retry จริง ต้องให้ทีมตัดสินใจก่อน
- Build: `dotnet build ProMaxx2.Automation.slnx` (agent solution) 0 warning/0 error; `dotnet test` agent 18/18 ผ่าน; sanity-check QA Hub solution ยังผ่าน 166/166 เหมือนเดิม (ไม่ถูกกระทบ เพราะคนละ solution)

### 2026-08-27 — AUT-TEST-008: Automation permission tests

- ปิด `AUT-TEST-008`
- รีแฟกเตอร์ `Program.cs`: ย้าย 8 บรรทัด `.AddPolicy("Automation...")` ออกเป็น `AutomationAuthorizationPolicies.AddAutomationPolicies()` extension method ใหม่ใน `src/ProMaxx2.QA.Api/Services/AutomationAuthorizationPolicies.cs` — เป็น single source of truth ที่ทั้ง Program.cs และเทสต์เรียกใช้ร่วมกัน ป้องกันเทสต์ drift จากของจริง
- เพิ่ม `AutomationAuthorizationPolicyTests.cs` 25 tests: build `IAuthorizationService` จริงบน `ServiceCollection` เปล่าๆ ผ่าน `AddAuthorizationCore` + policy set ข้างต้น (ไม่ต้องมี host) แล้วยิง `AuthorizeAsync` ตรงๆ — ครอบคลุมทั้ง 8 policy (View/Edit/Validate/Approve/Execute/Manage/Evidence/GenerateAI): ผ่านเมื่อมี claim ตรง, fail เมื่อไม่มี claim เลย, fail เมื่อมี Automation permission อื่นที่ไม่ตรง (กัน cross-privilege), AutomationExecute ผ่านได้ 3 ทาง (2 claim + SYS_ADMIN role) ตาม `RequireAssertion` เดิม
- **ขอบเขตที่ไม่ครอบคลุม (ตั้งใจ):** controller-level wiring (endpoint ไหนผูก policy ไหน) และ `[RequireProjectAccess]` filter ยังต้องมี hosted pipeline จริงถึงทดสอบได้ — ตรงกับ note ที่ทิ้งไว้ตอน AUT-TEST-007
- Build: `dotnet build ProMaxx2.QA.slnx` 0 warning/0 error; `dotnet test` ผ่านทั้งชุด 166/166 (เพิ่มจาก 141)

### 2026-08-27 — AUT-TEST-007: Evidence security tests

- ปิด `AUT-TEST-007`
- เพิ่ม `AutomationEvidenceSecurityTests.cs` 8 tests ครอบคลุม type/size validation (ทั้ง step evidence และ execution evidence upload), path-traversal guard ผ่าน `evidenceType` (free text ที่ถูกฝังลง path ตรงๆ โดยไม่ sanitize — ทดสอบว่า guard `full.StartsWith(root)` บล็อกได้จริงก่อนเขียนไฟล์), NotFound + orphan-file cleanup เมื่อ step/execution ไม่มีจริง, upload สำเร็จเขียนเฉพาะโฟลเดอร์ execution ตัวเอง, `GetEvidencePathAsync` ถูก scope ตาม projectId
- เทคนิค: instantiate `AutomationAgentController` ตรงๆ ด้วย fake `IWebHostEnvironment` (ไม่ต้องพึ่ง `WebApplicationFactory`/`Mvc.Testing` ที่ถอดออกไปแล้วตอน AUT-TEST-001) เพราะ method เหล่านี้ไม่แตะ HttpContext
- **ขอบเขตที่ไม่ครอบคลุม (ตั้งใจ):** `[Authorize(Policy="AutomationEvidence")]` และ `[RequireProjectAccess]` filter บน `AutomationController` ต้องมี ASP.NET pipeline จริงถึงทดสอบได้ (WebApplicationFactory) — ยกให้ AUT-TEST-008 (Permission tests) ทำแยกแทนที่จะ fake ผ่านไปในรอบนี้
- Build: `dotnet build ProMaxx2.QA.slnx` 0 warning/0 error; `dotnet test` ผ่านทั้งชุด 141/141 (เพิ่มจาก 133)

### 2026-08-27 — AUT-TEST-006: Batch Run/Multi-Agent tests

- ปิด `AUT-TEST-006`
- เพิ่ม `AutomationTestFixtures.SeedReadyCasesAsync` helper (สร้างหลาย Ready case ใน baseline เดียวกัน สำหรับเทสต์ batch)
- เพิ่ม `AutomationBatchRunTests.cs` 7 tests: batch create/skip, dedup, empty request throw, 2-agent concurrent drain ไม่ชนกัน+ผลรวมครบ, target routing (ทั้งกรณี non-default poller ถูก filter และกรณี default WindowsUI poller ไม่ถูก filter เลย), requested-agent ไม่ถูกบังคับ
- **ข้อสังเกตสำคัญ (พบระหว่างเขียนเทสต์ ต่างจากที่คาดไว้ตอนแรก):** target-based routing ใน `ClaimNextJobAsync` ทำงานแค่ทางเดียว — เงื่อนไข `if (target != "WindowsUI")` แปลว่า poller ที่ใช้ targetApp default "WindowsUI" **ไม่มี filter เลย** สามารถเคลม job ของ AutomationType ไหนก็ได้ (เช่น "iOSApp"); มีแค่ poller ที่ใช้ target อื่นที่ไม่ใช่ WindowsUI เท่านั้นที่ถูกจำกัดให้ตรง target ของตัวเองหรือ WindowsUI fallback — พฤติกรรมนี้ตรงข้ามกับสมมติฐานแรกที่คิดว่า WindowsUI poller จะถูกจำกัดด้วย จึงต้องแก้เทสต์ที่เขียนผิดออกจากการรันจริง
- ยืนยันอีกครั้งว่าไม่มี capability-based routing จริง (`capabilities` รับมาแต่ไม่ใช้กรอง) และ `RequestedAgentId` จาก batch ไม่ถูกบังคับตอน claim — ทั้งสองจุดนี้ทำให้ AC เดิม "กระจายงานตาม capability/target" ยังไม่ตรงกับพฤติกรรมจริงทั้งหมด ควรเปิดเป็นงานปรับปรุงแยกถ้าต้องการ routing ที่บังคับจริง
- Build: `dotnet build ProMaxx2.QA.slnx` 0 warning/0 error; `dotnet test` ผ่านทั้งชุด 133/133 (เพิ่มจาก 126)

### 2026-08-27 — AUT-TEST-005: Retry tests

- ปิด `AUT-TEST-005`
- เพิ่ม `AutomationRetryTests.cs` 7 tests ครอบคลุม retry เดียวต่อความล้มเหลว 1 ครั้งพร้อม backoff, chain retry จนถึง MaxAttempts แล้วหยุด (ไม่มีรอบถัดไป), non-retryable UI failure เข้า MaintenanceRequired ไม่ retry, error code ที่ไม่รู้จักไม่ retry (case กลับ Ready), retry policy Disabled ปิด retry ทั้งหมด, unsafe action ที่ execute ผ่านไปแล้วบล็อก retry, duplicate completion report ไม่สร้าง retry ซ้ำ (อาศัย idempotency guard ที่เพิ่มตอนทำ AUT-TEST-004)
- Build: `dotnet build ProMaxx2.QA.slnx` 0 warning/0 error; `dotnet test` ผ่านทั้งชุด 126/126 (เพิ่มจาก 119)

### 2026-08-27 — AUT-TEST-004: Cancel/Timeout/Lease Recovery tests + bug fix

- ปิด `AUT-TEST-004`
- เพิ่ม `AutomationCancelTimeoutTests.cs` 9 tests: cancel Queued/Running execution, cancel ซ้ำ execution ที่ terminal แล้วต้อง throw, Timeout/AgentLost result classify ถูก, late result หลัง cancel ไม่ทับสถานะ, duplicate result report ไม่ประมวลผลซ้ำ (ไม่สร้าง retry execution ซ้ำซ้อน), domain guard บน `AutomationJob.Complete`/`AutomationExecution.Complete`
- **พบและแก้ bug จริงระหว่างเขียนเทสต์:** `AutomationExecution.Complete` และ `AutomationJob.Complete` เดิมไม่มี guard เช็คสถานะปัจจุบันก่อน overwrite — ผลคือถ้า agent ส่งผลลัพธ์มาช้า (late result) หลังจาก execution ถูก user cancel ไปแล้ว หรือส่งผลซ้ำ (duplicate report) ระบบจะ**เขียนทับสถานะ Cancelled/Failed เดิมแบบเงียบๆ** และอาจสร้าง retry execution ซ้ำซ้อนได้ — แก้โดยเพิ่ม guard `if (Status is not ("Queued" or "Running")) throw InvalidOperationException` ในทั้งสอง entity (defense-in-depth) และเพิ่มเช็คใน `CompleteExecutionAsync` ก่อนเรียก `execution.Complete` ให้ return สถานะปัจจุบันแบบ idempotent เมื่อ execution terminal ไปแล้ว แทนที่จะ error หรือ overwrite
- **ข้อสังเกตสำคัญ:** ยังไม่มี background lease-expiry watchdog ในโค้ด (ไม่มี `IHostedService`/`BackgroundService` ตรวจ Running job ที่ agent heartbeat หมดอายุแล้ว mark เป็น AgentLost อัตโนมัติ) — ส่วน "expired lease" ตาม AC เดิมยังไม่ implement จริง เป็น gap ที่ควรเปิดเป็นงานใหม่แยกต่างหาก (เกี่ยวโยงกับ AUT-TEST-003 ที่ก็ไม่มี lease token เช่นกัน)
- Build: ต้อง `dotnet build-server shutdown` และหยุด `ProMaxx2.QA.Api.exe` dev instance ที่ค้างรันอยู่ก่อน เพราะ lock DLL ทำให้ build ไม่ผ่าน (ได้รับอนุมัติจากผู้ใช้ก่อนหยุด process); หลังจากนั้น `dotnet build ProMaxx2.QA.slnx` 0 warning/0 error; `dotnet test` ผ่านทั้งชุด 119/119 (เพิ่มจาก 110)

### 2026-08-27 — AUT-TEST-003: Atomic Job Claim tests

- ปิด `AUT-TEST-003`
- เพิ่ม `AutomationTestFixtures.SeedReadyCaseAsync` helper (Draft→Validate→Approve จนได้ Ready case + approved version พร้อม request execution/job ต่อ)
- เพิ่ม `AutomationJobClaimTests.cs` 7 tests ครอบคลุม double-claim prevention, assign ถูก agent, agent ปิดใช้งาน claim ไม่ได้, ไม่มี job ว่าง, backoff job ในอนาคต claim ไม่ได้ยัง, priority ordering, domain guard `AutomationJob.Assign`
- **ข้อสังเกตสำคัญ:** acceptance criteria เดิมพูดถึง "lease token" แต่โค้ดจริงไม่มี field นี้ — ผู้ที่เขียน spec เดิมอาจตั้งใจให้มี lease token/expiry สำหรับ recovery (เกี่ยวข้องกับ AUT-TEST-004 Cancel/Timeout/Lease Recovery) แต่ปัจจุบันยังไม่ implement; เทสต์ที่เพิ่มยืนยัน invariant ที่มีอยู่จริง (sequential double-claim + priority + backoff) ด้วย EF InMemory ซึ่งไม่รองรับ transaction ดังนั้นยังไม่ครอบคลุม concurrent race จริงบน production DB
- Build: `dotnet build ProMaxx2.QA.slnx` 0 warning/0 error; `dotnet test` ผ่านทั้งชุด 110/110 (เพิ่มจาก 103)

### 2026-08-27 — AUT-TEST-001/002: DSL Validator + Case Workflow tests

- ปิด `AUT-TEST-001`, `AUT-TEST-002`
- เพิ่ม `AutomationTestFixtures.cs` shared seed helper (InMemory `QaDbContext`, seed Project/Module/Release/Build/Environment/TestCase, `CaseService`/`AgentService` factory, sample DSL) ใช้ร่วมกันในเทสต์ Automation module ต่อจากนี้
- เพิ่ม `AutomationDslValidatorTests.cs` 17 tests: required fields, unsupported DslVersion, step number rules (positive/unique/contiguous), unknown Action/Object/TestData reference ทั้งกรณี library ว่าง (bypass) และไม่ว่าง (reject)
- เพิ่ม `AutomationCaseWorkflowTests.cs` 4 tests: reject สร้าง Case จาก TestCase ที่ไม่ใช่ automation candidate, full flow Draft→Validate→Approve→Ready→MaintenanceRequired→AssignOwner→Resolve→NeedsReview, invalid DSL ทำให้ version Invalid และ case กลับ NeedsReview, resolve maintenance ที่ไม่ได้อยู่สถานะ MaintenanceRequired ต้อง throw
- **พบและแก้ build blocker:** `ProMaxx2.QA.UnitTests.csproj` ถูกเพิ่ม `Microsoft.EntityFrameworkCore.Sqlite` และ `Microsoft.AspNetCore.Mvc.Testing` (ยังไม่มีโค้ดใช้งานจริง) ซึ่งดึง `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 ที่มี known vulnerability (NU1903) เข้ามา แล้วโดน root `Directory.Build.props` (`TreatWarningsAsErrors=true`) เปลี่ยนเป็น error ทำให้ restore/build ทั้ง test project ล้มเหลว — เอา 2 package ที่ยังไม่ได้ใช้ออกจนกว่าจะมีเทสต์ต้องใช้จริง (เช่น integration test ด้วย `WebApplicationFactory`)
- Build: `dotnet build ProMaxx2.QA.slnx` 0 warning/0 error; `dotnet test` ผ่านทั้งชุด 103/103 (เพิ่มจาก 79 เทสต์เดิม)

### 2026-08-26 — P0 Runtime: Verification, Maintenance, Failure Analysis, Retry, Quarantine, Failure Dashboard

- ปิด `AUT-P0-006` ถึง `AUT-P0-011`
- Migration ใหม่ `AddAutomationReliabilityAndVerification`: เพิ่มตาราง `AutomationObjectVerifications`, `AutomationRetryPolicySettings` (seed แถวเดียว MaxAttempts=2/BackoffSeconds=30/Enabled=true) และคอลัมน์ใหม่บน `AutomationExecutions` (ClassifiedFailureType/ClassifiedRecommendation/RetryOfExecutionId/RetryCount), `AutomationCases` (Maintenance*/Quarantine*), `AutomationActions` (RetrySafety) — apply สำเร็จบน dev DB จริง
- Domain: `AutomationCase.RequireMaintenance/AssignMaintenanceOwner/ResolveMaintenance/Quarantine/Unquarantine`, `AutomationExecution.SetClassification/MarkAsRetry`, `AutomationAction.RetrySafety`, entity ใหม่ `AutomationObjectVerification`, `AutomationRetryPolicySettings`
- `CompleteExecutionAsync` เรียก `AutomationFailureClassifier` ทุกครั้งที่ Fail/Timeout/AgentLost แล้ว persist ผล, ตัดสินใจ auto-retry ตาม Retry Policy + Unsafe Action check, สร้าง execution/job ใหม่พร้อม backoff ผ่าน `QueuedAt` ในอนาคต (ไม่ต้องมี background scheduler)
- แก้ bug เดิมใน `AutomationFailureClassifier`: guard เช็คเฉพาะ `Status == "Failed"` ทำให้ Timeout/AgentLost ไม่เคยถูกจำแนก (จึง retry ไม่เคยทำงานกับสถานะเหล่านี้) — ขยายเป็น `Failed`/`Timeout`/`AgentLost`
- API ใหม่: `objects/verify`, `objects/verifications`, `cases/{id}/maintenance/owner`, `cases/{id}/maintenance/resolve`, `cases/flaky-candidates`, `cases/{id}/quarantine`, `cases/{id}/unquarantine`, `failures/dashboard`, `failures/executions`, `settings/retry-policy` (GET/PUT), agent-facing `verifications/claim`, `verifications/result`
- Agent (`agent/ProMaxx2.Automation.*`): เพิ่ม `ObjectVerifier.cs`, `QaHubClient` เมธอด claim/report verification, คำสั่งใหม่ `runner verify --exe <path>`; build ผ่าน `ProMaxx2.Automation.slnx` ทั้งชุด — **ยังไม่ได้ทดสอบกับ ProMaxx2.exe จริง เพราะสภาพแวดล้อมนี้ไม่มี AUT/Windows session ให้รัน UI Automation — ต้องให้ QA ทดสอบภาคสนามก่อนใช้งานจริง**
- Frontend: Object Repository เพิ่มเลือกหลาย Object + ปุ่มตรวจสอบ + modal ผลตรวจสอบ; Case Detail เพิ่ม Maintenance Repair panel และ Quarantine panel; Cases tab เพิ่ม Flaky Candidates panel + Quarantine modal; Execution Detail แสดง classification ที่ persist แล้ว + retry badge; Jobs แสดง Retry Count; เพิ่ม tab ใหม่ "Failure Dashboard"; Action Library เพิ่ม Retry Safety; เพิ่มหน้าตั้งค่า Retry Policy ใน "การจัดการ"
- Tests: เพิ่ม 9 unit tests ใน `AutomationManagementTests.cs` (Maintenance/Quarantine/Classification/Retry/Verification/RetryPolicySettings) รวมทั้งไฟล์ 13 tests, test suite รวมผ่าน 79 tests
- Build: `dotnet build` (QA Hub solution + Agent solution แยกกัน), `npm.cmd run build`, `npm.cmd run lint`, boot-check API (swagger 200, ไม่มี DI error) ผ่านทั้งหมด

### 2026-08-26 — P0 Action/Object Management

- ปิด `AUT-P0-001` ถึง `AUT-P0-004`
- Action Library รองรับแก้ไข runtime metadata, Parameter Schema, Handler Key และเปิด/ปิด
- Object Repository รองรับแก้ Business Key/selector, เพิ่ม ObjectVersion, ป้องกัน Business Key ซ้ำ และเปิด/ปิด
- เพิ่ม server-side JSON validation และ error response สำหรับ update endpoints
- เพิ่ม `AutomationManagementTests.cs` จำนวน 4 tests; test suite รวมผ่าน 70 tests
- Frontend `npm.cmd run build` และ `npm.cmd run lint` ผ่าน

### 2026-08-26 — P0 Object Import

- ปิด `AUT-P0-005`
- Object Repository เพิ่ม Import Scanner modal รองรับ JSON/CSV จาก UI Inspector/AutomationId Scanner
- Preview แสดงสถานะ Ready/DuplicateKey/DuplicateAutomationId/Invalid และเลือก import เฉพาะรายการที่ต้องการได้
- Backend เพิ่ม `POST /api/v1/automation/objects/import` พร้อม validation และป้องกัน Business Key/AutomationId ซ้ำ
- Frontend `npm.cmd run build` และ backend `dotnet build` ผ่าน

### 2026-08-26 — สร้าง Tracker

- สร้าง Automation Development To-Do เป็นแหล่งติดตามงานคงค้างกลาง
- บันทึก baseline จากฐานข้อมูล: 11 Cases, 35 Executions, 23 Failed, 5 MaintenanceRequired, 5 Objects
- จัดลำดับงานเป็น P0 Runtime/Tests, P1 Suite/Schedule/Data/Regression และ P2 Monitoring/UX
- เพิ่มกฎใน `AGENTS.md` ให้ทุกงาน Automation ต้องอ่านและอัปเดตไฟล์นี้
