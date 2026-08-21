# ProMaxx2 QA Hub — TEST AUTOMATION PLAN

> แผนพัฒนา Test Automation สำหรับ **Promaxx2 Suite (Desktop WPF/.NET 10)** — ประกอบด้วย **Promaxxs.App.exe** (งานจัดการข้อมูล Master) และ **PromaxxsPos.exe** (งานเปิดบิลขาย) — โดยใช้ข้อมูลจาก **QA Hub**
> เอกสารนี้ครอบคลุม: การเลือกเครื่องมือ, สถาปัตยกรรม, Phase 0–5 แบบละเอียด และแผนรองรับการเข้ารหัสไฟล์ (Encryption Readiness)

---

## 1. บริบทและเป้าหมาย

### 1.1 ระบบที่เกี่ยวข้อง

| ระบบ | บทบาท | เทคโนโลยี |
|---|---|---|
| QA Hub (`QAManagementSystem`) | **Source of Truth** — เก็บ Test Case, Step, Cycle, Execution, Defect, Regression Analysis + API | React + ASP.NET Core + SQL Server |
| **Promaxxs.App.exe** (`ProMaxx2/1.0.0-beta.2`) | **AUT #1 — จัดการข้อมูล Master** (สินค้า, ราคา, โปรโมชั่น, ผู้้าย/ลูกค้า, ตั้งค่าระบบ) | .NET 10 WPF |
| **PromaxxsPos.exe** (`ProMaxx2/1.0.0-beta.2`) | **AUT #2 — งานเปิดบิลขาย (POS)** | .NET 10 WPF + Firebird DB local (`DB/FBMAXX2.FDB`) + `config/system.ini` |

> **Flow ข้อมูลระหว่าง AUT:** Master data ที่สร้าง/แก้ไขผ่าน `Promaxxs.App.exe` เป็น input ของงานขายใน `PromaxxsPos.exe` → Test Scenario จำนวนมากต้อง **เตรียม master data ก่อน แล้วค่อยทดสอบบน POS** (automation runner ต้องจัดลำดับ launch/setup ทั้งสอง app ได้)

### 1.2 ทำไมข้อมูล QA Hub "พร้อม" สำหรับ Automation

| ข้อมูลใน QA Hub | ประโยชน์ต่อ Automation |
|---|---|
| `TestCase.AutomationCandidate` (flag) | กรองเฉพาะ case ที่ design มาเพื่อ automate |
| `TestStep` (StepNo / Action / TestData / ExpectedResult) | โครงสร้างพร้อมแปลงเป็น script skeleton + assertion |
| Priority P0–P3, Status Draft→Review→Ready→Deprecated | เลือก scope run (เช่น smoke = P0 + Ready) |
| Module tree | จัดกลุ่ม PageObject ต่อ Module ได้ตรงกับโครงสร้าง app |
| Execution API (`POST /test-cycle-cases/{id}/executions` + `TestStepResult`) | **Write-back ผล run** → dashboard/pass rate อัปเดตอัตโนมัติ |
| Regression Impact (`includeAllCaseIds=true`) + Schedules + Notifications | ใช้เป็น run list ต่อ Build ได้ทันที |

### 1.3 เป้าหมาย (Goals)

1. Smoke set (P0) รันอัตโนมัติได้ทุกครั้งที่มี Build ใหม่
2. ผล run กลับเข้า QA Hub อัตโนมัติ (Execution + Step Result + Evidence)
3. Regression ต่อ Build เลือกชุดทดสอบจาก Impact Analysis แล้วรันอัตโนมัติ
4. Fail → สร้าง Defect พร้อม evidence อัตโนมัติ
5. **ทำงานได้แม้ AUT ถูกเข้ารหัสไฟล์/obfuscate** (ดูหัวข้อ 6)

---

## 2. การเลือกเครื่องมือ (Tool Selection)

