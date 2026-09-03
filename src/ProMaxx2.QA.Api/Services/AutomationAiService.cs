using System.Text.Json;

namespace ProMaxx2.QA.Api.Services;

public sealed record AiTestStep(int StepNo, string Action, string? TestData, string ExpectedResult);
public sealed record AutomationAiContext(string TestCaseCode, string Title, string? Objective, string? Preconditions, IReadOnlyList<AiTestStep> Steps, IReadOnlyList<string> AvailableActions, IReadOnlyList<string> ObjectKeys, IReadOnlyList<string> BusinessTerms);
public sealed record AutomationAiResult(string DslJson, double Confidence, string AiProvider, string AiModel);

public sealed class AutomationAiService(SharedAiConfigurationService configuration)
{
    public bool IsConfigured => configuration.IsConfigured;

    public async Task<AutomationAiResult> GenerateAsync(AutomationAiContext context, CancellationToken ct)
    {
        var runtime = await configuration.GetRuntimeAsync(ct);

        var stepsText = context.Steps.Count == 0
            ? "- (ไม่มี Step)"
            : string.Join("\n", context.Steps.Select(s => $"  {s.StepNo}. Action: {s.Action}\n     Test Data: {s.TestData ?? "-"}\n     Expected: {s.ExpectedResult}"));
        var actionsText = context.AvailableActions.Count == 0 ? "-" : string.Join(", ", context.AvailableActions);
        var objectsText = context.ObjectKeys.Count == 0 ? "-" : string.Join(", ", context.ObjectKeys);

        var userText = $"""
            Project: ProMaxx2
            Test Case Code: {context.TestCaseCode}
            Title: {context.Title}
            Objective: {context.Objective ?? "-"}
            Preconditions: {context.Preconditions ?? "-"}

            Test Steps:
            {stepsText}

            Available Actions (ใช้ได้เฉพาะในรายการนี้):
            {actionsText}

            Object Repository (BusinessKey = Screen.Object; ใช้กับ parameter 'object'):
            {objectsText}

            Business Terms: {(context.BusinessTerms.Count == 0 ? "-" : string.Join(", ", context.BusinessTerms))}

            กรุณาแปลง Test Case นี้เป็น Automation DSL v1
            """;

        var payload = new
        {
            model = configuration["OpenAI:Model"] ?? "gpt-5-mini",
            instructions = """
                IMPORTANT OBJECT REPOSITORY RULES:
                - Use an object parameter only when its exact BusinessKey appears in the Object Repository list in the input.
                - Never invent, translate, pluralize, or derive a BusinessKey; an invented key fails validation.
                - If the required UI object is unavailable, do not add an object parameter. Use EXPECT_MESSAGE when appropriate or leave the step for manual review.
                - If the Object Repository list is empty, the DSL must contain no object parameters.
                คุณเป็น Senior Automation QA Engineer เชี่ยวชาญการแปลง Test Case ภาษาคนเป็น Automation DSL v1

                กฎ:
                - ใช้เฉพาะ Action ที่อยู่ในรายการ Available Actions เท่านั้น ห้ามแต่ง Action ใหม่
                - แต่ละ Step ต้องมี stepNo เรียงจาก 1, action ตรงกับ Action Code ใหญ่ (เช่น LOGIN, OPEN_MENU, SELECT_ITEM, SET_QTY, SAVE_DOCUMENT, EXPECT_MESSAGE, EXPECT_TEXT, EXPECT_STOCK)
                - เมื่อต้องการอ้างอิง UI object ให้ใช้ parameter "object" = BusinessKey จาก Object Repository (เช่น "Sales.Qty")
                - ไม่ใส่ข้อมูล credential/รหัสผ่านใน DSL (login ใช้ LOGIN + userRef เท่านั้น)
                - Assertion ใช้ EXPECT_* (EXPECT_MESSAGE, EXPECT_TEXT, EXPECT_VISIBLE, EXPECT_STOCK, EXPECT_TRANSACTION)
                - ถ้าขั้นตอนไม่ชัดเจนให้ใช้ EXPECT_MESSAGE ตรวจสอบข้อความผลลัพธ์แทน
                - ห้ามแต่งขั้นตอนที่ไม่มีใน Test Case
                - confidence = ความมั่นใจว่า DSL ตรงกับ Test Case (0-1)
                """,
            input = new[] { new { role = "user", content = new[] { new Dictionary<string, object?> { { "type", "input_text" }, { "text", userText } } } } },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "automation_dsl_v1",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            dslVersion = new { type = "string", @const = "1.0" },
                            automationType = new { type = "string", @enum = new[] { "WindowsUI" } },
                            steps = new
                            {
                                type = "array",
                                minItems = 1,
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        stepNo = new { type = "integer" },
                                        action = new { type = "string" },
                                        parameters = new { type = "object", additionalProperties = new { type = "string" } }
                                    },
                                    required = new[] { "stepNo", "action" },
                                    additionalProperties = false
                                }
                            },
                            confidence = new { type = "number" }
                        },
                        required = new[] { "dslVersion", "automationType", "steps", "confidence" },
                        additionalProperties = false
                    }
                }
            }
        };

        var text = await configuration.SendStructuredAsync(payload, ct);
        var result = JsonSerializer.Deserialize<AiDslEnvelope>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("AI ส่งข้อมูล DSL กลับมาไม่ครบถ้วน");
        if (result.Steps is null || result.Steps.Count == 0) throw new InvalidOperationException("AI ไม่ได้สร้าง Automation Steps กลับมา");

        var dsl = new
        {
            dslVersion = "1.0",
            automationType = string.IsNullOrWhiteSpace(result.AutomationType) ? "WindowsUI" : result.AutomationType,
            steps = result.Steps.Select(s => new { stepNo = s.StepNo, action = s.Action, parameters = s.Parameters ?? new Dictionary<string, string>() })
        };
        var dslJson = JsonSerializer.Serialize(dsl, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new AutomationAiResult(dslJson, Math.Clamp(result.Confidence, 0, 1), runtime.Provider, runtime.Model);
    }

    private sealed class AiDslEnvelope
    {
        public string? DslVersion { get; set; }
        public string? AutomationType { get; set; }
        public List<AiDslStep>? Steps { get; set; }
        public double Confidence { get; set; }
    }

    private sealed class AiDslStep
    {
        public int StepNo { get; set; }
        public string Action { get; set; } = "";
        public Dictionary<string, string>? Parameters { get; set; }
    }
}
