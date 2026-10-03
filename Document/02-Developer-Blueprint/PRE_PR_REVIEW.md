# Pre-PR Review — ขั้นตอนตรวจงานก่อนเปิด Pull Request

> ใช้กับทุกงานที่จะรวมเข้า `main` (คนหรือ Agent) — ทำครบก่อนกด "Create pull request"
> สคริปต์ตรวจอัตโนมัติ: `tools/pre-pr-check.ps1` · PR template: `.github/pull_request_template.md`
> อัปเดตล่าสุด: 4 ตุลาคม 2026

## ภาพรวม

```
1. เตรียม branch  →  2. ตรวจอัตโนมัติ (สคริปต์)  →  3. อ่าน diff ตัวเอง  →  4. ทดสอบจริง
        →  5. AI review (/code-review)  →  6. เขียนคำอธิบาย PR  →  เปิด PR  →  merge
```

| ระดับ | ความหมาย | ต้องทำ |
|---|---|---|
| **FAIL / Blocker** | build/test/lint ไม่ผ่าน, ข้อมูลลับหลุด, ไฟล์ต้องห้าม, ข้อมูลผิด/หาย, ช่องโหว่สิทธิ์ | แก้ก่อนเปิด PR — ห้ามข้าม |
| **WARN / Should fix** | warning, ไม่ได้อัปเดตเอกสารที่ควรอัปเดต, ไม่มี test ของ logic ใหม่ | แก้ หรือเขียนเหตุผลใน PR หัวข้อ "ความเสี่ยง/สิ่งที่ยังไม่ได้ทำ" |
| **Note / Nice to have** | ปรับชื่อ/รูปแบบ, refactor | ทำใน PR ถัดไปได้ |

## ขั้นที่ 1 — เตรียม branch

- ทำงานบน branch แยกเสมอ (`feat/...`, `fix/...`, `docs/...`) ห้าม commit ตรงเข้า `main`
- 1 PR = 1 เรื่อง — ถ้า diff ปนหลายเรื่อง (เช่น ฟีเจอร์ + refactor ใหญ่) ให้แยก PR
- `git fetch` แล้ว rebase/merge `main` ล่าสุดก่อนตรวจ เพื่อให้ผลตรวจตรงกับตอน merge จริง
- commit งานให้ครบ — สคริปต์และ PR ตรวจเฉพาะสิ่งที่ commit แล้ว

## ขั้นที่ 2 — ตรวจอัตโนมัติ

```powershell
powershell -ExecutionPolicy Bypass -File tools\pre-pr-check.ps1            # เทียบกับ main
powershell -ExecutionPolicy Bypass -File tools\pre-pr-check.ps1 -SkipBuild # แก้เอกสารล้วน
```

สคริปต์รันเฉพาะส่วนที่เกี่ยวกับไฟล์ที่เปลี่ยน และสรุปเป็น PASS / WARN / FAIL (มี FAIL = exit code 1):

| ตรวจ | เกณฑ์ |
|---|---|
| Branch / ไฟล์ค้าง | ไม่อยู่บน `main`; เตือนถ้ามีไฟล์ยังไม่ commit |
| ไฟล์ต้องห้าม | ห้ามมี `bin/`, `obj/`, `node_modules/`, `dist/`, `App_Data/`, `.env`, `.pfx/.p12/.key`; เตือนไฟล์ > 5 MB |
| Whitespace | `git diff --check` ต้องผ่าน (รวมไฟล์ใหม่) |
| ภาษาไทย | ห้ามมีข้อความไทยเสีย (mojibake) ในบรรทัดที่เพิ่ม |
| ข้อมูลลับ | ห้ามมี password / client secret / API key / JWT / private key / connection string ที่มีรหัสผ่าน ในบรรทัดที่เพิ่ม |
| Backend | `dotnet build` (output แยกใน `%TEMP%\qa-pre-pr` ไม่ชน DLL ที่ API รันอยู่) 0 error + `dotnet test` ผ่านทั้งหมด |
| Frontend | `npm run build`, `npm run lint` (ห้ามมี warning), `tsc -p tsconfig.app.json`, `npm run test` |
| Automation Agent | `dotnet test agent/ProMaxx2.Automation.slnx` เมื่อแก้ `agent/` |
| เอกสาร (AGENTS.md) | แก้ UI → เตือนถ้าไม่ได้อัปเดต `UI_DESIGN_SYSTEM.md`; งาน Automation → **FAIL** ถ้าไม่ได้อัปเดต `AUTOMATION_TODO.md` |
| Migration | เตือนเมื่อแก้ Domain/Infrastructure แต่ไม่มีไฟล์ใน `Migrations/` |

Log ของแต่ละขั้นอยู่ที่ `%TEMP%\qa-pre-pr\*.log`

## ขั้นที่ 3 — อ่าน diff ตัวเอง (self-review)

เปิด `git diff main...HEAD` (หรือแท็บ Files changed ของ draft PR) แล้วอ่าน **ทุกไฟล์** ตาม checklist:

