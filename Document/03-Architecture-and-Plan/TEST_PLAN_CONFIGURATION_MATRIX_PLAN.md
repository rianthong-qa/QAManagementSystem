# Test Plan / Configuration Matrix — แผนพัฒนา

> สถานะ: เฟส 1–3 ใช้งานบน `ProMaxx2QA` แล้ว; เฟส 4–6 เสร็จในโค้ด (ไม่มี Migration ใหม่) — ต้อง restart API ก่อนใช้งาน; ยังไม่ได้ทดสอบกับ API/ข้อมูลจริง
>
> เป้าหมาย: เพิ่ม Test Plan สำหรับรวม Test Cycle หลายชุดและแสดง Configuration Matrix โดยรักษาข้อมูลและ flow เดิมของ Test Cycle, Test Suite และ Execution Workspace ให้ใช้งานต่อได้โดยไม่ต้อง migrate ข้อมูลเดิมแบบทำลายข้อมูล

## 1. ปัญหาและเป้าหมาย

### 1.1 ปัญหาปัจจุบัน

QA Hub มี `Release`, `Build`, `Test Environment`, `Test Suite`, `Test Cycle` และ `Execution Workspace` แล้ว แต่การวางแผนหลายรอบทดสอบยังต้องดูจากหน้า Test Cycle แยกกัน ทำให้:

- รวม Smoke, Regression, UAT หรือ Retest ภายใต้ Release เดียวกันได้ไม่เป็นชุดเดียว
- เปรียบเทียบ Build/Environment/Configuration หลายชุดได้ยาก
- มองภาพรวม Owner, Tester, Progress และผลรวมของหลาย Cycle ไม่ได้จากหน้าเดียว
- Rerun แผนเดิมไปยัง Build ใหม่ต้องทำทีละ Cycle
- ผู้ใช้ต้องใช้ชื่อ Cycle และ Filter เป็นตัวแทนของ Test Plan

### 1.2 เป้าหมายของ Feature

เพิ่มหน้าจอและ API สำหรับ:

1. สร้าง Test Plan ต่อ Project และ Release
2. รวม Test Cycle ที่มีอยู่แล้วเข้า Plan โดยไม่คัดลอก Test Case หรือ Execution History
3. สร้าง Run/Configuration หลายแถวใน Matrix
4. สร้าง Test Cycle ใหม่จาก Suite ภายใน Plan
5. แสดง Progress, Result, Owner, Tester และ Coverage แบบรวม
6. Clone/Rerun Plan ไปยัง Build หรือ Environment ใหม่
7. ปิดหรือ Freeze Plan เพื่อป้องกันการแก้ผลย้อนหลัง
8. ให้ Test Cycle และ Execution Workspace เดิมยังทำงานได้เหมือนเดิม

### 1.3 สิ่งที่ไม่รวมในแผนนี้

- Custom Fields / Tags / Workflow Designer
- External Issue Tracker เช่น Jira หรือ Azure DevOps
- JUnit/JSON Automation Ingestion
- Exploratory Testing Session
- Report Builder แบบลากวาง

Feature เหล่านี้ต้องเชื่อมต่อกับ Test Plan ในภายหลัง แต่ไม่ควรอยู่ใน Migration และ Release แรกเดียวกัน

## 1.4 Decision Log — ข้อสรุปก่อนเริ่ม Implementation

| คำถาม | คำตัดสินใจ |
|---|---|
| 1. นับ `InProgress` อย่างไร | นับใน `Touched/Progress` เพื่อสอดคล้องกับ Execution Workspace เดิม แต่ไม่รวมใน `Completed` และ `PassRate`; สถานะ Plan `Completed` ตัดสินจากสถานะ Run เท่านั้น (ดู §2.3) |
| 2. นิยาม Retest | นับ Test Cycle Case แบบไม่ซ้ำที่มี Execution **ที่ไม่ถูกลบและมีผลจบแล้ว** (`Pass/Fail/Blocked/Skipped`) มากกว่า 1 แถว; ไม่ใช้ `ExecutionNo` เพราะนับรวมแถวที่ลบและแถว Save Progress; แยก `RetestAttempts` สำหรับจำนวนครั้งจริง |
| 3. สถานะ Plan | `Draft/InProgress/Completed` คำนวณใน Query จาก Run ไม่เก็บลงตาราง; เก็บเฉพาะ `LifecycleStatus` (`Active/Closed/Cancelled`); Close/Cancel/Reopen เป็นคำสั่งเฉพาะที่ใช้ `TESTPLAN.FREEZE` ไม่มี Generic `POST /status` |
| 3.1 Closed/Cancelled กับ Freeze | Close และ Cancel ทำให้ `IsFrozen = true` อัตโนมัติ; Plan ที่ Closed/Cancelled Unfreeze ตรง ๆ ไม่ได้ ต้องใช้ `reopen` เท่านั้น |
| 4. Clone/Rerun | Target Build เลือกครั้งเดียวทั้ง Plan, Environment คงเดิมเป็นค่าเริ่มต้น, ใช้ `SourceSnapshot` ครั้งเดียวทั้ง Plan; `SuiteLatest` ยังไม่เปิดใน MVP; คัดลอก Configuration/Owner/Run-level Assignment แต่ไม่คัดลอกผู้รับผิดชอบรายเคส |
| 5. Permission | เพิ่ม `TESTPLAN.VIEW/EDIT/ASSIGN/FREEZE/EXPORT` (seed ด้วย Migration); ใช้ `EXECUTION.RUN` เดิมสำหรับบันทึกผล; VIEWER เห็น Read-only |
| 6. ลบ Cycle ที่ผูก Plan | บล็อกการลบ; Detach ได้เฉพาะ Run ที่ไม่เคยมี Execution เลย (นับรวมแถวที่ถูกลบ) และ Plan ยังไม่ Frozen; Run ที่มี History ให้ Cancel/Close แทน |
| 7. ลบ Plan | Soft Delete เท่านั้น และ Run ยังล็อก Cycle ไว้ตามเดิม (ลบ Cycle/Attach เข้า Plan อื่นไม่ได้) จนกว่าจะ Purge Plan ถาวรซึ่งไม่อยู่ใน MVP |

## 2. หลักการออกแบบที่ตัดสินใจแล้ว

### 2.1 ใช้ Test Cycle เดิมเป็น Execution Source of Truth

Test Plan จะเป็นชั้นสำหรับจัดกลุ่มและสรุปผล ไม่สร้างระบบ Execution ชุดใหม่

```text
TestPlan
  └─ TestPlanRun
       └─ TestCycle เดิม 1 ชุด
            └─ TestCycleCase
                 └─ TestExecution / TestStepResult
```

- `TestPlanRun` ต้องผูกกับ `TestCycle` ที่มีอยู่จริง 1 รายการ
- ห้ามคัดลอก Test Case หรือ Execution History เมื่อเพิ่ม Cycle เข้า Plan
- Test Cycle 1 รายการอยู่ใน Test Plan ได้ไม่เกิน 1 Plan ใน MVP เพื่อป้องกันตัวเลขซ้ำใน Report
- ถ้าต้องการใช้ Cycle เดิมเป็นอีก Plan ให้ Clone Cycle ตามกฎเดิมก่อน
- Progress และ Result ของ Plan คำนวณจาก `TestCycleCase` และ `TestExecution` ไม่เก็บตัวเลขซ้ำเป็น Source of Truth

### 2.2 Configuration เป็นข้อมูลของ Run ใน MVP

ยังไม่สร้าง Entity `TestConfiguration` แยกในรอบแรก ให้เก็บ Configuration ที่ `TestPlanRun` เพื่อให้เพิ่มได้เร็วและไม่กระทบ Test Cycle เดิม:

- `ConfigurationKey` เช่น `windows-chrome`
- `ConfigurationName` เช่น `Windows 11 · Chrome`
- `ConfigurationJson` สำหรับข้อมูลขยาย เช่น Browser, OS, Device, App Version

ตัวอย่าง:

```json
{
  "browser": "Chrome",
  "os": "Windows 11",
  "device": "Desktop",
  "appVersion": "10.0.229"
}
```

ถ้าในอนาคตต้องใช้ Configuration ร่วมหลาย Plan จึงค่อย normalize เป็น Master Configuration โดยไม่เปลี่ยน Contract ของ `TestPlanRun`

### 2.3 สถานะและ Freeze

สถานะของ Run แสดงจาก Test Cycle ที่ผูกอยู่ ไม่สร้างสถานะซ้ำที่อาจขัดแย้งกัน:

| Test Cycle | Test Plan Run |
|---|---|
| Draft | Draft |
| InProgress | In Progress |
| Completed | Completed |
| Closed | Closed |
| Cancelled | Cancelled |

Test Plan ใช้สถานะระดับแผนแบบ **คำนวณอัตโนมัติจาก Run** และมีสถานะปิด/ยกเลิกที่ผู้ใช้สั่งเป็นคำสั่งเฉพาะ ไม่เปิดให้ผู้ใช้แก้ `InProgress` หรือ `Completed` โดยตรง

ตารางเก็บเฉพาะ `LifecycleStatus` (`Active/Closed/Cancelled`) ส่วนสถานะที่แสดง (`Status`) คำนวณใน Query ตามลำดับนี้ เพื่อไม่ต้องแก้ ExecutionService/TestCycleService เดิมให้อัปเดต Plan ทุกครั้งที่บันทึกผล:

