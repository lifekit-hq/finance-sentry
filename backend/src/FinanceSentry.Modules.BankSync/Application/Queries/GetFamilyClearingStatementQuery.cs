namespace FinanceSentry.Modules.BankSync.Application.Queries;

using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Core.Cqrs;

// ── Result types ───────────────────────────────────────────────────────────────

/// <summary>
/// Native (account-currency) received/sent/net subtotal for one counterparty statement
/// line, one currency. Mirrors <see cref="CounterpartyCurrencyFlow"/>; <see cref="Net"/> is
/// presentational, exactly like <see cref="CounterpartyStatementLine.NetUsd"/> — see
/// docs/money-semantics.md §5.1.
/// </summary>
public record CounterpartyStatementCurrencySubtotal(string Currency, decimal Received, decimal Sent, decimal Net);

/// <summary>
/// One counterparty's settlement position for the statement month: gross received, gross
/// sent, a presentational net, and the native per-currency breakdown behind the USD figures.
/// </summary>
/// <param name="NetUsd">
/// <see cref="ReceivedUsd"/> minus <see cref="SentUsd"/> — presentational only. It is never
/// fed back into <see cref="MonthlyFlow"/> or any aggregation; the flow math stays gross per
/// direction per the 2026-09-03 owner ruling (spec 044 §US2).
/// </param>
/// <param name="RentExpectedAmount">
/// The counterparty's configured expected monthly inflow (e.g. rent), or null when none is
/// configured or this counterparty isn't <see cref="FlowRoles.FamilySupport"/>. See
/// docs/money-semantics.md §5.1.
/// </param>
/// <param name="RentExpectedCurrency">ISO 4217 currency of <see cref="RentExpectedAmount"/>, or null.</param>
/// <param name="RentConfirmed">
/// True when this month's native gross received in <see cref="RentExpectedCurrency"/> (from
/// <see cref="ByCurrency"/>) is at least <see cref="RentExpectedAmount"/>. Deliberately no FX
/// conversion — an inflow in a different currency never counts. Null alongside the other rent
/// fields when no expectation applies.
/// </param>
/// <param name="RentShortfall">The positive gap when not confirmed; null otherwise.</param>
public record CounterpartyStatementLine(
    string Name,
    string FlowRole,
    decimal ReceivedUsd,
    decimal SentUsd,
    decimal NetUsd,
    IReadOnlyList<CounterpartyStatementCurrencySubtotal> ByCurrency,
    decimal? RentExpectedAmount = null,
    string? RentExpectedCurrency = null,
    bool? RentConfirmed = null,
    decimal? RentShortfall = null);

/// <summary>
/// One month's family clearing house: who sent/received what, gross, plus the month's
/// family-support total. See docs/money-semantics.md §5.1 for the reconciliation invariant
/// against <see cref="MonthlyFlow.FamilySupportOutflowUsd"/>.
/// </summary>
/// <param name="SupportTotalUsd">
/// Sum of <see cref="CounterpartyStatementLine.SentUsd"/> across <see cref="Counterparties"/>.
/// Because every line here is already <c>family_support</c> (see
/// <see cref="GetFamilyClearingStatementQueryHandler"/>), this is computed from the same
/// classification rows, over the same month, as <c>MonthlyFlow.FamilySupportOutflowUsd</c> —
/// the two can never drift.
/// </param>
/// <param name="ReceivedTotalUsd">Sum of <see cref="CounterpartyStatementLine.ReceivedUsd"/> across <see cref="Counterparties"/>.</param>
/// <param name="ExcludedRoutingLegs">
/// Count of <c>self_routing</c> counterparty-month buckets left out of <see cref="Counterparties"/>
/// for this month, so a reader sees money was deliberately excluded rather than missing.
/// </param>
public record FamilyClearingStatement(
    string Month,
    IReadOnlyList<CounterpartyStatementLine> Counterparties,
    decimal SupportTotalUsd,
    decimal ReceivedTotalUsd,
    int ExcludedRoutingLegs);

// ── Query ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Returns the family clearing statement for one calendar month: per-<c>family_support</c>-
/// counterparty gross received/sent, native per-currency subtotals, and the month's
/// support total. Defaults <paramref name="Month"/> to the last complete calendar month.
/// </summary>
public record GetFamilyClearingStatementQuery(Guid UserId, string? Month = null, int Months = 6)
    : IQuery<FamilyClearingStatement>;

// ── Handler ────────────────────────────────────────────────────────────────────

