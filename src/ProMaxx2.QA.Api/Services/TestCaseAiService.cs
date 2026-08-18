using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ProMaxx2.QA.Application.Requirements;

namespace ProMaxx2.QA.Api.Services;

public sealed record GeneratedTestStep(int StepNo,string Action,string TestData,string ExpectedResult);
public sealed record GeneratedTestCase(string Title,string Objective,string Preconditions,string Priority,string TestType,bool AutomationCandidate,IReadOnlyList<GeneratedTestStep>Steps);
public sealed record GeneratedTestCases(IReadOnlyList<GeneratedTestCase>TestCases);

public sealed class TestCaseAiService(SharedAiConfigurationService configuration)
{
    public bool IsConfigured => configuration.IsConfigured;
    public async Task<IReadOnlyList<GeneratedTestCase>>GenerateAsync(string prompt,string?projectName,string?moduleName,IReadOnlyList<RequirementDto>requirements,IReadOnlyList<RequirementAiAttachment>attachments,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(prompt))throw new ArgumentException("กรุณาระบุสิ่งที่ต้องการทดสอบ");
        var runtime=await configuration.GetRuntimeAsync(ct);
        var requirementText=requirements.Count==0?"- ไม่มี Requirement ใน Module นี้":string.Join("\n",requirements.Take(100).Select(x=>$"- {x.RequirementCode} | {x.Title} | Priority {x.Priority} | Risk {x.RiskLevel??"-"} | Status {x.Status} | In Scope {x.IsInScope} | Acceptance: {x.AcceptanceCriteria??"-"}"));
        var content=new List<Dictionary<string,object?>>{new(){{"type","input_text"},{"text",$"Project: {projectName??"ไม่ระบุ"}\nModule: {moduleName??"ไม่ระบุ"}\n\nRequirements ของ Module:\n{requirementText}\n\nสิ่งที่ต้องการทดสอบ:\n{prompt.Trim()}"}}};
        foreach(var file in attachments){var base64=Convert.ToBase64String(file.Data);if(file.ContentType.StartsWith("image/",StringComparison.OrdinalIgnoreCase))content.Add(new(){{"type","input_image"},{"image_url",$"data:{file.ContentType};base64,{base64}"},{"detail","auto"}});else content.Add(new(){{"type","input_file"},{"filename",file.FileName},{"file_data",base64}});}
        var instructionExtra=requirements.Count>0?"\n- ให้สร้าง Test Case ที่สอดคล้องกับ Requirement ของ Module ที่ระบุ โดยครอบคลุม acceptance criteria ของแต่ละ Requirement ที่เกี่ยวข้อง":"";
        var payload=new{model=configuration["OpenAI:Model"]??"gpt-5-mini",instructions=$"คุณเป็น Senior QA Engineer สร้าง Test Case ภาษาไทยที่ชัดเจนและทำซ้ำได้ วิเคราะห์ข้อความกับไฟล์แนบทั้งหมด\n\nกฎ:\n- สร้าง Test Case หลายชุด (1-15) จากคำอธิบายที่ให้มา โดย覆盖different scenarios, edge cases, และ paths ที่หลากหลาย\n- แต่ละ Test Case ต้องมี Title กระชับ ไม่ซ้ำกัน\n- Objective และ Preconditions ต้องกระชับ\n- Steps ต้องเรียงจาก 1 และทุก Step ต้องมี Action กับ Expected Result ครบ\n- Priority ใช้ P0/P1/P2/P3 เท่านั้น\n- TestType ให้ใช้ Functional, Regression, Integration, Security, Performance หรือ Usability ตามความเหมาะสม\n- AutomationCandidate เป็น true เฉพาะกรณีที่เหมาะกับ automated test\n- ห้ามแต่งข้อมูลธุรกิจที่ไม่มีในบริบท{instructionExtra}",input=new[]{new{role="user",content}},text=new{format=new{type="json_schema",name="test_cases_draft",strict=true,schema=new{type="object",properties=new{testCases=new{type="array",minItems=1,maxItems=15,items=new{type="object",properties=new{title=new{type="string"},objective=new{type="string"},preconditions=new{type="string"},priority=new{type="string",@enum=new[]{"P0","P1","P2","P3"}},testType=new{type="string"},automationCandidate=new{type="boolean"},steps=new{type="array",minItems=1,items=new{type="object",properties=new{stepNo=new{type="integer"},action=new{type="string"},testData=new{type="string"},expectedResult=new{type="string"}},required=new[]{"stepNo","action","testData","expectedResult"},additionalProperties=false}}},required=new[]{"title","objective","preconditions","priority","testType","automationCandidate","steps"},additionalProperties=false}}},required=new[]{"testCases"},additionalProperties=false}}}};
        var text=await configuration.SendStructuredAsync(payload,ct);
        var result=JsonSerializer.Deserialize<GeneratedTestCases>(text,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("AI ส่งข้อมูล Test Case กลับมาไม่ครบถ้วน");
        if(result.TestCases.Count==0)throw new InvalidOperationException("AI ไม่ได้สร้าง Test Case กลับมา");
        return result.TestCases;
    }
}
