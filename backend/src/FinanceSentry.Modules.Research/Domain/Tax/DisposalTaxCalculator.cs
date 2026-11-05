namespace FinanceSentry.Modules.Research.Domain.Tax;

using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Positions;

/// <summary>One open lot's share of a hypothetical full sale, in euro.</summary>
public sealed record DisposalTaxLot(
    DateOnly AcquiredOn,
    decimal Quantity,
    decimal CostEur,
    decimal ProceedsEur,
    decimal GainEur,
    DateOnly? DeemedDisposalOn);

/// <summary>
/// "If you sold this position today": the Irish regime that would apply and a rough tax, always context.
/// <see cref="Status"/> is <c>shown</c> or <c>withheld</c>; a withheld context carries <see cref="WithheldReasons"/>
/// (<see cref="DisposalTaxCalculator"/> codes) and no figures. All amounts are euro: Irish tax is computed in euro,
/// each cost converted on its purchase date and the proceeds today (see money-semantics).
/// <see cref="EstimatedTaxEur"/> is before the CGT annual exemption; <see cref="TaxAfterExemptionEur"/> assumes this
/// sale is the year's only CGT disposal (the exemption is shared by all of them).
/// </summary>
public sealed record DisposalTaxContext(
    string Symbol,
    string Status,
    IReadOnlyList<string> WithheldReasons,
    string? Regime,
    int? TaxYear,
    bool RatesCarriedForward,
    string Currency,
    decimal? Rate,
    decimal? AnnualExemptionEur,
    decimal? ProceedsEur,
    decimal? CostEur,
    decimal? GainEur,
    decimal? EstimatedTaxEur,
    decimal? TaxAfterExemptionEur,
    int? DeemedDisposalYears,
    DateOnly? NextDeemedDisposalOn,
    IReadOnlyList<DisposalTaxLot> Lots,
    string Method,
    string RatesSource,
    string Disclaimer);

/// <summary>What the calculator needs to know about one position.</summary>
public sealed record DisposalTaxInput(
    string Symbol,
    string Provider,
    decimal HeldQuantity,
    decimal CurrentValueUsd,
    string ReturnsCoverage,
    string? BasisState,
    DisposalRegime? Regime,
    IReadOnlyList<LedgerFill> Fills);

/// <summary>
/// Pure first-in-first-out lot walk over a position's ingested fills, priced in euro, with the Irish disposal
/// tax for a sale of the whole position today (feature #454 S6). It withholds instead of guessing: any gate that
/// fails returns a context with reason codes and no figures.
/// </summary>
public static class DisposalTaxCalculator
{
    public const string Shown = "shown";
    public const string Withheld = "withheld";

    // Reason codes (stable; the frontend maps them to words).
    public const string ProviderNotSupported = "PROVIDER_NOT_SUPPORTED";
    public const string PartialCoverage = "PARTIAL_COVERAGE";
    public const string Unclassified = "UNCLASSIFIED";
    public const string BasisNotVerified = "BASIS_NOT_VERIFIED";
    public const string FourWeekRule = "FOUR_WEEK_RULE";
    public const string DeemedDisposalPassed = "DEEMED_DISPOSAL_PASSED";

    public const string Eur = "EUR";

    public const string Method =
        "Whole position sold today at its current value; cost by first-in-first-out lots from the ingested fills, " +
        "each converted to euro on its purchase date and the proceeds at today's rate. Selling costs and any tax " +
        "already paid are not modelled.";

    public const string Disclaimer =
        "Context for your own planning, not tax advice or a tax return. Confirm with Revenue or a tax adviser.";

    private const string BrokerProvider = "ibkr";
    private const string VerifiedBasis = "Verified";
    private const decimal QuantityTolerance = 0.0001m;
    private const int FourWeekDays = 28;
    private const int MoneyDecimals = 2;

    private sealed class OpenLot(DateOnly acquiredOn, decimal quantity, decimal costEur)
    {
        public DateOnly AcquiredOn { get; } = acquiredOn;
        public decimal Quantity { get; set; } = quantity;
        public decimal CostEur { get; set; } = costEur;
    }

