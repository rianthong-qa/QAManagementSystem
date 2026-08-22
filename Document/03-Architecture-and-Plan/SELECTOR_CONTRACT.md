# Promaxx2 — SELECTOR CONTRACT (Automation ↔ Development)

> **สถานะ: DRAFT — รอทีม Dev Promaxx2 ยืนยัน**
> เอกสารนี้เป็นข้อตกลงผูกพันระหว่างทีม Automation (QA) และทีมพัฒนา `Promaxxs.App.exe` / `PromaxxsPos.exe`
> อ้างอิง: `AUTOMATION_PLAN.md` §4 และ §6 | เวอร์ชัน 1.0 | วันที่ 2026-08-22

---

## 1. เป้าหมายของ Contract

ให้ Test Automation (FlaUI/UIA3) ควบคุมและตรวจสอบ UI ได้**เสถียร** โดยไม่พังจาก:
- การ rename code/obfuscation
- การเข้ารหัสไฟล์ assembly/resource ในอนาคต
- การเปลี่ยน text บนหน้าจอ (label ภาษาไทย/Eng)

## 2. กติกาฝั่ง Development

| # | กติกา |
|---|---|
| D1 | Control ที่ automation ใช้ (ปุ่ม, input, grid, tab, dialog สำคัญ) ต้องมี **`AutomationProperties.AutomationId`** กำกับชัดเจนใน XAML เสมอ — ค่าเป็น literal string ที่อ่านรู้เรื่อง เช่น `"Pos_SaleBtn"` |
| D2 | **ห้าม** derive AutomationId จากชื่อ class/property/method ที่ obfuscator rename ได้ |
| D3 | Obfuscation config ต้อง **exclude**: (ก) ทุก string ที่ใช้เป็น AutomationId, (ข) attribute `AutomationProperties.AutomationId*` ไม่ถูก strip |
| D4 | การ**เปลี่ยน/ลบ AutomationId ที่มีอยู่ = Breaking Change** → ต้องแจ้ง QA ล่วงหน้าใน release note + sync รายการที่กระทบ |
| D5 | Custom control ที่ automation ต้องอ่านค่า (grid/list) ต้อง expose ผ่าน UIA peer ให้เห็น rows/cells หรือให้ `Name` ที่ระบุแถวชัดเจน |

## 3. กติกาฝั่ง Automation

| # | กติกา |
|---|---|
| A1 | Selector ต้องใช้ `AutomationId` เป็นหลัก — ห้ามพึ่ง label text, coordinate, index ลำดับ control เป็นตัวหลัก |
| A2 | Automation test id = **TestCaseCode ของ QA Hub ตรงตัว** (เช่น `TC-SALE-001`) — trace กลับไป-มาโดยไม่ต้องแปล |
| A3 | Selector รวมศูนย์ใน PageObjects (`Pages.App.*` / `Pages.Pos.*`) — ห้าม query element กระจายใน test method |
| A4 | ห้าม hardcode path/credential ใน script — ใช้ env vars ตาม `AppConfig` |
| A5 | Route ตามประเภทงานเสมอ: POS/งานขายใช้ `PromaxxsPos.exe` (`targetApp: pos`); Master Data ใช้ `Promaxxs.App.exe` (`targetApp: app`) |

## 4. Registry ของ AutomationId (เติมระหว่างทาง)

ทั้งสองฝ่ายใช้ตารางนี้เป็น single source of truth — Dev ประกาศ ID ตอน implement หน้าจอ / QA อ้างอิงตอนเขียน PageObject

| App | Screen | Control | AutomationId | ตั้งแต่ Build |
|---|---|---|---|---|
| Pos | Login | login overlay | `LoginOverlay` | 1.0.0-beta.2 (runtime verified) |
| Pos | Login | employee ID field | `TxtEmpId` | 1.0.0-beta.2 (runtime verified) |
| Pos | Login | password field | `PwdBox` | 1.0.0-beta.2 (runtime verified) |
| Pos | Login | sign-in button | `BtnSignIn` | 1.0.0-beta.2 (runtime verified) |
| Pos | Login | result/error toast | `ToastText` | 1.0.0-beta.2 (runtime verified) |
| Pos | Sale | ปุ่มชำระเงิน | _(รอ Dev ยืนยัน)_ | — |
| App | Login | login panel | `LoginPanel` | 1.0.0-beta.2 (runtime verified) |
| App | Login | username field | `TxtUsername` | 1.0.0-beta.2 (runtime verified) |
| App | Login | password field | `PwdBox` | 1.0.0-beta.2 (runtime verified) |
| App | Login | sign-in button | `BtnSignIn` | 1.0.0-beta.2 (runtime verified) |
| App | Login | result/error toast | `ToastText` | 1.0.0-beta.2 (runtime verified) |
| App | Product list | ปุ่มเพิ่มสินค้า | _(รอ Dev ยืนยัน)_ | — |

## 5. การตรวจสอบความถูกต้อง (Verification)

1. **Automation Compatibility Check ทุก release** (AUTOMATION_PLAN.md §6.4): รัน Smoke Pack บน build ที่ obfuscate/encrypt แล้ว
2. Diff รายการ AutomationId ระหว่าง build → ID ที่หาย/เปลี่ยนต้องมี release note รองรับ
3. Runtime บน encrypted build ≤ 2× unencrypted build

### 5.1 Automated Runtime Scanner

ใช้ navigation manifest แยกตามโปรแกรมที่ `Automation/Promaxx2.Automation/examples/scanner.pos.json` และ `scanner.app.json` โดยเพิ่ม screen และขั้นตอน click เฉพาะเมนูที่ปลอดภัย เช่น:

