# ProMaxx2 QA Management — SQL Server Schema

> เป้าหมาย: โครงสร้างฐานข้อมูลสำหรับระบบ QA Management ของ ProMaxx2  
> Database: SQL Server  
> แนวทาง: Relational + Foreign Key + Audit + Soft Delete + Historical Execution

---

## 1. Database

```sql
CREATE DATABASE ProMaxx2QA;
GO

USE ProMaxx2QA;
GO
```

---

## 2. ตาราง Security

### 2.1 Users

```sql
CREATE TABLE dbo.Users (
    UserId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_Users PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    Username NVARCHAR(100) NOT NULL,
    DisplayName NVARCHAR(200) NOT NULL,
    Email NVARCHAR(255) NULL,
    PasswordHash NVARCHAR(MAX) NULL,

    IsActive BIT NOT NULL
        CONSTRAINT DF_Users_IsActive DEFAULT 1,

    LastLoginAt DATETIME2(0) NULL,

    CreatedAt DATETIME2(0) NOT NULL
        CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME(),

    CreatedBy UNIQUEIDENTIFIER NULL,
    UpdatedAt DATETIME2(0) NULL,
    UpdatedBy UNIQUEIDENTIFIER NULL,

    CONSTRAINT UQ_Users_Username UNIQUE (Username)
);
GO
```

### 2.2 Roles

```sql
CREATE TABLE dbo.Roles (
    RoleId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_Roles PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    RoleCode NVARCHAR(50) NOT NULL,
    RoleName NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NULL,

    IsActive BIT NOT NULL
        CONSTRAINT DF_Roles_IsActive DEFAULT 1,

    CONSTRAINT UQ_Roles_RoleCode UNIQUE (RoleCode)
);
GO
```

### 2.3 UserRoles

```sql
CREATE TABLE dbo.UserRoles (
    UserId UNIQUEIDENTIFIER NOT NULL,
    RoleId UNIQUEIDENTIFIER NOT NULL,

    CreatedAt DATETIME2(0) NOT NULL
        CONSTRAINT DF_UserRoles_CreatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),

    CONSTRAINT FK_UserRoles_Users
        FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId),

    CONSTRAINT FK_UserRoles_Roles
        FOREIGN KEY (RoleId) REFERENCES dbo.Roles(RoleId)
);
GO
```

### 2.4 Permissions

```sql
CREATE TABLE dbo.Permissions (
    PermissionId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_Permissions PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    PermissionCode NVARCHAR(100) NOT NULL,
    PermissionName NVARCHAR(200) NOT NULL,
    ModuleArea NVARCHAR(100) NULL,

    CONSTRAINT UQ_Permissions_Code UNIQUE (PermissionCode)
);
GO
```

### 2.5 RolePermissions

```sql
CREATE TABLE dbo.RolePermissions (
    RoleId UNIQUEIDENTIFIER NOT NULL,
    PermissionId UNIQUEIDENTIFIER NOT NULL,

    CONSTRAINT PK_RolePermissions
        PRIMARY KEY (RoleId, PermissionId),

    CONSTRAINT FK_RolePermissions_Roles
        FOREIGN KEY (RoleId) REFERENCES dbo.Roles(RoleId),

    CONSTRAINT FK_RolePermissions_Permissions
        FOREIGN KEY (PermissionId) REFERENCES dbo.Permissions(PermissionId)
);
GO
```

---

## 3. Project / Module

### 3.1 Projects

```sql
CREATE TABLE dbo.Projects (
    ProjectId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_Projects PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ProjectCode NVARCHAR(50) NOT NULL,
    ProjectName NVARCHAR(200) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    Status NVARCHAR(30) NOT NULL
        CONSTRAINT DF_Projects_Status DEFAULT 'Active',

    OwnerUserId UNIQUEIDENTIFIER NULL,

    IsActive BIT NOT NULL
        CONSTRAINT DF_Projects_IsActive DEFAULT 1,

    CreatedAt DATETIME2(0) NOT NULL
        CONSTRAINT DF_Projects_CreatedAt DEFAULT SYSUTCDATETIME(),

    CreatedBy UNIQUEIDENTIFIER NULL,
    UpdatedAt DATETIME2(0) NULL,
    UpdatedBy UNIQUEIDENTIFIER NULL,

    CONSTRAINT UQ_Projects_ProjectCode UNIQUE (ProjectCode),

    CONSTRAINT FK_Projects_Owner
        FOREIGN KEY (OwnerUserId) REFERENCES dbo.Users(UserId)
);
GO
```

### 3.2 Modules

```sql
CREATE TABLE dbo.Modules (
    ModuleId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_Modules PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ProjectId UNIQUEIDENTIFIER NOT NULL,
    ParentModuleId UNIQUEIDENTIFIER NULL,

    ModuleCode NVARCHAR(50) NOT NULL,
    ModuleName NVARCHAR(200) NOT NULL,
    Description NVARCHAR(MAX) NULL,

    OwnerUserId UNIQUEIDENTIFIER NULL,

    IsActive BIT NOT NULL
        CONSTRAINT DF_Modules_IsActive DEFAULT 1,

    CreatedAt DATETIME2(0) NOT NULL
        CONSTRAINT DF_Modules_CreatedAt DEFAULT SYSUTCDATETIME(),

    CreatedBy UNIQUEIDENTIFIER NULL,
    UpdatedAt DATETIME2(0) NULL,
    UpdatedBy UNIQUEIDENTIFIER NULL,

    CONSTRAINT UQ_Modules_Project_ModuleCode
        UNIQUE (ProjectId, ModuleCode),

    CONSTRAINT FK_Modules_Project
        FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId),

    CONSTRAINT FK_Modules_Parent
        FOREIGN KEY (ParentModuleId) REFERENCES dbo.Modules(ModuleId),

    CONSTRAINT FK_Modules_Owner
        FOREIGN KEY (OwnerUserId) REFERENCES dbo.Users(UserId)
);
GO
```

