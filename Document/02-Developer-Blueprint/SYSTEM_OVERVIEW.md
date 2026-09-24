# ProMaxx2 QA Hub — System Overview

> ## ⚠️ อ่านก่อนเริ่มงานทุกครั้ง (MANDATORY FIRST READ)
>
> **Agent หรือ Developer ต้องอ่านไฟล์นี้ทั้งไฟล์ก่อนเริ่มทำงานใด ๆ ใน repository นี้**
> เพื่อให้เข้าใจโครงสร้างระบบ วิธี build/run และข้อจำกัดของ environment ก่อนแก้ไขโค้ด
> งาน UI ต้องอ่าน `UI_DESIGN_SYSTEM.md` เพิ่มเติม และงาน Automation ต้องอ่าน `AUTOMATION_TODO.md` (ดูกฎใน `AGENTS.md`)
>
> อัปเดตล่าสุด: 23 กันยายน 2026

---

## 1. ระบบคืออะไร

**ProMaxx2 QA Hub** = ระบบบริหารจัดการ Quality Assurance (QA Management System) ภาษาไทย
ครอบคลุม lifecycle ทั้งหมด: Project/Module → Release/Build → Requirement → Test Design (Case/Suite/RTM) → Execution (Cycle/Workspace/My Work) → Defect/Regression/Automation → Governance (Summary/Risk/Sign-off)

## 2. Architecture & Tech Stack

| Layer | Technology | Notes |
|---|---|---|
| Frontend | React 19 + TypeScript + Vite | SPA หลัก `src/ProMaxx2.QA.Web/src/App.tsx` (~10,200 บรรทัด) + ไฟล์แยกบางหน้า (ดู §2.2) |
| Backend | ASP.NET Core (.NET 10) Web API | Clean Architecture 4 projects |
| Database | SQL Server `localhost\MSSQLSERVER2022`, DB `ProMaxx2QA` (Windows auth) | EF Core + Migrations; `Database:ApplyMigrations=false` — apply migration ด้วยมือ |
| Auth | JWT Bearer (Issuer `ProMaxx2.QA`, อายุ 24 ชม., Remember Me 30 วัน) | token ใน localStorage `qa.accessToken`; permission claims เช่น `PROJECT.VIEW`, `DEFECT.EDIT`, `AUTOMATION.EXECUTE` |
| Project access | `ProjectAccessFilter` + `ProjectScopeGuard` | ผู้ใช้เห็นเฉพาะ Project ที่เป็นสมาชิก (`ProjectUsers`) — ตรวจ projectId ใน route/query, `ProjectId` ใน request body และ route id (`releaseId`, `cycleId`, `cycleCaseId`, `defectId`, `testCaseId`, `requirementId`, `buildId`) |
| AI | OpenAI-compatible (ตั้งค่าใน Setting Center) | generate requirement / test case / test suite / automation DSL |
| Integration | CRM (BlueSea/BlueID), Email | `CrmSyncWorker` sync สถานะทุก 2 นาที |
| Automation | Windows Agent (`agent/`) + FlaUI | รับงานจาก Hub, รัน DSL, ส่ง Evidence |

### 2.1 โครงสร้าง Solution

```
src/
  ProMaxx2.QA.Domain          ← Entities: Projects, Releases, Requirements, TestManagement,
                                Execution, Defects, Governance, Identity, Settings, Dashboard, Automation
  ProMaxx2.QA.Application     ← Services/DTOs: Common, Dashboard, Execution, Identity, Projects,
                                Regression, Releases, Requirements, TestManagement, Automation
  ProMaxx2.QA.Infrastructure  ← EF Core (QaDbContext), Repositories, Migrations
  ProMaxx2.QA.Api             ← Controllers, Services (filters, workers, CRM, AI), Program.cs
  ProMaxx2.QA.Web             ← Vite React frontend
tests/ProMaxx2.QA.UnitTests   ← xUnit (EF InMemory + ToQueryString translation tests)
agent/                        ← ProMaxx2.Automation.slnx: Core, Hub, Runner, AgentGui (Capture Companion), Core.Tests
tools/ProMaxx2.ServiceManager ← WinForms: Start/Stop/Restart API + Web, log, CPU/RAM, Automation worker toggle
```

