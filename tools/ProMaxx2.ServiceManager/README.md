# ProMaxx2 QA System Manager

โปรแกรม Windows แยกสำหรับตรวจและควบคุม API (`5038`) กับ Web (`5173`) ของ ProMaxx2 QA Hub

## เปิดใช้งาน

ดับเบิลคลิก `OPEN_SYSTEM_MANAGER.bat` ที่ root ของ repository หรือรัน:

```powershell
dotnet run --project tools\ProMaxx2.ServiceManager\ProMaxx2.ServiceManager.csproj
```

หน้าจอ (ปรับใหม่ 2026-10-30, สีตาม Design Tokens ของเว็บ):

- **ส่วนหัว** — สถานะรวม ("ระบบพร้อมใช้งาน" / "API หยุดอยู่" / "กำลังดำเนินการ...") + เปิดหน้าเว็บ, Restart ทั้งหมด, Start ทั้งหมด (แสดงเมื่อมีบริการหยุดอยู่) — Start/Restart ทั้งหมดทำ API กับ Web **พร้อมกัน**
- **การ์ด API / Web / Schedule Worker** — ป้ายสถานะ (กำลังทำงาน / ไม่ตอบสนอง / หยุดอยู่ / กำลังเริ่ม... n วินาที), Port · PID, CPU · RAM · เวลาที่ทำงานมา และแสดงเฉพาะปุ่มที่ใช้ได้ (หยุดอยู่ → Start, ทำงานอยู่ → Restart / Stop); ระหว่างเริ่ม/หยุด ปุ่มของบริการนั้นถูกปิดกันกดซ้ำ; Stop ถามยืนยันก่อน
- **Restart แบบปลอดภัย** — Restart API / Restart ทั้งหมด จะ `dotnet build` API ลง `%TEMP%\qa-svcmgr-verify\api` ก่อน (ไม่ชน DLL ที่ API ตัวที่รันอยู่ล็อก — วิธีเดียวกับ `pre-pr-check.ps1`, ~20 วินาที) ถ้า build ไม่ผ่านจะ**ไม่หยุด API ตัวเดิม** และแสดง error `[BUILD]` สีแดงใน log
- **ป้าย "มีโค้ดใหม่ — ควร Restart"** (สีเหลืองในการ์ด) — API: ไฟล์ `*.cs` / `*.csproj` / `appsettings*.json` ใน Api/Application/Domain/Infrastructure ถูกแก้หลังจาก process เริ่ม; Web: เฉพาะ `vite.config.*`, `package*.json`, `.env*`, `tsconfig*.json` (source อื่น Vite เสิร์ฟสดอยู่แล้ว) — ส่วนหัวแสดง branch @ commit และจำนวนไฟล์ที่แก้ค้าง
- **แถวตรวจสภาพแวดล้อม** (ทุก 30 วินาที บน thread pool) — เว็บสาธารณะ `https://promaxx2.qahub.store`, API สาธารณะ `https://api-promaxx2.qahub.store/health` (สถานะ HTTP + เวลาตอบ, timeout 8 วินาที), Windows service `cloudflared` และ `MSSQL$MSSQLSERVER2022` (จาก `sc.exe query`), พื้นที่ว่างของไดรฟ์ workspace และไดรฟ์ระบบ (เหลือง < 15 GB, แดง < 5 GB) — แจ้งเตือนเมื่อ Tunnel/SQL Server หยุด หรือเข้าผ่านโดเมนไม่ได้ 2 รอบติดทั้งที่ในเครื่องทำงานอยู่
- **ตั้งค่า** (ปุ่มที่ส่วนหัว) — เปิดพร้อม Windows (เขียน `HKCU\...\Run` = `"<exe>" --tray` เปิดแบบซ่อนใน tray), Start API/Web อัตโนมัติเมื่อเปิดโปรแกรม, Start ใหม่อัตโนมัติเมื่อ API/Web หยุดเองโดยไม่ได้สั่ง (สูงสุด 3 ครั้งใน 10 นาที เกินนั้นหยุดและแจ้งเตือน) — เก็บที่ `%LOCALAPPDATA%\ProMaxx2.ServiceManager\settings.json`
- **แถบแจ้งผล** ใต้ส่วนหัวแทน MessageBox — สำเร็จหายเองใน 6 วินาที, error ค้าง 20 วินาที, ปิดได้ด้วย ×
- **การใช้ทรัพยากร** — กราฟ CPU (%) และ RAM (MB) รวมของ process ที่ฟัง Port 5038/5173 ทุก 1 วินาที ย้อนหลัง 2 นาที พร้อมแยก RAM ราย API/Web
- **System tray** — ย่อหน้าต่างแล้วซ่อนลง tray (ไม่ค้างบน taskbar, ครั้งแรกมีแจ้งเตือนบอก); คลิกไอคอน = เปิดกลับ, คลิกขวา = เมนู (เปิด System Manager, สถานะ API/Web, Start ทั้งหมด, Restart ทั้งหมด, เปิดหน้าเว็บ, ออกจากโปรแกรม); ไอคอนเป็นตัว Q มีจุดสีตามสถานะรวม (เขียว พร้อม / เหลือง กำลังทำงาน-ไม่ตอบสนอง / แดง มีบริการหยุด) และ tooltip บอกสถานะ; ระหว่างซ่อนอยู่ผลของคำสั่งและ **API/Web ที่หยุดเองโดยไม่ได้สั่ง** (เช่น crash) แจ้งเป็น notification ของ Windows — ปุ่ม × ยังปิดโปรแกรมตามเดิม
- **Activity Log** — สีตามระดับ (ข้อความระบบ / สำเร็จ เขียว / คำเตือน เหลือง / error แดง); ค่าเริ่มต้น**ซ่อน log ระดับ info ของ API/Web** (เช่น SQL ของ EF) แต่แสดงคำเตือนและ error เสมอ — ติ๊ก "แสดง log ทั้งหมดของ API/Web" เพื่อดูทั้งหมด; เลื่อนขึ้นไปอ่านแล้วจะไม่ถูกดึงลงล่าง; ปุ่มคัดลอก (ข้อความที่เลือกหรือทั้งหมด) และล้าง Log

ความลื่น: ตรวจสถานะทุก 3 วินาทีโดยเช็ค API/Web พร้อมกันและรัน `netstat` ครั้งเดียวบน thread pool (เดิมรัน 2 ครั้งแบบรอผลบน UI thread จึงกระตุก); Log รับผ่านคิวและวาดเป็นชุดทุก 250ms (ต่อข้อความสีเดียวกันเป็นก้อนเดียว, ปิดการวาดระหว่างเพิ่ม) จำกัดคิว 5,000 บรรทัดและข้อความบนจอ ~200,000 ตัวอักษร; รอ API เปิด Port ได้ 3 นาที (Web 1 นาที) และแจ้งทันทีถ้า process จบเองระหว่างเริ่ม

## สร้างไฟล์ EXE

```powershell
dotnet publish tools\ProMaxx2.ServiceManager\ProMaxx2.ServiceManager.csproj -c Release -r win-x64 --self-contained false -o .artifacts\service-manager-production
```
