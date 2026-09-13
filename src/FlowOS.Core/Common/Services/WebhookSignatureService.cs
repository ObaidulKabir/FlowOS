using System;
using System.Security.Cryptography;
using System.Text;
using FlowOS.Core.Common.Interfaces;

namespace FlowOS.Core.Common.Services;

public class WebhookSignatureService : IWebhookSignatureService
{
    private static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    public string ComputeSignature(string secret, string payload, long timestamp)
    {
        if (string.IsNullOrEmpty(secret))
            throw new ArgumentNullException(nameof(secret));

        var signedData = $"{timestamp}.{payload}";
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(signedData);

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(dataBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public string FormatSignatureHeader(string secret, string payload, long timestamp)
    {
        var hash = ComputeSignature(secret, payload, timestamp);
        return $"t={timestamp},v1={hash}";
    }

    public bool VerifySignature(string secret, string payload, string signatureHeader, TimeSpan? tolerance = null)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        var allowedTolerance = tolerance ?? DefaultTolerance;

        long timestamp = 0;
        string? expectedHash = null;

        var elements = signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var element in elements)
        {
            var parts = element.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var val = parts[1].Trim();

            if (string.Equals(key, "t", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(val, out var parsedTimestamp))
                {
                    timestamp = parsedTimestamp;
                }
            }
            else if (string.Equals(key, "v1", StringComparison.OrdinalIgnoreCase))
            {
                expectedHash = val.ToLowerInvariant();
            }
        }

        if (timestamp <= 0 || string.IsNullOrWhiteSpace(expectedHash))
            return false;

        // Anti-replay / timestamp tolerance check
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(nowSeconds - timestamp) > (long)allowedTolerance.TotalSeconds)
        {
            return false;
        }

        var computedHash = ComputeSignature(secret, payload ?? string.Empty, timestamp);
        var computedBytes = Encoding.UTF8.GetBytes(computedHash);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHash);

        return CryptographicOperations.FixedTimeEquals(computedBytes, expectedBytes);
    }
}