| สถานะ Plan | ความหมาย |
|---|---|
| Cancelled | `LifecycleStatus = Cancelled` |
| Closed | `LifecycleStatus = Closed` |
| Completed | `Active` และมีอย่างน้อย 1 Run และทุก Run อยู่ในสถานะ Completed/Closed/Cancelled |
| InProgress | `Active` และมีอย่างน้อย 1 Run เป็น InProgress หรือมี Case ที่ `CurrentStatus <> NotRun` |
| Draft | `Active` และยังไม่มี Run หรือทุก Run ยังเป็น Draft และยังไม่มี Case ถูกแตะ |

สถานะ `Completed` ตัดสินจากสถานะ Run เท่านั้น ไม่ดูว่ายังมี Case เป็น `InProgress` หรือไม่ เพราะ Cycle ที่ `Completed` ระบบเดิมไม่ให้บันทึกผลเพิ่มอยู่แล้ว (`ExecutionService.CreateAsync`) Matrix แสดง `CompletionPercent` แยกให้เห็นเคสที่ค้าง

กฎเพิ่มเติม:

- `InProgress` และ `Completed` เป็นสถานะ read-only ที่ระบบคำนวณจาก Run
- `Closed` และ `Cancelled` เป็นคำสั่งที่ผู้ใช้ทำได้ตาม Permission ผ่าน Endpoint เฉพาะ
- ไม่มี Generic `POST /status` สำหรับกำหนดสถานะ Plan โดยตรง
- Close และ Cancel ตั้ง `IsFrozen = true` อัตโนมัติใน Transaction เดียวกัน
- Plan ที่ Closed/Cancelled ใช้ `unfreeze` ไม่ได้ (409 `TEST_PLAN_NOT_ACTIVE`) ต้องใช้ `reopen` ซึ่งคืน `LifecycleStatus = Active` และ Unfreeze พร้อมกัน
- `freeze`/`unfreeze` แยกใช้ได้เฉพาะ Plan ที่ `Active` เช่น Freeze ระหว่างรอ Sign-off
- `IsFrozen = true` ต้องบล็อกการแก้ไขที่กระทบผลของ Plan ได้แก่ การบันทึก Execution, ลบ Execution, เพิ่ม/ลด Cycle Case, เปลี่ยน Assignment, แก้/เปลี่ยนสถานะ/ลบ Cycle และเพิ่ม/ถอด/แก้ Run (รายการ Endpoint ดู §7.3)
- Build/Environment ของ Cycle เปลี่ยนไม่ได้ในระบบเดิม การใช้ Build ใหม่ต้อง Clone/Rerun เป็น Plan ใหม่ (§4.4)

## 3. Domain และ Database Design

### 3.1 ตาราง `TestPlans`

| Column | Type | Rule |
|---|---|---|
| `TestPlanId` | `UNIQUEIDENTIFIER` | PK |
| `ProjectId` | `UNIQUEIDENTIFIER` | FK → Projects, required |
| `ReleaseId` | `UNIQUEIDENTIFIER` | FK → Releases, required ใน MVP |
| `PlanCode` | `NVARCHAR(100)` | Unique ต่อ Project |
| `PlanName` | `NVARCHAR(200)` | Required |
| `PlanType` | `NVARCHAR(50)` | Smoke/Regression/UAT/Retest/Other |
| `StartDate` | `DATETIME2(0)` | Nullable |
| `EndDate` | `DATETIME2(0)` | Nullable |
| `OwnerUserId` | `UNIQUEIDENTIFIER` | FK → Users, nullable |
| `LifecycleStatus` | `NVARCHAR(30)` | Active/Closed/Cancelled, default Active — สถานะที่แสดง (Draft/InProgress/Completed/Closed/Cancelled) คำนวณใน Query ตาม §2.3 |
| `IsFrozen` | `BIT` | Default 0 |
| `FrozenAt` | `DATETIME2(0)` | Nullable |
| `FrozenByUserId` | `UNIQUEIDENTIFIER` | FK → Users, nullable |
| `CopiedFromTestPlanId` | `UNIQUEIDENTIFIER` | FK → TestPlans (self), nullable — Plan ต้นทางเมื่อสร้างด้วย Clone/Rerun |
| `Notes` | `NVARCHAR(MAX)` | Nullable |
| `CreatedBy` / `CreatedAt` | existing convention | Audit |
| `UpdatedAt` / `UpdatedBy` | existing convention | Audit |
| `IsDeleted` / `DeletedAt` / `DeletedBy` | `BIT` / `DATETIME2(0)` / `UNIQUEIDENTIFIER` | Soft delete ตาม convention ของ Test Cycle/Recycle Bin |

Indexes/constraints:

- Unique `(ProjectId, PlanCode)`
- Index `(ProjectId, ReleaseId, LifecycleStatus)`
- Index `(ProjectId, IsDeleted, CreatedAt)`
- `ReleaseId` ต้องอยู่ Project เดียวกับ `ProjectId`
- Plan ที่มี Run หรือผล Execution แล้วห้าม Hard Delete
- Soft Delete Plan ไม่ลบ `TestPlanRuns` — Cycle ใน Plan ที่ถูกลบยังถูกล็อก (ดู §7.2.1)

### 3.2 ตาราง `TestPlanRuns`

| Column | Type | Rule |
|---|---|---|
| `TestPlanRunId` | `UNIQUEIDENTIFIER` | PK |
| `TestPlanId` | `UNIQUEIDENTIFIER` | FK → TestPlans, required |
| `TestCycleId` | `UNIQUEIDENTIFIER` | FK → TestCycles, required, unique ทั้งระบบใน MVP (รวม Run ของ Plan ที่ถูก Soft Delete) |
| `RunCode` | `NVARCHAR(100)` | Unique ต่อ Plan |
| `RunName` | `NVARCHAR(200)` | Required |
| `ConfigurationKey` | `NVARCHAR(100)` | Nullable |
| `ConfigurationName` | `NVARCHAR(200)` | Nullable |
| `ConfigurationJson` | `NVARCHAR(MAX)` | JSON ที่ validate ได้, nullable |
| `OwnerUserId` | `UNIQUEIDENTIFIER` | FK → Users, nullable |
| `CreatedBy` / `CreatedAt` | existing convention | Audit |
| `UpdatedAt` / `UpdatedBy` | existing convention | Audit |

Constraints:

- `TestCycle.ProjectId = TestPlan.ProjectId`
- `TestCycle.ReleaseId = TestPlan.ReleaseId`
- `TestCycleId` ห้ามผูกกับ Plan อื่นใน MVP
- Build และ Environment ใช้จาก Test Cycle เป็นค่าจริง ไม่เก็บซ้ำใน Run
- เมื่อ Clone Plan ไป Build/Environment ใหม่ ให้สร้าง Test Cycle ใหม่ผ่านกฎ Clone เดิม แล้วสร้าง `TestPlanRun` ใหม่ใน Transaction เดียวกัน — `TestCycleRepository.CloneAsync` เดิมเรียก `SaveChangesAsync` เองทุก Cycle จึงต้องครอบด้วย `BeginTransactionAsync` ระดับ Plan (หรือแยก method ที่ไม่ Save เอง) เพื่อให้ Clone หลาย Cycle ล้มเหลวแล้ว Rollback ทั้งชุด

### 3.3 ตาราง `TestPlanRunAssignments`

ใช้สำหรับระดับ Run/Configuration โดยไม่แทนที่ Assignment ราย Test Case เดิม

| Column | Type | Rule |
|---|---|---|
| `TestPlanRunAssignmentId` | `UNIQUEIDENTIFIER` | PK |
| `TestPlanRunId` | `UNIQUEIDENTIFIER` | FK → TestPlanRuns |
| `TesterUserId` | `UNIQUEIDENTIFIER` | FK → Users |
| `Role` | `NVARCHAR(30)` | Tester/Reviewer/Observer |
| `AssignedAt` | `DATETIME2(0)` | Required |
| `AssignedBy` | `UNIQUEIDENTIFIER` | FK → Users |

Unique `(TestPlanRunId, TesterUserId, Role)` และต้องตรวจ Project Access ของผู้ถูก Assign

ระดับ Run Assignment จะใช้เป็นค่าเริ่มต้นสำหรับการ Bulk Assign Test Cycle Cases เท่านั้น และจะไม่เขียนทับ Case ที่มีผู้รับผิดชอบอยู่แล้วโดยอัตโนมัติ

### 3.4 Counter และสูตร Progress

ทุก Counter คำนวณจาก Run ที่อยู่ใน Plan และกรอง `IsDeleted = 0`:

