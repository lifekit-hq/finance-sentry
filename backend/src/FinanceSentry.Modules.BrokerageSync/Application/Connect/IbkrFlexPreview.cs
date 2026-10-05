namespace FinanceSentry.Modules.BrokerageSync.Application.Connect;

/// <summary>
/// What a Flex query returned when it was run with a not-yet-saved token + query id: enough
/// for the user to recognise their own account and confirm the query carries the sections
/// finance-sentry reads, before anything is stored.
/// </summary>
public sealed record IbkrFlexPreview(
    string AccountId,
    DateOnly? FromDate,
    DateOnly? ToDate,
    DateTime? GeneratedAtUtc,
    int OpenPositionsCount,
    IReadOnlyList<string> CashCurrencies,
    int TradesCount,
    int CashTransactionsCount);