### 2.2 ไฟล์ Frontend หลัก

| ไฟล์ | เนื้อหา |
|---|---|
| `App.tsx` | App shell + หน้าส่วนใหญ่ (Dashboard, Test Case/Suite/Cycle, Execution Workspace, Defect, Test Summary ฯลฯ) |
| `AutomationPage.tsx` + `automation/*` + `Automation.css` | หน้า Automation: `AutomationPage.tsx` (หน้าหลัก/แท็บ Cases, ~1,300 บรรทัด) + `automation/` แยกตามแท็บ — `types.ts`, `shared.ts` (`fetchJson`, `useDebounced`, `useBuildsAndEnvironments`), `ui.tsx` (`ModalShell`, `Badge`, `Pager`), `caseModals`, `manageTabs`, `executionTabs`, `suiteTab`, `scheduleTabs` (Schedule/Build Trigger/Webhook), `dataTabs` (Snapshot/Seed/Data Profile) |
| `AuditLogPage.tsx` | หน้า Audit Log |
| `ExecutionDefectEditor.tsx` | Modal สร้าง/แก้ Defect + แนบรูปจาก Execution Workspace |
| `DefectModulePdf.ts` | Export PDF A4 อันดับ Defect รายโมดูล (jspdf + html2canvas โหลดแบบ dynamic import) |
| `styles.css`, `ExecutionWorkspace.css`, `TestManagement.css`, `TestSummary.css` ฯลฯ | stylesheet ตาม `UI_DESIGN_SYSTEM.md` |

### 2.3 Background workers

| Worker | หน้าที่ |
|---|---|
| `AutomationScheduleWorker` | poll ทุก 30 วินาที ยิง Automation Schedule ที่ถึงเวลา (เปิด/ปิดได้จาก Service Manager) |
| `CrmSyncWorker` | poll Defect ที่ Linked กับ CRM ทุก 2 นาที |
| `AutomationReaperWorker` | ทุก 1 นาที ปิด Automation Execution ที่ Agent เงียบเกิน 10 นาที (AgentLost) หรือรันเกิน 6 ชม. (Timeout) และปิด snapshot/restore/verification ที่ Agent ไม่รายงานผล (AUT-REL-002) |

## 3. Modules / Pages (20 หน้า)

| Group | Pages |
|---|---|
| ภาพรวม | Dashboard, My Work, Project/Module, Release/Build |
| Requirement & Test Design | Requirement, RTM, Test Case, Test Suite |
| Test Execution | Test Cycle, Execution Workspace, Defect, Regression, Automation |
| Release Governance | Test Summary, Risk Acceptance, Release Sign-off |
| Administration | User/Role, Setting Center, System Monitor, Audit Log |

## 4. Environment & Deployment (สำคัญมาก)

ระบบ deploy บน **Windows + Cloudflare Tunnel**:

```
Internet ─► qahub.store / promaxx2.qahub.store   ─► cloudflared ─► http://192.168.200.219:5173 (Vite dev)
Internet ─► api-promaxx2.qahub.store              ─► cloudflared ─► localhost:5038 (API บนเครื่องนี้)
```

- **Frontend dev**: Vite port **5173**, `.env.development`: `VITE_API_URL=https://api-promaxx2.qahub.store/api/v1`
  → **แก้ backend แล้วต้อง restart API local ไม่งั้น domain สาธารณะยังเรียกของเก่า**
- **API**: ฟังที่ `http://0.0.0.0:5038` และรันแบบ **Production** (launch profile `production`)
  - Production อ่าน connection string จาก `appsettings.Production.json` (Windows auth, ไม่มี secret)
  - **JWT signing key อยู่ใน User environment variable `Jwt__Key` เท่านั้น** — ห้าม commit key; Production จะไม่สตาร์ทถ้าไม่มี key
  - Swagger/OpenAPI เปิดเฉพาะ Development
