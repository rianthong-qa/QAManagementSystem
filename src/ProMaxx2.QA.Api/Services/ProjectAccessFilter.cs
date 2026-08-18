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
  if (allowed.Length == 0)
  {
   await access.AutoAssignAllProjectsAsync(userId.Value, context.HttpContext.RequestAborted);
   allowed = await access.GetAllowedProjectIdsAsync(userId.Value, context.HttpContext.RequestAborted);
  }
  projectCtx.AllowedProjectIds = allowed;

  if (TryGetProjectId(context, out var projectId))
  {
   if (!allowed.Contains(projectId))
   {
    context.Result = new ForbidResult();
    return;
   }
  }

  await next();
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
