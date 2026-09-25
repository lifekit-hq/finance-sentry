namespace FinanceSentry.Infrastructure.Fx;

/// <summary>
/// Outcome of a dated EUR conversion. <see cref="AmountEur"/> is null exactly when
/// <see cref="RateMissing"/> is true — a missing rate is surfaced, never replaced by today's
/// rate or by zero.
/// </summary>
public sealed record EurConversion(decimal? AmountEur, bool RateMissing)
{
    public static EurConversion Of(decimal amountEur) => new(amountEur, false);

    public static EurConversion Missing { get; } = new(null, true);
}

/// <summary>
/// The date-aware EUR reporting boundary: values an amount in EUR at the rate for the date of
/// the transaction (tax schedules), not at today's rate.
/// </summary>
public interface IEurReportingConverter
{
    Task<EurConversion> ToEurAsync(
        decimal amount, string currency, DateOnly onDate, CancellationToken ct = default);
}

public sealed class EurReportingConverter(IHistoricalExchangeRateService rates) : IEurReportingConverter
{
    private const string Eur = "EUR";
    private const string Usd = "USD";

    public async Task<EurConversion> ToEurAsync(
        decimal amount, string currency, DateOnly onDate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return EurConversion.Missing;

        var code = currency.Trim().ToUpperInvariant();
        if (code == Eur)
            return EurConversion.Of(amount);

        var usdPerUnit = await UsdPerUnitAsync(code, onDate, ct);
        var usdPerEur = await UsdPerUnitAsync(Eur, onDate, ct);
        if (usdPerUnit is not { } from || usdPerEur is not { } to || to <= 0m)
            return EurConversion.Missing;

        return EurConversion.Of(amount * from / to);
    }

    private async Task<decimal?> UsdPerUnitAsync(string currency, DateOnly date, CancellationToken ct) =>
        currency == Usd ? 1m : await rates.GetPublishedRateAsync(currency, date, ct);
}
