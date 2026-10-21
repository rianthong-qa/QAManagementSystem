# Automation Field Test Checklist (รอบที่ 1–7)

> สร้างเมื่อ: 24 กันยายน 2026
>
> ใช้คู่กับ `AUTOMATION_TODO.md` — ทดสอบครบตาม checklist นี้หนึ่งรอบ แล้วนำผลไปปิดรายการ IN PROGRESS ในตาราง P0
>
> ผู้ทดสอบ: QA ที่มีสิทธิ์ Automation และเครื่อง Windows ที่ติดตั้ง ProMaxx2 (POS และ/หรือ App)

## 0. รายการที่ checklist นี้ปิดได้

| รายการ | เรื่อง | ส่วนที่ต้องผ่าน |
|---|---|---|
| AUT-AGT-004 | secret ของ Agent, ลบ `trylogin`, https | A |
| AUT-AGT-001 | Agent login ใหม่เอง / retry / heartbeat ระหว่างรัน | B |
| AUT-AGT-003 | เปิดปิด ProMaxx2, รับงานทีละตัว | C |
| AUT-AGT-002 | ความถูกต้องของ UI actions | D |
| AUT-SEC-004 | DB assertion อ่านอย่างเดียว | E |
| AUT-REL-001 | งานชนกัน (rowversion, idempotency) | F1–F4 |
| AUT-REL-002 | Reaper ปิดงานค้าง | F5–F7 |
| AUT-UI-001 | บั๊กหน้า Automation (Defect, Token, Seed, error state, debounce) | G1–G7 |
| AUT-UI-002 | Modal มาตรฐาน + dropdown Build/Environment + Mobile | G8–G12 |
| AUT-CAP-001 ถึง 005 | Capture Companion | H |

**ไม่อยู่ใน checklist นี้ (ปิดด้วยการทดสอบภาคสนามไม่ได้):**

- AUT-REL-003 — เหลือ unit/integration test กรณี DB write ล้มระหว่าง fire schedule (งาน dev)
- AUT-CAP-006 และ integration test ของ AUT-CAP-001/002/004 — งาน dev (checklist นี้ครอบคลุมเฉพาะส่วนที่ต้องทดสอบกับของจริง)
- AA1–AA6 (Weighted Auto Assignment) — ยังขาด UI ต้องพัฒนาก่อน

## 1. เตรียมก่อนทดสอบ

### 1.1 เครื่องและโปรแกรม

- [ ] เครื่อง Windows ที่ login แบบ interactive (เห็น desktop) — ห้ามล็อกหน้าจอระหว่างทดสอบ Agent
- [ ] ติดตั้ง ProMaxx2 POS (และ App ถ้าจะทดสอบ C3) พร้อมบัญชีทดสอบที่ login ได้
- [ ] Firebird มี `gbak` บน PATH (สำหรับ I2) หรือรู้ path ของ `gbak.exe`
- [ ] .NET 10 SDK และ source ล่าสุดของ branch `feat/automation-suite-versioning` (commit `6230675` หรือใหม่กว่า)
- [ ] Build: `dotnet build agent\ProMaxx2.Automation.slnx` ผ่าน

### 1.2 ข้อมูลใน QA Hub

- [ ] บัญชี Agent (ไม่ใช่ admin) ที่มีสิทธิ์ `AUTOMATION.EXECUTE`
- [ ] บัญชี QA 2 คน: คนที่ **มี** `DEFECT.EDIT` และคนที่ **ไม่มี** (สำหรับ G2)
- [ ] Project + Release + Build + Environment ที่ใช้ทดสอบ (เลือก Project/Release ที่ Topbar ก่อนเสมอ)
- [ ] Automation Case ที่ Ready อย่างน้อย 3 ตัว:
  - **Case-OK** — DSL ที่ควรผ่านทั้งหมด (ใช้ใน B, C, F)
  - **Case-ACTIONS** — DSL ตาม D (ปุ่มพิเศษ, enabled/disabled, popup)
  - **Case-DB** — DSL ที่มี `EXPECT_DB_VALUE` / `EXPECT_DB_ROW_COUNT` (E)
- [ ] Object ใน Object Repository ครบตามที่ DSL อ้างถึง และ Validate/อนุมัติ version แล้ว
- [ ] Retry Policy เปิดอยู่ (หน้า Automation → จัดการ → Retry Policy) เพื่อดู auto-retry ใน F5

