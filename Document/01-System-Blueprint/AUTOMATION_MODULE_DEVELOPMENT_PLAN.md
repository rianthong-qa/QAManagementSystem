# ProMaxx2 QA Hub - Automation Module & Central Windows Agent Development Plan

**Document Type:** Development Plan / Technical Blueprint  
**System:** ProMaxx2 QA Hub  
**Official Name:** ProMaxx2 Quality Assurance Management System  
**Scope:** Automation Module + AI Test Interpreter + Central Windows Agent + ProMaxx2 Windows Automation  
**Version:** 1.0  
**Status:** Proposed for Development  

---

## 1. วัตถุประสงค์

เอกสารฉบับนี้กำหนดแผนพัฒนาส่วน **Automation** ให้เป็น Module ใหม่ภายใน ProMaxx2 QA Hub โดยยังใช้ Core QA Management เดิมร่วมกัน ได้แก่ Requirement, Test Case, Test Data, Test Suite, Test Cycle, Test Execution, Evidence, Defect, Regression, Build, Environment และ Release Readiness

Automation Module มีเป้าหมายหลักดังนี้

1. อ่าน Test Case ที่ QA สร้างไว้ใน QA Hub
2. ใช้ AI แปลง Test Case ภาษาคนเป็น Automation DSL ที่เป็นภาษากลางของระบบ
3. Validate ว่า Automation DSL สามารถ Execute ได้จริง
4. ส่ง Automation Job ไปยัง Central Windows Agent
5. ให้ Agent ควบคุม ProMaxx2 Windows Application บนเครื่องทดสอบ
6. ตรวจสอบผลทั้ง UI, Business Flow, Database และ API ตามที่ Test Case กำหนด
7. เก็บผลการทดสอบ, Screenshot, Log, SQL Result และ Evidence กลับเข้าสู่ QA Hub
8. เชื่อมผล Fail เข้ากับ Defect Management
9. รองรับ Regression Suite และการรันหลายเครื่องพร้อมกันในอนาคต
10. รองรับ AI Failure Analysis โดยไม่ให้ AI เป็นผู้ตัดสิน Release โดยตรง

หลักการสำคัญคือ **Automation จะเป็น Module ใหม่ภายใน QA Hub ไม่ใช่ระบบแยก** และจะใช้ Traceability เดิมร่วมกับระบบหลัก

```text
Requirement
   -> Test Case
      -> Manual Execution
      -> Automation Case
         -> Automation Execution
            -> Evidence
            -> Defect
            -> Retest
            -> Regression
               -> Release Readiness
```

---

## 2. ขอบเขตจากระบบเดิม

ระบบปัจจุบันมี Core Traceability ดังนี้

```text
Requirement -> Test Scenario -> Test Case -> Test Cycle -> Test Result
-> Defect -> Retest -> Regression -> Test Summary -> Release Sign-off
```

Automation Module ต้องต่อเข้ากับ Flow นี้โดยไม่ทำให้ Historical Execution เดิมเสียหาย

ข้อมูลเดิมที่นำกลับมาใช้ได้โดยตรง ได้แก่

- Projects / Modules
- Releases / Builds
- Requirements / RTM
- TestScenarios
- TestCases
- TestCaseRevisions
- TestSteps
- TestData
- TestEnvironments
- TestSuites
- TestCycles
- TestExecutions
- TestStepResults
- Defects
- Attachments
- AuditLogs
- Users / Roles / Permissions

Field สำคัญใน Test Case ที่มีอยู่แล้วคือ

```text
AutomationCandidate
```

Field นี้จะใช้เป็นจุดเริ่มต้นในการคัดเลือก Test Case ที่เหมาะสมสำหรับ Automation

---

# 3. Target Architecture

## 3.1 ภาพรวม

```text
                         ProMaxx2 QA Hub

              Web Frontend / Automation Module
                           |
                           v
                   ASP.NET Core .NET 10 API
                           |
       +-------------------+-------------------+
       |                   |                   |
       v                   v                   v
 Test Management      Automation Core        Defect
       |                   |                   |
       |             +-----+------+            |
       |             |            |            |
       v             v            v            |
   Test Case     AI Interpreter  Validator      |
                     |            |             |
                     +------v-----+             |
                         Job Queue              |
                            |                   |
                            v                   |
                   Agent Communication         |
                            |                   |
              REST / SignalR / WebSocket       |
                            |                   |
============================|================================
                            |
                            v
              ProMaxx2 Central Windows Agent
                            |
          +-----------------+------------------+
          |                 |                  |
          v                 v                  v
   UI Automation      DB Validator       Evidence Collector
          |                 |                  |
          +-----------------+------------------+
                            |
                            v
                      ProMaxx2.exe
                            |
                 Firebird / SQL Server
```

---

## 3.2 แบ่ง Responsibility

### QA Hub รับผิดชอบ

- Test Case Management
- Automation Case Management
- AI Test Interpretation
- Automation DSL
- Action Library
- Object Repository Metadata
- Automation Validation
- Job Queue
- Agent Management
- Execution Orchestration
- Result Management
- Evidence Metadata
- Defect Integration
- Regression Integration
- Dashboard / Coverage
- Audit / Permission

### Central Windows Agent รับผิดชอบ

- Register เครื่องกับ QA Hub
- Heartbeat
- รับ Automation Job
- Download Execution Package
- เปิด / ปิด ProMaxx2
- ตรวจสอบ Process
- ควบคุม Windows UI
- Execute Action ตาม DSL
- Query Database ตาม Validator ที่กำหนด
- Capture Screenshot
- เก็บ Automation Log
- เก็บ Application / Windows Event Log ตาม Policy
- ส่ง Step Result กลับ QA Hub
- Upload Evidence
- Cleanup Test Session

### ProMaxx2 Windows Application

ProMaxx2.exe ไม่ควรต้องรู้จัก QA Hub โดยตรงใน Phase แรก

การ Automation จะควบคุมผ่าน Windows UI Automation Layer และ Database/API Validation จากภายนอก

ข้อดีคือไม่ต้องแก้ Production Code ของ ProMaxx2 เพื่อรองรับ Automation ตั้งแต่เริ่มต้น

---

# 4. Automation Module ใน QA Hub

## 4.1 เมนูหลัก