- `TotalCases` = จำนวน Test Cycle Case ทั้งหมดของทุก Run
- `Untested` = CurrentStatus = `NotRun`
- `InProgress` = CurrentStatus = `InProgress`
- `Passed` = CurrentStatus = `Pass`
- `Failed` = CurrentStatus = `Fail`
- `Blocked` = CurrentStatus = `Blocked`
- `Skipped` = CurrentStatus = `Skipped`
- `FinalExecutions(case)` = จำนวน `TestExecution` ของ Case ที่ `IsDeleted = 0` และ `Status IN (Pass, Fail, Blocked, Skipped)` — ไม่นับแถว `InProgress`/`NotRun` จาก Save Progress
- `Retest` = จำนวน Case ที่ `FinalExecutions > 1`; นับ Case เดียวครั้งเดียว ไม่ใช่จำนวน Retest attempt
- `RetestAttempts` = ผลรวมของ `FinalExecutions - 1` ของทุก Case ที่ `FinalExecutions > 1` สำหรับใช้ใน Detail/Report เท่านั้น
- **ห้ามใช้ `ExecutionNo`** คำนวณ Retest เพราะ `NextExecutionNoAsync` นับรวมแถวที่ถูกลบและแถว Save Progress — กด Save Progress 1 ครั้งแล้วจบผลจะได้ `ExecutionNo = 2` ทั้งที่ทดสอบครั้งเดียว
- `Touched` = `InProgress + Passed + Failed + Blocked + Skipped`; ใช้แทนจำนวนเคสที่เริ่มทดสอบแล้วตามพฤติกรรมเดิมของ Execution Workspace
- `Completed` = `Passed + Failed + Blocked + Skipped`; **ไม่รวม `InProgress`**
- `ProgressPercent` = `Touched / TotalCases * 100`; ใช้สื่อว่าเริ่มทดสอบไปแล้วเท่าไร
- `CompletionPercent` = `Completed / TotalCases * 100`; ใช้สื่อว่าเสร็จสมบูรณ์เท่าไร
- `PassRate` = `Passed / (Passed + Failed + Blocked + Skipped)`; ถ้าไม่มีผลให้เป็น `null` ไม่ใช่ 0%
- `RunCount` = จำนวน `TestPlanRuns`

`InProgress` จึงถูกนับใน `Touched/Progress` แต่ไม่ถูกนับใน `Completed`, `PassRate` หรือสถานะ Plan `Completed`

การคำนวณต้องคืนค่าใน Query เดียวหรือ Query แบบรวมที่ไม่ยิง N+1 ต่อ Run

## 4. API Contract

Base route: `/api/v1/test-plans`

### 4.1 List และ Detail

```http
GET /test-plans?projectId=&releaseId=&status=&planType=&ownerUserId=&search=&page=1&size=20
GET /test-plans/{planId}
GET /test-plans/{planId}/matrix?status=&configurationKey=&ownerUserId=&page=1&size=50
```

List ต้องคืนรูปแบบ `PagedResult<T>` ตามหน้าปัจจุบัน และ Summary ในคำขอเดียว:

```json
{
  "items": {
    "total": 1,
    "rows": []
  },
  "summary": {
    "runCount": 3,
    "totalCases": 35,
    "touched": 26,
    "inProgress": 3,
    "completed": 23,
    "passed": 12,
    "failed": 11,
    "blocked": 0,
    "skipped": 0,
    "retest": 4,
    "retestAttempts": 6,
    "progressPercent": 74.3,
    "completionPercent": 65.7,
    "passRate": 52.2
  }
}
```

ตรวจสอบตัวอย่าง: `untested = 35 - 26 = 9`, `touched = 3 + 23 = 26`, `completed = 12 + 11 + 0 + 0 = 23`, `progressPercent = 26/35`, `completionPercent = 23/35`, `passRate = 12/23`

### 4.2 Plan Lifecycle

```http
POST /test-plans
GET  /test-plans/{planId}
PUT  /test-plans/{planId}
POST /test-plans/{planId}/close
POST /test-plans/{planId}/cancel
POST /test-plans/{planId}/reopen
POST /test-plans/{planId}/freeze
POST /test-plans/{planId}/unfreeze
DELETE /test-plans/{planId}
```

Create payload:

```json
{
  "projectId": "...",
  "releaseId": "...",
  "planCode": "REL26-REG-001",
  "planName": "Release 2026.10 Regression Plan",
  "planType": "Regression",
  "startDate": "2026-10-29T00:00:00Z",
  "endDate": "2026-10-31T23:59:59Z",
  "ownerUserId": "...",
  "notes": "Regression ก่อน Mark RC"
}
```

กฎสำคัญ:

- Project และ Release ต้องอยู่ใน Scope ของผู้ใช้
- Release ต้องไม่เป็น Cancelled
- EndDate ต้องไม่ก่อน StartDate
- Plan ที่ Closed/Cancelled/Frozen แก้ข้อมูลหลักไม่ได้
- `close` ใช้ได้เมื่อ Plan `Active` และทุก Run อยู่ในสถานะ Terminal (`Completed`, `Closed` หรือ `Cancelled`); ตั้ง `LifecycleStatus = Closed` และ Freeze อัตโนมัติ
- `cancel` ใช้ยกเลิก Plan ที่ `Active` และไม่ลบ Run/Execution; ตั้ง `LifecycleStatus = Cancelled` และ Freeze อัตโนมัติ
- `reopen` ใช้ได้กับ Plan ที่ Closed หรือ Cancelled; คืน `LifecycleStatus = Active` และ Unfreeze พร้อมกัน สถานะที่แสดงจะกลับเป็น Draft/InProgress/Completed ตาม Run ปัจจุบัน
- `freeze`/`unfreeze` ใช้ได้เฉพาะ Plan ที่ `Active`; Plan ที่ Closed/Cancelled เรียก `unfreeze` ได้ 409 `TEST_PLAN_NOT_ACTIVE`
- `close`, `cancel`, `reopen`, `freeze` และ `unfreeze` ใช้ Permission `TESTPLAN.FREEZE` และต้องเขียน Audit Log พร้อมเหตุผล (request body `{ "reason": "..." }` required)

### 4.3 Run และ Matrix

```http
POST   /test-plans/{planId}/runs
POST   /test-plans/{planId}/runs/attach-cycle
GET    /test-plans/{planId}/runs/{runId}
PUT    /test-plans/{planId}/runs/{runId}
DELETE /test-plans/{planId}/runs/{runId}
POST   /test-plans/{planId}/runs/{runId}/assign
POST   /test-plans/{planId}/clone
```

Create Run รองรับ 2 แบบ:

1. `createCycleFromSuite` — สร้าง Test Cycle ใหม่จาก Suite แล้วผูกเข้า Plan
2. `attachExistingCycle` — เลือก Test Cycle ที่อยู่ Project/Release เดียวกันแล้วผูกเข้า Plan

ตัวอย่าง Create Run:

```json
{
  "runCode": "REG-WIN-CHROME",
  "runName": "Regression · Windows · Chrome",
  "configurationKey": "windows-chrome",
  "configurationName": "Windows 11 · Chrome",
  "configurationJson": {
    "os": "Windows 11",
    "browser": "Chrome",
    "device": "Desktop"
  },
  "testCycleId": "...",
  "createCycleFromSuite": false,
  "ownerUserId": "..."
}
```

Assign payload:

```json
{
  "assignments": [
    { "testerUserId": "...", "role": "Tester" },
    { "testerUserId": "...", "role": "Reviewer" }
  ],
  "bulkAssignUnassignedCases": true
}
```

### 4.4 Clone/Rerun Plan — กฎตายตัวใน MVP

การ Clone/Rerun เป็นการสร้าง Plan ใหม่ ไม่แก้ Plan ต้นฉบับ:

- Clone อยู่ภายใน Project เดิมเสมอ (ตามกฎ Clone Cycle เดิม)
- ผู้ใช้เลือก Target Release และ Target Build **ครั้งเดียวทั้ง Plan**; Build ต้อง Active และอยู่ใน Target Release, Release ต้องไม่ใช่ Released/Cancelled
- ทุก Run ที่ Clone จะใช้ Target Build เดียวกัน
- Environment ของแต่ละ Run คงเดิมจาก Source Run เป็นค่าเริ่มต้น
- ถ้า Environment เดิมถูกปิดใช้งาน (`IsActive = false`) ให้ผู้ใช้เลือก Environment ใหม่เฉพาะ Run ที่มีปัญหาในขั้น Preview
- `cloneMode` ใช้ `SourceSnapshot` เท่านั้นใน MVP และเลือกครั้งเดียวทั้ง Plan
- `SuiteLatest` ยังไม่เปิดใน UI เพราะอาจทำให้จำนวน/Revision ของ Case เปลี่ยนจนเปรียบเทียบผลข้าม Build ไม่ได้
- ใช้ `CloneTestCycleRequest` เดิม (รับ `TargetReleaseId/TargetBuildId/TargetEnvironmentId` อยู่แล้ว) ต่อ Run ภายใน Transaction ระดับ Plan (§3.2)
- Run รหัสใหม่ใน Plan ใหม่ใช้ `RunCode` เดิมได้เพราะ Unique ต่อ Plan; Cycle ใหม่ใช้รหัสอัตโนมัติ `<ProjectCode>-CYC-nnn`

สิ่งที่คัดลอกและไม่คัดลอก:

