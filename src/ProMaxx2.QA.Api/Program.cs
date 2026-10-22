using ProMaxx2.QA.Application.Dashboard;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ProMaxx2.QA.Application.Identity;
using ProMaxx2.QA.Infrastructure;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;
using System.Text;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.Releases;
using ProMaxx2.QA.Application.Requirements;
using ProMaxx2.QA.Application.TestManagement;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Governance;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Performance;
using ProMaxx2.QA.Application.Knowledge;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers(options => options.Filters.Add<ProjectAccessFilter>());
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ProMaxx2 QA Management System API",
        Version = "v1",
        Description = "REST API สำหรับระบบจัดการคุณภาพ QA - ProMaxx2 QA Hub",
        Contact = new OpenApiContact { Name = "ProMaxx2 Team" }
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Token จากการ Login\n\nใส่ token โดยไม่ต้องมีคำว่า \"Bearer \" นำหน้า"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient();
builder.Services.AddDataProtection();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<ReleaseService>();
builder.Services.AddScoped<TestSummaryService>();
builder.Services.AddScoped<RiskAcceptanceService>();
builder.Services.AddScoped<CrmMirrorSyncService>(); // สำเนา CRM Ticket ทั้งทีม (บัญชีกลาง CrmMirror:AccountUsername) สำหรับวัดผลงาน
builder.Services.AddHostedService<CrmMirrorWorker>(); // ดึงทุก 15 นาที (90 วัน) + รอบใหญ่ 366 วันวันละครั้ง
builder.Services.AddScoped<ICrmPerformanceSource, CrmPerformanceSource>(); // ผลงาน CRM รายคน (หน้า ผลงานและการประเมิน)
builder.Services.AddScoped<PerformanceService>();
builder.Services.AddScoped<EvaluationService>();
builder.Services.AddScoped<PerformanceReportService>();
builder.Services.AddScoped<PerformanceShareLinkService>();
builder.Services.AddScoped<WeeklyReportService>(); // สรุปประจำสัปดาห์ส่งผู้บริหาร (หน้า ผลงานและการประเมิน)
builder.Services.AddScoped<PerformanceEvidenceStorage>(); // ไฟล์หลักฐานของบันทึกงาน (App_Data/PerformanceEvidence)
builder.Services.AddScoped<PerformanceReminderService>();
builder.Services.AddHostedService<PerformanceReminderWorker>(); // เตือนหัวหน้าทางอีเมลเมื่อการประเมินใกล้/เลยกำหนด ทุก 1 ชม.
builder.Services.AddScoped<ReleaseSignoffService>();
builder.Services.AddScoped<RequirementService>();
builder.Services.AddScoped<TestCaseService>();
builder.Services.AddScoped<TestSuiteService>();
builder.Services.AddScoped<TestCycleService>();
builder.Services.AddScoped<ExecutionService>();
builder.Services.AddScoped<AutomationCaseService>();
builder.Services.AddScoped<AutomationSuiteService>();
builder.Services.AddScoped<AutomationScheduleService>();
builder.Services.AddHostedService<AutomationScheduleWorker>(); // AUT-P1-006: polls and fires due Automation Schedules
builder.Services.AddHostedService<AutomationReaperWorker>(); // AUT-REL-002: closes executions/data jobs whose agent disappeared
builder.Services.AddScoped<AutomationBuildTriggerService>();
builder.Services.AddScoped<RegressionScheduleTriggerService>(); // AUT-REG-002
builder.Services.AddScoped<AutomationWebhookService>();
builder.Services.AddScoped<AutomationDataSnapshotService>(); // AUT-DATA-001
builder.Services.AddScoped<AutomationDataRestoreService>(); // AUT-DATA-002
builder.Services.AddScoped<AutomationDataSeedService>(); // AUT-DATA-003
builder.Services.AddScoped<AutomationEnvironmentDataProfileService>(); // AUT-DATA-006
builder.Services.AddScoped<AutomationAgentService>();
builder.Services.AddScoped<AutomationAiService>();
builder.Services.AddScoped<AutomationDefectService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<AdministrationService>();
builder.Services.AddScoped<RequirementAiService>();
builder.Services.AddScoped<TestCaseAiService>();
builder.Services.AddScoped<TestSuiteAiService>();
builder.Services.AddScoped<TestCycleAiService>();
builder.Services.AddScoped<DefectAutoCreateService>();
builder.Services.AddScoped<DefectActivityService>();
builder.Services.AddScoped<SharedAiConfigurationService>();
// คลังความรู้ (AI_KNOWLEDGE_CHAT_PLAN.md): index Defect/บทความ ทุก 5 นาที + ค้นหา Full-Text ภาษาไทย
builder.Services.AddScoped<KnowledgeIndexService>();
builder.Services.AddScoped<IKnowledgeTextSearch, SqlKnowledgeTextSearch>();
builder.Services.AddScoped<KnowledgeSearchService>();
// Phase 5: ค้นตามความหมาย (embedding เก็บใน KnowledgeChunks + cosine ในแอป) — ไม่ได้ตั้งค่า Embedding = ค้นด้วยคำอย่างเดียว
builder.Services.AddScoped<IKnowledgeEmbeddingSettings>(sp => sp.GetRequiredService<SharedAiConfigurationService>());
builder.Services.AddScoped<IEmbeddingClient, OpenAiCompatibleEmbeddingClient>();
builder.Services.AddSingleton<KnowledgeEmbeddingState>();
builder.Services.AddScoped<KnowledgeEmbeddingService>();
builder.Services.AddScoped<IKnowledgeVectorSearch>(sp => sp.GetRequiredService<KnowledgeEmbeddingService>());
builder.Services.AddScoped<KnowledgeFileService>(); // Phase 3: เอกสาร PDF/DOCX/MD/TXT
builder.Services.AddScoped<ICrmKnowledgeSource, CrmApiKnowledgeSource>(); // Phase 4: ดึงรายละเอียด/คำตอบ CRM ด้วยบัญชีกลาง
builder.Services.AddScoped<CrmKnowledgeFetcher>(); // mask แล้วเก็บ CrmTicketContents/Answers — เรียกจาก CrmMirrorWorker ทุกรอบ
builder.Services.AddScoped<IAiChatClient, AiChatClient>();
builder.Services.AddScoped<KnowledgeChatService>();
// MCP server ให้ Claude Code / Claude Desktop ค้นคลังความรู้ (AI_KNOWLEDGE_CHAT_PLAN.md Phase 2B) — stateless (ไม่ใช้ sampling/elicitation)
builder.Services.AddScoped<PersonalAccessTokenService>();
builder.Services.AddScoped<OAuthService>(); // OAuth 2.1 (DCR + PKCE) ให้ claude.ai Team เชื่อมต่อแบบ Login รายคน
builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "promaxx2-qa-hub", Title = "ProMaxx2 QA Hub — คลังความรู้", Version = "1.0.0" };
        options.ServerInstructions = KnowledgeMcpTools.ServerInstructions;
    })
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithTools<KnowledgeMcpTools>();
builder.Services.AddSingleton<KnowledgeIndexTrigger>();
builder.Services.AddHostedService<KnowledgeIndexWorker>(); // ปิดได้ด้วย Knowledge:IndexerEnabled=false
builder.Services.AddMemoryCache(); // CRM ticket list cache (CrmApiClient) — 60 วินาทีต่อผู้ใช้+ตัวกรอง
builder.Services.AddSingleton<CrmStaffDirectoryCache>(); // directory พนักงาน BlueID ใช้ร่วมทุกผู้ใช้ — cache 1 ชม.
builder.Services.AddSingleton<CrmTokenService>(); // must outlive request scope to actually cache the ~24h BlueID token
builder.Services.AddScoped<CrmConfigurationService>();
builder.Services.AddScoped<CrmSyncSettingsService>();
builder.Services.AddScoped<CrmApiClient>();
builder.Services.AddScoped<CrmTicketDetailService>();
builder.Services.AddScoped<CrmFlowTrackingService>();
builder.Services.AddScoped<CrmSendToCrmService>();
builder.Services.AddScoped<DefectShareLinkService>();
builder.Services.AddScoped<DefectImageStorage>();
builder.Services.AddScoped<EmailConfigurationService>();
builder.Services.AddScoped<EmailSenderService>();
builder.Services.AddScoped<CrmSyncService>();
builder.Services.AddScoped<IAuthorizationHandler, CrmViewAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CrmEditAuthorizationHandler>();
builder.Services.AddHostedService<CrmSyncWorker>(); // Phase 2: polls Linked Defects every 2min for CRM status/assignto changes
builder.Services.AddScoped<ProjectAccessContext>();
builder.Services.AddScoped<ProjectScopeGuard>();
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? throw new InvalidOperationException("Missing Jwt configuration.");
var jwtKey = builder.Configuration["Jwt:Key"] ?? "";
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32) throw new InvalidOperationException("Jwt:Key must contain at least 32 bytes. Set it via an environment variable or secret store — do not commit a signing key.");
if (!builder.Environment.IsDevelopment() && jwtKey.Contains("development-only-key-change-before-production", StringComparison.Ordinal)) throw new InvalidOperationException("Jwt:Key is the development placeholder — configure a production signing key via environment/secret store.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer=true, ValidIssuer=jwt.Issuer, ValidateAudience=true, ValidAudience=jwt.Audience, ValidateLifetime=true, ValidateIssuerSigningKey=true, IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), ClockSkew=TimeSpan.FromSeconds(30) };
    // /mcp (Claude ผ่าน MCP) ยืนยันตัวด้วย Personal Access Token เท่านั้น — path อื่น token qak_ ไม่ใช่ JWT จึงถูกปฏิเสธตามปกติ
    options.ForwardDefaultSelector = context => context.Request.Path.StartsWithSegments("/mcp") ? PersonalAccessTokenAuthenticationHandler.SchemeName : null;
}).AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, PersonalAccessTokenAuthenticationHandler>(PersonalAccessTokenAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminUser",p=>p.RequireClaim("permission","ADMIN.USER"))
    .AddPolicy("ProjectView",p=>p.RequireClaim("permission","PROJECT.VIEW"))
    .AddPolicy("ProjectEdit",p=>p.RequireClaim("permission","PROJECT.EDIT"))
    .AddPolicy("RequirementView",p=>p.RequireClaim("permission","REQUIREMENT.VIEW"))
    .AddPolicy("RequirementEdit",p=>p.RequireClaim("permission","REQUIREMENT.EDIT"))
    .AddPolicy("TestCaseView",p=>p.RequireClaim("permission","TESTCASE.VIEW"))
    .AddPolicy("TestCaseEdit",p=>p.RequireClaim("permission","TESTCASE.EDIT"))
    .AddPolicy("SysAdminOnly",p=>p.RequireRole("SYS_ADMIN"))
    .AddPolicy("RegressionView",p=>p.RequireAssertion(c=>c.User.IsInRole("SYS_ADMIN")||c.User.HasClaim("permission","REGRESSION.VIEW")))
    .AddPolicy("RegressionManage",p=>p.RequireAssertion(c=>c.User.IsInRole("SYS_ADMIN")||c.User.HasClaim("permission","REGRESSION.MANAGE")))
    .AddPolicy("DefectView",p=>p.RequireClaim("permission","DEFECT.VIEW"))
    .AddPolicy("DefectEdit",p=>p.RequireClaim("permission","DEFECT.EDIT"))
    .AddPolicy("CrmView",p=>p.AddRequirements(new CrmViewRequirement()))
    .AddPolicy("CrmEdit",p=>p.AddRequirements(new CrmEditRequirement()))
    .AddPolicy("ExecutionRun",p=>p.RequireClaim("permission","EXECUTION.RUN"))
    .AddPolicy("QaWorkloadView",p=>p.RequireClaim("permission","QA.WORKLOAD.VIEW"))
    .AddPolicy("QaMyWorkView",p=>p.RequireClaim("permission","QA.MYWORK.VIEW"))
    .AddPolicy("QaMyWorkExecute",p=>p.RequireClaim("permission","QA.MYWORK.EXECUTE"))
    .AddPolicy("QaAssignmentCreate",p=>p.RequireClaim("permission","QA.ASSIGN.CREATE"))
    .AddPolicy("QaAssignmentReassign",p=>p.RequireClaim("permission","QA.ASSIGN.REASSIGN"))
    .AddPolicy("QaAssignmentAuto",p=>p.RequireClaim("permission","QA.ASSIGN.AUTO"))
    .AddPolicy("RiskApprove",p=>p.RequireClaim("permission","RISK.APPROVE"))
    .AddPolicy("ReleaseSignoff",p=>p.RequireClaim("permission","RELEASE.SIGNOFF"))
    .AddPolicy("AuditView",p=>p.RequireAssertion(c=>c.User.IsInRole("SYS_ADMIN")||c.User.HasClaim("permission","AUDIT.VIEW")))
    .AddPolicy("KnowledgeView",p=>p.RequireAssertion(c=>c.User.IsInRole("SYS_ADMIN")||c.User.HasClaim("permission","KNOWLEDGE.VIEW")))
    .AddPolicy("KnowledgeEdit",p=>p.RequireAssertion(c=>c.User.IsInRole("SYS_ADMIN")||c.User.HasClaim("permission","KNOWLEDGE.EDIT")))
    // สร้าง/ยกเลิก Token เชื่อม Claude (หน้าเว็บ, JWT)
    .AddPolicy("KnowledgeMcpManage",p=>p.RequireAssertion(c=>c.User.IsInRole("SYS_ADMIN")||c.User.HasClaim("permission","KNOWLEDGE.MCP")))
    // endpoint /mcp — ต้องเป็น Personal Access Token scope knowledge:read และยังมีสิทธิ์อยู่ ณ ตอนเรียก
    .AddPolicy("KnowledgeMcp",p=>p.AddAuthenticationSchemes(PersonalAccessTokenAuthenticationHandler.SchemeName).RequireAuthenticatedUser().RequireAssertion(c=>
        c.User.HasClaim("scope",ProMaxx2.QA.Domain.Identity.PersonalAccessToken.KnowledgeReadScope)
        && (c.User.IsInRole("SYS_ADMIN")||(c.User.HasClaim("permission","KNOWLEDGE.VIEW")&&c.User.HasClaim("permission","KNOWLEDGE.MCP")))))
    .AddPolicy("PerformanceView",p=>p.RequireAssertion(c=>c.User.IsInRole("SYS_ADMIN")||c.User.HasClaim("permission","PERFORMANCE.VIEW")||c.User.HasClaim("permission","PERFORMANCE.EDIT")))
    .AddAutomationPolicies()
    // endpoint ที่ลืมใส่ [Authorize] ต้องไม่กลายเป็น public โดยปริยาย — endpoint anonymous ต้องประกาศ [AllowAnonymous] เอง
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
// จำกัดความถี่ endpoint anonymous ที่เดารหัสได้ — key ตาม IP จริงของ client (หลัง Cloudflare ใช้ CF-Connecting-IP)
static string ClientKey(HttpContext http) => http.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(ClientKey(http),
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("webhook", http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(ClientKey(http),
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // ลิงก์แชร์ Defect (anonymous) — เปิดหน้าหนึ่งครั้งยิง 1 + จำนวนรูป (รูป Defect ≤5 + รูปคอมเมนต์ ≤5/คอมเมนต์) คำขอ
    options.AddPolicy("share", http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(ClientKey(http),
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // OAuth ของ MCP — ผู้ใช้ claude.ai ทุกคนมาจากช่วง IP ของ Anthropic (160.79.104.0/21) จึงตั้งเพดานต่อ IP ให้สูงพอ
    options.AddPolicy("oauth", http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(ClientKey(http),
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // MCP (Claude) — จำกัดต่อ Personal Access Token
    options.AddPolicy("knowledge-mcp", http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirst("token_id")?.Value ?? ClientKey(http),
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // ผู้ช่วย AI คลังความรู้ — จำกัดต่อผู้ใช้ (ทุกคำถามเรียก LLM)
    options.AddPolicy("knowledge-chat", http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? ClientKey(http),
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();
if (allowedOrigins == null || allowedOrigins.Length == 0)
{
    allowedOrigins = new[] { "http://localhost:5173", "http://192.168.200.219:5173" };
}
builder.Services.AddCors(options => options.AddPolicy("Web", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()
    // หน้าเว็บกับ API อยู่คนละโดเมน ทุกคำขอที่มี Authorization จึงต้องมี preflight (OPTIONS) — ถ้าไม่ส่ง Access-Control-Max-Age
    // Chromium จำผลได้แค่ 5 วินาที ทำให้แทบทุกคำขอเสีย round trip เพิ่มอีกรอบผ่าน Cloudflare (วัดได้ 0.2–1.2 วินาที/รอบ);
    // 2 ชั่วโมงคือค่าสูงสุดที่ Chromium ยอมรับ
    .SetPreflightMaxAge(TimeSpan.FromHours(2))));
var app = builder.Build();
// CORS ต้องครอบ exception handler ด้วย เพื่อให้ response ที่เกิดจาก exception ยังมี
// Access-Control-Allow-Origin; ไม่เช่นนั้น browser จะแสดง "Failed to fetch" แทน error จริง
app.UseCors("Web");
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Trace-Id"] = context.TraceIdentifier;
    // Defect attachment/evidence ถูกเสิร์ฟแบบ inline — ห้าม browser เดา content-type เองจากเนื้อไฟล์
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ProMaxx2 QA API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "ProMaxx2 QA Management System API";
    });
}
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapHealthChecks("/health").AllowAnonymous();
// MCP ปิดอยู่ใน Setting Center = ปฏิเสธทุกคำขอ (ตรวจก่อน endpoint ทำงาน)
app.UseWhen(context => context.Request.Path.StartsWithSegments("/mcp"), mcp => mcp.Use(async (context, next) =>
{
    if (!await context.RequestServices.GetRequiredService<PersonalAccessTokenService>().IsMcpEnabledAsync(context.RequestAborted))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { detail = "ผู้ดูแลระบบปิดการเชื่อมต่อจาก Claude (MCP) อยู่" });
        return;
    }
    await next();
}));
app.MapControllers();
app.MapMcp("/mcp").RequireAuthorization("KnowledgeMcp").RequireRateLimiting("knowledge-mcp");
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
    await app.Services.InitializeDatabaseAsync(builder.Configuration["Seed:AdminPassword"]);
app.Run();

public partial class Program;