### 1.3 เครื่องมือตรวจ DB (อ่านอย่างเดียว)

รันบนเครื่อง QA Hub ด้วย Windows auth — **ใช้เฉพาะ SELECT**:

```powershell
sqlcmd -S "localhost\MSSQLSERVER2022" -E -d ProMaxx2QA -W -Q "<SQL>"
```

SQL ที่ใช้บ่อย:

```sql
-- execution ล่าสุด 10 รายการ
SELECT TOP 10 AutomationExecutionId, Status, ErrorCode, RetryCount, RetryOfExecutionId, StartedAt, CompletedAt
FROM AutomationExecutions ORDER BY CreatedAt DESC;

-- step ซ้ำ (ต้องได้ 0 แถวเสมอ)
SELECT AutomationExecutionId, StepNo, COUNT(*) c FROM AutomationStepResults
GROUP BY AutomationExecutionId, StepNo HAVING COUNT(*) > 1;

-- สถานะ Agent
SELECT AgentCode, Status, LastHeartbeatAt, CurrentExecutionId FROM AutomationAgents WHERE IsDeleted = 0;
```

### 1.4 บันทึกผล

ทุกข้อให้บันทึก: ผล (ผ่าน/ไม่ผ่าน), วันที่, ผู้ทดสอบ, หลักฐาน (Execution ID, screenshot, บรรทัด log ของ Agent) — ใช้ตารางใน §10

---

## A. ติดตั้งและรหัสผ่านของ Agent — AUT-AGT-004

- [ ] **A1 ตั้งค่าแบบเข้ารหัส:** รัน `agent\set-agent-env.ps1` ด้วย Windows user ที่จะรัน Agent
  - คาดหวัง: ช่องรหัสผ่านไม่แสดงตัวอักษรขณะพิมพ์; ค่า default ของ Hub URL เป็น `https://api-promaxx2.qahub.store/api/v1`
  - ตรวจ: `[Environment]::GetEnvironmentVariable('QAHUB_PASSWORD_DPAPI','User')` เป็นข้อความ hex ยาว และ `QAHUB_PASSWORD` ว่าง
- [ ] **A2 รันจาก console:** เปิด terminal ใหม่ รัน `agent\run-agent.ps1`
  - คาดหวัง: `[agent] Logged in to https://...` และ `Registered agent '<code>'`
  - ตรวจ: ไฟล์ `run-agent.ps1` ไม่มีรหัสผ่านอยู่ในไฟล์
- [ ] **A3 ถอดรหัสด้วย user อื่นไม่ได้:** รัน `run-agent.ps1` ด้วย Windows user อื่น (หรือ copy ค่า `*_DPAPI` ไปเครื่องอื่น)
  - คาดหวัง: Runner หยุดพร้อมข้อความ "ถอดรหัส QAHUB_PASSWORD_DPAPI ไม่ได้ … ให้รัน set-agent-env.ps1 ใหม่" (exit code 2) — ไม่ใช่ login fail แบบงง ๆ
- [ ] **A4 ปฏิเสธ http ไปเครื่องอื่น:** ตั้ง `QAHUB_BASE_URL` เป็น `http://<IP เครื่อง Hub>:5038/api/v1` แล้วรัน
  - คาดหวัง: Runner หยุดพร้อมข้อความให้ใช้ https; GUI กดบันทึก/ทดสอบเชื่อมต่อ/เริ่ม Agent แล้วขึ้นกล่องเตือนและไม่ทำต่อ
  - ตั้ง `QAHUB_ALLOW_INSECURE_HTTP=true` แล้วรันใหม่ → เชื่อมต่อได้ (คืนค่า https หลังทดสอบ)
- [ ] **A5 GUI เก็บรหัสผ่านแบบเข้ารหัส:** เปิด AgentGui → กรอกตั้งค่า → บันทึก → เปิด `agent-config.json` ข้าง exe
  - คาดหวัง: ช่อง Password/AutPassword/DbPassword เป็นข้อความเข้ารหัส (base64) ไม่ใช่รหัสจริง
