using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

// สรุปผลงานพนักงานแบบอ่านอย่างเดียวสำหรับ HR (ดู PerformanceShareLinkService) — AllowAnonymous เพราะ HR ไม่มีบัญชี QA Hub;
// สิทธิ์มาจาก code สุ่ม 10 ตัวที่ผูกกับพนักงาน 1 คน + ช่วงวันที่เดียว มีวันหมดอายุและยกเลิกได้; ไม่มี endpoint ไฟล์แนบ
// และไม่ใส่ RequireProjectAccess เพราะไม่มีผู้ใช้ให้ตรวจ (metric กรองตาม Project ของผู้สร้างลิงก์แทน)
[ApiController, Route("api/v1/shared/performance"), AllowAnonymous, EnableRateLimiting("share")]
public sealed class SharedPerformanceController(PerformanceShareLinkService shareLinks) : ControllerBase
{
    [HttpGet("{code}")]
    public async Task<ActionResult<SharedPerformanceReportDto>> Get(string code, CancellationToken ct)
    {
        // ข้อมูลส่วนบุคคล — ห้าม browser/proxy (รวม Cloudflare) เก็บ cache ไว้หลังลิงก์ถูกยกเลิก
        Response.Headers.CacheControl = "no-store";
        try
        {
            return await shareLinks.OpenAsync(code, ct) is { } report ? Ok(report) : NotFound();
        }
        catch (PerformanceShareLinkGoneException ex)
        {
            return StatusCode(StatusCodes.Status410Gone, new ProblemDetails { Title = "ลิงก์ใช้ไม่ได้แล้ว", Detail = ex.Message, Status = StatusCodes.Status410Gone });
        }
    }
}
