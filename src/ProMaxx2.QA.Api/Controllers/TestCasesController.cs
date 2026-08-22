using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.TestManagement;
using ClosedXML.Excel;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Requirements;

namespace ProMaxx2.QA.Api.Controllers;

[ApiController, Route("api/v1"), Authorize(Policy = "TestCaseView"), RequireProjectAccess]
public sealed class TestCasesController(TestCaseService service, ProjectService projects,TestCaseAiService ai,RequirementService requirements) : ControllerBase
{
    private sealed record TestCaseImportResult(int Imported,int Failed,IReadOnlyList<string>Errors);
    [HttpGet("test-cases")]
    public Task<PagedResult<TestCaseListDto>> List([FromQuery] Guid? projectId, [FromQuery] Guid? moduleId, [FromQuery] string? priority, [FromQuery] string? testType, [FromQuery] string? status, [FromQuery] bool? automation, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int size = 20, CancellationToken ct = default) => service.ListAsync(projectId, moduleId, priority, testType, status, automation, search, page, size, ct);

    [HttpGet("test-cases/{id:guid}")]
    public async Task<ActionResult<TestCaseDto>> Get(Guid id, CancellationToken ct)
    {
        try { return Ok(await service.GetAsync(id, ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpGet("test-cases/{id:guid}/revisions")]
    public Task<IReadOnlyList<TestCaseRevisionDto>> Revisions(Guid id, CancellationToken ct) => service.RevisionsAsync(id, ct);

    [HttpGet("test-cases/{id:guid}/requirements")]
    public Task<IReadOnlyList<TestCaseRequirementDto>> Requirements(Guid id, CancellationToken ct) => service.RequirementsAsync(id, ct);

    [HttpPatch("test-cases/{id:guid}/automation-target"), Authorize(Policy = "TestCaseEdit")]
    public async Task<ActionResult<TestCaseDto>> SetAutomationTarget(Guid id,SetAutomationTargetRequest request,CancellationToken ct)
    {
        try{return Ok(await service.SetAutomationTargetAsync(id,request.TargetApp,UserId(),ct));}
        catch(EntityNotFoundException){return NotFound();}
        catch(InvalidOperationException ex){return BadRequest(Problem("กำหนด Automation Target ไม่ได้",ex.Message,400));}
        catch(ArgumentException ex){return BadRequest(Problem("Automation Target ไม่ถูกต้อง",ex.Message,400));}
    }

    [HttpPost("test-cases"), Authorize(Policy = "TestCaseEdit")]
    public async Task<ActionResult<TestCaseDto>> Create(CreateTestCaseRequest request, CancellationToken ct)
    {
        try { var result = await service.CreateAsync(request, UserId(), ct); return CreatedAtAction(nameof(Get), new { id = result.TestCaseId }, result); }
        catch (DuplicateCodeException ex) { return Conflict(Problem("รหัส Test Case ซ้ำ", ex.Message, 409)); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }

    [HttpPost("test-cases/{id:guid}/clone"), Authorize(Policy = "TestCaseEdit")]
    public async Task<ActionResult<TestCaseDto>> Clone(Guid id, CancellationToken ct)
    {
        try { var result = await service.CloneAsync(id, UserId(), ct); return CreatedAtAction(nameof(Get), new { id = result.TestCaseId }, result); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("test-cases/generate-ai"),Authorize(Policy="TestCaseEdit"),RequestSizeLimit(21_000_000)]
    public async Task<ActionResult<IReadOnlyList<GeneratedTestCase>>>GenerateAi([FromForm]string prompt,[FromForm]string?projectName,[FromForm]string?moduleName,[FromForm]Guid?moduleId,[FromForm]Guid?projectId,List<IFormFile>?files,CancellationToken ct)
    {
        try{var uploads=files??[];if(uploads.Count>5)return BadRequest(Problem("ไฟล์แนบมากเกินไป","แนบไฟล์ได้ไม่เกิน 5 ไฟล์",400));if(uploads.Sum(x=>x.Length)>20_000_000)return BadRequest(Problem("ไฟล์แนบมีขนาดใหญ่เกินไป","ขนาดไฟล์รวมต้องไม่เกิน 20 MB",400));string[]allowed=[".pdf",".txt",".md",".csv",".docx",".xlsx",".png",".jpg",".jpeg",".webp"];var attachments=new List<RequirementAiAttachment>();foreach(var file in uploads){if(!allowed.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))return BadRequest(Problem("ชนิดไฟล์ไม่รองรับ",$"ไม่รองรับไฟล์ {Path.GetFileName(file.FileName)}",400));await using var memory=new MemoryStream();await file.CopyToAsync(memory,ct);attachments.Add(new(Path.GetFileName(file.FileName),file.ContentType,memory.ToArray()));}var requirementRows=moduleId.HasValue&&projectId.HasValue?(await requirements.ListAsync(new(projectId,null,moduleId,null,null,null,null,1,1000),ct)).Rows:new List<ProMaxx2.QA.Application.Requirements.RequirementDto>();return Ok(await ai.GenerateAsync(prompt,projectName,moduleName,requirementRows,attachments,ct));}catch(ArgumentException ex){return BadRequest(Problem("ข้อมูลสำหรับ AI ไม่ครบ",ex.Message,400));}catch(InvalidOperationException ex){return StatusCode(ai.IsConfigured?502:503,Problem("AI Generate Test Case ไม่พร้อมใช้งาน",ex.Message,ai.IsConfigured?502:503));}catch(OperationCanceledException){return StatusCode(504,Problem("AI ใช้เวลาประมวลผลนานเกินไป","กรุณาลองใหม่อีกครั้ง หรือลดขนาดข้อมูล input",504));}catch(Exception ex){return StatusCode(500,Problem("AI Generate Test Case ไม่สำเร็จ",ex.InnerException?.Message??ex.Message,500));}
    }

    [HttpGet("test-cases/import-template"), Authorize(Policy = "TestCaseEdit")]
    public async Task<IActionResult> DownloadImportTemplate([FromQuery] Guid projectId, CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest(Problem("ดาวน์โหลด Template ไม่ได้", "กรุณาเลือก Project ก่อนดาวน์โหลด", 400));
        var modules = (await projects.ListModulesAsync(projectId, ct)).Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.ModuleCode).ToList();
        if (modules.Count == 0) return BadRequest(Problem("ดาวน์โหลด Template ไม่ได้", "Project นี้ยังไม่มี Module ที่เปิดใช้งาน", 400));

        using var workbook = new XLWorkbook();
        var data = workbook.Worksheets.Add("Test Cases");
        string[] columns = ["Module Code", "Title", "Objective", "Preconditions", "Priority", "Type", "Automation", "Owner User ID", "Action", "Test Data", "Expected Result"];
        for (var index = 0; index < columns.Length; index++) data.Cell(1, index + 1).Value = columns[index];
        var sampleModule = modules[0];
        object[] sample = [sampleModule.ModuleCode, "ตรวจสอบการเข้าสู่ระบบด้วยข้อมูลที่ถูกต้อง", "ยืนยันว่าผู้ใช้สามารถเข้าสู่ระบบได้", "ผู้ใช้มีบัญชีที่เปิดใช้งานและอยู่ที่หน้า Login", "P1", "Functional", false, "", "เปิดหน้า Login|กรอก Username และ Password|กดปุ่มเข้าสู่ระบบ", "-|admin / รหัสผ่านที่ถูกต้อง|-", "หน้า Login แสดงครบถ้วน|ระบบยอมรับข้อมูล|เข้าสู่ Dashboard สำเร็จ"];
        for (var index = 0; index < sample.Length; index++) data.Cell(2, index + 1).Value = XLCellValue.FromObject(sample[index]);
        data.Range(1, 1, 1, columns.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#2457D6");
        data.Range(1, 1, 1, columns.Length).Style.Font.FontColor = XLColor.White;
        data.Range(1, 1, 1, columns.Length).Style.Font.Bold = true;
        data.Range(1, 1, 2, columns.Length).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        data.Range(1, 1, 2, columns.Length).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        data.SheetView.FreezeRows(1);
        data.RangeUsed()!.SetAutoFilter();
        data.Columns().AdjustToContents(10, 40);
        data.Columns(2, 11).Style.Alignment.WrapText = true;
        data.Row(2).Height = 42;

        var guide = workbook.Worksheets.Add("คำแนะนำ");
        guide.Cell("A1").Value = "คำแนะนำการกรอก Test Case Import Template";
        guide.Range("A1:C1").Merge().Style.Fill.BackgroundColor = XLColor.FromHtml("#2457D6");
        guide.Range("A1:C1").Style.Font.FontColor = XLColor.White;
        guide.Range("A1:C1").Style.Font.Bold = true;
        guide.Cell("A3").Value = "หัวข้อ"; guide.Cell("B3").Value = "วิธีกรอก"; guide.Cell("C3").Value = "ตัวอย่าง";
        string[,] instructions = {
            { "Module Code", "ต้องตรงกับ Module Code ที่เปิดใช้งานใน Project", sampleModule.ModuleCode },
            { "Priority", "ค่าที่แนะนำ: P0, P1, P2 หรือ P3", "P1" },
            { "Type", "ต้องตรงกับค่าประเภท Test Case ในการตั้งค่ากลาง", "Functional" },
            { "Automation", "กรอก true หรือ false", "false" },
            { "Owner User ID", "ไม่บังคับ; หากกรอกต้องเป็น User ID แบบ GUID", "เว้นว่างได้" },
            { "หลาย Test Steps", "ใช้เครื่องหมาย | คั่นแต่ละขั้นตอน และจำนวน Action/Expected Result ควรเท่ากัน", "เปิดหน้า|กดปุ่ม" },
            { "หนึ่งแถวต่อหนึ่ง Test Case", "ห้ามเปลี่ยนชื่อหัวคอลัมน์ และลบแถวตัวอย่างได้ก่อน Import", "-" }
        };
        for (var row = 0; row < instructions.GetLength(0); row++) for (var col = 0; col < 3; col++) guide.Cell(row + 4, col + 1).Value = instructions[row, col];
        guide.Range("A3:C3").Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EFFF");
        guide.Range("A3:C3").Style.Font.Bold = true;
        guide.RangeUsed()!.Style.Alignment.WrapText = true;
        guide.Columns().AdjustToContents(14, 55);
        guide.SheetView.FreezeRows(3);

        var moduleSheet = workbook.Worksheets.Add("Module Codes");
        moduleSheet.Cell("A1").Value = "Module Code"; moduleSheet.Cell("B1").Value = "Module Name";
        for (var index = 0; index < modules.Count; index++) { moduleSheet.Cell(index + 2, 1).Value = modules[index].ModuleCode; moduleSheet.Cell(index + 2, 2).Value = modules[index].ModuleName; }
        moduleSheet.Range("A1:B1").Style.Fill.BackgroundColor = XLColor.FromHtml("#2457D6");
        moduleSheet.Range("A1:B1").Style.Font.FontColor = XLColor.White;
        moduleSheet.Range("A1:B1").Style.Font.Bold = true;
        moduleSheet.Columns().AdjustToContents(14, 50);
        moduleSheet.SheetView.FreezeRows(1);

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "TestCase_Import_Template.xlsx");
    }

    [HttpPost("test-cases/import"), Authorize(Policy = "TestCaseEdit"), RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Import(IFormFile file,[FromForm] Guid projectId,CancellationToken ct)
    {
        if(file.Length==0||projectId==Guid.Empty)return BadRequest(Problem("นำเข้าไม่ได้","กรุณาเลือก Project และไฟล์ CSV/XLSX",400));
        var modules=await projects.ListModulesAsync(projectId,ct);
        var rows=new List<Dictionary<string,string>>();
        try
        {
            if(Path.GetExtension(file.FileName).Equals(".xlsx",StringComparison.OrdinalIgnoreCase))
            {
                await using var stream=file.OpenReadStream();using var workbook=new XLWorkbook(stream);var sheet=workbook.Worksheets.First();var firstRow=sheet.FirstRowUsed()??throw new ArgumentException("ไฟล์ไม่มีข้อมูล");var headers=firstRow.CellsUsed().ToDictionary(c=>c.Address.ColumnNumber,c=>c.GetString().Trim(),EqualityComparer<int>.Default);
                foreach(var row in sheet.RowsUsed().Skip(1))rows.Add(headers.ToDictionary(x=>x.Value,x=>row.Cell(x.Key).GetFormattedString(),StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                using var reader=new StreamReader(file.OpenReadStream());var all=await reader.ReadToEndAsync(ct);var lines=all.Replace("\r","").Split('\n',StringSplitOptions.RemoveEmptyEntries);if(lines.Length>0){var headers=ParseCsv(lines[0]);foreach(var line in lines.Skip(1)){var values=ParseCsv(line);rows.Add(headers.Select((h,i)=>(h,v:i<values.Count?values[i]:"")).ToDictionary(x=>x.h,x=>x.v,StringComparer.OrdinalIgnoreCase));}}
            }
        }
        catch(Exception ex){return BadRequest(Problem("อ่านไฟล์ไม่สำเร็จ",ex.Message,400));}
        var imported=0;var errors=new List<string>();var rowNo=1;
        foreach(var row in rows)
        {
            rowNo++;try{string Get(string key)=>row.GetValueOrDefault(key,"").Trim();var moduleCode=Get("Module Code");var module=modules.FirstOrDefault(x=>x.ModuleCode.Equals(moduleCode,StringComparison.OrdinalIgnoreCase));if(module is null)throw new ArgumentException($"ไม่พบ Module Code {moduleCode}");var actions=Get("Action").Split('|');var expected=Get("Expected Result").Split('|');var data=Get("Test Data").Split('|');var steps=actions.Select((x,i)=>new StepDto(i+1,x,i<data.Length?data[i]:null,i<expected.Length?expected[i]:"")).ToList();Guid?owner=Guid.TryParse(Get("Owner User ID"),out var ownerId)?ownerId:null;await service.CreateAsync(new(projectId,module.ModuleId,"",Get("Title"),Get("Objective"),Get("Preconditions"),string.IsNullOrWhiteSpace(Get("Priority"))?"P2":Get("Priority"),Get("Type"),Get("Automation").Equals("true",StringComparison.OrdinalIgnoreCase),owner,steps),UserId(),ct);imported++;}catch(Exception ex){errors.Add($"แถว {rowNo}: {ex.Message}");}
        }
        return Ok(new TestCaseImportResult(imported,errors.Count,errors));
    }

    [HttpPut("test-cases/{id:guid}"), Authorize(Policy = "TestCaseEdit")]
    public async Task<ActionResult<TestCaseDto>> Update(Guid id, UpdateTestCaseRequest request, CancellationToken ct)
    {
        try { return Ok(await service.UpdateAsync(id, request, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูลไม่ถูกต้อง", ex.Message, 400)); }
    }

    [HttpPost("test-cases/{id:guid}/revision"), Authorize(Policy = "TestCaseEdit")]
    public async Task<ActionResult<TestCaseDto>> Revision(Guid id, CreateTestCaseRevisionRequest request, CancellationToken ct)
    {
        try { return Ok(await service.ReviseAsync(id, request, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(Problem("ข้อมูล Revision ไม่ถูกต้อง", ex.Message, 400)); }
    }

    [HttpPost("test-cases/{id:guid}/status"), Authorize(Policy = "TestCaseEdit")]
    public async Task<ActionResult<TestCaseDto>> Status(Guid id, ChangeTestCaseStatusRequest request, CancellationToken ct)
    {
        try { return Ok(await service.StatusAsync(id, request.Status, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return BadRequest(Problem("เปลี่ยนสถานะไม่ได้", ex.Message, 400)); }
    }

    [HttpPost("test-cases/{id:guid}/automation"), Authorize(Policy = "TestCaseEdit")]
    public async Task<ActionResult<TestCaseDto>> SetAutomationCandidate(Guid id, SetAutomationCandidateRequest request, CancellationToken ct)
    {
        try { return Ok(await service.SetAutomationCandidateAsync(id, request.AutomationCandidate, UserId(), ct)); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpDelete("test-cases/{id:guid}"), Authorize(Policy = "TestCaseEdit")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await service.DeleteAsync(id, UserId(), ct); return NoContent(); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpPost("requirements/{requirementId:guid}/test-cases/{testCaseId:guid}"), Authorize(Policy = "TestCaseEdit")]
    public async Task<IActionResult> Link(Guid requirementId, Guid testCaseId, [FromQuery] string? coverageType, CancellationToken ct)
    {
        try { await service.LinkAsync(requirementId, testCaseId, coverageType, ct); return NoContent(); }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    [HttpDelete("requirements/{requirementId:guid}/test-cases/{testCaseId:guid}"), Authorize(Policy = "TestCaseEdit")]
    public async Task<IActionResult> Unlink(Guid requirementId, Guid testCaseId, CancellationToken ct) { await service.UnlinkAsync(requirementId, testCaseId, ct); return NoContent(); }

    [HttpGet("releases/{releaseId:guid}/rtm")]
    public Task<RtmListResultDto> Rtm(Guid releaseId, CancellationToken ct, [FromQuery]string? search = null, [FromQuery]string? moduleId = null, [FromQuery]string? coverage = null, [FromQuery]string? status = null, [FromQuery]int page = 1, [FromQuery]int size = 20) => service.RtmAsync(releaseId, search, moduleId, coverage, status, page, size, ct);

    [HttpGet("releases/{releaseId:guid}/coverage-summary")]
    public Task<CoverageSummary> Coverage(Guid releaseId, CancellationToken ct) => service.CoverageAsync(releaseId, ct);

    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
    private static ProblemDetails Problem(string title, string detail, int status) => new() { Title = title, Detail = detail, Status = status };
    private static List<string> ParseCsv(string line){var result=new List<string>();var current=new System.Text.StringBuilder();var quoted=false;for(var i=0;i<line.Length;i++){var c=line[i];if(c=='"'){if(quoted&&i+1<line.Length&&line[i+1]=='"'){current.Append('"');i++;}else quoted=!quoted;}else if(c==','&&!quoted){result.Add(current.ToString());current.Clear();}else current.Append(c);}result.Add(current.ToString());return result;}
}