- [ ] **A6 GUI ถอดรหัสไม่ได้:** copy `agent-config.json` ไปเปิดด้วย Windows user อื่น
  - คาดหวัง: หลังเปิดฟอร์มขึ้นกล่องเตือน "ถอดรหัส … ไม่ได้ — กรอกรหัสผ่านใหม่" และช่องรหัสผ่านว่าง (ไม่ใช่ข้อความเข้ารหัส)
- [ ] **A7 ไม่มีคำสั่งเดารหัส:** `ProMaxx2.Automation.Runner.exe trylogin --exe x`
  - คาดหวัง: ไม่มีการเปิด ProMaxx2 และไม่มีการลองรหัสผ่าน (ไม่มีคำสั่งนี้แล้ว — Runner เข้าสู่โหมดปกติ/แจ้งให้ตั้งค่า)
- [ ] **A8 `inspect` ไม่ต้องใส่รหัสบน command line:** `Runner.exe inspect --exe <PromaxxsPos.exe> --emp <user> --out uia.json`
  - คาดหวัง: login ProMaxx2 สำเร็จด้วย `AUT_PASSWORD_DPAPI`; ถ้าใส่ `--pwd` จะมีคำเตือน

## B. ความทนทานของการเชื่อมต่อ Hub — AUT-AGT-001

- [ ] **B1 Hub ยังไม่พร้อมตอนเริ่ม:** หยุด API (Service Manager → Stop API) แล้วเริ่ม Agent
  - คาดหวัง: Agent ไม่ปิดตัว แสดง "QA Hub ยังติดต่อไม่ได้ — ลองใหม่ใน 5/10/20… วินาที" (สูงสุด 60); เปิด API แล้ว Agent login ได้เอง
- [ ] **B2 Hub restart ระหว่าง Agent ว่าง:** Agent รันอยู่ → Restart API
  - คาดหวัง: log มี error ชั่วคราวแต่ Agent ไม่ crash และกลับมารับงานได้เองหลัง API ขึ้น (ทดสอบด้วยการสั่งรัน Case-OK)
- [ ] **B3 รหัสผ่านผิดจริง:** ตั้งรหัส QA Hub ผิดแล้วเริ่ม Agent
  - คาดหวัง: หยุดทันทีพร้อมข้อความ "Login ไป QA Hub ล้มเหลว … ตรวจ Username/Password" (ไม่วนลองซ้ำ)
- [ ] **B4 heartbeat ระหว่างงานยาว:** ตั้ง `ACTION_TIMEOUT_SECONDS=90` และทำ Case ที่มี `WAIT_OBJECT` ของ object ที่ไม่มีจริง → สั่งรัน
  - คาดหวัง: ระหว่างรอ (~90 วินาที) หน้า Automation → Agents แสดง Agent เป็น Busy/Online **ไม่ใช่ Offline**; SQL `LastHeartbeatAt` ขยับทุก ~15 วินาที และ `CurrentExecutionId` = execution นั้น
- [ ] **B5 token หมดอายุ (ทดสอบยาว):** เปิด Agent ทิ้งไว้ **เกิน 24 ชั่วโมง** แล้วสั่งรัน Case-OK
  - คาดหวัง: Agent login ใหม่เองและรับงานได้ ไม่ต้อง restart Agent
  - (ทางลัด ถ้าผู้ดูแลอนุมัติ: rotate `Jwt__Key` แล้ว restart API — ผู้ใช้ทุกคนต้อง login ใหม่ ห้ามทำในเวลาทำงาน)

## C. การเปิดปิด ProMaxx2 และการรันพร้อมกัน — AUT-AGT-003

- [ ] **C1 ปิด ProMaxx2 หลังงานผ่าน:** สั่งรัน Case-OK
  - คาดหวัง: หลังจบงาน ProMaxx2 ปิดเอง (ไม่เหลือใน Task Manager)
- [ ] **C2 ปิด ProMaxx2 หลังงาน Fail:** สั่งรัน Case ที่ตั้งใจให้ fail (เช่น EXPECT_TEXT ค่าผิด)
  - คาดหวัง: execution = Failed และ ProMaxx2 ปิดเช่นกัน
- [ ] **C3 POS + App พร้อมกัน:** GUI → เริ่ม Agent (Pos + App) → สั่งรัน Batch ที่มีทั้ง Case POS และ App
  - คาดหวัง: log ของตัวหนึ่งแสดง "Runner อีกตัวบนเครื่องนี้กำลังใช้หน้าจอ — รอจนเสร็จก่อนรับงาน"; งานรันทีละตัว ไม่มีช่วงที่สองโปรแกรมถูกกดพร้อมกัน; ทั้งสองงานจบถูกต้อง
