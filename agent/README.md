# ProMaxx2 Automation Agent

Central Windows Agent ที่รับ Job จาก ProMaxx2 QA Hub แล้วควบคุม **ProMaxx2 Windows Application** (System Under Test) ตาม Automation DSL ที่ QA Hub ส่งมา

สอดคล้องกับ `Document/01-System-Blueprint/AUTOMATION_MODULE_DEVELOPMENT_PLAN.md` (ข้อ 11-19)

## โครงสร้าง

```text
agent/
+-- ProMaxx2.Automation.Core      <- AgentConfig, DSL model, IUiAutomationDriver + FlaUI driver, ActionExecutor
+-- ProMaxx2.Automation.Hub       <- QaHubClient: register / heartbeat / claim / step result / evidence / complete
+-- ProMaxx2.Automation.Runner    <- console app (loop: heartbeat -> claim -> execute DSL -> report)
+-- ProMaxx2.Automation.AgentGui  <- GUI Launcher (ตั้งค่า + เริ่ม/หยุด Agent)
+-- run-agent.ps1                 <- ตั้ง env ชั่วคราวแล้วรัน Runner
+-- set-agent-env.ps1             <- ตั้ง User environment variables (เก็บเฉพาะเครื่อง)
+-- ProMaxx2.Automation.slnx
```

## ขั้นตอนติดตั้ง (บน Windows Test Machine)

1. Build: `dotnet build agent/ProMaxx2.Automation.slnx`
2. **แนะนำ: ใช้ GUI Launcher** — เปิด `agent\ProMaxx2.Automation.AgentGui\bin\Debug\net10.0-windows\ProMaxx2.Automation.AgentGui.exe`
   - กรอกการตั้งค่า (QA Hub URL/User/Password, Agent Code, AUT exe/User/Password, Database validator) → **บันทึกตั้งค่า**
   - **AUT EXE 1 (Pos)** + **AUT EXE 2 (App)** — รองรับ 2 แอป; กด **▶ เริ่ม Agent 1** (ใช้ path 1) และ **▶ เริ่ม Agent 2** (ใช้ path 2, Agent Code อัตโนมัติเป็น `{code}-APP`) รันพร้อมกันได้ / **■ หยุด** หยุดทั้ง 2 ตัว
   - ปุ่ม **ทดสอบเชื่อมต่อ** ตรวจ QA Hub login
   - Password เก็บเข้ารหัส DPAPI ใน `agent-config.json` (ข้าง exe)

## Target App Routing

- แต่ละ **Automation Case** มี `AutomationType` = `Pos` / `App` / `WindowsUI` (generic) — กำหนดได้ในหน้า Cases (Case Detail → select Target App)
- แต่ละ **Agent** ประกาศ target เองจากชื่อ exe (`PromaxxsPos.exe`→`Pos`, `Promaxxs.App.exe`→`App`; ตั้งค่า override ได้ด้วย env `AUT_TARGET`)
- ตอน Claim งาน QA Hub กรองให้ Agent รับเฉพาะงานที่ตรง target: `Pos` รับงาน `Pos`+`WindowsUI`, `App` รับงาน `App`+`WindowsUI`, `WindowsUI` รับทุกงาน
- ผลจริง: รัน batch (Pos case + App case) ด้วย 2 agents → POS agent รับ Pos job, APP agent รับ App job ✓
3. หรือตั้งค่า env เองแล้วรัน console: `agent\run-agent.ps1` หรือ `set-agent-env.ps1`

## Flow การทำงาน

```text
Runner เริ่ม -> Login QA Hub -> Register Agent
  -> loop:
       Heartbeat (ทุก 15 วิ)
       Claim Job (POST /automation/jobs/claim)
       ถ้ามี Job:
         - Parse DSL จาก Execution Package
         - Launch AUT (ProMaxx2.exe) + รอ Main Window
         - Execute ทุก Step ตาม Action (LOGIN/OPEN_MENU/CLICK/SET_TEXT/EXPECT_* ...)
         - ส่ง Step Result ทีละขั้น
         - ถ้า Fail: ถ่าย Screenshot แล้ว Upload Evidence
         - Complete Execution (Passed/Failed)
```

## Action ที่รองรับใน MVP