```text
Automation
|
+-- Dashboard
+-- Automation Cases
+-- AI Generator
+-- Automation Suites
+-- Execution
|   +-- Run Automation
|   +-- Execution Queue
|   +-- Running Jobs
|   +-- Run History
|
+-- Design
|   +-- Action Library
|   +-- Object Repository
|   +-- Test Data Mapping
|   +-- Database Validators
|
+-- Infrastructure
|   +-- Agents
|   +-- Test Machines
|   +-- Agent Health
|
+-- Evidence
+-- Maintenance
+-- Settings
    +-- AI Provider / Model
    +-- Timeout
    +-- Retry
    +-- Screenshot Policy
    +-- Log Policy
```

---

# 5. Automation Case

Automation Case เป็นตัวเชื่อมระหว่าง Test Case ภาษาคนกับ Automation Runtime

ตัวอย่าง

```text
Test Case
TC-SALE-001
ขายสินค้าเกิน Stock

        |
        v
Automation Case
AUT-SALE-001

        |
        +-- Version 1
        +-- Version 2
        +-- Version 3
```

หนึ่ง Test Case ควรมี Automation Case หลักหนึ่งรายการ แต่มี Automation Version ได้หลาย Version

---

## 5.1 Automation Lifecycle

```text
Draft
  |
  v
AI Generated
  |
  v
Needs Review
  |
  v
Validated
  |
  v
Approved
  |
  v
Ready
  |
  +------> Running
              |
        +-----+-----+
        |           |
       Pass        Fail
        |           |
        +-----+-----+
              |
      Maintenance Required
```

### Maintenance Required Trigger

Automation ต้องถูกเปลี่ยนเป็น Maintenance Required เมื่อพบเงื่อนไข เช่น

- Test Case Revision ใหม่
- Object Repository ที่ใช้อยู่ถูกเปลี่ยน
- Action Library Version เปลี่ยนแบบ Breaking Change
- Screen ของ ProMaxx2 เปลี่ยน
- AutomationId ของ Control เปลี่ยน
- Validator SQL ถูกแก้
- Test Data Mapping ใช้ไม่ได้
- Run Fail ซ้ำจาก `ObjectNotFound`

---

# 6. AI Test Interpreter

## 6.1 หลักการ

AI **ไม่ควร Generate C# แล้ว Execute โดยตรง**

AI ทำหน้าที่แปลง Test Case เป็น Automation DSL เท่านั้น

```text
Test Case Natural Language
          |
          v
       AI Model
          |
          v
    Automation DSL
          |
          v
      Validator
          |
          v
      Human Review
          |
          v
        Ready
```

แนวทางนี้ช่วยให้สามารถเปลี่ยน AI Model ได้โดยไม่ต้องเปลี่ยน Automation Runtime

ตัวอย่าง Model Provider ในอนาคต

- OpenAI
- Local LLM
- DeepSeek
- Qwen
- Kimi
- OpenRouter Compatible Model

AI Provider ต้องอยู่หลัง Interface กลาง เช่น

```csharp
public interface IAutomationAiProvider
{
    Task<AutomationGenerationResult> GenerateAsync(
        AutomationGenerationContext context,
        CancellationToken cancellationToken);
}
```

---

## 6.2 Input ที่ส่งให้ AI

ควรส่งเฉพาะข้อมูลที่จำเป็น

```text
Project
Module
Test Case Code
Title
Objective
Preconditions
Test Steps
Test Data
Expected Result
Available Actions
Available Screens
Known Objects
Known Business Terms
```

AI ไม่ควรได้รับ Credential จริง เช่น Password Production หรือ Connection String จริง

---

## 6.3 AI Output

AI ต้องคืน Structured Output

ตัวอย่าง

```json
{
  "testCaseCode": "TC-SALE-001",
  "automationType": "WindowsUI",
  "steps": [
    {
      "stepNo": 1,
      "action": "LOGIN",
      "parameters": {
        "userRef": "QA_STANDARD_USER"
      }
    },
    {
      "stepNo": 2,
      "action": "OPEN_MENU",
      "parameters": {
        "menu": "SALES"
      }
    },
    {
      "stepNo": 3,
      "action": "NEW_DOCUMENT",
      "parameters": {
        "documentType": "SALES"
      }
    },
    {
      "stepNo": 4,
      "action": "SELECT_ITEM",
      "parameters": {
        "itemCode": "A001"
      }
    },
    {
      "stepNo": 5,
      "action": "SET_QTY",
      "parameters": {
        "value": 20
      }
    },
    {
      "stepNo": 6,
      "action": "SAVE_DOCUMENT"
    }
  ],
  "assertions": [
    {
      "type": "EXPECT_MESSAGE",
      "messageKey": "STOCK_NOT_ENOUGH"
    },
    {
      "type": "EXPECT_DOCUMENT_NOT_CREATED"
    }
  ]
}
```

---

# 7. Automation DSL

DSL เป็น Contract ระหว่าง QA Hub กับ Agent

DSL ต้อง

- อ่านง่าย
- Validate ได้
- Version ได้
- Serialize เป็น JSON ได้
- ไม่ผูกกับ UI Framework โดยตรง
- ไม่ผูกกับ AutomationId โดยตรง
- รองรับ Action / Assertion / Variable / Condition ในอนาคต

ตัวอย่าง DSL แบบอ่านง่าย

```text
LOGIN user=QA_STANDARD_USER
OPEN_MENU menu=SALES
NEW_DOCUMENT type=SALES
SELECT_ITEM code=A001
SET_QTY value=20
SAVE_DOCUMENT
EXPECT_MESSAGE key=STOCK_NOT_ENOUGH
EXPECT_DOCUMENT_NOT_CREATED
```

Runtime จริงควรใช้ JSON Schema หรือ Typed Model มากกว่า Parse Plain Text โดยตรง

---

## 7.1 DSL Version

ทุก Automation Version ต้องระบุ DSL Version

```text
DslVersion = 1.0
```

เพื่อรองรับ Breaking Change ในอนาคต

Agent ต้องประกาศ Version ที่รองรับ เช่น

```text
Agent Version        : 1.2.0
Supported DSL        : 1.0, 1.1
Supported App        : ProMaxx2
```

---

# 8. Action Library

Action Library เป็นรายการคำสั่งที่ Agent รองรับ

## 8.1 Authentication

```text
LOGIN
LOGOUT
SWITCH_USER
```