    /// <param name="usdPerUnitOn">USD per unit of a currency on a past date (historical FX).</param>
    /// <param name="usdPerUnitNow">USD per unit of a currency today.</param>
    public static DisposalTaxContext Compute(
        DisposalTaxInput input,
        DateOnly today,
        Func<string, DateOnly, decimal> usdPerUnitOn,
        Func<string, decimal> usdPerUnitNow)
    {
        if (!input.Provider.Equals(BrokerProvider, StringComparison.OrdinalIgnoreCase))
        {
            return WithheldContext(input.Symbol, [ProviderNotSupported]);
        }

        var reasons = new List<string>();
        if (input.ReturnsCoverage != PositionReturnCalculator.Complete)
        {
            reasons.Add(PartialCoverage);
        }

        if (input.Regime is null)
        {
            reasons.Add(Unclassified);
        }

        if (!string.Equals(input.BasisState, VerifiedBasis, StringComparison.Ordinal))
        {
            reasons.Add(BasisNotVerified);
        }

        if (reasons.Count > 0 || input.Regime is not DisposalRegime regime)
        {
            return WithheldContext(input.Symbol, reasons);
        }

        var lots = WalkLots(input.Fills, usdPerUnitOn);
        var openQuantity = lots.Sum(l => l.Quantity);
        if (lots.Count == 0 || Math.Abs(openQuantity - input.HeldQuantity) > QuantityTolerance)
        {
            return WithheldContext(input.Symbol, [PartialCoverage]);
        }

        // Ordinary shares: the Irish 4-week matching rule replaces first-in-first-out around a buy and a sell
        // within 28 days of each other. That matching is not modelled, so such a position is withheld.
        if (regime == DisposalRegime.Cgt && HasBuyNearSell(input.Fills))
        {
            return WithheldContext(input.Symbol, [FourWeekRule]);
        }

        var rates = IrishTaxReference.ForYear(today.Year);
        var deemedYears = rates.Row.DeemedDisposalYears;

        // A lot held through its 8th anniversary has had a deemed disposal whose value on the day is not ingested.
        if (regime == DisposalRegime.ExitTax && lots.Any(l => l.AcquiredOn.AddYears(deemedYears) <= today))
        {
            return WithheldContext(input.Symbol, [DeemedDisposalPassed]);
        }

        var proceedsTotal = input.CurrentValueUsd / usdPerUnitNow(Eur);
        var results = lots
            .Select(l =>
            {
                var proceeds = proceedsTotal * l.Quantity / openQuantity;
                return new DisposalTaxLot(
                    l.AcquiredOn,
                    l.Quantity,
                    Round(l.CostEur),
                    Round(proceeds),
                    Round(proceeds - l.CostEur),
                    regime == DisposalRegime.ExitTax ? l.AcquiredOn.AddYears(deemedYears) : null);
            })
            .ToList();

        var cost = results.Sum(l => l.CostEur);
        var proceedsSum = results.Sum(l => l.ProceedsEur);
        var gain = proceedsSum - cost;

        decimal tax;
        decimal? afterExemption = null;
        decimal rate;
        decimal? exemption = null;
        if (regime == DisposalRegime.Cgt)
        {
            rate = rates.Row.CgtRate;
            exemption = rates.Row.CgtAnnualExemptionEur;
            tax = Math.Max(gain, 0m) * rate;
            afterExemption = Math.Max(gain - exemption.Value, 0m) * rate;
        }
        else
        {
            // Exit tax is taken lot by lot on its gain; a lot's loss is not set against another lot's gain.
            rate = rates.Row.ExitTaxRate;
            tax = results.Sum(l => Math.Max(l.GainEur, 0m)) * rate;
        }

        return new DisposalTaxContext(
            Symbol: input.Symbol,
            Status: Shown,
            WithheldReasons: [],
            Regime: regime == DisposalRegime.Cgt ? "cgt" : "exitTax",
            TaxYear: rates.Row.Year,
            RatesCarriedForward: rates.CarriedForward,
            Currency: Eur,
            Rate: rate,
            AnnualExemptionEur: exemption,
            ProceedsEur: Round(proceedsSum),
            CostEur: Round(cost),
            GainEur: Round(gain),
            EstimatedTaxEur: Round(tax),
            TaxAfterExemptionEur: afterExemption is decimal a ? Round(a) : null,
            DeemedDisposalYears: regime == DisposalRegime.ExitTax ? deemedYears : null,
            NextDeemedDisposalOn: regime == DisposalRegime.ExitTax
                ? results.Select(l => l.DeemedDisposalOn).OfType<DateOnly>().Where(d => d > today).Min()
                : null,
            Lots: results,
            Method: Method,
            RatesSource: rates.Row.Source,
            Disclaimer: Disclaimer);
    }

    private static List<OpenLot> WalkLots(IReadOnlyList<LedgerFill> fills, Func<string, DateOnly, decimal> usdPerUnitOn)
    {
        var open = new List<OpenLot>();
        foreach (var fill in fills.OrderBy(f => f.TradedAt).ThenByDescending(f => f.Quantity))
        {
            var day = DateOnly.FromDateTime(fill.TradedAt);
            if (fill.Quantity > 0m)
            {
                var commissionCurrency = fill.CommissionCurrency ?? fill.Currency;
                // Commission is negative when paid: subtracting it adds the acquisition cost.
                var commissionUsd = fill.Commission is decimal c ? c * usdPerUnitOn(commissionCurrency, day) : 0m;
                var costUsd = fill.Quantity * fill.Price * usdPerUnitOn(fill.Currency, day) - commissionUsd;
                open.Add(new OpenLot(day, fill.Quantity, costUsd / usdPerUnitOn(Eur, day)));
                continue;
            }

            // A sell (or a gift) consumes the oldest lots first; whatever it cannot match is simply ignored here
            // and surfaces as a held-quantity mismatch.
            var toSell = -fill.Quantity;
            foreach (var lot in open.Where(l => l.Quantity > QuantityTolerance))
            {
                if (toSell <= QuantityTolerance)
                {
                    break;
                }

                var taken = Math.Min(toSell, lot.Quantity);
                var share = taken / lot.Quantity;
                lot.CostEur -= lot.CostEur * share;
                lot.Quantity -= taken;
                toSell -= taken;
            }
        }

        return open.Where(l => l.Quantity > QuantityTolerance).ToList();
    }

    private static bool HasBuyNearSell(IReadOnlyList<LedgerFill> fills)
    {
        var buys = fills.Where(f => f.Quantity > 0m).Select(f => DateOnly.FromDateTime(f.TradedAt)).ToList();
        return fills
            .Where(f => f.Quantity < 0m)
            .Select(f => DateOnly.FromDateTime(f.TradedAt))
            .Any(sell => buys.Any(buy => Math.Abs(sell.DayNumber - buy.DayNumber) <= FourWeekDays));
    }

    private static DisposalTaxContext WithheldContext(string symbol, IReadOnlyList<string> reasons) => new(
        symbol, Withheld, reasons, null, null, false, Eur, null, null, null, null, null, null, null, null, null,
        [], Method, string.Empty, Disclaimer);

    private static decimal Round(decimal value) => Math.Round(value, MoneyDecimals);
}