### ทุกงาน
- [ ] ทุกบรรทัดที่เปลี่ยนเกี่ยวกับเรื่องของ PR นี้ (ไม่มีโค้ด debug, `console.log`, ไฟล์ชั่วคราว, โค้ดที่ comment ทิ้งไว้)
- [ ] กรณีพิเศษ: ข้อมูลว่าง/null, รายการยาวมาก, ผู้ใช้ไม่มีสิทธิ์, บริการภายนอกล่มหรือช้า
- [ ] ข้อความแจ้งผู้ใช้เป็นภาษาไทยที่เข้าใจได้ และไม่เปิดเผยข้อมูลภายใน (stack trace, credential)
- [ ] ชื่อตัวแปร/ฟังก์ชันสื่อความหมาย และ comment อธิบาย "ทำไม" ในจุดที่ไม่ชัด

### Backend (API / Service / DB)
- [ ] Endpoint ใหม่มี `[Authorize(Policy=...)]`; ข้อมูลผูก Project มี `[RequireProjectAccess]`; anonymous ต้องประกาศ `[AllowAnonymous]` พร้อมเหตุผล (SYSTEM_OVERVIEW §6)
- [ ] ไม่เชื่อค่าจาก client ในการกำหนดขอบเขตข้อมูล (projectId, userId, scope) — ตรวจซ้ำฝั่ง server
- [ ] เรียกบริการภายนอกมี timeout, จำกัดขนาดข้อมูล และ map error เป็นข้อความที่ผู้ใช้ทำต่อได้ (ไม่ให้เป็น 500)
- [ ] Background worker ไม่หยุดถาวรเมื่อเจอ error/timeout และไม่ log ซ้ำถี่จนท่วม
- [ ] ไม่มี `.Result` / `.Wait()`; ใช้ `CancellationToken` ต่อเนื่อง
- [ ] เปลี่ยน schema มี migration และเขียนวิธี apply ใน PR (`Database:ApplyMigrations=false`)
- [ ] วันที่/เวลา: เก็บ UTC แสดงเวลาไทย (Asia/Bangkok) ไม่ขึ้นกับ culture/timezone ของเครื่อง
- [ ] logic ใหม่มี unit test (โดยเฉพาะ parser, การคำนวณ, การตรวจสิทธิ์)

### Frontend / UI
- [ ] ทำตาม `UI_DESIGN_SYSTEM.md`: design token (ห้าม hex ซ้ำ token), ตัวอักษร ≥ 11px, modal ใช้ `ModalShell`, confirm/toast ใช้ `dialogStore`
- [ ] แยกสถานะ Loading / Error / Empty และยกเลิก request เก่า (AbortController, debounce ช่องค้นหา)
- [ ] 401 ที่ไม่ใช่ session หมดอายุต้องไม่ทำให้ผู้ใช้หลุด login
- [ ] Accessibility: `aria-label`/`label`, focus เห็นชัด, ใช้ได้ด้วยคีย์บอร์ด, ไม่สื่อด้วยสีอย่างเดียว
- [ ] `<button>` ที่จัด layout เอง ใช้ selector `button.<class>` (ไม่โดน global button style ทับ)

### Automation / Integration (CRM, Agent)
- [ ] การเขียนข้อมูลไประบบภายนอก (CRM ฯลฯ) ทดสอบกับข้อมูลทดสอบ 1 รายการก่อน และบันทึกผลใน PR
- [ ] งาน Automation อัปเดต `AUTOMATION_TODO.md` (สถานะ + Progress Log) ตาม Acceptance Criteria จริง

## ขั้นที่ 4 — ทดสอบจริง

- [ ] เปิดหน้าที่แก้ในแอปจริง ทำ flow หลักจนจบ 1 รอบ และลอง 1 กรณีผิดพลาด
- [ ] Desktop (~1440px) และ Mobile (~390px) — ไม่มี horizontal scroll ระดับหน้า
- [ ] แก้ backend: restart API ก่อนทดสอบ (SYSTEM_OVERVIEW §4) — โดเมนสาธารณะจะยังใช้โค้ดเก่าถ้าไม่ restart
- [ ] เขียนสิ่งที่ทดสอบแล้ว/ยังไม่ได้ทดสอบลงใน PR ตามจริง

## ขั้นที่ 5 — AI review

รัน `/code-review` ใน Claude Code กับ branch นี้ แล้วจัดการ finding ทุกข้อ: แก้ หรือเขียนเหตุผลที่ไม่แก้ใน PR

## ขั้นที่ 6 — เปิด PR

- ใช้ template (`.github/pull_request_template.md` ขึ้นอัตโนมัติ) กรอกให้ครบ: สรุป, ผลกระทบ, วิธีทดสอบ, ความเสี่ยง, การ deploy
- แนบผลสรุปของ `pre-pr-check.ps1` และภาพหน้าจอ (Desktop/Mobile) เมื่อแก้ UI
- PR ที่ยังไม่พร้อมให้เปิดเป็น **Draft**

## เกณฑ์ merge

- `pre-pr-check.ps1` ไม่มี FAIL และ WARN ทุกข้อมีคำอธิบาย
- checklist ใน PR ครบ และ finding ระดับ Blocker ถูกแก้แล้ว
- มีผู้ review อย่างน้อย 1 คน (ทำงานคนเดียว: self-review ผ่าน Files changed + `/code-review` แทน)
- merge แล้ว ถ้าแก้ backend ต้อง restart API บนเครื่อง deploy