## 8.2 Navigation

```text
OPEN_MENU
OPEN_SCREEN
CLOSE_SCREEN
WAIT_SCREEN
```

## 8.3 Document

```text
NEW_DOCUMENT
SEARCH_DOCUMENT
SAVE_DOCUMENT
APPROVE_DOCUMENT
CANCEL_DOCUMENT
DELETE_DOCUMENT
```

## 8.4 Item

```text
SELECT_ITEM
SET_QTY
SET_PRICE
SET_DISCOUNT
SET_LOT
REMOVE_ITEM
```

## 8.5 Generic UI

```text
CLICK
SET_TEXT
SELECT_COMBO
CHECK
UNCHECK
PRESS_KEY
WAIT_OBJECT
```

Generic Action ควรใช้เมื่อไม่มี Business Action ที่เหมาะสม

Business Action เช่น `SAVE_DOCUMENT` ควรถูกเลือกก่อน `CLICK SAVE_BUTTON` เพราะมีความหมายเชิงระบบชัดกว่า

---

## 8.6 Validation Actions

```text
EXPECT_MESSAGE
EXPECT_VALUE
EXPECT_TEXT
EXPECT_ENABLED
EXPECT_DISABLED
EXPECT_VISIBLE
EXPECT_NOT_VISIBLE
EXPECT_DOCUMENT_CREATED
EXPECT_DOCUMENT_NOT_CREATED
EXPECT_DB_VALUE
EXPECT_STOCK
EXPECT_LOT
EXPECT_TRANSACTION
```

---

# 9. Object Repository

Object Repository เป็น Mapping ระหว่างชื่อเชิง Business กับ Windows Control จริง

ตัวอย่าง

```text
Business Object
Sales.Save

        |
        v
Windows Object
AutomationId = btnSave
ControlType  = Button
```

Agent ห้ามให้ Automation Case อ้าง `btnSave` ตรง ๆ ถ้าไม่จำเป็น

ควรใช้

```text
Sales.Save
```

---

## 9.1 Object Model

```text
Application
  +-- Module
      +-- Screen
          +-- Object
```

ตัวอย่าง

```text
ProMaxx2
+-- Sales
    +-- SalesEntry
        +-- ItemCode
        +-- Quantity
        +-- Price
        +-- Save
        +-- Approve
```

---

## 9.2 Selector Strategy

ลำดับการเลือก Selector แนะนำ

1. AutomationId
2. Framework-specific stable property
3. Name + ControlType
4. Parent/Child relationship
5. Index - ใช้เป็นทางเลือกสุดท้าย

ห้ามใช้ Coordinate Click เป็น Default

```text
Mouse.Click(500,300)
```

ควรหลีกเลี่ยงเพราะ DPI, Resolution และ Window Position เปลี่ยนได้

---

# 10. Automation Validator

Automation ต้องผ่าน Validator ก่อนเข้าสถานะ Ready

Validator แบ่งเป็นหลายระดับ

## 10.1 Schema Validation

ตรวจ

- DSL Version
- Required field
- Parameter type
- Step number
- Duplicate ID

## 10.2 Action Validation

ตรวจว่า Action มีใน Action Library หรือไม่

```text
SET_QTY -> Found
SAVE_DOCUMENT -> Found
CONFIRM_SALE_X -> Not Found
```

## 10.3 Object Validation

ตรวจว่า Object ที่ Action ต้องใช้มี Mapping หรือไม่

```text
Sales.Quantity -> Found
Sales.Save     -> Found
```

## 10.4 Test Data Validation

ตรวจ Reference เช่น

```text
ITEM_A001
QA_STANDARD_USER
TEST_BRANCH_001
```

## 10.5 Agent Capability Validation

ก่อน Dispatch Job ต้องตรวจว่า Agent รองรับ

```text
Windows UI Automation   = Yes
Firebird Validation     = Yes
SQL Server Validation   = Yes
Video Capture           = No
```

---

# 11. Central Windows Agent

ชื่อแนะนำ

```text
ProMaxx2.Automation.Agent
```

Agent เป็นตัวกลางที่ติดตั้งบน Windows Test Machine

---

## 11.1 Process Model

ควรแยกอย่างน้อย 2 Process

```text
Windows Service
ProMaxx2.Automation.Agent.Service

       |
       v
Interactive Runner
ProMaxx2.Automation.Runner.exe
```

เหตุผลคือ Windows Service ปกติไม่ควรใช้ควบคุม Desktop UI โดยตรง เนื่องจาก Session Isolation

### Service รับผิดชอบ

- Register
- Heartbeat
- Receive Job
- Update Agent
- Start Runner ใน Interactive Session
- Report Machine Health

### Runner รับผิดชอบ

- UI Automation
- Launch ProMaxx2
- Execute DSL
- Screenshot
- DB Validation
- Result Collection

---

# 12. Agent Registration

ครั้งแรกที่ Agent เริ่มทำงาน

```text
Agent
  -> POST /api/automation/agents/register
```

ข้อมูลตัวอย่าง

```json
{
  "agentCode": "QA-PC01",
  "machineName": "QA-PC01",
  "agentVersion": "1.0.0",
  "os": "Windows 11",
  "architecture": "x64",
  "capabilities": [
    "WindowsUI",
    "Firebird",
    "SQLServer",
    "Screenshot"
  ]
}
```

Server คืน

```json
{
  "agentId": "...",
  "registrationStatus": "Approved",
  "heartbeatSeconds": 15
}
```

---

# 13. Agent Heartbeat

Agent ต้องส่ง Heartbeat เช่นทุก 15-30 วินาที

```json
{
  "agentId": "...",
  "status": "Idle",
  "currentExecutionId": null,
  "cpuPercent": 15,
  "memoryPercent": 42,
  "interactiveSessionAvailable": true,
  "promaxx2Running": false
}
```

สถานะ

```text
Offline
Online
Idle
Busy
Unhealthy
Maintenance
Disabled
```

---

# 14. Job Queue

QA Hub เป็นเจ้าของ Queue

Agent ไม่ควรเลือก Test Case เอง

Flow

```text
QA User
  |
  v
Run Automation
  |
  v
Create Automation Execution
  |
  v
Create Automation Job
  |
  v
Queue
  |
  v
Scheduler
  |
  v
Agent
```

Job Status