| ตัวเลือก | ข้อดี | ข้อเสีย | ผลการพิจารณา |
|---|---|---|---|
| **FlaUI (UIA3)** + NUnit | C#/.NET ตรง stack ทีม, รองรับ WPF UIA Tree เต็มรูปแบบ, OSS, รัน headful บน Windows runner ได้ | ต้องเขียน PageObject เอง | ✅ **เลือกใช้** |
| WinAppDriver + Appium | Protocol มาตรฐาน Appium | Microsoft **หยุดพัฒนาแล้ว**, setup ยุ่ง | ❌ ไม่เลือก |
| Ranorex/TestComplete | Commercial support ครบ | ค่า license สูง, lock-in | ❌ ยกเว้นอนาคตถ้า budget อนุญาต |

> หมายเหตุ: ส่วนที่เป็น WebView2 (ถ้ามี) ใช้ selector ฝั่ง DOM ผ่าน CDP แยกจาก UIA ได้ — ยังไม่ใช่ scope Phase แรก

---

## 3. สถาปัตยกรรมระบบ Automation

```
┌───────────────────────────── QA Hub ─────────────────────────────┐
│  GET /test-cases?automation&status=Ready     (JWT Bearer)        │
│  POST /releases/{id}/regression-impact       → run list ต่อ Build │
│  POST /test-cycle-cases/{id}/executions      ← write-back ผล     │
│  POST /defects                               ← auto defect       │
└──────┬──────────────────────────────────────────▲────────────────┘
       │ pull cases/run-list                       │ results/evidence
┌──────▼───────────────┐   ┌──────────────────────┴──────────────┐
│  Exporter / Planner  │   │  Result Publisher                   │
│  (map TC ↔ test id)  │   │  (execution + step result + shot)   │
└──────┬───────────────┘   └──────────────────────▲──────────────┘
       │ test plan (JSON)                          │ pass/fail + screenshot
┌──────▼───────────────────────────────────────────┴──────────────┐
│              Automation Runner (FlaUI/UIA3, NUnit)               │
│  AppLauncher ─► TestDataManager ─► PageObjects[App][Module] ─►OK │
└──────┬───────────────────────────────────────────────────────────┘
       │ launch / reset data
┌──────▼───────────────────────────────────────────────────────────┐
│  AUT: ProMaxx2/1.0.0-beta.x                                      │
│  ① Promaxxs.App.exe   — Master Data (สินค้า/ราคา/โปรโมชั่น/คู่ค้า) │
│  ② PromaxxsPos.exe    — เปิดบิลขาย (ใช้ master จาก ①)             │
│  config/*.ini · DB/FBMAXX2.FDB (Firebird)                        │
└───────────────────────────────────────────────────────────────────┘
```

**องค์ประกอบหลัก**

| Component | หน้าที่ |
|---|---|
| Exporter | ดึง Test Case จาก QA Hub → `testplan.json` (map `TestCaseCode ↔ automation id`) |
| Runner | NUnit + FlaUI รันตาม test plan, จับ screenshot ตอน fail |
| AppLauncher | start/close **ทั้ง `Promaxxs.App.exe` และ `PromaxxsPos.exe`** ตามลำดับที่ scenario ต้องการ + swap `system.ini` ต่อ environment |
| TestDataManager | copy/reset `FBMAXX2.FDB` ต่อ run + seed ข้อมูลด้วย SQL **หรือผ่าน UI ของ Promaxxs.App.exe** |
| Result Publisher | POST ผลเข้า Execution API (+ Step Result), สร้าง Defect เมื่อ fail |
| PageObjects | selector + interaction แยกต่อ **App** (`Pages.App.*` / `Pages.Pos.*`) และต่อ Module ภายใน app (Inventory/Transaction/Person/Settings...) |

> **หลักการแบ่งชุดทดสอบ:** case กลุ่ม Master Data → ทดสอบบน `Promaxxs.App.exe`; case กลุ่มขาย/บิล/สต๊อก → ทดสอบบน `PromaxxsPos.exe` โดยอ้าง master data ที่เตรียมไว้; test plan จึงต้องระบุว่าแต่ละ case "รันบน app ไหน" (field `targetApp: app|pos` ใน JSON)

