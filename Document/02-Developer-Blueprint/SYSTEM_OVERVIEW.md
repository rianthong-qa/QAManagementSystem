# ProMaxx2 QA Hub — System Overview

> ## ⚠️ อ่านก่อนเริ่มงานทุกครั้ง (MANDATORY FIRST READ)
>
> **Agent หรือ Developer ต้องอ่านไฟล์นี้ทั้งไฟล์ก่อนเริ่มทำงานใด ๆ ใน repository นี้**
> เพื่อให้เข้าใจโครงสร้างระบบ วิธี build/run และข้อจำกัดของ environment ก่อนแก้ไขโค้ด
> งาน UI ต้องอ่าน `UI_DESIGN_SYSTEM.md` เพิ่มเติมด้วย (ดูกฎใน `AGENTS.md`)

---

## 1. ระบบคืออะไร

**ProMaxx2 QA Hub** = ระบบบริหารจัดการ Quality Assurance (QA Management System) ภาษาไทย
ครอบคลุม lifecycle ทั้งหมด: Project/Module → Release/Build → Requirement → Test Design (Case/Suite/RTM) → Execution (Cycle/Workspace) → Defect/Regression → Governance (Summary/Risk/Sign-off)

## 2. Architecture & Tech Stack

| Layer | Technology | Notes |
|---|---|---|
| Frontend | React 19 + TypeScript + Vite | SPA หลักอยู่ในไฟล์เดียว `src/ProMaxx2.QA.Web/src/App.tsx` (~6,600+ บรรทัด) + `styles.css` |
| Backend | ASP.NET Core (.NET 10) Web API | Clean Architecture 4 projects |
| Database | SQL Server LocalDB (`(localdb)\mssqllocaldb`, DB `ProMaxx2QA`) | EF Core + Migrations |
| Auth | JWT Bearer (Issuer `ProMaxx2.QA`, expiry 60 นาที) | token เก็บ localStorage key `qa.accessToken`, ใช้ permission keys เช่น `PROJECT.VIEW`, `TESTCASE.VIEW`, `EXECUTION.RUN`, `DEFECT.EDIT` |
| AI | OpenAI-compatible API (`gpt-5-mini`) | ใช้ generate requirement / test case / test suite |

### โครงสร้าง Solution (`src/`)

```
ProMaxx2.QA.Domain          ← Entities: Projects, Releases, Requirements,
                              TestManagement, Execution, Defects, Governance,
                              Identity, Settings, Dashboard
ProMaxx2.QA.Application     ← Services/DTOs: Common, Dashboard, Execution,
                              Identity, Projects, Regression, Releases,
                              Requirements, TestManagement
ProMaxx2.QA.Infrastructure  ← EF Core (QaDbContext), Repositories, Migrations
ProMaxx2.QA.Api             ← Controllers, Program.cs, appsettings.json
ProMaxx2.QA.Web             ← Vite React frontend
```

## 3. Modules / Pages (18 หน้า)

| Group | Pages |
|---|---|
| ภาพรวม | Dashboard, Project/Module, Release/Build |
| Requirement & Test Design | Requirement, RTM, Test Case, Test Suite |
| Test Execution | Test Cycle, Execution Workspace, Defect, Regression |
| Release Governance | Test Summary, Risk Acceptance, Release Sign-off |
| Administration | User/Role, Setting Center, System Monitor, Audit Log |

## 4. Environment & Deployment (สำคัญมาก)

ระบบ deploy บน **Windows + Cloudflare Tunnel**:

```
Internet ─► qahub.store / promaxx2.qahub.store   ─► cloudflared ─► http://192.168.200.219:5173 (Vite dev)
Internet ─► api-promaxx2.qahub.store              ─► cloudflared ─► localhost:5038 (API บนเครื่องนี้)
```

- **Frontend dev**: Vite ที่ port **5173**, proxy `/api` → `https://api-promaxx2.qahub.store`
  (`.env.development`: `VITE_API_URL=https://api-promaxx2.qahub.store/api/v1`)
  → **แก้ backend แล้วต้อง restart API local ไม่งั้น domain สาธารณะยังเรียกของเก่า**
- **API**: ฟังที่ `http://0.0.0.0:5038` (launchSettings applicationUrl)
- **CORS AllowedOrigins**: `localhost:5173`, `192.168.200.219:5173`, `promaxx2.qahub.store`, `qahub.store`

### การ Run/Restart API (จาก WSL)