```text
Queued
Assigned
Preparing
Running
Passed
Failed
Blocked
Cancelled
Timeout
AgentLost
```

---

# 15. Execution Package

ก่อน Agent Run ระบบควรสร้าง Execution Package ที่ Immutable สำหรับ Run นั้น

ประกอบด้วย

```text
ExecutionId
AutomationCaseId
AutomationVersionId
TestCaseRevisionNo
BuildId
EnvironmentId
DSL
Resolved Test Data References
Object Repository Version
Action Library Version
Timeout Policy
Evidence Policy
```

ห้าม Agent ดึง Automation Case ล่าสุดระหว่าง Run เพราะข้อมูลอาจถูกแก้กลางทาง

---

# 16. การคุยระหว่าง QA Hub กับ Agent

แนะนำ Hybrid Communication

## Command / Metadata

ใช้ REST API

## Realtime Status

ใช้ SignalR / WebSocket

ตัวอย่าง

```text
QA Hub
   |
   +-- REST -> Register / Job / Result / Evidence Metadata
   |
   +-- SignalR -> Heartbeat / Job Notification / Step Progress
```

ถ้า Network ไม่เสถียร Agent ต้องสามารถ Queue Result ชั่วคราวใน Local Storage แล้ว Retry Upload ได้

---

# 17. Agent Execution Flow

```text
Receive Job
   |
   v
Validate Execution Package
   |
   v
Check Interactive Session
   |
   v
Check Environment
   |
   v
Kill Previous Test Instance if Policy Allows
   |
   v
Start ProMaxx2.exe
   |
   v
Wait Main Window
   |
   v
Execute Preconditions
   |
   v
Execute Automation Steps
   |
   +----> Step Result
   +----> Screenshot on Fail
   +----> Log
   |
   v
Execute Assertions
   |
   v
Database/API Validation
   |
   v
Finalize Result
   |
   v
Upload Evidence
   |
   v
Cleanup
```

---

# 18. Step Execution

ทุก Step ต้องมี Result แยก

```text
Step 1 LOGIN
Status      : Pass
StartedAt   : ...
CompletedAt : ...
Duration    : 840 ms

Step 2 OPEN_MENU SALES
Status      : Pass
Duration    : 520 ms

Step 5 SAVE_DOCUMENT
Status      : Fail
ErrorType   : ObjectNotFound
```

Agent ต้องส่ง Step Progress กลับ QA Hub เพื่อให้ UI แสดง Running แบบ Realtime

---

# 19. UI Automation Technology

สำหรับ C# / Windows แนะนำวาง Abstraction Layer ก่อน

```csharp
public interface IUiAutomationDriver
{
    Task LaunchAsync(...);
    Task ClickAsync(...);
    Task SetTextAsync(...);
    Task<string?> GetTextAsync(...);
    Task<bool> ExistsAsync(...);
}
```

Implementation Phase แรกสามารถใช้

```text
FlaUI
Microsoft UI Automation
```

อย่าให้ Business Action เรียก FlaUI โดยตรงทุกจุด

ควรผ่าน Layer

```text
Action Handler
    -> Object Repository Resolver
        -> UI Driver
            -> FlaUI / UIA
```

---

# 20. Business Action Handler

ตัวอย่าง

```text
SAVE_DOCUMENT
```

ไม่ควรแปลเป็น Click อย่างเดียว

Handler อาจทำ

```text
1. Resolve Sales.Save
2. Wait Until Enabled
3. Click
4. Wait Busy Indicator
5. Wait Save Complete
6. Detect Message/Dialog
7. Return Action Result
```

ทำให้ Test Case มีความเสถียรกว่าการเขียน Low-Level UI Step จำนวนมาก

---

# 21. Database Validation

Agent รองรับ Database Validation แต่ Credential ต้องมาจาก Secure Configuration ไม่ใช่ AI Output

ตัวอย่าง

```text
EXPECT_STOCK
ItemCode = A001
Expected = 10
```

Runtime

```text
EXPECT_STOCK
   |
   v
Stock Validator Handler
   |
   v
Resolve Database Profile
   |
   v
Parameterized Query
   |
   v
Compare
```

AI ไม่จำเป็นต้อง Generate SQL เองใน Use Case ทั่วไป

SQL Custom Validator สามารถรองรับภายหลังโดยต้องผ่าน Review

---

# 22. Evidence

ใช้ Attachment / Evidence Architecture เดิมของ QA Hub

Evidence Type ที่ Agent สร้างได้

```text
Screenshot
Video
Automation Log
Application Log
Windows Event Log
SQL Result
API Result
Crash Dump Reference
```

Policy ตัวอย่าง

```text
ScreenshotOnStep       = FailOnly
ScreenshotOnAssertion  = Always
VideoCapture           = Disabled
ApplicationLog         = Enabled
WindowsEventLog        = OnCrash
```

Evidence File จริงควรเก็บใน File/Object Storage และ SQL Server เก็บ Metadata ตาม Architecture เดิม

---

# 23. Result Mapping เข้าระบบ Test Execution เดิม

Automation Execution ควรสร้าง / Link `TestExecution`

เพิ่ม Field แนะนำ

```text
ExecutionType

Manual
Automation
```

ดังนั้น Dashboard, RTM, Regression และ Test Summary สามารถรวมผล Manual + Automation ได้

```text
Test Cycle
  +-- Manual TestExecution
  +-- Automation TestExecution
```

Historical Execution ห้ามถูกเขียนทับตาม Rule เดิม

---

# 24. Defect Integration

เมื่อ Automation Fail ต้องแยกก่อนว่า Fail Type คืออะไร

```text
ApplicationFailure
AutomationFailure
EnvironmentFailure
TestDataFailure
AssertionFailure
AgentFailure
Unknown
```

ไม่ควรสร้าง Defect ให้ ProMaxx2 อัตโนมัติทุก Fail

ตัวอย่าง

```text
ObjectNotFound
```

อาจเป็น Automation Maintenance ไม่ใช่ Product Defect

Flow

```text
Automation Fail
     |
     v
Failure Classification
     |
 +---+--------------------+
 |                        |
Product Failure     Automation/Environment
 |                        |
 v                        v
QA Review          Maintenance / Retry
 |
 v
Create Defect
```

---

# 25. AI Failure Analyzer

AI Failure Analyzer เป็น Phase หลังจาก Execution เสถียรแล้ว

