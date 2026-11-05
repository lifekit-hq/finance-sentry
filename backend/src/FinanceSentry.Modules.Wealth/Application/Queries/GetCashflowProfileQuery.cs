namespace FinanceSentry.Modules.Wealth.Application.Queries;

using System.Text.Json.Serialization;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.Wealth.Application.Services;

/// <summary>
/// What the cashflow profile can say. <see cref="InsufficientHistory"/>: under three complete months, or no income
/// across them, so nothing is shown rather than a ratio through a zero or negative base. <see cref="NoCash"/>: the
/// history is there but the latest snapshot's cash is zero or negative, so there is no runway to state (burn,
/// income and the savings rate still are). Serialized by name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CashflowProfileStatus>))]
public enum CashflowProfileStatus
{
    Shown,
    InsufficientHistory,
    NoCash,
}

/// <summary>Typed reasons a figure, or the whole profile, is withheld (money-semantics §15).</summary>
public static class CashflowProfileReasons
{
    /// <summary>Fewer than <see cref="Projection.MinCompleteMonths"/> complete months of flow.</summary>
    public const string InsufficientMonths = "INSUFFICIENT_MONTHS";

    /// <summary>The complete months together have no income, so a savings rate has no base.</summary>
    public const string NoPositiveIncome = "NO_POSITIVE_INCOME";

    /// <summary>There is no net-worth snapshot to read cash from.</summary>
    public const string NoSnapshot = "NO_SNAPSHOT";

    /// <summary>The latest snapshot has no cash / invested split (<c>CashTotal</c> is null): cash was not measured.</summary>
    public const string NoCashSplit = "NO_CASH_SPLIT";

    /// <summary>The latest snapshot's cash is zero or negative.</summary>
    public const string NoCash = "NO_CASH";

    /// <summary>Typical monthly spending is zero, so cash lasts for no measurable number of months.</summary>
    public const string NoBurn = "NO_BURN";
}

/// <summary>
/// Burn, savings rate and runway over the trailing complete months (#454 S1). Context for the person's own
/// planning, never a trading signal. Computed on read; nothing is persisted. The handler computes in USD; the API
/// controller re-expresses the two monthly amounts in the profile base currency and sets
/// <paramref name="BaseCurrency"/>. Months, the rate and the shares are not converted.
/// <para>
/// <paramref name="IncomeMonthlyMedian"/> and <paramref name="BurnMonthlyMedian"/> are medians over the complete
/// months (income is each month's net plus its outflow). <paramref name="SavingsRate"/> is pooled over the whole
/// window, the sum of the months' net over the sum of their income, a fraction that is negative while spending
/// exceeds income; a per-month ratio is never returned. <paramref name="RunwayMonths"/> is the latest snapshot's cash
/// over the burn. <paramref name="CommittedShare"/> is the pooled share of outflow the committed-outflow policy
/// claims, a floor rather than a measure (money-semantics §5a). <paramref name="FromMonth"/> and
/// <paramref name="ToMonth"/> ("yyyy-MM") name the months the figures cover. <paramref name="Reason"/> explains a
/// withheld profile, <paramref name="RunwayReason"/> a withheld runway.
/// </para>
/// </summary>
public record CashflowProfileResponse(
    CashflowProfileStatus Status,
    int CompleteMonths,
    string? FromMonth,
    string? ToMonth,
    decimal? IncomeMonthlyMedian,
    decimal? BurnMonthlyMedian,
    decimal? SavingsRate,
    decimal? RunwayMonths,
    string? RunwayReason,
    decimal? CommittedShare,
    bool HasStaleSleeves,
    string? Reason = null,
    string BaseCurrency = "USD",
    string Disclosure = CashflowProfileText.Disclosure);

/// <summary>The sentence every cashflow-profile response and the MCP tool description carry.</summary>
public static class CashflowProfileText
{
    public const string Disclosure =
        "Context for your own planning, never a trading signal or advice. Computed from past complete months; not a forecast.";
}

public record GetCashflowProfileQuery(Guid UserId) : IQuery<CashflowProfileResponse>;