- **CORS AllowedOrigins**: `localhost:5173`, `192.168.200.219:5173`, `promaxx2.qahub.store`, `qahub.store`
- traffic จาก Cloudflare Tunnel มาจาก loopback เสมอแต่มี header `CF-Connecting-IP` — endpoint ที่อนุญาตเฉพาะเครื่อง local (เช่น `automation/schedules/worker-status`) ต้องตรวจ header นี้ด้วย

### การ Run/Restart API

**วิธีหลัก: ใช้ Service Manager** (`.artifacts/service-manager-production/ProMaxx2.ServiceManager.exe`) กด Restart API
— ตัวนี้อ่าน `Jwt__Key` จาก User environment ใหม่ทุกครั้งและสตาร์ทด้วย profile `production` (ถ้าไม่พบ key จะ fallback เป็น Development)

**วิธีสำรอง (PowerShell):**

```powershell
# 1) หยุด API เดิม (process ที่รันอยู่ล็อก DLL ใน bin — MSB3027)
Get-Process ProMaxx2.QA.Api -ErrorAction SilentlyContinue | Stop-Process -Force

# 2) Start แบบ Production โดยส่ง key จาก User environment
$env:Jwt__Key = [Environment]::GetEnvironmentVariable('Jwt__Key','User')
Start-Process dotnet.exe -ArgumentList 'run','--project','H:\APP\QAManagementSystem\src\ProMaxx2.QA.Api\ProMaxx2.QA.Api.csproj','--launch-profile','production' -WindowStyle Hidden
Remove-Item Env:Jwt__Key

# 3) ยืนยัน: 200 = ขึ้นแล้ว, route ใหม่ต้องได้ 401 (ไม่ใช่ 404)
curl.exe -s -o NUL -w "%{http_code}" http://localhost:5038/health
```

- `Get-NetTCPConnection` ใช้ไม่ได้ในบาง shell ของเครื่องนี้ — ตรวจ port ด้วย `netstat -ano | Select-String ':5038 '`
- ถ้าต้อง build ขณะ API รันอยู่ ให้ build ไป output แยก: `dotnet build src/ProMaxx2.QA.Api -o <temp>` (ไม่ชน DLL lock)
- rotate JWT key: สร้าง key ใหม่ ≥ 32 bytes → `[Environment]::SetEnvironmentVariable('Jwt__Key',<key>,'User')` → restart API (token เดิมทุกใบจะใช้ไม่ได้)

## 5. Build / Check Commands

```powershell
# Frontend
cd src/ProMaxx2.QA.Web
npm.cmd run build                    # tsc -b && vite build
npm.cmd run lint                     # oxlint (ต้องไม่มี warning)
npx.cmd tsc --noEmit -p tsconfig.app.json
npm.cmd run test                     # vitest

# Backend
dotnet build src/ProMaxx2.QA.Api --nologo
dotnet test tests/ProMaxx2.QA.UnitTests --nologo

# Automation Agent (solution แยก)
dotnet test agent/ProMaxx2.Automation.slnx --nologo

# ทุกครั้งหลังแก้
git diff --check                     # ห้ามมี trailing whitespace
```

## 6. กฎการทำงาน (จาก AGENTS.md)