Input

```text
Test Case
Expected Result
Automation DSL
Step Results
Actual UI Message
Screenshot Metadata
Application Log
DB Validation Result
```

Output ตัวอย่าง

```json
{
  "classification": "PotentialProductDefect",
  "confidence": 0.86,
  "summary": "Application exception occurred before stock validation",
  "recommendation": "QAReviewBeforeCreateDefect"
}
```

AI Analysis เป็น Recommendation เท่านั้น

QA ต้องเป็นผู้ Confirm ก่อน Create Defect

---

# 26. Database Design - ตารางใหม่

## 26.1 AutomationCases

```text
AutomationCases
- AutomationCaseId
- TestCaseId
- AutomationCode
- AutomationType
- Status
- CurrentVersionNo
- OwnerUserId
- IsAiGenerated
- CreatedAt
- CreatedBy
- UpdatedAt
- UpdatedBy
- IsDeleted
```

Unique

```text
AutomationCode
TestCaseId + IsDeleted(false)
```

---

## 26.2 AutomationVersions

```text
AutomationVersions
- AutomationVersionId
- AutomationCaseId
- VersionNo
- TestCaseRevisionNo
- DslVersion
- DslJson
- GeneratedByAi
- AiProvider
- AiModel
- AiConfidence
- ValidationStatus
- ApprovedBy
- ApprovedAt
- ChangeReason
- CreatedAt
- CreatedBy
```

Execution ต้องอ้าง Version นี้โดยตรง

---

## 26.3 AutomationActions

```text
AutomationActions
- AutomationActionId
- ActionCode
- ActionName
- Category
- Description
- ParameterSchemaJson
- HandlerKey
- MinimumAgentVersion
- IsActive
- CreatedAt
- UpdatedAt
```

---

## 26.4 AutomationObjects

```text
AutomationObjects
- AutomationObjectId
- ProjectId
- ModuleId
- ApplicationCode
- ScreenCode
- ObjectCode
- ObjectName
- ControlType
- AutomationId
- SelectorJson
- ObjectVersion
- IsActive
- CreatedAt
- UpdatedAt
```

---

## 26.5 AutomationAgents

```text
AutomationAgents
- AgentId
- AgentCode
- MachineName
- AgentVersion
- OperatingSystem
- Architecture
- Status
- LastHeartbeatAt
- CurrentExecutionId
- RegisteredAt
- ApprovedBy
- IsEnabled
```

---

## 26.6 AutomationAgentCapabilities

```text
AutomationAgentCapabilities
- AgentId
- CapabilityCode
- CapabilityVersion
```

---

## 26.7 AutomationExecutions

```text
AutomationExecutions
- AutomationExecutionId
- AutomationCaseId
- AutomationVersionId
- TestExecutionId
- AgentId
- BuildId
- EnvironmentId
- JobId
- Status
- StartedAt
- CompletedAt
- DurationMs
- FailureType
- ErrorCode
- ErrorMessage
- CreatedAt
```

---

## 26.8 AutomationStepResults

```text
AutomationStepResults
- AutomationStepResultId
- AutomationExecutionId
- StepNo
- ActionCode
- Status
- StartedAt
- CompletedAt
- DurationMs
- ActualResult
- ErrorCode
- ErrorMessage
```

---

## 26.9 AutomationJobs

```text
AutomationJobs
- JobId
- AutomationExecutionId
- Priority
- RequestedAgentId
- AssignedAgentId
- Status
- QueuedAt
- AssignedAt
- StartedAt
- CompletedAt
- RetryCount
- LastError
```

---

# 27. Index แนะนำ

```text
AutomationCases(TestCaseId, Status)
AutomationVersions(AutomationCaseId, VersionNo)
AutomationObjects(ProjectId, ModuleId, ScreenCode, ObjectCode)
AutomationAgents(Status, LastHeartbeatAt)
AutomationJobs(Status, Priority, QueuedAt)
AutomationExecutions(AutomationCaseId, StartedAt)
AutomationExecutions(TestExecutionId)
AutomationStepResults(AutomationExecutionId, StepNo)
```

---

# 28. API Draft

## Automation Case

```text
GET    /api/automation/cases
GET    /api/automation/cases/{id}
POST   /api/automation/cases
PUT    /api/automation/cases/{id}
POST   /api/automation/cases/{id}/generate
POST   /api/automation/cases/{id}/validate
POST   /api/automation/cases/{id}/approve
POST   /api/automation/cases/{id}/run
```

## Action Library

```text
GET    /api/automation/actions
POST   /api/automation/actions
PUT    /api/automation/actions/{id}
```

## Object Repository

```text
GET    /api/automation/objects
POST   /api/automation/objects
PUT    /api/automation/objects/{id}
POST   /api/automation/objects/{id}/verify
```

## Agent

```text
POST   /api/automation/agents/register
POST   /api/automation/agents/{id}/heartbeat
GET    /api/automation/agents
GET    /api/automation/agents/{id}
POST   /api/automation/agents/{id}/enable
POST   /api/automation/agents/{id}/disable
```

## Job

```text
GET    /api/automation/jobs
POST   /api/automation/jobs/{id}/cancel
POST   /api/automation/jobs/{id}/retry
```

## Result

```text
POST   /api/automation/executions/{id}/start
POST   /api/automation/executions/{id}/steps/{stepNo}/result
POST   /api/automation/executions/{id}/complete
POST   /api/automation/executions/{id}/evidence
```

---

# 29. Security

## 29.1 Agent Authentication

Agent ห้ามใช้ Username/Password ของ QA User

ควรใช้ Agent Credential แยก เช่น

```text
AgentId + Client Secret
```

หรือ Certificate-based Authentication ในอนาคต

Secret ต้องเก็บใน Windows Protected Storage / DPAPI หรือ equivalent

---

## 29.2 Credential สำหรับ ProMaxx2 Test User

Automation DSL ใช้ Reference

```text
QA_STANDARD_USER
```

ไม่เก็บ Password ใน DSL

Agent Resolve จาก Secure Secret Store

---

## 29.3 Database Credential

ใช้ Environment Profile Reference

```text
QA_FIREBIRD_DB_01
```

ห้ามใส่ Connection String ลง AI Prompt

---

## 29.4 Permission ใหม่

เสนอ Permission