- Authentication: `LOGIN`
- Navigation: `OPEN_MENU`, `OPEN_SCREEN`, `CLOSE_SCREEN`, `WAIT_SCREEN`
- Document: `NEW_DOCUMENT`, `SEARCH_DOCUMENT`, `SAVE_DOCUMENT`, `APPROVE_DOCUMENT`, `CANCEL_DOCUMENT`, `DELETE_DOCUMENT`
- Item: `SELECT_ITEM`, `SET_QTY`, `SET_PRICE`, `SET_DISCOUNT`, `SET_LOT`, `REMOVE_ITEM`
- Generic UI: `CLICK`, `SET_TEXT`, `SELECT_COMBO`, `CHECK`, `UNCHECK`, `PRESS_KEY`, `WAIT_OBJECT`
- Validation: `EXPECT_MESSAGE`, `EXPECT_TEXT`, `EXPECT_VALUE`, `EXPECT_VISIBLE`, `EXPECT_NOT_VISIBLE`, `EXPECT_ENABLED`, `EXPECT_DISABLED`

Object ที่ Action อ้างอิงจะถูก resolve ผ่าน **Object Repository** ของ QA Hub ด้วย `ScreenCode.ObjectCode` → `AutomationId` (Selector Strategy ตามแผนข้อ 9.2)

## G5 Pilot — ผลจริงบน ProMaxx2 (PromaxxsPos.exe)

Discovery ผ่าน `Runner inspect` (FlaUI dump UIA tree) + auto-login → สร้าง Object Repository จริง:

| Business Key | AutomationId | จอ |
|---|---|---|
| `Login.TxtEmpId` | `TxtEmpId` | หน้า Login (รหัสพนักงาน) |
| `Login.PwdBox` | `PwdBox` | หน้า Login (รหัสผ่าน) |
| `Login.BtnSignIn` | `BtnSignIn` | หน้า Login (เข้าสู่ระบบ) |
| `Sales.ScanCodeBox` | `ScanCodeBox` | POS สแกนสินค้า |
| `Sales.Qty` | `TxtQty` | POS จำนวน |

Flow จริงที่พิสูจน์แล้ว (Agent รัน 5/5 ผ่าน):
```text
LOGIN  (supervisor/seniorsoftmaxx)          -> หน้า POS หลัก
SELECT_ITEM itemCode=0001                    -> เพิ่มสินค้าเข้าบิล
SET_QTY value=2                              -> ตั้งจำนวน
PRESS_KEY {F8}                               -> บันทึก (F8 = Save)
EXPECT_MESSAGE ยังไม่มีสินค้าในบิล / มูลค่าสินค้า / รายการขาย
```

คำสั่งสำหรับสำรวจจอใหม่:
```text
Runner inspect --exe <path> --out uia.json --wait 10 --emp <user> --pwd <pwd> --after 15 [--nav <menuId>] [--scan <code>] [--qty <value>] [--press {F8}]
Runner trylogin --exe <path>    # ลองหลาย user/pass จนกว่าจะผ่าน (dev เท่านั้น)
```

## Security

- Agent ใช้บัญชี QA Hub เฉพาะ (ไม่ใช่ admin) ที่มีสิทธิ์ `AUTOMATION.EXECUTE`
- Credential ของ ProMaxx2 (`AUT_USER`/`AUT_PASSWORD`) อยู่เฉพาะเครื่อง ไม่ถูกส่งเข้า DSL
- DSL ไม่มี credential ใด ๆ (ใช้ Reference เช่น `QA_STANDARD_USER`)

## G7 — Database Validation + Evidence

### Database Validator (Firebird / SQL Server)

Credential มาจาก env ไม่ใช่ DSL (Secure Configuration):

```text
AUT_DB_TYPE      = Firebird | SqlServer   (default Firebird)
AUT_DB_HOST      = 127.0.0.1
AUT_DB_PORT      = 3050
AUT_DB_USER      = SYSDBA
AUT_DB_PASSWORD  = <secret>
AUT_DB_DATABASE  = C:\SeniorSoft ProMaxx\FBMAXX.FDB
```

Actions ใหม่ (built-in parameterized, ไม่ให้ AI เขียน SQL ตรง):

```text
EXPECT_DB_VALUE    query=...  expected=...  column=...  parameters={"code":"003"}
EXPECT_DB_ROW_COUNT query=...  expected=...
EXPECT_STOCK       itemCode=...  expected=...
EXPECT_LOT         itemCode=...  lotNo=...  expected=...
EXPECT_TRANSACTION transNo=...  expected=1
```

