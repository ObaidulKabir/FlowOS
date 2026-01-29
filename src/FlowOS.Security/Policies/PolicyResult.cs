namespace FlowOS.Security.Policies;

public record PolicyResult(bool IsAllowed, string Reason)
{
    public static PolicyResult Allowed() => new(true, string.Empty);
    public static PolicyResult Denied(string reason) => new(false, reason);
}
