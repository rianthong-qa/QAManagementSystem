# Promaxx2.Automation — Windows Runner / Test Automation Worker

Worker ที่รัน **UI Automation** จริงบนเครื่อง Windows (เปิด `PromaxxsPos.exe` / `Promaxxs.App.exe` ด้วย FlaUI)
และเชื่อมต่อกับ **ProMaxx2 QA Hub** เพื่อรับงานจากหน้า Automation (Trigger & Queue) แล้วส่งผลกลับ (Pass/Fail + Evidence).

> ฝั่ง QA Hub เป็นตัวสั่งงาน (queue) เท่านั้น — **ต้องมี Runner บนเครื่อง Windows** ถึงจะรัน UI test ได้จริง

## ส่วนประกอบ (solution `Promaxx2.Automation.slnx`)

| Project | หน้าที่ |
|---|---|
| `Promaxx2.Automation.Runner` | CLI entry (คำสั่ง `worker`, `export`, `run`, `gate`, `scan`, `inspect`, `whoami`) |
| `Promaxx2.Automation.Core` | AppConfig, AppLauncher, AutomationId Scanner / Quality Gate |
| `Promaxx2.Automation.Hub` | QaHubClient (REST ไปยัง QA Hub API) |
| `Promaxx2.Automation.Data` | Firebird (FDB) access |
| `Promaxx2.Automation.Pages.Pos` / `.App` | Page objects สำหรับ POS / Master Data |
| `tests/Promaxx2.Automation.Tests` | Automation test cases (LoginSmoke ฯลฯ) |

---

## 1. สิ่งที่ต้องเตรียม (Prerequisites)

- **Windows 10/11** (UI Automation ต้องเปิดเดสก์ท็อปจริง ใน interactive session)
- **.NET 10 SDK** (สำหรับ build) หรือ .NET 10 Runtime (ถ้าใช้ exe ที่ publish แล้ว)
- **AUT binaries**: `PromaxxsPos.exe` (POS/งานขาย) และ `Promaxxs.App.exe` (Master Data) + `FBMAXX2.FDB` (Firebird, ถ้าใช้)
- บัญชี **QA Hub** ที่มี permission `EXECUTION.RUN` และถูก assign ไปยัง Project ที่จะรัน

## 2. Environment Variables

อ่านจาก **User environment variables** เท่านั้น (ห้ามใส่ credential ใน source/config)

| ตัวแปร | บังคับ | คำอธิบาย |
|---|---|---|
| `QAHUB_BASE_URL` | ใช่ | Base URL ของ QA Hub API เช่น `http://localhost:5038/api/v1/` |
| `QAHUB_USERNAME` | ใช่ | Username ของบัญชี QA Hub |
| `QAHUB_PASSWORD` | ใช่ | Password ของบัญชี QA Hub |
| `AUT_POS_EXE` | ใช่ | Full path ของ `PromaxxsPos.exe` |
| `AUT_APP_EXE` | ใช่ | Full path ของ `Promaxxs.App.exe` |
| `AUT_FDB_PATH` | ไม่ | Full path ของ `FBMAXX2.FDB` |
| `AUT_POS_USERNAME` / `AUT_POS_PASSWORD` | ไม่ | บัญชีสำหรับ Positive Login Smoke ของ POS |
| `AUT_APP_USERNAME` / `AUT_APP_PASSWORD` | ไม่ | บัญชีสำหรับ Positive Login Smoke ของ App |

> ใช้สคริปต์ `.\set-runner-env.ps1` ช่วยตั้งค่าได้ (ตั้ง scope = User)

## 3. Build / Publish

```powershell
# รันผ่าน dotnet (ต้องมี SDK บนเครื่อง)
dotnet run --project src\Promaxx2.Automation.Runner -- worker --project <ProjectGUID> --targets pos,app

# หรือ publish เป็น single-file exe (ไม่ต้องมี SDK ตอนรัน)
.\publish-worker.ps1
# ผลลัพธ์: .\publish\Promaxx2.Automation.Runner.exe
```

## 4. ลงทะเบียนเป็น Worker อัตโนมัติ (Scheduled Task)

