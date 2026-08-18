# ProMaxx2 QA Management System — Master Development Package

เอกสารกลางสำหรับออกแบบ พัฒนา ทดสอบ และส่งมอบระบบ QA Management

## Structure
- `01-System-Blueprint/` — Requirement, Logical Database Design, Workflow
- `02-Developer-Blueprint/` — Physical SQL Schema, REST API, Screen Specification
- `03-Architecture-and-Plan/` — Architecture, Source Structure, Development Plan

## Document Order
1. REQUIREMENTS.md
2. WORKFLOW.md
3. DATABASE_DESIGN.md
4. ARCHITECTURE.md
5. PROJECT_STRUCTURE.md
6. SQL_SERVER_SCHEMA.md
7. API_SPECIFICATION.md
8. SCREEN_SPECIFICATION.md
9. DEVELOPMENT_PLAN.md
10. UI_DESIGN_SYSTEM.md (UI Single Source of Truth)

## Technology
ASP.NET Core .NET 10 + SQL Server + REST API + Web Frontend + File/Object Storage

## Core Traceability
`Requirement → Test Case → Build → Test Execution → Defect → Retest → Regression → Release`

Historical Execution, Revision, Risk Approval และ Release Sign-off ต้อง Audit ย้อนหลังได้และไม่ถูกเขียนทับ


## Document Governance

```text
04-Document-Governance/
├─ CHANGELOG.md
├─ TRACEABILITY_MATRIX.md
└─ DOCUMENT_CONTROL.md
```

- `DOCUMENT_CONTROL.md` กำหนด Single Source of Truth, Version, Owner, Review และ Change Workflow
- `TRACEABILITY_MATRIX.md` ใช้ตรวจ Requirement → Workflow → DB → API → Screen → Test → Release
- `CHANGELOG.md` บันทึก Controlled Change ของ Master Package

Master Package นี้ควรเป็นเอกสารหลักที่แก้ไขต่อเนื่อง แทนการสร้าง Blueprint ซ้ำหลายชุด