---

## 4. Naming & Selector Contract (ทำตั้งแต่ Phase 0 — ห้ามข้าม)

1. **Automation id = TestCaseCode**: script id ต้องเป็น code เดียวกับ QA Hub เช่น `[TestCase("TC-SALE-001")]` → trace กลับไป-มาได้โดยไม่ต้องแปล
2. **Control selector contract กับทีม Dev Promaxx2 (App + Pos)**: control ที่ automation ใช้ต้องมี `AutomationProperties.AutomationId="..."` กำกับชัดเจนใน XAML เสมอ (ห้ามพึ่งชื่อ type/text ที่ obfuscator rename ได้)
3. **PageObject เดียวต่อหน้าจอ**: selector ทั้งหมดรวมในไฟล์เดียว — app เปลี่ยน UI แก้จุดเดียว
4. **ห้าม hardcode path/credential** ใน script — ใช้ config/environment variable

---

## 5. แผนพัฒนาราย Phase

> Estimate ต่อ phase เป็น person-week โดยประมาณ (QA Dev 1 คน) — ปรับได้ตามกำลังคนจริง

### Phase 0 — Foundation & Contract (~2 สัปดาห์)

**เป้าหมาย:** วางรากฐานข้อมูล + ข้อตกลงกับทีม Dev ก่อนเขียน automation แม้แต่บรรทัดเดียว

| # | งาน | ผู้รับผิดชอบ |
|---|---|---|
| 0.1 | Review Test Case ใน QA Hub → ติ๊ก `AutomationCandidate` + ดัน Status เป็น `Ready` (เริ่มจาก P0) | QA |
| 0.2 | กำหนด Naming Convention `TestCaseCode ↔ automation id` + JSON schema ของ test plan | QA Dev |
| 0.3 | **Selector Contract กับทีม Dev Promaxx2 (App + Pos)** — ทุก control สำคัญมี `AutomationProperties.AutomationId`, ตกลง rule ที่ obfuscator ห้ามแตะ (ดู §6) | QA + Dev Promaxx2 |
| 0.4 | สร้าง solution Automation (FlaUI.UIA3 + NUnit, .NET 8+) พร้อม CI build | QA Dev |
| 0.5 | เขียน Exporter ดึง Test Case → `testplan.json` (เรียก QA Hub API ด้วย JWT) | QA Dev |

**Deliverables:** repo automation, `testplan.json` ตัวอย่าง, เอกสาร Selector Contract
**Exit Criteria:** export ได้ case ที่ Ready+P0 ครบ, ทีม Dev ยืนยัน rule AutomationId

### Phase 1 — PoC Smoke Set (~4 สัปดาห์)

**เป้าหมาย:** พิสูจน์ว่าควบคุม POS ผ่าน UIA ได้จริง ด้วยชุด smoke เล็กที่เสถียร

| # | งาน |
|---|---|
| 1.1 | AppLauncher — launch/close **`Promaxxs.App.exe` + `PromaxxsPos.exe`** (ตามลำดับที่ scenario กำหนด, รอพร้อมทำงาน, จัดการ crash/timeout) |
| 1.2 | Environment switcher — swap `config/system.ini` ต่อ scenario |
| 1.3 | PageObject pattern แยก **ต่อ App (`Pages.App.*` / `Pages.Pos.*`) และต่อ Module** (เริ่มเฉพาะ module ที่ smoke cover) |
| 1.4 | เขียน Smoke Set P0 ~10–15 case จาก `testplan.json` |
| 1.5 | Runner CLI (`run --plan testplan.json --env lab`) + report พื้นฐาน (console/HTML) |

**Exit Criteria:** รันซ้ำ 5 ครั้งผ่าน ≥95% (วัดความนิ่ง), runtime <15 นาที

### Phase 2 — Test Data & Write-back (~3 สัปดาห์)

**เป้าหมาย:** run แต่ละครั้งเริ่มจากข้อมูล clean และผลกลับเข้า QA Hub โดยไม่ต้องกรอกมือ

