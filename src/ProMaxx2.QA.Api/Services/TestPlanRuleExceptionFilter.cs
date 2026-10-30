using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ProMaxx2.QA.Domain.TestPlanning;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// แปลง <see cref="TestPlanRuleException"/> ที่ไม่มี controller จับ (เช่น Freeze guard ใน Execution/Test Cycle/Assignment เดิม)
/// เป็น 409 ProblemDetails พร้อม <c>code</c> — endpoint เดิมจึงไม่ต้องเพิ่ม catch ทีละจุดและไม่ตอบ 500
/// </summary>
public sealed class TestPlanRuleExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not TestPlanRuleException rule) return;
        var problem = new ProblemDetails { Title = rule.Code == TestPlanErrorCodes.Frozen ? "Test Plan ถูก Freeze" : "ดำเนินการกับ Test Plan ไม่ได้", Detail = rule.Message, Status = StatusCodes.Status409Conflict };
        problem.Extensions["code"] = rule.Code;
        context.Result = new ConflictObjectResult(problem);
        context.ExceptionHandled = true;
    }
}
