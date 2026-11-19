using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Controllers;

/// <summary>
/// ค่าส่วนตัวของผู้ใช้ที่ต้องตามไปทุกเครื่อง (เช่น key "sidebar" = เมนูปักหมุด / หน้าล่าสุด / หมวดที่พับ)
/// ทุกคนอ่าน/เขียนได้เฉพาะของตัวเอง (ผูกกับ user ใน token) จึงไม่ต้องมี Policy เพิ่มจาก [Authorize]
/// </summary>
[ApiController, Route("api/v1/auth/me/preferences"), Authorize]
public sealed class UserPreferencesController(QaDbContext db) : ControllerBase
{
    /// <summary>200 = { value } ที่เคยบันทึก, 204 = ยังไม่เคยบันทึก (frontend ใช้ค่าเริ่มต้น/ค่าในเครื่อง)</summary>
    [HttpGet("{key}")]
    [ProducesResponseType<UserPreferenceView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<UserPreferenceView>> Get(string key, CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();
        string normalized;
        try { normalized = UserPreference.NormalizeKey(key); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Detail = ex.Message }); }
        var row = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.PreferenceKey == normalized, ct);
        if (row is null) return NoContent();
        using var doc = JsonDocument.Parse(row.Value);
        return Ok(new UserPreferenceView(doc.RootElement.Clone(), row.UpdatedAt));
    }

    [HttpPut("{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Save(string key, SaveUserPreferenceRequest request, CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();
        try
        {
            var normalized = UserPreference.NormalizeKey(key);
            if (request.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) throw new ArgumentException("ไม่มีค่าที่จะบันทึก");
            var value = request.Value.GetRawText();
            var now = DateTime.UtcNow;
            var row = await db.UserPreferences.FirstOrDefaultAsync(x => x.UserId == userId && x.PreferenceKey == normalized, ct);
            if (row is null) db.UserPreferences.Add(UserPreference.Create(userId, normalized, value, now));
            else row.Update(value, now);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) when (row is null)
            {
                // สองแท็บบันทึกครั้งแรกพร้อมกัน — อีกคำขอสร้างแถวไปแล้ว ให้ทับด้วยค่าล่าสุดแทน
                db.ChangeTracker.Clear();
                var existing = await db.UserPreferences.FirstAsync(x => x.UserId == userId && x.PreferenceKey == normalized, ct);
                existing.Update(value, now);
                await db.SaveChangesAsync(ct);
            }
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Detail = ex.Message }); }
    }

    private Guid? CurrentUserId()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(subject, out var id) ? id : null;
    }
}

public sealed record UserPreferenceView(JsonElement Value, DateTime UpdatedAt);
public sealed record SaveUserPreferenceRequest(JsonElement Value);