| # | งาน |
|---|---|
| 2.1 | TestDataManager — snapshot/copy `FBMAXX2.FDB` ต่อ run, restore เมื่อจบ, SQL seed ข้อมูลทดสอบ |
| 2.1b | **Master Data Setup flow** — เตรียมข้อมูลหลัก (สินค้า/ราคา/โปรโมชั่น) ก่อน scenario POS: ผ่าน UI automation บน `Promaxxs.App.exe` หรือ seed DB ตรง (เลือกตามความเสถียร) |
| 2.2 | Map `TestCaseCode → cycleCaseId` ของ Cycle ปัจจุบัน (API) + route case ไปยัง app ปลายทาง (`targetApp`) |
| 2.3 | Result Publisher — `POST /test-cycle-cases/{id}/executions` ระดับ Pass/Fail/Blocked + Step Result ราย step |
| 2.4 | Screenshot + log evidence แนบตอน fail (upload ตาม mechanism ของ QA Hub) |
| 2.5 | Auto create Defect draft เมื่อ fail (title/code/log/screenshot) — ให้ QA review ก่อน submit |

**Exit Criteria:** Dashboard/Execution Workspace ของ QA Hub อัปเดตจาก run อัตโนมัติได้ครบ loop

### Phase 3 — CI Integration + Encryption Check (~2 สัปดาห์)

**เป้าหมาย:** รันอัตโนมัติบนเครื่อง runner และ**พิสูจน์ว่าทำงานได้กับ build ที่เข้ารหัสจริง**

| # | งาน |
|---|---|
| 3.1 | เตรียม Windows self-hosted runner (GitHub Actions หรือ Task Scheduler) — ต้องมี display session สำหรับ UIA |
| 3.2 | Pipeline trigger ต่อ Build ใหม่ (manual trigger ก่อน → ต่อ webhook/notification ภายหลัง) |
| 3.3 | Report + notification หลังรัน (summary pass/fail, artifact screenshots/logs) |
| 3.4 | **Automation Compatibility Check** — รัน smoke pack กับ build ที่ obfuscate/encrypt แล้วทุก release (ดู §6.4) |

**Exit Criteria:** รัน green บน runner, ผ่าน smoke pack บน encrypted build ≥1 เวอร์ชันจริง

### Phase 4 — Regression Closed Loop (~3 สัปดาห์)

**เป้าหมาย:** Build ใหม่มา → เลือกชุดทดสอบจาก Impact Analysis → รัน → ผล+defect กลับเข้าระบบ โดยไม่แตะมือ

| # | งาน |
|---|---|
| 4.1 | ใช้ `regression-impact` (recommended case ids) เป็น run list ต่อ Build |
| 4.2 | Trigger อัตโนมัติจาก Scheduled Regression notification ของ QA Hub |
| 4.3 | ขยาย PageObject ครอบคลุม case regression ที่เลือกได้บ่อย (ทยอยตาม usage) |
| 4.4 | สรุปผล automation vs manual บน Dashboard (แยก source ของ execution) |

**Exit Criteria:** flow "Build ใหม่ → analyze → run → write-back → defect" ทำงาน end-to-end โดยไม่กรอกมือ

### Phase 5 — Hardening (ต่อเนื่อง)

| # | งาน | เกณฑ์ |
|---|---|---|
| 5.1 | จัดการ flaky test (retry policy, quarantine list, root cause) | flaky rate <5% |
| 5.2 | Parallel/distributed execution (แยก machine ต่อ environment) | runtime ลดตามเป้า |
| 5.3 | Review KPI ราย release (coverage, pass rate, maintenance hours) | maintenance <20% ของเวลาทั้งหมด |

---

## 6. แผนรองรับการเข้ารหัสไฟล์ (Encryption Readiness Plan)

> สมมติฐาน: อนาคตทีม POS จะ **obfuscate/encrypt assembly + resource** และอาจเข้ารหัส `config/*.ini`, `DB/FBMAXX2.FDB`

### 6.1 หลักการ — ทำไม encryption ไม่ฆ่า UI Automation