---

## 4. Release / Build

### 4.1 Releases

```sql
CREATE TABLE dbo.Releases (
    ReleaseId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_Releases PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ProjectId UNIQUEIDENTIFIER NOT NULL,
    ReleaseCode NVARCHAR(100) NOT NULL,
    Version NVARCHAR(50) NOT NULL,
    ReleaseType NVARCHAR(50) NULL,
    Scope NVARCHAR(MAX) NULL,

    PlannedReleaseDate DATE NULL,
    ActualReleaseDate DATE NULL,

    Status NVARCHAR(30) NOT NULL
        CONSTRAINT DF_Releases_Status DEFAULT 'Draft',

    ReleaseOwnerUserId UNIQUEIDENTIFIER NULL,

    CreatedAt DATETIME2(0) NOT NULL
        CONSTRAINT DF_Releases_CreatedAt DEFAULT SYSUTCDATETIME(),

    CreatedBy UNIQUEIDENTIFIER NULL,
    UpdatedAt DATETIME2(0) NULL,
    UpdatedBy UNIQUEIDENTIFIER NULL,

    CONSTRAINT UQ_Releases_Project_ReleaseCode
        UNIQUE (ProjectId, ReleaseCode),

    CONSTRAINT FK_Releases_Project
        FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId),

    CONSTRAINT FK_Releases_Owner
        FOREIGN KEY (ReleaseOwnerUserId) REFERENCES dbo.Users(UserId)
);
GO
```

### 4.2 Builds

```sql
CREATE TABLE dbo.Builds (
    BuildId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_Builds PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ReleaseId UNIQUEIDENTIFIER NOT NULL,

    BuildNumber NVARCHAR(100) NOT NULL,
    ApplicationVersion NVARCHAR(100) NULL,
    PackageVersion NVARCHAR(100) NULL,
    CommitReference NVARCHAR(200) NULL,

    BuildDate DATETIME2(0) NULL,
    ChangeNotes NVARCHAR(MAX) NULL,
    KnownIssues NVARCHAR(MAX) NULL,

    IsReleaseCandidate BIT NOT NULL
        CONSTRAINT DF_Builds_IsReleaseCandidate DEFAULT 0,

    Status NVARCHAR(30) NOT NULL
        CONSTRAINT DF_Builds_Status DEFAULT 'Ready',

    CreatedAt DATETIME2(0) NOT NULL
        CONSTRAINT DF_Builds_CreatedAt DEFAULT SYSUTCDATETIME(),

    CreatedBy UNIQUEIDENTIFIER NULL,

    CONSTRAINT UQ_Builds_Release_BuildNumber
        UNIQUE (ReleaseId, BuildNumber),

    CONSTRAINT FK_Builds_Release
        FOREIGN KEY (ReleaseId) REFERENCES dbo.Releases(ReleaseId)
);
GO
```

---

## 5. Requirement / RTM

### 5.1 Requirements

```sql
CREATE TABLE dbo.Requirements (
    RequirementId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_Requirements PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ProjectId UNIQUEIDENTIFIER NOT NULL,
    ReleaseId UNIQUEIDENTIFIER NULL,
    ModuleId UNIQUEIDENTIFIER NOT NULL,

    RequirementCode NVARCHAR(100) NOT NULL,
    Title NVARCHAR(300) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    AcceptanceCriteria NVARCHAR(MAX) NULL,

    Priority NVARCHAR(20) NOT NULL
        CONSTRAINT DF_Requirements_Priority DEFAULT 'P2',

    RiskLevel NVARCHAR(20) NULL,
    Source NVARCHAR(200) NULL,
    OwnerUserId UNIQUEIDENTIFIER NULL,

    Status NVARCHAR(30) NOT NULL
        CONSTRAINT DF_Requirements_Status DEFAULT 'Draft',

    RevisionNo INT NOT NULL
        CONSTRAINT DF_Requirements_RevisionNo DEFAULT 1,

    IsInScope BIT NOT NULL
        CONSTRAINT DF_Requirements_IsInScope DEFAULT 1,

    IsDeleted BIT NOT NULL
        CONSTRAINT DF_Requirements_IsDeleted DEFAULT 0,

    CreatedAt DATETIME2(0) NOT NULL
        CONSTRAINT DF_Requirements_CreatedAt DEFAULT SYSUTCDATETIME(),

    CreatedBy UNIQUEIDENTIFIER NULL,
    UpdatedAt DATETIME2(0) NULL,
    UpdatedBy UNIQUEIDENTIFIER NULL,

    CONSTRAINT UQ_Requirements_Project_Code
        UNIQUE (ProjectId, RequirementCode),

    CONSTRAINT FK_Requirements_Project
        FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId),

    CONSTRAINT FK_Requirements_Release
        FOREIGN KEY (ReleaseId) REFERENCES dbo.Releases(ReleaseId),

    CONSTRAINT FK_Requirements_Module
        FOREIGN KEY (ModuleId) REFERENCES dbo.Modules(ModuleId),

    CONSTRAINT FK_Requirements_Owner
        FOREIGN KEY (OwnerUserId) REFERENCES dbo.Users(UserId)
);
GO
```

### 5.2 RequirementRevisions