```text
Automation.View
Automation.Create
Automation.Edit
Automation.GenerateAI
Automation.Validate
Automation.Approve
Automation.Execute
Automation.Cancel
Automation.ManageActions
Automation.ManageObjects
Automation.ManageAgents
Automation.ViewEvidence
Automation.Admin
```

---

# 30. Audit

ต้อง Audit อย่างน้อย

- Create Automation Case
- Generate AI
- AI Model / Provider ที่ใช้
- Edit DSL
- Approve Automation
- Change Object Repository
- Change Action Library
- Run
- Cancel
- Retry
- Agent Enable/Disable
- Manual Result Override

Historical Automation Version ที่เคย Run แล้วห้าม Hard Delete

---

# 31. Error Handling

Error Code ควรเป็นมาตรฐาน เช่น

```text
AUT-AGENT-001 Agent Offline
AUT-AGENT-002 Interactive Session Missing
AUT-APP-001 Application Start Failed
AUT-APP-002 Main Window Not Found
AUT-UI-001 Object Not Found
AUT-UI-002 Object Disabled
AUT-UI-003 Action Timeout
AUT-DB-001 Database Connection Failed
AUT-DB-002 Validation Query Failed
AUT-AI-001 AI Generation Failed
AUT-DSL-001 DSL Validation Failed
AUT-JOB-001 Job Timeout
AUT-JOB-002 Agent Lost
```

Result ต้องแยก Technical Error กับ Test Assertion Fail

---

# 32. Timeout / Retry

Retry ต้องระวังไม่ให้เกิด Side Effect เช่นบันทึกเอกสารซ้ำ

แบ่ง Action

```text
SafeToRetry
UnsafeToRetry
ConditionalRetry
```

ตัวอย่าง

```text
WAIT_SCREEN       SafeToRetry
OPEN_MENU         SafeToRetry
SAVE_DOCUMENT     UnsafeToRetry
APPROVE_DOCUMENT  UnsafeToRetry
```

---

# 33. Test Data Reset

ระบบเดิมมี `ResetInstruction` ใน Test Data อยู่แล้ว

Automation ควรใช้ข้อมูลนี้ต่อยอด

```text
Pre-run Reset
   -> Execute Test
      -> Post-run Cleanup
```

Phase แรกสามารถให้ QA กำหนด Reset แบบ Manual/Script Reference ก่อน

ไม่ควรให้ AI Generate Data Destructive SQL แล้ว Execute อัตโนมัติ

---

# 34. Environment Requirement สำหรับ Windows Agent

เครื่อง Test Machine ควรกำหนด Baseline

```text
Windows Version
Display Scale
Resolution
ProMaxx2 Version
Database Profile
Required Runtime
Agent Version
Interactive User
```

ก่อน Run ให้ Agent ทำ Preflight

```text
[PASS] Windows Session
[PASS] Agent Version
[PASS] ProMaxx2.exe
[PASS] Database Reachable
[PASS] Required Resolution
[PASS] Object Repository Compatible
```

Fail Preflight -> Blocked ไม่ใช่ Product Fail

---

# 35. Agent Health Dashboard

ตัวอย่าง

| Agent | Status | App Version | Current Job | Last Heartbeat |
|---|---|---|---|---|
| QA-PC01 | Idle | 10.2.9 | - | 5 sec ago |
| QA-PC02 | Busy | 10.2.9 | AUT-10024 | 3 sec ago |
| QA-PC03 | Offline | - | - | 18 min ago |

---

# 36. Automation Dashboard

Metric แนะนำ

```text
Total Test Cases
Automation Candidate
Automation Generated
Ready
Maintenance Required
Running
Pass Today
Fail Today
Automation Coverage
Average Duration
Agent Online / Offline
```

Coverage

```text
Automation Coverage = Ready Automated Test Cases / Active Test Cases
```

ควรแยก

```text
Candidate Coverage
Ready Coverage
Execution Coverage
```

---

# 37. Regression Integration

Test Suite เดิมยังเป็นเจ้าของชุด Test Case

เมื่อสร้าง Cycle ระบบตรวจแต่ละ Test Case

```text
มี Automation Ready -> สามารถ Run Automation
ไม่มี Automation -> Assign Manual QA
```

ตัวอย่าง

```text
Sales Regression

TC-SALE-001  Automation Ready
TC-SALE-002  Automation Ready
TC-SALE-003  Manual
TC-SALE-004  Maintenance Required
```

ผลทั้งหมดเข้า Test Cycle เดียวกัน

---

# 38. Release Readiness

Automation ไม่เปลี่ยน Release Gate เดิม

Release Readiness ต้องคำนวณจาก Source Data เหมือนเดิม

สิ่งที่เพิ่มคือ Source ของ TestExecution อาจเป็น

```text
Manual
Automation
```

AI ห้าม Override GO / NO-GO

---

# 39. Project Structure แนะนำ

## QA Hub Backend

```text
src/
+-- ProMaxx2.QAHub.Api
+-- ProMaxx2.QAHub.Application
+-- ProMaxx2.QAHub.Domain
+-- ProMaxx2.QAHub.Infrastructure
|
+-- ProMaxx2.QAHub.Automation.Application
+-- ProMaxx2.QAHub.Automation.Domain
+-- ProMaxx2.QAHub.Automation.Infrastructure
+-- ProMaxx2.QAHub.Automation.Contracts
+-- ProMaxx2.QAHub.Automation.AI
```

## Agent

```text
agent/
+-- ProMaxx2.Automation.Agent.Service
+-- ProMaxx2.Automation.Runner
+-- ProMaxx2.Automation.Core
+-- ProMaxx2.Automation.Contracts
+-- ProMaxx2.Automation.UI
+-- ProMaxx2.Automation.Database
+-- ProMaxx2.Automation.Actions
+-- ProMaxx2.Automation.Evidence
+-- ProMaxx2.Automation.Diagnostics
```

## Tests

```text
tests/
+-- ProMaxx2.QAHub.Automation.UnitTests
+-- ProMaxx2.QAHub.Automation.IntegrationTests
+-- ProMaxx2.Automation.Agent.UnitTests
+-- ProMaxx2.Automation.Actions.Tests
+-- ProMaxx2.Automation.E2E.Tests
```

---

# 40. Development Roadmap

## Phase G0 - Preparation

### เป้าหมาย
กำหนด Contract ก่อนเริ่ม Automation จริง