Selector ของ FlaUI/UIA ไม่ได้อ่านจากไฟล์ แต่ได้จาก **UI Automation Tree ที่ WPF ปล่อยตอน runtime** ต่อให้ DLL/XAML ถูก encrypt พอ app decrypt ใน memory แล้ว render → control ยัง expose `AutomationId`/`Name`/`ControlType` เหมือนเดิม สิ่งที่เสียคือ decompile/reflection — ซึ่ง automation เราไม่ใช้

### 6.2 ความเสี่ยงจริงและการป้องกัน

| ความเสี่ยง | ผลกระทบ | ป้องกัน |
|---|---|---|
| Obfuscator rename class/method | ไม่กระทบ (automation ไม่อ้างชื่อ type) | — |
| Obfuscator **เข้ารหัส string literal** | ⚠️ `AutomationId` อาจเพี้ยนถ้าเก็บเป็น resource/string | Contract §6.3-ก: exclude AutomationId จาก string encryption |
| Dev ไม่ตั้ง `AutomationProperties.AutomationId` (derive จากชื่อ) | ⚠️ ชื่อถูก rename → selector พัง | Contract §6.3-ข: ตั้ง ID ชัดเจนใน XAML เสมอ |
| Encrypt `system.ini` / `FDB` | ⚠️ AppLauncher/TestDataManager แก้ไฟล์ไม่ได้ | Test Data Channel §6.5 |
| Startup ช้าลงจาก decryption | Timeout หลวมๆ | เผื่อ timeout ×2 บน encrypted build |

### 6.3 Selector Stability Contract (ตกลงกับทีม Dev Promaxx2 (App + Pos) — ผูกกับ Phase 0)

- ก) ทุก control ที่ automation ใช้ ต้องมี `AutomationProperties.AutomationId` literal ชัดเจน และ **obfuscation config ต้อง exclude** ID เหล่านี้จาก renaming และ string encryption
- ข) ห้าม derive AutomationId จากชื่อ class/property ที่ obfuscate ได้
- ค) เปลี่ยน AutomationId = breaking change → ต้องแจ้ง QA ล่วงหน้าใน release note
- ง) ส่ง build ที่ obfuscate แล้วให้ QA ทดสอบ automation **ตั้งแต่ release แรกที่เริ่มใช้** (ไม่รอตอน ship จริง)

### 6.4 Automation Compatibility Check (ผูกกับ Phase 3.4)

ทุก release ก่อน sign-off:

1. รับ build ที่ผ่าน obfuscation/encryption แล้ว
2. รัน **Smoke Pack** (ชุดเดียวกับ Phase 1) บน build นั้น
3. ตรวจ checklist:
   - [ ] Launch สำเร็จ, UIA tree มี control ตาม PageObject ครบ
   - [ ] AutomationId ตรง contract (diff กับ baseline ของ release ก่อน)
   - [ ] Runtime ไม่เกิน 2 เท่าของ unencrypted build
   - [ ] Evidence (screenshot/log) บันทึกได้ปกติ
4. Fail รายการใด → ค้าง release จนทีม Dev แก้ obfuscation config

### 6.5 Test Data Channel สำหรับไฟล์ที่เข้ารหัส

เมื่อ `ini`/`FDB` ถูกเข้ารหัส TestDataManager ใช้สิทธิ์เขียนไฟล์ตรงไม่ได้อีก เลือก 1 ใน 3 ทาง (ตกลงกับ Dev Promaxx2):

| ทางเลือก | วิธี | เหมาะเมื่อ |
|---|---|---|
| A. Seed Tool จากทีม Dev | Dev ส่ง CLI/API ที่รับ "scenario id" แล้วเขียนข้อมูลลง FDB ที่ encrypt เอง | มีงบ coordinate กับ Dev |
| B. Environment Lab ไม่เข้ารหัส | Lab ใช้ build ปกติ + ไฟล์เปล่า, Production-like เท่านั้นที่ encrypt | ต้องการความเร็ว, ยอมรับ env ต่างกัน |
| C. Snapshot ล่วงหน้า | ให้ Dev สร้าง FDB snapshot ที่ seed แล้วต่อ scenario → runner แค่ restore ไฟล์ | ชุดข้อมูลคงที่ ไม่บ่อย |

