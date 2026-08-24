namespace ProMaxx2.QA.Application.Automation;

public static class AutomationFailureClassifier
{
    public static AutomationFailureClassificationDto Classify(AutomationExecutionDto execution)
    {
        if (execution.Status != "Failed") return new("NotFailed", false, "None", "Execution ยังไม่ Fail");
        var code = string.IsNullOrWhiteSpace(execution.ErrorCode) ? "" : execution.ErrorCode.Trim();
        var failedSteps = execution.StepResults.Where(s => s.Status == "Fail").ToList();
        var firstStepCode = failedSteps.Select(s => s.ErrorCode?.Trim()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
        var effective = !string.IsNullOrEmpty(code) ? code : firstStepCode;
        var assertionFailed = failedSteps.Any(s => s.ActionCode.StartsWith("EXPECT_", StringComparison.OrdinalIgnoreCase));
        var dbFailed = effective is "AUT-DB-001" or "AUT-DB-002";

        if (dbFailed) return new("EnvironmentFailure", false, "RetryOrCheckEnvironment", "Database เชื่อมต่อ/query ไม่สำเร็จ — ตรวจ Database Profile");
        if (assertionFailed && effective is "AUT-UI-003" or "" or "AUT-UI-002")
            return new("AssertionFailure", true, "QAReviewBeforeCreateDefect", "Assertion/Expectation ไม่ตรง (ผลจริงไม่ตรงคาดหวัง) — ต้อง QA ตรวจสอบก่อนสร้าง Defect (อาจเป็น Product หรือ Test Data)");

        return effective switch
        {
            "AUT-UI-001" or "AUT-UI-002" or "AUT-UI-003" => new("AutomationFailure", false, "MaintenanceRequired", "UI/Object ไม่เสถียร (ObjectNotFound/Disabled/Timeout) — ควรซ่อม Object Repository หรือ DSL ไม่ใช่ Product Defect"),
            "AUT-APP-001" or "AUT-APP-002" => new("EnvironmentFailure", false, "RetryOrCheckEnvironment", "Application เริ่มไม่สำเร็จ/หา Main Window ไม่เจอ — ตรวจ Environment ก่อน"),
            "AUT-AGENT-001" or "AUT-AGENT-002" or "AUT-JOB-001" or "AUT-JOB-002" => new("AgentFailure", false, "Retry", "Agent/Job มีปัญหา (Offline/Session/Timeout/AgentLost) — ตรวจ Agent แล้ว Retry"),
            "AUT-DSL-001" or "AUT-AI-001" => new("AutomationFailure", false, "MaintenanceRequired", "DSL/AI ผิดพลาด — ซ่อม DSL แล้ว Validate ใหม่"),
            "" when failedSteps.Count > 0 => new("AssertionFailure", true, "QAReviewBeforeCreateDefect", "ขั้นตอน Fail โดยผลจริงไม่ตรงคาดหวัง — ต้อง QA ตรวจสอบก่อนสร้าง Defect"),
            "" => new("Unknown", false, "QAReview", "ไม่สามารถระบุประเภท Fail ได้ — ตรวจสอบ Log/Evidence"),
            _ when failedSteps.Count > 0 => new("AssertionFailure", true, "QAReviewBeforeCreateDefect", $"Fail ที่ ErrorCode '{effective}' — ต้อง QA ตรวจสอบก่อนสร้าง Defect"),
            _ => new("Unknown", false, "QAReview", "ไม่สามารถระบุประเภท Fail ได้"),
        };
    }
}