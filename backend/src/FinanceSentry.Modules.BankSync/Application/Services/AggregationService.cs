namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// Aggregates bank account balance and type data for a user across all currencies.
/// </summary>
public interface IAggregationService
{
    /// <summary>
    /// Returns a dictionary of currency → total current balance for all active accounts.
    /// Accounts with null CurrentBalance are excluded from the sum.
    /// </summary>
    Task<Dictionary<string, decimal>> GetAggregatedBalanceAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the USD-equivalent total of all active bank account balances.
    /// </summary>
    Task<decimal> GetTotalNetWorthUsdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Returns a dictionary of account type → count for all active accounts.
    /// </summary>
    Task<Dictionary<string, int>> GetAccountCountByTypeAsync(Guid userId, CancellationToken ct = default);
}

/// <inheritdoc />
public class AggregationService(IBankAccountRepository accounts) : IAggregationService
{
    private readonly IBankAccountRepository _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));

    /// <inheritdoc />
    public async Task<Dictionary<string, decimal>> GetAggregatedBalanceAsync(Guid userId, CancellationToken ct = default)
    {
        var accounts = await _accounts.GetByUserIdAsync(userId, ct);

        return accounts
            .Where(a => a.CurrentBalance.HasValue)
            .GroupBy(a => a.Currency)
            .ToDictionary(g => g.Key, g => g.Sum(a => AccountBalanceMath.SignedForNetTotal(a.AccountType, a.CurrentBalance!.Value)));
    }

    /// <inheritdoc />
    public async Task<decimal> GetTotalNetWorthUsdAsync(Guid userId, CancellationToken ct = default)
    {
        var accounts = await _accounts.GetByUserIdAsync(userId, ct);

        return accounts
            .Where(a => a.CurrentBalance.HasValue)
            .Sum(a => AccountBalanceMath.SignedForNetTotal(a.AccountType, CurrencyConverter.ToUsd(a.CurrentBalance!.Value, a.Currency)));
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, int>> GetAccountCountByTypeAsync(Guid userId, CancellationToken ct = default)
    {
        var accounts = await _accounts.GetByUserIdAsync(userId, ct);

        return accounts
            .GroupBy(a => a.AccountType)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