| ข้อมูล | Clone |
|---|---|
| Test Cycle Case, Revision snapshot, Priority, ลำดับ | คัดลอก (พฤติกรรม `SourceSnapshot` เดิม) |
| `RunCode`, `RunName`, `ConfigurationKey/Name/Json`, `OwnerUserId` ของ Run | คัดลอก (แก้ได้ใน Preview) |
| `TestPlanRunAssignments` (ทีมระดับ Run) | คัดลอกเป็นค่าเริ่มต้น โดย `AssignedAt/AssignedBy` เป็นผู้ Clone และเวลาที่ Clone |
| ผู้รับผิดชอบรายเคส (`AssignedTesterUserId`) | **ไม่คัดลอก** — เคสใหม่ว่างทั้งหมด (พฤติกรรมเดิมของ Clone Cycle) ใช้ Bulk assign จาก Run-level Assignment ได้หลัง Clone |
| TestExecution, StepResult, Defect, Evidence, `TestCycleCaseAssignment` history | **ไม่คัดลอก** |
| `IsFrozen`, `LifecycleStatus` | ไม่คัดลอก — Plan ใหม่เริ่ม `Active` และไม่ Frozen |

การ Clone ต้องแสดง Preview ทุก Run ก่อน Confirm และรายการที่สร้างใหม่ต้องมี `TestPlans.CopiedFromTestPlanId` และ `TestCycles.CopiedFromTestCycleId` (มีอยู่แล้ว) สำหรับ Traceability

ตัวอย่าง payload:

```json
{
  "targetReleaseId": "...",
  "targetBuildId": "...",
  "cloneMode": "SourceSnapshot",
  "environmentOverrides": [
    { "sourceRunId": "...", "targetEnvironmentId": "..." }
  ],
  "planName": "Release 2026.11 Regression Plan"
}
```

ผลลัพธ์ต้องคืน Plan ใหม่พร้อม Matrix ใหม่ และเปิดหน้า Detail ของ Plan ใหม่หลังสำเร็จ

### 4.5 Permission และ Scope

เพิ่ม Permission ใหม่เพื่อให้แยกสิทธิ์ได้ แต่ Default Role ให้ map กับสิทธิ์เดิม:

| Permission | Default mapping |
|---|---|
| `TESTPLAN.VIEW` | QA_LEAD, QA_TESTER, RELEASE_OWNER, VIEWER |
| `TESTPLAN.EDIT` | SYS_ADMIN, QA_LEAD |
| `TESTPLAN.ASSIGN` | `EXECUTION.ASSIGN` |
| `TESTPLAN.FREEZE` | SYS_ADMIN, QA_LEAD, RELEASE_OWNER |
| `TESTPLAN.EXPORT` | `REPORT.EXPORT` |

การเริ่ม/บันทึกผลการทดสอบยังใช้ `EXECUTION.RUN` เดิม ไม่เพิ่ม `TESTPLAN.RUN` ซ้ำซ้อน

`TESTPLAN.FREEZE` ครอบคลุม `close`, `cancel`, `reopen`, `freeze` และ `unfreeze` เพราะ Close/Cancel Freeze Plan ด้วย

Permission ใหม่ต้อง seed ผ่าน EF Migration แยก (เช่น `SeedTestPlanPermissions`) ตาม pattern `SeedCrmPermissions` และเพิ่มใน `DatabaseInitializer` (`PermissionCodes` + `DefaultRolePermissions`); SYS_ADMIN ได้ทุก Permission ผ่าน `PermissionCodes` อยู่แล้ว และเพิ่ม Policy `TestPlanView/Edit/Assign/Freeze/Export` ใน `Program.cs`

VIEWER ได้ `TESTPLAN.VIEW` และเห็นหน้า List/Detail/Matrix แบบ Read-only แต่ไม่เห็น Create/Edit/Assign/Close/Freeze และไม่สามารถเปิด Execution เพื่อบันทึกผลได้ถ้าไม่มี `EXECUTION.RUN`

ทุก Endpoint ต้องใช้ `Authorize(Policy=...)` และ `RequireProjectAccess` หรือ Project Scope Guard ตามรูปแบบปัจจุบัน

## 5. UI/UX Plan ให้สอดคล้องกับ UI เดิม

### 5.1 ตำแหน่งใน Navigation

เพิ่มเมนู `Test Plan` ในกลุ่ม `TEST EXECUTION` ก่อน `Test Cycle`:

```text
TEST EXECUTION
  Test Plan
  Test Cycle
  Execution Workspace
  Defect
  Regression
  Automation
```

เพิ่ม Page id `test-plans` ใน `shared/appShared.tsx` และ lazy import ใน `App.tsx`

### 5.2 หน้า Test Plan List — `#/test-plans`

ใช้ pattern เดิมของ `TestCyclesPage` และ `TestSuitesPage`:

```text
Page Header
  Test Plan
  คำอธิบายสั้น
  [ลบแล้ว] [Export] [สร้าง Test Plan]

Filter Card
  Project context จาก Topbar
  Release
  Status chips: ทั้งหมด / Draft / กำลังทำ / เสร็จ / ปิดแล้ว
  Plan Type
  Owner
  Search

Summary Chips
  Plans · Active Runs · Progress · Open Failures

Table / table-cards
  Plan Code / Name
  Release
  Runs
  Progress
  Pass Rate
  Owner
  Status
  Actions
```

กฎ UI:

- ใช้ `content-card`, `table`, `table-cards`, `Badge`, `ModalShell` และ Dialog กลางที่มีอยู่
- ใช้ Design Token ใน `styles.css` ห้ามสร้างสีหรือ Card style ใหม่ซ้ำ
- Primary action มีเพียง `สร้าง Test Plan`
- กดแถวเปิด Detail; ปุ่ม Action รองใช้ icon button ที่มี `aria-label`
- Filter ต้องโหลดด้วยคำขอเดียว และไม่ยิง API แยกต่อ Status chip
- Mobile ≤ 760px เปลี่ยนตารางเป็น Card โดยให้ Plan Code/Name, Status, Progress และ Action เห็นก่อน
- ไม่ทำให้เกิด horizontal scroll ระดับหน้า

### 5.3 หน้า Test Plan Detail

แนะนำให้ใช้ Route เดียวกับ List และเปิด Detail เป็นหน้าหรือ panel ตาม pattern ของ Test Suite/Release:

```text
Plan Header
  eyebrow: TEST PLAN
  Plan Code · Plan Name
  Release · Owner · Date Range
  Status Badge · Frozen Badge
  [Edit] [Clone/Rerun] [Freeze] [Export]

Progress Strip
  Total · Untested · In Progress · Passed · Failed · Blocked · Skipped · Retest
  แถบ Progress = Touched/Total และ Completion = Completed/Total แยกกัน
  Retest แสดงจำนวน Case ที่มีผลจบแล้วมากกว่า 1 ครั้ง (§3.4); เปิดรายละเอียดดูจำนวน RetestAttempts ได้

Tabs
  Matrix
  Runs
  Activity

Matrix tab
  Filter Configuration / Run Status / Owner
  Matrix rows:
    Run / Configuration | Build | Environment | Owner | Progress | Result | Action

Run detail
  Test Cycle reference
  Suite / Case count
  Assigned testers
  Build / Environment
  Open Execution Workspace
```

### 5.4 Create/Edit Plan Modal

ใช้ `ModalShell` และ section แบบฟอร์มเดิม:

1. ข้อมูลหลัก: Plan Name, Plan Code, Type, Release, Owner
2. ช่วงเวลา: Start Date, End Date
3. หมายเหตุ
4. สรุป Validation ก่อน Save

Create Run ใช้ Modal แยก:

- เลือก `สร้าง Cycle ใหม่จาก Suite` หรือ `เพิ่ม Cycle ที่มีอยู่`
- เลือก Build และ Environment เฉพาะที่สัมพันธ์กับ Release
- Configuration Name และ Configuration details
- Owner และ Run-level Tester
- Checkbox `Bulk assign เฉพาะเคสที่ยังไม่ถูก assign`

ห้ามใช้ Form เดียวที่ยาวจนต้องเลื่อนทั้งหน้าใน Desktop และ Mobile

### 5.5 การเชื่อมกับหน้าเดิม

`TestCyclesPage` เพิ่มเฉพาะข้อมูลเสริม:

- Column/Badge `Plan` เมื่อ Cycle อยู่ใน Plan
- Filter `Plan`
- Action `เพิ่มเข้า Test Plan` สำหรับ Cycle ที่ยังไม่อยู่ Plan
- Detail แสดง `อยู่ใน Plan: <Plan Code>` พร้อมลิงก์กลับ

`ExecutionWorkspacePage` ไม่เปลี่ยน Layout หรือ Result Flow ใน Release แรก แต่เพิ่ม Context metadata:

- Plan Code / Run Name ใน Context strip ถ้า Cycle อยู่ใน Plan
- ถ้า Plan Frozen ให้แสดง Banner และ disable การบันทึกผลพร้อมข้อความที่ทำต่อได้

## 6. Application และ Service Design

เพิ่ม Module ใน Application ชื่อ `TestPlanning` แยกจาก `Execution` แต่เรียกใช้ `ITestCycleRepository`/`TestCycleClone` เดิมผ่าน Application interface ที่เหมาะสม

องค์ประกอบหลัก:

- `TestPlanContracts.cs` — DTO, Request, Summary, Matrix row
- `ITestPlanRepository.cs` — List/Get/Save/Attach/Clone/Counter
- `TestPlanService.cs` — lifecycle, validation, freeze, clone orchestration
- `TestPlanSummaryQuery.cs` — aggregate counters และ matrix query
- `TestPlanScopeGuard.cs` — ตรวจ Project/Release/Cycle/Build/Environment