```sql
CREATE TABLE dbo.RequirementRevisions (
    RequirementRevisionId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_RequirementRevisions PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    RequirementId UNIQUEIDENTIFIER NOT NULL,
    RevisionNo INT NOT NULL,

    Title NVARCHAR(300) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    AcceptanceCriteria NVARCHAR(MAX) NULL,

    ChangedBy UNIQUEIDENTIFIER NULL,
    ChangedAt DATETIME2(0) NOT NULL
        CONSTRAINT DF_RequirementRevisions_ChangedAt DEFAULT SYSUTCDATETIME(),

    ChangeReason NVARCHAR(1000) NULL,

    CONSTRAINT UQ_RequirementRevisions
        UNIQUE (RequirementId, RevisionNo),

    CONSTRAINT FK_RequirementRevisions_Requirement
        FOREIGN KEY (RequirementId) REFERENCES dbo.Requirements(RequirementId),

    CONSTRAINT FK_RequirementRevisions_User
        FOREIGN KEY (ChangedBy) REFERENCES dbo.Users(UserId)
);
GO
```

---

## 6. Test Design

### 6.1 TestScenarios

```sql
CREATE TABLE dbo.TestScenarios (
    TestScenarioId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_TestScenarios PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ProjectId UNIQUEIDENTIFIER NOT NULL,
    ModuleId UNIQUEIDENTIFIER NOT NULL,

    ScenarioCode NVARCHAR(100) NOT NULL,
    Title NVARCHAR(300) NOT NULL,
    Objective NVARCHAR(MAX) NULL,
    TestType NVARCHAR(50) NULL,

    Priority NVARCHAR(20) NOT NULL
        CONSTRAINT DF_TestScenarios_Priority DEFAULT 'P2',

    RiskLevel NVARCHAR(20) NULL,

    Status NVARCHAR(30) NOT NULL
        CONSTRAINT DF_TestScenarios_Status DEFAULT 'Draft',

    OwnerUserId UNIQUEIDENTIFIER NULL,

    CreatedAt DATETIME2(0) NOT NULL
        CONSTRAINT DF_TestScenarios_CreatedAt DEFAULT SYSUTCDATETIME(),

    CreatedBy UNIQUEIDENTIFIER NULL,
    UpdatedAt DATETIME2(0) NULL,
    UpdatedBy UNIQUEIDENTIFIER NULL,

    CONSTRAINT UQ_TestScenarios_Project_Code
        UNIQUE (ProjectId, ScenarioCode),

    CONSTRAINT FK_TestScenarios_Project
        FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId),

    CONSTRAINT FK_TestScenarios_Module
        FOREIGN KEY (ModuleId) REFERENCES dbo.Modules(ModuleId),

    CONSTRAINT FK_TestScenarios_Owner
        FOREIGN KEY (OwnerUserId) REFERENCES dbo.Users(UserId)
);
GO
```

### 6.2 TestCases

```sql
CREATE TABLE dbo.TestCases (
    TestCaseId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_TestCases PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ProjectId UNIQUEIDENTIFIER NOT NULL,
    ModuleId UNIQUEIDENTIFIER NOT NULL,
    TestScenarioId UNIQUEIDENTIFIER NULL,

    TestCaseCode NVARCHAR(100) NOT NULL,
    Title NVARCHAR(300) NOT NULL,
    Objective NVARCHAR(MAX) NULL,
    Preconditions NVARCHAR(MAX) NULL,

    Priority NVARCHAR(20) NOT NULL
        CONSTRAINT DF_TestCases_Priority DEFAULT 'P2',

    TestType NVARCHAR(50) NULL,

    AutomationCandidate BIT NOT NULL
        CONSTRAINT DF_TestCases_AutomationCandidate DEFAULT 0,

    Status NVARCHAR(30) NOT NULL
        CONSTRAINT DF_TestCases_Status DEFAULT 'Draft',

    RevisionNo INT NOT NULL
        CONSTRAINT DF_TestCases_RevisionNo DEFAULT 1,

    OwnerUserId UNIQUEIDENTIFIER NULL,

    IsDeleted BIT NOT NULL
        CONSTRAINT DF_TestCases_IsDeleted DEFAULT 0,

    CreatedAt DATETIME2(0) NOT NULL
        CONSTRAINT DF_TestCases_CreatedAt DEFAULT SYSUTCDATETIME(),

    CreatedBy UNIQUEIDENTIFIER NULL,
    UpdatedAt DATETIME2(0) NULL,
    UpdatedBy UNIQUEIDENTIFIER NULL,

    CONSTRAINT UQ_TestCases_Project_Code
        UNIQUE (ProjectId, TestCaseCode),

    CONSTRAINT FK_TestCases_Project
        FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId),

    CONSTRAINT FK_TestCases_Module
        FOREIGN KEY (ModuleId) REFERENCES dbo.Modules(ModuleId),

    CONSTRAINT FK_TestCases_Scenario
        FOREIGN KEY (TestScenarioId) REFERENCES dbo.TestScenarios(TestScenarioId),

    CONSTRAINT FK_TestCases_Owner
        FOREIGN KEY (OwnerUserId) REFERENCES dbo.Users(UserId)
);
GO
```

### 6.3 TestCaseRevisions

```sql
CREATE TABLE dbo.TestCaseRevisions (
    TestCaseRevisionId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_TestCaseRevisions PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    TestCaseId UNIQUEIDENTIFIER NOT NULL,
    RevisionNo INT NOT NULL,

    Title NVARCHAR(300) NOT NULL,
    Objective NVARCHAR(MAX) NULL,
    Preconditions NVARCHAR(MAX) NULL,

    ChangedBy UNIQUEIDENTIFIER NULL,
    ChangedAt DATETIME2(0) NOT NULL
        CONSTRAINT DF_TestCaseRevisions_ChangedAt DEFAULT SYSUTCDATETIME(),

    ChangeReason NVARCHAR(1000) NULL,

    CONSTRAINT UQ_TestCaseRevisions
        UNIQUE (TestCaseId, RevisionNo),

    CONSTRAINT FK_TestCaseRevisions_TestCase
        FOREIGN KEY (TestCaseId) REFERENCES dbo.TestCases(TestCaseId)
);
GO
```

