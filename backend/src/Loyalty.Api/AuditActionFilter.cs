using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Audit.Application;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Loyalty.Api;

/// <summary>
/// Unified audit trail (B5): records one append-only entry for every successful mutating action made by
/// an authenticated admin/operator (who, what, when). API-key and member-token requests are not admin
/// actions, so they are skipped. Auditing never breaks the operation — failures are swallowed + logged.
/// </summary>
public sealed class AuditActionFilter(AdminContext admin, AuditService audit, ILogger<AuditActionFilter> logger) : IAsyncActionFilter
{
    private static readonly HashSet<string> Mutating = new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next(); // run the action first

        if (!admin.IsEstablished) return;
        var req = context.HttpContext.Request;
        if (!Mutating.Contains(req.Method) || executed.Exception is { }) return;

        var status = (executed.Result as IStatusCodeActionResult)?.StatusCode ?? StatusCodes.Status200OK;
        if (status is < 200 or >= 300) return; // only record successful mutations

        var targetId = context.RouteData.Values.TryGetValue("id", out var v) ? v?.ToString() : null;
        try
        {
            await audit.RecordAsync(admin.ApplicationId, admin.UserId, admin.Username, admin.Role.ToString(),
                req.Method, req.Path.Value ?? "", targetId, status, context.HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write audit entry for {Method} {Path}", req.Method, req.Path.Value);
        }
    }
}
