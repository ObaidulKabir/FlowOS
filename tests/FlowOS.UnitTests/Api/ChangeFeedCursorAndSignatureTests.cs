using System;
using System.Text;
using FlowOS.Api.Http;
using FlowOS.Application.Common;
using Xunit;

namespace FlowOS.UnitTests.Api;

public class ChangeFeedCursorAndSignatureTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(9_000_000_000L)]
    public void Cursor_round_trips(long sequence)
    {
        var cursor = ChangeFeedCursor.Encode(sequence);

        Assert.True(ChangeFeedCursor.TryDecode(cursor, out var decoded));
        Assert.Equal(sequence, decoded);
        Assert.DoesNotContain('=', cursor); // safe in a query string without escaping
    }

    [Fact]
    public void Missing_cursor_means_from_the_start()
    {
        Assert.True(ChangeFeedCursor.TryDecode(null, out var a));
        Assert.True(ChangeFeedCursor.TryDecode("", out var b));
        Assert.Equal(0, a);
        Assert.Equal(0, b);
    }

    [Theory]
    [InlineData("!!")]
    [InlineData("NQ")]          // "5" without the version prefix
    [InlineData("djE6LTU")]     // "v1:-5"
    [InlineData("djE6YWJj")]    // "v1:abc"
    public void Malformed_cursor_is_rejected(string cursor)
    {
        Assert.False(ChangeFeedCursor.TryDecode(cursor, out _));
    }

    [Fact]
    public void Signature_matches_an_independent_HMAC_SHA256()
    {
        // Expected value computed with Python: hmac.new(b"secret", b'1700000000.{"a":1}', sha256).hexdigest()
        var header = ChangeFeedSignature.Compute(Encoding.UTF8.GetBytes("secret"), 1700000000, Encoding.UTF8.GetBytes("{\"a\":1}"));

        Assert.Equal("t=1700000000,v1=49f24e537407743fa4a0242bb63b94b9a47ee99cbbe071ccd8a22550ae411686", header);
    }

    [Fact]
    public void Verify_rejects_tampering_a_wrong_key_and_old_signatures()
    {
        var key = Encoding.UTF8.GetBytes("secret");
        var body = Encoding.UTF8.GetBytes("{\"a\":1}");
        var signedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000);
        var header = ChangeFeedSignature.Compute(key, signedAt.ToUnixTimeSeconds(), body);
        var maxAge = TimeSpan.FromMinutes(5);

        Assert.True(ChangeFeedSignature.Verify(key, header, body, signedAt, maxAge));
        Assert.False(ChangeFeedSignature.Verify(key, header, Encoding.UTF8.GetBytes("{\"a\":2}"), signedAt, maxAge));
        Assert.False(ChangeFeedSignature.Verify(Encoding.UTF8.GetBytes("other"), header, body, signedAt, maxAge));
        Assert.False(ChangeFeedSignature.Verify(key, header, body, signedAt.AddHours(1), maxAge));
        Assert.False(ChangeFeedSignature.Verify(key, null, body, signedAt, maxAge));
        Assert.False(ChangeFeedSignature.Verify(key, "garbage", body, signedAt, maxAge));
    }
}
