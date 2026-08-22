namespace FinanceSentry.Modules.Liquidity.Application.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Liquidity.Application.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily Hangfire job that projects each account 30 days forward from its detected
/// recurring outflows and fires a CashShortfall alert if the projected balance goes
/// negative. No LLM call is made at any point.
/// </summary>
public class LiquiditySentinelJob(
    IBankingAccountsReader accountsReader,
    IActiveSubscriptionsReader subscriptionsReader,
    IAlertGeneratorService alerts,
    ILogger<LiquiditySentinelJob> logger)
{
    private readonly IBankingAccountsReader _accountsReader = accountsReader;
    private readonly IActiveSubscriptionsReader _subscriptionsReader = subscriptionsReader;
    private readonly IAlertGeneratorService _alerts = alerts;
    private readonly ILogger<LiquiditySentinelJob> _logger = logger;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var userIds = await _accountsReader.GetAllActiveUserIdsAsync(ct);

        if (userIds.Count == 0)
        {
            _logger.LogDebug("LiquiditySentinel: no active users found, skipping.");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var userId in userIds)
        {
            try
            {
                await ProcessUserAsync(userId, today, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LiquiditySentinel: error processing user {UserId}", userId);
            }
        }
    }

    private async Task ProcessUserAsync(Guid userId, DateOnly today, CancellationToken ct)
    {
        var accounts = await _accountsReader.GetActiveAccountSnapshotsAsync(userId, ct);
        var subscriptions = await _subscriptionsReader.GetActiveSubscriptionsAsync(userId, ct);

        foreach (var account in accounts)
        {
            if (account.CurrentBalance is not { } balance)
            {
                _logger.LogDebug(
                    "LiquiditySentinel: skipping account {AccountId} (null balance)", account.AccountId);
                continue;
            }

            var shortfall = CashFlowProjectionService.Project(balance, account.Currency, subscriptions, today);

            if (shortfall is not null)
            {
                _logger.LogInformation(
                    "LiquiditySentinel: shortfall on account {AccountId} on {Date}, amount {Amount} {Currency}",
                    account.AccountId, shortfall.Date, shortfall.Amount, account.Currency);

                await _alerts.GenerateCashShortfallAlertAsync(
                    userId,
                    account.AccountId,
                    account.DisplayName,
                    shortfall.Date,
                    shortfall.Amount,
                    account.Currency,
                    ct);
            }
            else
            {
                await _alerts.ResolveCashShortfallAlertAsync(userId, account.AccountId, ct);
            }
        }
    }
}
