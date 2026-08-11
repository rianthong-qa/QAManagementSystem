# ProMaxx2 QA Management System — CHANGELOG

> เอกสารนี้ใช้บันทึกการเปลี่ยนแปลงของ Master Development Package  
> หลักการ: ทุกการเปลี่ยน Requirement, Workflow, Database, API, Screen หรือ Architecture ที่มีผลต่อการพัฒนาต้องมีรายการในไฟล์นี้

## Versioning

ใช้รูปแบบ `MAJOR.MINOR.PATCH`

- **MAJOR** — เปลี่ยน Architecture/Workflow/Contract สำคัญ หรือ Breaking Change
- **MINOR** — เพิ่ม Requirement, API, Table, Screen หรือ Feature ใหม่
- **PATCH** — แก้คำอธิบาย, Validation, Field หรือรายละเอียดที่ไม่เป็น Breaking Change

## Change Types

- `ADDED`
- `CHANGED`
- `FIXED`
- `REMOVED`
- `DEPRECATED`
- `SECURITY`
- `DATABASE`
- `API`
- `UI`
- `WORKFLOW`

## Change Log

### [1.1.0] — 2026-08-11

#### ADDED
- เพิ่ม `ARCHITECTURE.md`
- เพิ่ม `PROJECT_STRUCTURE.md`
- เพิ่ม `DEVELOPMENT_PLAN.md`
- เพิ่ม `CHANGELOG.md`
- เพิ่ม `TRACEABILITY_MATRIX.md`
- เพิ่ม `DOCUMENT_CONTROL.md`
- รวม System Blueprint และ Developer Blueprint เป็น Master Development Package

#### ARCHITECTURE
- กำหนด Modular Monolith + Clean Architecture principles
- กำหนด SQL Server เป็น System of Record
- กำหนด Test Execution และ Sign-off History เป็น Immutable
- กำหนด Evidence แยกออกจาก SQL Server

#### GOVERNANCE
- กำหนด Master Package เป็น Single Source of Truth
- เพิ่ม Document Control และ Change Impact Process

### [1.0.0] — 2026-08-11

#### ADDED
- REQUIREMENTS.md
- DATABASE_DESIGN.md
- WORKFLOW.md
- SQL_SERVER_SCHEMA.md
- API_SPECIFICATION.md
- SCREEN_SPECIFICATION.md

## Change Entry Template

```text
### [x.y.z] — YYYY-MM-DD

#### ADDED / CHANGED / FIXED / REMOVED
- Change ID:
- Request/Requirement:
- Description:
- Reason:
- Requested By:
- Owner:
- Affected Documents:
- Database Impact:
- API Impact:
- UI Impact:
- Workflow Impact:
- Test Impact:
- Breaking Change: Yes/No
- Migration Required: Yes/No
- Approval:
```

## Change Management Rule

เมื่อมี Requirement ใหม่หรือแก้ Requirement:

`Change Request → Impact Analysis → Update Requirement → Update Design/Workflow → Update DB/API/UI → Update Test → Review → Update Changelog`

ห้ามแก้เฉพาะ Code โดยไม่ตรวจเอกสารที่เกี่ยวข้อง หากเป็นการเปลี่ยน Contract หรือ Business Rule