### งาน

- Finalize Automation terminology
- เพิ่ม Automation เป็น Main Module
- เพิ่ม Permission
- กำหนด ExecutionType
- กำหนด Automation Status
- กำหนด DSL v1
- กำหนด Agent Protocol

### Deliverable

```text
Automation Architecture Approved
DSL v1
API Contract Draft
DB Migration Draft
```

---

## Phase G1 - Automation Foundation

### QA Hub

- Automation Cases List
- Automation Case Detail
- Link TestCase
- Version History
- Status Workflow
- Manual DSL Editor

### Database

- AutomationCases
- AutomationVersions
- ExecutionType

### Acceptance

QA สามารถสร้าง Automation Case จาก Test Case และเก็บ DSL Version ได้

---

## Phase G2 - Action Library + Object Repository

### งาน

- Action Library CRUD
- Object Repository CRUD
- Screen/Object hierarchy
- Object Verification Tool Prototype
- Action Schema Validation

### Acceptance

ระบบ Validate ได้ว่า DSL อ้าง Action/Object ที่มีจริง

---

## Phase G3 - Validator

### งาน

- DSL Schema Validator
- Action Validator
- Object Validator
- Test Data Validator
- Environment Validator

### Acceptance

Automation ที่ Validation Fail ห้ามเข้า Ready

---

## Phase G4 - Windows Agent MVP

### งาน

- Agent Service
- Agent Registration
- Heartbeat
- Interactive Runner
- Launch ProMaxx2
- FlaUI/UIA Driver
- Execute CLICK / SET_TEXT / WAIT / EXPECT_TEXT
- Screenshot

### Acceptance

QA Hub สามารถสั่ง Agent เปิด ProMaxx2 และ Execute Demo Case ได้

---

## Phase G5 - Business Actions

เริ่มจาก Module ขนาดเล็กหรือ Flow ที่เสถียรก่อน

แนะนำ Action ชุดแรก

```text
LOGIN
OPEN_MENU
OPEN_SCREEN
NEW_DOCUMENT
SELECT_ITEM
SET_QTY
SAVE_DOCUMENT
EXPECT_MESSAGE
```

### Acceptance

สามารถ Automation Test Case จริงของ ProMaxx2 อย่างน้อย 5-10 Cases ได้

---

## Phase G6 - Execution Queue + Result

### งาน

- Job Queue
- Agent Assignment
- Running Status
- Step Result
- Timeout
- Cancel
- Retry Policy
- TestExecution Integration

### Acceptance

ผล Automation ถูกบันทึกเป็น TestExecution เดิมและแสดงใน Cycle ได้

---

## Phase G7 - Evidence + Database Validation

### งาน

- Screenshot Upload
- Automation Log
- SQL Result
- Firebird Validator
- SQL Server Validator
- Evidence Viewer

### Acceptance

Fail Case มี Evidence ที่ Trace กลับ Execution ได้ครบ

---

## Phase G8 - AI Generator

### งาน

- AI Provider abstraction
- Prompt Template
- Structured Output
- Available Action Context
- Object Repository Context
- Confidence
- Human Review

### Acceptance

AI สามารถ Generate DSL จาก Test Case และผ่าน Validator ใน Use Case ที่กำหนด

---

## Phase G9 - Defect / Failure Classification

### งาน

- Failure Type
- Product vs Automation Failure
- Create Defect from Automation Result
- AI Failure Analyzer Prototype

### Acceptance

Automation Fail ไม่สร้าง Defect ผิดประเภทโดยอัตโนมัติ

---

## Phase G10 - Regression / Multi-Agent

### งาน

- Automation Suite View
- Multi-Agent Scheduler
- Parallel Run
- Regression Result Merge
- Automation Coverage Dashboard

### Acceptance

สามารถกระจาย Regression ไปหลาย Agent และรวมผลใน Test Cycle เดียวกัน

---

# 41. MVP Scope ที่แนะนำ

เพื่อไม่ให้ Automation Module ใหญ่เกินไปในครั้งแรก

## Automation MVP

```text
1. Automation Case
2. Automation Version
3. DSL v1
4. Action Library
5. Object Repository
6. Validator
7. Agent Registration / Heartbeat
8. Windows Runner
9. Basic UI Actions
10. Job Queue แบบเครื่องเดียวก่อน
11. Step Result
12. Screenshot on Fail
13. TestExecution Integration
```

ยังไม่จำเป็นใน MVP

```text
AI Generator
AI Failure Analyzer
Video
Self-healing Object
Parallel Agent Scheduler
Custom SQL Generator
CI/CD Trigger
```

**หมายเหตุ:** แม้เป้าหมายสุดท้ายต้องการ AI เป็นตัวกลาง แต่ควรให้ Runtime/DSL/Agent เสถียรก่อน แล้วค่อยเสียบ AI เข้ามา เพื่อให้รู้ได้ชัดว่า Fail มาจาก AI Translation หรือ Automation Runtime

---

# 42. Pilot Module แนะนำ

ไม่ควรเริ่ม Full Regression ทั้งระบบทันที

เลือก Module ที่

- UI ค่อนข้างเสถียร
- Test Data สร้างง่าย
- Expected Result ชัด
- ไม่มี External Device ซับซ้อน

Pilot 10-20 Test Cases

ตัวอย่าง Flow

```text
Login
Search Item
Open Sales
Add Item
Set Qty
Save
Validate Message
```

หลัง Pilot ผ่านจึงขยายไป

```text
Sales
Inventory
Purchase
Lot
Permission
Regression
```

---

# 43. QA Strategy สำหรับ Automation Framework เอง

Automation System ก็ต้องถูก Test

## Unit Test

- DSL Parser
- Validator
- Action Handler
- Selector Resolver
- Result Mapper

## Integration Test

- API -> Queue
- Queue -> Agent
- Agent -> Result
- Evidence Upload
- DB Validator

## End-to-End

```text
Create Test Case
-> Generate/Create Automation
-> Validate
-> Run
-> Agent Open App
-> Execute
-> Return Result
-> Create TestExecution
```

## Fault Test

- Agent Offline
- Network Disconnect
- App Crash
- Dialog unexpected
- Object missing
- DB unavailable
- Hub Restart
- Agent Restart

---

# 44. Definition of Done - Automation Case

Automation Case ถือว่า Ready เมื่อ

