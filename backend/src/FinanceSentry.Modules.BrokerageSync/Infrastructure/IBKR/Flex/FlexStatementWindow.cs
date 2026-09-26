namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;

/// <summary>
/// An explicit date range override for a Flex Web Service pull (<c>fd</c>/<c>td</c> on
/// <c>SendRequest</c>), used when the caller needs a specific window rather than whatever
/// range the saved Activity Flex Query is configured with in Client Portal. IBKR caps any
/// single pull at 365 days, so a multi-year backfill walks one window per year.
/// </summary>
public sealed record FlexStatementWindow(DateOnly FromDate, DateOnly ToDate)
{
    private const string WireFormat = "yyyyMMdd";

    public string FromDateWire => FromDate.ToString(WireFormat);
    public string ToDateWire => ToDate.ToString(WireFormat);
}