⚠️ ประสบการณ์จากงานจริง: process ที่รันอยู่จะ **ล็อก DLL ใน bin** (MSB3027) — ต้อง kill ก่อน rebuild

```bash
# 1) Kill API เดิม
taskkill.exe /IM ProMaxx2.QA.Api.exe /F

# 2) Build (dotnet ไม่อยู่ใน PATH ของ WSL — ใช้ full path)
"/mnt/c/Program Files/dotnet/dotnet.exe" build src/ProMaxx2.QA.Api --nologo

# 3) Start ใหม่ — ต้องตั้ง ASPNETCORE_URLS เอง ไม่งั้นจะไปฟัง port 5000 default!
powershell.exe -NoProfile -Command "\$env:ASPNETCORE_URLS='http://0.0.0.0:5038'; \$env:ASPNETCORE_ENVIRONMENT='Development'; Start-Process -FilePath 'H:\APP\QAManagementSystem\src\ProMaxx2.QA.Api\bin\Debug\net10.0\ProMaxx2.QA.Api.exe' -WorkingDirectory 'H:\APP\QAManagementSystem\src\ProMaxx2.QA.Api\bin\Debug\net10.0' -WindowStyle Hidden"

# 4) ยืนยันว่า route ใหม่ขึ้นจริง (401 = route มี, 404 = ยังเป็น build เก่า)
curl.exe -s -o NUL -w "%{http_code}" "http://localhost:5038/api/v1/dashboard/shared/nonexistent99"
```

## 5. Build / Check Commands

```bash
# Frontend (ใช้ npm.cmd ผ่าน interop; fallback เป็น npm ถ้า cmd ล่ม)
cd src/ProMaxx2.QA.Web
npm.cmd run build    # tsc -b && vite build
npm.cmd run lint     # oxlint

# Backend
"/mnt/c/Program Files/dotnet/dotnet.exe" build src/ProMaxx2.QA.Api --nologo

# ทุกครั้งหลังแก้ frontend
git diff --check     # ห้ามมี trailing whitespace
```

## 6. กฎการทำงาน (จาก AGENTS.md)

1. **งาน UI ทุกชนิด** ต้องอ่าน `UI_DESIGN_SYSTEM.md` ทั้งไฟล์ก่อน และใช้ design tokens/form/modal/responsive/a11y ของระบบ
2. UI ต้องตรวจ **Desktop + Mobile** ห้ามเกิด horizontal scroll ระดับหน้าโดยไม่จำเป็น
3. Pattern/กฎ UI ใหม่ → ต้อง update `UI_DESIGN_SYSTEM.md` + Change Log ในงานเดียวกัน
4. Requirement ผู้ใช้ล่าสุดชนะเอกสาร — แก้เอกสารให้ตรงผลลัพธ์ใหม่
5. **ห้าม commit เอง** เว้นแต่ผู้ใช้สั่ง

## 7. แผนที่เอกสาร (`Document/`)

| Path | เนื้อหา |
|---|---|
| `02-Developer-Blueprint/SYSTEM_OVERVIEW.md` | **ไฟล์นี้ — อ่านก่อนเสมอ** |
| `02-Developer-Blueprint/UI_DESIGN_SYSTEM.md` | กฎ UI + design tokens + Change Log |
| `02-Developer-Blueprint/API_SPECIFICATION.md` | สเปก API |
| `02-Developer-Blueprint/SCREEN_SPECIFICATION.md` | สเปกหน้าจอ |
| `02-Developer-Blueprint/SQL_SERVER_SCHEMA.md` | Schema ฐานข้อมูล |
| `01-System-Blueprint/` | REQUIREMENTS / DATABASE_DESIGN / WORKFLOW |
| `03-Architecture-and-Plan/` | แผนสถาปัตยกรรม |
| `05-Module/` | เอกสารรายโมดูล |

## 8. สถานะงานล่าสุด (2026-08-21)

1. **Dashboard Module Overview** — rollup ยอด Test Case จาก submodules ขึ้น parent + health badge/status bar + responsive
2. **Test Case page pagination fix** — หน้าเดิม fetch ข้อมูลไม่ครบ (server-paginated 20 แถว); แก้เป็น server-side filter/pagination + debounce search
3. **Executive Timeline บน share link** — เพิ่ม endpoint `[AllowAnonymous] GET /dashboard/shared/{code}/timeline` และ `GET /dashboard/shared/timeline?token=` + frontend ดึง timeline ใน share mode; บั๊กที่พบ: ลืม controller endpoint + API รัน build เก่า (ต้อง restart ตาม §4)
