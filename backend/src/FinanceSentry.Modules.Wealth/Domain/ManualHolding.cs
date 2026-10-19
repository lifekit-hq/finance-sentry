namespace FinanceSentry.Modules.Wealth.Domain;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;

/// <summary>
/// A value the person enters by hand for wealth no provider reads (a Steam wallet, framework §5.5): what they would
/// receive for it after fees, in its own currency, as of a date. It counts in total net worth through
/// <c>ManualWealthSource</c>; liquid net worth counts it only when its <see cref="Convertibility"/> is
/// <see cref="Core.Interfaces.Convertibility.Cash"/>.
/// </summary>
public sealed class ManualHolding : IHasUpdatedAt, IUserOwned
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateOnly AsOf { get; private set; }
    public Convertibility Convertibility { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>A typed-in figure is an estimate by definition (L3), so it is not stored.</summary>
    public Reliability Reliability => Reliability.L3Estimated;

    private ManualHolding() { }

    public static ManualHolding Create(
        Guid userId, string label, decimal amount, string currency, DateOnly asOf, Convertibility convertibility)
    {
        var holding = new ManualHolding
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        holding.Update(label, amount, currency, asOf, convertibility);
        return holding;
    }

    public void Update(string label, decimal amount, string currency, DateOnly asOf, Convertibility convertibility)
    {
        Label = label.Trim();
        Amount = amount;
        Currency = currency.ToUpperInvariant();
        AsOf = asOf;
        Convertibility = convertibility;
    }
}

/// <summary>The bounds a manual holding is validated against and stored in.</summary>
public static class ManualHoldingLimits
{
    public const int LabelMaxLength = 100;

    /// <summary>Below the 18,2 money column's ceiling.</summary>
    public const decimal MaxAmount = 1_000_000_000_000m;
}
