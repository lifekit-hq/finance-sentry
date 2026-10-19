namespace FinanceSentry.Modules.Wealth.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// M5, the net-worth bridge (finance-framework §3.3), over the first and last snapshot in the window (<see cref="From"/>
/// / <see cref="To"/>), in USD until the controller applies the base currency. End − Start is split into
/// <see cref="Savings"/> (the bank accounts' honest net: income less spending, counterparty and transfer corrected),
/// <see cref="InvestmentReturn"/> (the brokerage and crypto sleeves' growth: <see cref="Market"/> + <see cref="Income"/>
/// − <see cref="Fees"/> − <see cref="Taxes"/>), <see cref="FxTranslation"/> (the base-currency effect of rate moves on
/// the non-USD bank balances), <see cref="Revaluation"/> (the change of the sources outside the three sleeves, a manual
/// value), <see cref="NewlyLinked"/> and <see cref="Unlinked"/> (value that entered or left with a source linked or
/// removed inside the window, both positive) and the <see cref="Residual"/> that none of them explains. The lines
/// always sum to End − Start exactly, the residual included: it is always shown, never hidden. A residual above 0.2%
/// of the start value sets <see cref="ResidualAboveTolerance"/> and makes the bridge DEGRADED with
/// <c>RESIDUAL_ABOVE_TOLERANCE</c>; <see cref="Quality"/> / <see cref="Reasons"/> carry that and the end rows' and
/// sleeves' own reasons. Fewer than two snapshots leave every amount null and the bridge unavailable.
/// </summary>
public record NetWorthBridgeResponse(
    DateOnly? From,
    DateOnly? To,
    decimal? StartValue,
    decimal? EndValue,
    decimal? Savings,
    decimal? InvestmentReturn,
    decimal? Market,
    decimal? Income,
    decimal? Fees,
    decimal? Taxes,
    decimal? FxTranslation,
    decimal? Revaluation,
    decimal? NewlyLinked,
    decimal? Unlinked,
    decimal? Residual,
    bool ResidualAboveTolerance,
    HeadlineQuality Quality,
    IReadOnlyList<HeadlineReason> Reasons,
    string Currency = "USD");

/// <summary>The bridge between the first and last snapshot dated <paramref name="From"/> through <paramref name="To"/> (either open).</summary>
public record GetNetWorthBridgeQuery(Guid UserId, DateOnly? From = null, DateOnly? To = null) : IQuery<NetWorthBridgeResponse>;

