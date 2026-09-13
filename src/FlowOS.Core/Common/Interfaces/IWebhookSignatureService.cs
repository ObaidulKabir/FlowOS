using System;

namespace FlowOS.Core.Common.Interfaces;

public interface IWebhookSignatureService
{
    string ComputeSignature(string secret, string payload, long timestamp);
    string FormatSignatureHeader(string secret, string payload, long timestamp);
    bool VerifySignature(string secret, string payload, string signatureHeader, TimeSpan? tolerance = null);
}