- Linked Test Case Revision ถูกต้อง
- DSL ผ่าน Schema Validation
- Actions ทั้งหมด Active
- Objects ทั้งหมด Resolve ได้
- Test Data Reference ครบ
- Environment Compatible
- ได้รับ Approval ตาม Permission
- Agent Capability รองรับ

---

# 45. Definition of Done - Automation Execution

Execution ถือว่าสมบูรณ์เมื่อ

- มี Start / Complete Timestamp
- ทุก Step มี Status
- Assertion มี Actual Result
- Failure มี Failure Type
- Evidence Policy ทำงานครบ
- TestExecution ถูกสร้าง/อัปเดต
- Audit ถูกบันทึก
- Agent กลับ Idle หรือมีเหตุผลถ้า Unhealthy

---

# 46. ความเสี่ยงหลัก

## UI ไม่รองรับ AutomationId ที่เสถียร

ผลกระทบ: Object หาไม่เจอเมื่อ UI เปลี่ยน

แนวทาง: Object Repository + Selector หลายระดับ + ร่วมกับ Dev เพิ่ม AutomationId ในจุดสำคัญถ้าจำเป็น

## Windows Session Isolation

ผลกระทบ: Windows Service ควบคุม Desktop ไม่ได้

แนวทาง: แยก Agent Service กับ Interactive Runner

## Test Data ไม่สะอาด

ผลกระทบ: Test เดิม Run ซ้ำแล้วได้ผลไม่เหมือนกัน

แนวทาง: Test Data Reset / Dedicated Dataset

## AI แปลผิด

ผลกระทบ: Run ผิด Flow

แนวทาง: Validator + Human Approval + Restricted Action Library

## Retry ทำรายการซ้ำ

ผลกระทบ: ข้อมูลผิด

แนวทาง: Action Retry Classification

## Automation Fail ถูกมองเป็น Product Defect

แนวทาง: Failure Classification ก่อน Defect Creation

---

# 47. แนวทางการพัฒนาที่แนะนำ

ลำดับที่ควรทำจริง

```text
Core QA Hub พร้อม Test Execution
        |
        v
Automation Case
        |
        v
DSL
        |
        v
Action Library
        |
        v
Object Repository
        |
        v
Validator
        |
        v
Windows Agent MVP
        |
        v
Run ProMaxx2 จริง
        |
        v
Result + Evidence
        |
        v
Database Validation
        |
        v
AI Generator
        |
        v
AI Failure Analyzer
        |
        v
Multi-Agent Regression
```

อย่าเริ่มจาก AI ก่อน Agent Runtime เพราะจะ Debug ยากว่า Error เกิดจาก AI, DSL, Selector, UI หรือ Application

---

# 48. Target End-to-End Flow

```text
QA สร้าง Requirement
        |
        v
สร้าง Test Case
        |
        v
AutomationCandidate = Yes
        |
        v
กด Generate Automation
        |
        v
AI อ่าน Test Case
        |
        v
Generate DSL
        |
        v
Validator
        |
        v
QA Review / Approve
        |
        v
Automation Ready
        |
        v
เลือก Build + Environment + Agent
        |
        v
Create Job
        |
        v
Central Windows Agent
        |
        v
Launch ProMaxx2.exe
        |
        v
Execute UI Actions
        |
        v
Validate UI + DB + API
        |
        v
Collect Evidence
        |
        v
Send Result to QA Hub
        |
   +----+----+
   |         |
 PASS       FAIL
   |         |
   |     Failure Classification
   |         |
   |     QA Review / Defect
   |         |
   +----+----+
        |
        v
Regression / RTM
        |
        v
Release Readiness
```

---

# 49. Phase แรกที่ควรเริ่มทำต่อจากระบบปัจจุบัน

แนะนำ Sprint แรกของ Automation Module ให้ทำเพียง Foundation ดังนี้

1. เพิ่ม `Automation` เป็น Main Menu ระดับเดียวกับ Test Management และ Defect Management
2. เพิ่ม `ExecutionType = Manual | Automation` ใน Test Execution
3. เพิ่มตาราง `AutomationCases`
4. เพิ่มตาราง `AutomationVersions`
5. สร้างหน้า Automation Cases
6. สร้างหน้า Automation Case Detail
7. Link Test Case / Test Case Revision
8. ทำ DSL v1 แบบ Typed JSON
9. สร้าง Action Library ชุดแรก 8-10 Actions
10. สร้าง Object Repository Schema
11. สร้าง Validator ก่อน Agent
12. ทำ Agent Prototype ให้เปิด ProMaxx2 และหา Main Window ให้สำเร็จ

เมื่อ 12 ข้อนี้ทำได้แล้ว ค่อยต่อ Execution Queue และ Business Actions จริง

---

# 50. สรุป

Automation Module ควรเป็นส่วนหนึ่งของ ProMaxx2 QA Hub และใช้ข้อมูล Core QA เดิมร่วมกันทั้งหมด

```text
QA Hub = Control Plane
Agent  = Execution Plane
AI     = Translation / Analysis Layer
ProMaxx2 = System Under Test
```

บทบาทต้องแยกชัดเจน

```text
QA Hub
- รู้ว่าจะทดสอบอะไร
- รู้ Test Case / Build / Environment / Result

AI
- แปล Test Case -> DSL
- ช่วยวิเคราะห์ Fail

Agent
- รู้ว่าจะ Execute DSL อย่างไรบน Windows

ProMaxx2
- เป็น Application ที่ถูกทดสอบ
```

หลักการนี้ทำให้สามารถขยายระบบในอนาคตได้โดยไม่ผูก AI เข้ากับ Windows UI โดยตรง และช่วยรักษา Traceability เดิมของ QA Hub ตั้งแต่ Requirement จนถึง Release Sign-off

---

# 51. เอกสารต้นทางที่ใช้ประกอบแผน

เอกสารนี้ต่อยอดจาก Blueprint ปัจจุบันของระบบ ได้แก่

- `REQUIREMENTS.md`
- `DATABASE_DESIGN.md`
- `WORKFLOW.md`
- `README(2).md` - System Blueprint

ส่วน Automation Module, AI Interpreter, Automation DSL, Object Repository, Agent Protocol และ Windows Runner เป็นส่วนขยายที่เสนอเพิ่มจาก Core Blueprint เดิม