- [ ] **C4 มี ProMaxx2 ค้างอยู่ก่อน:** เปิด ProMaxx2 เองค้างไว้ → สั่งรัน Case-OK
  - คาดหวัง: Agent ปิดตัวที่ค้าง (log ของ execution มี "ปิด … ที่ค้างอยู่ 1/1 instance") แล้วเปิดใหม่และรันผ่าน
- [ ] **C5 ไม่ให้ปิดโปรแกรมของผู้ใช้:** ตั้ง `AUT_CLOSE_EXISTING=false` → เปิด ProMaxx2 ค้างไว้ → สั่งรัน
  - คาดหวัง: execution = Failed, ErrorCode `AUT-APP-001` ข้อความ "…เปิดอยู่แล้ว… ปิดโปรแกรมก่อน"; ProMaxx2 ที่เปิดไว้ไม่ถูกปิด (คืนค่าหลังทดสอบ)
- [ ] **C6 path โปรแกรมผิด:** ตั้ง `AUT_EXE` เป็น path ที่ไม่มีอยู่ → สั่งรัน
  - คาดหวัง: Failed, `AUT-APP-001`, Failure Type = EnvironmentFailure, คำแนะนำ RetryOrCheckEnvironment — **Case ต้องไม่ถูกเปลี่ยนเป็น MaintenanceRequired**
- [ ] **C7 ปิดไม่ลง:** ทำให้ ProMaxx2 ถามยืนยันตอนปิด (เช่นมีบิลค้าง) แล้วจบงาน
  - คาดหวัง: ภายใน ~10 วินาทีหลังจบงาน ProMaxx2 ถูกบังคับปิด
- [ ] **C8 inspect ชนกับงาน:** ระหว่าง Agent รันงาน สั่ง `Runner.exe inspect …` ในอีก terminal
  - คาดหวัง: inspect หยุดพร้อมข้อความ "Runner อีกตัวบนเครื่องนี้กำลังใช้หน้าจออยู่"

## D. ความถูกต้องของ UI actions — AUT-AGT-002

สร้าง **Case-ACTIONS** ที่ครอบคลุมขั้นตอนต่อไปนี้ (ปรับ object/screen ให้ตรงกับ Object Repository จริง) แล้วสั่งรัน:

- [ ] **D1 ปุ่มพิเศษ:** `LOGIN` ที่ต้องกด `{ENTER}`, `SELECT_ITEM` (ยืนยันด้วย Enter), `PRESS_KEY` `{F8}` (Save)
  - คาดหวัง: ปุ่มถูกกดจริง (ไม่มีข้อความ `{ENTER}`/`{F8}` ถูกพิมพ์ลงช่อง) และขั้นตอนถัดไปเห็นผลของการกด
- [ ] **D2 ปุ่มผสม:** `PRESS_KEY` `%F` (Alt+F), `^S` (Ctrl+S), `+{TAB}` (Shift+Tab)
  - คาดหวัง: เมนู/คำสั่งที่เกี่ยวข้องทำงาน; ถ้าใส่ `{ENTR}` (สะกดผิด) ได้ `AUT-DSL-002`
- [ ] **D3 EXPECT_ENABLED / EXPECT_DISABLED:** ตรวจปุ่มที่ disabled จริงด้วย `EXPECT_DISABLED` และที่ enabled ด้วย `EXPECT_ENABLED`
  - คาดหวัง: ผ่านทั้งคู่; สลับกัน (EXPECT_ENABLED กับปุ่มที่ disabled) → Fail; object ที่ไม่มีอยู่ → Fail
- [ ] **D4 popup / MessageBox:** ทำให้ ProMaxx2 เด้ง MessageBox แล้วใช้ `EXPECT_MESSAGE` และ `CLICK` ปุ่มใน popup
  - คาดหวัง: หา control ใน popup เจอ (ไม่ใช่เฉพาะหน้าต่างหลัก)
- [ ] **D5 WAIT_SCREEN:** `WAIT_SCREEN` หน้าจอที่ใช้เวลาเปิด 3–5 วินาที
  - คาดหวัง: รอจนหน้าจอขึ้นจริง; ใส่ชื่อหน้าจอที่ไม่มี → Fail หลัง timeout (เดิมผ่านทันที)
