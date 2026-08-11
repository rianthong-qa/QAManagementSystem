# ProMaxx2 QA Management — System Blueprint

เอกสารสำหรับนำ QA Management Prototype ไปพัฒนาเป็นระบบจริง

## Files
- REQUIREMENTS.md — Requirement, Business Rules, MVP
- DATABASE_DESIGN.md — Database entities/relationships/integrity
- WORKFLOW.md — End-to-end QA workflow

## Architecture แนะนำ
`Web Frontend → ASP.NET Core .NET 10 REST API → Application/Domain → SQL Server`

Evidence เช่น Screenshot/Video/Log ควรเก็บใน File/Object Storage และเก็บ Metadata ใน SQL Server

Core Traceability:
`Requirement → Test Case → Build → Execution → Defect → Retest → Regression → Release`
