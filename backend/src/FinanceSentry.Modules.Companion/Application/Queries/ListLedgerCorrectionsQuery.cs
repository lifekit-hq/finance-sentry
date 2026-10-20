namespace FinanceSentry.Modules.Companion.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Companion.API.Responses;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;

/// <summary>
/// One calendar month (UTC) of the user's Ledger corrections for review, newest first, with that month's full count
/// and the count for every month that has any. <see cref="Limit"/> caps the items, never the counts.
/// </summary>
public sealed record ListLedgerCorrectionsQuery(Guid UserId, int Year, int Month, int Limit) : IQuery<LedgerCorrectionsResult>;

public sealed class ListLedgerCorrectionsQueryHandler(ILedgerCorrectionRepository corrections)
    : IQueryHandler<ListLedgerCorrectionsQuery, LedgerCorrectionsResult>
{
    public async Task<LedgerCorrectionsResult> Handle(ListLedgerCorrectionsQuery query, CancellationToken ct)
    {
        var from = new DateTimeOffset(query.Year, query.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var month = LedgerCorrectionLimits.MonthKey(from);

        var items = await corrections.ListAsync(query.UserId, from, from.AddMonths(1), query.Limit, ct);
        var byMonth = (await corrections.ListCorrectedAtAsync(query.UserId, ct))
            .GroupBy(LedgerCorrectionLimits.MonthKey)
            .Select(g => new LedgerCorrectionMonth(g.Key, g.Count()))
            .OrderByDescending(m => m.Month, StringComparer.Ordinal)
            .ToList();

        return new LedgerCorrectionsResult(
            month,
            byMonth.FirstOrDefault(m => m.Month == month)?.Count ?? 0,
            [.. items.Select(c => new LedgerCorrectionItem(
                c.Id, c.AgentSaid, c.WhatWasWrong, c.OwnerCorrection, c.CorrectedAt, c.RecordedAt))],
            byMonth);
    }
}
