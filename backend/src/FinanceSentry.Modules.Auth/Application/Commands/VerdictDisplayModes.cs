namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>How a person reads a research verdict: the word with its reasons, or the numbered rule trail behind it.</summary>
public static class VerdictDisplayModes
{
    public const string Simple = "simple";
    public const string Detailed = "detailed";

    public static bool IsKnown(string? value) => value is Simple or Detailed;
}