ไม่ให้ Controller คำนวณ Progress หรืออ่าน EF relation โดยตรง

Transaction boundaries:

- Create Plan + Audit
- Create Cycle from Suite + Attach Run + Assign
- Attach Existing Cycle + Audit
- Clone Plan + Clone Cycles + Attach Runs
- Freeze/Unfreeze Plan + Audit
- Delete Run ที่ยังไม่มี Execution + Audit

## 7. Migration และ Backward Compatibility

### 7.1 Migration strategy

สร้าง EF Migration ใหม่ เช่น `AddTestPlansAndConfigurationMatrix` โดย:

- สร้างตารางใหม่เท่านั้นใน Migration แรก
- ไม่เพิ่ม `TestPlanId` ที่ `TestCycles` เพื่อหลีกเลี่ยงข้อมูลซ้ำและทำให้ Cycle เดิมยัง standalone ได้
- ใช้ FK/Index/Unique constraint ตามข้อ 3
- Apply ด้วยขั้นตอน SQL Server ที่กำหนดใน `SYSTEM_OVERVIEW.md`
- ห้าม `Database:ApplyMigrations=true` ใน Production ตามกฎปัจจุบัน

### 7.2 ข้อมูลเดิม

- Test Cycle เดิมทั้งหมดแสดงในหน้า Test Cycle เหมือนเดิม
- หน้า Test Plan แสดงเฉพาะ Plan ใหม่จนกว่าผู้ใช้จะ Attach Cycle
- ไม่มีการสร้าง Plan อัตโนมัติจากข้อมูลเก่าใน MVP เพื่อไม่เดาความหมายของ Cycle
- API เดิม `/test-cycles`, `/test-cycles/{id}/execution` และ Execution API ต้องไม่เปลี่ยนรูปแบบ Response ที่มีอยู่
- Cycle ที่อยู่คนละ Project/Release ห้าม Attach

### 7.2.1 การลบ Cycle ที่ผูกกับ Plan

ใน MVP ให้ **บล็อกการลบ Test Cycle ที่ผูกกับ Test Plan** เพื่อป้องกัน Matrix และ Historical Report หายหรือมีตัวเลขไม่ครบ:

- `DELETE /test-cycles/{id}` ตอบ `409 TEST_CYCLE_LINKED_TO_PLAN` พร้อม Plan Code/Run Code ที่ต้องจัดการ (เป็น Error ใหม่ของ API เดิม แต่ไม่เปลี่ยนรูปแบบ Response ที่สำเร็จ)
- จุดที่ Soft Delete Test Cycle ในระบบปัจจุบันมีเพียง `TestCycleService.DeleteAsync` — การลบ Test Suite ไม่ลบ Cycle ตาม และ Recycle Bin จัดการเฉพาะ Cycle ที่ถูกลบแล้ว จึงบล็อกจุดเดียวพอ แต่ถ้าเพิ่มช่องทางลบ Cycle ใหม่ต้องเรียก Guard เดียวกัน
- ถ้า Run **ไม่เคยมี Execution เลย** (นับรวม Execution ที่ถูก Soft Delete แล้ว เพราะเคยเป็นผลที่บันทึกจริง) และ Plan ยังไม่ Frozen ผู้มีสิทธิ์ `TESTPLAN.EDIT` ใช้ `DELETE /test-plans/{planId}/runs/{runId}` เพื่อ Detach Run ก่อน แล้วจึงลบ/ปิด Cycle ได้
- ถ้า Run มี Execution แล้ว ห้าม Detach และห้ามลบ Cycle; ให้เปลี่ยน Cycle เป็น `Cancelled` หรือ Plan เป็น `Closed` แทน
- ไม่ซ่อน Run ที่ถูกลบ เพราะจะทำให้ Summary ของ Plan เปลี่ยนย้อนหลังโดยไม่มีเหตุผล
- การลบ Plan เป็น Soft Delete เท่านั้น และ Plan ที่มี Run/Execution จะลบได้เมื่อ `Closed` หรือ `Cancelled` พร้อมเก็บข้อมูลเพื่อ Report/Audit
- Soft Delete Plan **ไม่ปลดล็อก Cycle**: `TestPlanRuns` ยังอยู่ Cycle ในนั้นยังลบไม่ได้และ Attach เข้า Plan อื่นไม่ได้ เพื่อให้ Restore Plan จาก "ลบแล้ว" ได้ครบเหมือนเดิม; Error ให้บอกว่า Plan อยู่ในรายการลบแล้ว
- ถ้าต้องการใช้ Cycle นั้นในแผนใหม่ให้ Clone Cycle/Plan แทน; การ Purge Plan ถาวรเพื่อปลดล็อกไม่อยู่ใน MVP

### 7.3 Freeze guard

ใช้ Guard กลางก่อน Mutation ที่กระทบผล:

```text
Load entity → Resolve TestCycle → Find linked TestPlanRun
→ ถ้า TestPlan.IsFrozen = true ให้หยุดด้วย 409 TEST_PLAN_FROZEN
→ Validate permission → Mutate + Audit
```

Mutation ที่ต้องตรวจอย่างน้อย (อ้างอิงโค้ดปัจจุบัน):

| กลุ่ม | จุดที่ต้องเรียก Guard |
|---|---|
| Execution | `ExecutionService.CreateAsync` (รวม Save Progress) และ `ExecutionService.DeleteAsync` — ใส่ตำแหน่งเดียวกับที่บล็อก Cycle `Completed` อยู่แล้ว |
| Test Cycle | `TestCycleService.UpdateAsync`, `StatusAsync`, `DeleteAsync` |
| Cycle Case | `TestCycleRepository.PopulateAsync` (Create Cycle แบบ Populate จาก Suite) และ endpoint เพิ่ม/ลบ Case ใน Cycle ถ้ามี |
| Assignment | `TestCycleCase.AssignTester` ทุกเส้นทาง: `WorkloadController`, `WeightedAssignmentController` (Auto/Weighted assign) และ Run-level Bulk assign ของ Test Plan |
| Test Plan | `PUT` Plan, เพิ่ม/ถอด/แก้ Run และ Run-level Assign |

ไม่มี Endpoint ย้าย Cycle ไป Build/Environment อื่นในระบบปัจจุบัน (`TestCycle` ไม่มี method เปลี่ยน Build/Environment) จึงไม่ต้องมี Guard ส่วนนี้ — ถ้าเพิ่มในอนาคตต้องเรียก Guard เดียวกัน

แนะนำให้ Guard อยู่ใน Application (เช่น `ITestPlanFreezeGuard.EnsureNotFrozenAsync(cycleId)`) แล้วให้ Service/Controller เดิมเรียก ไม่กระจาย Query ซ้ำในแต่ละ Controller

## 8. แผนการพัฒนาเป็นเฟส

| เฟส | งาน | ผลลัพธ์ |
|---|---|---|
| 0 | Finalize contract, status, formula, permission และ screen spec | Decision complete + reviewed contract |
| 1 | Domain entity, EF configuration, migration, repository และ unit tests | สร้าง/อ่าน Plan และ Run ได้ |
| 2 | API List/Get/Create/Edit/Attach/Matrix พร้อม Scope/Permission/Audit | API ใช้งานได้โดยไม่กระทบ API เดิม |
| 3 | Frontend Test Plan List + Detail + Create/Edit + Matrix | ใช้งานผ่าน UI Desktop/Mobile |
| 4 | Clone/Rerun Plan, Run-level Assignment และ Counter optimization | ใช้ Plan กับ Build ใหม่ได้จริง |
| 5 | Freeze guard เชื่อม Test Cycle/Execution/Assignment | ป้องกัน Historical Result ถูกแก้ |
| 6 | Export Plan Summary และเพิ่ม Plan context ใน Test Cycle/Execution | รายงานและ Navigation เชื่อมครบ |

### 8.1 สถานะรายเฟส

| เฟส | สถานะ | หมายเหตุ |
|---|---|---|
| 0 | เสร็จ | Decision Log §1.4 |
| 1 | เสร็จ (2026-10-29) | ดูรายละเอียดด้านล่าง |
| 2 | เสร็จ (2026-10-29) — Migration ยังไม่ apply ลง `ProMaxx2QA` | ดูรายละเอียดด้านล่าง |
| 3 | เสร็จ (2026-10-29) — Migration apply แล้ว รอทดสอบกับข้อมูลจริงผ่าน UI | ดูรายละเอียดด้านล่างและ `UI_DESIGN_SYSTEM.md` Change Log 2026-10-29 |
| 4 | เสร็จในโค้ด (2026-10-29) — ไม่มี Migration ใหม่, ต้อง restart API | ดูรายละเอียดด้านล่าง |
| 5 | เสร็จในโค้ด (2026-10-29) — ต้อง restart API | ดูรายละเอียดด้านล่าง |
| 6 | เสร็จในโค้ด (2026-10-30) — ต้อง restart API | ดูรายละเอียดด้านล่างและ `UI_DESIGN_SYSTEM.md` Change Log 2026-10-30 |

เฟส 1 ที่ทำแล้ว:

