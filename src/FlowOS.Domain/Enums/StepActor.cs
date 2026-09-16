namespace FlowOS.Domain.Enums;

public static class StepActor
{
    public const string Human = "Human";
    public const string Agent = "Agent";
    public const string Either = "Either";

    public static bool IsKnown(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value, Human, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Agent, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Either, StringComparison.OrdinalIgnoreCase);

    public static bool IsAgentHandled(string? value) =>
        string.Equals(value, Agent, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Either, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Human : value.Trim();
}