- [ ] **D6 EXPECT_VALUE / SET_TEXT:** กรอกช่องที่มีค่าเดิมด้วย `SET_TEXT` แล้ว `EXPECT_VALUE`
  - คาดหวัง: ค่าเดิมถูกล้างก่อนพิมพ์ (ไม่พิมพ์ต่อท้าย) และ EXPECT_VALUE อ่านค่าในช่อง ไม่ใช่ label
- [ ] **D7 EXPECT_NOT_VISIBLE:** กับ control ที่หายไปแล้ว
  - คาดหวัง: ผ่านภายใน ~1 วินาที (ไม่รอจนครบ timeout)
- [ ] **D8 action ที่ Agent ไม่รองรับ:** ถ้ามี action ใหม่ที่ Agent ยังไม่รองรับ
  - คาดหวัง: ErrorCode `AUT-AGENT-002` (ข้ามได้ถ้าไม่มีตัวอย่าง)

## E. DB assertion อ่านอย่างเดียว — AUT-SEC-004

- [ ] **E1 SELECT ใช้ได้ (Firebird):** Case-DB ที่มี `EXPECT_DB_VALUE` (`query` = SELECT) บน Environment ที่เป็น Firebird → รัน
  - คาดหวัง: Pass เมื่อค่าตรง, Fail เมื่อค่าไม่ตรง
- [ ] **E2 นับแถว (SQL Server):** `EXPECT_DB_ROW_COUNT` บน Environment ที่เป็น SQL Server
  - คาดหวัง: ได้จำนวนแถวถูกต้อง (เดิมพังเพราะ derived table ไม่มี alias)
- [ ] **E3 Hub ปฏิเสธคำสั่งเขียน:** สร้าง version ใหม่ที่ `query` เป็น `UPDATE …`, `DELETE …`, `SELECT 1; DROP TABLE x`, หรือมี comment `--`
  - คาดหวัง: Validate ไม่ผ่านพร้อมข้อความที่ระบุว่า query ต้องเป็น SELECT/WITH คำสั่งเดียว
- [ ] **E4 ข้อมูลไม่ถูกแก้:** หลังรัน E1/E2 ตรวจตารางที่ query อ้างถึงใน DB ของ AUT
  - คาดหวัง: ไม่มีการเปลี่ยนแปลงข้อมูล (read-only transaction / rollback)

## F. งานชนกันและงานค้าง — AUT-REL-001 / AUT-REL-002

เตรียม token ของบัญชีที่มี `AUTOMATION.EXECUTE` (PowerShell บนเครื่องใดก็ได้):

```powershell
$hub = 'https://api-promaxx2.qahub.store/api/v1'
$cred = Get-Credential   # บัญชีทดสอบ
$token = (Invoke-RestMethod "$hub/auth/login" -Method Post -ContentType 'application/json' `
  -Body (@{ username = $cred.UserName; password = $cred.GetNetworkCredential().Password } | ConvertTo-Json)).accessToken
