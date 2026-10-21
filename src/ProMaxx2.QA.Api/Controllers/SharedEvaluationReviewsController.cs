using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProMaxx2.QA.Application.Performance;

namespace ProMaxx2.QA.Api.Controllers;

// แบบประเมินสำหรับหัวหน้าแผนกอื่น (ดู EvaluationService.CreateExternalReviewAsync) — AllowAnonymous เพราะผู้ประเมินไม่มีบัญชี QA Hub;
// สิทธิ์มาจาก code สุ่ม 12 ตัวที่ผูกกับผู้ประเมิน 1 คนของการประเมิน 1 รอบ มีวันหมดอายุ ยกเลิกได้ และส่งได้ครั้งเดียว
// ข้อมูลที่เปิดให้เห็นมีเพียงชื่อ/ตำแหน่งพนักงาน ช่วงประเมิน และเกณฑ์ — ไม่มีคะแนนตนเอง/หัวหน้าตรงหรือผลงานอื่น
[ApiController, Route("api/v1/shared/evaluation-reviews"), AllowAnonymous, EnableRateLimiting("share")]
public sealed class SharedEvaluationReviewsController(EvaluationService evaluations) : ControllerBase
{
    [HttpGet("{code}")]
    public Task<ActionResult<ExternalReviewFormDto>> Get(string code, CancellationToken ct)
        => Run(() => evaluations.OpenExternalReviewAsync(code, ct));

    [HttpPost("{code}")]
    public Task<ActionResult<ExternalReviewFormDto>> Submit(string code, SubmitExternalReviewRequest request, CancellationToken ct)
        => Run(() => evaluations.SubmitExternalReviewAsync(code, request, ct));

    private async Task<ActionResult<ExternalReviewFormDto>> Run(Func<Task<ExternalReviewFormDto?>> action)
    {
        // ข้อมูลบุคคล — ห้าม browser/proxy (รวม Cloudflare) เก็บ cache
        Response.Headers.CacheControl = "no-store";
        try
        {
            return await action() is { } form ? Ok(form) : NotFound();
        }
        catch (ExternalReviewGoneException ex)
        {
            return StatusCode(StatusCodes.Status410Gone, new ProblemDetails { Title = "ลิงก์ใช้ไม่ได้แล้ว", Detail = ex.Message, Status = StatusCodes.Status410Gone });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "ข้อมูลไม่ถูกต้อง", Detail = ex.Message, Status = StatusCodes.Status400BadRequest });
        }
        catch (InvalidOperationException ex) when (ex.Source?.StartsWith("ProMaxx2.", StringComparison.Ordinal) == true)
        {
            return Conflict(new ProblemDetails { Title = "ดำเนินการไม่ได้", Detail = ex.Message, Status = StatusCodes.Status409Conflict });
        }
    }
}