### 6.4 TestSteps

```sql
CREATE TABLE dbo.TestSteps (
    TestStepId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_TestSteps PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    TestCaseId UNIQUEIDENTIFIER NOT NULL,
    RevisionNo INT NOT NULL,
    StepNo INT NOT NULL,

    Action NVARCHAR(MAX) NOT NULL,
    TestDataText NVARCHAR(MAX) NULL,
    ExpectedResult NVARCHAR(MAX) NOT NULL,

    CONSTRAINT UQ_TestSteps
        UNIQUE (TestCaseId, RevisionNo, StepNo),

    CONSTRAINT FK_TestSteps_TestCase
        FOREIGN KEY (TestCaseId) REFERENCES dbo.TestCases(TestCaseId)
);
GO
```

### 6.5 RequirementTestCases

```sql
CREATE TABLE dbo.RequirementTestCases (
    RequirementId UNIQUEIDENTIFIER NOT NULL,
    TestCaseId UNIQUEIDENTIFIER NOT NULL,

    CoverageType NVARCHAR(30) NULL,

    CreatedAt DATETIME2(0) NOT NULL
        CONSTRAINT DF_RequirementTestCases_CreatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_RequirementTestCases
        PRIMARY KEY (RequirementId, TestCaseId),

    CONSTRAINT FK_RequirementTestCases_Requirement
        FOREIGN KEY (RequirementId) REFERENCES dbo.Requirements(RequirementId),

    CONSTRAINT FK_RequirementTestCases_TestCase
        FOREIGN KEY (TestCaseId) REFERENCES dbo.TestCases(TestCaseId)
);
GO
```

---

## 7. Test Data / Environment

```sql
CREATE TABLE dbo.TestData (
    TestDataId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_TestData PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ProjectId UNIQUEIDENTIFIER NOT NULL,
    DataCode NVARCHAR(100) NOT NULL,
    DataType NVARCHAR(100) NULL,
    Description NVARCHAR(MAX) NULL,
    DataValue NVARCHAR(MAX) NULL,
    ExpectedInitialState NVARCHAR(MAX) NULL,
    ResetInstruction NVARCHAR(MAX) NULL,
    IsSensitive BIT NOT NULL DEFAULT 0,
    OwnerUserId UNIQUEIDENTIFIER NULL,
    IsActive BIT NOT NULL DEFAULT 1,

    CONSTRAINT UQ_TestData_Project_Code UNIQUE (ProjectId, DataCode),
    CONSTRAINT FK_TestData_Project FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId),
    CONSTRAINT FK_TestData_Owner FOREIGN KEY (OwnerUserId) REFERENCES dbo.Users(UserId)
);
GO

CREATE TABLE dbo.TestCaseTestData (
    TestCaseId UNIQUEIDENTIFIER NOT NULL,
    TestDataId UNIQUEIDENTIFIER NOT NULL,

    CONSTRAINT PK_TestCaseTestData PRIMARY KEY (TestCaseId, TestDataId),
    CONSTRAINT FK_TestCaseTestData_TestCase FOREIGN KEY (TestCaseId) REFERENCES dbo.TestCases(TestCaseId),
    CONSTRAINT FK_TestCaseTestData_TestData FOREIGN KEY (TestDataId) REFERENCES dbo.TestData(TestDataId)
);
GO

CREATE TABLE dbo.TestEnvironments (
    EnvironmentId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_TestEnvironments PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ProjectId UNIQUEIDENTIFIER NOT NULL,
    EnvironmentName NVARCHAR(200) NOT NULL,
    OperatingSystem NVARCHAR(200) NULL,
    AppVersion NVARCHAR(100) NULL,
    DatabaseType NVARCHAR(100) NULL,
    DatabaseVersion NVARCHAR(100) NULL,
    DatasetName NVARCHAR(200) NULL,
    DpiScale NVARCHAR(50) NULL,
    Resolution NVARCHAR(50) NULL,
    NetworkProfile NVARCHAR(200) NULL,
    ServiceVersions NVARCHAR(MAX) NULL,
    DeviceInfo NVARCHAR(MAX) NULL,
    Notes NVARCHAR(MAX) NULL,
    IsActive BIT NOT NULL DEFAULT 1,

    CONSTRAINT UQ_TestEnvironments_Project_Name UNIQUE (ProjectId, EnvironmentName),
    CONSTRAINT FK_TestEnvironments_Project FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId)
);
GO
```

---

## 8. Test Suite / Cycle / Execution

