using FlowOS.Api.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace FlowOS.UnitTests.Api;

/// <summary>CR-10: rules for the Idempotency-Key request header.</summary>
public class IdempotencyKeyHeaderTests
{
    private static HttpRequest Request(params string[] headerValues)
    {
        var context = new DefaultHttpContext();
        if (headerValues.Length > 0)
            context.Request.Headers[IdempotencyKeyHeader.Name] = new StringValues(headerValues);
        return context.Request;
    }

    [Fact]
    public void Header_is_used_when_the_body_has_no_key()
    {
        Assert.True(IdempotencyKeyHeader.TryResolve(Request(" key-1 "), null, out var key, out var error));
        Assert.Equal("key-1", key);
        Assert.Null(error);
    }

    [Fact]
    public void Body_key_is_used_when_there_is_no_header()
    {
        Assert.True(IdempotencyKeyHeader.TryResolve(Request(), "body-key", out var key, out _));
        Assert.Equal("body-key", key);
    }

    [Fact]
    public void Same_key_in_header_and_body_is_accepted()
    {
        Assert.True(IdempotencyKeyHeader.TryResolve(Request("same"), "same", out var key, out _));
        Assert.Equal("same", key);
    }

    [Fact]
    public void No_key_anywhere_is_allowed()
    {
        Assert.True(IdempotencyKeyHeader.TryResolve(Request(), "  ", out var key, out var error));
        Assert.Null(key);
        Assert.Null(error);
    }

    [Fact]
    public void Different_header_and_body_keys_are_rejected()
    {
        Assert.False(IdempotencyKeyHeader.TryResolve(Request("header"), "body", out var key, out var error));
        Assert.Null(key);
        Assert.Contains("differ", error);
    }

    [Fact]
    public void Two_header_values_are_rejected()
    {
        Assert.False(IdempotencyKeyHeader.TryResolve(Request("a", "b"), null, out _, out var error));
        Assert.Contains("one", error);
    }

    [Fact]
    public void Keys_over_200_characters_are_rejected()
    {
        Assert.True(IdempotencyKeyHeader.TryResolve(Request(new string('k', 200)), null, out _, out _));
        Assert.False(IdempotencyKeyHeader.TryResolve(Request(new string('k', 201)), null, out var key, out var error));
        Assert.Null(key);
        Assert.Contains("200", error);
    }
}
