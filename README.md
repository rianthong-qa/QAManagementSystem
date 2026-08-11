# ProMaxx2 QA Management System

ระบบบริหารงาน Quality Assurance ตามเอกสารใน `Document/` พัฒนาด้วย ASP.NET Core .NET 10, React/TypeScript และ SQL Server โดยใช้ Modular Monolith และ Clean Architecture principles

## เริ่มใช้งาน

ต้องติดตั้ง .NET SDK 10 และ Node.js 22 ขึ้นไป

```powershell
dotnet restore
dotnet run --project src/ProMaxx2.QA.Api
```

เปิด terminal อีกหน้าต่าง:

```powershell
cd src/ProMaxx2.QA.Web
npm install
npm run dev
```

Web UI: `http://localhost:5173`  
API health: ดู URL HTTPS จาก output ของ `dotnet run` แล้วเรียก `/health`  
OpenAPI (Development): `/openapi/v1.json`

## Database และผู้ดูแลระบบเริ่มต้น

กำหนด connection string ด้วย secret/environment variable แล้วสร้างฐานข้อมูล:

```powershell
$env:ConnectionStrings__QaDatabase="Server=localhost;Database=ProMaxx2QA;User Id=...;Password=...;TrustServerCertificate=True"
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/ProMaxx2.QA.Infrastructure --startup-project src/ProMaxx2.QA.Api
```

ระบบรองรับการ migrate และ seed roles/permissions ตอน startup โดยไม่เก็บรหัสผ่านใน repository:

```powershell
$env:Database__ApplyMigrations="true"
$env:Seed__AdminPassword="กำหนดรหัสผ่านที่ปลอดภัย"
dotnet run --project src/ProMaxx2.QA.Api
```

บัญชีที่สร้างคือ `admin`; หลังการ seed ควรยกเลิก `Database__ApplyMigrations` และล้างค่า `Seed__AdminPassword`

## โครงสร้าง

- `src/ProMaxx2.QA.Domain` — entities และ business rules
- `src/ProMaxx2.QA.Application` — use cases, DTO และ interfaces
- `src/ProMaxx2.QA.Infrastructure` — persistence และ integrations
- `src/ProMaxx2.QA.Api` — REST API `/api/v1`
- `src/ProMaxx2.QA.Web` — React/TypeScript UI
- `tests` — automated tests
- `database` — migrations, scripts, seeds และ views
- `Document` — master specification และ UI prototype

## ตรวจสอบคุณภาพ

```powershell
dotnet test
cd src/ProMaxx2.QA.Web
npm run build
```

สถานะปัจจุบันคือ Phase 0 foundation และ working UI shell; module screens ที่ยังไม่ต่อ data จะแสดง placeholder เพื่อรองรับ vertical slices ถัดไป