```json
{
  "name": "Product List",
  "navigation": [
    { "automationId": "MasterMenu", "waitForId": "ProductMenu" },
    { "automationId": "ProductMenu", "waitForId": "ProductGrid" }
  ]
}
```

รัน scanner และเปรียบเทียบ baseline:

```powershell
dotnet run --project Automation/Promaxx2.Automation/src/Promaxx2.Automation.Runner -- scan `
  --manifest Automation/Promaxx2.Automation/examples/scanner.app.json `
  --baseline Automation/Promaxx2.Automation/artifacts/baseline/app-report.json `
  --out Automation/Promaxx2.Automation/artifacts/app-report.json `
  --registry Automation/Promaxx2.Automation/artifacts/app-registry.md
```

Exit code `0` หมายถึงไม่พบ actionable control ที่ ID ว่าง/ซ้ำและไม่มี ID หายจาก baseline; exit code `2` หมายถึง contract finding ที่ต้อง review ก่อนรับ build

### 5.2 Runtime Scan Baseline — 1.0.0-beta.2 (2026-08-22)

| App | Screens | Elements | Missing actionable ID | Duplicate ID | Report |
|---|---:|---:|---:|---:|---|
| Pos | 2 (Login, POS Home) | 171 | 42 | 0 | `artifacts/scanner-pos-report.json` |
| App | 9 (Login + top-level modules) | 1,104 | 108 | 20 | `artifacts/scanner-app-report.json` |

รายการ duplicate ที่พบใน App รวม `glyphIcon`, `BtnMinimize`, `BtnMaximize`, `BtnClose` และ `root`; รายละเอียด control ที่ ID ว่างและตำแหน่ง runtime ดูจาก report/registry ใน `Automation/Promaxx2.Automation/artifacts/` ผลนี้เป็น baseline finding ไม่ใช่การแก้ ID — ทีม Dev ต้องกำหนด ID ที่ unique ใน scope ที่ automation ใช้ แล้ว QA scan ซ้ำจน quality gate ผ่าน

### 5.3 Build Quality Gate

Baseline เริ่มต้นที่รอ QA/Dev อนุมัติของ `1.0.0-beta.2` อยู่ที่ `Automation/Promaxx2.Automation/baselines/1.0.0-beta.2/pos.json` และ `app.json` โดยใช้ policy `quality-gate-policy.json` ซึ่งอนุญาต technical debt เดิมชั่วคราว แต่ห้าม regression ใหม่

```powershell
Automation/Promaxx2.Automation/run-quality-gate.ps1 `
  -Build "1.0.0-beta.3" `
  -BaselineBuild "1.0.0-beta.2"
```

เมื่อเป็น Build ที่อยู่ใน QA Hub ให้ส่ง `-QaHubUrl`, `-AccessToken`, `-ProjectId`, `-ReleaseId` และ `-BuildId` เพิ่มเติม สคริปต์จะ publish ผลของ POS/App เข้าประวัติ Build อัตโนมัติ โดย QA Hub จะไม่อนุญาตให้ตั้ง Build เป็น `Passed` หรือ Release Candidate จนกว่าผลล่าสุดของทั้งสองโปรแกรมจะผ่าน

Quality Gate ต้อง fail เมื่อพบอย่างน้อยหนึ่งข้อ:

1. actionable control ที่ไม่มี AutomationId เพิ่มจาก baseline
2. AutomationId ซ้ำเพิ่มจาก baseline
3. AutomationId เดิมหาย โดยไม่ได้อยู่ใน `allowedRemoved`
4. ControlType/Class ของ ID เดิมเปลี่ยน โดยไม่ได้อยู่ใน `allowedChanged`

การเพิ่ม allowlist ต้องอ้าง release note หรือ change request ของทีม Dev และ review โดย QA Lead; ห้าม promote report ที่ fail เป็น baseline ใหม่เพื่อข้ามปัญหา

### 5.4 Windows Runner Queue

หลังตั้งค่า `QAHUB_BASE_URL`, `QAHUB_USERNAME`, `QAHUB_PASSWORD`, `AUT_POS_EXE`, `AUT_APP_EXE` และ credential ของ AUT ให้เปิด Worker ใน Windows interactive session:

```powershell
dotnet run --project Automation/Promaxx2.Automation/src/Promaxx2.Automation.Runner -- worker `
  --project "<project-guid>" `
  --targets "pos,app" `
  --poll 10
```

Worker จะรับเฉพาะ Ready Automation Candidate ที่กำหนด `AutomationTarget` ตรงกับ target ของงาน หากไม่มี case ที่เข้าเงื่อนไขจะปิดงานเป็น Failed โดยไม่เปิด AUT

## 6. การอนุมัติ

| ฝ่าย | ชื่อ | วันที่อนุมัติ |
|---|---|---|
| QA Lead | ___________ | ___________ |
| Dev Lead (Promaxx2) | ___________ | ___________ |

### Runner Agent และ Lease

Worker ส่ง heartbeat ทุก 20 วินาทีเพื่อยืนยัน interactive session และต่อ queue lease อีก 2 นาที หาก heartbeat หยุด QA Hub จะคืนงานเข้า Queue โดยไม่แก้ Selector Contract หรือ baseline ของ AUT

```powershell
.\Automation\Promaxx2.Automation\install-worker-task.ps1 -ProjectId "<project-guid>" -Targets "pos,app"
```

### Retry Classification

Runner ต้องส่ง `ErrorType` โดยไม่พึ่ง selector text: `Timeout`, `Infrastructure`, `ApplicationStart` retry ได้ ส่วน `Assertion` และ `Configuration` ต้องจบเป็น Failed เพื่อไม่ซ่อน regression จริงหรือ contract ที่ตั้งค่าผิด
