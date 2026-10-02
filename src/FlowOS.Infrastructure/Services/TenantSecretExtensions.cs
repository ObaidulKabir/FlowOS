using FlowOS.Core.Common.Interfaces;
using FlowOS.Domain.Entities;

namespace FlowOS.Infrastructure.Services;

public static class TenantSecretExtensions
{
    public static void ProtectWebhookSigningSecret(this Tenant tenant, ITenantSecretProtector protector)
    {
        var current = tenant.WebhookSigningSecret;
        var protectedValue = protector.Protect(current);
        if (!string.Equals(current, protectedValue, StringComparison.Ordinal))
        {
            tenant.SetWebhookSigningSecret(protectedValue!);
        }
    }

    public static string? UnprotectedWebhookSigningSecret(this Tenant tenant, ITenantSecretProtector protector)
    {
        return protector.Unprotect(tenant.WebhookSigningSecret);
    }
}
