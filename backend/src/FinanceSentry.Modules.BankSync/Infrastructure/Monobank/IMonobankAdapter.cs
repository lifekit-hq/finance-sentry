namespace FinanceSentry.Modules.BankSync.Infrastructure.Monobank;

public interface IMonobankAdapter
{
    Task<IReadOnlyList<MonobankAccountInfo>> ConnectAsync(string token, CancellationToken ct = default);

    Task<MonobankClientInfo> GetClientInfoAsync(string token, CancellationToken ct = default);

    Task<IReadOnlyList<MonobankTransaction>> GetStatementsAsync(
        string token, string accountId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches one statement window and maps it to categorized transaction candidates
    /// (MCC resolved to a category). Window must be ≤ 31 days per Monobank's API limit.
    /// </summary>
    Task<IReadOnlyList<Application.Services.TransactionCandidate>> GetCandidatesAsync(
        string token, string externalAccountId, Guid accountId, Guid userId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    Task SetWebhookAsync(string token, string url, CancellationToken ct = default);
}
