# ProMaxx2 QA System Manager

โปรแกรม Windows แยกสำหรับตรวจและควบคุม API (`5038`) กับ Web (`5173`) ของ ProMaxx2 QA Hub

## เปิดใช้งาน

ดับเบิลคลิก `OPEN_SYSTEM_MANAGER.bat` ที่ root ของ repository หรือรัน:

```powershell
dotnet run --project tools\ProMaxx2.ServiceManager\ProMaxx2.ServiceManager.csproj
```

โปรแกรมรองรับ Start, Stop, Restart, Start/Restart ทั้งหมด, เปิดหน้าเว็บ, ตรวจสถานะอัตโนมัติทุก 3 วินาที และ Activity Log

Activity Log รับข้อความจาก process ผ่านคิวและอัปเดตหน้าจอเป็นชุดทุก 250ms โดยจำกัดคิวไว้ 5,000 บรรทัดและข้อความบนหน้าจอประมาณ 200,000 ตัวอักษร เพื่อไม่ให้ UI และหน่วยความจำสะสมจนโปรแกรมค้างเมื่อเปิดทิ้งไว้นาน

การ์ด Service Resources แสดงกราฟ CPU (%) และ RAM (MB) รวมของ process ที่กำลังฟัง Port API 5038 และ Web 5173 แบบ real time ทุก 1 วินาที พร้อมข้อมูลย้อนหลังสูงสุด 2 นาที

## สร้างไฟล์ EXE

```powershell
dotnet publish tools\ProMaxx2.ServiceManager\ProMaxx2.ServiceManager.csproj -c Release -r win-x64 --self-contained false -o .artifacts\system-manager
```
