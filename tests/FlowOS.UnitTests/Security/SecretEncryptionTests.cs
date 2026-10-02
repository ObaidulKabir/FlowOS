using System;
using System.Linq;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace FlowOS.UnitTests.Security;

public class SecretEncryptionTests
{
    [Fact]
    public void EphemeralDataProtector_Protect_Then_Unprotect_Roundtrips()
    {
        var provider = new EphemeralDataProtectionProvider();
        ITenantSecretProtector protector = new DataProtectionTenantSecretProtector(provider);

        var plaintext = "whsec_abc123def456";

        var cipher = protector.Protect(plaintext);

        Assert.NotNull(cipher);
        Assert.NotEqual(plaintext, cipher);

        var decrypted = protector.Unprotect(cipher);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Unprotect_LegacyPlaintext_ReturnsOriginal()
    {
        var provider = new EphemeralDataProtectionProvider();
        ITenantSecretProtector protector = new DataProtectionTenantSecretProtector(provider);

        var legacyPlaintext = "whsec_oldSecret123";

        var result = protector.Unprotect(legacyPlaintext);

        Assert.Equal(legacyPlaintext, result);
    }

    [Fact]
    public void NullOrEmptyValues_PassThrough()
    {
        var provider = new EphemeralDataProtectionProvider();
        ITenantSecretProtector protector = new DataProtectionTenantSecretProtector(provider);

        Assert.Null(protector.Protect(null));
        Assert.Equal(string.Empty, protector.Protect(string.Empty));

        Assert.Null(protector.Unprotect(null));
        Assert.Equal(string.Empty, protector.Unprotect(string.Empty));
    }

    [Fact]
    public void HashKey_ProducesConsistent64CharLowercaseHex()
    {
        var input = "test";

        var hash1 = TenantApiKey.HashKey(input);
        var hash2 = TenantApiKey.HashKey(input);

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
        Assert.All(hash1, c => Assert.True(char.IsLower(c) || char.IsDigit(c)));
        Assert.NotEqual(input, hash1);
    }
}
