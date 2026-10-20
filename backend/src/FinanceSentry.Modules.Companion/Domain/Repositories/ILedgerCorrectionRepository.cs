namespace FinanceSentry.Modules.Companion.Domain.Repositories;

using FinanceSentry.Modules.Companion.Domain;

/// <summary>The Ledger correction log. Only the request path (the MCP caller) uses it, so every read runs under the Owner filter.</summary>
public interface ILedgerCorrectionRepository
{
    Task<LedgerCorrection> AddAsync(LedgerCorrection correction, CancellationToken ct = default);

    /// <summary>The user's corrections made in [<paramref name="from"/>, <paramref name="to"/>), newest first.</summary>
    Task<IReadOnlyList<LedgerCorrection>> ListAsync(
        Guid userId, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct = default);

    /// <summary>When each of the user's corrections was made, for the per-month counts.</summary>
    Task<IReadOnlyList<DateTimeOffset>> ListCorrectedAtAsync(Guid userId, CancellationToken ct = default);
}