- Domain `ProMaxx2.QA.Domain/TestPlanning/TestPlan.cs` — `TestPlan` (Close/Cancel Freeze อัตโนมัติ, Reopen, Freeze/Unfreeze เฉพาะ Active, `EnsureEditable`, Soft delete), `TestPlanRun` (normalize `ConfigurationKey`, ตรวจ `ConfigurationJson` เป็น object), `TestPlanRunAssignment`
- EF `TestPlanConfiguration.cs` + Migration `20261029132845_AddTestPlansAndConfigurationMatrix` (สร้าง 3 ตารางใหม่เท่านั้น, unique `TestPlanRuns.TestCycleId` ทั้งระบบ)
- Application `ProMaxx2.QA.Application/TestPlanning/TestPlanContracts.cs` — DTO, `ITestPlanRepository`, `TestPlanService` (Create พร้อมรหัส `<ProjectCode>-TP-nnn`, Update ห้ามเปลี่ยน Project/Release/Code, Attach Cycle เดิม)
- Repository `TestPlanRepository.cs` — List (filter ตามสถานะที่คำนวณ + Summary ของทุกแถวที่ตรง filter), Get (Summary + Runs/Matrix rows), สถานะ Plan คำนวณใน Query เดียว (`WithStatus`), Retest จาก Execution ที่ไม่ถูกลบและมีผลจบแล้ว (`RetestCountsQuery`)
- Unit tests `TestPlanTests.cs` 28 รายการ (Domain lifecycle, สูตรตัวนับตามตัวอย่าง §4.1, Attach/Scope/Soft delete, สถานะที่คำนวณ, ToQueryString แปลงเป็น SQL)

เฟส 2 ที่ทำแล้ว (รายละเอียด endpoint ดู `API_SPECIFICATION.md` §16.1):

- `TestPlansController` — List/Get/Matrix/Create/Update/Close/Cancel/Reopen/Freeze/Unfreeze/Delete และ Run (Create จาก Suite หรือ Attach, Get, Update, Detach); ProblemDetails + `code` `TEST_PLAN_*`
- Domain โยน `TestPlanRuleException` พร้อมรหัส (`TestPlanErrorCodes`) แทน `InvalidOperationException` ธรรมดา
- Policy `TestPlanView/Edit/Freeze` (`TestPlanAuthorizationPolicies`), Permission `TESTPLAN.VIEW/EDIT/ASSIGN/FREEZE/EXPORT` ใน `DatabaseInitializer` + Migration `20261029133946_SeedTestPlanPermissions` (ASSIGN/EXPORT ให้ Role ที่มี `EXECUTION.ASSIGN`/`REPORT.EXPORT` อยู่แล้วในฐานข้อมูล)
- `ProjectScopeGuard` ตรวจ route `planId`/`runId`
- Create Run จาก Suite ใช้ `TestCycleService.CreateAsync` เดิม + Attach ใน Transaction เดียว (`RunInTransactionAsync`)
- Unique index ชนตอน Attach พร้อมกัน → 409 `TEST_PLAN_CYCLE_LINKED`/`TEST_PLAN_DUPLICATE_CODE`
- `DELETE /test-cycles/{id}` ของ Cycle ที่ผูก Plan (รวม Plan ที่ลบแล้ว) → 409 `TEST_CYCLE_LINKED_TO_PLAN`
- Audit Log ทุกคำสั่งที่แก้ข้อมูล (`EntityType = TestPlan`) พร้อมเหตุผลของ Close/Cancel/Reopen/Freeze/Unfreeze
- Unit tests เพิ่ม 12 รายการ (lifecycle, frozen, create run จาก suite, detach, ลบ plan/cycle, matrix, scope guard, policy, error mapping)

เฟส 3 ที่ทำแล้ว:

- หน้า `#/test-plans` (`pages/TestPlansPage.tsx` + `TestPlansPage.css`) — เมนู Test Plan ก่อน Test Cycle, สิทธิ์ `TESTPLAN.VIEW`; รายการแบบ `ListPage` (ชิปสถานะ, ตัวกรองประเภท/เจ้าของ, แบ่งหน้า), หน้าต่างรายละเอียด (ความคืบหน้ารวม + Configuration Matrix + Lifecycle ตามสิทธิ์ พร้อมเหตุผล), ฟอร์มแผน และฟอร์ม Run (Attach Cycle / สร้างจาก Suite + Configuration)
- API `GET /test-plans` และ `/matrix` รับ `size` 1–500 ให้ตรงกับ `ListPager` (เดิม 1–100)
- ส่วนที่ต่างจาก §5: ยังไม่มีปุ่ม Export (เฟส 6) และ "ลบแล้ว" (Recycle Bin ยังไม่รองรับ Test Plan), ยังไม่มีแท็บ Activity (ดูได้ที่หน้า Audit Log `EntityType = TestPlan`), Release ใช้ตัวเลือกที่ Topbar แทน filter ในการ์ด (แบบเดียวกับหน้า Test Cycle), ชิปสถานะไม่มีจำนวน

เฟส 4 ที่ทำแล้ว (คำตัดสิน 2026-10-29: Bulk assign วนทีละคน, บันทึกทีมแบบแทนที่ทั้งชุด, Clone ได้ทุกสถานะที่มี Run, วัดประสิทธิภาพด้วยข้อมูลจำลอง):

- `POST /test-plans/{id}/runs/{runId}/assign` (`TESTPLAN.ASSIGN`) — ทีมระดับ Run แทนที่ทั้งชุด, ตรวจสมาชิก Project (`ProjectUsers`), Bulk assign เฉพาะเคส `NotRun` ที่ยังไม่มีผู้รับผิดชอบแบบ round-robin ผ่าน flow เดิม (`TestCycleCase.AssignTester` + `TestCycleCaseAssignment` + `AssignmentHistory` `testplan-round-robin-v1`) ให้ My Work เห็นงาน; Plan Frozen/ปิด ถูกบล็อก; `GET /test-plans/{id}/members` สำหรับตัวเลือก
- `POST /test-plans/{id}/clone` (`TESTPLAN.EDIT`) — ตาม §4.4: Plan ใหม่ + `CopiedFromTestPlanId`, Clone Cycle ทุก Run ด้วย `TestCycleService.CloneAsync` (SourceSnapshot) ไป Target Build เดียว, Environment override ราย Run, คัดลอก Configuration/Owner/ทีม Run, ไม่คัดลอก Execution/ผู้รับผิดชอบรายเคส — ทั้งหมดใน Transaction (ตรวจบน SQL Server: Clone ล้มกลางทางแล้วไม่มี Plan/Cycle ค้าง)
- Counter optimization — วัดบน DB ทดสอบ (60 Plan, 300 Run, 30,000 เคส, 28,292 Execution): เดิม List 137 ms / กรองสถานะ 318 ms (Query สถานะแบบ subquery ต่อ Plan รันซ้ำ 3 รอบ) → ใหม่ List 123 ms / กรองสถานะ 112 ms (นับเคสด้วย `GROUP BY (TestCycleId, CurrentStatus)` ครั้งเดียว ใช้ index `IX_TestCycleCases_TestCycleId` ที่ INCLUDE `CurrentStatus` อยู่แล้ว แล้วรวม/คำนวณสถานะในหน่วยความจำด้วย `PlanStatus` — unit test เทียบสถานะหน้ารายการกับรายละเอียดทุกขั้น); Detail ~45 ms, Matrix ~18 ms — ไม่ต้องเพิ่ม index
- UI: บรรทัดทีม/เคสที่ยังไม่มีผู้รับผิดชอบในแถว Matrix, หน้าต่างจัดทีม, หน้าต่าง Clone/Rerun พร้อม Preview ทุก Run (ดู `UI_DESIGN_SYSTEM.md` Change Log)

เฟส 5 ที่ทำแล้ว (Freeze guard §7.3):

- `ITestPlanFreezeGuard.EnsureNotFrozenAsync(testCycleId)` (Application) + `TestPlanFreezeGuard` (Infrastructure) — Cycle ที่อยู่ใน Plan ที่ `IsFrozen` (นับรวม Plan ที่ปิด/ยกเลิก/ลบแล้ว) โยน `TestPlanRuleException` `TEST_PLAN_FROZEN` พร้อม Plan/Run และสาเหตุ (ถูก Freeze / ปิดแล้ว / ถูกยกเลิก)
- จุดที่เรียก: `ExecutionService.CreateAsync` (รวม Save Progress) และ `DeleteAsync`; `TestCycleService.UpdateAsync`, `StatusAsync`; `WorkloadController` assign/reassign; `WeightedAssignmentController` confirm/rebalance; Run-level assign ของ Test Plan ใช้ `EnsureEditable` อยู่แล้ว
- `TestCycleService.DeleteAsync` **ไม่ใช้ guard** — Cycle ที่อยู่ใน Plan ลบไม่ได้อยู่แล้ว (`TEST_CYCLE_LINKED_TO_PLAN`) และข้อความนั้นบอกทางที่ถูกกว่า (Reopen อย่างเดียวไม่ทำให้ลบได้)
- ไม่มี `PopulateAsync` แยกเป็น endpoint (ใช้ตอนสร้าง Cycle ใหม่ซึ่งยังไม่อยู่ใน Plan) และ Clone Cycle ไม่แก้ Cycle ต้นทาง — จึงไม่ต้องใส่ guard
- `TestPlanRuleExceptionFilter` (MVC filter กลาง) แปลง `TestPlanRuleException` ที่ไม่มี controller จับเป็น 409 ProblemDetails + `code` — endpoint เดิมไม่ต้องเพิ่ม catch และไม่ตอบ 500
- ข้อจำกัด: ตรวจก่อนบันทึก (check-then-act) — คำขอบันทึกผลที่เริ่มก่อนกด Freeze เพียงเสี้ยววินาทีอาจบันทึกสำเร็จ; ไม่ได้ทำ lock ระดับฐานข้อมูลตาม §9.2 "Concurrent Freeze กับ Save Execution"
- Unit tests: บันทึก/ลบผลถูกบล็อกเมื่อ Freeze และใช้ได้หลัง Unfreeze, แก้/เปลี่ยนสถานะ Cycle ของแผนที่ปิดถูกบล็อก, Cycle ที่ไม่อยู่ในแผนไม่ถูกกระทบ, filter แปลงเป็น 409 (ทั้งชุด 926 ผ่าน)