1. **งาน UI ทุกชนิด** ต้องอ่าน `UI_DESIGN_SYSTEM.md` ทั้งไฟล์ก่อน และใช้ design tokens/form/modal/responsive/a11y ของระบบ
2. UI ต้องตรวจ **Desktop + Mobile** ห้ามเกิด horizontal scroll ระดับหน้าโดยไม่จำเป็น
3. Pattern/กฎ UI ใหม่ → ต้อง update `UI_DESIGN_SYSTEM.md` + Change Log ในงานเดียวกัน
4. งาน Automation → อ่านและอัปเดต `AUTOMATION_TODO.md` ในงานเดียวกัน
5. Requirement ผู้ใช้ล่าสุดชนะเอกสาร — แก้เอกสารให้ตรงผลลัพธ์ใหม่
6. **ห้าม commit เอง** เว้นแต่ผู้ใช้สั่ง
7. Endpoint ใหม่: ต้องมี `[Authorize(Policy=...)]` (มี fallback policy บังคับ login อยู่แล้ว) และถ้าข้อมูลผูกกับ Project ต้องมี `[RequireProjectAccess]`; endpoint anonymous ต้องประกาศ `[AllowAnonymous]` พร้อมเหตุผล

## 7. แผนที่เอกสาร (`Document/`)

| Path | เนื้อหา |
|---|---|
| `02-Developer-Blueprint/SYSTEM_OVERVIEW.md` | **ไฟล์นี้ — อ่านก่อนเสมอ** |
| `02-Developer-Blueprint/UI_DESIGN_SYSTEM.md` | กฎ UI + design tokens + Change Log |
| `02-Developer-Blueprint/API_SPECIFICATION.md` | สเปก API |
| `02-Developer-Blueprint/SCREEN_SPECIFICATION.md` | สเปกหน้าจอ |
| `02-Developer-Blueprint/SQL_SERVER_SCHEMA.md` | Schema ฐานข้อมูล |
| `01-System-Blueprint/` | REQUIREMENTS / DATABASE_DESIGN / WORKFLOW |
| `03-Architecture-and-Plan/AUTOMATION_TODO.md` | **Automation Work Tracker (Single Source of Truth)** |
| `03-Architecture-and-Plan/AUTOMATION_PLAN.md`, `SELECTOR_CONTRACT.md`, `AUTOMATIONID_IMPLEMENTATION_GUIDE.md` | แผน Automation Phase 0–5, Selector Contract, คู่มือใส่ AutomationId |
| `03-Architecture-and-Plan/CRM_INTEGRATION_PLAN.md`, `CRM_DEFECT_KANBAN_PLAN.md` | การเชื่อม CRM และแผน Defect Kanban (ยังเป็นแผน) |
| `03-Architecture-and-Plan/WEIGHTED_AUTO_ASSIGN.md` | Weighted Auto Assignment |
| `05-Module/` | เอกสารรายโมดูล |

## 8. สถานะงานล่าสุด (2026-09-23)

1. **System hardening (ตรวจทั้งระบบ)**
   - ปิด IDOR: filter ตรวจ `ProjectId` ใน body และ route id ข้าม Project; Projects/Modules/Test Suite/Defect unlink ตรวจ Project ของ record
   - Dashboard share ต้องผูก Project ที่ผู้สร้างมีสิทธิ์ (ลิงก์แบบทุก Project ถูกปฏิเสธ)
   - `worker-status` ไม่เปิด anonymous จาก internet แล้ว; fallback policy บังคับ login; rate limit login/webhook; header `nosniff`
   - ลบคอมเมนต์ Defect ได้เฉพาะเจ้าของ/SYS_ADMIN และบันทึก activity
   - API สาธารณะเปลี่ยนเป็น Production + JWT key ใหม่จาก User environment
   - Execution Workspace: บันทึกผลแล้วไม่เด้งกลับเคสแรก; แก้ Defect แล้ว assignee ไม่หาย
2. **ก่อนหน้า (ก.ย. 2026)**: Execution Workspace inline Defect + แนบรูป, Defect module ranking + PDF, Audit Log, Test Cycle clone lineage, Regression AUT-REG-001/002, Automation Suite versioning, CRM integration
3. **ค้าง/ต่อไป**: ดู `AUTOMATION_TODO.md` (AUT-REG-003+, AUT-CAP-006, AUT-P2-005+) และ Phase 4 ของการตรวจระบบ: แยก App.tsx เป็นไฟล์รายหน้า, token แบบ httpOnly cookie + refresh, credential เฉพาะ Agent, CI
