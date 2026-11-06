namespace FinanceSentry.Modules.Wealth.Application.Queries;

using System.Text.Json.Serialization;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// How the declared monthly income compares with the observed one (the cashflow profile's median month). Serialized by
/// name. <see cref="NotComparable"/>: a declared gross figure, or only irregular sources, cannot be held against income
/// that landed after tax. <see cref="DeclaredUnreadable"/>: this host holds no encryption key, so the amounts are
/// unreadable here.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IncomeVariance>))]
public enum IncomeVariance
{
    Matches,
    ObservedLower,
    ObservedHigher,
    NoObserved,
    NoDeclared,
    NotComparable,
    DeclaredUnreadable,
}

/// <summary>
/// One declared source. <paramref name="Label"/>, <paramref name="Amount"/> and <paramref name="MonthlyEquivalentUsd"/>
/// are null when the host cannot read the encrypted fields, and <paramref name="MonthlyEquivalentUsd"/> also for an
/// irregular source.
/// </summary>
public record IncomeProfileSource(
    Guid Id,
    IncomeKind Kind,
    string? Label,
    decimal? Amount,
    string Currency,
    IncomeCadence Cadence,
    IncomeBasis GrossOrNet,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool Active,
    decimal? MonthlyEquivalentUsd);

/// <summary>
/// What the person declares they earn next to what landed (#454 S4). Declared income is context: it never replaces the
/// observed figure in any projection. All amounts in <paramref name="Sources"/> are in their own currency except the
/// USD fields.
/// </summary>
public record IncomeProfileResponse(
    bool DeclaredReadable,
    IReadOnlyList<IncomeProfileSource> Sources,
    int ActiveSources,
    decimal? DeclaredMonthlyUsd,
    string? DeclaredBasis,
    decimal? ObservedMonthlyUsd,
    string? ObservedReason,
    IncomeVariance Variance,
    string Currency = "USD",
    string Disclosure = IncomeProfileText.Disclosure);

public static class IncomeProfileText
{
    public const string Disclosure =
        "Context for your own planning, never a trading signal or advice. Declared income is what the person says they receive; "
        + "observed income is what landed in the complete months. Neither replaces the other.";
}

public record GetIncomeProfileQuery(Guid UserId) : IQuery<IncomeProfileResponse>;

/// <summary>
/// Composes the declared sources with the observed income from the cashflow profile. On a host that holds no encryption
/// key (the MCP host) it reads only the plain columns and returns the sources without a label, an amount or a monthly
/// equivalent, with variance <see cref="IncomeVariance.DeclaredUnreadable"/> - the smallest read that needs no key.
/// </summary>
public class GetIncomeProfileQueryHandler(
    IIncomeSourceRepository sources,
    IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse> cashflow,
    TimeProvider clock)
    : IQueryHandler<GetIncomeProfileQuery, IncomeProfileResponse>
{
    /// <summary>Observed within this share of declared reads as a match; pay rises, bonuses and FX move a month by more than rounding.</summary>
    public const decimal MatchTolerance = 0.10m;

    private const string NetBasis = "Net";
    private const string GrossBasis = "Gross";
    private const string MixedBasis = "Mixed";
    private const int MoneyDecimals = 2;

    public async Task<IncomeProfileResponse> Handle(GetIncomeProfileQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var observed = await cashflow.Handle(new GetCashflowProfileQuery(query.UserId), cancellationToken);
        var observedMonthly = observed.IncomeMonthlyMedian;

        if (!sources.CanReadEncryptedFields)
        {
            var outlines = await sources.ListOutlinesAsync(query.UserId, cancellationToken);
            var unreadable = outlines
                .Select(o => new IncomeProfileSource(
                    o.Id, o.Kind, null, null, o.Currency, o.Cadence, o.GrossOrNet, o.StartDate, o.EndDate,
                    o.IsActiveOn(today), null))
                .ToList();
            var active = unreadable.Count(s => s.Active);
            return new IncomeProfileResponse(
                false, unreadable, active, null, null, observedMonthly, observed.Reason,
                active == 0 ? IncomeVariance.NoDeclared : IncomeVariance.DeclaredUnreadable);
        }

        var declared = (await sources.ListAsync(query.UserId, cancellationToken))
            .Select(s => new IncomeProfileSource(
                s.Id, s.Kind, s.Label, s.Amount, s.Currency, s.Cadence, s.GrossOrNet, s.StartDate, s.EndDate,
                s.IsActiveOn(today),
                s.MonthlyEquivalent is { } monthly ? Math.Round(CurrencyConverter.ToUsd(monthly, s.Currency), MoneyDecimals) : null))
            .ToList();

        var activeSources = declared.Where(s => s.Active).ToList();
        var priced = activeSources.Where(s => s.MonthlyEquivalentUsd is not null).ToList();
        decimal? declaredMonthly = priced.Count == 0 ? null : priced.Sum(s => s.MonthlyEquivalentUsd!.Value);
        var basis = BasisOf(priced);

        return new IncomeProfileResponse(
            true, declared, activeSources.Count, declaredMonthly, basis, observedMonthly, observed.Reason,
            VarianceOf(activeSources.Count, declaredMonthly, basis, observedMonthly));
    }

    private static string? BasisOf(IReadOnlyList<IncomeProfileSource> priced)
    {
        if (priced.Count == 0)
            return null;
        if (priced.All(s => s.GrossOrNet == IncomeBasis.Net))
            return NetBasis;
        return priced.All(s => s.GrossOrNet == IncomeBasis.Gross) ? GrossBasis : MixedBasis;
    }

    private static IncomeVariance VarianceOf(int activeCount, decimal? declared, string? basis, decimal? observed)
    {
        if (activeCount == 0)
            return IncomeVariance.NoDeclared;
        if (observed is not { } seen)
            return IncomeVariance.NoObserved;
        if (declared is not { } expected || basis != NetBasis || expected <= 0m)
            return IncomeVariance.NotComparable;

        if (Math.Abs(seen - expected) <= expected * MatchTolerance)
            return IncomeVariance.Matches;

        return seen < expected ? IncomeVariance.ObservedLower : IncomeVariance.ObservedHigher;
    }
}