```sql
CREATE TABLE dbo.TestSuites (
    TestSuiteId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_TestSuites PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ProjectId UNIQUEIDENTIFIER NOT NULL,
    SuiteCode NVARCHAR(100) NOT NULL,
    SuiteName NVARCHAR(200) NOT NULL,
    SuiteType NVARCHAR(50) NULL,
    Description NVARCHAR(MAX) NULL,
    RiskTier NVARCHAR(20) NULL,
    IsActive BIT NOT NULL DEFAULT 1,

    CONSTRAINT UQ_TestSuites_Project_Code UNIQUE (ProjectId, SuiteCode),
    CONSTRAINT FK_TestSuites_Project FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId)
);
GO

CREATE TABLE dbo.TestSuiteCases (
    TestSuiteId UNIQUEIDENTIFIER NOT NULL,
    TestCaseId UNIQUEIDENTIFIER NOT NULL,
    SortOrder INT NOT NULL DEFAULT 0,
    IsRequired BIT NOT NULL DEFAULT 1,

    CONSTRAINT PK_TestSuiteCases PRIMARY KEY (TestSuiteId, TestCaseId),
    CONSTRAINT FK_TestSuiteCases_Suite FOREIGN KEY (TestSuiteId) REFERENCES dbo.TestSuites(TestSuiteId),
    CONSTRAINT FK_TestSuiteCases_TestCase FOREIGN KEY (TestCaseId) REFERENCES dbo.TestCases(TestCaseId)
);
GO

CREATE TABLE dbo.TestCycles (
    TestCycleId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_TestCycles PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ProjectId UNIQUEIDENTIFIER NOT NULL,
    ReleaseId UNIQUEIDENTIFIER NOT NULL,
    BuildId UNIQUEIDENTIFIER NOT NULL,
    EnvironmentId UNIQUEIDENTIFIER NOT NULL,
    TestSuiteId UNIQUEIDENTIFIER NULL,

    CycleCode NVARCHAR(100) NOT NULL,
    CycleName NVARCHAR(300) NOT NULL,
    CycleType NVARCHAR(50) NULL,

    StartDate DATETIME2(0) NULL,
    EndDate DATETIME2(0) NULL,

    OwnerUserId UNIQUEIDENTIFIER NULL,

    Status NVARCHAR(30) NOT NULL DEFAULT 'Draft',
    Notes NVARCHAR(MAX) NULL,

    CreatedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy UNIQUEIDENTIFIER NULL,

    CONSTRAINT UQ_TestCycles_Project_Code UNIQUE (ProjectId, CycleCode),

    CONSTRAINT FK_TestCycles_Project FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId),
    CONSTRAINT FK_TestCycles_Release FOREIGN KEY (ReleaseId) REFERENCES dbo.Releases(ReleaseId),
    CONSTRAINT FK_TestCycles_Build FOREIGN KEY (BuildId) REFERENCES dbo.Builds(BuildId),
    CONSTRAINT FK_TestCycles_Environment FOREIGN KEY (EnvironmentId) REFERENCES dbo.TestEnvironments(EnvironmentId),
    CONSTRAINT FK_TestCycles_Suite FOREIGN KEY (TestSuiteId) REFERENCES dbo.TestSuites(TestSuiteId),
    CONSTRAINT FK_TestCycles_Owner FOREIGN KEY (OwnerUserId) REFERENCES dbo.Users(UserId)
);
GO

CREATE TABLE dbo.TestCycleCases (
    TestCycleCaseId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_TestCycleCases PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    TestCycleId UNIQUEIDENTIFIER NOT NULL,
    TestCaseId UNIQUEIDENTIFIER NOT NULL,
    TestCaseRevisionNo INT NOT NULL,

    AssignedTesterUserId UNIQUEIDENTIFIER NULL,
    Priority NVARCHAR(20) NULL,
    ExecutionOrder INT NOT NULL DEFAULT 0,

    CurrentStatus NVARCHAR(30) NOT NULL DEFAULT 'NotRun',

    CONSTRAINT UQ_TestCycleCases UNIQUE (TestCycleId, TestCaseId),

    CONSTRAINT FK_TestCycleCases_Cycle FOREIGN KEY (TestCycleId) REFERENCES dbo.TestCycles(TestCycleId),
    CONSTRAINT FK_TestCycleCases_TestCase FOREIGN KEY (TestCaseId) REFERENCES dbo.TestCases(TestCaseId),
    CONSTRAINT FK_TestCycleCases_Tester FOREIGN KEY (AssignedTesterUserId) REFERENCES dbo.Users(UserId)
);
GO

CREATE TABLE dbo.TestExecutions (
    TestExecutionId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_TestExecutions PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    TestCycleCaseId UNIQUEIDENTIFIER NOT NULL,
    ExecutionNo INT NOT NULL,

    BuildId UNIQUEIDENTIFIER NOT NULL,
    EnvironmentId UNIQUEIDENTIFIER NOT NULL,
    TesterUserId UNIQUEIDENTIFIER NOT NULL,

    StartedAt DATETIME2(0) NULL,
    CompletedAt DATETIME2(0) NULL,

    Status NVARCHAR(30) NOT NULL,
    ActualResult NVARCHAR(MAX) NULL,
    Comment NVARCHAR(MAX) NULL,

    CONSTRAINT UQ_TestExecutions UNIQUE (TestCycleCaseId, ExecutionNo),

    CONSTRAINT FK_TestExecutions_CycleCase FOREIGN KEY (TestCycleCaseId) REFERENCES dbo.TestCycleCases(TestCycleCaseId),
    CONSTRAINT FK_TestExecutions_Build FOREIGN KEY (BuildId) REFERENCES dbo.Builds(BuildId),
    CONSTRAINT FK_TestExecutions_Environment FOREIGN KEY (EnvironmentId) REFERENCES dbo.TestEnvironments(EnvironmentId),
    CONSTRAINT FK_TestExecutions_Tester FOREIGN KEY (TesterUserId) REFERENCES dbo.Users(UserId)
);
GO

CREATE TABLE dbo.TestStepResults (
    TestStepResultId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_TestStepResults PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    TestExecutionId UNIQUEIDENTIFIER NOT NULL,
    TestStepId UNIQUEIDENTIFIER NULL,
    StepNo INT NOT NULL,

    Status NVARCHAR(30) NOT NULL,
    ActualResult NVARCHAR(MAX) NULL,
    Comment NVARCHAR(MAX) NULL,

    CONSTRAINT FK_TestStepResults_Execution FOREIGN KEY (TestExecutionId) REFERENCES dbo.TestExecutions(TestExecutionId),
    CONSTRAINT FK_TestStepResults_Step FOREIGN KEY (TestStepId) REFERENCES dbo.TestSteps(TestStepId)
);
GO
```