/// <summary>
/// Gathers each line from the part of the snapshot it moves and hands them to <see cref="NetWorthBridge.Measure"/>,
/// which forms the investment return, the residual and the tolerance flag. The sleeves come from their own growth split
/// (<see cref="GetSleeveGrowthQuery"/> over the same window): a sleeve whose flows are not known keeps its change in the
/// residual (<c>UNKNOWN_FLOW</c>), and its deposits from a bank account meet the bank's investment debits there, so a
/// transfer between two parts in scope nets to zero. A link or removal is bridged on the part it touched
/// (<see cref="ScopeLinks"/>): the stretch across it is the linked value, that stretch's own savings and rate moves
/// included. A part that held nothing at the opening and gained value with a link brings its first value in as newly
/// linked, and the mirror for a part emptied by a removal. The FX line translates each non-USD bank balance over the
/// banking closes those links leave (<see cref="TranslationAsync"/>); an account removed inside the window is no
/// longer read, so its rate move before the removal stays in the residual.
/// </summary>
public class GetNetWorthBridgeQueryHandler(
    INetWorthSnapshotRepository snapshots,
    IQueryHandler<GetSleeveGrowthQuery, SleeveGrowthResponse> sleeveGrowth,
    IHonestMonthlyFlowReader honestFlow,
    IBankingAccountsReader bankAccounts,
    IBankingTransactionReader bankTransactions,
    IHistoricalExchangeRateService rates,
    IEnumerable<IAccountScopeChangeReader> scopeChangeReaders)
    : IQueryHandler<GetNetWorthBridgeQuery, NetWorthBridgeResponse>
{
    private static readonly string[] BridgedSleeves = [WealthSourceIds.Brokerage, WealthSourceIds.Crypto];

    public async Task<NetWorthBridgeResponse> Handle(GetNetWorthBridgeQuery query, CancellationToken cancellationToken)
    {
        var rows = (await snapshots.GetByUserIdAsync(query.UserId, query.From, query.To, cancellationToken))
            .OrderBy(s => s.SnapshotDate)
            .ToList();
        if (rows.Count < 2 || rows[0].SnapshotDate >= rows[^1].SnapshotDate)
            return Unavailable(rows.FirstOrDefault()?.SnapshotDate, rows.LastOrDefault()?.SnapshotDate);

        var (first, last) = (rows[0], rows[^1]);
        var (opening, closing) = (first.SnapshotDate, last.SnapshotDate);
        var ends = SnapshotSources.OfEach(
            [first, last], await snapshots.GetSourcesAsync(query.UserId, [first.Id, last.Id], cancellationToken));
        var signals = ends[0].Sources.Signals(first).Merge(ends[1].Sources.Signals(last));

        var linkDays = await LinkDaysAsync(query.UserId, opening, closing, cancellationToken);
        var banking = Part([.. rows.Select(s => new ValuePoint(s.SnapshotDate, s.BankingTotal))], [.. linkDays[WealthSourceIds.Banking]]);
        var other = Part(
            [.. rows.Select(s => new ValuePoint(s.SnapshotDate, s.TotalNetWorth - s.BankingTotal - s.BrokerageTotal - s.CryptoTotal))],
            [.. linkDays[WealthSourceIds.Manual]]);

        // Inflow and outflow at the umbrella scope: the savings line, and each sleeve's income, fees and taxes.
        var flows = new List<ClassifiedFlow>();
        var savings = await honestFlow.GetWindowNetAsync(query.UserId, opening.AddDays(1), closing, cancellationToken);
        if (savings != 0m)
            flows.Add(new ClassifiedFlow(closing, savings > 0m ? FlowClass.ExternalInflow : FlowClass.ExternalOutflow, Math.Abs(savings)));

        var market = 0m;
        var (linkedIn, linkedOut) = (banking.LinkedInUsd + other.LinkedInUsd, banking.LinkedOutUsd + other.LinkedOutUsd);
        var scopeChanged = banking.ScopeChanged || other.ScopeChanged;
        foreach (var sleeve in BridgedSleeves)
        {
            var growth = await sleeveGrowth.Handle(new GetSleeveGrowthQuery(query.UserId, sleeve, opening, closing), cancellationToken);
            signals = signals with
            {
                UnknownFlowSources = [.. signals.UnknownFlowSources.Concat(growth.SourcesWithoutFlows)],
                UnmatchedTransferSources = [.. signals.UnmatchedTransferSources.Concat(
                    growth.Headline.Reasons.Where(r => r.Code == HeadlineReasonCode.UnmatchedTransfer && r.Source is not null).Select(r => r.Source!))],
            };
            if (growth is not { Growth: { } g, Income: { } income, Fees: { } fees, Taxes: { } taxes, NewlyLinked: { } linked })
                continue;

            market += g - income + fees + taxes;
            AddFlow(flows, closing, FlowClass.Income, income);
            AddFlow(flows, closing, FlowClass.Fee, fees);
            AddFlow(flows, closing, FlowClass.Tax, taxes);
            linkedIn += Math.Max(linked, 0m);
            linkedOut += Math.Max(-linked, 0m);

            // A sleeve empty at the opening close that a link filled opens its split on its first valued close: that
            // first value came in with the link. The mirror for a sleeve a removal left empty by the closing. With no
            // link there (a first deposit, a failed read) the jump stays in the residual.
            var column = sleeve == WealthSourceIds.Brokerage
                ? (Opening: first.BrokerageTotal, Closing: last.BrokerageTotal)
                : (Opening: first.CryptoTotal, Closing: last.CryptoTotal);
            var sleeveLinks = linkDays[sleeve];
            if (column.Opening == 0m && growth is { From: { } splitFrom, StartValue: { } startValue } && splitFrom > opening
                && sleeveLinks.Any(d => d > opening && d <= splitFrom))
                linkedIn += startValue;
            if (column.Closing == 0m && growth is { To: { } splitTo, EndValue: { } endValue } && splitTo < closing
                && sleeveLinks.Any(d => d >= splitTo && d <= closing))
                linkedOut += endValue;
            scopeChanged |= growth.Headline.Reasons.Any(r => r.Code == HeadlineReasonCode.ScopeChanged);
        }

        var fx = await TranslationAsync(query.UserId, banking.Bridge, cancellationToken);
        var measured = NetWorthBridge.Measure(
            first.TotalNetWorth, last.TotalNetWorth, flows,
            new BridgePositionLines(market, fx, other.HeldChangeUsd, linkedIn, linkedOut));
        if (measured.Value is not { } bridge)
        {
            var failed = HeadlineQualityMapper.Assess(signals, amountAvailable: false, measured.Failure);
            return Unavailable(opening, closing) with { Quality = failed.Quality, Reasons = failed.Reasons };
        }

        var assessment = HeadlineQualityMapper.Assess(
            signals with { ScopeChanged = scopeChanged, ResidualAboveTolerance = bridge.ResidualAboveTolerance },
            amountAvailable: true);
        return new NetWorthBridgeResponse(
            opening, closing, bridge.StartUsd, bridge.EndUsd, bridge.SavingsUsd, bridge.InvestmentReturnUsd,
            bridge.MarketUsd, bridge.IncomeUsd, bridge.FeesUsd, bridge.TaxesUsd, bridge.FxTranslationUsd,
            bridge.RevaluationUsd, bridge.NewlyLinkedUsd, bridge.UnlinkedUsd, bridge.ResidualUsd,
            bridge.ResidualAboveTolerance, assessment.Quality, assessment.Reasons);
    }

    private static NetWorthBridgeResponse Unavailable(DateOnly? from, DateOnly? to)
    {
        var assessment = HeadlineQualityMapper.Assess(HeadlineSignals.None, amountAvailable: false, MeasureFailure.InsufficientData);
        return new NetWorthBridgeResponse(
            from, to, null, null, null, null, null, null, null, null, null, null, null, null, null,
            false, assessment.Quality, assessment.Reasons);
    }

    private static void AddFlow(List<ClassifiedFlow> flows, DateOnly date, FlowClass flowClass, decimal amountUsd)
    {
        if (amountUsd != 0m)
            flows.Add(new ClassifiedFlow(date, flowClass, amountUsd));
    }

    private async Task<ILookup<string, DateOnly>> LinkDaysAsync(
        Guid userId, DateOnly opening, DateOnly closing, CancellationToken ct)
    {
        var since = opening.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var days = new List<(string Part, DateOnly Day)>();
        foreach (var reader in scopeChangeReaders)
        {
            foreach (var change in await reader.GetChangesSinceAsync(userId, since, ct))
            {
                var day = DateOnly.FromDateTime(change.AtUtc);
                if (day <= closing)
                    days.Add((reader.WealthSourceId ?? string.Empty, day));
            }
        }

        return days.ToLookup(d => d.Part, d => d.Day, StringComparer.Ordinal);
    }

    /// <summary>
    /// One part's series bridged across its link days: the value that entered and left with them, and the part's own
    /// change on the closes left (what revalued it). The part's change outside those closes stays in the residual.
    /// </summary>
    private static PartBridge Part(IReadOnlyList<ValuePoint> series, IReadOnlyList<DateOnly> linkDays)
    {
        var bridge = ScopeLinks.Bridge(series, [], linkDays);
        var (openingUsd, closingUsd) = (series[0].ValueUsd ?? 0m, series[^1].ValueUsd ?? 0m);
        var touched = linkDays.Any(d => d >= series[0].Date && d <= series[^1].Date);
        var (linkedIn, linkedOut, held) = (bridge.LinkedInUsd, bridge.LinkedOutUsd, 0m);

        if (bridge is { Opening.ValueUsd: { } from, Closing.ValueUsd: { } to })
        {
            if (bridge.Opening.Value.Date < bridge.Closing.Value.Date)
                held = to - from - bridge.NewlyLinkedUsd;

            // ScopeLinks starts a series at its first valued close: the value a link brought into an empty part, and
            // the value a removal took out of a part it left empty, are linked value too.
            if (touched && openingUsd == 0m)
                linkedIn += from;
            if (touched && closingUsd == 0m && bridge.Closing.Value.Date < series[^1].Date)
                linkedOut += to;
        }

        return new PartBridge(bridge, linkedIn, linkedOut, held, bridge.ScopeChanged || touched);
    }

    /// <summary>
    /// The rate effect on the non-USD bank balances over the closes the banking series keeps once its links are
    /// bridged: each stretch between two kept closes translates the balance it ended on (the balance walked back from
    /// today, a day's close holding that day's transactions, as the history backfill walks it) at the rates published
    /// for its two ends. A stretch a link bridged is left out (its rate move rides in the linked value), and so is
    /// every stretch before an account was linked. A stretch with no published rate at either end leaves its move in
    /// the residual.
    /// </summary>
    private async Task<decimal> TranslationAsync(Guid userId, LinkBridge banking, CancellationToken ct)
    {
        var closes = banking.Values;
        var stretches = closes.Zip(closes.Skip(1))
            .Where(s => !banking.Links.Any(link => link > s.First.Date && link < s.Second.Date))
            .Select(s => (From: s.First.Date, To: s.Second.Date))
            .Reverse()
            .ToList();
        var held = (await bankAccounts.GetActiveAccountSnapshotsAsync(userId, ct))
            .Where(a => a.CurrentBalance is not null && !string.Equals(a.Currency, "USD", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (stretches.Count == 0 || held.Count == 0)
            return 0m;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var settled = (await bankTransactions.GetTransactionsAsync(userId, closes[0].Date.AddDays(1), today, ct))
            .Where(t => !t.IsPending)
            .ToLookup(t => t.AccountId);

        var translation = 0m;
        foreach (var currency in held.GroupBy(a => a.Currency.ToUpperInvariant()))
        {
            var published = await rates.GetPublishedSeriesAsync(currency.Key, closes[0].Date, closes[^1].Date, ct);
            foreach (var account in currency)
            {
                var linkedOn = account.LinkedAtUtc is { } linkedAt ? DateOnly.FromDateTime(linkedAt) : DateOnly.MinValue;
                var effects = settled[account.AccountId]
                    .Select(t => (Date: DateOnly.FromDateTime(t.EffectiveDate),
                        Native: AccountBalanceMath.NativeEffect(account.AccountType, t.TransactionType, t.Amount)))
                    .OrderByDescending(e => e.Date)
                    .ToList();
                var (balance, next) = (account.CurrentBalance!.Value, 0);

                // Latest stretch first, walking the balance back as it goes.
                foreach (var (from, to) in stretches)
                {
                    if (from < linkedOn)
                        break;

                    for (; next < effects.Count && effects[next].Date > to; next++)
                        balance -= effects[next].Native;
                    var opening = balance - effects.Skip(next).TakeWhile(e => e.Date > from).Sum(e => e.Native);
                    if (PublishedRateLookup.Resolve(published, from) is not { } r0
                        || PublishedRateLookup.Resolve(published, to) is not { } r1)
                        continue;

                    var split = NetWorthBridge.Translate(opening, balance, r0.Rate, r1.Rate);
                    translation += AccountBalanceMath.SignedForNetTotal(account.AccountType, split.TranslationUsd);
                }
            }
        }

        return translation;
    }

    private sealed record PartBridge(LinkBridge Bridge, decimal LinkedInUsd, decimal LinkedOutUsd, decimal HeldChangeUsd, bool ScopeChanged);
}
