# ProMaxx2 QA Management — Screen Specification

> เป้าหมาย: กำหนดหน้าจอ, Component, Action, Validation และ Permission สำหรับ Frontend  
> ภาษา UI หลัก: ไทย  
> Desktop-first, Responsive สำหรับ Tablet

---

## 1. Layout หลัก

### Sidebar
หมวด:
1. Dashboard
2. Project / Module
3. Release / Build
4. Requirement / RTM
5. Test Design
6. Test Execution
7. Defect / Regression
8. Reports / Sign-off
9. Administration

### Top Bar
- Current Project
- Current Release
- Current Build
- Search
- Notification
- User Menu

### Global Filters
ควรจำค่า User ล่าสุด:
- Project
- Release
- Build
- Module
- Date Range

---

# 2. Login

## Route
`/login`

## Fields
- Username
- Password
- Remember Me

## Actions
- Login

## Validation
- Required
- Invalid Credential Message แบบไม่บอกว่า Username หรือ Password ผิดส่วนไหน

---

# 3. Dashboard

## Route
`/dashboard`

## KPI Cards
- Requirement Coverage
- Test Execution %
- Pass Rate
- Open P0
- Open P1
- Regression %
- Release Readiness

## Widgets
- Module Health
- Defect Trend
- Test Status Pie/Bar
- Tester Workload
- Recent Activity
- Release Gate
- Blocker List

## Actions
- Filter Release/Build
- Drill-down ไป Requirement/Test Cycle/Defect
- Export Summary

## Permission
`PROJECT.VIEW`

---

# 4. Project List

## Route
`/projects`

## Columns
- Project Code
- Project Name
- Owner
- Status
- Active
- Updated At

## Actions
- Create
- Edit
- Open
- Deactivate

## Permission
View: `PROJECT.VIEW`
Edit: `PROJECT.EDIT`

---

# 5. Module Management

## Route
`/projects/{projectId}/modules`

## UI
Tree + Table

## Fields
- Module Code
- Module Name
- Parent Module
- Owner
- Description
- Active

## Actions
- Add Parent
- Add Child
- Edit
- Disable

---

# 6. Release List

## Route
`/releases`

## Columns
- Release Code
- Version
- Type
- Planned Date
- Status
- Release Owner
- Readiness

## Filters
- Status
- Date
- Owner

## Actions
- Create Release
- Open
- Change Status
- Generate Summary
- Sign-off

---

# 7. Release Detail

## Route
`/releases/{releaseId}`

## Tabs
1. Overview
2. Builds
3. Requirements
4. Test Cycles
5. Defects
6. Regression
7. Risks
8. Test Summary
9. Sign-off
10. Audit

## Header
- Release Code
- Version
- Status
- Owner
- Planned Date
- Current Candidate Build

## Release Gate Panel
- Smoke
- Coverage
- Regression
- Open P0/P1
- Update Test
- Approved Risks
- Recommended Decision

---

# 8. Build List / Build Detail

## Route
`/releases/{releaseId}/builds`

## Columns
- Build Number
- App Version
- Package Version
- Build Date
- RC
- Status
- Smoke Result

## Create/Edit Fields
- Build Number
- Application Version
- Package Version
- Commit Reference
- Change Notes
- Known Issues
- Build Date
- Release Candidate

## Actions
- Create Smoke Cycle
- Mark RC
- Compare Build
- Open Change Impact

---

# 9. Requirement List

## Route
`/requirements`

## Columns
- Requirement ID
- Title
- Module
- Release
- Priority
- Risk
- Coverage
- Latest Result
- Status
- Owner

## Filters
- Release
- Module
- Status
- Priority
- Coverage
- In Scope
- Owner
- Search

## Actions
- Create
- Bulk Import
- Bulk In/Out Scope
- Link Test Case
- Export RTM

---

# 10. Requirement Detail

## Route
`/requirements/{id}`

## Sections
### General
- Requirement Code
- Title
- Description
- Acceptance Criteria
- Module
- Release
- Priority
- Risk
- Owner
- Status
- In Scope

### Traceability
Table:
- Test Case ID
- Test Case Title
- Priority
- Latest Execution
- Defect Count
- Coverage Type

