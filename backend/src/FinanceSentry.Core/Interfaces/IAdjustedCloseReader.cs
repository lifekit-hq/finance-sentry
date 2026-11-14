namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Core-facing read of a ticker's stored daily adjusted closes (the bars Radar already ingests; nothing is fetched
/// from a provider). Used wherever a deterministic return between two dates is needed, such as grading the
/// coverage universe against the S&amp;P 500.
/// </summary>
public interface IAdjustedCloseReader
{
    /// <summary>The ticker's adjusted closes on or after <paramref name="since"/>, oldest first; empty when it has no bars.</summary>
    Task<IReadOnlyList<AdjustedClose>> GetSinceAsync(string ticker, DateOnly since, CancellationToken ct = default);
}

/// <summary>One trading day's adjusted close.</summary>
public sealed record AdjustedClose(DateOnly Date, decimal Close);