เฟส 6 ที่ทำแล้ว (Export + Plan context §5.5):

- **Export (สิทธิ์ `TESTPLAN.EXPORT`)** — สร้าง CSV ฝั่ง browser (UTF-8 BOM, รูปแบบเดียวกับ Export หน้า Test Cycle/RTM; ไม่มี endpoint ใหม่): ปุ่ม Export ในรายการ = ทุกแผนตามตัวกรอง (ขอทุกหน้าพร้อมกัน หน้าละ 500) และในรายละเอียด = **Plan Summary** (ข้อมูลแผน + ตัวนับรวม §3.4 + Configuration Matrix ราย Run: Configuration/OS/Browser/Device, Cycle, Build, Environment, ทีม, เคสที่ยังไม่มีผู้รับผิดชอบ, ตัวนับ, Progress, Pass Rate) — ตัวเลขชุดเดียวกับหน้าจอเพราะใช้ข้อมูลจาก `GET /test-plans/{id}`
- **API:** `TestCycleDto.plan` และ `ExecutionWorkspaceDto.plan` = `{ testPlanId, planCode, planName, testPlanRunId, runCode, runName, configurationName, lifecycleStatus, isFrozen, isPlanDeleted }` (null = ไม่อยู่ใน Plan) — โหลดเพิ่ม 1 Query ต่อหน้า (`TestCyclePlanLinks`, JOIN `TestPlanRuns`→`TestPlans`) ไม่ใช่ต่อแถว; `GET /test-cycles?plan=none|any|{testPlanId}`
- **หน้า Test Cycle:** บรรทัด "รหัสแผน · Run" ใต้ชื่อ Cycle (ไอคอนแม่กุญแจสีเหลืองเมื่อ Frozen), ตัวกรอง Test Plan (ทุกแผน / ยังไม่อยู่ในแผน / อยู่ในแผน / รายแผน), คอลัมน์ Test Plan/Run ใน CSV; Cycle ในแผนที่ Frozen ปิดปุ่มเริ่ม/ปิด Cycle/แก้ไขพร้อมเหตุผล และ Cycle ที่อยู่ในแผนปิดปุ่มลบ; รายละเอียด Cycle มีแถบล็อก + ส่วน "อยู่ใน Test Plan" พร้อมลิงก์เปิดรายละเอียดแผน (`qa.targetPlanId`) และปุ่ม **"เพิ่มเข้า Test Plan"** (สิทธิ์ `TESTPLAN.EDIT`, Cycle ที่ยังไม่อยู่ในแผนและไม่ Cancelled) — เลือกได้เฉพาะแผน Active ที่ไม่ Freeze ของ Release เดียวกัน, ใช้ `POST /test-plans/{id}/runs/attach-cycle`
- **Execution Workspace:** แถบ "Test Plan · Run · Configuration" ใต้แถวตัวเลือก (ไม่แตะความกว้างคงที่ของ `dl.execution-context`); แผน Frozen/ปิด = แถบเหลืองบอกเหตุผลและทางแก้ (Unfreeze/Reopen) + ปิด Save Progress / Skip / Complete / ลบผล ล่วงหน้า และปุ่มลัด P/F/B แจ้งเตือนแทนการส่ง — Server ยังบล็อกด้วย 409 เป็นชั้นสุดท้าย
- Unit tests: `plan` ใน List/Get และตัวกรอง none/any/planId, Workspace คืน `plan.isFrozen` หลัง Freeze, Query ของ `TestCyclePlanLinks` แปลงเป็น SQL JOIN (ทั้งชุด 929 ผ่าน); UI ตรวจด้วย headless Chrome + mock API ที่ 1440px และ 390px (รายการ/รายละเอียด Cycle, หน้าต่างเพิ่มเข้าแผน, Workspace Frozen, รายละเอียดแผน) ไม่มี horizontal scroll

ค้าง/ยกไปเฟสถัดไป:

- ตรวจบน SQL Server แล้ว (2026-10-29): สร้าง DB ทดสอบ `ProMaxx2QA_TestPlanCheck` บน `MSSQLSERVER2022` apply ทุก Migration → รัน Service/Repository จริง (Create/Attach/Create Run จาก Suite/Get/List filter/Matrix/Retest/Unique index ชน → `TEST_PLAN_CYCLE_LINKED`/ลบ Cycle ที่ผูก → 409/Freeze/Cancel/Delete/Audit) ผ่าน และตรวจ `SeedTestPlanPermissions` ทั้ง Up/Down กับ Role จริง (ASSIGN/EXPORT ตาม Role ที่มี `EXECUTION.ASSIGN`/`REPORT.EXPORT`) แล้วลบ DB ทดสอบทิ้ง
- ยังไม่มี Integration test แบบ HTTP (Concurrent Attach/Freeze ตาม §9.2) — Concurrent Attach พึ่ง Unique index + mapping 409
- Restart API เพื่อใช้งานเฟส 4–6 แล้วทดสอบตาม §9.4 กับข้อมูลจริง (ยังไม่ได้ทำ)
- Freeze guard เป็น check-then-act (ดูข้อจำกัดเฟส 5) — ยังไม่ได้ทำ lock ระดับฐานข้อมูล
- ยังไม่มีแท็บ Activity ในรายละเอียดแผน (ดูที่ Audit Log `EntityType = TestPlan`) และ Recycle Bin ยังไม่รองรับ Test Plan

ลำดับ Release แนะนำ:

- Release A: เฟส 1–3 — Plan/Run/Matrix แบบไม่ Freeze
- Release B: เฟส 4 — Clone/Rerun/Assignment
- Release C: เฟส 5–6 — Freeze/Export/Context integration

## 9. Test Plan

### 9.1 Domain/Unit Tests

- สร้าง Plan สำเร็จและปฏิเสธ Plan Code ซ้ำใน Project เดียวกัน
- ปฏิเสธ Release ที่ไม่อยู่ Project เดียวกัน
- Attach Cycle สำเร็จเมื่อ Project/Release ตรงกัน
- ปฏิเสธ Cycle ที่ผูกกับ Plan อื่น
- คำนวณ Total/Progress/Pass Rate ตามสถานะจริง
- คำนวณ Retest จาก Execution ที่ไม่ถูกลบและมีผลจบแล้วมากกว่า 1 แถวโดยไม่เพิ่ม Total Case
- Save Progress (`InProgress`) แล้วจบผล 1 ครั้งต้อง**ไม่**นับเป็น Retest และ Execution ที่ถูกลบไม่ถูกนับ
- สถานะ Plan ที่คำนวณได้ถูกต้องทุกกรณี: ไม่มี Run, Run Draft ที่มี Case ถูกแตะ, ทุก Run Terminal, Closed, Cancelled
- Freeze แล้ว Mutation ที่กระทบผลถูกปฏิเสธ
- Close/Cancel ทำให้ Frozen อัตโนมัติ; Unfreeze บน Plan ที่ Closed/Cancelled ถูกปฏิเสธ; Reopen คืน Active และ Unfreeze
- Unfreeze ต้องมี Permission และ Audit
- Detach Run ถูกปฏิเสธเมื่อ Run เคยมี Execution (รวมที่ถูกลบ) และลบ Cycle ที่ผูก Plan (รวม Plan ที่ถูก Soft Delete) ได้ 409
- Clone Plan สร้าง Cycle/Run ใหม่โดยไม่คัดลอก Execution History

### 9.2 API/Integration Tests

- Project Scope: อ่าน/Attach/แก้ Plan ข้าม Project ไม่ได้
- Role Matrix: `TESTPLAN.VIEW/EDIT/ASSIGN/FREEZE/EXPORT` ทำงานตาม Permission และการบันทึกผลยังใช้ `EXECUTION.RUN`; VIEWER อ่านได้แต่แก้ไม่ได้
- Freeze guard ครอบคลุมทุกจุดใน §7.3 รวม Workload/Weighted assignment
- Concurrent Attach Cycle: สำเร็จได้เพียงคำขอเดียวเมื่อชน Unique constraint
- Concurrent Freeze กับ Save Execution: ต้องได้ผลลัพธ์ deterministic และไม่เขียนผลหลัง Freeze
- List/Matrix ไม่ยิง N+1 และคืน Summary ตรงกับ Rows
- Soft Delete ไม่ทำให้ Report ของ Execution เดิมหาย
- Error contract ใช้ ProblemDetails และรหัส `TEST_PLAN_*`

### 9.3 Frontend Tests