### History
- Revision
- Changed By
- Changed At
- Change Reason

## Actions
- Edit
- Create Revision
- Link Test Case
- Change Status

---

# 11. RTM Screen

## Route
`/rtm`

## Matrix Columns
- Requirement
- Module
- Priority
- Test Case Count
- Coverage
- Execution
- Defects
- Status

## Color
- Green = Passed
- Red = Failed
- Yellow = Blocked/Partial
- Gray = Not Tested
- Blue = Covered

## Actions
- Expand Requirement
- Link/Unlink Test Case
- Export Excel
- Filter Not Covered

---

# 12. Test Scenario List

## Route
`/test-scenarios`

## Columns
- Scenario ID
- Module
- Title
- Test Type
- Priority
- Risk
- Test Case Count
- Status
- Owner

## Actions
- Create
- Edit
- Open
- Create Test Case

---

# 13. Test Case List

## Route
`/test-cases`

## Columns
- Test Case ID
- Title
- Module
- Scenario
- Priority
- Type
- Revision
- Status
- Owner
- Last Result

## Filters
- Module
- Scenario
- Priority
- Type
- Status
- Owner
- Tag
- Automation Candidate

## Actions
- Create
- Duplicate
- Bulk Add to Suite
- Import
- Export
- Deprecate

---

# 14. Test Case Editor

## Route
`/test-cases/new`
`/test-cases/{id}/edit`

## Header Fields
- Test Case ID
- Module
- Scenario
- Title
- Objective
- Preconditions
- Priority
- Test Type
- Automation Candidate
- Owner
- Status

## Steps Grid
Columns:
- Step No.
- Action
- Test Data
- Expected Result
- Drag/Reorder
- Delete

## Linked Data
- Requirement
- Test Data
- Tags

## Validation
- ID unique
- Title required
- Module required
- Ready Case ต้องมีอย่างน้อย 1 Step
- ทุก Step ต้องมี Action + Expected Result

## Actions
- Save Draft
- Submit Review
- Mark Ready
- Create Revision

---

# 15. Test Data Screen

## Route
`/test-data`

## Columns
- Data ID
- Type
- Description
- Sensitive
- Owner
- Active

## Detail Fields
- Data Value/Reference
- Initial State
- Reset Instruction

## Security
Sensitive Data ต้อง Mask บน List และต้องมี Permission เพิ่มถ้าจะ Reveal

---

# 16. Test Environment Screen

## Route
`/test-environments`

## Fields
- Environment Name
- OS
- App Version
- Database
- Dataset
- DPI
- Resolution
- Network
- Service/API Version
- Device
- Notes

## Actions
- Create
- Clone
- Edit
- Disable

---

# 17. Test Suite List

## Route
`/test-suites`

## Columns
- Suite Code
- Suite Name
- Type
- Risk Tier
- Case Count
- Active

## Actions
- Create
- Clone
- Edit
- Open
- Create Cycle

---

# 18. Test Suite Editor

## Layout
ซ้าย: Available Test Cases  
ขวา: Selected Cases

## Filter
- Module
- Priority
- Tag
- Risk
- Test Type

## Actions
- Add
- Remove
- Reorder
- Required/Optional

---

# 19. Test Cycle List

## Route
`/test-cycles`

## Columns
- Cycle Code
- Name
- Release
- Build
- Environment
- Type
- Progress
- Pass Rate
- Owner
- Status

## Actions
- Create
- Clone
- Start
- Close
- Open Execution Workspace

---

# 20. Create Test Cycle

## Fields
- Project
- Release
- Build
- Environment
- Suite
- Cycle Code
- Cycle Name
- Cycle Type
- Start/End
- Owner

## Options
- Populate from Suite
- Auto Assign by Module Owner
- Include only Required Cases

## Validation
- Build ต้องอยู่ Release ที่เลือก
- Environment ต้อง Active
- Closed Release ห้ามสร้าง Cycle ใหม่ เว้น Permission พิเศษ

---

# 21. Execution Workspace

## Route
`/test-cycles/{cycleId}/execute`

## Layout
### Left Panel
รายการ Test Case:
- ID
- Title
- Priority
- Assigned Tester
- Current Status

