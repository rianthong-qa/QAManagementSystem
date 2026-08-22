using System.Text.Json;
using System.Text.Json.Serialization;

namespace Promaxx2.Automation.Hub;

// ---------------------------------------------------------------------------
// Naming Convention (Phase 0.2):
//   Automation test id = TestCaseCode ของ QA Hub แบบตรงตัว เช่น "TC-SALE-001"
//   → trace กลับไป-มาได้โดยไม่ต้องแปล, ห้าม rename ฝั่ง automation
// targetApp: "pos" = PromaxxsPos.exe (บิลขาย) / "app" = Promaxxs.App.exe (Master Data)
// ---------------------------------------------------------------------------

public sealed record TestPlan(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("generatedAtUtc")] string GeneratedAtUtc,
    [property: JsonPropertyName("projectId")] Guid? ProjectId,
    [property: JsonPropertyName("testCycleId")] Guid? TestCycleId,
    [property: JsonPropertyName("releaseCode")] string? ReleaseCode,
    [property: JsonPropertyName("buildNumber")] string? BuildNumber,
    [property: JsonPropertyName("cases")] IReadOnlyList<TestPlanCase> Cases);

public sealed record TestPlanCase(
    [property: JsonPropertyName("testCaseId")] Guid? TestCaseId,
    [property: JsonPropertyName("testCaseCode")] string TestCaseCode,
    [property: JsonPropertyName("targetApp")] string TargetApp,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("module")] string Module,
    [property: JsonPropertyName("prerequisites")] IReadOnlyList<string> Prerequisites,
    [property: JsonPropertyName("steps")] IReadOnlyList<TestPlanStep> Steps);

public sealed record TestPlanStep(
    [property: JsonPropertyName("stepNo")] int StepNo,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("expected")] string Expected);

public static class TestPlanMapper
{
    public const string DefaultTargetApp = "pos";
    public const string SchemaVersion = "1.1";

    /// <summary>
    /// แปลง TestCaseDto จาก QA Hub → TestPlan
    /// - id = TestCaseCode (naming convention)
    /// - เรียงตาม code, steps เรียงตาม StepNo
    /// </summary>
    public static TestPlan ToTestPlan(
        TestPlanSource source,
        string targetApp,
        string? releaseCode = null,
        string? buildNumber = null)
    {
        ValidateTargetApp(targetApp);

        var cases = source.Cases
            .Select(c => new TestPlanCase(
                TestCaseId: c.TestCaseId,
                TestCaseCode: c.TestCaseCode,
                TargetApp: targetApp,
                Title: c.Title,
                Priority: c.Priority,
                Module: source.Modules.TryGetValue(c.ModuleId, out var name) ? name : c.ModuleId.ToString(),
                Prerequisites: [],
                Steps: c.Steps
                    .OrderBy(s => s.StepNo)
                    .Select(s => new TestPlanStep(s.StepNo, s.Action, s.TestData ?? "", s.ExpectedResult))
                    .ToList()))
            .OrderBy(c => c.TestCaseCode, StringComparer.Ordinal)
            .ToList();

        // duplicate code = contract violation — ปฏิเสธตั้งแต่ export ไม่ใช่ตอนรัน
        var dupes = cases.GroupBy(c => c.TestCaseCode).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
            throw new InvalidOperationException($"Duplicate TestCaseCode in export: {string.Join(", ", dupes)}");

        var projectIds = source.Cases.Select(c => c.ProjectId).Distinct().ToList();
        if (projectIds.Count > 1)
            throw new InvalidOperationException("All exported test cases must belong to the same project.");

        return new TestPlan(SchemaVersion, DateTime.UtcNow.ToString("o"), projectIds.SingleOrDefault(), null, releaseCode, buildNumber, cases);
    }

    public static void ValidateTargetApp(string targetApp)
    {
        if (targetApp is not ("pos" or "app"))
            throw new ArgumentException($"targetApp must be \"pos\" or \"app\", got \"{targetApp}\".");
    }
}

public static class TestPlanWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task WriteAsync(TestPlan plan, string outputPath, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(plan, WriteOptions), ct);
    }
}
