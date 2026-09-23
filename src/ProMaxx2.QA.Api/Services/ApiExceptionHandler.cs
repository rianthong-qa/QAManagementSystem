using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.Api.Services;

/// <summary>แปลง exception ที่ controller ไม่ได้จับเองให้เป็น ProblemDetails รูปแบบเดียวกันทั้งระบบ —
/// EntityNotFound → 404, DuplicateCode → 409, ArgumentException → 400 และที่เหลือ → 500 พร้อม log
/// โดยไม่ส่งข้อความภายใน (stack/inner exception) กลับไปให้ client</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested) return true;

        var (status, title, detail) = exception switch
        {
            EntityNotFoundException ex => (StatusCodes.Status404NotFound, "ไม่พบข้อมูล", ex.Message),
            DuplicateCodeException ex => (StatusCodes.Status409Conflict, "ข้อมูลซ้ำ", ex.Message),
            ArgumentException ex => (StatusCodes.Status400BadRequest, "ข้อมูลไม่ถูกต้อง", ex.Message),
            _ => (StatusCodes.Status500InternalServerError, "เกิดข้อผิดพลาดภายในระบบ", $"กรุณาลองใหม่ หรือแจ้งผู้ดูแลพร้อมรหัส {http.TraceIdentifier}"),
        };
        if (status >= 500) logger.LogError(exception, "Unhandled exception for {Method} {Path} (trace {TraceId})", http.Request.Method, http.Request.Path, http.TraceIdentifier);
        else logger.LogInformation("Mapped {ExceptionType} to {Status} for {Path}: {Message}", exception.GetType().Name, status, http.Request.Path, exception.Message);

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail, Extensions = { ["traceId"] = http.TraceIdentifier } },
        });
    }
}