---

## 9. Defect

```sql
CREATE TABLE dbo.Defects (
    DefectId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_Defects PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ProjectId UNIQUEIDENTIFIER NOT NULL,
    DefectCode NVARCHAR(100) NOT NULL,
    ModuleId UNIQUEIDENTIFIER NOT NULL,

    Title NVARCHAR(300) NOT NULL,
    Description NVARCHAR(MAX) NULL,

    Severity NVARCHAR(20) NOT NULL,
    Priority NVARCHAR(20) NOT NULL,
    Status NVARCHAR(30) NOT NULL DEFAULT 'New',

    BuildFoundId UNIQUEIDENTIFIER NOT NULL,
    EnvironmentId UNIQUEIDENTIFIER NULL,

    ReporterUserId UNIQUEIDENTIFIER NOT NULL,
    AssigneeUserId UNIQUEIDENTIFIER NULL,

    Precondition NVARCHAR(MAX) NULL,
    StepsToReproduce NVARCHAR(MAX) NULL,
    ExpectedResult NVARCHAR(MAX) NULL,
    ActualResult NVARCHAR(MAX) NULL,

    Frequency NVARCHAR(100) NULL,
    BusinessImpact NVARCHAR(MAX) NULL,
    Workaround NVARCHAR(MAX) NULL,

    RootCause NVARCHAR(MAX) NULL,
    Resolution NVARCHAR(MAX) NULL,
    FixBuildId UNIQUEIDENTIFIER NULL,
    RegressionImpact NVARCHAR(MAX) NULL,

    CreatedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    ClosedAt DATETIME2(0) NULL,

    CONSTRAINT UQ_Defects_Project_Code UNIQUE (ProjectId, DefectCode),

    CONSTRAINT FK_Defects_Project FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId),
    CONSTRAINT FK_Defects_Module FOREIGN KEY (ModuleId) REFERENCES dbo.Modules(ModuleId),
    CONSTRAINT FK_Defects_BuildFound FOREIGN KEY (BuildFoundId) REFERENCES dbo.Builds(BuildId),
    CONSTRAINT FK_Defects_Environment FOREIGN KEY (EnvironmentId) REFERENCES dbo.TestEnvironments(EnvironmentId),
    CONSTRAINT FK_Defects_Reporter FOREIGN KEY (ReporterUserId) REFERENCES dbo.Users(UserId),
    CONSTRAINT FK_Defects_Assignee FOREIGN KEY (AssigneeUserId) REFERENCES dbo.Users(UserId),
    CONSTRAINT FK_Defects_FixBuild FOREIGN KEY (FixBuildId) REFERENCES dbo.Builds(BuildId)
);
GO

CREATE TABLE dbo.DefectTestExecutions (
    DefectId UNIQUEIDENTIFIER NOT NULL,
    TestExecutionId UNIQUEIDENTIFIER NOT NULL,

    CONSTRAINT PK_DefectTestExecutions PRIMARY KEY (DefectId, TestExecutionId),
    CONSTRAINT FK_DefectTestExecutions_Defect FOREIGN KEY (DefectId) REFERENCES dbo.Defects(DefectId),
    CONSTRAINT FK_DefectTestExecutions_Execution FOREIGN KEY (TestExecutionId) REFERENCES dbo.TestExecutions(TestExecutionId)
);
GO

CREATE TABLE dbo.DefectTestCases (
    DefectId UNIQUEIDENTIFIER NOT NULL,
    TestCaseId UNIQUEIDENTIFIER NOT NULL,

    CONSTRAINT PK_DefectTestCases PRIMARY KEY (DefectId, TestCaseId),
    CONSTRAINT FK_DefectTestCases_Defect FOREIGN KEY (DefectId) REFERENCES dbo.Defects(DefectId),
    CONSTRAINT FK_DefectTestCases_TestCase FOREIGN KEY (TestCaseId) REFERENCES dbo.TestCases(TestCaseId)
);
GO

CREATE TABLE dbo.DefectRequirements (
    DefectId UNIQUEIDENTIFIER NOT NULL,
    RequirementId UNIQUEIDENTIFIER NOT NULL,

    CONSTRAINT PK_DefectRequirements PRIMARY KEY (DefectId, RequirementId),
    CONSTRAINT FK_DefectRequirements_Defect FOREIGN KEY (DefectId) REFERENCES dbo.Defects(DefectId),
    CONSTRAINT FK_DefectRequirements_Requirement FOREIGN KEY (RequirementId) REFERENCES dbo.Requirements(RequirementId)
);
GO

CREATE TABLE dbo.DefectHistory (
    DefectHistoryId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_DefectHistory PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    DefectId UNIQUEIDENTIFIER NOT NULL,
    FromStatus NVARCHAR(30) NULL,
    ToStatus NVARCHAR(30) NOT NULL,
    Comment NVARCHAR(MAX) NULL,

    ChangedBy UNIQUEIDENTIFIER NOT NULL,
    ChangedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_DefectHistory_Defect FOREIGN KEY (DefectId) REFERENCES dbo.Defects(DefectId),
    CONSTRAINT FK_DefectHistory_User FOREIGN KEY (ChangedBy) REFERENCES dbo.Users(UserId)
);
GO
```

---

## 10. Attachment / Report / Risk / Sign-off / Audit

