using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

/// <summary>
/// รูป/ฟอนต์ของบทความ HTML ที่แยกเก็บนอกตัว HTML (KnowledgeAssetService) — หน้าเว็บอัปโหลดก่อนบันทึกบทความแล้วใส่ <c>qa-asset:{id}</c> แทน data: base64
/// </summary>
[ApiController, Route("api/v1/knowledge/assets")]
public sealed class KnowledgeAssetsController(KnowledgeAssetService assets) : ControllerBase
{
    /// <summary>multipart: files (1–60) — คืนผลตามลำดับไฟล์ (assetId หรือ error รายไฟล์)</summary>
    [HttpPost, Authorize(Policy = "KnowledgeEdit"), RequestSizeLimit(KnowledgeAssetService.MaxTotalBytes + 1_000_000),
     RequestFormLimits(MultipartBodyLengthLimit = KnowledgeAssetService.MaxTotalBytes + 1_000_000, ValueCountLimit = 200)]
    public async Task<ActionResult<IReadOnlyList<KnowledgeAssetUploadResult>>> Upload(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var userId)) return Unauthorized();
        var data = new List<byte[]>();
        foreach (var file in Request.Form.Files)
        {
            if (file.Length > KnowledgeAssetService.MaxFileBytes) { data.Add([]); continue; } // ไม่อ่านไฟล์ที่เกิน — UploadAsync รายงาน error รายไฟล์
            await using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);
            data.Add(memory.ToArray());
        }
        try { return Ok(await assets.UploadAsync(data, userId, ct)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Detail = ex.Message }); }
    }

    /// <summary>
    /// AllowAnonymous: รูปถูกโหลดโดย iframe บทความที่เป็น origin ทึบ (sandbox ไม่มี allow-same-origin) และหน้าแชร์ที่ไม่ต้อง Login — ส่ง token ไม่ได้
    /// สิทธิ์มาจาก id สุ่ม 128 บิตที่รู้ได้จาก HTML ของบทความเท่านั้น; เนื้อไฟล์ไม่เปลี่ยนตาม id จึง cache ได้ถาวร
    /// CSP sandbox กัน SVG ที่เปิดตรง ๆ รันสคริปต์บนโดเมน API; ACAO * ให้ฟอนต์ (ต้องผ่าน CORS) โหลดจาก origin ทึบ (Origin: null) ได้
    /// </summary>
    [HttpGet("{id:guid}"), AllowAnonymous, EnableRateLimiting("knowledge-asset")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (await assets.OpenAsync(id, ct) is not { } asset) return NotFound();
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        Response.Headers.AccessControlAllowOrigin = "*";
        Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        return PhysicalFile(asset.Path, asset.ContentType);
    }
}
