using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Automation;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.TestManagement;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed record AiFailureAnalysisDto(string Classification, double Confidence, string Summary, string Recommendation);

public sealed class AutomationDefectService(
    QaDbContext db,
    AutomationAgentService executions,
    AutomationCaseService cases,
    ITestCaseRepository testCases,
    IProjectRepository projects,
    SharedAiConfigurationService aiConfiguration)
{
    public async Task<AutomationFailureClassificationDto> ClassifyAsync(Guid executionId, Guid projectId, CancellationToken ct)
    {
        var execution = await executions.GetExecutionAsync(executionId, projectId, ct);
        return AutomationFailureClassifier.Classify(execution);
    }

    public async Task<AiFailureAnalysisDto> AnalyzeAsync(Guid executionId, Guid projectId, CancellationToken ct)
    {
        var execution = await executions.GetExecutionAsync(executionId, projectId, ct);
        var caseEntity = await cases.GetCaseAsync(execution.AutomationCaseId, projectId, ct);
        var testCase = await testCases.GetAsync(caseEntity.TestCaseId, ct) ?? throw new EntityNotFoundException("Test case not found.");
        var runtime = await aiConfiguration.GetRuntimeAsync(ct);

        var failedSteps = execution.StepResults.Where(s => s.Status == "Fail").ToList();
        var stepsText = execution.StepResults.Count == 0
            ? "-"
            : string.Join("\n", execution.StepResults.Select(s => $"  Step {s.StepNo} {s.ActionCode} => {s.Status}{(string.IsNullOrWhiteSpace(s.ErrorMessage) ? "" : " | " + s.ErrorMessage)}"));
        var userText = $"""
            Test Case: {caseEntity.TestCaseCode} | {caseEntity.TestCaseTitle}
            Objective: {testCase.Objective ?? "-"}
            Automation: {execution.AutomationCode} (Rev {execution.VersionNo}) | Build {execution.BuildNumber} | Env {execution.EnvironmentName} | Agent {execution.AgentCode ?? "-"}
            Execution status: {execution.Status} | ErrorCode: {execution.ErrorCode ?? "-"} | ErrorMessage: {execution.ErrorMessage ?? "-"}

            Step Results:
            {stepsText}

            วิเคราะห์ว่าความล้มเหลวนี้เป็นประเภทใดและควรแนะนำอะไร
            """;

        var payload = new
        {
            model = aiConfiguration["OpenAI:Model"] ?? "gpt-5-mini",
            instructions = """
                คุณเป็น QA Lead เชี่ยวชาญการวิเคราะห์ความล้มเหลวของ Automated Test (AI Failure Analyzer)

                จำแนก Failure Type จากข้อมูลที่ให้เท่านั้น:
                - ApplicationFailure = แอปทำงานผิด (เช่น แสดงข้อผิดพลาด/ผลไม่ตรง)
                - AutomationFailure = ตัว Automation ผิด (Object/DSL/Action)
                - EnvironmentFailure = Environment/DB/เครื่องผิด
                - TestDataFailure = ข้อมูลทดสอบผิด
                - AssertionFailure = ผลจริงไม่ตรงคาดหวัง (ยังแยกไม่ออกว่า app หรือ automation)
                - AgentFailure = Agent/Job ผิด
                - Unknown

                หมายเหตุ: AI วิเคราะห์เป็นเพียงคำแนะนำเท่านั้น QA เป็นผู้ตัดสินใจสุดท้าย
                """,
            input = new[] { new { role = "user", content = new[] { new Dictionary<string, object?> { { "type", "input_text" }, { "text", userText } } } } },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "failure_analysis",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            classification = new { type = "string", @enum = new[] { "ApplicationFailure", "AutomationFailure", "EnvironmentFailure", "TestDataFailure", "AssertionFailure", "AgentFailure", "Unknown" } },
                            confidence = new { type = "number" },
                            summary = new { type = "string" },
                            recommendation = new { type = "string", @enum = new[] { "QAReviewBeforeCreateDefect", "MaintenanceRequired", "Retry", "RetryOrCheckEnvironment", "Ignore" } }
                        },
                        required = new[] { "classification", "confidence", "summary", "recommendation" },
                        additionalProperties = false
                    }
                }
            }
        };

        var text = await aiConfiguration.SendStructuredAsync(payload, ct);
        var result = JsonSerializer.Deserialize<AiFailureEnvelope>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("AI วิเคราะห์ Fail ไม่สำเร็จ");
        return new AiFailureAnalysisDto(result.Classification, Math.Clamp(result.Confidence, 0, 1), result.Summary ?? "", result.Recommendation ?? "QAReviewBeforeCreateDefect");
    }

    public async Task<object> CreateDefectAsync(Guid executionId, Guid projectId, CreateAutomationDefectRequest request, Guid? userId, CancellationToken ct)
    {
        var execution = await executions.GetExecutionAsync(executionId, projectId, ct);
        if (execution.Status != "Failed") throw new InvalidOperationException("เฉพาะ Execution ที่ Fail เท่านั้นที่สร้าง Defect ได้");
        if (execution.DefectId.HasValue) throw new InvalidOperationException("Execution นี้สร้าง Defect แล้ว");
        var caseEntity = await cases.GetCaseAsync(execution.AutomationCaseId, projectId, ct);
        var testCase = await testCases.GetAsync(caseEntity.TestCaseId, ct) ?? throw new EntityNotFoundException("Test case not found.");
        var classification = string.IsNullOrWhiteSpace(request.Classification)
            ? AutomationFailureClassifier.Classify(execution)
            : new AutomationFailureClassificationDto(request.Classification, request.Classification is "ApplicationFailure" or "AssertionFailure" or "TestDataFailure", "QAReviewBeforeCreateDefect", "QA กำหนดประเภทเอง");
        var project = await projects.GetAsync(projectId, ct) ?? throw new EntityNotFoundException("Project not found.");
        var buildNumber = await db.Builds.Where(b => b.BuildId == execution.BuildId).Select(b => b.BuildNumber).FirstOrDefaultAsync(ct) ?? "-";
        var envName = await db.TestEnvironments.Where(e => e.TestEnvironmentId == execution.EnvironmentId).Select(e => e.EnvironmentName).FirstOrDefaultAsync(ct) ?? "-";
        var agentName = await db.AutomationAgents.Where(a => a.AgentId == execution.AgentId).Select(a => a.AgentCode).FirstOrDefaultAsync(ct);

        var failedSteps = execution.StepResults.Where(s => s.Status == "Fail").ToList();
        var lines = new List<string>
        {
            $"Automation: {execution.AutomationCode} (Execution {executionId})",
            $"Build: {buildNumber} | Environment: {envName}{(string.IsNullOrWhiteSpace(agentName) ? "" : $" | Agent: {agentName}")}",
            $"Failure Type: {classification.FailureType}",
            $"Version: Rev {execution.VersionNo}",
        };
        if (!string.IsNullOrWhiteSpace(execution.ErrorCode)) lines.Add($"ErrorCode: {execution.ErrorCode}");
        if (!string.IsNullOrWhiteSpace(execution.ErrorMessage)) lines.Add($"ErrorMessage: {execution.ErrorMessage}");
        if (failedSteps.Count > 0)
        {
            lines.Add("ขั้นตอนที่ Fail:");
            foreach (var s in failedSteps) lines.Add($"- Step {s.StepNo} {s.ActionCode}: {s.ErrorMessage ?? s.ActualResult ?? "-"}");
        }
        var description = Truncate(string.Join("\n", lines), 2000);
        var stepsText = Truncate(string.Join("\n", execution.StepResults.OrderBy(s => s.StepNo)
            .Select(s => $"{s.StepNo}. {s.ActionCode} ({s.Status}){(string.IsNullOrWhiteSpace(s.ErrorMessage) ? "" : " | " + s.ErrorMessage)}")), 4000);
        var expected = testCase.Steps.FirstOrDefault(s => s.StepNo == failedSteps.FirstOrDefault()?.StepNo)?.ExpectedResult;
        var prefix = $"{project.ProjectCode}-DEF";
        var codes = await db.Defects.Where(x => x.ProjectId == projectId && x.DefectCode.StartsWith(prefix + "-")).Select(x => x.DefectCode).ToListAsync(ct);
        var code = BusinessCodeGenerator.NextAvailable(prefix, codes);
        var title = Truncate(string.IsNullOrWhiteSpace(request.Title) ? $"{execution.AutomationCode} ล้มเหลว: {caseEntity.TestCaseTitle}" : request.Title, 300);
        var severity = string.IsNullOrWhiteSpace(request.Severity) ? "High" : request.Severity;
        var defect = new Defect(projectId, null, execution.BuildId, testCase.ModuleId, code, title, severity, "Open", userId,
            string.IsNullOrWhiteSpace(request.Description) ? description : request.Description,
            stepsText, expected, execution.ErrorMessage ?? failedSteps.FirstOrDefault()?.ErrorMessage ?? "-", testCase.OwnerUserId);
        db.Defects.Add(defect);
        db.DefectTestCaseLinks.Add(new DefectTestCaseLink(defect.DefectId, testCase.TestCaseId, userId));
        db.DefectActivities.Add(new DefectActivity(defect.DefectId, "Created", $"สร้าง Defect จาก Automation Fail {execution.AutomationCode} ({classification.FailureType})", userId));
        db.DefectActivities.Add(new DefectActivity(defect.DefectId, "TestLinked", $"เชื่อมโยง Test Case {testCase.TestCaseCode}", userId));
        await db.SaveChangesAsync(ct);
        var execEntity = await db.AutomationExecutions.SingleOrDefaultAsync(x => x.AutomationExecutionId == executionId, ct);
        if (execEntity is not null)
        {
            execEntity.LinkDefect(defect.DefectId);
            await db.SaveChangesAsync(ct);
        }
        return new { defectCode = defect.DefectCode, defectId = defect.DefectId, classification = classification.FailureType };
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private sealed class AiFailureEnvelope
    {
        public string Classification { get; set; } = "Unknown";
        public double Confidence { get; set; }
        public string? Summary { get; set; }
        public string? Recommendation { get; set; }
    }
}