```sql
CREATE TABLE dbo.Attachments (
    AttachmentId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_Attachments PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ProjectId UNIQUEIDENTIFIER NOT NULL,
    EntityType NVARCHAR(100) NOT NULL,
    EntityId UNIQUEIDENTIFIER NOT NULL,

    FileName NVARCHAR(500) NOT NULL,
    StoredFileName NVARCHAR(500) NOT NULL,
    ContentType NVARCHAR(200) NULL,
    FileSize BIGINT NULL,
    FileHash NVARCHAR(128) NULL,
    StoragePath NVARCHAR(1000) NOT NULL,

    UploadedBy UNIQUEIDENTIFIER NOT NULL,
    UploadedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_Attachments_Project FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId),
    CONSTRAINT FK_Attachments_User FOREIGN KEY (UploadedBy) REFERENCES dbo.Users(UserId)
);
GO

CREATE TABLE dbo.TestSummaries (
    TestSummaryId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_TestSummaries PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ReleaseId UNIQUEIDENTIFIER NOT NULL,
    BuildId UNIQUEIDENTIFIER NOT NULL,

    Scope NVARCHAR(MAX) NULL,
    OutOfScope NVARCHAR(MAX) NULL,

    Planned INT NOT NULL DEFAULT 0,
    Executed INT NOT NULL DEFAULT 0,
    Passed INT NOT NULL DEFAULT 0,
    Failed INT NOT NULL DEFAULT 0,
    Blocked INT NOT NULL DEFAULT 0,

    RequirementCoverage DECIMAL(5,2) NULL,
    PassRate DECIMAL(5,2) NULL,

    DefectSummary NVARCHAR(MAX) NULL,
    RegressionResult NVARCHAR(MAX) NULL,
    UpdateResult NVARCHAR(MAX) NULL,
    PerformanceResult NVARCHAR(MAX) NULL,
    KnownIssues NVARCHAR(MAX) NULL,
    RemainingRisks NVARCHAR(MAX) NULL,

    QARecommendation NVARCHAR(30) NULL,

    GeneratedBy UNIQUEIDENTIFIER NOT NULL,
    GeneratedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_TestSummaries_Release FOREIGN KEY (ReleaseId) REFERENCES dbo.Releases(ReleaseId),
    CONSTRAINT FK_TestSummaries_Build FOREIGN KEY (BuildId) REFERENCES dbo.Builds(BuildId),
    CONSTRAINT FK_TestSummaries_User FOREIGN KEY (GeneratedBy) REFERENCES dbo.Users(UserId)
);
GO

CREATE TABLE dbo.RiskAcceptances (
    RiskAcceptanceId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_RiskAcceptances PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ReleaseId UNIQUEIDENTIFIER NOT NULL,
    RiskCode NVARCHAR(100) NOT NULL,
    DefectId UNIQUEIDENTIFIER NULL,

    Title NVARCHAR(300) NOT NULL,
    Description NVARCHAR(MAX) NULL,

    Impact NVARCHAR(50) NULL,
    Probability NVARCHAR(50) NULL,
    RiskLevel NVARCHAR(50) NULL,

    Workaround NVARCHAR(MAX) NULL,

    OwnerUserId UNIQUEIDENTIFIER NULL,
    TargetFixDate DATE NULL,
    ReviewDate DATE NULL,

    Status NVARCHAR(30) NOT NULL DEFAULT 'Draft',

    ApprovedBy UNIQUEIDENTIFIER NULL,
    ApprovedAt DATETIME2(0) NULL,

    CONSTRAINT UQ_RiskAcceptances_Release_Code UNIQUE (ReleaseId, RiskCode),

    CONSTRAINT FK_RiskAcceptances_Release FOREIGN KEY (ReleaseId) REFERENCES dbo.Releases(ReleaseId),
    CONSTRAINT FK_RiskAcceptances_Defect FOREIGN KEY (DefectId) REFERENCES dbo.Defects(DefectId),
    CONSTRAINT FK_RiskAcceptances_Owner FOREIGN KEY (OwnerUserId) REFERENCES dbo.Users(UserId),
    CONSTRAINT FK_RiskAcceptances_Approver FOREIGN KEY (ApprovedBy) REFERENCES dbo.Users(UserId)
);
GO

CREATE TABLE dbo.ReleaseSignoffs (
    ReleaseSignoffId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_ReleaseSignoffs PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    ReleaseId UNIQUEIDENTIFIER NOT NULL,
    BuildId UNIQUEIDENTIFIER NOT NULL,

    SignoffType NVARCHAR(30) NOT NULL,
    Decision NVARCHAR(30) NOT NULL,
    Comment NVARCHAR(MAX) NULL,

    ApproverUserId UNIQUEIDENTIFIER NOT NULL,
    ApprovedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_ReleaseSignoffs_Release FOREIGN KEY (ReleaseId) REFERENCES dbo.Releases(ReleaseId),
    CONSTRAINT FK_ReleaseSignoffs_Build FOREIGN KEY (BuildId) REFERENCES dbo.Builds(BuildId),
    CONSTRAINT FK_ReleaseSignoffs_User FOREIGN KEY (ApproverUserId) REFERENCES dbo.Users(UserId)
);
GO

CREATE TABLE dbo.Notifications (
    NotificationId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT PK_Notifications PRIMARY KEY
        DEFAULT NEWSEQUENTIALID(),

    UserId UNIQUEIDENTIFIER NOT NULL,
    Type NVARCHAR(100) NULL,
    Title NVARCHAR(300) NOT NULL,
    Message NVARCHAR(MAX) NULL,
    EntityType NVARCHAR(100) NULL,
    EntityId UNIQUEIDENTIFIER NULL,

    IsRead BIT NOT NULL DEFAULT 0,

    CreatedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    ReadAt DATETIME2(0) NULL,

    CONSTRAINT FK_Notifications_User FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId)
);
GO

CREATE TABLE dbo.AuditLogs (
    AuditLogId BIGINT IDENTITY(1,1) NOT NULL
        CONSTRAINT PK_AuditLogs PRIMARY KEY,

    UserId UNIQUEIDENTIFIER NULL,
    Action NVARCHAR(100) NOT NULL,
    EntityType NVARCHAR(100) NOT NULL,
    EntityId UNIQUEIDENTIFIER NULL,

    ChangeSummary NVARCHAR(MAX) NULL,
    BeforeJson NVARCHAR(MAX) NULL,
    AfterJson NVARCHAR(MAX) NULL,
    ClientIp NVARCHAR(100) NULL,

    CreatedAt DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_AuditLogs_User FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId)
);
GO
```

