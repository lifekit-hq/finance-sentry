namespace FinanceSentry.Modules.BankSync.Application.Services;

/// <summary>The bank's EUR to UAH buy rate (what Monobank pays per EUR) and when the bank published it.</summary>
public record EurUahBuyRate(decimal RateBuy, DateTimeOffset PublishedAt);

public interface IEurUahRateSource
{
    /// <summary>The current rate, or null when the bank has none and none was fetched earlier.</summary>
    Task<EurUahBuyRate?> GetAsync(CancellationToken cancellationToken = default);
}
