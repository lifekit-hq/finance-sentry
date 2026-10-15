namespace FinanceSentry.Core.Measures;

/// <summary>
/// The lines of the bridge that come from positions rather than from flows, in USD over the window.
/// <paramref name="MarketUsd"/> is price moves in each holding's own currency, converted at the opening rate;
/// <paramref name="FxTranslationUsd"/> the base-currency effect of rate moves (<see cref="NetWorthBridge.Translate"/>);
/// <paramref name="RevaluationUsd"/> the re-pricing of non-market assets (a fund unit price, a manual value);
/// <paramref name="NewlyLinkedUsd"/> and <paramref name="UnlinkedUsd"/> the value of accounts linked or
/// unlinked during the window, at their link or unlink date, both as positive amounts.
/// </summary>
public sealed record BridgePositionLines(
    decimal MarketUsd,
    decimal FxTranslationUsd,
    decimal RevaluationUsd,
    decimal NewlyLinkedUsd,
    decimal UnlinkedUsd);

/// <summary>
/// M5: End − Start split into lines that add up exactly. <paramref name="InvestmentReturnUsd"/> is market +
/// income − fees − taxes. <paramref name="ResidualUsd"/> is (End − Start) minus every other line: always
/// shown, never hidden. <paramref name="ResidualAboveTolerance"/> marks the bridge DEGRADED (reason
/// <c>RESIDUAL_ABOVE_TOLERANCE</c>) when the residual exceeds 0.2% of the start value.
/// </summary>
public sealed record NetWorthBridgeResult(
    decimal StartUsd,
    decimal EndUsd,
    decimal SavingsUsd,
    decimal MarketUsd,
    decimal IncomeUsd,
    decimal FeesUsd,
    decimal TaxesUsd,
    decimal InvestmentReturnUsd,
    decimal FxTranslationUsd,
    decimal RevaluationUsd,
    decimal NewlyLinkedUsd,
    decimal UnlinkedUsd,
    decimal ResidualUsd,
    bool ResidualAboveTolerance);

/// <summary>One holding's change in USD split into its own-currency move and its currency translation.</summary>
public readonly record struct TranslationSplit(decimal LocalMoveUsd, decimal TranslationUsd);

/// <summary>M5, the net-worth bridge (finance-framework §3.3). Pure: no I/O, no clock.</summary>
public static class NetWorthBridge
{
    /// <summary>The residual tolerance: 0.2% of the start value.</summary>
    public const decimal ResidualTolerance = 0.002m;

    /// <param name="startUsd">Net worth at the window's opening.</param>
    /// <param name="endUsd">Net worth at the window's closing.</param>
    /// <param name="windowFlows">The window's flows classified at the umbrella scope: external flows give the
    /// savings line, income, fees and taxes their own lines; internal flows are invisible.</param>
    /// <param name="positions">The lines that come from positions.</param>
    public static Measured<NetWorthBridgeResult> Measure(
        decimal startUsd, decimal endUsd, IReadOnlyList<ClassifiedFlow> windowFlows, BridgePositionLines positions)
    {
        if (MeasureWindow.HasNegativeAmount(windowFlows) || positions.NewlyLinkedUsd < 0m || positions.UnlinkedUsd < 0m)
            return Measured<NetWorthBridgeResult>.Fails(MeasureFailure.InvalidInput);

        var savings = ContributionsAndGrowth.Sum(windowFlows, FlowClass.ExternalInflow)
            - ContributionsAndGrowth.Sum(windowFlows, FlowClass.ExternalOutflow);
        var income = ContributionsAndGrowth.Sum(windowFlows, FlowClass.Income);
        var fees = ContributionsAndGrowth.Sum(windowFlows, FlowClass.Fee);
        var taxes = ContributionsAndGrowth.Sum(windowFlows, FlowClass.Tax);
        var investmentReturn = positions.MarketUsd + income - fees - taxes;
        var explained = savings + investmentReturn + positions.FxTranslationUsd + positions.RevaluationUsd
            + positions.NewlyLinkedUsd - positions.UnlinkedUsd;
        var residual = endUsd - startUsd - explained;

        return Measured<NetWorthBridgeResult>.Of(new NetWorthBridgeResult(
            startUsd, endUsd, savings, positions.MarketUsd, income, fees, taxes, investmentReturn,
            positions.FxTranslationUsd, positions.RevaluationUsd, positions.NewlyLinkedUsd, positions.UnlinkedUsd,
            residual, Math.Abs(residual) > ResidualTolerance * Math.Abs(startUsd)));
    }

    /// <summary>
    /// Splits the USD change of a holding with no flow in the window: the own-currency move at the opening rate,
    /// (N1 − N0)·R0, and the translation, N1·(R1 − R0). The two add up to N1·R1 − N0·R0.
    /// </summary>
    /// <param name="openingLocal">The holding's value in its own currency at the opening.</param>
    /// <param name="closingLocal">The holding's value in its own currency at the closing.</param>
    /// <param name="openingRateUsd">USD per unit of the holding's currency at the opening.</param>
    /// <param name="closingRateUsd">USD per unit of the holding's currency at the closing.</param>
    public static TranslationSplit Translate(
        decimal openingLocal, decimal closingLocal, decimal openingRateUsd, decimal closingRateUsd)
        => new((closingLocal - openingLocal) * openingRateUsd, closingLocal * (closingRateUsd - openingRateUsd));
}
