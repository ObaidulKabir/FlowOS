using System.Collections.Generic;
using FlowOS.API.Filters;
using FlowOS.Application.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace FlowOS.UnitTests.Api;

/// <summary>CR-06 / CR-10: concurrency and idempotency conflicts are HTTP 409 with a stable code.</summary>
public class ApiExceptionFilterConflictTests
{
    [Theory]
    [InlineData(ConcurrencyConflictException.ConcurrentUpdate)]
    [InlineData(ConcurrencyConflictException.IdempotencyInProgress)]
    public void Conflict_is_409_with_code(string code)
    {
        var context = ContextFor(new ConcurrencyConflictException(code, "changed by another request"));

        new ApiExceptionFilterAttribute().OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        var details = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(code, details.Extensions["code"]);
        Assert.True(context.ExceptionHandled);
    }

    [Fact]
    public void Other_invalid_operations_stay_400()
    {
        var context = ContextFor(new System.InvalidOperationException("bad transition"));

        new ApiExceptionFilterAttribute().OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    private static ExceptionContext ContextFor(System.Exception exception) =>
        new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>())
        {
            Exception = exception
        };
}
