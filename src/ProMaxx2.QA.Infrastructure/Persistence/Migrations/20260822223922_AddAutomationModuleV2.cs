using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationModuleV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationQualityGateRuns");

            migrationBuilder.DropTable(
                name: "AutomationQueueJobs");

            migrationBuilder.DropTable(
                name: "AutomationRunCases");

            migrationBuilder.DropTable(
                name: "AutomationRunnerAgents");

            migrationBuilder.DropTable(
                name: "AutomationSchedules");

            migrationBuilder.DropTable(
                name: "AutomationRuns");

            migrationBuilder.AddColumn<string>(
                name: "ExecutionType",
                table: "TestExecutions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AutomationActions",
                columns: table => new
                {
                    AutomationActionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ActionName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ParameterSchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HandlerKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    MinimumAgentVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationActions", x => x.AutomationActionId);
                });

            migrationBuilder.CreateTable(
                name: "AutomationAgents",
                columns: table => new
                {
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    MachineName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AgentVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OperatingSystem = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Architecture = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrentExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationAgents", x => x.AgentId);
                });

            migrationBuilder.CreateTable(
                name: "AutomationCases",
                columns: table => new
                {
                    AutomationCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    AutomationType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CurrentVersionNo = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsAiGenerated = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationCases", x => x.AutomationCaseId);
                    table.ForeignKey(
                        name: "FK_AutomationCases_TestCases_TestCaseId",
                        column: x => x.TestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "TestCaseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AutomationObjects",
                columns: table => new
                {
                    AutomationObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApplicationCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ScreenCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ObjectCode = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ObjectName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ControlType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AutomationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SelectorJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ObjectVersion = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationObjects", x => x.AutomationObjectId);
                    table.ForeignKey(
                        name: "FK_AutomationObjects_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "Modules",
                        principalColumn: "ModuleId");
                });

            migrationBuilder.CreateTable(
                name: "AutomationAgentCapabilities",
                columns: table => new
                {
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CapabilityCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CapabilityVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationAgentCapabilities", x => new { x.AgentId, x.CapabilityCode });
                    table.ForeignKey(
                        name: "FK_AutomationAgentCapabilities_AutomationAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AutomationAgents",
                        principalColumn: "AgentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AutomationVersions",
                columns: table => new
                {
                    AutomationVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    TestCaseRevisionNo = table.Column<int>(type: "int", nullable: false),
                    DslVersion = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DslJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GeneratedByAi = table.Column<bool>(type: "bit", nullable: false),
                    AiProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AiModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AiConfidence = table.Column<double>(type: "float", nullable: true),
                    ValidationStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ValidationErrors = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChangeReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationVersions", x => x.AutomationVersionId);
                    table.ForeignKey(
                        name: "FK_AutomationVersions_AutomationCases_AutomationCaseId",
                        column: x => x.AutomationCaseId,
                        principalTable: "AutomationCases",
                        principalColumn: "AutomationCaseId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AutomationExecutions",
                columns: table => new
                {
                    AutomationExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    FailureType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequestedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationExecutions", x => x.AutomationExecutionId);
                    table.ForeignKey(
                        name: "FK_AutomationExecutions_AutomationAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AutomationAgents",
                        principalColumn: "AgentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationExecutions_AutomationCases_AutomationCaseId",
                        column: x => x.AutomationCaseId,
                        principalTable: "AutomationCases",
                        principalColumn: "AutomationCaseId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationExecutions_AutomationVersions_AutomationVersionId",
                        column: x => x.AutomationVersionId,
                        principalTable: "AutomationVersions",
                        principalColumn: "AutomationVersionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationExecutions_Builds_BuildId",
                        column: x => x.BuildId,
                        principalTable: "Builds",
                        principalColumn: "BuildId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationExecutions_TestEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "TestEnvironments",
                        principalColumn: "TestEnvironmentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AutomationJobs",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    RequestedAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationJobs", x => x.JobId);
                    table.ForeignKey(
                        name: "FK_AutomationJobs_AutomationAgents_AssignedAgentId",
                        column: x => x.AssignedAgentId,
                        principalTable: "AutomationAgents",
                        principalColumn: "AgentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationJobs_AutomationExecutions_AutomationExecutionId",
                        column: x => x.AutomationExecutionId,
                        principalTable: "AutomationExecutions",
                        principalColumn: "AutomationExecutionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AutomationStepResults",
                columns: table => new
                {
                    AutomationStepResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepNo = table.Column<int>(type: "int", nullable: false),
                    ActionCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    ActualResult = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EvidencePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationStepResults", x => x.AutomationStepResultId);
                    table.ForeignKey(
                        name: "FK_AutomationStepResults_AutomationExecutions_AutomationExecutionId",
                        column: x => x.AutomationExecutionId,
                        principalTable: "AutomationExecutions",
                        principalColumn: "AutomationExecutionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationActions_ActionCode",
                table: "AutomationActions",
                column: "ActionCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationAgents_AgentCode",
                table: "AutomationAgents",
                column: "AgentCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationAgents_Status_LastHeartbeatAt",
                table: "AutomationAgents",
                columns: new[] { "Status", "LastHeartbeatAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationCases_AutomationCode",
                table: "AutomationCases",
                column: "AutomationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationCases_TestCaseId_IsDeleted",
                table: "AutomationCases",
                columns: new[] { "TestCaseId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationExecutions_AgentId",
                table: "AutomationExecutions",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationExecutions_AutomationCaseId_CreatedAt",
                table: "AutomationExecutions",
                columns: new[] { "AutomationCaseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationExecutions_AutomationVersionId",
                table: "AutomationExecutions",
                column: "AutomationVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationExecutions_BuildId",
                table: "AutomationExecutions",
                column: "BuildId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationExecutions_EnvironmentId",
                table: "AutomationExecutions",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationJobs_AssignedAgentId",
                table: "AutomationJobs",
                column: "AssignedAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationJobs_AutomationExecutionId",
                table: "AutomationJobs",
                column: "AutomationExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationJobs_Status_Priority_QueuedAt",
                table: "AutomationJobs",
                columns: new[] { "Status", "Priority", "QueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationObjects_ModuleId",
                table: "AutomationObjects",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationObjects_ProjectId_ApplicationCode_ScreenCode_ObjectCode",
                table: "AutomationObjects",
                columns: new[] { "ProjectId", "ApplicationCode", "ScreenCode", "ObjectCode" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationStepResults_AutomationExecutionId_StepNo",
                table: "AutomationStepResults",
                columns: new[] { "AutomationExecutionId", "StepNo" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationVersions_AutomationCaseId_VersionNo",
                table: "AutomationVersions",
                columns: new[] { "AutomationCaseId", "VersionNo" });

            migrationBuilder.Sql("UPDATE TestExecutions SET ExecutionType = 'Manual' WHERE ExecutionType = ''");

            migrationBuilder.Sql("""
                INSERT INTO AutomationActions (AutomationActionId, ActionCode, ActionName, Category, Description, ParameterSchemaJson, HandlerKey, MinimumAgentVersion, IsActive, CreatedAt)
                SELECT NEWID(), v.Code, v.Name, v.Category, NULL, '{}', v.Code, '1.0.0', 1, SYSUTCDATETIME()
                FROM (VALUES
                    ('LOGIN','Login','Authentication'),
                    ('LOGOUT','Logout','Authentication'),
                    ('SWITCH_USER','Switch User','Authentication'),
                    ('OPEN_MENU','Open Menu','Navigation'),
                    ('OPEN_SCREEN','Open Screen','Navigation'),
                    ('CLOSE_SCREEN','Close Screen','Navigation'),
                    ('WAIT_SCREEN','Wait Screen','Navigation'),
                    ('NEW_DOCUMENT','New Document','Document'),
                    ('SEARCH_DOCUMENT','Search Document','Document'),
                    ('SAVE_DOCUMENT','Save Document','Document'),
                    ('APPROVE_DOCUMENT','Approve Document','Document'),
                    ('CANCEL_DOCUMENT','Cancel Document','Document'),
                    ('DELETE_DOCUMENT','Delete Document','Document'),
                    ('SELECT_ITEM','Select Item','Item'),
                    ('SET_QTY','Set Quantity','Item'),
                    ('SET_PRICE','Set Price','Item'),
                    ('SET_DISCOUNT','Set Discount','Item'),
                    ('SET_LOT','Set Lot','Item'),
                    ('REMOVE_ITEM','Remove Item','Item'),
                    ('CLICK','Click','Generic UI'),
                    ('SET_TEXT','Set Text','Generic UI'),
                    ('SELECT_COMBO','Select Combo','Generic UI'),
                    ('CHECK','Check','Generic UI'),
                    ('UNCHECK','Uncheck','Generic UI'),
                    ('PRESS_KEY','Press Key','Generic UI'),
                    ('WAIT_OBJECT','Wait Object','Generic UI'),
                    ('EXPECT_MESSAGE','Expect Message','Validation'),
                    ('EXPECT_VALUE','Expect Value','Validation'),
                    ('EXPECT_TEXT','Expect Text','Validation'),
                    ('EXPECT_ENABLED','Expect Enabled','Validation'),
                    ('EXPECT_DISABLED','Expect Disabled','Validation'),
                    ('EXPECT_VISIBLE','Expect Visible','Validation'),
                    ('EXPECT_NOT_VISIBLE','Expect Not Visible','Validation'),
                    ('EXPECT_DOCUMENT_CREATED','Expect Document Created','Validation'),
                    ('EXPECT_DOCUMENT_NOT_CREATED','Expect Document Not Created','Validation'),
                    ('EXPECT_DB_VALUE','Expect DB Value','Validation'),
                    ('EXPECT_STOCK','Expect Stock','Validation'),
                    ('EXPECT_LOT','Expect Lot','Validation'),
                    ('EXPECT_TRANSACTION','Expect Transaction','Validation')
                ) AS v(Code, Name, Category)
                WHERE NOT EXISTS (SELECT 1 FROM AutomationActions a WHERE a.ActionCode = v.Code)
                """);

            migrationBuilder.Sql("""
                INSERT INTO Permissions (PermissionId, PermissionCode, PermissionName, ModuleArea)
                SELECT NEWID(), v.Code, v.Name, 'Automation'
                FROM (VALUES
                    ('AUTOMATION.VIEW','Automation View'),
                    ('AUTOMATION.EDIT','Automation Edit'),
                    ('AUTOMATION.VALIDATE','Automation Validate'),
                    ('AUTOMATION.APPROVE','Automation Approve'),
                    ('AUTOMATION.EXECUTE','Automation Execute'),
                    ('AUTOMATION.MANAGE','Automation Manage'),
                    ('AUTOMATION.VIEWEVIDENCE','Automation View Evidence')
                ) AS v(Code, Name)
                WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.PermissionCode = v.Code)
                """);

            migrationBuilder.Sql("""
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.RoleId, p.PermissionId
                FROM Roles r
                CROSS JOIN Permissions p
                WHERE p.PermissionCode LIKE 'AUTOMATION.%'
                  AND r.RoleCode = 'SYS_ADMIN'
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId)
                """);

            migrationBuilder.Sql("""
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.RoleId, p.PermissionId
                FROM Roles r
                CROSS JOIN Permissions p
                WHERE p.PermissionCode IN ('AUTOMATION.VIEW','AUTOMATION.EDIT','AUTOMATION.VALIDATE','AUTOMATION.APPROVE','AUTOMATION.EXECUTE','AUTOMATION.MANAGE','AUTOMATION.VIEWEVIDENCE')
                  AND r.RoleCode = 'QA_LEAD'
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId)
                """);

            migrationBuilder.Sql("""
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.RoleId, p.PermissionId
                FROM Roles r
                CROSS JOIN Permissions p
                WHERE p.PermissionCode IN ('AUTOMATION.VIEW','AUTOMATION.EDIT','AUTOMATION.EXECUTE')
                  AND r.RoleCode = 'QA_TESTER'
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId)
                """);

            migrationBuilder.Sql("""
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.RoleId, p.PermissionId
                FROM Roles r
                CROSS JOIN Permissions p
                WHERE p.PermissionCode = 'AUTOMATION.VIEW'
                  AND r.RoleCode IN ('PRODUCT_OWNER','RELEASE_OWNER','VIEWER')
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationActions");

            migrationBuilder.DropTable(
                name: "AutomationAgentCapabilities");

            migrationBuilder.DropTable(
                name: "AutomationJobs");

            migrationBuilder.DropTable(
                name: "AutomationObjects");

            migrationBuilder.DropTable(
                name: "AutomationStepResults");

            migrationBuilder.DropTable(
                name: "AutomationExecutions");

            migrationBuilder.DropTable(
                name: "AutomationAgents");

            migrationBuilder.DropTable(
                name: "AutomationVersions");

            migrationBuilder.DropTable(
                name: "AutomationCases");

            migrationBuilder.DropColumn(
                name: "ExecutionType",
                table: "TestExecutions");

            migrationBuilder.CreateTable(
                name: "AutomationQualityGateRuns",
                columns: table => new
                {
                    AutomationQualityGateRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaselineBuild = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedCount = table.Column<int>(type: "int", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrentBuild = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Messages = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NewDuplicateCount = table.Column<int>(type: "int", nullable: false),
                    NewMissingCount = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RemovedCount = table.Column<int>(type: "int", nullable: false),
                    RunnerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetApp = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationQualityGateRuns", x => x.AutomationQualityGateRunId);
                });

            migrationBuilder.CreateTable(
                name: "AutomationQueueJobs",
                columns: table => new
                {
                    AutomationQueueJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    AutomationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ErrorType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Pack = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Smoke"),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RunnerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SourceScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetApp = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TestCycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationQueueJobs", x => x.AutomationQueueJobId);
                });

            migrationBuilder.CreateTable(
                name: "AutomationRunnerAgents",
                columns: table => new
                {
                    AutomationRunnerAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Capabilities = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CurrentJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastHeartbeatAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MachineName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RunnerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRunnerAgents", x => x.AutomationRunnerAgentId);
                });

            migrationBuilder.CreateTable(
                name: "AutomationRuns",
                columns: table => new
                {
                    AutomationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestCycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    PassedCount = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RunnerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetApp = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TotalCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRuns", x => x.AutomationRunId);
                    table.ForeignKey(
                        name: "FK_AutomationRuns_TestCycles_TestCycleId",
                        column: x => x.TestCycleId,
                        principalTable: "TestCycles",
                        principalColumn: "TestCycleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AutomationSchedules",
                columns: table => new
                {
                    AutomationScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastQueuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Pack = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunAtUtc = table.Column<TimeOnly>(type: "time", nullable: false),
                    TargetApp = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationSchedules", x => x.AutomationScheduleId);
                });

            migrationBuilder.CreateTable(
                name: "AutomationRunCases",
                columns: table => new
                {
                    AutomationRunCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EvidencePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TestCaseCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TestCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRunCases", x => x.AutomationRunCaseId);
                    table.ForeignKey(
                        name: "FK_AutomationRunCases_AutomationRuns_AutomationRunId",
                        column: x => x.AutomationRunId,
                        principalTable: "AutomationRuns",
                        principalColumn: "AutomationRunId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AutomationRunCases_TestExecutions_TestExecutionId",
                        column: x => x.TestExecutionId,
                        principalTable: "TestExecutions",
                        principalColumn: "TestExecutionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationQualityGateRuns_ProjectId_BuildId_TargetApp_CompletedAt",
                table: "AutomationQualityGateRuns",
                columns: new[] { "ProjectId", "BuildId", "TargetApp", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationQueueJobs_ProjectId_BuildId_RequestedAt",
                table: "AutomationQueueJobs",
                columns: new[] { "ProjectId", "BuildId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationQueueJobs_Status_TargetApp_RequestedAt",
                table: "AutomationQueueJobs",
                columns: new[] { "Status", "TargetApp", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRunCases_AutomationRunId",
                table: "AutomationRunCases",
                column: "AutomationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRunCases_TestExecutionId",
                table: "AutomationRunCases",
                column: "TestExecutionId",
                unique: true,
                filter: "[TestExecutionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRunnerAgents_ProjectId_LastHeartbeatAt",
                table: "AutomationRunnerAgents",
                columns: new[] { "ProjectId", "LastHeartbeatAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRunnerAgents_ProjectId_RunnerName",
                table: "AutomationRunnerAgents",
                columns: new[] { "ProjectId", "RunnerName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRuns_ProjectId_CompletedAt",
                table: "AutomationRuns",
                columns: new[] { "ProjectId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRuns_TestCycleId",
                table: "AutomationRuns",
                column: "TestCycleId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationSchedules_ProjectId_IsActive_NextRunAt",
                table: "AutomationSchedules",
                columns: new[] { "ProjectId", "IsActive", "NextRunAt" });
        }
    }
}
