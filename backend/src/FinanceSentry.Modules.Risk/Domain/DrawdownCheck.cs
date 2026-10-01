namespace FinanceSentry.Modules.Risk.Domain;

/// <summary>
/// The book's measured decline from its peak set against the owner's tolerated decline (#700). Both
/// are fractions of value in [0,1]. Absent from an evaluation when the owner has recorded no drawdown
/// tolerance or the book's history is too short to measure a decline.
/// </summary>
public readonly record struct DrawdownCheck(decimal MaxDrawdown, decimal ObservedDrawdown);
