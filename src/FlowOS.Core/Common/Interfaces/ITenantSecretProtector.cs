namespace FlowOS.Core.Common.Interfaces;

public interface ITenantSecretProtector
{
    string? Protect(string? value);
    string? Unprotect(string? value);
}
