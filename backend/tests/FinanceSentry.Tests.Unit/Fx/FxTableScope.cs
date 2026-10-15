namespace FinanceSentry.Tests.Unit.Fx;

using FinanceSentry.Core.Utils;

/// <summary>
/// Puts the process-wide <see cref="CurrencyConverter"/> table into a named state for one test and
/// restores the same end state the other converter tests leave behind.
/// </summary>
public sealed class FxTableScope : IDisposable
{
    private FxTableScope()
    {
    }

    /// <summary>The state of a process that has not yet run its first live refresh.</summary>
    public static FxTableScope Seed()
    {
        CurrencyConverter.ResetToSeed();
        return new FxTableScope();
    }

    /// <summary>A live refresh that carried exactly <paramref name="rates"/>; the rest stays on the seed.</summary>
    public static FxTableScope Live(IReadOnlyDictionary<string, decimal> rates)
    {
        CurrencyConverter.ResetToSeed();
        CurrencyConverter.UpdateRates(rates);
        return new FxTableScope();
    }

    public void Dispose() => CurrencyConverter.UpdateRates(CurrencyConverter.FallbackRates);
}
