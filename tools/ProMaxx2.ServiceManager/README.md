# ProMaxx2 QA System Manager

โปรแกรม Windows แยกสำหรับตรวจและควบคุม API (`5038`) กับ Web (`5173`) ของ ProMaxx2 QA Hub

## เปิดใช้งาน

ดับเบิลคลิก `OPEN_SYSTEM_MANAGER.bat` ที่ root ของ repository หรือรัน:

```powershell
dotnet run --project tools\ProMaxx2.ServiceManager\ProMaxx2.ServiceManager.csproj
```

โปรแกรมรองรับ Start, Stop, Restart, Start/Restart ทั้งหมด, เปิดหน้าเว็บ, ตรวจสถานะอัตโนมัติทุก 3 วินาที และ Activity Log

## สร้างไฟล์ EXE

```powershell
dotnet publish tools\ProMaxx2.ServiceManager\ProMaxx2.ServiceManager.csproj -c Release -r win-x64 --self-contained false -o .artifacts\system-manager
```