- Query เป็น **parameterized** (ป้องกัน injection) — Parameter `@code` ฯลฯ มาจาก `parameters` JSON
- เปรียบเทียบรองรับตัวเลข, ข้อความ, bool และ operator (`>=`, `>`, `<`, `<=`, `!=`)
- ตัวอย่างที่ใช้ได้จริงกับ Firebird ProMaxx2:

```text
EXPECT_STOCK itemCode=003 expected=48          -> stock จริงจาก CALCFIFO
EXPECT_DB_VALUE query="SELECT ITEMNAME FROM ITEMS WHERE SYSITEMID=(SELECT SYSITEMID FROM ITEMBARCODE WHERE BARCODE=@code)" parameters="{\"code\":\"003\"}" column="ITEMNAME" expected="ทดสอบ 003"
EXPECT_DB_VALUE query="SELECT COUNT(*) FROM TRANS WHERE TRANDATE=CURRENT_DATE" expected=">=1"
```

### Evidence (ทุกการรัน)

Agent อัปโหลด evidence เข้า QA Hub (ตาราง `AutomationEvidences` + ไฟล์ใน `App_Data/AutomationEvidence`):

| EvidenceType | เนื้อหา |
|---|---|
| `Screenshot` | หน้าจอตอน step fail |
| `SqlResult` | JSON ของ query + parameters + actual/expected + passed ทุก DB assertion |
| `AutomationLog` | Log ทุก action/step/result ตอนจบ execution |

ผลลัพธ์จริง (Pilot G7): `AUT-PMX2-MOD-015-TC-004` Passed — LOGIN → EXPECT_STOCK(48) → EXPECT_DB_VALUE(ชื่อ item) → EXPECT_DB_VALUE(count) พร้อม evidence SqlResult 3 ไฟล์ + AutomationLog 1 ไฟล์ trace กลับ execution ได้ครบ

## G9 — Defect / Failure Classification (ฝั่ง QA Hub)

เมื่อ Execution Fail QA Hub จะแยกประเภท Fail (ตามแผน §24/§31) ผ่าน:
- **Rule-based Classifier** (`POST /automation/executions/{id}/classify`): ดู ErrorCode + Action ของ step ที่ Fail
  - `EXPECT_*` mismatch → **AssertionFailure** (Product Defect Candidate = ใช่ → QA ตรวจก่อนสร้าง Defect)
  - `AUT-UI-001/002/003` (Object ไม่เจอ/Disabled/Timeout) → **AutomationFailure** (Maintenance ไม่ใช่ Product Defect)
  - `AUT-APP-*` / `AUT-DB-*` → **EnvironmentFailure** · `AUT-AGENT-*` / `AUT-JOB-*` → **AgentFailure**
- **AI Failure Analyzer** (`POST .../analyze`): AI วิเคราะห์ → `{ classification, confidence, summary, recommendation }` — เป็นคำแนะนำเท่านั้น QA ตัดสินใจสุดท้าย
- **สร้าง Defect** (`POST .../defect`): หลัง QA ยืนยัน → สร้าง Defect (DefectCode `{PROJECT}-DEF-xxx`) + link TestCase + **link Execution↔Defect** (`AutomationExecutions.DefectId`)

## G10 — Regression / Multi-Agent (QA Hub)

- **Batch Run** (`POST /automation/batch-run`): สร้าง Execution + Job พร้อมกันหลาย Automation Case (เฉพาะ Ready + มี approved version) → เข้าคิว งานกระจายไปหลาย Agent รัน parallel
- **Coverage Dashboard** (`GET /automation/dashboard`): Total Test Cases / Candidates / Cases / Ready / Maintenance / Running / Pass-Fail วันนี้ / Avg Duration / Agents Online + Ready Coverage + Candidate Coverage
- **Agent**: ไม่ต้องแก้ — จุด Claim งานเดิม (`/automation/jobs/claim`) รองรับหลาย agent พร้อมกันอยู่แล้ว (แต่ละ agent claim job ที่ว่างคนละชุด)
- ผลจริง: รัน batch 7 cases ด้วย 2 agents (`QA-POS-PILOT` + `QA-POS-PILOT-2`) → 4 Passed / 3 Failed กระจายกันรัน parallel (G10 acceptance)