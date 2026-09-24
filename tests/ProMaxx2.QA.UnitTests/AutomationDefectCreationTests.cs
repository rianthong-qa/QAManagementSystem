using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Automation;

namespace ProMaxx2.QA.UnitTests;

/// <summary>AUT-UI-001: สร้าง Defect จาก Execution ที่ Fail ต้องเชื่อม execution ในการบันทึกเดียวกัน และสร้างซ้ำไม่ได้</summary>
public sealed class AutomationDefectCreationTests
{
    [Fact]
    public async Task Creating_a_defect_links_the_execution_and_a_second_attempt_is_rejected()
    {
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var agents = AutomationTestFixtures.AgentService(db);
        var (baseline, readyCase, versionId) = await AutomationTestFixtures.SeedReadyCaseAsync(db);
        var projectId = baseline.Project.ProjectId;
        await agents.RegisterAsync(new RegisterAgentRequest("AGENT-A", "MACHINE-A", "1.0.0", "Windows", "x64", []), null, CancellationToken.None);
        await agents.RequestExecutionAsync(projectId, new RequestExecutionRequest(readyCase.AutomationCaseId, versionId, baseline.Build.BuildId, baseline.Environment.TestEnvironmentId, null, 5), null, CancellationToken.None);
        var claim = await agents.ClaimNextJobAsync(new ClaimJobRequest("AGENT-A", "1.0.0", [], "WindowsUI"), CancellationToken.None) ?? throw new InvalidOperationException("Expected a job.");
        await agents.CompleteExecutionAsync(claim.AutomationExecutionId, new CompleteExecutionRequest("Failed", "AutomationFailure", "AUT-UI-003", "Assertion failed."), CancellationToken.None);

        // SharedAiConfigurationService is only used by AnalyzeAsync, not by CreateDefectAsync.
        var service = new AutomationDefectService(db, agents, AutomationTestFixtures.CaseService(db, projectId),
            AutomationTestFixtures.TestCaseRepository(db, projectId), AutomationTestFixtures.ProjectRepository(db, projectId), null!);
        var request = new CreateAutomationDefectRequest(null, "High", null, null);

        await service.CreateDefectAsync(claim.AutomationExecutionId, projectId, request, null, CancellationToken.None);

        var execution = await db.AutomationExecutions.AsNoTracking().SingleAsync(x => x.AutomationExecutionId == claim.AutomationExecutionId);
        var defect = await db.Defects.AsNoTracking().SingleAsync();
        Assert.Equal(defect.DefectId, execution.DefectId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateDefectAsync(claim.AutomationExecutionId, projectId, request, null, CancellationToken.None));
        Assert.Equal(1, await db.Defects.CountAsync());
    }
}
