namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Risk.Application.Queries;

/// <summary>
/// The one advice engine for rebalancing and cash deployment. Both answers are derived from the same inputs — the
/// stored investment policy (carried by the drift read, with its version) and the stored risk rule set's minimum
/// cash buffer — in one pass, so they cannot contradict each other. It holds no policy values of its own: every
/// threshold is read from the stored policy.
/// <para>
/// Cash buffer: the in-book floor is the larger of the risk rule's <c>MinCashBufferPct</c> (a fraction of the book,
/// 0–1, as stored) and the IPS cash sleeve's minimum weight. The 3-month emergency buffer is off-book by the ruling
/// of 2026-10-29, so it is neither counted in the book's cash nor added to this floor. Deployable cash is the cash
/// above that floor; with the cash sleeve under its band the floor is above the cash held, so the engine can never
/// say "deploy cash" while it says "buy cash".
/// </para>
/// <para>
/// Degenerate buffer: a stored fraction below <see cref="MinPlausibleBufferFraction"/> of the book is reported as
/// <see cref="CashAdviceStatus.BufferNotSet"/> and never used to recommend deployment (a $9 buffer on a $13k book
/// came from a fraction read as a percentage). See docs/money-semantics.md, "Rebalance and cash-deployment advice".
/// </para>
/// </summary>
public static class PortfolioAdviceEngine
{
    /// <summary>
    /// Smallest buffer, as a fraction of the book, that counts as set. Below 1% of the book a "minimum cash buffer"
    /// is a unit slip rather than a policy, so it is reported as not set.
    /// </summary>
    public const decimal MinPlausibleBufferFraction = 0.01m;

    /// <summary>Rebalance entry point: the orders that bring the book back inside the policy bands.</summary>
    public static IReadOnlyList<RebalanceOrder> Rebalance(AllocationDriftDto drift, RiskRuleSetDto? rules)
        => Advise(drift, rules).Orders;

    /// <summary>Cash-deployment entry point: what to do with idle cash, from the same engine pass as the orders.</summary>
    public static CashAdvice CashDeployment(AllocationDriftDto drift, RiskRuleSetDto? rules)
        => Advise(drift, rules).Cash;

    public static PortfolioAdvice Advise(AllocationDriftDto drift, RiskRuleSetDto? rules)
    {
        var orders = drift.HasIps && drift.NeedsRebalance ? BuildOrders(drift) : [];
        var basis = new AdviceBasis(
            drift.PolicyVersion,
            rules?.Version,
            ResolveBufferUsd(drift, rules, out var status));

        if (status is CashAdviceStatus.BufferNotSet || basis.BufferUsd is not { } bufferUsd)
            return new PortfolioAdvice(orders, new CashAdvice(CashAdviceStatus.BufferNotSet, drift.CashUsd, 0m), basis);

        var excess = Math.Round(drift.CashUsd - bufferUsd, 2);

        // Belt and braces for the invariant: cash is never deployed while the same pass tells the owner to buy cash.
        var buysCash = orders.Any(o => o.Direction == OrderDirection.Buy && o.AssetClass == AssetClassNormalizer.Cash);
        return excess > 0 && !buysCash
            ? new PortfolioAdvice(orders, new CashAdvice(CashAdviceStatus.Deploy, drift.CashUsd, excess), basis)
            : new PortfolioAdvice(orders, new CashAdvice(CashAdviceStatus.Hold, drift.CashUsd, 0m), basis);
    }

    private static decimal? ResolveBufferUsd(AllocationDriftDto drift, RiskRuleSetDto? rules, out CashAdviceStatus status)
    {
        status = CashAdviceStatus.Hold;

        if (drift.TotalValueUsd <= 0 || rules?.MinCashBufferPct is not { } fraction || fraction < MinPlausibleBufferFraction)
        {
            status = CashAdviceStatus.BufferNotSet;
            return null;
        }

        var ruleBufferUsd = fraction * drift.TotalValueUsd;
        var cashSleeve = drift.Sleeves.FirstOrDefault(s => s.AssetClass == AssetClassNormalizer.Cash && s.MinPct > 0);
        var policyFloorUsd = cashSleeve is null ? 0m : cashSleeve.MinPct / 100m * drift.TotalValueUsd;

        return Math.Round(Math.Max(ruleBufferUsd, policyFloorUsd), 2);
    }

    private static List<RebalanceOrder> BuildOrders(AllocationDriftDto drift)
    {
        var orders = new List<RebalanceOrder>();

        foreach (var sleeve in drift.Sleeves)
        {
            if (sleeve.Status == "OverBand")
            {
                var excess = sleeve.ActualValueUsd - (sleeve.TargetPct / 100m * drift.TotalValueUsd);
                if (excess > 0)
                    orders.Add(new RebalanceOrder(sleeve.AssetClass, OrderDirection.Sell, Math.Round(excess, 2), BandsOut(sleeve, sleeve.MaxPct - sleeve.TargetPct)));
            }
            else if (sleeve.Status == "UnderBand")
            {
                var deficit = (sleeve.TargetPct / 100m * drift.TotalValueUsd) - sleeve.ActualValueUsd;
                if (deficit > 0)
                    orders.Add(new RebalanceOrder(sleeve.AssetClass, OrderDirection.Buy, Math.Round(deficit, 2), BandsOut(sleeve, sleeve.TargetPct - sleeve.MinPct)));
            }
            else if (sleeve.Status == "Unplanned" && sleeve.ActualPct >= 1m)
            {
                // Unplanned holding ≥ 1%: flag for review (no sizing target defined in IPS)
                orders.Add(new RebalanceOrder(sleeve.AssetClass, OrderDirection.Review, Math.Round(sleeve.ActualValueUsd, 2), 0));
            }
        }

        return orders;
    }

    /// <summary>
    /// How many whole band widths the sleeve sits from its target: the IPS band (the 5/25 rule's wider of the
    /// two, or the sleeve's own min/max) is already what decides "off target", so one more band width of drift is
    /// what makes a standing proposal materially different. Always at least 1 for a breaching sleeve.
    /// </summary>
    private static int BandsOut(AllocationSleeveDrift sleeve, decimal bandPct)
        => bandPct > 0 ? Math.Max(1, (int)(Math.Abs(sleeve.DriftPct) / bandPct)) : 1;
}

public enum OrderDirection { Sell, Buy, Review }

public enum CashAdviceStatus
{
    /// <summary>Cash is at or below the floor, or the policy asks to buy cash: nothing to deploy.</summary>
    Hold,

    /// <summary>Cash above the floor: that excess may be deployed into the policy sleeves.</summary>
    Deploy,

    /// <summary>No usable buffer (unset or degenerate): cash is never recommended for deployment.</summary>
    BufferNotSet,
}

public sealed record RebalanceOrder(string AssetClass, OrderDirection Direction, decimal NotionalUsd, int BandsOut);

public sealed record CashAdvice(CashAdviceStatus Status, decimal IdleCashUsd, decimal DeployableUsd);

/// <summary>What the advice was computed from: the IPS version, the risk rule set version, the buffer in USD.</summary>
public sealed record AdviceBasis(int? IpsVersion, int? RiskRuleSetVersion, decimal? BufferUsd)
{
    public string Describe()
        => $"IPS v{IpsVersion?.ToString() ?? "?"}, risk rules v{RiskRuleSetVersion?.ToString() ?? "?"}, "
           + (BufferUsd is { } b ? $"cash buffer ${b:N0}" : "cash buffer not set");
}

public sealed record PortfolioAdvice(IReadOnlyList<RebalanceOrder> Orders, CashAdvice Cash, AdviceBasis Basis);
