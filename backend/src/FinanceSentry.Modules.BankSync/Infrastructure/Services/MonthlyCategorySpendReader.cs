namespace FinanceSentry.Modules.BankSync.Infrastructure.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;
using FinanceSentry.Modules.BankSync.Domain;

/// <summary>
/// <see cref="IMonthlyCategorySpendReader"/> over <see cref="IFlowBreakdownService"/>: the dashboard's own
/// Spending-bucket arithmetic (pending in, pair-matched transfers and routing out, counterparty roles applied),
/// bucketed by month and by line. Family-support and household-role outflow are lines of their own; every other
/// debit is its stored category. A debit carrying an income or transfer category that nevertheless reached the
/// Spending bucket (a counterparty saved with no role) is filed under UNCATEGORIZED, so the lines still add up to the
/// tile.
/// <para>
/// An active monthly commitment is placed on the line its latest charge landed in, found by the same key the detector
/// stored it under (<see cref="CommitmentKeyResolver"/>). That keeps one source of truth for "where does this spend
/// go": a mortgage paid card to card lands in the household line because its charges do, not because a stored
/// category says otherwise. A commitment with no charge in the user's history cannot be placed and is left out. A
/// commitment that stopped (an installment paid off, a rent that lapsed) is placed the same way, so an estimate can
/// tell its line from one that never had a commitment.
/// </para>
/// </summary>
public sealed class MonthlyCategorySpendReader(
    IFlowBreakdownService breakdown,
    IActiveCommitmentsReader commitments,
    IDetectionPhraseProvider detectionPhrases) : IMonthlyCategorySpendReader
{
    // The same span the dashboard's longest range reads (FlowBreakdownService.HistoryMonths).
    private const int HistoryMonths = 120;

    public async Task<MonthlyCategorySpend> GetAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // The whole history, not just [from, to]: whether a month is before the user's first data needs the first day
        // they have any, and a commitment is placed by charges that may be older than the window.
        var range = await breakdown.GetRangeBreakdownUnscopedAsync(userId, DateOnly.MinValue, to, HistoryMonths, ct);
        if (range.Items.Count == 0)
            return new MonthlyCategorySpend(null, [], [], []);

        var firstDataDay = range.Items.Min(i => DateOnly.FromDateTime(i.Date));
        var spending = range.Items.Where(i => i.Bucket == FlowBuckets.Spending).ToList();
        var fromMonthStart = new DateOnly(from.Year, from.Month, 1);

        var months = spending
            .Where(i => DateOnly.FromDateTime(i.Date) >= fromMonthStart)
            .GroupBy(i => (Month: i.Date.ToString("yyyy-MM"), Line: LineOf(i)))
            .Select(g => new CategoryMonthSpend(g.Key.Month, g.Key.Line, g.Sum(i => i.AmountUsd)))
            .OrderBy(r => r.Month, StringComparer.Ordinal)
            .ThenBy(r => r.Line, StringComparer.Ordinal)
            .ToList();

        return new MonthlyCategorySpend(
            firstDataDay,
            months,
            Place(await commitments.GetActiveCommitmentsAsync(userId, ct), spending),
            Place(await commitments.GetEndedCommitmentsAsync(userId, ct), spending));
    }

    private IReadOnlyList<CommitmentOnLine> Place(
        IReadOnlyList<ActiveCommitment> all, IReadOnlyList<FlowBreakdownItem> spending)
    {
        var monthly = all.Where(c => c.Cadence == SubscriptionCadences.Monthly).ToList();
        if (monthly.Count == 0)
            return [];

        // The line each commitment key's most recent charge landed in.
        var phrases = detectionPhrases.Current;
        var lineByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in spending.OrderByDescending(i => i.Date))
        {
            var key = CommitmentKeyResolver.Resolve(phrases, item.MerchantName, item.Description, item.Amount, item.Mcc);
            lineByKey.TryAdd(key, LineOf(item));
        }

        var placed = new List<CommitmentOnLine>();
        foreach (var commitment in monthly)
        {
            // A legacy hand-typed row's stored key matches no charge, so it is re-keyed by its name (as the committed
            // outflow policy does); every other row is looked up by the key it was stored under.
            var key = commitment.IsTracked
                ? commitment.Key
                : MerchantNameNormalizer.NormalizeDetectionKey(commitment.DisplayName, description: null);
            if (!lineByKey.TryGetValue(key, out var line))
                continue;

            placed.Add(new CommitmentOnLine(
                line, CurrencyConverter.ToUsd(commitment.Amount, commitment.Currency), commitment.EndDate));
        }

        return placed;
    }

    private static string LineOf(FlowBreakdownItem item)
    {
        if (item.FlowRole == FlowRoles.FamilySupport)
            return CategoryKeys.FamilySupport;
        if (item.FlowRole == FlowRoles.Household)
            return SpendLines.Household;

        var category = item.Category;
        return string.IsNullOrWhiteSpace(category)
               || category == CategoryKeys.Income
               || CategoryKeys.IsTransfer(category)
            ? CategoryKeys.Uncategorized
            : category;
    }
}
