using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FlowOS.API.Filters;

public sealed class RequireRuntimePlanAttribute : TypeFilterAttribute
{
    public RequireRuntimePlanAttribute() : base(typeof(RequireRuntimePlanFilter))
    {
    }
}

public sealed class RequireRuntimePlanFilter : IAsyncActionFilter
{
    private readonly ITenantEntitlementService _entitlement;
    private readonly ICurrentUser _currentUser;

    public RequireRuntimePlanFilter(ITenantEntitlementService entitlement, ICurrentUser currentUser)
    {
        _entitlement = entitlement;
        _currentUser = currentUser;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var decision = await _entitlement.EnsureRuntimeAllowedAsync(_currentUser.TenantId, context.HttpContext.RequestAborted);
        if (!decision.Allowed)
        {
            context.Result = new ObjectResult(new
            {
                code = decision.Code,
                message = decision.Message
            })
            {
                StatusCode = StatusCodes.Status402PaymentRequired
            };
            return;
        }

        await next();
    }
}
