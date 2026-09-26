namespace FinanceSentry.Modules.Research.Domain.ThesisMonitor;

/// <summary>
/// Closed vocabulary of comparison directions a <see cref="ThesisInvalidationTrigger"/> may use.
/// </summary>
public static class ThesisTriggerDirection
{
    public const string LessThan = "lessThan";
    public const string GreaterThan = "greaterThan";

    public static bool IsKnown(string? direction) => direction is LessThan or GreaterThan;
}