```
.\install-worker-task.ps1 -ProjectId <ProjectGUID> -Targets pos,app
```
จะลง Scheduled Task ชื่อ `Promaxx2 QA Automation Worker` ซึ่งรันตอนผู้ใช้ Logon (interactive session) —
**ต้องตั้ง env vars (ข้อ 2) ก่อน** สคริปต์จะตรวจแล้ว throw ถ้าขาด `QAHUB_USERNAME`, `QAHUB_PASSWORD`, `AUT_POS_EXE`, `AUT_APP_EXE`

## 4b. รันเป็นโปรแกรมแยกบนเครื่อง (Background)

ถ้าต้องการรันเป็น **โปรแกรมแยกบนเครื่อง** (ไม่ต้องเปิดหน้าต่างค้างไว้ / รัน background) ให้ใช้:

```powershell
# publish ครั้งแรก + รัน exe แบบ minimized (แยกหน้าต่าง)
.\run-worker.ps1 -ProjectId <ProjectGUID> -Targets pos,app

# หรือให้หาค่า Project GUID อัตโนมัติจากบัญชีที่ login (ใช้ ProjectCode แทน)
.\run-worker.ps1 -ProjectCode PMX2 -Targets pos,app
```

- ตัว script จะ **publish** (ถ้า `publish\Promaxx2.Automation.Runner.exe` ยังไม่มี) แล้ว `Start-Process` รัน **minimized** เป็นโปรแกรมแยก
- ถ้าใช้ `-ProjectCode` สคริปต์จะเรียกคำสั่ง `projects --code <code>` เพื่อ resolve เป็น GUID เอง (ต้องตั้ง `QAHUB_USERNAME`/`QAHUB_PASSWORD` ก่อน)
- เขียน log ลงไฟล์ (default `queue-work\worker.log`) ผ่าน option `--log` — ดูว่า worker ทำอะไรได้จากไฟล์นี้
- ไปหน้า **Automation → Windows Runner Agents** เพื่อยืนยันว่าขึ้น **Online**

ดู Project ที่บัญชี login ได้โดยตรง:

```powershell
.\publish\Promaxx2.Automation.Runner.exe projects        # รายชื่อ Project (GUID · Code · ชื่อ)
.\publish\Promaxx2.Automation.Runner.exe projects --code PMX2   # คืน GUID ของ ProjectCode
```

> # สคริปต์ / คำสั่งทั้งหมด
> | สคริปต์ | หน้าที่ |
> |---|---|
> | `set-runner-env.ps1` | ตั้ง env vars (User scope) |
> | `publish-worker.ps1` | build single-file exe → `publish\` |
> | `run-worker.ps1` | publish (ถ้าไม่มี) + รัน exe แบบ minimized (โปรแกรมแยก) |
> | `install-worker-task.ps1` | ลง Scheduled Task รันตอน Logon (interactive) |

## 5. ยืนยันผลใน QA Hub

1. เปิดหน้า **Automation → Windows Runner Agents** → เห็น Runner ขึ้นเป็น **Online** (heartbeat ทุก ~10–20 วิ)
2. หน้า **Automation Trigger & Queue** → กด **▶ รันทันที** → Runner จะรับงาน (`Claimed` → `Running` → `Completed/Failed`)
3. ผลรันไปแสดงที่ **Run History** และเขียนกลับ Test Cycle ได้ (Write-back)

### คำสั่งอื่น ๆ ของ CLI

```powershell
.\Promaxx2.Automation.Runner.exe whoami                 # ตรวจ login + project ที่ได้
.\Promaxx2.Automation.Runner.exe export --project x --app pos --out testplan.json
.\Promaxx2.Automation.Runner.exe run --plan testplan.json --target-app pos --out run-results.json
.\Promaxx2.Automation.Runner.exe gate --baseline b.json --current c.json --policy quality-gate-policy.json
.\Promaxx2.Automation.Runner.exe inspect --exe "C:\path\PromaxxsPos.exe" --out uia-tree.json
```

---

## หมายเหตุความปลอดภัย

- เก็บ credential ผ่าน environment variable เท่านั้น — ห้าม commit ลง source/config
- `QaHubClient` ส่ง token ผ่าน header `Authorization: Bearer` (ไม่อยู่ใน URL)
- Evidence อัปโหลดผ่าน endpoint ที่ต้อง authenticate
