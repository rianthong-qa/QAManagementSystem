using System.Text.Json;
using ProMaxx2.QA.Application.TestManagement;

namespace ProMaxx2.QA.Api.Services;

public sealed record GeneratedTestCycle(string CycleName,string CycleType,string?StartDate,string?EndDate,string?Notes,string SelectionSummary);
public sealed record GeneratedTestCycles(IReadOnlyList<GeneratedTestCycle>Cycles);

public sealed class TestCycleAiService(SharedAiConfigurationService configuration)
{
    public bool IsConfigured => configuration.IsConfigured;
    public async Task<IReadOnlyList<GeneratedTestCycle>>GenerateAsync(string projectName,string releaseCode,string?releasePlannedDate,string buildNumber,string environmentName,TestSuiteDto?suite,int totalSuiteCases,IReadOnlyList<string>cycleTypes,IReadOnlyList<string>existingCycleNames,CancellationToken ct)
    {
        var runtime=await configuration.GetRuntimeAsync(ct);
        if(cycleTypes.Count==0)throw new ArgumentException("กรุณาตั้งค่า Test Cycle Type ในการตั้งค่ากลางก่อนใช้งาน AI");
        var suiteText=suite is null?"- ไม่ระบุ Test Suite":$"- Suite: {suite.SuiteName} | Type {suite.SuiteType??"-"} | Risk {suite.RiskTier??"-"} | Cases {totalSuiteCases} รายการ{(suite.Cases.Count<totalSuiteCases?$" (แสดง {suite.Cases.Count} รายการแรก)":"")}\n{CasesText(suite)}";
        var content=new List<Dictionary<string,object?>>{new(){{"type","input_text"},{"text",$"Project: {projectName}\nRelease: {releaseCode} (Planned: {releasePlannedDate??"-"})\nBuild: {buildNumber}\nEnvironment: {environmentName}\n\nAllowed Cycle Types: {string.Join(", ",cycleTypes)}\nExisting Cycle Names: {string.Join(", ",existingCycleNames)}\n\nSelected Test Suite:\n{suiteText}"}}};
        var payload=new
        {
            model=runtime.Model,
            store=false,
            instructions=$"คุณเป็น QA Test Lead วางแผนรอบทดสอบ (Test Cycle) ภาษาไทยที่นำไปใช้งานจริง\n\nกฎ:\n- สร้าง Test Cycle 1-3 รอบ ครอบคลุมขอบเขตที่สมเหตุสมผลจาก Release/Build/Environment/Test Suite ที่ให้มา\n- CycleName กระชับเป็นภาษาไทย ไม่ซ้ำกับ Existing Cycle Names และไม่ซ้ำกันเอง\n- CycleType ต้องอยู่ใน Allowed Cycle Types เท่านั้น\n- StartDate/EndDate เป็น ISO date (YYYY-MM-DD) โดยอิงจาก Planned Release Date ถ้ามี และ EndDate ต้องไม่ก่อน StartDate\n- Notes อธิบายขอบเขตการทดสอบของรอบนี้ สั้น ๆ เป็นภาษาไทย\n- SelectionSummary อธิบายเหตุผลการวางรอบทดสอบนี้ สั้น ๆ",
            input=new[]{new{role="user",content}},
            text=new{format=new{type="json_schema",name="test_cycles_draft",strict=true,schema=new{type="object",properties=new{
                cycles=new{type="array",minItems=1,maxItems=3,items=new{type="object",properties=new{
                    cycleName=new{type="string"},
                    cycleType=new{type="string",@enum=cycleTypes},
                    startDate=new{type="string",format="date"},
                    endDate=new{type="string",format="date"},
                    notes=new{type="string"},
                    selectionSummary=new{type="string"}
                },required=new[]{"cycleName","cycleType","startDate","endDate","notes","selectionSummary"},additionalProperties=false}}
            },required=new[]{"cycles"},additionalProperties=false}}}
        };
        var outputText=await configuration.SendStructuredAsync(payload,ct);
        GeneratedTestCycles result;
        try { result=JsonSerializer.Deserialize<GeneratedTestCycles>(AiJsonParser.Extract(outputText),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("AI ส่งข้อมูล Test Cycle กลับมาไม่ครบถ้วน"); }
        catch (JsonException) { throw new InvalidOperationException("AI ส่งข้อมูล Test Cycle กลับมาไม่ตรงตามรูปแบบที่กำหนด กรุณาลองใหม่อีกครั้ง"); }
        if(result.Cycles.Count==0)throw new InvalidOperationException("AI ไม่ได้สร้าง Test Cycle กลับมา");
        return result.Cycles;
    }
    private static string CasesText(TestSuiteDto suite)=>string.Join("\n",suite.Cases.Take(200).Select(x=>$"  - {x.TestCaseCode} | {x.Title} | Priority {x.Priority} | {(x.IsRequired?"Required":"Optional")}"));
}