> ตัดสินใจก่อนเข้า Phase 2 สำคัญ เพราะกระทบ design ของ TestDataManager

---

## 7. KPIs

| KPI | เป้า |
|---|---|
| Automation coverage (case Ready ที่ automate แล้ว / AutomationCandidate ทั้งหมด) | ≥60% ภายใน 6 เดือน |
| Smoke pack stability (ผ่านเมื่อ app ไม่พังจริง) | ≥98% |
| Flaky rate | <5% |
| Lead time "Build ใหม่ → ผล regression" | <24 ชม. |
| Maintenance time / total automation time | <20% |

## 8. Risks & Mitigations (ภาพรวม)

| Risk | ผลกระทบ | Mitigation |
|---|---|---|
| UI ของ POS เปลี่ยนบ่อย | PageObject พังทั้งชุด | Selector Contract + review UI change ก่อน merge |
| UIA บาง custom control ไม่ expose | เขียน automation ไม่ได้ | แจ้ง Dev เพิ่ม AutomationPeer/Id ตั้งแต่ Phase 0 |
| Runner ไม่มี interactive desktop (CI) | UIA รันไม่ได้ | ใช้ self-hosted runner แบบมี session / VM เฉพาะ |
| Encryption timeline เปลี่ยน | แผน §6 ล้าสมัย | Review §6 ทุก release retrospective |
| ทีม QA มีเวลาจำกัด | Phase ล่าช้า | ตัด scope Phase 4.3 — ใช้ manual ต่อใน case ที่ automate ไม่คุ้ม |

## 9. Appendix

### 9.1 ตัวอย่าง `testplan.json`

```json
{
  "build": { "releaseCode": "REL-001", "buildNumber": "1.0.0-beta.2" },
  "source": "qahub",
  "cases": [
    {
      "testCaseCode": "TC-SALE-001",
      "targetApp": "pos",
      "title": "ขายสินค้าสด จ่ายเงินสด",
      "priority": "P0",
      "module": "Transaction",
      "prerequisites": ["TC-PROD-001"],
      "steps": [
        { "stepNo": 1, "action": "เปิดหน้าขาย", "data": "", "expected": "หน้าขายแสดง" },
        { "stepNo": 2, "action": "สแกนสินค้า", "data": "SKU=1001", "expected": "รายการเพิ่ม 1 แถว" }
      ]
    },
    {
      "testCaseCode": "TC-PROD-001",
      "targetApp": "app",
      "title": "สร้างสินค้าใหม่ 1 รายการ",
      "priority": "P0",
      "module": "Inventory",
      "prerequisites": [],
      "steps": [
        { "stepNo": 1, "action": "เปิดหน้าสินค้า", "data": "", "expected": "ลิสต์สินค้าแสดง" }
      ]
    }
  ]
}
```

> `targetApp`: `"app"` = `Promaxxs.App.exe` (Master Data) / `"pos"` = `PromaxxsPos.exe` (บิลขาย); `prerequisites` = case master data ที่ต้องผ่านก่อน

### 9.2 โครงสร้าง solution (FlaUI)

```
Promaxx2.Automation/
├── Promaxx2.Automation.Core/        # Config, Logger, Screenshot
├── Promaxx2.Automation.Hub/         # QA Hub API client (export/write-back)
├── Promaxx2.Automation.Data/        # FDB snapshot/restore + ini switch
├── Promaxx2.Automation.Pages.App/   # PageObjects ของ Promaxxs.App.exe (Master Data)
├── Promaxx2.Automation.Pages.Pos/   # PageObjects ของ PromaxxsPos.exe (บิลขาย)
├── Promaxx2.Automation.Tests/       # NUnit + FlaUI (smoke/regression)
└── Promaxx2.Automation.Runner/      # CLI entry point
```
