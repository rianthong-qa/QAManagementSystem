using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Domain.Requirements;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Settings;
using ProMaxx2.QA.Domain.Dashboard;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Automation;
using ProMaxx2.QA.Domain.Integrations;

namespace ProMaxx2.QA.Infrastructure.Persistence;

public sealed class QaDbContext(DbContextOptions<QaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProductModule> Modules => Set<ProductModule>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<Build> Builds => Set<Build>();
    public DbSet<Requirement> Requirements => Set<Requirement>();
    public DbSet<RequirementRevision> RequirementRevisions => Set<RequirementRevision>();
    public DbSet<TestCase> TestCases => Set<TestCase>();
    public DbSet<TestStep> TestSteps => Set<TestStep>();
    public DbSet<TestCaseRevision> TestCaseRevisions => Set<TestCaseRevision>();
    public DbSet<RequirementTestCase> RequirementTestCases => Set<RequirementTestCase>();
    public DbSet<TestSuite> TestSuites => Set<TestSuite>();
    public DbSet<TestSuiteCase> TestSuiteCases => Set<TestSuiteCase>();
    public DbSet<TestEnvironment> TestEnvironments => Set<TestEnvironment>();
    public DbSet<TestCycle> TestCycles => Set<TestCycle>();
    public DbSet<TestCycleCase> TestCycleCases => Set<TestCycleCase>();
    public DbSet<TestExecution> TestExecutions => Set<TestExecution>();
    public DbSet<RegressionAnalysis> RegressionAnalyses => Set<RegressionAnalysis>();
    public DbSet<RegressionActivity> RegressionActivities => Set<RegressionActivity>();
    public DbSet<RegressionProfile> RegressionProfiles => Set<RegressionProfile>();
    public DbSet<RegressionSchedule> RegressionSchedules => Set<RegressionSchedule>();
    public DbSet<TestStepResult> TestStepResults => Set<TestStepResult>();
    public DbSet<QaSkillMatrixEntry> QaSkillMatrixEntries => Set<QaSkillMatrixEntry>();
    public DbSet<QaAvailability> QaAvailabilities => Set<QaAvailability>();
    public DbSet<AssignmentPreview> AssignmentPreviews => Set<AssignmentPreview>();
    public DbSet<AssignmentHistory> AssignmentHistories => Set<AssignmentHistory>();
    public DbSet<TestCycleCaseAssignment> TestCycleCaseAssignments => Set<TestCycleCaseAssignment>();
    public DbSet<Defect> Defects => Set<Defect>();
    public DbSet<DefectActivity> DefectActivities => Set<DefectActivity>();
    public DbSet<DefectTestCaseLink> DefectTestCaseLinks => Set<DefectTestCaseLink>();
    public DbSet<MasterOption> MasterOptions => Set<MasterOption>();
    public DbSet<AiConfiguration> AiConfigurations => Set<AiConfiguration>();
    public DbSet<EmailConfiguration> EmailConfigurations => Set<EmailConfiguration>();
    public DbSet<CrmConfiguration> CrmConfigurations => Set<CrmConfiguration>();
    public DbSet<CrmProjectMapping> CrmProjectMappings => Set<CrmProjectMapping>();
    public DbSet<CrmSyncSettings> CrmSyncSettings => Set<CrmSyncSettings>();
    public DbSet<DashboardShare> DashboardShares => Set<DashboardShare>();
    public DbSet<ProjectUser> ProjectUsers => Set<ProjectUser>();
    public DbSet<RiskAcceptance> RiskAcceptances => Set<RiskAcceptance>();
    public DbSet<ReleaseSignoff> ReleaseSignoffs => Set<ReleaseSignoff>();
    public DbSet<AutomationCase> AutomationCases => Set<AutomationCase>();
    public DbSet<AutomationVersion> AutomationVersions => Set<AutomationVersion>();
    public DbSet<AutomationAction> AutomationActions => Set<AutomationAction>();
    public DbSet<AutomationObject> AutomationObjects => Set<AutomationObject>();
    public DbSet<AutomationAgent> AutomationAgents => Set<AutomationAgent>();
    public DbSet<AutomationAgentCapability> AutomationAgentCapabilities => Set<AutomationAgentCapability>();
    public DbSet<AutomationAgentHeartbeatEvent> AutomationAgentHeartbeatEvents => Set<AutomationAgentHeartbeatEvent>();
    public DbSet<AutomationExecution> AutomationExecutions => Set<AutomationExecution>();
    public DbSet<AutomationStepResult> AutomationStepResults => Set<AutomationStepResult>();
    public DbSet<AutomationJob> AutomationJobs => Set<AutomationJob>();
    public DbSet<AutomationEvidence> AutomationEvidences => Set<AutomationEvidence>();
    public DbSet<AutomationObjectVerification> AutomationObjectVerifications => Set<AutomationObjectVerification>();
    public DbSet<AutomationRetryPolicySettings> AutomationRetryPolicySettings => Set<AutomationRetryPolicySettings>();
    public DbSet<AutomationSuite> AutomationSuites => Set<AutomationSuite>();
    public DbSet<AutomationSuiteCase> AutomationSuiteCases => Set<AutomationSuiteCase>();
    public DbSet<AutomationSuiteRevision> AutomationSuiteRevisions => Set<AutomationSuiteRevision>();
    public DbSet<AutomationSchedule> AutomationSchedules => Set<AutomationSchedule>();
    public DbSet<AutomationScheduleRun> AutomationScheduleRuns => Set<AutomationScheduleRun>();
    public DbSet<AutomationScheduleNotification> AutomationScheduleNotifications => Set<AutomationScheduleNotification>();
    public DbSet<AutomationBuildTriggerPolicy> AutomationBuildTriggerPolicies => Set<AutomationBuildTriggerPolicy>();
    public DbSet<AutomationBuildTriggerRun> AutomationBuildTriggerRuns => Set<AutomationBuildTriggerRun>();
    public DbSet<AutomationWebhookToken> AutomationWebhookTokens => Set<AutomationWebhookToken>();
    public DbSet<AutomationWebhookDelivery> AutomationWebhookDeliveries => Set<AutomationWebhookDelivery>();
    public DbSet<AutomationDbSnapshot> AutomationDbSnapshots => Set<AutomationDbSnapshot>();
    public DbSet<AutomationDbRestore> AutomationDbRestores => Set<AutomationDbRestore>();
    public DbSet<AutomationDataSeedScript> AutomationDataSeedScripts => Set<AutomationDataSeedScript>();
    public DbSet<AutomationDataSeedRun> AutomationDataSeedRuns => Set<AutomationDataSeedRun>();
    public DbSet<AutomationEnvironmentDataProfile> AutomationEnvironmentDataProfiles => Set<AutomationEnvironmentDataProfile>();
    public DbSet<AutomationCaptureSession> AutomationCaptureSessions => Set<AutomationCaptureSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyConfigurationsFromAssembly(typeof(QaDbContext).Assembly);

    // ทุก DateTime ในระบบเก็บเป็น UTC เสมอ (DateTime.UtcNow — ไม่มีที่ไหนใช้ DateTime.Now) แต่ SQL Server
    // datetime2 ไม่เก็บ DateTimeKind ไว้ พออ่านค่ากลับมาจาก DB ผ่าน EF Core ค่าจะกลายเป็น Kind=Unspecified
    // เสมอ ทำให้ System.Text.Json serialize ออกไปโดยไม่มี "Z" ต่อท้าย (เช่น "2026-08-30T10:15:00" แทนที่จะ
    // เป็น "...Z") ฝั่ง frontend (browser) จึงตีความผิดว่าเป็นเวลา local แทน UTC — ทำให้หน้า Defect (และ
    // หน้าอื่นที่อ่านค่าจาก DB) แสดงวันที่/เวลาเพี้ยนไปเท่ากับ timezone offset ของเครื่อง (เช่น ผิดไป 7
    // ชั่วโมงถ้าเทียบกับเวลาไทย) ใช้ convention นี้ทวง Kind=Utc กลับคืนให้ทุก DateTime/DateTime? property
    // ทุกครั้งที่อ่านออกจาก DB เพื่อให้ serialize มี Z แนบมาด้วยเสมอ — ไม่ต้องแก้ทีละ entity/DTO
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }
}

file sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v,
    v => v.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : v.ToUniversalTime());

file sealed class UtcNullableDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v,
    v => v.HasValue ? (v.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v.Value.ToUniversalTime()) : v);
