using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProMaxx2.QA.Api.Services;

public sealed record GenerateRequirementRequest(string Prompt,string?ProjectName,string?ModuleName,string?ReleaseName,IReadOnlyList<RequirementAiAttachment>?Attachments=null);
public sealed record RequirementAiAttachment(string FileName,string ContentType,byte[] Data);
public sealed record GeneratedRequirement(string Title,string Description,string AcceptanceCriteria,string Priority,string RiskLevel,string Source);

public sealed class RequirementAiService(SharedAiConfigurationService configuration)
{
    public bool IsConfigured => configuration.IsConfigured;
    public async Task<GeneratedRequirement>GenerateAsync(GenerateRequirementRequest request,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(request.Prompt))throw new ArgumentException("กรุณาระบุความต้องการที่ต้องการให้ AI วิเคราะห์");
        var runtime=await configuration.GetRuntimeAsync(ct);
        var model=runtime.Model;
        var context=$"Project: {request.ProjectName??"ไม่ระบุ"}\nModule: {request.ModuleName??"ไม่ระบุ"}\nRelease: {request.ReleaseName??"ไม่ระบุ"}";
        var content=new List<Dictionary<string,object?>>
        {
            new(){{"type","input_text"},{"text",$"{context}\n\nความต้องการจากผู้ใช้:\n{request.Prompt.Trim()}"}}
        };
        foreach(var file in request.Attachments??[])
        {
            var base64=Convert.ToBase64String(file.Data);
            if(file.ContentType.StartsWith("image/",StringComparison.OrdinalIgnoreCase))
                content.Add(new(){{"type","input_image"},{"image_url",$"data:{file.ContentType};base64,{base64}"},{"detail","auto"}});
            else
                content.Add(new(){{"type","input_file"},{"filename",file.FileName},{"file_data",base64}});
        }
        var payload=new
        {
            model,
            instructions="คุณเป็น Business Analyst และ QA Lead จัดทำ Requirement ภาษาไทยที่ชัดเจน ทดสอบได้ วิเคราะห์ข้อความและไฟล์แนบทั้งหมดเป็นข้อมูลอ้างอิง ไม่แต่งข้อมูลธุรกิจที่ผู้ใช้ไม่ได้ระบุ หากข้อมูลขัดแย้งให้ยึดคำอธิบายล่าสุดของผู้ใช้ Acceptance Criteria ให้เขียนเป็นรายการ Given/When/Then แยกบรรทัด Priority ต้องเป็น P0/P1/P2/P3 และ RiskLevel ต้องเป็น Critical/High/Medium/Low เท่านั้น",
            input=new[]{new{role="user",content}},
            text=new{format=new{type="json_schema",name="requirement_draft",strict=true,schema=new{type="object",properties=new{title=new{type="string"},description=new{type="string"},acceptanceCriteria=new{type="string"},priority=new{type="string",@enum=new[]{"P0","P1","P2","P3"}},riskLevel=new{type="string",@enum=new[]{"Critical","High","Medium","Low"}},source=new{type="string"}},required=new[]{"title","description","acceptanceCriteria","priority","riskLevel","source"},additionalProperties=false}}}
        };
        var text=await configuration.SendStructuredAsync(payload,ct);
        return JsonSerializer.Deserialize<GeneratedRequirement>(text??"",new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("AI ส่งข้อมูลกลับมาไม่ครบถ้วน");
    }
}
