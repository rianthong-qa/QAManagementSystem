using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeHub : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowCrmDataToExternalAi",
                table: "AiConfigurations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingApiKeyHint",
                table: "AiConfigurations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingBaseUrl",
                table: "AiConfigurations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModel",
                table: "AiConfigurations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingProvider",
                table: "AiConfigurations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "EncryptedEmbeddingApiKey",
                table: "AiConfigurations",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ChatSessions",
                columns: table => new
                {
                    ChatSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessions", x => x.ChatSessionId);
                });

            migrationBuilder.CreateTable(
                name: "CrmTicketAnswers",
                columns: table => new
                {
                    CrmTicketAnswerId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AnswerNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AnswerType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    TextMasked = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnsweredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmTicketAnswers", x => x.CrmTicketAnswerId);
                    table.ForeignKey(
                        name: "FK_CrmTicketAnswers_CrmTickets_JobNo",
                        column: x => x.JobNo,
                        principalTable: "CrmTickets",
                        principalColumn: "JobNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrmTicketContents",
                columns: table => new
                {
                    JobNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DescriptionMasked = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmTicketContents", x => x.JobNo);
                    table.ForeignKey(
                        name: "FK_CrmTicketContents_CrmTickets_JobNo",
                        column: x => x.JobNo,
                        principalTable: "CrmTickets",
                        principalColumn: "JobNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeArticles",
                columns: table => new
                {
                    KnowledgeArticleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeArticles", x => x.KnowledgeArticleId);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeDocuments",
                columns: table => new
                {
                    KnowledgeDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExcludeFromAi = table.Column<bool>(type: "bit", nullable: false),
                    ContainsCustomerData = table.Column<bool>(type: "bit", nullable: false),
                    ChunkCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IndexedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeDocuments", x => x.KnowledgeDocumentId);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeFiles",
                columns: table => new
                {
                    KnowledgeFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExtractedText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExtractError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeFiles", x => x.KnowledgeFileId);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    ChatMessageId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChatSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CitationsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PromptTokens = table.Column<int>(type: "int", nullable: true),
                    CompletionTokens = table.Column<int>(type: "int", nullable: true),
                    Feedback = table.Column<short>(type: "smallint", nullable: true),
                    FeedbackNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.ChatMessageId);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatSessions_ChatSessionId",
                        column: x => x.ChatSessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "ChatSessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeChunks",
                columns: table => new
                {
                    KnowledgeChunkId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KnowledgeDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChunkNo = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Embedding = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    EmbeddingModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EmbeddingDim = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeChunks", x => x.KnowledgeChunkId);
                    table.ForeignKey(
                        name: "FK_KnowledgeChunks_KnowledgeDocuments_KnowledgeDocumentId",
                        column: x => x.KnowledgeDocumentId,
                        principalTable: "KnowledgeDocuments",
                        principalColumn: "KnowledgeDocumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ChatSessionId_ChatMessageId",
                table: "ChatMessages",
                columns: new[] { "ChatSessionId", "ChatMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_UserId_IsDeleted_UpdatedAt",
                table: "ChatSessions",
                columns: new[] { "UserId", "IsDeleted", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmTicketAnswers_JobNo_AnswerNo",
                table: "CrmTicketAnswers",
                columns: new[] { "JobNo", "AnswerNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeArticles_IsDeleted_ProjectId",
                table: "KnowledgeArticles",
                columns: new[] { "IsDeleted", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeChunks_KnowledgeDocumentId_ChunkNo",
                table: "KnowledgeChunks",
                columns: new[] { "KnowledgeDocumentId", "ChunkNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocuments_Scope_ProjectId",
                table: "KnowledgeDocuments",
                columns: new[] { "Scope", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocuments_SourceType_SourceKey",
                table: "KnowledgeDocuments",
                columns: new[] { "SourceType", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocuments_State",
                table: "KnowledgeDocuments",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeFiles_IsDeleted_ProjectId",
                table: "KnowledgeFiles",
                columns: new[] { "IsDeleted", "ProjectId" });

            // สิทธิ์คลังความรู้ — seed ที่นี่แบบ idempotent เพราะ DatabaseInitializer ไม่ทำงานเมื่อ Database:ApplyMigrations=false (แบบเดียวกับ SeedCrmPermissions)
            // KNOWLEDGE.VIEW = ใช้ผู้ช่วย AI / ค้นหาความรู้, KNOWLEDGE.EDIT = เพิ่ม/แก้บทความและไฟล์ในคลังความรู้
            migrationBuilder.Sql("""
                INSERT INTO Permissions (PermissionId, PermissionCode, PermissionName, ModuleArea)
                SELECT NEWID(), c.Code, c.Code, 'KNOWLEDGE'
                FROM (VALUES ('KNOWLEDGE.VIEW'), ('KNOWLEDGE.EDIT')) AS c(Code)
                WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.PermissionCode = c.Code)
                """);

            migrationBuilder.Sql("""
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.RoleId, p.PermissionId
                FROM Roles r CROSS JOIN Permissions p
                WHERE ((p.PermissionCode = 'KNOWLEDGE.VIEW' AND r.RoleCode IN ('SYS_ADMIN','QA_LEAD','QA_TESTER','DEVELOPER','PRODUCT_OWNER','RELEASE_OWNER','VIEWER'))
                    OR (p.PermissionCode = 'KNOWLEDGE.EDIT' AND r.RoleCode IN ('SYS_ADMIN','QA_LEAD')))
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId)
                """);

            // Full-Text Index ภาษาไทย (LCID 1054) สำหรับค้นความรู้ — DDL ของ Full-Text รันใน transaction ไม่ได้จึง suppressTransaction
            // ต้องมี Full-Text Search ติดตั้งใน instance (ตรวจ: SELECT SERVERPROPERTY('IsFullTextInstalled'))
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'KnowledgeCatalog')
                    CREATE FULLTEXT CATALOG KnowledgeCatalog;
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.KnowledgeChunks'))
                    CREATE FULLTEXT INDEX ON dbo.KnowledgeChunks ([Text] LANGUAGE 1054)
                    KEY INDEX PK_KnowledgeChunks ON KnowledgeCatalog
                    WITH CHANGE_TRACKING AUTO;
                """, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.KnowledgeChunks'))
                    DROP FULLTEXT INDEX ON dbo.KnowledgeChunks;
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'KnowledgeCatalog')
                    DROP FULLTEXT CATALOG KnowledgeCatalog;
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                DELETE rp FROM RolePermissions rp JOIN Permissions p ON p.PermissionId = rp.PermissionId WHERE p.PermissionCode IN ('KNOWLEDGE.VIEW','KNOWLEDGE.EDIT');
                DELETE FROM Permissions WHERE PermissionCode IN ('KNOWLEDGE.VIEW','KNOWLEDGE.EDIT');
                """);

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "CrmTicketAnswers");

            migrationBuilder.DropTable(
                name: "CrmTicketContents");

            migrationBuilder.DropTable(
                name: "KnowledgeArticles");

            migrationBuilder.DropTable(
                name: "KnowledgeChunks");

            migrationBuilder.DropTable(
                name: "KnowledgeFiles");

            migrationBuilder.DropTable(
                name: "ChatSessions");

            migrationBuilder.DropTable(
                name: "KnowledgeDocuments");

            migrationBuilder.DropColumn(
                name: "AllowCrmDataToExternalAi",
                table: "AiConfigurations");

            migrationBuilder.DropColumn(
                name: "EmbeddingApiKeyHint",
                table: "AiConfigurations");

            migrationBuilder.DropColumn(
                name: "EmbeddingBaseUrl",
                table: "AiConfigurations");

            migrationBuilder.DropColumn(
                name: "EmbeddingModel",
                table: "AiConfigurations");

            migrationBuilder.DropColumn(
                name: "EmbeddingProvider",
                table: "AiConfigurations");

            migrationBuilder.DropColumn(
                name: "EncryptedEmbeddingApiKey",
                table: "AiConfigurations");
        }
    }
}
