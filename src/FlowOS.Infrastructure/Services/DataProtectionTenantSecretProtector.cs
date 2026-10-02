using System;
using System.Security.Cryptography;
using FlowOS.Core.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace FlowOS.Infrastructure.Services;

public class DataProtectionTenantSecretProtector : ITenantSecretProtector
{
    private readonly IDataProtector? _protector;

    public DataProtectionTenantSecretProtector(IDataProtectionProvider? dataProtectionProvider = null)
    {
        _protector = dataProtectionProvider?.CreateProtector("FlowOS.TenantSecrets");
    }

    public string? Protect(string? value)
    {
        if (_protector == null || string.IsNullOrEmpty(value))
        {
            return value;
        }

        return _protector.Protect(value);
    }

    public string? Unprotect(string? value)
    {
        if (_protector == null || string.IsNullOrEmpty(value))
        {
            return value;
        }

        try
        {
            return _protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            if (value.StartsWith("whsec_", StringComparison.Ordinal))
            {
                return value;
            }
            return value;
        }
    }
}
