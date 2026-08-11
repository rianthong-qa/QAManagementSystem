# ProMaxx2 QA Management System — PROJECT_STRUCTURE

## 1. Repository

```text
ProMaxx2.QA/
├─ docs/
│  ├─ system/
│  ├─ development/
│  └─ architecture/
├─ src/
│  ├─ ProMaxx2.QA.Api/
│  ├─ ProMaxx2.QA.Application/
│  ├─ ProMaxx2.QA.Domain/
│  ├─ ProMaxx2.QA.Infrastructure/
│  └─ ProMaxx2.QA.Web/
├─ tests/
│  ├─ ProMaxx2.QA.UnitTests/
│  ├─ ProMaxx2.QA.IntegrationTests/
│  └─ ProMaxx2.QA.ArchitectureTests/
├─ database/
│  ├─ migrations/
│  ├─ scripts/
│  ├─ seeds/
│  └─ views/
├─ tools/
├─ Directory.Build.props
├─ ProMaxx2.QA.sln
└─ README.md
```

## 2. Domain
เก็บ Entities, Enums, Value Objects และ Domain Rules แยกตาม Identity, Projects, Releases, Requirements, TestManagement, TestExecution, Defects, Regression และ Governance

ห้ามมี DbContext, Controller, SQL หรือ UI DTO

## 3. Application
แนะนำ Feature Folder:

```text
TestCases/
├─ Commands/
│  ├─ CreateTestCase/
│  ├─ UpdateTestCase/
│  └─ CreateRevision/
├─ Queries/
├─ DTOs/
└─ Validators/
```

เก็บ Use Cases, Commands, Queries, DTOs, Validators และ Interfaces

## 4. Infrastructure

```text
Infrastructure/
├─ Persistence/
│  ├─ QaDbContext.cs
│  ├─ Configurations/
│  ├─ Migrations/
│  └─ Repositories/
├─ Identity/
├─ Storage/
├─ Notifications/
├─ Logging/
└─ DependencyInjection.cs
```

## 5. API

```text
Api/
├─ Controllers/
│  ├─ AuthController.cs
│  ├─ ProjectsController.cs
│  ├─ ReleasesController.cs
│  ├─ BuildsController.cs
│  ├─ RequirementsController.cs
│  ├─ RtmController.cs
│  ├─ TestCasesController.cs
│  ├─ TestCyclesController.cs
│  ├─ ExecutionsController.cs
│  ├─ DefectsController.cs
│  └─ SignoffsController.cs
├─ Middleware/
├─ Authorization/
├─ Extensions/
├─ Filters/
└─ Program.cs
```

Controller ควรบาง: `HTTP → Application Use Case → Response`

## 6. Web
จัด Feature-based: dashboard, releases, requirements, rtm, test-cases, test-cycles, executions, defects, regression, risks, signoff พร้อม reusable components/services/types

## 7. Database
ใช้ Migration เป็นหลัก และแยก scripts/seeds/views สำหรับ Roles, Permissions และ Views

## 8. Tests
- Unit: Release Gate, Coverage, Defect Transition, Revision, Risk Rules
- Integration: API + DB, Auth, Transaction, FK, File Metadata
- Architecture: Domain ห้ามอ้าง Infrastructure, Application ห้ามอ้าง API, Controller ห้ามใช้ DbContext ตรง

## 9. Naming
- Entity `TestCase`
- Command `CreateTestCaseCommand`
- Handler `CreateTestCaseHandler`
- DTO `TestCaseDto`
- API URL kebab-case `/test-cases`
- JSON camelCase
- DB Table plural, PK `<Entity>Id`, Index `IX_Table_Columns`

## 10. Branch
`main`, `develop`, `feature/...`, `fix/...`

PR ต้อง Build/Test ผ่าน และ Review Migration/API Contract เมื่อมีการเปลี่ยน

## 11. Configuration
ห้าม Commit Password, Production Connection String, JWT Secret หรือ Storage Secret ใช้ Environment Variables/Secret Store

## 12. Definition of Done
Requirement/Acceptance Criteria, Migration, API, Permission, UI, Validation, Audit, Unit/Integration Test, Error Handling, Documentation และ QA Test ต้องครบตามขอบเขต Feature