/// <summary>
/// Projects <see cref="ICounterpartyClassificationService"/> output into a
/// <see cref="FamilyClearingStatement"/>. Reads the classification service only — no second
/// classification path — so the statement can never disagree with the dashboard or the money
/// flow. Only <see cref="FlowRoles.FamilySupport"/> counterparties become lines (C2); the
/// excluded <see cref="FlowRoles.SelfRouting"/> and <see cref="FlowRoles.Investment"/>
/// counterparties are never lines, but self-routing ones are counted in
/// <see cref="FamilyClearingStatement.ExcludedRoutingLegs"/>.
/// </summary>
public class GetFamilyClearingStatementQueryHandler(
    ICounterpartyClassificationService classification, ICounterpartyRepository counterparties)
    : IQueryHandler<GetFamilyClearingStatementQuery, FamilyClearingStatement>
{
    private readonly ICounterpartyClassificationService _classification = classification;
    private readonly ICounterpartyRepository _counterparties = counterparties;

    public async Task<FamilyClearingStatement> Handle(
        GetFamilyClearingStatementQuery request, CancellationToken cancellationToken)
    {
        var result = await _classification.ClassifyForWindowAsync(
            request.UserId, request.Months, cancellationToken);

        var month = request.Month ?? LastCompleteMonth();
        var monthFlows = result.MonthlyFlows.Where(f => f.Month == month).ToList();

        // Keyed by name, family_support only: the classification service only exposes
        // counterparties by name (CounterpartyMonthlyFlow.CounterpartyName), not id, and a
        // non-family_support counterparty must never contribute rent fields even if it happens
        // to share a name with a family_support one. Prefers the caller's own row over a system
        // default (UserId == Guid.Empty) when both exist for the same name.
        var expectedInflowByName = (await _counterparties.GetForUserAsync(request.UserId, cancellationToken))
            .Where(c => c.FlowRole == FlowRoles.FamilySupport)
            .OrderByDescending(c => c.UserId == request.UserId)
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var lines = monthFlows
            .Where(f => f.FlowRole == FlowRoles.FamilySupport)
            .OrderBy(f => f.CounterpartyName, StringComparer.Ordinal)
            .Select(f =>
            {
                var byCurrency = (f.ByCurrency ?? [])
                    .Select(c => new CounterpartyStatementCurrencySubtotal(
                        c.Currency, c.Received, c.Sent, c.Received - c.Sent))
                    .OrderBy(c => c.Currency, StringComparer.Ordinal)
                    .ToList();

                decimal? rentExpectedAmount = null;
                string? rentExpectedCurrency = null;
                bool? rentConfirmed = null;
                decimal? rentShortfall = null;

                if (expectedInflowByName.TryGetValue(f.CounterpartyName, out var counterparty)
                    && counterparty.ExpectedMonthlyInflowAmount is { } expectedAmount
                    && counterparty.ExpectedMonthlyInflowCurrency is { } expectedCurrency)
                {
                    var nativeReceived = byCurrency
                        .FirstOrDefault(c => c.Currency == expectedCurrency)
                        ?.Received ?? 0m;

                    rentExpectedAmount = expectedAmount;
                    rentExpectedCurrency = expectedCurrency;
                    rentConfirmed = nativeReceived >= expectedAmount;
                    rentShortfall = rentConfirmed == true ? null : expectedAmount - nativeReceived;
                }

                return new CounterpartyStatementLine(
                    f.CounterpartyName,
                    f.FlowRole,
                    f.InflowUsd,
                    f.OutflowUsd,
                    f.InflowUsd - f.OutflowUsd,
                    byCurrency,
                    rentExpectedAmount,
                    rentExpectedCurrency,
                    rentConfirmed,
                    rentShortfall);
            })
            .ToList();

        var excludedRoutingLegs = monthFlows.Count(f => f.FlowRole == FlowRoles.SelfRouting);

        return new FamilyClearingStatement(
            month,
            lines,
            SupportTotalUsd: lines.Sum(l => l.SentUsd),
            ReceivedTotalUsd: lines.Sum(l => l.ReceivedUsd),
            excludedRoutingLegs);
    }

    // Same "floor to month start, step back one" shape as MonthWindow.StartOfMonthsAgo, but
    // this needs the CLOSED prior month's label, not a window floor.
    private static string LastCompleteMonth()
    {
        var now = DateTime.UtcNow;
        var startOfThisMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return startOfThisMonth.AddMonths(-1).ToString("yyyy-MM");
    }
}
