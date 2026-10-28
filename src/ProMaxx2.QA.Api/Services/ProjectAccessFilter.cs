using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Infrastructure.Identity;

namespace ProMaxx2.QA.Api.Services;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireProjectAccessAttribute : Attribute { }

public sealed class ProjectAccessFilter(ProjectAccessService access, ProjectAccessContext projectCtx) : IAsyncActionFilter
{
 public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
 {
  var attr = context.ActionDescriptor.EndpointMetadata.OfType<RequireProjectAccessAttribute>().Any();
  if (!attr) { await next(); return; }

  var userId = GetUserId(context.HttpContext);
  if (!userId.HasValue) { await next(); return; }

  projectCtx.CurrentUserId = userId.Value;

  var allowed = await access.GetAllowedProjectIdsAsync(userId.Value, context.HttpContext.RequestAborted);
  projectCtx.AllowedProjectIds = allowed;

  if (TryGetProjectId(context, out var projectId))
  {
   if (!allowed.Contains(projectId))
   {
    context.Result = new ForbidResult();
    return;
   }
  }
  else if (allowed.Length == 0)
  {
   context.Result = new ForbidResult();
   return;
  }

  // ProjectId ที่ส่งมาใน request body (เช่น SaveDefectRequest, SaveTestSuiteRequest) ต้องอยู่ใน Project ที่ผู้ใช้มีสิทธิ์ด้วย
  // — เดิมตรวจเฉพาะ route/query ชื่อ projectId ทำให้ผู้ใช้สร้างข้อมูลลง Project ที่ตัวเองไม่ได้เป็นสมาชิกได้
  foreach (var bodyProjectId in GetBodyProjectIds(context))
  {
   if (!allowed.Contains(bodyProjectId))
   {
    context.Result = new ForbidResult();
    return;
   }
  }

  // route value ที่ระบุชนิด record ได้ชัด (releaseId, cycleId, cycleCaseId, defectId ...) ต้องเป็นของ Project ที่มีสิทธิ์
  // ตอบ 404 แทน 403 เพื่อไม่เปิดเผยว่ามี record นี้อยู่ใน Project อื่น
  var scope = context.HttpContext.RequestServices.GetRequiredService<ProjectScopeGuard>();
  foreach (var (name, value) in context.HttpContext.Request.RouteValues)
  {
   if (name.Equals("projectId", StringComparison.OrdinalIgnoreCase) || name.Equals("id", StringComparison.OrdinalIgnoreCase)) continue;
   if (value is null || !Guid.TryParse(value.ToString(), out var routeId)) continue;
   if (!await scope.RouteValueAsync(name, routeId, context.HttpContext.RequestAborted))
   {
    context.Result = new NotFoundResult();
    return;
   }
  }

  await next();
 }

 private static readonly ConcurrentDictionary<Type, PropertyInfo?> ProjectIdProperties = new();

 private static IEnumerable<Guid> GetBodyProjectIds(ActionExecutingContext ctx)
 {
  foreach (var (name, value) in ctx.ActionArguments)
  {
   if (value is null || name.Equals("projectId", StringComparison.OrdinalIgnoreCase)) continue;
   var type = value.GetType();
   if (type.IsPrimitive || type.IsEnum || value is string or Guid or CancellationToken or System.Collections.IEnumerable) continue;
   var property = ProjectIdProperties.GetOrAdd(type, t =>
   {
    var p = t.GetProperty("ProjectId", BindingFlags.Public | BindingFlags.Instance);
    return p is not null && (p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?)) ? p : null;
   });
   if (property?.GetValue(value) is Guid id && id != Guid.Empty) yield return id;
  }
 }

 private static Guid? GetUserId(HttpContext http)
 {
  var sub = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.User.FindFirstValue("sub");
  return Guid.TryParse(sub, out var id) ? id : null;
 }

 private static bool TryGetProjectId(ActionExecutingContext ctx, out Guid projectId)
 {
  projectId = default;
  var routeValues = ctx.HttpContext.Request.RouteValues;
  if (routeValues.TryGetValue("projectId", out var rv) && rv is Guid rvg)
  {
   projectId = rvg;
   return true;
  }
  if (ctx.ActionArguments.TryGetValue("projectId", out var av) && av is Guid avg)
  {
   projectId = avg;
   return true;
  }
  return false;
 }
}
