using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FlowOS.Api.Http;

/// <summary>
/// CR-10c: HMAC-SHA256 signature over a change-feed response, sent as
/// <c>X-FlowOS-Signature: t=&lt;unix seconds&gt;,v1=&lt;lower-case hex&gt;</c>.
/// The signed bytes are <c>"&lt;t&gt;." + body</c>, so a replayed old body can be rejected by its timestamp.
/// </summary>
public static class ChangeFeedSignature
{
    public const string HeaderName = "X-FlowOS-Signature";

    public static string Compute(byte[] key, long unixSeconds, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.ASCII.GetBytes(unixSeconds.ToString(CultureInfo.InvariantCulture) + ".");
        var message = new byte[prefix.Length + body.Length];
        prefix.CopyTo(message, 0);
        body.CopyTo(message.AsSpan(prefix.Length));
        var mac = HMACSHA256.HashData(key, message);
        return $"t={unixSeconds.ToString(CultureInfo.InvariantCulture)},v1={Convert.ToHexString(mac).ToLowerInvariant()}";
    }

    /// <summary>Checks a header value against the body, with a constant-time comparison and a maximum age.</summary>
    public static bool Verify(byte[] key, string? header, ReadOnlySpan<byte> body, DateTimeOffset now, TimeSpan maxAge)
    {
        if (string.IsNullOrEmpty(header)) return false;
        long? t = null;
        string? v1 = null;
        foreach (var part in header.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) return false;
            if (kv[0] == "t" && long.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) t = parsed;
            else if (kv[0] == "v1") v1 = kv[1];
        }
        if (t is null || v1 is null) return false;
        if (Math.Abs(now.ToUnixTimeSeconds() - t.Value) > maxAge.TotalSeconds) return false;

        var expected = Compute(key, t.Value, body);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(header));
    }
}