- List filter, search, status chip และ pagination
- Create/Edit validation และ error mapping
- Attach Existing Cycle / Create from Suite
- Matrix drill-down เปิด Run detail และ Execution Workspace ถูก Cycle
- Clone/Rerun แสดง Build/Environment ใหม่ถูกต้อง
- Frozen Plan disable action และแสดงข้อความชัดเจน
- Mobile card layout ที่ 390px และ Tablet ที่ 768px ไม่มี page-level horizontal scroll
- Loading/Empty/Error state แยกกัน และไม่ล้างข้อมูลเดิมระหว่าง refresh

### 9.4 Manual Acceptance

ตรวจอย่างน้อยที่ 1440px, 1024px, 768px และ 390px ตาม `UI_DESIGN_SYSTEM.md`:

1. สร้าง Plan จาก Project/Release ปัจจุบัน
2. เพิ่ม Cycle เดิม 2–3 รายการต่าง Build/Environment
3. สร้าง Run ใหม่จาก Suite
4. Assign Tester ระดับ Run และ Bulk assign เคสที่ยังว่าง
5. เปิด Execution Workspace จาก Matrix
6. บันทึกผลและตรวจ Counter/Progress ของ Plan
7. Clone Plan ไป Build ใหม่และยืนยันว่าไม่มี Execution History ถูกคัดลอก
8. Freeze Plan แล้วทดสอบบันทึกผล/แก้ Cycle/Assign
9. Export/เปิด Report และตรวจ Project Scope

## 10. ความเสี่ยงและการควบคุม

| ความเสี่ยง | ผลกระทบ | การควบคุม |
|---|---|---|
| Counter ซ้ำระหว่าง Plan/Run/Cycle | Report ผิด | ใช้ Cycle/Execution เป็น Source of Truth และ Query กลาง |
| Freeze ไม่ครอบคลุมทุก Mutation | Historical Result ถูกแก้ | ใช้ Guard กลางและ Integration Test ครบทุก Endpoint |
| Attach Cycle ผิด Release | Matrix และ Release Gate ผิด | Validate Project/Release/Build/Environment ฝั่ง Server |
| Plan Detail ยิง API จำนวนมาก | หน้าช้าเมื่อมีหลาย Run | Summary + Matrix ใน Query แบบ aggregate และ pagination |
| UI ใหม่แตกต่างจากหน้าปัจจุบัน | ผู้ใช้เรียนรู้ใหม่ | ใช้ component/token/pattern เดิมจาก Test Cycle/Test Suite |
| ConfigurationJson ใช้ค่าไม่เป็นมาตรฐาน | Filter/Report ต่อไม่ได้ | บังคับ `ConfigurationKey`, จำกัด JSON schema ระดับพื้นฐาน และเก็บชื่อที่อ่านได้ |
| Clone คัดลอกผลย้อนหลังโดยไม่ตั้งใจ | Audit/Release ผิด | Clone เฉพาะ Cycle Case snapshot และไม่คัดลอก TestExecution |

## 11. Definition of Done

- [ ] มี Migration และ EF Model Snapshot ที่ build ผ่าน รวม Migration seed Permission `TESTPLAN.*`
- [ ] Existing Test Cycle/Execution API และหน้าเดิมทำงานเหมือนเดิม
- [ ] สร้าง Plan, Attach Cycle, Create Run และดู Matrix ได้
- [ ] Summary Counter ตรงกับข้อมูลจริงและไม่มี N+1 query
- [ ] Project Scope, Permission และ Audit ครบ
- [ ] Clone/Rerun ไม่คัดลอก Execution History
- [ ] Freeze Guard ครอบคลุม Execution, Cycle และ Assignment
- [ ] UI สอดคล้องกับ Design System และผ่าน Desktop/Mobile acceptance
- [ ] Backend unit/integration tests และ frontend build/lint/test ผ่าน
- [ ] อัปเดต `SCREEN_SPECIFICATION.md`, `API_SPECIFICATION.md`, `SQL_SERVER_SCHEMA.md` และ `SYSTEM_OVERVIEW.md` เมื่อเริ่ม implementation จริง

## 12. Progress Log

| วันที่ | รายการ | สถานะ |
|---|---|---|
| 2026-10-29 | สร้างแผน Test Plan / Configuration Matrix จากโครงสร้าง Test Cycle/Execution ปัจจุบัน | Draft |
| 2026-10-29 | ตัดสินใจเรื่อง InProgress, Retest, Plan lifecycle, Clone/Rerun, Permission และการลบ Cycle ที่ผูก Plan | Decision complete |
| 2026-10-29 | Review รอบ 2 เทียบโค้ดจริง: Retest ไม่ใช้ `ExecutionNo` (นับเฉพาะผลจบที่ไม่ถูกลบ), แก้ตัวอย่าง Summary JSON ให้ลงตัว, เก็บ `LifecycleStatus` แทน `Status` และคำนวณสถานะใน Query, Close/Cancel Freeze อัตโนมัติ + Unfreeze ได้เฉพาะ Active + Reopen, Clone เพิ่ม `CopiedFromTestPlanId` + ตารางสิ่งที่คัดลอก + Transaction ระดับ Plan, Freeze guard อ้าง Service/Controller จริง (รวม Workload/Weighted assignment) และตัด "Move Build/Environment", Soft Delete Plan ยังล็อก Cycle, Seed Permission ผ่าน Migration | Decision complete |
| 2026-10-29 | เฟส 1: Domain/EF/Migration `AddTestPlansAndConfigurationMatrix`/Application service/Repository + unit tests 28 รายการ (ทั้งชุด 905 ผ่าน); อัปเดต `SQL_SERVER_SCHEMA.md` §8.x; Migration ยังไม่ apply | เฟส 1 เสร็จ |
| 2026-10-29 | เฟส 2: `TestPlansController` + Policy/Permission `TESTPLAN.*` (Migration `SeedTestPlanPermissions`) + Lifecycle/Run endpoint + บล็อกลบ Cycle ที่ผูก Plan + Error code `TEST_PLAN_*` + unit tests 12 รายการ (ทั้งชุด 917 ผ่าน); อัปเดต `API_SPECIFICATION.md` §16.1; ตรวจ Query/Migration บน SQL Server ใน DB ทดสอบชั่วคราวผ่าน (ลบทิ้งแล้ว) | เฟส 2 เสร็จ — Migration ยังไม่ apply ลง ProMaxx2QA |
| 2026-10-29 | เฟส 3: หน้า Test Plan (รายการ/รายละเอียด/Matrix/ฟอร์มแผน/ฟอร์ม Run/Lifecycle) + เมนู + สิทธิ์; ตรวจ build/lint และ headless Chrome + mock API ที่ 1440/1024/390px ไม่มี horizontal scroll; การ apply Migration ลง ProMaxx2QA ถูกระบบ permission ปฏิเสธ (ทรัพยากรที่ใช้ร่วม) — รอผู้ใช้อนุญาตหรือ apply เอง | เฟส 3 เสร็จในโค้ด |
| 2026-10-29 | ผู้ใช้อนุญาต: apply `AddTestPlansAndConfigurationMatrix` + `SeedTestPlanPermissions` ลง `ProMaxx2QA` (sqlcmd -b -I ผ่าน PowerShell — Git Bash แปลง argument ผิดจน sqlcmd แจ้ง -E/-U ชนกัน) และ restart API แบบ Production; ตรวจ `/health` 200, `/test-plans` 401 ทั้ง local และโดเมนสาธารณะ, Vite เสิร์ฟไฟล์ใหม่; สิทธิ์ ASSIGN/EXPORT ตาม Role จริง (QA_TESTER ได้ด้วยเพราะมี `EXECUTION.ASSIGN`/`REPORT.EXPORT` อยู่แล้ว) | Migration apply แล้ว |
| 2026-10-29 | เฟส 4: Run-level Assignment (+ members), Clone/Rerun Plan, Counter optimization (List กรองสถานะ 318 → 112 ms ที่ 30,000 เคส), UI จัดทีม/Clone; unit tests ทั้งชุด 922 ผ่าน; ตรวจ Clone/Assign บน SQL Server ใน DB ทดสอบ (ลบแล้ว) | เฟส 4 เสร็จในโค้ด — รอ restart API |
| 2026-10-29 | เฟส 5: Freeze guard ใน Execution (บันทึก/ลบ), Test Cycle (แก้/สถานะ), Assignment (Workload, Weighted) + exception filter กลาง 409 `TEST_PLAN_FROZEN`; การ restart API ถูกระบบ permission บล็อก (Production Deploy) — รอผู้ใช้ restart | เฟส 5 เสร็จในโค้ด |
| 2026-10-30 | เฟส 6: Export (รายการแผน + Plan Summary เป็น CSV), `plan` ใน `TestCycleDto`/`ExecutionWorkspaceDto` + ตัวกรอง `plan` ของ `GET /test-cycles`, หน้า Test Cycle (บรรทัดแผน, ตัวกรอง, ลิงก์ไปแผน, "เพิ่มเข้า Test Plan", ปิดปุ่มเมื่อ Frozen), Execution Workspace (แถบแผน + Banner Frozen ปิดปุ่มบันทึกล่วงหน้า); แก้บรรทัดสถานะที่ยังเขียนว่า "รอ commit" | เฟส 6 เสร็จในโค้ด — รอ restart API |