---

## 11. Indexes

```sql
CREATE INDEX IX_Requirements_Release_Module_Status
ON dbo.Requirements (ReleaseId, ModuleId, Status)
INCLUDE (RequirementCode, Title, Priority, IsInScope);

CREATE INDEX IX_TestCases_Project_Module_Priority_Status
ON dbo.TestCases (ProjectId, ModuleId, Priority, Status)
INCLUDE (TestCaseCode, Title, RevisionNo);

CREATE INDEX IX_TestCycleCases_Cycle_Status_Tester
ON dbo.TestCycleCases (TestCycleId, CurrentStatus, AssignedTesterUserId);

CREATE INDEX IX_TestExecutions_CycleCase_ExecutionNo
ON dbo.TestExecutions (TestCycleCaseId, ExecutionNo);

CREATE INDEX IX_Defects_Project_Status_Severity_Assignee
ON dbo.Defects (ProjectId, Status, Severity, AssigneeUserId)
INCLUDE (DefectCode, Title, Priority, BuildFoundId, FixBuildId);

CREATE INDEX IX_AuditLogs_Entity
ON dbo.AuditLogs (EntityType, EntityId, CreatedAt DESC);

CREATE INDEX IX_Attachments_Entity
ON dbo.Attachments (EntityType, EntityId);
GO
```

---

## 12. Recommended Views

### 12.1 Requirement Coverage

```sql
CREATE VIEW dbo.vw_RequirementCoverage
AS
SELECT
    r.RequirementId,
    r.ReleaseId,
    r.ModuleId,
    r.RequirementCode,
    r.Title,
    COUNT(rtc.TestCaseId) AS TestCaseCount
FROM dbo.Requirements r
LEFT JOIN dbo.RequirementTestCases rtc
    ON rtc.RequirementId = r.RequirementId
WHERE r.IsDeleted = 0
GROUP BY
    r.RequirementId,
    r.ReleaseId,
    r.ModuleId,
    r.RequirementCode,
    r.Title;
GO
```

### 12.2 Test Execution Summary

```sql
CREATE VIEW dbo.vw_TestExecutionSummary
AS
SELECT
    tc.TestCycleId,
    COUNT(*) AS TotalCases,
    SUM(CASE WHEN tcc.CurrentStatus = 'Pass' THEN 1 ELSE 0 END) AS Passed,
    SUM(CASE WHEN tcc.CurrentStatus = 'Fail' THEN 1 ELSE 0 END) AS Failed,
    SUM(CASE WHEN tcc.CurrentStatus = 'Blocked' THEN 1 ELSE 0 END) AS Blocked,
    SUM(CASE WHEN tcc.CurrentStatus = 'NotRun' THEN 1 ELSE 0 END) AS NotRun
FROM dbo.TestCycles tc
JOIN dbo.TestCycleCases tcc
    ON tcc.TestCycleId = tc.TestCycleId
GROUP BY tc.TestCycleId;
GO
```

---

## 13. Constraints ที่ควร enforce ใน Application Layer

บาง Business Rule ซับซ้อนเกินกว่าจะบังคับด้วย FK อย่างเดียว:

1. Build ของ TestCycle ต้องอยู่ Release เดียวกัน
2. FixBuild ของ Defect ต้องสัมพันธ์กับ Release ที่เหมาะสม
3. Closed TestCycle ห้ามแก้ Historical Execution
4. Approved Sign-off ห้ามแก้ Record เดิม
5. Test Case Revision ใหม่ต้องไม่เปลี่ยน Revision ที่ Cycle เก่าอ้างอยู่
6. P0 Open ต้องทำให้ Release Gate เป็น NO-GO
7. P1 Blocker ต้องปิดหรือมี Approved Risk Acceptance
8. Attachment ต้องตรวจ MIME/Size/Extension
9. Sensitive Test Data ห้ามส่งออกแบบ Plain Text
10. Dashboard Readiness ต้องคำนวณจาก Source Data ไม่รับค่าจาก UI โดยตรง

---

## 14. Seed Data ที่แนะนำ

### Roles
- SYS_ADMIN
- QA_LEAD
- QA_TESTER
- DEVELOPER
- PRODUCT_OWNER
- RELEASE_OWNER
- VIEWER

### Permission ตัวอย่าง
- PROJECT.VIEW
- PROJECT.EDIT
- REQUIREMENT.VIEW
- REQUIREMENT.EDIT
- TESTCASE.VIEW
- TESTCASE.EDIT
- EXECUTION.RUN
- EXECUTION.ASSIGN
- DEFECT.CREATE
- DEFECT.EDIT
- DEFECT.RESOLVE
- RISK.APPROVE
- RELEASE.SIGNOFF
- REPORT.EXPORT
- ADMIN.USER
- ADMIN.PERMISSION

### Module ตัวอย่างของ ProMaxx2
- CORE
- AUTH
- SALES
- POS
- STOCK
- PURCHASE
- REPORT
- DATABASE
- API
- UPDATE
- SECURITY
