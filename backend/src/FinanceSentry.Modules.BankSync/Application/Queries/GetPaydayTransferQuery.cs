namespace FinanceSentry.Modules.BankSync.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>The rate behind the figure and when the bank published it.</summary>
public record PaydayTransferRateDto(decimal RateBuy, DateTimeOffset PublishedAt, string Source);

/// <summary>
/// The payday-transfer breakdown. <see cref="HasCard"/> is false when no Monobank credit card is connected (nothing to
/// cover); the rate-derived fields are null while the bank's rate is unavailable.
/// </summary>
public record PaydayTransferDto(
    bool HasCard,
    decimal OwedUah,
    decimal FixedOutflowUah,
    decimal TotalUah,
    decimal RoundingStepEur,
    PaydayTransferRateDto? Rate,
    decimal? ExactEur,
    decimal? SuggestedEur,
    decimal? CushionEur,
    DateTime? BalanceSyncedAt);

/// <summary>
/// The EUR to send so the card's amount owed plus the fixed monthly outflow is covered, at the bank's EUR buy rate.
/// Reads the amount owed the Monobank sync already stores; fetches only the rate.
/// </summary>
public sealed record GetPaydayTransferQuery(Guid UserId) : IQuery<PaydayTransferDto>;

public sealed class GetPaydayTransferQueryHandler(
    IBankAccountRepository accounts,
    IPaydayTransferSettingsRepository settings,
    IEurUahRateSource rates,
    TimeProvider clock) : IQueryHandler<GetPaydayTransferQuery, PaydayTransferDto>
{
    public const string RateSourceName = "Monobank /bank/currency";

    private const string MonobankProvider = "monobank";
    private const string CreditType = "credit";
    private const string UahCurrency = "UAH";

    public async Task<PaydayTransferDto> Handle(GetPaydayTransferQuery query, CancellationToken ct)
    {
        var stored = await settings.GetAsync(query.UserId, ct);
        var outflow = stored?.FixedOutflowUah ?? PaydayTransferDefaults.FixedOutflowUah;
        var step = stored?.RoundingStepEur ?? PaydayTransferDefaults.RoundingStepEur;

        var now = clock.GetUtcNow();
        var cards = (await accounts.GetByUserIdAsync(query.UserId, ct))
            .Where(a => a.Provider == MonobankProvider && a.AccountType == CreditType && a.Currency == UahCurrency
                        && a.CountsInLiveTotals(now))
            .ToList();
        if (cards.Count == 0)
            return new PaydayTransferDto(false, 0m, outflow, outflow, step, null, null, null, null, null);

        // CurrentBalance is the amount owed (positive = debt); a card in credit owes nothing.
        var owed = cards.Sum(a => Math.Max(a.CurrentBalance ?? 0m, 0m));
        var syncedAt = cards.Max(a => a.LastTransactionSyncAt);
        var rate = await rates.GetAsync(ct);
        if (rate is null)
            return new PaydayTransferDto(true, owed, outflow, owed + outflow, step, null, null, null, null, syncedAt);

        var amounts = PaydayTransferCalculator.Calculate(owed, outflow, rate.RateBuy, step);
        return new PaydayTransferDto(
            true, owed, outflow, amounts.TotalUah, step,
            new PaydayTransferRateDto(rate.RateBuy, rate.PublishedAt, RateSourceName),
            amounts.ExactEur, amounts.SuggestedEur, amounts.CushionEur, syncedAt);
    }
}
