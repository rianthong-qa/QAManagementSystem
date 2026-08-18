using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ProMaxx2.QA.Application.Requirements;
using ProMaxx2.QA.Application.TestManagement;

namespace ProMaxx2.QA.Api.Services;

public sealed record GeneratedSuiteCase(Guid TestCaseId,bool IsRequired,string Reason);
public sealed record GeneratedTestSuite(string SuiteName,string SuiteType,string Description,string RiskTier,IReadOnlyList<GeneratedSuiteCase>TestCases,string SelectionSummary);
public sealed record GeneratedTestSuites(IReadOnlyList<GeneratedTestSuite>Suites);

public sealed class TestSuiteAiService(SharedAiConfigurationService configuration)
{
    public bool IsConfigured => configuration.IsConfigured;
    public async Task<IReadOnlyList<GeneratedTestSuite>> GenerateAsync(string projectName,string moduleName,IReadOnlyList<RequirementDto> requirements,IReadOnlyList<TestCaseDto> testCases,IReadOnlyList<string> suiteTypes,IReadOnlyList<string> riskTiers,CancellationToken ct)
    {
        var runtime=await configuration.GetRuntimeAsync(ct);
        if(testCases.Count==0)throw new ArgumentException("Module ที่เลือกยังไม่มี Test Case สำหรับสร้าง Test Suite");
        if(suiteTypes.Count==0||riskTiers.Count==0)throw new ArgumentException("กรุณาตั้งค่า Test Suite Type และ Risk Tier ในการตั้งค่ากลางก่อนใช้งาน AI");

        var requirementText=requirements.Count==0?"- ไม่มี Requirement ใน Module นี้":string.Join("\n",requirements.Take(150).Select(x=>$"- {x.RequirementCode} | {x.Title} | Priority {x.Priority} | Risk {x.RiskLevel??"-"} | Status {x.Status} | In Scope {x.IsInScope} | Acceptance: {x.AcceptanceCriteria??"-"}"));
        var caseText=string.Join("\n",testCases.Take(250).Select(x=>$"- ID={x.TestCaseId} | {x.TestCaseCode} | {x.Title} | Priority {x.Priority} | Type {x.TestType??"-"} | Status {x.Status} | Steps {x.Steps.Count}"));
        var content=new List<Dictionary<string,object?>>{new(){{"type","input_text"},{"text",$"Project: {projectName}\nModule: {moduleName}\n\nAllowed Suite Types: {string.Join(", ",suiteTypes)}\nAllowed Risk Tiers: {string.Join(", ",riskTiers)}\n\nTotal Test Cases: {testCases.Count}\n\nRequirements:\n{requirementText}\n\nCandidate Test Cases:\n{caseText}"}}};
        var caseIds=testCases.Select(x=>x.TestCaseId.ToString()).ToArray();
        var instructions=$"คุณเป็น QA Lead วิเคราะห์ Requirement และ Test Case ของ Module เพื่อจัด Test Suite ภาษาไทยที่นำไปใช้งานจริง\n\nกฎ:\n- สร้าง Test Suite หลายชุด โดยแบ่งตามกลุ่มฟังก์ชันหรือตามลักษณะการทดสอบ\n- แต่ละ Suite ต้องมีชื่อกระชับเป็นภาษาไทย ไม่ซ้ำกัน\n- ห้ามสร้าง TestCaseId ใหม่ ใช้เฉพาะ ID ที่ให้มา\n- เลือก TestCase ที่สัมพันธ์กับวัตถุประสงค์ของ Suite นั้น\n- ให้กรณีเส้นทางหลัก ความเสี่ยงสูง และ P0/P1 เป็น Required ส่วนกรณีเสริมหรือความเสี่ยงต่ำเป็น Optional\n- แต่ละ TestCase ควรอยู่ใน Suite เดียวเท่านั้น ไม่ควรซ้ำกัน\n- อธิบายเหตุผลการเลือกแต่ละ TestCase สั้น ๆ\n- สร้างอย่างน้อย 1 Suite สูงสุด 5 Suites ตามความเหมาะสม";
        var payload=new
        {
            model=runtime.Model,
            store=false,
            instructions,
            input=new[]{new{role="user",content}},
            text=new{format=new{type="json_schema",name="test_suites_draft",strict=true,schema=new{type="object",properties=new{
                suites=new{type="array",minItems=1,maxItems=5,items=new{type="object",properties=new{
                    suiteName=new{type="string"},
                    suiteType=new{type="string",@enum=suiteTypes},
                    description=new{type="string"},
                    riskTier=new{type="string",@enum=riskTiers},
                    testCases=new{type="array",minItems=1,items=new{type="object",properties=new{testCaseId=new{type="string",@enum=caseIds},isRequired=new{type="boolean"},reason=new{type="string"}},required=new[]{"testCaseId","isRequired","reason"},additionalProperties=false}},
                    selectionSummary=new{type="string"}
                },required=new[]{"suiteName","suiteType","description","riskTier","testCases","selectionSummary"},additionalProperties=false}}
            },required=new[]{"suites"},additionalProperties=false}}}
        };
        var outputText=await configuration.SendStructuredAsync(payload,ct);
        var result=JsonSerializer.Deserialize<GeneratedTestSuites>(outputText??"",new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("AI ส่งข้อมูล Test Suite กลับมาไม่ครบถ้วน");
        var allowed=testCases.Select(x=>x.TestCaseId).ToHashSet();
        var validSuites=result.Suites.Select(draft=>{
            var validCases=draft.TestCases.Where(x=>allowed.Contains(x.TestCaseId)).GroupBy(x=>x.TestCaseId).Select(x=>x.First()).ToList();
            return draft with{TestCases=validCases};
        }).Where(x=>x.TestCases.Count>0).ToList();
        if(validSuites.Count==0)throw new InvalidOperationException("AI ไม่ได้เลือก Test Case ที่ใช้งานได้");
        return validSuites;
    }
}