/// <summary>
/// Reads through <see cref="ProjectionInputsReader"/>, the reader the projections use, so the profile covers the
/// same complete months (the trailing six, the in-progress month dropped), the same stale-source flag and the same
/// latest snapshot as the FIRE spend and savings path.
/// </summary>
public class GetCashflowProfileQueryHandler(ProjectionInputsReader inputs)
    : IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse>
{
    private const int RateDecimals = 4;
    private const int RunwayDecimals = 1;

    private readonly ProjectionInputsReader _inputs = inputs;

    public async Task<CashflowProfileResponse> Handle(GetCashflowProfileQuery query, CancellationToken cancellationToken)
    {
        var inputs = await _inputs.ReadAsync(query.UserId, cancellationToken);
        var months = inputs.CompleteMonths.OrderBy(m => m.Month, StringComparer.Ordinal).ToList();

        if (months.Count < Projection.MinCompleteMonths)
            return Withheld(inputs, months, CashflowProfileReasons.InsufficientMonths);

        // Income is the month's net plus its outflow (net is inflow less outflow); the pooled rate sums both sides
        // before dividing, so one thin month cannot read as -500,000%.
        var incomes = months.Select(m => m.NetUsd + m.OutflowUsd).ToList();
        var pooledIncome = incomes.Sum();
        if (pooledIncome <= 0m)
            return Withheld(inputs, months, CashflowProfileReasons.NoPositiveIncome);

        var burn = Projection.Median(months.Select(m => m.OutflowUsd));
        var savingsRate = Math.Round(months.Sum(m => m.NetUsd) / pooledIncome, RateDecimals);
        var (status, runway, runwayReason) = RunwayOf(inputs, burn);

        return new CashflowProfileResponse(
            status,
            months.Count,
            months[0].Month,
            months[^1].Month,
            Projection.Median(incomes),
            burn,
            savingsRate,
            runway,
            runwayReason,
            CommittedShareOf(months),
            inputs.HasStaleSleeves);
    }

    private static CashflowProfileResponse Withheld(
        ProjectionInputs inputs, IReadOnlyList<HonestMonthlyFlow> months, string reason) => new(
        CashflowProfileStatus.InsufficientHistory,
        months.Count,
        FromMonth: null,
        ToMonth: null,
        IncomeMonthlyMedian: null,
        BurnMonthlyMedian: null,
        SavingsRate: null,
        RunwayMonths: null,
        RunwayReason: null,
        CommittedShare: null,
        inputs.HasStaleSleeves,
        reason);

    /// <summary>Latest cash over the typical month's spending; null with a reason when either side is missing.</summary>
    private static (CashflowProfileStatus Status, decimal? Runway, string? Reason) RunwayOf(
        ProjectionInputs inputs, decimal burn)
    {
        if (inputs.Snapshot is null)
            return (CashflowProfileStatus.Shown, null, CashflowProfileReasons.NoSnapshot);
        if (inputs.Snapshot.CashTotal is not { } cash)
            return (CashflowProfileStatus.Shown, null, CashflowProfileReasons.NoCashSplit);
        if (cash <= 0m)
            return (CashflowProfileStatus.NoCash, null, CashflowProfileReasons.NoCash);
        if (burn <= 0m)
            return (CashflowProfileStatus.Shown, null, CashflowProfileReasons.NoBurn);

        return (CashflowProfileStatus.Shown, Math.Round(cash / burn, RunwayDecimals), null);
    }

    /// <summary>Pooled committed outflow over pooled outflow; null when any month lacks the split or nothing was spent.</summary>
    private static decimal? CommittedShareOf(IReadOnlyList<HonestMonthlyFlow> months)
    {
        var outflow = months.Sum(m => m.OutflowUsd);
        if (outflow <= 0m || months.Any(m => m.CommittedOutflowUsd is null))
            return null;

        return Math.Round(months.Sum(m => m.CommittedOutflowUsd ?? 0m) / outflow, RateDecimals);
    }
}
