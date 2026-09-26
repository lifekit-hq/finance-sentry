namespace FinanceSentry.Modules.BrokerageSync.Domain.Interfaces;

/// <summary>
/// Abstraction over a brokerage adapter.
///
/// Each method takes the id of the calling user's <c>IBKRCredential</c> so the
/// adapter can resolve that user's stored OAuth artifacts and sign requests
/// against the broker's Web API on their behalf.
/// </summary>
public interface IBrokerAdapter
{
    string BrokerName { get; }
    Task EnsureSessionAsync(Guid credentialId, CancellationToken ct = default);
    Task<string> GetAccountIdAsync(Guid credentialId, CancellationToken ct = default);
    Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(Guid credentialId, string accountId, CancellationToken ct = default);
}

public sealed record BrokerPosition(
    string Symbol,
    string InstrumentType,
    decimal Quantity,
    decimal UsdValue,
    decimal? AverageCostUsd = null,
    long? Conid = null,
    string? Isin = null);