### Main Panel
- Case Header
- Preconditions
- Test Data
- Steps
- Expected Result
- Actual Result
- Step Status
- Evidence
- Comment

### Right Panel
- Requirement Links
- Defect Links
- Execution History
- Build / Environment
- Activity

## Actions
- Start
- Pass
- Fail
- Block
- Skip
- Attach Evidence
- Create Defect
- Next Case

## UX
Keyboard shortcut แนะนำ:
- P = Pass
- F = Fail
- B = Blocked
- N = Next

ต้องมี Confirmation ก่อน Finalize Result

---

# 22. Defect List

## Route
`/defects`

## Columns
- Defect ID
- Title
- Module
- Severity
- Priority
- Status
- Build Found
- Fix Build
- Assignee
- Age

## Filters
- Release
- Build
- Module
- Severity
- Priority
- Status
- Assignee
- Reporter
- Search

## Highlight
P0/P1 ใช้ Indicator ชัดเจน

---

# 23. Defect Detail

## Route
`/defects/{id}`

## Header
- Defect ID
- Severity
- Priority
- Status
- Module
- Assignee

## Tabs
1. Detail
2. Evidence
3. Linked Test Cases
4. Linked Requirements
5. Execution
6. Resolution
7. History

## Detail
- Description
- Precondition
- Steps to Reproduce
- Expected
- Actual
- Frequency
- Business Impact
- Workaround

## Resolution
Developer:
- Root Cause
- Resolution
- Fix Build
- Changed Components
- Regression Impact
- Technical Note

## Actions
- Assign
- Transition
- Ready for Retest
- Retest
- Reopen
- Close

---

# 24. Retest Screen

## Route
`/defects/{id}/retest`

## แสดง
- Original Defect
- Build Found
- Fix Build
- Original Execution
- Linked Test Case
- Previous Evidence

## Input
- Environment
- Result
- Comment
- Evidence

## Result
Pass → Closed  
Fail → Reopen

---

# 25. Regression Impact Screen

## Route
`/releases/{releaseId}/regression`

## Inputs
- Build
- Changed Module
- Shared Component
- DB Change
- API Change
- Calculation
- Update/Installer
- Defect Fix

## Output
Recommended Test Cases:
- Direct Impact
- Shared Dependency
- P0/P1 Cases
- Historical Defect Cases

## Actions
- Generate Regression Suite
- Add Selected Cases
- Create Regression Cycle

---

# 26. Daily QA Status

## Route
`/qa-status/daily`

## Auto Metrics
- Planned
- Executed
- Pass
- Fail
- Blocked
- Defect New
- Retested

## Manual Fields
- Scope
- Blocker
- Risk
- Completed
- Next Plan
- Need Help From

## Actions
- Save Draft
- Publish
- Export

---

# 27. Weekly QA Status

## Route
`/qa-status/weekly`

## Auto
- Progress
- Coverage
- Pass Rate
- Defect Trend
- Regression
- Module Health

## Manual
- Green/Yellow/Red
- Top Risks
- Blockers
- Next Milestones
- Recommendation

---

# 28. Test Summary

## Route
`/releases/{releaseId}/test-summary`

## Sections
- Executive Summary
- Scope
- Out-of-Scope
- Environment
- Metrics
- Requirement Coverage
- Defects
- Regression
- Installation/Update
- Performance
- Known Issues
- Remaining Risks
- QA Recommendation

## Actions
- Generate
- Regenerate
- Edit Narrative
- Export PDF/Excel
- Submit for Sign-off

---

# 29. Risk Acceptance

## Route
`/risk-acceptances`

## Columns
- Risk ID
- Release
- Defect
- Title
- Impact
- Probability
- Risk Level
- Owner
- Status
- Review Date

## Detail Actions
- Draft
- Submit
- Approve
- Reject
- Close

## Approval UI
ต้องแสดง:
- Issue
- Business Impact
- Workaround
- Target Fix
- QA Recommendation
- Linked Defect

---

# 30. Release Sign-off

## Route
`/releases/{releaseId}/signoff`

## Release Gate Panel
- Smoke
- Coverage
- Regression
- P0
- P1 Blocker
- Update/Migration
- Approved Risks

