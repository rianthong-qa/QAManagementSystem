using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.Api.Controllers;

public sealed record CreateChatSessionRequest(Guid? ProjectId = null);
public sealed record ChatFeedbackRequest(short? Feedback, string? Note = null);

/// <summary>
/// ผู้ช่วย AI คลังความรู้ — บทสนทนาเป็นของผู้ใช้แต่ละคน (เห็นเฉพาะของตัวเอง)
/// POST sessions/{id}/messages ตอบเป็น Server-Sent Events (event: sources / delta / done / error) และส่ง ": ping" ทุก 15 วินาที
/// ให้ Cloudflare ไม่ตัดการเชื่อมต่อระหว่างรอคำตอบแรก; ?stream=false = รอคำตอบครบแล้วตอบ JSON (ใช้เมื่อ proxy ไม่รองรับ stream)
/// </summary>
[ApiController, Route("api/v1/knowledge/chat"), Authorize(Policy = "KnowledgeView")]
public sealed class KnowledgeChatController(KnowledgeChatService chat) : ControllerBase
{
    // ไม่ escape อักษรไทยเป็น \uXXXX (stream เล็กลง ~2 เท่า) — ยัง escape อักขระ HTML และขึ้นบรรทัดตามปกติ
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) };
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<ChatSessionDto>>> Sessions(CancellationToken ct) =>
        UserId() is { } userId ? Ok(await chat.ListSessionsAsync(userId, ct)) : Unauthorized();

    [HttpPost("sessions")]
    public async Task<ActionResult<ChatSessionDto>> CreateSession(CreateChatSessionRequest? request, CancellationToken ct) =>
        UserId() is { } userId ? Ok(await chat.CreateSessionAsync(userId, request?.ProjectId, ct)) : Unauthorized();

    [HttpGet("sessions/{id:guid}")]
    public async Task<ActionResult<ChatSessionDetailDto>> Session(Guid id, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        return await chat.GetSessionAsync(userId, id, ct) is { } detail ? Ok(detail) : NotFound();
    }

    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> DeleteSession(Guid id, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        return await chat.DeleteSessionAsync(userId, id, ct) ? NoContent() : NotFound();
    }

    [HttpPost("messages/{id:long}/feedback")]
    public async Task<IActionResult> Feedback(long id, ChatFeedbackRequest request, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Unauthorized();
        try { return await chat.SetFeedbackAsync(userId, id, request.Feedback, request.Note, ct) ? NoContent() : NotFound(); }
        catch (KnowledgeChatException ex) { return Problem(ex.StatusCode, ex.Message); }
    }

    [HttpPost("sessions/{id:guid}/messages"), EnableRateLimiting("knowledge-chat")]
    public async Task<IActionResult> Ask(Guid id, AskKnowledgeRequest request, [FromQuery] bool stream = true, CancellationToken ct = default)
    {
        if (UserId() is not { } userId) return Unauthorized();
        if (!stream) return await AskWithoutStreamAsync(userId, id, request, ct);

        var writeLock = new SemaphoreSlim(1, 1);
        var started = false;
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? heartbeat = null;

        async Task Write(string text)
        {
            await writeLock.WaitAsync(ct);
            try { await Response.WriteAsync(text, Encoding.UTF8, ct); await Response.Body.FlushAsync(ct); }
            finally { writeLock.Release(); }
        }

        async Task Emit(string eventName, object data)
        {
            if (!started)
            {
                started = true;
                Response.StatusCode = StatusCodes.Status200OK;
                Response.ContentType = "text/event-stream; charset=utf-8";
                Response.Headers.CacheControl = "no-cache, no-transform";
                Response.Headers["X-Accel-Buffering"] = "no";
                HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
                heartbeat = HeartbeatAsync(Write, heartbeatStop.Token);
            }
            await Write($"event: {eventName}\ndata: {JsonSerializer.Serialize(data, Json)}\n\n");
        }

        try
        {
            await chat.AskAsync(User, userId, id, request, Emit, ct);
        }
        catch (KnowledgeChatException ex) when (!started) { return Problem(ex.StatusCode, ex.Message); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* ผู้ใช้ปิดการเชื่อมต่อ */ }
        catch (Exception ex) when (started && !ct.IsCancellationRequested)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<KnowledgeChatController>>().LogError(ex, "Knowledge chat failed after streaming started");
            try { await Emit("error", new { message = "ผู้ช่วย AI ตอบไม่สำเร็จ กรุณาลองใหม่" }); } catch { /* connection ปิดแล้ว */ }
        }
        finally
        {
            await heartbeatStop.CancelAsync();
            if (heartbeat is not null) { try { await heartbeat; } catch (OperationCanceledException) { } }
        }
        return new EmptyResult();
    }

    private async Task<IActionResult> AskWithoutStreamAsync(Guid userId, Guid id, AskKnowledgeRequest request, CancellationToken ct)
    {
        var answer = new StringBuilder();
        object? sources = null, done = null;
        string? error = null;
        Task Collect(string eventName, object data)
        {
            switch (eventName)
            {
                case "sources": sources = data; break;
                case "delta": answer.Append(JsonSerializer.SerializeToElement(data, Json).GetProperty("text").GetString()); break;
                case "done": done = data; break;
                case "error": error = JsonSerializer.SerializeToElement(data, Json).GetProperty("message").GetString(); break;
            }
            return Task.CompletedTask;
        }
        try { await chat.AskAsync(User, userId, id, request, Collect, ct); }
        catch (KnowledgeChatException ex) { return Problem(ex.StatusCode, ex.Message); }
        if (error is not null) return Problem(StatusCodes.Status502BadGateway, error);
        return Ok(new { answer = answer.ToString(), sources, done });
    }

    private static async Task HeartbeatAsync(Func<string, Task> write, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        try { while (await timer.WaitForNextTickAsync(ct)) await write(": ping\n\n"); }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    private ObjectResult Problem(int status, string detail) => StatusCode(status, new ProblemDetails { Status = status, Detail = detail });
    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
}
