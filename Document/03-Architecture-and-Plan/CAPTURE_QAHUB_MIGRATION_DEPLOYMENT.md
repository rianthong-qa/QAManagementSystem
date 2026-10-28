# Capture Companion — QA Hub Migration Deployment

Migration: `20260830171259_AddUniqueAutomationIdIndex`

This migration belongs to the QA Hub SQL Server database (`QaDatabase`). Do not run it against the ProMaxx2 Firebird database file.

## Pre-deployment

1. Take a SQL Server backup of the QA Hub database.
2. Run `CAPTURE_FIREBIRD_DUPLICATE_AUDIT.sql` only for the separate ProMaxx2 audit; the unique index in this migration is created in QA Hub.
3. Confirm the target connection string points to the QA Hub SQL Server database.
4. Confirm the duplicate audit returned no active duplicate `AutomationId` rows.

## Generate an idempotent SQL script

From the repository root:

```powershell
dotnet ef migrations script `
  20260830155830_AddAutomationCaptureSessions `
  20260830171259_AddUniqueAutomationIdIndex `
  --idempotent `
  --project src/ProMaxx2.QA.Infrastructure `
  --startup-project src/ProMaxx2.QA.Api
```

Review the generated SQL and execute it through the normal QA Hub release process.

## Post-deployment checks

- Confirm the migration is recorded in `__EFMigrationsHistory`.
- Confirm the unique index exists on `AutomationObjects(ProjectId, ApplicationCode, AutomationId)`.
- Run the QA Hub unit/integration test suite.
- Verify Capture Companion Preview and Commit against a non-production test case.

Rollback must use the approved SQL Server backup/release rollback procedure. Do not manually drop the index in production without change approval.