## Sign-off Cards
- QA
- Development
- Product
- Release Owner

แต่ละ Card:
- Decision
- Comment
- Approver
- Timestamp

## Final Decision
- GO
- CONDITIONAL GO
- NO-GO

## Validation
- GO ใช้ไม่ได้ถ้า Gate ที่ Hard Block ไม่ผ่าน
- Conditional Go ต้องมี Approved Risk ถ้ามี Blocker ที่องค์กรอนุญาต

---

# 31. User Management

## Route
`/admin/users`

## Columns
- Username
- Display Name
- Email
- Roles
- Active
- Last Login

## Actions
- Create
- Edit
- Activate/Deactivate
- Assign Role
- Reset Password (ถ้า Local Auth)

---

# 32. Role / Permission

## Route
`/admin/roles`

## UI
Role List + Permission Matrix

Columns:
- Permission
- View
- Create
- Edit
- Delete
- Execute
- Assign
- Approve
- Export

---

# 33. Audit Log

## Route
`/admin/audit`

## Columns
- Date/Time
- User
- Action
- Entity Type
- Entity ID
- Summary

## Filters
- User
- Entity
- Action
- Date

## Detail
Before / After Diff

---

# 34. Notification Center

## Route
`/notifications`

## Group
- Assignment
- Defect
- Retest
- Build
- Risk
- Sign-off
- Blocker

## Actions
- Mark Read
- Open Entity
- Mark All Read

---

# 35. Import Screen

## Route
`/imports`

## Types
- Requirement
- Test Case
- Test Data

## Flow
1. Download Template
2. Upload
3. Validate
4. Preview Error
5. Confirm Import
6. Result Summary

ต้องไม่ Import ทันทีโดยไม่ Preview

---

# 36. Export Screen

## Route
`/exports`

## Reports
- RTM
- Test Cases
- Test Execution
- Defect
- Daily Status
- Weekly Status
- Test Summary

## Filters
Release/Build/Module/Date

---

# 37. Global Search

## Route
`/search?q=...`

Search:
- Requirement ID/Title
- Test Case ID/Title
- Defect ID/Title
- Release
- Build

ผลลัพธ์แยกประเภท

---

# 38. Common Validation / UX Rules

1. Required Field แสดงชัด
2. Unsaved Change ต้อง Confirm
3. Critical Action ต้อง Confirm
4. Delete ใช้ Soft Delete
5. Error ต้องแสดงข้อความเข้าใจง่าย
6. ไม่แสดง Raw Stack Trace
7. List ทุกหน้ามี Pagination
8. Filter ควร Persist ต่อ User
9. URL ต้องเปิดตรง Detail ได้
10. Permission ตรวจทั้ง Frontend และ Backend
11. P0/P1 ต้องเห็นชัดในทุกหน้าที่เกี่ยวข้อง
12. Historical Execution ต้อง Read-only

---

# 39. Responsive Rules

Desktop:
- Sidebar เต็ม
- Table + Detail Panel

Tablet:
- Sidebar Collapse
- Table ลด Column
- Detail เปิด Drawer/Full Page

Mobile:
- ใช้ได้สำหรับดู Status/Approval เป็นหลัก
- Execution Workspace อาจไม่เหมาะเป็น Primary Device

---

# 40. Suggested Frontend Component Library

Component ที่ควรมี reusable:
- DataTable
- FilterBar
- StatusBadge
- PriorityBadge
- UserPicker
- ModulePicker
- ReleasePicker
- BuildPicker
- AttachmentUploader
- AuditTimeline
- ApprovalCard
- RequirementLinker
- TestCaseSelector
- ExecutionResultPanel
- ConfirmDialog
- EmptyState
- ErrorState
- SkeletonLoading

---

# 41. MVP Screen Priority

## P0
- Login
- Dashboard
- Release
- Build
- Requirement
- RTM
- Test Case
- Test Suite
- Test Cycle
- Execution Workspace
- Defect
- User/Role

## P1
- Test Data
- Environment
- Regression
- Evidence
- Test Summary
- Risk Acceptance
- Sign-off
- Audit

## P2
- Daily/Weekly
- Notification
- Import/Export
- Advanced Analytics