$h = @{ Authorization = "Bearer $token" }
```

- [ ] **F1 ยกเลิกซ้อนสองคำขอ:** สั่งรัน Case-OK แล้วระหว่างที่ Running ยิง cancel สองคำขอพร้อมกัน
  ```powershell
  $exec = '<executionId>'; $proj = '<projectId>'
  1..2 | ForEach-Object { Start-Job { param($u,$h) try { Invoke-RestMethod $u -Method Post -Headers $h; 'OK' } catch { $_.Exception.Response.StatusCode.value__ } } -ArgumentList "$hub/automation/executions/$exec/cancel?projectId=$proj", $h } | Wait-Job | Receive-Job
  ```
  - คาดหวัง: ได้ `OK` หนึ่งคำขอ และ `409` อีกคำขอ (**ห้ามมี 500**); SQL: execution = Cancelled หนึ่งแถว, ไม่มี retry เกิดขึ้น
- [ ] **F2 ยกเลิกชนกับงานจบ:** กดยกเลิกในหน้า Automation ช่วงวินาทีท้าย ๆ ของ Case-OK
  - คาดหวัง: ได้อย่างใดอย่างหนึ่ง — ยกเลิกสำเร็จ หรือข้อความ "Execution นี้เพิ่งจบหรือถูกยกเลิกโดยคำขออื่น" (409); ไม่มี 500 และผลที่แสดงตรงกับ DB
- [ ] **F3 เลข version ไม่ซ้ำ:** Case ที่มี version 1, 2 → อนุมัติ version 1 → สร้าง version ใหม่
  - คาดหวัง: version ใหม่เป็น 3 (เดิมได้ 2 ซ้ำ)
- [ ] **F4 webhook RequestId ซ้ำพร้อมกัน:** สร้าง Webhook Token แล้วยิงสองคำขอพร้อมกันด้วย `requestId` เดียวกัน
  ```powershell
  $body = @{ releaseId = '<releaseId>'; buildNumber = "FT-$(Get-Random)"; requestId = "ft-$(Get-Random)" } | ConvertTo-Json
  1..2 | ForEach-Object { Start-Job { param($u,$b,$t) try { (Invoke-RestMethod $u -Method Post -ContentType 'application/json' -Headers @{ 'X-Webhook-Token' = $t } -Body $b).status } catch { $_.Exception.Response.StatusCode.value__ } } -ArgumentList "$hub/webhooks/automation/builds", $body, '<webhook token>' } | Wait-Job | Receive-Job
  ```
  - คาดหวัง: `Created` หนึ่งคำขอ และ `Duplicate` อีกคำขอ; มี Build ใหม่แค่ 1 ตัว (ลบ Build ทดสอบหลังจบ)
- [ ] **F5 Agent ตายระหว่างรัน → AgentLost:** สั่งรัน Case ที่ใช้เวลานาน (เช่น B4) → kill `ProMaxx2.Automation.Runner.exe` ใน Task Manager
  - คาดหวัง: ภายใน ~11 นาที execution = AgentLost, ErrorCode `AUT-AGENT-001`, Job = AgentLost; มี execution retry ใหม่ (Queued) ถ้าเปิด Retry Policy; log ของ API มี "AutomationReaper closed stale work"
- [ ] **F6 Agent กลับมารายงานผลช้า:** หลัง F5 เริ่ม Agent ใหม่
  - คาดหวัง: execution เดิมยังเป็น AgentLost (ไม่ถูกเขียนทับ); Agent รับงาน retry ได้ตามปกติ
- [ ] **F7 Agent ที่ยังทำงานไม่ถูกปิด:** ระหว่าง B4 (งานยาวแต่ Agent ส่ง heartbeat) รอเกิน 10 นาที
  - คาดหวัง: execution **ไม่** ถูกปิดเป็น AgentLost (reaper ดู heartbeat ไม่ใช่อายุงาน)
- [ ] **F8 ตรวจ step ซ้ำ:** หลังจบทุกข้อ รัน SQL "step ซ้ำ" ใน §1.3
  - คาดหวัง: 0 แถว

## G. หน้า Automation — AUT-UI-001 / AUT-UI-002

ทดสอบบน Desktop (1440 และ 1024 px) และ Mobile (768 และ 390 px — ใช้ DevTools device mode ได้) — ทุกขนาด **ต้องไม่มี horizontal scroll ระดับหน้า**

- [ ] **G1 modal Execution ไม่ค้างข้อมูลเก่า:** เปิด Execution ที่ Failed → กด "จำแนก Fail" และ "วิเคราะห์ด้วย AI" → ปิด → เปิดอีก Execution
  - คาดหวัง: ผลจำแนก/AI ของ Execution แรกไม่แสดงใน Execution ที่สอง
- [ ] **G2 สิทธิ์สร้าง Defect:** login ด้วยบัญชี **ไม่มี** `DEFECT.EDIT` → ไม่เห็นปุ่ม "สร้าง Defect"; บัญชีที่ **มี** → เห็นปุ่ม
- [ ] **G3 สร้าง Defect ครั้งเดียว:** กด "สร้าง Defect" → ยืนยัน
  - คาดหวัง: มีกล่องยืนยันก่อน, สร้างแล้วปุ่มหายทันที, ปิดแล้วเปิด Execution เดิมจากรายการก็ไม่มีปุ่ม; หน้า Defect มี Defect ใหม่ 1 รายการ
- [ ] **G4 Token webhook:** จัดการ → Webhook → สร้าง Token
  - คาดหวัง: คลิกพื้นหลังไม่ปิด; ปุ่ม "คัดลอก" ใช้ได้ (วางใน Notepad ได้ค่าเดียวกัน); กดปิด/Escape ก่อนคัดลอกมีกล่องยืนยัน; บน http ที่ไม่ใช่ localhost ถ้าคัดลอกอัตโนมัติไม่ได้ ข้อความถูกเลือกไว้พร้อมคำแนะนำ Ctrl+C
- [ ] **G5 สั่งรัน Seed/Cleanup:** Seed/Cleanup → รัน Script ประเภท Cleanup
  - คาดหวัง: หัวข้อเป็น "สั่งรัน Cleanup Script — <ชื่อ>" (ไม่ใช่ "ขอ Snapshot"), มีคำเตือนสีเหลือง, กล่องยืนยันระบุ Environment/Build, หลังส่งมีข้อความ "ส่ง … เข้าคิวแล้ว"
- [ ] **G6 error ไม่แสดงเป็นรายการว่าง:** หยุด API ชั่วคราว แล้วสลับแท็บ Cases/Execution/Failure/Suites/Seed
  - คาดหวัง: แสดงข้อความ error สีแดง ไม่ใช่ "ยังไม่มี…"; เปิด API แล้วกด "ลองใหม่"/สลับแท็บกลับมาแสดงข้อมูลได้
- [ ] **G7 ค้นหาไม่ยิงทุกตัวอักษร:** DevTools → Network → พิมพ์ในช่องค้นหา Cases เร็ว ๆ 6 ตัวอักษร
  - คาดหวัง: มีคำขอ `/automation/cases?…search=` หลังหยุดพิมพ์ ~0.3 วินาที (ไม่ใช่ 6 คำขอ)
- [ ] **G8 Escape และ focus:** เปิดฟอร์มสร้าง Schedule → ช่องแรกได้ focus; กด Tab วนอยู่ใน modal; ปิดแล้ว focus กลับปุ่มที่กดเปิด
- [ ] **G9 ฟอร์มไม่หายเพราะคลิกพื้นหลัง:** กรอกฟอร์ม (Schedule/Build Trigger/Seed Script/Suite) → คลิกพื้นหลัง → ไม่ปิด; กด Escape → มีกล่องยืนยันก่อนปิด
- [ ] **G10 modal อ่านอย่างเดียว:** รายละเอียด Execution/ประวัติการรัน → คลิกพื้นหลังหรือ Escape ปิดได้ทันที; modal ซ้อนกัน Escape ปิดเฉพาะตัวบนสุด
- [ ] **G11 dropdown Build/Environment:** เปิดสั่งรัน Case / Batch / Suite / Schedule / Snapshot ขณะไม่ได้เลือก Release → มีข้อความให้เลือก Release (Snapshot/Seed) หรือ Build ว่าง; ถ้าโหลดล้ม (หยุด API ก่อนเปิด) ข้อความ error แสดง **ภายใน modal**
- [ ] **G12 Mobile:** ที่ 390 px modal เปิดเต็มจอ, ปุ่มกดง่าย, ช่อง Token และปุ่มคัดลอกเรียงแนวตั้ง

## H. Capture Companion — AUT-CAP-001 ถึง 005

- [ ] **H1 เริ่ม session (CAP-001/004):** AgentGui → Capture UIA → login QA Hub → เลือก Project/Module/Test Case
  - คาดหวัง: เห็นเฉพาะ Project ที่มีสิทธิ์; session ถูกสร้าง (SQL `AutomationCaptureSessions` มีแถว Draft ของผู้ทดสอบ)
- [ ] **H2 จับการคลิก (CAP-003):** เปิด ProMaxx2 เป็นหน้าต่างหน้า → คลิกปุ่มต่าง ๆ 3–5 ครั้ง
  - คาดหวัง: Step ถูกบันทึกพร้อม AutomationId/ControlType; control ที่ไม่มี AutomationId ถูกแจ้งเตือน; คลิกนอก ProMaxx2 (เช่น Notepad) ไม่ถูกบันทึก
- [ ] **H3 กรอกข้อความและรหัสผ่าน (CAP-005):** กรอกช่องข้อความทั่วไปและช่องรหัสผ่านของ ProMaxx2
  - คาดหวัง: ช่องทั่วไปได้ TextInput step พร้อมค่า; ช่องรหัสผ่านถูก mask และ **ไม่มีค่าจริง** ในหน้า Preview และใน DB
- [ ] **H4 Preview / Step Editor (CAP-004):** แก้ลำดับ/ลบ step → Preview
  - คาดหวัง: Preview แสดง Object ที่จะ reuse และที่จะสร้างใหม่แยกกันชัดเจน
- [ ] **H5 Commit (CAP-002):** กด Commit
  - คาดหวัง: Object ที่มีอยู่แล้วถูก reuse (ไม่สร้างซ้ำ), Object ใหม่ถูกสร้าง, Test Case ได้ revision ใหม่; SQL session = Committed
- [ ] **H6 Clear/Discard:** เริ่ม session ใหม่ → Clear session / Discard
  - คาดหวัง: session = Discarded และไม่มีข้อมูลถูกเขียนลง Object/Test Case
- [ ] **H7 เครือข่ายหลุด (CAP-004):** ระหว่าง capture หยุด API 30 วินาทีแล้วเปิด
  - คาดหวัง: Capture ไม่ crash, แจ้งสถานะ และ Commit ได้หลังเชื่อมต่อกลับ
- [ ] **H8 token/session หมดอายุ (CAP-004):** ทิ้ง session ไว้จนหมดอายุ แล้วกด Commit
  - คาดหวัง: ได้ข้อความว่า session หมดอายุ/ต้องเริ่มใหม่ ไม่ใช่ error ทั่วไป

## I. ตรวจซ้ำรายการ DONE ที่ยังไม่เคยทดสอบกับของจริง (ไม่บังคับ แต่แนะนำ)

- [ ] **I1 Object Verification (AUT-P0-006):** Object Repository → เลือก Object → ตรวจสอบ → `Runner.exe verify --exe <PromaxxsPos.exe>`
  - คาดหวัง: ผล Found/NotFound/ControlTypeMismatch ตรงกับหน้าจอจริง
- [ ] **I2 Snapshot + Restore (AUT-DATA-001/002):** ขอ Snapshot → `Runner.exe snapshot` → ขอ Restore → `Runner.exe restore`
  - คาดหวัง: Snapshot Succeeded พร้อม checksum; ระหว่าง gbak ทำงาน ดู command line ของ `gbak.exe` ใน Task Manager (คอลัมน์ Command line) ต้อง **ไม่มีรหัสผ่าน** (AUT-AGT-004); Restore Succeeded และ checksum/availability ตรวจแล้ว
- [ ] **I3 Seed / Cleanup / Master Data (AUT-DATA-003/004/005):** สั่งรันแต่ละประเภท → `Runner.exe seed`
  - คาดหวัง: Succeeded พร้อม rows affected; Master Data ที่ยังไม่อนุมัติรันไม่ได้; ผู้สร้างอนุมัติ script ตัวเองไม่ได้

---

## 10. สรุปผล (กรอกหลังทดสอบ)

| ข้อ | ผล | วันที่ | ผู้ทดสอบ | หลักฐาน / หมายเหตุ |
|---|---|---|---|---|
| A1–A8 | | | | |
| B1–B5 | | | | |
| C1–C8 | | | | |
| D1–D8 | | | | |
| E1–E4 | | | | |
| F1–F8 | | | | |
| G1–G12 | | | | |
| H1–H8 | | | | |
| I1–I3 | | | | |

## 11. การปิดรายการใน AUTOMATION_TODO.md

- รายการในตาราง §0 เปลี่ยนเป็น `DONE` ได้เมื่อ **ทุกข้อในส่วนที่ผูกไว้ผ่าน** — ใส่วันที่และสรุปหลักฐานในช่อง Acceptance Criteria และเพิ่ม Progress Log
- ข้อที่ไม่ผ่าน: คงสถานะ `IN PROGRESS` และเปิดรายการใหม่ในตาราง P0 พร้อมขั้นตอนทำซ้ำ, Execution ID และ screenshot
- หลังทดสอบ คืนค่าที่เปลี่ยนชั่วคราว: `QAHUB_ALLOW_INSECURE_HTTP`, `AUT_CLOSE_EXISTING`, `AUT_EXE`, `ACTION_TIMEOUT_SECONDS`, Build/Token ทดสอบ
